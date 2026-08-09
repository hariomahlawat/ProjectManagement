const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const index = fs.readFileSync(path.join(root, 'Pages/Notebook/Index.cshtml'), 'utf8');
const pageModel = fs.readFileSync(path.join(root, 'Pages/Notebook/Index.cshtml.cs'), 'utf8');
const app = fs.readFileSync(path.join(root, 'wwwroot/js/notebook/notebook-app.js'), 'utf8');
const board = fs.readFileSync(path.join(root, 'wwwroot/js/notebook/notebook-board.js'), 'utf8');

test('system-shared Conference digest is not rendered on All Notes', () => {
  assert.doesNotMatch(index, /SECTION: Live command digest/);
  assert.match(index, /else if \(Model\.Notebook\.View == "shared"\)/);
  assert.match(index, /data-notebook-shared-source="prism"/);
  assert.match(index, /var hasAnyHomeItems = Model\.Notebook\.PinnedItems\.Any\(\)\s*\|\| Model\.Notebook\.OtherItems\.Any\(\);/);
});

test('Shared with me count includes exactly one live PRISM surface when available', () => {
  assert.match(index, /data-system-shared-count="@Model\.SystemSharedSurfaceCount"/);
  assert.match(pageModel, /public int SystemSharedSurfaceCount => ConferenceDigest is \{ TotalDirectionCount: > 0 \} \? 1 : 0;/);
  assert.match(pageModel, /shared\.Count \+= 1;/);
  assert.match(app, /const systemSharedCount = Math\.max\(0, Number\.parseInt\(shell\.dataset\.systemSharedCount/);
  assert.match(app, /key\.toLowerCase\(\) === 'shared'[\s\S]*?numericValue \+ systemSharedCount/);
});

test('system digest respects Shared view search and never participates in labels or type filters', () => {
  assert.match(pageModel, /Notebook\.View, "shared"/);
  assert.match(pageModel, /!string\.IsNullOrWhiteSpace\(Filter\)/);
  assert.match(pageModel, /!string\.IsNullOrWhiteSpace\(Tag\)/);
  assert.match(pageModel, /Contains\(group\.OfficerDisplayName, term\)/);
  assert.match(pageModel, /Contains\(item\.Title, term\)/);
  assert.match(pageModel, /Contains\(item\.DirectionText, term\)/);
});

test('client empty-state reconciliation counts PRISM-shared virtual cards', () => {
  assert.match(board, /querySelectorAll\('\[data-notebook-system-shared-card\]'\)\.length/);
  assert.match(board, /notebookCount \+ systemSharedCount > 0/);
});
