// Prism Rush online: match the color, number or picture; chain same numbers; first to empty their hand wins.
import { h, StoryLog } from "../ui.js";

const COLORS = [
  { name: "Cyan", hex: "#2EC8F0", shape: "◆" },
  { name: "Gold", hex: "#FFC83D", shape: "☀" },
  { name: "Rose", hex: "#FF5FA2", shape: "★" },
  { name: "Lime", hex: "#72D84E", shape: "🍃" }
];
const ACTIONS = { flip: { label: "Flip", icon: "⇄" }, hop: { label: "Hop", icon: "↷" }, pulse: { label: "Pulse", icon: "⚡" }, prism: { label: "Prism", icon: "🌈" } };

/** Card ids: color c uses 20c..20c+19 (numbers 1-7 twice, then Flip, Hop, Pulse twice); 80-83 are Prisms. */
export function cardInfo(id) {
  if (id >= 80) return { id, color: -1, kind: "prism", number: 0 };
  const color = Math.floor(id / 20), k = id % 20;
  if (k < 14) return { id, color, kind: "number", number: Math.floor(k / 2) + 1 };
  return { id, color, kind: ["flip", "hop", "pulse"][Math.floor((k - 14) / 2)], number: 0 };
}

export function cardName(id) {
  const c = cardInfo(id);
  if (c.kind === "prism") return "Prism";
  return `${COLORS[c.color].name} ${c.kind === "number" ? c.number : ACTIONS[c.kind].label}`;
}

/** The same matching rule the server uses (PrismModule.IsPlayable). */
export function isPlayable(id, topId, activeColor, pending) {
  const c = cardInfo(id), top = cardInfo(topId);
  if (pending > 0) return c.kind === "pulse";
  if (c.kind === "prism") return true;
  if (c.color === activeColor) return true;
  if (c.kind === "number" && top.kind === "number" && c.number === top.number) return true;
  if (c.kind !== "number" && c.kind === top.kind) return true;
  return false;
}

export default {
  id: "prism-rush",
  name: "Prism Rush",
  theme: "theme-prism",
  artClass: "art-prism",
  blurb: "A colorful race to empty your hand. Match the color, number or picture, chain same numbers, and zap friends with Pulses.",
  art: () => [4, 28, 54, 80].map(id => cardEl(id, { small: true })),
  create
};

function cardEl(id, opts = {}) {
  const c = cardInfo(id);
  const big = c.kind === "number" ? String(c.number) : ACTIONS[c.kind].icon;
  const el = h("div.pr-card", {
    class: (c.kind === "prism" ? "wild " : "") + (opts.big ? "big-card " : "") + (opts.cls || ""),
    style: { "--pc": c.color >= 0 ? COLORS[c.color].hex : "#2A1B47" },
    title: cardName(id),
    role: opts.onclick ? "button" : null,
    tabindex: opts.onclick ? 0 : null,
    onclick: opts.onclick || null,
    onkeydown: opts.onclick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); opts.onclick(); } } : null
  },
    h("span.corner", c.kind === "number" ? String(c.number) : ""),
    h("div.face",
      h("span.big", big),
      c.color >= 0 ? h("span.shape", COLORS[c.color].shape) : null,
      c.kind === "pulse" ? h("span.shape", "+2") : null),
    opts.order ? h("span.order", String(opts.order)) : null);
  if (opts.small) Object.assign(el.style, { width: "46px", height: "66px", cursor: "default" });
  return el;
}

