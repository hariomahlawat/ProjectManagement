const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadModule() {
  const file = path.join(__dirname, 'notebook-create-draft.js');
  let source = fs.readFileSync(file, 'utf8');
  source = source
    .replace(/export const /g, 'const ')
    .replace(/export function /g, 'function ')
    .concat('\nmodule.exports = { NOTEBOOK_CREATE_DRAFT_VERSION, hasMeaningfulCreateDraft, createNotebookCreateDraftStore };');
  const context = { module: { exports: {} }, exports: {}, Date, String, Boolean, Array, JSON };
  vm.createContext(context);
  vm.runInContext(source, context);
  return context.module.exports;
}

function memoryStorage() {
  const values = new Map();
  return {
    getItem: (key) => values.has(key) ? values.get(key) : null,
    setItem: (key, value) => values.set(key, String(value)),
    removeItem: (key) => values.delete(key)
  };
}

test('default reminder schedule alone is not treated as user data', () => {
  const { hasMeaningfulCreateDraft } = loadModule();
  assert.equal(hasMeaningfulCreateDraft({ type: 'Reminder', reminderDate: '2026-07-19', reminderTime: '09:00', scheduleTouched: false }), false);
  assert.equal(hasMeaningfulCreateDraft({ type: 'Reminder', reminderDate: '2026-07-19', reminderTime: '09:00', scheduleTouched: true }), true);
});

test('draft storage is scoped by authenticated user and item type', () => {
  const { createNotebookCreateDraftStore } = loadModule();
  const storage = memoryStorage();
  const nowProvider = () => new Date('2026-07-18T00:00:00.000Z');
  const owner = createNotebookCreateDraftStore({ storage, userId: 'owner-1', nowProvider });
  const other = createNotebookCreateDraftStore({ storage, userId: 'owner-2', nowProvider });
  owner.save('Reminder', { title: 'Call HQ' });
  assert.equal(owner.load('Reminder').title, 'Call HQ');
  assert.equal(owner.load('Note'), null);
  assert.equal(other.load('Reminder'), null);
});

test('corrupt or mismatched drafts are removed instead of restored', () => {
  const { createNotebookCreateDraftStore } = loadModule();
  const storage = memoryStorage();
  const store = createNotebookCreateDraftStore({ storage, userId: 'owner-1' });
  storage.setItem(store.keyFor('Note'), '{bad-json');
  assert.equal(store.load('Note'), null);
});
