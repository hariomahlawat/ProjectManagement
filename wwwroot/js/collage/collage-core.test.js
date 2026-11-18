const test = require('node:test');
const assert = require('node:assert/strict');
const collage = require('./collage-core.js');
const print = require('./collage-print.js');

test('every page is 1,000 units wide and keeps its size shape', () => {
  assert.equal(collage.pageHeight('a4l'), 1000 * 2480 / 3508);
  assert.equal(collage.pageHeight('square'), 1000);
  assert.equal(collage.pageHeight('story'), 1000 * 3840 / 2160);
  // An unknown size falls back to A4 landscape instead of breaking the page.
  assert.equal(collage.pageHeight('nope'), collage.pageHeight('a4l'));
  // Paper sizes, cards and pocket cards included, print at 300 dots per inch.
  for (const key of Object.keys(collage.SIZES).filter((k) => collage.SIZES[k].mm)) {
    const size = collage.SIZES[key];
    assert.ok(Math.abs(size.width - size.mm[0] / 25.4 * 300) < 1, key);
    assert.ok(Math.abs(size.height - size.mm[1] / 25.4 * 300) < 1, key);
  }
});

test('a new collage is empty, white and versioned', () => {
  const c = collage.newCollage('slide');
  assert.deepEqual(c, { version: 1, size: 'slide', background: { type: 'color', value: '#ffffff' }, layout: null, items: [] });
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
  const item = { id: 'a', kind: 'photo', photo: 'p1', x: 100, y: 100, width: 200, aspect: 1, angle: 12, opacity: 80, locked: true, hidden: true };
  assert.deepEqual(collage.duplicateItem(item, 'b'), { ...item, id: 'b', x: 124, y: 124, locked: false, hidden: false });
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

test('a frame shows the largest part of the photo with its shape, zoomed and kept inside', () => {
  const close = (a, b) => Math.abs(a - b) < 1e-9;
  // A 4:3 photo in a square frame shows the middle three quarters of its width.
  const square = collage.cropRegion(4 / 3, 1, collage.FULL_CROP);
  assert.ok(close(square.width, 0.75) && close(square.height, 1) && close(square.x, 0.125) && square.y === 0);
  // In a wide 2:1 frame it shows the full width and part of the height.
  const wide = collage.cropRegion(4 / 3, 2, collage.FULL_CROP);
  assert.ok(close(wide.width, 1) && close(wide.height, 2 / 3));
  // Zoom 2 halves both sides; a centre too near the edge is pulled back inside.
  const zoomed = collage.cropRegion(1, 1, { zoom: 2, x: 0.95, y: 0.1 });
  assert.ok(close(zoomed.width, 0.5) && close(zoomed.x, 0.5) && close(zoomed.y, 0));
  // Zoom is kept between 1 and 4.
  assert.ok(close(collage.cropRegion(1, 1, { zoom: 9, x: 0.5, y: 0.5 }).width, 0.25));
  assert.ok(close(collage.cropRegion(1, 1, { zoom: 0.2, x: 0.5, y: 0.5 }).width, 1));
  assert.deepEqual(collage.clampCrop(1, 1, { zoom: 2, x: 0.95, y: 0.1 }), { zoom: 2, x: 0.75, y: 0.25 });
});

test('dragging a photo inside its frame shows the other side of it, never past its edge', () => {
  const item = { width: 200, aspect: 1, crop: { zoom: 2, x: 0.5, y: 0.5 } };
  // Dragging 100 units right (half the frame) moves the view a quarter of the photo left.
  const moved = collage.panCrop(1, item, 100, 0);
  assert.equal(moved.zoom, 2);
  assert.ok(Math.abs(moved.x - 0.25) < 1e-9 && moved.y === 0.5);
  // Further than the edge stops at the edge.
  assert.ok(Math.abs(collage.panCrop(1, item, 1000, -1000).x - 0.25) < 1e-9);
  assert.ok(Math.abs(collage.panCrop(1, item, 1000, -1000).y - 0.75) < 1e-9);
  // An unzoomed photo that already fills the frame cannot move.
  assert.deepEqual(collage.panCrop(1, { ...item, crop: { ...collage.FULL_CROP } }, 50, 50), { zoom: 1, x: 0.5, y: 0.5 });
});

test('every layout fills the page with frames the gap apart', () => {
  for (const [key, layout] of Object.entries(collage.LAYOUTS)) {
    // The cells cover the 12 × 12 grid exactly once.
    const area = layout.cells.reduce((sum, [, , w, h]) => sum + w * h, 0);
    assert.equal(area, 144, key);
    const frames = collage.layoutFrames('a4l', key, 12);
    assert.equal(frames.length, layout.cells.length, key);
    for (const f of frames) {
      const h = f.width / f.aspect;
      assert.ok(f.x - f.width / 2 >= 12 - 1e-9 && f.x + f.width / 2 <= 1000 - 12 + 1e-9, key);
      assert.ok(f.y - h / 2 >= 12 - 1e-9 && f.y + h / 2 <= collage.pageHeight('a4l') - 12 + 1e-9, key);
    }
  }
  // Two side by side with no gap meet in the middle and touch every edge.
  const [left, right] = collage.layoutFrames('square', 't2', 0);
  assert.deepEqual([left.x, left.width, right.x, left.aspect], [250, 500, 750, 0.5]);
  // The gap is kept between 0 and 60.
  assert.deepEqual(collage.layoutFrames('square', 't2', 999), collage.layoutFrames('square', 't2', 60));
  assert.deepEqual(collage.layoutFrames('square', 'nope', 10), []);
});

test('a layout takes the page photos, then spare ones, then leaves empty frames', () => {
  let n = 0;
  const newId = () => `n${++n}`;
  const page = { ...collage.newCollage('a4l'), items: [
    { ...collage.photoItem('a', 'p1', 1.5, { x: 1, y: 1, width: 100 }), angle: 20, crop: { zoom: 2, x: 0.3, y: 0.3 } },
    collage.photoItem('b', 'p2', 1.5, { x: 2, y: 2, width: 100 })
  ] };
  const { collage: laid, removed } = collage.applyLayout(page, 't4', 10, 8, ['p2', 'p3'], newId);
  assert.deepEqual(removed, []);
  assert.deepEqual(laid.layout, { key: 't4', gap: 10, radius: 8 });
  assert.deepEqual(laid.items.map((i) => i.kind), ['photo', 'photo', 'photo', 'slot']);
  assert.deepEqual(laid.items.map((i) => i.photo), ['p1', 'p2', 'p3', undefined], 'a photo already on the page is not used twice');
  assert.equal(laid.items[0].id, 'a', 'photos keep their ids');
  assert.equal(laid.items[0].angle, 0);
  assert.deepEqual(laid.items[0].crop, collage.FULL_CROP);
  assert.ok(laid.items.every((i) => i.radius === 8));

  // A smaller layout sends the extra photos back to Photos.
  const two = collage.applyLayout(laid, 't2', 10, 0, [], newId);
  assert.deepEqual(two.removed, ['n1']);
  assert.deepEqual(two.collage.items.map((i) => i.id), ['a', 'b']);
});

test('an empty frame filled with a photo keeps its place, size and angle', () => {
  const slot = { ...collage.slotItem('s', { x: 10, y: 20, width: 300, aspect: 2 }), angle: 5, radius: 6 };
  const filled = collage.fillSlot(slot, 'p9');
  assert.deepEqual(filled, { ...slot, kind: 'photo', photo: 'p9', crop: { zoom: 1, x: 0.5, y: 0.5 }, ...collage.PLAIN_FINISH });
});

test('casual tilts frames both ways, straighten undoes it, shuffle moves photos along', () => {
  let n = 0;
  const { collage: laid } = collage.applyLayout(collage.newCollage('a4l'), 't4', 10, 0, ['p1', 'p2', 'p3'], () => `n${++n}`);
  const casual = collage.makeCasual(laid);
  assert.equal(casual.layout, null, 'the page is no longer a strict layout');
  const angles = casual.items.map((i) => i.angle);
  assert.ok(angles.some((a) => a > 0) && angles.some((a) => a < 0));
  assert.ok(angles.every((a) => Math.abs(a) >= 3 && Math.abs(a) <= 8));
  assert.deepEqual(collage.makeCasual(laid), casual, 'the same page gives the same result');
  assert.ok(casual.items.every((i, k) => i.width < laid.items[k].width));
  assert.ok(collage.straightenAll(casual).items.every((i) => i.angle === 0));

  const shuffled = collage.shufflePhotos(laid);
  assert.deepEqual(shuffled.items.map((i) => i.photo), ['p2', 'p3', 'p1', undefined]);
  const one = { ...laid, items: laid.items.slice(0, 1) };
  assert.equal(collage.shufflePhotos(one), one);
});

test('laying a page out again keeps every photo in its own frame', () => {
  let n = 0;
  const newId = () => `n${++n}`;
  // Two empty frames; only the right one is filled.
  const { collage: laid } = collage.applyLayout(collage.newCollage('a4l'), 't2', 10, 4, [], newId);
  assert.deepEqual(laid.items.map((i) => i.cell), [0, 1]);
  const right = laid.items[1];
  const filled = { ...laid, items: [laid.items[0], { ...collage.fillSlot(right, 'p1'), crop: { zoom: 2, x: 0.4, y: 0.5 } }] };
  // More space: the photo stays in the right-hand frame, the left stays empty, the crop is kept.
  const wider = collage.reflowLayout(filled, 40);
  assert.equal(wider.layout.gap, 40);
  assert.equal(wider.items[0].kind, 'slot');
  assert.equal(wider.items[1].kind, 'photo');
  assert.ok(wider.items[1].x > 500 && wider.items[0].x < 500);
  assert.deepEqual(wider.items[1].crop, { zoom: 2, x: 0.4, y: 0.5 });
  assert.deepEqual(collage.layoutFrames('a4l', 't2', 40)[1].x, wider.items[1].x);
  // Drawn in another order, frames still go back to their own cells.
  const reordered = { ...filled, items: [filled.items[1], filled.items[0]] };
  assert.ok(collage.reflowLayout(reordered, 12).items[0].x > 500);
  // A page with no layout is left as it is.
  const free = { ...filled, layout: null };
  assert.equal(collage.reflowLayout(free, 30), free);
});

test('a saved picture is print quality or 1,920 pixels long, and paper knows its dpi', () => {
  assert.deepEqual(collage.savePixels('a4l', 'print'), { width: 3508, height: 2480, dpi: 300 });
  assert.deepEqual(collage.savePixels('a4p', 'screen'), { width: 1357, height: 1920, dpi: 164 });
  assert.deepEqual(collage.savePixels('slide', 'screen'), { width: 1920, height: 1080, dpi: null });
  assert.deepEqual(collage.savePixels('banner', 'print'), { width: 6000, height: 2000, dpi: null });
});

test('a picture too large for the browser is made smaller, keeping its shape', () => {
  assert.deepEqual(collage.fitArea(3508, 4961, 0), { width: 3508, height: 4961, scaled: false });
  assert.deepEqual(collage.fitArea(1000, 500, 600000), { width: 1000, height: 500, scaled: false });
  const fitted = collage.fitArea(3508, 4961, 16777216);
  assert.equal(fitted.scaled, true);
  assert.ok(fitted.width * fitted.height <= 16777216);
  assert.ok(Math.abs(fitted.width / fitted.height - 3508 / 4961) < 0.001);
  // A3 at 300 dpi becomes about 295 dpi on Safari's limit.
  assert.equal(collage.paperDpi('a3p', fitted.width), 295);
});

test('the PDF page is the true paper size, and other sizes print at 300 dpi', () => {
  const a4 = collage.pdfPage('a4p');
  assert.ok(Math.abs(a4.width - 595.28) < 0.01 && Math.abs(a4.height - 841.89) < 0.01);
  assert.deepEqual(collage.pdfPage('slide'), { width: 921.6, height: 518.4 });
});

test('an original is read only as large as its frame needs, never larger than it is', () => {
  const photo = { width: 4000, height: 3000 };
  // A 4:3 frame 1,000 pixels wide showing the whole photo needs 1,000 pixels across.
  assert.deepEqual(collage.decodeSize(photo, 4 / 3, collage.FULL_CROP, 1000), { width: 1000, height: 750 });
  // Zoomed 2× it needs twice as many, so the part shown keeps its detail.
  assert.deepEqual(collage.decodeSize(photo, 4 / 3, { zoom: 2, x: 0.5, y: 0.5 }, 1000), { width: 2000, height: 1500 });
  // A square frame shows three quarters of the width, so it needs a third more.
  assert.deepEqual(collage.decodeSize(photo, 1, collage.FULL_CROP, 750), { width: 1000, height: 750 });
  // Never more than the original.
  assert.deepEqual(collage.decodeSize(photo, 4 / 3, collage.FULL_CROP, 9000), { width: 4000, height: 3000 });
});

test('empty frames are left out of the saved picture', () => {
  const c = { ...collage.newCollage('a4l'), items: [
    collage.photoItem('a', 'p1', 1, { x: 1, y: 1, width: 10 }),
    collage.slotItem('s', { x: 1, y: 1, width: 10, aspect: 1 })
  ] };
  assert.deepEqual(collage.itemsToSave(c).map((i) => i.id), ['a']);
});

test('file names lose characters a computer refuses and fall back to Collage', () => {
  assert.equal(collage.fileStem('Farewell: Col Sharma / 2026'), 'Farewell Col Sharma 2026');
  assert.equal(collage.fileStem('  ..picnic..  '), 'picnic');
  assert.equal(collage.fileStem('a\u0007b'), 'a b');
  assert.equal(collage.fileStem('???'), 'Collage');
  assert.equal(collage.fileStem('CON'), 'Collage');
  assert.equal(collage.fileStem(''), 'Collage');
  assert.equal(collage.fileStem('शुभकामनाएँ'), 'शुभकामनाएँ');
  assert.equal(collage.fileStem('x'.repeat(200)).length, 80);
});

test('a draft read back keeps what is usable and drops what is not', () => {
  const good = { ...collage.photoItem('a', 'p1', 1.5, { x: 100, y: 100, width: 200 }), angle: 30, crop: { zoom: 2, x: 0.3, y: 0.6 } };
  const raw = {
    version: 1,
    size: 'a4p',
    background: { type: 'color', value: '#fafafa' },
    layout: { key: 't4', gap: 20, radius: 6 },
    items: [
      good,
      { ...good, id: 'b', photo: 'gone' },
      { id: 'c', kind: 'sticker', x: 1, y: 1, width: 10, aspect: 1 },
      { ...good, id: 'd', width: 'wide' },
      { ...good, id: 'e', angle: 400, opacity: 0, radius: 999, crop: { zoom: 99 } },
      collage.slotItem('s', { x: 5, y: 5, width: 50, aspect: 2 }),
      null
    ]
  };
  const read = collage.readDraftModel(raw, ['p1']);
  assert.equal(read.size, 'a4p');
  assert.deepEqual(read.layout, { key: 't4', gap: 20, radius: 6 });
  assert.deepEqual(read.background, { type: 'color', value: '#fafafa' });
  assert.deepEqual(read.items.map((i) => i.id), ['a', 'e', 's']);
  assert.deepEqual(read.items[0].crop, { zoom: 2, x: 0.3, y: 0.6 });
  const e = read.items[1];
  assert.deepEqual([e.angle, e.opacity, e.radius, e.crop.zoom], [40, 10, 60, 4]);

  // Not a draft, or made by a newer PRISM: nothing is opened.
  assert.equal(collage.readDraftModel(null, []), null);
  assert.equal(collage.readDraftModel({ items: 'x' }, []), null);
  assert.equal(collage.readDraftModel({ version: 99, items: [] }, []), null);
  // Unknown size, layout and background fall back safely.
  const odd = collage.readDraftModel({ size: 'huge', layout: { key: 'zz' }, background: { type: 'color', value: 'red' }, items: [] }, []);
  assert.deepEqual([odd.size, odd.layout, odd.background.value, odd.version], ['a4l', null, '#ffffff', 1]);
});

test('deleted drafts hide at once and are removed for good after the undo time', () => {
  const now = 1_000_000;
  const drafts = [
    { id: 'old', saved: 10 },
    { id: 'new', saved: 50 },
    { id: 'just-deleted', saved: 70, deletedAt: now - 2000 },
    { id: 'long-deleted', saved: 60, deletedAt: now - collage.DRAFT_UNDO_MS - 1 }
  ];
  const { shown, purge } = collage.sortDrafts(drafts, now);
  assert.deepEqual(shown.map((d) => d.id), ['new', 'old']);
  assert.deepEqual(purge, ['long-deleted']);
});

test('a new draft past the limit names the oldest one to delete', () => {
  const few = [{ id: 'a', saved: 1 }];
  assert.equal(collage.draftToMakeRoom(few), null);
  const full = Array.from({ length: collage.MAX_DRAFTS }, (_, i) => ({ id: `d${i}`, saved: 100 - i }));
  assert.equal(collage.draftToMakeRoom(full).id, `d${collage.MAX_DRAFTS - 1}`);
});

test('a draft says when it was last saved in plain words', () => {
  const now = Date.UTC(2026, 10, 15, 12, 0, 0);
  assert.equal(collage.savedAgo(now - 10_000, now), 'just now');
  assert.equal(collage.savedAgo(now - 5 * 60_000, now), '5 min ago');
  assert.equal(collage.savedAgo(now - 3 * 3_600_000, now), '3 h ago');
  assert.equal(collage.savedAgo(now - 30 * 3_600_000, now), 'yesterday');
  assert.equal(collage.savedAgo(Date.UTC(2026, 9, 2, 12), now), '2 Oct 2026');
});

test('a frame leaves a picture area inside it, and a polaroid a deep bottom for its caption', () => {
  const plain = collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 200 });
  assert.deepEqual(collage.framePads(plain), { left: 0, right: 0, top: 0, bottom: 0 });
  assert.deepEqual(collage.innerBox(plain), { x: 0, y: 0, width: 200, height: 200 });
  const polaroid = { ...plain, frame: 'polaroid' };
  assert.deepEqual(collage.framePads(polaroid), { left: 10, right: 10, top: 10, bottom: 40 });
  assert.deepEqual(collage.innerBox(polaroid), { x: 10, y: 10, width: 180, height: 150 });
  // A frame needs the square shape: a heart has none.
  assert.equal(collage.isFramed({ ...polaroid, shape: 'heart' }), false);
  assert.deepEqual(collage.framePads({ ...polaroid, shape: 'heart' }), { left: 0, right: 0, top: 0, bottom: 0 });
  // The whole item's shape for a 4:3 picture in a polaroid 200 wide: 180 × 135 inside, plus 50.
  assert.ok(Math.abs(collage.outerAspectFor(polaroid, 4 / 3) - 200 / 185) < 1e-9);
  const fitted = { ...polaroid, aspect: collage.outerAspectFor(polaroid, 4 / 3) };
  assert.ok(Math.abs(collage.innerAspect(fitted) - 4 / 3) < 1e-9);
});

