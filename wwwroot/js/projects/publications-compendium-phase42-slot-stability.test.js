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
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const assignmentPolicy = read('Services/Compendiums/CompendiumCoverSlotAssignmentPolicy.cs');

const sandbox = {};
sandbox.globalThis = sandbox;
vm.runInNewContext(helperSource, sandbox, { filename: 'compendium-cover-editor-state.js' });
const contract = sandbox.PrismCompendiumCoverState;

const automaticSlot = (slotKey, projectId, photoId) => ({
  surface: 'Front',
  slotKey,
  imageMode: 'Automatic',
  projectId,
  photoId,
  focalX: .5,
  focalY: .5,
  fitMode: 'Fill',
  previewUrl: `/photos/${photoId}`
});

test('changing Supporting image 1 preserves Hero and Supporting image 2 exactly', () => {
  const hero = automaticSlot('Hero', 1, 101);
  const secondary1 = automaticSlot('Secondary1', 2, 202);
  const secondary2 = automaticSlot('Secondary2', 3, 303);

  contract.applyExplicitPhoto(secondary1, 4, {
    photoId: 404,
    previewUrl: '/photos/404',
    width: 1600,
    height: 900
  });

  assert.deepEqual(
    [hero, secondary1, secondary2].map(slot => [slot.imageMode, slot.projectId, slot.photoId]),
    [
      ['Automatic', 1, 101],
      ['Explicit', 4, 404],
      ['Automatic', 3, 303]
    ]
  );
});

test('automatic resolution remains Automatic while carrying a stable persisted identity', () => {
  const slot = automaticSlot('Secondary2', null, null);

  contract.applyAutomaticPhoto(slot, {
    projectId: 8,
    photoId: 808,
    focalX: .2,
    focalY: .75
  }, {
    photoId: 808,
    previewUrl: '/photos/808',
    width: 1800,
    height: 1200
  });

  assert.equal(slot.imageMode, 'Automatic');
  assert.equal(contract.slotReference(slot).projectId, 8);
  assert.equal(contract.slotReference(slot).photoId, 808);
  assert.equal(slot.focalX, .2);
  assert.equal(slot.focalY, .75);
});

test('resetting one automatic slot cannot clear another automatic slot', () => {
  const first = automaticSlot('Secondary1', 2, 202);
  const second = automaticSlot('Secondary2', 3, 303);

  contract.resetAutomaticAssignment(first);

  assert.equal(contract.slotReference(first), null);
  assert.equal(contract.slotReference(second).projectId, 3);
  assert.equal(contract.slotReference(second).photoId, 303);
});

test('Portfolio Quartet picker detects a photograph reserved by another slot only', () => {
  const hero = automaticSlot('Hero', 1, 101);
  const secondary1 = automaticSlot('Secondary1', 2, 202);
  const slots = [hero, secondary1];

  assert.equal(contract.isPhotoUsedByOtherSlot(slots, 'front', secondary1, 1, 101), true);
  assert.equal(contract.isPhotoUsedByOtherSlot(slots, 'front', secondary1, 2, 202), false);
});

test('editor separates transient preview invalidation from explicit bulk refresh', () => {
  const preferenceBody = editorSource.slice(
    editorSource.indexOf('function updatePreference'),
    editorSource.indexOf('async function refreshAutomaticImages'));
  const chooseBody = editorSource.slice(
    editorSource.indexOf('function choosePhoto'),
    editorSource.indexOf('function setSlotMode'));
  assert.match(editorSource, /function invalidateAutomaticPreviews\(surface = null\)/);
  assert.match(editorSource, /function resetVisibleAutomaticAssignments\(surface = state\.activeSurface\)/);
  assert.match(editorSource, /async function refreshAutomaticImages\(\)/);
  assert.match(editorSource, /resetVisibleAutomaticAssignments\(surface\)/);
  assert.doesNotMatch(chooseBody, /resetVisibleAutomaticAssignments/);
  assert.doesNotMatch(preferenceBody, /resetVisibleAutomaticAssignments/);
  assert.match(coverPage, /data-cover-refresh-automatic/);
});

test('automatic slot identities cross the save, persistence and PDF boundaries', () => {
  assert.match(editorSource, /projectId:\s*Number\(resolved\?\.projectId \|\| persisted\.projectId\) \|\| null/);
  assert.match(presetService, /image\.ImageMode != CompendiumCoverImageMode\.None \? image\.ProjectId : null/);
  assert.match(presetService, /sticky resolution snapshots/);
  assert.match(exportService, /CompendiumCoverSlotAssignmentPolicy\.Resolve/);
  assert.match(exportService, /var sticky = slot\.ProjectId is > 0 && slot\.PhotoId is > 0/);
  assert.match(exportService, /var stickyConflicts = sticky is not null/);
  assert.match(exportService, /strictDistinctSurface[\s\S]*used\.Contains/);
  assert.match(assignmentPolicy, /Pass 1: reserve every manual assignment/);
  assert.match(assignmentPolicy, /Pass 2: retain every valid sticky automatic assignment/);
  assert.match(assignmentPolicy, /Pass 3: allocate only the automatic slots/);
});

test('automatic fallback never consumes a manually reserved photograph', () => {
  const sequenceBody = editorSource.slice(
    editorSource.indexOf('function automaticCandidateSequence'),
    editorSource.indexOf('async function hydrateVisibleSlotPreviews'));
  assert.match(sequenceBody, /if \(reservedExplicitPhotos\.has\(key\)\) return/);
  assert.match(sequenceBody, /if \(!isQuartet\(surface\)\)/);
  assert.match(sequenceBody, /append\(item, true\)/);
  assert.match(assignmentPolicy, /!explicitPhotos\.Contains\(\(candidate\.ProjectId, candidate\.PhotoId\)\)/);
});
