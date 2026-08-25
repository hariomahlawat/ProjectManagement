const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const indexModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const structureModel = read('Pages/Projects/Publications/Compendium/Structure.cshtml.cs');
const structureJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const structureState = read('wwwroot/js/projects/compendium-structure-state.js');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const parser = read('Services/Compendiums/CompendiumNarrativeDocument.cs');
const measurement = read('Services/Compendiums/CompendiumDossierTextMeasurementService.cs');
const flow = read('Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const pagePlanner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const narrativeRenderer = read('Utilities/Reporting/CompendiumNarrativePdfRenderer.cs');
const pdfBuilder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const readiness = read('Services/Compendiums/CompendiumReadinessPolicy.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const projectContent = read('Pages/Projects/_ProjectContentTabs.cshtml');

 test('phase 37.6 fixes Additional Note save-load-save round-trip on the main workspace', () => {
  assert.match(indexModel, /AdditionalNote\s*=\s*project\.AdditionalNote/);
  assert.match(indexModel, /review\.AdditionalNote/);
  assert.match(mainJs, /additionalNote:\s*String\(config\.additionalNote/);
  assert.match(mainJs, /data-review-additional-note/);
});

test('phase 37.6 distinguishes absent Structure note state from an explicit clear', () => {
  assert.match(structureModel, /AdditionalNote\s*=\s*item\.AdditionalNoteSpecified\s*\?/);
  assert.match(structureModel, /:\s*baseConfiguration\.AdditionalNote/);
  assert.match(structureModel, /bool AdditionalNoteSpecified/);
  assert.match(structureJs, /additionalNoteSpecified:\s*false/);
  assert.match(structureJs, /incoming\.additionalNoteSpecified\s*===\s*false/);
  assert.match(structureState, /Object\.prototype\.hasOwnProperty\.call\(source,\s*"additionalNote"\)/);
  assert.match(structureState, /const VERSION = (?:4|5)/);
});

test('phase 37.6 introduces one controlled semantic narrative model for measurement and PDF rendering', () => {
  assert.match(parser, /enum CompendiumNarrativeBlockKind/);
  assert.match(parser, /MinorHeading/);
  assert.match(parser, /BulletList/);
  assert.match(parser, /allowMinorHeadings/);
  assert.match(measurement, /MeasureSemanticAtFontSize/);
  assert.match(measurement, /MinorHeadingFontScale/);
  assert.match(measurement, /BulletGutterPoints/);
  assert.match(narrativeRenderer, /CompendiumNarrativeParser\.Parse/);
  assert.match(narrativeRenderer, /DisableHtml\(\)/);
  assert.doesNotMatch(narrativeRenderer, /UsePipeTables|UseAutoLinks|UseTaskLists/);
  assert.match(pdfBuilder, /CompendiumNarrativePdfRenderer\.Render/);
});

test('phase 37.6 physically paginates semantic narrative and keeps Additional Note heading semantics disabled', () => {
  assert.match(flow, /SplitForPhysicalPages[\s\S]*bool allowMinorHeadings = true/);
  assert.match(flow, /Never strand a semantic heading/);
  assert.match(flow, /FirstKeepWithNextUnit/);
  assert.match(flow, /allowMinorHeadings:\s*allowMinorHeadings/);
  assert.match(pagination, /allowMinorHeadings:\s*false/);
  assert.match(pagePlanner, /allowMinorHeadings:\s*false/);
  assert.match(pdfBuilder, /allowMinorHeadings:\s*false/);
});

test('phase 37.6 browser proof consumes server narrative blocks rather than independently inventing page semantics', () => {
  assert.match(indexModel, /narrativeBlocks\s*=\s*ToNarrativeBlockPayloads/);
  assert.match(indexModel, /sideBlocks\s*=\s*ToNarrativeBlockPayloads/);
  assert.match(indexModel, /belowBlocks\s*=\s*ToNarrativeBlockPayloads/);
  assert.match(mainJs, /renderNarrativeBlocks\(/);
  assert.match(mainJs, /review\.narrativeBlocks/);
  assert.match(mainJs, /flow\.sideBlocks/);
  assert.match(mainJs, /flow\.belowBlocks/);
});

test('phase 37.6 makes the controlled formatting vocabulary discoverable without adding a rich text editor', () => {
  assert.match(projectContent, /Compendium formatting:/);
  assert.match(projectContent, /<code>###<\/code> minor heading/);
  assert.match(indexView, /Formatting: <code>\*\*bold\*\*<\/code>/);
  assert.match(indexView, /<code>-<\/code> bullet/);
});

test('phase 37.6 retains duplicate-content protection and conservative placeholder warnings', () => {
  assert.match(readiness, /duplicateNarrativeParagraph/);
  assert.match(readiness, /lorem ipsum/i);
  assert.match(readiness, /dummy description/i);
  assert.match(readiness, /sample description/i);
  assert.match(readiness, /normalized\.Length <= 96/);
});

test('phase 37.6 changes render identity while preserving persisted schema and structure contract', () => {
  assert.match(fingerprint, /compendium-review-v(?:18-semantic-narrative|19-cover-identity)/);
  assert.match(readService, /CompendiumPdf_2026-08-16_(?:semantic-narrative-v25|cover-identity-v26)/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*(?:11|12|13)/);
  assert.match(structureState, /const VERSION = (?:4|5)/);
});
