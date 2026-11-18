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
const DARK_ROOTS = new Set([':root[data-bs-theme="dark"]', ':root[data-bs-theme=dark]', '[data-bs-theme=dark]', '[data-bs-theme="dark"]', 'html[data-bs-theme="dark"]']);

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

// The same in OKLab (CSS Color 4 matrices), for color-mix(in oklab, ...).
const toLinear = (c) => { const v = c / 255; return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
const fromLinear = (v) => 255 * (v <= 0.0031308 ? 12.92 * v : 1.055 * Math.max(0, v) ** (1 / 2.4) - 0.055);

function srgbToOklab([r, g, b]) {
  const [lr, lg, lb] = [r, g, b].map(toLinear);
  const l = Math.cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
  const m = Math.cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
  const s = Math.cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
  return [
    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
  ];
}

function oklabToSrgb([L, A, B]) {
  const l = (L + 0.3963377774 * A + 0.2158037573 * B) ** 3;
  const m = (L - 0.1055613458 * A - 0.0638541728 * B) ** 3;
  const s = (L - 0.0894841775 * A - 1.2914855480 * B) ** 3;
  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s,
  ].map(fromLinear);
}

function mixOklab(c1, p1, c2, p2) {
  if (p1 === undefined && p2 === undefined) { p1 = 50; p2 = 50; } else if (p1 === undefined) p1 = 100 - p2; else if (p2 === undefined) p2 = 100 - p1;
  const total = p1 + p2;
  if (total <= 0) return null;
  const w1 = p1 / total;
  const w2 = p2 / total;
  // A fully transparent colour has no hue to contribute (CSS Color 4: missing components take
  // the other colour's), so it only lowers the alpha.
  const lab1 = srgbToOklab(c1);
  const lab2 = srgbToOklab(c2);
  const a1 = c1[3];
  const a2 = c2[3];
  const from1 = a1 === 0 ? lab2 : lab1;
  const from2 = a2 === 0 ? lab1 : lab2;
  const alpha = a1 * w1 + a2 * w2;
  const lab = [0, 1, 2].map((i) => (alpha === 0 ? 0 : (from1[i] * a1 * w1 + from2[i] * a2 * w2) / alpha));
  return [...oklabToSrgb(lab), alpha * Math.min(1, total / 100)];
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

// A percentage: 40%, a token holding one (var(--x-mix)), or calc() of those with * and /.
function resolvePercent(text, tokens, depth = 0) {
  const t = text.trim();
  if (depth > 8) return NaN;
  if (/^[\d.]+%$/.test(t)) return parseFloat(t);
  const v = t.match(/^var\(\s*(--[\w-]+)\s*\)$/);
  if (v) return tokens.has(v[1]) ? resolvePercent(tokens.get(v[1]), tokens, depth + 1) : NaN;
  const c = t.match(/^calc\(([\s\S]*)\)$/);
  if (c) {
    const m = c[1].trim().match(/^(var\(--[\w-]+\)|[\d.]+%)\s*([*/])\s*([\d.]+)$/);
    if (!m) return NaN;
    const base = resolvePercent(m[1], tokens, depth + 1);
    return m[2] === '*' ? base * parseFloat(m[3]) : base / parseFloat(m[3]);
  }
  return NaN;
}

function evaluateMix(text, tokens, seen = new Set()) {
  const inner = text.slice(text.indexOf('(') + 1, -1);
  const [space, ...stops] = splitTopLevel(inner);
  const space2 = space.trim().toLowerCase().replace(/\s+/g, ' ');
  if ((space2 !== 'in srgb' && space2 !== 'in oklab') || stops.length !== 2) return null;
  const parsed = stops.map((stop) => {
    const m = stop.match(/^([\s\S]*?)\s+(calc\([\s\S]*\)|var\(--[\w-]+\)|[\d.]+%)$/);
    if (!m) return { colour: stop, percent: undefined };
    return { colour: m[1], percent: m[2] };
  });
  const percent = (p) => (p === undefined ? undefined : resolvePercent(p, tokens));
  const c1 = resolveColour(parsed[0].colour, tokens, seen);
  const c2 = resolveColour(parsed[1].colour, tokens, seen);
  const p1 = percent(parsed[0].percent);
  const p2 = percent(parsed[1].percent);
  if (!c1 || !c2 || Number.isNaN(p1) || Number.isNaN(p2)) return null;
  return space2 === 'in oklab' ? mixOklab(c1, p1, c2, p2) : mixSrgb(c1, p1, c2, p2);
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

const isDarkPart = (part) => /data-bs-theme="?dark"?\]/.test(part) && !/:not\(\[data-bs-theme="?dark/.test(part);

// The tokens a colour mix can rely on, for Light and Dark:
//   - :root tokens from Bootstrap, then site.css, then the stylesheet itself (later wins);
//   - tokens a component defines (.cw-local-rail { --cw-glass-hi: ... }) anywhere in
//     wwwroot/css, but only when every definition for that theme agrees. A token with several
//     values (a hue set per tile) depends on the element, so a mix using it is approximated.
function tokenMaps(rootSources, scopedRules) {
  const light = new Map();
  const dark = new Map();
  for (const rules of rootSources) {
    for (const rule of rules) {
      if (rule.atRules.length) continue;
      const map = LIGHT_ROOTS.has(rule.selector) ? light : DARK_ROOTS.has(rule.selector) ? dark : null;
      if (!map) continue;
      for (const d of rule.declarations) if (d.property.startsWith('--')) map.set(d.property, d.value);
    }
  }
  const darkOnly = new Map(dark);
  for (const [k, v] of light) if (!darkOnly.has(k)) dark.set(k, v);
  for (const [k, v] of darkOnly) dark.set(k, v);

  const scoped = { light: new Map(), dark: new Map() };
  // Other stylesheets' :root tokens count as component tokens: landing.css's spectrum is used by
  // the status and About pages, which load it.
  const ownRoots = new Set(rootSources.flat());
  for (const rule of scopedRules) {
    if (ownRoots.has(rule)) continue;
    if (rule.atRules.some((at) => /^@media\s+print\b/.test(at) || at.startsWith(SUPPORTS))) continue;
    const theme = DARK_ROOTS.has(rule.selector) || splitTopLevel(rule.selector).every(isDarkPart) ? 'dark' : 'light';
    for (const d of rule.declarations) {
      if (!d.property.startsWith('--')) continue;
      const map = scoped[theme];
      if (!map.has(d.property)) map.set(d.property, new Set());
      map.get(d.property).add(d.value.replace(/\s+/g, ' '));
    }
  }
  // Ambiguity is per theme: a Light preview inside the Dark theme makes the Dark value depend on
  // the element, but leaves the Light value certain.
  const unique = (map) => new Map([...map].filter(([, values]) => values.size === 1).map(([k, values]) => [k, [...values][0]]));
  const ambiguousIn = (map) => new Set([...map].filter(([, values]) => values.size > 1).map(([k]) => k));
  const withScoped = (base, own, ownAmbiguous, fallback) => {
    const map = new Map(base);
    for (const [k, v] of own) if (!map.has(k)) map.set(k, v);
    // In Dark, a component token falls back to its Light definition when Dark does not redefine it.
    for (const [k, v] of fallback) if (!map.has(k) && !ownAmbiguous.has(k)) map.set(k, v);
    return map;
  };
  const lightAmbiguous = ambiguousIn(scoped.light);
  const darkAmbiguous = new Set([...ambiguousIn(scoped.dark), ...[...lightAmbiguous].filter((k) => !scoped.dark.has(k))]);
  return {
    light: withScoped(light, unique(scoped.light), lightAmbiguous, new Map()),
    dark: withScoped(dark, unique(scoped.dark), darkAmbiguous, unique(scoped.light)),
  };
}

// ---------- Approximation ----------

// A mix that cannot be worked out exactly (a colour that depends on the element) falls back to
// its dominant colour, as written: 14% of a hue into transparent becomes transparent, 78% of the
// primary into the text colour becomes the primary. Older browsers understand var() itself.
function approximateMix(text, tokens) {
  const inner = text.slice(text.indexOf('(') + 1, -1);
  const [, ...stops] = splitTopLevel(inner);
  if (stops.length !== 2) return 'transparent';
  const parsed = stops.map((stop) => {
    const m = stop.match(/^([\s\S]*?)\s+(calc\([\s\S]*\)|var\(--[\w-]+\)|[\d.]+%)$/);
    return m ? { colour: m[1].trim(), percent: resolvePercent(m[2], tokens) } : { colour: stop.trim(), percent: undefined };
  });
  let [a, b] = parsed.map((stop) => stop.percent);
  if (a === undefined && b === undefined) { a = 50; b = 50; } else if (a === undefined) a = 100 - b; else if (b === undefined) b = 100 - a;
  if (Number.isNaN(a) || Number.isNaN(b)) { a = 50; b = 50; }
  const pick = a >= b ? parsed[0].colour : parsed[1].colour;
  return /color-mix\(/i.test(pick) ? substituteMixes(pick, (inner2) => approximateMix(inner2, tokens)) : pick;
}

// ---------- Generation ----------

function stripGenerated(source, file = 'the stylesheet') {
  const begin = source.indexOf(BEGIN);
  if (begin < 0) return source;
  const end = source.indexOf(END, begin);
  if (end < 0) throw new Error(`${file} has the generated block start marker but no end marker.`);
  return source.slice(0, begin).replace(/\s*$/, '\n') + source.slice(end + END.length).replace(/^\s*\n/, '');
}

// source: the stylesheet; options.rootSources: rule lists whose :root tokens apply (Bootstrap,
// site.css), before the stylesheet's own; options.scopedRules: every rule in wwwroot/css, for
// component tokens; options.prefix: the generated token prefix (--pm-cm- for site.css).
function generate(source, options = {}) {
  const base = stripGenerated(source, options.file);
  const rules = readRules(base);
  const prefix = options.prefix || '--pm-cm-';
  const { light, dark } = tokenMaps([...(options.rootSources || []), rules], options.scopedRules || rules);

  // The last declaration of each (context, selector, property) wins in the cascade; only that
  // one decides, so a colour mix that a later plain declaration overrides gets no fallback.
  const last = new Map();
  rules.forEach((rule, ruleIndex) => rule.declarations.forEach((d, i) => {
    last.set(`${rule.atRules.join('|')}§${rule.selector}§${d.property}`, `${ruleIndex}:${i}`);
  }));

  const rootLight = [];
  const rootDark = [];
  const mixTokens = new Map(); // theme§color-mix text -> { name, light, dark }
  const names = new Map(); // light§dark -> name, so equal colours share a token
  const contexts = new Map(); // at-rule prelude list -> [{ selector, lines }]
  const approximated = [];
  let exact = 0;

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
      const pair = `${formatColour(l)}§${formatColour(k)}`;
      if (!names.has(pair)) names.set(pair, `${prefix}${names.size + 1}`);
      mixTokens.set(key, { name: names.get(pair), light: formatColour(l), dark: formatColour(k) });
    }
    return `var(${mixTokens.get(key).name})`;
  };
  const themeOf = (selector) => {
    const parts = splitTopLevel(selector);
    if (parts.every(isDarkPart)) return 'dark';
    if (parts.every((p) => /:not\(\[data-bs-theme="?dark"?\]\)/.test(p))) return 'light';
    return null;
  };
  const replaceMix = (text, resolve, tokensForApprox, where) => {
    const exactValue = resolve(text);
    if (exactValue !== null) { exact++; return exactValue; }
    approximated.push(where);
    return approximateMix(text, tokensForApprox);
  };

  rules.forEach((rule, ruleIndex) => {
    if (rule.atRules.some((at) => /^@(keyframes|-webkit-keyframes|font-face|page)\b|^@media\s+print\b/.test(at))) return;
    const isLightRoot = rule.atRules.length === 0 && LIGHT_ROOTS.has(rule.selector);
    const isDarkRoot = rule.atRules.length === 0 && DARK_ROOTS.has(rule.selector);
    const lines = [];
    rule.declarations.forEach((d, i) => {
      if (!/color-mix\(/i.test(d.value)) return;
      if (last.get(`${rule.atRules.join('|')}§${rule.selector}§${d.property}`) !== `${ruleIndex}:${i}`) return;
      const important = d.important ? ' !important' : '';
      const where = `${rule.selector} { ${d.property} }`;
      if ((isLightRoot || isDarkRoot) && d.property.startsWith('--')) {
        const tokens = isLightRoot ? light : dark;
        const value = substituteMixes(d.value, (text) => replaceMix(text, (t) => { const c = evaluateMix(t, tokens); return c ? formatColour(c) : null; }, tokens, where));
        (isLightRoot ? rootLight : rootDark).push(`        ${d.property}: ${value.replace(/\s+/g, ' ')}${important};`);
        return;
      }
      const only = themeOf(rule.selector);
      const value = substituteMixes(d.value, (text) => replaceMix(text, (t) => tokenFor(t, only), only === 'dark' ? dark : light, where));
      lines.push(`${d.property}: ${value.replace(/\s+/g, ' ')}${important};`);
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

  const uniqueTokens = [...new Map([...mixTokens.values()].map((t) => [t.name, t])).values()];
  if (!rootLight.length && !rootDark.length && !contexts.size) {
    return { css: base, approximated, exact, tokens: 0, mixes: [], rootLight, rootDark };
  }
  const out = [BEGIN, '/* Browsers without color-mix() (Chromium/Edge before 111) would draw these colours blank.', '   Each value here is what the color-mix() in the rule above it produces, worked out for Light', '   and Dark; browsers with color-mix() skip this block. */', `${SUPPORTS} {`];
  if (rootLight.length || uniqueTokens.length) {
    out.push('    :root {');
    out.push(...rootLight);
    for (const t of uniqueTokens) out.push(`        ${t.name}: ${t.light};`);
    out.push('    }');
  }
  const darkTokens = uniqueTokens.filter((t) => t.dark !== t.light);
  if (rootDark.length || darkTokens.length) {
    out.push('', '    :root[data-bs-theme="dark"] {');
    out.push(...rootDark);
    for (const t of darkTokens) out.push(`        ${t.name}: ${t.dark};`);
    out.push('    }');
  }
  for (const [key, list] of contexts) {
    const ats = key ? key.split('\n') : [];
    const indent = '    '.repeat(1 + ats.length);
    out.push('');
    ats.forEach((at, depth) => out.push(`${'    '.repeat(1 + depth)}${at} {`));
    list.forEach((rule, i) => {
      if (i) out.push('');
      out.push(`${indent}${splitTopLevel(rule.selector).join(`,\n${indent}`)} {`);
      for (const line of rule.lines) out.push(`${indent}    ${line}`);
      out.push(`${indent}}`);
    });
    for (let depth = ats.length - 1; depth >= 0; depth--) out.push(`${'    '.repeat(1 + depth)}}`);
  }
  out.push('}', END);
  const css = `${base.replace(/\s*$/, '')}\n\n${out.join('\n')}\n`;
  return { css, approximated, exact, tokens: uniqueTokens.length, mixes: [...mixTokens].map(([key, t]) => ({ key, ...t })), rootLight, rootDark };
}

// ---------- Files ----------

const CSS_ROOT = path.join(ROOT, 'wwwroot', 'css');
// Stylesheets with hand-written fallbacks (the sign-in and landing pages, see
// tools/public-pages-old-browsers.test.js), so the generator leaves them alone.
const HAND_WRITTEN = new Set(['landing.css', 'auth-login.css', 'public-shell.css']);

function cssFiles() {
  const out = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.css') && !entry.name.endsWith('.min.css')) out.push(full);
    }
  };
  walk(CSS_ROOT);
  return out.sort();
}

const relative = (file) => path.relative(CSS_ROOT, file).split(path.sep).join('/');
const prefixFor = (file) => (relative(file) === 'site.css' ? '--pm-cm-' : `--cm-${relative(file).replace(/\.css$/, '').replace(/[^a-z0-9]+/gi, '-').toLowerCase()}-`);

// Generates every stylesheet's block: { file -> { css, approximated, exact, tokens } }.
function generateAll(read = (file) => fs.readFileSync(file, 'utf8')) {
  const files = cssFiles();
  const sources = new Map(files.map((file) => [file, read(file)]));
  const bootstrapRules = readRules(read(BOOTSTRAP_CSS));
  const siteRules = readRules(stripGenerated(sources.get(SITE_CSS), 'site.css'));
  const scopedRules = files.flatMap((file) => readRules(stripGenerated(sources.get(file), relative(file))));
  const results = new Map();
  for (const file of files) {
    if (HAND_WRITTEN.has(relative(file))) continue;
    const source = sources.get(file);
    if (!/color-mix\(/i.test(stripGenerated(source, relative(file))) && !source.includes(BEGIN)) continue;
    const rootSources = file === SITE_CSS ? [bootstrapRules] : [bootstrapRules, siteRules];
    results.set(file, { source, ...generate(source, { file: relative(file), rootSources, scopedRules, prefix: prefixFor(file) }) });
  }
  return results;
}

function main() {
  const write = process.argv.includes('--write');
  const verbose = process.argv.includes('--verbose');
  const results = generateAll();
  const stale = [];
  let exact = 0;
  let approximated = 0;
  for (const [file, result] of results) {
    exact += result.exact;
    approximated += result.approximated.length;
    if (verbose && result.approximated.length) console.log(`${relative(file)}: approximated ${result.approximated.length}\n  ${result.approximated.join('\n  ')}`);
    if (result.css === result.source) continue;
    if (write) fs.writeFileSync(file, result.css);
    else stale.push(relative(file));
  }
  const summary = `${results.size} stylesheets, ${exact} colour mixes worked out exactly, ${approximated} approximated by their dominant colour (they depend on the element)`;
  if (stale.length) {
    console.error(`color-mix() fallbacks are out of date in: ${stale.join(', ')}\nRun node tools/color-mix-fallbacks.js --write`);
    process.exitCode = 1;
    return;
  }
  console.log(write ? `color-mix() fallbacks written: ${summary}.` : `color-mix() fallbacks are up to date: ${summary}.`);
}

module.exports = { mixOklab, tokenMaps, splitTopLevel, findMixes, resolveColour, parseColour, formatColour, mixSrgb, evaluateMix, approximateMix, resolvePercent, generate, generateAll, stripGenerated, prefixFor, BEGIN, END, SUPPORTS, HAND_WRITTEN };

if (require.main === module) main();
