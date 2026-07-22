const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'prism-confirm-dialog.js'), 'utf8');

function createDom() {
  const dom = new JSDOM(`<!doctype html><html><body>
    <button id="origin">Open</button>
    <dialog data-prism-confirm-dialog>
      <span data-prism-confirm-icon><span class="bi"></span></span>
      <h2 data-prism-confirm-title></h2>
      <p data-prism-confirm-message></p>
      <p data-prism-confirm-detail hidden></p>
      <button data-prism-confirm-cancel>Cancel</button>
      <button data-prism-confirm-accept>Confirm</button>
    </dialog>
  </body></html>`, { runScripts: 'outside-only' });

  const dialog = dom.window.document.querySelector('dialog');
  dialog.showModal = function showModal() {
    this.open = true;
  };
  dialog.close = function close() {
    this.open = false;
    this.dispatchEvent(new dom.window.Event('close'));
  };
  return dom;
}

test('PrismConfirm renders contextual content and resolves acceptance', async () => {
  const dom = createDom();
  dom.window.eval(script);

  const resultPromise = dom.window.PrismConfirm.show({
    title: 'Discard changes?',
    message: 'Unsaved values will be lost.',
    confirmText: 'Discard',
    cancelText: 'Keep editing',
    tone: 'danger'
  });

  const dialog = dom.window.document.querySelector('dialog');
  assert.equal(dialog.dataset.tone, 'danger');
  assert.equal(dialog.querySelector('[data-prism-confirm-title]').textContent, 'Discard changes?');
  assert.equal(dialog.querySelector('[data-prism-confirm-accept]').textContent, 'Discard');

  dialog.querySelector('[data-prism-confirm-accept]').click();
  assert.equal(await resultPromise, true);
});

test('PrismConfirm resolves false when cancelled', async () => {
  const dom = createDom();
  dom.window.eval(script);

  const resultPromise = dom.window.PrismConfirm.show({ title: 'Confirm?' });
  dom.window.document.querySelector('[data-prism-confirm-cancel]').click();
  assert.equal(await resultPromise, false);
});
