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
  assert.match(js, /Fix image/);
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
  assert.match(view, /Approve for publication/);
  assert.match(view, /data-cover-hero-approve/);
  assert.doesNotMatch(view, /Use this image/);
  assert.match(js, /allReviewed/);
  assert.match(js, /const coverReady = isCurrentCoverApproved\(\)/);
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
  assert.match(renderer, /AlignBottom\(\)[\s\S]{0,220}PaddingBottom\(88\)[\s\S]{0,180}Height\(410\)/);
  assert.match(renderer, /AlignBottom\(\)[\s\S]{0,120}Height\(88\)[\s\S]{0,140}Background\("#082A26"\)/);
  const contemporary = renderer.slice(renderer.indexOf('private static void ComposeContemporaryCover'), renderer.indexOf('private static void ComposeDigitalInstitutionalOpening'));
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
  assert.match(planner, /requireClosingOnFinalPage/);
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
  assert.match(js, /Restore all institutional publication text/);
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
  assert.match(metrics, /ProjectBodyMinimumFontSize = 9f/);
  assert.match(metrics, /ProjectTitlePreferredFontSize = 10f/);
  assert.match(metrics, /ProjectTitleMinimumFontSize = 9\.25f/);
});

test('phase 16 keeps compact closing matter as a measured two-part institutional module', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(metrics, /ClosingVisionBodyFontSize = 9\.8f/);
  assert.match(metrics, /ClosingVisionHeadingFontSize = 10\.6f/);
  assert.match(metrics, /ClosingSectionSpacingPoints = 5f/);
  assert.match(printRenderer, /Visionary Horizons & Strategic Objectives/);
  assert.match(printRenderer, /New Simulators\./);
  assert.match(printRenderer, /ComposeClosingMatter/);
});

test('phase 14 keeps exact 16:9 imagery and nine-point typography across all normal density candidates', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(metrics, /SingleImageAspectRatio = 16f \/ 9f/);
  assert.match(metrics, /GalleryImageAspectRatio = 16f \/ 9f/);
  assert.match(metrics, /ProjectBodyPreferredFontSize = 9f/);
  assert.match(metrics, /BrochurePrintLayoutVariant\.Dense[\s\S]{0,420}BodyFontSize: ProjectBodyPreferredFontSize/);
  assert.match(metrics, /DenseImageWidths = \{ 144f, 140f, 136f, 132f \}/);
  assert.match(metrics, /AdaptiveImageMinimumPoints = 132f/);
  assert.match(metrics, /AdaptiveImageMaximumPoints = 156f/);
});

test('phase 14 generates a bounded Pareto frontier instead of three fixed print templates', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(measurement, /GenerateProjectCandidates/);
  assert.match(measurement, /ParetoFilter/);
  assert.match(measurement, /AddDensityCandidates/);
  assert.match(metrics, /MaximumParetoCandidatesPerProject = 6/);
  assert.match(metrics, /CandidateDominanceHeightTolerancePoints/);
  assert.match(metrics, /CandidateDominanceQualityTolerance/);
});

test('phase 14 Automatic image mode is truly planner-aware while explicit Gallery 2 remains mandatory', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(measurement, /BrochureImageMode\.Single => new\[\] \{ false \}/);
  assert.match(measurement, /BrochureImageMode\.GalleryTwo => new\[\] \{ item\.HasSecondaryPhoto \}/);
  assert.match(measurement, /_ when item\.HasSecondaryPhoto => new\[\] \{ false, true \}/);
  assert.match(printRenderer, /layout\.UsesSecondaryImage[\s\S]{0,80}project\.SecondaryPhoto is not null/);
  assert.match(js, /print planner use one or two selected images/);
});

test('phase 14 preserves current editorial order and exposes Smart Flow only as an explicit suggestion', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  const contracts = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureContracts.cs'), 'utf8');
  assert.match(planner, /PlanWithSmartFlow/);
  assert.match(planner, /originalOrder = Enumerable\.Range/);
  assert.match(planner, /SmartFlowMaximumMoveDistance/);
  assert.match(planner, /FindSmartFlowSuggestion/);
  assert.match(contracts, /BrochurePrintFlowSuggestion/);
  assert.match(contracts, /BrochurePrintOrderMove/);
  assert.match(view, /data-smart-flow/);
  assert.match(view, /Apply suggested order/);
  assert.match(js, /applySmartFlow/);
  assert.match(js, /undoSmartFlow/);
  assert.match(js, /smartFlowUndoOrder/);
  assert.match(view, /data-smart-flow-treatment/);
  assert.match(view, /data-smart-flow-sheet-map/);
  assert.match(js, /adaptiveTreatmentSummary/);
  assert.match(js, /suggestedSheetPlan/);
});

test('phase 14 optimizer makes page count dominant then balances fill, visual quality and edit distance', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  assert.match(planner, /plan\.EstimatedTotalPageCount \* 1_000_000d/);
  assert.match(planner, /TotalPositionShift/);
  assert.match(planner, /SmartFlowBeamWidth/);
  assert.match(planner, /SmartFlowMaximumBoundaryMovesPerState/);
  assert.match(planner, /IsMaterialImprovement/);
});

