const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'register-view.js'), 'utf8');

// Four records: a pending patent, a granted copyright, a withdrawn patent (closed) and an
// unlinked pending patent with no evidence. Row facts mirror what Index.cshtml renders.
const RECORDS = [
  { Id: 1, Title: 'Driving simulator', ApplicationNumber: 'P-001', IprType: 'Patent', Status: 'Patent pending', ProjectId: 10, ProjectName: 'Tank Sml', FiledBy: 'Lt Col A', FiledOn: '2021-06-10T00:00:00', GrantedOn: null, ExternalRemark: null, PendingAge: '5y 4m', Attachments: [{ Id: 7, FileName: 'receipt.pdf', FileSize: '1 MB', UploadedBy: 'X', UploadedAt: '01 Jan 2022' }] },
  { Id: 2, Title: 'Drill companion', ApplicationNumber: 'C-002', IprType: 'Copyright', Status: 'Copyright registered', ProjectId: 11, ProjectName: 'Drill App', FiledBy: '', FiledOn: '2022-01-18T00:00:00', GrantedOn: '2023-08-29T00:00:00', ExternalRemark: 'Registered', Attachments: [] },
  { Id: 3, Title: 'Old recoil kit', ApplicationNumber: 'P-003', IprType: 'Patent', Status: 'Withdrawn', ProjectId: 10, ProjectName: 'Tank Sml', FiledBy: '', FiledOn: '2019-02-01T00:00:00', GrantedOn: null, ExternalRemark: null, Attachments: [] },
  { Id: 4, Title: 'Remote pilot aircraft', ApplicationNumber: 'P-004', IprType: 'Patent', Status: 'Patent pending', ProjectId: null, ProjectName: 'Unassigned project', FiledBy: '', FiledOn: '2026-04-02T00:00:00', GrantedOn: null, ExternalRemark: null, PendingAge: '6m', Attachments: [] }
];

const ROWS = [
  { id: 1, type: 'Patent', status: 'Filed', counted: true, closed: false, files: 1, projectId: 10, project: 'Tank Sml', filed: 2021, granted: '' },
  { id: 2, type: 'Copyright', status: 'Granted', counted: true, closed: false, files: 0, projectId: 11, project: 'Drill App', filed: 2022, granted: 2023 },
  { id: 3, type: 'Patent', status: 'Withdrawn', counted: false, closed: true, files: 0, projectId: 10, project: 'Tank Sml', filed: 2019, granted: '' },
  { id: 4, type: 'Patent', status: 'Filed', counted: true, closed: false, files: 0, projectId: '', project: '', filed: 2026, granted: '' }
];

