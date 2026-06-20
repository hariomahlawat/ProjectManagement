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
    .replace(/import .*notebook-errors\.js';\n/, 'class NotebookCardHtmlError extends Error { constructor(message) { super(message); this.code = \'notebook_invalid_card_html\'; } } class NotebookBoardTargetError extends Error { constructor(message) { super(message); this.code = \'notebook_target_board_missing\'; } }\n')
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