test('moving a framed photo inside its frame measures the picture area, not the frame', () => {
  const framed = { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 200 }), frame: 'polaroid', crop: { zoom: 2, x: 0.5, y: 0.5 } };
  framed.aspect = collage.outerAspectFor(framed, 1);
  // 90 units is half of the 180-unit picture area, so the view moves a quarter of the photo.
  const moved = collage.panCrop(1, framed, 90, 0);
  assert.ok(Math.abs(moved.x - 0.25) < 1e-9);
});

test('a look and the sliders become one filter, and plain photos have none', () => {
  const plain = collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 100 });
  assert.equal(collage.lookFilter(plain), '');
  assert.equal(collage.lookFilter({ ...plain, look: 'bw' }), 'grayscale(1) contrast(1.08)');
  assert.equal(collage.lookFilter({ ...plain, bright: 120, contrast: 90 }), 'brightness(1.2) contrast(0.9)');
  assert.equal(collage.lookFilter({ ...plain, warmth: 40 }), 'sepia(0.250) saturate(1.200)');
  assert.equal(collage.lookFilter({ ...plain, warmth: -30 }), 'hue-rotate(-10.5deg) saturate(1.100)');
  assert.equal(collage.lookFilter({ ...plain, look: 'unknown' }), '');
});

test('every shape is a closed path inside its box', () => {
  for (const shape of Object.keys(collage.SHAPES)) {
    const path = collage.shapePath(shape, 10, 20, 100, 80, 12);
    assert.ok(path.length >= 1, shape);
    const points = [];
    for (const step of path) {
      if (step[0] === 'M' || step[0] === 'L') points.push([step[1], step[2]]);
      if (step[0] === 'Q') points.push([step[3], step[4]]);
      if (step[0] === 'C') points.push([step[5], step[6]]);
      if (step[0] === 'E') points.push([step[1] - step[3], step[2] - step[4]], [step[1] + step[3], step[2] + step[4]]);
      if (step[0] === 'R') points.push([step[1], step[2]], [step[1] + step[3], step[2] + step[4]]);
    }
    for (const [x, y] of points) {
      assert.ok(x >= 10 - 1e-9 && x <= 110 + 1e-9 && y >= 20 - 1e-9 && y <= 100 + 1e-9, `${shape} ${x},${y}`);
    }
    const last = path[path.length - 1][0];
    assert.ok(['Z', 'E', 'R'].includes(last), shape);
  }
  // A star has ten points; a hexagon six; corners never round past half a side.
  assert.equal(collage.shapePath('star', 0, 0, 10, 10).filter((s) => s[0] !== 'Z').length, 10);
  assert.equal(collage.shapePath('hex', 0, 0, 10, 10).filter((s) => s[0] !== 'Z').length, 6);
  assert.deepEqual(collage.shapePath('rect', 0, 0, 10, 40, 99), [['R', 0, 0, 10, 40, 5]]);
});

test('a torn edge is jagged, stays inside the box and is the same every time', () => {
  const edge = collage.tornEdge(300, 200);
  assert.deepEqual(edge, collage.tornEdge(300, 200));
  assert.ok(edge.length > 80);
  assert.ok(edge.every(([x, y]) => x >= 0 && x <= 300 && y >= 0 && y <= 200));
  assert.ok(new Set(edge.slice(0, 23).map(([, y]) => y.toFixed(3))).size > 5, 'the top edge is jagged');
});

test('casual puts photos in polaroids, every third taped, keeping each photo shape', () => {
  let n = 0;
  const { collage: laid } = collage.applyLayout(collage.newCollage('a4l'), 't4', 10, 0, ['p1', 'p2', 'p3', 'p4'], () => `n${++n}`);
  const casual = collage.makeCasual(laid);
  assert.deepEqual(casual.items.map((i) => i.frame), ['polaroid', 'polaroid', 'tape', 'polaroid']);
  assert.ok(casual.items.every((i) => i.shadow === true));
  casual.items.forEach((item, k) => {
    assert.ok(Math.abs(collage.innerAspect(item) - laid.items[k].aspect) < 1e-9, 'the picture keeps its shape');
  });
});

test('a draft keeps each photo finish, within its limits', () => {
  const item = { ...collage.photoItem('a', 'p1', 1, { x: 1, y: 1, width: 100 }), frame: 'polaroid', caption: 'x'.repeat(60), shadow: true, shape: 'star', look: 'vivid', bright: 999, contrast: 10, warmth: -99, flip: true };
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [item, { ...item, id: 'b', frame: 'zz', shape: 'zz', look: 'zz', shadow: 'yes', flip: 1 }] }, ['p1']);
  const [a, b] = read.items;
  assert.deepEqual([a.frame, a.shape, a.look, a.caption.length, a.shadow, a.flip, a.bright, a.contrast, a.warmth], ['polaroid', 'star', 'vivid', 40, true, true, 150, 50, -50]);
  assert.deepEqual([b.frame, b.shape, b.look, b.shadow, b.flip], ['none', 'rect', 'none', false, false]);
});

test('a mirrored photo moves with the pointer inside its frame, not against it', () => {
  const item = { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 100 }), crop: { zoom: 2, x: 0.5, y: 0.5 } };
  const plain = collage.panCrop(1, item, 10, 0);
  const mirrored = collage.panCrop(1, { ...item, flip: true }, 10, 0);
  // Unmirrored, a drag right shows more of the left of the photo; mirrored, more of its right.
  assert.ok(plain.x < 0.5 && mirrored.x > 0.5);
  assert.ok(Math.abs((0.5 - plain.x) - (mirrored.x - 0.5)) < 1e-9);
  // Up and down are not mirrored.
  assert.equal(collage.panCrop(1, { ...item, flip: true }, 0, 10).y, collage.panCrop(1, item, 0, 10).y);
});

test('a framed photo squeezed flat keeps a usable picture area', () => {
  const polaroid = { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 400 }), frame: 'polaroid' };
  // 400 × 80: the polaroid's 20 + 80 units of edges would leave no photo.
  const squeezed = { ...polaroid, aspect: 5 };
  const kept = { ...squeezed, aspect: collage.limitFrameAspect(squeezed) };
  assert.ok(collage.innerBox(kept).height >= 40 - 1e-9);
  assert.ok(Math.abs(collage.innerBox(kept).height - 40) < 1e-9, 'a tenth of the width at least');
  // A photo without a frame, or a frame already tall enough, is left as it is.
  assert.equal(collage.limitFrameAspect({ ...squeezed, frame: 'none' }), 5);
  assert.equal(collage.limitFrameAspect({ ...polaroid, aspect: 1 }), 1);
});

// A stand-in for a canvas's measureText: every letter half as wide as the font is tall.
const halfEm = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) / 2;

test('a text item has every font shipped with PRISM and a plain look', () => {
  const item = collage.textItem('t', 'Farewell', { x: 500, y: 100 }, { font: 'script', size: 80 });
  assert.equal(item.kind, 'text');
  assert.equal(collage.fontOf(item), '700 80px "Dancing Script", Kalam, cursive');
  assert.equal(collage.fontOf({ ...item, italic: true, bold: false, font: 'nope' }), 'italic 400 80px Mulish, "Noto Sans Devanagari", sans-serif');
  assert.equal(collage.fontOf({ ...item, bold: false, font: 'engraved' }), '400 80px Cinzel, "Tiro Devanagari Hindi", serif');
  assert.equal(collage.fontOf({ ...item, bold: false, font: 'formal' }), '400 80px "Pinyon Script", Kalam, cursive');
  // Every English font falls back to a Devanagari one, so Hindi always shows.
  for (const [key, font] of Object.entries(collage.FONTS)) {
    assert.match(font.css, /Kalam|Devanagari|Baloo 2/, key);
  }
});

