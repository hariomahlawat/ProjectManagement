const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(__dirname, 'actuals-edit.js'), 'utf8');

// PO Timeline Increment 2c: "Clear the recorded start date" is an explicit control that only
// drives the start field; the hidden ClearActualStart flag is still derived from that field.
function editor({ officialStart = '2024-01-10', startValue = '2024-01-10', checked = false } = {}) {
  const dom = new JSDOM(`<div id="offcanvasActualDates"><form data-actuals-form data-today="2024-02-01">
    <section data-actuals-row data-stage-code="FS" data-stage-status="Completed" data-official-start="${officialStart}">
      <input type="date" data-field="start" value="${startValue}">
      <input type="checkbox" data-clear-start-toggle${checked ? ' checked' : ''}>
      <input type="date" data-field="completed" value="2024-01-25">
      <input type="hidden" data-clear-start value="false">
      <div data-row-error class="d-none"></div>
    </section>
    <button data-actuals-save disabled>Submit for approval</button></form></div>`, { runScripts: 'outside-only' });
  dom.window.eval(source);
  const doc = dom.window.document;
  const start = doc.querySelector('[data-field="start"]');
  const toggle = doc.querySelector('[data-clear-start-toggle]');
  return {
    dom,
    start,
    toggle,
    clear: () => doc.querySelector('[data-clear-start]').value,
    submitDisabled: () => doc.querySelector('[data-actuals-save]').disabled,
    check(value) {
      toggle.checked = value;
      toggle.dispatchEvent(new dom.window.Event('change', { bubbles: true }));
    },
    type(value) {
      start.value = value;
      start.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    }
  };
}

test('checking the toggle empties the start and sends an explicit clear', () => {
  const ui = editor();
  ui.check(true);
  assert.equal(ui.start.value, '');
  assert.equal(ui.clear(), 'true');
  assert.equal(ui.submitDisabled(), false);
  ui.dom.window.close();
});

test('unchecking restores the date that was there before the clear', () => {
  const ui = editor({ startValue: '2024-01-12' });
  ui.check(true);
  ui.check(false);
  assert.equal(ui.start.value, '2024-01-12');
  assert.equal(ui.clear(), 'false');
  ui.dom.window.close();
});

test('unchecking a server-rendered clear falls back to the official start', () => {
  const ui = editor({ startValue: '', checked: true });
  ui.check(false);
  assert.equal(ui.start.value, '2024-01-10');
  assert.equal(ui.clear(), 'false');
  ui.dom.window.close();
});

test('emptying or typing the start keeps the toggle in step', () => {
  const ui = editor();
  ui.type('');
  assert.equal(ui.toggle.checked, true);
  assert.equal(ui.clear(), 'true');
  ui.type('2024-01-11');
  assert.equal(ui.toggle.checked, false);
  assert.equal(ui.clear(), 'false');
  ui.dom.window.close();
});
