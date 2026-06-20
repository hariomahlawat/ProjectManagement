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
