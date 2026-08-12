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
  'brochure-print-content-grid',
  'brochure-print-plan-summary',
  'brochure-review-image-mode',
  'brochure-print-word-status'
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
  assert.match(view, /Original brochure format/);
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
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(metrics, /ReferenceWidthPoints = 423\.23f/);
  assert.match(metrics, /ReferenceHeightPoints = 846\.755f/);
  assert.match(printRenderer, /ReferenceWidthPoints = BrochurePrintLayoutMetrics\.ReferenceWidthPoints/);
  assert.match(printRenderer, /ReferenceHeightPoints = BrochurePrintLayoutMetrics\.ReferenceHeightPoints/);
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


test('phase 9 replaces word-count heuristics with font-aware DM Sans measurement', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(measurement, /SKPaint/);
  assert.match(measurement, /MeasureText\(/);
  assert.match(measurement, /DMSans-Regular\.ttf/);
  assert.match(measurement, /DMSans-SemiBold\.ttf/);
  assert.match(measurement, /MeasureProject/);
  assert.match(measurement, /MeasureClosing/);
  assert.match(measurement, /MeasureFrontPage/);
  assert.doesNotMatch(measurement, /wordsPerLine/);
});

test('phase 9 uses an order-preserving measured sheet planner with final closing reservation', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  assert.match(planner, /MaximumProjectsPerSheet/);
  assert.match(planner, /TryPlanWithSharedClosing/);
  assert.match(planner, /MeasureClosing/);
  assert.match(planner, /MeasureProject/);
  assert.match(planner, /ClosingMatterSharesFinalPage/);
  assert.match(planner, /SheetPlan/);
  assert.match(printRenderer, /BrochurePrintCompactPlan plan/);
  assert.match(printRenderer, /sheet\.Projects/);
  assert.match(printRenderer, /sheet\.IncludesClosingMatter/);
});

test('phase 8 print A never substitutes an arbitrary first-project photograph', () => {
  const frontHero = printRenderer.slice(
    printRenderer.indexOf('private static void ComposeFrontHero'),
    printRenderer.indexOf('private static void ComposeFrontLockup'));
  assert.match(frontHero, /institutionalArtwork/);
  assert.match(frontHero, /ComposeInstitutionalFallbackArtwork/);
  assert.doesNotMatch(frontHero, /data\.Projects\[0\]/);
  assert.doesNotMatch(frontHero, /PrimaryPhoto/);
});

test('phase 8 hard-copy institutional matter participates in authoritative preflight', () => {
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  const policy = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPublicationPolicy.cs'), 'utf8');
  assert.match(service, /ApplyPublicationLevelPreflight/);
  assert.match(service, /BrochurePrintPublicationPolicy\.Validate/);
  assert.match(policy, /PrintInstitutionalContentMissing/);
  assert.match(policy, /PrintInstitutionalContentTooLong/);
  assert.match(view, /data-brochure-approved-print-content/);
  assert.match(js, /updatePrintMatterWordCounts/);
  assert.match(js, /Restore all hard-copy institutional text/);
});

test('phase 8 exposes compact plan estimates in the same publication preflight panel', () => {
  assert.match(view, /data-print-plan-summary/);
  assert.match(view, /data-print-estimate-pages/);
  assert.match(view, /data-print-estimate-fill/);
  assert.match(view, /data-print-estimate-closing/);
  assert.match(js, /result\.estimatedPageCount/);
  assert.match(js, /result\.closingMatterSharesFinalPage/);
});

test('phase 8 exposes Gallery 2 directly during publication review', () => {
  assert.match(view, /data-review-image-mode/);
  assert.match(view, />Gallery 2</);
  assert.match(js, /reviewImageModeSelect/);
  assert.match(js, /openPhotoEditor\(activeReviewProjectId, "secondary"\)/);
  assert.match(js, /config\.imageMode !== modeGalleryTwo \|\| config\.secondaryPhotoId != null/);
});

