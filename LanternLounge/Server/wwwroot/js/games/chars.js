// The four Hide & Seek characters, drawn with plain canvas shapes (no image files needed).
// Each is a little round critter seen from a bit above, looking the way it walks.

export const CHARACTERS = [
  { id: "red-panda", name: "Red Panda", blurb: "Rusty fur, ringed tail, white cheeks." },
  { id: "siamese-cat", name: "Siamese Cat", blurb: "Cream coat, dark paws, bright blue eyes." },
  { id: "honey-badger", name: "Honey Badger", blurb: "Fearless, with a silver stripe down the back." },
  { id: "brown-cat", name: "Brown Cat", blurb: "A fluffy brown cat with a pink bow." }
];

const byId = Object.fromEntries(CHARACTERS.map(c => [c.id, c]));
export const characterName = (id) => (byId[id] ? byId[id].name : "Critter");

function disc(ctx, x, y, r, fill, stroke, lw) {
  ctx.beginPath();
  ctx.arc(x, y, r, 0, Math.PI * 2);
  if (fill) { ctx.fillStyle = fill; ctx.fill(); }
  if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = lw || 1; ctx.stroke(); }
}

function oval(ctx, x, y, rx, ry, rot, fill, stroke, lw) {
  ctx.beginPath();
  ctx.ellipse(x, y, rx, ry, rot, 0, Math.PI * 2);
  if (fill) { ctx.fillStyle = fill; ctx.fill(); }
  if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = lw || 1; ctx.stroke(); }
}

/**
 * Draws a character centred on (x, y), about `size` pixels wide.
 * (fx, fy) is the way it is facing; `t` is a time in seconds, for a little bobbing; `alpha` fades it (hiding).
 */