test('phase 14 keeps residual handling as a final polish pass and never changes planned image width', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  assert.match(planner, /ApplyResidualPolish/);
  assert.match(metrics, /ResidualMaximumExtraModuleVerticalPaddingPoints/);
  assert.match(metrics, /ResidualMaximumExtraInterModuleSpacingPoints/);
  assert.doesNotMatch(planner, /ResidualImageExpansionStepPoints/);
  assert.doesNotMatch(planner, /ResidualMaximumImageExpansionPoints/);
});

test('phase 14 retains semantic float splitting and avoids forced word-continuation justification', () => {
  const contracts = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureContracts.cs'), 'utf8');
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(contracts, /BrochureFloatSplitKind/);
  assert.match(measurement, /BuildEditorialBoundaries/);
  assert.match(measurement, /BrochureFloatSplitKind\.Paragraph/);
  assert.match(measurement, /BrochureFloatSplitKind\.Sentence/);
  assert.match(measurement, /BrochureFloatSplitKind\.Word/);
  const continuation = printRenderer.slice(printRenderer.indexOf('var hasContinuation'), printRenderer.indexOf('if (!string.IsNullOrWhiteSpace(layout.TrailingNarrative))'));
  assert.doesNotMatch(continuation, /\.Justify\(\)/);
});

test('Cover A treats every approved artwork as identity-complete and never overlays duplicate logos', () => {
  const catalog = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureInstitutionalCoverArtworkCatalog.cs'), 'utf8');
  for (const artwork of ['ReferenceOriginal', 'PremiumGreenGold', 'CinematicCyber', 'ExecutiveTeal', 'LuminousHalo']) {
    assert.match(catalog, new RegExp(`${artwork}[\\s\\S]{0,130}BrochureInstitutionalArtworkIdentityMode\\.FullArtwork`));
  }
  assert.doesNotMatch(catalog, /BrochureInstitutionalArtworkIdentityMode\.BackgroundOnly/);
  assert.doesNotMatch(printRenderer, /ComposeOfficialInstitutionalMarks/);
  assert.match(renderer, /artworkContainsIdentity/);
  assert.match(renderer, /if \(!artworkContainsIdentity\)/);
});

test('phase 14 preflight returns actionable Smart Flow diagnostics instead of silently reordering', () => {
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  const page = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml.cs'), 'utf8');
  assert.match(service, /PlanWithSmartFlow/);
  assert.match(service, /PrintSmartFlowAvailable/);
  assert.match(service, /SmartFlowSuggestion = plan\.SmartFlowSuggestion/);
  assert.match(page, /smartFlowSuggestion/);
  assert.match(page, /suggestedProjectIds/);
  assert.match(page, /moves =/);
});

test('phase 14 project and closing geometry remains deterministic between planner and QuestPDF', () => {
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  const sheetComposer = printRenderer.slice(
    printRenderer.indexOf('private static void ComposeProjectSheet'),
    printRenderer.indexOf('private static void ComposeProjectModule'));
  assert.match(printRenderer, /\.Height\(plannedProject\.Measurement\.TotalHeightPoints \+ sheet\.ExtraModuleVerticalPaddingPoints\)/);
  assert.match(printRenderer, /\.ShowEntire\(\)\s*\.Height\(plannedProject\.Measurement\.TotalHeightPoints \+ sheet\.ExtraModuleVerticalPaddingPoints\)/);
  assert.match(sheetComposer, /InterModuleSpacingPoints[\s\S]{0,120}ExtraInterModuleSpacingPoints/);
  assert.match(sheetComposer, /Height\(BrochurePrintLayoutMetrics\.ClosingGapPoints\)/);
  assert.match(measurement, /ProjectMeasurementSafetyPoints/);
});

test('print pagination packs forward, exempts the final sheet, and guards narrative integrity', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(planner, /ForwardResidualFractions/);
  assert.match(planner, /CompareResidualVectors/);
  assert.match(planner, /allowRaggedResidual/);
  assert.match(planner, /isFinalPage[\s\S]{0,180}page\.Projects\.Count/);
  assert.match(planner, /minimumRemainingHeight/);
  assert.match(planner, /nonFinalProjectPages\.Average\(page => page\.UtilizationPercent\)/);
  assert.match(metrics, /PreferredMaximumProjectsPerSheet = 4/);
  assert.match(metrics, /MaximumProjectsPerSheet = 5/);
  assert.match(measurement, /EnsureNarrativePartition/);
  assert.match(js, /sheet\.isFinal/);
  assert.match(view, /Lowest page fill/);
  assert.match(view, /Average page fill/);
});

