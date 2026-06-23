const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function createDocument(catalogueJson = '[]') {
  const listeners = new Map();
  const events = [];
  const script = { textContent: catalogueJson };
  const documentRef = {
    defaultView: {
      CustomEvent: class CustomEvent {
        constructor(type, init) {
          this.type = type;
          this.detail = init?.detail;
        }
      }
    },
    querySelector: (selector) => selector === '#notebook-label-catalog' ? script : null,
    dispatchEvent: (event) => {
      events.push(event);
      (listeners.get(event.type) || []).forEach((handler) => handler(event));
      return true;
    },
    addEventListener: (type, handler) => {
      const handlers = listeners.get(type) || [];
      handlers.push(handler);
      listeners.set(type, handlers);
    },
    removeEventListener: (type, handler) => {
      listeners.set(type, (listeners.get(type) || []).filter((candidate) => candidate !== handler));
    },
    createElement: () => ({})
  };
  return { documentRef, events, listeners, script };
}

function load(documentRef = createDocument().documentRef) {
  let source = fs.readFileSync(path.join(__dirname, 'notebook-label-picker.js'), 'utf8');
  source = source
    .replace("import { NotebookApi } from './notebook-api.js';", 'const NotebookApi = {};')
    .replace(/export async function /g, 'async function ')
    .replace(/export function /g, 'function ');
  source += `\nmodule.exports={
    normaliseLabelName,
    normaliseLabels,
    setNotebookLabelCatalog,
    hydrateNotebookLabelCatalog,
    getNotebookLabelCatalog,
    resetNotebookLabelCatalogForTests,
    initNotebookLabelPicker
  };`;
  const context = {
    module: { exports: {} },
    exports: {},
    document: documentRef,
    window: { innerWidth: 1280, innerHeight: 800 },
    queueMicrotask,
    CustomEvent: documentRef.defaultView.CustomEvent,
    globalThis: { CustomEvent: documentRef.defaultView.CustomEvent },
    console
  };
  vm.runInNewContext(source, context);
  return context.module.exports;
}

test('label names are trimmed and hashes removed', () => {
  const { normaliseLabelName } = load();
  assert.equal(normaliseLabelName(' ## Procurement '), 'Procurement');
});

test('labels are deduplicated case-insensitively', () => {
  const { normaliseLabels } = load();
  assert.deepEqual(Array.from(normaliseLabels(['Docs', ' docs ', '#OPS', 'ops'])), ['Docs', 'OPS']);
});

test('empty labels are removed from picker values', () => {
  const { normaliseLabels } = load();
  assert.deepEqual(Array.from(normaliseLabels(['', '  ', '#', 'Work'])), ['Work']);
});

test('empty embedded catalogue hydrates once without dispatching a change event', () => {
  const { documentRef, events } = createDocument('[]');
  const api = load(documentRef);

  assert.deepEqual(Array.from(api.hydrateNotebookLabelCatalog(documentRef)), []);
  assert.deepEqual(Array.from(api.hydrateNotebookLabelCatalog(documentRef)), []);
  assert.equal(events.filter((event) => event.type === 'notebook:labels-changed').length, 0);
});

test('getNotebookLabelCatalog is side-effect free after empty hydration', () => {
  const { documentRef, events } = createDocument('[]');
  const api = load(documentRef);
  api.hydrateNotebookLabelCatalog(documentRef);

  for (let index = 0; index < 20; index += 1) {
    assert.deepEqual(Array.from(api.getNotebookLabelCatalog()), []);
  }

  assert.equal(events.length, 0);
});

test('identical catalogue assignments emit only one change event', () => {
  const { documentRef, events } = createDocument('[]');
  const api = load(documentRef);
  api.hydrateNotebookLabelCatalog(documentRef);
  const labels = [{ id: 1, name: 'Work', count: 0 }];

  api.setNotebookLabelCatalog(labels, documentRef);
  api.setNotebookLabelCatalog(labels, documentRef);

  const changes = events.filter((event) => event.type === 'notebook:labels-changed');
  assert.equal(changes.length, 1);
  assert.equal(changes[0].detail.labels[0].name, 'Work');
});

test('catalogue setter returns defensive copies', () => {
  const { documentRef } = createDocument('[]');
  const api = load(documentRef);
  api.hydrateNotebookLabelCatalog(documentRef);
  const assigned = api.setNotebookLabelCatalog([{ id: 1, name: 'Work', count: 2 }], documentRef);
  assigned[0].name = 'Changed';

  assert.equal(api.getNotebookLabelCatalog()[0].name, 'Work');
});

test('malformed embedded catalogue hydrates as empty without recursion', () => {
  const { documentRef, events } = createDocument('{bad json');
  const api = load(documentRef);

  assert.doesNotThrow(() => api.hydrateNotebookLabelCatalog(documentRef));
  assert.deepEqual(Array.from(api.getNotebookLabelCatalog()), []);
  assert.equal(events.length, 0);
});
