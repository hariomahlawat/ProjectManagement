const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const mainView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const mainModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');

test('phase 30.1 makes inherited cover identity explicit and reversible', () => {
  assert.match(coverView, /Publication identity is inherited by default/);
  assert.match(coverView, /data-cover-inherited-value="title"/);
  assert.match(coverView, /data-cover-override="title"/);
  assert.match(coverView, /data-cover-reset="title"/);
  assert.match(coverJs, /overrideEditing/);
  assert.match(coverJs, /Reset to inherited|data-cover-reset/);
});

test('phase 30.1 uses authoritative fixed A4 proof coordinates instead of responsive reflow', () => {
  assert.match(css, /\.compendium-cover-proof-sheet\s*\{[\s\S]*width:\s*595px;[\s\S]*height:\s*842px;/);
  assert.match(css, /data-template="EditorialSplit"[\s\S]*top:\s*355px;[\s\S]*height:\s*471px;/);
  assert.match(css, /data-template="Triptych"[\s\S]*top:\s*395px;[\s\S]*height:\s*431px;/);
  assert.match(css, /cover-proof-identity h3[\s\S]*font-size:\s*34px/);
  assert.match(coverView, /same layout metrics/);
});

test('phase 30.1 automatic multi-image composition prefers different photos and projects', () => {
  assert.match(coverJs, /usedProjects/);
  assert.match(coverJs, /usedPhotos/);
  assert.match(coverJs, /chooseAutomaticCandidate\(surface, usedProjects, usedPhotos\)/);
  assert.match(exportService, /usedProjects = new HashSet<int>/);
  assert.match(exportService, /!usedProjects\.Contains\(item\.ProjectId\)/);
});

test('phase 30.1 makes compact cover actions template-aware', () => {
  assert.match(mainView, /data-cover-quick-hero-actions/);
  assert.match(mainView, /data-cover-editor-label/);
  assert.match(mainJs, /singleHeroTemplate/);
  assert.match(mainJs, /imageryFreeTemplate/);
  assert.match(mainJs, /Edit cover images/);
});

test('phase 30.1 uses meaningful mark names and optical sizing', () => {
  assert.match(coverView, /Formation mark/);
  assert.match(coverView, /SDD mark/);
  assert.match(css, /cover-proof-mark--formation[\s\S]*44px/);
  assert.match(css, /cover-proof-mark--sdd[\s\S]*48px/);
  assert.match(builder, /ConstantItem\(44\)\.Height\(44\)/);
  assert.match(builder, /ConstantItem\(48\)\.Height\(48\)/);
  assert.match(builder, /ConstantItem\(20\)/);
});

test('phase 30.1 elevates missing cover-suitable automatic imagery to warning', () => {
  assert.match(mainModel, /CompendiumFindingSeverity\.Warning,[\s\S]*"coverHeroUsesFallback"/);
});

test('phase 30.1 keeps crop unavailable while project imagery uses Fit', () => {
  assert.match(mainJs, /setControlDisabled\(reviewAdjustCrop, !photo \|\| config\.imageFitMode === "fit"\)/);
  assert.match(coverJs, /slot\.fitMode === 'Fit' \? 'disabled'/);
});

test('phase 30.1 advances publication build identity without a schema migration', () => {
  assert.match(readService, /(?:CompendiumPdf_(?:2026-08-14_(?:cover-fidelity-v9|adaptive-dossier-v10|adaptive-pagination-v11)|2026-08-15_(?:adaptive-composition-v12|production-hardening-v13|programme-iconography-v1[45]|programme-semantics-v16|programme-particulars-v17|final-composition-v18|composition-hardening-v19))|CompendiumPdf_2026-08-16_(?:physical-composition-v20|editorial-constraints-v21|final-editorial-v22))/);
});
