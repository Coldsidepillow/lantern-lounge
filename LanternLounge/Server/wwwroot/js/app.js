// Lantern Lounge website: pick a game, make or join a room, and play online with friends.
import { h, dot, toast, sound, isSoundOn, setSoundOn } from "./ui.js";
import { Session } from "./net.js";
import pebble from "./games/pebble.js";
import tidebound from "./games/tidebound.js";
import prism from "./games/prism.js";
import holdem from "./games/holdem.js";

const GAMES = [tidebound, prism, pebble, holdem];
const byId = Object.fromEntries(GAMES.map(g => [g.id, g]));
const app = document.getElementById("app");
const session = new Session();

let screen = "home";        // home | start | lobby | game
let chosenGame = null;       // game id on the start screen (null = join only)
let startMessage = "";
let busy = false;            // waiting for the server after Create / Join
let wakeTimer = null;
let game = null;             // { def, controller, els } while a game page is showing
let lastTurnSeat = null;

function loadName() { try { return localStorage.getItem("ll.name") || ""; } catch (e) { return ""; } }
function saveName(name) { try { localStorage.setItem("ll.name", name); } catch (e) { /* ignore */ } }

export function seatName(room, seat) {
  const s = room && room.seats && room.seats[seat];
  return (s && s.name) || `Seat ${seat + 1}`;
}

// ---------- Header ----------
function topbar(extra) {
  const pill = statusPill();
  return h("header.topbar",
    h("a.brand", { onclick: () => { if (!session.inRoom) goHome(); }, title: "Lantern Lounge home" },
      h("div.lantern"), h("h1", "Lantern Lounge")),
    h("div.spacer"),
    pill,
    extra || null);
}

function statusPill() {
  const map = {
    connected: ["Online", "ok"], connecting: ["Connecting...", "warn"],
    reconnecting: ["Reconnecting...", "warn"], offline: [null, ""]
  };
  const [text, cls] = map[session.status] || [null, ""];
  return text ? h("span.status-pill", { class: cls }, text) : null;
}

// ---------- Home ----------
function goHome() {
  screen = "home";
  destroyGame();
  render();
}

function renderHome() {
  const codeField = h("input.field.code", { maxlength: 5, placeholder: "CODE", "aria-label": "Room code", autocomplete: "off" });
  codeField.addEventListener("input", () => { codeField.value = cleanCode(codeField.value); });
  const join = () => {
    const code = cleanCode(codeField.value);
    if (code.length !== 5) { toast("Room codes have 5 letters and numbers."); return; }
    chosenGame = null;
    screen = "start";
    render(code);
  };
  codeField.addEventListener("keydown", e => { if (e.key === "Enter") join(); });

  return [
    topbar(),
    h("section.hero",
      h("h2", { style: { fontSize: "clamp(24px, 4vw, 32px)" } }, "Pick a table and play with friends"),
      h("p", "Make a room, share its 5-letter code, and friends join from their own phone or computer. Bots fill any empty seats, so you can start right away.")),
    h("div.games", GAMES.map(def =>
      h("article.game-card",
        h("div.game-art", { class: def.artClass }, def.art()),
        h("div.body",
          h("h2", def.name),
          h("p", def.blurb),
          h("button.btn.primary", { onclick: () => { chosenGame = def.id; screen = "start"; startMessage = ""; render(); } }, "Play online"))))),
    h("div.join-strip",
      h("span.label", "Have a code?"),
      codeField,
      h("button.btn", { onclick: join }, "Join"))
  ];
}

// ---------- Create / join ----------
function renderStart(prefillCode) {
  const def = chosenGame ? byId[chosenGame] : null;
  const nameField = h("input.field#name", { maxlength: 16, placeholder: "e.g. Sam", value: loadName(), autocomplete: "nickname", "aria-label": "Your name" });
  const codeField = h("input.field.code#code", { maxlength: 5, placeholder: "CODE", value: prefillCode || "", autocomplete: "off", "aria-label": "Room code" });
  codeField.addEventListener("input", () => { codeField.value = cleanCode(codeField.value); });

  const create = () => {
    if (busy || !def) return;
    const name = nameField.value.trim();
    saveName(name);
    begin();
    session.create(def.id, name);
  };
  const join = () => {
    if (busy) return;
    const code = cleanCode(codeField.value);
    if (code.length !== 5) { setStartMessage("Room codes have 5 letters and numbers."); return; }
    const name = nameField.value.trim();
    saveName(name);
    begin();
    session.join(code, name);
  };
  nameField.addEventListener("keydown", e => { if (e.key === "Enter") (def ? create : join)(); });
  codeField.addEventListener("keydown", e => { if (e.key === "Enter") join(); });

  return [
    topbar(),
    h("div.lobby",
      h("div.row",
        h("button.btn.small", { onclick: () => { busy = false; if (session.status !== "offline") session.leave(); goHome(); } }, "< Back"),
        h("h2.grow", def ? `${def.name} online` : "Join a room")),
      h("div.panel",
        h("div.label", "Your name"),
        h("div", { style: { marginTop: "8px" } }, nameField),
        def ? h("button.btn.primary", { style: { width: "100%", marginTop: "14px" }, disabled: busy, onclick: create }, "Create a room") : null,
        h("div.muted", { style: { textAlign: "center", margin: "16px 0 8px", fontSize: "14px" } }, def ? "or join a friend's room" : "Type the code your friend shared"),
        h("div.row", h("div.grow", codeField), h("button.btn", { disabled: busy, onclick: join }, "Join"))),
      h("div#start-message", startMessageNode()))
  ];
}

