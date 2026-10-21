#!/usr/bin/env node
'use strict';

// Theme coverage: counts literal colours (hex, rgb(), hsl()) that paint the UI without going
// through the site tokens, per stylesheet and in inline view styles. A literal is fine for
// Light but leaves Dark (data-bs-theme="dark") with a light island unless a dark rule
// repaints it, so this is the work list for making every page coherent in both themes.
//
// Not counted: declarations inside [data-bs-theme="dark"] rules or @media print, var()
// fallbacks (var(--pm-card, #fff)), and the token definitions in site.css :root blocks.
//
// Usage:
//   node tools/theme-coverage.js            check against the baseline (exit 1 on growth)
//   node tools/theme-coverage.js --write    lower the baseline and regenerate the report
//   node tools/theme-coverage.js --write --allow-increase   accept growth (needs a reason in review)
//
// The baseline (tools/theme-coverage-baseline.json) only ratchets down: --write never raises
// a number unless --allow-increase is passed.

const fs = require('node:fs');
const path = require('node:path');
const { readRules, isDarkSelector, splitSelector, lightSelectorOf, propertyFamily } = require('./lib/css-rules');

const ROOT = path.resolve(__dirname, '..');
const CSS_ROOT = path.join(ROOT, 'wwwroot', 'css');
const VIEW_ROOTS = ['Pages', 'Areas'].map((dir) => path.join(ROOT, dir));
const BASELINE_PATH = path.join(__dirname, 'theme-coverage-baseline.json');
const REPORT_PATH = path.join(ROOT, 'docs', 'ux', 'theme-coverage.md');

// Files whose colours are intentionally fixed in both themes.
const EXEMPT = {
  'wwwroot/css/print.css': 'Print output is always light.',
  'wwwroot/css/progress-review.print.css': 'Print output is always light.',
  'wwwroot/css/projects/arpp-library-print.css': 'Print output is always light.',
  'wwwroot/css/project-office-reports/arpp-print.css': 'Print output is always light.',
  'wwwroot/css/project-office-reports/ffc-detailed-table.print.css': 'Print output is always light.',
  'wwwroot/css/fonts/mulish.css': 'Font faces only.',
};

// Waves group pages by how often they are used; earlier waves are converted first.
const WAVES = [
  { name: 'Wave 0: shell and Project Overview (done in #2700)', match: /navigation-shell\.css$|pages\/project-portfolio\.css$|pages\/project-lifecycle-actions\.css$|projects\/timeline\.css$|components\/prism-confirm-dialog\.css$/ },
  { name: 'Wave 1: Dashboard, My Workspace, Projects lists, Calendar, shared site.css', match: /(^|\/)site\.css$|pages\/dashboard\.css$|widgets\/|dashboard\/|(^|\/)workspace\.css$|(^|\/)command-workspace\.css$|erp-activity-strip\.css$|officer-conference\.css$|todo-widget\.css$|calendar\.css$|projects\/(index|ongoing-conference|create)\.css$|components\/pm-table\.css$|pages\/projects-completed-summary\.css$/ },
  { name: 'Wave 2: Notebook, Photos, Documents, Search', match: /notebook\.css$|pages\/photos-|docrepo|pages\/search\.css$|pages\/project-(photos|videos|video-upload|documents|document-upload)/ },
  { name: 'Wave 3: Project pages and reports', match: /pages\/project-|pages\/projects-|projects\/|stage-tracker\.css$|process-flow\.css$|progress-review\.css$|proliferation|project-ideas|tot\.css$|site-ipr\.css$|analytics\.css$/ },
  { name: 'Wave 4: Action tracker, tasks, office reports, admin and the rest', match: /./ },
];

const COLOUR = /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(/g;

function toPosix(file) {
  return path.relative(ROOT, file).split(path.sep).join('/');
}

function walk(dir, ext, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, ext, out);
    else if (entry.name.endsWith(ext)) out.push(full);
  }
  return out;
}

// Removes var() fallbacks so var(--pm-card, #fff) counts as a token use, not a literal.
function stripVarFallbacks(value) {
  let previous;
  let current = value;
  do {
    previous = current;
    current = current.replace(/var\(\s*(--[\w-]+)\s*,[^()]*(?:\([^()]*\)[^()]*)*\)/g, 'var($1)');
  } while (current !== previous);
  return current;
}