test('straight text keeps its lines and alignment inside its box', () => {
  const item = collage.textItem('t', 'AB\nABCD', { x: 0, y: 0 }, { size: 20, align: 'left' });
  const layout = collage.layoutText(item, halfEm);
  // The longest line is 4 letters of 10: 40 wide, plus a little room each side.
  assert.ok(Math.abs(layout.width - (40 + 2 * 1.6)) < 1e-9);
  // Each line is one run, so letters that join (Hindi) are drawn together.
  assert.deepEqual(layout.glyphs.map((g) => g.ch), ['AB', 'ABCD']);
  const [a, c] = layout.glyphs;
  assert.equal(a.left, c.left, 'left aligned: both lines start together');
  assert.ok(c.y > a.y, 'the second line is below the first');
  const centred = collage.layoutText({ ...item, align: 'center' }, halfEm);
  assert.ok(centred.glyphs[0].left > centred.glyphs[1].left, 'centred: the short line starts further in');
  assert.equal(centred.glyphs[0].x, centred.glyphs[1].x, 'centred lines share a middle');
  // Letter spacing widens the box; a label adds room round the words.
  assert.ok(collage.layoutText({ ...item, spacing: 20 }, halfEm).width > layout.width);
  const labelled = collage.layoutText({ ...item, label: '#ffd23f' }, halfEm);
  assert.ok(labelled.width > layout.width && labelled.label);
});

test('curved text bends up or down along an arc, the same both ways', () => {
  const item = collage.textItem('t', 'HAPPY DAYS', { x: 0, y: 0 }, { size: 40, curve: 50 });
  const up = collage.layoutText(item, halfEm);
  const down = collage.layoutText({ ...item, curve: -50 }, halfEm);
  assert.equal(up.glyphs.length, 10);
  const first = up.glyphs[0];
  const middle = up.glyphs[5];
  assert.ok(first.y > middle.y, 'bent up: the ends are lower than the middle');
  assert.ok(first.angle < 0 && up.glyphs[9].angle > 0, 'letters lean out along the arc');
  assert.ok(down.glyphs[0].y < down.glyphs[5].y, 'bent down: the ends are higher');
  assert.ok(Math.abs(up.width - down.width) < 1e-9 && Math.abs(up.height - down.height) < 1e-9);
  assert.ok(up.glyphs.every((g) => g.x >= 0 && g.x <= up.width && g.y >= 0 && g.y <= up.height));
  // A label is for straight text only.
  assert.equal(collage.layoutText({ ...item, label: '#ffffff' }, halfEm).label, null);
});

test('a Hindi syllable stays one letter cluster on a curve', () => {
  // शु and भ: the vowel sign stays with its consonant.
  assert.deepEqual(collage.graphemes('शुभ'), ['शु', 'भ']);
  const curved = collage.layoutText(collage.textItem('t', 'शुभ दिन', { x: 0, y: 0 }, { size: 40, curve: 40, font: 'hindiHand' }), halfEm);
  assert.deepEqual(curved.glyphs.map((g) => g.ch), ['शु', 'भ', ' ', 'दि', 'न']);
});

test('a draft keeps text, within its limits', () => {
  const text = collage.textItem('t', 'शुभकामनाएँ', { x: 10, y: 10 }, { font: 'hindiHand', size: 999, curve: -400, color: '#c62f3a', label: 'red', outlineColor: 'x', align: 'middle', bold: 'yes' });
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [text, { ...text, id: 'u', font: 'comic', text: 'x'.repeat(1200) }, { ...text, id: 'v', font: 'Comic Sans"' }] }, []);
  const [a, b, c] = read.items;
  assert.deepEqual([a.text, a.font, a.size, a.curve, a.color, a.label, a.outlineColor, a.align, a.bold], ['शुभकामनाएँ', 'hindiHand', 300, -100, '#c62f3a', '', '#ffffff', 'center', false]);
  // A font this PRISM does not ship keeps its name for a newer PRISM (it draws as the clean
  // font here); a name no PRISM writes falls back to clean (docs/ux/invitations-posters.md, PR 2).
  assert.deepEqual([b.font, b.text.length], ['comic', collage.TEXT_LIMITS.maxLength]);
  assert.equal(collage.fontOf(b), collage.fontOf({ ...b, font: 'clean' }));
  assert.equal(c.font, 'clean');
  assert.equal('crop' in a, false);
});

test('a background read back from a draft is a known one, or white', () => {
  const ok = (bg, ids = []) => collage.readBackground(bg, ids);
  assert.deepEqual(ok({ type: 'color', value: '#123abc' }), { type: 'color', value: '#123abc' });
  assert.deepEqual(ok({ type: 'gradient', key: 'sunset', extra: 1 }), { type: 'gradient', key: 'sunset' });
  assert.deepEqual(ok({ type: 'pattern', key: 'gingham' }), { type: 'pattern', key: 'gingham' });
  assert.deepEqual(ok({ type: 'texture', key: 'cork' }), { type: 'texture', key: 'cork' });
  assert.deepEqual(ok({ type: 'photo', photo: 'p1' }, ['p1']), { type: 'photo', photo: 'p1' });
  // Unknown names, a photo the draft does not hold, a bad colour: white.
  for (const bad of [{ type: 'gradient', key: 'zz' }, { type: 'photo', photo: 'p9' }, { type: 'color', value: 'url(x)' }, null, 'red']) {
    assert.deepEqual(ok(bad, ['p1']), { type: 'color', value: '#ffffff' });
  }
  const read = collage.readDraftModel({ version: 1, size: 'a4l', background: { type: 'texture', key: 'wood' }, items: [] }, []);
  assert.deepEqual(read.background, { type: 'texture', key: 'wood' });
});

test('backgrounds compare by their choice, and the grain is the same every time', () => {
  assert.ok(collage.sameBackground({ type: 'pattern', key: 'dots' }, { type: 'pattern', key: 'dots' }));
  assert.ok(!collage.sameBackground({ type: 'pattern', key: 'dots' }, { type: 'texture', key: 'dots' }));
  assert.ok(!collage.sameBackground({ type: 'color', value: '#ffffff' }, { type: 'color', value: '#000000' }));
  const a = [1, 2, 3].map(collage.steadyRandom);
  assert.deepEqual(a, [1, 2, 3].map(collage.steadyRandom));
  assert.ok(a.every((v) => v >= 0 && v < 1));
  assert.notEqual(a[0], a[1]);
});

test('lining up puts the turned box against an edge or on a centre line, one way only', () => {
  const item = { id: 'a', kind: 'photo', x: 300, y: 300, width: 200, aspect: 2, angle: 0 };
  const height = collage.pageHeight('a4l');
  assert.deepEqual(collage.alignItem(item, 'a4l', 'left'), { x: 100, y: 300 });
  assert.deepEqual(collage.alignItem(item, 'a4l', 'right'), { x: 900, y: 300 });
  assert.deepEqual(collage.alignItem(item, 'a4l', 'centre'), { x: 500, y: 300 });
  assert.deepEqual(collage.alignItem(item, 'a4l', 'top'), { x: 300, y: 50 });
  assert.deepEqual(collage.alignItem(item, 'a4l', 'middle'), { x: 300, y: height / 2 });
  assert.deepEqual(collage.alignItem(item, 'a4l', 'bottom'), { x: 300, y: height - 50 });
  // A turned item lines up by the box round its corners.
  const turned = collage.alignItem({ ...item, angle: 90 }, 'a4l', 'left');
  assert.ok(Math.abs(turned.x - 50) < 1e-9);
  assert.deepEqual(collage.alignItem(item, 'a4l', 'nowhere'), { x: 300, y: 300 });
});

test('hidden items are not saved, and drafts keep hidden and locked', () => {
  const shown = collage.photoItem('a', 'p1', 1, { x: 1, y: 1, width: 10 });
  const hidden = { ...shown, id: 'b', hidden: true };
  assert.deepEqual(collage.itemsToSave({ items: [shown, hidden] }).map((i) => i.id), ['a']);
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [{ ...shown, locked: true }, { ...hidden, locked: 'yes' }] }, ['p1']);
  assert.deepEqual(read.items.map((i) => [i.hidden, i.locked]), [[false, true], [true, false]]);
});

test('the Layers list names text by its first words', () => {
  assert.equal(collage.layerName({ kind: 'text', text: 'Happy Birthday\nPriya' }), 'Happy Birthday');
  assert.equal(collage.layerName({ kind: 'text', text: 'x'.repeat(40) }), `${'x'.repeat(27)}…`);
  assert.equal(collage.layerName({ kind: 'text', text: '  ' }), 'Text');
  assert.equal(collage.layerName({ kind: 'slot' }), 'Empty frame');
});

test('casual, straighten and shuffle leave locked items as they are', () => {
  const a = { ...collage.photoItem('a', 'p1', 1, { x: 100, y: 100, width: 200 }), angle: 7, locked: true };
  const b = collage.photoItem('b', 'p2', 1, { x: 400, y: 100, width: 200 });
  const c = collage.photoItem('c', 'p3', 1, { x: 700, y: 100, width: 200 });
  const page = { version: 1, size: 'a4l', layout: null, items: [a, b, c] };
  assert.deepEqual(collage.makeCasual(page).items[0], a);
  assert.notEqual(collage.makeCasual(page).items[1].angle, 0);
  assert.equal(collage.straightenAll(page).items[0].angle, 7);
  const shuffled = collage.shufflePhotos(page).items;
  assert.deepEqual(shuffled.map((i) => i.photo), ['p1', 'p3', 'p2']);
});

test('changing the layout or its spacing leaves locked frames where they are', () => {
  let n = 0;
  const id = () => `n${n++}`;
  const start = collage.applyLayout(collage.newCollage('a4l'), 't2', 12, 0, ['p1', 'p2'], id).collage;
  const locked = { ...start.items[0], locked: true, angle: 5 };
  const page = { ...start, items: [locked, start.items[1]] };
  const spaced = collage.reflowLayout(page, 40);
  assert.deepEqual(spaced.items[0], locked);
  assert.notDeepEqual(spaced.items[1], page.items[1]);
  const relaid = collage.applyLayout(page, 't4', 12, 0, [], id).collage;
  assert.deepEqual(relaid.items.find((i) => i.id === locked.id), locked);
  assert.equal(relaid.items.filter((i) => i.kind === 'photo' && !i.locked).length, 1);
});

test('a new page size leaves locked items where they are', () => {
  const page = { ...collage.newCollage('a4l'), items: [
    { id: 'a', kind: 'text', x: 100, y: 200, width: 100, aspect: 2, angle: 0, locked: true },
    { id: 'b', kind: 'text', x: 100, y: 200, width: 100, aspect: 2, angle: 0 }
  ] };
  const upright = collage.changeSize(page, 'a4p');
  assert.equal(upright.items[0].y, 200);
  assert.notEqual(upright.items[1].y, 200);
});

test('drafts keep crests and logos and drop malformed sticker names', () => {
  const crest = collage.stickerItem('a', 'unit:5', { x: 10, y: 10, width: 160, aspect: 1.5 });
  const bad = { ...crest, id: 'b', sticker: 'unit:../5' };
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [crest, bad] }, []);
  assert.deepEqual(read.items.map((item) => item.sticker), ['unit:5']);
});

test('a picture with more than 250 million pixels is refused by name', () => {
  assert.ok(collage.PHOTO_LIMITS.maxPixels >= 200 * 1000 * 1000);
  assert.equal(collage.refusedMessage([{ name: 'huge.png', reason: 'too-many-pixels' }]), '“huge.png” has more than 250 million pixels.');
});

test('a photo goes into the empty frame closest to its shape', () => {
  const tall = { id: 't', kind: 'slot', aspect: 0.75 };
  const wide = { id: 'w', kind: 'slot', aspect: 1.5 };
  const square = { id: 's', kind: 'slot', aspect: 1 };
  assert.equal(collage.bestSlotFor([tall, wide, square], 4 / 3).id, 'w');
  assert.equal(collage.bestSlotFor([tall, wide, square], 3 / 4).id, 't');
  assert.equal(collage.bestSlotFor([tall, wide, square], 1).id, 's');
  assert.equal(collage.bestSlotFor([{ ...wide, locked: true }], 1.5), null);
  assert.equal(collage.bestSlotFor([], 1), null);
});

test('a collage nobody named is called by its first words or when it was started', () => {
  assert.equal(collage.autoTitle([{ kind: 'text', text: '  \n Farewell Col Rao\nWith love' }], 0), 'Farewell Col Rao');
  assert.equal(collage.autoTitle([{ kind: 'text', text: 'x'.repeat(50) }], 0), `${'x'.repeat(39)}…`);
  const started = new Date(2026, 10, 15, 14, 5).getTime();
  assert.equal(collage.autoTitle([{ kind: 'photo' }], started), 'Collage, 15 Nov 14:05');
});

test('a draft is read back with only plain, unique ids and sensible shapes and crops', () => {
  const base = collage.photoItem('a', 'p1', 1, { x: 1, y: 1, width: 100 });
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [
    base,
    { ...base, id: 'a' },
    { ...base, id: 'x"><img src=x onerror=alert(1)>' },
    { ...base, id: 'b', aspect: Infinity },
    { ...base, id: 'c', aspect: '2' },
    { ...base, id: 'd', aspect: 1e9, crop: { zoom: 2, x: 99, y: -5 }, cell: 'x' }
  ] }, ['p1']);
  assert.deepEqual(read.items.map((item) => item.id), ['a', 'd']);
  const d = read.items[1];
  assert.equal(d.aspect, 50);
  assert.deepEqual([d.crop.x, d.crop.y], [1, 0]);
  assert.equal('cell' in d, false);
});

