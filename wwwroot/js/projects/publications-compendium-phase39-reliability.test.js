const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

test('phase 39 removes the output dock page scope failure', () => {
  const source = read('wwwroot', 'js', 'pages', 'projects-compendium.js');
  assert.match(source, /const builderPage = form\.closest\("\.compendium-builder-page"\)/);
  assert.match(source, /builderPage\?\.classList\.toggle\('has-output-dock', shouldShow\)/);
  assert.doesNotMatch(source, /\bpage\?\.classList\.toggle\('has-output-dock', shouldShow\)/);
});

test('phase 39 structure handoff round-trips unsaved publication cover and photo state', () => {
  const source = read('wwwroot', 'js', 'projects', 'compendium-structure-state.js');
  const data = new Map();
  const sessionStorage = {
    setItem: (key, value) => data.set(String(key), String(value)),
    getItem: key => data.get(String(key)) ?? null,
    removeItem: key => data.delete(String(key))
  };
  const sandbox = { sessionStorage, console };
  sandbox.globalThis = sandbox;
  vm.runInNewContext(source, sandbox, { filename: 'compendium-structure-state.js' });

  const handoff = {
    presetId: 17,
    rowVersion: 'rv-17',
    persisted: false,
    orderedIds: [101],
    sections: [],
    configs: { 101: {} },
    projectStates: {},
    publication: {
      title: 'Unsaved title',
      subtitle: 'Unsaved subtitle',
      edition: 'Capability Edition · 2026',
      handlingMarking: 'RESTRICTED'
    },
    coverDesign: {
      frontTemplate: 'InstitutionalHero',
      backTemplate: 'Clean',
      publicationTheme: 'DeepNavy',
      backgroundTreatment: 'Solid',
      showFrontTitle: true,
      showBackEdition: false,
      images: [{ surface: 'Front', slotKey: 'Hero', imageMode: 'Explicit', projectId: 101, photoId: 501, focalX: .31, focalY: .69, fitMode: 'Fill' }]
    },
    photoPreferences: [{ projectId: 101, photoId: 501, preferredForPublication: true, suitableForCoverHero: true }]
  };

  assert.equal(sandbox.PrismCompendiumStructure.write(handoff), true);
  const restored = sandbox.PrismCompendiumStructure.read(17);
  assert.equal(restored.persisted, false);
  assert.equal(restored.publication.title, 'Unsaved title');
  assert.equal(restored.publication.handlingMarking, 'RESTRICTED');
  assert.equal(restored.coverDesign.backTemplate, 'Clean');
  assert.equal(restored.coverDesign.showBackEdition, false);
  assert.equal(restored.coverDesign.images[0].projectId, 101);
  assert.equal(restored.photoPreferences[0].photoId, 501);
});

test('phase 39 main and structure workspaces preserve the complete handoff payload', () => {
  const main = read('wwwroot', 'js', 'pages', 'projects-compendium.js');
  const editor = read('wwwroot', 'js', 'pages', 'projects-compendium-structure-editor.js');
  assert.match(main, /handlingMarking:\s*String\(form\.elements\["Input\.HandlingMarking"\]/);
  assert.match(main, /restorePublicationSnapshot\(snapshot\.publication\)/);
  assert.match(main, /applyCoverDesignSnapshot\(snapshot\.coverDesign\)/);
  assert.match(main, /photoPreferencesState = snapshot\.photoPreferences\.map/);
  assert.match(editor, /publication:\s*incomingHandoff\?\.publication \|\| null/);
  assert.match(editor, /coverDesign:\s*incomingHandoff\?\.coverDesign \|\| null/);
  assert.match(editor, /photoPreferences:\s*incomingHandoff\?\.photoPreferences \|\| \[\]/);
});

test('phase 39 rejects stale explicit cover assignments before export and assesses automatic imagery', () => {
  const page = read('Pages', 'Projects', 'Publications', 'Compendium', 'Index.cshtml.cs');
  const main = read('wwwroot', 'js', 'pages', 'projects-compendium.js');
  assert.match(page, /"coverImageProjectNotSelected"/);
  assert.match(page, /selectedProjectIds\.Contains\(item\.ProjectId!\.Value\)/);
  assert.match(page, /"coverAutomaticImageLowResolution"/);
  assert.match(page, /foreach \(var requirement in coverSlots\)/);
  assert.match(main, /coverDesignState\.images\.forEach\(slot =>/);
  assert.match(main, /slot\.imageMode = "Automatic"/);
});

test('phase 39 composition verification follows effective cover identity', () => {
  const verifier = read('Utilities', 'Reporting', 'CompendiumPdfCompositionVerifier.cs');
  assert.match(verifier, /var frontTitle = ResolveEffectiveFrontTitle\(context\)/);
  assert.match(verifier, /if \(!design\.ShowFrontTitle\)\s*\{\s*return null;/s);
  assert.match(verifier, /var backEdition = ResolveEffectiveBackEdition\(context\)/);
  assert.match(verifier, /if \(!design\.ShowBackEdition\)\s*\{\s*return null;/s);
  assert.doesNotMatch(verifier, /VerifyTextOnPage\(canonicalPages, context\.Title, 1/);
  assert.doesNotMatch(verifier, /VerifyTextOnPage\(\s*canonicalPages,\s*context\.Edition,/s);
});

test('phase 39 generation failures return actionable diagnostics without exposing unexpected internals', () => {
  const page = read('Pages', 'Projects', 'Publications', 'Compendium', 'Index.cshtml.cs');
  const main = read('wwwroot', 'js', 'pages', 'projects-compendium.js');
  assert.match(page, /DescribeGenerationFailure\(exception, preview\)/);
  assert.match(page, /HttpContext\.TraceIdentifier/);
  assert.match(page, /"compositionVerificationFailed"/);
  assert.match(page, /"invalidPublicationState"/);
  assert.match(page, /new\(new \{ message, code, traceId \}\)/);
  assert.match(main, /error\.traceId = payload\?\.traceId \|\| null/);
  assert.match(main, /Reference: \$\{error\.traceId\}/);
  assert.match(main, /Preparing Compendium preview/);
});

test('phase 39 debounces identity-field preflight while retaining immediate structural checks', () => {
  const source = read('wwwroot', 'js', 'pages', 'projects-compendium.js');
  assert.match(source, /const schedulePreflight = \(delayMs = 220\) =>/);
  assert.match(source, /schedulePreflight\(650\)/);
  assert.match(source, /input\.addEventListener\("change", \(\) => schedulePreflight\(180\)\)/);
});
