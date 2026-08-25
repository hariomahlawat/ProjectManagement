const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const typography = read('Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs');
const flow = read('Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs');
const renderer = read('Utilities/Reporting/CompendiumNarrativePdfRenderer.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexPage = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const dtos = read('Services/Compendiums/CompendiumDtos.cs');
const exportContract = read('Services/Compendiums/ICompendiumExportService.cs');
const presetContracts = read('Services/Publications/CompendiumPresetContracts.cs');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const model = read('Models/Publications/CompendiumPreset.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const buildIdentity = read('Utilities/Reporting/CompendiumBuildIdentity.cs');

const compact = value => value.replace(/\s+/g, ' ');

test('phase 44 replaces width-gated justification with semantic narrative-region policy', () => {
  assert.match(typography, /enum CompendiumNarrativeSegment[\s\S]*FullWidth[\s\S]*BalancedSide[\s\S]*BelowImage[\s\S]*Continuation[\s\S]*AdditionalNote/);
  assert.match(typography, /ResolveAlignment\(/);
  assert.doesNotMatch(typography, /MinimumSafeJustifiedColumnWidthPoints/);
  assert.doesNotMatch(typography, /sideColumnWidthPoints\s*>=/);
  assert.match(flow, /CompendiumNarrativeSegment\.BalancedSide/);
  assert.match(flow, /CompendiumNarrativeSegment\.BelowImage/);
  assert.match(renderer, /if \(justifyParagraphs\) text\.Justify\(\)/);
  assert.match(builder, /flow\.SideAlignment/);
  assert.match(builder, /flow\.BelowAlignment/);
  assert.match(builder, /CompendiumNarrativeSegment\.Continuation/);
  assert.match(builder, /CompendiumNarrativeSegment\.AdditionalNote/);
});

test('phase 44 makes Justified the authoring default while preserving legacy saved presets as Left', () => {
  assert.match(indexPage, /Input\.NarrativeAlignment\s*=\s*nameof\(CompendiumNarrativeAlignment\.Justified\)/);
  assert.match(indexPage, /public string NarrativeAlignment \{ get; set; \} = nameof\(CompendiumNarrativeAlignment\.Justified\)/);
  assert.match(dtos, /CompendiumPublicationRequest[\s\S]*DefaultNarrativeAlignment \{ get; init; \} = CompendiumNarrativeAlignment\.Justified/);
  assert.match(exportContract, /DefaultNarrativeAlignment \{ get; init; \} = CompendiumNarrativeAlignment\.Justified/);
  assert.match(presetContracts, /DefaultNarrativeAlignment \{ get; init; \} = CompendiumNarrativeAlignment\.Justified/);

  // Database/entity and pre-v9 schema fallbacks intentionally remain Left for backward compatibility.
  assert.match(model, /DefaultNarrativeAlignment[\s\S]*=\s*"Left"/);
  assert.match(presetService, /SettingsSchemaVersion\s*<\s*9[\s\S]*CompendiumNarrativeAlignment\.Left/);
});

test('phase 44 UI describes actual semantic behaviour instead of the obsolete narrow-column exception', () => {
  const compactView = compact(indexView);
  assert.match(compactView, /data-narrative-alignment-value="Justified">Justified\s*<span>Default<\/span>/);
  assert.match(compactView, /Justified aligns normal narrative prose to both margins/);
  assert.doesNotMatch(compactView, /Narrow Balanced side columns remain left aligned/);
});

test('phase 44 invalidates only existing justified review fingerprints and advances PDF build identity', () => {
  assert.match(fingerprint, /compendium-review-v19-cover-identity/);
  assert.match(fingerprint, /compendium-review-v20-semantic-justification/);
  assert.match(fingerprint, /NarrativeAlignment\s*==\s*CompendiumNarrativeAlignment\.Justified/);
  assert.match(buildIdentity, /Phase\s*=\s*"(?:44|45)"/);
  assert.match(buildIdentity, /physical-a4-v45/);
  assert.match(buildIdentity, /semantic-justification/i);
});
