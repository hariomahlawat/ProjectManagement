#!/usr/bin/env node
'use strict';
// color-mix() fallbacks for site.css, for browsers older than Chromium/Edge 111 (still found on
// offline LAN machines that are not updated).
//
// In such a browser a declaration that uses color-mix() and var() is not dropped when the
// stylesheet is read: it turns invalid when the page is drawn and the property resets (a
// background turns transparent, a border disappears, a dark-theme surface token goes blank).
// This tool works out, for Light and Dark, the colour each color-mix() in site.css produces,
// and writes a generated block at the end of site.css that applies only where color-mix() is
// unsupported:
//   - :root tokens built with color-mix() get the computed colour directly;
//   - every other rule gets the same value with each color-mix(...) replaced by a generated
//     token (--pm-cm-N), defined in :root (Light) and :root[data-bs-theme="dark"] (Dark).
// Browsers with color-mix() never read the block, so they render exactly as before.
//
// Usage:
//   node tools/color-mix-fallbacks.js           check that the block is up to date (exit 1 if not)
//   node tools/color-mix-fallbacks.js --write   regenerate the block
//
// Run --write after changing a color-mix() or a token it uses in site.css; npm test checks it.
const fs = require('node:fs');
const path = require('node:path');
const { readRules } = require('./lib/css-rules');

const ROOT = path.resolve(__dirname, '..');
const SITE_CSS = path.join(ROOT, 'wwwroot', 'css', 'site.css');
const BOOTSTRAP_CSS = path.join(ROOT, 'wwwroot', 'lib', 'bootstrap', 'dist', 'css', 'bootstrap.min.css');
const BEGIN = '/* BEGIN GENERATED: color-mix() fallbacks for older browsers. Do not edit by hand; run node tools/color-mix-fallbacks.js --write */';
const END = '/* END GENERATED: color-mix() fallbacks */';
const SUPPORTS = '@supports not (color: color-mix(in srgb, currentColor 50%, transparent))';

const LIGHT_ROOTS = new Set([':root', ':root,[data-bs-theme=light]', ':root:not([data-bs-theme="dark"])']);
const DARK_ROOTS = new Set([':root[data-bs-theme="dark"]', '[data-bs-theme=dark]']);

// ---------- Colours ----------

const NAMED = { transparent: [0, 0, 0, 0], white: [255, 255, 255, 1], black: [0, 0, 0, 1] };

function parseColour(text) {
  const value = text.trim().toLowerCase();
  if (NAMED[value]) return NAMED[value].slice();
  let m = value.match(/^#([0-9a-f]{3,8})$/);
  if (m) {
    let hex = m[1];
    if (hex.length === 3 || hex.length === 4) hex = [...hex].map((c) => c + c).join('');
    if (hex.length !== 6 && hex.length !== 8) return null;
    const n = (i) => parseInt(hex.slice(i, i + 2), 16);
    return [n(0), n(2), n(4), hex.length === 8 ? n(6) / 255 : 1];
  }
  m = value.match(/^rgba?\(([^)]*)\)$/);
  if (m) {
    const parts = m[1].split(/[\s,/]+/).filter(Boolean);
    if (parts.length < 3) return null;
    const channel = (p) => (p.endsWith('%') ? (parseFloat(p) * 255) / 100 : parseFloat(p));
    const alpha = parts[3] === undefined ? 1 : (parts[3].endsWith('%') ? parseFloat(parts[3]) / 100 : parseFloat(parts[3]));
    const rgba = [channel(parts[0]), channel(parts[1]), channel(parts[2]), alpha];
    return rgba.every((x) => Number.isFinite(x)) ? rgba : null;
  }
  return null;
}

function formatColour([r, g, b, a]) {
  const c = (x) => Math.max(0, Math.min(255, Math.round(x)));
  const alpha = Math.round(a * 1000) / 1000;
  if (alpha >= 1) return `#${[r, g, b].map((x) => c(x).toString(16).padStart(2, '0')).join('')}`;
  return `rgba(${c(r)}, ${c(g)}, ${c(b)}, ${alpha})`;
}

