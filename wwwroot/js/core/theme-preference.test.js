const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'theme-preference.js'), 'utf8');

const controls = `
  <button id="light" data-theme-preference-value="light"></button>
  <button id="dark" data-theme-preference-value="dark"></button>
  <button id="system" data-theme-preference-value="system"></button>`;

function createDom({ stored, systemDark = false, storageThrows = false } = {}) {
  const dom = new JSDOM(`<!doctype html><html><body>${controls}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/',
  });
  const { window } = dom;
  const listeners = [];
  const query = {
    matches: systemDark,
    addEventListener: (_type, listener) => listeners.push(listener),
  };
  window.matchMedia = () => query;
  if (storageThrows) {
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() { throw new window.DOMException('blocked', 'SecurityError'); },
    });
  } else if (stored !== undefined) {
    window.localStorage.setItem('prism.theme', stored);
  }
  window.eval(script);
  const setSystemDark = (matches) => {
    query.matches = matches;
    listeners.forEach((listener) => listener());
  };
  return { window, document: window.document, setSystemDark };
}

const theme = (document) => document.documentElement.getAttribute('data-bs-theme');

test('defaults to light and leaves <html> without data-bs-theme', () => {
  const { document } = createDom({ systemDark: true });

  assert.equal(theme(document), null);
  assert.equal(document.getElementById('light').getAttribute('aria-pressed'), 'true');
});

test('applies a stored dark preference immediately', () => {
  const { document } = createDom({ stored: 'dark' });

  assert.equal(theme(document), 'dark');
});

test('ignores an unknown stored value', () => {
  const { document } = createDom({ stored: 'navy' });

  assert.equal(theme(document), null);
});

test('clicking a control applies and persists the choice', () => {
  const { window, document } = createDom();

  document.getElementById('dark').click();

  assert.equal(theme(document), 'dark');
  assert.equal(window.localStorage.getItem('prism.theme'), 'dark');
  assert.equal(document.getElementById('dark').getAttribute('aria-pressed'), 'true');
  assert.equal(document.getElementById('light').getAttribute('aria-pressed'), 'false');

  document.getElementById('light').click();

  assert.equal(theme(document), null);
  assert.equal(window.localStorage.getItem('prism.theme'), 'light');
});

test('system follows the operating system, including later changes', () => {
  const { document, setSystemDark } = createDom({ stored: 'system', systemDark: true });

  assert.equal(theme(document), 'dark');

  setSystemDark(false);
  assert.equal(theme(document), null);
});

test('works without storage access', () => {
  const { document } = createDom({ storageThrows: true });

  assert.equal(theme(document), null);
  document.getElementById('dark').click();
  assert.equal(theme(document), 'dark');
});

test('prints in light', () => {
  const { window, document } = createDom({ stored: 'dark' });

  window.dispatchEvent(new window.Event('beforeprint'));
  assert.equal(theme(document), null);

  window.dispatchEvent(new window.Event('afterprint'));
  assert.equal(theme(document), 'dark');
});

test('without storage, a chosen Dark survives a print cycle', () => {
  const { window, document } = createDom({ storageThrows: true });

  document.getElementById('dark').click();
  window.dispatchEvent(new window.Event('beforeprint'));
  window.dispatchEvent(new window.Event('afterprint'));

  assert.equal(theme(document), 'dark');
  assert.equal(window.PMThemePreference.get(), 'dark');
});

test('without storage, a chosen System keeps following the operating system', () => {
  const { document, setSystemDark } = createDom({ storageThrows: true, systemDark: false });

  document.getElementById('system').click();
  assert.equal(theme(document), null);

  setSystemDark(true);
  assert.equal(theme(document), 'dark');
});
