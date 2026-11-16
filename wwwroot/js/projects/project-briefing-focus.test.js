// Behaviour tests for the Briefing Studio focus view (blocks plan PR 6): the real head script
// runs in jsdom, first against <head> alone (as the browser runs it, before the body exists) and
// then against the deck header's Focus button.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const focusSource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-focus.js'),
  'utf8');

const STORAGE_KEY = 'prism.briefingStudio.focus';

const HEADER = `
  <header class="pm-topbar">PRISM</header>
  <header class="pbd-deck-header">
    <h1>Monthly review</h1>
    <button type="button" class="btn pbd-focus-toggle" data-pbd-focus-toggle>
      <i class="bi bi-fullscreen" aria-hidden="true" data-pbd-focus-icon></i>
      <span data-pbd-focus-label>Focus</span>
    </button>
    <button type="submit" data-pbd-generate>Generate PowerPoint</button>
  </header>`;

const startPage = async ({ stored = null, storageThrows = false } = {}) => {
  const dom = new JSDOM(`<!doctype html><html><head></head><body>${HEADER}</body></html>`, {
    url: 'http://localhost/Workspace/BriefingDecks/7',
    runScripts: 'outside-only',
    pretendToBeVisual: true
  });
  const { window } = dom;
  if (stored !== null) window.localStorage.setItem(STORAGE_KEY, stored);
  if (storageThrows) {
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() { throw new window.DOMException('Blocked', 'SecurityError'); }
    });
  }
  const events = [];
  window.addEventListener('pbd:focus-change', (event) => events.push(event.detail.on));
  window.eval(focusSource);
  if (window.document.readyState === 'loading') {
    await new Promise((resolve) => window.document.addEventListener('DOMContentLoaded', resolve));
  }
  return { window, document: window.document, events, button: window.document.querySelector('[data-pbd-focus-toggle]') };
};

const press = (window, button) => {
  button.focus();
  button.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true }));
};

test('a remembered focus view is on <html> before the body is parsed', () => {
  // The browser runs the script from <head>: no body, no button yet.
  const dom = new JSDOM('<!doctype html><html><head></head></html>', { runScripts: 'outside-only', url: 'http://localhost/' });
  dom.window.localStorage.setItem(STORAGE_KEY, 'on');
  Object.defineProperty(dom.window.document, 'readyState', { configurable: true, get: () => 'loading' });
  dom.window.eval(focusSource);

  assert.equal(dom.window.document.documentElement.hasAttribute('data-pbd-focus'), true);
});

test('the Focus button starts off when nothing is remembered', async () => {
  const { document, button } = await startPage();

  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), false);
  assert.equal(button.textContent.trim(), 'Focus');
  assert.equal(button.querySelector('[data-pbd-focus-label]').textContent, 'Focus');
});

test('a remembered focus view shows the button pressed as Exit focus', async () => {
  const { document, button } = await startPage({ stored: 'on' });

  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), true);
  assert.equal(button.textContent.trim(), 'Exit focus');
  assert.equal(button.querySelector('[data-pbd-focus-label]').textContent, 'Exit focus');
  assert.equal(button.querySelector('[data-pbd-focus-icon]').classList.contains('bi-fullscreen-exit'), true);
});

test('pressing Focus enters and leaves the view, remembers it and keeps keyboard focus on the button', async () => {
  const { window, document, button, events } = await startPage();
  // One announcement of the state: the label, not aria-pressed as well.
  assert.equal(button.hasAttribute('aria-pressed'), false);

  press(window, button);
  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), true);
  assert.equal(button.textContent.trim(), 'Exit focus');
  assert.equal(button.querySelector('[data-pbd-focus-label]').textContent, 'Exit focus');
  assert.equal(window.localStorage.getItem(STORAGE_KEY), 'on');
  assert.equal(document.activeElement, button);

  press(window, button);
  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), false);
  assert.equal(button.textContent.trim(), 'Focus');
  assert.equal(button.querySelector('[data-pbd-focus-label]').textContent, 'Focus');
  assert.equal(window.localStorage.getItem(STORAGE_KEY), null);
  assert.equal(document.activeElement, button);

  // The Studio measures its height again on each change.
  assert.deepEqual(events, [true, false]);
});

test('Escape does not leave the focus view (it belongs to dialogs and Present)', async () => {
  const { window, document, button } = await startPage({ stored: 'on' });

  button.focus();
  document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  document.body.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), true);
});

test('blocked storage still lets the button work for this page', async () => {
  const { window, document, button } = await startPage({ storageThrows: true });

  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), false);
  press(window, button);
  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), true);
  assert.equal(button.textContent.trim(), 'Exit focus');
});

test('the focus view hides the top bar and rail only from 576px, and the button not on phones', () => {
  const css = fs.readFileSync(
    path.resolve(__dirname, '..', '..', 'css', 'pages', 'project-briefing-decks.css'),
    'utf8');
  const section = css.slice(css.indexOf('/* SECTION: Focus view'));

  assert.match(section, /@media \(min-width: 576px\) \{\s*html\[data-pbd-focus\]/);
  assert.match(section, /html\[data-pbd-focus\] \.pm-topbar/);
  assert.match(section, /\[data-workspace-rail\]/);
  assert.match(section, /--topbar-height: 0px/);
  assert.match(section, /@media \(max-width: 575\.98px\) \{\s*\.pbd-focus-toggle \{\s*display: none !important;/);
  // Token-only colours (Dark theme follows the --pbd-* tokens).
  assert.doesNotMatch(section, /#[0-9a-fA-F]{3,8}\b|rgba?\(/);
});

test('a change in another Studio tab, or a page back from the cache, follows the remembered choice', async () => {
  const { window, document, button, events } = await startPage({ stored: 'on' });

  // Another tab left focus: storage no longer has the key.
  window.localStorage.removeItem(STORAGE_KEY);
  window.dispatchEvent(new window.StorageEvent('storage', { key: STORAGE_KEY, newValue: null }));
  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), false);
  assert.equal(button.textContent.trim(), 'Focus');

  // Back to a cached page after focus was turned on elsewhere.
  window.localStorage.setItem(STORAGE_KEY, 'on');
  const pageshow = new window.Event('pageshow');
  Object.defineProperty(pageshow, 'persisted', { value: true });
  window.dispatchEvent(pageshow);
  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), true);
  assert.equal(button.textContent.trim(), 'Exit focus');

  // An unrelated key changes nothing.
  window.dispatchEvent(new window.StorageEvent('storage', { key: 'prism.theme', newValue: 'dark' }));
  assert.equal(document.documentElement.hasAttribute('data-pbd-focus'), true);
  assert.deepEqual(events, [false, true]);
});
