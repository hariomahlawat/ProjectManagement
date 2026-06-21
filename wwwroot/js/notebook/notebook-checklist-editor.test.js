const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { JSDOM } = require('jsdom');

// SECTION: ESM loader helper for checklist editor module.
async function loadChecklistModule() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'notebook-checklist-test-'));
  fs.writeFileSync(path.join(tempDir, 'package.json'), '{"type":"module"}');
  const source = fs.readFileSync(path.resolve(__dirname, 'notebook-checklist-editor.js'), 'utf8');
  const modulePath = path.join(tempDir, `notebook-checklist-editor-${Date.now()}-${Math.random()}.mjs`);
  fs.writeFileSync(modulePath, source);
  return import(`file://${modulePath}`);
}

// SECTION: DOM setup helper.
async function createEditor(onChange = () => {}) {
  const dom = new JSDOM('<!doctype html><div id="root"></div>', { pretendToBeVisual: true });
  global.window = dom.window;
  global.document = dom.window.document;
  const { createChecklistEditor } = await loadChecklistModule();
  const root = document.getElementById('root');
  return { editor: createChecklistEditor(root, { onChange }), root, dom };
}

test('reconcileRows hydrates server id by client key for next getRows call', async () => {
  const { editor } = await createEditor();
  editor.setRows([{ id: null, clientKey: 'client-row-1', text: 'New item', isDone: false, sortOrder: 0 }]);

  editor.reconcileRows([{ id: 143, clientKey: 'client-row-1', text: 'New item', isDone: false, sortOrder: 0 }], [{ id: null, clientKey: 'client-row-1', text: 'New item', isDone: false, sortOrder: 0 }]);

  assert.equal(editor.getRows()[0].id, 143);
  assert.equal(editor.getRows()[0].clientKey, 'client-row-1');
});

test('reconcileRows retains focus and caret while hydrating id', async () => {
  const { editor, root } = await createEditor();
  editor.setRows([{ id: null, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }]);
  const input = root.querySelector('[data-checklist-text]');
  input.focus();
  input.setSelectionRange(2, 2);

  editor.reconcileRows([{ id: 144, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }], [{ id: null, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }]);

  assert.equal(document.activeElement, root.querySelector('[data-checklist-text]'));
  assert.equal(document.activeElement.selectionStart, 2);
  assert.equal(editor.getRows()[0].id, 144);
});

test('reconcileRows preserves local text changed after submitted snapshot', async () => {
  const { editor, root } = await createEditor();
  editor.setRows([{ id: null, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }]);
  root.querySelector('[data-checklist-text]').value = 'Item updated';

  editor.reconcileRows([{ id: 145, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }], [{ id: null, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }]);

  assert.equal(editor.getRows()[0].id, 145);
  assert.equal(editor.getRows()[0].text, 'Item updated');
});

test('reconcileRows does not notify onChange and cause a save loop', async () => {
  let changes = 0;
  const { editor } = await createEditor(() => { changes += 1; });
  editor.setRows([{ id: null, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }]);

  editor.reconcileRows([{ id: 146, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }], [{ id: null, clientKey: 'client-row-1', text: 'Item', isDone: false, sortOrder: 0 }]);

  assert.equal(changes, 0);
});

test('reconcileRows removes stale DOM rows that are absent from authoritative result', async () => {
  const { editor, root } = await createEditor();
  editor.setRows([
    { id: 1, text: 'A', isDone: false, sortOrder: 0 },
    { id: 2, text: 'B', isDone: false, sortOrder: 1 },
    { id: 3, text: 'C', isDone: false, sortOrder: 2 }
  ]);

  editor.reconcileRows([
    { id: 1, text: 'A', isDone: false, sortOrder: 0 },
    { id: 3, text: 'C', isDone: false, sortOrder: 1 }
  ], []);

  assert.deepEqual(editor.getRows().map((row) => row.text), ['A', 'C']);
  assert.deepEqual([...root.querySelectorAll('[data-checklist-row] [data-checklist-text]')].map((input) => input.value), ['A', 'C']);
  assert.equal(root.querySelector('[data-row-id="2"]'), null);
});

