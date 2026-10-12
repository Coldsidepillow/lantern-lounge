// Tidebound online: ask a friend for a creature you hold; pairs make pods; most pods wins.
import { h, StoryLog } from "../ui.js";

const CREATURES = [
  ["Brinekin", "🦀", "reef"], ["Pinsnap Shrimp", "🦐", "reef"], ["Glimmerjelly", "🪼", "drift"], ["Mistfin Wisp", "🫧", "drift"],
  ["Puffcap Ray", "🐠", "shallows"], ["Ripple Seahare", "🐚", "shallows"], ["Lanternmoss", "🐌", "glow"], ["Starspeck Squid", "🦑", "glow"],
  ["Barnacle Owlet", "🦉", "kelpwood"], ["Kelpwhisk", "🪸", "abyss"], ["Bubblebeak Puffer", "🐡", "reef"], ["Cloudfin Turtle", "🐢", "drift"],
  ["Sandsprite Starfish", "⭐", "shallows"], ["Twinkle Urchin", "🌟", "glow"], ["Kelpcap Octopus", "🐙", "kelpwood"], ["Glowbell Angler", "🏮", "abyss"]
];
const FAMILY_COLORS = { reef: "#FFB59F", drift: "#C9BAFF", shallows: "#A8EBDD", glow: "#FFE08A", kelpwood: "#B9E6B4", abyss: "#A9C8FF" };
const TIDES = ["Dawn", "Noon", "Dusk", "Night"];
const creatureOf = card => Math.floor(card / 4);
const nameOf = c => (CREATURES[c] || ["a creature"])[0];

export default {
  id: "tidebound",
  name: "Tidebound",
  theme: "theme-tide",
  artClass: "art-tide",
  blurb: "Ask friends for sea creatures you already hold. Every matching pair makes a pod. No luck? Dive into the Current!",
  art: () => ["🦀", "🪼", "🐢", "🐙"].map(e => h("span", e)),
  create
};

