const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, 'stages.js'), 'utf8');
const vm = require('node:vm');

for (const [name, stage, expected] of [
  ['blocked ignores Completed reopen default', { currentStatus: 'Blocked', suggestedStart: '2026-08-20', reopenSuggestedStart: '2026-08-21' }, ''],
  ['skipped ignores Completed reopen default', { currentStatus: 'Skipped', suggestedStart: '2026-08-20', reopenSuggestedStart: '2026-08-21' }, ''],
  ['unknown start and undetermined boundary', { currentStatus: 'Completed', suggestedStart: '2026-08-20' }, ''],
  ['blocked recorded start', { currentStatus: 'Blocked', actualStart: '2026-08-06', reopenSuggestedStart: '2026-08-11' }, '2026-08-06'],
  ['recorded start', { currentStatus: 'Completed', actualStart: '2026-08-06', reopenSuggestedStart: '2026-08-11' }, '2026-08-06'],
  ['working-day reopen suggestion', { currentStatus: 'Completed', suggestedStart: '2026-08-08', reopenSuggestedStart: '2026-08-11' }, '2026-08-11']
]) {
  test(`PO reopen default uses ${name}`, () => {
    const dom = new JSDOM('<div><select data-stage-request-stage><option value="BM">BM</option></select><input type="date" data-stage-request-date></div>');
    const row = dom.window.document.querySelector('div');
    const functionSource = source.slice(source.indexOf('    function setDateState('), source.indexOf('    function retainedStartDate('));
    const context = { row, stageOptionFor: () => stage, dateConfiguration: () => ({ required: true }), todayIso: () => '2026-08-28', formatDate: (value) => value };
    vm.runInNewContext(`${functionSource}\nsetDateState(row, 'InProgress', { resetValue: true });`, context);
    assert.equal(row.querySelector('input').value, expected);
    assert.equal(row.querySelector('input').readOnly, Boolean(stage.actualStart));
    dom.window.close();
  });
}
function setup(defaultDate = '', startMode = true, recordedStart = '', status = 'Reopen', browserToday = '') {
  const dom = new JSDOM(`<button data-direct-apply data-project="1" data-stage="BM" data-status="${status}" data-reopen-start="${startMode}" data-actual-start="${recordedStart}" data-default-date="${defaultDate}" data-earliest-start="2026-08-06" data-actual-today="2026-08-28"></button>
    <div id="stageDirectApplyModal"><form data-direct-apply-form novalidate>
    <input name="projectId"><input name="stageCode"><input name="status"><input name="__RequestVerificationToken" value="token">
    <input type="date" name="date"><div data-direct-apply-start-group><input type="date" name="startDate"><div data-direct-apply-start-hint></div></div><span data-direct-apply-date-label></span><div data-direct-apply-date-hint></div>
    <div data-direct-apply-errors></div><button data-direct-apply-submit></button></form></div>`, { url: 'http://prism.test/', runScripts: 'outside-only' });
  if (browserToday) {
    const NativeDate = dom.window.Date;
    dom.window.Date = class extends NativeDate {
      constructor(...args) { super(...(args.length ? args : [`${browserToday}T12:00:00`])); }
      static now() { return new NativeDate(`${browserToday}T12:00:00`).getTime(); }
    };
  }
  const calls = [];
  dom.window.bootstrap = { Modal: { getOrCreateInstance: () => ({ show() {}, hide() {} }) }, Toast: {} };
  dom.window.fetch = async (url, options) => { calls.push(JSON.parse(options.body)); return { status: 422, json: async () => ({ details: ['test response'] }) }; };
  Object.defineProperty(dom.window.document, 'readyState', { value: 'complete' });
  dom.window.eval(source);
  dom.window.document.querySelector('[data-direct-apply]').click();
  return { dom, calls, input: dom.window.document.querySelector('input[name="date"]'), form: dom.window.document.querySelector('form') };
}
test('reopen in progress does not invent today; requires explicit actual start', () => {
  const { dom, input, form, calls } = setup();
  assert.equal(input.value, '');
  assert.equal(input.required, true);
  assert.equal(dom.window.document.querySelector('[data-direct-apply-date-label]').textContent, 'Started on');
  form.dispatchEvent(new dom.window.Event('submit', { cancelable: true }));
  assert.equal(calls.length, 0);
  dom.window.close();
});
test('reopen prefills only supplied start and keeps explicit same-day valid until confirmation', async () => {
  const { dom, input, form, calls } = setup('2026-08-07');
  assert.equal(input.value, '2026-08-07');
  assert.equal(input.min, '2026-08-06');
  assert.equal(calls.length, 0);
  input.value = '2026-08-06';
  form.dispatchEvent(new dom.window.Event('submit', { cancelable: true }));
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(calls.length, 1);
  assert.equal(calls[0].date, '2026-08-06');
  dom.window.close();
});
test('future reopen suggestion cannot be submitted as an actual start', () => {
  const { dom, input, form, calls } = setup('9999-01-01');
  assert.equal(input.max, '2026-08-28');
  form.dispatchEvent(new dom.window.Event('submit', { cancelable: true }));
  assert.equal(calls.length, 0);
  dom.window.close();
});
test('explicit reset to not started submits no start date', async () => {
  const { dom, input, form, calls } = setup('', false);
  assert.equal(input.value, '');
  assert.equal(input.required, false);
  form.dispatchEvent(new dom.window.Event('submit', { cancelable: true }));
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(calls.length, 1);
  assert.equal(calls[0].date, null);
  dom.window.close();
});

