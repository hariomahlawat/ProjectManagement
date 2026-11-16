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
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [text, { ...text, id: 'u', font: 'comic', text: 'x'.repeat(400) }] }, []);
  const [a, b] = read.items;
  assert.deepEqual([a.text, a.font, a.size, a.curve, a.color, a.label, a.outlineColor, a.align, a.bold], ['शुभकामनाएँ', 'hindiHand', 300, -100, '#c62f3a', '', '#ffffff', 'center', false]);
  assert.deepEqual([b.font, b.text.length], ['clean', 300]);
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