function countLiterals(text) {
  // Quoted strings (content: "black") are text, not colours.
  const unquoted = text.replace(/"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'/g, '""');
  const matches = stripVarFallbacks(unquoted).replace(/\b(?:rgba?|hsla?)\(\s*var\(/g, 'var(').match(COLOUR);
  return matches ? matches.length : 0;
}

// Literals per file, and "gaps": literals in light rules that no [data-bs-theme="dark"] rule
// repaints (same selector and property family). Gaps are what still shows light in Dark.
function analyseCss(source, file) {
  const isSite = file.endsWith('wwwroot/css/site.css');
  const rules = readRules(source);
  const covered = new Set();
  let darkRules = 0;
  for (const rule of rules) {
    if (!isDarkSelector(rule.selector)) continue;
    darkRules += 1;
    for (const part of splitSelector(rule.selector)) {
      const light = lightSelectorOf(part);
      // Scoped by at-rules: a dark rule inside a media query only covers that query.
      for (const d of rule.declarations) covered.add(`${light}|${propertyFamily(d.property)}|${rule.atRules.join('|')}`);
    }
  }
  let literals = 0;
  let gaps = 0;
  for (const rule of rules) {
    if (isDarkSelector(rule.selector)) continue;
    if (rule.atRules.some((at) => /^@media[^{]*\b(print|prefers-color-scheme\s*:\s*dark)\b/.test(at))) continue;
    const parts = splitSelector(rule.selector);
    for (const d of rule.declarations) {
      if (isSite && d.property.startsWith('--') && parts.some((part) => /^:root\b/.test(part))) continue;
      const n = countLiterals(d.value);
      if (!n) continue;
      literals += n;
      const family = propertyFamily(d.property);
      // A top-level dark rule covers every width; a scoped one only its own query.
      const scope = rule.atRules.join('|');
      const isCovered = (part) => covered.has(`${part}|${family}|`) || covered.has(`${part}|${family}|${scope}`);
      if (!parts.every(isCovered)) gaps += n;
    }
  }
  return { literals, gaps, darkRules };
}

// Literal colours in inline style attributes, in both quote forms: style="..." and style='...'.
function countInlineStyleLiterals(source) {
  let count = 0;
  // (?<![\w-]) so data-style="..." and similar attributes are not mistaken for style.
  for (const match of source.matchAll(/(?<![\w-])style\s*=\s*(?:"([^"]*)"|'([^']*)')/g)) count += countLiterals(match[1] ?? match[2]);
  return count;
}

function analyseViews() {
  const results = {};
  for (const root of VIEW_ROOTS) {
    for (const file of walk(root, '.cshtml')) {
      const source = fs.readFileSync(file, 'utf8');
      const count = countInlineStyleLiterals(source);
      if (count > 0) results[toPosix(file)] = count;
    }
  }
  return results;
}

function measure() {
  const css = {};
  const gaps = {};
  const dark = {};
  for (const file of walk(CSS_ROOT, '.css')) {
    const key = toPosix(file);
    if (EXEMPT[key]) continue;
    const result = analyseCss(fs.readFileSync(file, 'utf8'), key);
    css[key] = result.literals;
    gaps[key] = result.gaps;
    dark[key] = result.darkRules;
  }
  return { css, gaps, dark, views: analyseViews() };
}

function readBaseline() {
  if (!fs.existsSync(BASELINE_PATH)) return { css: {}, gaps: {}, views: {} };
  return JSON.parse(fs.readFileSync(BASELINE_PATH, 'utf8'));
}

function compare(current, baseline) {
  const grown = [];
  const lowered = [];
  for (const kind of ['css', 'gaps', 'views']) {
    const keys = new Set([...Object.keys(current[kind]), ...Object.keys(baseline[kind] || {})]);
    for (const key of keys) {
      const now = current[kind][key] || 0;
      const allowed = (baseline[kind] || {})[key] || 0;
      if (now > allowed) grown.push({ kind, key, now, allowed });
      else if (now < allowed) lowered.push({ kind, key, now, allowed });
    }
  }
  return { grown, lowered };
}

function waveOf(file) {
  return WAVES.findIndex((wave) => wave.match.test(file));
}

function sorted(object) {
  return Object.fromEntries(Object.entries(object).filter(([, n]) => n > 0).sort(([a], [b]) => a.localeCompare(b)));
}

function renderReport(current) {
  const total = Object.values(current.css).reduce((a, b) => a + b, 0);
  const viewTotal = Object.values(current.views).reduce((a, b) => a + b, 0);
  const lines = [
    '# Theme coverage',
    '',
    'Generated by `node tools/theme-coverage.js --write`; do not edit by hand.',
    '',
    'A **literal** is a colour written directly (hex, `rgb()`, `hsl()`) instead of a site token.',
    'It renders fine in Light but stays light in Dark unless a `[data-bs-theme="dark"]` rule',
    'repaints it. Not counted: dark rules, `@media print`, `var()` fallbacks and the token',
    'definitions in `site.css`. The npm test suite fails if any file gains literals; see',
    '[the dark theme doc](dark-theme-palette.md) for how to convert a page.',
    '',
    `Totals: **${total}** stylesheet literals in ${Object.values(current.css).filter((n) => n > 0).length} files, **${viewTotal}** in inline view styles.`,
    '',
  ];
  WAVES.forEach((wave, index) => {
    const files = Object.keys(current.css).filter((file) => waveOf(file) === index && current.css[file] > 0)
      .sort((a, b) => current.css[b] - current.css[a] || a.localeCompare(b));
    const waveTotal = files.reduce((sum, file) => sum + current.css[file], 0);
    lines.push(`## ${wave.name}`, '', `${waveTotal} literals.`, '');
    if (files.length === 0) { lines.push('Nothing left.', ''); return; }
    lines.push('| Stylesheet | Literals | Dark rules |', '| --- | ---: | ---: |');
    for (const file of files) lines.push(`| \`${file.replace('wwwroot/css/', '')}\` | ${current.css[file]} | ${current.dark[file] || 0} |`);
    lines.push('');
  });
  const views = Object.entries(current.views).sort(([, a], [, b]) => b - a);
  lines.push('## Inline view styles', '');
  if (views.length === 0) lines.push('None.', '');
  else {
    lines.push('| View | Literals |', '| --- | ---: |');
    for (const [file, n] of views) lines.push(`| \`${file}\` | ${n} |`);
    lines.push('');
  }
  lines.push('## Exempt', '', '| File | Why |', '| --- | --- |');
  for (const [file, why] of Object.entries(EXEMPT)) lines.push(`| \`${file.replace('wwwroot/css/', '')}\` | ${why} |`);
  lines.push('');
  return lines.join('\n');
}

function main(argv) {
  const write = argv.includes('--write');
  const allowIncrease = argv.includes('--allow-increase');
  const current = measure();
  const baseline = readBaseline();
  const { grown, lowered } = compare(current, baseline);

  if (write) {
    if (grown.length && !allowIncrease) {
      for (const g of grown) console.error(`Not raising ${g.key}: ${g.allowed} -> ${g.now}. Use tokens, or pass --allow-increase.`);
      return 1;
    }
    fs.writeFileSync(BASELINE_PATH, `${JSON.stringify({ css: sorted(current.css), gaps: sorted(current.gaps), views: sorted(current.views) }, null, 2)}\n`);
    fs.writeFileSync(REPORT_PATH, renderReport(current));
    console.log(`Baseline and ${toPosix(REPORT_PATH)} written.`);
    return 0;
  }

  if (grown.length) {
    console.error('Literal colours grew. Use the site tokens (docs/ux/dark-theme-palette.md) instead:');
    for (const g of grown) console.error(`  ${g.key} (${g.kind}): ${g.allowed} -> ${g.now}`);
    return 1;
  }
  if (lowered.length) {
    console.log(`${lowered.length} file(s) are below the baseline; run "node tools/theme-coverage.js --write" to lock that in.`);
  }
  return 0;
}

module.exports = { analyseCss, countInlineStyleLiterals, countLiterals, stripVarFallbacks, measure, readBaseline, compare, EXEMPT };

if (require.main === module) {
  process.exitCode = main(process.argv.slice(2));
}
