const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { JSDOM } = require('jsdom');

// SECTION: ESM loader helper with local dependency stubs.
async function loadEditorModule() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'notebook-editor-test-'));
  fs.writeFileSync(path.join(tempDir, 'package.json'), '{"type":"module"}');
  fs.writeFileSync(path.join(tempDir, 'notebook-api.mjs'), "export class NotebookApiError extends Error { constructor(message, options = {}) { super(message); Object.assign(this, options); } } export const NotebookApi = {};\n");
  fs.writeFileSync(path.join(tempDir, 'notebook-autosave.mjs'), 'export function createAutosave() { return {}; }\n');
  fs.writeFileSync(path.join(tempDir, 'notebook-checklist-editor.mjs'), 'export function createChecklistEditor() { return {}; }\n');
  fs.writeFileSync(path.join(tempDir, 'notebook-reconcile.mjs'), 'export function reconcileMutation() {} export function requireMutationItem(value) { return value?.item ?? value; }\n');
  fs.writeFileSync(path.join(tempDir, 'notebook-errors.mjs'), fs.readFileSync(path.resolve(__dirname, 'notebook-errors.js'), 'utf8'));
  fs.writeFileSync(path.join(tempDir, 'notebook-colour-picker.mjs'), fs.readFileSync(path.resolve(__dirname, 'notebook-colour-picker.js'), 'utf8'));
  fs.writeFileSync(path.join(tempDir, 'notebook-label-picker.mjs'), fs.readFileSync(path.resolve(__dirname, 'notebook-label-picker.js'), 'utf8').replace("./notebook-api.js", './notebook-api.mjs'));
  const source = fs.readFileSync(path.resolve(__dirname, 'notebook-editor.js'), 'utf8')
    .replace("./notebook-api.js", './notebook-api.mjs')
    .replace("./notebook-autosave.js", './notebook-autosave.mjs')
    .replace("./notebook-checklist-editor.js", './notebook-checklist-editor.mjs')
    .replace("./notebook-reconcile.js", './notebook-reconcile.mjs')
    .replace("./notebook-errors.js", './notebook-errors.mjs')
    .replace("./notebook-colour-picker.js", './notebook-colour-picker.mjs')
    .replace("./notebook-label-picker.js", './notebook-label-picker.mjs');
  const modulePath = path.join(tempDir, 'notebook-editor.mjs');
  fs.writeFileSync(modulePath, source);
  return import(`file://${modulePath}`);
}

test('buildUpdatePayload returns normal-note content payload only', async () => {
  const { buildUpdatePayload } = await loadEditorModule();
  const version = '123e4567-e89b-12d3-a456-426614174000';

  assert.deepEqual(buildUpdatePayload({ title: ' Hello ', body: ' Body ', version, type: 'Note', checklistRows: [] }), {
    title: 'Hello',
    body: 'Body'
  });
});


test('buildUpdatePayload keeps checklist rows for checklist content updates', async () => {
  const { buildUpdatePayload } = await loadEditorModule();
  const version = '123e4567-e89b-12d3-a456-426614174000';
  const checklistRows = [{ id: 7, text: 'Ship fix', isDone: true, sortOrder: 1000 }];

  assert.deepEqual(buildUpdatePayload({ title: ' Tasks ', body: '', version, type: 'Checklist', checklistRows }), {
    title: 'Tasks',
    body: '',
    checklistRows
  });
});

test('assertValidVersion rejects non-guid local versions before PATCH', async () => {
  const { assertValidVersion } = await loadEditorModule();

  assert.throws(() => assertValidVersion('not-a-guid'), (error) => error.code === 'notebook_invalid_local_version');
});

test('shouldTreatDraftAsConflict detects stale source versions only', async () => {
  const { shouldTreatDraftAsConflict } = await loadEditorModule();
  const current = { version: '123e4567-e89b-12d3-a456-426614174000' };

  assert.equal(shouldTreatDraftAsConflict({ sourceVersion: current.version }, current), false);
  assert.equal(shouldTreatDraftAsConflict({ sourceVersion: '223e4567-e89b-12d3-a456-426614174000' }, current), true);
  assert.equal(shouldTreatDraftAsConflict({ sourceVersion: null }, current), false);
});

