const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.resolve(__dirname, 'index.js'), 'utf8');
const formSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../Areas/ProjectOfficeReports/Pages/Ipr/_RecordFormPartial.cshtml'),
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

test('IPR granted date is exposed only for granted records and stale values are cleared', () => {
  assert.match(formSource, /data-ipr-status-select/);
  assert.match(formSource, /data-ipr-granted-date-field/);
  assert.match(formSource, /data-ipr-granted-date/);
  assert.match(source, /grantedInput\.disabled = !granted/);
  assert.match(source, /grantedInput\.required = granted/);
  assert.match(source, /grantedInput\.value = ''/);
});

test('IPR attachment upload communicates PDF policy and prevents duplicate submission', () => {
  assert.match(formSource, /accept="\.pdf,application\/pdf"/);
  assert.match(source, /button\.disabled = true/);
  assert.match(source, /aria-busy/);
  assert.match(source, /Uploading/);
});

test('IPR filter selects auto-submit while retaining an accessible hidden submit control', () => {
  assert.match(source, /\[data-ipr-auto-submit\]/);
  assert.match(source, /form\.requestSubmit\(\)/);
});
