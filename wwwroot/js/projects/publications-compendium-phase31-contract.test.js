const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (file) => fs.readFileSync(path.join(root, file), 'utf8');

const dto = read('Services/Compendiums/CompendiumDtos.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const planner = read('Services/Compendiums/CompendiumDossierLayoutPlanner.cs');
const pagePlanner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const view = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const projectTabs = read('Pages/Projects/_ProjectContentTabs.cshtml');
const projectService = read('Services/Projects/ProjectContentService.cs');
const projectModel = read('Models/ProjectTechnicalSpecificationItem.cs');
const db = read('Data/ApplicationDbContext.cs');
const migration = read('Migrations/20261208190000_AddCompendiumAdaptiveDossiers.cs');
const snapshot = read('Migrations/ApplicationDbContextModelSnapshot.cs');
const preset = read('Services/Publications/CompendiumPresetService.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const programme = read('Services/Compendiums/CompendiumProgrammeInformation.cs');

// Phase 31 replaces the ERP-style facts grid with four adaptive dossier families.
test('phase 31 exposes four controlled dossier families with Automatic as the default authoring mode', () => {
  assert.match(dto, /enum CompendiumDossierLayout[\s\S]*Automatic[\s\S]*VisualHero[\s\S]*Balanced[\s\S]*MultiImageEditorial[\s\S]*Technical/);
  assert.match(view, /data-review-layout="Automatic"/);
  assert.match(view, /data-review-layout="VisualHero"/);
  assert.match(view, /data-review-layout="Balanced"/);
  assert.match(view, /data-review-layout="MultiImageEditorial"/);
  assert.match(view, /data-review-layout="Technical"/);
  assert.match(planner, /Technical content requires additional readable space/);
  assert.match(planner, /Multiple publication photographs are available/);
});

test('phase 31 records authoritative hardware technical specification bullets on the project master record', () => {
  assert.match(projectModel, /class ProjectTechnicalSpecificationItem/);
  assert.match(projectModel, /DisplayOrder/);
  assert.match(projectTabs, /Hardware \/ technical specification/);
  assert.match(projectTabs, /TechnicalSpecificationMaximumCount/);
  assert.match(projectService, /SaveTechnicalSpecificationsAsync/);
  assert.match(projectService, /Remove duplicate hardware \/ technical specification bullets/);
  assert.match(db, /ProjectTechnicalSpecificationItems/);
});

test('phase 31 persists dossier presentation choices through schema v7 and a forward migration', () => {
  assert.match(preset, /CurrentSchemaVersion\s*=\s*(?:[7-9]|10)/);
  assert.match(migration, /Migration\("20261208190000_AddCompendiumAdaptiveDossiers"\)/);
  assert.match(migration, /DossierLayout/);
  assert.match(migration, /DossierImageCount/);
  assert.match(migration, /SupportingPhoto1Id/);
  assert.match(migration, /SupportingPhoto2Id/);
  assert.match(migration, /ProjectTechnicalSpecificationItems/);
  assert.match(snapshot, /HasDefaultValue\((?:8|9|10)\)/);
  assert.match(snapshot, /ProjectTechnicalSpecificationItem/);
});

test('phase 31 resolves programme information from live PRISM data and omits absent optional facts', () => {
  assert.match(readService, /SponsoringLineDirectorate/);
  assert.match(readService, /IprStatus\.Filed \|\| item\.Status == IprStatus\.Granted/);
  assert.match(readService, /ProjectTotStatus\.InProgress \|\| item\.Status == ProjectTotStatus\.Completed/);
  assert.match(builder, /ComposeProgrammeInformation/);
  assert.match(builder, /CompendiumProgrammeInformation\.Resolve/);
  assert.match(builder, /ProgrammeModules/);
  assert.match(programme, /Proliferation cost/);
  assert.match(programme, /Technology transfer/);
  assert.match(builder, /if \(modules\.Count == 0\) return/);
});

test('phase 31 supports one to three distinct dossier images with per-slot fit crop and explicit locking', () => {
  assert.match(dto, /CompendiumDossierImageRole[\s\S]*Primary[\s\S]*Supporting1[\s\S]*Supporting2/);
  assert.match(view, /data-photo-role="Primary"/);
  assert.match(view, /data-photo-role="Supporting1"/);
  assert.match(view, /data-photo-role="Supporting2"/);
  assert.match(view, /data-photo-role-fit="Fill"/);
  assert.match(view, /data-photo-role-fit="Fit"/);
  assert.match(mainJs, /already used on this page/);
  assert.match(readService, /used\.Contains/);
  assert.match(exportService, /ResolveDossierSlotGeometry/);
});

test('phase 31 renders adaptive technical specifications rather than fixed empty boxes', () => {
  assert.match(builder, /HARDWARE \/ TECHNICAL SPECIFICATION/);
  assert.match(builder, /project\.DossierSpecificationColumns/);
  assert.match(read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs'), /MeasureAtFontSize\([\s\S]*LineCount <= maximumLines/);
  assert.match(css, /--spec-columns/);
  assert.match(mainJs, /technicalSpecifications/);
});

test('phase 31 allows photography to yield space before text and provides deterministic technical continuation pages', () => {
  assert.match(builder, /DossierPrimaryImageHeightPoints/);
  assert.doesNotMatch(pagePlanner, /ResolveDossierNarrativeBudget/);
  assert.match(pagePlanner, /SplitTechnicalSpecifications/);
  assert.match(builder, /TECHNICAL REFERENCE/);
  assert.match(pagePlanner, /IsTechnicalContinuation/);
});

test('phase 31 live proof mirrors the modular programme specification and multi-image geometry', () => {
  assert.match(view, /data-live-page-dossier-main/);
  assert.match(view, /data-live-page-support-image="1"/);
  assert.match(view, /data-live-page-support-image="2"/);
  assert.match(view, /data-live-page-specifications/);
  assert.match(css, /layout-balanced/);
  assert.match(css, /layout-visualhero/);
  assert.match(css, /layout-multiimageeditorial/);
  assert.match(css, /layout-technical/);
  assert.match(css, /grid-row:\s*1 \/ 3/);
});

test('phase 31 review fingerprint invalidates approval when adaptive dossier facts or presentation changes', () => {
  assert.match(fingerprint, /compendium-review-v(?:6-adaptive-pagination|7-adaptive-composition|8-production-hardening|9-programme-iconography|10-sponsoring-line-directorate|11-balanced-text-flow|12-professional-typesetting|13-physical-measurement|14-editorial-constraints|15-additional-note-final-hardening)/);
  assert.match(fingerprint, /DossierLayout/);
  assert.match(fingerprint, /DossierImages/);
  assert.match(fingerprint, /TechnicalSpecifications/);
  assert.match(fingerprint, /IprCredentials/);
  assert.match(fingerprint, /TechnologyTransfer/);
});

test('phase 31 advances the publication build identity', () => {
  assert.match(readService, /(?:CompendiumPdf_2026-08-(?:14_adaptive-pagination-v11|15_(?:adaptive-composition-v12|production-hardening-v13|programme-iconography-v1[45]|programme-semantics-v16|programme-particulars-v17|final-composition-v18|composition-hardening-v19))|CompendiumPdf_2026-08-16_(?:physical-composition-v20|editorial-constraints-v21|final-editorial-v22))/);
});