test('photos in use: the frames and the background; Undo knows which photos its steps show', () => {
  const PC = require('./collage-core.js');
  let model = PC.newCollage(PC.DEFAULT_SIZE);
  model.items.push({ id: 'i1', kind: 'photo', photo: 'p1' }, { id: 'i2', kind: 'slot' });
  model.background = { type: 'photo', photo: 'p2' };
  assert.deepEqual([...PC.photosInUse(model)].sort(), ['p1', 'p2']);
  const history = PC.createHistory(model);
  model = { ...model, items: [], background: { type: 'color', value: '#ffffff' } };
  history.record(model);
  assert.equal(PC.photosInUse(model).size, 0);
  assert.equal(history.mentions('p1'), true, 'Undo can bring back the step that shows it');
  assert.equal(history.mentions('p3'), false);
  assert.equal(history.mentions('p'), false, 'a different photo whose id starts the same');
});

test('a file that could not be read is refused as damaged, not as "not a photo"', () => {
  const PC = require('./collage-core.js');
  const { accepted, refused } = PC.checkPhotos([
    { name: 'cloud.jpg', size: 10, type: null, unreadable: true },
    { name: 'ok.jpg', size: 10, type: 'image/jpeg' }
  ], 0);
  assert.equal(accepted.length, 1);
  assert.deepEqual(refused, [{ name: 'cloud.jpg', reason: 'unreadable' }]);
  assert.match(PC.refusedMessage(refused), /could not be opened/);
});

test('the page canvas stays under the iPhone canvas limit at any zoom', () => {
  const PC = require('./collage-core.js');
  // A phone at zoom 4 on an A4 portrait page: about 1404 × 1972 CSS pixels on a 3× screen.
  const ratio = PC.canvasPixelRatio(1404, 1972, 3);
  assert.ok(1404 * ratio * 1972 * ratio <= PC.CANVAS_LIMITS.maxPixels);
  assert.ok(ratio < 3 && ratio >= 1);
  // Small enough: the screen's own ratio is kept.
  assert.equal(PC.canvasPixelRatio(800, 600, 2), 2);
  assert.equal(PC.canvasPixelRatio(800, 600, undefined), 1);
  // Never below half a pixel per CSS pixel, however large.
  assert.equal(PC.canvasPixelRatio(20000, 20000, 2), 0.5);
});

test('a photo taken out of a layout leaves an empty frame in its place', () => {
  const PC = require('./collage-core.js');
  const photo = { ...PC.photoItem('i1', 'p1', 1.5, { x: 200, y: 300, width: 400 }), angle: 5, radius: 12, frame: 'polaroid' };
  const slot = PC.emptyFrame(photo);
  assert.deepEqual(slot, { id: 'i1', kind: 'slot', x: 200, y: 300, width: 400, aspect: 1.5, angle: 5, opacity: 100, radius: 12 });
});

test('an emptied frame keeps its cell, so a change of space still moves it with the layout', () => {
  const PC = require('./collage-core.js');
  let model = PC.applyLayout(PC.newCollage('a4l'), Object.keys(PC.LAYOUTS)[1], PC.LAYOUT_LIMITS.defaultGap, 0, ['p1', 'p2', 'p3', 'p4'], (() => { let n = 0; return () => `i${n += 1}`; })()).collage;
  const photo = model.items.find((i) => i.kind === 'photo');
  assert.ok(Number.isInteger(photo.cell));
  model = { ...model, items: model.items.map((i) => (i === photo ? PC.emptyFrame(i) : i)) };
  const emptied = model.items.find((i) => i.id === photo.id);
  assert.equal(emptied.cell, photo.cell);
  const wider = PC.reflowLayout(model, PC.LAYOUT_LIMITS.defaultGap + 20);
  const moved = wider.items.find((i) => i.id === photo.id);
  assert.notDeepEqual([moved.x, moved.width], [emptied.x, emptied.width], 'it moved with the layout');
});

test('a new item goes where it covers nothing yet', () => {
  const PC = require('./collage-core.js');
  const heading = { id: 't1', kind: 'text', x: 500, y: 500, width: 600, aspect: 6, angle: 0 };
  const spot = PC.freeSpot([heading], 'square', 250, 40);
  const overlapsX = Math.abs(spot.x - heading.x) < 250 + 300;
  const overlapsY = Math.abs(spot.y - heading.y) < 40 + 50;
  assert.ok(!(overlapsX && overlapsY), 'clear of the heading');
  assert.deepEqual(PC.freeSpot([], 'square', 100, 50), { x: 500, y: 500 }, 'the centre when the page is empty');
  // A hidden item does not count.
  assert.deepEqual(PC.freeSpot([{ ...heading, hidden: true }], 'square', 250, 40), { x: 500, y: 500 });
});

test('words that only show where to type never name a collage', () => {
  const PC = require('./collage-core.js');
  const started = new Date(2026, 10, 16, 9, 5).getTime();
  assert.equal(PC.autoTitle([{ kind: 'text', text: PC.TEXT_PLACEHOLDERS.heading }], started), 'Collage, 16 Nov 09:05');
  assert.equal(PC.autoTitle([{ kind: 'text', text: 'यहाँ लिखें' }, { kind: 'text', text: 'Farewell Col Rao' }], started), 'Farewell Col Rao');
});

test('"Make it casual" keeps the frames and shapes chosen by hand', () => {
  const PC = require('./collage-core.js');
  const plain = PC.photoItem('a', 'p1', 1.5, { x: 200, y: 200, width: 300 });
  const heart = { ...PC.photoItem('b', 'p2', 1, { x: 600, y: 200, width: 300 }), shape: 'heart' };
  const torn = { ...PC.photoItem('c', 'p3', 1, { x: 400, y: 600, width: 300 }), frame: 'torn' };
  const casual = PC.makeCasual({ ...PC.newCollage('square'), items: [plain, heart, torn] });
  const [a, b, c] = casual.items;
  assert.equal(a.frame, 'polaroid');
  assert.equal(b.shape, 'heart');
  assert.equal(b.frame, heart.frame);
  assert.equal(c.frame, 'torn');
  assert.notEqual(b.angle, 0, 'still tilted');
  assert.equal(b.aspect, heart.aspect);
});

test('a caption strip takes its room from the picture, inside any frame', () => {
  const PC = require('./collage-core.js');
  const plain = PC.photoItem('a', 'p1', 1, { x: 0, y: 0, width: 500 });
  assert.equal(PC.captionStripHeight(plain), 0);
  const captioned = { ...plain, caption: '  Annual day  ' };
  assert.equal(PC.captionStripHeight(captioned), 500 * PC.CAPTION_STRIP);
  assert.equal(PC.innerBox(captioned).height, 500 - 500 * PC.CAPTION_STRIP);
  const bordered = { ...captioned, frame: 'border' };
  assert.equal(PC.framePads(bordered).bottom, 500 * PC.FRAMES.border.bottom + 500 * PC.CAPTION_STRIP);
  assert.equal(PC.hasCaptionStrip({ ...plain, caption: '   ' }), false, 'spaces are no caption');
  assert.equal(PC.hasCaptionStrip({ ...captioned, shape: 'heart' }), true, 'a shaped photo has one too');
});

test('a caption strip never takes a flat photo\'s picture, and fitting a frame keeps the photo whole', () => {
  const PC = require('./collage-core.js');
  // A 10:1 photo, 500 wide and 50 high: the strip keeps to 40% of the height.
  const wide = { ...PC.photoItem('w', 'p1', 10, { x: 0, y: 0, width: 500 }), caption: 'Long caption' };
  assert.ok(PC.captionStripHeight(wide) <= 50 * 0.4 + 1e-9);
  assert.ok(PC.innerBox(wide).height >= 50 * 0.6 - 1e-9);
  // Fitting the frame to a photo with a caption gives a picture area of the photo's own shape.
  for (const aspect of [1, 1.5, 0.7, 10]) {
    const item = { ...PC.photoItem('s', 'p1', 1, { x: 0, y: 0, width: 500 }), caption: 'Annual day' };
    item.aspect = PC.outerAspectFor(item, aspect);
    const inner = PC.innerBox(item);
    assert.ok(Math.abs(inner.width / inner.height - aspect) < 1e-6, `picture keeps aspect ${aspect}`);
  }
});

test('sliders say their unit', () => {
  const PC = require('./collage-core.js');
  assert.equal(PC.sliderText('width', 482), '48% of the page');
  assert.equal(PC.sliderText('radius', 12), '1.2% of the page');
  assert.equal(PC.sliderText('gap', 0), 'None');
  assert.equal(PC.sliderText('angle', -14.6), '-15°');
  assert.equal(PC.sliderText('opacity', 80), '80%');
  assert.equal(PC.sliderText('zoom', 1.25), '1.3×');
  assert.equal(PC.sliderText('warmth', 12), '+12');
});

test('a file named as a photo that is not one is called damaged', () => {
  const PC = require('./collage-core.js');
  const { refused } = PC.checkPhotos([
    { name: 'IMG_0042.JPG', size: 10, type: null },
    { name: 'notes.txt', size: 10, type: null }
  ], 0);
  assert.deepEqual(refused.map((r) => r.reason), ['unreadable', 'not-photo']);
});

test('a lock stops moving, refilling and removing, and nothing else', () => {
  const item = { ...collage.photoItem('a', 'p1', 1.5, { x: 100, y: 100, width: 100 }), locked: true };
  for (const action of ['delete', 'adjust', 'replace', 'fill-slot', 'rotate-left', 'rotate-right', 'straighten', 'fit-photo']) {
    assert.equal(collage.lockBlocks(item, action), true, action);
    assert.equal(collage.lockBlocks({ ...item, locked: false }, action), false, action);
  }
  for (const action of ['lock', 'hide', 'duplicate', 'forward', 'back']) assert.equal(collage.lockBlocks(item, action), false, action);
  assert.equal(collage.lockBlocks(null, 'delete'), false);
});

test('a locked photo kept in place: the new layout leaves out the frames it covers and it stays on the page', () => {
  let n = 0;
  const newId = () => `n${++n}`;
  const height = collage.pageHeight('a4l');
  // A locked photo over the top right quarter, partly off the right edge.
  const locked = { ...collage.photoItem('L', 'p9', 1, { x: 900, y: 160, width: 300 }), locked: true };
  const page = { ...collage.newCollage('a4l'), items: [collage.photoItem('a', 'p1', 1.5, { x: 1, y: 1, width: 100 }), locked] };
  const { collage: laid } = collage.applyLayout(page, 't4', 10, 0, [], newId);
  const kept = laid.items.find((i) => i.id === 'L');
  assert.equal(kept.locked, true);
  assert.equal(kept.x, 850, 'moved just inside the page');
  assert.equal(kept.y, 160, 'not moved where it was already inside');
  // The top right frame (cell 1) is left out; the others keep their own cells.
  const cells = laid.items.filter((i) => i.id !== 'L').map((i) => i.cell).sort();
  assert.deepEqual(cells, [0, 2, 3]);
  assert.deepEqual(laid.items.filter((i) => i.id !== 'L').map((i) => i.kind).sort(), ['photo', 'slot', 'slot']);
  // A change of space puts each frame back in its own cell; the locked photo stays.
  const reflowed = collage.reflowLayout(laid, 30);
  assert.deepEqual(reflowed.items.find((i) => i.id === 'L'), kept);
  assert.equal(collage.lockedFrames(laid).length, 1);
});

test('a locked photo put into the new layout fills a frame like the others and stays locked', () => {
  let n = 0;
  const locked = { ...collage.photoItem('L', 'p9', 1, { x: 900, y: 200, width: 400 }), locked: true, angle: 12 };
  const page = { ...collage.newCollage('a4l'), items: [locked, collage.photoItem('a', 'p1', 1.5, { x: 1, y: 1, width: 100 })] };
  const { collage: laid } = collage.applyLayout(page, 't2', 10, 0, [], () => `n${++n}`, { locked: 'join' });
  assert.deepEqual(laid.items.map((i) => [i.id, i.cell]), [['L', 0], ['a', 1]]);
  assert.equal(laid.items[0].locked, true);
  assert.equal(laid.items[0].angle, 0);
});

test('a kept locked photo stays under the words above it', () => {
  let n = 0;
  const locked = { ...collage.photoItem('L', 'p9', 1, { x: 850, y: 160, width: 300 }), locked: true };
  const words = { id: 'T', kind: 'text', x: 850, y: 160, width: 200, aspect: 3, angle: 0 };
  const page = { ...collage.newCollage('a4l'), items: [collage.photoItem('a', 'p1', 1.5, { x: 1, y: 1, width: 100 }), locked, words] };
  const { collage: laid } = collage.applyLayout(page, 't4', 10, 0, [], () => `n${++n}`);
  const ids = laid.items.map((i) => i.id);
  assert.ok(ids.indexOf('L') < ids.indexOf('T'), ids.join(','));
});

test('a locked empty frame put into the new layout keeps its id and its lock', () => {
  let n = 0;
  const slot = { ...collage.slotItem('S', { x: 900, y: 200, width: 300, aspect: 1 }), locked: true };
  const page = { ...collage.newCollage('a4l'), items: [collage.photoItem('a', 'p1', 1.5, { x: 1, y: 1, width: 100 }), slot] };
  const { collage: laid } = collage.applyLayout(page, 't2', 10, 0, [], () => `n${++n}`, { locked: 'join' });
  assert.deepEqual(laid.items.map((i) => [i.id, i.kind, i.cell, Boolean(i.locked)]), [['a', 'photo', 0, false], ['S', 'slot', 1, true]]);
  // With more locked empty frames than frames left, the rest stay where they are.
  const two = { ...page, items: [...page.items, { ...slot, id: 'S2' }] };
  const one = collage.applyLayout(two, 't2', 10, 0, [], () => `m${++n}`, { locked: 'join' }).collage;
  assert.deepEqual(one.items.map((i) => [i.id, Boolean(i.locked)]), [['a', false], ['S', true], ['S2', true]]);
});

