const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const dtos = read('Services/Compendiums/CompendiumDtos.cs');
const typography = read('Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs');
const flowPlanner = read('Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs');
const imageQuality = read('Services/Compendiums/CompendiumImageQualityPolicy.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const presetContracts = read('Services/Publications/CompendiumPresetContracts.cs');
const model = read('Models/Publications/CompendiumPreset.cs');
const migration = read('Migrations/20261215170000_AddCompendiumNarrativeAlignment.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const markdownRenderer = read('Utilities/Reporting/MarkdownPdfRenderer.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexPage = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const structurePage = read('Pages/Projects/Publications/Compendium/Structure.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const structureJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const structureState = read('wwwroot/js/projects/compendium-structure-state.js');
const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const css = read('wwwroot/css/pages/projects-publications.css');

const compact = value => value.replace(/\s+/g, ' ');

test('phase 37 introduces narrative alignment with schema-v9 legacy-safe persistence', () => {
  assert.match(dtos, /enum CompendiumNarrativeAlignment[\s\S]*Left\s*=\s*0[\s\S]*Justified\s*=\s*1/);
  assert.match(model, /SettingsSchemaVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*(?:9|10|11|12)/);
  assert.match(model, /DefaultNarrativeAlignment[\s\S]*=\s*"Left"/);
  assert.match(model, /NarrativeAlignmentOverride/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*(?:9|10|11|12)/);
  assert.match(presetService, /SettingsSchemaVersion\s*<\s*9[\s\S]*CompendiumNarrativeAlignment\.Left/);
  assert.match(presetContracts, /DefaultNarrativeAlignment/);
  assert.match(presetContracts, /NarrativeAlignmentOverride/);
  assert.match(migration, /DefaultNarrativeAlignment/);
  assert.match(migration, /NarrativeAlignmentOverride/);
  assert.match(migration, /defaultValue:\s*"Left"/);
});

test('phase 37 upgrades Balanced flow to paragraph-first plus sentence-fill with measurable side utilisation', () => {
  assert.match(flowPlanner, /paragraph-first/i);
  assert.match(flowPlanner, /sentence-by-sentence|complete sentences/i);
  assert.match(flowPlanner, /SplitParagraphAtSentenceBoundary/);
  assert.match(flowPlanner, /SideRemainingHeightPoints/);
  assert.match(flowPlanner, /SideUtilizationRatio/);
  assert.match(flowPlanner, /HasExcessiveGap[\s\S]*(?:>\s*40f|MaximumFlowBelowGapPoints)/);
  assert.doesNotMatch(flowPlanner, /Substring\(|Split\('\s'\)|Split\("\s"\)/);
  assert.match(pagination, /SideRemainingHeightPoints\s*<=\s*18f/);
  assert.match(pagination, /SideRemainingHeightPoints\s*<=\s*30f/);
  assert.match(pagination, /SideRemainingHeightPoints\s*>\s*40f/);
});

test('phase 37 jointly evaluates multiple image heights instead of freezing the Balanced frame first', () => {
  assert.match(pagination, /CandidateImageHeights/);
  assert.match(pagination, /CompendiumDossierLayout\.Balanced[\s\S]*300f[\s\S]*285f[\s\S]*270f[\s\S]*255f/);
  assert.match(pagination, /AssessSideFlow\([\s\S]*narrative[\s\S]*renderedImageHeight[\s\S]*narrativeFontScale/);
  assert.match(pagination, /OrderByDescending\(candidate => candidate\.CompositionScore\)/);
  assert.match(pagination, /ThenBy\(candidate => candidate\.Side(?:OverflowHeightPoints \+ candidate\.SideRemainingHeightPoints|RemainingHeightPoints)\)/);
});

test('phase 37 provides publication default and per-project narrative alignment without unsafe narrow-column justification', () => {
  assert.match(typography, /MinimumSafeJustifiedColumnWidthPoints\s*=\s*245f/);
  assert.match(typography, /ResolveSideAlignment/);
  assert.match(typography, /sideColumnWidthPoints\s*>=\s*MinimumSafeJustifiedColumnWidthPoints/);
  assert.match(typography, /ResolveFullWidthAlignment/);
  assert.match(markdownRenderer, /justifyParagraphs/);
  assert.match(markdownRenderer, /text\.Justify\(\)/);
  assert.match(builder, /flow\.SideAlignment/);
  assert.match(builder, /flow\.BelowAlignment/);
  assert.match(indexView, />Narrative alignment</);
  assert.match(indexView, /data-narrative-alignment-value="Justified"/);
  assert.match(indexView, /data-review-narrative-alignment="default"/);
  assert.match(mainJs, /setProjectNarrativeAlignment/);
  assert.match(mainJs, /config\.narrativeAlignmentOverride\s*=\s*next;/);
  assert.doesNotMatch(mainJs, /narrativeAlignmentOverride\s*=\s*next\s*&&\s*next\s*!==\s*editorialState\.narrativeAlignment/);
  assert.match(mainJs, /invalidateProjectReview/);
});

test('phase 37 hard-gates low-DPI automatic hero layouts while retaining publisher override architecture', () => {
  assert.match(imageQuality, /PreferredPrintDpi\s*=\s*200/);
  assert.match(imageQuality, /AcceptablePrintDpi\s*=\s*150/);
  assert.match(imageQuality, /MinimumLargeImageDpi\s*=\s*120/);
  assert.match(imageQuality, /effectiveDpi\.Value\s*<\s*MinimumLargeImageDpi[\s\S]*Balanced[\s\S]*Technical/);
  assert.match(pagination, /!explicitLayout[\s\S]*IsAutomaticLayoutAllowed/);
  assert.match(pagination, /BuildCandidateLayouts\([\s\S]*primaryImageEffectiveDpi/);
  assert.match(pagination, /MaximumAutomaticImageHeight/);
  assert.match(readService, /planningDpi/);
  assert.match(readService, /effectiveDpi/);
});

test('phase 37 strengthens residual-space scoring and responsive Project Particulars', () => {
  assert.match(pagination, /ResidualSpacePoints/);
  assert.match(pagination, />\s*110f/);
  assert.match(pagination, />\s*80f/);
  assert.match(pagination, /<\s*45f/);
  assert.match(pagination, /ResolveIdealResidualSpace/);
  assert.match(pagination, /moduleCount switch[\s\S]*1\s*=>\s*52f[\s\S]*<=\s*3\s*=>\s*57f/);
  assert.match(builder, /panelPaddingVertical/);
  assert.match(builder, /modules\.Count\s*==\s*1/);
});

test('phase 37 corrects Portfolio Quartet proof geometry into upper identity plus lower 1+3 mosaic', () => {
  assert.match(css, /\.cover-proof-quartet\{[^}]*top:338px;[^}]*height:488px;[^}]*grid-template-columns:2fr 1fr/);
  assert.match(css, /\.cover-proof-quartet__stack\{[^}]*grid-template-rows:repeat\(3,1fr\)/);
  assert.match(coverJs, /PortfolioQuartet[\s\S]*identity[\s\S]*cover-proof-quartet/);
  assert.match(coverView, /Portfolio Triptych/);
  assert.match(coverView, />Fit page</);
  assert.ok(compact(builder).includes('CompendiumFrontCoverTemplate.PortfolioQuartet'));
});

test('phase 37 preserves alignment through Structure Editor handoff and persisted structure save', () => {
  assert.match(structureState, /const VERSION = (?:3|4)/);
  assert.match(structureState, /narrativeAlignmentOverride/);
  assert.match(structureState, /narrativeAlignment/);
  assert.match(structureJs, /narrativeAlignmentOverride/);
  assert.match(structureJs, /narrativeAlignment:\s*incomingHandoff/);
  assert.match(structurePage, /NarrativeAlignmentOverride\s*=\s*ParseNarrativeAlignmentOverride/);
  assert.match(structurePage, /public string\? NarrativeAlignmentOverride/);
});

test('phase 37 binds typography changes into review and publication identities', () => {
  assert.match(fingerprint, /compendium-review-v(?:12-professional-typesetting|13-physical-measurement|14-editorial-constraints|15-additional-note-final-hardening|16-particulars-style|17-editorial-rules|18-semantic-narrative|19-cover-identity)/);
  assert.match(fingerprint, /input\.NarrativeAlignment\.ToString\(\)/);
  assert.match(readService, /(?:CompendiumPdf_2026-08-15_composition-hardening-v19|CompendiumPdf_2026-08-16_(?:physical-composition-v20|editorial-constraints-v21|final-editorial-v22|particulars-style-v23|editorial-rules-v24|semantic-narrative-v25|cover-identity-v26))/);
  assert.match(indexPage, /NarrativeAlignmentOverride/);
  assert.match(indexPage, /DefaultNarrativeAlignment/);
});

test('phase 37 keeps browser proof driven by server flow/alignment decisions rather than local pagination heuristics', () => {
  assert.match(mainJs, /review\.narrativeFlow/);
  assert.match(mainJs, /flow\.sideAlignment/);
  assert.match(mainJs, /flow\.belowAlignment/);
  assert.match(mainJs, /is-justified/);
  assert.doesNotMatch(mainJs, /function\s+(?:resolve|plan).*Sentence/i);
  assert.doesNotMatch(mainJs, /function resolveSpecificationColumns/);
});