test('publication approvals are bound to authoritative project and cover fingerprints', () => {
  const contracts = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureContracts.cs'), 'utf8');
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  const page = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml.cs'), 'utf8');
  const fingerprint = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureReviewFingerprint.cs'), 'utf8');

  assert.match(contracts, /ReviewFingerprint/);
  assert.match(contracts, /ProjectReviewStale/);
  assert.match(contracts, /CoverReviewStale/);
  assert.match(service, /ApplyReviewApprovalValidation/);
  assert.match(service, /RequirePublicationReview/);
  assert.match(service, /SubmittedReviewFingerprint/);
  assert.match(fingerprint, /IncrementalHash\.CreateHash\(HashAlgorithmName\.SHA256\)/);
  assert.match(fingerprint, /FixedTimeEquals/);
  assert.match(page, /RequirePublicationReview:\s*!preview/);
  assert.match(view, /data-brochure-cover-review-fingerprint/);
  assert.match(js, /currentProjectReviewFingerprints/);
  assert.match(js, /config\.reviewFingerprint = fingerprint/);
  assert.match(js, /coverReviewFingerprint = fingerprint/);
});

test('compact and digital Cover A renderers use the same identity-mode contract', () => {
  assert.match(printRenderer, /artworkContainsIdentity/);
  assert.match(printRenderer, /BrochureInstitutionalCoverArtworkCatalog\.IdentityMode/);
  assert.match(printRenderer, /if \(!artworkContainsIdentity\)/);
  assert.match(renderer, /BrochureInstitutionalCoverArtworkCatalog\.IdentityMode/);
});

