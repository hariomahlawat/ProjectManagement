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
const activeFilterSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_ActiveFilterChips.cshtml'),
  'utf8'
);
const pageSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
  'utf8'
);
const registerViewSource = fs.readFileSync(path.resolve(__dirname, 'register-view.js'), 'utf8');
const iprDir = path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr');

test('IPR editor uses a container-aware two-column form instead of Bootstrap viewport columns', () => {
  assert.match(cssSource, /\.ipr-form-grid\s*\{[\s\S]*grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
  assert.match(cssSource, /@media\s*\(max-width:\s*720px\)[\s\S]*\.ipr-form-grid[\s\S]*grid-template-columns:\s*1fr/);
  assert.doesNotMatch(formSource, /col-md-/);
});

test('IPR mutation forms explicitly target the Index Razor Page and named handlers', () => {
  assert.match(formSource, /asp-page="\.\/Index"[\s\S]*asp-page-handler="@handler"/);
  assert.doesNotMatch(formSource, /asp-page-handler="Attach"/);
  assert.match(formSource, /asp-page-handler="RemoveAttachment"/);
  assert.match(formSource, /asp-page-handler="Delete"/);
  assert.doesNotMatch(formSource, /asp-all-route-data="formRoutes"/);
  // Paging was retired with the all-records register; old pageNumber/pageSize links are ignored.
  assert.doesNotMatch(stateSource, /pageNumber|PageSize/);
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

test('IPR evidence PDF is saved with the record in one submit', () => {
  assert.match(formSource, /enctype="multipart\/form-data"/);
  assert.match(formSource, /asp-for="Input\.Evidence"[\s\S]*accept="\.pdf,application\/pdf"/);
  assert.match(formSource, /form="iprRemoveAttachment@\(attachment\.Id\)"/);
  assert.match(formSource, /id="iprRemoveAttachment@\(attachment\.Id\)"/);
  assert.match(source, /Saving and uploading…/);
});

test('IPR editor opens in the record panel and confirms before discarding changes', () => {
  assert.match(pageSource, /id="iprRecordEditor"/);
  assert.match(pageSource, /ipr-record-panel--editor/);
  assert.match(pageSource, /data-ipr-close-url="@editorCloseUrl"/);
  assert.doesNotMatch(pageSource, /iprRecordOffcanvas/);
  assert.match(source, /initialiseEditor/);
  assert.match(source, /Discard your changes to this record\?/);
  assert.match(formSource, /data-ipr-editor-close/);
});
test('IPR active scope is removable and visible across module views', () => {
  assert.match(pageSource, /_ActiveFilterChips/);
  assert.match(activeFilterSource, /BuildRemoveFilterRoute/);
  assert.match(activeFilterSource, /BuildClearFiltersRoute/);
  assert.match(pageSource, /"Scope: "/);
  assert.match(pageSource, /ipr-actions__filters/);
});


test('IPR Action list lists each filing once with a direct fix per issue', () => {
  assert.match(pageSource, />Action list</);
  assert.match(pageSource, /Each filing appears once, under its most urgent issue/);
  assert.match(pageSource, /"overdue" => "Record outcome"/);
  assert.match(pageSource, /"linkage" => "Assign project"/);
  assert.match(pageSource, /"evidence" => "iprEvidenceHeading"/);
  assert.match(pageSource, /asp-fragment="@actionFragment"/);
  assert.match(pageSource, /AttentionIssueCounts/);
  assert.match(source, /hashId/);
});
test('IPR Insights draws its charts in HTML and every chart opens the filtered register', () => {
  assert.match(pageSource, /id="iprInsightsHeading">Insights</);
  assert.match(pageSource, /IDictionary<string, string\?> RegisterLink\(object overrides\)/);
  assert.match(pageSource, /RegisterLink\(new \{ year = cohort\.Year/);
  assert.match(pageSource, /RegisterLink\(new \{ year = row\.Year, dateBasis = "Protected" \}\)/);
  assert.match(pageSource, /RegisterLink\(new \{ pendingAge = band\.Band\.ToString\(\) \}\)/);
  assert.match(pageSource, /RegisterLink\(new \{ evidence = "Missing" \}\)/);
  assert.doesNotMatch(pageSource, /chart\.umd\.js/);
  assert.doesNotMatch(pageSource, /<canvas/);
  assert.match(cssSource, /\.ipr-bars__stack/);
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
  assert.match(pageSource, /Action list/);
  assert.match(pageSource, />Insights</);
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

test('IPR Filters drawer is retired; the view tabs carry the Register filters', () => {
  assert.equal(fs.existsSync(path.join(iprDir, '_IprFilterDrawer.cshtml')), false);
  assert.equal(fs.existsSync(path.join(iprDir, '_ProjectFilterPicker.cshtml')), false);
  assert.doesNotMatch(pageSource, /iprFilterOffcanvas|_IprFilterDrawer/);
  assert.doesNotMatch(source, /initialiseFilterDrawer/);
  assert.match(pageSource, /asp-all-route-data="registerTabRoute" data-ipr-view="records"/);
  assert.match(pageSource, /asp-all-route-data="actionListTabRoute" data-ipr-view="followup"/);
  assert.match(pageSource, /asp-all-route-data="insightsTabRoute" data-ipr-view="analytics"/);
  // Search text and Group by stay with the Register.
  assert.match(pageSource, /actionListTabRoute = Model\.GetRouteValuesForLinks\(new \{ tab = "followup",.*query = \(string\?\)null, group = \(string\?\)null/);
  assert.match(registerViewSource, /document\.querySelectorAll\('\[data-ipr-view\]'\)/);
  assert.match(registerViewSource, /if \(view === 'records' \|\| key !== 'query'\) viewParams\.append\(key, value\);/);
  assert.match(activeFilterSource, /Change in Register/);
});

test('IPR register styles use tokens and collapse to title and status on phones', () => {
  const start = cssSource.indexOf('/* Register workspace:');
  assert.ok(start > 0);
  const section = cssSource.slice(start);
  assert.doesNotMatch(section.replace(/var\([^)]*\)/g, ''), /#[0-9a-fA-F]{3,8}/);
  assert.match(section, /\.ipr-record-panel\s*\{[\s\S]*position:\s*fixed/);
  assert.match(section, /@media \(max-width: 720px\)[\s\S]*\.ipr-table td:nth-child\(n\+2\):not\(:nth-child\(4\)\)/);
});

test('IPR Insights links never open more or fewer filings than the chart counts', () => {
  // Insights ignores search text, so drill-down links must not carry it into the register.
  assert.match(pageSource, /IDictionary<string, string\?> RegisterLink\(object overrides\)[\s\S]*\["query"\] = null/);
  // A year bar opens the register only when it does not replace a year already in the scope.
  assert.match(pageSource, /bool CanOpenYear\(int year, IprDateBasis basis\)\s*=> scopeCounts && \(!Model\.Year\.HasValue \|\| \(Model\.Year == year && Model\.DateBasis == basis\)\)/);
  assert.match(pageSource, /@if \(CanOpenYear\(cohort\.Year, IprDateBasis\.Filed\)\)/);
  assert.match(pageSource, /@if \(CanOpenYear\(row\.Year, IprDateBasis\.Filed\)\)/);
  assert.match(pageSource, /@if \(CanOpenYear\(row\.Year, IprDateBasis\.Protected\)\)/);
  // Status, type, evidence and age links open only values already inside the scope.
  assert.match(pageSource, /@if \(CanOpenStatus\(IprStatus\.Granted\)\)/);
  assert.match(pageSource, /@if \(CanOpenStatus\(IprStatus\.Filed\)\)/);
  assert.match(pageSource, /@if \(CanOpenMissingEvidence\(\)\)/);
  assert.match(pageSource, /if \(CanOpenAge\(band\.Band\)\)/);
  assert.match(pageSource, /var typeInScope = CanOpenType\(row\.Type\);/);
  // Closed statuses never travel into a drill-down for counted filings.
  assert.match(pageSource, /if \(!values\.ContainsKey\("statuses"\) && Model\.Statuses\.Any\(IprStatusPolicy\.IsClosed\)\)/);
  assert.match(pageSource, /var scopeCounts = Model\.Statuses\.Count == 0 \|\| scopedCountedStatuses\.Length > 0;/);
  assert.match(pageSource, /bool CanOpenMissingEvidence\(\) => scopeCounts &&/);
  assert.doesNotMatch(pageSource, /<td><a asp-page="\.\/Index" asp-all-route-data="@RegisterLink\(new \{ types/);
});

test('IPR form posts keep the pending-age and Group by scope', () => {
  assert.match(stateSource, /@if \(Model\.PendingAge != IprPendingAge\.Any\)\s*\{\s*<input type="hidden" name="PendingAge" value="@Model\.PendingAge" \/>/);
  assert.match(stateSource, /<input type="hidden" name="Group" value="@Model\.Group" \/>/);
});