function startMessageNode() {
  if (busy) {
    return h("div.wake", h("div.spinner"), session.status === "connecting" && wakeTimer === null
      ? "Waking up the server... this can take up to a minute."
      : "Connecting...");
  }
  return startMessage ? h("div", { style: { color: "var(--amber)", fontWeight: 800 } }, startMessage) : null;
}

function setStartMessage(text) {
  startMessage = text;
  const el = document.getElementById("start-message");
  if (el) { el.replaceChildren(); const n = startMessageNode(); if (n) el.appendChild(n); }
}

function begin() {
  busy = true;
  startMessage = "";
  clearTimeout(wakeTimer);
  wakeTimer = setTimeout(() => { wakeTimer = null; if (busy) setStartMessage(""); }, 2500);
  render();
}

// ---------- Waiting room ----------
function renderLobby(room) {
  const def = byId[room.game];
  const link = `${location.origin}/#${room.code}`;
  const copy = async () => {
    try { await navigator.clipboard.writeText(link); toast("Invite link copied!"); }
    catch (e) { toast(`Share this link: ${link}`, 5000); }
  };
  return [
    topbar(),
    h("div.lobby",
      h("div.row", h("h2.grow", def ? def.name : room.game), h("button.btn.small.danger", { onclick: leave }, "Leave")),
      h("div.panel",
        h("div.label", "Room code"),
        h("div.code-boxes", { style: { margin: "8px 0" } }, [...room.code].map(c => h("span", c))),
        h("div.row",
          h("span.muted.grow", "Friends type this code, or open your invite link."),
          h("button.btn.small", { onclick: copy }, "Copy invite link"))),
      h("div.panel",
        h("div.label", { style: { marginBottom: "10px" } }, "Players"),
        h("div.seat-list", room.seats.map(s => h("div.seat-row",
          dot(s.seat),
          h("strong.grow", s.isHuman ? s.name : h("span.muted", "Empty: a bot will play")),
          s.seat === room.you ? h("span.tag.you", "you") : null,
          s.isHost ? h("span.tag.host", "host") : null,
          s.isHuman && !s.connected ? h("span.tag.away", "reconnecting") : null)))),
      room.you === room.hostSeat
        ? h("button.btn.primary", { onclick: () => { sound.tap(); session.start(); } }, "Start the game")
        : h("div.wake", h("div.spinner"), "Waiting for the host to start..."))
  ];
}

// ---------- Game page ----------
function enterGame(room) {
  const def = byId[room.game];
  if (!def) { toast("This game isn't on the website yet."); leave(); return; }
  destroyGame();
  screen = "game";
  const els = {
    seats: h("div.seats"),
    banner: h("div.turn-banner"),
    area: h("div.game-area"),
    over: h("div")
  };
  const soundBtn = h("button.btn.small", { onclick: () => { setSoundOn(!isSoundOn()); soundBtn.textContent = isSoundOn() ? "Sound on" : "Sound off"; } },
    isSoundOn() ? "Sound on" : "Sound off");
  const page = h("section.game-page", { class: def.theme },
    h("div.game-head",
      h("h2.grow", def.name),
      h("span.code", room.code),
      soundBtn,
      h("button.btn.small.danger", { onclick: leave }, "Leave")),
    els.seats, els.banner, els.area, els.over);
  app.replaceChildren(topbar(), page);
  const api = {
    send: (move) => { if (!session.move(move)) toast("Reconnecting... try again in a moment."); },
    seatName: (seat) => seatName(session.room, seat),
    get you() { return session.seat; },
    toast, sound,
    refresh: () => { if (game && session.room) updateGame(session.room); }
  };
  game = { def, controller: def.create(els.area, api), els, api };
  lastTurnSeat = null;
  updateGame(room);
}