test('photos on a page with no layout go into a neat grid, each fully in view', () => {
  const items = [];
  for (let i = 0; i < 4; i += 1) {
    const place = collage.gridPlace(items, 'a4p', 4 / 3, 4);
    assert.ok(place, `photo ${i}`);
    items.push(collage.photoItem(`p${i}`, `f${i}`, 4 / 3, place));
  }
  // No two photos overlap, and every photo is on the page.
  const height = collage.pageHeight('a4p');
  for (const a of items) {
    assert.ok(a.x - a.width / 2 >= 0 && a.x + a.width / 2 <= 1000);
    assert.ok(a.y - collage.itemHeight(a) / 2 >= 0 && a.y + collage.itemHeight(a) / 2 <= height);
    for (const b of items) {
      if (a === b) continue;
      const w = Math.min(a.x + a.width / 2, b.x + b.width / 2) - Math.max(a.x - a.width / 2, b.x - b.width / 2);
      const h = Math.min(a.y + collage.itemHeight(a) / 2, b.y + collage.itemHeight(b) / 2) - Math.max(a.y - collage.itemHeight(a) / 2, b.y - collage.itemHeight(b) / 2);
      assert.ok(!(w > 0 && h > 0), `${a.id} and ${b.id} overlap`);
    }
  }
  // A full grid has no place left; a single photo is left to placeNewPhoto.
  assert.equal(collage.gridPlace(items, 'a4p', 4 / 3, 4), null);
  assert.equal(collage.gridPlace([], 'a4p', 4 / 3, 1), null);
  // Columns follow the page's shape: 6 on a landscape page are 3 across.
  assert.equal(new Set(collage.gridCells('a4l', 6).map((c) => Math.round(c.y))).size, 2);
});

test('a layout moved by hand or tilted is off its cells; laid out again it is not', () => {
  let n = 0;
  const laid = collage.applyLayout(collage.newCollage('a4l'), 't2', 10, 0, ['p1', 'p2'], () => `n${++n}`).collage;
  assert.equal(collage.offLayout(laid), false);
  const tilted = { ...laid, items: laid.items.map((i, k) => (k === 0 ? { ...i, angle: 4 } : i)) };
  assert.equal(collage.offLayout(tilted), true);
  assert.equal(collage.offLayout(collage.reflowLayout(tilted, 10)), false);
  // A locked frame moved by hand does not count: the space never moves it.
  const lockedMoved = { ...laid, items: laid.items.map((i, k) => (k === 0 ? { ...i, x: i.x + 50, locked: true } : i)) };
  assert.equal(collage.offLayout(lockedMoved), false);
});

test('long thin photos each take their own grid cell', () => {
  const items = [];
  for (let i = 0; i < 4; i += 1) {
    const place = collage.gridPlace(items, 'a4p', 16, 4);
    assert.ok(place, `photo ${i}`);
    items.push(collage.photoItem(`p${i}`, `f${i}`, 16, place));
  }
  assert.equal(new Set(items.map((i) => `${Math.round(i.x)},${Math.round(i.y)}`)).size, 4);
});

test('a grid photo always fits inside its cell, and a grid too fine is not used', () => {
  const items = [];
  let placed = 0;
  for (let i = 0; i < 60; i += 1) {
    const place = collage.gridPlace(items, 'a4l', 1 / 4, 60);
    if (!place) continue;
    placed += 1;
    const item = collage.photoItem(`p${i}`, `f${i}`, 1 / 4, place);
    for (const other of items) {
      const w = Math.min(item.x + item.width / 2, other.x + other.width / 2) - Math.max(item.x - item.width / 2, other.x - other.width / 2);
      const h = Math.min(item.y + collage.itemHeight(item) / 2, other.y + collage.itemHeight(other) / 2) - Math.max(item.y - collage.itemHeight(item) / 2, other.y - collage.itemHeight(other) / 2);
      assert.ok(!(w > 0 && h > 0), `${item.id} overlaps ${other.id}`);
    }
    items.push(item);
  }
  assert.ok(placed < 60, 'cells too small for the smallest photo are not used');
});

test('a layout frame given another shape (a Polaroid) is off its cell', () => {
  let n = 0;
  const laid = collage.applyLayout(collage.newCollage('a4l'), 't2', 10, 0, ['p1', 'p2'], () => `n${++n}`).collage;
  const reshaped = { ...laid, items: laid.items.map((i, k) => (k === 0 ? { ...i, aspect: i.aspect * 0.8 } : i)) };
  assert.equal(collage.offLayout(reshaped), true);
});

test('a kept locked photo a little wider than half the page leaves out only the frames it mostly covers', () => {
  let n = 0;
  const id = () => `n${++n}`;
  const start = collage.applyLayout(collage.newCollage('a4l'), 't3', collage.LAYOUT_LIMITS.defaultGap, 0, ['p1', 'p2', 'p3'], id).collage;
  const big = start.items.find((i) => i.cell === 0);
  const page = { ...start, items: start.items.map((i) => (i === big ? { ...i, locked: true } : i)) };
  assert.deepEqual(collage.framesLeftOut(page, 't4', collage.LAYOUT_LIMITS.defaultGap), { out: 2, of: 4 });
  const { collage: laid } = collage.applyLayout(page, 't4', collage.LAYOUT_LIMITS.defaultGap, 0, [], id);
  const cells = laid.items.filter((i) => i.id !== big.id).map((i) => i.cell).sort();
  assert.deepEqual(cells, [1, 3], 'the right-hand frames stay');
  assert.equal(laid.items.filter((i) => i.kind === 'photo' && !i.locked).length, 2, 'the other photos go into them');
});

const turnedBox = (i) => {
  const r = (i.angle || 0) * Math.PI / 180;
  const h = collage.itemHeight(i);
  const hw = (i.width * Math.abs(Math.cos(r)) + h * Math.abs(Math.sin(r))) / 2;
  const hh = (i.width * Math.abs(Math.sin(r)) + h * Math.abs(Math.cos(r))) / 2;
  return { l: i.x - hw, r: i.x + hw, t: i.y - hh, b: i.y + hh };
};

test('"Make it casual" keeps every print on the page and each inside its own frame\'s box', () => {
  for (const [size, key] of [['a4l', 't4'], ['a4p', 't3'], ['a4l', 't6']]) {
    let n = 0;
    const frames = collage.layoutFrames(size, key, 12).length;
    const laid = collage.applyLayout(collage.newCollage(size), key, 12, 0, Array.from({ length: frames }, (_, i) => `p${i}`), () => `n${++n}`).collage;
    const casual = collage.makeCasual(laid);
    const height = collage.pageHeight(size);
    casual.items.forEach((print, k) => {
      const b = turnedBox(print);
      assert.ok(b.l >= -30 && b.t >= -30 && b.r <= 1000 + 30 && b.b <= height + 30, `${size} ${key} print ${k} is off the page`);
      const before = turnedBox(laid.items[k]);
      // Inside the box its frame had, give or take the nudge.
      assert.ok(b.r - b.l <= before.r - before.l + 1 && b.b - b.t <= before.b - before.t + 1, `${size} ${key} print ${k} grew past its frame`);
    });
  }
});

test('a batch fills empty frames tall to tall and wide to wide', () => {
  const slots = [
    collage.slotItem('wide', { x: 200, y: 200, width: 400, aspect: 1.6 }),
    collage.slotItem('tall', { x: 700, y: 200, width: 200, aspect: 0.6 })
  ];
  const chosen = collage.assignToSlots(slots, [{ id: 'portrait', aspect: 0.75 }, { id: 'landscape', aspect: 1.5 }]);
  assert.equal(chosen.get('portrait'), 'tall');
  assert.equal(chosen.get('landscape'), 'wide');
  // More photos than frames: the worst match is left over.
  const three = collage.assignToSlots(slots, [{ id: 'a', aspect: 1.5 }, { id: 'b', aspect: 0.6 }, { id: 'c', aspect: 3 }]);
  assert.deepEqual([...three.keys()].sort(), ['a', 'b']);
});

test('words over a photo are told apart from words on the empty page', () => {
  const photo = collage.photoItem('p', 'f', 1.5, { x: 300, y: 300, width: 400 });
  const over = { id: 't', kind: 'text', x: 300, y: 300, width: 200, aspect: 4, angle: 0 };
  const away = { ...over, x: 850, y: 600 };
  assert.equal(collage.overPhoto([photo, over], over), true);
  assert.equal(collage.overPhoto([photo, away], away), false);
});

// docs/ux/invitations-posters.md, PR 2: a draft from a newer PRISM keeps what this one does not
// understand, so saving it here never deletes a card's shapes, back page or new logo.
test('a draft keeps the parts a newer PRISM made, and writes them back in their places', () => {
  const photo = collage.photoItem('a', 'p1', 1.5, { x: 100, y: 100, width: 200 });
  const shape = { id: 'b', kind: 'seal', seal: 'rect', x: 10, y: 10, width: 980, aspect: 0.7, stroke: '#a8842f', points: [[0, 0], [1, 1]] };
  const logo = { id: 'c', kind: 'sticker', sticker: 'seal:navy', x: 400, y: 40, width: 120, aspect: 0.59 };
  const words = { ...collage.textItem('d', 'Raising Day Dinner', { x: 500, y: 300 }), font: 'cinzel', field: 'occasion', maxWidth: 800 };
  const reverse = { background: { type: 'color', value: '#fbf7ee' }, items: [{ id: 'e', kind: 'photo', photo: 'p2', x: 0, y: 0, width: 1000, aspect: 1.4 }] };
  const raw = { version: 1, size: 'a4p', background: { type: 'color', value: '#ffffff' }, layout: null, items: [photo, shape, logo, words], reverse, design: 'classic' };

  const read = collage.readDraftModel(raw, ['p1', 'p2']);
  // Only what this PRISM draws is on the page; the rest is held aside, untouched.
  assert.deepEqual(read.items.map((i) => i.id), ['a', 'd']);
  assert.equal(collage.keptCount(read), 4);
  assert.deepEqual(read.kept.items, [{ at: 1, item: shape }, { at: 2, item: logo }]);
  assert.deepEqual(read.kept.fields, { reverse, design: 'classic' });
  // A newer font and new fields on a known item stay by name; the font draws as the clean one.
  assert.equal(read.items[1].font, 'cinzel');
  assert.equal(read.items[1].field, 'occasion');
  assert.equal(read.items[1].maxWidth, 800);
  assert.equal(collage.fontOf(read.items[1]), collage.fontOf({ ...read.items[1], font: 'clean' }));
  // A photo named only inside a kept part is still in use, so the draft keeps it.
  assert.deepEqual([...collage.photosInUse(read)].sort(), ['p1', 'p2']);

  // An edit here (a page size change copies the collage) keeps them, and the draft gets them back
  // in their places, exactly as they were.
  const edited = collage.changeSize(read, 'a4l');
  const written = collage.draftModel(edited);
  assert.equal(written.kept, undefined);
  assert.deepEqual(written.items.map((i) => i.id), ['a', 'b', 'c', 'd']);
  assert.deepEqual(written.items[1], shape);
  assert.deepEqual(written.items[2], logo);
  assert.deepEqual(written.reverse, reverse);
  assert.equal(written.design, 'classic');
  assert.equal(written.size, 'a4l');
  // Read again, written again: nothing is lost or doubled however often an older page saves.
  const again = collage.draftModel(collage.readDraftModel(JSON.parse(JSON.stringify(written)), ['p1', 'p2']));
  assert.deepEqual(again, written);

  // A collage with nothing kept is written as it is.
  const plain = collage.readDraftModel({ version: 1, size: 'a4l', items: [photo] }, ['p1']);
  assert.equal(plain.kept, undefined);
  assert.equal(collage.keptCount(plain), 0);
  assert.equal(collage.draftModel(plain), plain);
});

test('kept parts are only ones a PRISM could have written, within limits, and a newer version is still refused', () => {
  const base = { x: 1, y: 1, width: 10, aspect: 1 };
  const read = collage.readDraftModel({
    version: 1,
    size: 'a4l',
    items: [
      { ...base, id: 'a', kind: '<img src=x>' },
      { ...base, id: 'b', kind: 'Seal' },
      { ...base, id: 'bad id', kind: 'seal' },
      { ...base, id: 'c', kind: 'seal' },
      { ...base, id: 'c', kind: 'seal' },
      { ...base, id: 'd', kind: 'sticker', sticker: 'javascript:alert(1)' },
      { ...base, id: 'e', kind: 'sticker' }
    ],
    '<b>': 1,
    note: undefined
  }, []);
  assert.deepEqual(read.kept.items.map((k) => k.item.id), ['c']);
  assert.deepEqual(read.kept.fields, {});
  assert.deepEqual(read.items, []);

  const many = Array.from({ length: 600 }, (_, i) => ({ ...base, id: `s${i}`, kind: 'seal' }));
  assert.equal(collage.readDraftModel({ version: 1, items: many }, []).kept.items.length, 500);
  const huge = collage.readDraftModel({ version: 1, items: [], back: 'x'.repeat(2_100_000) }, []);
  assert.equal(huge.kept, undefined);

  assert.equal(collage.readDraftModel({ version: 2, items: [{ ...base, id: 'c', kind: 'seal' }] }, []), null);
});