// CSS Color 5 color-mix() in srgb: premultiplied-alpha interpolation, percentages normalised,
// and a total under 100% scales the alpha.
function mixSrgb(c1, p1, c2, p2) {
  if (p1 === undefined && p2 === undefined) { p1 = 50; p2 = 50; } else if (p1 === undefined) p1 = 100 - p2; else if (p2 === undefined) p2 = 100 - p1;
  const total = p1 + p2;
  if (total <= 0) return null;
  const w1 = p1 / total;
  const w2 = p2 / total;
  const alpha = c1[3] * w1 + c2[3] * w2;
  const channel = (i) => (alpha === 0 ? 0 : (c1[i] * c1[3] * w1 + c2[i] * c2[3] * w2) / alpha);
  return [channel(0), channel(1), channel(2), alpha * Math.min(1, total / 100)];
}

// ---------- Values ----------

// Finds each top-level color-mix( ... ) in a value: [{ start, end, text }].
function findMixes(value) {
  const found = [];
  const re = /color-mix\(/gi;
  let m;
  while ((m = re.exec(value))) {
    let depth = 0;
    let i = m.index + 'color-mix'.length;
    for (; i < value.length; i++) {
      if (value[i] === '(') depth++;
      else if (value[i] === ')' && --depth === 0) break;
    }
    found.push({ start: m.index, end: i + 1, text: value.slice(m.index, i + 1) });
    re.lastIndex = i + 1;
  }
  return found;
}

// Splits on commas that are not inside parentheses.
function splitTopLevel(text) {
  const parts = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < text.length; i++) {
    if (text[i] === '(') depth++;
    else if (text[i] === ')') depth--;
    else if (text[i] === ',' && depth === 0) { parts.push(text.slice(start, i)); start = i + 1; }
  }
  parts.push(text.slice(start));
  return parts.map((p) => p.trim());
}

