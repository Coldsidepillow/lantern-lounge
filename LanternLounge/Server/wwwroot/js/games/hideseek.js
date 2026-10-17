// Hide & Seek online: a maze seen from above. One seeker, three hiders, real time.
// The server decides everything (including what each player may see); this page draws it,
// smooths the 10-per-second updates, and turns a thumb stick and one button into controls.
import { h } from "../ui.js";
import { CHARACTERS, characterName, drawChar, charCanvas } from "./chars.js";

const VIEW_TILES = 13;          // how many tiles wide the camera shows
const SEND_MS = 100;            // how often controls are sent while the stick is held
const COOLDOWN_FULL_MS = 1500;  // the longest wait after a miss, for the button's fill
const FLOOR_A = "#2c2742", FLOOR_B = "#312b49", WALL_TOP = "#6a5d92", WALL_SIDE = "#41386a";

export default {
  id: "hide-seek",
  name: "Hide & Seek",
  theme: "theme-hideseek",
  artClass: "art-hideseek",
  seatDot: false,
  realtime: true,
  blurb: "Four friends in a twisty maze. One seeks, three hide. Pick a red panda, Siamese cat, honey badger or brown cat, then duck into hiding spots and find the secret rooms.",
  art: () => CHARACTERS.map(c => charCanvas(c.id, 44)),
  create
};

const pad2 = (n) => String(n).padStart(2, "0");
const clock = (ms) => { const s = Math.max(0, Math.ceil(ms / 1000)); return `${Math.floor(s / 60)}:${pad2(s % 60)}`; };

