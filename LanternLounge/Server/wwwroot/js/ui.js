// Small helpers shared by every screen: building elements, toasts, sounds and seat colors.

/** h("div.class#id", {attrs}, ...children) builds an element. Strings become text. */
export function h(tag, attrs, ...children) {
  const [name, ...rest] = tag.split(/(?=[.#])/);
  const el = document.createElement(name || "div");
  for (const part of rest) {
    if (part.startsWith(".")) el.classList.add(part.slice(1));
    else if (part.startsWith("#")) el.id = part.slice(1);
  }
  if (attrs && (typeof attrs !== "object" || attrs instanceof Node || Array.isArray(attrs))) {
    children.unshift(attrs);
    attrs = null;
  }
  if (attrs) {
    for (const [key, value] of Object.entries(attrs)) {
      if (value === undefined || value === null || value === false) continue;
      if (key.startsWith("on") && typeof value === "function") el.addEventListener(key.slice(2), value);
      else if (key === "style" && typeof value === "object") {
        for (const [prop, v] of Object.entries(value)) {
          if (prop.startsWith("--")) el.style.setProperty(prop, v);
          else el.style[prop] = v;
        }
      }
      else if (key === "class") el.className += " " + value;
      else if (key in el && typeof value !== "string") el[key] = value;
      else el.setAttribute(key, value === true ? "" : value);
    }
  }
  append(el, children);
  return el;
}

function append(el, children) {
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    if (Array.isArray(child)) append(el, child);
    else el.appendChild(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

export const SEAT_COLORS = ["var(--seat0)", "var(--seat1)", "var(--seat2)", "var(--seat3)"];
export const SEAT_COLOR_NAMES = ["Red", "Yellow", "Blue", "Green"];

export function dot(seat, extra) {
  return h("span.dot", { style: { background: SEAT_COLORS[seat % 4] }, class: extra || "" });
}

let toastTimer;
export function toast(text, ms = 2200) {
  const el = document.getElementById("toast");
  if (!el) return;
  el.textContent = text;
  el.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove("show"), ms);
}

// ---------- Sounds: tiny synthesized plinks, started after the first tap ----------
let audio = null;
let soundOn = true;
try { soundOn = localStorage.getItem("ll.sound") !== "off"; } catch (e) { /* storage blocked */ }

export function isSoundOn() { return soundOn; }
export function setSoundOn(on) {
  soundOn = on;
  try { localStorage.setItem("ll.sound", on ? "on" : "off"); } catch (e) { /* ignore */ }
}

function tone(freq, start, length, type = "sine", gain = 0.15) {
  if (!soundOn) return;
  try {
    if (!audio) audio = new (window.AudioContext || window.webkitAudioContext)();
    if (audio.state === "suspended") audio.resume();
    const t = audio.currentTime + start;
    const o = audio.createOscillator();
    const g = audio.createGain();
    o.type = type;
    o.frequency.setValueAtTime(freq, t);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(gain, t + 0.01);
    g.gain.exponentialRampToValueAtTime(0.0001, t + length);
    o.connect(g).connect(audio.destination);
    o.start(t);
    o.stop(t + length + 0.02);
  } catch (e) { /* audio not available */ }
}

export const sound = {
  tap: () => tone(880, 0, 0.06, "sine", 0.06),
  drop: () => tone(420, 0, 0.18, "triangle"),
  card: () => tone(1200, 0, 0.08, "triangle", 0.08),
  turn: () => { tone(784, 0, 0.2, "triangle"); tone(1175, 0.12, 0.25, "triangle"); },
  win: () => [523, 659, 784, 1047, 1319].forEach((f, i) => tone(f, i * 0.09, 0.3, "triangle")),
  oops: () => tone(150, 0, 0.18, "square", 0.06),
  coins: () => [1568, 1319, 1760].forEach((f, i) => tone(f, i * 0.05, 0.1, "sine", 0.08))
};

/** Remembers the last few lines of a game's story, without repeating a move seen twice. */
export class StoryLog {
  constructor(max = 5) { this.max = max; this.lines = []; this.lastKey = null; }
  add(key, lines) {
    if (key === this.lastKey) return false;
    this.lastKey = key;
    for (const line of lines) if (line) this.lines.unshift(line);
    this.lines.length = Math.min(this.lines.length, this.max);
    return true;
  }
  render() { return h("div.log", this.lines.length ? this.lines.map(l => h("div", l)) : [h("div.muted", "Good luck, everyone!")]); }
}
