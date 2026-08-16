const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const coverModel = read('Pages/Projects/Publications/Compendium/Cover.cshtml.cs');
const mainView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const mainModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const preset = read('Services/Publications/CompendiumPresetService.cs');
const model = read('Models/Publications/CompendiumPreset.cs');
const migration = read('Migrations/20261208180000_AddCompendiumCoverComposer.cs');
const manifest = read('Migrations/immutable-migration-ids.txt');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const photoService = read('Services/Publications/BrochurePhotoService.cs');
const readiness = read('Services/Compendiums/CompendiumReadinessPolicy.cs');

// Phase 30 intentionally replaces the former one-hero cover contract with a controlled composer.
test('phase 30 persists first class front/back cover composition through schema v6', () => {
  assert.match(preset, /CurrentSchemaVersion\s*=\s*(?:[6-9]|10)/);
  assert.match(model, /ICollection<CompendiumPresetCoverImage>\s+CoverImages/);
  assert.match(model, /ICollection<CompendiumPresetPhotoPreference>\s+PhotoPreferences/);
  assert.match(model, /FrontCoverTemplate/);
  assert.match(model, /BackCoverTemplate/);
  assert.match(model, /FrontLogoPlacement/);
  assert.match(model, /BackLogoPlacement/);
  assert.match(migration, /Migration\("20261208180000_AddCompendiumCoverComposer"\)/);
  assert.match(migration, /CompendiumPresetCoverImages/);
  assert.match(migration, /CompendiumPresetPhotoPreferences/);
  assert.match(manifest, /20261208180000_AddCompendiumCoverComposer/);
});

test('phase 30 exposes a dedicated full screen cover editor with controlled layouts', () => {
  assert.match(mainView, /data-cover-editor/);
  assert.match(coverView, /data-compendium-cover-editor/);
  assert.match(coverView, /Institutional hero/);
  assert.match(coverView, /Full-bleed hero/);
  assert.match(coverView, /Editorial split/);
  assert.match(coverView, /Triptych/);
  assert.match(coverView, /Minimal/);
  assert.match(coverView, /Image echo/);
  assert.match(coverView, /Portfolio strip/);
  assert.match(coverView, /Typography only/);
  assert.match(coverView, /data-cover-logo-placement/);
  assert.match(css, /compendium-cover-editor-layout/);
});

test('phase 30 makes cover identity publication-controlled rather than renderer hard-coded', () => {
  assert.match(coverView, /data-cover-text="title"/);
  assert.match(coverView, /data-cover-text="subtitle"/);
  assert.match(coverView, /data-cover-text="edition"/);
  assert.match(coverView, /data-cover-text="eyebrow"/);
  assert.match(coverView, /Publication identity is inherited by default/);
  assert.doesNotMatch(builder, /Detailed Project Reference/);
  assert.doesNotMatch(builder, /Capability Edition ·/);
  assert.doesNotMatch(builder, /Simulators Compendium/);
  assert.match(builder, /design\.FrontTitle/);
  assert.match(builder, /design\.BackTitle/);
});

test('phase 30 supports single and multi image covers with independent fit and crop', () => {
  assert.match(dto, /CompendiumFrontCoverTemplate[\s\S]*EditorialSplit[\s\S]*Triptych/);
  assert.match(dto, /CompendiumImageFitMode[\s\S]*Fill[\s\S]*Fit/);
  assert.match(coverJs, /templateSlots/);
  assert.match(exportService, /CompendiumCoverTemplatePolicy\.ResolveGeometry/);
  assert.match(coverJs, /data-cover-fit/);
  assert.match(coverJs, /focalX/);
  assert.match(coverJs, /focalY/);
  assert.match(photoService, /BrochurePhotoFitMode\.Fit/);
  assert.match(photoService, /DrawImage/);
  assert.match(exportService, /CompendiumCoverTemplatePolicy\.ResolveGeometry/);
});

test('phase 30 gives project dossier imagery fit/fill parity across review and PDF', () => {
  assert.match(mainView, /data-review-image-fit/);
  assert.match(mainJs, /imageFitMode/);
  assert.match(dto, /ImageFitMode/);
  assert.match(exportService, /(?:project\.ImageFitMode|image\.FitMode) == CompendiumImageFitMode\.Fit/);
  assert.match(builder, /public CompendiumImageFitMode ImageFitMode/);
});

test('phase 30 separates technical image quality from editorial cover suitability', () => {
  assert.match(dto, /PreferredForPublication/);
  assert.match(dto, /SuitableForCoverHero/);
  assert.match(coverJs, /Cover preferred/);
  assert.match(coverJs, /Cover suitable/);
  assert.match(exportService, /SuitableForCoverHero \? (?:500|800) : (?:350|550)/);
  assert.match(mainModel, /coverHeroUsesFallback/);
  assert.match(mainModel, /coverImageLowResolution/);
  assert.match(mainModel, /coverImageUnavailable/);
});

test('phase 30 blocks stale explicit cover images and retains automatic fallback resilience', () => {
  assert.match(exportService, /slot\.ImageMode == CompendiumCoverImageMode\.Explicit/);
  assert.match(exportService, /is no longer available in this Compendium/);
  assert.match(exportService, /could not be rendered\. Choose another image/);
  assert.match(exportService, /when \(slot\.ImageMode != CompendiumCoverImageMode\.Explicit\)/);
});

test('phase 30 adds deterministic publication content hygiene warnings without rewriting source data', () => {
  assert.match(readiness, /placeholderNarrative/);
  assert.match(readiness, /duplicateNarrativeParagraph/);
  assert.match(readiness, /lorem ipsum/);
  assert.match(readiness, /ContainsDuplicateNarrativeParagraph/);
});

test('phase 30 preserves cover editor state and logo placement through save contracts', () => {
  assert.match(coverModel, /FrontLogoPlacement/);
  assert.match(coverModel, /BackLogoPlacement/);
  assert.match(coverJs, /frontLogoPlacement/);
  assert.match(coverJs, /backLogoPlacement/);
  assert.match(mainJs, /frontLogoPlacement/);
  assert.match(mainJs, /backLogoPlacement/);
});
