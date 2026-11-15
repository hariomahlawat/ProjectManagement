const test = require('node:test');
const assert = require('node:assert/strict');
const collage = require('./collage-core.js');

test('every page is 1,000 units wide and keeps its size shape', () => {
  assert.equal(collage.pageHeight('a4l'), 1000 * 2480 / 3508);
  assert.equal(collage.pageHeight('square'), 1000);
  assert.equal(collage.pageHeight('story'), 1000 * 3840 / 2160);
  // An unknown size falls back to A4 landscape instead of breaking the page.
  assert.equal(collage.pageHeight('nope'), collage.pageHeight('a4l'));
  // Paper sizes print at 300 dots per inch.
  for (const key of ['a4l', 'a4p', 'a3p']) {
    const size = collage.SIZES[key];
    assert.ok(Math.abs(size.width - size.mm[0] / 25.4 * 300) < 1, key);
    assert.ok(Math.abs(size.height - size.mm[1] / 25.4 * 300) < 1, key);
  }
});

test('a new collage is empty, white and versioned', () => {
  const c = collage.newCollage('slide');
  assert.deepEqual(c, { version: 1, size: 'slide', background: { type: 'color', value: '#ffffff' }, items: [] });
  assert.equal(collage.newCollage('bogus').size, 'a4l');
});

test('the picture type comes from the first bytes, not the name', () => {
  const pad = (bytes) => Uint8Array.from([...bytes, ...new Array(16).fill(0)]);
  assert.equal(collage.sniffImageType(pad([0xFF, 0xD8, 0xFF, 0xE0])), 'jpeg');
  assert.equal(collage.sniffImageType(pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])), 'png');
  assert.equal(collage.sniffImageType(pad([0x47, 0x49, 0x46, 0x38, 0x39, 0x61])), 'gif');
  assert.equal(collage.sniffImageType(pad([0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50])), 'webp');
  assert.equal(collage.sniffImageType(pad([0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45])), null, 'a WAV is not a WebP');
  assert.equal(collage.sniffImageType(pad([0x42, 0x4D])), 'bmp');
  assert.equal(collage.sniffImageType(pad([0x25, 0x50, 0x44, 0x46])), null, 'a PDF is not a photo');
  assert.equal(collage.sniffImageType(new Uint8Array(3)), null);
});

test('photos are checked for type, size and how many the collage takes', () => {
  const ok = { name: 'a.jpg', size: 1000, type: 'jpeg' };
  const pdf = { name: 'b.pdf', size: 1000, type: null };
  const huge = { name: 'c.jpg', size: 50 * 1000 * 1000 + 1, type: 'jpeg' };
  const { accepted, refused } = collage.checkPhotos([ok, pdf, huge], 0);
  assert.deepEqual(accepted, [ok]);
  assert.deepEqual(refused.map((r) => r.reason), ['not-photo', 'too-large']);

  // Exactly 50 MB is allowed; the count stops at 60.
  const atLimit = { name: 'd.jpg', size: 50 * 1000 * 1000, type: 'png' };
  const full = collage.checkPhotos([atLimit, ok, ok], 58);
  assert.equal(full.accepted.length, 2);
  assert.deepEqual(full.refused.map((r) => r.reason), ['too-many']);
});

test('refused photos are explained in one sentence', () => {
  assert.equal(collage.refusedMessage([]), '');
  assert.equal(
    collage.refusedMessage([{ name: 'notes.pdf', reason: 'not-photo' }]),
    '“notes.pdf” is not a JPG, PNG, WebP, GIF or BMP photo.');
  assert.equal(
    collage.refusedMessage([{ name: 'a', reason: 'too-large' }, { name: 'b', reason: 'too-large' }, { name: 'c', reason: 'too-many' }]),
    '2 files are larger than 50 MB; “c” did not fit: a collage takes up to 60 photos.');
  assert.equal(
    collage.refusedMessage([{ name: 'broken.jpg', reason: 'unreadable' }]),
    '“broken.jpg” could not be opened; it may be damaged.');
});

test('the working copy is at most 2,000 pixels long and never made larger', () => {
  assert.deepEqual(collage.workingSize(4000, 3000), { width: 2000, height: 1500 });
  assert.deepEqual(collage.workingSize(3000, 6000), { width: 1000, height: 2000 });
  assert.deepEqual(collage.workingSize(800, 600), { width: 800, height: 600 });
  assert.deepEqual(collage.workingSize(9000, 1), { width: 2000, height: 1 });
  // The tray's thumbnail is made from the working copy at a small size.
  assert.equal(collage.PHOTO_LIMITS.thumbnailSide, 240);
  assert.deepEqual(collage.workingSize(2000, 1500, collage.PHOTO_LIMITS.thumbnailSide), { width: 240, height: 180 });
});

test('a new photo fits on the page and the next ones do not land on top of it', () => {
  const height = collage.pageHeight('a4l');
  const places = [0, 1, 2, 3].map((n) => collage.placeNewPhoto('a4l', 4 / 3, n));
  for (const p of places) {
    assert.ok(p.width <= 1000 * 0.42 + 1);
    assert.ok(p.width / (4 / 3) <= height * 0.42 + 1);
  }
  assert.equal(new Set(places.map((p) => `${p.x},${p.y}`)).size, 4);
  // A tall photo on a short banner is sized by the page's height.
  const tall = collage.placeNewPhoto('banner', 3 / 4, 0);
  assert.ok(tall.width / (3 / 4) <= collage.pageHeight('banner') * 0.42 + 1);
});