test('phase 8 print project typography centres headings and justifies publication copy', () => {
  assert.match(printRenderer, /ProjectName\.ToUpperInvariant\(\)/);
  assert.match(printRenderer, /\.AlignCenter\(\)/);
  assert.match(printRenderer, /ComposeNarrativeText/);
  assert.match(printRenderer, /if \(justify\)[\s\S]{0,260}\.Justify\(\)/);
});

test('phase 9 measured Cover A composition removes fixed body/contact spacer geometry', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(printRenderer, /frontPlan\.HeroHeightPoints/);
  assert.match(printRenderer, /frontPlan\.BodyBlockHeightPoints/);
  assert.match(printRenderer, /frontPlan\.ContactBlockHeightPoints/);
  assert.match(printRenderer, /frontPlan\.BodyFontSize/);
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(metrics, /FrontBodyPreferredFontSize/);
  assert.match(metrics, /FrontBodyMinimumFontSize/);
  assert.doesNotMatch(printRenderer, /PaddingTop\(326\)/);
  assert.doesNotMatch(printRenderer, /Height\(98\)/);
});

test('phase 9 print plan UI exposes measured per-sheet mapping and fill diagnostics', () => {
  assert.match(view, /data-print-lowest-fill/);
  assert.match(view, /data-print-final-fill/);
  assert.match(view, /data-print-sheet-map/);
  assert.match(js, /result\.lowestProjectPageUtilizationPercent/);
  assert.match(js, /result\.finalPageUtilizationPercent/);
  assert.match(js, /result\.printSheetPlan/);
  assert.match(css, /\.brochure-print-sheet-chip/);
});

test('phase 9 registers and validates measured print services through the publication DI graph', () => {
  const registration = fs.readFileSync(path.join(root, 'Services', 'Publications', 'PublicationServiceCollectionExtensions.cs'), 'utf8');
  const runtime = fs.readFileSync(path.join(root, 'Services', 'Publications', 'PublicationRuntimeValidationHostedService.cs'), 'utf8');
  assert.match(registration, /AddSingleton<IBrochurePrintMeasurementService, BrochurePrintMeasurementService>/);
  assert.match(registration, /AddSingleton<IBrochurePrintPagePlanner, BrochurePrintPagePlanner>/);
  assert.match(runtime, /GetRequiredService<IBrochurePrintMeasurementService>/);
  assert.match(runtime, /GetRequiredService<IBrochurePrintPagePlanner>/);
});

test('phase 9 profile defaults prefer institutional print and contemporary digital until user chooses explicitly', () => {
  assert.match(js, /coverSelectionTouched/);
  assert.match(js, /preferredValue = isPrintCompactProfile\(\) \? "1" : "2"/);
  assert.match(js, /isPrintCompactProfile\(\)/);
  assert.match(js, /coverSelectionTouched = true/);
});


test('phase 10 print compact restores reference float composition and removes image alternation', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');

  assert.match(measurement, /SplitNarrativeForFloat/);
  assert.match(measurement, /LeadingNarrative/);
  assert.match(measurement, /TrailingNarrative/);
  assert.match(printRenderer, /layout\.LeadingNarrative/);
  assert.match(printRenderer, /layout\.TrailingNarrative/);
  assert.match(printRenderer, /row\.ConstantItem\(layout\.ImageWidthPoints\)\.AlignTop\(\)/);
  assert.doesNotMatch(printRenderer, /plannedProject\.ProjectIndex\s*%\s*2/);
  assert.doesNotMatch(printRenderer, /imageOnRight/);
  assert.match(metrics, /ProjectBodyPreferredFontSize = 9f/);
  assert.match(metrics, /ProjectBodyMinimumFontSize = 8\.5f/);
  assert.match(metrics, /ProjectTitlePreferredFontSize = 10f/);
  assert.match(metrics, /ProjectTitleMinimumFontSize = 9\.25f/);
});

