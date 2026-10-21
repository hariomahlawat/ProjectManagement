const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadBoard(dom) {
  global.document = dom.window.document;
  global.CSS = dom.window.CSS || { escape: (value) => String(value).replace(/"/g, '\\"') };
  const scriptPath = path.resolve(__dirname, 'notebook-board.js');
  const script = fs.readFileSync(scriptPath, 'utf8')
    .replace(
      /^import\s*\{[\s\S]*?\}\s*from\s*['\"]\.\/notebook-errors\.js['\"];\r?\n/m,
      "class NotebookCardHtmlError extends Error { constructor(message) { super(message); this.code = 'notebook_invalid_card_html'; } } class NotebookBoardTargetError extends Error { constructor(message) { super(message); this.code = 'notebook_target_board_missing'; } }\n"
    )
    .replace('export function createNotebookBoard', 'function createNotebookBoard');
  const context = vm.createContext({ document: dom.window.document, CSS: global.CSS });
  vm.runInContext(`${script}; globalThis.__createNotebookBoard = createNotebookBoard;`, context);
  return context.__createNotebookBoard(dom.window.document);
}

test('notebook board rejects empty card HTML', async () => {
  const dom = new JSDOM('<div data-notebook-board="others"></div>');
  const board = loadBoard(dom);
  assert.throws(() => board.upsertCard('note-1', '', false), /empty/);
});

test('notebook board rejects error page HTML', async () => {
  const dom = new JSDOM('<div data-notebook-board="others"></div>');
  const board = loadBoard(dom);
  assert.throws(() => board.upsertCard('note-1', '<html><body>Error</body></html>', false), /note card|exactly one root/);
});

test('notebook board rejects multiple card roots', async () => {
  const dom = new JSDOM('<div data-notebook-board="others"></div>');
  const board = loadBoard(dom);
  assert.throws(() => board.upsertCard('note-1', '<article data-note-id="note-1"></article><article data-note-id="note-1"></article>', false), /exactly one root/);
});

test('notebook board rejects mismatched note id', async () => {
  const dom = new JSDOM('<div data-notebook-board="others"></div>');
  const board = loadBoard(dom);
  assert.throws(() => board.upsertCard('note-1', '<article data-note-id="note-2"></article>', false), /did not match/);
});

test('notebook board inserts a valid card into the requested board only', async () => {
  const dom = new JSDOM('<section><div data-notebook-board="pinned"></div><div data-notebook-board="others"></div></section>');
  const board = loadBoard(dom);
  const card = board.upsertCard('note-1', '<article data-note-id="note-1" data-version="v1"></article>', false);
  assert.equal(card.dataset.noteId, 'note-1');
  assert.equal(dom.window.document.querySelector('[data-notebook-board="others"] > [data-note-id="note-1"]'), card);
});



test('notebook board falls back to generic board for single-board views', async () => {
  const dom = new JSDOM('<div data-notebook-board></div>');
  const board = loadBoard(dom);
  const card = board.upsertCard('note-1', '<article data-note-id="note-1"></article>', true);
  assert.equal(dom.window.document.querySelector('[data-notebook-board] > [data-note-id="note-1"]'), card);
});

test('notebook board throws typed error for missing target board', async () => {
  const dom = new JSDOM('<div data-notebook-board="others"></div>');
  const board = loadBoard(dom);
  assert.throws(
    () => board.upsertCard('note-1', '<article data-note-id="note-1"></article>', true),
    (error) => error.code === 'notebook_target_board_missing'
  );
});

test('notebook board throws typed error for invalid card HTML', async () => {
  const dom = new JSDOM('<div data-notebook-board="others"></div>');
  const board = loadBoard(dom);
  assert.throws(
    () => board.upsertCard('note-1', '<article data-note-id="note-2"></article>', false),
    (error) => error.code === 'notebook_invalid_card_html'
  );
});


test('notebook board uses grid for four or fewer adaptive cards', async () => {
  const dom = new JSDOM('<div data-notebook-board="others" data-layout-policy="masonry-threshold"><article data-note-id="1"></article><article data-note-id="2"></article><article data-note-id="3"></article><article data-note-id="4"></article></div>');
  const board = loadBoard(dom);
  const element = dom.window.document.querySelector('[data-notebook-board="others"]');
  board.refreshSectionVisibility();
  assert.equal(element.dataset.itemCount, '4');
  assert.equal(element.dataset.layout, 'grid');
});

test('notebook board switches adaptive layout to masonry above four cards', async () => {
  const dom = new JSDOM('<div data-notebook-board="others" data-layout-policy="masonry-threshold"><article data-note-id="1"></article><article data-note-id="2"></article><article data-note-id="3"></article><article data-note-id="4"></article></div>');
  const board = loadBoard(dom);
  const element = dom.window.document.querySelector('[data-notebook-board="others"]');
  board.insertCard('<article data-note-id="5"></article>', false);
  assert.equal(element.dataset.itemCount, '5');
  assert.equal(element.dataset.layout, 'masonry');
});


test('notebook board honours always-masonry policy even for compact sections', async () => {
  const dom = new JSDOM('<div data-notebook-board="others" data-layout-policy="masonry-always"><article data-note-id="1"></article><article data-note-id="2"></article></div>');
  const board = loadBoard(dom);
  const element = dom.window.document.querySelector('[data-notebook-board="others"]');
  board.refreshSectionVisibility();
  assert.equal(element.dataset.itemCount, '2');
  assert.equal(element.dataset.layout, 'masonry');
});

test('notebook board counts a home system note as a visual card without requiring a note id', () => {
  const dom = new JSDOM('<section data-notebook-section="others"><span data-notebook-count="others"></span><div data-notebook-board="others" data-layout-policy="masonry-always"><article data-note-id="1"></article><article data-notebook-system-card="conference-directions" data-notebook-system-home-card="conference-directions"></article></div></section>');
  const board = loadBoard(dom);
  const element = dom.window.document.querySelector('[data-notebook-board="others"]');
  board.refreshSectionVisibility();
  assert.equal(element.dataset.itemCount, '2');
  assert.equal(dom.window.document.querySelector('[data-notebook-count="others"]').textContent, '2');
});

test('the Others heading shows only when Pinned and Others both have cards', () => {
  const markup = (pinned, others) => `
    <section data-notebook-section="pinned"><div data-notebook-board="pinned">${pinned}</div></section>
    <section data-notebook-section="others">
      <div data-notebook-section-header="others"><span data-notebook-count="others"></span></div>
      <div data-notebook-board="others">${others}</div>
    </section>`;
  const card = (id) => `<article data-note-id="${id}"></article>`;
  const headerHidden = (pinned, others) => {
    const dom = new JSDOM(markup(pinned, others));
    loadBoard(dom).refreshSectionVisibility();
    return dom.window.document.querySelector('[data-notebook-section-header="others"]').hidden;
  };

  assert.equal(headerHidden('', card(1)), true, 'nothing pinned');
  assert.equal(headerHidden(card(1), ''), true, 'nothing else');
  assert.equal(headerHidden(card(1), card(2)), false, 'both sections');
});

test('grouped reminder boards hide empty groups and keep updated cards in their group', () => {
  const dom = new JSDOM(`
    <section data-notebook-group="past-due"><span data-notebook-group-count>1</span><div data-notebook-board><article data-note-id="a"></article></div></section>
    <section data-notebook-group="today"><span data-notebook-group-count>1</span><div data-notebook-board><article data-note-id="b"></article></div></section>`);
  const board = loadBoard(dom);
  const doc = dom.window.document;

  board.upsertCard('b', '<article data-note-id="b" data-updated="true"></article>', true, { preservePosition: false });
  assert.equal(doc.querySelector('[data-notebook-group="today"] [data-note-id="b"]').dataset.updated, 'true', 'stays in Today');

  board.removeCard('a');
  assert.equal(doc.querySelector('[data-notebook-group="past-due"]').hidden, true);
  assert.equal(doc.querySelector('[data-notebook-group="past-due"] [data-notebook-group-count]').textContent, '0');
  assert.equal(doc.querySelector('[data-notebook-group="today"]').hidden, false);
});

test('a rescheduled or new reminder lands in the group its server-rendered card names', () => {
  const dom = new JSDOM(`
    <section data-notebook-group="past-due"><span data-notebook-group-count></span><div data-notebook-board><article data-note-id="a" data-reminder-group="past-due"></article><article data-note-id="x" data-reminder-group="past-due"></article></div></section>
    <section data-notebook-group="today"><span data-notebook-group-count></span><div data-notebook-board></div></section>
    <section data-notebook-group="upcoming"><span data-notebook-group-count></span><div data-notebook-board></div></section>`);
  const board = loadBoard(dom);
  const doc = dom.window.document;
  const groupOf = (id) => doc.querySelector(`[data-note-id="${id}"]`).closest('[data-notebook-group]').dataset.notebookGroup;

  // Past due -> later today, even when the caller asks to preserve position.
  board.upsertCard('a', '<article data-note-id="a" data-reminder-group="today"></article>', false, { preservePosition: true });
  assert.equal(groupOf('a'), 'today');
  assert.equal(doc.querySelector('[data-notebook-group="today"] [data-notebook-group-count]').textContent, '1');

  board.insertCard('<article data-note-id="b" data-reminder-group="upcoming"></article>');
  assert.equal(groupOf('b'), 'upcoming');
  assert.equal(doc.querySelector('[data-notebook-group="upcoming"]').hidden, false);
});
