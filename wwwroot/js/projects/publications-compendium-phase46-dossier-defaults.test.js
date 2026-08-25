const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const exists = file => fs.existsSync(path.join(root, file));

const model = read('Models/Publications/CompendiumPreset.cs');
const contracts = read('Services/Publications/CompendiumPresetContracts.cs');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const indexPage = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const structure = read('Pages/Projects/Publications/Compendium/Structure.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const structureState = read('wwwroot/js/projects/compendium-structure-state.js');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const db = read('Data/ApplicationDbContext.cs');
const buildIdentity = read('Utilities/Reporting/CompendiumBuildIdentity.cs');

const compact = value => value.replace(/\s+/g, ' ');

test('phase 46 persists compendium dossier defaults and nullable project overrides', () => {
  assert.match(model, /DefaultDossierLayout/);
  assert.match(model, /DefaultBalancedTextFlowMode/);
  assert.match(model, /DefaultImageFitMode/);
  assert.match(model, /DossierLayoutOverride/);
  assert.match(model, /BalancedTextFlowModeOverride/);
  assert.match(model, /ImageFitModeOverride/);
  assert.match(contracts, /DefaultDossierLayout/);
  assert.match(contracts, /DefaultBalancedTextFlowMode/);
  assert.match(contracts, /DefaultImageFitMode/);
  assert.match(contracts, /CompendiumDossierLayout\?\s+DossierLayoutOverride/);
  assert.match(contracts, /CompendiumBalancedTextFlowMode\?\s+BalancedTextFlowModeOverride/);
  assert.match(contracts, /CompendiumImageFitMode\?\s+ImageFitModeOverride/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*13/);
});

test('phase 46 resolves effective dossier presentation centrally on the server', () => {
  assert.ok(exists('Services/Compendiums/CompendiumDossierPresentationPolicy.cs'));
  const policy = read('Services/Compendiums/CompendiumDossierPresentationPolicy.cs');
  assert.match(policy, /CompendiumDossierPresentationDefaults/);
  assert.match(policy, /DossierLayoutOverride\s*\?\?/);
  assert.match(policy, /BalancedTextFlowModeOverride\s*\?\?/);
  assert.match(policy, /ImageFitModeOverride\s*\?\?/);
  assert.match(policy, /NarrativeAlignmentOverride\s*\?\?/);
  assert.match(dto, /CompendiumDossierPresentationDefaults/);
});

test('phase 46 exposes dossier presentation defaults at compendium level', () => {
  assert.match(indexPage, /Dossier presentation defaults/i);
  assert.match(indexPage, /data-default-dossier-layout-value="Automatic"/);
  assert.match(indexPage, /data-default-text-flow-value="FlowBelowImage"/);
  assert.match(indexPage, /data-default-image-fit-value="Fill"/);
  assert.match(indexModel, /DefaultDossierLayout/);
  assert.match(indexModel, /DefaultBalancedTextFlowMode/);
  assert.match(indexModel, /DefaultImageFitMode/);
});

test('phase 46 supports publication-default project overrides and one reset action', () => {
  assert.match(indexPage, /data-review-layout="default"/);
  assert.match(indexPage, /data-review-text-flow-mode="default"/);
  assert.match(indexPage, /data-review-image-fit="default"/);
  assert.match(indexPage, /data-review-reset-dossier-overrides/);
  assert.match(mainJs, /dossierLayoutOverride/);
  assert.match(mainJs, /balancedTextFlowModeOverride/);
  assert.match(mainJs, /imageFitModeOverride/);
  assert.match(mainJs, /resetDossierOverrides/);
});


test('phase 46 Focus Review makes inherited values explicit and only offers reset for real overrides', () => {
  assert.match(mainJs, /Publication default · \${editorialState\.narrativeAlignment/);
  assert.match(mainJs, /reviewResetDossierOverrides\.hidden/);
  assert.match(mainJs, /config\.imageSelectionMode === "explicit"/);
});

test('phase 46 selectively invalidates only inheriting projects when defaults change', () => {
  assert.match(mainJs, /changeDefaultDossierLayout/);
  assert.match(mainJs, /!ensureConfig\(id\)\.dossierLayoutOverride/);
  assert.match(mainJs, /changeDefaultTextFlow/);
  assert.match(mainJs, /!ensureConfig\(id\)\.balancedTextFlowModeOverride/);
  assert.match(mainJs, /changeDefaultImageFit/);
  assert.match(mainJs, /!ensureConfig\(id\)\.imageFitModeOverride/);
});

test('phase 46 structure handoff preserves nullable overrides and publication defaults end to end', () => {
  assert.match(structure, /StructureStateVersion\s*=\s*5/);
  assert.match(structure, /DossierLayoutOverride/);
  assert.match(structure, /BalancedTextFlowModeOverride/);
  assert.match(structure, /ImageFitModeOverride/);
  assert.match(mainJs, /dossierLayoutOverride:\s*config\.dossierLayoutOverride\s*\|\|\s*null/);
  assert.match(mainJs, /balancedTextFlowModeOverride:\s*config\.balancedTextFlowModeOverride\s*\|\|\s*null/);
  assert.match(mainJs, /imageFitModeOverride:\s*config\.imageFitModeOverride\s*\|\|\s*null/);
  assert.match(structureState, /dossierLayoutOverride:/);
  assert.match(structureState, /balancedTextFlowModeOverride:/);
  assert.match(structureState, /imageFitModeOverride:/);
  assert.match(structureState, /defaultDossierLayout:/);
  assert.match(structureState, /defaultBalancedTextFlowMode:/);
  assert.match(structureState, /defaultImageFitMode:/);
});

test('phase 46 adds schema-13 migration and updates EF defaults', () => {
  assert.ok(exists('Migrations/20261216190000_AddCompendiumDossierPresentationDefaults.cs'));
  const migration = read('Migrations/20261216190000_AddCompendiumDossierPresentationDefaults.cs');
  assert.match(migration, /DefaultDossierLayout/);
  assert.match(migration, /DefaultBalancedTextFlowMode/);
  assert.match(migration, /DefaultImageFitMode/);
  assert.match(migration, /nullable:\s*true/);
  assert.match(db, /SettingsSchemaVersion\)\.HasDefaultValue\(13\)/);
  assert.match(compact(buildIdentity), /Phase\s*=\s*"(?:46|46\.2)"/);
});

test('phase 46 presentation policy keeps nullable alignment target-typed for C# compilation', () => {
  const policy = read('Services/Compendiums/CompendiumDossierPresentationPolicy.cs');
  assert.match(policy, /CompendiumNarrativeAlignment\?\s+alignmentOverride\s*=/);
  assert.match(policy, /alignmentOverride\s*\?\?\s*defaultAlignment/);
});

test('phase 46 export resolves requested layout to a non-null publication-or-project value', () => {
  const exportService = read('Services/Compendiums/CompendiumExportService.cs');
  assert.match(exportService, /CompendiumDossierPresentationPolicy\.Normalize\([\s\S]*?request\.DossierPresentationDefaults\s+with/);
  assert.match(exportService, /DossierLayoutRequested\s*=\s*project\.DossierLayoutOverride\s*\?\?\s*exportPresentationDefaults\.DossierLayout/);
  assert.doesNotMatch(exportService, /DossierLayoutRequested\s*=\s*project\.DossierLayoutOverride\s*,/);
});