test('a kept part keeps its place in the stacking order when items around it are deleted here', () => {
  const a = collage.photoItem('a', 'p1', 1.5, { x: 100, y: 100, width: 200 });
  const shape = { id: 'b', kind: 'seal', seal: 'rect', x: 10, y: 10, width: 980, aspect: 0.7 };
  const c = collage.textItem('c', 'On top of the shape', { x: 500, y: 300 });
  const d = collage.textItem('d', 'Also on top', { x: 500, y: 400 });
  const read = collage.readDraftModel({ version: 1, size: 'a4p', items: [a, shape, c, d] }, ['p1']);
  const ids = (collage) => collage.items.map((item) => item.id);
  const without = (model, id) => ({ ...model, items: model.items.filter((item) => item.id !== id) });

  // The item under it deleted: still under the words that were above it.
  assert.deepEqual(ids(collage.draftModel(without(read, 'a'))), ['b', 'c', 'd']);
  // The item just above it deleted: under the next one that was above it.
  assert.deepEqual(ids(collage.draftModel(without(read, 'c'))), ['a', 'b', 'd']);
  // Everything above it deleted: on top.
  assert.deepEqual(ids(collage.draftModel(without(without(read, 'c'), 'd'))), ['a', 'b']);
  // A new item added here goes on top, above the kept part.
  const added = { ...read, items: [...read.items, collage.textItem('n', 'New', { x: 1, y: 1 })] };
  assert.deepEqual(ids(collage.draftModel(added)), ['a', 'b', 'c', 'd', 'n']);
  // Two kept parts in a row stay in their order.
  const two = collage.readDraftModel({ version: 1, size: 'a4p', items: [a, shape, { ...shape, id: 'e' }, c] }, ['p1']);
  assert.deepEqual(ids(collage.draftModel(without(two, 'a'))), ['b', 'e', 'c']);
  // Items reordered here (text d sent to the back): the kept shapes stay in their own order.
  const mixed = collage.readDraftModel({ version: 1, size: 'a4p', items: [shape, { ...c, id: 'tb' }, { ...shape, id: 'sc' }, { ...d, id: 'td' }] }, []);
  const moved = { ...mixed, items: [mixed.items[1], mixed.items[0]] };
  const written = ids(collage.draftModel(moved));
  assert.ok(written.indexOf('b') < written.indexOf('sc'), written.join());
  assert.deepEqual([...written].sort(), ['b', 'sc', 'tb', 'td']);
});

// Lines and borders (docs/ux/invitations-posters.md, PR 3).
test('a shape item: its forms, its defaults, its name and how a draft reads it back', () => {
  const line = collage.shapeItem('l', 'line', { x: 500, y: 300, width: 600 });
  assert.equal(line.kind, 'shape');
  assert.equal(line.aspect, 600 / collage.SHAPE_LIMITS.lineBox, 'a line is thin, but still easy to grab and stretch');
  assert.ok(collage.SHAPE_LIMITS.lineBox > collage.ITEM_LIMITS.minWidth);
  assert.equal(collage.shapeItem('x', 'star', { x: 0, y: 0, width: 10 }).form, 'rect');
  assert.equal(collage.layerName(line), 'Line');
  assert.equal(collage.layerName(collage.shapeItem('o', 'ellipse', { x: 0, y: 0, width: 10 })), 'Oval');
  assert.equal(collage.layerName(collage.shapeItem('r', 'rect', { x: 0, y: 0, width: 10 })), 'Rectangle');

  const raw = { version: 1, size: 'a4p', items: [
    collage.shapeItem('a', 'rect', { x: 500, y: 700, width: 940, aspect: 0.7 }, { stroke: '#a8842f', fill: '#fbf7ee', strokeWidth: 4, radius: 12 }),
    { id: 'b', kind: 'shape', form: 'hexagon', fill: 'red', stroke: 'javascript:1', strokeWidth: 999, radius: 999, x: 1, y: 1, width: 50, aspect: 2, crop: { zoom: 2 } }
  ] };
  const read = collage.readDraftModel(raw, []);
  assert.equal(collage.keptCount(read), 0, 'a shape is drawn now, not kept aside');
  const [a, b] = read.items;
  assert.deepEqual([a.form, a.fill, a.stroke, a.strokeWidth, a.radius], ['rect', '#fbf7ee', '#a8842f', 4, 12]);
  assert.deepEqual([b.form, b.fill, b.stroke, b.strokeWidth, b.radius], ['rect', '', '', 40, collage.LAYOUT_LIMITS.radiusMax]);
  assert.equal('crop' in b, false);
});

test('a page border takes no room when a new item looks for a free spot; other shapes do', () => {
  const border = collage.shapeItem('a', 'rect', { x: 500, y: 707, width: 940, aspect: 940 / 1354 }, { stroke: '#a8842f' });
  const spot = collage.freeSpot([border], 'a4p', 100, 40);
  assert.deepEqual(spot, collage.freeSpot([], 'a4p', 100, 40));
  // A band across the middle: the next shape goes where it covers nothing, not on top of it.
  const band = collage.shapeItem('b', 'rect', spot && { x: 500, y: spot.y, width: 1000, aspect: 1000 / 120 }, { fill: '#1d2c55' });
  const next = collage.freeSpot([border, band], 'a4p', 160, 50);
  assert.ok(Math.abs(next.y - band.y) >= 60 + 50, `${next.y} is clear of the band at ${band.y}`);
});

// Card sizes (docs/ux/invitations-posters.md, PR 4): every size in common use, both ways round.
test('every card and pocket card size is a true paper size, both ways round, in its menu group', () => {
  const mm = (key) => collage.SIZES[key].mm;
  assert.deepEqual(mm('a5p'), [148, 210]);
  assert.deepEqual(mm('a5l'), [210, 148]);
  assert.deepEqual(mm('a6p'), [105, 148]);
  assert.deepEqual(mm('in57p'), [127, 177.8]);
  assert.deepEqual(mm('in46p'), [101.6, 152.4]);
  assert.deepEqual(mm('dlp'), [99, 210]);
  assert.deepEqual(mm('in45p'), [101.6, 127]);
  assert.deepEqual(mm('sq5'), [127, 127]);
  assert.deepEqual(mm('a7p'), [74, 105]);
  assert.deepEqual(mm('in355p'), [88.9, 127]);
  assert.deepEqual(mm('passl'), [88.9, 50.8]);
  assert.deepEqual(mm('passp'), [50.8, 88.9]);
  // A landscape size is its portrait one turned: the PDF page and the picture both swap.
  assert.deepEqual([collage.SIZES.a5l.width, collage.SIZES.a5l.height], [collage.SIZES.a5p.height, collage.SIZES.a5p.width]);
  const a5 = collage.pdfPage('a5p');
  assert.ok(Math.abs(a5.width - 148 / 25.4 * 72) < 0.01 && Math.abs(a5.height - 210 / 25.4 * 72) < 0.01);
  // Every size is in exactly one group of the menu.
  const groups = collage.sizesByGroup();
  assert.deepEqual(groups.map((g) => g.key), ['page', 'card', 'pocket', 'folded']);
  // Folded cards are the flat sheet (PR 9c).
  assert.deepEqual([collage.SIZES.fold6.mm, collage.SIZES.fold6.fold], [[210, 148], 'middle']);
  assert.deepEqual(collage.SIZES.fold5.mm, [297, 210]);
  assert.deepEqual(groups.flatMap((g) => g.keys).sort(), Object.keys(collage.SIZES).sort());
  // A draft keeps a card size; an unknown one falls back.
  assert.equal(collage.readDraftModel({ version: 1, size: 'passl', items: [] }, []).size, 'passl');
  assert.equal(collage.readDraftModel({ version: 1, size: 'a9', items: [] }, []).size, 'a4l');
});

// Fitting words to a width (docs/ux/invitations-posters.md, PR 4).
test('words with a width wrap onto new lines, then get smaller, and never change', () => {
  const measure = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) / 2;
  const base = collage.textItem('t', 'Change of Command Parade and Reception', { x: 500, y: 300 }, { size: 40, maxWidth: 420 });
  // Natural: one line, far wider than 420.
  assert.ok(collage.layoutText({ ...base, maxWidth: undefined }, measure).width > 420);
  const fitted = collage.layoutText(base, measure);
  assert.ok(fitted.width <= 420.5, `${fitted.width}`);
  assert.ok(fitted.lines >= 2 && fitted.lines <= 3);
  assert.equal(fitted.size, 40, 'wrapping was enough, the letters keep their size');
  assert.equal(fitted.glyphs.map((g) => g.ch).join(' '), base.text, 'the words are all there, in order');

  // One line only: the letters get smaller instead.
  const oneLine = collage.layoutText({ ...base, maxLines: 1 }, measure);
  assert.equal(oneLine.lines, 1);
  assert.ok(oneLine.size < 40 && oneLine.width <= 420.5);
  // Never below the smallest share of the size, even if it then runs wider.
  const tiny = collage.layoutText({ ...base, maxWidth: 30, maxLines: 1 }, measure);
  assert.equal(tiny.size, Math.max(collage.TEXT_LIMITS.size[0], 40 * collage.TEXT_FIT.smallest));
  // Lines the user broke stay broken; curved words are not fitted.
  assert.equal(collage.layoutText({ ...base, text: 'Raising Day\nDinner', maxWidth: 2000 }, measure).lines, 2);
  assert.equal(collage.layoutText({ ...base, curve: 40 }, measure).size, 40);

  // A draft keeps a usable width and line count, and drops a bad one.
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [{ ...base, maxLines: 9 }, { ...base, id: 'u', maxWidth: 3, maxLines: 'x' }] }, []);
  assert.deepEqual([read.items[0].maxWidth, read.items[0].maxLines], [420, 9]);
  assert.equal('maxWidth' in read.items[1], false);
  assert.equal('maxLines' in read.items[1], false);
});

test('a card is called by its occasion', () => {
  const host = collage.textItem('h', 'The Commandant and Officers', { x: 0, y: 0 });
  const occasion = { ...collage.textItem('o', 'Raising Day Dinner', { x: 0, y: 0 }), field: 'occasion' };
  assert.equal(collage.autoTitleFromText([host, occasion]), 'Raising Day Dinner card');
  assert.equal(collage.autoTitleFromText([host, { ...occasion, text: '  ' }]), 'The Commandant and Officers');
});

// A size typed in mm (docs/ux/invitations-posters.md, PR 5b).
test('a custom size is typed in mm, 50 to 300 a side, and prints at its true paper size', () => {
  assert.deepEqual(collage.customKey(120, 180), { key: 'mm:120x180' });
  assert.deepEqual(collage.customKey('99.04', '210'), { key: 'mm:99x210' }, 'rounded to a tenth of a mm');
  assert.deepEqual(collage.customKey(120.25, 180), { key: 'mm:120.3x180' });
  assert.match(collage.customKey(40, 180).error, /from 50 to 300 mm/);
  assert.match(collage.customKey('', 'x').error, /width and the height/);
  const size = collage.sizeOf('mm:120x180');
  assert.deepEqual([size.label, size.group, size.mm], ['Custom 120 × 180 mm', 'custom', [120, 180]]);
  assert.ok(Math.abs(size.width - 120 / 25.4 * 300) < 1);
  const pdf = collage.pdfPage('mm:120x180');
  assert.ok(Math.abs(pdf.width - 120 / 25.4 * 72) < 0.01 && Math.abs(pdf.height - 180 / 25.4 * 72) < 0.01);
  assert.equal(collage.isSize('mm:120x180'), true);
  for (const bad of ['mm:30x180', 'mm:120x400', 'mm:120x180.25', 'mm:x', 'mm:120X180']) assert.equal(collage.isSize(bad), false, bad);
  // New collages, size changes and drafts take a custom size; a bad one falls back.
  assert.equal(collage.newCollage('mm:120x180').size, 'mm:120x180');
  assert.equal(collage.changeSize(collage.newCollage('a4l'), 'mm:120x180').size, 'mm:120x180');
  assert.equal(collage.readDraftModel({ version: 1, size: 'mm:120x180', items: [] }, []).size, 'mm:120x180');
  assert.equal(collage.readDraftModel({ version: 1, size: 'mm:10x10', items: [] }, []).size, 'a4l');
});

// The back (PR 8): a size change moves both sides, and the back's parts kept for a newer PRISM
// survive a read, a save and a read again.
test('a card with a back: a size change moves both sides; the back keeps its newer parts through a save', () => {
  const card = { ...collage.newCollage('a5p'), items: [{ id: 'f', kind: 'text', text: 'Front', x: 500, y: 1300, width: 200, aspect: 4, size: 40 }],
    back: { background: { type: 'color', value: '#ffffff' }, items: [{ id: 'b', kind: 'text', text: 'Back', x: 500, y: 1300, width: 200, aspect: 4, size: 40 }] } };
  const turned = collage.changeSize(card, 'a5l');
  const height = collage.pageHeight('a5l');
  assert.ok(turned.items[0].y < height && turned.back.items[0].y < height, 'both sides on the page');
  assert.ok(Math.abs(turned.back.items[0].y - turned.items[0].y) < 1e-6);

  const raw = { version: 1, size: 'a5p', background: { type: 'color', value: '#ffffff' }, items: [],
    back: { background: { type: 'color', value: '#ffffff' }, items: [
      { id: 'n', kind: 'text', text: 'Note', x: 500, y: 500, width: 200, aspect: 4, size: 40 },
      { id: 'h', kind: 'hologram', x: 1, y: 1 }
    ] } };
  const read = collage.readDraftModel(JSON.parse(JSON.stringify(raw)), []);
  assert.equal(collage.keptCount(read), 1);
  const again = collage.readDraftModel(JSON.parse(JSON.stringify(collage.draftModel(read))), []);
  assert.equal(collage.keptCount(again), 1, 'the back\'s newer part survives a save');
  assert.deepEqual(collage.draftModel(again).back.items.map((item) => item.id), ['n', 'h']);
});

