const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = relativePath => fs.readFileSync(path.join(root, relativePath), 'utf8');

const metrics = read('Utilities/Reporting/CompendiumLayoutMetrics.cs');
const generationException = read('Utilities/Reporting/CompendiumPdfGenerationException.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const pageModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const fonts = read('Utilities/Reporting/PublicationFontRegistry.cs');

test('phase 41 reserves one real shaping line between Skia planning and QuestPDF composition', () => {
  assert.match(metrics, /MaximumProjectBodyLineHeightPoints/);
  assert.match(metrics, /PhysicalPaginationNativeShapingTolerancePoints/);
  assert.match(metrics, /PhysicalPaginationReservePoints\s*=\s*MaximumProjectBodyLineHeightPoints\s*\+\s*PhysicalPaginationNativeShapingTolerancePoints/);
  assert.match(metrics, /native-shaping tolerance|native shaping tolerance/i);
});

test('phase 41 makes DM Sans a hard Compendium pagination contract rather than silently using Lato', () => {
  assert.match(fonts, /FallbackFamilyName\s*=\s*"Lato"/);
  assert.match(builder, /!fontStatus\.DmSansAvailable/);
  assert.match(builder, /fontStatus\.PrimaryFamily[\s\S]*PublicationFontService\.PrimaryFamilyName/);
  assert.match(builder, /CompendiumPdfGenerationStage\.FontInitialization/);
  assert.match(builder, /Volatile\.Write\(ref s_primaryFontFamily, PublicationFontService\.PrimaryFamilyName\)/);
});

test('phase 41 classifies QuestPDF layout compose and drawing failures explicitly', () => {
  assert.match(builder, /using QuestPDF\.Drawing\.Exceptions/);
  assert.match(builder, /catch \(DocumentLayoutException exception\)/);
  assert.match(builder, /catch \(DocumentComposeException exception\)/);
  assert.match(builder, /catch \(DocumentDrawingException exception\)/);
  assert.match(generationException, /enum CompendiumPdfGenerationStage/);
  assert.match(generationException, /PdfLayout/);
  assert.match(generationException, /PdfDrawing/);
  assert.match(generationException, /PdfVerification/);
});

test('phase 41 automatically probes planned pages after a QuestPDF failure to localise production-only data', () => {
  assert.match(builder, /TryLocateFailingPlannedPage/);
  assert.match(builder, /foreach \(var planned in plan\.Pages\)/);
  assert.match(builder, /ComposePlannedPage\(container, planned, state, enableIndexLinks: false\)/);
  assert.match(builder, /MatchesFailureStage/);
  assert.match(builder, /Planned physical page/);
  assert.match(builder, /ProjectName=\{ProjectName\}/);
  assert.match(builder, /enableNavigationLinks/);
});

test('phase 41 turns explicit cover render faults into controlled publication validation', () => {
  assert.match(exportService, /Explicit Compendium cover image failed to render/);
  assert.match(exportService, /Re-select the image or use Automatic cover imagery/);
  assert.match(exportService, /catch \(OperationCanceledException\)[\s\S]*throw;/);
});

test('phase 41 separates page planning and physical verification failures from generic composer errors', () => {
  assert.match(exportService, /CompendiumPdfGenerationStage\.PagePlanning/);
  assert.match(exportService, /CompendiumPdfGenerationStage\.PdfVerification/);
  assert.match(exportService, /required DM Sans publication fonts could not be loaded/);
  assert.match(pageModel, /publicationFontUnavailable/);
  assert.match(pageModel, /paginationPlanningFailed/);
  assert.match(pageModel, /pdfLayoutFailed/);
  assert.match(pageModel, /pdfDrawingFailed/);
  assert.match(pageModel, /pdfVerificationFailed/);
});

test('phase 41 returns transport status that matches the failure class', () => {
  assert.match(pageModel, /PdfLayout[^\n]*=>\s*StatusCodes\.Status409Conflict/);
  assert.match(pageModel, /FontInitialization[^\n]*=>\s*StatusCodes\.Status503ServiceUnavailable/);
  assert.match(pageModel, /CompendiumPdfGenerationException\s*=>\s*StatusCodes\.Status500InternalServerError/);
});
