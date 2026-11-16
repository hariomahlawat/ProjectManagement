'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { createEditorState, createLaterParts } = require('./collage-state.js');

test('state reads the current values and cannot be written', () => {
  let zoom = 1;
  let selectedId = null;
  const { state, change } = createEditorState(
    { zoom: () => zoom, selectedId: () => selectedId },
    { choose: (id) => { selectedId = id; } }
  );
  assert.equal(state.zoom, 1);
  zoom = 2;
  assert.equal(state.zoom, 2, 'reads are live');
  assert.throws(() => { state.zoom = 3; }, TypeError, 'a part cannot set a value');
  assert.throws(() => { state.other = 1; }, TypeError, 'nor add one');
  assert.equal(zoom, 2);
  change.choose('a');
  assert.equal(state.selectedId, 'a', 'a named change is the way to change it');
  assert.throws(() => { change.choose = () => {}; }, TypeError, 'the changes are fixed too');
});

test('state values and changes must be functions', () => {
  assert.throws(() => createEditorState({ zoom: 1 }), TypeError);
  assert.throws(() => createEditorState({}, { choose: 'x' }), TypeError);
});

test('a later part is found once it exists, and reading it before then says so', () => {
  let renderProps;
  const parts = createLaterParts({ renderProps: () => renderProps });
  assert.throws(() => parts.renderProps, /not ready yet/);
  renderProps = () => 'drawn';
  assert.equal(parts.renderProps(), 'drawn');
  assert.throws(() => { parts.renderProps = null; }, TypeError);
});

test('no editor part writes the editor state itself, or reaches the old shared object', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  for (const name of ['page', 'drafts-screen', 'panels', 'tray', 'keys', 'add-panels', 'actions', 'start', 'toolbar', 'messages']) {
    const source = fs.readFileSync(path.join(__dirname, `collage-${name}.js`), 'utf8').replace(/\/\/.*$/gm, '');
    assert.doesNotMatch(source, /(?<![-\w])app\.\w/, `collage-${name}.js uses the old app object`);
    assert.doesNotMatch(source, /(\+\+|--)\s*state\.\w|state\.\w+\s*(\+\+|--|[-+*/]?=(?!=))/, `collage-${name}.js writes state`);
  }
});

test('no editor part is handed more than a dozen names', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  for (const name of ['page', 'drafts-screen', 'panels', 'tray', 'keys', 'actions', 'add-panels', 'start', 'toolbar', 'messages']) {
    const source = fs.readFileSync(path.join(__dirname, `collage-${name}.js`), 'utf8');
    const handed = source.match(/const \{([^}]*)\} = deps;/)[1].split(',').map((n) => n.trim()).filter(Boolean);
    assert.ok(handed.length <= 12, `collage-${name}.js is handed ${handed.length} names: ${handed.join(', ')}`);
  }
});