test('angles stay between -180 and 180 and click to straight and 45°', () => {
  assert.equal(collage.normaliseAngle(190), -170);
  assert.equal(collage.normaliseAngle(-190), 170);
  assert.equal(collage.normaliseAngle(360), 0);
  assert.equal(collage.normaliseAngle(180), 180);
  assert.equal(collage.snapAngle(2.5), 0);
  assert.equal(collage.snapAngle(-43), -45);
  assert.equal(collage.snapAngle(10), 10);
  assert.equal(collage.snapAngle(178.5), 180);
  // With Shift, every 15°.
  assert.equal(collage.snapAngle(22, true), 15);
  assert.equal(collage.snapAngle(23, true), 30);
});

test('the box around a turned item grows to hold its corners', () => {
  const flat = collage.turnedHalfBox(200, 100, 0);
  assert.deepEqual(flat, { halfWidth: 100, halfHeight: 50 });
  const quarter = collage.turnedHalfBox(200, 100, 90);
  assert.ok(Math.abs(quarter.halfWidth - 50) < 1e-9 && Math.abs(quarter.halfHeight - 100) < 1e-9);
  const tilted = collage.turnedHalfBox(200, 100, 45);
  assert.ok(Math.abs(tilted.halfWidth - 150 / Math.SQRT2) < 1e-9);
});

test('a moved item snaps to the page centre, its edges and other items', () => {
  const box = { halfWidth: 100, halfHeight: 50 };
  const centre = collage.snapMove({ x: 503, y: 200, ...box }, 'square', [], 7);
  assert.equal(centre.x, 500);
  assert.deepEqual(centre.guides, [{ axis: 'x', at: 500 }]);

  const edge = collage.snapMove({ x: 104, y: 52, ...box }, 'square', [], 7);
  assert.equal(edge.x, 100);
  assert.equal(edge.y, 50);
  assert.deepEqual(edge.guides, [{ axis: 'x', at: 0 }, { axis: 'y', at: 0 }]);

  const other = collage.snapMove({ x: 300, y: 702, ...box }, 'square', [{ x: 700, y: 700 }], 7);
  assert.equal(other.y, 700);
  assert.deepEqual(other.guides, [{ axis: 'y', at: 700 }]);

  const free = collage.snapMove({ x: 320, y: 320, ...box }, 'square', [], 7);
  assert.deepEqual(free, { x: 320, y: 320, guides: [] });
});

test('a new page size keeps items across the page and their size', () => {
  const c = { ...collage.newCollage('a4l'), items: [{ id: 'a', kind: 'photo', x: 500, y: collage.pageHeight('a4l') / 2, width: 300, aspect: 1.5, angle: 10, opacity: 100 }] };
  const upright = collage.changeSize(c, 'a4p');
  assert.equal(upright.size, 'a4p');
  assert.ok(Math.abs(upright.items[0].y - collage.pageHeight('a4p') / 2) < 1e-9);
  assert.equal(upright.items[0].width, 300);
  assert.equal(upright.items[0].angle, 10);
  assert.equal(c.items[0].y, collage.pageHeight('a4l') / 2, 'the old collage is not changed');
  assert.equal(collage.changeSize(c, 'nope'), c);
});

test('items move forward, backward, to the front and to the back', () => {
  const items = ['a', 'b', 'c', 'd'].map((id) => ({ id }));
  const ids = (list) => list.map((i) => i.id).join('');
  assert.equal(ids(collage.reorder(items, 'b', 'forward')), 'acbd');
  assert.equal(ids(collage.reorder(items, 'b', 'backward')), 'bacd');
  assert.equal(ids(collage.reorder(items, 'a', 'backward')), 'abcd');
  assert.equal(ids(collage.reorder(items, 'b', 'front')), 'acdb');
  assert.equal(ids(collage.reorder(items, 'c', 'back')), 'cabd');
  assert.equal(ids(collage.reorder(items, 'zz', 'front')), 'abcd');
});

test('a duplicate gets a new id and sits a little lower and to the right', () => {
  const item = { id: 'a', kind: 'photo', photo: 'p1', x: 100, y: 100, width: 200, aspect: 1, angle: 12, opacity: 80 };
  assert.deepEqual(collage.duplicateItem(item, 'b'), { ...item, id: 'b', x: 124, y: 124 });
});

test('widths stay grabbable and within reason', () => {
  assert.equal(collage.clampWidth(3), 24);
  assert.equal(collage.clampWidth(99999), 4000);
  assert.equal(collage.clampWidth('x'), 24);
  assert.equal(collage.clampWidth(321.5), 321.5);
});

test('undo and redo walk through the snapshots; a new change drops the redo', () => {
  const history = collage.createHistory({ n: 0 });
  assert.equal(history.canUndo(), false);
  assert.equal(history.record({ n: 0 }), false, 'the same collage is not recorded twice');
  history.record({ n: 1 });
  history.record({ n: 2 });
  assert.deepEqual(history.undo(), { n: 1 });
  assert.deepEqual(history.undo(), { n: 0 });
  assert.deepEqual(history.undo(), { n: 0 }, 'nothing before the first');
  assert.deepEqual(history.redo(), { n: 1 });
  history.record({ n: 5 });
  assert.equal(history.canRedo(), false);
  assert.deepEqual(history.undo(), { n: 1 });

  const short = collage.createHistory({ n: 0 }, 3);
  for (let n = 1; n <= 5; n++) short.record({ n });
  short.undo(); short.undo(); short.undo();
  assert.deepEqual(short.current(), { n: 3 }, 'only the last three are kept');
});
