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

test('phase 31.1 replaces fixed character budgets with a shared geometry-aware fit planner', () => {
  assert.match(pagination, /UsableContentHeightPoints/);
  assert.match(pagination, /EstimateNarrativeHeight/);
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
  assert.match(pagination, /longest <= (?:78|125)/);
  assert.match(pagination, /longest <= (?:175|285)/);
  assert.match(builder, /ResolveTechnicalSpecificationColumns/);
  assert.match(mainJs, /resolveSpecificationColumns/);
  assert.match(mainJs, /longest <= (?:175|285)/);
});

test('phase 31.1 aggregates all IPR credentials into one programme module rather than truncating to two records', () => {
  assert.doesNotMatch(builder, /IprCredentials\.Take\(2\)/);
  assert.match(builder, /BuildIprProgrammeValue/);
  assert.match(mainJs, /aggregateIprCredentials/);
  assert.doesNotMatch(mainJs, /iprCredentials \|\| \[\]\)\.slice\(0,2\)/);
});

test('phase 31.1 uses a readable two-by-two programme grid when four optional modules are present', () => {
  assert.match(pagination, /ResolveProgrammeColumns/);
  assert.match(pagination, /_ => 2/);
  assert.match(builder, /ResolveProgrammeColumns\(modules\.Count\)/);
  assert.match(mainJs, /resolveProgrammeColumns/);
});

test('phase 31.1 fixes continuation labelling and wide-register collision regression', () => {
  assert.match(builder, /CONTINUED · PART/);
  assert.doesNotMatch(builder, /narrativeLabel\.ToUpperInvariant\(\)\} · CONTINUED/);
  assert.match(css, /min-width:\s*1280px !important/);
  assert.match(css, /white-space:\s*normal !important/);
  assert.match(css, /-webkit-line-clamp:\s*2/);
});

test('phase 31.1 invalidates stale approvals when the pagination contract changes', () => {
  assert.match(fingerprint, /compendium-review-v(?:6-adaptive-pagination|7-adaptive-composition)/);
});

test('phase 31.1 advances the publication build identity', () => {
  assert.match(readService, /CompendiumPdf_2026-08-(?:14_adaptive-pagination-v11|15_adaptive-composition-v12)/);
});
