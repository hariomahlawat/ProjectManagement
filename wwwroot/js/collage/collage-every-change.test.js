'use strict';

// Every change to the collage goes through the change log (docs/ux/collage-review-4-fixes.md,
// PR 12): a real collage, as the editor holds it, cannot be written in any of the places the
// editor's parts change (a photo's crop, the layout, the background, the items), and edit() leaves
// the collage it replaced as it was, so an Undo step taken before a change is never changed by it.

const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('./collage-core.js');
const { deepFreeze, createModelEditor } = require('./collage-model.js');
const { createChangeLog } = require('./collage-changes.js');

let nextId = 0;
const newId = () => `n${(nextId += 1)}`;

/** A collage as the editor holds one: a layout with two photos and an empty frame, words, and a gradient. */
function realCollage() {
  let collage = core.newCollage(core.DEFAULT_SIZE);
  collage = core.applyLayout(collage, 't3', 12, 8, ['p1', 'p2'], newId).collage;
  const words = core.textItem(newId(), 'Annual day', { x: 100, y: 100, width: 400 });
  collage = { ...collage, background: { type: 'gradient', key: Object.keys(core.GRADIENTS || { sunrise: 1 })[0] }, items: [...collage.items, words] };
  return collage;
}

/** The editor's wiring, as collage-app.js makes it: a frozen collage, edit(), and the change log. */
function editorWithLog() {
  let current = deepFreeze(realCollage());
  const kept = [];
  const log = createChangeLog({
    core,
    getModel: () => current,
    onKeep: () => kept.push('keep'),
    timers: { set: () => 0, clear: () => {} }
  });
  const editor = createModelEditor({
    getModel: () => current,
    setModel: (next) => { current = next; },
    keep: { commit: log.commit, record: log.record, hold: log.hold, live: () => {} }
  });
  return { editor, log, kept, get: () => current, set: (model) => { current = deepFreeze(model); } };
}

test('the collage the editor holds cannot be written where the parts change it', () => {
  const { get } = editorWithLog();
  const collage = get();
  const photo = collage.items.find((item) => item.kind === 'photo');
  const slot = collage.items.find((item) => item.kind === 'slot');
  const words = collage.items.find((item) => item.kind === 'text');
  assert.ok(photo && slot && words && collage.layout, 'the collage has a layout, a photo, an empty frame and words');

  const writes = {
    'a photo\'s crop': () => { photo.crop.x = 0.3; },
    'a photo\'s crop, replaced': () => { photo.crop = { ...photo.crop, zoom: 2 }; },
    'a photo\'s place': () => { photo.x += 10; },
    'a photo\'s corners': () => { photo.radius = 30; },
    'an empty frame\'s photo': () => { slot.photo = 'p9'; },
    'words': () => { words.text = 'Sports day'; },
    'the layout\'s space': () => { collage.layout.gap = 40; },
    'the layout\'s corners': () => { collage.layout.radius = 30; },
    'the layout': () => { collage.layout = null; },
    'the background\'s colour': () => { collage.background.key = 'other'; },
    'the background': () => { collage.background = { type: 'color', value: '#000000' }; },
    'the page size': () => { collage.size = 'a3-portrait'; },
    'the items': () => { collage.items = []; },
    'an item added': () => { collage.items.push({ id: 'x' }); },
    'an item removed': () => { collage.items.splice(0, 1); },
    'the order': () => { collage.items.reverse(); },
    'the order, sorted': () => { collage.items.sort(() => -1); },
    'a property added to an item': () => { photo.locked = true; },
    'a property removed from an item': () => { delete photo.crop; }
  };
  const before = JSON.stringify(collage);
  for (const [what, write] of Object.entries(writes)) {
    assert.throws(write, TypeError, `${what} could be written directly`);
  }
  assert.equal(JSON.stringify(collage), before, 'nothing was changed by the refused writes');
});

test('edit() leaves the collage it replaced untouched, so every Undo step is as it was taken', () => {
  const { editor, log, get } = editorWithLog();
  const first = get();
  const firstText = JSON.stringify(first);
  const photo = first.items.find((item) => item.kind === 'photo');

  // Changes made the ways the parts make them: a crop moved, the layout's space, the
  // background, an item added and the order changed.
  editor.editItem(photo.id, (item) => { item.crop = { ...item.crop, x: 0.25, zoom: 1.5 }; });
  editor.edit((draft) => { draft.layout.gap = 30; });
  editor.edit((draft) => { draft.background = { type: 'color', value: '#123456' }; });
  editor.edit((draft) => { draft.items.push(core.textItem('late', 'Later words', { x: 10, y: 10, width: 200 })); });
  editor.edit((draft) => { draft.items.reverse(); });

  assert.equal(JSON.stringify(first), firstText, 'the first collage was not changed by any edit');
  assert.ok(Object.isFrozen(first.items[0].crop), 'and it is still frozen');
  const last = get();
  assert.notEqual(last, first);
  assert.ok(Object.isFrozen(last) && Object.isFrozen(last.items) && Object.isFrozen(last.layout) && Object.isFrozen(last.background));
  for (const item of last.items) {
    assert.ok(Object.isFrozen(item), `${item.id} is frozen`);
    if (item.crop) assert.ok(Object.isFrozen(item.crop), `${item.id}'s crop is frozen`);
  }

  // Five changes, five Undo steps, each back to exactly what was there.
  let model = null;
  for (let step = 0; step < 5; step += 1) model = log.undo();
  assert.equal(JSON.stringify(model), firstText, 'five Undo steps bring back the first collage exactly');
  assert.equal(log.undo(), null, 'and there is no step before it');
});

test('a change made outside edit() reaches neither Undo nor the draft, because it cannot be made', () => {
  const { editor, log, kept, get } = editorWithLog();
  const photo = get().items.find((item) => item.kind === 'photo');
  // A part that forgot edit() and wrote the crop would throw before anything was recorded.
  assert.throws(() => { get().items.find((item) => item.id === photo.id).crop.zoom = 3; }, TypeError);
  assert.equal(kept.length, 0, 'nothing asked for the draft to be written');
  assert.equal(log.undo(), null, 'nothing reached Undo');

  // The same change through editItem is one Undo step, and is kept in the draft.
  editor.editItem(photo.id, (item) => { item.crop = { ...item.crop, zoom: 3 }; });
  assert.equal(get().items.find((item) => item.id === photo.id).crop.zoom, 3);
  assert.equal(kept.length, 1);
  const back = log.undo();
  assert.equal(back.items.find((item) => item.id === photo.id).crop.zoom, photo.crop.zoom);
});

test('a collage put back by Undo is frozen again once the editor holds it', () => {
  const { editor, log, set, get } = editorWithLog();
  editor.edit((draft) => { draft.layout.gap = 30; });
  // The editor holds what Undo gives back through setCollage, which freezes it.
  set(log.undo());
  assert.throws(() => { get().layout.gap = 5; }, TypeError);
  assert.throws(() => { get().items[0].crop.x = 0.5; }, TypeError);
});
