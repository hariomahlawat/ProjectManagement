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


test('IPR register uses a row-based workbench with a persistent inspector', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /ipr-record-workbench/);
  assert.match(pageSource, /data-ipr-record-inspector/);
  assert.match(pageSource, /id="iprRecordData"/);
  assert.match(source, /initialiseRecordInspector/);
  assert.match(source, /sessionStorage\.setItem/);
  assert.match(cssSource, /\.ipr-register-table thead\s*\{[\s\S]*position:\s*sticky/);
});

test('IPR project view is grouped into expandable project dossiers', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /data-ipr-project-group/);
  assert.match(pageSource, /Project dossiers/);
  assert.match(source, /initialiseProjectGroups/);
  assert.match(source, /Expand awaiting/);
});

test('IPR module provides an operational follow-up view and compact insight ribbon', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /ipr-insight-ribbon/);
  assert.match(pageSource, /IPR follow-up/);
  assert.match(pageSource, /ipr-followup-group/);
});


test('IPR records workbench exposes clear selection, keyboard access and a resizable inspector', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /data-ipr-inspector-resize/);
  assert.match(pageSource, /aria-selected=/);
  assert.match(source, /initialiseInspectorResize/);
  assert.match(source, /aria-valuenow/);
  assert.match(source, /event\.key !== 'Enter'/);
  assert.match(cssSource, /--ipr-inspector-width:\s*400px/);
  assert.match(cssSource, /\.ipr-row-edit-link\s*\{[\s\S]*opacity:\s*0/);
});

test('IPR project dossiers provide explicit status filtering, operational sorting and targeted expansion', () => {
  const pageSource = fs.readFileSync(
    path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml'),
    'utf8'
  );
  assert.match(pageSource, /All IPR statuses/);
  assert.match(pageSource, /data-ipr-project-group-sort/);
  assert.match(pageSource, /Attention first/);
  assert.match(pageSource, /Expand awaiting/);
  assert.match(source, /sortGroups/);
  assert.match(source, /group\.dataset\.awaiting === 'true'/);
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
