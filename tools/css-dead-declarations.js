#!/usr/bin/env node
'use strict';

// Dead CSS declarations: a declaration can never apply when a later rule with the same
// selector, in the same or an unconditional condition context, sets the same property (or a
// shorthand that resets it) with equal or higher importance. Equal specificity plus later
// source order always wins, so the earlier declaration is dead in every state — hover,
// focus and theme included. Override layers leave these behind; this keeps them out. A later
// value an older browser may reject (see RECENT_VALUE) does not count: the earlier
// declaration is that browser's fallback.
//
// Usage:
//   node tools/css-dead-declarations.js <file.css>          report; exit 1 if any are dead
//   node tools/css-dead-declarations.js <file.css> --fix    remove them, fold same-selector
//                                                           rules where provably safe, tidy
//   node tools/css-dead-declarations.js --all               check every stylesheet under
//                                                           wwwroot/css against the baseline
//   node tools/css-dead-declarations.js --all --write       lower the baseline after a cleanup
//                                                           (--allow-increase to raise it)
//
// The baseline (tools/css-dead-declarations-baseline.json) lists each stylesheet's dead
// declarations; a file not listed must have none. `npm test` fails when a file goes above its
// number or cannot be read, and --write never raises a number without --allow-increase.
//
// --fix only deletes dead declarations (and rules or @media blocks left empty), and merges
// a later rule into the first rule with the same selector list and media context when no
// rule in between declares a related property — so the cascade result cannot change.
// Supports plain rules and the conditions @media, @supports and @container (nested
// conditions combine), and skips @keyframes, @page and @font-face, which hold no style rules
// for elements. Other at-rules (@layer, @scope, @import, nesting) and stray braces throw, so a
// file the tool cannot reason about is never "clean".

const fs = require('node:fs');
const path = require('node:path');

// SECTION: Parsing (keeps source offsets so edits preserve formatting)
function parse(src) {
  let i = 0;
  const n = src.length;
  const skipComment = () => { const end = src.indexOf('*/', i + 2); i = end < 0 ? n : end + 2; };
  function readUntil(stopChars) {
    let depth = 0;
    while (i < n) {
      const c = src[i];
      if (c === '/' && src[i + 1] === '*') { skipComment(); continue; }
      if (c === '"' || c === "'") { i++; while (i < n && src[i] !== c) { if (src[i] === '\\') i++; i++; } i++; continue; }
      if (c === '(' || c === '[') depth++;
      else if (c === ')' || c === ']') depth--;
      else if (depth === 0 && stopChars.includes(c)) return i;
      i++;
    }
    return n;
  }
  function parseDeclarations() {
    const decls = [];
    while (i < n) {
      while (i < n && /\s/.test(src[i])) i++;
      if (src[i] === '/' && src[i + 1] === '*') { skipComment(); continue; }
      if (src[i] === '}') { i++; break; }
      const start = i;
      const stop = readUntil(';}');
      const text = src.slice(start, stop);
      const colon = text.indexOf(':');
      i = src[stop] === ';' ? stop + 1 : stop;
      if (colon < 0) { if (text.trim()) throw new Error(`Unparsable declaration: ${text.trim()}`); continue; }
      const value = text.slice(colon + 1).trim();
      decls.push({ prop: text.slice(0, colon).trim().toLowerCase(), important: /!\s*important\s*$/i.test(value), recent: RECENT_VALUE.test(value), start, end: i });
    }
    return decls;
  }
  // Steps past a block body whose contents are not style rules (the opening brace is consumed).
  function skipBlock() {
    let depth = 1;
    while (i < n && depth > 0) {
      const stop = readUntil('{}');
      if (stop >= n) { i = n; return; }
      depth += src[stop] === '{' ? 1 : -1;
      i = stop + 1;
    }
  }
  function parseBlock(endChar, ctx) {
    const items = [];
    while (i < n) {
      while (i < n && /\s/.test(src[i])) i++;
      if (i >= n) break;
      if (src[i] === '/' && src[i + 1] === '*') { skipComment(); continue; }
      if (src[i] === endChar) { i++; return items; }
      const start = i;
      const stop = readUntil('{;}');
      if (src[stop] !== '{') throw new Error(`Unexpected CSS near offset ${start}: ${src.slice(start, stop + 1).trim()}`);
      const prelude = src.slice(start, stop).trim();
      i = stop + 1;
      if (/^@(media|supports|container)\b/i.test(prelude)) {
        // A rule here applies only under this condition: it interacts with rules under the same
        // condition, and an unconditional later rule overrides it (see findDead). For
        // @container the same element always queries the same container, so the same
        // condition text gives the same result for it.
        const condition = normaliseCondition(prelude);
        items.push({ type: 'media', start, children: parseBlock('}', ctx ? `${ctx} ${condition}` : condition), end: i });
      } else if (/^@((-[a-z]+-)?keyframes|page|font-face)\b/i.test(prelude)) {
        // Keyframe steps, page boxes (and their margin boxes) and font faces are not style
        // rules for elements: nothing in them overrides, or is overridden by, a style rule.
        skipBlock();
        items.push({ type: 'opaque', start, end: i });
      } else if (prelude.startsWith('@')) {
        throw new Error(`Unsupported at-rule: ${prelude}`);
      } else {
        const bodyStart = i;
        const decls = parseDeclarations();
        items.push({ type: 'rule', start, end: i, bodyStart, bodyEnd: i - 1, selectors: splitSelectors(prelude), ctx, decls });
      }
    }
    return items;
  }
  return parseBlock(null, '');
}

