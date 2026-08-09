const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const partialPath = path.join(__dirname, '../../../Pages/Notebook/_NotebookConferenceDigest.cshtml');
const cssPath = path.join(__dirname, '../../css/notebook.css');

function readPartial() {
  return fs.readFileSync(partialPath, 'utf8');
}

function readCss() {
  return fs.readFileSync(cssPath, 'utf8');
}

test('conference digest preview is deliberately shallow and PO-wise', () => {
  const partial = readPartial();

  assert.match(partial, /const int previewOfficerLimit = 2;/);
  assert.match(partial, /const int previewDirectionLimit = 3;/);
  assert.match(partial, /previewGroups = Model\.OfficerGroups\.Take\(previewOfficerLimit\)/);
  assert.match(partial, /notebook-conference-digest-preview-group__more/);
  assert.match(partial, /View all @Model\.TotalDirectionCount direction/);
});

test('conference digest does not repeat project idea or task type labels', () => {
  const partial = readPartial();

  assert.doesNotMatch(partial, /@item\.KindLabel/);
  assert.doesNotMatch(partial, /notebook-conference-digest-item__kind/);
  assert.doesNotMatch(partial, /notebook-conference-digest-preview-item__meta/);
  assert.match(partial, /notebook-conference-digest-item__heading/);
});

test('conference digest card is wide and presents two PO previews side by side on desktop', () => {
  const css = readCss();

  assert.match(css, /\.notebook-conference-digest-card\s*\{[\s\S]*?width:\s*min\(100%,\s*780px\);/);
  assert.match(css, /\.notebook-conference-digest-card__groups\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\);/);
  assert.match(css, /@media \(max-width: 760px\)[\s\S]*?grid-template-columns:\s*minmax\(0,\s*1fr\);/);
});

test('expanded digest uses a concise register row instead of type-specific cards', () => {
  const css = readCss();

  assert.match(css, /\.notebook-conference-digest-item__heading/);
  assert.doesNotMatch(css, /\.notebook-conference-digest-item\.is-idea/);
  assert.doesNotMatch(css, /\.notebook-conference-digest-item\.is-task/);
  assert.doesNotMatch(css, /\.notebook-conference-digest-item__kind/);
});

test('expanded digest uses latest terminology and omits redundant per-officer direction counts', () => {
  const partial = readPartial();

  assert.match(partial, /@Model\.TotalDirectionCount latest direction/);
  assert.doesNotMatch(partial, /@Model\.TotalDirectionCount current direction/);
  assert.doesNotMatch(partial, /<span>@group\.Directions\.Count current direction/);
  assert.match(partial, /Source: Conference Review/);
});

test('expanded digest separates PO groups without drawing rules between every direction', () => {
  const css = readCss();
  const officerRule = css.match(/\.notebook-conference-digest-officer\s*\{([^}]*)\}/)?.[1] ?? '';
  const itemRule = css.match(/\.notebook-conference-digest-item\s*\{([^}]*)\}/)?.[1] ?? '';

  assert.match(officerRule, /border-bottom:\s*1px solid/);
  assert.doesNotMatch(itemRule, /border-bottom/);
  assert.match(css, /\.notebook-conference-digest-officer__items\s*\{[\s\S]*?gap:\s*\.48rem;/);
});

