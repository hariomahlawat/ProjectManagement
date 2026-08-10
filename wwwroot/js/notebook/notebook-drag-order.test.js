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
  const context = {
    module: { exports: {} },
    exports: {},
    document,
    window: document.defaultView,
    Element: document.defaultView.Element,
    MutationObserver: document.defaultView.MutationObserver,
    CustomEvent: document.defaultView.CustomEvent
  };
  vm.createContext(context);
  vm.runInContext(source, context);
  return context.module.exports;
}

test('serialiseBoard preserves DOM order and versions', () => {
  const dom = new JSDOM('<div id="b"><article data-note-id="a" data-version="v1" data-reorderable="true"></article><article data-note-id="b" data-version="v2" data-reorderable="true"></article></div>');
  const helpers = loadHelpers(dom.window.document);
  assert.deepEqual(JSON.parse(JSON.stringify(helpers.serialiseBoard(dom.window.document.querySelector('#b')))), [
    { id: 'a', version: 'v1' }, { id: 'b', version: 'v2' }
  ]);
});


test('directCards only includes direct board children while keeping selectors element-matchable', () => {
  const dom = new JSDOM(`
    <div id="b">
      <article id="direct" data-note-id="direct" data-reorderable="true"></article>
      <div><article id="nested" data-note-id="nested" data-reorderable="true"></article></div>
    </div>`);
  const helpers = loadHelpers(dom.window.document);
  const board = dom.window.document.querySelector('#b');

  assert.deepEqual(helpers.directCards(board).map((card) => card.id), ['direct']);
});

test('serialiseBoard excludes shared read-only cards from an owner reorder payload', () => {
  const dom = new JSDOM('<div id="b"><article data-note-id="owned-a" data-version="v1" data-reorderable="true"></article><article data-note-id="shared" data-version="v2" data-reorderable="false"></article><article data-note-id="owned-b" data-version="v3" data-reorderable="true"></article></div>');
  const helpers = loadHelpers(dom.window.document);
  assert.deepEqual(JSON.parse(JSON.stringify(helpers.serialiseBoard(dom.window.document.querySelector('#b')))), [
    { id: 'owned-a', version: 'v1' }, { id: 'owned-b', version: 'v3' }
  ]);
});



test('system note participates in visual drag order but is excluded from normal Notebook reorder payload', () => {
  const dom = new JSDOM('<div id="b"><article data-note-id="a" data-version="v1" data-reorderable="true"></article><article data-notebook-system-home-card="conference-directions" data-reorderable="true"></article><article data-note-id="b" data-version="v2" data-reorderable="true"></article></div>');
  const helpers = loadHelpers(dom.window.document);
  const board = dom.window.document.querySelector('#b');
  assert.equal(helpers.directCards(board).length, 3);
  assert.deepEqual(JSON.parse(JSON.stringify(helpers.serialiseBoard(board))), [
    { id: 'a', version: 'v1' }, { id: 'b', version: 'v2' }
  ]);
  assert.equal(helpers.cardKey(board.children[1]), 'system:conference-directions');
});

test('restoreOrder restores mixed normal/system card sequence by stable keys', () => {
  const dom = new JSDOM('<div id="b"><article data-note-id="a"></article><article data-notebook-system-home-card="conference-directions"></article><article data-note-id="b"></article></div>');
  const helpers = loadHelpers(dom.window.document);
  const board = dom.window.document.querySelector('#b');
  helpers.restoreOrder(board, ['note:b', 'system:conference-directions', 'note:a']);
  assert.deepEqual([...board.children].map((card) => helpers.cardKey(card)), [
    'note:b', 'system:conference-directions', 'note:a'
  ]);
});
test('restoreOrder restores a previous board sequence', () => {
  const dom = new JSDOM('<div id="b"><article data-note-id="b"></article><article data-note-id="a"></article></div>');
  const helpers = loadHelpers(dom.window.document);
  const board = dom.window.document.querySelector('#b');
  helpers.restoreOrder(board, ['note:a', 'note:b']);
  assert.deepEqual([...board.children].map((x) => x.dataset.noteId), ['a', 'b']);
});

test('card body and open area are valid drag surfaces while controls are excluded', () => {
  const dom = new JSDOM(`
    <article data-note-id="a">
      <a class="notebook-card__open-area"><h3 id="title">Title</h3></a>
      <button id="button">Action</button>
      <a class="notebook-tag-chip" id="tag">Tag</a>
      <div class="notebook-card__open-area"><button id="nested-button">Nested action</button></div>
      <div id="empty"></div>
    </article>`);
  const helpers = loadHelpers(dom.window.document);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#title')), false);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#empty')), false);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#button')), true);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#nested-button')), true);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#tag')), true);
});


test('system note full-card open overlay is a passive drag surface while its actions stay protected', () => {
  const dom = new JSDOM(`
    <article data-notebook-system-home-card="conference-directions" data-reorderable="true">
      <button id="open" data-card-passive-open></button>
      <div class="notebook-card-actions"><button id="colour">Colour</button></div>
    </article>`);
  const helpers = loadHelpers(dom.window.document);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#open')), false);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#colour')), true);
});

