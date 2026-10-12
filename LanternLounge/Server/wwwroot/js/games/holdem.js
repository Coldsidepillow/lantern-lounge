// Treehouse Hold'em online: a kid-friendly Texas Hold'em with pretend coins.
import { h, StoryLog } from "../ui.js";

const SUITS = [
  { name: "Hearts", sym: "♥", color: "#F0386B" },
  { name: "Diamonds", sym: "♦", color: "#FF8A1F" },
  { name: "Clubs", sym: "♣", color: "#22A85A" },
  { name: "Spades", sym: "♠", color: "#3D6BFF" }
];
const RANK_LABEL = r => ({ 11: "J", 12: "Q", 13: "K", 14: "A" }[r] || String(r));
const RANK_WORD = r => ({ 2: "Two", 3: "Three", 4: "Four", 5: "Five", 6: "Six", 7: "Seven", 8: "Eight", 9: "Nine", 10: "Ten", 11: "Jack", 12: "Queen", 13: "King", 14: "Ace" }[r]);
const PLURAL = r => r === 6 ? "Sixes" : RANK_WORD(r) + "s";
const CATEGORIES = ["High Card", "Pair", "Two Pairs", "Three of a Kind", "Straight", "Flush", "Full House", "Four of a Kind", "Straight Flush", "Royal Flush"];
const rankOf = c => Math.floor(c / 4) + 2;
const suitOf = c => c % 4;
const COIN_COLORS = { 25: "#9B6BFF", 10: "#34B6F0", 5: "#FF6F91", 1: "#FFC93C" };

export default {
  id: "treehouse-holdem",
  name: "Treehouse Hold'em",
  theme: "theme-tree",
  artClass: "art-tree",
  blurb: "A friendly Texas Hold'em for family game night, played with pretend coins. Make the best five-card hand to win the prize pile.",
  art: () => [cardEl(48), cardEl(47), coinEl(25), coinEl(10)],
  create
};

function cardEl(id, size = "", extra = "") {
  if (id === null || id === undefined) return h("div.th-card.slot", { class: size });
  if (id < 0) return h("div.th-card.back", { class: size });
  const s = SUITS[suitOf(id)];
  return h("div.th-card", { class: `${size} ${extra}`, style: { "--sc": s.color }, title: `${RANK_WORD(rankOf(id))} of ${s.name}` },
    h("span.r", RANK_LABEL(rankOf(id))), h("span.s", s.sym));
}

function coinEl(value) {
  return h("span.coin", { style: { background: COIN_COLORS[value] }, title: `${value} coin` }, String(value));
}

function coinPile(amount) {
  const out = [];
  for (const v of [25, 10, 5, 1]) {
    let n = Math.floor(amount / v);
    amount -= n * v;
    n = Math.min(n, 3);
    for (let i = 0; i < n; i++) out.push(coinEl(v));
  }
  return out;
}

// ---------- Hand evaluation (for the hints; the server decides who wins) ----------
function eval5(cards) {
  const s = [...cards].sort((a, b) => b - a);
  const r = s.map(rankOf);
  const flush = s.every(c => suitOf(c) === suitOf(s[0]));
  const distinct = new Set(r).size === 5;
  let top = 0;
  if (distinct && r[0] - r[4] === 4) top = r[0];
  else if (distinct && r[0] === 14 && r[1] === 5) top = 5;
  const pack = (cat, ranks) => ranks.reduce((acc, x, i) => acc + x * Math.pow(16, 4 - i), cat * Math.pow(16, 5));
  if (top) {
    const cat = flush ? (top === 14 ? 9 : 8) : 4;
    return { score: pack(cat, [top]), cat, top, cards: s };
  }
  const groups = {};
  for (const c of s) (groups[rankOf(c)] = groups[rankOf(c)] || []).push(c);
  const g = Object.values(groups).sort((a, b) => b.length - a.length || rankOf(b[0]) - rankOf(a[0]));
  const a = g[0].length, b = g[1] ? g[1].length : 0;
  const cat = a === 4 ? 7 : a === 3 && b === 2 ? 6 : flush ? 5 : a === 3 ? 3 : a === 2 && b === 2 ? 2 : a === 2 ? 1 : 0;
  const ranks = cat === 5 ? r : g.map(x => rankOf(x[0]));
  return { score: pack(cat, ranks), cat, top: ranks[0], second: ranks[1], cards: cat === 5 ? s : g.flat() };
}

