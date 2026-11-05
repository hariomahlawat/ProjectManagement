const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'overview-tot.js'), 'utf8');

// The drawer as _ProjectTotDrawer.cshtml renders it for an approver while a request is pending.
const markup = `
  <div id="project-tot-card"><span data-tot-card-title></span><span data-tot-card-summary></span></div>
  <div id="offcanvasTot" data-project-id="9">
    <div data-tot-root>
      <div data-tot-loading class="d-none"></div>
      <div data-tot-content>
        <div data-tot-global-error class="d-none"></div>
        <div data-tot-success class="d-none"></div>
        <section data-tot-summary-view>
          <section class="d-none" data-tot-pending>
            <h6 data-tot-pending-title></h6><span data-tot-pending-meta></span>
            <table><tbody data-tot-changes></tbody></table>
            <form class="d-none" data-tot-decide-form action="/Projects/Overview/9?handler=TotDecide">
              <input type="hidden" name="__RequestVerificationToken" value="token" />
              <input type="hidden" name="rowVersion" data-tot-decide-version />
              <textarea name="reason" data-tot-decide-reason></textarea>
              <div class="d-none" data-tot-decide-error></div>
              <button type="button" data-tot-decide="true">Approve</button>
              <button type="button" data-tot-decide="false">Reject</button>
            </form>
          </section>
          <section class="d-none" data-tot-history-wrap><ol data-tot-history></ol></section>
        </section>
        <div data-tot-summary-footer><button type="button" class="d-none" data-tot-edit-button>Update details</button></div>
      </div>
    </div>
  </div>`;

const details = (pending, history = []) => ({
  success: true,
  canManage: true,
  pendingApproval: !!pending,
  pending,
  history,
  input: { status: 'InProgress' },
  card: pending
    ? { title: 'Approval pending', summary: 'Proposed: Completed', tone: 'info' }
    : { title: 'ToT completed', summary: 'Completed Oct 2026', tone: 'positive' },
  summary: { statusLabel: 'In progress', summary: '', facts: [] }
});

const pendingRequest = {
  proposedStatusLabel: 'Completed',
  submittedBy: 'K Srinath',
  submittedOn: '03 Nov 2026, 10:40',
  changes: [
    { field: 'Status', now: 'In progress', proposed: 'Completed' },
    { field: 'Completed', now: '—', proposed: 'Oct 2026' }
  ],
  rowVersion: 'dmVyc2lvbg==',
  canDecide: true
};

function run(respond) {
  const dom = new JSDOM(`<!doctype html><body>${markup}</body>`, {
    url: 'http://localhost/Projects/Overview/9',
    runScripts: 'outside-only'
  });
  const { window } = dom;
  const calls = [];
  window.fetch = (url, options = {}) => {
    calls.push({ url: String(url), options });
    const body = respond(String(url), options, calls.length);
    return Promise.resolve({ ok: body.ok !== false, json: () => Promise.resolve(body) });
  };
  window.bootstrap = {
    Offcanvas: {
      getOrCreateInstance: (element) => ({
        show: () => element.dispatchEvent(new window.Event('show.bs.offcanvas'))
      })
    }
  };
  window.eval(script);
  return { window, calls, open: () => window.document.getElementById('offcanvasTot').dispatchEvent(new window.Event('show.bs.offcanvas')) };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));
const $ = (window, selector) => window.document.querySelector(selector);
const hidden = (window, selector) => $(window, selector).classList.contains('d-none');

