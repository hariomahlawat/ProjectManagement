// Tests for the working copies' memory (docs/ux/collage-review-3-fixes.md, PR 9): a pixel budget
// shared by every photo, and removed photos let go once Undo and Redo can no longer reach them.
const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('./collage-core.js');
const { createChangeLog } = require('./collage-changes.js');
const { createPhotoReader, forgetUnreachable, releasePhoto } = require('./collage-photos.js');

const pixels = (size) => size.width * size.height;

test('60 phone photos stay under the working-pixel budget', () => {
  const max = core.PHOTO_LIMITS.maxPhotos;
  const budget = core.PHOTO_LIMITS.workingPixels;
  for (const [width, height] of [[4000, 3000], [3000, 4000], [8000, 6000], [6000, 6000], [12000, 2000], [2000, 2000]]) {
    const total = pixels(core.budgetedSize(width, height, max)) * max;
    assert.ok(total <= budget, `${width}x${height}: ${total} > ${budget}`);
  }
  // About 240 MB of working copies at most, against about 720 MB before.
  assert.ok(budget * 4 <= 250 * 1000 * 1000);
});

test('each working copy gets its share of the budget at several photo counts', () => {
  assert.equal(core.workingPixelsEach(1), 2000 * 2000);
  assert.equal(core.workingPixelsEach(15), 2000 * 2000);
  assert.equal(core.workingPixelsEach(20), 3000000);
  assert.equal(core.workingPixelsEach(30), 2000000);
  assert.equal(core.workingPixelsEach(60), 1000000);
  assert.equal(core.workingPixelsEach(0), 2000 * 2000);
  // A 12 MP phone photo: the full 2,000 px while few, smaller as the count grows.
  assert.deepEqual(core.budgetedSize(4000, 3000, 1), { width: 2000, height: 1500 });
  assert.deepEqual(core.budgetedSize(4000, 3000, 15), { width: 2000, height: 1500 });
  assert.deepEqual(core.budgetedSize(4000, 3000, 30), { width: 1632, height: 1224 });
  assert.deepEqual(core.budgetedSize(4000, 3000, 60), { width: 1154, height: 866 });
  for (const count of [1, 10, 16, 25, 40, 60]) {
    const size = core.budgetedSize(4000, 3000, count);
    assert.ok(pixels(size) <= core.workingPixelsEach(count));
    assert.ok(Math.max(size.width, size.height) <= core.PHOTO_LIMITS.workingSide);
  }
  // A small photo is never made larger.
  assert.deepEqual(core.budgetedSize(800, 600, 60), { width: 800, height: 600 });
});

function fakeCanvas() {
  const canvas = {
    width: 0,
    height: 0,
    drawn: null,
    getContext: () => ({ drawImage: (source, x, y, w, h) => { canvas.drawn = { source, w, h }; } })
  };
  return canvas;
}

test('copies made while there were few photos are made smaller when more are added', () => {
  const reader = createPhotoReader({ core, nextId: () => 'x', makeCanvas: fakeCanvas });
  const photos = new Map();
  // 15 photos at the full 2,000 px, then 45 more sized for 60.
  for (let i = 0; i < 60; i += 1) {
    const size = i < 15 ? core.budgetedSize(4000, 3000, 15) : core.budgetedSize(4000, 3000, 60);
    const canvas = fakeCanvas();
    canvas.width = size.width;
    canvas.height = size.height;
    photos.set(`p${i}`, { id: `p${i}`, canvas });
  }
  const before = [...photos.values()].map((photo) => photo.canvas);
  const total = () => [...photos.values()].reduce((sum, photo) => sum + photo.canvas.width * photo.canvas.height, 0);
  assert.ok(total() > core.PHOTO_LIMITS.workingPixels);
  const shrunk = reader.fitBudget(photos);
  assert.equal(shrunk.length, 15);
  assert.ok(total() <= core.PHOTO_LIMITS.workingPixels);
  // The larger copy is let go, and the new one drawn from it.
  assert.equal(before[0].width, 0);
  assert.equal(photos.get('p0').canvas.drawn.source, before[0]);
  assert.deepEqual([photos.get('p0').canvas.width, photos.get('p0').canvas.height], [1154, 866]);
  // Copies already within their share are left alone, and a second pass changes nothing.
  assert.equal(photos.get('p59').canvas, before[59]);
  assert.deepEqual(reader.fitBudget(photos), []);
});

