const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'overview-tot.js'), 'utf8');

// The shared drawer hosted by another page: no project card, its own endpoint.
const markup = `
  <div id="offcanvasTot" data-project-id="55" data-tot-endpoint="/ProjectOfficeReports/Tot">
    <div data-tot-root>
      <div data-tot-loading class="d-none"></div>
      <div data-tot-content>
        <div data-tot-global-error class="d-none"></div>
        <div data-tot-success class="d-none"></div>
        <section data-tot-summary-view>
          <button type="button" data-tot-add-remark>Add remark</button>
          <form class="d-none" data-tot-remark-form action="/ProjectOfficeReports/Tot?handler=TotRemark">
            <input type="hidden" name="projectId" data-tot-project-field />
            <textarea name="body" data-tot-remark-body></textarea>
          </form>
        </section>
        <section class="d-none" data-tot-edit-view>
          <form data-tot-form action="/ProjectOfficeReports/Tot?handler=Tot">
            <input type="hidden" name="ProjectId" data-tot-project-field />
            <select name="Status" data-tot-status><option value="NotStarted">Not started</option></select>
          </form>
        </section>
        <div data-tot-summary-footer>
          <a href="#" data-tot-open-project>Open project</a>
          <button type="button" class="d-none" data-tot-edit-button>Update details</button>
        </div>
      </div>
    </div>
  </div>`;

function run(payload) {
  const dom = new JSDOM(`<!doctype html><body>${markup}</body>`, {
    url: 'http://localhost/ProjectOfficeReports/Tot?tot=open',
    runScripts: 'outside-only'
  });
  const { window } = dom;
  const calls = [];
  window.fetch = (url) => {
    calls.push(String(url));
    return Promise.resolve({ ok: true, json: () => Promise.resolve(payload) });
  };
  window.bootstrap = {
    Offcanvas: {
      getOrCreateInstance: (element) => ({ show: () => element.dispatchEvent(new window.Event('show.bs.offcanvas')) })
    }
  };
  window.eval(script);
  return { window, calls };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

test('the drawer works without a project card and uses its host page endpoint', async () => {
  const { window, calls } = run({ success: true, canManage: false, pendingApproval: false, input: { status: 'NotStarted' }, summary: { statusLabel: 'Not started', facts: [] } });
  await settle();

  assert.equal(calls[0], '/ProjectOfficeReports/Tot?handler=TotDetails&id=55');
  const $ = (selector) => window.document.querySelector(selector);
  assert.equal(new URL($('[data-tot-form]').action).search, '?handler=Tot&id=55');
  assert.equal(new URL($('[data-tot-remark-form]').action).search, '?handler=TotRemark&id=55');
  assert.deepEqual([...window.document.querySelectorAll('[data-tot-project-field]')].map((field) => field.value), ['55', '55']);
  assert.equal(new URL($('[data-tot-open-project]').href).pathname, '/Projects/Overview/55');
  assert.equal($('[data-tot-add-remark]').classList.contains('d-none'), true, 'no remark for a project the user cannot manage');
  assert.equal($('[data-tot-edit-button]').classList.contains('d-none'), true);
});

test('a project the user may manage shows Update details and Add remark', async () => {
  const { window } = run({ success: true, canManage: true, pendingApproval: false, input: { status: 'NotStarted' }, summary: { statusLabel: 'Not started', facts: [] } });
  await settle();

  assert.equal(window.document.querySelector('[data-tot-edit-button]').classList.contains('d-none'), false);
  assert.equal(window.document.querySelector('[data-tot-add-remark]').classList.contains('d-none'), false);
});