function updateGame(room) {
  if (!game) return;
  const { controller, els, def } = game;
  controller.update(room);

  const playing = room.status === "playing";
  const toAct = new Set(room.seatsToAct || []);
  els.seats.replaceChildren(...room.seats.map(s => {
    const classes = [];
    if (playing && toAct.has(s.seat) && toAct.size === 1) classes.push("turn");
    if (room.result && room.result.over && (room.result.winningSeats || []).includes(s.seat)) classes.push("winner");
    const extra = controller.seatClass ? controller.seatClass(s.seat) : "";
    if (extra) classes.push(extra);
    const clickable = !!(controller.onSeat && controller.canPickSeat && controller.canPickSeat(s.seat));
    if (clickable) classes.push("clickable");
    const kind = s.seat === room.you ? "you" : s.isBot ? "bot" : !s.connected ? "away" : "";
    return h("div.seat-chip", {
      class: classes.join(" "), role: clickable ? "button" : null, tabindex: clickable ? 0 : null,
      onclick: clickable ? () => controller.onSeat(s.seat) : null,
      onkeydown: clickable ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); controller.onSeat(s.seat); } } : null
    },
      h("div.name", def.seatDot === false ? null : dot(s.seat), s.name || `Seat ${s.seat + 1}`),
      h("div.info", [kind === "you" ? "You" : kind === "bot" ? "Bot" : kind === "away" ? "Away" : "Player",
        controller.seatInfo ? " · " + controller.seatInfo(s.seat) : ""].join("")));
  }));

  // Whose turn
  let bannerText = "", mine = false;
  if (session.status === "reconnecting") bannerText = "Reconnecting...";
  else if (controller.banner) [bannerText, mine] = controller.banner(room);
  else if (playing && toAct.size === 1) {
    const seat = [...toAct][0];
    mine = seat === room.you;
    bannerText = mine ? "Your turn!" : `${seatName(room, seat)}'s turn`;
  }
  els.banner.textContent = bannerText;
  els.banner.classList.toggle("mine", mine);
  const turnSeat = playing && toAct.size === 1 ? [...toAct][0] : null;
  if (turnSeat === room.you && lastTurnSeat !== room.you) sound.turn();
  lastTurnSeat = turnSeat;

  // Game over card
  els.over.replaceChildren();
  if (room.status === "finished" && room.result) {
    const winners = room.result.winningSeats || [];
    const youWon = winners.includes(room.you);
    const title = room.result.isDraw ? "It's a draw!" : youWon ? (winners.length > 1 ? "You share the win!" : "You win!") :
      winners.length ? `${winners.map(s => seatName(room, s)).join(" & ")} wins!` : "Game over";
    const ranking = controller.ranking ? controller.ranking(room) : [];
    els.over.appendChild(h("div.over-card",
      h("div.panel",
        h("h3", title),
        ranking.map(r => h("div.rank", dot(r.seat), h("strong.grow", seatName(room, r.seat)), h("span", r.text))),
        room.you === room.hostSeat
          ? h("button.btn.primary", { onclick: () => session.rematch() }, "Play again")
          : h("div.muted", "Waiting for the host to start another game..."),
        h("button.btn", { onclick: leave }, "Leave"))));
    if (!game.celebrated) { game.celebrated = true; if (youWon) sound.win(); }
  } else {
    game.celebrated = false;
  }
}

function destroyGame() {
  if (game && game.controller.destroy) game.controller.destroy();
  game = null;
}

function leave() {
  session.leave();
  goHome();
}

// ---------- Render ----------
function render(prefillCode) {
  if (screen !== "game") destroyGame();
  let nodes;
  if (screen === "lobby" && session.room) nodes = renderLobby(session.room);
  else if (screen === "start") nodes = renderStart(prefillCode);
  else nodes = renderHome();
  app.replaceChildren(...nodes.filter(Boolean));
}

// ---------- Session events ----------
session.on("room", room => {
  busy = false;
  if (room.status === "lobby") {
    screen = "lobby";
    render();
  } else if (screen !== "game" || !game || game.def.id !== room.game) {
    enterGame(room);
  } else {
    updateGame(room);
  }
});

session.on("error", (request, code, message) => {
  if (screen === "start") {
    busy = false;
    startMessage = message;
    render();
  } else {
    toast(message);
    if (game && game.controller.onError) game.controller.onError(request, code);
  }
  if (request === "move") sound.oops();
});

session.on("left", reason => {
  busy = false;
  screen = "home";
  destroyGame();
  render();
  if (reason) toast(reason, 4000);
});

session.on("status", () => {
  const pill = document.querySelector(".status-pill");
  const fresh = statusPill();
  if (pill && fresh) pill.replaceWith(fresh);
  else if (pill) pill.remove();
  else if (fresh) { const bar = document.querySelector(".topbar .spacer"); if (bar) bar.after(fresh); }
  if (screen === "start") setStartMessage(startMessage);
  if (game && session.room) updateGame(session.room);
});

function cleanCode(text) {
  return (text || "").toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 5);
}

// ---------- Start up ----------
const hashCode = cleanCode(location.hash.slice(1));
if (session.resume()) {
  screen = "start";
  busy = true;
  render();
} else if (hashCode.length === 5) {
  chosenGame = null;
  screen = "start";
  render(hashCode);
} else {
  render();
}
