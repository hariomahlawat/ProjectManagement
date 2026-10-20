const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(__dirname, 'actuals-edit.js'), 'utf8');

// Phase D-3c: the assigned PO-only user's editor is prefilled with the pending
// proposal, while data-official-start keeps the recorded value.
function proposalEditor({ officialStart, prefilledStart, note = '' }) {
  const dom = new JSDOM(`<div id="offcanvasActualDates"><form data-actuals-form data-today="2024-02-01">
    <section data-actuals-row data-stage-code="FS" data-stage-status="Completed" data-official-start="${officialStart}">
      <input type="date" data-field="start" value="${prefilledStart}">
      <input type="date" data-field="completed" value="2024-01-25">
      <input type="hidden" data-clear-start value="false">
      <div data-row-error class="d-none"></div>
    </section>
    <textarea data-actuals-note>${note}</textarea>
    <button data-actuals-save disabled>Submit for approval</button></form></div>`, { runScripts: 'outside-only' });
  dom.window.eval(source);
  const doc = dom.window.document;
  return {
    dom,
    doc,
    clear: () => doc.querySelector('[data-clear-start]').value,
    submitDisabled: () => doc.querySelector('[data-actuals-save]').disabled,
    input(selector, value) {
      const element = doc.querySelector(selector);
      element.value = value;
      element.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    }
  };
}

test('a pending start clear keeps its explicit intent when the prefilled editor is resubmitted', () => {
  const ui = proposalEditor({ officialStart: '2024-01-10', prefilledStart: '' });
  assert.equal(ui.clear(), 'true');
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});

test('restoring the official start drops the pending clear', () => {
  const ui = proposalEditor({ officialStart: '2024-01-10', prefilledStart: '' });
  ui.input('[data-field="start"]', '2024-01-10');
  assert.equal(ui.clear(), 'false');
  assert.equal(ui.submitDisabled(), false);
  ui.dom.window.close();
});

test('a blank start that is blank officially is never sent as a clear', () => {
  const ui = proposalEditor({ officialStart: '', prefilledStart: '2024-01-12' });
  ui.input('[data-field="start"]', '');
  assert.equal(ui.clear(), 'false');
  ui.dom.window.close();
});

test('revising only the reason enables resubmission', () => {
  const ui = proposalEditor({ officialStart: '2024-01-10', prefilledStart: '2024-01-11', note: 'From register' });
  assert.equal(ui.submitDisabled(), true);
  ui.input('[data-actuals-note]', 'From the signed register');
  assert.equal(ui.submitDisabled(), false);
  ui.input('[data-actuals-note]', ' From register ');
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});

test('a backfill proposal response is announced as submitted, not saved', () => {
  const overview = fs.readFileSync(path.join(__dirname, 'overview.js'), 'utf8');
  assert.match(overview, /data\.pendingApproval === true/);
  assert.match(overview, /data\?\.ok !== true/);
  assert.match(overview, /submissionMode: modeInput\?\.value/);
});
