const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = process.cwd();
const view = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml'), 'utf8');
const css = fs.readFileSync(path.join(root, 'wwwroot', 'css', 'pages', 'projects-publications.css'), 'utf8');
const js = fs.readFileSync(path.join(root, 'wwwroot', 'js', 'pages', 'projects-brochure.js'), 'utf8');
const renderer = fs.readFileSync(path.join(root, 'Utilities', 'Reporting', 'BrochurePdfReportBuilder.cs'), 'utf8');
const printRenderer = fs.readFileSync(path.join(root, 'Utilities', 'Reporting', 'BrochurePrintCompactComposer.cs'), 'utf8');

const criticalClasses = [
  'brochure-filter-toolbar',
  'brochure-search-field',
  'brochure-project-table-wrap',
  'brochure-project-table',
  'brochure-copy-status',
  'brochure-photo-summary',
  'brochure-selection-panel',
  'brochure-preflight-grid',
  'brochure-preflight-issues',
  'brochure-photo-choice',
  'brochure-focal-stage',
  'brochure-generate-actions',
  'brochure-cover-hero-panel',
  'brochure-review-panel',
  'brochure-review-card',
  'brochure-review-nav',
  'brochure-cover-review-state',
  'brochure-cover-crop-editor',
  'brochure-profile-options',
  'brochure-profile-option',
  'brochure-print-content',
  'brochure-print-content-grid'
];

test('brochure critical Razor classes have explicit stylesheet coverage', () => {
  const markupOrClient = `${view}\n${js}`;
  for (const className of criticalClasses) {
    assert.match(markupOrClient, new RegExp(`\\b${className}\\b`), `markup/client should use ${className}`);
    assert.match(css, new RegExp(`\\.${className}(?:[^a-zA-Z0-9_-]|$)`), `css should style ${className}`);
  }
});

test('brochure uses the publication-photo handler instead of fixed project derivatives', () => {
  assert.match(view, /"Photo"/);
  assert.match(view, /mode = "thumb"/);
  assert.match(view, /mode = "source"/);
  assert.doesNotMatch(view, /\/Projects\/Photos\/View/);
});

test('brochure selection workspace has bounded filtering and readiness controls', () => {
  assert.match(view, /data-brochure-filter="readiness"/);
  assert.match(view, /data-brochure-selected-only/);
  assert.match(view, /data-brochure-match-count/);
  assert.match(css, /max-height:\s*min\(58vh,\s*620px\)/);
  assert.match(css, /position:\s*sticky;\s*top:\s*0;/);
});

test('brochure client renders all preflight findings on demand and offers actions', () => {
  assert.match(js, /data-preflight-show-all/);
  assert.match(js, /Show all \$\{ordered\.length\} findings/);
  assert.match(js, /Open \${narrativeInfo\(project\)\.label}/);
  assert.match(js, /Configure image/);
  assert.doesNotMatch(js, /slice\(0,\s*8\)/);
});

