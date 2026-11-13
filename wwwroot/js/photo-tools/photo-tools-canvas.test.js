const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// The canvas helpers run in the page; a small stand-in window and document are enough here.
function loadShared() {
  const window = { PrismPhotoTools: {} };
  const context = { window, document: { querySelector: () => ({}) } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'photo-tools-canvas.js'), 'utf8'), context);
  return window.PrismPhotoCanvas;
}

const shared = loadShared();

test('isMainPointer takes the first finger or pen and the left mouse button only', () => {
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'touch', button: 0 }), true);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'pen', button: 0 }), true);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'mouse', button: 0 }), true);
  assert.equal(shared.isMainPointer({ isPrimary: false, pointerType: 'touch', button: 0 }), false);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'mouse', button: 2 }), false);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'mouse', button: 1 }), false);
});

test('isRepeatClick skips the second click of a double click but not a key press', () => {
  assert.equal(shared.isRepeatClick({ detail: 1 }), false);
  assert.equal(shared.isRepeatClick({ detail: 0 }), false);
  assert.equal(shared.isRepeatClick({ detail: 2 }), true);
  assert.equal(shared.isRepeatClick(undefined), false);
});

// Every place that draws or drags with a pointer starts only for the main pointer. Passport
// photo's own crop is left out: it reads two fingers on purpose, to pinch.
test('drawing and dragging start only for the main pointer', () => {
  const starts = [
    ['photo-tools-editor.js', "photoBox.addEventListener('pointerdown'"],
    ['photo-tools-editor.js', "cropBox.addEventListener('pointerdown'"],
    ['photo-tools-print.js', "thumb.addEventListener('pointerdown'"],
    ['photo-tools-background.js', "photoBox.addEventListener('pointerdown'"]
  ];
  for (const [file, start] of starts) {
    const source = fs.readFileSync(path.join(__dirname, file), 'utf8');
    const at = source.indexOf(start);
    assert.ok(at >= 0, `${file}: ${start} not found`);
    const handler = source.slice(at, source.indexOf('});', at));
    assert.match(handler, /shared\.isMainPointer\(event\)/, `${file}: ${start} must check isMainPointer`);
  }
});
