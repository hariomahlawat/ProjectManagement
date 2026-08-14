const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const structureView = read('Pages/Projects/Publications/Compendium/Structure.cshtml');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const editorJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const css = read('wwwroot/css/pages/projects-publications.css');

test('phase 29.1 keeps floating output commands visually and functionally aligned with final output', () => {
  assert.match(mainJs, /setControlDisabled\(outputDockGenerate, !canDownload\)/);
  assert.match(mainJs, /setVisible\(!Boolean\(entry\?\.isIntersecting\)\)/);
  assert.match(mainJs, /if \(reviewFocusMode\) \{ setVisible\(true\); return; \}/);
  assert.match(css, /\.compendium-output-dock__actions \.btn:disabled/);
  assert.match(css, /cursor: not-allowed/);
});

test('phase 29.1 makes focus review proof-first instead of squeezing the structure rail', () => {
  assert.match(css, /\.compendium-builder-page\.is-review-focus \.compendium-builder-rail\s*\{\s*display: none;/);
  assert.match(css, /\.compendium-builder-page\.is-review-focus \.compendium-builder-layout[\s\S]*grid-template-columns: minmax\(0, 1fr\)/);
  assert.match(mainJs, /requestAnimationFrame\(\(\) => setupOutputDockObserver\(\)\)/);
});

test('phase 29.1 structure editor is a viewport application with persistent save state', () => {
  assert.match(structureView, /data-editor-save-state/);
  assert.doesNotMatch(structureView, /data-editor-done/);
  assert.match(editorJs, /fitEditorViewport/);
  assert.match(editorJs, /compendium-structure-editor-mode/);
  assert.match(css, /height: var\(--compendium-structure-editor-height/);
  assert.match(css, /html\.compendium-structure-editor-mode/);
  assert.match(css, /overflow: hidden !important/);
});

test('phase 29.1 supports filtered bulk selection and a collapsible section navigator', () => {
  assert.match(structureView, /data-editor-select-filtered/);
  assert.match(structureView, /data-editor-toggle-sections/);
  assert.match(editorJs, /orderedIds\.filter\(filterMatches\)/);
  assert.match(editorJs, /sectionsNavigatorCollapsed/);
  assert.match(css, /is-sections-collapsed \.compendium-structure-editor-sections \{ display: none; \}/);
});

test('phase 29.1 makes custom section rename affordance explicit', () => {
  assert.match(editorJs, /compendium-structure-editor-section-name/);
  assert.match(editorJs, /bi-pencil-square/);
  assert.match(css, /compendium-structure-editor-section-name > i/);
});

test('phase 29.1 removes desktop project-register horizontal overflow pressure', () => {
  assert.match(css, /@media \(min-width: 1600px\)[\s\S]*\.compendium-builder-page \.compendium-project-table[\s\S]*min-width: 0;/);
});
