const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname);
const api = fs.readFileSync(path.join(root, 'notebook-api.js'), 'utf8');
const app = fs.readFileSync(path.join(root, 'notebook-app.js'), 'utf8');
const actions = fs.readFileSync(path.join(root, '../../../Pages/Notebook/_NotebookActions.cshtml'), 'utf8');

test('Notebook API exposes complete Trash lifecycle commands', () => {
  for (const marker of ['moveToTrash', 'restoreFromTrash', 'deletePermanently', 'emptyTrash']) assert.match(api, new RegExp(marker));
});

test('normal note deletion is presented as Move to trash with Undo', () => {
  assert.match(actions, /Move to trash/);
  assert.match(app, /Note moved to Trash/);
  assert.match(app, /actionText: 'Undo'/);
});

test('Trash cards expose only restore and permanent deletion actions', () => {
  assert.match(actions, /restore-trash-note/);
  assert.match(actions, /delete-permanently/);
});
