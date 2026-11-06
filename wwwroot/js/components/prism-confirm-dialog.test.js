const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'prism-confirm-dialog.js'), 'utf8');

// The real partial, with its Razor comment removed, so the tests follow the shipped markup.
const partial = fs
  .readFileSync(path.join(__dirname, '..', '..', '..', 'Pages', 'Shared', '_PrismConfirmDialog.cshtml'), 'utf8')
  .replace(/@\*[\s\S]*?\*@/g, '');

function createDom() {
  const dom = new JSDOM(`<!doctype html><html><body>
    <button id="origin">Open</button>
    ${partial}
  </body></html>`, { runScripts: 'outside-only' });

  const dialog = dom.window.document.querySelector('dialog');
  dialog.showModal = function showModal() {
    this.open = true;
  };
  // Like browsers, "close" fires in a later task, not inside close().
  dialog.close = function close() {
    this.open = false;
    dom.window.setTimeout(() => this.dispatchEvent(new dom.window.Event('close')), 0);
  };
  dom.window.eval(script);
  return dom;
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0));
const $ = (dom, selector) => dom.window.document.querySelector(selector);

test('PrismConfirm renders contextual content and resolves acceptance', async () => {
  const dom = createDom();

  const resultPromise = dom.window.PrismConfirm.show({
    title: 'Discard changes?',
    message: 'Unsaved values will be lost.',
    confirmText: 'Discard',
    cancelText: 'Keep editing',
    tone: 'danger'
  });

  const dialog = $(dom, 'dialog');
  assert.equal(dialog.dataset.tone, 'danger');
  assert.equal($(dom, '[data-prism-confirm-title]').textContent, 'Discard changes?');
  assert.equal($(dom, '[data-prism-confirm-accept]').textContent, 'Discard');
  assert.equal($(dom, '.prism-confirm-dialog__btn--quiet').textContent, 'Keep editing');
  assert.equal($(dom, '[data-prism-confirm-icon] .bi').className, 'bi bi-trash3');

  $(dom, '[data-prism-confirm-accept]').click();
  assert.equal(await resultPromise, true);
});

test('PrismConfirm resolves false when cancelled', async () => {
  const dom = createDom();

  const resultPromise = dom.window.PrismConfirm.show({ title: 'Confirm?' });
  $(dom, '.prism-confirm-dialog__btn--quiet').click();
  assert.equal(await resultPromise, false);
});

test('a click on the dimmed page outside the sheet cancels', async () => {
  const dom = createDom();

  const resultPromise = dom.window.PrismConfirm.show({ title: 'Confirm?' });
  $(dom, 'dialog').dispatchEvent(new dom.window.MouseEvent('click', { bubbles: true }));
  assert.equal(await resultPromise, false);
});

test('danger focuses Cancel so Enter never deletes; other tones focus the action', async () => {
  const dom = createDom();

  dom.window.PrismConfirm.show({ title: 'Delete?', tone: 'danger' });
  await flush();
  assert.equal(dom.window.document.activeElement, $(dom, '.prism-confirm-dialog__btn--quiet'));

  dom.window.PrismConfirm.show({ title: 'Publish?', tone: 'primary' });
  await flush();
  assert.equal(dom.window.document.activeElement, $(dom, '[data-prism-confirm-accept]'));
});

test('the subject card shows the record and hides when there is none', () => {
  const dom = createDom();

  dom.window.PrismConfirm.show({
    title: 'Delete this document?',
    tone: 'danger',
    subject: { name: 'Technical Evaluation Report.pdf', meta: '2.4 MB', icon: 'bi-file-earmark-pdf' }
  });
  assert.equal($(dom, '[data-prism-confirm-subject]').hidden, false);
  assert.equal($(dom, '[data-prism-confirm-subject-name]').textContent, 'Technical Evaluation Report.pdf');
  assert.equal($(dom, '[data-prism-confirm-subject-icon]').className, 'bi bi-file-earmark-pdf');

  dom.window.PrismConfirm.show({ title: 'Publish?' });
  assert.equal($(dom, '[data-prism-confirm-subject]').hidden, true);
});

test('an unsafe icon name falls back to the tone icon', () => {
  const dom = createDom();

  dom.window.PrismConfirm.show({ title: 'Publish?', icon: 'bi-x" onclick="alert(1)' });
  assert.equal($(dom, '[data-prism-confirm-icon] .bi').className, 'bi bi-question-circle');
});

test('irreversible shows the final note', () => {
  const dom = createDom();

  dom.window.PrismConfirm.show({ title: 'Purge?', tone: 'danger', irreversible: true });
  const note = $(dom, '[data-prism-confirm-note]');
  assert.equal(note.hidden, false);
  assert.ok(note.classList.contains('is-final'));
  assert.equal($(dom, '[data-prism-confirm-note-text]').textContent, 'This cannot be undone.');
});

test('a required reason keeps the action disabled until it is long enough, then resolves to it', async () => {
  const dom = createDom();

  const resultPromise = dom.window.PrismConfirm.show({
    title: 'Purge this project permanently?',
    tone: 'danger',
    reason: { label: 'Reason', hint: 'Recorded in the audit log', minLength: 10 }
  });
  const accept = $(dom, '[data-prism-confirm-accept]');
  const reason = $(dom, '[data-prism-confirm-reason]');
  assert.equal($(dom, '[data-prism-confirm-field]').hidden, false);
  assert.equal(reason.hidden, false);
  assert.equal(accept.disabled, true);

  await flush();
  assert.equal(dom.window.document.activeElement, reason);

  reason.value = 'too short';
  reason.dispatchEvent(new dom.window.Event('input'));
  assert.equal(accept.disabled, true);

  reason.value = '  Duplicate demo record  ';
  reason.dispatchEvent(new dom.window.Event('input'));
  assert.equal(accept.disabled, false);

  accept.click();
  assert.equal(await resultPromise, 'Duplicate demo record');
});

test('a confirm phrase unlocks the action only on an exact match; Enter confirms', async () => {
  const dom = createDom();

  const resultPromise = dom.window.PrismConfirm.show({
    title: 'Delete this user account?',
    tone: 'danger',
    confirmPhrase: 'averma'
  });
  const accept = $(dom, '[data-prism-confirm-accept]');
  const phrase = $(dom, '[data-prism-confirm-phrase]');
  assert.match($(dom, '[data-prism-confirm-field-label]').textContent, /Type averma to confirm/);
  assert.equal(accept.disabled, true);

  phrase.value = 'AVERMA';
  phrase.dispatchEvent(new dom.window.Event('input'));
  phrase.dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'Enter' }));
  assert.equal(accept.disabled, true);
  assert.equal($(dom, 'dialog').open, true);

  phrase.value = 'averma';
  phrase.dispatchEvent(new dom.window.Event('input'));
  assert.equal(accept.disabled, false);
  phrase.dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'Enter' }));
  assert.equal(await resultPromise, true);
});

test('opening a second confirmation cancels the first and clears its fields', async () => {
  const dom = createDom();

  const first = dom.window.PrismConfirm.show({ title: 'Purge?', reason: {} });
  $(dom, '[data-prism-confirm-reason]').value = 'Left over from the first dialog';
  dom.window.PrismConfirm.show({ title: 'Publish?' });

  assert.equal(await first, false);
  await flush();
  // The first dialog's late "close" event must not shut the second one.
  assert.equal($(dom, 'dialog').open, true);
  assert.equal($(dom, '[data-prism-confirm-field]').hidden, true);
  assert.equal($(dom, '[data-prism-confirm-reason]').value, '');
  assert.equal($(dom, '[data-prism-confirm-accept]').disabled, false);
});
