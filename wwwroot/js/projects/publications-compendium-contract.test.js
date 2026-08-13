const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const view = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const page = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const js = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const service = read('Services/Compendiums/CompendiumReadService.cs');
const readiness = read('Services/Compendiums/CompendiumReadinessPolicy.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const planner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const metrics = read('Utilities/Reporting/CompendiumLayoutMetrics.cs');
const verifier = read('Utilities/Reporting/CompendiumPdfCompositionVerifier.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const registrations = read('Services/Publications/PublicationServiceCollectionExtensions.cs');
const preset = read('Services/Publications/CompendiumPresetService.cs');
const presetContracts = read('Services/Publications/CompendiumPresetContracts.cs');
const model = read('Models/Publications/CompendiumPreset.cs');
const db = read('Data/ApplicationDbContext.cs');
const registration = read('Services/Publications/PublicationServiceCollectionExtensions.cs');
const landing = read('Pages/Projects/Publications/Index.cshtml');
const migration = read('Migrations/20261208140000_AddCompendiumPublicationImagery.cs');
const manifest = read('Migrations/immutable-migration-ids.txt');

// Phase 22 foundation remains a contract for Phase 23.
test('phase 23 preserves all-project candidate selection and proliferation as a filter', () => {
  assert.match(service, /ProjectLifecycleStatus\.Active/);
  assert.match(service, /ProjectLifecycleStatus\.Completed/);
  assert.match(view, /All proliferation status/);
  assert.match(view, /Not available for proliferation/);
  assert.match(view, /Availability not assessed/);
  assert.match(view, /data-project-checkbox/);
  assert.match(view, /data-filter-selected/);
});

test('phase 23 keeps user-authored project membership and order authoritative', () => {
  assert.match(view, /data-order-list/);
  assert.match(js, /orderedIds/);
  assert.match(js, /dragstart/);
  assert.match(js, /Select first 100 matching/);
  assert.match(service, /GroupInPublicationOrder/);
  assert.match(exportService, /ProjectSelections/);
});

test('phase 23 turns review into a focused project-by-project workspace', () => {
  assert.match(view, /data-review-progress/);
  assert.match(view, /data-review-previous/);
  assert.match(view, /data-review-next-attention/);
  assert.match(view, /data-review-image-frame/);
  assert.match(view, /data-review-facts/);
  assert.match(view, /data-review-description/);
  assert.match(view, /data-review-mark-reviewed/);
  assert.match(page, /OnPostReviewAsync/);
  assert.match(service, /GetReviewProjectAsync/);
});

test('phase 23 stores publication-specific image choice and focal crop, not factual project data', () => {
  assert.match(model, /PrimaryPhotoId/);
  assert.match(model, /PrimaryFocalX/);
  assert.match(model, /PrimaryFocalY/);
  assert.match(model, /ImageSelectionMode/);
  assert.match(presetContracts, /CompendiumPresetProjectConfiguration/);
  assert.doesNotMatch(model, /ArmService|ProliferationCost|ProjectDescription/);
  assert.match(js, /imageSelectionMode/);
  assert.match(js, /focalX/);
  assert.match(js, /focalY/);
});

test('phase 23 provides automatic and explicit image selection with missing-saved-image recovery', () => {
  assert.match(dto, /CompendiumImageSelectionMode/);
  assert.match(dto, /ExplicitPublication/);
  assert.match(service, /SelectAutomaticPhoto/);
  assert.match(service, /ExplicitPhotoUnavailable/);
  assert.match(preset, /publicationImageUnavailable/);
  assert.match(view, /Use automatic selection/);
  assert.match(js, /publicationConfigChanged/);
});

test('phase 23 uses the shared Publications image source for probe, preview and final crop render', () => {
  assert.match(service, /IBrochurePhotoService/);
  assert.match(service, /ProbeAsync/);
  assert.match(page, /GetPreviewAsync/);
  assert.match(exportService, /BrochurePhotoRenderRequest/);
  assert.match(exportService, /RenderAsync/);
  assert.match(exportService, /PrimaryFocalX/);
  assert.match(exportService, /PrimaryFocalY/);
});

test('phase 24 evaluates effective DPI against the redesigned reviewed project-image frame', () => {
  assert.match(dto, /FrameWidthPoints\s*=\s*519/);
  assert.match(dto, /FrameHeightPoints\s*=\s*214/);
  assert.match(dto, /CalculateEffectiveDpi/);
  assert.match(dto, /GoodDpi\s*=\s*180/);
  assert.match(dto, /AcceptableDpi\s*=\s*150/);
  assert.match(readiness, /lowResolutionPhoto/);
  assert.match(readiness, /acceptableResolutionPhoto/);
  assert.match(js, /effectiveDpi/);
});

test('phase 23 review fingerprint binds live facts and publication imagery but is not persisted in presets', () => {
  assert.match(fingerprint, /compendium-review-v1/);
  assert.match(fingerprint, /ProjectName/);
  assert.match(fingerprint, /ProliferationCostLakhs/);
  assert.match(fingerprint, /ResolvedPhotoId/);
  assert.match(fingerprint, /FocalX/);
  assert.match(dto, /ReviewFingerprint/);
  assert.doesNotMatch(model, /ReviewFingerprint/);
  assert.doesNotMatch(presetContracts, /ReviewFingerprint/);
});

test('phase 23 dirty state excludes review fingerprint while including image and crop decisions', () => {
  assert.match(js, /serializeConfigs\(false\)/);
  assert.match(js, /includeReviewFingerprint/);
  assert.match(js, /primaryPhotoId/);
  assert.match(js, /focalX/);
  assert.match(js, /imageSelectionMode/);
  assert.match(js, /captureSnapshot/);
});

test('phase 23 readiness has stable blocker warning and information semantics', () => {
  assert.match(dto, /CompendiumFindingSeverity/);
  assert.match(readiness, /missingPhoto/);
  assert.match(readiness, /missingArmService/);
  assert.match(readiness, /missingDescription/);
  assert.match(readiness, /proliferationNotAssessed/);
  assert.match(readiness, /notAvailableForProliferation/);
  assert.match(readiness, /reviewRequired/);
  assert.match(readiness, /projectChangedAfterReview/);
  assert.match(page, /blockers = data\.Preflight\.BlockerCount/);
});

test('phase 23 readiness findings are filterable and actionable rather than a passive warning register', () => {
  assert.match(view, /data-finding-filter="blocker"/);
  assert.match(view, /data-finding-filter="warning"/);
  assert.match(view, /data-finding-filter="information"/);
  assert.match(view, /data-findings-current-only/);
  assert.match(js, /renderFindings/);
  assert.match(js, /data-finding-action/);
  assert.match(js, /Review image/);
  assert.match(js, /Review project/);
});

test('phase 23 review actions respect project maintenance authority', () => {
  assert.match(page, /CanMaintainProjectData/);
  assert.match(page, /RoleNames\.Admin/);
  assert.match(page, /RoleNames\.HoD/);
  assert.match(page, /CompletedSummary\/Edit/);
  assert.match(page, /completedEditUrl/);
  assert.match(js, /canMaintainProjectData/);
});

test('phase 23 avoids stale async preflight and review responses', () => {
  assert.match(js, /preflightRevision/);
  assert.match(js, /preflightController/);
  assert.match(js, /reviewRequestRevision/);
  assert.match(js, /reviewRequestController/);
  assert.match(js, /AbortController/);
  assert.match(js, /Checking publication/);
});

test('phase 23 saved Compendiums persist image configuration through schema v2', () => {
  assert.match(preset, /CurrentSchemaVersion\s*=\s*2/);
  assert.match(migration, /Migration\("20261208140000_AddCompendiumPublicationImagery"\)/);
  assert.match(migration, /PrimaryPhotoId/);
  assert.match(migration, /PrimaryFocalX/);
  assert.match(migration, /PrimaryFocalY/);
  assert.match(migration, /ImageSelectionMode/);
  assert.match(manifest, /20261208140000_AddCompendiumPublicationImagery/);
  assert.match(db, /ImageSelectionMode/);
});

test('phase 23 registers readiness policy without creating a second factual store', () => {
  assert.match(registration, /ICompendiumReadinessPolicy/);
  assert.match(registration, /CompendiumReadinessPolicy/);
  assert.match(service, /ApplicationDbContext/);
  assert.doesNotMatch(model, /DescriptionMarkdown|CompletionYear|LifecycleDisplay/);
});

test('phase 23 retains safe shared-preset concurrency and PRISM modal load handling', () => {
  assert.match(preset, /BeginTransactionAsync/);
  assert.match(preset, /EnsureVersion/);
  assert.match(preset, /RollbackAsync/);
  assert.match(view, /id="compendiumDiscardModal"/);
  assert.match(js, /discardModal/);
  assert.doesNotMatch(js, /\bconfirm\s*\(/);
});

test('phase 23 user-facing copy removes server and data-model language', () => {
  assert.match(view, /Set the title and publication details for this Compendium/);
  assert.match(view, /Check the selected projects for publication completeness and quality/);
  assert.match(view, /Projects are grouped by technical category in the final Compendium/);
  assert.doesNotMatch(view, /Server preflight/);
  assert.doesNotMatch(view, /Project facts remain live from PRISM/);
  assert.match(landing, /Create professional publications from PRISM project records/);
});

test('phase 23 adds dedicated responsive review and crop presentation', () => {
  assert.match(css, /compendium-review-workspace/);
  assert.match(css, /compendium-review-image-frame/);
  assert.match(css, /compendium-photo-modal-layout/);
  assert.match(css, /compendium-crop-frame/);
  assert.match(css, /compendium-focal-marker/);
});

// Phase 23.1 runtime hardening and review-freeze contracts.
test('phase 23.1 makes selection readiness semantic without relying on colour alone', () => {
  assert.match(view, /bi-check2/);
  assert.match(view, /bi-exclamation-lg/);
  assert.match(view, /Description available/);
  assert.match(view, /Arm\/Service missing/);
  assert.match(view, /No photo/);
  assert.match(css, /compendium-data-badges > span > i/);
  assert.match(css, /compendium-photo-count\.is-missing/);
});

test('phase 23.1 treats the zero-selection state as setup rather than a visible publication error', () => {
  assert.match(view, /data-ready-blockers>—</);
  assert.match(view, /Select projects to build the catalogue structure/);
  assert.match(view, /compendium-readiness-empty/);
  assert.match(view, /data-clear-selection hidden disabled/);
  assert.match(js, /readyBlockers\.textContent = "—"/);
  assert.match(js, /Select projects to begin publication readiness checks/);
});

test('phase 23.1 disables review navigation and output actions with explicit accessibility state', () => {
  assert.match(view, /data-review-previous[^>]*disabled[^>]*aria-disabled="true"/);
  assert.match(view, /data-review-next-attention disabled aria-disabled="true"/);
  assert.match(js, /setControlDisabled/);
  assert.match(js, /updateReviewNavigation/);
  assert.match(js, /setControlDisabled\(preview, !canPreview\)/);
  assert.match(js, /setControlDisabled\(generate, !canDownload\)/);
  assert.match(css, /compendium-output-actions \.btn:disabled/);
  assert.match(css, /cursor:not-allowed/);
});

test('phase 23.1 gives next-attention navigation deterministic blocker-warning-review priority', () => {
  assert.match(js, /const attentionPriority = id =>/);
  assert.match(js, /severity === "blocker"/);
  assert.match(js, /reviewRequired", "projectChangedAfterReview"/);
  assert.match(js, /return 1/);
  assert.match(js, /return 2/);
  assert.match(js, /return 3/);
  assert.match(js, /const nextAttentionId = \(\) =>/);
});

test('phase 23.1 presents reviewed project state as Ready Warning or Review required', () => {
  assert.match(js, /visualProjectState/);
  assert.match(js, /reviewState\.textContent = "Ready"/);
  assert.match(js, /reviewState\.textContent = "Warning"/);
  assert.match(js, /reviewState\.textContent = "Review required"/);
  assert.match(js, /Reviewed with warnings/);
  assert.match(js, /Ready for publication/);
  assert.match(css, /compendium-review-state\.is-blocker/);
});

test('phase 23.1 confirms review immediately while retaining server fingerprint verification', () => {
  assert.match(js, /projectStateById\.set\(Number\(activeReviewId\)/);
  assert.match(js, /isReviewed: true, isReviewStale: false/);
  assert.match(js, /ensureConfig\(activeReviewId\)\.reviewFingerprint/);
  assert.match(js, /schedulePreflight\(\)/);
});

test('phase 23.1 disables findings during refresh but preserves filter context until selection is cleared', () => {
  assert.match(js, /setFindingToolbarAvailability/);
  assert.match(js, /if \(preflightPending\)/);
  assert.match(js, /setFindingToolbarAvailability\(false\)/);
  assert.match(js, /setFindingToolbarAvailability\(true\)/);
  assert.match(js, /if \(findingsCurrentOnly\) findingsCurrentOnly\.checked = false/);
  assert.match(css, /compendium-finding-toolbar\.is-disabled/);
});


test('phase 24 separates physical page planning from QuestPDF composition', () => {
  assert.match(planner, /ICompendiumPagePlanner/);
  assert.match(planner, /CompendiumPagePlan/);
  assert.match(planner, /CompendiumPageKind\.Cover/);
  assert.match(planner, /CompendiumPageKind\.Index/);
  assert.match(planner, /CompendiumPageKind\.ProjectContinuation/);
  assert.match(planner, /CompendiumPageKind\.BackCover/);
  assert.match(exportService, /_pagePlanner\.Plan\(context\)/);
  assert.match(builder, /context\.Plan \?\? new CompendiumPagePlanner\(\)\.Plan\(context\)/);
});

test('phase 24 uses one reviewed photo geometry across browser DPI and final PDF', () => {
  assert.match(metrics, /ProjectImageWidthPoints\s*=\s*ContentWidthPoints/);
  assert.match(metrics, /ProjectImageHeightPoints\s*=\s*214/);
  assert.match(dto, /FrameWidthPoints\s*=\s*519/);
  assert.match(dto, /FrameHeightPoints\s*=\s*214/);
  assert.match(builder, /Height\(CompendiumLayoutMetrics\.ProjectImageHeightPoints\)/);
  assert.match(view, /data-photo-frame-width="@CompendiumPublicationImagePolicy\.FrameWidthPoints"/);
});

test('phase 24 supports deterministic continuation pages without emergency font shrinking', () => {
  assert.match(planner, /FirstPageDescriptionBudgetWithPhoto/);
  assert.match(planner, /ContinuationDescriptionBudget/);
  assert.match(planner, /CompendiumMarkdownChunker\.Split/);
  assert.match(builder, /Project description · continued/);
  assert.match(metrics, /ProjectBodyMinimumFontSize\s*=\s*9\.5f/);
  assert.match(metrics, /ProjectBodyFontSize\s*=\s*10f/);
  assert.match(metrics, /ProjectBodyMinimumFontSize\s*=\s*9\.5f/);
  assert.match(builder, /CompendiumLayoutMetrics\.ProjectBodyFontSize/);
  assert.match(builder, /CompendiumLayoutMetrics\.ContinuationBodyFontSize/);
});

test('phase 24 replaces the missing-photo placeholder with a designed text-led project layout', () => {
  assert.match(planner, /CompendiumProjectLayoutVariant\.NoPhoto/);
  assert.match(builder, /ComposeNoPhotoTreatment/);
  assert.doesNotMatch(builder, /Photograph not available/);
  assert.doesNotMatch(builder, /Add or mark a project cover photograph/);
});

test('phase 24 cover and back cover use publication-controlled identity without fixed narrative copy', () => {
  assert.match(builder, /ComposeCover\(/);
  assert.match(builder, /ComposeBackCover\(/);
  assert.match(builder, /Text\(title\)/);
  assert.match(builder, /Text\(subtitle\)/);
  assert.match(builder, /Text\(edition\)/);
  assert.doesNotMatch(builder, /Capability catalogue generated from/);
  assert.doesNotMatch(builder, /Generated \{/);
});

test('phase 24 verifies the physical PDF before it is issued', () => {
  assert.match(verifier, /UglyToad\.PdfPig/);
  assert.match(verifier, /expectedPageCount/);
  assert.match(verifier, /ProjectStartPages/);
  assert.match(exportService, /_compositionVerifier\.Verify\(pdfBytes, context, plan\)/);
  assert.match(page, /X-PRISM-Publication-Composition-Verified/);
  assert.match(page, /X-PRISM-Publication-Page-Count/);
  assert.match(js, /PDF verified ·/);
});

test('phase 24 allows preview before review but gates final issue on complete review', () => {
  assert.match(js, /const canPreview = technicallyValid/);
  assert.match(js, /const canDownload = technicallyValid && allReviewed/);
  assert.match(js, /Review required/);
  assert.match(exportService, /RequireAllReviewed/);
  assert.match(exportService, /Review all selected projects before final issue/);
  assert.match(page, /RequireAllReviewed: !preview/);
});

test('phase 24 renders review markdown instead of exposing markdown source syntax', () => {
  assert.match(js, /renderInlineMarkdown/);
  assert.match(js, /<strong>\$1<\/strong>/);
  assert.match(js, /compendium-review-markdown-heading/);
  assert.match(css, /compendium-review-description-text strong/);
});

test('phase 24 suppresses routine automatic-image info after the project is reviewed', () => {
  assert.match(readiness, /ImageSelectionMode == CompendiumImageSelectionMode\.Automatic && !isReviewed/);
});

test('phase 24 registers page planning and physical verification services', () => {
  assert.match(registrations, /AddSingleton<ICompendiumPagePlanner, CompendiumPagePlanner>/);
  assert.match(registrations, /AddSingleton<ICompendiumPdfCompositionVerifier, CompendiumPdfCompositionVerifier>/);
});
