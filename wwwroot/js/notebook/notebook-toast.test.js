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
