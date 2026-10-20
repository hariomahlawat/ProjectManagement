const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(__dirname, 'actuals-edit.js'), 'utf8');

function editor(status, start, completed) {
  const dom = new JSDOM(`<div id="offcanvasActualDates"><form data-actuals-form data-today="2024-02-01">
    <section data-actuals-row data-stage-code="FS" data-stage-status="${status}">
      <input type="date" data-field="start" value="${start}">
      <input type="date" data-field="completed" value="${completed}">
      <input type="hidden" data-clear-start value="false">
      <div data-row-error class="d-none"></div>
    </section><button data-actuals-save disabled>Save</button></form></div>`, { runScripts: 'outside-only' });
  dom.window.eval(source);
  const doc = dom.window.document;
  return { dom, doc, change(field, value) {
    const input = doc.querySelector(`[data-field="${field}"]`);
    input.value = value;
    input.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
  } };
}

test('completed start clear sends explicit intent and is reversible before saving', () => {
  const ui = editor('Completed', '2024-01-10', '2024-01-25');
  ui.change('start', '');
  assert.equal(ui.doc.querySelector('[data-clear-start]').value, 'true');
  assert.equal(ui.doc.querySelector('[data-actuals-save]').disabled, false);
  ui.change('start', '2024-01-10');
  assert.equal(ui.doc.querySelector('[data-clear-start]').value, 'false');
  assert.equal(ui.doc.querySelector('[data-actuals-save]').disabled, true);
  ui.dom.window.close();
});

test('blank start that was already blank does not send explicit clear', () => {
  const ui = editor('Completed', '', '2024-01-25');
  ui.change('completed', '2024-01-26');
  assert.equal(ui.doc.querySelector('[data-clear-start]').value, 'false');
  assert.equal(ui.doc.querySelector('[data-actuals-save]').disabled, false);
  ui.dom.window.close();
});

test('in-progress start clear is blocked with a lifecycle instruction', () => {
  const ui = editor('InProgress', '2024-01-10', '');
  ui.change('start', '');
  assert.equal(ui.doc.querySelector('[data-actuals-save]').disabled, true);
  assert.match(ui.doc.querySelector('[data-row-error]').textContent, /start.*required|cannot.*clear/i);
  ui.dom.window.close();
});

test('in-progress completion is blocked without changing status', () => {
  const ui = editor('InProgress', '2024-01-10', '');
  ui.change('completed', '2024-01-25');
  assert.equal(ui.doc.querySelector('[data-actuals-save]').disabled, true);
  assert.match(ui.doc.querySelector('[data-row-error]').textContent, /use Update stage/i);
  assert.equal(ui.doc.querySelector('[data-actuals-row]').dataset.stageStatus, 'InProgress');
  ui.dom.window.close();
});
