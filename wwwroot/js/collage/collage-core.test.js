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
  assert.deepEqual(filled, { ...slot, kind: 'photo', photo: 'p9', crop: { zoom: 1, x: 0.5, y: 0.5 } });
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