// A condition's key: its text exactly as written, apart from the at-keyword's case. Any
// normalisation risks equating two conditions with different results (case inside quoted
// values, spacing inside selector() or after a CSS escape), so conditions written differently
// stay apart. That can only miss a dead declaration, never remove a live one.
function normaliseCondition(prelude) {
  return prelude.trim().replace(/^@[-a-z]+/i, keyword => keyword.toLowerCase());
}

function splitSelectors(text) {
  const out = [];
  let depth = 0;
  let current = '';
  for (const c of text) {
    if (c === '(' || c === '[') depth++;
    if (c === ')' || c === ']') depth--;
    if (c === ',' && depth === 0) { out.push(current); current = ''; continue; }
    current += c;
  }
  out.push(current);
  return out
    .map((s) => s.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\s+/g, ' ').replace(/\s*([>+~])\s*(?![^[]*\])/g, ' $1 ').trim())
    .filter(Boolean);
}

// Not a style rule: kept as written by fix, and keeps its enclosing condition group.
const holdsOpaque = (items) => items.some((item) => item.type === 'opaque' || (item.type === 'media' && holdsOpaque(item.children)));

const flatten = (items, out = []) => {
  for (const item of items) {
    if (item.type === 'media') flatten(item.children, out);
    else if (item.type === 'rule') out.push(item);
  }
  return out;
};

