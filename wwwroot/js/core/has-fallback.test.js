const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const script = fs.readFileSync(path.join(__dirname, 'has-fallback.js'), 'utf8');

function createPage(body, { supportsHas = false } = {}) {
  const dom = new JSDOM(`<!doctype html><html><body>${body}</body></html>`, { runScripts: 'outside-only', url: 'https://prism.local/' });
  const { window } = dom;
  window.CSS = { supports: (query) => supportsHas && query === 'selector(:has(*))' };
  window.requestAnimationFrame = (callback) => callback();
  window.eval(script);
  return window;
}

test('a browser that understands :has() is left alone', () => {
  const window = createPage('<label class="pbd-design"><input type="radio" checked></label>', { supportsHas: true });
  assert.equal(window.document.documentElement.classList.contains('pm-no-has'), false);
  assert.equal(window.document.querySelector('.pbd-design').classList.contains('pm-has-checked'), false);
});

test('without :has(), the chosen design card is marked, and follows the choice', () => {
  const window = createPage(`
    <label class="pbd-design" id="a"><input type="radio" name="d" checked></label>
    <label class="pbd-design" id="b"><input type="radio" name="d"></label>`);
  const { document } = window;
  assert.equal(document.documentElement.classList.contains('pm-no-has'), true);
  assert.equal(document.getElementById('a').classList.contains('pm-has-checked'), true);
  assert.equal(document.getElementById('b').classList.contains('pm-has-checked'), false);

  const second = document.querySelector('#b input');
  second.checked = true;
  second.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.equal(document.getElementById('a').classList.contains('pm-has-checked'), false);
  assert.equal(document.getElementById('b').classList.contains('pm-has-checked'), true);
});

test('keyboard focus on a hidden input shows on its card, and leaves with it', () => {
  const window = createPage(`
    <label class="brochure-artwork-option"><input type="radio" id="art"></label>
    <button id="elsewhere">Elsewhere</button>`);
  const { document } = window;
  const card = document.querySelector('.brochure-artwork-option');
  document.getElementById('art').focus();
  assert.equal(card.classList.contains('pm-has-focus-visible'), true);
  document.getElementById('elsewhere').focus();
  assert.equal(card.classList.contains('pm-has-focus-visible'), false);
});

test('page and viewer states follow the DOM: open viewer, visible strip, ticked to-do', async () => {
  const window = createPage(`
    <div class="command-workspace--briefing-decks"><dialog class="pbd-viewer"></dialog></div>
    <div class="photos-viewer__stage"><div class="photos-viewer__strip" hidden></div></div>
    <div class="todo-row"><input type="checkbox" class="js-done-checkbox"></div>`);
  const { document } = window;
  const html = document.documentElement;
  assert.equal(document.body.classList.contains('pm-has-briefing-decks'), true);
  assert.equal(html.classList.contains('pm-has-pbd-viewer-open'), false);

  document.querySelector('.pbd-viewer').setAttribute('open', '');
  document.querySelector('.photos-viewer__strip').hidden = false;
  await new Promise((resolve) => setTimeout(resolve, 0)); // let the MutationObserver run
  assert.equal(html.classList.contains('pm-has-pbd-viewer-open'), true);
  assert.equal(document.querySelector('.photos-viewer__stage').classList.contains('pm-has-strip'), true);

  const box = document.querySelector('.js-done-checkbox');
  box.checked = true;
  box.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.equal(document.querySelector('.todo-row').classList.contains('pm-has-done-checked'), true);
});

test('every state class the stylesheets use is one the script sets, and each twin follows its :has() rule', () => {
  const window = createPage('');
  const classes = new Set(window.PrismHasFallback.rules.map((rule) => rule.className));
  const cssRoot = path.join(ROOT, 'wwwroot', 'css');
  const files = [];
  (function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.css') && !entry.name.endsWith('.min.css')) files.push(full);
    }
  })(cssRoot);
  const used = new Set();
  for (const file of files) {
    const css = fs.readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
    for (const match of css.matchAll(/\.pm-has-[a-z-]+/g)) {
      const name = match[0].slice(1);
      used.add(name);
      assert.ok(classes.has(name), `${path.relative(ROOT, file)} uses .${name}, which has-fallback.js never sets`);
    }
    // A twin sits under .pm-no-has, so a browser with :has() never sees it.
    for (const match of css.matchAll(/([^{}]*\.pm-has-[a-z-]+[^{}]*)\{/g)) {
      assert.match(match[1], /\.pm-no-has\b/, `${path.relative(ROOT, file)}: "${match[1].trim()}" must be scoped under .pm-no-has`);
    }
  }
  for (const name of classes) assert.ok(used.has(name), `has-fallback.js sets .${name}, but no stylesheet uses it`);
});

test('no stylesheet mixes :has() with other selectors in one list (an older browser would drop the whole rule)', () => {
  const { readRules } = require(path.join(ROOT, 'tools', 'lib', 'css-rules'));
  const { splitTopLevel, stripGenerated } = require(path.join(ROOT, 'tools', 'color-mix-fallbacks'));
  const cssRoot = path.join(ROOT, 'wwwroot', 'css');
  const files = [];
  (function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.css') && !entry.name.endsWith('.min.css')) files.push(full);
    }
  })(cssRoot);
  const mixed = [];
  for (const file of files) {
    for (const rule of readRules(stripGenerated(fs.readFileSync(file, 'utf8')))) {
      const parts = splitTopLevel(rule.selector);
      const withHas = parts.filter((part) => part.includes(':has('));
      if (withHas.length && withHas.length < parts.length) mixed.push(`${path.relative(ROOT, file)}: ${rule.selector}`);
    }
  }
  assert.deepEqual(mixed, [], 'Put the :has() selectors in a rule of their own');
});

test('the layout loads the script', () => {
  const layout = fs.readFileSync(path.join(ROOT, 'Pages', 'Shared', '_Layout.cshtml'), 'utf8');
  assert.match(layout, /<script src="~\/js\/core\/has-fallback\.js" asp-append-version="true" defer><\/script>/);
});
