const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const publicationTests = read('ProjectManagement.Tests/Compendiums/CompendiumPublicationTests.cs');

// Regression for the deterministic defect found in the Phase 32 cover editor: Bootstrap
// modals are outside data-compendium-cover-editor, so root.querySelector cannot reach them.
test('phase 33 resolves cover modal controls from the document portal', () => {
  assert.match(coverView, /id="compendiumCoverPhotoModal"/);
  assert.match(coverView, /id="compendiumCoverCropModal"/);
  assert.match(coverJs, /const\s+portalBy\s*=\s*selector\s*=>\s*document\.querySelector\(selector\)/);
  for (const control of [
    'data-cover-photo-modal-slot',
    'data-cover-project-select',
    'data-cover-project-search',
    'data-cover-photo-grid',
    'data-cover-photo-state',
    'data-cover-crop-image',
    'data-cover-crop-target',
    'data-cover-crop-stage',
    'data-cover-crop-centre',
    'data-cover-return-unsaved',
    'data-cover-save-return'
  ]) {
    assert.match(coverJs, new RegExp(`portalBy\\('\\[${control}\\]`));
  }
});

test('phase 33 lets a visible automatic Fill image transition directly into crop editing', () => {
  assert.match(coverJs, /async\s+function\s+pinResolvedAutomaticSlot\(slot\)/);
  assert.match(coverJs, /slot\.imageMode\s*=\s*'Explicit'/);
  assert.match(coverJs, /slot\.projectId\s*=\s*Number\(resolved\.projectId\)/);
  assert.match(coverJs, /slot\.photoId\s*=\s*Number\(resolved\.photoId\)/);
  assert.match(coverJs, /async\s+function\s+openCrop\(slotKey\)/);
  assert.match(coverJs, /slot\.imageMode\s*===\s*'Automatic'\s*&&\s*!\(await\s+pinResolvedAutomaticSlot\(slot\)\)/);
  assert.match(coverJs, /slot\.imageMode\s*===\s*'None'\s*\|\|\s*!slot\.previewUrl\s*\|\|\s*slot\.fitMode\s*===\s*'Fit'/);
});

test('phase 33 fixes icon-text spacing and back-button hover without leaking heading styles into buttons', () => {
  assert.match(css, /\.compendium-review-focus-toggle\s*\{[^}]*gap:\s*\.42rem/s);
  assert.match(css, /\.compendium-cover-editor-heading\s*>\s*div\s*>\s*span/);
  assert.match(css, /\.compendium-cover-editor-heading\s*>\s*a\s*>\s*span\s*\{[^}]*color:\s*inherit/s);
  assert.doesNotMatch(css, /\.compendium-cover-editor-heading\s+span\s*\{/);
  assert.match(css, /\.compendium-structure-editor-heading\s*>\s*div\s*>\s*span/);
  assert.match(css, /\.compendium-structure-editor-heading\s*>\s*a\s*>\s*span\s*\{[^}]*color:\s*inherit/s);
});

test('phase 33 makes dossier layout choices container-safe instead of forcing five narrow equal columns', () => {
  assert.match(css, /\.compendium-dossier-layout-options\s*\{[^}]*grid-template-columns:repeat\(6,minmax\(0,1fr\)\)/s);
  assert.match(css, /\.compendium-dossier-layout-options button\s*\{[^}]*grid-column:span 2[^}]*white-space:normal[^}]*overflow-wrap:anywhere/s);
  assert.match(css, /\.compendium-dossier-layout-options button:nth-child\(n\+4\)\s*\{grid-column:span 3\}/);
  assert.doesNotMatch(css, /\.compendium-dossier-layout-options\s*\{[^}]*repeat\(5,minmax\(0,1fr\)\)/s);
});

test('phase 33 uses the same local DM Sans publication family in browser proofs and QuestPDF', () => {
  assert.match(css, /font-family:\s*"PRISM Publication Sans"/);
  assert.match(css, /DMSans-Regular\.ttf/);
  assert.match(css, /\.compendium-live-page__sheet\s*\{[^}]*font-family:\s*"PRISM Publication Sans"/s);
  assert.match(css, /\.compendium-cover-proof-sheet[^{]*\{[^}]*font-family:\s*"PRISM Publication Sans"/s);
  assert.match(builder, /private readonly IPublicationFontService _fontService;/);
  assert.match(builder, /_fontService\.EnsureRegistered\(\)/);
  assert.match(builder, /\.FontFamily\(Volatile\.Read\(ref s_primaryFontFamily\)\)/);
  assert.match(publicationTests, /new PublicationFontService\(/);
});

test('phase 33 normalises publication tracking and title leading', () => {
  assert.doesNotMatch(builder, /LetterSpacing\((?:1(?:\.\d+)?|\.75f|\.8f|\.7f|\.65f|\.6f)\)/);
  assert.match(builder, /ResolveProjectTitleFontSize\(project\.ProjectName\)\)\.SemiBold\(\)\.LineHeight\(1\.08f\)/);
  assert.match(builder, /HARDWARE \/ TECHNICAL SPECIFICATION/);
  assert.match(builder, /FontSize\(7\.7f\)\.SemiBold\(\)\.LetterSpacing\(\.14f\)/);
});

test('phase 33 keeps the fixed output dock from consuming the final working controls', () => {
  assert.match(mainJs, /classList\.toggle\('has-output-dock',\s*shouldShow\)/);
  assert.match(css, /\.compendium-builder-page\.has-output-dock:not\(\.is-review-focus\) \.compendium-builder-rail\s*\{\s*padding-bottom:\s*5\.5rem/);
});

test('phase 33 advances review and PDF identities so existing approvals do not mask changed presentation', () => {
  assert.match(fingerprint, /compendium-review-v(?:8-production-hardening|9-programme-iconography|10-sponsoring-line-directorate|11-balanced-text-flow)/);
  assert.match(readService, /CompendiumPdf_2026-08-15_(?:production-hardening-v13|programme-iconography-v1[45]|programme-semantics-v16|programme-particulars-v17|final-composition-v18)/);
});
