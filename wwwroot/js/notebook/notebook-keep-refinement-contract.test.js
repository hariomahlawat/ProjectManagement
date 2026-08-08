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
