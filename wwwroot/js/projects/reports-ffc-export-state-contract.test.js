const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = process.cwd();
const view = fs.readFileSync(
  path.join(root, 'Pages', 'Projects', 'Reports', 'FfcProjectsUpdate.cshtml'),
  'utf8');
const script = fs.readFileSync(
  path.join(root, 'wwwroot', 'js', 'pages', 'projects-reports-ffc.js'),
  'utf8');
const css = fs.readFileSync(
  path.join(root, 'wwwroot', 'css', 'pages', 'projects-reports.css'),
  'utf8');

test('FFC report exposes explicit applied-state export protection', () => {
  assert.match(view, /data-ffc-export/);
  assert.match(view, /data-ffc-update-required/);
  assert.match(script, /const appliedState = Object\.freeze/);
  assert.match(script, /const hasPendingChanges = \(\) =>/);
  assert.match(script, /setExportDisabled\(link, disabled\)/);
  assert.match(script, /event\.preventDefault\(\)/);
});

test('FFC report communicates that changed options require update', () => {
  assert.match(script, /report-refresh-button--pending/);
  assert.match(script, /updateRequired\.hidden = !pending/);
  assert.match(css, /\.report-update-state/);
  assert.match(css, /\[data-ffc-export\]\[aria-disabled="true"\]/);
});

test('FFC report synchronizes country selection before every GET submit', () => {
  assert.match(script, /form\.addEventListener\("submit"/);
  assert.match(script, /syncHiddenSelection\(\)/);
  assert.doesNotMatch(script, /form\.submit\(\)/);
});