test('serialiseNotebookContent includes checklist state for safe copy', async () => {
  const { serialiseNotebookContent } = await loadEditorModule();

  assert.equal(serialiseNotebookContent({
    title: 'Release tasks',
    body: 'Before production',
    type: 'Checklist',
    checklistRows: [
      { text: 'Run tests', isDone: true },
      { text: 'Deploy', isDone: false }
    ]
  }), 'Release tasks\n\nBefore production\n\n☑ Run tests\n☐ Deploy');
});

test('serialiseNotebookContent omits empty checklist rows', async () => {
  const { serialiseNotebookContent } = await loadEditorModule();

  assert.equal(serialiseNotebookContent({
    title: '',
    body: '',
    type: 'Checklist',
    checklistRows: [{ text: '   ', isDone: false }, { text: 'Keep this', isDone: false }]
  }), '☐ Keep this');
});

test('shouldIgnoreSaveResult rejects pre-conflict responses only', async () => {
  const { shouldIgnoreSaveResult } = await loadEditorModule();

  assert.equal(shouldIgnoreSaveResult({ conflictGenerationAtDispatch: 2 }, 3), true);
  assert.equal(shouldIgnoreSaveResult({ conflictGenerationAtDispatch: 3 }, 3), false);
  assert.equal(shouldIgnoreSaveResult({}, 3), false);
});


test('Notebook editor template exposes the required modal contract', async () => {
  const { cloneNotebookEditorTemplate, EditorSelectors } = await loadEditorModule();
  const templateMarkup = fs.readFileSync(
    path.resolve(__dirname, '../../../Pages/Notebook/_NotebookEditorTemplate.cshtml'),
    'utf8'
  );
  const dom = new JSDOM(`<!doctype html><body>${templateMarkup}</body>`);

  const editor = cloneNotebookEditorTemplate(dom.window.document);

  assert.equal(editor.matches(EditorSelectors.editor), true);
  assert.equal(editor.hidden, true);
  assert.equal(editor.getAttribute('role'), 'dialog');
  assert.equal(editor.getAttribute('aria-modal'), 'true');
  assert.equal(editor.querySelectorAll(EditorSelectors.title).length, 1);
  assert.equal(editor.querySelectorAll(EditorSelectors.body).length, 1);
  assert.equal(editor.querySelectorAll(EditorSelectors.checklist).length, 1);
  assert.equal(editor.querySelectorAll(EditorSelectors.conflict).length, 1);
  assert.equal(editor.querySelector(EditorSelectors.conflict).hidden, true);
  assert.equal(editor.querySelectorAll('[data-notebook-use-local]').length, 1);
  assert.equal(editor.querySelectorAll('[data-notebook-reload-latest]').length, 1);
  assert.equal(editor.querySelectorAll('[data-notebook-copy-local]').length, 1);
  assert.equal(editor.querySelectorAll('button[type="submit"]').length, 0);
});

test('Notebook editor template clone is independent across repeated opens', async () => {
  const { cloneNotebookEditorTemplate } = await loadEditorModule();
  const templateMarkup = fs.readFileSync(
    path.resolve(__dirname, '../../../Pages/Notebook/_NotebookEditorTemplate.cshtml'),
    'utf8'
  );
  const dom = new JSDOM(`<!doctype html><body>${templateMarkup}</body>`);

  const first = cloneNotebookEditorTemplate(dom.window.document);
  const second = cloneNotebookEditorTemplate(dom.window.document);

  first.querySelector('[data-modal-title]').value = 'First note';
  assert.notEqual(first, second);
  assert.equal(second.querySelector('[data-modal-title]').value, '');
});

test('Notebook editor template clone fails clearly when the template is missing', async () => {
  const { cloneNotebookEditorTemplate } = await loadEditorModule();
  const dom = new JSDOM('<!doctype html><body></body>');

  assert.throws(
    () => cloneNotebookEditorTemplate(dom.window.document),
    (error) => error.code === 'notebook_editor_template_missing'
  );
});

test('Notebook Index renders the editor template partial', () => {
  const page = fs.readFileSync(
    path.resolve(__dirname, '../../../Pages/Notebook/Index.cshtml'),
    'utf8'
  );

  assert.match(page, /<partial\s+name="_NotebookEditorTemplate"\s*\/>/);
});
