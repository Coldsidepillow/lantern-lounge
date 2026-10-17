// Lantern Lounge website: pick a game, make or join a room, and play online with friends.
import { h, dot, toast, sound, isSoundOn, setSoundOn } from "./ui.js";
import { Session } from "./net.js";
import pebble from "./games/pebble.js";
import tidebound from "./games/tidebound.js";
import prism from "./games/prism.js";
import holdem from "./games/holdem.js";
import hideseek from "./games/hideseek.js";
import { CHARACTERS, charCanvas } from "./games/chars.js";

const GAMES = [tidebound, prism, pebble, holdem, hideseek];
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

// Table chat. The messages live in the session; this is only what the screen needs.
let chatEls = null;          // { el, list, input, toggle, collapsible } while a chat panel is showing
let chatOpen = false;        // is the collapsible chat on the game page open?
let chatUnread = 0;          // messages that came in while it was closed
let chatDraft = "";          // what you've typed but not sent, kept when the screen redraws

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
      gamePicker(room),
      characterPicker(room),
      h("div.panel",
        h("div.label", { style: { marginBottom: "10px" } }, "Players"),
        h("div.seat-list", room.seats.map(s => h("div.seat-row",
          s.choice ? charCanvas(s.choice, 30) : dot(s.seat),
          h("strong.grow", s.isHuman ? s.name : h("span.muted", "Empty: a bot will play")),
          s.seat === room.you ? h("span.tag.you", "you") : null,
          s.isHost ? h("span.tag.host", "host") : null,
          s.isHuman && !s.connected ? h("span.tag.away", "reconnecting") : null)))),
      buildChat(room, false) || h("div.muted", { style: { textAlign: "center" } }, `Chat is off in ${def ? def.name : "this game"}.`),
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
    over: h("div"),
    change: h("button.btn.small", { onclick: changeGame }, "Change game")
  };
  const soundBtn = h("button.btn.small", { onclick: () => { setSoundOn(!isSoundOn()); soundBtn.textContent = isSoundOn() ? "Sound on" : "Sound off"; } },
    isSoundOn() ? "Sound on" : "Sound off");
  const page = h("section.game-page", { class: def.theme },
    h("div.game-head",
      h("h2.grow", def.name),
      h("span.code", room.code),
      soundBtn,
      els.change,
      h("button.btn.small.danger", { onclick: leave }, "Leave")),
    els.seats, els.banner, els.area, els.over, buildChat(room, true));
  app.replaceChildren(topbar(), page);
  scrollChatToEnd();
  const api = {
    send: (move) => { if (!session.move(move)) toast("Reconnecting... try again in a moment."); },
    sendQuiet: (move) => session.move(move),
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
  els.change.style.display = room.you === room.hostSeat ? "" : "none";
  const toAct = new Set(room.seatsToAct || []);
  const chips = room.seats.map(s => {
    const classes = [];
    if (playing && toAct.has(s.seat) && toAct.size === 1) classes.push("turn");
    if (room.result && room.result.over && (room.result.winningSeats || []).includes(s.seat)) classes.push("winner");
    const extra = controller.seatClass ? controller.seatClass(s.seat) : "";
    if (extra) classes.push(extra);
    const clickable = !!(controller.onSeat && controller.canPickSeat && controller.canPickSeat(s.seat));
    if (clickable) classes.push("clickable");
    const kind = s.seat === room.you ? "you" : s.isBot ? "bot" : !s.connected ? "away" : "";
    return {
      seat: s.seat, classes: classes.join(" "), clickable,
      name: s.name || `Seat ${s.seat + 1}`,
      info: [kind === "you" ? "You" : kind === "bot" ? "Bot" : kind === "away" ? "Away" : "Player",
        controller.seatInfo ? " · " + controller.seatInfo(s.seat) : ""].join("")
    };
  });
  // Real-time games send an update ten times a second: only redraw the player strip when it really changed.
  const chipSig = JSON.stringify(chips);
  if (chipSig !== game.chipSig) {
    game.chipSig = chipSig;
    els.seats.replaceChildren(...chips.map(c => h("div.seat-chip", {
      class: c.classes, role: c.clickable ? "button" : null, tabindex: c.clickable ? 0 : null,
      onclick: c.clickable ? () => controller.onSeat(c.seat) : null,
      onkeydown: c.clickable ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); controller.onSeat(c.seat); } } : null
    },
      h("div.name", controller.seatIcon ? controller.seatIcon(c.seat) : def.seatDot === false ? null : dot(c.seat), c.name),
      h("div.info", c.info))));
  }

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
  if (room.status === "finished" && room.result) {
    const winners = room.result.winningSeats || [];
    const youWon = winners.includes(room.you);
    const custom = controller.resultTitle ? controller.resultTitle(room) : null;
    const title = custom ? custom : room.result.isDraw ? "It's a draw!" : youWon ? (winners.length > 1 ? "You share the win!" : "You win!") :
      winners.length ? `${winners.map(s => seatName(room, s)).join(" & ")} wins!` : "Game over";
    const ranking = controller.ranking ? controller.ranking(room) : [];
    const overSig = JSON.stringify([room.match, title, ranking, room.you === room.hostSeat]);
    if (overSig !== game.overSig) {
    game.overSig = overSig;
    els.over.replaceChildren();
    els.over.appendChild(h("div.over-card",
      h("div.panel",
        h("h3", title),
        ranking.map(r => h("div.rank", dot(r.seat), h("strong.grow", seatName(room, r.seat)), h("span", r.text))),
        room.you === room.hostSeat
          ? h("button.btn.primary", { onclick: () => session.rematch() }, "Play again")
          : h("div.muted", "Waiting for the host to start another game..."),
        room.you === room.hostSeat ? h("button.btn", { onclick: changeGame }, "Change game") : null,
        h("button.btn", { onclick: leave }, "Leave"))));
    }
    if (!game.celebrated) { game.celebrated = true; if (youWon) sound.win(); }
  } else {
    els.over.replaceChildren();
    game.overSig = null;
    game.celebrated = false;
  }
}

