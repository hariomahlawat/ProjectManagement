const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

// SECTION: ESM loader helper with local dependency stubs.
async function loadEditorModule() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'notebook-editor-test-'));
  fs.writeFileSync(path.join(tempDir, 'package.json'), '{"type":"module"}');
  fs.writeFileSync(path.join(tempDir, 'notebook-api.mjs'), "export class NotebookApiError extends Error { constructor(message, options = {}) { super(message); Object.assign(this, options); } } export const NotebookApi = {};\n");
  fs.writeFileSync(path.join(tempDir, 'notebook-autosave.mjs'), 'export function createAutosave() { return {}; }\n');
  fs.writeFileSync(path.join(tempDir, 'notebook-checklist-editor.mjs'), 'export function createChecklistEditor() { return {}; }\n');
  fs.writeFileSync(path.join(tempDir, 'notebook-reconcile.mjs'), 'export function reconcileMutation() {} export function requireMutationItem(value) { return value?.item ?? value; }\n');
  fs.writeFileSync(path.join(tempDir, 'notebook-errors.mjs'), fs.readFileSync(path.resolve(__dirname, 'notebook-errors.js'), 'utf8'));
  const source = fs.readFileSync(path.resolve(__dirname, 'notebook-editor.js'), 'utf8')
    .replace("./notebook-api.js", './notebook-api.mjs')
    .replace("./notebook-autosave.js", './notebook-autosave.mjs')
    .replace("./notebook-checklist-editor.js", './notebook-checklist-editor.mjs')
    .replace("./notebook-reconcile.js", './notebook-reconcile.mjs')
    .replace("./notebook-errors.js", './notebook-errors.mjs');
  const modulePath = path.join(tempDir, 'notebook-editor.mjs');
  fs.writeFileSync(modulePath, source);
  return import(`file://${modulePath}`);
}

test('buildUpdatePayload returns normal-note content payload only', async () => {
  const { buildUpdatePayload } = await loadEditorModule();
  const version = '123e4567-e89b-12d3-a456-426614174000';

  assert.deepEqual(buildUpdatePayload({ title: ' Hello ', body: ' Body ', version, type: 'Note', checklistRows: [] }), {
    title: 'Hello',
    body: 'Body',
    version
  });
});


test('buildUpdatePayload keeps checklist rows for checklist content updates', async () => {
  const { buildUpdatePayload } = await loadEditorModule();
  const version = '123e4567-e89b-12d3-a456-426614174000';
  const checklistRows = [{ id: 7, text: 'Ship fix', isDone: true, sortOrder: 1000 }];

  assert.deepEqual(buildUpdatePayload({ title: ' Tasks ', body: '', version, type: 'Checklist', checklistRows }), {
    title: 'Tasks',
    body: '',
    version,
    checklistRows
  });
});

test('assertValidVersion rejects non-guid local versions before PATCH', async () => {
  const { assertValidVersion } = await loadEditorModule();

  assert.throws(() => assertValidVersion('not-a-guid'), (error) => error.code === 'notebook_invalid_local_version');
});
