const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const partialPath = path.join(__dirname, '../../../Pages/Notebook/_NotebookConferenceDigest.cshtml');
const indexPath = path.join(__dirname, '../../../Pages/Notebook/Index.cshtml');
const cssPath = path.join(__dirname, '../../css/notebook.css');

const read = (file) => fs.readFileSync(file, 'utf8');

test('conference digest is a PRISM-shared normal-width notebook card', () => {
  const partial = read(partialPath);
  const index = read(indexPath);
  const css = read(cssPath);

  assert.match(partial, /class="notebook-card notebook-card-color-white notebook-conference-shared-card"/);
  assert.match(partial, /PRISM · Read only/);
  assert.match(index, /Model\.Notebook\.View == "shared"/);
  assert.match(index, /<h2>From PRISM<\/h2><span>1<\/span>/);
  assert.match(index, /<h2>From people<\/h2>/);
  assert.match(css, /\.notebook-shell\[data-board-view="grid"\] \.notebook-board\.notebook-system-shared-board\s*\{[\s\S]*?280px\) !important;/);
  assert.doesNotMatch(css, /\.notebook-conference-digest-card\s*\{[\s\S]*?780px/);
});

test('conference digest preview remains deliberately shallow and PO-wise', () => {
  const partial = read(partialPath);

  assert.match(partial, /const int previewOfficerLimit = 2;/);
  assert.match(partial, /const int previewDirectionLimit = 3;/);
  assert.match(partial, /previewGroups = Model\.OfficerGroups\.Take\(previewOfficerLimit\)/);
  assert.match(partial, /notebook-conference-shared-card__officer/);
  assert.match(partial, /hiddenDirectionCount/);
});

test('conference digest does not repeat project idea or task type labels', () => {
  const partial = read(partialPath);

  assert.doesNotMatch(partial, /@item\.KindLabel/);
  assert.doesNotMatch(partial, /notebook-conference-digest-item__kind/);
  assert.doesNotMatch(partial, />PROJECT</);
  assert.doesNotMatch(partial, />IDEA</);
  assert.doesNotMatch(partial, />TASK</);
});

test('expanded digest uses latest terminology and concise PO register hierarchy', () => {
  const partial = read(partialPath);

  assert.match(partial, /@Model\.TotalDirectionCount latest direction/);
  assert.doesNotMatch(partial, /@Model\.TotalDirectionCount current direction/);
  assert.doesNotMatch(partial, /<span>@group\.Directions\.Count current direction/);
  assert.match(partial, /Command · PRISM/);
  assert.match(partial, /Source: Conference Review/);
});

test('expanded digest separates PO groups without rules between every direction', () => {
  const css = read(cssPath);
  const officerRule = css.match(/\.notebook-conference-digest-officer\s*\{([^}]*)\}/)?.[1] ?? '';
  const itemRule = css.match(/\.notebook-conference-digest-item\s*\{([^}]*)\}/)?.[1] ?? '';

  assert.match(officerRule, /border-bottom:\s*1px solid/);
  assert.doesNotMatch(itemRule, /border-bottom/);
  assert.match(css, /\.notebook-conference-digest-officer__items\s*\{[\s\S]*?gap:\s*\.48rem;/);
});