test('direct reopen exposes recorded start as readonly while correction stays separate', () => {
  const { dom, input } = setup('2026-08-10', true, '2026-08-10');
  assert.equal(input.value, '2026-08-10');
  assert.equal(input.readOnly, true);
  dom.window.close();
});

test('PO reopen cannot display a conflicting pending replacement for a recorded start', () => {
  const dom = new JSDOM('<div><select data-stage-request-stage><option value="BM">BM</option></select><input type="date" data-stage-request-date></div>');
  const row = dom.window.document.querySelector('div');
  const functionSource = source.slice(source.indexOf('    function setDateState('), source.indexOf('    function retainedStartDate('));
  vm.runInNewContext(`${functionSource}\nsetDateState(row, 'InProgress', { resetValue: true, preferredValue: '2026-08-12' });`, {
    row, stageOptionFor: () => ({ currentStatus: 'Completed', actualStart: '2026-08-10' }), dateConfiguration: () => ({ required: true }), todayIso: () => '2026-08-28', formatDate: (value) => value
  });
  assert.equal(row.querySelector('input').value, '2026-08-10');
  assert.equal(row.querySelector('input').readOnly, true);
  dom.window.close();
});

for (const browserToday of ['2026-08-27', '2026-08-29']) {
  for (const status of ['InProgress', 'Completed']) {
    test(`direct ${status} defaults and max use server IST when browser today is ${browserToday}`, () => {
      const { dom, input } = setup('', true, '', status, browserToday);
      assert.equal(input.value, '2026-08-28');
      assert.equal(input.max, '2026-08-28');
      if (status === 'Completed') assert.equal(dom.window.document.querySelector('[name="startDate"]').max, '2026-08-28');
      dom.window.close();
    });
  }
  test(`direct reopen retains recorded start and IST max when browser today is ${browserToday}`, () => {
    const { dom, input } = setup('2026-08-10', true, '2026-08-10', 'Reopen', browserToday);
    assert.equal(input.value, '2026-08-10');
    assert.equal(input.readOnly, true);
    assert.equal(input.max, '2026-08-28');
    dom.window.close();
  });
  for (const status of ['Blocked', 'Skipped']) {
    test(`direct ${status} optional actual date uses IST max when browser today is ${browserToday}`, () => {
      const { dom, input } = setup('', true, '', status, browserToday);
      assert.equal(input.value, '');
      assert.equal(input.max, '2026-08-28');
      dom.window.close();
    });
  }
}
