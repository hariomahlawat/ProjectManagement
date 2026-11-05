const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'overview-tot.js'), 'utf8');

// The parts of the Overview ToT card and drawer the script reads.
const markup = `
  <div id="project-tot-card"><span data-tot-card-title></span><span data-tot-card-summary></span></div>
  <div id="offcanvasTot" data-project-id="7">
    <div data-tot-root>
      <div data-tot-loading class="d-none"></div>
      <div data-tot-content>
        <div data-tot-global-error class="d-none"></div>
        <div data-tot-success class="d-none"></div>
        <div data-tot-summary-view></div>
        <div data-tot-edit-view class="d-none">
          <form data-tot-form action="/Projects/Overview/7?handler=TotUpdate">
            <div data-tot-errors class="d-none"></div>
            <select name="Status" data-tot-status>
              <option value="NotRequired">Not required</option>
              <option value="Completed">Completed</option>
            </select>
          </form>
        </div>
        <div data-tot-summary-footer><button type="button" data-tot-edit-button>Edit</button></div>
        <div data-tot-edit-footer class="d-none"><button type="button" data-tot-cancel-button></button></div>
      </div>
    </div>
  </div>`;

function run(fetchImpl) {
  const dom = new JSDOM(`<!doctype html><body>${markup}</body>`, {
    url: 'http://localhost/Projects/Overview/7?tot=edit',
    runScripts: 'outside-only'
  });
  const { window } = dom;
  window.fetch = () => fetchImpl(window);
  window.bootstrap = {
    Offcanvas: {
      getOrCreateInstance: (element) => ({
        show: () => element.dispatchEvent(new window.Event('show.bs.offcanvas'))
      })
    }
  };
  window.eval(script);
  return window;
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));
const isHidden = (window, selector) => window.document.querySelector(selector).classList.contains('d-none');

test('?tot=edit does not open the editor when the details fail to load', async () => {
  const window = run((page) => Promise.reject(new page.Error('Network down')));
  await settle();

  assert.equal(isHidden(window, '[data-tot-edit-view]'), true, 'editor must stay closed');
  assert.equal(isHidden(window, '[data-tot-edit-button]'), true, 'Edit must be hidden after a failed load');
  assert.equal(window.document.querySelector('[data-tot-global-error]').textContent, 'Network down');
  assert.equal(window.location.search, '', 'the tot parameter is removed');
});

test('?tot=edit opens the editor once the details have loaded', async () => {
  const window = run(() => Promise.resolve({
    ok: true,
    json: () => Promise.resolve({
      success: true,
      canManage: true,
      pendingApproval: false,
      input: { status: 'Completed' },
      card: { title: 'ToT completed', summary: 'Completed Mar 2026', tone: 'positive' }
    })
  }));
  await settle();

  assert.equal(isHidden(window, '[data-tot-edit-view]'), false, 'editor opens');
  assert.equal(window.document.querySelector('[data-tot-status]').value, 'Completed');
});
