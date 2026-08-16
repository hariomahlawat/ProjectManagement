const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const pagePlanner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const view = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const pageModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const programme = read('Services/Compendiums/CompendiumProgrammeInformation.cs');

test('phase 31.1 replaces fixed character budgets with a shared geometry-aware fit planner', () => {
  assert.match(pagination, /(?:UsableContentHeightPoints|PhysicalContentHeightPoints)/);
  assert.match(pagination, /(?:EstimateNarrativeHeight|measurementSession\.Measure|CompendiumDossierTextMeasurementService\.Measure)/);
  assert.match(pagination, /CandidateImageHeights/);
  assert.match(pagination, /photography was reduced to preserve readable one-page content/);
  assert.doesNotMatch(pagePlanner, /ResolveDossierNarrativeBudget/);
  assert.match(pagePlanner, /DossierFirstPageNarrativeBudget/);
  assert.match(readService, /CompendiumDossierPaginationPlanner\.Resolve/);
});

test('phase 31.1 publishes pagination diagnostics into review instead of hiding continuation decisions', () => {
  assert.match(dto, /EstimatedDossierPageCount/);
  assert.match(dto, /DossierPaginationNote/);
  assert.match(dto, /DossierPaginationReason/);
  assert.match(pageModel, /review\.EstimatedDossierPageCount/);
  assert.match(view, /data-review-pagination-badge/);
  assert.match(view, /data-review-pagination-note/);
  assert.match(view, /data-live-page-continuation/);
  assert.match(mainJs, /estimatedDossierPageCount/);
  assert.match(css, /compendium-dossier-fit-insight/);
});

test('phase 31.1 uses the exact planned image height for PDF rendering and browser proof geometry', () => {
  assert.match(dto, /DossierPrimaryImageHeightPoints/);
  assert.match(exportService, /project\.DossierPrimaryImageHeightPoints/);
  assert.match(builder, /project\.DossierPrimaryImageHeightPoints/);
  assert.match(builder, /ComposeDossierMosaic\(mosaic, images, imageHeight\)/);
  assert.match(mainJs, /dossierPrimaryImageHeightPoints/);
  assert.match(mainJs, /style\.setProperty\("aspect-ratio"/);
});

test('phase 31.1 protects technical readability using the longest bullet as well as aggregate length', () => {
  assert.match(pagination, /MeasureAtFontSize\([\s\S]*LineCount <= maximumLines/);
  assert.match(pagination, /FitsColumns\(2, 4\)/);
  assert.match(builder, /project\.DossierSpecificationColumns/);
  assert.match(mainJs, /review\.dossierSpecificationColumns/);
  assert.doesNotMatch(mainJs, /function resolveSpecificationColumns/);
});

test('phase 31.1 aggregates all IPR credentials into one programme module rather than truncating to two records', () => {
  assert.doesNotMatch(builder, /IprCredentials\.Take\(2\)/);
  assert.match(programme, /BuildIprValue/);
  assert.match(programme, /CompendiumIprVisualState\.Mixed/);
  assert.match(mainJs, /programmeModules/);
  assert.doesNotMatch(mainJs, /iprCredentials \|\| \[\]\)\.slice\(0,2\)/);
});

test('phase 31.1 uses a readable two-by-two programme grid when four optional modules are present', () => {
  assert.match(pagination, /ResolveProgrammeColumns/);
  assert.match(pagination, /_ => 2/);
  assert.match(builder, /project\.DossierProgrammeColumns/);
  assert.match(mainJs, /review\.dossierProgrammeColumns/);
});

test('phase 31.1 fixes continuation labelling and wide-register collision regression', () => {
  assert.match(builder, /CONTINUED · PART/);
  assert.doesNotMatch(builder, /narrativeLabel\.ToUpperInvariant\(\)\} · CONTINUED/);
  assert.match(css, /min-width:\s*1280px !important/);
  assert.match(css, /white-space:\s*normal !important/);
  assert.match(css, /-webkit-line-clamp:\s*2/);
});

test('phase 31.1 invalidates stale approvals when the pagination contract changes', () => {
  assert.match(fingerprint, /compendium-review-v(?:6-adaptive-pagination|7-adaptive-composition|8-production-hardening|9-programme-iconography|10-sponsoring-line-directorate|11-balanced-text-flow|12-professional-typesetting|13-physical-measurement|14-editorial-constraints|15-additional-note-final-hardening|16-particulars-style|17-editorial-rules|18-semantic-narrative)/);
});

test('phase 31.1 advances the publication build identity', () => {
  assert.match(readService, /(?:CompendiumPdf_2026-08-(?:14_adaptive-pagination-v11|15_(?:adaptive-composition-v12|production-hardening-v13|programme-iconography-v1[45]|programme-semantics-v16|programme-particulars-v17|final-composition-v18|composition-hardening-v19))|CompendiumPdf_2026-08-16_(?:physical-composition-v20|editorial-constraints-v21|final-editorial-v22|particulars-style-v23|editorial-rules-v24|semantic-narrative-v25))/);
});
