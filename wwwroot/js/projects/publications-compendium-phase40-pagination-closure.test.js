const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = relativePath => fs.readFileSync(path.join(root, relativePath), 'utf8');

const metrics = read('Utilities/Reporting/CompendiumLayoutMetrics.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const measurement = read('Services/Compendiums/CompendiumDossierTextMeasurementService.cs');
const particulars = read('Services/Compendiums/CompendiumProjectParticularsLayoutPolicy.cs');
const pagePlanner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const verifier = read('Utilities/Reporting/CompendiumPdfCompositionVerifier.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const pageModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');

test('phase 40 derives physical body heights from the exact shared A4 chrome contract', () => {
  assert.match(metrics, /RunningHeaderHeightPoints\s*=\s*28f/);
  assert.match(metrics, /ProjectContentHeightPoints\s*=\s*[\s\S]*PageHeightPoints[\s\S]*TopMarginPoints[\s\S]*RunningHeaderHeightPoints[\s\S]*FooterHeightPoints[\s\S]*ProjectContentTopPaddingPoints/);
  assert.match(metrics, /SecondaryContentHeightPoints\s*=\s*[\s\S]*SecondaryContentTopPaddingPoints/);
  assert.match(pagination, /PhysicalContentHeightPoints\s*=\s*CompendiumLayoutMetrics\.ProjectContentHeightPoints/);
  assert.match(builder, /Height\(CompendiumLayoutMetrics\.RunningHeaderHeightPoints\)/);
  assert.match(builder, /Height\(CompendiumLayoutMetrics\.FooterHeightPoints\)/);
});

test('phase 40 measures project title and kicker with the publication semibold face', () => {
  assert.match(measurement, /bool semiBold = false/);
  assert.match(measurement, /semiBold \? SemiBoldTypeface\.Value : RegularTypeface\.Value/);
  assert.match(pagination, /ResolveProjectTitleFontSize\(projectName\)/);
  assert.match(pagination, /MeasureAtFontSize\([\s\S]*projectName[\s\S]*semiBold:\s*true/);
  assert.match(pagination, /kickerText\.ToUpperInvariant\(\)[\s\S]*ProjectKickerFontSize[\s\S]*semiBold:\s*true/);
  assert.match(readService, /planningKicker/);
  assert.match(readService, /CompendiumGroupingMode\.CustomSections/);
});

test('phase 40 replaces index row-unit heuristics with measured DM Sans physical heights', () => {
  assert.match(pagePlanner, /BuildIndexSeeds\(context\.Title, context\.Categories\)/);
  assert.match(pagePlanner, /MeasureIndexHeadingHeight/);
  assert.match(pagePlanner, /MeasureIndexGroupHeadingHeight/);
  assert.match(pagePlanner, /MeasureIndexProjectRowHeight/);
  assert.match(pagePlanner, /measurement\.MeasureAtFontSize/);
  assert.doesNotMatch(pagePlanner, /EstimateIndexProjectRowUnits/);
  assert.doesNotMatch(pagePlanner, /unitsUsed/);
});

test('phase 40 makes index project and continuation bodies atomic QuestPDF fragments', () => {
  const showEntireCalls = builder.match(/ShowEntire\(\)/g) || [];
  assert.ok(showEntireCalls.length >= 3, 'expected atomic index, project and continuation page bodies');
  assert.match(builder, /SecondaryContentTopPaddingPoints[\s\S]*ShowEntire\(\)\.Column\(content/);
  assert.match(builder, /ProjectContentTopPaddingPoints[\s\S]*ShowEntire\(\)\.Section\(ProjectAnchorId/);
  assert.match(builder, /SecondaryContentTopPaddingPoints[\s\S]*ShowEntire\(\)\.Column\(column/);
});

test('phase 40 computes continuation capacity from the actual title and page chrome', () => {
  assert.match(pagination, /ResolveContinuationBodyHeightPoints/);
  assert.match(pagination, /ContinuationTitleFontSize/);
  assert.match(pagination, /SecondaryContentHeightPoints\s*-\s*fixedGeometry/);
  assert.match(pagePlanner, /continuationBodyHeight\s*=\s*CompendiumDossierPaginationPlanner\.ResolveContinuationBodyHeightPoints/);
  assert.match(pagePlanner, /SplitTechnicalSpecificationsForPhysicalPages\([\s\S]*continuationBodyHeight/);
  assert.match(pagePlanner, /SplitForPhysicalPages\([\s\S]*continuationBodyHeight/);
});

test('phase 40 keeps planner and renderer aligned for Fit image intrinsic height', () => {
  assert.match(exportService, /rendered\?\.SourceWidth \?\? image\.SourceWidth/);
  assert.match(exportService, /rendered\?\.SourceHeight \?\? image\.SourceHeight/);
  assert.match(builder, /SourceWidth = null/);
  assert.match(builder, /CompendiumDossierImageGeometryPolicy\.Resolve/);
  assert.match(builder, /Height\(geometry\.RenderedHeightPoints\)/);
  assert.match(pagination, /CompendiumDossierImageGeometryPolicy\.Resolve/);
});

test('phase 40 measures Project Particulars with the same semibold and border geometry as QuestPDF', () => {
  assert.match(particulars, /CompendiumLayoutMetrics\.ContentWidthPoints/);
  assert.match(particulars, /semiBold:\s*true/);
  assert.match(particulars, /borderPoints\s*=\s*1f/);
  assert.match(particulars, /PROJECT PARTICULARS/);
  assert.match(builder, /Text\("PROJECT PARTICULARS"\)[\s\S]*LineHeight\(1\.08f\)/);
});

test('phase 40 localises page-count drift before rejection and returns a conflict response', () => {
  assert.match(verifier, /canonicalPages\s*=\s*pages\.Select/);
  assert.match(verifier, /FindFirstObservableDrift/);
  assert.match(verifier, /Index pages are checked first/);
  assert.match(verifier, /First observable drift:/);
  assert.match(verifier, /ActualPhysicalPage/);
  assert.match(pageModel, /exception is CompendiumPdfCompositionException[\s\S]*StatusCodes\.Status409Conflict/);
});
