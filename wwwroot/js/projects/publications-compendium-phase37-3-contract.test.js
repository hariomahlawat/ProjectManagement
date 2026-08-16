const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const model = read('Models/Publications/CompendiumPreset.cs');
const db = read('Data/ApplicationDbContext.cs');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const presetContracts = read('Services/Publications/CompendiumPresetContracts.cs');
const migration = read('Migrations/20261216110000_AddCompendiumProjectAdditionalNote.cs');
const snapshot = read('Migrations/ApplicationDbContextModelSnapshot.cs');
const manifest = read('Migrations/immutable-migration-ids.txt');
const notePolicy = read('Services/Compendiums/CompendiumPublicationNotePolicy.cs');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const readiness = read('Services/Compendiums/CompendiumReadinessPolicy.cs');
const editorial = read('Services/Compendiums/CompendiumDossierEditorialPolicy.cs');
const flow = read('Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const measurement = read('Services/Compendiums/CompendiumDossierTextMeasurementService.cs');
const pagePlanner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const structurePage = read('Pages/Projects/Publications/Compendium/Structure.cshtml.cs');
const structureState = read('wwwroot/js/projects/compendium-structure-state.js');
const structureJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');


test('phase 37.3 persists optional per-project Additional Note through schema v10 and an additive migration', () => {
  assert.match(model, /SettingsSchemaVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*(?:10|11)/);
  assert.match(model, /string\?\s+AdditionalNote/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*(?:10|11)/);
  assert.match(presetService, /SettingsSchemaVersion\s*<\s*10\s*\?\s*null\s*:\s*NormalizeAdditionalNote/);
  assert.match(presetContracts, /string\?\s+AdditionalNote/);
  assert.match(db, /AdditionalNote\)\.HasColumnType\("text"\)/);
  assert.match(db, /SettingsSchemaVersion\)\.HasDefaultValue\((?:10|11)\)/);
  assert.match(migration, /Migration\("20261216110000_AddCompendiumProjectAdditionalNote"\)/);
  assert.match(migration, /AddColumn<string>[\s\S]*AdditionalNote[\s\S]*type:\s*"text"/);
  assert.match(migration, /SET "SettingsSchemaVersion" = 10/);
  assert.match(migration, /defaultValue:\s*10/);
  assert.match(snapshot, /Property<string>\("AdditionalNote"\)/);
  assert.match(snapshot, /SettingsSchemaVersion"\)\.ValueGeneratedOnAdd\(\).*HasDefaultValue\((?:10|11)\)/);
  assert.match(manifest, /20261216110000_AddCompendiumProjectAdditionalNote/);
});

test('phase 37.3 keeps Additional Note publication-specific and survives authoring plus Structure handoff', () => {
  assert.match(indexModel, /AdditionalNote\s*=\s*NormalizeAdditionalNote\(payload\.AdditionalNote\)/);
  assert.match(indexModel, /public string\? AdditionalNote/);
  assert.match(structurePage, /AdditionalNote/);
  assert.match(structureState, /const VERSION = 4/);
  assert.match(structureState, /additionalNote/);
  assert.match(structureJs, /additionalNote/);
  assert.doesNotMatch(read('Models/Project.cs'), /AdditionalNote/);
});

test('phase 37.3 exposes a non-destructive Additional Note editor and live proof', () => {
  assert.match(indexView, /data-review-additional-note/);
  assert.match(indexView, />Additional note</i);
  assert.match(indexView, /Optional note to appear at the end of this project dossier/);
  assert.doesNotMatch(indexView, /data-review-additional-note[^>]*maxlength=/);
  assert.match(indexView, /data-live-page-additional-note/);
  assert.match(indexView, /ADDITIONAL NOTE/);
  assert.match(mainJs, /additionalNoteAdvisory/);
  assert.match(mainJs, /AdvisoryCharacterCount|600/);
  assert.match(mainJs, /1000/);
  assert.match(css, /compendium-live-page__additional-note/);
});