test('reconcileRows accepts an empty authoritative result and leaves only add control', async () => {
  const { editor, root } = await createEditor();
  editor.setRows([{ id: 1, text: 'A', isDone: false, sortOrder: 0 }]);

  editor.reconcileRows([], [{ id: 1, text: 'A', isDone: false, sortOrder: 0 }]);

  assert.deepEqual(editor.getRows(), []);
  assert.equal(root.querySelectorAll('[data-checklist-row]').length, 0);
  assert.equal(root.querySelectorAll('[data-checklist-add]').length, 1);
});

test('reconcileRows hydrates duplicate text rows by client key rather than text', async () => {
  const { editor } = await createEditor();
  editor.setRows([
    { id: null, clientKey: 'client-a', text: 'Task', isDone: false, sortOrder: 0 },
    { id: null, clientKey: 'client-b', text: 'Task', isDone: false, sortOrder: 1 }
  ]);

  editor.reconcileRows([
    { id: 21, clientKey: 'client-a', text: 'Task', isDone: false, sortOrder: 0 },
    { id: 22, clientKey: 'client-b', text: 'Task', isDone: false, sortOrder: 1 }
  ], [
    { id: null, clientKey: 'client-a', text: 'Task', isDone: false, sortOrder: 0 },
    { id: null, clientKey: 'client-b', text: 'Task', isDone: false, sortOrder: 1 }
  ]);

  assert.deepEqual(editor.getRows().map((row) => row.id), [21, 22]);
});

test('destroy removes root event listeners before a new editor is created', async () => {
  let changes = 0;
  const { editor, root } = await createEditor(() => { changes += 1; });
  editor.setRows([{ id: 1, text: 'A', isDone: false, sortOrder: 0 }]);
  editor.destroy();

  const { createChecklistEditor } = await loadChecklistModule();
  const second = createChecklistEditor(root, { onChange: () => { changes += 1; } });
  second.setRows([{ id: 1, text: 'A', isDone: false, sortOrder: 0 }]);
  const input = root.querySelector('[data-checklist-text]');
  input.value = 'B';
  input.dispatchEvent(new window.Event('input', { bubbles: true }));

  assert.equal(changes, 1);
});

test('reconcileRows preserves row added after dispatch', async () => {
  const { editor } = await createEditor();
  editor.setRows([
    { id: 1, text: 'Submitted', isDone: false, sortOrder: 0 },
    { id: null, clientKey: 'later-row', text: 'Later', isDone: false, sortOrder: 1 }
  ]);

  editor.reconcileRows([
    { id: 1, text: 'Submitted', isDone: false, sortOrder: 0 }
  ], [
    { id: 1, text: 'Submitted', isDone: false, sortOrder: 0 }
  ]);

  assert.deepEqual(editor.getRows().map((row) => row.text), ['Submitted', 'Later']);
});

test('reconcileRows preserves row deleted after dispatch', async () => {
  const { editor, root } = await createEditor();
  editor.setRows([
    { id: 1, text: 'Kept', isDone: false, sortOrder: 0 }
  ]);
  editor.removeRow(root.querySelector('[data-checklist-row]'));

  editor.reconcileRows([
    { id: 1, text: 'Kept', isDone: false, sortOrder: 0 }
  ], [
    { id: 1, text: 'Kept', isDone: false, sortOrder: 0 }
  ]);

  assert.deepEqual(editor.getRows(), []);
});

test('reconcileRows preserves checkbox changed after dispatch', async () => {
  const { editor, root } = await createEditor();
  editor.setRows([{ id: 1, text: 'Task', isDone: false, sortOrder: 0 }]);
  root.querySelector('[data-checklist-done]').checked = true;

  editor.reconcileRows([
    { id: 1, text: 'Task', isDone: false, sortOrder: 0 }
  ], [
    { id: 1, text: 'Task', isDone: false, sortOrder: 0 }
  ]);

  assert.equal(editor.getRows()[0].isDone, true);
});
