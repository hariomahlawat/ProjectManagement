const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.resolve(__dirname, 'index.js'), 'utf8');
const formSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_RecordFormPartial.cshtml'),
  'utf8'
);
const pickerSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_ProjectSearchPicker.cshtml'),
  'utf8'
);
const stateSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_RegisterStateFields.cshtml'),
  'utf8'
);
const cssSource = fs.readFileSync(
  path.resolve(__dirname, '../../../css/site-ipr.css'),
  'utf8'
);
const filterDrawerSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_IprFilterDrawer.cshtml'),
  'utf8'
);
const activeFilterSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_ActiveFilterChips.cshtml'),
  'utf8'
);
const pageSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
  'utf8'
);
const selectListsSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.SelectLists.cs'),
  'utf8'
);

test('IPR drawer uses a container-aware two-column form instead of Bootstrap viewport columns', () => {
  assert.match(cssSource, /\.ipr-form-grid\s*\{[\s\S]*grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
  assert.match(cssSource, /@media\s*\(max-width:\s*720px\)[\s\S]*\.ipr-form-grid[\s\S]*grid-template-columns:\s*1fr/);
  assert.doesNotMatch(formSource, /col-md-/);
});

test('IPR mutation forms explicitly target the Index Razor Page and named handlers', () => {
  assert.match(formSource, /asp-page="\.\/Index"[\s\S]*asp-page-handler="@handler"/);
  assert.match(formSource, /asp-page-handler="Attach"/);
  assert.match(formSource, /asp-page-handler="RemoveAttachment"/);
  assert.match(formSource, /asp-page-handler="Delete"/);
  assert.doesNotMatch(formSource, /asp-all-route-data="formRoutes"/);
  assert.match(stateSource, /name="pageNumber"/);
  assert.match(stateSource, /name="PageSize"/);
  assert.match(stateSource, /name="Tab"/);
});

test('IPR project control is a searchable ID-backed combobox', () => {
  assert.match(pickerSource, /data-ipr-project-picker/);
  assert.match(pickerSource, /role="combobox"/);
  assert.match(pickerSource, /asp-for="Input.ProjectId" type="hidden"/);
  assert.match(pickerSource, /data-project-search="@project.SearchText"/);
  assert.match(source, /class IprProjectPicker/);
  assert.match(source, /ArrowDown/);
  assert.match(source, /aria-activedescendant/);
  assert.match(source, /Select a project from the search results/);
});

test('IPR granted date is exposed only for granted records and stale values are cleared', () => {
  assert.match(formSource, /data-ipr-status-select/);
  assert.match(formSource, /data-ipr-granted-date-field/);
  assert.match(formSource, /data-ipr-granted-date/);
  assert.match(source, /input\.disabled = !granted/);
  assert.match(source, /input\.required = granted/);
  assert.match(source, /input\.value = ''/);
});

test('IPR create and edit forms prevent duplicate submission', () => {
  assert.match(formSource, /data-ipr-submit-button/);
  assert.match(source, /button\.disabled = true/);
  assert.match(source, /aria-busy/);
  assert.match(source, /data-submitting-text/);
});

test('IPR attachment upload communicates PDF policy and prevents duplicate submission', () => {
  assert.match(formSource, /accept="\.pdf,application\/pdf"/);
  assert.match(source, /Uploading…/);
});

test('IPR filter drawer supports filed and grant-registration year bases', () => {
  assert.match(filterDrawerSource, /asp-for="DateBasis"/);
  assert.match(selectListsSource, /Grant \/ registration year/);
  assert.match(filterDrawerSource, /data-has-filed/);
  assert.match(filterDrawerSource, /data-has-protected/);
  assert.match(source, /syncDateYears/);
  assert.match(source, /patent grant or copyright registration date/);
});

test('IPR active scope is removable and visible across module views', () => {
  assert.match(pageSource, /_ActiveFilterChips/);
  assert.match(activeFilterSource, /BuildRemoveFilterRoute/);
  assert.match(activeFilterSource, /BuildClearFiltersRoute/);
  assert.match(pageSource, /Analytics scope:/);
  assert.match(pageSource, /ipr-followup-filters/);
});


test('IPR follow-up uses a full-width priority queue and explains issue deduplication', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /highest-priority issue/);
  assert.match(pageSource, /ipr-followup-group--wide/);
  assert.match(cssSource, /\.ipr-followup-group--wide\s*\{\s*grid-column:\s*1\s*\/\s*-1/);
  assert.doesNotMatch(pageSource, />Review<\/a>/);
});

test('IPR analytics replaces the repeated grant-rate tile with evidence coverage', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /Evidence coverage/);
  assert.match(pageSource, /recordsWithEvidence/);
});


