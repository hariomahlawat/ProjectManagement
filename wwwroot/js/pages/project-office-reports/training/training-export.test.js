const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(
  path.resolve(__dirname, '../../../project-office-reports/training/index.js'),
  'utf8'
);
const pageSource = fs.readFileSync(
  path.resolve(__dirname, '../../../../../Areas/ProjectOfficeReports/Pages/Training/Index.cshtml'),
  'utf8'
);

test('training export uses a controlled fetch and Blob download lifecycle', () => {
  assert.match(source, /fetch\(form\.action/);
  assert.match(source, /response\.blob\(\)/);
  assert.match(source, /triggerBlobDownload\(blob, fileName\)/);
  assert.match(source, /content-disposition/);
  assert.match(source, /AbortController/);
});

test('training export always restores the form state after success or failure', () => {
  assert.match(source, /finally\s*\{/);
  assert.match(source, /delete form\.dataset\.trainingExportBusy/);
  assert.match(source, /form\.removeAttribute\('aria-busy'\)/);
  assert.match(source, /setExportButtonBusy\(submitter, false\)/);
  assert.match(source, /showExportErrors/);
});

test('export modal exposes project parity and explicit trainee roster semantics', () => {
  assert.match(pageSource, /asp-for="Export\.ProjectId" type="hidden"/);
  assert.match(pageSource, /data-training-export-project-search/);
  assert.match(pageSource, /data-training-export-project-option/);
  assert.match(pageSource, /modal-dialog-scrollable modal-xl/);
  assert.match(pageSource, /Includes training events attended by this category\. Event totals remain complete\./);
  assert.match(pageSource, /asp-for="Export\.RosterScope"/);
  assert.match(pageSource, /Selected trainee category only/);
  assert.match(pageSource, /Default values match the current tracker view/);
});

test('export project picker requires an explicit matching selection', () => {
  assert.match(source, /initTrainingExportProjectPicker/);
  assert.match(source, /validateTrainingExportProjectPicker/);
  assert.match(source, /Select a project from the matching list/);
  assert.match(source, /aria-activedescendant/);
  assert.match(source, /ArrowDown/);
  assert.match(source, /ArrowUp/);
});