test('direct drag source no longer requires explicit rearrange mode controls', () => {
  const source = fs.readFileSync('wwwroot/js/notebook/notebook-drag-order.js', 'utf8');
  assert.equal(source.includes('data-notebook-rearrange-toggle'), false);
  assert.equal(source.includes('rearrangeMode'), false);
  assert.match(source, /const isEnabled = \(\) => shell\.dataset\.boardView === 'grid'/);
  assert.match(source, /TOUCH_LONG_PRESS_MS = 300/);
});

test('visual rows are ordered top-to-bottom and left-to-right', () => {
  const dom = new JSDOM('<div id="b"><article id="c" data-note-id="c" data-reorderable="true"></article><article id="a" data-note-id="a" data-reorderable="true"></article><article id="b2" data-note-id="b" data-reorderable="true"></article></div>');
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
  const dom = new JSDOM('<div id="b"><article id="a" data-note-id="a" data-reorderable="true"></article><article id="b2" data-note-id="b" data-reorderable="true"></article><article id="c" data-note-id="c" data-reorderable="true"></article></div>');
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


test('adjacent forward movement crosses the immediate next card, not the card after it', () => {
  const dom = new JSDOM(`
    <div id="board">
      <article id="a" data-note-id="a" data-reorderable="true"></article>
      <div id="placeholder"></div>
      <article id="b" data-note-id="b" data-reorderable="true"></article>
      <article id="c" data-note-id="c" data-reorderable="true"></article>
    </div>`);
  const document = dom.window.document;
  const rects = {
    a: { top: 0, left: 0, right: 100, bottom: 80, width: 100, height: 80 },
    placeholder: { top: 0, left: 120, right: 220, bottom: 80, width: 100, height: 80 },
    b: { top: 0, left: 240, right: 340, bottom: 80, width: 100, height: 80 },
    c: { top: 0, left: 360, right: 460, bottom: 80, width: 100, height: 80 }
  };
  Object.entries(rects).forEach(([id, rect]) => {
    document.querySelector(`#${id}`).getBoundingClientRect = () => rect;
  });

  const helpers = loadHelpers(document);
  const board = document.querySelector('#board');
  const placeholder = document.querySelector('#placeholder');
  const cards = helpers.directCards(board);
  const transition = helpers.resolveAdjacentTransition(placeholder, cards, 1, 2);

  assert.equal(transition.boundaryCard.id, 'b');
  assert.equal(transition.axis, 'x');
  assert.equal(helpers.movePlaceholder(board, placeholder, 2, { index: 1 }, { x: 305, y: 40 }), true);
  assert.deepEqual([...board.children].map((node) => node.id), ['a', 'b', 'placeholder', 'c']);
});

test('adjacent reverse movement uses the real placeholder index and is symmetric', () => {
  const dom = new JSDOM(`
    <div id="board">
      <article id="a" data-note-id="a" data-reorderable="true"></article>
      <article id="b" data-note-id="b" data-reorderable="true"></article>
      <div id="placeholder"></div>
      <article id="c" data-note-id="c" data-reorderable="true"></article>
    </div>`);
  const document = dom.window.document;
  const rects = {
    a: { top: 0, left: 0, right: 100, bottom: 80, width: 100, height: 80 },
    b: { top: 0, left: 120, right: 220, bottom: 80, width: 100, height: 80 },
    placeholder: { top: 0, left: 240, right: 340, bottom: 80, width: 100, height: 80 },
    c: { top: 0, left: 360, right: 460, bottom: 80, width: 100, height: 80 }
  };
  Object.entries(rects).forEach(([id, rect]) => {
    document.querySelector(`#${id}`).getBoundingClientRect = () => rect;
  });

  const helpers = loadHelpers(document);
  const board = document.querySelector('#board');
  const placeholder = document.querySelector('#placeholder');
  const cards = helpers.directCards(board);
  const transition = helpers.resolveAdjacentTransition(placeholder, cards, 2, 1);

  assert.equal(transition.boundaryCard.id, 'b');
  assert.equal(transition.axis, 'x');
  assert.equal(helpers.movePlaceholder(board, placeholder, 1, { index: 2 }, { x: 155, y: 40 }), true);
  assert.deepEqual([...board.children].map((node) => node.id), ['a', 'placeholder', 'b', 'c']);
});

test('masonry row transitions use vertical hysteresis instead of an unrelated x coordinate', () => {
  const dom = new JSDOM(`
    <div id="board">
      <article id="a" data-note-id="a" data-reorderable="true"></article>
      <div id="placeholder"></div>
      <article id="b" data-note-id="b" data-reorderable="true"></article>
      <article id="c" data-note-id="c" data-reorderable="true"></article>
    </div>`);
  const document = dom.window.document;
  const rects = {
    a: { top: 0, left: 0, right: 100, bottom: 80, width: 100, height: 80 },
    placeholder: { top: 0, left: 120, right: 220, bottom: 80, width: 100, height: 80 },
    b: { top: 120, left: 0, right: 100, bottom: 200, width: 100, height: 80 },
    c: { top: 120, left: 120, right: 220, bottom: 200, width: 100, height: 80 }
  };
  Object.entries(rects).forEach(([id, rect]) => {
    document.querySelector(`#${id}`).getBoundingClientRect = () => rect;
  });

  const helpers = loadHelpers(document);
  const board = document.querySelector('#board');
  const placeholder = document.querySelector('#placeholder');
  const transition = helpers.resolveAdjacentTransition(placeholder, helpers.directCards(board), 1, 2);

  assert.equal(transition.boundaryCard.id, 'b');
  assert.equal(transition.axis, 'y');
  assert.equal(transition.boundary, 100);
  assert.equal(helpers.movePlaceholder(board, placeholder, 2, { index: 1 }, { x: 0, y: 115 }), true);
  assert.deepEqual([...board.children].map((node) => node.id), ['a', 'b', 'placeholder', 'c']);
});

test('reverse masonry row transitions use the same vertical boundary symmetrically', () => {
  const dom = new JSDOM(`
    <div id="board">
      <article id="a" data-note-id="a" data-reorderable="true"></article>
      <div id="placeholder"></div>
      <article id="b" data-note-id="b" data-reorderable="true"></article>
      <article id="c" data-note-id="c" data-reorderable="true"></article>
    </div>`);
  const document = dom.window.document;
  const rects = {
    a: { top: 0, left: 0, right: 100, bottom: 80, width: 100, height: 80 },
    placeholder: { top: 120, left: 0, right: 100, bottom: 200, width: 100, height: 80 },
    b: { top: 120, left: 120, right: 220, bottom: 200, width: 100, height: 80 },
    c: { top: 120, left: 240, right: 340, bottom: 200, width: 100, height: 80 }
  };
  Object.entries(rects).forEach(([id, rect]) => {
    document.querySelector(`#${id}`).getBoundingClientRect = () => rect;
  });

  const helpers = loadHelpers(document);
  const board = document.querySelector('#board');
  const placeholder = document.querySelector('#placeholder');
  const transition = helpers.resolveAdjacentTransition(placeholder, helpers.directCards(board), 1, 0);

  assert.equal(transition.boundaryCard.id, 'a');
  assert.equal(transition.axis, 'y');
  assert.equal(transition.boundary, 100);
  assert.equal(helpers.movePlaceholder(board, placeholder, 0, { index: 1 }, { x: 999, y: 85 }), true);
  assert.deepEqual([...board.children].map((node) => node.id), ['placeholder', 'a', 'b', 'c']);
});

test('dropping at the reorderable end keeps shared read-only cards outside the owned order region', () => {
  const dom = new JSDOM(`
    <div id="board">
      <article id="a" data-note-id="a" data-reorderable="true"></article>
      <div id="placeholder"></div>
      <article id="b" data-note-id="b" data-reorderable="true"></article>
      <article id="shared" data-note-id="shared" data-reorderable="false"></article>
    </div>`);
  const document = dom.window.document;
  const helpers = loadHelpers(document);
  const board = document.querySelector('#board');
  const placeholder = document.querySelector('#placeholder');

  assert.equal(helpers.movePlaceholder(board, placeholder, 2, null, { x: 0, y: 0 }), true);
  assert.deepEqual([...board.children].map((node) => node.id), ['a', 'b', 'placeholder', 'shared']);
  assert.deepEqual(helpers.directCards(board).map((card) => card.id), ['a', 'b']);
});

test('drag engine no longer depends on native HTML drag events', () => {
  const source = fs.readFileSync('wwwroot/js/notebook/notebook-drag-order.js', 'utf8');
  assert.equal(/addEventListener\(['"]dragstart/.test(source), false);
  assert.equal(/addEventListener\(['"]dragover/.test(source), false);
  assert.equal(/addEventListener\(['"]drop/.test(source), false);
  assert.equal(/\.draggable\s*=\s*true/.test(source), false);
});


test('checklist text remains a passive card surface while the checkbox stays protected during direct drag', () => {
  const dom = new JSDOM(`
    <article data-note-id="a">
      <div class="notebook-checklist-preview" id="checklist">
        <div id="blank"></div>
        <div class="notebook-check-row">
          <button class="notebook-check-toggle" id="toggle"></button>
          <span class="notebook-check-text" id="rowtext">Row</span>
        </div>
        <strong id="summary">1/2 complete</strong>
      </div>
    </article>`);
  const helpers = loadHelpers(dom.window.document);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#blank')), false);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#summary')), false);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#toggle')), true);
  assert.equal(helpers.isInteractiveDragTarget(dom.window.document.querySelector('#rowtext')), false);
});