function create(area, api) {
  let room = null, view = null;
  let lastMatch = -1, lastElapsed = 0, lastHidersLeft = -1, lastPhase = null;
  let seen = [];                       // what each tile looked like the last time you saw it
  const actors = new Map();            // seat -> { x, y, fx, fy, tx, ty, hidden, caught, isSeeker }
  let raf = 0, last = 0, tSec = 0;
  let destroyed = false;

  // ----- controls state -----
  let stick = { x: 0, y: 0 };          // -1..1
  let keys = new Set();
  let actSeq = 0;
  try { actSeq = Number(sessionStorage.getItem("ll.hs.act")) || 0; } catch (e) { actSeq = 0; }
  let sent = { dx: 99999, dy: 0, actSeq: -1 };
  let dirty = true;

  // ----- page parts -----
  const hud = h("div.hs-hud",
    h("span.hs-role"), h("span.hs-clock"), h("span.hs-left"));
  const canvas = h("canvas.hs-canvas", { "aria-label": "The maze, seen from above" });
  const mini = h("canvas.hs-mini", { title: "Tap to enlarge the map", "aria-label": "Mini map" });
  const overlay = h("div.hs-overlay");
  const wrap = h("div.hs-wrap", hud, canvas, mini, overlay);

  const knob = h("div.hs-knob");
  const stickEl = h("div.hs-stick", { "aria-label": "Move: drag the thumb stick" }, knob);
  const actBtn = h("button.hs-act", { type: "button" }, h("span.hs-act-label", "Hide"));
  const controls = h("div.hs-controls", stickEl, actBtn);
  area.appendChild(wrap);
  area.appendChild(controls);
  area.appendChild(h("p.muted.hs-help", { style: { textAlign: "center", fontSize: "13px", margin: "8px 0 0" } },
    "Drag the stick to walk. Keyboard: arrows or WASD, Space for the button."));

  const ctx = canvas.getContext("2d");
  const mctx = mini.getContext("2d");
  let cssW = 0, cssH = 0, dpr = 1, ts = 40, miniBig = false;

  function resize() {
    dpr = Math.min(2, window.devicePixelRatio || 1);
    cssW = Math.max(240, Math.min(wrap.clientWidth || 340, 640));
    cssH = Math.round(Math.max(240, Math.min(cssW * 1.05, window.innerHeight * 0.5)));
    canvas.width = Math.round(cssW * dpr);
    canvas.height = Math.round(cssH * dpr);
    canvas.style.height = cssH + "px";
    ts = cssW / VIEW_TILES;
    sizeMini();
  }

  function sizeMini() {
    const w = view ? view.width : 31, hgt = view ? view.height : 21;
    const cell = miniBig ? Math.max(6, Math.floor((cssW - 24) / w)) : 4;
    mini.width = w * cell * dpr;
    mini.height = hgt * cell * dpr;
    mini.style.width = w * cell + "px";
    mini.style.height = hgt * cell + "px";
    mini.classList.toggle("big", miniBig);
  }
  mini.addEventListener("click", () => { miniBig = !miniBig; sizeMini(); });
  const onResize = () => resize();
  window.addEventListener("resize", onResize);
  resize();

  // ----- thumb stick -----
  let stickId = null;
  const stickMove = (e) => {
    const r = stickEl.getBoundingClientRect();
    const R = r.width / 2;
    let vx = e.clientX - (r.left + R), vy = e.clientY - (r.top + R);
    const len = Math.hypot(vx, vy);
    const max = R * 0.9;
    if (len > max) { vx = vx / len * max; vy = vy / len * max; }
    stick = { x: vx / max, y: vy / max };
    knob.style.transform = `translate(${vx}px, ${vy}px)`;
    dirty = true;
  };
  stickEl.addEventListener("pointerdown", (e) => {
    stickId = e.pointerId;
    try { stickEl.setPointerCapture(e.pointerId); } catch (err) { /* ignore */ }
    stickMove(e);
    e.preventDefault();
  });
  stickEl.addEventListener("pointermove", (e) => { if (e.pointerId === stickId) stickMove(e); });
  const stickEnd = (e) => {
    if (e.pointerId !== stickId) return;
    stickId = null;
    stick = { x: 0, y: 0 };
    knob.style.transform = "";
    dirty = true;
    sendNow();
  };
  stickEl.addEventListener("pointerup", stickEnd);
  stickEl.addEventListener("pointercancel", stickEnd);
  stickEl.addEventListener("lostpointercapture", stickEnd);

  // ----- the one button -----
  function press() {
    if (!view || !room || room.status !== "playing") return;
    if (view.youCaught) return;
    if (view.youIsSeeker && view.phase === "hiding") return;
    actSeq++;
    try { sessionStorage.setItem("ll.hs.act", String(actSeq)); } catch (e) { /* ignore */ }
    api.sound.tap();
    dirty = true;
    sendNow();
  }
  actBtn.addEventListener("pointerdown", (e) => { e.preventDefault(); press(); });
  actBtn.addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") e.preventDefault(); });

  // ----- keyboard -----
  const keyVec = () => {
    let x = 0, y = 0;
    if (keys.has("ArrowLeft") || keys.has("a")) x -= 1;
    if (keys.has("ArrowRight") || keys.has("d")) x += 1;
    if (keys.has("ArrowUp") || keys.has("w")) y -= 1;
    if (keys.has("ArrowDown") || keys.has("s")) y += 1;
    const len = Math.hypot(x, y) || 1;
    return { x: x / len, y: y / len };
  };
  const typing = () => { const el = document.activeElement; return el && (el.tagName === "INPUT" || el.tagName === "TEXTAREA"); };
  const onKeyDown = (e) => {
    if (typing()) return;
    const k = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    if (["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "a", "d", "w", "s"].includes(k)) {
      keys.add(k); dirty = true; e.preventDefault();
    } else if ((k === " " || k === "Enter" || k === "e") && !e.repeat) {
      press(); e.preventDefault();
    }
  };
  const onKeyUp = (e) => {
    const k = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    if (keys.delete(k)) { dirty = true; sendNow(); }
  };
  const onBlur = () => { keys.clear(); stick = { x: 0, y: 0 }; dirty = true; sendNow(); };
  window.addEventListener("keydown", onKeyDown);
  window.addEventListener("keyup", onKeyUp);
  window.addEventListener("blur", onBlur);

  // ----- sending controls -----
  function currentVector() {
    const kv = keyVec();
    const x = kv.x || kv.y ? kv.x : stick.x;
    const y = kv.x || kv.y ? kv.y : stick.y;
    return { dx: Math.round(Math.max(-1, Math.min(1, x)) * 100), dy: Math.round(Math.max(-1, Math.min(1, y)) * 100) };
  }
  function sendNow() {
    if (destroyed) return;
    const v = currentVector();
    const move = { dx: v.dx, dy: v.dy, actSeq };
    const moving = v.dx !== 0 || v.dy !== 0;
    if (!dirty && !moving) return;
    if (sent.dx === move.dx && sent.dy === move.dy && sent.actSeq === move.actSeq && !moving) { dirty = false; return; }
    if (!api.sendQuiet(move)) { dirty = true; return; }  // not connected right now: try again next time
    sent = move;
    dirty = false;
  }
  const sendTimer = setInterval(sendNow, SEND_MS);

  // ----- taking in the server's picture -----
  function charOf(seat) {
    const s = room && room.seats[seat];
    return (s && s.choice) || CHARACTERS[seat % CHARACTERS.length].id;
  }

  function newRound() {
    seen = new Array(view.width * view.height).fill(null);
    actors.clear();
    sizeMini();
    dirty = true;
  }

  function update(r) {
    room = r;
    view = r.view;
    if (!view) return;
    if (r.match !== lastMatch || view.elapsedMs < lastElapsed - 500 || seen.length !== view.width * view.height) {
      lastMatch = r.match;
      lastHidersLeft = -1;
      lastPhase = null;
      newRound();
    }
    lastElapsed = view.elapsedMs;

    // remember what is lit right now
    const n = view.width * view.height;
    const vis = view.visible;
    for (let i = 0; i < n; i++) {
      if ((parseInt(vis[i >> 2], 16) >> (3 - (i & 3))) & 1) seen[i] = view.map[i];
    }

    const here = new Set();
    for (const p of view.players) {
      here.add(p.seat);
      let a = actors.get(p.seat);
      if (!a) { a = { x: p.x, y: p.y }; actors.set(p.seat, a); }
      a.tx = p.x; a.ty = p.y; a.fx = p.facingX; a.fy = p.facingY;
      a.hidden = p.hidden; a.caught = p.caught; a.isSeeker = p.isSeeker;
      if (Math.hypot(a.tx - a.x, a.ty - a.y) > 3) { a.x = a.tx; a.y = a.ty; }  // a jump (teleport / new view): don't glide
    }
    for (const seat of [...actors.keys()]) if (!here.has(seat)) actors.delete(seat);

    if (lastPhase && lastPhase !== view.phase && view.phase === "seeking") api.sound.turn();
    if (lastHidersLeft >= 0 && view.hidersLeft < lastHidersLeft) api.sound.oops();
    lastPhase = view.phase;
    lastHidersLeft = view.hidersLeft;

    updateHud();
    if (!raf) { last = performance.now(); raf = requestAnimationFrame(frame); }
  }

  function updateHud() {
    const role = hud.children[0], clk = hud.children[1], left = hud.children[2];
    const seeker = view.youIsSeeker;
    role.textContent = view.spectating ? "Watching" : seeker ? "Seeker" : "Hider";
    role.className = "hs-role " + (seeker ? "seeker" : "hider");
    if (view.phase === "over") clk.textContent = "Round over";
    else clk.textContent = (view.phase === "hiding" ? "Hiding time " : "Time left ") + clock(view.msLeft);
    clk.className = "hs-clock" + (view.phase === "hiding" ? " hiding" : "");
    left.textContent = `Hiders left: ${view.hidersLeft}`;

    // button
    const label = actBtn.firstChild;
    let text = "Hide", disabled = false, hot = false;
    if (seeker) {
      text = "Grab";
      disabled = view.phase !== "seeking";
    } else if (view.youCaught) {
      text = "Caught"; disabled = true;
    } else if (view.youHidden) {
      text = "Come out";
    } else {
      text = "Hide";
      hot = view.youCanHide;
    }
    if (view.phase === "over") disabled = true;
    label.textContent = text;
    actBtn.classList.toggle("disabled", disabled);
    actBtn.classList.toggle("hot", hot && !disabled);
    const cd = seeker ? Math.min(1, view.youCooldownMs / COOLDOWN_FULL_MS) : 0;
    actBtn.style.setProperty("--cd", String(cd));
    actBtn.classList.toggle("cooling", cd > 0);
    stickEl.classList.toggle("off", view.youCaught || (seeker && view.phase === "hiding") || view.phase === "over");

    // overlay messages
    overlay.replaceChildren();
    if (seeker && view.phase === "hiding") {
      overlay.appendChild(h("div.hs-blind", h("div.big", clock(view.msLeft)),
        h("div", "Eyes closed! The hiders are running off to hide."), h("div.small", "You can look when the clock hits zero.")));
    } else if (view.spectating) {
      overlay.appendChild(h("div.hs-note", "You were caught! Watching through the seeker's eyes."));
    } else if (view.youHidden && view.phase !== "over") {
      overlay.appendChild(h("div.hs-note.good", "You're hiding. Stay still, or tap Come out."));
    }
  }

  // ----- drawing -----
  function frame(now) {
    raf = 0;
    if (destroyed) return;
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    tSec += dt;
    if (view) {
      const k = 1 - Math.exp(-dt * 16);
      for (const a of actors.values()) { a.x += (a.tx - a.x) * k; a.y += (a.ty - a.y) * k; }
      draw();
      drawMini();
    }
    raf = requestAnimationFrame(frame);
  }

  function lit(i) { return (parseInt(view.visible[i >> 2], 16) >> (3 - (i & 3))) & 1; }

  function draw() {
    const W = view.width, H = view.height;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = "#0a0813";
    ctx.fillRect(0, 0, cssW, cssH);

    // camera: follow yourself, or the seeker if you were caught
    const focusSeat = view.spectating ? view.seeker : view.youSeat;
    const f = actors.get(focusSeat);
    let cx = f ? f.x : view.youX, cy = f ? f.y : view.youY;
    const halfW = cssW / ts / 2, halfH = cssH / ts / 2;
    cx = W <= halfW * 2 ? W / 2 : Math.max(halfW, Math.min(W - halfW, cx));
    cy = H <= halfH * 2 ? H / 2 : Math.max(halfH, Math.min(H - halfH, cy));
    const ox = cssW / 2 - cx * ts, oy = cssH / 2 - cy * ts;
    const x0 = Math.max(0, Math.floor(cx - halfW) - 1), x1 = Math.min(W - 1, Math.ceil(cx + halfW) + 1);
    const y0 = Math.max(0, Math.floor(cy - halfH) - 1), y1 = Math.min(H - 1, Math.ceil(cy + halfH) + 1);

    for (let y = y0; y <= y1; y++) {
      for (let x = x0; x <= x1; x++) {
        const i = y * W + x;
        const isLit = lit(i);
        const ch = isLit ? view.map[i] : seen[i];
        if (!ch) continue;
        const px = Math.floor(ox + x * ts), py = Math.floor(oy + y * ts);
        const size = Math.ceil(ts) + 1;
        drawTile(ch, x, y, i, px, py, size, W);
        if (!isLit) {
          ctx.fillStyle = "rgba(8,6,18,.62)";
          ctx.fillRect(px, py, size, size);
        }
      }
    }

    // the people you can see
    const list = [...actors.entries()].sort((a, b) => a[1].y - b[1].y);
    for (const [seat, a] of list) {
      const sx = ox + a.x * ts, sy = oy + a.y * ts;
      if (sx < -ts || sy < -ts || sx > cssW + ts || sy > cssH + ts) continue;
      const mine = seat === view.youSeat;
      if (a.isSeeker) {
        ctx.beginPath();
        ctx.arc(sx, sy + ts * 0.05, ts * (0.62 + Math.sin(tSec * 5) * 0.04), 0, Math.PI * 2);
        ctx.strokeStyle = "rgba(255,90,90,.9)"; ctx.lineWidth = Math.max(2, ts * 0.06); ctx.stroke();
      }
      const alpha = a.caught ? 0.45 : a.hidden ? 0.4 : 1;
      drawChar(ctx, charOf(seat), sx, sy, ts * 0.95, a.fx || 0, a.fy || 1, a.hidden ? 0 : tSec, alpha);
      if (a.caught) {
        ctx.fillStyle = "rgba(255,255,255,.9)"; ctx.font = `800 ${Math.round(ts * 0.3)}px Nunito, sans-serif`; ctx.textAlign = "center";
        ctx.fillText("caught", sx, sy - ts * 0.6);
      } else if (!mine) {
        ctx.fillStyle = "rgba(255,255,255,.88)"; ctx.font = `800 ${Math.round(ts * 0.27)}px Nunito, sans-serif`; ctx.textAlign = "center";
        const name = (api.seatName(seat) || "").slice(0, 10);
        ctx.fillText(name, sx, sy - ts * 0.62);
      } else {
        ctx.fillStyle = "#ffe08a";
        ctx.beginPath(); ctx.moveTo(sx - ts * 0.1, sy - ts * 0.72); ctx.lineTo(sx + ts * 0.1, sy - ts * 0.72); ctx.lineTo(sx, sy - ts * 0.58); ctx.closePath(); ctx.fill();
      }
    }

    // lantern light: the edge of what you can see fades out
    if (view.phase !== "hiding" && view.phase !== "over") {
      const fx = ox + (f ? f.x : view.youX) * ts, fy = oy + (f ? f.y : view.youY) * ts;
      const g = ctx.createRadialGradient(fx, fy, ts * 2.2, fx, fy, ts * 6.4);
      g.addColorStop(0, "rgba(10,8,19,0)");
      g.addColorStop(1, "rgba(10,8,19,.5)");
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, cssW, cssH);
    }
  }

  function wallAt(x, y, W) {
    if (x < 0 || y < 0 || x >= W || y >= view.height) return true;
    const c = view.map[y * W + x];
    return c === "#" || c === "S";
  }

  function drawTile(ch, x, y, i, px, py, size, W) {
    if (ch === "#" || ch === "S") {
      const below = !wallAt(x, y + 1, W);
      ctx.fillStyle = WALL_TOP;
      ctx.fillRect(px, py, size, size);
      if (below) {
        ctx.fillStyle = WALL_SIDE;
        ctx.fillRect(px, py + size * 0.7, size, size * 0.3 + 1);
      }
      ctx.fillStyle = "rgba(255,255,255,.07)";
      ctx.fillRect(px, py, size, Math.max(2, size * 0.08));
      if (ch === "S") {
        // a hider's secret: a faint keyhole in the wall
        ctx.fillStyle = "rgba(255,214,102,.8)";
        ctx.beginPath(); ctx.arc(px + size / 2, py + size * 0.42, size * 0.1, 0, Math.PI * 2); ctx.fill();
        ctx.fillRect(px + size * 0.46, py + size * 0.42, size * 0.08, size * 0.22);
      }
      return;
    }
    ctx.fillStyle = (x + y) % 2 ? FLOOR_A : FLOOR_B;
    ctx.fillRect(px, py, size, size);
    if (ch === "D") {
      ctx.strokeStyle = "rgba(255,214,102,.85)"; ctx.lineWidth = Math.max(2, size * 0.07);
      ctx.strokeRect(px + size * 0.08, py + size * 0.08, size * 0.84, size * 0.84);
    } else if (ch === "H") {
      const kind = (x * 7 + y * 13) % 3;
      const c = size / 2;
      if (kind === 0) {                       // leafy bush
        for (const [dx, dy, r] of [[-0.18, 0.06, 0.26], [0.2, 0.04, 0.24], [0, -0.16, 0.27]]) {
          ctx.fillStyle = "#3f9a58"; ctx.beginPath(); ctx.arc(px + c + dx * size, py + c + dy * size, r * size, 0, Math.PI * 2); ctx.fill();
        }
        ctx.fillStyle = "#6fcf86"; ctx.beginPath(); ctx.arc(px + c - size * 0.08, py + c - size * 0.2, size * 0.1, 0, Math.PI * 2); ctx.fill();
      } else if (kind === 1) {                // barrel
        ctx.fillStyle = "#8a5a2e"; ctx.beginPath(); ctx.arc(px + c, py + c, size * 0.34, 0, Math.PI * 2); ctx.fill();
        ctx.strokeStyle = "#5a3818"; ctx.lineWidth = size * 0.06;
        ctx.beginPath(); ctx.arc(px + c, py + c, size * 0.34, 0, Math.PI * 2); ctx.stroke();
        ctx.beginPath(); ctx.arc(px + c, py + c, size * 0.18, 0, Math.PI * 2); ctx.stroke();
      } else {                                // crate
        ctx.fillStyle = "#b07a3a"; ctx.fillRect(px + size * 0.16, py + size * 0.16, size * 0.68, size * 0.68);
        ctx.strokeStyle = "#6b4719"; ctx.lineWidth = size * 0.06;
        ctx.strokeRect(px + size * 0.16, py + size * 0.16, size * 0.68, size * 0.68);
        ctx.beginPath(); ctx.moveTo(px + size * 0.16, py + size * 0.16); ctx.lineTo(px + size * 0.84, py + size * 0.84);
        ctx.moveTo(px + size * 0.84, py + size * 0.16); ctx.lineTo(px + size * 0.16, py + size * 0.84); ctx.stroke();
      }
    }
  }

  function drawMini() {
    const W = view.width, H = view.height;
    const cell = (parseFloat(mini.style.width) || W * 4) / W;
    mctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    mctx.clearRect(0, 0, W * cell, H * cell);
    mctx.fillStyle = "rgba(10,8,19,.72)";
    mctx.fillRect(0, 0, W * cell, H * cell);
    for (let i = 0; i < W * H; i++) {
      const isLit = lit(i);
      const ch = isLit ? view.map[i] : seen[i];
      if (!ch) continue;
      const x = i % W, y = (i / W) | 0;
      if (ch === "#") mctx.fillStyle = isLit ? "#8c80b8" : "#5a5080";
      else if (ch === "S") mctx.fillStyle = "#e0b85a";
      else if (ch === "D") mctx.fillStyle = "#ffd666";
      else if (ch === "H") mctx.fillStyle = isLit ? "#58b06c" : "#3c7a4a";
      else mctx.fillStyle = isLit ? "#3b3558" : "#2a2542";
      mctx.fillRect(x * cell, y * cell, cell, cell);
    }
    for (const [seat, a] of actors) {
      if (a.hidden && seat !== view.youSeat) continue;
      const blink = seat === view.youSeat ? (Math.sin(tSec * 8) > -0.3 ? 1 : 0.3) : 1;
      mctx.globalAlpha = blink;
      mctx.fillStyle = a.isSeeker ? "#ff6a6a" : "#ffe08a";
      mctx.beginPath(); mctx.arc(a.x * cell, a.y * cell, Math.max(2, cell * 0.55), 0, Math.PI * 2); mctx.fill();
      mctx.globalAlpha = 1;
    }
  }

  // ----- hooks used by the page around the game -----
  return {
    update,
    seatInfo(seat) {
      const s = room && room.seats[seat];
      const name = characterName(s && s.choice);
      if (!view) return name;
      const role = seat === view.seeker ? "Seeker" : "Hider";
      return `${name} · ${role}`;
    },
    seatIcon(seat) {
      const s = room && room.seats[seat];
      return s && s.choice ? charCanvas(s.choice, 30) : null;
    },
    seatClass(seat) {
      if (!view || !view.players) return "";
      const p = view.players.find(q => q.seat === seat);
      return p && p.caught ? "caught" : "";
    },
    banner(r) {
      const v = r.view;
      if (!v) return ["", false];
      if (r.status === "finished" || v.phase === "over") return ["Round over!", false];
      if (v.spectating) return ["You were caught. Watch the rest of the round!", false];
      if (v.youIsSeeker) {
        return v.phase === "hiding"
          ? ["Eyes closed! Wait for the clock.", false]
          : ["Find the hiders! Get close to a hiding spot and tap Grab.", true];
      }
      if (v.youHidden) return ["Shh! You're hiding.", true];
      if (v.phase === "hiding") return [v.youCanHide ? "You're at a hiding spot. Tap Hide!" : "Run! Find a bush, barrel or crate to hide in.", true];
      return [v.youCanHide ? "A hiding spot is right here. Tap Hide!" : "Stay out of sight!", false];
    },
    resultTitle(r) {
      const v = r.view;
      if (!v) return null;
      const seekerName = api.seatName(v.seeker);
      if (v.seekerWon) return v.youIsSeeker ? "You found everyone!" : `${seekerName} caught everyone!`;
      if (v.youIsSeeker) return "The hiders got away!";
      return v.youCaught ? "Some hiders got away!" : "You got away!";
    },
    ranking(r) {
      const v = r.view;
      if (!v) return [];
      const rows = [];
      const caughtCount = v.players.filter(p => p.caught && !p.isSeeker).length;
      rows.push({ seat: v.seeker, text: `Seeker · caught ${caughtCount}` });
      for (const p of v.players) {
        if (p.isSeeker) continue;
        rows.push({ seat: p.seat, text: p.caught ? "Caught" : "Escaped!" });
      }
      return rows;
    },
    destroy() {
      destroyed = true;
      cancelAnimationFrame(raf);
      clearInterval(sendTimer);
      window.removeEventListener("resize", onResize);
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("blur", onBlur);
    }
  };
}
