// Tests for the change log (docs/ux/collage-review-2-fixes.md, PR 4): every change reaches Undo
// and the draft one way, and a held change (typing, a nudge) is its own Undo step before the next
// action changes anything.
const test = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const core = require('./collage-core.js');
const { createChangeLog, isBoundary } = require('./collage-changes.js');

function fakeTimers() {
  let next = 1;
  const waiting = new Map();
  return {
    set: (fn) => { const id = next++; waiting.set(id, fn); return id; },
    clear: (id) => waiting.delete(id),
    fire: () => { const all = [...waiting.values()]; waiting.clear(); all.forEach((fn) => fn()); },
    waiting
  };
}

function setUp() {
  const timers = fakeTimers();
  let model = { size: 'square', items: [{ id: 't1', kind: 'text', text: 'Hello', bold: false }] };
  const recorded = [];
  let keeps = 0;
  const log = createChangeLog({
    core,
    getModel: () => model,
    onRecorded: (kind) => recorded.push(kind),
    onKeep: () => { keeps += 1; },
    timers
  });
  return {
    log, timers, recorded,
    get keeps() { return keeps; },
    get model() { return model; },
    set model(next) { model = next; }
  };
}

test('typing, then a click on Bold before the pause, is two Undo steps', () => {
  const t = setUp();
  const text = t.model.items[0];
  text.text = 'Hello Col';
  t.log.hold();
  text.text = 'Hello Col Rao';
  t.log.hold();
  assert.equal(t.recorded.length, 0);
  // The click starts a new action: the editor calls flush() at its boundary, then commits Bold.
  t.log.flush();
  text.bold = true;
  t.log.commit();
  assert.deepEqual(t.recorded, ['quiet', 'commit']);
  const back = t.log.undo();
  assert.equal(back.items[0].bold, false, 'Undo takes back Bold only');
  assert.equal(back.items[0].text, 'Hello Col Rao', 'the words stay');
  const before = t.log.undo();
  assert.equal(before.items[0].text, 'Hello');
});

test('a held change becomes one step when it pauses, and is kept in the draft at once', () => {
  const t = setUp();
  t.model.items[0].text = 'A';
  t.log.hold(600);
  t.model.items[0].text = 'AB';
  t.log.hold(600);
  assert.equal(t.keeps, 2);
  assert.equal(t.log.pending, true);
  t.timers.fire();
  assert.deepEqual(t.recorded, ['quiet']);
  assert.equal(t.log.pending, false);
  assert.equal(t.log.canUndo(), true);
});

test('Undo records a held change first, so it is never skipped', () => {
  const t = setUp();
  t.model.items[0].text = 'Typed';
  t.log.hold();
  const back = t.log.undo();
  assert.equal(back.items[0].text, 'Hello');
  t.model = back;
  const again = t.log.redo();
  assert.equal(again.items[0].text, 'Typed');
});

test('a new collage starts Undo again and forgets a held change', () => {
  const t = setUp();
  t.model.items[0].text = 'Typed';
  t.log.hold();
  t.model = { size: 'square', items: [] };
  t.log.start(t.model);
  assert.equal(t.log.pending, false);
  assert.equal(t.log.canUndo(), false);
  assert.equal(t.log.undo(), null);
  assert.equal(t.timers.waiting.size, 0);
});

test('Undo knows which photos its steps show', () => {
  const t = setUp();
  t.model.items.push({ id: 'i1', kind: 'photo', photo: 'p7' });
  t.log.commit();
  t.model.items.pop();
  t.log.commit();
  assert.equal(t.log.mentions('p7'), true);
  assert.equal(t.log.mentions('p8'), false);
});

test('a boundary is a pointer pressed, focus moved, or a key that is not typing or a nudge', () => {
  const dom = new JSDOM('<body><textarea></textarea><button>Bold</button></body>');
  const doc = dom.window.document;
  const field = doc.querySelector('textarea');
  const button = doc.querySelector('button');
  const key = (target, key, extra = {}) => ({ type: 'keydown', key, target, ...extra });
  assert.equal(isBoundary({ type: 'pointerdown', target: button }), true);
  assert.equal(isBoundary({ type: 'focusin', target: button }), true);
  assert.equal(isBoundary(key(field, 'a')), false, 'typing is the held change');
  assert.equal(isBoundary(key(field, 'Enter')), false);
  assert.equal(isBoundary(key(field, 'z', { ctrlKey: true })), true);
  assert.equal(isBoundary(key(doc.body, 'ArrowLeft')), false, 'a nudge goes on');
  assert.equal(isBoundary(key(doc.body, 'Shift')), false);
  assert.equal(isBoundary(key(button, 'Enter')), true, 'Enter on Bold starts a new action');
  assert.equal(isBoundary(key(doc.body, 'Delete')), true);
  assert.equal(isBoundary({ type: 'input', target: field }), false);
});
