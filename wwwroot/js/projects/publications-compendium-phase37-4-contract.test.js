const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const model = read('Models/Publications/CompendiumPreset.cs');
const db = read('Data/ApplicationDbContext.cs');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const presetContracts = read('Services/Publications/CompendiumPresetContracts.cs');
const migration = read('Migrations/20261216123000_AddCompendiumProjectParticularsStyle.cs');
const snapshot = read('Migrations/ApplicationDbContextModelSnapshot.cs');
const manifest = read('Migrations/immutable-migration-ids.txt');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const policy = read('Services/Compendiums/CompendiumProjectParticularsLayoutPolicy.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const structurePage = read('Pages/Projects/Publications/Compendium/Structure.cshtml.cs');
const structureState = read('wwwroot/js/projects/compendium-structure-state.js');
const structureJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');

test('phase 37.4 persists one publication-level particulars style through schema v11', () => {
  assert.match(dto, /enum CompendiumProjectParticularsStyle[\s\S]*Panel[\s\S]*Minimal/);
  assert.match(model, /ProjectParticularsStyle[\s\S]*=\s*"Panel"/);
  assert.match(model, /SettingsSchemaVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*11/);
  assert.match(presetContracts, /CompendiumProjectParticularsStyle\s+ProjectParticularsStyle/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*11/);
  assert.match(presetService, /SettingsSchemaVersion\s*<\s*11[\s\S]*Panel/);
  assert.match(db, /ProjectParticularsStyle\)\.HasMaxLength\(24\)\.HasDefaultValue\("Panel"\)/);
  assert.match(db, /SettingsSchemaVersion\)\.HasDefaultValue\(11\)/);
  assert.match(migration, /Migration\("20261216123000_AddCompendiumProjectParticularsStyle"\)/);
  assert.match(migration, /AddColumn<string>[\s\S]*ProjectParticularsStyle[\s\S]*defaultValue:\s*"Panel"/);
  assert.match(snapshot, /Property<string>\("ProjectParticularsStyle"\)[\s\S]*HasDefaultValue\("Panel"\)/);
  assert.match(manifest, /20261216123000_AddCompendiumProjectParticularsStyle/);
});

test('phase 37.4 exposes exactly Panel and Minimal as one Compendium-level presentation choice', () => {
  assert.match(indexView, /data-project-particulars-style/);
  assert.match(indexView, /data-particulars-style-value="Panel"/);
  assert.match(indexView, /data-particulars-style-value="Minimal"/);
  assert.match(indexView, /Choose one consistent treatment for every project dossier/);
  assert.doesNotMatch(indexView, /data-review-particulars-style/);
  assert.match(indexModel, /ProjectParticularsStyle\s*\{\s*get;\s*set;/);
  assert.match(indexModel, /ParseProjectParticularsStyle/);
});

test('phase 37.4 uses one authoritative particulars layout policy for both skins and pagination', () => {
  assert.match(policy, /ResolvePanelColumns/);
  assert.match(policy, /ResolveMinimalColumns/);
  assert.match(policy, /MeasureAtFontSize/);
  assert.match(policy, /EstimatePanelHeight/);
  assert.match(policy, /EstimateMinimalHeight/);
  assert.match(pagination, /CompendiumProjectParticularsLayoutPolicy\.Resolve/);
  assert.match(pagination, /programmeHeight\s*=\s*particularsLayout\.HeightPoints/);
});

test('phase 37.4 renders current Panel unchanged and adds a frameless Minimal PDF treatment', () => {
  assert.match(builder, /ComposeProjectParticularsPanel/);
  assert.match(builder, /Background\(Forest50\)\.Border\(1\)/);
  assert.match(builder, /ComposeProjectParticularsMinimal/);
  assert.match(builder, /PROJECT PARTICULARS/);
  assert.match(builder, /Background\(GoldSoft\)/);
  assert.doesNotMatch(builder.match(/private static void ComposeProjectParticularsMinimal[\s\S]*?private static bool IsCompactSingleProgrammeModule/)?.[0] || '', /\.Border\(1\)/);
  assert.match(css, /compendium-live-page__programme\.is-minimal/);
  assert.match(css, /background:\s*transparent/);
  assert.match(css, /border:\s*0/);
});

test('phase 37.4 invalidates all reviews when the publication particulars style changes', () => {
  assert.match(mainJs, /changeProjectParticularsStyle/);
  assert.match(mainJs, /orderedIds\.forEach\(invalidateProjectReview\)/);
  assert.match(mainJs, /projectParticularsStyle:\s*editorialState\.projectParticularsStyle/);
  assert.match(fingerprint, /compendium-review-v16-particulars-style/);
  assert.match(fingerprint, /ProjectParticularsStyle/);
  assert.match(readService, /CompendiumPdf_2026-08-16_particulars-style-v23/);
});

test('phase 37.4 preserves the global style through Structure Editor handoff and save', () => {
  assert.match(structureState, /projectParticularsStyle/);
  assert.match(structureJs, /projectParticularsStyle/);
  assert.match(structurePage, /projectParticularsStyle\s*=\s*loaded\.Configuration\.ProjectParticularsStyle/);
  assert.match(structurePage, /ProjectParticularsStyle\s*=\s*ParseProjectParticularsStyle/);
  assert.match(structureState, /const VERSION = 4/);
});
