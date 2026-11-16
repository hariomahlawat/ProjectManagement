'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { deepFreeze, createModelEditor } = require('./collage-model.js');

function editorFor(model) {
  let current = deepFreeze(model);
  const kept = [];
  const keep = { commit: () => kept.push('commit'), hold: () => kept.push('hold'), record: () => kept.push('record'), live: () => kept.push('live') };
  const editor = createModelEditor({ getModel: () => current, setModel: (next) => { current = next; }, keep });
  return { editor, kept, get: () => current };
}

test('the collage cannot be changed in place', () => {
  const { get } = editorFor({ items: [{ id: 'a', x: 1, crop: { zoom: 1 } }], background: { type: 'color' } });
  assert.throws(() => { get().items[0].x = 2; }, TypeError);
  assert.throws(() => { get().items[0].crop.zoom = 2; }, TypeError);
  assert.throws(() => { get().items.push({}); }, TypeError);
  assert.throws(() => { get().background = null; }, TypeError);
});

test('edit changes a copy, freezes it, and keeps it the way it was asked', () => {
  const { editor, kept, get } = editorFor({ items: [{ id: 'a', x: 1 }] });
  const before = get();
  editor.edit((draft) => { draft.items[0].x = 5; });
  assert.equal(get().items[0].x, 5);
  assert.equal(before.items[0].x, 1, 'the old collage is untouched (Undo keeps its own copy)');
  assert.ok(Object.isFrozen(get().items[0]));
  editor.edit((draft) => { draft.items.push({ id: 'b' }); }, 'hold');
  editor.edit(() => {}, 'record');
  editor.edit(() => {}, 'live');
  editor.edit(() => {}, 'none');
  assert.deepEqual(kept, ['commit', 'hold', 'record', 'live']);
  assert.throws(() => editor.edit(() => {}, 'later'), /Unknown way/);
});

test('edit takes a new collage returned by the change, and passes other answers back', () => {
  const { editor, get } = editorFor({ items: [{ id: 'a' }] });
  editor.edit(() => ({ items: [], layout: null }));
  assert.deepEqual(get().items, []);
  const answer = editor.edit((draft) => { draft.items.push({ id: 'z' }); return true; });
  assert.equal(answer, true);
  assert.equal(get().items.length, 1);
});

test('editItem changes one item, and nothing when it is gone', () => {
  const { editor, kept, get } = editorFor({ items: [{ id: 'a', angle: 0 }, { id: 'b', angle: 0 }] });
  editor.editItem('b', (item) => { item.angle = 15; });
  assert.deepEqual(get().items.map((i) => i.angle), [0, 15]);
  editor.editItem('gone', (item) => { item.angle = 5; });
  assert.deepEqual(get().items.map((i) => i.angle), [0, 15]);
  assert.equal(kept.length, 2);
});

test('no editor part writes the collage directly', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  for (const name of ['app', 'page', 'panels', 'tray', 'keys', 'actions', 'add-panels', 'drafts-screen', 'toolbar', 'start', 'messages']) {
    const source = fs.readFileSync(path.join(__dirname, `collage-${name}.js`), 'utf8').replace(/\/\/.*$/gm, '');
    assert.doesNotMatch(source, /state\.collage\.[\w.]+\s*=(?!=)|state\.collage\.items\.(push|splice|sort|reverse|unshift|pop|shift)\(/, `collage-${name}.js writes state.collage`);
    assert.doesNotMatch(source, /(?<![.\w])collage\.(items|layout|background|size|name)\s*=(?!=)|(?<![.\w])collage\.items\.(push|splice)\(/, `collage-${name}.js writes the collage`);
    assert.doesNotMatch(source, /(selectedItem|itemById)\([^)]*\)\??\.\w+\s*=(?!=)/, `collage-${name}.js writes an item it read`);
  }
});
