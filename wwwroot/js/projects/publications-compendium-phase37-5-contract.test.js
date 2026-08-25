const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const structureState = read('wwwroot/js/projects/compendium-structure-state.js');

test('phase 37.5 uses one heading rule for technical specifications and additional note', () => {
  const specs = builder.match(/private static void ComposeTechnicalSpecifications[\s\S]*?private static void ComposeSpecificationBullet/)?.[0] || '';
  const note = builder.match(/private static void ComposeAdditionalNote[\s\S]*?private static void ComposeProjectMetadata/)?.[0] || '';
  assert.match(specs, /Row\(header\s*=>/);
  assert.match(specs, /HARDWARE \/ TECHNICAL SPECIFICATION/);
  assert.match(specs, /RelativeItem\(\)\.PaddingTop\(4\.6f\)\.Height\(1f\)\.Background\(GoldSoft\)/);
  assert.doesNotMatch(specs, /BorderTop\(/);
  assert.match(note, /row\.AutoItem\(\)\.Text\("ADDITIONAL NOTE"\)/);
  assert.match(note, /row\.RelativeItem\(\)\.PaddingTop\(4\.4f\)\.Height\(1f\)\.Background\(GoldSoft\)/);
  assert.doesNotMatch(note, /BorderTop\(/);
});

test('phase 37.5 browser proof mirrors the same heading-rule grammar', () => {
  assert.match(css, /\.compendium-live-page__specifications\{[^}]*border-top:0/);
  assert.match(css, /\.compendium-live-page__specifications>header::after\{[^}]*height:1px[^}]*background:#e9d9a7/);
  assert.match(css, /\.compendium-live-page__additional-note\{[^}]*border-top:0/);
  assert.match(css, /\.compendium-live-page__additional-note>header::after\{[^}]*height:1px[^}]*background:#e9d9a7/);
});

test('phase 37.5 retains recovered rule height as editorial breathing reserve', () => {
  assert.match(pagination, /preservedEditorialBreathingPoints\s*=\s*7f/);
  assert.match(pagination, /noteHeadingGeometryPoints\s*=\s*23f/);
  assert.match(pagination, /headingGeometryPoints\s*=\s*15\.5f/);
  assert.match(pagination, /preservedEditorialBreathingPoints\s*=\s*6f/);
});

test('phase 37.5 opens project and edit actions in a new tab without giving the new tab opener access', () => {
  assert.match(indexView, /data-review-open-project[^>]*target="_blank"[^>]*rel="noopener noreferrer"/);
  assert.match(indexView, /data-review-edit-record[^>]*target="_blank"[^>]*rel="noopener noreferrer"/);
  assert.match(mainJs, /reviewOpen\.target\s*=\s*"_blank"/);
  assert.match(mainJs, /reviewOpen\.rel\s*=\s*"noopener noreferrer"/);
  assert.match(mainJs, /reviewEdit\.target\s*=\s*"_blank"/);
  assert.match(mainJs, /target="_blank" rel="noopener noreferrer" href="\$\{canEdit/);
});

test('phase 37.5 changes renderer identity without changing persisted schema or structure state version', () => {
  assert.match(fingerprint, /(?:compendium-review-v17-editorial-rules|compendium-review-v18-semantic-narrative|19-cover-identity)/);
  assert.match(readService, /CompendiumPdf_2026-08-16_(?:editorial-rules-v24|semantic-narrative-v25|cover-identity-v26)/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*(?:11|12|13)/);
  assert.match(structureState, /const VERSION = (?:4|5)/);
});

test('phase 37.5 leaves Panel and Minimal particulars renderers intact', () => {
  assert.match(builder, /ComposeProjectParticularsPanel/);
  assert.match(builder, /ComposeProjectParticularsMinimal/);
  assert.match(builder, /Background\(Forest50\)\.Border\(1\)/);
  const minimal = builder.match(/private static void ComposeProjectParticularsMinimal[\s\S]*?private static bool IsCompactSingleProgrammeModule/)?.[0] || '';
  assert.match(minimal, /PROJECT PARTICULARS/);
  assert.doesNotMatch(minimal, /BorderTop\(/);
});