test('preflight has one findings scroll owner and reports review completion accurately', () => {
  assert.match(css, /\.brochure-preflight-panel \.brochure-preflight-issues \{ max-height: none; overflow: visible;/);
  assert.match(js, /All approvals are complete; review the warnings before final issue/);
  assert.match(js, /Publication preflight and all required approvals are complete/);
  assert.match(js, /Approve the Cover B hero and crop before final issue/);
  assert.match(js, /name\.title = name\.textContent/);
});

test('phase 15 keeps final output permanently separate from preflight and moves image editing into a modal workspace', () => {
  assert.match(view, /<h2>Publication readiness<\/h2>/);
  assert.match(view, /<h2>Final output<\/h2>/);
  assert.match(view, /data-output-readiness/);
  assert.match(view, /data-brochure-photo-editor[\s\S]{0,220}role="dialog"|role="dialog"[\s\S]{0,220}data-brochure-photo-editor/);
  assert.match(view, /data-photo-editor-dismiss/);
  assert.match(css, /\.brochure-sidebar \{[\s\S]{0,280}grid-template-rows:\s*minmax\(0,\s*1fr\)\s+auto;/);
  assert.match(css, /\.brochure-preflight-panel > \.publication-panel__body,[\s\S]{0,180}max-height:\s*none;/);
  assert.match(css, /\.brochure-photo-editor \{[\s\S]{0,120}position:\s*fixed;/);
  assert.match(js, /photoEditorCloseButtons\.forEach/);
  assert.match(js, /document\.body\.classList\.add\("brochure-modal-open"\)/);
  assert.match(js, /setOutputReadiness/);
});

test('phase 15 explicitly reports approval invalidation after editorial image changes', () => {
  assert.match(view, /data-review-notice/);
  assert.match(js, /showReviewNotice/);
  assert.match(js, /Primary image changed/);
  assert.match(js, /Image crop changed/);
  assert.match(js, /publication approval reset/);
});


test('phase 16 post-compose verification makes the rendered PDF the final pagination authority', () => {
  const builder = fs.readFileSync(path.join(root, 'Utilities', 'Reporting', 'BrochurePdfReportBuilder.cs'), 'utf8');
  const verifier = fs.readFileSync(path.join(root, 'Utilities', 'Reporting', 'BrochurePdfCompositionVerifier.cs'), 'utf8');
  const page = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml.cs'), 'utf8');
  assert.match(builder, /BrochurePdfCompositionVerifier\.Verify\(pdfBytes, data, printPlan\)/);
  assert.match(verifier, /PdfDocument\.Open/);
  assert.match(verifier, /ExpectedPageCount/);
  assert.match(verifier, /ActualPageCount/);
  assert.match(verifier, /private static string Canonical\(/);
  assert.match(verifier, /Visionary Horizons & Strategic Objectives/);
  assert.match(page, /compositionMismatch/);
  assert.match(page, /X-PRISM-Publication-Composition-Verified/);
  assert.match(page, /X-PRISM-Publication-Page-Count/);
});

test('phase 16 gives compact planning a physical compositor reserve and keeps closing measurement geometry shared', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  assert.match(metrics, /ProjectComposerSafetyReservePoints = 12f/);
  assert.match(metrics, /ReferenceHeightPoints[\s\S]{0,900}ProjectComposerSafetyReservePoints/);
  assert.match(measurement, /ClosingVisionHorizontalPaddingPoints/);
  assert.match(measurement, /ClosingVisionVerticalPaddingPoints/);
  assert.match(measurement, /ClosingVisionHeadingHorizontalPaddingPoints/);
  assert.match(measurement, /ClosingSectionSpacingPoints/);
});

test('phase 16 makes project copy ragged-right while retaining measured semantic float composition', () => {
  const projectModule = printRenderer.slice(
    printRenderer.indexOf('private static void ComposeProjectModule'),
    printRenderer.indexOf('private static void ComposeClosingMatter'));
  assert.match(projectModule, /justify: false/);
  assert.doesNotMatch(projectModule, /justify: true/);
});

test('phase 16 explains Cover A identity ownership and hides non-rendered identity controls without dropping their values', () => {
  assert.match(view, /data-cover-a-identity-field/);
  assert.match(view, /data-cover-a-identity-note/);
  assert.match(view, /approved institutional artwork unchanged in Print \/ Compact/);
  assert.match(js, /coverAUsesFullArtworkIdentity/);
  assert.match(js, /field\.hidden = coverAUsesFullArtworkIdentity/);
  assert.match(js, /coverAIdentityNote\.hidden = !coverAUsesFullArtworkIdentity/);
});

test('phase 16 simplifies publication order and protects clearing reviewed selections', () => {
  assert.match(view, />Clear selection</);
  assert.match(js, /dataset\.reorderButton/);
  assert.match(js, /window\.confirm/);
  assert.match(css, /\.brochure-selected-item__name[\s\S]{0,220}-webkit-line-clamp:\s*2/);
  assert.match(css, /\[data-reorder-button\]/);
  assert.match(css, /:focus-within/);
});

test('phase 16 presents review actions as publication image work versus authoritative source work', () => {
  assert.match(view, /<span>Publication image<\/span>/);
  assert.match(view, /<span>Authoritative source<\/span>/);
  assert.match(view, />Project brief</);
  assert.match(view, />Project photos</);
  assert.match(css, /\.brochure-review-action-group/);
});

test('phase 16 output success reads physical composition verification and page count', () => {
  assert.match(js, /X-PRISM-Publication-Composition-Verified/);
  assert.match(js, /X-PRISM-Publication-Page-Count/);
  assert.match(js, /physicalPageCount/);
});


test('phase 17 uses page terminology throughout the visible preflight experience', () => {
  const publicationService = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintPagePlanner.cs'), 'utf8');
  assert.match(view, /<span>Planned pages<\/span>/);
  assert.match(view, /<span>Final page composition<\/span>/);
  assert.match(view, /<div><span>Pages<\/span><strong data-smart-flow-pages>/);
  assert.doesNotMatch(view, />Planned sheets</i);
  assert.doesNotMatch(view, />Final-sheet composition</i);
  assert.match(js, /Dedicated closing page/);
  assert.match(planner, /-page composition is available at 9 pt/);
  assert.match(publicationService, /dedicated closing page/);
  assert.match(publicationService, /non-final hard-copy project page/);
});

test('phase 17 keeps post-compose verification persistently visible until publication geometry changes', () => {
  assert.match(view, /data-output-verification/);
  assert.match(view, /data-output-verification-text/);
  assert.match(css, /\.brochure-output-verification/);
  assert.match(js, /let lastVerifiedPdf = null/);
  assert.match(js, /PDF verified · \$\{pages\} page/);
  assert.match(js, /lastVerifiedPdf = \{ verified: true, pageCount: physicalPageCount \}/);
  assert.match(js, /const schedulePreflight = \(\) => \{[\s\S]{0,520}lastVerifiedPdf = null/);
});

test('phase 17 findings provide direct Fix image actions instead of locate-plus-configure indirection', () => {
  assert.match(js, /fixImage\.textContent = "Fix image"/);
  assert.match(js, /openPhotoEditor\(projectId, "select"\)/);
  assert.match(js, /link\.textContent = project\.photos\?\.length \? "Photos" : "Add photo"/);
  assert.match(js, /show\.textContent = "Show project"/);
  assert.doesNotMatch(js, /locate\.textContent = "Locate"/);
  assert.doesNotMatch(js, /configure\.textContent = "Configure image"/);
});

test('phase 17 evaluates image warnings by effective cropped dpi at physical render size', () => {
  const evaluator = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePhotoPrintQualityEvaluator.cs'), 'utf8');
  const publicationService = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  assert.match(evaluator, /EffectiveCropDimensions/);
  assert.match(evaluator, /EffectiveDpi/);
  assert.match(evaluator, /PrintCompactRecommendedDpi = 240d/);
  assert.match(evaluator, /AdaptiveImageMaximumPoints/);
  assert.match(publicationService, /BrochurePhotoPrintQualityEvaluator\.Assess/);
  assert.match(publicationService, /effective dpi/);
  assert.doesNotMatch(publicationService.slice(publicationService.indexOf('private static void AddQualityFinding'), publicationService.indexOf('private enum PhotoPlacement')), /minimumWidth|minimumHeight/);
});

test('phase 17 makes 9 pt a hard compact-project body-copy floor', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  const contracts = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureContracts.cs'), 'utf8');
  assert.match(metrics, /ProjectBodyPreferredFontSize = 9f/);
  assert.match(metrics, /ProjectBodyMinimumFontSize = 9f/);
  assert.match(metrics, /BrochurePrintLayoutVariant\.Compact[\s\S]{0,220}BodyFontSize: ProjectBodyMinimumFontSize/);
  assert.match(contracts, /body copy remains 9 pt/);
});

test('phase 17 approval status handles singular and plural grammar', () => {
  const page = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml.cs'), 'utf8');
  assert.match(js, /pendingApprovals === 1 \? "requires" : "require"/);
  assert.match(page, /unreviewed == 1 \? "requires" : "require"/);
});

test('phase 18 replaces the brochure hero chrome with a compact shared-publication workspace header', () => {
  assert.match(view, /brochure-workspace-header/);
  assert.match(view, /data-brochure-preset-control/);
  assert.match(view, /Compose, review and generate capability publications from authoritative PRISM project records\./);
  assert.doesNotMatch(view, /CAPABILITY PUBLICATION/);
  assert.doesNotMatch(view, />\s*Offline PDF\s*</i);
  assert.match(css, /\.brochure-workspace-header/);
  assert.match(css, /\.brochure-preset-control/);
});

test('phase 18 exposes shared saved brochures to all authorised users while reserving mutations for HoD and Comdt', () => {
  const page = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml.cs'), 'utf8');
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePresetService.cs'), 'utf8');
  assert.match(view, /data-preset-select/);
  assert.match(view, /@foreach \(var preset in Model\.SavedBrochures\)/);
  assert.match(view, /@if \(Model\.CanManageSavedBrochures\)/);
  assert.match(page, /User\.IsInRole\(RoleNames\.HoD\) \|\| User\.IsInRole\(RoleNames\.Comdt\)/);
  assert.match(service, /!user\.IsInRole\(RoleNames\.HoD\) && !user\.IsInRole\(RoleNames\.Comdt\)/);
  assert.doesNotMatch(service.slice(service.indexOf('private void EnsureCanManage'), service.indexOf('private static void EnsureVersion')), /RoleNames\.Admin/);
});

test('phase 18 persists builder configuration but deliberately excludes approvals, preflight and PDF verification', () => {
  const contracts = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePresetContracts.cs'), 'utf8');
  const page = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml.cs'), 'utf8');
  assert.match(contracts, /record BrochurePresetConfiguration/);
  assert.match(contracts, /record BrochurePresetProjectConfiguration/);
  assert.doesNotMatch(contracts, /CoverReviewed|ReviewFingerprint|IsReviewed|Preflight|VerifiedPdf/);
  assert.match(page, /CoverReviewed = false/);
  assert.match(page, /PrimaryPhotoConfirmed = false/);
  assert.match(page, /IsReviewed = false/);
  assert.match(page, /ReviewFingerprint = null/);
  assert.match(js, /durablePresetFieldNames/);
  const snapshot = js.slice(js.indexOf('const capturePresetSnapshot'), js.indexOf('const formatPresetDate'));
  assert.doesNotMatch(snapshot, /reviewFingerprint|isReviewed|primaryPhotoConfirmed|lastPreflight|lastVerifiedPdf/);
});

test('phase 18 supports multiple shared presets, exact ordered project configuration and optimistic concurrency', () => {
  const model = fs.readFileSync(path.join(root, 'Models', 'Publications', 'BrochurePreset.cs'), 'utf8');
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePresetService.cs'), 'utf8');
  const db = fs.readFileSync(path.join(root, 'Data', 'ApplicationDbContext.cs'), 'utf8');
  assert.match(model, /ICollection<BrochurePresetProject> Projects/);
  assert.match(model, /SortOrder/);
  assert.match(model, /PrimaryPhotoId/);
  assert.match(model, /SecondaryPhotoId/);
  assert.match(model, /PrimaryFocalX/);
  assert.match(model, /ImageMode/);
  assert.match(model, /\[ConcurrencyCheck\][\s\S]{0,100}RowVersion/);
  assert.match(db, /UX_BrochurePresets_NormalizedName/);
  assert.match(db, /UX_BrochurePresetProjects_Preset_SortOrder/);
  assert.match(service, /EnsureVersion\(preset, rowVersion\)/);
  assert.match(service, /BrochurePresetConcurrencyException/);
});

test('phase 18 rehydrates saved brochures against current PRISM projects and photos with safe diagnostics', () => {
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePresetService.cs'), 'utf8');
  assert.match(service, /projectUnavailable/);
  assert.match(service, /photoUnavailable/);
  assert.match(service, /coverHeroUnavailable/);
  assert.match(service, /!project\.IsDeleted/);
  assert.match(service, /!project\.IsArchived/);
  assert.match(service, /PRISM will resolve a current image automatically/);
});

test('phase 18 distinguishes shared preset dirty state from local working-copy changes', () => {
  assert.match(js, /presetDirtyState\.textContent = canManagePresets \? "Modified" : "Modified locally"/);
  assert.match(js, /capturePresetSnapshot/);
  assert.match(js, /presetBaselineSnapshot/);
  assert.match(js, /markPresetClean/);
  assert.match(js, /beforeunload/);
  assert.match(js, /Your current brochure has local changes/);
});

test('phase 18 provides save-as-new, update, rename, duplicate, soft-delete and conflict workflows', () => {
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePresetService.cs'), 'utf8');
  assert.match(view, /data-preset-save-as-new/);
  assert.match(view, /data-preset-save-changes/);
  assert.match(view, /data-preset-rename/);
  assert.match(view, /data-preset-duplicate/);
  assert.match(view, /data-preset-delete/);
  assert.match(view, /data-preset-conflict-modal/);
  assert.match(js, /saveAsNew/);
  assert.match(js, /presetConflict/);
  assert.match(service, /preset\.IsActive = false/);
  assert.match(service, /#DELETED#/);
});

test('phase 18 registers shared preset persistence and includes a dedicated additive migration', () => {
  const registration = fs.readFileSync(path.join(root, 'Services', 'Publications', 'PublicationServiceCollectionExtensions.cs'), 'utf8');
  const migration = fs.readFileSync(path.join(root, 'Migrations', '20261208100000_AddSharedBrochurePresets.cs'), 'utf8');
  const immutable = fs.readFileSync(path.join(root, 'Migrations', 'immutable-migration-ids.txt'), 'utf8');
  assert.match(registration, /AddScoped<IBrochurePresetService, BrochurePresetService>/);
  assert.match(migration, /CreateTable\([\s\S]*name: "BrochurePresets"/);
  assert.match(migration, /name: "BrochurePresetProjects"/);
  assert.match(migration, /FK_BrochurePresetProjects_Projects_ProjectId/);
  assert.match(immutable, /20261208100000_AddSharedBrochurePresets/);
});


test('phase 19 gives Smart Flow distinct opportunity and applied states', () => {
  assert.match(view, /data-smart-flow-title>Smart Flow opportunity</);
  assert.match(view, /data-smart-flow-note/);
  assert.match(js, /smartFlowTitle\.textContent = "Smart Flow opportunity"/);
  assert.match(js, /smartFlowTitle\.textContent = "Smart Flow applied"/);
  assert.match(js, /Only the publication sequence changed; project content and image treatment were not altered\./);
  assert.match(js, /smartFlowPanel\.classList\.toggle\("is-applied"/);
  assert.match(css, /\.brochure-smart-flow\.is-applied/);
});

test('phase 19 disables Load when there is no saved brochure to load', () => {
  assert.match(view, /data-preset-load disabled>Load<\/button>/);
  assert.match(js, /const updatePresetLoadState = \(\) =>/);
  assert.match(js, /const nothingSelected = selectedId == null/);
  assert.match(js, /presetLoad\.disabled = presetMutationBusy \|\| nothingSelected \|\| alreadyLoadedAndClean/);
  assert.match(js, /presetSelect\?\.addEventListener\("change", \(\) => \{[\s\S]{0,180}updatePresetLoadState\(\)/);
});

test('phase 19 Smart Flow participates in shared-brochure dirty-state fingerprinting and undo restores the original sequence', () => {
  const snapshot = js.slice(js.indexOf('const capturePresetSnapshot'), js.indexOf('const formatPresetDate'));
  assert.match(snapshot, /projects: orderedIds\.map/);
  assert.match(js, /smartFlowUndoOrder = \[\.\.\.orderedIds\];[\s\S]{0,100}orderedIds = suggestedIds;[\s\S]{0,100}renderSelected\(true, false\)/);
  assert.match(js, /orderedIds = \[\.\.\.smartFlowUndoOrder\];[\s\S]{0,120}renderSelected\(true, false\)/);
  assert.match(js, /const schedulePreflight = \(\) => \{[\s\S]{0,520}renderPresetDirtyState\(\)/);
});

test('phase 19 restores a modernised heritage identity to the final Visionary panel without changing its measured content box', () => {
  const metrics = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintLayoutMetrics.cs'), 'utf8');
  const measurement = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePrintMeasurementService.cs'), 'utf8');
  const closing = printRenderer.slice(printRenderer.indexOf('private static void ComposeClosingMatter'), printRenderer.indexOf('private static void ComposeImage'));
  assert.match(metrics, /ClosingVisionBorderPoints = 2f/);
  assert.match(metrics, /ClosingVisionHorizontalPaddingPoints = 8\.1f/);
  assert.match(metrics, /ClosingVisionVerticalPaddingPoints = 6\.1f/);
  assert.match(measurement, /visionInnerWidth = outerWidth[\s\S]{0,180}ClosingVisionBorderPoints[\s\S]{0,180}ClosingVisionHorizontalPaddingPoints/);
  assert.match(printRenderer, /ClosingCream = "#FBF4D8"/);
  assert.match(printRenderer, /ClosingNavy = "#173F63"/);
  assert.match(closing, /BorderColor\(ClosingNavy\)/);
  assert.match(closing, /Background\(ClosingCream\)/);
  assert.match(closing, /Background\(ClosingNavy\)/);
  assert.match(closing, /justify: false/);
  assert.doesNotMatch(closing, /\.Italic\(\)/);
  assert.match(closing, /Background\(Forest800\)/);
});

test('phase 19 saved brochure label uses sentence case', () => {
  assert.match(view, /<span>Saved brochure<\/span>/);
  const labelRule = css.slice(css.indexOf('.brochure-preset-control__label'), css.indexOf('.brochure-preset-dirty'));
  assert.match(labelRule, /text-transform:\s*none/);
});


test('phase 20 Digital Comfortable has a dedicated one-or-two-project screen-first planner', () => {
  const planner = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureLayoutPlanner.cs'), 'utf8');
  const policy = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureDigitalPublicationPolicy.cs'), 'utf8');
  assert.match(planner, /PlanDigitalComfortable/);
  assert.match(planner, /BrochurePageLayoutKind\.TwoFeature/);
  assert.match(planner, /BrochurePageLayoutKind\.SingleFeature/);
  assert.match(planner, /maximumCombinedWords = 350/);
  assert.match(policy, /ProjectPageCount/);
  assert.match(policy, /InstitutionalPageCount/);
  assert.match(policy, /EstimatedTotalPageCount/);
});

test('phase 20 Digital Comfortable adds dedicated institutional opening and closing pages', () => {
  assert.match(renderer, /ComposeDigitalInstitutionalOpening/);
  assert.match(renderer, /WHY SIMULATORS/);
  assert.match(renderer, /FUTURE-READY CAPABILITY/);
  assert.match(renderer, /ComposeDigitalInstitutionalClosing/);
  assert.match(renderer, /Future capability & engagement/);
  assert.match(renderer, /Visionary Horizons & Strategic Objectives/);
  assert.match(renderer, /PROCUREMENT \/ ENGAGEMENT/);
  assert.match(renderer, /New Simulators\./);
});

test('phase 20 Cover A removes the empty montage frame and duplicate edition while Cover B becomes image-led', () => {
  const coverA = renderer.slice(renderer.indexOf('private static void ComposeInstitutionalCover'), renderer.indexOf('private static void ComposeContemporaryCover'));
  const coverB = renderer.slice(renderer.indexOf('private static void ComposeContemporaryCover'), renderer.indexOf('private static void ComposeDigitalInstitutionalOpening'));
  assert.match(coverA, /Width\(268\)[\s\S]{0,80}Height\(268\)/);
  assert.doesNotMatch(coverA, /PaddingTop\(14\)\.Text\(data\.Options\.Edition\)/);
  assert.match(coverB, /CAPABILITY PUBLICATION · CONTEMPORARY EDITION/);
  assert.match(coverB, /Height\(410\)/);
  assert.match(coverB, /Selected PRISM project imagery/);
});

test('phase 20 institutional content is profile-aware in the builder and digital preflight has its own page map', () => {
  assert.match(view, /Institutional content<\/summary>/);
  assert.doesNotMatch(view, />Print institutional content</);
  assert.match(view, /dedicated About SDD and Future capability &amp; engagement pages/);
  assert.match(view, /data-digital-plan-summary/);
  assert.match(view, /data-digital-page-map/);
  assert.match(js, /showDigitalPlan/);
  assert.match(js, /digitalSingleFeaturePageCount/);
  assert.match(js, /brochure-digital-page-chip/);
});

test('phase 20 uses full physical-document numbering and verifies Digital PDFs after composition', () => {
  const verifier = fs.readFileSync(path.join(root, 'Utilities', 'Reporting', 'BrochurePdfCompositionVerifier.cs'), 'utf8');
  assert.match(renderer, /ConfigureInnerPage\(page, data\.Options, fonts, sddLogo, "PROJECT CAPABILITIES", pageNumber, totalPages\)/);
  assert.match(renderer, /Text\(\$"\{pageNumber\} \/ \{totalPages\}"\)/);
  assert.match(renderer, /BrochurePdfCompositionVerifier\.VerifyDigital\(pdfBytes, data, digitalPlan\)/);
  assert.match(verifier, /public static void VerifyDigital/);
  assert.match(verifier, /Digital brochure page membership changed after rendering/);
});

test('phase 20 Digital institutional content has explicit readability limits and optional additional introduction pagination', () => {
  const policy = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochureDigitalPublicationPolicy.cs'), 'utf8');
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  assert.match(policy, /InstitutionalOpeningMaximumWords = 430/);
  assert.match(policy, /InstitutionalClosingMaximumWords = 420/);
  assert.match(policy, /SplitAdditionalIntroduction/);
  assert.match(policy, /DigitalInstitutionalOpeningTooLong/);
  assert.match(policy, /DigitalInstitutionalClosingTooLong/);
  assert.match(service, /ValidateInstitutionalMatter/);
  assert.match(view, /Additional introduction \(optional\)/);
});

test('phase 20 Digital image quality follows actual feature, split and premium-cover render geometry', () => {
  const evaluator = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePhotoPrintQualityEvaluator.cs'), 'utf8');
  const service = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePublicationService.cs'), 'utf8');
  assert.match(evaluator, /ProjectEditorialSplit = 4/);
  assert.match(evaluator, /DigitalEditorialSplitMaximumWidthPoints = 225d/);
  assert.match(evaluator, /DigitalCoverHeroWidthPoints = 543\.276d/);
  assert.match(evaluator, /\? 1055d[\s\S]{0,40}: 1360d/);
  assert.match(service, /BuildDigitalPhotoPlacements/);
  assert.match(service, /PhotoPlacement\.EditorialSplit/);
  assert.match(js, /1800 \/ \(isPrintCompactProfile\(\) \? 1055 : 1360\)/);
});


test('phase 20.2 Cover B approval is bound to the exact current server preflight fingerprint', () => {
  assert.match(js, /const currentCoverReviewFingerprint = \(\) =>/);
  assert.match(js, /const isCurrentCoverApproved = \(\) =>/);
  assert.match(js, /coverReviewFingerprint === serverFingerprint/);
  assert.match(js, /const coverReady = isCurrentCoverApproved\(\)/);
  assert.match(js, /isContemporaryCover\(\) && !isCurrentCoverApproved\(\)/);
  assert.doesNotMatch(js, /const coverReady = !isContemporaryCover\(\) \|\| Boolean\(coverReviewed && coverReviewFingerprint\)/);
});

test('phase 20.2 preflight invalidates stale Cover B state immediately and rejects superseded responses', () => {
  assert.match(js, /let preflightRevision = 0/);
  assert.match(js, /let preflightPending = false/);
  assert.match(js, /preflightAbort\?\.abort\(\);[\s\S]{0,180}preflightRevision \+= 1/);
  assert.match(js, /const runPreflight = async revision =>/);
  assert.match(js, /if \(revision !== preflightRevision\) return/);
  assert.match(js, /Checking selected narratives, cover state and publication source images/);
  assert.match(js, /Checking cover…/);
});

test('phase 20.2 approving an automatic Cover B hero does not silently convert it to an explicit saved hero', () => {
  const approve = js.slice(
    js.indexOf('coverHeroApprove?.addEventListener'),
    js.indexOf('coverHeroFocalStage?.addEventListener')
  );
  assert.match(approve, /const hero = resolvedCoverHero\(\)/);
  assert.match(approve, /const fingerprint = currentCoverReviewFingerprint\(\)/);
  assert.doesNotMatch(approve, /ensureExplicitCoverHero\(\)/);
  assert.match(approve, /updateButtons\(Boolean\(lastPreflight\?\.canGenerate\)\)/);
});

test('phase 20.2 final Cover B generation performs an independent current-approval guard', () => {
  const requestStart = js.indexOf('const requestPdf = async preview =>');
  const requestEnd = js.indexOf('rows.forEach(row =>', requestStart);
  const request = js.slice(requestStart, requestEnd);
  assert.match(request, /!preview && isContemporaryCover\(\) && !isCurrentCoverApproved\(\)/);
  assert.match(request, /Cover B is being rechecked/);
  assert.match(request, /Approve the current Cover B hero and crop before final download/);
});

test('phase 20.2 generation errors carry structured publication issue codes and recover stale Cover B state', () => {
  const pageModel = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml.cs'), 'utf8');
  assert.match(pageModel, /code = "publicationStateChanged"/);
  assert.match(pageModel, /issues = blockerIssues\.Select/);
  assert.match(pageModel, /code = issue\.Code\.ToString\(\)/);
  assert.match(js, /const publicationErrorFromResponse = async response =>/);
  assert.match(js, /hasCoverApprovalIssue/);
  assert.match(js, /CoverReviewRequired/);
  assert.match(js, /CoverReviewStale/);
  assert.match(js, /schedulePreflight\(\)/);
});

test('phase 20.2 separates Cover B editorial approval from technical image quality', () => {
  assert.match(view, /data-cover-hero-quality-state/);
  assert.match(css, /\.brochure-cover-quality-state\.is-low/);
  assert.match(css, /\.brochure-cover-quality-state\.is-good/);
  assert.match(js, /Approved for cover/);
  assert.match(js, /coverHeroApprove\.hidden = coverIsApproved/);
  assert.match(js, /Image quality ·/);
  assert.match(js, /resolvedCoverHeroQuality/);
});

test('phase 20.2 digital structural settings trigger fresh preflight and Digital verification is surfaced', () => {
  assert.match(view, /asp-for="Input\.IncludeBackCover"[^>]*data-brochure-preflight-trigger/);
  assert.match(view, /asp-for="Input\.IntroductionTitle"[^>]*data-brochure-preflight-trigger/);
  assert.match(view, /asp-for="Input\.IntroductionText"[^>]*data-brochure-preflight-trigger/);
  const verification = js.slice(js.indexOf('const renderPdfVerification'), js.indexOf('const updateButtons'));
  assert.doesNotMatch(verification, /isPrintCompactProfile\(\)/);
});

test('phase 20.2 publication photo rendering treats ImageSharp processing failures as a single-photo failure instead of a request crash', () => {
  const photoService = fs.readFileSync(path.join(root, 'Services', 'Publications', 'BrochurePhotoService.cs'), 'utf8');
  assert.match(photoService, /IsRecoverableImageException/);
  assert.match(photoService, /ArgumentException/);
  assert.match(photoService, /NotSupportedException/);
  assert.match(photoService, /StartsWith\("SixLabors\.ImageSharp\."/);
});
