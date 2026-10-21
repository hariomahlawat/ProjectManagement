const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const indexMarkup = fs.readFileSync(path.join(root, 'Pages/Notebook/Index.cshtml'), 'utf8');
const actionMarkup = fs.readFileSync(path.join(root, 'Pages/Notebook/_NotebookActions.cshtml'), 'utf8');
const editorMarkup = fs.readFileSync(path.join(root, 'Pages/Notebook/_NotebookEditorTemplate.cshtml'), 'utf8');
const composerSource = fs.readFileSync(path.join(root, 'wwwroot/js/notebook/notebook-composer.js'), 'utf8');
const boardSource = fs.readFileSync(path.join(root, 'wwwroot/js/notebook/notebook-board.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'wwwroot/css/notebook.css'), 'utf8');

test('quick capture carries colour and labels into the create payload', () => {
  assert.match(indexMarkup, /notebook-composer__tools[^>]*role="toolbar"/);
  assert.match(composerSource, /colorKey:\s*colourPicker\?\.getValue\(\)\s*\|\|\s*null/);
  assert.match(composerSource, /labels:\s*labelPicker\?\.getValue\(\)\s*\|\|\s*\[\]/);
  assert.match(composerSource, /resizeTextareaToContent\(body,\s*\{\s*minimumHeight:\s*56,\s*maximumHeight:\s*200\s*\}\)/);
});

test('card actions keep labels one click away instead of duplicating the command in More', () => {
  const matches = actionMarkup.match(/data-action="label-note"/g) || [];
  assert.equal(matches.length, 1);
  assert.match(actionMarkup, /class="notebook-action-icon"\s+data-action="label-note"/);
});

test('all notebook grid boards opt into deterministic masonry', () => {
  assert.doesNotMatch(indexMarkup, /data-layout-policy="masonry-threshold"/);
  assert.match(indexMarkup, /data-layout-policy="masonry-always"/);
  assert.match(boardSource, /policy === 'masonry-always'/);
});

test('existing note editor has a unified bottom action bar', () => {
  assert.match(editorMarkup, /class="notebook-editor-bottom-bar"/);
  assert.match(editorMarkup, /data-notebook-editor-toolbar role="toolbar"/);
  assert.match(css, /\.notebook-modal:not\(\.is-create-mode\) \.notebook-editor-bottom-bar > footer/);
});

test('list view renders compact rows and touch screens keep reminder actions visible', () => {
  assert.match(css, /SECTION: List view — compact rows/);
  // Row layout targets note cards only, so the PRISM digest card keeps its own layout.
  assert.match(css, /\.notebook-shell\[data-board-view="list"\] \.notebook-board > \[data-note-id\] \.notebook-checklist-preview > :not\(strong\) \{\s*display: none;/);
  assert.doesNotMatch(css, /\.notebook-shell\[data-board-view="list"\] \.notebook-card-body \{/);
  // Long labels shrink instead of overflowing; actions reserve their width in the row.
  assert.match(css, /\[data-note-id\] \.notebook-card-tags \{\s*flex: 0 1 auto;[\s\S]*?min-width: 0;/);
  assert.match(css, /\[data-note-id\] > \.notebook-card-actions \{\s*position: static;\s*flex: 0 0 auto;/);
  assert.doesNotMatch(css, /grid-template-columns: minmax\(0, 720px\)/, 'list view is no longer a single 720px column of cards');
  // Without this, cards with a reminder had no actions (including Done) on touch screens.
  assert.match(css, /@media \(hover: none\) \{[\s\S]*?\.notebook-card\.has-due-action:not\(:hover\):not\(:focus-within\) \.notebook-card-actions \{[\s\S]*?visibility: visible;/);
});
