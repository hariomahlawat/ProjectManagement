const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { JSDOM } = require('jsdom');

// The module is an ES module in a CommonJS package, so load a copy with an .mjs extension.
let modulePromise;
function loadModule() {
  if (!modulePromise) {
    const copy = path.join(os.tmpdir(), `prism-search-combobox-${process.pid}.mjs`);
    fs.copyFileSync(path.join(__dirname, 'search-combobox.js'), copy);
    modulePromise = import(pathToFileURL(copy).href);
  }
  return modulePromise;
}

const markup = `
  <form id="form" action="/Common/Search" method="get">
    <input id="q" type="search" name="q" data-search-combobox />
    <button type="button" data-clear hidden>x</button>
  </form>
  <a id="outside" href="#">elsewhere</a>`;

function createDom({ storageThrows = false } = {}) {
  const dom = new JSDOM(`<!doctype html><html><body>${markup}</body></html>`, {
    url: 'https://prism.local/Projects',
    pretendToBeVisual: true,
  });
  const { window } = dom;
  if (storageThrows) {
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() { throw new window.DOMException('blocked', 'SecurityError'); },
    });
  }
  return { window, document: window.document };
}

function jsonResponse(payload, ok = true) {
  return Promise.resolve({ ok, status: ok ? 200 : 500, json: () => Promise.resolve(payload) });
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

function key(window, target, name, extra = {}) {
  const event = new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...extra });
  target.dispatchEvent(event);
  return event;
}

async function typeQuery(window, input, value) {
  input.focus();
  input.value = value;
  input.dispatchEvent(new window.Event('input', { bubbles: true }));
  await tick();
  await tick();
}

const suggestions = [
  { title: 'AURA surveillance drone', subtitle: 'Project', url: '/Projects/Overview/12', sourceModule: 'Projects', category: 'Projects', identifier: 'SDD-12' },
  { title: '<img src=x onerror=alert(1)> report', url: '/DocumentRepository/Documents/View/5', sourceModule: 'Document Repository', category: 'Documents' },
  { title: 'Phishing', url: 'https://evil.example/steal', sourceModule: 'Projects', category: 'Projects' },
];

test('wires the input as a combobox that controls a stable listbox', async () => {
  const { createSearchCombobox } = await loadModule();
  const { document } = createDom();
  const input = document.getElementById('q');
  const combobox = createSearchCombobox(input, { fetchImpl: () => jsonResponse([]), debounceMs: 0 });

  assert.equal(input.getAttribute('role'), 'combobox');
  assert.equal(input.getAttribute('aria-expanded'), 'false');
  assert.equal(input.getAttribute('aria-controls'), 'q-listbox');
  assert.equal(combobox.listbox.id, 'q-listbox');
  assert.equal(combobox.listbox.getAttribute('role'), 'listbox');
  assert.equal(createSearchCombobox(input), null, 'a second call does not wire the input twice');
});

test('renders suggestions as text, drops other origins and keeps "See all" reachable by arrows', async () => {
  const { createSearchCombobox } = await loadModule();
  const { window, document } = createDom();
  const input = document.getElementById('q');
  const { listbox } = createSearchCombobox(input, { fetchImpl: () => jsonResponse(suggestions), debounceMs: 0 });

  await typeQuery(window, input, 'aura');

  const options = Array.from(listbox.querySelectorAll('[role="option"]'));
  assert.equal(options.length, 3, 'two same-origin suggestions plus "See all results"');
  assert.equal(listbox.querySelector('img'), null, 'titles are never parsed as HTML');
  assert.equal(options[0].querySelector('mark').textContent, 'AURA');
  assert.ok(options.every((option) => option.tabIndex === -1));
  assert.equal(input.getAttribute('aria-expanded'), 'true');

  key(window, input, 'ArrowDown');
  assert.equal(input.getAttribute('aria-activedescendant'), options[0].id);
  assert.equal(options[0].getAttribute('aria-selected'), 'true');
  key(window, input, 'ArrowUp');
  key(window, input, 'ArrowUp');
  assert.equal(input.getAttribute('aria-activedescendant'), options[1].id);
  key(window, input, 'ArrowDown');
  assert.match(options[2].textContent, /See all results for “aura”/);
  assert.equal(input.getAttribute('aria-activedescendant'), options[2].id);
});

test('Enter on an active suggestion navigates only within the site', async () => {
  const { createSearchCombobox } = await loadModule();
  const { window, document } = createDom();
  const input = document.getElementById('q');
  const visited = [];
  createSearchCombobox(input, { fetchImpl: () => jsonResponse(suggestions), debounceMs: 0, navigate: (url) => visited.push(url) });

  await typeQuery(window, input, 'aura');
  key(window, input, 'ArrowDown');
  const enter = key(window, input, 'Enter');

  assert.equal(enter.defaultPrevented, true);
  assert.deepEqual(visited, ['https://prism.local/Projects/Overview/12']);
});

test('Escape closes the suggestions without clearing the query', async () => {
  const { createSearchCombobox } = await loadModule();
  const { window, document } = createDom();
  const input = document.getElementById('q');
  createSearchCombobox(input, { fetchImpl: () => jsonResponse(suggestions), debounceMs: 0 });

  await typeQuery(window, input, 'aura');
  const first = key(window, input, 'Escape');
  assert.equal(first.defaultPrevented, true, 'the browser must not clear type="search" on Escape');
  assert.equal(input.value, 'aura');
  assert.equal(input.getAttribute('aria-expanded'), 'false');

  const second = key(window, input, 'Escape');
  assert.equal(second.defaultPrevented, true);
  assert.equal(input.value, 'aura');
});