// Values an older browser may reject: it then drops that declaration and keeps the earlier
// one, so a declaration using one of these does not make an earlier one dead (the earlier one
// is that browser's fallback, e.g. `height: 100vh` before `height: 100dvh`). PRISM runs on an
// offline LAN, where browsers can be several versions old. Listed: the dynamic, small and
// large viewport units and container units (2022-23), color-mix(), oklch()/oklab()/lch()/lab()
// (2023), light-dark() (2024) and the round()/mod()/rem() stepped functions (2024).
const RECENT_VALUE = /\d(?:[dsl]v(?:h|w|i|b|min|max)|cq(?:w|h|i|b|min|max))\b|\b(?:color-mix|oklch|oklab|lch|lab|light-dark|round|mod|rem)\(/i;

// SECTION: Property relationships
// Each shorthand's actual longhands (the properties it resets). Listed explicitly: prefixes
// are wrong in both directions (flex does not reset flex-direction; border-top does not
// reset border-top-left-radius; outline does not reset outline-offset).
const SIDES = ['top', 'right', 'bottom', 'left'];
const BORDER_SIDE = (side) => [`border-${side}-width`, `border-${side}-style`, `border-${side}-color`];
const LONGHANDS = {
  margin: SIDES.map((s) => `margin-${s}`),
  padding: SIDES.map((s) => `padding-${s}`),
  inset: SIDES,
  gap: ['row-gap', 'column-gap'],
  flex: ['flex-grow', 'flex-shrink', 'flex-basis'],
  'flex-flow': ['flex-direction', 'flex-wrap'],
  background: ['background-color', 'background-image', 'background-position', 'background-position-x', 'background-position-y', 'background-size', 'background-repeat', 'background-attachment', 'background-origin', 'background-clip'],
  font: ['font-style', 'font-variant', 'font-weight', 'font-stretch', 'font-size', 'line-height', 'font-family'],
  outline: ['outline-color', 'outline-style', 'outline-width'],
  transition: ['transition-property', 'transition-duration', 'transition-timing-function', 'transition-delay', 'transition-behavior'],
  animation: ['animation-name', 'animation-duration', 'animation-timing-function', 'animation-delay', 'animation-iteration-count', 'animation-direction', 'animation-fill-mode', 'animation-play-state'],
  'list-style': ['list-style-type', 'list-style-position', 'list-style-image'],
  overflow: ['overflow-x', 'overflow-y'],
  'grid-template': ['grid-template-rows', 'grid-template-columns', 'grid-template-areas'],
  columns: ['column-width', 'column-count'],
  'text-decoration': ['text-decoration-line', 'text-decoration-style', 'text-decoration-color', 'text-decoration-thickness'],
  'border-radius': ['border-top-left-radius', 'border-top-right-radius', 'border-bottom-right-radius', 'border-bottom-left-radius'],
  'border-width': SIDES.map((s) => `border-${s}-width`),
  'border-style': SIDES.map((s) => `border-${s}-style`),
  'border-color': SIDES.map((s) => `border-${s}-color`),
  'border-image': ['border-image-source', 'border-image-slice', 'border-image-width', 'border-image-outset', 'border-image-repeat'],
  'place-items': ['align-items', 'justify-items'],
  'place-content': ['align-content', 'justify-content'],
  'place-self': ['align-self', 'justify-self'],
  ...Object.fromEntries(SIDES.map((s) => [`border-${s}`, BORDER_SIDE(s)]))
};
LONGHANDS.border = [...SIDES.flatMap(BORDER_SIDE), 'border-width', 'border-style', 'border-color', ...SIDES.map((s) => `border-${s}`), 'border-image', ...LONGHANDS['border-image']];
// A shorthand also resets other shorthands made only of its longhands (border -> border-top).
for (const [short, longs] of Object.entries(LONGHANDS)) {
  for (const [other, otherLongs] of Object.entries(LONGHANDS)) {
    if (other !== short && otherLongs.every((l) => longs.includes(l)) && !longs.includes(other)) longs.push(other);
  }
}

// Does setting `short` reset `long`? (`all` resets every property except custom ones,
// direction and unicode-bidi.)
function covers(short, long) {
  if (short === long) return true;
  if (short.startsWith('--') || long.startsWith('--')) return false;
  if (short === 'all') return !['direction', 'unicode-bidi'].includes(long);
  return Boolean(LONGHANDS[short]?.includes(long));
}

// Conservative "can these two declarations interact?" used before moving anything: the same
// family, or one resets the other (e.g. columns / column-width, all / anything).
function family(prop) {
  if (prop.startsWith('--')) return prop;
  const bare = prop.replace(/^-(webkit|moz|ms|o)-/, '');
  if (['top', 'right', 'bottom', 'left', 'inset'].includes(bare)) return 'inset';
  if (['gap', 'row-gap', 'column-gap'].includes(bare)) return 'gap';
  if (bare === 'line-height') return 'font';
  if (/^(place|align|justify)-/.test(bare)) return 'alignment';
  return bare.split('-')[0];
}
const related = (a, b) => {
  const bareA = a.replace(/^-(webkit|moz|ms|o)-/, '');
  const bareB = b.replace(/^-(webkit|moz|ms|o)-/, '');
  return family(a) === family(b) || covers(bareA, bareB) || covers(bareB, bareA);
};

// SECTION: Analysis
function findDead(src) {
  const tree = parse(src);
  const rules = flatten(tree);
  const later = new Map(); // `${ctx}\0${selector}` -> declarations from later rules
  const key = (ctx, selector) => `${ctx}\u0000${selector}`;
  const overriddenBy = (list, decl) => (list || []).some((d) => !d.recent && covers(d.prop, decl.prop) && (d.important || !decl.important));
  const dead = [];
  for (let r = rules.length - 1; r >= 0; r--) {
    const rule = rules[r];
    for (const decl of rule.decls) {
      const killed = rule.selectors.every((selector) =>
        overriddenBy(later.get(key(rule.ctx, selector)), decl) || (rule.ctx && overriddenBy(later.get(key('', selector)), decl)));
      if (killed) dead.push({ rule, decl });
    }
    // Registered after the whole rule: duplicates inside one rule are deliberate fallbacks.
    for (const selector of rule.selectors) {
      const k = key(rule.ctx, selector);
      if (!later.has(k)) later.set(k, []);
      later.get(k).push(...rule.decls);
    }
  }
  return { tree, rules, dead };
}

// SECTION: Editing
function wholeLines(src, start, end) {
  let lineStart = start; while (lineStart > 0 && src[lineStart - 1] !== '\n') lineStart--;
  let lineEnd = end; while (lineEnd < src.length && src[lineEnd] !== '\n') lineEnd++;
  if (!src.slice(lineStart, start).trim() && !src.slice(end, lineEnd).trim()) return [lineStart, Math.min(src.length, lineEnd + 1)];
  let e = end; while (e < src.length && src[e] === ' ') e++;
  return [start, e];
}

function applyEdits(src, edits) {
  let out = src;
  let floor = Infinity;
  for (const { start, end, text = '' } of [...edits].sort((a, b) => b.start - a.start || b.end - a.end)) {
    if (end > floor) continue;
    out = out.slice(0, start) + text + out.slice(end);
    floor = start;
  }
  return out;
}

function removeDead(src) {
  const { tree, dead } = findDead(src);
  const deadSet = new Set(dead.map((d) => d.decl));
  const edits = [];
  (function walk(items) {
    for (const item of items) {
      if (item.type === 'opaque') continue;
      if (item.type === 'media') {
        // A group goes only when every rule in it is dead and it holds nothing opaque
        // (@keyframes have no style rules, so they would otherwise look "all dead").
        if (!holdsOpaque(item.children) && flatten(item.children).every((r) => r.decls.every((d) => deadSet.has(d)))) {
          const [start, end] = wholeLines(src, item.start, item.end);
          edits.push({ start, end });
        } else walk(item.children);
        continue;
      }
      const deadDecls = item.decls.filter((d) => deadSet.has(d));
      if (deadDecls.length && deadDecls.length === item.decls.length) {
        const [start, end] = wholeLines(src, item.start, item.end);
        edits.push({ start, end });
      } else {
        deadDecls.forEach((d) => { const [start, end] = wholeLines(src, d.start, d.end); edits.push({ start, end }); });
      }
    }
  })(tree);
  return { out: applyEdits(src, edits), removed: dead.length };
}

function foldRules(src) {
  const rules = flatten(parse(src));
  const groups = new Map();
  rules.forEach((rule, index) => {
    const k = `${rule.ctx}\u0000${rule.selectors.join(', ')}`;
    if (!groups.has(k)) groups.set(k, []);
    groups.get(k).push(index);
  });
  const inserts = new Map();
  const edits = [];
  let folded = 0;
  for (const indexes of groups.values()) {
    const [first, ...rest] = indexes;
    for (const index of rest) {
      const rule = rules[index];
      const between = rules.slice(first + 1, index);
      // Whole rules only, and only when nothing in between competes for any of its properties.
      const blocked = rule.decls.some((decl) => between.some((other) => other.decls.some((d) => related(d.prop, decl.prop))));
      if (blocked || !rule.decls.length) continue;
      if (!inserts.has(first)) inserts.set(first, []);
      rule.decls.forEach((decl) => {
        const text = src.slice(decl.start, decl.end).trim();
        inserts.get(first).push(text.endsWith(';') ? text : `${text};`);
      });
      const [start, end] = wholeLines(src, rule.start, rule.end);
      edits.push({ start, end });
      folded++;
    }
  }
  for (const [index, texts] of inserts) {
    const rule = rules[index];
    const last = rule.decls[rule.decls.length - 1];
    if (src.slice(rule.bodyStart, rule.bodyEnd).includes('\n') && last) {
      let lineStart = last.start; while (lineStart > 0 && src[lineStart - 1] !== '\n') lineStart--;
      const indent = src.slice(lineStart, last.start).trim() ? '    ' : src.slice(lineStart, last.start);
      edits.push({ start: last.end, end: last.end, text: texts.map((t) => `\n${indent}${t}`).join('') });
    } else {
      edits.push({ start: rule.bodyEnd, end: rule.bodyEnd, text: `${src[rule.bodyEnd - 1] === ' ' ? '' : ' '}${texts.join(' ')} ` });
    }
  }
  return { out: applyEdits(src, edits), folded };
}

// Blank lines inside rule bodies and before a closing brace go; runs of blank lines between
// rules collapse to one.
function tidy(src) {
  const bodies = flatten(parse(src)).map((r) => ({ start: r.bodyStart, end: r.bodyEnd }));
  const edits = bodies.map(({ start, end }) => ({ start, end, text: src.slice(start, end).replace(/\n[ \t]*(?=\n)/g, '') }));
  return applyEdits(src, edits)
    .replace(/\n[ \t]*\n([ \t]*\n)+/g, '\n\n')
    // No blank line just before a closing brace (left when the last rule of a group goes).
    .replace(/\n[ \t]*\n([ \t]*\})/g, '\n$1')
    .replace(/^\s*\n/, '');
}

