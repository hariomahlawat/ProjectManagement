const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, 'plan-edit.js'), 'utf8');

// PO Timeline Increment 2d: the planner warns before unsaved entries are lost and reopens on
// the tab the user last used.
function setup(t, { restored = false, storedTab = null, activePane = 'exact' } = {}) {
  const dom = new JSDOM(`<div id="offcanvasPlanEdit">
    ${restored ? '<div data-plan-submission-errors data-plan-restored></div>' : ''}
    <ul id="plan-edit-tabs">
      <li><button id="plan-edit-exact-tab" data-bs-toggle="tab" class="${activePane === 'exact' ? 'active' : ''}">Exact dates</button></li>
      <li><button id="plan-edit-durations-tab" data-bs-toggle="tab" class="${activePane === 'durations' ? 'active' : ''}">Durations</button></li>
    </ul>
    <div class="tab-pane ${activePane === 'exact' ? 'active' : ''}" id="plan-edit-exact">
      <form action="/Projects/Timeline/EditPlan/1" data-plan-edit-form id="exact">
        <input name="Input.Mode" value="Exact" type="hidden">
        <input name="Input.ProjectId" value="1" type="hidden">
        <input name="Input.Rows[0].PlannedDue" type="date" value="">
        <button type="submit" name="Input.Action" value="SaveDraft">Save draft</button>
      </form>
    </div>
    <div class="tab-pane ${activePane === 'durations' ? 'active' : ''}" id="plan-edit-durations">
      <form action="/Projects/Timeline/EditPlan/1" data-plan-edit-form id="durations">
        <input name="Input.Mode" value="Durations" type="hidden">
        <input name="Input.ProjectId" value="1" type="hidden">
        <input name="Input.Rows[0].DurationDays" type="number" value="3">
        <div class="border-top"><button type="submit" name="Input.Action" value="SaveDraft">Save draft</button></div>
      </form>
    </div></div>`, { url: 'https://prism.test/Projects/Overview/1', runScripts: 'outside-only' });
  t.after(() => dom.window.close());
  const { window } = dom;
  const confirms = [];
  let answer = true;
  // The shared PRISM dialog: records each question and answers with `answer`.
  window.PrismConfirm = { show: (options) => { confirms.push(options.title); return Promise.resolve(answer); } };
  window.setTimeout = () => 0;
  window.clearTimeout = () => {};
  const shown = [];
  // Bootstrap's Offcanvas.hide() fires hide.bs.offcanvas again; record whether it went through.
  const closes = [];
  window.bootstrap = {
    Tab: { getOrCreateInstance: (tab) => ({ show: () => shown.push(tab.id) }) },
    Offcanvas: {
      getOrCreateInstance: (node) => ({
        hide: () => {
          const replay = new window.Event('hide.bs.offcanvas', { cancelable: true });
          node.dispatchEvent(replay);
          closes.push(!replay.defaultPrevented);
        }
      })
    }
  };
  if (storedTab) window.localStorage.setItem('prism.planEditor.tab.1', storedTab);
  const submitted = [];
  window.eval(source);
  window.document.querySelectorAll('form').forEach((form) => form.addEventListener('submit', (event) => {
    if (!event.defaultPrevented) submitted.push(form.id);
    event.preventDefault();
  }));
  const offcanvas = window.document.getElementById('offcanvasPlanEdit');
  return {
    window, confirms, shown, submitted, closes,
    answer(value) { answer = value; },
    edit(formId) {
      const input = window.document.querySelector(`#${formId} input:not([type="hidden"])`);
      input.value = formId === 'exact' ? '2026-03-01' : '5';
      input.dispatchEvent(new window.Event('input', { bubbles: true }));
    },
    hide() {
      const event = new window.Event('hide.bs.offcanvas', { cancelable: true });
      offcanvas.dispatchEvent(event);
      return event.defaultPrevented;
    },
    submit(formId) {
      window.document.querySelector(`#${formId} button[type="submit"]`).click();
    }
  };
}

const settle = () => new Promise((resolve) => setImmediate(resolve));

test('closing with unsaved entries asks first and stays open when declined', async (t) => {
  const h = setup(t);
  h.edit('exact');
  h.answer(false);
  assert.equal(h.hide(), true, 'the close is held while the dialog is open');
  await settle();
  assert.equal(h.confirms.length, 1);
  assert.deepEqual(h.closes, [], 'declined: the planner stays open');

  h.answer(true);
  assert.equal(h.hide(), true);
  await settle();
  assert.deepEqual(h.closes, [true], 'accepted: the planner closes once');
});

test('closing keeps the entries unsaved, so leaving the page still warns', async (t) => {
  const h = setup(t);
  h.edit('exact');
  h.answer(true);
  h.hide();
  await settle();
  assert.deepEqual(h.closes, [true]);
  const leave = new h.window.Event('beforeunload', { cancelable: true });
  h.window.dispatchEvent(leave);
  assert.equal(leave.defaultPrevented, true);
  h.hide();
  await settle();
  assert.equal(h.confirms.length, 2);
});

test('leaving the page without edits does not warn', (t) => {
  const h = setup(t);
  const leave = new h.window.Event('beforeunload', { cancelable: true });
  h.window.dispatchEvent(leave);
  assert.equal(leave.defaultPrevented, false);
});

test('closing without edits does not ask', (t) => {
  const h = setup(t);
  assert.equal(h.hide(), false);
  assert.equal(h.confirms.length, 0);
});

test('saving one tab while the other has unsaved edits asks first', async (t) => {
  const h = setup(t);
  h.edit('exact');
  h.answer(false);
  h.submit('durations');
  await settle();
  assert.deepEqual(h.submitted, []);
  h.answer(true);
  h.submit('durations');
  assert.deepEqual(h.submitted, [], 'held while the dialog is open');
  await settle();
  assert.deepEqual(h.submitted, ['durations']);
});

test('a successful autosave clears the Durations tab', (t) => {
  const h = setup(t);
  h.edit('durations');
  h.window.document.getElementById('durations').dispatchEvent(new h.window.CustomEvent('plan:autosaved'));
  assert.equal(h.hide(), false);
  assert.equal(h.confirms.length, 0);
});

test('entries restored after a refused post count as unsaved', (t) => {
  const h = setup(t, { restored: true });
  h.answer(false);
  assert.equal(h.hide(), true);
});

test('the planner reopens on the remembered tab', (t) => {
  const h = setup(t, { storedTab: 'plan-edit-durations-tab' });
  assert.deepEqual(h.shown, ['plan-edit-durations-tab']);
});

test('a refused post keeps the tab it came from', (t) => {
  const h = setup(t, { restored: true, storedTab: 'plan-edit-durations-tab' });
  assert.deepEqual(h.shown, []);
});

test('switching tabs is remembered for the project', (t) => {
  const h = setup(t);
  h.window.document.getElementById('plan-edit-durations-tab').dispatchEvent(new h.window.Event('shown.bs.tab'));
  assert.equal(h.window.localStorage.getItem('prism.planEditor.tab.1'), 'plan-edit-durations-tab');
});
