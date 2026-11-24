// Generates the Runesgalder crest as SVG. Dependency-free: `node tools/mark.mjs`
// writes crest.svg and crest-dark.svg next to this folder.
//
// The mark is constructed, not drawn by hand: four tapered feather blades fan
// from one wing foot, are cut by a mask along the true gaps between their
// spines, and are mirrored; the Algiz stave, its node and arms are separate
// paths; the ember diamond is the only coloured element.

import { writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const dir = resolve(here, '..');

const BONE = '#f1ede3';
const INK = '#11110f';
const EMBER = '#c84630';

const f = (n) => Math.round(n * 100) / 100;
const P = (p) => `${f(p[0])} ${f(p[1])}`;

// A tapered blade: pointed base at p0, spine through p1, point at p2. `sk`
// skews the thickest section toward the p2 end (sk > 1) or the p0 end (sk < 1);
// `ex` sharpens (ex > 1) or fattens (ex < 1) the taper.
function blade(p0, p1, p2, W, sk = 1, ex = 1.1, n = 64) {
  const A = [], B = [];
  const at = (t) => Math.pow(Math.sin(Math.PI * Math.pow(t, sk)), ex);
  let peak = 0;
  for (let i = 0; i <= n; i++) peak = Math.max(peak, at(i / n));
  for (let i = 0; i <= n; i++) {
    const t = i / n;
    const u = 1 - t;
    const Q = [
      u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
      u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1],
    ];
    const D = [
      2 * u * (p1[0] - p0[0]) + 2 * t * (p2[0] - p1[0]),
      2 * u * (p1[1] - p0[1]) + 2 * t * (p2[1] - p1[1]),
    ];
    const L = Math.hypot(D[0], D[1]) || 1;
    const half = (W * at(t)) / peak / 2;
    A.push([Q[0] - D[1] / L * half, Q[1] + D[0] / L * half]);
    B.push([Q[0] + D[1] / L * half, Q[1] - D[0] / L * half]);
  }
  const path = (pts) => {
    const n2 = pts.length;
    const g = (i) => pts[Math.max(0, Math.min(n2 - 1, i))];
    let d = '';
    for (let i = 0; i < n2 - 1; i++) {
      const a = g(i - 1), b = g(i), c = g(i + 1), e = g(i + 2);
      d += `C${P([b[0] + (c[0] - a[0]) / 6, b[1] + (c[1] - a[1]) / 6])} ` +
           `${P([c[0] - (e[0] - b[0]) / 6, c[1] - (e[1] - b[1]) / 6])} ${P(c)}`;
    }
    return d;
  };
  return `M${P(A[0])}${path(A)}L${P(B[n])}${path([...B].reverse())}Z`;
}

// Right wing: four feathers fanning from one foot point beside the gem. Fused
// for the lower half, splitting into staggered tips at the rim, outermost
// tallest. Each blade bows along the sweep so the fan curls outward.
const FOOT = [592, 720];
const TIPS = [
  [620, 455],
  [700, 372],
  [782, 302],
  [858, 240],
];
const SPINES = TIPS.map((tip) => {
  const d = [tip[0] - FOOT[0], tip[1] - FOOT[1]];
  const L = Math.hypot(d[0], d[1]);
  const n = [-d[1] / L, d[0] / L];
  return {
    c: [FOOT[0] + d[0] * 0.5 + n[0] * L * 0.16, FOOT[1] + d[1] * 0.5 + n[1] * L * 0.16],
    w: L * 0.17,
  };
});
const FEATHERS = SPINES.map(({ c, w }, k) => `<path d="${blade(FOOT, c, TIPS[k], w, 0.72, 0.72)}"/>`);

const quad = (c, tip, t) => {
  const u = 1 - t;
  return [u * u * FOOT[0] + 2 * u * t * c[0] + t * t * tip[0],
          u * u * FOOT[1] + 2 * u * t * c[1] + t * t * tip[1]];
};
const gap = (k, t) => {
  const a = quad(SPINES[k].c, TIPS[k], t);
  const b = quad(SPINES[k + 1].c, TIPS[k + 1], t);
  return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
};
const FISSURES = SPINES.slice(0, -1).map((_, k) =>
  `<path d="${blade(gap(k, 0.44), gap(k, 0.72), gap(k, 1.0), 15)}" fill="#000"/>`).join('');

const STAVE =
  'M500 118' +
  'C507 158 514 206 517 262' +
  'C518 330 517 420 515 520' +
  'L515 660' +
  'C515 682 511 710 506 730' +
  'L500 692' +
  'L494 730' +
  'C489 710 485 682 485 660' +
  'L485 520' +
  'C483 420 482 330 483 262' +
  'C486 206 493 158 500 118' +
  'Z';

const NODE =
  'M500 404' +
  'C512 428 526 444 526 462' +
  'C526 482 512 494 500 516' +
  'C488 494 474 482 474 462' +
  'C474 444 488 428 500 404' +
  'Z';

const GEM = 'M500 734 L544 792 L500 850 L456 792 Z';

const ARM_BASE = [504, 358];

function body(ink) {
  const arm = `<path d="${blade(ARM_BASE, [550, 320], [610, 272], 24, 0.55, 0.9)}"/>`;
  const mask =
    `<defs><mask id="wing" maskUnits="userSpaceOnUse" x="0" y="0" width="1000" height="1000">` +
    `<rect width="1000" height="1000" fill="#fff"/>${FISSURES}</mask></defs>`;
  const wing = `<g mask="url(#wing)">${FEATHERS.join('')}</g>`;

  return (
    mask +
    `<g fill="${ink}">` +
    wing +
    `<g transform="translate(1000 0) scale(-1 1)">${wing}</g>` +
    `<path d="${STAVE}"/>` +
    `<path d="${NODE}"/>` +
    arm +
    `<g transform="translate(1000 0) scale(-1 1)">${arm}</g>` +
    `</g>` +
    `<path d="${GEM}" fill="${EMBER}"/>`
  );
}

function svg(inner) {
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1000" viewBox="0 0 1000 1000">` +
    inner +
    `</svg>`
  );
}

writeFileSync(resolve(dir, 'crest.svg'), svg(body(INK)));
writeFileSync(resolve(dir, 'crest-dark.svg'), svg(body(BONE)));

console.log('wrote crest.svg / crest-dark.svg');