function createDom(state = {}, url = 'http://localhost/ProjectOfficeReports/Ipr?tab=records') {
  const rows = ROWS.map(row => {
    const record = RECORDS.find(item => item.Id === row.id);
    return `<tr data-ipr-row data-id="${row.id}" data-type="${row.type}" data-status="${row.status}"
        data-counted="${row.counted}" data-closed="${row.closed}" data-files="${row.files}"
        data-project-id="${row.projectId}" data-project="${row.project}" data-filed-year="${row.filed}"
        data-granted-year="${row.granted}" data-search="${record.Title}&#10;${record.ApplicationNumber}&#10;${row.project}">
        <td><button type="button" data-ipr-open="${row.id}">${record.Title}</button></td></tr>`;
  }).join('');
  const stateJson = JSON.stringify(Object.assign({ query: '', types: [], statuses: [], year: '', dateBasis: 'Filed', linkage: 'All', evidence: 'All', projectId: '', group: '', selectedRecordId: '' }, state))
    .replace(/"/g, '&quot;');

  const dom = new JSDOM(`<!doctype html><html><body>
    <a data-ipr-scope="closed" href="/ProjectOfficeReports/Ipr?tab=records&statuses=Rejected&statuses=Withdrawn">Closed</a>
    <a data-ipr-export href="/ProjectOfficeReports/Ipr?handler=Export">Export</a>
    <section data-ipr-register data-ipr-state="${stateJson}" data-ipr-project-base-url="/Projects/Overview" data-ipr-download-url="/ProjectOfficeReports/Ipr/Download">
      <input data-ipr-filter-query />
      <button data-ipr-filter-type="all"></button>
      <button data-ipr-filter-type="Patent"><span data-ipr-count></span></button>
      <button data-ipr-filter-type="Copyright"><span data-ipr-count></span></button>
      <button data-ipr-filter-status="counted"><span data-ipr-count></span></button>
      <button data-ipr-filter-status="Filed"><span data-ipr-count></span></button>
      <button data-ipr-filter-status="Granted"><span data-ipr-count></span></button>
      <button data-ipr-filter-status="closed"><span data-ipr-count></span></button>
      <button data-ipr-filter-flag="missing"><span data-ipr-count></span></button>
      <button data-ipr-filter-flag="unassigned"><span data-ipr-count></span></button>
      <select data-ipr-filter-year><option value="">Any year</option><option value="2021">2021</option></select>
      <span data-ipr-year-label></span>
      <select data-ipr-group><option value=""></option><option value="project">Project</option><option value="status">Status</option><option value="year">Year</option></select>
      <span data-ipr-project-chip class="d-none"><span data-ipr-project-chip-label></span><button data-ipr-project-chip-clear></button></span>
      <span data-ipr-result-count></span>
      <button data-ipr-clear class="d-none"></button>
      <table><tbody data-ipr-rows>${rows}</tbody></table>
      <div data-ipr-empty class="d-none"><h3 data-ipr-empty-title></h3><p data-ipr-empty-hint></p></div>
      <div data-ipr-panel-scrim hidden></div>
      <aside data-ipr-panel hidden tabindex="-1">
        <span data-ipr-panel-dot></span><span data-ipr-panel-eyebrow></span>
        <h2 data-ipr-panel-title></h2><span data-ipr-panel-status></span>
        <button data-ipr-panel-close></button>
        <a data-ipr-panel-edit href="#"></a><a data-ipr-panel-grant href="#"></a><a data-ipr-panel-evidence href="#"></a>
        <dd data-ipr-panel-number></dd><dd data-ipr-panel-type></dd><dd data-ipr-panel-filed></dd>
        <dt data-ipr-panel-granted-label></dt><dd data-ipr-panel-granted></dd><dd data-ipr-panel-by></dd>
        <dd data-ipr-panel-project></dd>
        <span data-ipr-panel-progress-filed></span>
        <li data-ipr-panel-progress-outcome><strong data-ipr-panel-outcome-title></strong><span data-ipr-panel-outcome-text></span></li>
        <div data-ipr-panel-files></div><p data-ipr-panel-notes></p>
      </aside>
      <script type="application/json" id="iprRecordData">${JSON.stringify(RECORDS)}</script>
    </section>
  </body></html>`, { url, runScripts: 'outside-only', pretendToBeVisual: true });
  dom.window.HTMLElement.prototype.scrollIntoView = function scrollIntoView() {};
  dom.window.eval(script);
  return dom;
}

const visibleIds = dom => Array.from(dom.window.document.querySelectorAll('[data-ipr-row]'))
  .filter(row => !row.hidden)
  .map(row => Number(row.dataset.id));

const count = (dom, selector) => dom.window.document.querySelector(`${selector} [data-ipr-count]`).textContent;

test('register shows the positive count by default and counts each status', () => {
  const dom = createDom();
  assert.deepEqual(visibleIds(dom), [1, 2, 4]);
  assert.equal(count(dom, '[data-ipr-filter-status="counted"]'), '3');
  assert.equal(count(dom, '[data-ipr-filter-status="Filed"]'), '2');
  assert.equal(count(dom, '[data-ipr-filter-status="Granted"]'), '1');
  assert.equal(count(dom, '[data-ipr-filter-status="closed"]'), '1');
  assert.equal(count(dom, '[data-ipr-filter-flag="missing"]'), '2');
  assert.equal(count(dom, '[data-ipr-filter-flag="unassigned"]'), '1');
  assert.equal(dom.window.document.querySelector('[data-ipr-result-count]').textContent, '3 records');
});

test('closed status lists history only and keeps the server parameter names in the URL and export', () => {
  const dom = createDom();
  dom.window.document.querySelector('[data-ipr-filter-status="closed"]').click();
  assert.deepEqual(visibleIds(dom), [3]);
  const url = new URL(dom.window.location.href);
  assert.deepEqual(url.searchParams.getAll('statuses'), ['Rejected', 'Withdrawn']);
  const exportUrl = new URL(dom.window.document.querySelector('[data-ipr-export]').href);
  assert.equal(exportUrl.searchParams.get('handler'), 'Export');
  assert.deepEqual(exportUrl.searchParams.getAll('statuses'), ['Rejected', 'Withdrawn']);
});

test('summary card filters in place instead of reloading', () => {
  const dom = createDom();
  const card = dom.window.document.querySelector('[data-ipr-scope="closed"]');
  const event = new dom.window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
  card.dispatchEvent(event);
  assert.equal(event.defaultPrevented, true);
  assert.deepEqual(visibleIds(dom), [3]);
});

test('category, flags and year combine and map to linkage and evidence parameters', () => {
  const dom = createDom();
  const doc = dom.window.document;
  doc.querySelector('[data-ipr-filter-flag="missing"]').click();
  assert.deepEqual(visibleIds(dom), [2, 4]);
  doc.querySelector('[data-ipr-filter-type="Patent"]').click();
  assert.deepEqual(visibleIds(dom), [4]);
  doc.querySelector('[data-ipr-filter-flag="unassigned"]').click();
  const url = new URL(dom.window.location.href);
  assert.equal(url.searchParams.get('evidence'), 'Missing');
  assert.equal(url.searchParams.get('linkage'), 'Unassigned');
  assert.deepEqual(url.searchParams.getAll('types'), ['Patent']);
  assert.deepEqual(visibleIds(dom), [4]);
});

test('search matches title, filing number and project', async () => {
  const dom = createDom();
  const input = dom.window.document.querySelector('[data-ipr-filter-query]');
  input.value = 'drill';
  input.dispatchEvent(new dom.window.Event('input'));
  await new Promise(resolve => setTimeout(resolve, 200));
  assert.deepEqual(visibleIds(dom), [2]);
  assert.equal(new URL(dom.window.location.href).searchParams.get('query'), 'drill');

  // Like the server, a term must sit inside one field; it never spans title and number.
  input.value = 'simulator P-001';
  input.dispatchEvent(new dom.window.Event('input'));
  await new Promise(resolve => setTimeout(resolve, 200));
  assert.deepEqual(visibleIds(dom), []);

  input.value = 'p-001';
  input.dispatchEvent(new dom.window.Event('input'));
  await new Promise(resolve => setTimeout(resolve, 200));
  assert.deepEqual(visibleIds(dom), [1]);
});

test('group by project inserts one header per project, unlinked first', () => {
  const dom = createDom({ group: 'project' });
  const headers = Array.from(dom.window.document.querySelectorAll('[data-ipr-group-row] span')).map(item => item.textContent);
  assert.deepEqual(headers, ['Not linked to a project', 'Drill App', 'Tank Sml']);
  assert.equal(new URL(dom.window.location.href).searchParams.get('group'), 'project');
});

test('row click opens the record panel with escaped text; Escape closes it', () => {
  const dom = createDom();
  const doc = dom.window.document;
  doc.querySelector('[data-ipr-row][data-id="1"] td').click();
  const panel = doc.querySelector('[data-ipr-panel]');
  assert.equal(panel.hidden, false);
  assert.equal(doc.querySelector('[data-ipr-panel-title]').textContent, 'Driving simulator');
  assert.equal(doc.querySelector('[data-ipr-panel-filed]').textContent, '10 Jun 2021');
  assert.equal(doc.querySelector('[data-ipr-panel-outcome-text]').textContent, 'Pending for 5y 4m');
  assert.match(doc.querySelector('[data-ipr-panel-files] a').href, /Download\?iprRecordId=1&attachmentId=7/);
  assert.match(doc.querySelector('[data-ipr-panel-edit]').href, /mode=edit&id=1/);
  assert.equal(new URL(dom.window.location.href).searchParams.get('selectedRecordId'), '1');

  doc.dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'Escape' }));
  assert.equal(panel.hidden, true);
  assert.equal(new URL(dom.window.location.href).searchParams.get('selectedRecordId'), null);
});

test('a selected record in the URL opens its panel on load', () => {
  const dom = createDom({ selectedRecordId: '2' });
  const doc = dom.window.document;
  assert.equal(doc.querySelector('[data-ipr-panel]').hidden, false);
  assert.equal(doc.querySelector('[data-ipr-panel-granted-label]').textContent, 'Registered on');
  assert.equal(doc.querySelector('[data-ipr-panel-files] p').textContent, 'No filing receipt or grant certificate uploaded yet.');
});

test('register script builds the panel with textContent, never innerHTML', () => {
  assert.doesNotMatch(script, /innerHTML/);
});
