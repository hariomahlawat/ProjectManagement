const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const index = fs.readFileSync(path.join(root, 'Pages/Notebook/Index.cshtml'), 'utf8');
const pageModel = fs.readFileSync(path.join(root, 'Pages/Notebook/Index.cshtml.cs'), 'utf8');
const partial = fs.readFileSync(path.join(root, 'Pages/Notebook/_NotebookConferenceDigest.cshtml'), 'utf8');
const app = fs.readFileSync(path.join(root, 'wwwroot/js/notebook/notebook-app.js'), 'utf8');
const board = fs.readFileSync(path.join(root, 'wwwroot/js/notebook/notebook-board.js'), 'utf8');

test('Shared with me remains the authoritative home for the PRISM conference digest', () => {
  assert.match(index, /else if \(Model\.Notebook\.View == "shared"\)/);
  assert.match(index, /data-notebook-shared-source="prism"/);
  assert.match(index, /<h2>From PRISM<\/h2><span>1<\/span>/);
  assert.match(partial, /PRISM · Read only/);
  assert.doesNotMatch(partial, /Shared with [A-Z]/);
});

test('user may explicitly add the live PRISM note to All Notes without creating a NotebookItem', () => {
  assert.match(index, /preference\.ShowInHome|ConferenceDigestPreference!\.IsPinned/);
  assert.match(index, /systemOthers && index == otherSystemPosition/);
  assert.match(index, /systemPinned && index == pinnedSystemPosition/);
  assert.match(partial, /data-action="system-add-home"/);
  assert.match(partial, /Add to My Notebook/);
  assert.match(partial, /data-action="system-remove-home"/);
  assert.match(app, /updateSystemPreferenceWithSingleRetry\(key, \{ showInHome: true \}\)/);
  assert.match(app, /updateSystemPreferenceWithSingleRetry\(key, \{ showInHome: false \}\)/);
});

test('rail counts preserve virtual shared and home surfaces across normal Notebook count refreshes', () => {
  assert.match(index, /data-system-shared-count="@Model\.SystemSharedSurfaceCount"/);
  assert.match(index, /data-system-home-count="@Model\.SystemHomeSurfaceCount"/);
  assert.match(pageModel, /shared\.Count \+= 1;/);
  assert.match(pageModel, /if \(!preference\.ShowInHome\) return;/);
  assert.match(pageModel, /home\.Count \+= 1;/);
  assert.match(app, /numericValue \+ systemSharedCount/);
  assert.match(app, /numericValue \+ systemHomeCount/);
});

test('system digest supports personal labels and label view search while content remains read only', () => {
  assert.match(pageModel, /"labels" => !string\.IsNullOrWhiteSpace\(Tag\)/);
  assert.match(pageModel, /preference\.Labels\.Any\(label => string\.Equals\(label, Tag/);
  assert.match(partial, /data-action="label-note"/);
  assert.match(partial, /data-system-card-tags/);
  assert.match(app, /updateSystemPreferenceWithSingleRetry\(key, \{ labels \}\)/);
  assert.doesNotMatch(partial, /data-action="share-note"/);
  assert.doesNotMatch(partial, /data-action="archive-note"/);
  assert.doesNotMatch(partial, /data-action="complete-note"/);
});

test('client board accounting treats the live system card as a visible surface', () => {
  assert.match(board, /VISUAL_CARD_SELECTOR/);
  assert.match(board, /\[data-notebook-system-card\]/);
  assert.match(board, /querySelectorAll\(VISUAL_CARD_SELECTOR\)\.length/);
});