// Each side keeps its own photo layout when the card turns over, and in the draft (PR 8).
test('a side keeps its photo layout through turning over and a draft', () => {
  const front = { ...collage.newCollage('a5p'), layout: { key: 't2', gap: 12, radius: 0 }, back: { background: { type: 'color', value: '#ffffff' }, items: [], layout: null } };
  const over = collage.flipSide(front);
  assert.equal(over.layout, null, 'the back has no layout');
  assert.deepEqual(over.back.layout, { key: 't2', gap: 12, radius: 0 });
  assert.deepEqual(collage.flipSide(over).layout, { key: 't2', gap: 12, radius: 0 }, 'back to the front, its layout is there');
  const read = collage.readDraftModel(JSON.parse(JSON.stringify(collage.draftModel(over))), []);
  assert.deepEqual(read.back.layout?.key, 't2');
});

// The back's own page-level fields a newer PRISM wrote are kept through a save (PR 8).
test('the back keeps its own newer fields through a read and a save', () => {
  const raw = { version: 1, size: 'a5p', background: { type: 'color', value: '#ffffff' }, items: [],
    back: { background: { type: 'color', value: '#ffffff' }, items: [], finishing: { foil: 'gold' } } };
  const read = collage.readDraftModel(JSON.parse(JSON.stringify(raw)), []);
  const saved = collage.draftModel(read);
  assert.deepEqual(saved.back.finishing, { foil: 'gold' });
  assert.deepEqual(collage.draftModel(collage.readDraftModel(JSON.parse(JSON.stringify(saved)), [])).back.finishing, { foil: 'gold' });
});

// A programme of ten lines reads back from a draft at the same size (PR 8).
test('a ten-line programme keeps its size when the draft is reopened', () => {
  const measure = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) * 0.56;
  const programme = Array.from({ length: 10 }, (_, i) => `${18 + i}00 h  Item ${i + 1}`).join(String.fromCharCode(10));
  const item = { id: 'p', kind: 'text', text: programme, x: 500, y: 500, width: 800, aspect: 1, size: 31, maxWidth: 800, maxLines: 10, field: 'programme' };
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [item] }, []);
  assert.equal(read.items[0].maxLines, 10);
  assert.ok(Math.abs(collage.layoutText(read.items[0], measure).size - collage.layoutText(item, measure).size) < 0.01);
});

// Print sheets (docs/ux/invitations-posters.md, PR 9).
test('cards go as many to an A4 or A3 sheet as fit, turned whichever way holds more, with cut marks in the gaps', () => {
  const count = (size, sheet) => print.sheetLayout(size, sheet).count;
  assert.deepEqual([count('a6p', 'a4'), count('a7p', 'a4'), count('passl', 'a4'), count('a5p', 'a4'), count('a5p', 'a3')], [2, 4, 10, 1, 2]);
  assert.equal(print.sheetLayout('a4p', 'a4').count, 0, 'an A4 page does not fit inside an A4 sheet with margins');
  assert.equal(print.sheetLayout('slide', 'a4'), null, 'a screen size has no paper size');
  // A6 upright holds two side by side only on a turned A4.
  const a6 = print.sheetLayout('a6p', 'a4');
  assert.deepEqual([a6.upright, a6.sheet, a6.cols, a6.rows], [false, [297, 210], 2, 1]);
  // Every card on the sheet, inside the margins, none overlapping.
  for (const [size, sheet] of [['passl', 'a4'], ['a7l', 'a3'], ['sq5', 'a4']]) {
    const layout = print.sheetLayout(size, sheet);
    for (const c of layout.cards) {
      assert.ok(c.x >= print.SHEET_SPACE.margin - 1e-9 && c.y >= print.SHEET_SPACE.margin - 1e-9, size);
      assert.ok(c.x + c.w <= layout.sheet[0] - print.SHEET_SPACE.margin + 1e-9 && c.y + c.h <= layout.sheet[1] - print.SHEET_SPACE.margin + 1e-9, size);
    }
    for (let i = 0; i < layout.cards.length; i += 1) for (let j = i + 1; j < layout.cards.length; j += 1) {
      const a = layout.cards[i];
      const b = layout.cards[j];
      assert.ok(a.x + a.w <= b.x + 1e-9 || b.x + b.w <= a.x + 1e-9 || a.y + a.h <= b.y + 1e-9 || b.y + b.h <= a.y + 1e-9, `${size}: cards overlap`);
    }
    // Cut marks never cross a card.
    for (const [x1, y1, x2, y2] of print.cutMarks(layout)) {
      for (const c of layout.cards) {
        const inside = (x, y) => x > c.x + 1e-9 && x < c.x + c.w - 1e-9 && y > c.y + 1e-9 && y < c.y + c.h - 1e-9;
        assert.ok(!inside(x1, y1) && !inside(x2, y2) && !inside((x1 + x2) / 2, (y1 + y2) / 2), `${size}: a cut mark crosses a card`);
      }
    }
  }
  // The back sheet lines up after a long-edge flip: mirrored across an upright sheet, top to bottom on a turned one.
  const front = print.sheetLayout('passl', 'a4');
  const back = print.sheetLayout('passl', 'a4', { back: true });
  assert.equal(front.upright, true);
  assert.equal(back.cards[0].x, front.cards[front.cols - 1].x);
  assert.equal(back.cards[0].y, front.cards[0].y);
  const turnedFront = print.sheetLayout('a6p', 'a4');
  const turnedBack = print.sheetLayout('a6p', 'a4', { back: true });
  assert.deepEqual(turnedBack.cards.map((c) => c.x), turnedFront.cards.map((c) => c.x), 'one row: a top-to-bottom flip keeps the columns');
});

// The guest list (docs/ux/invitations-posters.md, PR 10).
test('guest names come one a line from pasted text, or the first column of a CSV, within limits', () => {
  assert.deepEqual(print.guestNames('Name\nLt Gen R. K. Mehta, AVSM\n\n  Maj A and   Mrs B  '), ['Lt Gen R. K. Mehta, AVSM', 'Maj A and Mrs B']);
  assert.deepEqual(print.guestNames('Name,Email\n"Brig P. Singh, SM",x@y\nCol D. Nair,col@x\r\n"Say ""Hi""",z', { csv: true }), ['Brig P. Singh, SM', 'Col D. Nair', 'Say "Hi"']);
  const many = Array.from({ length: 400 }, (_, i) => `Guest ${i}`).join('\n');
  assert.equal(print.guestNames(many).length, collage.GUEST_LIMITS.names);
  assert.equal(print.guestNames('x'.repeat(500))[0].length, collage.GUEST_LIMITS.length);
  // Names past the limit are counted, so the list can say how many were left out.
  assert.deepEqual(print.readGuests(many).over, 400 - collage.GUEST_LIMITS.names);
  assert.equal(print.readGuests('Maj A').over, 0);
  // A CSV whose first row heads a Name column takes that column, not the rank before it.
  assert.deepEqual(print.guestNames('Rank,Name,Unit\nBrig,"P. Singh, SM",5 Sikh\nCol,D. Nair,', { csv: true }), ['Brig P. Singh, SM', 'Col D. Nair']);
  assert.deepEqual(print.guestNames('Rank;Guest name' + String.fromCharCode(10) + 'Brig;P. Singh, SM', { csv: true }), ['Brig P. Singh, SM']);
  assert.deepEqual(print.guestNames('Guests' + String.fromCharCode(10) + 'Maj A' + String.fromCharCode(10) + 'Capt B'), ['Maj A', 'Capt B']);
  // First name and surname are joined; a quoted address with a line break stays in its cell.
  assert.deepEqual(print.guestNames('First name,Surname,Address'+String.fromCharCode(10)+'Ravi,Kumar,"12 Mall Road'+String.fromCharCode(10)+'Delhi"'+String.fromCharCode(10)+'Asha,Rao,x', { csv: true }), ['Ravi Kumar', 'Asha Rao']);
  // A draft keeps a guest called anything, even "Name".
  const kept = collage.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {}, guests: ['Name', 'Guests', 'Maj B'] } }, []);
  assert.deepEqual(kept.card.guests, ['Name', 'Guests', 'Maj B']);
  assert.deepEqual(print.guestNames('Full name\nMaj A', { csv: true }), ['Maj A']);
  // A draft keeps the list, cleaned the same way.
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {}, guests: ['Col D. Nair', '', 7, 'Maj B'] } }, []);
  assert.deepEqual(read.card.guests, ['Col D. Nair', 'Maj B']);
  assert.equal(collage.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {} } }, []).card.guests, undefined);
});

// Cold review fixes (19 Nov 2026).
test('a programme of eight lines keeps its size after its draft is read back', () => {
  const measure = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) / 2;
  const text = Array.from({ length: 8 }, (_, i) => `19${i}0 h · Item ${i}`).join('\n');
  const item = collage.textItem('p', text, { x: 500, y: 500 }, { size: 31, maxWidth: 800, maxLines: 10 });
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [item] }, []).items[0];
  assert.equal(collage.layoutText(read, measure).size, 31, 'lines the user broke always count');
  // A tint names a unit colour 1 to 3 for a colour; anything else is dropped.
  const shape = { ...collage.shapeItem('s', 'rect', { x: 1, y: 1, width: 10 }), tint: { fill: 1, stroke: 9, width: 2 } };
  assert.deepEqual(collage.readDraftModel({ version: 1, size: 'a5p', items: [shape] }, []).items[0].tint, { fill: 1 });
});

test('a card\'s back moves with the page size, and keeps its newer parts on every save', () => {
  const front = [collage.textItem('a', 'Front', { x: 500, y: 1000 })];
  const backItems = [collage.textItem('b', 'Back', { x: 500, y: 1200 }), { id: 'z', kind: 'seal', x: 1, y: 1, width: 10, aspect: 1 }];
  const card = collage.readDraftModel({ version: 1, size: 'a5p', items: front, back: { background: { type: 'color', value: '#ffffff' }, items: backItems } }, []);
  assert.equal(collage.keptCount(card), 1, 'the back\'s newer part counts');
  const resized = collage.changeSize(card, 'a5l');
  assert.ok(resized.back.items[0].y < collage.pageHeight('a5l'), 'the back moved onto the new page');
  // Two round trips: the back's newer part is still there.
  const once = collage.draftModel(card);
  const twice = collage.draftModel(collage.readDraftModel(JSON.parse(JSON.stringify(once)), []));
  assert.ok(twice.back.items.some((item) => item.kind === 'seal'));
});

// A word too long for its width even at the smallest size is broken, so it stays on the page.
test('one very long word in a fitted text item is broken across lines and stays within the width', () => {
  const measure = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) * 0.56;
  const item = { kind: 'text', text: 'X'.repeat(80), size: 40, maxWidth: 300, maxLines: 2, font: 'Mulish' };
  const layout = collage.layoutText(item, measure);
  assert.ok(layout.width <= item.maxWidth + 0.5, `the text is ${layout.width} wide`);
  assert.ok(layout.lines > 1);
  assert.equal(layout.glyphs.map((glyph) => glyph.ch).join(''), 'X'.repeat(80));
  // A word that fits once smaller is made smaller, not broken.
  const short = collage.layoutText({ ...item, text: 'Sudarshan' }, measure);
  assert.equal(short.lines, 1);
});

// A long programme (ten lines of 60 letters) is kept whole by the draft, in the item and the details.
test('a programme of 600 letters is kept whole in a draft', () => {
  const programme = Array.from({ length: 10 }, (_, i) => `${18 + i}00 h  ` + 'Item of the evening with its place'.padEnd(52, '.')).join(String.fromCharCode(10));
  assert.ok(programme.length > 600);
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [{ id: 'p', kind: 'text', text: programme, x: 500, y: 500, width: 600, aspect: 1, size: 40, angle: 0, field: 'programme' }], card: { design: 'classic', details: { programme } } }, []);
  assert.equal(read.items[0].text, programme);
  assert.equal(read.card.details.programme, programme);
});

// Backs on print sheets (cold review 2): on a turned sheet each back is turned 180°, so a card
// turned over like a book page reads upright; on an upright sheet nothing is turned.
test('sheet placements turn the backs on a turned sheet, and each card still fills its slot', () => {
  const pt = print.PT_PER_MM;
  const turned = print.sheetLayout('a6p', 'a4', { back: true });
  assert.equal(turned.upright, false, 'A6 portrait goes two to a turned A4');
  const backs = print.sheetPlacements(turned, true);
  const [, sh] = turned.sheet;
  backs.forEach((place, i) => {
    const slot = turned.cards[i];
    assert.equal(place.rotate, 180);
    // Turned about (x, y), the card covers x - width … x and y - height … y.
    assert.ok(Math.abs(place.x - place.width - slot.x * pt) < 0.01);
    assert.ok(Math.abs(place.y - place.height - (sh - slot.y - slot.h) * pt) < 0.01);
  });
  assert.ok(print.sheetPlacements(print.sheetLayout('a6p', 'a4'), false).every((place) => place.rotate === 0), 'fronts are never turned');
  const upright = print.sheetLayout('a7p', 'a4', { back: true });
  assert.equal(upright.upright, true);
  assert.ok(print.sheetPlacements(upright, true).every((place) => place.rotate === 0), 'an upright sheet keeps its backs upright');
});