test('brochure Gallery 2 remains an explicit second-image editorial choice', () => {
  assert.match(js, /secondaryPhotoId: project\.defaultSecondaryPhotoId \?\? null/);
  assert.match(js, /config\.secondaryPhotoId = Number\(photo\.photoId\)/);
  assert.doesNotMatch(js, /find\(photo => Number\(photo\.photoId\).*secondaryPhotoId/);
});


test('phase 6 keeps cover hero independent from project ordering and project primary imagery', () => {
  assert.match(view, /data-brochure-cover-hero-project/);
  assert.match(view, /data-brochure-cover-hero-photo/);
  assert.match(view, /data-brochure-cover-hero-focal-x/);
  assert.match(view, /data-cover-hero-approve/);
  assert.match(js, /explicitCoverHeroPhotoId/);
  assert.match(js, /coverHeroFocalX/);
  assert.match(js, /coverReviewed/);
  assert.match(js, /flatMap\(id =>/);
  assert.doesNotMatch(js, /orderedIds = next;\s*invalidateAllReviews\(\)/);
});

test('phase 6 provides project approval and cover approval for final download', () => {
  assert.match(view, /Review publication/);
  assert.match(view, /Approve project/);
  assert.match(view, /data-cover-hero-approve/);
  assert.doesNotMatch(view, /Use this image/);
  assert.match(js, /allReviewed/);
  assert.match(js, /coverReady = !isContemporaryCover\(\) \|\| coverReviewed/);
  assert.match(js, /finalReady = previewReady && allReviewed\(\) && coverReady/);
});

test('phase 5 refreshes authoritative project state after cross-tab edits', () => {
  assert.match(view, /data-brochure-project-state-url/);
  assert.match(js, /refreshProjectState/);
  assert.match(js, /visibilitychange/);
  assert.match(js, /window\.addEventListener\("focus"/);
  assert.match(js, /renderSelected\(false, false\)/);
});

test('phase 5 uses fetch and blob for preview and final brochure download', () => {
  assert.match(view, /data-brochure-preview-url/);
  assert.match(view, /data-brochure-generate-url/);
  assert.match(js, /new FormData\(form\)/);
  assert.match(js, /response\.blob\(\)/);
  assert.match(js, /URL\.createObjectURL/);
  assert.match(js, /X-PRISM-Publication-FileName/);
  assert.match(js, /Preparing brochure/);
});

test('phase 5 selection wording refers to matching projects, not viewport visibility', () => {
  assert.match(js, /Select first .* matching|Select .* matching/);
  assert.doesNotMatch(view, />Select visible</);
});


test('phase 6 renderer uses independent Cover B artwork with finalised full-page geometry', () => {
  assert.match(renderer, /var hero = data\.CoverHeroImage\?\.Content/);
  assert.match(renderer, /AlignBottom\(\)[\s\S]{0,220}PaddingBottom\(92\)[\s\S]{0,120}Height\(364\)/);
  assert.match(renderer, /AlignBottom\(\)[\s\S]{0,120}Height\(92\)[\s\S]{0,120}Background\(Forest950\)/);
  const contemporary = renderer.slice(renderer.indexOf('private static void ComposeContemporaryCover'), renderer.indexOf('private static void ComposeIntroductionPages'));
  assert.doesNotMatch(contemporary, /Generated from authoritative PRISM records/);
});

test('phase 6 renderer gives two-project pages adaptive imagery and SingleFeature a dedicated page composer', () => {
  assert.match(renderer, /ComposeTwoFeatureBlock/);
  assert.match(renderer, /<= 125 => \(225f, 145f, 112f\)/);
  assert.match(renderer, /<= 155 => \(215f, 132f, 108f\)/);
  assert.match(renderer, /ComposeSingleFeaturePage/);
  assert.match(renderer, /Width\(445\)\.Height\(250\)/);
  assert.match(renderer, /imageOnRight:\s*index % 2 == 0/);
});


test('phase 6 technical preflight no longer counts unconfirmed project images as warnings', () => {
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  assert.doesNotMatch(service, /BrochurePreflightIssueCode\.UnconfirmedPrimaryPhoto[\s\S]{0,120}PublicationIssueSeverity\.Warning/);
  assert.match(view, /data-review-reviewed-count/);
});

test('phase 6 renderer supports an optional dedicated back cover', () => {
  assert.match(renderer, /data\.Options\.IncludeBackCover/);
  assert.match(renderer, /ComposeBackCover/);
  assert.match(view, /Input\.IncludeBackCover/);
});


test('phase 7 exposes original-format compact print and digital comfortable profiles', () => {
  assert.match(view, /Print \/ Compact/);
  assert.match(view, /423\.23 × 846\.755 pt/);
  assert.match(view, /Digital \/ Comfortable/);
  assert.match(view, /data-brochure-profile/);
  assert.match(js, /isPrintCompactProfile/);
  assert.match(js, /updatePublicationProfileUi/);
});

test('phase 7 keeps the reference brochure front and final institutional content editable', () => {
  assert.match(view, /Input\.PrintIntroText/);
  assert.match(view, /Input\.PrintFutureText/);
  assert.match(view, /Input\.PrintProcurementText/);
  assert.match(view, /Input\.PrintDevelopingAgencyText/);
  assert.match(view, /Input\.PrintManufacturingAgencyText/);
  assert.match(view, /Input\.PrintVisionaryText/);
  assert.match(view, /Input\.PrintNewSimulatorsText/);
  assert.match(printRenderer, /Visionary Horizons & Strategic Objectives/);
  assert.match(printRenderer, /New Simulators\./);
});

test('phase 7 print compositor uses the reference CropBox dimensions and natural project packing', () => {
  assert.match(printRenderer, /ReferenceWidthPoints = 423\.23f/);
  assert.match(printRenderer, /ReferenceHeightPoints = 846\.755f/);
  assert.match(printRenderer, /ShowEntire\(\)/);
  assert.match(printRenderer, /ShowEntire\(\)/);
  assert.match(printRenderer, /ComposeProjectModule/);
  assert.doesNotMatch(printRenderer, /PageSizes\.A4/);
});

test('phase 7 review image buttons have distinct select and crop behaviour', () => {
  assert.match(js, /openPhotoEditor\(activeReviewProjectId, "select"\)/);
  assert.match(js, /openPhotoEditor\(activeReviewProjectId, "crop"\)/);
  assert.match(js, /photoEditorFocusMode === "crop"/);
  assert.match(js, /primaryStage\.focus/);
});

test('phase 7 cover image controls focus newly opened chooser and crop editor', () => {
  assert.match(js, /coverHeroChoices\.scrollIntoView/);
  assert.match(js, /coverHeroCropPanel\.scrollIntoView/);
  assert.match(js, /coverHeroFocalStage\?\.focus/);
});
