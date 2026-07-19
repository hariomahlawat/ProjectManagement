const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'ffc-powerpoint-export.js'), 'utf8');

function createDom() {
  const dom = new JSDOM(`<!doctype html><html><body>
    <form action="/ProjectOfficeReports/FFC/Footprint?handler=ExportPowerPoint" data-ffc-ppt-form>
      <div data-ffc-ppt-error tabindex="-1" hidden></div>
      <div data-ffc-ppt-success hidden></div>
      <label><input type="radio" name="PowerPoint.Scope" value="current" checked></label>
      <label><input type="radio" name="PowerPoint.Scope" value="selected"></label>
      <div data-ffc-ppt-countries hidden>
        <button type="button" data-ffc-ppt-select-all>Select all</button>
        <button type="button" data-ffc-ppt-clear-all>Clear</button>
        <input type="checkbox" name="PowerPoint.SelectedCountryIds" value="1">
        <input type="checkbox" name="PowerPoint.SelectedCountryIds" value="2">
        <div data-ffc-ppt-country-error hidden></div>
      </div>
      <label><input type="radio" name="PowerPoint.PresentationType" value="executive" checked></label>
      <label><input type="radio" name="PowerPoint.PresentationType" value="full"></label>
      <div data-ffc-ppt-detail-options>
        <label><input type="checkbox" name="PowerPoint.IncludeProjects" checked></label>
        <label data-ffc-ppt-progress-option><input type="checkbox" name="PowerPoint.IncludeProgress" checked></label>
      </div>
      <input name="PowerPoint.Title" value="FFC Global Portfolio" required>
      <button type="submit" data-ffc-ppt-submit><span data-ffc-ppt-submit-text>Generate and download</span></button>
    </form>
  </body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.test/ProjectOfficeReports/FFC/Footprint'
  });

  const downloads = [];
  dom.window.URL.createObjectURL = () => 'blob:ffc-powerpoint';
  dom.window.URL.revokeObjectURL = () => {};
  dom.window.HTMLAnchorElement.prototype.click = function click() {
    downloads.push({ href: this.href, download: this.download });
  };
  return { dom, downloads };
}

function tick() {
  return new Promise(resolve => setTimeout(resolve, 0));
}

test('executive brief disables full-portfolio detail controls', () => {
  const { dom } = createDom();
  dom.window.eval(script);

  assert.equal(dom.window.document.querySelector('[name="PowerPoint.IncludeProjects"]').disabled, true);
  assert.equal(dom.window.document.querySelector('[name="PowerPoint.IncludeProgress"]').disabled, true);

  const full = dom.window.document.querySelector('[name="PowerPoint.PresentationType"][value="full"]');
  full.checked = true;
  full.dispatchEvent(new dom.window.Event('change', { bubbles: true }));

  assert.equal(dom.window.document.querySelector('[name="PowerPoint.IncludeProjects"]').disabled, false);
  assert.equal(dom.window.document.querySelector('[name="PowerPoint.IncludeProgress"]').disabled, false);
});

test('selected-country scope requires at least one country before export', async () => {
  const { dom } = createDom();
  let fetchCount = 0;
  dom.window.fetch = async () => { fetchCount += 1; };
  dom.window.eval(script);

  const selected = dom.window.document.querySelector('[name="PowerPoint.Scope"][value="selected"]');
  selected.checked = true;
  selected.dispatchEvent(new dom.window.Event('change', { bubbles: true }));
  dom.window.document.querySelector('form').dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }));
  await tick();

  assert.equal(fetchCount, 0);
  assert.equal(dom.window.document.querySelector('[data-ffc-ppt-country-error]').hidden, false);
});

test('successful export downloads the returned pptx and reports slide count', async () => {
  const { dom, downloads } = createDom();
  dom.window.fetch = async () => ({
    ok: true,
    headers: new dom.window.Headers({
      'content-disposition': 'attachment; filename="FFC_Portfolio_2026-07-19.pptx"',
      'X-FFC-Presentation-Slides': '9'
    }),
    blob: async () => new dom.window.Blob(['pptx'], { type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation' })
  });
  dom.window.eval(script);

  dom.window.document.querySelector('form').dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }));
  await tick();
  await tick();

  assert.equal(downloads.length, 1);
  assert.equal(downloads[0].download, 'FFC_Portfolio_2026-07-19.pptx');
  assert.match(dom.window.document.querySelector('[data-ffc-ppt-success]').textContent, /9 slides/);
  assert.equal(dom.window.document.querySelector('[data-ffc-ppt-submit]').disabled, false);
});
