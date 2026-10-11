// Pebble Drop online: 16 x 12 board, four players, connect four to win.
import { h, SEAT_COLORS } from "../ui.js";

const COLS = 16, ROWS = 12;

export default {
  id: "pebble-drop",
  name: "Pebble Drop",
  theme: "theme-pebble",
  artClass: "art-pebble",
  blurb: "Four players drop pebbles into a big 16 × 12 riverbed. Line up four of your color in any direction to win.",
  art: () => [0, 1, 2, 3].map(s => h("span.dot", { style: { background: SEAT_COLORS[s], width: "30px", height: "30px" } })),
  create
};

function create(area, api) {
  let view = null;
  let room = null;
  let hover = 8;
  let waiting = false;       // our move was sent; waiting for the server's board
  let lastMoveCount = -1;
  let lastMatch = -1;

  const rail = h("div.pd-rail");
  const board = h("div.pd-board", { tabindex: 0, role: "grid", "aria-label": "Pebble Drop board. Left and right arrows choose a column, Enter drops." });
  const railCells = [];
  const cells = [];
  for (let c = 0; c < COLS; c++) { const d = h("div"); railCells.push(d); rail.appendChild(d); }
  for (let r = 0; r < ROWS; r++) {
    for (let c = 0; c < COLS; c++) {
      const cell = h("div.pd-cell", h("div.pd-hole"));
      cells.push(cell);
      board.appendChild(cell);
    }
  }
  area.appendChild(h("div.pd-wrap", rail, board));
  area.appendChild(h("p.muted", { style: { textAlign: "center", fontSize: "13px", margin: "10px 0 0" } },
    "Tap a column on your turn. Keyboard: ← → to choose, Enter to drop."));

  const colAt = (e) => {
    const rect = board.getBoundingClientRect();
    const pad = rect.width * 0.014;
    return Math.max(0, Math.min(COLS - 1, Math.floor((e.clientX - rect.left - pad) / ((rect.width - 2 * pad) / COLS))));
  };
  board.addEventListener("pointermove", e => setHover(colAt(e)));
  board.addEventListener("click", e => { const c = colAt(e); setHover(c); drop(c); });
  board.addEventListener("keydown", e => {
    if (e.key === "ArrowLeft") { setHover((hover + COLS - 1) % COLS); e.preventDefault(); }
    else if (e.key === "ArrowRight") { setHover((hover + 1) % COLS); e.preventDefault(); }
    else if (e.key === "Enter" || e.key === " " || e.key === "ArrowDown") { drop(hover); e.preventDefault(); }
  });

  const cellAt = (r, c) => view.cells[r * COLS + c];
  const myTurn = () => view && room && room.status === "playing" && view.currentSeat === api.you && !waiting;

  function drop(col) {
    if (!view) return;
    if (!myTurn()) { if (room && room.status === "playing" && !waiting) api.toast("Wait for your turn."); return; }
    if (cellAt(0, col) !== -1) { api.sound.oops(); api.toast(`Column ${col + 1} is full. Pick another!`); return; }
    waiting = true;
    api.send({ column: col });
    drawHover();
  }

  function setHover(col) { if (col !== hover) { hover = col; drawHover(); } }

  function drawHover() {
    railCells.forEach(d => d.replaceChildren());
    cells.forEach(c => c.classList.remove("hover"));
    if (!view || !myTurn() || cellAt(0, hover) !== -1) return;
    railCells[hover].appendChild(h("div.pebble.ghost", { style: { "--c": SEAT_COLORS[api.you] } }));
    for (let r = 0; r < ROWS; r++) cells[r * COLS + hover].classList.add("hover");
  }

  function draw(animateCell) {
    const win = new Set(view.winningCells || []);
    for (let i = 0; i < cells.length; i++) {
      const hole = cells[i].firstChild;
      const seat = view.cells[i];
      let pebble = hole.firstChild;
      if (seat === -1) { if (pebble) pebble.remove(); continue; }
      if (!pebble) { pebble = h("div.pebble"); hole.appendChild(pebble); }
      pebble.style.setProperty("--c", SEAT_COLORS[seat]);
      pebble.className = "pebble" + (win.has(i) ? " win" : "") + (i === view.lastCell && !win.has(i) ? " last" : "");
      if (i === animateCell) {
        const row = Math.floor(i / COLS);
        pebble.style.setProperty("--from", `${-(row + 1.5) * 112}%`);
        pebble.style.setProperty("--t", `${0.25 + Math.sqrt(row + 1) * 0.09}s`);
        pebble.classList.add("drop");
      }
    }
    board.classList.toggle("waiting", !myTurn());
    drawHover();
  }

  return {
    update(r) {
      room = r;
      const next = r.view;
      if (!next) return;
      const newMatch = r.match !== lastMatch;
      const animate = !newMatch && next.moveCount === lastMoveCount + 1 && next.lastCell >= 0 ? next.lastCell : -1;
      if (next.moveCount !== lastMoveCount || next.currentSeat !== api.you || newMatch) waiting = false;
      lastMatch = r.match;
      lastMoveCount = next.moveCount;
      view = next;
      draw(animate);
      if (animate >= 0) api.sound.drop();
    },
    seatInfo(seat) {
      return ["Red", "Yellow", "Blue", "Green"][seat];
    },
    banner(r) {
      if (!view) return ["", false];
      if (r.status !== "playing") return [view.isDraw ? "The board is full. It's a draw!" : "", false];
      if (view.currentSeat === api.you) return [waiting ? "Dropping..." : "Your turn! Tap a column.", true];
      const bot = r.seats[view.currentSeat] && r.seats[view.currentSeat].isBot;
      return [`${api.seatName(view.currentSeat)}${bot ? " (bot)" : ""} is choosing a column...`, false];
    },
    ranking(r) {
      return (r.result.winningSeats || []).map(seat => ({ seat, text: "Four in a row!" }));
    }
  };
}