// ---------- Changing the game without remaking the group ----------
function gamePicker(room) {
  const isHost = room.you === room.hostSeat;
  return h("div.panel",
    h("div.label", { style: { marginBottom: "10px" } }, "Game"),
    h("div.game-picker", GAMES.map(def => h("button.btn.small", {
      class: def.id === room.game ? "primary" : "",
      disabled: !isHost && def.id !== room.game,
      "aria-pressed": def.id === room.game ? "true" : "false",
      onclick: () => { if (isHost && def.id !== room.game) { sound.tap(); session.setGame(def.id); } }
    }, def.name))),
    h("div.muted", { style: { marginTop: "10px", fontSize: "14px" } },
      isHost ? "Pick any game. Everyone stays in this room with the same code." : "The host picks the game."));
}

// ---------- Picking a character (Hide & Seek) ----------
function characterPicker(room) {
  if (!room.choices || !room.choices.length) return null;
  const mine = room.seats[room.you] && room.seats[room.you].choice;
  return h("div.panel",
    h("div.label", { style: { marginBottom: "10px" } }, "Pick your character"),
    h("div.char-picker", room.choices.map(id => {
      const info = CHARACTERS.find(c => c.id === id) || { id, name: id, blurb: "" };
      const owner = room.seats.find(s => s.choice === id);
      const taken = owner && owner.seat !== room.you;
      return h("button.char-card", {
        type: "button", class: (id === mine ? "picked " : "") + (taken ? "taken" : ""),
        disabled: !!taken, "aria-pressed": id === mine ? "true" : "false",
        onclick: () => { if (!taken && id !== mine) { sound.tap(); session.choose(id); } }
      }, charCanvas(id, 64), h("strong", info.name),
        h("span.muted", taken ? `${owner.name} is this one` : id === mine ? "You" : info.blurb)
      );
    })),
    h("div.muted", { style: { marginTop: "10px", fontSize: "14px" } }, "Everyone picks a different one. Bots get whichever are left."));
}

