const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const { JSDOM } = require('jsdom');

function load() {
  const dom = new JSDOM(`<!doctype html><body>
    <button id="trigger">Open</button>
    <div data-notebook-confirm hidden>
      <div class="notebook-confirm__backdrop" data-confirm-cancel></div>
      <section class="notebook-confirm__dialog" tabindex="-1">
        <h2 data-confirm-title></h2>
        <p data-confirm-message></p>
        <p data-confirm-detail hidden></p>
        <button class="notebook-confirm__close" data-confirm-cancel>Close</button>
        <button data-confirm-cancel>Cancel</button>
        <button data-confirm-accept>Confirm</button>
      </section>
    </div>
  </body>`, { url: 'https://example.test/Notebook' });
  let source = fs.readFileSync(path.join(__dirname, 'notebook-confirm-dialog.js'), 'utf8');
  source = source.replace(/export function /g, 'function ');
  source += '\nmodule.exports={initNotebookConfirmDialog,confirmNotebookAction,disposeNotebookConfirmDialog};';
  const context = {
    module: { exports: {} }, exports: {},
    document: dom.window.document,
    window: dom.window,
    queueMicrotask,
    console
  };
  vm.runInNewContext(source, context);
  return { api: context.module.exports, dom };
}

test('confirmation resolves true and applies professional copy', async () => {
  const { api, dom } = load();
  api.initNotebookConfirmDialog();
  const promise = api.confirmNotebookAction({
    title: 'Delete label?',
    message: 'The label will be removed.',
    detail: 'Notes remain.',
    confirmText: 'Delete label',
    tone: 'danger'
  });
  assert.equal(dom.window.document.querySelector('[data-notebook-confirm]').hidden, false);
  assert.equal(dom.window.document.querySelector('[data-confirm-title]').textContent, 'Delete label?');
  assert.equal(dom.window.document.querySelector('[data-confirm-accept]').textContent, 'Delete label');
  assert.equal(dom.window.document.querySelector('[data-notebook-confirm]').dataset.tone, 'danger');
  dom.window.document.querySelector('[data-confirm-accept]').click();
  assert.equal(await promise, true);
  assert.equal(dom.window.document.querySelector('[data-notebook-confirm]').hidden, true);
});

test('Escape cancels and restores previous focus', async () => {
  const { api, dom } = load();
  api.initNotebookConfirmDialog();
  const trigger = dom.window.document.querySelector('#trigger');
  trigger.focus();
  const promise = api.confirmNotebookAction({ message: 'Discard?' });
  dom.window.document.dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.equal(await promise, false);
  await new Promise((resolve) => queueMicrotask(resolve));
  assert.equal(dom.window.document.activeElement, trigger);
});

test('backdrop cancellation can be disabled', async () => {
  const { api, dom } = load();
  api.initNotebookConfirmDialog();
  const promise = api.confirmNotebookAction({ message: 'Continue?', allowBackdropClose: false });
  dom.window.document.querySelector('.notebook-confirm__backdrop').click();
  assert.equal(dom.window.document.querySelector('[data-notebook-confirm]').hidden, false);
  dom.window.document.querySelector('[data-confirm-cancel]:not(.notebook-confirm__backdrop)').click();
  assert.equal(await promise, false);
});

test('starting a second confirmation safely cancels the first', async () => {
  const { api, dom } = load();
  api.initNotebookConfirmDialog();
  const first = api.confirmNotebookAction({ message: 'First' });
  const second = api.confirmNotebookAction({ message: 'Second' });
  assert.equal(await first, false);
  dom.window.document.querySelector('[data-confirm-accept]').click();
  assert.equal(await second, true);
});