// A newer PRISM's parts of a card (cold review 2): the back's own fields, a back this PRISM cannot
// read, and the card's own fields are kept and written back as they were.
test('a draft keeps a newer PRISM\'s back fields, an unreadable back and unknown card fields', () => {
  const raw = {
    version: 1, size: 'a5p', items: [], background: { type: 'color', value: '#ffffff' },
    card: { design: 'classic', details: {}, placed: [], ribbon: { colour: 'gold' } },
    back: { background: { type: 'color', value: '#ffffff' }, items: [], foil: { on: true } }
  };
  const read = collage.readDraftModel(JSON.parse(JSON.stringify(raw)), []);
  assert.deepEqual(read.card.ribbon, { colour: 'gold' });
  // A change of design keeps them too.
  const designs = require('./collage-designs.js');
  const measure = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) * 0.56;
  let n = 0;
  const redesigned = designs.applyDesign(read, 'navy', designs.detailsOf(read), { measure, newId: () => `r${++n}` });
  assert.deepEqual(redesigned.card.ribbon, { colour: 'gold' });
  const written = collage.draftModel(read);
  assert.deepEqual(written.back.foil, { on: true });
  assert.deepEqual(written.card.ribbon, { colour: 'gold' });
  // A back of a shape this PRISM does not know is kept whole.
  const odd = collage.readDraftModel({ ...raw, back: { pages: [1, 2] } }, []);
  assert.equal(odd.back, undefined);
  assert.deepEqual(collage.draftModel(odd).back, { pages: [1, 2] });
});

// Print sheet geometry, exactly (cold review 2): margins and gaps, the back's rows mirrored on a
// turned sheet of several rows, and cut marks at the card corners in the gaps.
test('sheet layout: 8 mm margins, 4 mm gaps, mirrored rows on a turned sheet, cut marks at the corners', () => {
  const front = print.sheetLayout('a7l', 'a4');
  const back = print.sheetLayout('a7l', 'a4', { back: true });
  assert.equal(front.upright, false);
  assert.equal(front.rows, 2);
  assert.equal(front.cols, 2);
  // The gap between neighbours is 4 mm, and the cards sit centred inside 8 mm margins.
  const [a, b] = [front.cards[0], front.cards[1]];
  assert.ok(Math.abs(b.x - (a.x + a.w) - 4) < 1e-6, 'a 4 mm gap across');
  assert.ok(a.x >= 8 - 1e-6 && a.y >= 8 - 1e-6, 'inside the margins');
  const rowGap = front.cards[2].y - (front.cards[0].y + front.cards[0].h);
  assert.ok(Math.abs(rowGap - 4) < 1e-6, 'a 4 mm gap down');
  // On a turned sheet the back's rows swap (top to bottom), the columns stay.
  back.cards.forEach((slot, i) => {
    const mirrored = front.cards[(1 - Math.floor(i / 2)) * 2 + (i % 2)];
    assert.deepEqual([slot.x, slot.y], [mirrored.x, mirrored.y], `back slot ${i}`);
  });
  // Every cut mark lies outside every card, and each card corner has its marks.
  const marks = print.cutMarks(front);
  const inside = (x, y) => front.cards.some((c) => x > c.x + 1e-6 && x < c.x + c.w - 1e-6 && y > c.y + 1e-6 && y < c.y + c.h - 1e-6);
  for (const [x1, y1, x2, y2] of marks) assert.ok(!inside((x1 + x2) / 2, (y1 + y2) / 2));
  for (const c of front.cards) {
    for (const [cx, cy] of [[c.x, c.y], [c.x + c.w, c.y], [c.x, c.y + c.h], [c.x + c.w, c.y + c.h]]) {
      assert.ok(marks.some(([x1, y1, x2, y2]) => (Math.abs(x1 - cx) < 1e-6 && Math.abs(x2 - cx) < 1e-6) || (Math.abs(y1 - cy) < 1e-6 && Math.abs(y2 - cy) < 1e-6)), 'a mark lines up with each corner');
    }
  }
});

// Damaged drafts (cold review 2): a size, design or font named like an object's own property, or
// a value that is an object where text belongs, is read as unknown, never as a size or a crash.
test('a draft with a size named "constructor" or an object for a sticker id reads safely', () => {
  for (const name of ['constructor', 'toString', 'valueOf', 'hasOwnProperty', '__proto__']) {
    assert.equal(collage.isSize(name), false, name);
    assert.ok(Number.isFinite(collage.pageHeight(name)), name);
    const read = collage.readDraftModel({ version: 1, size: name, items: [] }, []);
    assert.equal(read.size, collage.DEFAULT_SIZE ?? read.size);
    assert.ok(collage.isSize(read.size));
  }
  const odd = { toString: 1 };
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [
    { id: 's1', kind: 'sticker', sticker: odd, x: 1, y: 1, width: 10, aspect: 1 },
    { id: 't1', kind: 'text', text: 'Hi', color: odd, x: 1, y: 1, width: 10, aspect: 1, size: 40 },
    { id: 'f1', kind: 'photo', photo: 'p', frame: 'constructor', look: 'toString', shape: 'valueOf', x: 1, y: 1, width: 10, aspect: 1 }
  ], background: { type: 'color', value: odd } }, ['p']);
  assert.ok(read);
  assert.equal(read.items.find((item) => item.id === 't1').color, '#172033');
  const photo = read.items.find((item) => item.id === 'f1');
  assert.deepEqual([photo.frame, photo.look, photo.shape], ['none', 'none', 'rect']);
});

// Columns pasted from a spreadsheet (cold review 2): with a heading row, rank and name are joined;
// without one, only the first column is used, and the list says so.
test('names pasted from a spreadsheet: columns read by their headings, else the first, and said', () => {
  const tab = String.fromCharCode(9);
  const nl = String.fromCharCode(10);
  const headed = print.readGuests(['Rank' + tab + 'Name' + tab + 'Unit', 'Brig' + tab + 'P. Singh, SM' + tab + 'HQ'].join(nl));
  assert.deepEqual(headed.names, ['Brig P. Singh, SM']);
  assert.equal(headed.firstColumnOnly, false);
  const plain = print.readGuests(['Brig P. Singh, SM and Mrs Singh' + tab + 'HQ', 'Col D. Nair' + tab + '5 Sikh'].join(nl));
  assert.deepEqual(plain.names, ['Brig P. Singh, SM and Mrs Singh', 'Col D. Nair']);
  assert.equal(plain.firstColumnOnly, true);
  // One name a line, commas and all, stays as it was.
  assert.deepEqual(print.readGuests('Brig P. Singh, SM' + nl + 'Col D. Nair').names, ['Brig P. Singh, SM', 'Col D. Nair']);
  assert.equal(print.readGuests('Brig P. Singh, SM').firstColumnOnly, false);
});

// Edge to edge (cold review 2): cards touching, no margin or gaps, so more fit on A4; no cut marks.
test('edge to edge on A4 fits 4 A6 and 2 A5, touching, with no cut marks', () => {
  const a6 = print.sheetLayout('a6p', 'a4edge');
  assert.equal(a6.count, 4);
  assert.equal(a6.edge, true);
  assert.deepEqual(print.cutMarks(a6), []);
  const [a, b] = a6.cards;
  assert.ok(Math.abs(b.x - (a.x + a.w)) < 1e-6, 'touching');
  assert.equal(print.sheetLayout('a5p', 'a4edge').count, 2);
  assert.equal(print.sheetLayout('a7p', 'a4edge').count, 8);
  // Backs still line up with their fronts.
  const back = print.sheetLayout('a6p', 'a4edge', { back: true });
  assert.equal(back.cards.length, 4);
  assert.equal(print.sheetLayout('a6p', 'a4').count, 2, 'with room for cutting, 2');
});

// A card's draft is stored front first (cold review 3): the saved shape never depends on which
// side was showing, and the collage's own newer fields stay at the top.
test('a card saved with its back showing is stored front first, and opens with the back showing', () => {
  const front = { id: 'f', kind: 'text', text: 'Front', x: 500, y: 500, width: 200, aspect: 4, size: 40 };
  const back = { id: 'b', kind: 'text', text: 'Back', x: 500, y: 500, width: 200, aspect: 4, size: 40 };
  const card = collage.readDraftModel({ version: 1, size: 'a5p', background: { type: 'color', value: '#ffffff' }, items: [front], ribbon: 'gold',
    back: { background: { type: 'color', value: '#ffffff' }, items: [back] } }, []);
  const showingBack = collage.flipSide(card);
  assert.equal(showingBack.items[0].id, 'b');
  const saved = collage.draftModel(showingBack);
  assert.equal(saved.items[0].id, 'f', 'the front at the top');
  assert.equal(saved.back.items[0].id, 'b');
  assert.equal(saved.side, 'back');
  assert.equal(saved.ribbon, 'gold', 'the collage\'s own field at the top');
  assert.equal(saved.back.ribbon, undefined);
  const reopened = collage.readDraftModel(JSON.parse(JSON.stringify(saved)), []);
  assert.equal(reopened.side, 'back');
  assert.equal(reopened.items[0].id, 'b', 'opens with the back showing');
  assert.equal(collage.draftModel(reopened).items[0].id, 'f');
});

// Guest list headings as units write them (cold review 3): "Ser No, Rank & Name, Unit" and
// "S.No, Rank, Name of Guest" give names, never serial numbers; a stray quote spoils one line only.
test('guest list headings with serial numbers, "Rank & Name" and "Name of Guest"; a stray quote', () => {
  const nl = String.fromCharCode(10);
  assert.deepEqual(print.guestNames(['Ser No,Rank & Name,Unit', '1,Brig P. Singh,HQ', '2,Col D. Nair,5 Sikh'].join(nl), { csv: true }), ['Brig P. Singh', 'Col D. Nair']);
  assert.deepEqual(print.guestNames(['S.No,Rank,Name of Guest', '1,Maj,A. Rao'].join(nl), { csv: true }), ['Maj A. Rao']);
  // A unit's name is not a guest's.
  assert.deepEqual(print.guestNames(['Name,Unit Name', 'Col D. Nair,5 Sikh'].join(nl), { csv: true }), ['Col D. Nair']);
  // An unclosed quote: each line still a guest of its own.
  assert.deepEqual(print.guestNames(['Name', '"Maj A', 'Capt B', 'Lt C'].join(nl), { csv: true }), ['"Maj A', 'Capt B', 'Lt C']);
});

// Turning a card over keeps the parts a newer PRISM kept on either side (cold review 3).
test('turning a card over and back keeps both sides\' kept parts', () => {
  const raw = { version: 1, size: 'a5p', background: { type: 'color', value: '#ffffff' }, items: [{ id: 'fh', kind: 'hologram' }],
    back: { background: { type: 'color', value: '#ffffff' }, items: [{ id: 'bh', kind: 'hologram' }] } };
  const read = collage.readDraftModel(JSON.parse(JSON.stringify(raw)), []);
  assert.equal(collage.keptCount(read), 2);
  const over = collage.flipSide(read);
  assert.equal(collage.keptCount(over), 2, 'turned over');
  assert.equal(collage.keptCount(collage.flipSide(over)), 2, 'turned back');
  const saved = collage.draftModel(over);
  assert.deepEqual(saved.items.map((item) => item.id), ['fh']);
  assert.deepEqual(saved.back.items.map((item) => item.id), ['bh']);
});

// Cut marks exactly (cold review 3): both marks at every card corner, 1 mm out from it and
// SHEET_SPACE.mark long; the cards centred on the sheet.
test('each card corner has both cut marks, 1 mm out, and the cards are centred', () => {
  const layout = print.sheetLayout('a6p', 'a4');
  const marks = print.cutMarks(layout);
  assert.equal(marks.length, layout.cards.length * 8);
  const { mark } = print.SHEET_SPACE;
  for (const card of layout.cards) {
    for (const [cx, cy] of [[card.x, card.y], [card.x + card.w, card.y], [card.x, card.y + card.h], [card.x + card.w, card.y + card.h]]) {
      const vertical = marks.find(([x1, y1, x2, y2]) => Math.abs(x1 - cx) < 1e-9 && Math.abs(x2 - cx) < 1e-9 && Math.abs(Math.min(Math.abs(y1 - cy), Math.abs(y2 - cy)) - 1) < 1e-9);
      const horizontal = marks.find(([x1, y1, x2, y2]) => Math.abs(y1 - cy) < 1e-9 && Math.abs(y2 - cy) < 1e-9 && Math.abs(Math.min(Math.abs(x1 - cx), Math.abs(x2 - cx)) - 1) < 1e-9);
      assert.ok(vertical && Math.abs(Math.abs(vertical[3] - vertical[1]) - mark) < 1e-9, 'a vertical mark at the corner');
      assert.ok(horizontal && Math.abs(Math.abs(horizontal[2] - horizontal[0]) - mark) < 1e-9, 'a horizontal mark at the corner');
    }
  }
  const left = Math.min(...layout.cards.map((c) => c.x));
  const right = Math.max(...layout.cards.map((c) => c.x + c.w));
  const top = Math.min(...layout.cards.map((c) => c.y));
  const bottom = Math.max(...layout.cards.map((c) => c.y + c.h));
  assert.ok(Math.abs(left - (layout.sheet[0] - right)) < 1e-9, 'centred across');
  assert.ok(Math.abs(top - (layout.sheet[1] - bottom)) < 1e-9, 'centred down');
});
