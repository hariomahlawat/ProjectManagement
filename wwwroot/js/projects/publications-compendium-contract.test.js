const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const view = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const page = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const js = read('wwwroot/js/pages/projects-compendium.js');
const service = read('Services/Compendiums/CompendiumReadService.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const preset = read('Services/Publications/CompendiumPresetService.cs');
const db = read('Data/ApplicationDbContext.cs');
const landing = read('Pages/Projects/Publications/Index.cshtml');

test('phase 22 exposes the complete normal publication portfolio instead of proliferation-only eligibility', () => {
  assert.match(service, /LifecycleStatus\s*==\s*ProjectLifecycleStatus\.Active/);
  assert.match(service, /LifecycleStatus\s*==\s*ProjectLifecycleStatus\.Completed/);
  assert.match(service, /GetCandidateProjectsAsync/);
  assert.match(view, /All proliferation status/);
  assert.match(view, /Available for proliferation/);
});

test('phase 22 provides Brochure-grade selection, selected-only filtering and order controls', () => {
  assert.match(view, /data-project-checkbox/);
  assert.match(view, /data-filter-selected/);
  assert.match(view, /data-select-matching/);
  assert.match(view, /data-order-list/);
  assert.match(view, /drag to reorder/);
  assert.match(js, /orderedIds/);
  assert.match(js, /dragstart/);
  assert.match(js, /Select first 100 matching/);
});

test('phase 22 export is selection-aware and derives category groups only from selected projects', () => {
  assert.match(exportService, /SelectedProjectIds/);
  assert.match(exportService, /GetPublicationAsync/);
  assert.match(service, /GroupInPublicationOrder/);
  assert.match(service, /requestedIds\.Contains\(project\.Id\)/);
});

test('phase 22 supports shared reusable Compendium configurations without duplicating project facts', () => {
  assert.match(preset, /ICompendiumPresetService/);
  assert.match(preset, /Only HoD or Comdt may maintain shared Compendium configurations/);
  assert.match(preset, /Publications\.CompendiumPresetCreated/);
  assert.match(preset, /Publications\.CompendiumPresetUpdated/);
  assert.match(preset, /Publications\.CompendiumPresetDeleted/);
  assert.match(preset, /ProjectNameSnapshot/);
  assert.match(db, /DbSet<CompendiumPreset>/);
  assert.match(db, /UX_CompendiumPresetProjects_Preset_SortOrder/);
  assert.doesNotMatch(read('Models/Publications/CompendiumPreset.cs'), /ProliferationCost|ArmService|ProjectDescription/);
});

test('phase 22 uses a PRISM unsaved-changes modal and never browser confirm for preset loading', () => {
  assert.match(view, /id="compendiumDiscardModal"/);
  assert.match(view, /Discard and load/);
  assert.match(js, /discardModal/);
  assert.doesNotMatch(js, /\bconfirm\s*\(/);
});

test('phase 22 establishes four-stage Compendium authoring and persistent final-output rail', () => {
  assert.match(view, />1<\/span>[\s\S]*Publication settings/);
  assert.match(view, />2<\/span>[\s\S]*Select projects/);
  assert.match(view, />3<\/span>[\s\S]*Review publication/);
  assert.match(view, />4<\/span>[\s\S]*Publication readiness/);
  assert.match(view, /compendium-builder-rail/);
  assert.match(view, /Download Compendium PDF/);
});

test('phase 22 landing page copy is user-facing rather than implementation-facing', () => {
  assert.match(landing, /Create professional publications from PRISM project records/);
  assert.match(landing, /Detailed project publication/i);
  assert.match(landing, /Create compendium/i);
  assert.doesNotMatch(landing, /recommended default/i);
  assert.doesNotMatch(landing, /readiness checks retained/i);
  assert.doesNotMatch(landing, /second factual project record/i);
});

test('phase 22 preserves legacy proliferation catalogue compatibility separately from authored Compendiums', () => {
  assert.match(service, /GetProliferationCompendiumAsync/);
  assert.match(service, /Compatibility path for \/Projects\/Compendium/);
  assert.match(service, /ExcludedNotAvailableCount/);
  assert.match(service, /MissingAvailabilityStatusCount/);
});

test('phase 22 never enables final output from stale or pending preflight state', () => {
  assert.match(js, /preflightRevision/);
  assert.match(js, /lastPreflight/);
  assert.match(js, /invalidatePreflight/);
  assert.match(js, /const canGenerate = isCurrent && Boolean\(preflight\.canGenerate\)/);
  assert.match(js, /Checking publication/);
});

test('phase 22 replaces saved project order transactionally to respect unique sort-order constraints', () => {
  assert.match(preset, /BeginTransactionAsync/);
  assert.match(preset, /RemoveRange\(preset\.Projects\)/);
  assert.match(preset, /await _db\.SaveChangesAsync\(cancellationToken\)/);
  assert.match(preset, /AddRange\(prepared\.Projects\)/);
  assert.match(preset, /CommitAsync/);
  assert.match(preset, /RollbackAsync/);
});

test('phase 22 adds an additive relational saved-Compendium migration and registration', () => {
  const migration = read('Migrations/20261208130000_AddSharedCompendiumPresets.cs');
  const registration = read('Services/Publications/PublicationServiceCollectionExtensions.cs');
  assert.match(migration, /name: "CompendiumPresets"/);
  assert.match(migration, /name: "CompendiumPresetProjects"/);
  assert.match(migration, /FK_CompendiumPresetProjects_Projects_ProjectId/);
  assert.match(registration, /AddScoped<ICompendiumPresetService, CompendiumPresetService>/);
});

test('phase 22 supports ongoing projects safely in the existing PDF renderer', () => {
  const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
  assert.match(builder, /LifecycleDisplay/);
  assert.match(builder, /"Status"/);
  assert.match(builder, /Status \/ year/);
  assert.match(builder, /Edition/);
});
