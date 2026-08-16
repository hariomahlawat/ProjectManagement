const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const view = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const structureView = read('Pages/Projects/Publications/Compendium/Structure.cshtml');
const structurePage = read('Pages/Projects/Publications/Compendium/Structure.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const editorJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const stateJs = read('wwwroot/js/projects/compendium-structure-state.js');
const css = read('wwwroot/css/pages/projects-publications.css');

test('phase 29 makes each candidate project row a keyboard accessible selection target', () => {
  assert.match(view, /data-project-row[\s\S]*role="option"[\s\S]*tabindex="0"[\s\S]*aria-selected="false"/);
  assert.match(mainJs, /toggleProjectFromRow/);
  assert.match(mainJs, /applySelectionRange/);
  assert.match(mainJs, /event\.shiftKey/);
  assert.match(mainJs, /rowTargetIsInteractive/);
  assert.match(mainJs, /row\.setAttribute\("aria-selected"/);
  assert.match(css, /tr\[data-project-row\]\.is-selected/);
});

test('phase 29 exposes a full screen structure editor from the compact rail', () => {
  assert.match(view, /data-open-structure-editor/);
  assert.match(view, /data-structure-editor-url/);
  assert.match(mainJs, /writeStructureHandoff/);
  assert.match(mainJs, /applyStructureHandoffOnResume/);
  assert.match(structureView, /@page "\/Projects\/Publications\/Compendium\/Structure"/);
  assert.match(structureView, /PUBLICATION COMPOSER/);
  assert.match(structureView, /data-editor-canvas/);
  assert.match(structureView, /data-editor-section-nav/);
});

test('phase 29 structure editor reuses the existing preset model and bulk updates structure without a schema fork', () => {
  assert.match(structurePage, /ICompendiumPresetService/);
  assert.match(structurePage, /_presetService\.LoadAsync/);
  assert.match(structurePage, /_presetService\.UpdateAsync/);
  assert.match(structurePage, /loaded\.Configuration with/);
  assert.match(structurePage, /Projects = ordered/);
  assert.match(structurePage, /Sections = sections/);
  assert.doesNotMatch(structurePage, /AddMigration|Database\.Migrate|StructureEditorPreset/);
});

test('phase 29 structure editor supports large publication direct manipulation and bulk movement', () => {
  assert.match(structureView, /data-editor-bulk-section/);
  assert.match(structureView, /data-editor-bulk-move/);
  assert.match(structureView, /data-editor-bulk-remove/);
  assert.match(editorJs, /editorSelection/);
  assert.match(editorJs, /dragstart/);
  assert.match(editorJs, /dragover/);
  assert.match(editorJs, /beginAutoScroll/);
  assert.match(editorJs, /draggedSectionKey/);
  assert.match(editorJs, /groupingMode === "CustomSections"/);
  assert.match(editorJs, /sortMode !== "Manual"/);
});

test('phase 29 preserves current browser authoring and review state across the structure editor route', () => {
  assert.match(stateJs, /sessionStorage/);
  assert.match(stateJs, /reviewFingerprint/);
  assert.match(stateJs, /projectStates/);
  assert.match(stateJs, /persisted/);
  assert.match(mainJs, /PrismCompendiumStructure/);
  assert.match(editorJs, /incomingHandoff/);
  assert.match(editorJs, /writeHandoff/);
  assert.match(editorJs, /returnUrl/);
});

test('phase 29 protects unsaved structure work and supports explicit save and return semantics', () => {
  assert.match(structureView, /compendiumStructureLeaveModal/);
  assert.match(structureView, /Save and return/);
  assert.match(structureView, /(?:Return without saving|Keep changes and return)/);
  assert.match(editorJs, /beforeunload/);
  assert.match(editorJs, /saveStructure/);
  assert.match(editorJs, /baselineSignature/);
  assert.match(editorJs, /navigateBack/);
});