function bestHand(cards) {
  if (cards.length < 5) {
    const groups = {};
    for (const c of cards) (groups[rankOf(c)] = groups[rankOf(c)] || []).push(c);
    const g = Object.values(groups).sort((a, b) => b.length - a.length || rankOf(b[0]) - rankOf(a[0]));
    const cat = g[0].length >= 2 ? 1 : 0;
    return { cat, top: rankOf(g[0][0]), cards: g[0].length >= 2 ? g[0] : [g[0][0]] };
  }
  let best = null;
  const n = cards.length;
  for (let a = 0; a < n; a++) for (let b = a + 1; b < n; b++) for (let c = b + 1; c < n; c++)
    for (let d = c + 1; d < n; d++) for (let e = d + 1; e < n; e++) {
      const v = eval5([cards[a], cards[b], cards[c], cards[d], cards[e]]);
      if (!best || v.score > best.score) best = v;
    }
  return best;
}

/** The cards that actually make the hand (the pair, the trips...), not the leftover kickers. */
function madeCards(v) {
  if (v.cat === 0) return v.cards.slice(0, 1);
  if ([1, 2, 3, 6, 7].includes(v.cat)) {
    const count = {};
    for (const c of v.cards) count[rankOf(c)] = (count[rankOf(c)] || 0) + 1;
    return v.cards.filter(c => count[rankOf(c)] >= 2);
  }
  return v.cards;
}

function describe(v) {
  switch (v.cat) {
    case 0: return `${RANK_WORD(v.top)} high`;
    case 1: return `Pair of ${PLURAL(v.top)}`;
    case 2: return `Two Pairs: ${PLURAL(v.top)} and ${PLURAL(v.second)}`;
    case 3: return `Three ${PLURAL(v.top)}`;
    case 4: return `Straight up to ${RANK_WORD(v.top)}`;
    case 5: return `${RANK_WORD(v.top)}-high Flush`;
    case 6: return `Full House: ${PLURAL(v.top)} over ${PLURAL(v.second)}`;
    case 7: return `Four ${PLURAL(v.top)}`;
    case 8: return `Straight Flush up to ${RANK_WORD(v.top)}`;
    default: return "Royal Flush!";
  }
}

/** How often this hand wins against random hands (ties count half), by dealing the rest many times. */
function winChance(hole, board, opponents, trials = 220) {
  const known = new Set([...hole, ...board]);
  const deck = [];
  for (let c = 0; c < 52; c++) if (!known.has(c)) deck.push(c);
  const need = 5 - board.length;
  let wins = 0;
  for (let t = 0; t < trials; t++) {
    const pick = need + opponents * 2;
    for (let i = 0; i < pick; i++) { const j = i + Math.floor(Math.random() * (deck.length - i)); [deck[i], deck[j]] = [deck[j], deck[i]]; }
    const full = [...board, ...deck.slice(0, need)];
    const mine = bestHand([...hole, ...full]).score;
    let best = -1, ties = 0;
    for (let o = 0; o < opponents; o++) {
      const sc = bestHand([deck[need + 2 * o], deck[need + 2 * o + 1], ...full]).score;
      if (sc > best) { best = sc; ties = 1; } else if (sc === best) ties++;
    }
    if (mine > best) wins += 1; else if (mine === best) wins += 1 / (ties + 1);
  }
  return wins / trials;
}

