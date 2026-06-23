const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { JSDOM } = require('jsdom');

function loadHelpers(document) {
  let source = fs.readFileSync('wwwroot/js/notebook/notebook-drag-order.js', 'utf8');
  source = source.replace(/export function initNotebookDragOrder/, 'function initNotebookDragOrder')
    .replace(/export const notebookDragOrderTestHelpers =/, 'const notebookDragOrderTestHelpers =');
  source += '\nmodule.exports = notebookDragOrderTestHelpers;';
  const context = { module: { exports: {} }, exports: {}, document, window: document.defaultView, Element: document.defaultView.Element, MutationObserver: document.defaultView.MutationObserver };
  vm.createContext(context);
  vm.runInContext(source, context);
  return context.module.exports;
}

test('serialiseBoard preserves DOM order and versions', () => {
  const dom = new JSDOM('<div id="b"><article data-note-id="a" data-version="v1"></article><article data-note-id="b" data-version="v2"></article></div>');
  const helpers = loadHelpers(dom.window.document);
  assert.deepEqual(JSON.parse(JSON.stringify(helpers.serialiseBoard(dom.window.document.querySelector('#b')))), [
    { id: 'a', version: 'v1' }, { id: 'b', version: 'v2' }
  ]);
});

test('restoreOrder restores a previous board sequence', () => {
  const dom = new JSDOM('<div id="b"><article data-note-id="b"></article><article data-note-id="a"></article></div>');
  const helpers = loadHelpers(dom.window.document);
  const board = dom.window.document.querySelector('#b');
  helpers.restoreOrder(board, ['a', 'b']);
  assert.deepEqual([...board.children].map((x) => x.dataset.noteId), ['a', 'b']);
});

test('card body and open area are valid drag surfaces while controls are excluded', () => {
  const dom = new JSDOM(`
    <article data-note-id="a">
      <a class="notebook-card__open-area"><h3 id="title">Title</h3></a>
      <button id="button">Action</button>
      <a class="notebook-tag-chip" id="tag">Tag</a>
      <div id="empty"></div>
    </article>`);
  const helpers = loadHelpers(dom.window.document);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#title')), false);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#empty')), false);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#button')), true);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#tag')), true);
});

test('visual rows are ordered top-to-bottom and left-to-right', () => {
  const dom = new JSDOM('<div id="b"><article id="c" data-note-id="c"></article><article id="a" data-note-id="a"></article><article id="b2" data-note-id="b"></article></div>');
  const document = dom.window.document;
  const rects = {
    a: { top: 0, left: 0, right: 100, bottom: 80, width: 100, height: 80 },
    b: { top: 0, left: 120, right: 220, bottom: 80, width: 100, height: 80 },
    c: { top: 100, left: 0, right: 100, bottom: 180, width: 100, height: 80 }
  };
  document.querySelector('#a').getBoundingClientRect = () => rects.a;
  document.querySelector('#b2').getBoundingClientRect = () => rects.b;
  document.querySelector('#c').getBoundingClientRect = () => rects.c;
  const helpers = loadHelpers(document);
  const rows = helpers.groupVisualRows([...document.querySelectorAll('[data-note-id]')]);
  assert.deepEqual(JSON.parse(JSON.stringify(rows.map((row) => row.items.map((item) => item.card.dataset.noteId)))), [['a', 'b'], ['c']]);
});

test('insertion index follows row midpoint boundaries', () => {
  const dom = new JSDOM('<div id="b"><article id="a" data-note-id="a"></article><article id="b2" data-note-id="b"></article><article id="c" data-note-id="c"></article></div>');
  const document = dom.window.document;
  const rects = {
    a: { top: 0, left: 0, right: 100, bottom: 80, width: 100, height: 80 },
    b: { top: 0, left: 120, right: 220, bottom: 80, width: 100, height: 80 },
    c: { top: 100, left: 0, right: 100, bottom: 180, width: 100, height: 80 }
  };
  document.querySelector('#a').getBoundingClientRect = () => rects.a;
  document.querySelector('#b2').getBoundingClientRect = () => rects.b;
  document.querySelector('#c').getBoundingClientRect = () => rects.c;
  const helpers = loadHelpers(document);
  const board = document.querySelector('#b');
  assert.equal(helpers.calculateInsertionIndex(board, 20, 20), 0);
  assert.equal(helpers.calculateInsertionIndex(board, 180, 20), 2);
  assert.equal(helpers.calculateInsertionIndex(board, 20, 140), 2);
});

test('drag engine no longer depends on native HTML drag events', () => {
  const source = fs.readFileSync('wwwroot/js/notebook/notebook-drag-order.js', 'utf8');
  assert.equal(/addEventListener\(['"]dragstart/.test(source), false);
  assert.equal(/addEventListener\(['"]dragover/.test(source), false);
  assert.equal(/addEventListener\(['"]drop/.test(source), false);
  assert.equal(/\.draggable\s*=\s*true/.test(source), false);
});
