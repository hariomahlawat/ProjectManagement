const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../..');
const read = relativePath => fs.readFileSync(path.join(root, relativePath), 'utf8');
const helperSource = read('wwwroot/js/projects/compendium-cover-editor-state.js');
const editorSource = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const coverPage = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');

const sandbox = {};
sandbox.globalThis = sandbox;
vm.runInNewContext(helperSource, sandbox, { filename: 'compendium-cover-editor-state.js' });
const contract = sandbox.PrismCompendiumCoverState;

test('replacing a slot photograph resets the focal point owned by the old source', () => {
  const slot = {
    imageMode: 'Explicit',
    projectId: 10,
    photoId: 100,
    focalX: .12,
    focalY: .91,
    fitMode: 'Fill'
  };

  const changed = contract.applyExplicitPhoto(slot, 20, {
    photoId: 200,
    previewUrl: '/photos/200',
    width: 1600,
    height: 900
  });

  assert.equal(changed, true);
  assert.equal(slot.projectId, 20);
  assert.equal(slot.photoId, 200);
  assert.equal(slot.focalX, .5);
  assert.equal(slot.focalY, .5);
  assert.equal(slot.fitMode, 'Fill');
});

test('reselecting the same photograph preserves its intentional crop', () => {
  const slot = { projectId: 20, photoId: 200, focalX: .23, focalY: .68 };

  const changed = contract.applyExplicitPhoto(slot, 20, {
    photoId: 200,
    thumbnailUrl: '/thumbs/200',
    width: 1200,
    height: 900
  });

  assert.equal(changed, false);
  assert.equal(slot.focalX, .23);
  assert.equal(slot.focalY, .68);
  assert.equal(slot.previewUrl, '/thumbs/200');
});

test('stale or project-mismatched photo-list responses cannot update the picker', () => {
  assert.equal(contract.shouldCommitPhotoRequest(8, 8, 20, '20'), true);
  assert.equal(contract.shouldCommitPhotoRequest(7, 8, 20, '20'), false);
  assert.equal(contract.shouldCommitPhotoRequest(8, 8, 20, '21'), false);
  assert.equal(contract.shouldCommitPhotoRequest(8, 8, 20, ''), false);
});

test('cover editor uses abortable request sequencing and slot-local invalidation', () => {
  assert.match(editorSource, /photoPickerRequestVersion/);
  assert.match(editorSource, /photoPickerAbortController\?\.abort\(\)/);
  assert.match(editorSource, /signal:\s*signal/);
  assert.match(editorSource, /shouldCommitPhotoRequest/);
  assert.match(editorSource, /applyExplicitPhoto\(slot, projectId, photo\)/);
  assert.match(editorSource, /bumpHydrationVersion\(surface\)/);
  assert.doesNotMatch(editorSource, /function choosePhoto[\s\S]*?resetVisibleAutomaticAssignments[\s\S]*?function setSlotMode/);
  assert.match(editorSource, /function invalidateAutomaticPreviews\(surface = null\)/);
  assert.match(editorSource, /function resetAutomaticSlot\(slot\)/);
});

test('server automatic allocation uses the same independent front/back surface contract', () => {
  assert.match(exportService, /HashSet<\(CompendiumCoverSurface Surface, int ProjectId, int PhotoId\)>/);
  assert.match(exportService, /used\.Contains\(\(required\.Surface, candidate\.ProjectId, candidate\.PhotoId\)\)/);
  assert.match(exportService, /usedProjects\.Contains\(\(required\.Surface, candidate\.ProjectId\)\)/);
  assert.match(exportService, /required\.Surface == CompendiumCoverSurface\.Front/);
});

test('server-canonical save state is rehydrated before the editor reports completion', () => {
  assert.match(editorSource, /state\.design = normaliseDesign\(result\.coverDesign\)/);
  assert.match(editorSource, /await hydrateVisibleSlotPreviews\('front'\)/);
  assert.match(editorSource, /await hydrateVisibleSlotPreviews\('back'\)/);
  assert.match(editorSource, /updateInspector\(\);\s*setDirty\(\);\s*return true/);
});

test('the pure state contract loads before the DOM editor', () => {
  const stateIndex = coverPage.indexOf('compendium-cover-editor-state.js');
  const editorIndex = coverPage.indexOf('projects-compendium-cover-editor.js');
  assert.ok(stateIndex >= 0);
  assert.ok(editorIndex > stateIndex);
});