export function drawChar(ctx, id, x, y, size, fx = 0, fy = 1, t = 0, alpha = 1) {
  const len = Math.hypot(fx, fy) || 1;
  fx /= len; fy /= len;
  const u = size / 2;                       // one "unit": the body radius
  const px = -fy, py = fx;                  // sideways direction
  const bob = Math.sin(t * 9) * u * 0.04;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.translate(x, y + bob);
  // soft ground shadow
  oval(ctx, 0, u * 0.62, u * 0.85, u * 0.28, 0, "rgba(0,0,0,.28)");
  // everything below is drawn relative to the facing direction
  const at = (f, s) => [fx * f * u + px * s * u, fy * f * u + py * s * u];

  if (id === "red-panda") {
    const [tx, ty] = at(-1.15, 0);
    // ringed tail trailing behind
    oval(ctx, tx * 0.8, ty * 0.8, u * 0.55, u * 0.4, Math.atan2(fy, fx), "#c8582b", "#8b3a18", u * 0.06);
    for (let i = 0; i < 3; i++) {
      const [rx, ry] = at(-0.95 - i * 0.22, 0);
      oval(ctx, rx, ry, u * 0.09, u * 0.36, Math.atan2(fy, fx), "#5b2a14");
    }
    disc(ctx, 0, u * 0.1, u * 0.78, "#c8582b", "#8b3a18", u * 0.06);                  // body
    const [e1x, e1y] = at(0.25, -0.62), [e2x, e2y] = at(0.25, 0.62);
    disc(ctx, e1x, e1y - u * 0.15, u * 0.3, "#8b3a18"); disc(ctx, e2x, e2y - u * 0.15, u * 0.3, "#8b3a18");
    disc(ctx, e1x, e1y - u * 0.15, u * 0.17, "#f4e6d0"); disc(ctx, e2x, e2y - u * 0.15, u * 0.17, "#f4e6d0");
    const [hx, hy] = at(0.32, 0);
    disc(ctx, hx, hy - u * 0.1, u * 0.62, "#d96a33", "#8b3a18", u * 0.06);           // head
    const [c1x, c1y] = at(0.35, -0.38), [c2x, c2y] = at(0.35, 0.38);
    oval(ctx, c1x, c1y - u * 0.05, u * 0.26, u * 0.2, 0, "#fff6e8"); oval(ctx, c2x, c2y - u * 0.05, u * 0.26, u * 0.2, 0, "#fff6e8");
    face(ctx, at, u, fx, fy, "#2a1a14", "#2a1a14", -0.1);
  } else if (id === "siamese-cat") {
    const [tx, ty] = at(-1.1, 0.25);
    ctx.strokeStyle = "#4b3326"; ctx.lineWidth = u * 0.26; ctx.lineCap = "round";
    ctx.beginPath(); ctx.moveTo(...at(-0.6, 0)); ctx.quadraticCurveTo(...at(-1.1, -0.35), tx, ty); ctx.stroke();
    disc(ctx, 0, u * 0.1, u * 0.76, "#f1e3c6", "#b79b72", u * 0.05);                  // body
    const [e1x, e1y] = at(0.2, -0.6), [e2x, e2y] = at(0.2, 0.6);
    for (const [ex, ey] of [[e1x, e1y], [e2x, e2y]]) {
      ctx.beginPath();
      ctx.moveTo(ex - u * 0.28, ey - u * 0.05); ctx.lineTo(ex, ey - u * 0.62); ctx.lineTo(ex + u * 0.28, ey - u * 0.05); ctx.closePath();
      ctx.fillStyle = "#4b3326"; ctx.fill();
    }
    const [hx, hy] = at(0.3, 0);
    disc(ctx, hx, hy - u * 0.1, u * 0.6, "#f6ead2", "#b79b72", u * 0.05);            // head
    oval(ctx, hx + fx * u * 0.12, hy - u * 0.08 + fy * u * 0.12, u * 0.38, u * 0.34, 0, "rgba(75,51,38,.9)"); // dark mask
    const [p1x, p1y] = at(0.55, -0.45), [p2x, p2y] = at(0.55, 0.45);
    disc(ctx, p1x, p1y + u * 0.35, u * 0.17, "#4b3326"); disc(ctx, p2x, p2y + u * 0.35, u * 0.17, "#4b3326"); // paws
    face(ctx, at, u, fx, fy, "#4aa3e8", "#e9a6a6", -0.1, true);
  } else if (id === "honey-badger") {
    disc(ctx, 0, u * 0.1, u * 0.82, "#23232b", "#0f0f14", u * 0.06);                  // body
    // silver stripe along the back, from the head to the tail
    const [a1x, a1y] = at(0.55, -0.62), [a2x, a2y] = at(0.55, 0.62), [b1x, b1y] = at(-0.7, -0.42), [b2x, b2y] = at(-0.7, 0.42);
    ctx.beginPath();
    ctx.moveTo(a1x, a1y + u * 0.1); ctx.lineTo(a2x, a2y + u * 0.1); ctx.lineTo(b2x, b2y + u * 0.1); ctx.lineTo(b1x, b1y + u * 0.1); ctx.closePath();
    ctx.fillStyle = "#cfd4dc"; ctx.fill();
    const [e1x, e1y] = at(0.15, -0.7), [e2x, e2y] = at(0.15, 0.7);
    disc(ctx, e1x, e1y - u * 0.1, u * 0.2, "#23232b"); disc(ctx, e2x, e2y - u * 0.1, u * 0.2, "#23232b");
    const [hx, hy] = at(0.42, 0);
    disc(ctx, hx, hy - u * 0.1, u * 0.52, "#2d2d37", "#0f0f14", u * 0.05);            // head
    const [sx, sy] = at(0.9, 0);
    oval(ctx, sx, sy - u * 0.06, u * 0.3, u * 0.24, Math.atan2(fy, fx), "#e8e1d6");   // pale snout
    face(ctx, at, u, fx, fy, "#f2d34a", "#111", -0.1);
  } else {
    // Brown Cat: a fluffy brown cat with a pink bow. Fur is a ring of little tufts around a round body.
    const fur = "#9a6a43", dark = "#6e4527", light = "#e0bd93";
    const tuft = (cx, cy, r, n, color) => {
      for (let i = 0; i < n; i++) {
        const a = (i / n) * Math.PI * 2;
        disc(ctx, cx + Math.cos(a) * r, cy + Math.sin(a) * r, r * 0.28, color);
      }
    };
    // big fluffy tail curling up behind
    const [t1x, t1y] = at(-1.0, 0.35);
    ctx.strokeStyle = dark; ctx.lineWidth = u * 0.52; ctx.lineCap = "round";
    ctx.beginPath(); ctx.moveTo(...at(-0.5, 0)); ctx.quadraticCurveTo(...at(-1.15, -0.5), t1x, t1y); ctx.stroke();
    ctx.strokeStyle = fur; ctx.lineWidth = u * 0.4;
    ctx.beginPath(); ctx.moveTo(...at(-0.5, 0)); ctx.quadraticCurveTo(...at(-1.15, -0.5), t1x, t1y); ctx.stroke();
    tuft(0, u * 0.1, u * 0.78, 14, dark);
    disc(ctx, 0, u * 0.1, u * 0.8, fur);                                              // fluffy body
    tuft(0, u * 0.1, u * 0.72, 12, fur);
    oval(ctx, 0, u * 0.2, u * 0.4, u * 0.5, 0, "rgba(224,189,147,.55)");              // lighter tummy
    const [e1x, e1y] = at(0.2, -0.62), [e2x, e2y] = at(0.2, 0.62);
    for (const [ex, ey] of [[e1x, e1y], [e2x, e2y]]) {
      ctx.beginPath();
      ctx.moveTo(ex - u * 0.3, ey - u * 0.02); ctx.lineTo(ex, ey - u * 0.66); ctx.lineTo(ex + u * 0.3, ey - u * 0.02); ctx.closePath();
      ctx.fillStyle = dark; ctx.fill();
      ctx.beginPath();
      ctx.moveTo(ex - u * 0.15, ey - u * 0.05); ctx.lineTo(ex, ey - u * 0.42); ctx.lineTo(ex + u * 0.15, ey - u * 0.05); ctx.closePath();
      ctx.fillStyle = "#f0a5b4"; ctx.fill();
    }
    const [hx, hy] = at(0.32, 0);
    tuft(hx, hy - u * 0.1, u * 0.56, 12, fur);                                        // fluffy cheeks
    disc(ctx, hx, hy - u * 0.1, u * 0.6, fur, dark, u * 0.04);                        // head
    const [mx, my] = at(0.62, 0);
    oval(ctx, mx, my - u * 0.1, u * 0.3, u * 0.24, 0, light);                         // muzzle
    const [p1x, p1y] = at(0.55, -0.5), [p2x, p2y] = at(0.55, 0.5);
    disc(ctx, p1x, p1y + u * 0.35, u * 0.17, light); disc(ctx, p2x, p2y + u * 0.35, u * 0.17, light); // paws
    // pink bow on one ear
    const [bx, by] = at(0.4, 0.42);
    for (const s2 of [-1, 1]) {
      ctx.beginPath();
      ctx.moveTo(bx, by - u * 0.55);
      ctx.lineTo(bx + s2 * u * 0.3, by - u * 0.72); ctx.lineTo(bx + s2 * u * 0.3, by - u * 0.38); ctx.closePath();
      ctx.fillStyle = "#ff7fa8"; ctx.fill();
    }
    disc(ctx, bx, by - u * 0.55, u * 0.09, "#e0527f");
    face(ctx, at, u, fx, fy, "#2a1a14", "#e58a9b", -0.1, true);
  }
  ctx.restore();
}