// Resolves a colour expression (a colour, a var(), a nested color-mix()) against a token map.
function resolveColour(expr, tokens, seen = new Set()) {
  const text = expr.trim();
  const v = text.match(/^var\(\s*(--[\w-]+)\s*(?:,\s*([\s\S]+))?\)$/);
  if (v) {
    if (tokens.has(v[1]) && !seen.has(v[1])) return resolveColour(tokens.get(v[1]), tokens, new Set([...seen, v[1]]));
    return v[2] !== undefined ? resolveColour(v[2], tokens, seen) : null;
  }
  if (/^color-mix\(/i.test(text)) return evaluateMix(text, tokens, seen);
  return parseColour(text);
}

function evaluateMix(text, tokens, seen = new Set()) {
  const inner = text.slice(text.indexOf('(') + 1, -1);
  const [space, ...stops] = splitTopLevel(inner);
  if (!/^in\s+srgb$/i.test(space) || stops.length !== 2) return null;
  const parsed = stops.map((stop) => {
    const m = stop.match(/^([\s\S]*?)\s+(calc\([\s\S]*\)|[\d.]+%)$/);
    if (!m) return { colour: stop, percent: undefined };
    return { colour: m[1], percent: m[2] };
  });
  const percent = (p) => {
    if (p === undefined) return undefined;
    if (/^[\d.]+%$/.test(p)) return parseFloat(p);
    return NaN; // calc() of tokens is not resolved here
  };
  const c1 = resolveColour(parsed[0].colour, tokens, seen);
  const c2 = resolveColour(parsed[1].colour, tokens, seen);
  const p1 = percent(parsed[0].percent);
  const p2 = percent(parsed[1].percent);
  if (!c1 || !c2 || Number.isNaN(p1) || Number.isNaN(p2)) return null;
  return mixSrgb(c1, p1, c2, p2);
}

// Replaces every color-mix() in a value by the result of `replace(text)`, or returns null when
// one cannot be replaced.
function substituteMixes(value, replace) {
  let out = '';
  let last = 0;
  for (const mix of findMixes(value)) {
    const replacement = replace(mix.text);
    if (replacement === null) return null;
    out += value.slice(last, mix.start) + replacement;
    last = mix.end;
  }
  return out + value.slice(last);
}

// ---------- Tokens ----------

function tokenMaps(siteRules, bootstrapRules) {
  const light = new Map();
  const dark = new Map();
  const take = (rules, roots, map) => {
    for (const rule of rules) {
      if (rule.atRules.length || !roots.has(rule.selector)) continue;
      for (const d of rule.declarations) if (d.property.startsWith('--')) map.set(d.property, d.value);
    }
  };
  take(bootstrapRules, LIGHT_ROOTS, light);
  take(siteRules, LIGHT_ROOTS, light);
  for (const [k, v] of light) dark.set(k, v);
  take(bootstrapRules, DARK_ROOTS, dark);
  take(siteRules, DARK_ROOTS, dark);
  return { light, dark };
}

// ---------- Generation ----------

function stripGenerated(source) {
  const begin = source.indexOf(BEGIN);
  if (begin < 0) return source;
  const end = source.indexOf(END, begin);
  if (end < 0) throw new Error('site.css has the generated block start marker but no end marker.');
  return source.slice(0, begin).replace(/\s*$/, '\n') + source.slice(end + END.length).replace(/^\s*\n/, '');
}

function generate(siteSource, bootstrapSource) {
  const base = stripGenerated(siteSource);
  const siteRules = readRules(base);
  const { light, dark } = tokenMaps(siteRules, readRules(bootstrapSource));

  // The last declaration of each (context, selector, property) wins in the cascade; only that
  // one decides, so a colour mix that a later plain declaration overrides gets no fallback.
  const last = new Map();
  siteRules.forEach((rule, ruleIndex) => rule.declarations.forEach((d, i) => {
    last.set(`${rule.atRules.join('|')}§${rule.selector}§${d.property}`, `${ruleIndex}:${i}`);
  }));

  const rootLight = [];
  const rootDark = [];
  const mixTokens = new Map(); // color-mix text -> token name
  const contexts = new Map(); // at-rule prelude list -> [{ selector, lines }]
  const unresolved = [];

  // A rule that only applies in one theme needs only that theme's colour (Dark rules may mix
  // the --pm-dark-* tokens, which Light does not define); the other theme reuses it.
  const tokenFor = (text, only) => {
    const key = `${only || 'both'}§${text}`;
    if (!mixTokens.has(key)) {
      let l = only === 'dark' ? null : evaluateMix(text, light);
      let k = only === 'light' ? null : evaluateMix(text, dark);
      if (only === 'dark') l = k;
      if (only === 'light') k = l;
      if (!l || !k) return null;
      const existing = [...mixTokens.values()].find((t) => t.light === formatColour(l) && t.dark === formatColour(k));
      mixTokens.set(key, existing || { name: `--pm-cm-${new Set([...mixTokens.values()].map((t) => t.name)).size + 1}`, light: formatColour(l), dark: formatColour(k) });
    }
    return `var(${mixTokens.get(key).name})`;
  };
  const themeOf = (selector) => {
    const parts = splitTopLevel(selector);
    if (parts.every((p) => /data-bs-theme="?dark"?\]/.test(p) && !/:not\(\[data-bs-theme="?dark/.test(p))) return 'dark';
    if (parts.every((p) => /:not\(\[data-bs-theme="?dark"?\]\)/.test(p))) return 'light';
    return null;
  };

  siteRules.forEach((rule, ruleIndex) => {
    if (rule.atRules.some((at) => /^@(keyframes|-webkit-keyframes|font-face|page)\b|^@media\s+print\b/.test(at))) return;
    const isLightRoot = rule.atRules.length === 0 && LIGHT_ROOTS.has(rule.selector);
    const isDarkRoot = rule.atRules.length === 0 && DARK_ROOTS.has(rule.selector);
    const lines = [];
    rule.declarations.forEach((d, i) => {
      if (!/color-mix\(/i.test(d.value)) return;
      if (last.get(`${rule.atRules.join('|')}§${rule.selector}§${d.property}`) !== `${ruleIndex}:${i}`) return;
      const important = d.important ? ' !important' : '';
      if ((isLightRoot || isDarkRoot) && d.property.startsWith('--')) {
        const tokens = isLightRoot ? light : dark;
        const value = substituteMixes(d.value, (text) => { const c = evaluateMix(text, tokens); return c ? formatColour(c) : null; });
        if (value === null) unresolved.push(`${rule.selector} { ${d.property} }`);
        else (isLightRoot ? rootLight : rootDark).push(`        ${d.property}: ${value.replace(/\s+/g, ' ')}${important};`);
        return;
      }
      const only = themeOf(rule.selector);
      const value = substituteMixes(d.value, (text) => tokenFor(text, only));
      if (value === null) unresolved.push(`${rule.selector} { ${d.property} }`);
      else lines.push(`${d.property}: ${value.replace(/\s+/g, ' ')}${important};`);
    });
    if (!lines.length) return;
    const key = rule.atRules.join('\n');
    if (!contexts.has(key)) contexts.set(key, []);
    const list = contexts.get(key);
    const previous = list[list.length - 1];
    // Consecutive declarations of one rule stay together; a repeated selector gets its own rule.
    if (previous && previous.selector === rule.selector && previous.ruleIndex === ruleIndex) previous.lines.push(...lines);
    else list.push({ selector: rule.selector, ruleIndex, lines });
  });

  const out = [BEGIN, '/* Browsers without color-mix() (Chromium/Edge before 111) would draw these colours blank.', '   Each value here is what the color-mix() in the rule above it produces, worked out for Light', '   and Dark; browsers with color-mix() skip this block. */', `${SUPPORTS} {`];
  out.push('    :root {');
  out.push(...rootLight);
  const uniqueTokens = [...new Map([...mixTokens.values()].map((t) => [t.name, t])).values()];
  for (const t of uniqueTokens) out.push(`        ${t.name}: ${t.light};`);
  out.push('    }', '');
  out.push('    :root[data-bs-theme="dark"] {');
  out.push(...rootDark);
  for (const t of uniqueTokens) if (t.dark !== t.light) out.push(`        ${t.name}: ${t.dark};`);
  out.push('    }');
  for (const [key, rules] of contexts) {
    const ats = key ? key.split('\n') : [];
    const indent = '    '.repeat(1 + ats.length);
    out.push('');
    ats.forEach((at, depth) => out.push(`${'    '.repeat(1 + depth)}${at} {`));
    rules.forEach((rule, i) => {
      if (i) out.push('');
      out.push(`${indent}${splitTopLevel(rule.selector).join(`,\n${indent}`)} {`);
      for (const line of rule.lines) out.push(`${indent}    ${line}`);
      out.push(`${indent}}`);
    });
    for (let depth = ats.length - 1; depth >= 0; depth--) out.push(`${'    '.repeat(1 + depth)}}`);
  }
  out.push('}', END);
  const css = `${base.replace(/\s*$/, '')}\n\n${out.join('\n')}\n`;
  return { css, unresolved, tokens: uniqueTokens.length, mixes: [...mixTokens].map(([key, t]) => ({ key, ...t })), rootLight, rootDark };
}

function main() {
  const write = process.argv.includes('--write');
  const site = fs.readFileSync(SITE_CSS, 'utf8');
  const { css, unresolved, tokens } = generate(site, fs.readFileSync(BOOTSTRAP_CSS, 'utf8'));
  if (unresolved.length) {
    console.error(`color-mix fallbacks: these could not be worked out (a colour that is not a hex, rgb() or token, or a calc() percentage):\n  ${unresolved.join('\n  ')}`);
    process.exitCode = 1;
    return;
  }
  if (write) {
    fs.writeFileSync(SITE_CSS, css);
    console.log(`color-mix fallbacks written to site.css (${tokens} generated colours).`);
    return;
  }
  if (css !== site) {
    console.error('site.css color-mix() fallbacks are out of date: run node tools/color-mix-fallbacks.js --write');
    process.exitCode = 1;
    return;
  }
  console.log('site.css color-mix() fallbacks are up to date.');
}

module.exports = { parseColour, formatColour, mixSrgb, evaluateMix, generate, BEGIN, END };

if (require.main === module) main();
