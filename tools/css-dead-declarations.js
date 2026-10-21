#!/usr/bin/env node
'use strict';

// Dead CSS declarations: a declaration can never apply when a later rule with the same
// selector, in the same or an unconditional media context, sets the same property (or a
// shorthand that resets it) with equal or higher importance. Equal specificity plus later
// source order always wins, so the earlier declaration is dead in every state — hover,
// focus and theme included. Override layers leave these behind; this keeps them out.
//
// Usage:
//   node tools/css-dead-declarations.js <file.css>          report; exit 1 if any are dead
//   node tools/css-dead-declarations.js <file.css> --fix    remove them, fold same-selector
//                                                           rules where provably safe, tidy
//
// --fix only deletes dead declarations (and rules or @media blocks left empty), and merges
// a later rule into the first rule with the same selector list and media context when no
// rule in between declares a related property — so the cascade result cannot change.
// Supports plain rules and @media (no nesting, @layer or @supports); throws otherwise.

const fs = require('node:fs');

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
      decls.push({ prop: text.slice(0, colon).trim().toLowerCase(), important: /!\s*important\s*$/i.test(value), start, end: i });
    }
    return decls;
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
      if (prelude.startsWith('@media')) {
        const media = prelude.replace(/\s+/g, ' ').toLowerCase();
        items.push({ type: 'media', start, children: parseBlock('}', media), end: i });
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

const flatten = (items, out = []) => {
  for (const item of items) item.type === 'media' ? flatten(item.children, out) : out.push(item);
  return out;
};

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
  const overriddenBy = (list, decl) => (list || []).some((d) => covers(d.prop, decl.prop) && (d.important || !decl.important));
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
      if (item.type === 'media') {
        if (flatten(item.children).every((r) => r.decls.every((d) => deadSet.has(d)))) {
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

// Blank lines inside rule bodies go; runs of blank lines between rules collapse to one.
function tidy(src) {
  const bodies = flatten(parse(src)).map((r) => ({ start: r.bodyStart, end: r.bodyEnd }));
  const edits = bodies.map(({ start, end }) => ({ start, end, text: src.slice(start, end).replace(/\n[ \t]*(?=\n)/g, '') }));
  return applyEdits(src, edits).replace(/\n[ \t]*\n([ \t]*\n)+/g, '\n\n').replace(/^\s*\n/, '');
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

module.exports = { parse, findDead, removeDead, foldRules, tidy, fix, covers };

if (require.main === module) {
  const [file, flag] = process.argv.slice(2);
  if (!file) { console.error('Usage: node tools/css-dead-declarations.js <file.css> [--fix]'); process.exit(2); }
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