function create(area, api) {
  let view = null, room = null;
  let selection = [];
  let waiting = false;
  let lastMatch = -1;
  let picker = null;
  const story = new StoryLog(5);

  const myTurn = () => view && room && room.status === "playing" && view.currentSeat === api.you && !waiting;

  function selectable(id) {
    if (!myTurn()) return false;
    if (view.drawnCard >= 0) return id === view.drawnCard;
    if (!selection.length) return isPlayable(id, view.topCard, view.activeColor, view.pendingDraw);
    const first = cardInfo(selection[0]), c = cardInfo(id);
    return first.kind === "number" && c.kind === "number" && c.number === first.number;
  }

  function toggle(id) {
    if (!myTurn()) return;
    if (selection.includes(id)) {
      selection = selection.filter(x => x !== id);
      if (!selection.some(x => isPlayable(x, view.topCard, view.activeColor, view.pendingDraw))) selection = [];
    } else if (selectable(id)) {
      selection.push(id);
    } else if (view.drawnCard < 0 && isPlayable(id, view.topCard, view.activeColor, view.pendingDraw)) {
      selection = [id];
    } else {
      api.sound.oops();
      api.toast(view.drawnCard >= 0 ? "You can only play the card you just drew, or keep it."
        : view.pendingDraw > 0 ? "Only a Pulse can answer a Pulse. Or take the cards."
        : "That card doesn't match the color, number or picture.");
      return;
    }
    api.sound.tap();
    draw();
  }

  function ordered() {
    const lead = selection.find(x => isPlayable(x, view.topCard, view.activeColor, view.pendingDraw));
    if (lead === undefined) return [];
    return [lead, ...selection.filter(x => x !== lead)];
  }

  function play() {
    if (!myTurn()) return;
    const cards = ordered();
    if (!cards.length) { api.toast("Tap a glowing card first."); return; }
    if (cardInfo(cards[cards.length - 1]).kind === "prism") { openPicker(cards); return; }
    send({ kind: "play", cards });
  }

  function send(move) {
    waiting = true;
    selection = [];
    api.send(move);
    draw();
  }

  function openPicker(cards) {
    closePicker();
    picker = h("div.modal",
      h("div.panel",
        h("h3", { style: { textAlign: "center" } }, "🌈 Pick the next color"),
        h("div.pr-picker", COLORS.map((col, i) => h("button", {
          style: { background: col.hex },
          onclick: () => { closePicker(); send({ kind: "play", cards, color: i }); }
        }, `${col.shape} ${col.name}`))),
        h("button.btn", { onclick: closePicker }, "Not yet")));
    document.body.appendChild(picker);
  }
  function closePicker() { if (picker) { picker.remove(); picker = null; } }

  function describe(events) {
    const lines = [];
    for (const e of events || []) {
      const who = api.seatName(e.seat);
      switch (e.kind) {
        case "played":
          lines.push(e.cards.length > 1
            ? `${who} chained ${e.cards.length} cards: ${e.cards.map(cardName).join(", ")}!`
            : `${who} played ${cardName(e.cards[0])}${cardInfo(e.cards[0]).kind === "prism" ? ` and chose ${COLORS[e.color].name}` : ""}.`);
          break;
        case "drew": lines.push(e.count > 1 ? `${who} drew ${e.count} cards.` : `${who} drew a card.`); break;
        case "pulsePenalty": lines.push(`${who} took ${e.count} cards. Zap!`); break;
        case "kept": lines.push(`${who} kept the card.`); break;
        case "flipped": lines.push("Flip! Play changes direction."); break;
        case "hopped": lines.push(`Hop! ${who} is skipped.`); break;
        case "pulsed": lines.push(`Pulse! ${who} owes +${e.count}.`); break;
        case "reshuffled": lines.push("The played pile was shuffled into the deck."); break;
        case "lastLight": lines.push(`Last light! ${who} has one card left!`); break;
        case "nothingToDraw": lines.push(`${who} couldn't draw: no cards left.`); break;
        case "won": lines.push(`${who} played their last card!`); break;
        case "stalemate": lines.push("Nobody can play or draw. Fewest cards wins!"); break;
        default: break;
      }
    }
    return lines.reverse();
  }

  function draw() {
    area.replaceChildren();
    if (!view) return;
    const mine = myTurn();
    const active = view.activeColor >= 0 ? COLORS[view.activeColor] : COLORS[0];

    const deck = h("button.pr-deck", {
      title: "Draw until you get a card you can play", disabled: !mine || view.drawnCard >= 0,
      onclick: () => { if (myTurn() && view.drawnCard < 0) send({ kind: "draw" }); }
    }, h("div.back"), h("span.count", String(view.drawPileCount)));

    const pile = h("div.pr-pile", { style: { "--active": active.hex } },
      h("div.dir", { class: view.direction < 0 ? "ccw" : "" }),
      cardEl(view.topCard, { big: true }),
      view.pendingDraw > 0 ? h("span.badge", `+${view.pendingDraw}`) : null,
      h("span.colorname", `${active.shape} ${active.name}`));

    area.appendChild(h("div.pr-centre", deck, pile,
      h("div.muted", { style: { fontSize: "13px", maxWidth: "140px" } },
        view.direction < 0 ? "↺ Play goes backwards" : "↻ Play goes forwards")));

    area.appendChild(story.render());

    // Actions
    if (mine) {
      const cards = ordered();
      const playLabel = cards.length === 0 ? "Play" : cards.length === 1 ? `Play ${cardName(cards[0])}` : `Play ${cards.length} cards`;
      area.appendChild(h("div.actions",
        h("button.btn.primary", { disabled: !cards.length, onclick: play }, playLabel),
        view.drawnCard >= 0
          ? h("button.btn", { onclick: () => send({ kind: "keep" }) }, "Keep it")
          : h("button.btn", { onclick: () => send({ kind: "draw" }) }, view.pendingDraw > 0 ? `Take +${view.pendingDraw}` : "Draw")));
    }

    // Your hand
    const hand = [...view.myHand].sort(sortHand);
    area.appendChild(h("div.pr-hand", hand.map(id => {
      const sel = selection.indexOf(id);
      const can = selectable(id);
      return cardEl(id, {
        cls: (sel >= 0 ? "sel " : can ? "playable " : mine ? "dim " : "") + (id === view.drawnCard ? "new" : ""),
        order: sel >= 0 && selection.length > 1 ? sel + 1 : 0,
        onclick: () => toggle(id)
      });
    })));
    if (!hand.length) area.appendChild(h("p.muted", { style: { textAlign: "center" } }, "No cards!"));
  }

  function sortHand(a, b) {
    const x = cardInfo(a), y = cardInfo(b);
    const cx = x.color < 0 ? 9 : x.color, cy = y.color < 0 ? 9 : y.color;
    if (cx !== cy) return cx - cy;
    const kx = x.kind === "number" ? x.number : 10 + ["flip", "hop", "pulse", "prism"].indexOf(x.kind);
    const ky = y.kind === "number" ? y.number : 10 + ["flip", "hop", "pulse", "prism"].indexOf(y.kind);
    return kx - ky || a - b;
  }

  return {
    update(r) {
      room = r;
      const next = r.view;
      if (!next) return;
      if (!view || next.turn !== view.turn || r.match !== lastMatch) { waiting = false; selection = []; }
      lastMatch = r.match;
      const fresh = story.add(`${r.match}:${next.turn}`, describe(next.lastEvents));
      if (fresh) api.sound.card();
      view = next;
      if (view.drawnCard >= 0 && view.currentSeat === api.you && !selection.length) selection = [view.drawnCard];
      draw();
    },
    onError(request) { if (request === "move") { waiting = false; draw(); } },
    destroy() { closePicker(); },
    seatInfo(seat) {
      if (!view) return "";
      const n = view.handCounts[seat];
      return n === 1 ? "1 card · last light!" : `${n} cards`;
    },
    banner(r) {
      if (!view || r.status !== "playing") return ["", false];
      if (view.currentSeat === api.you) {
        if (waiting) return ["...", true];
        if (view.drawnCard >= 0) return [`You drew ${cardName(view.drawnCard)}! Play it or keep it.`, true];
        if (view.pendingDraw > 0) return [`Pulse! Answer with a Pulse or take +${view.pendingDraw}.`, true];
        return ["Your turn! Match the color, number or picture.", true];
      }
      return [`${api.seatName(view.currentSeat)}'s turn`, false];
    },
    ranking(r) {
      if (!view) return [];
      return view.handCounts.map((n, seat) => ({ seat, n }))
        .sort((a, b) => a.n - b.n)
        .map(x => ({ seat: x.seat, text: x.n === 0 ? "out!" : `${x.n} left` }));
    }
  };
}