test('phase 10 print compact restores stronger closing matter and reference-green treatment', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(metrics, /ClosingVisionBodyFontSize = 10\.6f/);
  assert.match(metrics, /ClosingVisionHeadingFontSize = 11\.2f/);
  assert.match(metrics, /ClosingNewSimulatorsFontSize = 8\.8f/);
  assert.match(metrics, /ClosingStraplineFontSize = 8\.2f/);
  assert.match(printRenderer, /Forest800 = "#156656"/);
  assert.match(printRenderer, /Text\("CONTACTS"\)/);
});

test('phase 11 locks 16:9 print imagery and nine-point normal typography', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(metrics, /SingleImageAspectRatio = 16f \/ 9f/);
  assert.match(metrics, /GalleryImageAspectRatio = 16f \/ 9f/);
  assert.match(metrics, /BrochurePrintLayoutVariant\.Balanced[\s\S]{0,420}BodyFontSize: ProjectBodyPreferredFontSize/);
  assert.match(metrics, /ImageWidthPoints: 154f \+ Math\.Clamp\(imageAdjustment/);
  assert.match(metrics, /ImageWidthPoints: 148f \+ Math\.Clamp\(imageAdjustment/);
  assert.match(metrics, /ResidualTargetUtilization = \.95f/);
});

test('phase 11 print planner protects typography before page count and expands residual imagery', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  assert.match(planner, /pageCount < existing\.PageCount/);
  assert.match(planner, /ApplyResidualImageExpansion/);
  assert.match(planner, /ResidualImageExpansionStepPoints/);
  assert.match(planner, /ResidualMaximumImageExpansionPoints/);
  assert.match(planner, /ProjectBodyPreferredFontSize/);
});

test('phase 11 float splitter prefers editorial boundaries before word fallback', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(measurement, /BuildEditorialBoundaries/);
  assert.match(measurement, /EditorialBoundaryKind\.Paragraph/);
  assert.match(measurement, /EditorialBoundaryKind\.Sentence/);
  assert.match(measurement, /EditorialBoundaryKind\.Word/);
  assert.match(measurement, /FloatBoundaryToleranceLines/);
});

test('phase 12 Cover A uses a dedicated CONTACTS row and asymmetric agency columns', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(printRenderer, /FrontContactBadgeHeightPoints/);
  assert.match(printRenderer, /FrontContactDevelopingFraction/);
  assert.match(printRenderer, /FrontContactManufacturingFraction/);
  assert.match(printRenderer, /Text\("CONTACTS"\)/);
  assert.match(metrics, /FrontContactDevelopingFraction = \.61f/);
  const imageComposer = printRenderer.slice(
    printRenderer.indexOf('private static void ComposeImage'),
    printRenderer.length);
  assert.doesNotMatch(imageComposer, /\.Padding\(1\)/);
  assert.match(imageComposer, /\.FitArea\(\)/);
});

test('phase 12 ships selectable institutional Cover A artwork including the approved reference', () => {
  assert.match(view, /data-brochure-institutional-artwork-panel/);
  assert.match(view, /Reference Original/);
  assert.match(view, /Premium Green–Gold/);
  assert.match(view, /Cinematic Cyber/);
  assert.match(view, /Executive Teal/);
  assert.match(view, /Luminous Halo/);
  assert.match(js, /data-artwork-option/);
  assert.match(renderer, /TryLoadInstitutionalArtwork/);
  assert.match(renderer, /cover-a-reference-original\.jpg/);
});

test('phase 12 treats 8.5 pt compact layout as single-project emergency only', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  assert.match(planner, /CandidatesForSegment\(itemCount, physicalCapacity\)/);
  assert.match(planner, /if \(itemCount == 1 && requiresEmergencyCompact\)/);
  assert.match(planner, /yield return Compact/);
});

