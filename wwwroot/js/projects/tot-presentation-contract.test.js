const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const totViews = [
  'Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml',
  'Areas/ProjectOfficeReports/Pages/Tot/Summary.cshtml',
  'Pages/Projects/_ProjectTotDrawer.cshtml'
];

test('ToT text is at least 13px (0.8125rem) and labels are not uppercase', () => {
  for (const rel of ['wwwroot/css/tot.css', 'wwwroot/css/pages/project-tot-drawer.css']) {
    const css = read(rel);
    for (const match of css.matchAll(/font-size:\s*(\d*\.?\d+)rem/g)) {
      assert.ok(Number(match[1]) >= 0.8125, `${rel}: font-size ${match[1]}rem is below 13px`);
    }
    assert.doesNotMatch(css, /text-transform:\s*uppercase/, `${rel} has an uppercase label`);
  }
});

test('ToT pages use one date style', () => {
  for (const rel of totViews) {
    assert.doesNotMatch(read(rel), /dd\/MM\/yyyy/, `${rel} uses dd/MM/yyyy`);
  }
});

test('ToT pages share one name and a working Dashboard link', () => {
  const index = read('Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml');
  const summary = read('Areas/ProjectOfficeReports/Pages/Tot/Summary.cshtml');
  for (const page of [index, summary]) {
    assert.match(page, /asp-area="" asp-page="\/Dashboard\/Index">Dashboard<\/a>/);
    assert.doesNotMatch(page, /ToT tracker dashboard|ToT records|Transfer of Technology summary|Transfer of Technology tracker/);
  }
  assert.match(index, /<h1 class="mb-0">Transfer of Technology register<\/h1>/);
  assert.match(summary, /<h1 class="tot-summary__heading mb-2">Transfer of Technology<\/h1>/);
});

test('the register keeps its search box when nothing matches', () => {
  const index = read('Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml');
  const search = index.indexOf('class="tot-searchbar"');
  const empty = index.indexOf('@if (!Model.Projects.Any())');
  assert.ok(search > 0 && empty > search, 'the search form comes before the empty-result branch');
  assert.doesNotMatch(index, /No request/);
  assert.doesNotMatch(index, /More filters/);
});
