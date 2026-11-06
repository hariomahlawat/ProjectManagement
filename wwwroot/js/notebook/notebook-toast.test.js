const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const { JSDOM } = require('jsdom');

function load() {
  const dom = new JSDOM('<!doctype html><body><div data-notebook-toast-region></div></body>');
  let source = fs.readFileSync(path.join(__dirname, 'notebook-toast.js'), 'utf8');
  source = source.replace(/export function /g, 'function ');
  source += '\nmodule.exports={initNotebookToastRegion,showNotebookToast,clearNotebookToasts};';
  const context = { module: { exports: {} }, exports: {}, document: dom.window.document, window: dom.window, console };
  vm.runInNewContext(source, context);
  return { api: context.module.exports, dom };
}

test('toast renders accessible message and dismisses', () => {
  const { api, dom } = load();
  api.initNotebookToastRegion();
  api.showNotebookToast({ message: 'Label deleted.', duration: 0 });
  const toast = dom.window.document.querySelector('.notebook-toast');
  assert.equal(toast.textContent.includes('Label deleted.'), true);
  assert.equal(toast.getAttribute('role'), 'status');
  toast.querySelector('.notebook-toast__close').click();
  assert.equal(dom.window.document.querySelector('.notebook-toast'), null);
});

test('toast action executes and removes toast', async () => {
  const { api, dom } = load();
  api.initNotebookToastRegion();
  let called = 0;
  api.showNotebookToast({ message: 'Moved.', actionText: 'Undo', onAction: async () => { called += 1; }, duration: 0 });
  dom.window.document.querySelector('.notebook-toast button:not(.notebook-toast__close)').click();
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(called, 1);
  assert.equal(dom.window.document.querySelector('.notebook-toast'), null);
});

test('a repeated error is shown once and the stack keeps at most three toasts', () => {
  const { api, dom } = load();
  api.initNotebookToastRegion();
  api.showNotebookToast({ message: 'Save failed.', tone: 'error', duration: 0 });
  api.showNotebookToast({ message: 'Save failed.', tone: 'error', duration: 0 });
  assert.equal(dom.window.document.querySelectorAll('.notebook-toast').length, 1);

  ['One.', 'Two.', 'Three.'].forEach((message) => api.showNotebookToast({ message, duration: 0 }));
  const texts = [...dom.window.document.querySelectorAll('.notebook-toast > span')].map((span) => span.textContent);
  assert.deepEqual(texts, ['One.', 'Two.', 'Three.'], 'oldest message gives way');
});

test('with the shared glass toast on the page, the Notebook shows its message there', () => {
  const { api, dom } = load();
  const shown = [];
  dom.window.PrismToast = { show: (options) => { shown.push(options); return { element: dom.window.document.createElement('div'), dismiss: () => {} }; } };
  api.initNotebookToastRegion();

  let undone = 0;
  const handle = api.showNotebookToast({ message: 'Note moved to Trash.', tone: 'success', actionText: 'Undo', onAction: () => { undone += 1; }, duration: 0 });

  assert.equal(shown.length, 1);
  assert.equal(shown[0].tone, 'success');
  assert.equal(shown[0].text, 'Note moved to Trash.');
  assert.equal(shown[0].sticky, true, 'duration 0 stays until dismissed');
  assert.equal(shown[0].action.label, 'Undo');
  shown[0].action.run();
  assert.equal(undone, 1);
  assert.equal(typeof handle.close, 'function');
  assert.equal(dom.window.document.querySelector('.notebook-toast'), null, 'the Notebook region stays empty');
});