/** Host: end the current game (or finished one) and go back to the waiting room to pick another. */
function changeGame() {
  const room = session.room;
  if (!room || room.you !== room.hostSeat) return;
  if (room.status === "playing" &&
      !confirm("End this game and go back to the waiting room? Everyone stays in the room, and you can pick any game.")) return;
  session.setGame(room.game);
}

// ---------- Table chat ----------
/** The chat panel for a room, or null if this game has no chat. The lobby one is always open; the game page one folds away. */
function buildChat(room, collapsible) {
  if (!room.chatEnabled) { chatEls = null; return null; }
  const input = h("input.field.chat-input", {
    maxlength: 200, placeholder: "Say something...", autocomplete: "off", "aria-label": "Chat message", value: chatDraft
  });
  const send = () => {
    const text = input.value.trim();
    if (!text) return;
    if (session.sendChat(text)) { input.value = ""; chatDraft = ""; }
    else toast("Reconnecting... try again in a moment.");
  };
  input.addEventListener("input", () => { chatDraft = input.value; });
  input.addEventListener("keydown", e => { if (e.key === "Enter") { e.preventDefault(); send(); } });

  const list = h("div.chat-list", { role: "log", "aria-label": "Chat messages" });
  const toggle = collapsible
    ? h("button.chat-toggle", { type: "button", "aria-expanded": chatOpen ? "true" : "false", onclick: () => setChatOpen(!chatOpen) })
    : h("div.chat-toggle.static", "Chat");
  const el = h("section.chat", { class: !collapsible || chatOpen ? "open" : "" },
    toggle,
    h("div.chat-body", list, h("div.row.chat-send", h("div.grow", input), h("button.btn", { onclick: send }, "Send"))));
  chatEls = { el, list, input, toggle, collapsible };
  if (collapsible) updateChatToggle();
  fillChatList();
  return el;
}

function setChatOpen(open) {
  chatOpen = open;
  if (open) chatUnread = 0;
  if (!chatEls) return;
  chatEls.el.classList.toggle("open", open);
  updateChatToggle();
  if (open) { scrollChatToEnd(); chatEls.input.focus(); }
}

function updateChatToggle() {
  if (!chatEls || !chatEls.collapsible) return;
  chatEls.toggle.textContent = chatUnread > 0 ? `Chat · ${chatUnread} new` : "Chat";
  chatEls.toggle.setAttribute("aria-expanded", chatOpen ? "true" : "false");
  chatEls.toggle.classList.toggle("unread", chatUnread > 0);
}

function chatLine(m) {
  return h("div.chat-line", { class: m.seat === session.seat ? "mine" : "" },
    dot(m.seat),
    h("strong", `${m.name || `Seat ${m.seat + 1}`}:`),
    h("span.chat-text", m.text));
}

function fillChatList() {
  if (!chatEls) return;
  const { list } = chatEls;
  const atEnd = list.scrollHeight - list.scrollTop - list.clientHeight < 40;
  list.replaceChildren(...(session.chat.length
    ? session.chat.map(chatLine)
    : [h("div.muted.chat-empty", "No messages yet. Say hi!")]));
  if (atEnd) list.scrollTop = list.scrollHeight;
}

function scrollChatToEnd() {
  if (chatEls) chatEls.list.scrollTop = chatEls.list.scrollHeight;
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
  // The lobby is redrawn whenever anything changes; don't make someone lose their place while typing.
  const typing = chatEls && document.activeElement === chatEls.input
    ? { start: chatEls.input.selectionStart, end: chatEls.input.selectionEnd } : null;
  chatEls = null;
  let nodes;
  if (screen === "lobby" && session.room) nodes = renderLobby(session.room);
  else if (screen === "start") nodes = renderStart(prefillCode);
  else nodes = renderHome();
  app.replaceChildren(...nodes.filter(Boolean));
  scrollChatToEnd();
  if (typing && chatEls) {
    chatEls.input.focus();
    try { chatEls.input.setSelectionRange(typing.start, typing.end); } catch (e) { /* not supported */ }
  }
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

session.on("chat", message => {
  fillChatList();
  if (message && chatEls && chatEls.collapsible && !chatOpen && message.seat !== session.seat) {
    chatUnread++;
    updateChatToggle();
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