function create(area, api) {
  let view = null, room = null;
  let target = -1, creature = -1;
  let waiting = false;
  let lastMatch = -1;
  const story = new StoryLog(5);

  function canAsk() { return view && room && room.status === "playing" && view.currentSeat === api.you && !waiting; }

  function ask() {
    if (!canAsk()) return;
    if (target < 0) { api.toast("Tap a friend at the top to ask."); return; }
    if (creature < 0) { api.toast("Tap a creature in your hand."); return; }
    waiting = true;
    api.send({ target, creature });
    draw();
  }

  function describe(events) {
    const lines = [];
    for (const e of events || []) {
      const who = api.seatName(e.seat);
      switch (e.kind) {
        case "asked":
          lines.push(e.count > 0
            ? `${who} asked ${api.seatName(e.targetSeat)} for ${nameOf(e.creature)}: got ${e.count}!`
            : `${who} asked ${api.seatName(e.targetSeat)} for ${nameOf(e.creature)}. Nope! Dive!`);
          break;
        case "dove": lines.push(`${who} dove into the Current.`); break;
        case "luckyDive": lines.push(`${who} found a ${nameOf(e.creature)}! Go again.`); break;
        case "currentEmpty": lines.push(`The Current is empty, so the tide passes.`); break;
        case "refilled": lines.push(`${who} had no cards and drew one.`); break;
        case "noOneToAsk": lines.push(`Nobody else had cards, so ${who} drew one.`); break;
        case "podFormed": lines.push(`${who} made a ${nameOf(e.creature)} pod! ${(CREATURES[e.creature] || [])[1] || ""}`); break;
        case "gameOver": lines.push("Every pod is claimed!"); break;
        default: break;
      }
    }
    return lines.reverse();
  }

  function draw() {
    area.replaceChildren();
    if (!view) return;
    const mine = canAsk();

    // Keep the choices valid.
    const counts = new Map();
    for (const card of view.myHand) counts.set(creatureOf(card), (counts.get(creatureOf(card)) || 0) + 1);
    if (!counts.has(creature)) creature = -1;
    if (target >= 0 && (target === api.you || !view.handCounts[target])) target = -1;
    if (mine && target < 0) {
      for (let i = 1; i < view.playerCount; i++) {
        const s = (api.you + i) % view.playerCount;
        if (view.handCounts[s] > 0) { target = s; break; }
      }
    }

    area.appendChild(h("div.tb-current",
      h("div.stack", String(view.currentCount)),
      h("div",
        h("strong", "The Current"),
        h("div.muted", view.currentCount ? `${view.currentCount} cards to dive for` : "All gone!"))));

    area.appendChild(story.render());

    if (mine) {
      area.appendChild(h("div.actions",
        h("div.muted", { style: { width: "100%", textAlign: "center" } },
          target >= 0 && creature >= 0 ? `Ask ${api.seatName(target)} for a ${nameOf(creature)}?` :
          creature < 0 ? "1. Tap a creature in your hand." : "2. Tap a friend at the top."),
        h("button.btn.primary.tb-ask", { disabled: target < 0 || creature < 0, onclick: ask }, "Ask!")));
    }

    const myPods = (view.pods[api.you] || []);
    area.appendChild(h("h3", { style: { margin: "16px 0 8px", fontSize: "18px" } },
      `Your hand · ${view.myHand.length} cards · ${myPods.length} ${myPods.length === 1 ? "pod" : "pods"}`));
    area.appendChild(h("div.tb-pods", myPods.map(c => h("span", { title: `${nameOf(c)} pod` }, CREATURES[c][1]))));
    if (!view.myHand.length) area.appendChild(h("p.muted", "No cards right now. You'll draw one when your tide comes."));
    area.appendChild(h("div.tb-hand", [...counts.keys()].sort((a, b) => a - b).map(c => {
      const [nm, emoji, family] = CREATURES[c];
      const tides = view.myHand.filter(card => creatureOf(card) === c).map(card => TIDES[card % 4]).join(", ");
      return h("button.tb-creature", {
        class: c === creature ? "sel" : "", disabled: !mine, title: tides,
        onclick: () => { creature = c; api.sound.tap(); draw(); }
      },
        h("div.art", { style: { background: FAMILY_COLORS[family] } }, emoji),
        h("div.nm", nm),
        h("div.cnt", counts.get(c) > 1 ? `× ${counts.get(c)}` : tides));
    })));
  }

  return {
    update(r) {
      room = r;
      const next = r.view;
      if (!next) return;
      if (!view || next.turn !== view.turn || r.match !== lastMatch) waiting = false;
      lastMatch = r.match;
      const fresh = story.add(`${r.match}:${next.turn}`, describe(next.lastEvents));
      if (fresh && (next.lastEvents || []).some(e => e.kind === "podFormed" && e.seat === api.you)) api.sound.win();
      else if (fresh) api.sound.card();
      view = next;
      draw();
    },
    onError(request) { if (request === "move") { waiting = false; draw(); } },
    seatInfo(seat) {
      if (!view) return "";
      const pods = (view.pods[seat] || []).length;
      return `${view.handCounts[seat]} cards · ${pods} ${pods === 1 ? "pod" : "pods"}`;
    },
    seatClass(seat) { return canAsk() && seat === target ? "target" : ""; },
    canPickSeat(seat) { return canAsk() && seat !== api.you && view.handCounts[seat] > 0; },
    onSeat(seat) { target = seat; api.sound.tap(); draw(); api.refresh(); },
    banner(r) {
      if (!view || r.status !== "playing") return ["", false];
      if (view.currentSeat === api.you) return [waiting ? "Asking..." : "Your tide!", true];
      return [`${api.seatName(view.currentSeat)}'s tide`, false];
    },
    ranking(r) {
      if (!view) return [];
      return view.pods.map((p, seat) => ({ seat, n: p.length }))
        .sort((a, b) => b.n - a.n)
        .map(x => ({ seat: x.seat, text: `${x.n} ${x.n === 1 ? "pod" : "pods"}` }));
    }
  };
}