test('a pending request shows only the changed fields, with the decision for an approver', async () => {
  const { window, open } = run(() => details(pendingRequest, [
    { label: 'Rejected', by: 'Hari Om Ahlawat', on: '26 Oct 2026, 07:21', values: 'Not started', reason: '<b>Not now</b>' }
  ]));
  open();
  await settle();

  assert.equal(hidden(window, '[data-tot-pending]'), false);
  assert.equal($(window, '[data-tot-pending-title]').textContent, 'Waiting for your decision');
  assert.equal($(window, '[data-tot-pending-meta]').textContent, 'K Srinath · 03 Nov 2026, 10:40');
  const rows = [...window.document.querySelectorAll('[data-tot-changes] tr')].map((row) => row.textContent);
  assert.deepEqual(rows, ['StatusIn progressCompleted', 'Completed—Oct 2026']);
  assert.equal(hidden(window, '[data-tot-decide-form]'), false);
  assert.equal($(window, '[data-tot-decide-version]').value, 'dmVyc2lvbg==');

  assert.equal(hidden(window, '[data-tot-history-wrap]'), false);
  const reason = $(window, '[data-tot-history] li div:last-child');
  assert.equal(reason.textContent, 'Reason: <b>Not now</b>', 'history text is never parsed as markup');
  assert.equal(reason.querySelector('b'), null);
});

test('someone who cannot decide sees who sent it, without the decision', async () => {
  const { window, open } = run(() => details({ ...pendingRequest, canDecide: false }));
  open();
  await settle();

  assert.equal($(window, '[data-tot-pending-title]').textContent, 'Waiting for HoD approval');
  assert.equal(hidden(window, '[data-tot-decide-form]'), true);
});

test('rejecting needs a reason before anything is sent', async () => {
  const { window, calls, open } = run(() => details(pendingRequest));
  open();
  await settle();
  const before = calls.length;

  $(window, '[data-tot-decide="false"]').click();
  await settle();

  assert.equal(calls.length, before, 'nothing posted');
  assert.equal($(window, '[data-tot-decide-error]').textContent, 'Give a reason to reject the update.');
});

test('approving posts the version token and reloads the drawer', async () => {
  let decided = false;
  const { window, calls, open } = run((url) => {
    if (url.includes('handler=TotDecide')) {
      decided = true;
      return { success: true, message: 'Transfer of Technology update approved.', card: { title: 'ToT completed', summary: 'Completed Oct 2026', tone: 'positive' } };
    }
    return decided ? details(null) : details(pendingRequest);
  });
  open();
  await settle();

  $(window, '[data-tot-decide="true"]').click();
  await settle(); await settle();

  const post = calls.find((call) => call.url.includes('handler=TotDecide'));
  assert.ok(post, 'decision posted');
  assert.equal(post.options.method, 'POST');
  assert.equal(post.options.body.get('approve'), 'true');
  assert.equal(post.options.body.get('rowVersion'), 'dmVyc2lvbg==');
  assert.equal(post.options.body.get('__RequestVerificationToken'), 'token');
  assert.equal(hidden(window, '[data-tot-pending]'), true, 'no longer pending after the reload');
  assert.equal($(window, '[data-tot-success]').textContent, 'Transfer of Technology update approved.');
  assert.equal($(window, '[data-tot-card-title]').textContent, 'ToT completed');
  assert.equal(hidden(window, '[data-tot-edit-button]'), false, 'Update details is available again once decided');
});

test('the drawer always renders the editor for managers, hidden while a request is pending', () => {
  const drawer = fs.readFileSync(path.join(__dirname, '..', '..', '..', 'Pages', 'Projects', '_ProjectTotDrawer.cshtml'), 'utf8');
  assert.doesNotMatch(drawer, /CanManageTot && !pending/, 'the editor must exist so a decision in place can unblock it');
  assert.match(drawer, /btn btn-primary @\(pending \? "d-none" : string\.Empty\)" data-tot-edit-button/);
});

test('a refused decision keeps the request and shows why', async () => {
  const { window, open } = run((url) => url.includes('handler=TotDecide')
    ? { ok: false, error: 'This request has changed since you opened it. Reload and try again.' }
    : details(pendingRequest));
  open();
  await settle();

  $(window, '[data-tot-decide-reason]').value = 'Certificate missing';
  $(window, '[data-tot-decide="false"]').click();
  await settle(); await settle();

  assert.equal($(window, '[data-tot-decide-error]').textContent, 'This request has changed since you opened it. Reload and try again.');
  assert.equal(hidden(window, '[data-tot-pending]'), false);
  assert.equal($(window, '[data-tot-decide-reason]').value, 'Certificate missing', 'the reason is kept');
});
