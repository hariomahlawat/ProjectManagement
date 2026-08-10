const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

const index = read('Pages', 'Notebook', 'Index.cshtml');
const digest = read('Pages', 'Notebook', '_NotebookConferenceDigest.cshtml');
const drag = read('wwwroot', 'js', 'notebook', 'notebook-drag-order.js');
const app = read('wwwroot', 'js', 'notebook', 'notebook-app.js');
const css = read('wwwroot', 'css', 'notebook.css');
const masonry = read('wwwroot', 'js', 'notebook', 'notebook-masonry-grid.js');

test('desktop rearrangement is direct and does not expose a mode button', () => {
  assert.doesNotMatch(index, /data-notebook-rearrange-toggle/);
  assert.doesNotMatch(index, /data-notebook-rearrange-done/);
  assert.doesNotMatch(drag, /rearrangeMode/);
  assert.match(drag, /DRAG_THRESHOLD_PX = 6/);
  assert.match(drag, /const isEnabled = \(\) => shell\.dataset\.boardView === 'grid'/);
});

test('direct child matching and adjacent transitions remain bidirectional', () => {
  assert.match(drag, /function directChildrenMatching/);
  assert.doesNotMatch(drag, /const OWNED_CARD_SELECTOR = ':scope/);
  assert.match(drag, /const crossedIndex = direction > 0 \? currentIndex : currentIndex - 1/);
  assert.match(drag, /axis: 'y'/);
});

test('touch drag keeps a deliberate long-press threshold', () => {
  assert.match(drag, /TOUCH_LONG_PRESS_MS = 300/);
  assert.match(drag, /TOUCH_CANCEL_DISTANCE_PX = 8/);
  assert.match(drag, /state\.timer = window\.setTimeout/);
});

test('system note click overlay is explicitly a passive drag surface', () => {
  assert.match(digest, /data-card-passive-open/);
  assert.match(drag, /\[data-card-passive-open\]/);
});

test('floating card surfaces are raised and close before dragging', () => {
  assert.match(app, /syncCardFloatingState/);
  assert.match(app, /notebook:drag-start/);
  assert.match(app, /closeCardColourPickers/);
  assert.match(css, /\.notebook-card\.has-open-popover/);
  assert.match(css, /bottom: calc\(100% \+ \.4rem\) !important/);
});

test('colour palette is compact within card geometry', () => {
  assert.match(css, /\.notebook-card \.notebook-colour-picker__popover[\s\S]*?width: min\(252px/);
  assert.match(css, /grid-template-columns: repeat\(4, minmax\(44px, 1fr\)\)/);
});

test('keyboard reorder handle remains available but is not shown on ordinary hover', () => {
  assert.match(css, /\.notebook-card-drag-handle:focus-visible/);
  assert.doesNotMatch(css, /\.notebook-card:hover \.notebook-card-drag-handle/);
});

test('system note remains a first-class masonry item', () => {
  assert.match(masonry, /\[data-notebook-system-home-card\]/);
});
