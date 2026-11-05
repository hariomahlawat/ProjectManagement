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

test('ToT is one page with one name and a working Dashboard link', () => {
  const index = read('Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml');
  assert.match(index, /asp-area="" asp-page="\/Dashboard\/Index">Dashboard<\/a>/);
  assert.doesNotMatch(index, /ToT tracker dashboard|ToT records|Transfer of Technology summary|Transfer of Technology tracker|asp-page="Summary"/);
  assert.match(index, /<h1 class="mb-1">Transfer of Technology<\/h1>/);
  // The old summary address only redirects to the one page.
  const summaryModel = read('Areas/ProjectOfficeReports/Pages/Tot/Summary.cshtml.cs');
  assert.match(summaryModel, /RedirectToPage\("\/Tot\/Index"/);
  assert.doesNotMatch(summaryModel, /TrackerReadService|ApplicationDbContext/);
});

test('the register keeps its search box when nothing matches', () => {
  const index = read('Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml');
  const search = index.indexOf('class="tot-searchbar"');
  const empty = index.indexOf('@if (!Model.Projects.Any())');
  assert.ok(search > 0 && empty > search, 'the search form comes before the empty-result branch');
  assert.doesNotMatch(index, /No request/);
  assert.doesNotMatch(index, /More filters/);
});

test('the long-waiting scope survives the filter and export dialogs', () => {
  const filters = read('Areas/ProjectOfficeReports/Pages/Tot/_TotFiltersModal.cshtml');
  const index = read('Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml');
  assert.match(filters, /<input type="hidden" asp-for="LongWaiting" \/>/);
  const exportForm = index.slice(index.indexOf('asp-page-handler="Export"'));
  assert.match(exportForm, /<input type="hidden" asp-for="LongWaiting" \/>/);
  assert.match(index, /<input type="hidden" name="LongWaiting" value="@Model.LongWaiting" \/>/);
});