test('copies are made smaller for the count a batch will bring, before it is read', () => {
  const reader = createPhotoReader({ core, nextId: () => 'x', makeCanvas: fakeCanvas });
  const photos = new Map();
  for (let i = 0; i < 15; i += 1) {
    const canvas = fakeCanvas();
    canvas.width = 2000;
    canvas.height = 2000;
    photos.set(`p${i}`, { id: `p${i}`, canvas });
  }
  // 15 square copies fill the budget; 45 more are coming, so each gets a 60th of it now.
  assert.deepEqual(reader.fitBudget(photos), []);
  assert.equal(reader.fitBudget(photos, 60).length, 15);
  for (const photo of photos.values()) assert.deepEqual([photo.canvas.width, photo.canvas.height], [1000, 1000]);
  const batch = 45 * pixels(core.budgetedSize(6000, 6000, 60));
  const kept = [...photos.values()].reduce((sum, photo) => sum + photo.canvas.width * photo.canvas.height, 0);
  assert.ok(kept + batch <= core.PHOTO_LIMITS.workingPixels);
});

/** A collage whose photo items are the given ids, the change log over it, and the photos map. */
function setUp() {
  let model = { size: 'square', items: [], background: { type: 'colour', colour: '#ffffff' } };
  const photos = new Map();
  const released = [];
  const photo = (id) => {
    const canvas = { width: 100, height: 75 };
    const entry = { id, canvas, thumbnail: 'data:', file: { name: `${id}.jpg` } };
    photos.set(id, entry);
    return entry;
  };
  let log;
  // As the editor does: after every recorded step, removed photos no step reaches are let go.
  const onRecorded = () => { for (const id of forgetUnreachable(photos, log.mentions)) released.push(id); };
  log = createChangeLog({ core, getModel: () => model, onRecorded });
  return {
    photos,
    released,
    photo,
    log,
    set: (items) => { model = { ...model, items }; },
    get model() { return model; },
    restore: (next) => { model = next; }
  };
}

const photoItem = (id) => ({ id: `i-${id}`, kind: 'photo', photo: id, x: 0, y: 0, width: 100, height: 75 });
const textItem = (n) => ({ id: 't', kind: 'text', text: `Step ${n}` });

test('a removed photo is let go once Redo can no longer bring it back, not before', () => {
  const s = setUp();
  const a = s.photo('a');
  s.set([photoItem('a')]);
  s.log.commit();
  // Undo takes the photo off the page; the user then removes it from the tray.
  s.restore(s.log.undo());
  a.removed = true;
  s.log.flush();
  assert.deepEqual(forgetUnreachable(s.photos, s.log.mentions), []);
  assert.equal(s.photos.get('a'), a);
  assert.equal(a.canvas.width, 100);
  // A new change drops the Redo step that showed it: it is let go at once.
  s.set([textItem(1)]);
  s.log.commit();
  assert.deepEqual(s.released, ['a']);
  assert.equal(s.photos.has('a'), false);
  assert.equal(a.canvas, null);
  assert.equal(a.thumbnail, '');
  assert.ok(a.file, 'the file itself is not touched');
});

test('a removed photo is let go when the oldest Undo step that showed it is dropped, not before', () => {
  const s = setUp();
  const a = s.photo('a');
  const canvas = a.canvas;
  s.set([photoItem('a')]);
  s.log.commit();
  s.set([]);
  s.log.commit();
  a.removed = true;
  // History: [empty, a, empty]; each change adds a step and the oldest goes past 100 steps.
  for (let n = 1; n <= 98; n += 1) {
    s.set([textItem(n)]);
    s.log.commit();
  }
  assert.equal(s.photos.get('a'), a, 'Undo can still reach the step that showed it');
  assert.deepEqual(s.released, []);
  s.set([textItem(99)]);
  s.log.commit();
  assert.deepEqual(s.released, ['a']);
  assert.equal(canvas.width, 0);
  assert.equal(canvas.height, 0);
});

test('a photo still in use, being imported or not removed is never let go', () => {
  const s = setUp();
  const kept = s.photo('kept');
  const importing = s.photo('importing');
  importing.removed = true;
  importing.importing = true;
  s.set([textItem(1)]);
  s.log.commit();
  assert.deepEqual(s.released, []);
  assert.equal(s.photos.get('kept'), kept);
  assert.equal(s.photos.get('importing'), importing);
});

test('releasing a photo empties its canvas and is safe twice', () => {
  const canvas = { width: 10, height: 10 };
  const photo = { id: 'p', canvas, thumbnail: 'data:' };
  releasePhoto(photo);
  releasePhoto(photo);
  releasePhoto(null);
  assert.equal(canvas.width, 0);
  assert.equal(photo.canvas, null);
  assert.equal(photo.released, true);
});
