const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const view = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const pageModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');

test('phase 32 ranks every valid one-page candidate instead of accepting first fit', () => {
  assert.match(pagination, /validCandidates/);
  assert.match(pagination, /ScoreCandidate/);
  assert.match(pagination, /ResidualSpacePoints/);
  assert.match(pagination, /ResolveIdealResidualSpace/);
  assert.match(pagination, /MaximumImageHeight/);
  assert.match(pagination, /VisualHero => (?:315f|330f)/);
  assert.match(pagination, /optimised/);
});

test('phase 32 makes narrative typography part of the shared composition contract', () => {
  assert.match(dto, /DossierNarrativeFontScale/);
  assert.match(readService, /DossierNarrativeFontScale = paginationDecision\.NarrativeFontScale/);
  assert.match(exportService, /project\.DossierNarrativeFontScale/);
  assert.match(builder, /project\.DossierNarrativeFontScale/);
  assert.match(pageModel, /review\.DossierNarrativeFontScale/);
  assert.match(mainJs, /dossierNarrativeFontScale/);
  assert.match(css, /--narrative-scale/);
});

test('phase 32 is conservative with technical specification columns and improves readable type', () => {
  assert.match(pagination, /EstimatedLines\(item, 24\) <= 2/);
  assert.match(pagination, /EstimatedLines\(item, 37\) <= 4/);
  assert.match(mainJs, /review\.dossierSpecificationColumns/);
  assert.doesNotMatch(mainJs, /function resolveSpecificationColumns/);
  assert.match(builder, /FontSize\(8\.75f\)/);
  assert.match(builder, /LineHeight\(1\.22f\)/);
  assert.match(css, /compendium-live-page__specifications p[\s\S]*font-size:\s*\.45rem/);
});

test('phase 32 distinguishes curated photography from images displayed by the selected layout', () => {
  assert.match(view, /data-review-photo-usage-summary/);
  assert.match(view, /Supporting images are retained non-destructively/);
  assert.match(mainJs, /explicitSingleImageLayout/);
  assert.match(mainJs, /supportingImageCount/);
  assert.match(mainJs, /is-retained/);
  assert.match(mainJs, /Supporting images are preserved non-destructively/);
  assert.match(css, /compendium-dossier-photo-usage/);
  assert.match(css, /btn\.is-retained/);
});

test('phase 32 adapts programme labels and uses coloured programme icon tiles', () => {
  assert.match(builder, /labelFontSize/);
  assert.match(builder, /labelLetterSpacing/);
  assert.match(builder, /ComposeProgrammeIcon/);
  assert.match(mainJs, /dataset\.programmeColumns/);
  assert.match(mainJs, /programmeIconUrl/);
  assert.match(css, /data-programme-columns="3"/);
  assert.match(css, /compendium-live-page__programme-icon/);
});

test('phase 32 gives the cover proof Fit 75 and 100 percent controls with viewport reset', () => {
  assert.match(coverView, /data-cover-proof-zoom="fit"/);
  assert.match(coverView, /data-cover-proof-zoom="75"/);
  assert.match(coverView, /data-cover-proof-zoom="100"/);
  assert.match(coverJs, /function applyProofZoom/);
  assert.match(coverJs, /function resetProofViewport/);
  assert.match(coverJs, /ResizeObserver/);
  assert.match(coverJs, /sheet\.style\.zoom/);
  assert.match(css, /compendium-cover-proof-tools/);
});

test('phase 32 advances review and PDF composition identities', () => {
  assert.match(fingerprint, /compendium-review-v(?:7-adaptive-composition|8-production-hardening|9-programme-iconography|10-sponsoring-line-directorate|11-balanced-text-flow|12-professional-typesetting|13-physical-measurement)/);
  assert.match(readService, /(?:CompendiumPdf_2026-08-15_(?:adaptive-composition-v12|production-hardening-v13|programme-iconography-v1[45]|programme-semantics-v16|programme-particulars-v17|final-composition-v18|composition-hardening-v19)|CompendiumPdf_2026-08-16_physical-composition-v20)/);
});