test('an empty submit is stopped instead of loading an empty search', async () => {
  const { createSearchCombobox } = await loadModule();
  const { window, document } = createDom();
  const input = document.getElementById('q');
  createSearchCombobox(input, { fetchImpl: () => jsonResponse([]), debounceMs: 0 });

  const submit = new window.Event('submit', { bubbles: true, cancelable: true });
  document.getElementById('form').dispatchEvent(submit);
  assert.equal(submit.defaultPrevented, true);
});

test('shows an error row, not "no matches", when suggestions fail', async () => {
  const { createSearchCombobox } = await loadModule();
  const { window, document } = createDom();
  const input = document.getElementById('q');
  const original = console.warn;
  console.warn = () => {};
  try {
    const { popup, listbox } = createSearchCombobox(input, { fetchImpl: () => jsonResponse(null, false), debounceMs: 0 });
    await typeQuery(window, input, 'aura');

    const status = popup.querySelector('.pm-search-popup__status');
    assert.equal(status.hidden, false);
    assert.match(status.textContent, /unavailable/i);
    assert.equal(listbox.querySelectorAll('[role="option"]').length, 1, 'only "See all results" remains');
  } finally {
    console.warn = original;
  }
});

test('says when there are no quick matches', async () => {
  const { createSearchCombobox } = await loadModule();
  const { window, document } = createDom();
  const input = document.getElementById('q');
  const { popup } = createSearchCombobox(input, { fetchImpl: () => jsonResponse([]), debounceMs: 0 });

  await typeQuery(window, input, 'zzzz');
  assert.match(popup.querySelector('.pm-search-popup__status').textContent, /No quick matches/);
});

test('remembers searches and offers them on an empty box', async () => {
  const { createSearchCombobox, rememberSearch, readRecentSearches, RECENT_LIMIT } = await loadModule();
  const { window, document } = createDom();
  ['alpha', 'beta', 'Alpha'].forEach((query) => rememberSearch(query, window));
  assert.deepEqual(readRecentSearches(window), ['Alpha', 'beta']);

  for (let index = 0; index < RECENT_LIMIT + 3; index += 1) rememberSearch(`q${index}`, window);
  assert.equal(readRecentSearches(window).length, RECENT_LIMIT);

  const input = document.getElementById('q');
  const { listbox } = createSearchCombobox(input, { fetchImpl: () => jsonResponse([]), debounceMs: 0 });
  input.focus();
  input.dispatchEvent(new window.Event('focus'));
  const labels = Array.from(listbox.querySelectorAll('[role="option"]'), (option) => option.textContent);
  assert.equal(labels.length, RECENT_LIMIT + 1);
  assert.equal(labels.at(-1), 'Clear recent searches');
});

test('recent searches survive blocked storage', async () => {
  const { rememberSearch, readRecentSearches, clearRecentSearches } = await loadModule();
  const { window } = createDom({ storageThrows: true });
  assert.doesNotThrow(() => rememberSearch('alpha', window));
  assert.deepEqual(readRecentSearches(window), []);
  assert.doesNotThrow(() => clearRecentSearches(window));
});

test('Ctrl+K focuses a visible search box, otherwise opens the Search page', async () => {
  const { initSearchShortcuts } = await loadModule();
  const { window, document } = createDom();
  const visited = [];
  initSearchShortcuts(document, { navigate: (url) => visited.push(url) });

  const hidden = key(window, document.body, 'k', { ctrlKey: true });
  assert.equal(hidden.defaultPrevented, true);
  assert.deepEqual(visited, ['/Common/Search']);

  const input = document.getElementById('q');
  input.getClientRects = () => [{}];
  key(window, document.body, 'k', { ctrlKey: true });
  assert.equal(document.activeElement, input);
  assert.equal(visited.length, 1);

  input.blur();
  const typing = key(window, document.getElementById('outside'), '/');
  assert.equal(typing.defaultPrevented, true);
  assert.equal(document.activeElement, input);
  const inField = key(window, input, '/');
  assert.equal(inField.defaultPrevented, false, '"/" is typed normally inside a field');
});

test('helpers: same-origin check, prefix highlight and OS shortcut label', async () => {
  const { sameOriginUrl, appendHighlighted, shortcutLabel } = await loadModule();
  const { document } = createDom();
  const location = new URL('https://prism.local/Projects');

  assert.equal(sameOriginUrl('/Projects/1', location), 'https://prism.local/Projects/1');
  assert.equal(sameOriginUrl('https://evil.example/', location), null);
  assert.equal(sameOriginUrl('javascript:alert(1)', location), null);

  const node = appendHighlighted(document.createElement('span'), 'Paura and AURA', 'aura');
  assert.equal(node.querySelector('mark').textContent, 'AURA', 'a word-start match wins over a mid-word one');
  assert.equal(node.textContent, 'Paura and AURA');

  assert.equal(shortcutLabel({ platform: 'MacIntel' }), '⌘K');
  assert.equal(shortcutLabel({ userAgentData: { platform: 'Windows' } }), 'Ctrl K');
});
