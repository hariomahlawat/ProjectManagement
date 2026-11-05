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

test('a slow answer for the project opened earlier never fills the drawer', async () => {
  const dom = new JSDOM(`<!doctype html><body>${markup(false, '')}</body>`, {
    url: 'http://localhost/ProjectOfficeReports/Tot',
    runScripts: 'outside-only'
  });
  const { window } = dom;
  const pending = {};
  window.fetch = (url) => {
    const id = Number(new URL(String(url), 'http://localhost').searchParams.get('id'));
    return new Promise((resolve) => {
      pending[id] = () => resolve({ ok: true, json: () => Promise.resolve({
        success: true, projectId: id, projectName: `Project ${id}`, canManage: true, pendingApproval: false,
        input: { status: 'NotStarted' }, summary: { statusLabel: `Status of ${id}`, facts: [] }
      }) });
    });
  };
  window.bootstrap = {
    Offcanvas: { getOrCreateInstance: (element) => ({ show: () => element.dispatchEvent(new window.Event('show.bs.offcanvas')) }) }
  };
  window.eval(script);

  window.document.querySelector('#row-630').click();
  window.document.querySelector('#row-631').click();
  pending[631]();
  await settle(); await settle();
  pending[630]();
  await settle(); await settle();

  assert.equal(window.document.querySelector('[data-tot-status-label]').textContent, 'Status of 631');
  assert.equal(new URL(window.document.querySelector('[data-tot-form]').action).search, '?handler=Tot&id=631');
  assert.equal(window.document.querySelector('[data-tot-project-field]').value, '631');
});

test('when the next project fails to load, nothing of the previous one stays or can be posted', async () => {
  const withActions = `
    <div id="offcanvasTot" data-project-id="" data-tot-endpoint="/ProjectOfficeReports/Tot">
      <p data-tot-project-name></p>
      <div data-tot-root>
        <div data-tot-loading class="d-none"></div>
        <div data-tot-content>
          <div data-tot-global-error class="d-none"></div>
          <div data-tot-success class="d-none"></div>
          <section data-tot-summary-view>
            <strong data-tot-status-label></strong>
            <button type="button" data-tot-add-remark>Add remark</button>
            <section class="d-none" data-tot-pending>
              <h6 data-tot-pending-title></h6><span data-tot-pending-meta></span>
              <table><tbody data-tot-changes></tbody></table>
              <form class="d-none" data-tot-decide-form action="/ProjectOfficeReports/Tot?handler=TotDecide">
                <input type="hidden" name="rowVersion" data-tot-decide-version />
                <textarea name="reason" data-tot-decide-reason></textarea>
                <div class="d-none" data-tot-decide-error></div>
                <button type="button" data-tot-decide="true">Approve</button>
              </form>
            </section>
            <form class="d-none" data-tot-remark-form action="/ProjectOfficeReports/Tot?handler=TotRemark">
              <input type="hidden" name="projectId" data-tot-project-field />
              <textarea name="body" data-tot-remark-body></textarea>
              <div class="d-none" data-tot-remark-error></div>
            </form>
          </section>
          <div data-tot-summary-footer><button type="button" class="d-none" data-tot-edit-button>Update details</button></div>
        </div>
      </div>
    </div>`;
  const dom = new JSDOM(`<!doctype html><body>${withActions}</body>`, { url: 'http://localhost/ProjectOfficeReports/Tot', runScripts: 'outside-only' });
  const { window } = dom;
  const posts = [];
  window.fetch = (url, options = {}) => {
    const id = Number(new URL(String(url), 'http://localhost').searchParams.get('id'));
    if (options.method === 'POST') { posts.push(String(url)); return Promise.resolve({ ok: true, json: () => Promise.resolve({ success: true }) }); }
    if (id === 2) return Promise.resolve({ ok: false, json: () => Promise.resolve({ error: 'Project not found.' }) });
    return Promise.resolve({ ok: true, json: () => Promise.resolve({
      success: true, projectId: 1, projectName: 'Project 1', canManage: true, pendingApproval: true,
      pending: { canDecide: true, changes: [{ field: 'Status', now: 'In progress', proposed: 'Completed' }], rowVersion: 'djE=' },
      input: { status: 'InProgress' }, summary: { statusLabel: 'In progress', facts: [] }
    }) });
  };
  let offcanvasEl;
  window.bootstrap = { Offcanvas: { getOrCreateInstance: (element) => { offcanvasEl = element; return { show: () => element.dispatchEvent(new window.Event('show.bs.offcanvas')) }; } } };
  window.eval(script);
  const $ = (selector) => window.document.querySelector(selector);
  const opener = (id) => { const a = window.document.createElement('a'); a.href = '#'; a.setAttribute('data-tot-open-project-id', String(id)); window.document.body.appendChild(a); return a; };

  opener(1).click();
  await settle(); await settle();
  assert.equal($('[data-tot-status-label]').textContent, 'In progress');
  assert.equal($('[data-tot-decide-form]').classList.contains('d-none'), false);

  $('[data-tot-decide-reason]').value = 'Reason written for project 1.';
  opener(2).click();
  await settle(); await settle();

  assert.equal($('[data-tot-decide-reason]').value, '', 'a reason typed for project 1 is not carried over');
  assert.equal($('[data-tot-global-error]').textContent, 'Project not found.');
  assert.equal($('[data-tot-status-label]').textContent, '', 'project 1 is no longer shown');
  assert.equal($('[data-tot-pending]').classList.contains('d-none'), true);
  assert.equal($('[data-tot-decide-form]').classList.contains('d-none'), true);
  assert.equal($('[data-tot-add-remark]').classList.contains('d-none'), true);
  assert.equal($('[data-tot-edit-button]').classList.contains('d-none'), true);
  assert.equal($('[data-tot-project-field]').value, '');
  assert.equal($('[data-tot-decide-version]').value, '');

  $('[data-tot-decide="true"]').click();
  $('[data-tot-remark-body]').value = 'This must not reach project 1.';
  $('[data-tot-remark-form]').dispatchEvent(new window.Event('submit', { cancelable: true }));
  await settle();
  assert.deepEqual(posts, [], 'nothing is posted while no project is loaded');
  assert.ok(offcanvasEl);
});

test('a selected project in the address opens on load', async () => {
  const { calls, shown } = run({ openOnLoad: true, projectId: 631 });
  await settle();

  assert.equal(shown(), 1);
  assert.deepEqual(calls, ['/ProjectOfficeReports/Tot?handler=TotDetails&id=631']);
});