test('phase 37.3 appends Additional Note after particulars and technical reference and physically paginates it', () => {
  assert.match(builder, /ComposeAdditionalNote/);
  assert.match(builder, /ADDITIONAL NOTE/);
  assert.match(pagePlanner, /AdditionalNoteMarkdown/);
  assert.match(pagePlanner, /SplitForPhysicalPages\([\s\S]*additionalNote/);
  assert.match(pagePlanner, /CanShareContinuationPage/);
  assert.match(pagination, /MeasureAdditionalNoteHeightPoints/);
  assert.match(pagination, /ContinuationBodyHeightPoints/);
  assert.doesNotMatch(pagePlanner, /CompendiumMarkdownChunker/);
});

test('phase 37.3 review identity binds note content and concrete media rendition identity', () => {
  assert.match(fingerprint, /compendium-review-v(?:15-additional-note-final-hardening|16-particulars-style)/);
  assert.match(fingerprint, /NormalizeNarrative\(input\.AdditionalNote\)/);
  assert.match(fingerprint, /PhotoVersion/);
  assert.match(fingerprint, /SourceWidth/);
  assert.match(fingerprint, /SourceHeight/);
  assert.match(dto, /PhotoVersion/);
  assert.match(readService, /PhotoVersion\s*=\s*candidate\.Version/);
  assert.match(readService, /SourceWidth\s*=\s*candidate\.Width/);
  assert.match(readService, /SourceHeight\s*=\s*candidate\.Height/);
});

test('phase 37.3 enforces measured Flow Below Image balance before Automatic optimisation', () => {
  assert.match(editorial, /MaximumFlowBelowGapPoints\s*=\s*40f/);
  assert.match(flow, /HasExcessiveGap[\s\S]*MaximumFlowBelowGapPoints/);
  assert.match(pagination, /flowBelowBalanced\s*=\s*!side\.HasExcessiveGap/);
  assert.match(pagination, /IsEditoriallyValid\s*=\s*imageGeometryValid\s*&&\s*sideColumnBalanced\s*&&\s*flowBelowBalanced/);
  assert.match(pagination, /Text beside the image leaves excessive unused vertical space/);
});

test('phase 37.3 keeps authoritative continuation and specification pagination physically measured', () => {
  assert.match(flow, /SplitForPhysicalPages/);
  assert.match(flow, /SplitToMeasuredHeight/);
  assert.match(flow, /measurementSession\.Fits/);
  assert.match(pagination, /SplitTechnicalSpecificationsForPhysicalPages/);
  assert.match(pagination, /MeasureAtFontSize\([\s\S]*LineCount <= maximumLines/);
  assert.match(pagination, /CanShareContinuationPage/);
  assert.doesNotMatch(pagePlanner, /CompendiumMarkdownChunker/);
  assert.doesNotMatch(pagePlanner, /fragment\.\[\.\.|fragment\[\.\./);
});

test('phase 37.3 warns rather than blocks when a complete Fit image becomes editorially too shallow', () => {
  assert.match(editorial, /ShallowFitWarningHeightPoints\s*=\s*72f/);
  assert.match(editorial, /ShallowFitWarning/);
  assert.match(editorial, /Consider Fill or a wider image layout/);
  assert.match(pagination, /shallowFitWarning/);
  assert.match(pagination, /HasEditorialWarning:\s*!best\.IsEditoriallyValid\s*\|\|\s*!string\.IsNullOrWhiteSpace\(best\.EditorialWarning\)/);
});

test('phase 37.3 hardens paragraph duplicate preflight without modifying source content', () => {
  assert.match(readiness, /duplicateNarrativeParagraph/);
  assert.match(readiness, /\.92d/);
  assert.match(readiness, /120/);
  assert.match(readiness, /NormalizeDuplicateComparisonText|NarrativeSimilarity|Similarity/i);
  assert.doesNotMatch(readiness, /Description\s*=\s*.*duplicate/i);
});

test('phase 37.3 makes DM Sans authoritative for physical page measurement', () => {
  assert.match(measurement, /DMSans-Regular\.ttf/);
  assert.match(measurement, /InvalidOperationException/);
  assert.doesNotMatch(measurement, /SKTypeface\.Default/);
  assert.match(measurement, /Physical page measurement cannot safely fall back to a different host font/);
});

test('phase 37.3 presents Project Brief as the publication default and advances final identities', () => {
  assert.match(indexView, /Project brief[\s\S]*DEFAULT/i);
  assert.match(notePolicy, /AdvisoryCharacterCount\s*=\s*600/);
  assert.match(notePolicy, /StrongAdvisoryCharacterCount\s*=\s*1000/);
  assert.match(readService, /CompendiumPdf_2026-08-16_(?:final-editorial-v22|particulars-style-v23)/);
});
