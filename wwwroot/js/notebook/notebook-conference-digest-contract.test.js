const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const partialPath = path.join(root, 'Pages/Notebook/_NotebookConferenceDigest.cshtml');
const modalPath = path.join(root, 'Pages/Notebook/_NotebookConferenceDigestModal.cshtml');
const indexPath = path.join(root, 'Pages/Notebook/Index.cshtml');
const cssPath = path.join(root, 'wwwroot/css/notebook.css');

const read = (file) => fs.readFileSync(file, 'utf8');

test('conference digest uses normal Notebook card geometry and one-line summary metadata', () => {
  const partial = read(partialPath);
  const index = read(indexPath);
  const css = read(cssPath);

  assert.match(partial, /class="notebook-card notebook-card-color-@colorKey notebook-conference-shared-card/);
  assert.match(partial, /@digest\.TotalDirectionCount latest direction/);
  assert.match(partial, /· @digest\.OfficerCount Project Officer/);
  assert.match(index, /<h2>From PRISM<\/h2>/);
  assert.match(css, /\.notebook-system-shared-board/);
  assert.doesNotMatch(css, /\.notebook-conference-digest-card\s*\{[\s\S]*?780px/);
});

test('compact preview remains shallow, PO-wise and has an unambiguous View all action', () => {
  const partial = read(partialPath);

  assert.match(partial, /const int previewOfficerLimit = 2;/);
  assert.match(partial, /const int previewDirectionLimit = 3;/);
  assert.match(partial, /previewGroups = digest\.OfficerGroups\.Take\(previewOfficerLimit\)/);
  assert.match(partial, /notebook-conference-shared-card__officer/);
  assert.match(partial, /View all @digest\.TotalDirectionCount/);
  assert.doesNotMatch(partial, /more directions/);
});

test('conference digest does not repeat project idea or task type labels', () => {
  const partial = read(partialPath);
  const modal = read(modalPath);
  const combined = partial + modal;

  assert.doesNotMatch(combined, /@item\.KindLabel/);
  assert.doesNotMatch(combined, />PROJECT</);
  assert.doesNotMatch(combined, />IDEA</);
  assert.doesNotMatch(combined, />TASK</);
});

test('expanded digest uses precise provenance and latest terminology', () => {
  const modal = read(modalPath);

  assert.match(modal, /PRISM · Conference Review/);
  assert.match(modal, /@digest\.TotalDirectionCount latest direction/);
  assert.doesNotMatch(modal, /current direction/);
  assert.match(modal, /Source: Conference Review/);
});

test('expanded digest uses a two-column title-date heading and only PO group dividers', () => {
  const css = read(cssPath);
  const officerRule = css.match(/\.notebook-conference-digest-officer\s*\{([^}]*)\}/)?.[1] ?? '';
  const itemRule = css.match(/\.notebook-conference-digest-item\s*\{([^}]*)\}/)?.[1] ?? '';
  assert.match(officerRule, /border-bottom:\s*1px solid/);
  assert.doesNotMatch(itemRule, /border-bottom/);
  assert.match(css, /\.notebook-conference-digest-item__heading\s*\{[\s\S]*?display:\s*grid;[\s\S]*?grid-template-columns:\s*minmax\(0,\s*1fr\)\s+max-content;/);
});
