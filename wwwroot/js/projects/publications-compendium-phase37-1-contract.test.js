const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const typography = read('Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs');
const measurement = read('Services/Compendiums/CompendiumDossierTextMeasurementService.cs');
const geometry = read('Services/Compendiums/CompendiumDossierImageGeometryPolicy.cs');
const flow = read('Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const markdown = read('Utilities/Reporting/MarkdownPdfRenderer.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');

test('phase 37.1 centralises physical narrative typography limits', () => {
  assert.match(typography, /MinimumScale\s*=\s*1f/);
  assert.match(typography, /MaximumScale\s*=\s*1\.10f/);
  assert.match(typography, /BodyFontSizePoints\s*=\s*10f/);
  assert.match(typography, /BodyLineHeightMultiplier\s*=\s*1\.25f/);
  assert.match(pagination, /CompendiumNarrativeTypographyPolicy\.MaximumScale/);
  assert.match(builder, /CompendiumNarrativeTypographyPolicy\.NormalizeScale/);
  assert.doesNotMatch(mainJs, /Math\.min\(1\.08/);
});

test('phase 37.1 measures narrative physically with the publication font instead of character count for candidate height', () => {
  assert.match(measurement, /SkiaSharp/);
  assert.match(measurement, /DMSans-Regular\.ttf/);
  assert.match(measurement, /paint\.MeasureText/);
  assert.match(pagination, /(?:measurementSession\.Measure|CompendiumDossierTextMeasurementService\.Measure)/);
  assert.match(flow, /(?:measurementSession\.Fits|CompendiumDossierTextMeasurementService\.Fits)/);
});

test('phase 37.1 makes Fit aspect-ratio-aware and passes source dimensions into planning', () => {
  assert.match(geometry, /CompendiumImageFitMode\.Fit/);
  assert.match(geometry, /Math\.Min/);
  assert.match(geometry, /RenderedHeightPoints/);
  assert.match(readService, /probe\?\.Width/);
  assert.match(readService, /probe\?\.Height/);
  assert.match(readService, /selectedProbe\?\.Width/);
  assert.match(pagination, /CompendiumDossierImageGeometryPolicy\.Resolve/);
  assert.match(pagination, /PhysicalContentHeightPoints\s*=\s*748f/);
  assert.match(exportService, /primaryImageHeightPoints = Math\.Max\(1f/);
});

test('phase 37.1 fixes resolved alignment parity for narrow Balanced side columns', () => {
  const sideColumnBranch = builder.slice(builder.indexOf('container.Row(row =>'), builder.indexOf('private static void ComposeDossierImage'));
  assert.match(sideColumnBranch, /narrativeAlignment:\s*flow\.SideAlignment/);
  assert.doesNotMatch(sideColumnBranch, /row\.RelativeItem\(\.88f\)[\s\S]*narrativeAlignment:\s*project\.NarrativeAlignment/);
});

test('phase 37.1 makes Markdown paragraph typography caller-controlled', () => {
  assert.match(markdown, /record MarkdownPdfTypography/);
  assert.match(markdown, /BodyFontSize/);
  assert.match(markdown, /BodyLineHeight/);
  assert.match(markdown, /typography\.BodyFontSize/);
  assert.match(builder, /new MarkdownPdfTypography/);
  assert.match(builder, /typography:\s*typography/);
});

test('phase 37.1 binds renderer change to a new review and PDF identity without a schema migration', () => {
  assert.match(fingerprint, /(?:compendium-review-v13-physical-measurement|compendium-review-v14-editorial-constraints|15-additional-note-final-hardening|16-particulars-style|17-editorial-rules|18-semantic-narrative)/);
  assert.match(readService, /CompendiumPdf_2026-08-16_(?:physical-composition-v20|editorial-constraints-v21|final-editorial-v22|particulars-style-v23|editorial-rules-v24|semantic-narrative-v25)/);
});


test('phase 37.1 browser proof preserves server paragraph rhythm without local clipping heuristics', () => {
  assert.match(mainJs, /split\(\/\\n\\s\*\\n\//);
  assert.match(mainJs, /join\("\\n\\n"\)/);
  assert.match(css, /Phase 37\.1 — server-owned physical narrative proof parity/);
  assert.match(css, /line-height:\s*1\.25/);
  assert.match(css, /max-height:\s*none\s*!important/);
});
