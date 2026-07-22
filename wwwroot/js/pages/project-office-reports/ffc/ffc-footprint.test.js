const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'ffc-footprint.js'), 'utf8');

function createDom(view = 'cards', selectedCountryId = '') {
  const countries = [{
    id: 7,
    name: 'Myanmar',
    iso3: 'MMR',
    recordCount: 1,
    projectCount: 1,
    installed: 1,
    delivered: 0,
    planned: 0,
    total: 1,
    years: [{
      recordId: 9,
      year: 2026,
      projectCount: 1,
      installed: 1,
      delivered: 0,
      planned: 0,
      total: 1,
      overallPosition: 'Ready',
      workspaceUrl: '/workspace/9',
      detailedUrl: '/detail/9',
      projects: [{ name: '<Unsafe>', quantity: 1, position: 'Installed', progress: '<b>Progress</b>' }]
    }]
  }];

  const dom = new JSDOM(`<!doctype html><html><body>
    <main data-ffc-footprint data-view="${view}" data-metric="total" data-selected-country-id="${selectedCountryId}" data-geo-url="/map.geojson">
      <button data-ffc-country-trigger data-country-id="7">Myanmar</button>
      <div class="offcanvas" id="ffcCountryPanel">
        <button class="btn-close"></button>
        <div data-ffc-panel-iso></div>
        <h2 data-ffc-panel-title></h2>
        <div data-ffc-panel-summary></div>
        <div data-ffc-panel-years></div>
      </div>
    </main>
    <script type="application/json" id="ffc-footprint-data">${JSON.stringify(countries)}</script>
  </body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.test/ProjectOfficeReports/FFC/Footprint?view=cards'
  });

  let showCount = 0;
  dom.window.bootstrap = {
    Offcanvas: {
      getOrCreateInstance: () => ({ show() { showCount += 1; } })
    }
  };
  return { dom, getShowCount: () => showCount };
}

test('country cards open the shared detail panel and render text safely', () => {
  const { dom, getShowCount } = createDom();
  dom.window.eval(script);

  dom.window.document.querySelector('[data-ffc-country-trigger]').click();

  assert.equal(getShowCount(), 1);
  assert.equal(dom.window.document.querySelector('[data-ffc-panel-title]').textContent, 'Myanmar');
  const project = dom.window.document.querySelector('.ffc-country-panel-project strong');
  assert.equal(project.textContent, '<Unsafe>');
  assert.equal(project.querySelector('b'), null);
  assert.match(dom.window.document.querySelector('.ffc-country-panel-project p').textContent, /<b>Progress<\/b>/);
});

test('selectedCountryId opens the requested country after page load', async () => {
  const { dom, getShowCount } = createDom('cards', '7');
  dom.window.eval(script);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(getShowCount(), 1);
});

test('country-card mode does not require Leaflet', () => {
  const { dom } = createDom('cards');
  assert.equal(dom.window.L, undefined);
  assert.doesNotThrow(() => dom.window.eval(script));
});
