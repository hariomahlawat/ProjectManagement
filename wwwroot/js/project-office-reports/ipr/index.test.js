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
  assert.match(stateSource, /name="page"/);
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

test('IPR filter selects auto-submit while retaining an accessible hidden submit control', () => {
  assert.match(source, /\[data-ipr-auto-submit\]/);
  assert.match(source, /form\.requestSubmit\(\)/);
});