test('IPR entry workflow provides layered patent and copyright guidance', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  const guidanceSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_IprGuidanceModal.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /Patent or copyright\?/);
  assert.match(pageSource, /_IprGuidanceModal/);
  assert.match(formSource, /data-ipr-type-select/);
  assert.match(formSource, /data-ipr-type-guidance/);
  assert.match(formSource, /Not sure\? Check in 3 questions/);
  assert.match(formSource, /A project may require both/);
  assert.match(guidanceSource, /Protects a technical invention/);
  assert.match(guidanceSource, /Protects original expression/);
  assert.match(source, /initialiseTypeGuidance/);
  assert.match(source, /Copyright registered/);
  assert.match(source, /Patent granted/);
});

test('IPR workspace shows a four-figure summary and three views without the old Projects tab', () => {
  assert.match(pageSource, /class="ipr-summary"/);
  assert.match(pageSource, /data-ipr-scope="@card\.Scope"/);
  assert.match(pageSource, /Scope = "counted"[\s\S]*Scope = "Granted"[\s\S]*Scope = "Filed"[\s\S]*Scope = "closed"/);
  assert.match(pageSource, /class="ipr-views"/);
  assert.match(pageSource, />Register</);
  assert.match(pageSource, /Follow-up/);
  assert.match(pageSource, />Analytics</);
  assert.doesNotMatch(pageSource, /Project dossiers/);
  assert.doesNotMatch(pageSource, /ipr-insight-ribbon/);
});

test('IPR register filters in the browser from server-rendered row facts', () => {
  assert.match(pageSource, /data-ipr-register/);
  assert.match(pageSource, /data-counted="@\(row\.IsCounted/);
  assert.match(pageSource, /data-closed="@\(row\.IsClosed/);
  assert.match(pageSource, /data-ipr-filter-status="closed"/);
  assert.match(pageSource, /data-ipr-group/);
  assert.match(pageSource, /register-view\.js/);
  assert.doesNotMatch(pageSource, /_RecordsPagination/);
  assert.doesNotMatch(pageSource, /data-ipr-record-inspector/);
});

test('IPR structured filter drawer stays for Follow-up and Analytics only', () => {
  assert.match(pageSource, /@if \(!isRegister\)[\s\S]*data-bs-target="#iprFilterOffcanvas"/);
  assert.match(pageSource, /@if \(!isRegister\)\s*\{\s*<partial name="_IprFilterDrawer"/);
  assert.match(filterDrawerSource, /data-ipr-filter-form/);
  assert.match(filterDrawerSource, /Apply filters/);
});

test('IPR register styles use tokens and collapse to title and status on phones', () => {
  const start = cssSource.indexOf('/* Register workspace:');
  assert.ok(start > 0);
  const section = cssSource.slice(start);
  assert.doesNotMatch(section.replace(/var\([^)]*\)/g, ''), /#[0-9a-fA-F]{3,8}/);
  assert.match(section, /\.ipr-record-panel\s*\{[\s\S]*position:\s*fixed/);
  assert.match(section, /@media \(max-width: 720px\)[\s\S]*\.ipr-table td:nth-child\(n\+2\):not\(:nth-child\(4\)\)/);
});