function create(area, api) {
  let view = null, room = null, shown = [];
  let waiting = false;
  let hints = true;
  let hintKey = null, hintStars = 0;
  let summary = "";
  try { hints = localStorage.getItem("ll.holdemHints") !== "off"; } catch (e) { /* ignore */ }
  const story = new StoryLog(5);

  const myTurn = () => view && room && room.status === "playing" && view.phase === "betting" && view.currentSeat === api.you && !waiting;

  function act(kind) {
    if (kind === "next") {
      if (!view || view.phase !== "roundOver") return;
    } else if (!myTurn()) return;
    waiting = kind !== "next";
    api.send({ kind });
    draw();
  }

  function eventLines(events) {
    const lines = [];
    for (const e of events || []) {
      const who = e.seat >= 0 ? api.seatName(e.seat) : "";
      switch (e.kind) {
        case "roundStarted": lines.push(`Round ${e.amount} begins. ${who} deals.`); break;
        case "folded": lines.push(`${who} sits this one out.`); break;
        case "checked": lines.push(`${who} checks.`); break;
        case "called": lines.push(`${who} matches with ${e.amount} coins.`); break;
        case "raised": lines.push(`${who} adds ${e.amount} coins!`); break;
        case "allIn": lines.push(`${who} is all in!`); break;
        case "sharedCards": lines.push(["", "The Flop: 3 shared cards!", "The Turn: card number 4.", "The River: the last card!"][e.extra] || "New shared cards."); break;
        case "showdown": lines.push("Everyone shows their cards!"); break;
        case "wonCoins": lines.push(e.extra < 0 ? `${who} wins ${e.amount} coins: everyone else folded.` : `${who} wins ${e.amount} coins with ${CATEGORIES[e.extra]}!`); break;
        case "outOfCoins": lines.push(`${who} is out of coins and cheers from the side.`); break;
        case "gameOver": lines.push("Game night is over!"); break;
        default: break;
      }
    }
    return lines.reverse();
  }

  function stars() {
    const mineCards = shown[api.you] || [];
    if (mineCards.length !== 2) return 0;
    const opponents = Math.max(1, view.liveOpponents);
    const key = `${mineCards.join(",")}|${view.board.join(",")}|${opponents}`;
    if (key !== hintKey) {
      hintKey = key;
      const edge = winChance(mineCards, view.board, opponents) * (opponents + 1);
      hintStars = edge >= 2 ? 5 : edge >= 1.5 ? 4 : edge >= 1 ? 3 : edge >= 0.6 ? 2 : 1;
    }
    return hintStars;
  }

  function draw() {
    area.replaceChildren();
    if (!view) return;
    const me = api.you;
    const mineCards = shown[me] || [];
    const best = mineCards.length === 2 ? bestHand([...mineCards, ...view.board]) : null;
    const bestSet = new Set(hints && best && view.board.length ? madeCards(best) : []);

    const street = ["start", "flop", "turn", "river"].indexOf(view.street);
    const streetLabel = view.board.length === 0 ? "Peek at your 2 secret cards!" :
      ["", "The Flop: 3 shared cards", "The Turn: card number 4", "The River: the last card!"][street] || "";
    const slots = [];
    for (let i = 0; i < 5; i++) slots.push(i < view.board.length ? cardEl(view.board[i], "", bestSet.has(view.board[i]) ? "best" : "") : cardEl(null));

    // Cards shown at the showdown
    const reveals = [];
    for (let s = 0; s < view.playerCount; s++) {
      if (s !== me && view.revealed[s] && shown[s] && shown[s].length === 2) {
        reveals.push(h("div", { style: { textAlign: "center" } },
          h("div", { style: { fontSize: "12px", fontWeight: 800 } }, api.seatName(s)),
          h("div.th-mini", shown[s].map(c => cardEl(c, "sm")))));
      }
    }

    area.appendChild(h("div.th-table",
      h("div", { style: { fontWeight: 800 } }, streetLabel),
      h("div.th-board", slots),
      h("div.th-pot", coinPile(view.pot), view.pot > 0 ? `Prize pile: ${view.pot}` : "Prize pile"),
      reveals.length ? h("div.row", { style: { justifyContent: "center", gap: "14px" } }, reveals) : null,
      view.phase !== "betting" && summary ? h("div", { style: { fontWeight: 800, textAlign: "center" } }, summary) : null));

    area.appendChild(story.render());

    // You: your two cards, coins and the hint
    const folded = view.folded[me];
    const hintBox = h("div.th-hint",
      h("div.hand", best ? (view.board.length === 0 && best.cat === 0 ? `${RANK_WORD(best.top)} is your top card` : describe(best)) : "No cards this round"),
      hints && best && view.phase === "betting" && !folded
        ? [h("div.stars", "★".repeat(stars()) + "☆".repeat(5 - stars())), h("div", { style: { fontSize: "13px" } }, advice(stars(), view.board.length < 5))]
        : null,
      h("div", { style: { fontSize: "13px", marginTop: "4px" } }, `Your coins: ${view.coins[me]}`),
      h("label", { style: { fontSize: "13px", display: "flex", gap: "6px", alignItems: "center", marginTop: "6px" } },
        h("input", { type: "checkbox", checked: hints, onchange: (e) => {
          hints = e.target.checked;
          try { localStorage.setItem("ll.holdemHints", hints ? "on" : "off"); } catch (err) { /* ignore */ }
          draw();
        } }), "Show hints"));
    area.appendChild(h("div.th-me",
      h("div.row", { style: { gap: "6px" } }, mineCards.length === 2
        ? mineCards.map(c => { const el = cardEl(c, "lg", bestSet.has(c) ? "best" : ""); if (folded) el.style.opacity = ".5"; return el; })
        : [cardEl(-1, "lg"), cardEl(-1, "lg")]),
      hintBox));

    // Actions
    const actions = h("div.actions");
    if (view.phase === "roundOver") {
      actions.appendChild(h("button.btn.primary", { onclick: () => act("next") }, "Next round!"));
      actions.appendChild(h("div.muted", { style: { width: "100%", textAlign: "center", fontSize: "13px" } }, "The next round starts by itself in a few seconds."));
    } else if (myTurn()) {
      if (view.canFold) actions.appendChild(h("button.btn", { onclick: () => act("fold"), title: "Sit out this round" }, "Fold"));
      if (view.canCheck) actions.appendChild(h("button.btn.primary", { onclick: () => act("check"), title: "Stay in for free" }, "Check"));
      if (view.canCall) actions.appendChild(h("button.btn.primary", { onclick: () => act("call"), title: "Match the coins" }, `Call ${Math.min(view.costToCall, view.coins[me])}`));
      if (view.canRaise) actions.appendChild(h("button.btn", { onclick: () => act("raise"), title: "Add more coins" }, `Raise +${view.raiseSize}`));
      actions.appendChild(h("div.muted", { style: { width: "100%", textAlign: "center", fontSize: "13px" } },
        view.costToCall > 0 ? "Call matches the coins added. Raise adds more. Fold sits out this round." : "Check stays in for free. Raise adds more coins."));
    }
    if (actions.childNodes.length) area.appendChild(actions);
  }

  function advice(n, early) {
    return [, early ? "Not much yet, but cards are still coming!" : "Not much this time. It's fine to fold if someone raises.",
      early ? "Just okay so far. More cards are coming!" : "Just okay. Folding is fine if someone raises.",
      "Pretty good. Checking or calling is fine.", "Strong hand! Raising or calling are both good.", "Super strong! Raising is a great idea."][n] || "";
  }

  return {
    update(r) {
      room = r;
      const wrap = r.view;
      if (!wrap || !wrap.view) return;
      const next = wrap.view;
      const key = `${r.match}|${next.roundNumber}|${next.currentSeat}|${next.pot}|${next.phase}|${(next.lastEvents || []).length}|${next.board.length}`;
      const fresh = story.add(key, eventLines(next.lastEvents));
      if (fresh) {
        waiting = false;
        const wins = (next.lastEvents || []).filter(e => e.kind === "wonCoins");
        if (wins.length) {
          summary = wins.map(e => `${api.seatName(e.seat)} won ${e.amount} coins${e.extra >= 0 ? ` with ${CATEGORIES[e.extra]}` : ""}`).join(" · ");
          if (wins.some(e => e.seat === api.you)) api.sound.win(); else api.sound.coins();
        } else if ((next.lastEvents || []).some(e => e.kind === "called" || e.kind === "raised")) api.sound.coins();
        else api.sound.card();
        if ((next.lastEvents || []).some(e => e.kind === "roundStarted")) summary = "";
      }
      view = next;
      shown = wrap.cardsBySeat || [];
      draw();
    },
    onError(request) { if (request === "move") { waiting = false; draw(); } },
    seatInfo(seat) {
      if (!view) return "";
      const parts = [`${view.coins[seat]} coins`];
      if (!view.inRound[seat] && view.coins[seat] === 0) parts.push("cheering");
      else if (view.folded[seat]) parts.push("sat out");
      else if (view.inRound[seat] && view.coins[seat] === 0 && view.phase === "betting") parts.push("all in");
      if (view.roundBet[seat] > 0) parts.push(`+${view.roundBet[seat]}`);
      if (view.dealer === seat) parts.push("dealer");
      return parts.join(" · ");
    },
    banner(r) {
      if (!view || r.status !== "playing") return ["", false];
      if (view.phase === "roundOver") return [`Round ${view.roundNumber} of ${view.rounds} is over!`, false];
      if (view.currentSeat === api.you) return [waiting ? "..." : "Your turn!", true];
      return [`${api.seatName(view.currentSeat)}'s turn`, false];
    },
    ranking(r) {
      if (!view) return [];
      return view.coins.map((c, seat) => ({ seat, c })).sort((a, b) => b.c - a.c).map(x => ({ seat: x.seat, text: `${x.c} coins` }));
    }
  };
}