/** Two eyes and (optionally) a nose, pushed toward the way the critter is facing. */
function face(ctx, at, u, fx, fy, eyeColor, noseColor, lift, bigEyes, noNose) {
  const ey = u * (lift || 0);
  for (const s of [-1, 1]) {
    const [x, y] = at(0.62, s * 0.24);
    disc(ctx, x, y + ey, u * (bigEyes ? 0.14 : 0.1), eyeColor);
    disc(ctx, x + u * 0.03, y + ey - u * 0.03, u * 0.035, "rgba(255,255,255,.85)");
  }
  if (!noNose) {
    const [nx, ny] = at(0.88, 0);
    disc(ctx, nx, ny + ey, u * 0.09, noseColor);
  }
}

/** A small canvas element showing one character, for the picker and the player chips. */
export function charCanvas(id, px = 56, lookX = 0.3, lookY = 1) {
  const dpr = Math.min(2, window.devicePixelRatio || 1);
  const c = document.createElement("canvas");
  c.width = c.height = Math.round(px * dpr);
  c.style.width = c.style.height = px + "px";
  c.className = "char-art";
  c.setAttribute("role", "img");
  c.setAttribute("aria-label", characterName(id));
  const ctx = c.getContext("2d");
  ctx.scale(dpr, dpr);
  drawChar(ctx, id, px / 2, px / 2 - px * 0.04, px * 0.72, lookX, lookY, 0, 1);
  return c;
}
