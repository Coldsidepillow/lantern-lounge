// The connection to the game server: one WebSocket, JSON messages, automatic reconnect.

const WS_URL = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/ws";
const PING_MS = 15000;
const RECONNECT_MS = 2500;
const MAX_RECONNECTS = 25;
const SAVED = "ll.room";

export class Session {
  constructor() {
    this.ws = null;
    this.status = "offline";      // offline | connecting | connected | reconnecting
    this.room = null;             // the last "room" message
    this.chat = [];               // recent chat lines, oldest first
    this.seat = -1;
    this.code = null;
    this.token = null;
    this.reconnects = 0;
    this.leaving = false;
    this.listeners = { room: [], error: [], status: [], left: [], chat: [] };
    this.pingTimer = null;
    this.reconnectTimer = null;
  }

  on(event, fn) { this.listeners[event].push(fn); }
  emit(event, ...args) { for (const fn of this.listeners[event]) { try { fn(...args); } catch (e) { console.error(e); } } }

  get inRoom() { return !!this.code; }

  // ---------- Requests ----------
  create(game, name) { this.connectThen({ type: "create", game, name }); }
  join(code, name) { this.connectThen({ type: "join", code, name }); }
  start() { this.send({ type: "start" }); }
  rematch() { this.send({ type: "rematch" }); }
  move(move) { return this.send({ type: "move", move }); }
  /** Host only: back to the waiting room with this game, keeping the same group. */
  setGame(game) { return this.send({ type: "setgame", game }); }
  /** Pick your character in the waiting room (Hide & Seek). */
  choose(choice) { return this.send({ type: "choose", choice }); }
  sendChat(text) { return this.send({ type: "chat", text }); }

  leave() {
    this.send({ type: "leave" });
    this.forget();
    this.leaving = true;
    const ws = this.ws;
    this.ws = null;
    if (ws) setTimeout(() => { try { ws.close(1000, "bye"); } catch (e) { /* ignore */ } }, 300);
    this.setStatus("offline");
    this.emit("left", null);
  }

  /** After a page refresh, sit back down in the room we were in. */
  resume() {
    let saved = null;
    try { saved = JSON.parse(sessionStorage.getItem(SAVED) || "null"); } catch (e) { saved = null; }
    if (!saved || !saved.code || !saved.token) return false;
    this.code = saved.code;
    this.token = saved.token;
    this.connectThen({ type: "rejoin", code: saved.code, token: saved.token });
    return true;
  }

  // ---------- Connection ----------
  send(message) {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return false;
    this.ws.send(JSON.stringify(message));
    return true;
  }

  connectThen(first) {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) { this.send(first); return; }
    if (this.ws && this.ws.readyState === WebSocket.CONNECTING) { this.pendingFirst = first; return; }
    this.leaving = false;
    this.pendingFirst = first;
    if (this.status !== "reconnecting") this.setStatus("connecting");
    let ws;
    try { ws = new WebSocket(WS_URL); } catch (e) { this.failed(); return; }
    this.ws = ws;
    ws.onopen = () => {
      if (ws !== this.ws) return;
      this.setStatus("connected");
      if (this.pendingFirst) this.send(this.pendingFirst);
      this.pendingFirst = null;
      clearInterval(this.pingTimer);
      this.pingTimer = setInterval(() => this.send({ type: "ping" }), PING_MS);
    };
    ws.onmessage = (event) => { if (ws === this.ws) this.handle(event.data); };
    ws.onclose = () => {
      if (ws !== this.ws) return;
      clearInterval(this.pingTimer);
      this.ws = null;
      if (this.leaving) return;
      if (this.code && this.token) this.scheduleReconnect();
      else this.failed();
    };
    ws.onerror = () => { /* onclose follows */ };
  }

  failed() {
    this.setStatus("offline");
    this.emit("error", this.pendingFirst ? this.pendingFirst.type : null, "cant_connect",
      "Couldn't reach the game server. Check your internet and try again.");
    this.pendingFirst = null;
  }

  scheduleReconnect() {
    this.setStatus("reconnecting");
    clearTimeout(this.reconnectTimer);
    if (this.reconnects >= MAX_RECONNECTS) {
      const message = `Lost the connection to room ${this.code}. Join again with the code.`;
      this.forget();
      this.setStatus("offline");
      this.emit("left", message);
      return;
    }
    this.reconnects++;
    this.reconnectTimer = setTimeout(() => {
      if (this.code && this.token) this.connectThen({ type: "rejoin", code: this.code, token: this.token });
    }, RECONNECT_MS);
  }

  forget() {
    this.code = null;
    this.token = null;
    this.room = null;
    this.chat = [];
    this.seat = -1;
    this.reconnects = 0;
    clearTimeout(this.reconnectTimer);
    try { sessionStorage.removeItem(SAVED); } catch (e) { /* ignore */ }
  }

  handle(text) {
    let m;
    try { m = JSON.parse(text); } catch (e) { return; }
    switch (m.type) {
      case "joined":
        this.code = m.code;
        this.token = m.token;
        this.seat = m.seat;
        this.reconnects = 0;
        try { sessionStorage.setItem(SAVED, JSON.stringify({ code: m.code, token: m.token })); } catch (e) { /* ignore */ }
        break;
      case "chatHistory":
        if (!this.code) return;
        this.chat = Array.isArray(m.messages) ? m.messages.slice(-100) : [];
        this.emit("chat", null);
        break;
      case "chat":
        if (!this.code || !m.message) return;
        if (!this.chat.some(x => x.id === m.message.id)) this.chat.push(m.message);
        if (this.chat.length > 100) this.chat.splice(0, this.chat.length - 100);
        this.emit("chat", m.message);
        break;
      case "room":
        if (!this.code) return; // a message for a room we already left
        this.room = m;
        this.seat = m.you;
        this.emit("room", m);
        break;
      case "error":
        this.emit("error", m.request, m.code, m.message);
        if (m.request === "rejoin" && (m.code === "no_room" || m.code === "not_seated")) {
          this.forget();
          this.setStatus(this.ws ? "connected" : "offline");
          this.emit("left", m.message);
        }
        break;
      default:
        break;
    }
  }

  setStatus(status) {
    if (this.status === status) return;
    this.status = status;
    this.emit("status", status);
  }
}
