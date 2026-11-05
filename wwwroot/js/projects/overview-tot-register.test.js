const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'overview-tot.js'), 'utf8');

// The ToT register: rows open the shared drawer in place (redesign PR 6b).
const markup = (openOnLoad, projectId) => `
  <a href="/ProjectOfficeReports/Tot?SelectedProjectId=630" class="tot-row" id="row-630"
     data-tot-open-project-id="630" data-tot-project-name="BMP-II Sml"><span>BMP-II Sml</span></a>
  <a href="/ProjectOfficeReports/Tot?SelectedProjectId=631" class="tot-row" id="row-631"
     data-tot-open-project-id="631" data-tot-project-name="HE 36 Grenade Sml">HE 36 Grenade Sml</a>
  <div id="offcanvasTot" data-project-id="${projectId}" data-tot-endpoint="/ProjectOfficeReports/Tot" data-tot-open-on-load="${openOnLoad}">
    <p data-tot-project-name></p>
    <div data-tot-root>
      <div data-tot-loading class="d-none"></div>
      <div data-tot-content>
        <div data-tot-global-error class="d-none"></div>
        <div data-tot-success class="d-none"></div>
        <section data-tot-summary-view><strong data-tot-status-label></strong></section>
        <section class="d-none" data-tot-edit-view>
          <form data-tot-form action="/ProjectOfficeReports/Tot?handler=Tot">
            <input type="hidden" name="ProjectId" data-tot-project-field />
            <select name="Status" data-tot-status><option value="NotStarted">Not started</option></select>
          </form>
        </section>
        <div data-tot-summary-footer><button type="button" class="d-none" data-tot-edit-button>Update details</button></div>
      </div>
    </div>
  </div>`;

function run({ openOnLoad = false, projectId = '' } = {}) {
  const dom = new JSDOM(`<!doctype html><body>${markup(openOnLoad, projectId)}</body>`, {
    url: 'http://localhost/ProjectOfficeReports/Tot',
    runScripts: 'outside-only'
  });
  const { window } = dom;
  const calls = [];
  let shown = 0;
  window.fetch = (url) => {
    calls.push(String(url));
    const id = Number(new URL(String(url), 'http://localhost').searchParams.get('id'));
    const names = { 630: 'BMP-II Sml', 631: 'HE 36 Grenade Sml' };
    return Promise.resolve({ ok: true, json: () => Promise.resolve({
      success: true, projectId: id, projectName: names[id], canManage: id === 631, pendingApproval: false,
      input: { status: 'NotStarted' }, summary: { statusLabel: `Status of ${id}`, facts: [] }
    }) });
  };
  window.bootstrap = {
    Offcanvas: {
      getOrCreateInstance: (element) => ({ show: () => { shown += 1; element.dispatchEvent(new window.Event('show.bs.offcanvas')); } })
    }
  };
  window.eval(script);
  return { window, calls, shown: () => shown };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

test('clicking a row opens the drawer for that project instead of reloading', async () => {
  const { window, calls, shown } = run();
  const click = new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
  window.document.querySelector('#row-630 span').dispatchEvent(click);
  await settle();

  assert.equal(click.defaultPrevented, true, 'the link does not navigate');
  assert.equal(shown(), 1);
  assert.deepEqual(calls, ['/ProjectOfficeReports/Tot?handler=TotDetails&id=630']);
  assert.equal(window.document.querySelector('[data-tot-project-name]').textContent, 'BMP-II Sml');
  assert.equal(window.document.querySelector('[data-tot-status-label]').textContent, 'Status of 630');
  assert.equal(window.document.querySelector('[data-tot-edit-button]').classList.contains('d-none'), true);
});

test('another row switches the drawer to that project and its rights', async () => {
  const { window, calls } = run();
  window.document.querySelector('#row-630').click();
  await settle();
  window.document.querySelector('#row-631').click();
  await settle();

  assert.equal(calls.at(-1), '/ProjectOfficeReports/Tot?handler=TotDetails&id=631');
  assert.equal(new URL(window.document.querySelector('[data-tot-form]').action).search, '?handler=Tot&id=631');
  assert.equal(window.document.querySelector('[data-tot-project-field]').value, '631');
  assert.equal(window.document.querySelector('[data-tot-edit-button]').classList.contains('d-none'), false);
});

test('a ctrl-click keeps the link, so the project can open in a new tab', async () => {
  const { window, calls } = run();
  const click = new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ctrlKey: true });
  window.document.querySelector('#row-630').dispatchEvent(click);
  await settle();

  assert.equal(click.defaultPrevented, false);
  assert.deepEqual(calls, []);
});

test('a selected project in the address opens on load', async () => {
  const { calls, shown } = run({ openOnLoad: true, projectId: 631 });
  await settle();

  assert.equal(shown(), 1);
  assert.deepEqual(calls, ['/ProjectOfficeReports/Tot?handler=TotDetails&id=631']);
});
