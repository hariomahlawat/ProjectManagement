const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = process.cwd();

const read = (...segments) =>
  fs.readFileSync(path.join(root, ...segments), 'utf8');

const controller = read(
  'wwwroot', 'js', 'pages', 'projects-report-controls.js');
const arpp = read(
  'Pages', 'Projects', 'Reports', 'ArppFyUpdate.cshtml');
const ffc = read(
  'Pages', 'Projects', 'Reports', 'FfcProjectsUpdate.cshtml');
const ffcScript = read(
  'wwwroot', 'js', 'pages', 'projects-reports-ffc.js');

test('configurable reports share one applied-state controller', () => {
  assert.match(arpp, /data-report-controls/);
  assert.match(ffc, /data-report-controls/);
  assert.match(arpp, /projects-report-controls\.js/);
  assert.match(ffc, /projects-report-controls\.js/);

  assert.match(controller, /\[data-report-setting\]/);
  assert.match(controller, /\[data-report-update\]/);
  assert.match(controller, /\[data-report-update-required\]/);
  assert.match(controller, /\[data-report-export\]/);
});

test('ARPP options are draft settings until Update report is submitted', () => {
  assert.doesNotMatch(arpp, /onchange="this\.form\.submit\(\)"/);
  assert.match(arpp, /data-report-setting-key="financialYearStart"/);
  assert.match(arpp, /data-report-setting-key="listingDateMode"/);
  assert.match(arpp, /data-report-setting-key="includePresentStage"/);
  assert.match(arpp, />\s*Update report\s*</);
});

test('FFC retains only report-specific country-year mechanics', () => {
  assert.match(ffcScript, /syncHiddenSelection/);
  assert.match(ffcScript, /prism:report-settings-changed/);
  assert.doesNotMatch(ffcScript, /baseExportDisabled/);
  assert.doesNotMatch(ffcScript, /setExportDisabled/);
});