function fix(src) {
  let out = src;
  let removed = 0;
  let folded = 0;
  for (let pass = 0; pass < 5; pass++) {
    let step;
    do { step = removeDead(out); out = step.out; removed += step.removed; } while (step.removed);
    const folding = foldRules(out);
    out = folding.out;
    folded += folding.folded;
    if (!folding.folded) break;
  }
  return { out: tidy(out), removed, folded };
}

// SECTION: Baseline ratchet over every stylesheet
const ROOT = path.join(__dirname, '..');
const CSS_ROOT = path.join(ROOT, 'wwwroot', 'css');
const BASELINE_PATH = path.join(__dirname, 'css-dead-declarations-baseline.json');

function stylesheets(dir = CSS_ROOT) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...stylesheets(full));
    else if (entry.name.endsWith('.css')) out.push(full);
  }
  return out.sort();
}

const relative = (file) => path.relative(ROOT, file).split(path.sep).join('/');

// Dead declarations per stylesheet; a file the tool cannot read is reported under `errors`.
function measureAll() {
  const counts = {};
  const errors = {};
  for (const file of stylesheets()) {
    try {
      counts[relative(file)] = findDead(fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n')).dead.length;
    } catch (error) {
      errors[relative(file)] = error.message.split('\n')[0];
    }
  }
  return { counts, errors };
}

function readBaseline() {
  return fs.existsSync(BASELINE_PATH) ? JSON.parse(fs.readFileSync(BASELINE_PATH, 'utf8')) : {};
}

// Files above their baseline (a file not listed is allowed none), and files below it.
function compareToBaseline(counts, baseline) {
  const grown = [];
  const lowered = [];
  for (const [file, now] of Object.entries(counts)) {
    const allowed = baseline[file] || 0;
    if (now > allowed) grown.push({ file, allowed, now });
    else if (now < allowed) lowered.push({ file, allowed, now });
  }
  return { grown, lowered };
}

function writeBaseline(counts) {
  const kept = Object.fromEntries(Object.entries(counts).filter(([, n]) => n > 0).sort(([a], [b]) => a.localeCompare(b)));
  fs.writeFileSync(BASELINE_PATH, `${JSON.stringify(kept, null, 2)}\n`);
}

function runAll(argv) {
  const write = argv.includes('--write');
  const allowIncrease = argv.includes('--allow-increase');
  const { counts, errors } = measureAll();
  for (const [file, message] of Object.entries(errors)) console.error(`${file}: cannot be checked: ${message}`);
  if (Object.keys(errors).length) return 1;
  const { grown, lowered } = compareToBaseline(counts, readBaseline());
  if (write) {
    if (grown.length && !allowIncrease) {
      for (const g of grown) console.error(`Not raising ${g.file}: ${g.allowed} -> ${g.now}. Edit the earlier rule instead of overriding it, or pass --allow-increase.`);
      return 1;
    }
    writeBaseline(counts);
    console.log(`Baseline written: ${Object.values(counts).reduce((a, b) => a + b, 0)} dead declaration(s) in ${Object.values(counts).filter(Boolean).length} file(s).`);
    return 0;
  }
  for (const g of grown) console.error(`${g.file}: ${g.now} dead declaration(s), baseline ${g.allowed}. Run: node tools/css-dead-declarations.js ${g.file}`);
  if (lowered.length) console.log(`${lowered.length} file(s) are below the baseline; run "node tools/css-dead-declarations.js --all --write" to lock that in.`);
  return grown.length ? 1 : 0;
}

module.exports = { parse, findDead, removeDead, foldRules, tidy, fix, covers, measureAll, readBaseline, compareToBaseline };

if (require.main === module) {
  const args = process.argv.slice(2);
  if (args[0] === '--all') {
    process.exitCode = runAll(args);
  } else {
    const [file, flag] = args;
    if (!file) { console.error('Usage: node tools/css-dead-declarations.js <file.css> [--fix] | --all [--write [--allow-increase]]'); process.exit(2); }
    const raw = fs.readFileSync(file, 'utf8');
    const src = raw.replace(/\r\n/g, '\n');
    if (flag === '--fix') {
      const { out, removed, folded } = fix(src);
      fs.writeFileSync(file, raw.includes('\r\n') ? out.replace(/\n/g, '\r\n') : out);
      console.log(`${file}: removed ${removed} dead declaration(s), folded ${folded} rule(s).`);
    } else {
      const { dead } = findDead(src);
      dead.forEach(({ rule, decl }) => console.log(`${rule.ctx || '(all)'} | ${rule.selectors.join(', ')} | ${decl.prop}`));
      console.log(`${file}: ${dead.length} dead declaration(s).`);
      process.exitCode = dead.length ? 1 : 0;
    }
  }
}
