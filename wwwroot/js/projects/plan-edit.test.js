const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, 'plan-edit.js'), 'utf8');
const tick = () => new Promise((resolve) => setImmediate(resolve));
const jsonResponse = (data, options = {}) => ({
  ok: true, redirected: false,
  headers: { get: () => 'application/json' },
  json: async () => data,
  ...options
});

function setup(t, fetchResponse = () => jsonResponse({ ok: true, savedAt: '2026-10-16T10:00:00Z' })) {
  const dom = new JSDOM(`<div id="offcanvasPlanEdit"><form action="/Projects/Timeline/EditPlan/1" data-plan-edit-form>
    <input name="Input.Mode" value="Durations" type="hidden">
    <input name="Input.ProjectId" value="1" type="hidden">
    <input name="__RequestVerificationToken" value="test-token" type="hidden">
    <input name="Input.Rows[0].DurationDays" type="number" min="1" value="3" required>
    <div class="border-top">
      <button type="submit" name="Input.Action" value="Calculate">Calculate</button>
      <button type="submit" name="Input.Action" value="SaveDraft">Save draft</button>
      <button type="submit" name="Input.Action" value="SaveChanges">Save changes</button>
      <button type="submit" name="Input.Action" value="Submit">Save &amp; request approval</button>
    </div></form>
    <form action="/Projects/Timeline/EditPlan/1" data-plan-edit-form id="exactPlanForm">
      <input name="Input.Mode" value="Exact" type="hidden">
      <input name="Input.ProjectId" value="1" type="hidden">
      <button type="submit" name="Input.Action" value="SaveChanges">Save changes</button>
    </form>
    <form action="/Projects/Timeline/EditPlan/1?handler=DeleteDraft" id="discardPlanForm">
      <input name="Input.ProjectId" value="1" type="hidden">
      <button type="submit">Discard draft</button>
    </form></div>`, { url: 'https://prism.test/Projects/Overview/1', runScripts: 'outside-only' });
  t.after(() => dom.window.close());
  const { window } = dom;
  const form = window.document.querySelector('form');
  const calls = [];
  const submissions = [];
  const timers = new Map();
  let nextTimer = 0;
  window.setTimeout = (fn) => { timers.set(++nextTimer, fn); return nextTimer; };
  window.clearTimeout = (id) => timers.delete(id);
  window.fetch = async (url, options) => { calls.push({ url, options }); return fetchResponse(url, options); };
  // jsdom has no navigation. Observe successful controls at the native boundary.
  // A return to native submit() must lose the button action, just as it does in a browser.
  const allForms = window.document.querySelectorAll('form');
  allForms.forEach((candidate) => {
    candidate.submit = () => submissions.push(new window.FormData(candidate));
  });
  window.eval(source);
  allForms.forEach((candidate) => candidate.addEventListener('submit', (event) => {
    if (!event.defaultPrevented) {
      submissions.push(new window.FormData(candidate, event.submitter));
      event.preventDefault();
    }
  }));
  return {
    window, form, calls, submissions, timers,
    status: () => window.document.getElementById('planSavedAt').textContent,
    input(value = '4') {
      const input = form.querySelector('input[type="number"]');
      input.value = value;
      input.dispatchEvent(new window.Event('input', { bubbles: true }));
    },
    async autosave() {
      const queued = [...timers.values()];
      timers.clear();
      queued.forEach((fn) => fn());
      await tick();
    },
    click(action) { form.querySelector(`button[value="${action}"]`).click(); }
  };
}

test('routed Duration form initializes status and autosaves its entered values', async (t) => {
  const h = setup(t);
  assert.ok(h.window.document.getElementById('planSavedAt'));
  h.input('7');
  await h.autosave();
  assert.equal(h.calls.length, 1);
  assert.equal(new URL(h.calls[0].url).pathname, '/Projects/Timeline/EditPlan/1');
  assert.equal(h.calls[0].options.body.get('Input.Mode'), 'Durations');
  assert.equal(h.calls[0].options.body.get('Input.Rows[0].DurationDays'), '7');
  assert.match(h.status(), /^Saved/);
});

test('marked Exact form also receives explicit submission protection', async (t) => {
  const h = setup(t);
  h.input();
  const button = h.window.document.querySelector('#exactPlanForm button');
  button.click();
  button.click();
  await h.autosave();
  assert.equal(h.submissions.length, 1);
  assert.equal(h.submissions[0].get('Input.Mode'), 'Exact');
  assert.equal(h.submissions[0].get('Input.Action'), 'SaveChanges');
  assert.equal(h.calls.length, 0);
});

test('unmarked discard form is not adopted by planner submit handling', async (t) => {
  const h = setup(t);
  const button = h.window.document.querySelector('#discardPlanForm button');
  button.click();
  button.click();
  await tick();
  assert.equal(h.submissions.length, 2, 'planner duplicate guard must not intercept an unrelated form');
  assert.equal(h.calls.length, 0);
});

