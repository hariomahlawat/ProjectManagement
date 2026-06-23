const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { JSDOM } = require('jsdom');

function load(document) {
  let source = fs.readFileSync('wwwroot/js/notebook/notebook-masonry-grid.js', 'utf8');
  source = source
    .replace(/export function calculateMasonrySpan/, 'function calculateMasonrySpan')
    .replace(/export function layoutMasonryBoard/, 'function layoutMasonryBoard')
    .replace(/export function initNotebookMasonryGrid/, 'function initNotebookMasonryGrid')
    .replace(/export const notebookMasonryTestHelpers =/, 'const notebookMasonryTestHelpers =');
  source += '\nmodule.exports = { calculateMasonrySpan, layoutMasonryBoard, notebookMasonryTestHelpers };';
  const context = {
    module: { exports: {} }, exports: {}, document, window: document.defaultView,
    MutationObserver: document.defaultView.MutationObserver,
    HTMLImageElement: document.defaultView.HTMLImageElement,
    getComputedStyle: document.defaultView.getComputedStyle
  };
  vm.createContext(context);
  vm.runInContext(source, context);
  return context.module.exports;
}

test('masonry span is calculated from intrinsic height and grid metrics', () => {
  const dom = new JSDOM('<div></div>');
  const { calculateMasonrySpan } = load(dom.window.document);
  assert.equal(calculateMasonrySpan(88, 8, 12), 5);
  assert.equal(calculateMasonrySpan(1, 8, 12), 1);
});

test('masonry layout applies row spans while preserving DOM order', () => {
  const dom = new JSDOM('<div class="notebook-shell" data-board-view="grid"><div id="b" data-notebook-board data-layout="masonry"><article data-note-id="a"></article><article data-note-id="b"></article></div></div>');
  const document = dom.window.document;
  const board = document.querySelector('#b');
  Object.defineProperty(dom.window, 'getComputedStyle', { value: () => ({ gridAutoRows: '8px', rowGap: '12px' }) });
  global.getComputedStyle = dom.window.getComputedStyle;
  document.querySelector('[data-note-id="a"]').getBoundingClientRect = () => ({ height: 88 });
  document.querySelector('[data-note-id="b"]').getBoundingClientRect = () => ({ height: 128 });
  const { layoutMasonryBoard } = load(document);
  layoutMasonryBoard(board, document.querySelector('.notebook-shell'));
  assert.equal(document.querySelector('[data-note-id="a"]').style.gridRowEnd, 'span 5');
  assert.equal(document.querySelector('[data-note-id="b"]').style.gridRowEnd, 'span 7');
  assert.deepEqual([...board.children].map((x) => x.dataset.noteId), ['a', 'b']);
});

test('list mode clears masonry spans', () => {
  const dom = new JSDOM('<div class="notebook-shell" data-board-view="list"><div id="b" data-notebook-board data-layout="masonry"><article data-note-id="a" style="grid-row-end:span 5"></article></div></div>');
  const document = dom.window.document;
  const { layoutMasonryBoard } = load(document);
  layoutMasonryBoard(document.querySelector('#b'), document.querySelector('.notebook-shell'));
  assert.equal(document.querySelector('[data-note-id="a"]').style.gridRowEnd, '');
});


test('normal grid layout clears spans and relies on natural row height', () => {
  const dom = new JSDOM('<div class="notebook-shell" data-board-view="grid"><div id="b" data-notebook-board data-layout="grid"><article data-note-id="a" style="grid-row-end:span 5"></article></div></div>');
  const document = dom.window.document;
  const { layoutMasonryBoard } = load(document);
  const board = document.querySelector('#b');
  layoutMasonryBoard(board, document.querySelector('.notebook-shell'));
  assert.equal(document.querySelector('[data-note-id="a"]').style.gridRowEnd, '');
  assert.equal(board.classList.contains('is-masonry-ready'), false);
});
