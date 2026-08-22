const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = relativePath => fs.readFileSync(path.join(root, relativePath), 'utf8');

const identity = read('Utilities/Reporting/CompendiumBuildIdentity.cs');
const fontContract = read('Utilities/Reporting/PublicationFontContract.cs');
const fontRegistry = read('Utilities/Reporting/PublicationFontRegistry.cs');
const measurement = read('Services/Compendiums/CompendiumDossierTextMeasurementService.cs');
const selfTest = read('Utilities/Reporting/CompendiumOfflineSelfTest.cs');
const diagnostics = read('Utilities/Reporting/CompendiumGenerationDiagnostics.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const pageModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const program = read('Program.cs');
const publishScript = read('ops/publish/create-publish-folder.ps1');

test('phase 41 exposes one build identity across HTTP PDF self-test and diagnostics', () => {
  assert.match(identity, /CompendiumPdf_2026-08-22_phase41-production-convergence/);
  assert.match(identity, /physical-a4-v41/);
  assert.match(pageModel, /CompendiumBuildIdentity\.HeaderName/);
  assert.match(pageModel, /Response\.Headers\[CompendiumBuildIdentity\.HeaderName\]/);
  assert.match(diagnostics, /build = CompendiumBuildIdentity\.BuildStamp/);
  assert.match(selfTest, /build = CompendiumBuildIdentity\.BuildStamp/);
});

test('QuestPDF and Skia planning resolve the same complete local DM Sans contract', () => {
  for (const face of ['Regular', 'Medium', 'SemiBold', 'Bold', 'Italic', 'BoldItalic']) {
    assert.match(fontContract, new RegExp(`DMSans-${face}\\.ttf`));
  }
  assert.match(fontContract, /AppContext\.BaseDirectory/);
  assert.match(fontContract, /PRISM_PUBLICATION_FONTS_DIR/);
  assert.match(fontRegistry, /PublicationFontContract\.InspectDmSans/);
  assert.match(measurement, /PublicationFontContract\.ResolveRequiredDmSansFile/);
  assert.doesNotMatch(measurement, /CandidateFontPaths/);
});

test('air-gapped payload self-test runs before web host and database construction', () => {
  const switchIndex = program.indexOf('CompendiumOfflineSelfTest.CommandLineSwitch');
  const builderIndex = program.indexOf('WebApplication.CreateBuilder');
  assert.ok(switchIndex >= 0 && switchIndex < builderIndex);
  assert.match(selfTest, /SKTypeface\.FromFile/);
  assert.match(selfTest, /GeneratePdf\(\)/);
  assert.match(selfTest, /PdfDocument\.Open/);
});

test('publish output is self-contained win-x64 and validates fonts native runtime and PDF chain', () => {
  assert.match(publishScript, /--runtime win-x64/);
  assert.match(publishScript, /--self-contained true/);
  assert.match(publishScript, /coreclr\.dll/);
  assert.match(publishScript, /libSkiaSharp\.dll/);
  assert.match(publishScript, /DMSans-BoldItalic\.ttf/);
  assert.match(publishScript, /--compendium-offline-self-test/);
});

test('large Compendium renders are bounded and failures have durable offline diagnostics', () => {
  assert.match(exportService, /SemaphoreSlim GenerationGate/);
  assert.match(exportService, /GenerationGate\.WaitAsync\(cancellationToken\)/);
  assert.match(exportService, /GenerationGate\.Release\(\)/);
  assert.match(diagnostics, /compendium-generation-\{DateTime\.UtcNow:yyyyMMdd\}\.jsonl/);
  assert.match(diagnostics, /PRISM_COMPENDIUM_DIAGNOSTICS_DIR/);
  assert.match(pageModel, /CompendiumGenerationDiagnostics\.TryWrite/);
});

test('publication read cover planning composition and verification remain distinguishable', () => {
  for (const stage of [
    'PublicationRead',
    'CoverResolution',
    'FontInitialization',
    'PagePlanning',
    'PdfComposition',
    'PdfLayout',
    'PdfDrawing',
    'PdfVerification'
  ]) {
    assert.match(read('Utilities/Reporting/CompendiumPdfGenerationException.cs'), new RegExp(stage));
  }
  assert.match(pageModel, /coverResolutionFailed/);
  assert.match(pageModel, /publicationReadFailed/);
});