test('phase 12 removes forced mid-sentence justification and uses residual breathing room', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  assert.match(measurement, /ContinuationNarrative/);
  assert.match(printRenderer, /layout\.ContinuationNarrative/);
  assert.match(planner, /ResidualMaximumExtraModuleVerticalPaddingPoints/);
  assert.match(planner, /ResidualMaximumExtraInterModuleSpacingPoints/);
  assert.doesNotMatch(printRenderer, /ClosingStraplineFontSize/);
});


test('phase 13 planner and renderer consume the same fixed project height contract', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');

  assert.match(printRenderer, /\.Height\(plannedProject\.Measurement\.TotalHeightPoints \+ sheet\.ExtraModuleVerticalPaddingPoints\)/);
  assert.doesNotMatch(printRenderer, /\.MinHeight\(plannedProject\.Measurement\.TotalHeightPoints/);
  assert.match(measurement, /ProjectMeasurementSafetyPoints/);
  assert.match(metrics, /ProjectMeasurementSafetyPoints = 1\.0f/);
  assert.match(planner, /WorstResidualFraction/);
  assert.match(planner, /pageCount < existing\.PageCount/);
});

test('phase 13 locks nine-point normal copy, compact paragraph rhythm and reference image cap', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(metrics, /ProjectBodyPreferredFontSize = 9f/);
  assert.match(metrics, /ProjectParagraphSpacingPoints = 2\.25f/);
  assert.match(metrics, /ResidualMaximumImageWidthPoints = 160f/);
  assert.match(metrics, /ResidualMaximumImageExpansionPoints = 12f/);
  assert.match(measurement, /Math\.Max\(\s*18f,/);
});

test('phase 13 float contract records semantic split kind and renders forced continuations without justification', () => {
  const contracts = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureContracts.cs'), 'utf8');
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(contracts, /BrochureFloatSplitKind/);
  assert.match(contracts, /FloatSplitKind/);
  assert.match(measurement, /BrochureFloatSplitKind\.Paragraph/);
  assert.match(measurement, /BrochureFloatSplitKind\.Sentence/);
  assert.match(measurement, /BrochureFloatSplitKind\.Word/);
  assert.match(printRenderer, /layout\.RemainderGapPoints/);
  const continuation = printRenderer.slice(printRenderer.indexOf('var hasContinuation'), printRenderer.indexOf('if (!string.IsNullOrWhiteSpace(layout.TrailingNarrative))'));
  assert.doesNotMatch(continuation, /\.Justify\(\)/);
});

test('phase 13 generated institutional alternatives overlay exact PRISM identity assets', () => {
  assert.match(printRenderer, /ComposeOfficialInstitutionalMarks/);
  assert.match(printRenderer, /InstitutionalCoverArtwork != BrochureInstitutionalCoverArtwork\.ReferenceOriginal/);
  assert.match(printRenderer, /Image\(artracLogo\)\.FitArea\(\)/);
  assert.match(printRenderer, /Image\(sddLogo\)\.FitArea\(\)/);
});


test('phase 13 project and closing spacers are explicit so planner and PDF have identical physical geometry', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  const sheetComposer = printRenderer.slice(
    printRenderer.indexOf('private static void ComposeProjectSheet'),
    printRenderer.indexOf('private static void ComposeProjectModule'));

  assert.doesNotMatch(sheetComposer, /column\.Spacing\(BrochurePrintLayoutMetrics\.InterModuleSpacingPoints/);
  assert.match(sheetComposer, /if \(projectOffset > 0\)/);
  assert.match(sheetComposer, /InterModuleSpacingPoints[\s\S]{0,120}ExtraInterModuleSpacingPoints/);
  assert.match(sheetComposer, /Height\(BrochurePrintLayoutMetrics\.ClosingGapPoints\)/);
  assert.match(measurement, /newSimulatorInnerWidth = outerWidth - 16f/);
  assert.match(printRenderer, /PaddingTop\(1\)\.PaddingBottom\(2\)\.Column/);
});
