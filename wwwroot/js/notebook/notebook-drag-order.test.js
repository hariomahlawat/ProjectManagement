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
  const context = { module: { exports: {} }, exports: {}, document, window: document.defaultView, MutationObserver: document.defaultView.MutationObserver };
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
