const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const dtos = read('Services/Compendiums/CompendiumDtos.cs');
const coverPolicy = read('Services/Compendiums/CompendiumCoverTemplatePolicy.cs');
const flowPlanner = read('Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const imageQualityPolicy = read('Services/Compendiums/CompendiumImageQualityPolicy.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const preset = read('Services/Publications/CompendiumPresetService.cs');
const model = read('Models/Publications/CompendiumPreset.cs');
const migration = read('Migrations/20261208200000_AddCompendiumBalancedTextFlow.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const brochureContracts = read('Services/Publications/BrochureContracts.cs');
const brochurePhotos = read('Services/Publications/BrochurePhotoService.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const pagePlanner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexPage = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const coverPage = read('Pages/Projects/Publications/Compendium/Cover.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const structureJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const structureState = read('wwwroot/js/projects/compendium-structure-state.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const iprSvg = read('wwwroot/images/publications/compendium-icons/ipr-granted.svg');

const compact = value => value.replace(/\s+/g, ' ');

test('phase 36 introduces the quartet and balanced text-flow contracts with schema-safe identity bumps', () => {
  assert.match(dtos, /PortfolioQuartet\s*=\s*5/);
  assert.match(dtos, /enum CompendiumBalancedTextFlowMode[\s\S]*SideColumn\s*=\s*0[\s\S]*FlowBelowImage\s*=\s*1/);
  assert.match(model, /SettingsSchemaVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*[89]/);
  assert.match(preset, /CurrentSchemaVersion\s*=\s*[89]/);
  assert.match(migration, /BalancedTextFlowMode/);
  assert.match(migration, /defaultValue:\s*"SideColumn"/);
  assert.match(fingerprint, /compendium-review-v(?:11-balanced-text-flow|12-professional-typesetting|13-physical-measurement|14-editorial-constraints)/);
  assert.match(readService, /(?:CompendiumPdf_2026-08-15_(?:final-composition-v18|composition-hardening-v19)|CompendiumPdf_2026-08-16_(?:physical-composition-v20|editorial-constraints-v21))/);
});

test('phase 36 centralises cover slots and enforces a four-photo Fill-only Portfolio Quartet', () => {
  assert.match(coverPolicy, /PortfolioQuartet/);
  assert.match(coverPolicy, /"Hero",\s*1400,\s*1600,\s*true,\s*true/);
  assert.match(coverPolicy, /"Secondary1",\s*720,\s*540,\s*true,\s*true/);
  assert.match(coverPolicy, /"Secondary2",\s*720,\s*540,\s*true,\s*true/);
  assert.match(coverPolicy, /"Secondary3",\s*720,\s*540,\s*true,\s*true/);
  assert.match(coverPolicy, /IReadOnlyList<string> Slots/);
  assert.match(coverPolicy, /IReadOnlyList<string> RequiredSlots/);
  assert.match(exportService, /Portfolio Quartet requires four distinct, resolvable photographs before final issue/);
  assert.match(exportService, /usedProjects/);
  assert.match(coverPage, /portfolioQuartetEligible/);
  assert.match(coverJs, /quartetResolved/);
  assert.match(coverJs, /strictRequiredSlots/);
  assert.match(coverJs, /isFillOnlyTemplate/);
  assert.match(coverView, /Portfolio quartet/i);
  assert.match(builder, /cover-proof|PortfolioQuartet|ComposePortfolioQuartet/);
});

test('phase 36 makes existing cover mosaics adaptive instead of rendering blank white cells', () => {
  assert.match(coverPolicy, /EditorialSplit[\s\S]*"Secondary1",\s*700,\s*1700,\s*false/);
  assert.match(coverPolicy, /Triptych[\s\S]*"Secondary1",\s*700,\s*1500,\s*false[\s\S]*"Secondary2",\s*700,\s*1500,\s*false/);
  assert.match(coverJs, /rendered\.length === 1 \? 'is-single'/);
  assert.match(coverJs, /cover-proof-triptych is-\$\{rendered\.length\}/);
  assert.match(builder, /available\.Length <= 1/);
});

test('phase 36 owns Balanced narrative segmentation on the server and sends exact segments to browser and PDF', () => {
  assert.match(flowPlanner, /paragraph-first|intact paragraph/i);
  assert.match(flowPlanner, /sentence-by-sentence|sentence boundaries/i);
  assert.doesNotMatch(flowPlanner, /Substring\(|\.\.[^.]*budget|Split\('\s'\)/);
  assert.match(readService, /CompendiumDossierNarrativeFlowPlanner\.Resolve/);
  assert.match(pagePlanner, /project\.NarrativeFlow/);
  assert.match(mainJs, /review\.narrativeFlow/);
  assert.match(mainJs, /belowImageSegment/);
  assert.match(indexView, />Text flow</);
  assert.match(indexView, /Flow below image/);
  assert.match(indexView, /Side column/);
  assert.match(indexPage, /BalancedTextFlowMode/);
  assert.match(structureJs, /balancedTextFlowMode/);
});

test('phase 36 uses server-selected technical/programme columns and quality-aware residual-space scoring', () => {
  assert.match(pagination, /EstimatedLines\(item, 24\) <= 2/);
  assert.match(pagination, /ResolveIdealResidualSpace/);
  assert.match(imageQualityPolicy, /AcceptablePrintDpi\s*=\s*150/);
  assert.match(pagination, /CompendiumImageQualityPolicy\.AcceptablePrintDpi/);
  assert.match(readService, /DossierSpecificationColumns = paginationDecision\.SpecificationColumns/);
  assert.match(readService, /DossierProgrammeColumns = paginationDecision\.ProgrammeColumns/);
  assert.match(mainJs, /review\.dossierSpecificationColumns/);
  assert.match(mainJs, /review\.dossierProgrammeColumns/);
  assert.doesNotMatch(mainJs, /function resolveSpecificationColumns/);
  assert.doesNotMatch(mainJs, /function resolveProgrammeColumns/);
});

test('phase 36 makes Compendium Fit frameless without changing Brochure defaults', () => {
  assert.match(brochureContracts, /PadFitToTarget\s*\{\s*get;\s*init;\s*\}\s*=\s*true/);
  assert.match(brochurePhotos, /request\.PadFitToTarget/);
  assert.match(exportService, /PadFitToTarget\s*=\s*false/);
  assert.match(builder, /FitArea\(/);
  assert.match(css, /\.compendium-live-page__image\.is-fit/);
  assert.match(css, /\.compendium-review-image-frame\.is-fit/);
});

test('phase 36 preserves flow and phase-35 dossier image state through Structure Editor handoff', () => {
  assert.match(structureState, /const VERSION = [23]/);
  for (const field of [
    'balancedTextFlowMode', 'dossierImageCount', 'supportingPhoto1Id', 'supportingPhoto1FocalX',
    'supportingPhoto1FocalY', 'supportingPhoto1FitMode', 'supportingPhoto2Id',
    'supportingPhoto2FocalX', 'supportingPhoto2FocalY', 'supportingPhoto2FitMode'
  ]) assert.match(structureState, new RegExp(field));
});

test('phase 36 applies the final composition micro-refinements', () => {
  assert.match(builder, /SDD SIMULATORS COMPENDIUM/);
  assert.match(builder, /#A97712/);
  assert.match(iprSvg, /#A97712/i);
  assert.ok(compact(builder).includes('PROJECT PARTICULARS'));
});