for (const action of ['Submit', 'Calculate', 'SaveDraft', 'SaveChanges']) {
  test(`duration ${action} preserves the clicked submitter and cancels pending autosave`, async (t) => {
    const h = setup(t);
    h.input();
    h.click(action);
    await tick();
    await h.autosave();
    assert.equal(h.submissions.length, 1);
    assert.equal(h.submissions[0].get('Input.Action'), action);
    assert.equal(h.submissions[0].get('Input.Rows[0].DurationDays'), '4');
    assert.equal(h.calls.length, 0);
    h.click(action);
    assert.equal(h.submissions.length, 1, 'duplicate explicit submission is blocked');
  });
}

test('approval submission keeps normal HTML constraint validation', async (t) => {
  const h = setup(t);
  h.input('0');
  h.click('Submit');
  await tick();
  assert.equal(h.submissions.length, 0);
});

test('autosave uses explicit working intent and confirms server persistence', async (t) => {
  let resolve;
  const h = setup(t, () => new Promise((r) => { resolve = r; }));
  h.input();
  await h.autosave();
  assert.match(h.status(), /Saving/);
  assert.equal(h.calls[0].options.body.get('Input.Action'), 'Autosave');
  assert.equal(h.calls[0].options.body.get('__RequestVerificationToken'), 'test-token');
  resolve(jsonResponse({ ok: true, savedAt: '2026-10-16T10:00:00Z' }));
  await tick();
  assert.match(h.status(), /^Saved/);
});

for (const [name, response] of [
  ['validation rejection', () => jsonResponse({ ok: false, message: 'Duration is invalid.' }, { ok: false })],
  ['redirected HTML', () => jsonResponse(null, { redirected: true, headers: { get: () => 'text/html' } })],
  ['HTTP 200 rejection', () => jsonResponse({ ok: false, message: 'Draft is locked.' })],
  ['invalid JSON', () => ({ ok: true, redirected: false, headers: { get: () => 'application/json' }, json: async () => { throw Error('Bad JSON'); } })],
  ['network failure', () => { throw Error('Network failure'); }]
]) {
  test(`autosave ${name} never reports Saved and retains entered values`, async (t) => {
    const h = setup(t, response);
    h.input('7');
    await h.autosave();
    assert.match(h.status(), /Could not save|validation failed/i);
    assert.doesNotMatch(h.status(), /^Saved/);
    assert.equal(h.form.querySelector('input[type="number"]').value, '7');
  });
}

for (const action of ['Submit', 'Calculate', 'SaveDraft', 'SaveChanges']) {
test(`in-flight autosave settles before ${action}; no delayed draft follows`, async (t) => {
  let resolve;
  const h = setup(t, () => new Promise((r) => { resolve = r; }));
  h.input();
  await h.autosave();
  h.input('5');
  h.click(action);
  h.click(action);
  await tick();
  assert.equal(h.submissions.length, 0);
  resolve(jsonResponse({ ok: true, savedAt: '2026-10-16T10:00:00Z' }));
  await tick();
  await h.autosave();
  assert.equal(h.submissions.length, 1);
  assert.equal(h.submissions[0].get('Input.Action'), action);
  assert.equal(h.submissions[0].get('Input.Rows[0].DurationDays'), '5');
  assert.equal(h.calls.length, 1);
});
}

test('autosaves serialize and an older response cannot mark newer input Saved', async (t) => {
  const pending = [];
  const h = setup(t, () => new Promise((r) => pending.push(r)));
  h.input('4');
  await h.autosave();
  h.input('6');
  await h.autosave();
  assert.equal(h.calls.length, 1, 'no overlapping writes');
  pending.shift()(jsonResponse({ ok: true, savedAt: '2026-10-16T10:00:00Z' }));
  await tick();
  assert.doesNotMatch(h.status(), /^Saved/);
  await h.autosave();
  assert.equal(h.calls.length, 2);
  assert.equal(h.calls[1].options.body.get('Input.Rows[0].DurationDays'), '6');
  pending.shift()(jsonResponse({ ok: true, savedAt: '2026-10-16T10:01:00Z' }));
  await tick();
  assert.match(h.status(), /^Saved/);
});

for (const eventName of ['hide.bs.offcanvas', 'pagehide']) {
  test(`${eventName} cancels queued autosave`, async (t) => {
    const h = setup(t);
    h.input();
    const target = eventName === 'pagehide' ? h.window : h.window.document.getElementById('offcanvasPlanEdit');
    target.dispatchEvent(new h.window.Event(eventName));
    await h.autosave();
    assert.equal(h.calls.length, 0);
  });
}
