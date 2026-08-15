const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..', '..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

const view = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const brochureView = read('Pages/Projects/Publications/Brochure/Index.cshtml');
const page = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const js = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const service = read('Services/Compendiums/CompendiumReadService.cs');
const readiness = read('Services/Compendiums/CompendiumReadinessPolicy.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const programme = read('Services/Compendiums/CompendiumProgrammeInformation.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const planner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const metrics = read('Utilities/Reporting/CompendiumLayoutMetrics.cs');
const verifier = read('Utilities/Reporting/CompendiumPdfCompositionVerifier.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const pagePlanner = read('Utilities/Reporting/CompendiumPagePlanner.cs');
const registrations = read('Services/Publications/PublicationServiceCollectionExtensions.cs');
const preset = read('Services/Publications/CompendiumPresetService.cs');
const presetContracts = read('Services/Publications/CompendiumPresetContracts.cs');
const model = read('Models/Publications/CompendiumPreset.cs');
const db = read('Data/ApplicationDbContext.cs');
const registration = read('Services/Publications/PublicationServiceCollectionExtensions.cs');
const landing = read('Pages/Projects/Publications/Index.cshtml');
const migration = read('Migrations/20261208140000_AddCompendiumPublicationImagery.cs');
const coverMigration = read('Migrations/20261208150000_AddCompendiumCoverHeroControls.cs');
const editorialMigration = read('Migrations/20261208160000_AddCompendiumEditorialComposer.cs');
const workspaceMigration = read('Migrations/20261208170000_AddCompendiumFirstClassSections.cs');
const sanitizer = read('Utilities/Reporting/CompendiumPublicationTextSanitizer.cs');
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
  assert.match(service, /BuildPublicationStructure/);
  assert.match(exportService, /ProjectSelections/);
});



test('editorial composer offers project brief by default with capability and description alternatives', () => {
  assert.match(dto, /CompendiumNarrativeSource/);
  assert.match(dto, /ProjectBrief\s*=\s*1/);
  assert.match(view, /data-narrative-value="ProjectBrief"/);
  assert.match(view, /data-narrative-value="CapabilityOverview"/);
  assert.match(view, /data-narrative-value="ProjectDescription"/);
  assert.match(service, /ProjectCapabilityStatements/);
  assert.match(service, /ResolveNarrative/);
  assert.match(js, /changeNarrativeSource/);
});

test('editorial composer separates publication grouping from sort order and preserves authoritative technical category', () => {
  assert.match(dto, /CompendiumGroupingMode/);
  assert.match(dto, /CompendiumSortMode/);
  assert.match(view, /data-grouping-value="TechnicalCategory"/);
  assert.match(view, /data-grouping-value="CustomSections"/);
  assert.match(view, /data-sort-value="LatestFirst"/);
  assert.match(view, /data-sort-value="Alphabetical"/);
  assert.match(service, /SortProjects/);
  assert.match(service, /CustomSectionName/);
  assert.match(builder, /TechnicalCategoryDisplay/);
  assert.match(js, /publicationGroups/);
});

test('editorial composer exposes a direct cover hero chooser', () => {
  assert.match(view, /data-cover-choose/);
  assert.match(view, /id="compendiumCoverHeroModal"/);
  assert.match(view, /data-cover-hero-picker/);
  assert.match(js, /renderCoverHeroPicker/);
  assert.match(js, /data-cover-hero-choice/);
});

test('capability dossier project pages use a consistent editorial grammar and dynamic narrative heading', () => {
  assert.match(builder, /CAPABILITY DOSSIER/);
  assert.match(builder, /ComposeProjectImage/);
  assert.match(builder, /NarrativeLabel/);
  assert.match(builder, /TechnicalCategoryDisplay/);
  assert.match(builder, /NormalizeNarrativeLabel/);
  assert.doesNotMatch(builder, /if \(planned\.IsFirstProjectInCategory\)/);
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
  assert.match(dto, /LongFrameHeightPoints\s*=\s*190/);
  assert.match(dto, /MediumFrameHeightPoints\s*=\s*240/);
  assert.match(dto, /ShortFrameHeightPoints\s*=\s*300/);
  assert.match(dto, /ResolveFrameHeightPoints/);
  assert.match(dto, /CalculateEffectiveDpi/);
  assert.match(dto, /GoodDpi\s*=\s*180/);
  assert.match(dto, /AcceptableDpi\s*=\s*150/);
  assert.match(readiness, /lowResolutionPhoto/);
  assert.match(readiness, /acceptableResolutionPhoto/);
  assert.match(js, /effectiveDpi/);
});

test('phase 23 review fingerprint binds live facts and publication imagery but is not persisted in presets', () => {
  assert.match(fingerprint, /compendium-review-v(?:3|4|5|6-adaptive-pagination|7-adaptive-composition|8-production-hardening|9-programme-iconography)/);
  assert.match(fingerprint, /PublicationSectionKey/);
  assert.match(fingerprint, /PublicationSectionName/);
  assert.match(fingerprint, /NarrativeSource/);
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

test('phase 24.1 readiness separates workflow review state from publication-quality findings', () => {
  assert.match(dto, /CompendiumFindingSeverity/);
  assert.match(readiness, /missingPhoto/);
  assert.match(readiness, /missingArmService/);
  assert.match(readiness, /missingDescription/);
  assert.doesNotMatch(readiness, /automaticImageSelected/);
  assert.doesNotMatch(readiness, /proliferationNotAssessed/);
  assert.doesNotMatch(readiness, /notAvailableForProliferation/);
  assert.doesNotMatch(readiness, /Warning\(\s*"reviewRequired"/);
  assert.doesNotMatch(readiness, /Warning\(\s*"projectChangedAfterReview"/);
  assert.match(page, /reviewed = data\.Groups/);
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

test('publication workspace persists first-class sections and project narrative overrides through schema v5', () => {
  assert.match(preset, /CurrentSchemaVersion\s*=\s*[567]/);
  assert.match(migration, /Migration\("20261208140000_AddCompendiumPublicationImagery"\)/);
  assert.match(migration, /PrimaryPhotoId/);
  assert.match(migration, /PrimaryFocalX/);
  assert.match(migration, /PrimaryFocalY/);
  assert.match(migration, /ImageSelectionMode/);
  assert.match(manifest, /20261208140000_AddCompendiumPublicationImagery/);
  assert.match(coverMigration, /Migration\("20261208150000_AddCompendiumCoverHeroControls"\)/);
  assert.match(coverMigration, /CoverImageMode/);
  assert.match(manifest, /20261208150000_AddCompendiumCoverHeroControls/);
  assert.match(model, /CoverHeroPhotoId/);
  assert.match(presetContracts, /CompendiumCoverConfiguration/);
  assert.match(db, /ImageSelectionMode/);
  assert.match(editorialMigration, /Migration\("20261208160000_AddCompendiumEditorialComposer"\)/);
  assert.match(editorialMigration, /NarrativeSource/);
  assert.match(editorialMigration, /GroupingMode/);
  assert.match(editorialMigration, /SortMode/);
  assert.match(editorialMigration, /CustomSectionName/);
  assert.match(manifest, /20261208160000_AddCompendiumEditorialComposer/);
  assert.match(workspaceMigration, /Migration\("20261208170000_AddCompendiumFirstClassSections"\)/);
  assert.match(workspaceMigration, /CompendiumPresetSections/);
  assert.match(workspaceMigration, /CustomSectionId/);
  assert.match(workspaceMigration, /NarrativeSourceOverride/);
  assert.match(manifest, /20261208170000_AddCompendiumFirstClassSections/);
  assert.match(model, /ICollection<CompendiumPresetSection> Sections/);
  assert.match(model, /CustomSectionId/);
  assert.match(model, /NarrativeSourceOverride/);
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
  assert.match(view, /Publication structure/);
  assert.match(view, /authoritative technical category/);
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
  assert.match(view, /Arms \/ Services/);
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

test('phase 24.1 makes initial review sequential and keeps warnings as a separate follow-up pass', () => {
  assert.match(js, /const nextUnreviewedId/);
  assert.match(js, /const nextWarningId/);
  assert.match(js, /Review & next/);
  assert.match(js, /Finish review/);
  assert.match(js, /Review warnings/);
  assert.match(js, /reviewAndAdvance/);
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
  assert.match(js, /projectStateById\.set\(reviewedId/);
  assert.match(js, /isReviewed: true, isReviewStale: false/);
  assert.match(js, /ensureConfig\(reviewedId\)\.reviewFingerprint/);
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

test('phase 24.1 uses content-aware reviewed photo geometry across browser DPI and final PDF', () => {
  assert.match(metrics, /ProjectImageWidthPoints\s*=\s*ContentWidthPoints/);
  assert.match(metrics, /ProjectImageLongHeightPoints\s*=\s*190/);
  assert.match(metrics, /ProjectImageMediumHeightPoints\s*=\s*240/);
  assert.match(metrics, /ProjectImageShortHeightPoints\s*=\s*300/);
  assert.match(dto, /FrameWidthPoints\s*=\s*519/);
  assert.match(dto, /ResolveFrameHeightPoints/);
  assert.match(builder, /ProjectImageHeightPoints\(layout\)/);
  assert.match(page, /ImageFrameHeightPoints/);
  assert.match(js, /reviewImageFrame\.style\.aspectRatio/);
});

test('phase 24 supports deterministic continuation pages without emergency font shrinking', () => {
  assert.match(metrics, /FirstPageDescriptionBudgetPhotoLong/);
  assert.match(metrics, /FirstPageDescriptionBudgetPhotoMedium/);
  assert.match(metrics, /FirstPageDescriptionBudgetPhotoShort/);
  assert.match(planner, /ContinuationDescriptionBudget/);
  assert.match(planner, /CompendiumMarkdownChunker\.Split/);
  assert.match(builder, /TECHNICAL REFERENCE|narrativeLabel.*CONTINUED/);
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
  assert.match(builder, /Text\(title!?\)/);
  assert.match(builder, /Text\(subtitle!?\)/);
  assert.match(builder, /Text\(edition!?\)/);
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

test('phase 24.1 removes routine automatic-image and normal proliferation-state noise from readiness', () => {
  assert.doesNotMatch(readiness, /automaticImageSelected/);
  assert.doesNotMatch(readiness, /proliferationNotAssessed/);
  assert.doesNotMatch(readiness, /notAvailableForProliferation/);
});

test('phase 24 registers page planning and physical verification services', () => {
  assert.match(registrations, /AddSingleton<ICompendiumPagePlanner, CompendiumPagePlanner>/);
  assert.match(registrations, /AddSingleton<ICompendiumPdfCompositionVerifier, CompendiumPdfCompositionVerifier>/);
});


test('phase 24.1 supports continuous review with Ctrl Enter while avoiding editable controls and modals', () => {
  assert.match(js, /event\.ctrlKey && event\.key === "Enter"/);
  assert.match(js, /HTMLTextAreaElement/);
  assert.match(js, /document\.querySelector\("\.modal\.show"\)/);
  assert.match(js, /reviewAndAdvance\(\)/);
});

test('phase 24.1 makes review metadata context-sensitive and cost units explicit', () => {
  assert.match(js, /programmeModules/);
  assert.match(programme, /Technology transfer/);
  assert.match(dto, /₹.*lakh/);
  assert.match(programme, /Proliferation cost/);
  assert.match(builder, /ComposeProgrammeInformation/);
});

test('phase 24.1 replaces the automatic cover mosaic with one controlled hero or graphic fallback', () => {
  assert.match(view, /data-cover-setting/);
  assert.match(view, /data-review-use-cover/);
  assert.match(js, /coverState/);
  assert.match(js, /Use as cover hero|coverState\.imageMode = "explicit"/);
  assert.match(exportService, /ResolveCover(?:Hero|Design)Async/);
  assert.match(builder, /ComposeCoverHero/);
  assert.doesNotMatch(builder, /ComposeCoverImageMosaic/);
});

test('phase 24.1 sanitizes publication text before planning and rendering', () => {
  assert.match(sanitizer, /UnicodeCategory\.Control/);
  assert.match(sanitizer, /NormalizationForm\.FormC/);
  assert.match(exportService, /CompendiumPublicationTextSanitizer\.Sanitize/);
});

test('phase 24.1 orders readiness findings by severity and publication order', () => {
  assert.match(service, /OrderByDescending\(finding => finding\.Severity\)/);
  assert.match(service, /projectOrder\.GetValueOrDefault/);
  assert.match(service, /Findings = orderedFindings/);
});

test('phase 24.1 has a content-aware no-JS review-image aspect fallback', () => {
  assert.match(css, /compendium-review-image-frame[^\n]*aspect-ratio:519\/240/);
});


test('phase 26 makes Brochure and Compendium true full-width publication workspaces', () => {
  assert.match(view, /ViewData\["UseFullWidth"\]\s*=\s*true/);
  assert.match(view, /ViewData\["PageShell"\]\s*=\s*"workspace"/);
  assert.match(brochureView, /ViewData\["UseFullWidth"\]\s*=\s*true/);
  assert.match(brochureView, /ViewData\["PageShell"\]\s*=\s*"workspace"/);
  assert.match(css, /publications-page\.compendium-builder-page[\s\S]*max-width:\s*none/);
  assert.match(css, /publications-page\.brochure-builder[\s\S]*max-width:\s*none/);
  assert.match(css, /@media \(min-width: 1800px\)/);
});

test('phase 26 custom sections are first-class independent objects rather than project-derived names', () => {
  assert.match(dto, /CompendiumPublicationSection/);
  assert.match(presetContracts, /CompendiumPresetSectionConfiguration/);
  assert.match(page, /CustomSectionsJson/);
  assert.match(js, /customSections/);
  assert.match(js, /serializeSections/);
  assert.match(js, /createSectionKey/);
  assert.match(js, /data-custom-section-add/);
  assert.match(js, /const knownCustomSections = \(\) => customSections\.map/);
  assert.match(service, /NormalizeSections/);
});

test('phase 26 preserves custom section order while sorting projects within sections', () => {
  assert.match(service, /foreach \(var section in normalizedSections\.OrderBy\(section => section\.SortOrder\)\)/);
  assert.match(service, /grouped\.Add\(\(section\.Name, SortProjects\(members, sortMode\)\)\)/);
  assert.match(js, /customSections\.map\(section =>/);
  assert.match(js, /sortProjectIds/);
  assert.match(js, /moveSection/);
});

test('phase 26 supports safe custom section creation rename reorder delete and drag assignment', () => {
  assert.match(view, /id="compendiumSectionDeleteModal"/);
  assert.match(js, /data-section-rename/);
  assert.match(js, /data-section-group-up/);
  assert.match(js, /data-section-group-down/);
  assert.match(js, /data-section-delete/);
  assert.match(js, /data-section-drag-handle/);
  assert.match(js, /assignProjectToSection/);
  assert.match(js, /Unassigned/);
  assert.doesNotMatch(js, /\bconfirm\s*\(/);
});

test('phase 26 supports a per-project narrative override while retaining a publication default', () => {
  assert.match(dto, /NarrativeSourceOverride/);
  assert.match(page, /NarrativeSourceOverride/);
  assert.match(presetContracts, /NarrativeSourceOverride/);
  assert.match(service, /selection\.NarrativeSourceOverride \?\?/);
  assert.match(js, /effectiveNarrativeSource/);
  assert.match(js, /setProjectNarrativeSource/);
  assert.match(js, /Use publication default/);
});

test('phase 26 aggregates readiness findings and removes authoring language from issued no-photo pages', () => {
  assert.match(js, /compendium-finding-group/);
  assert.match(css, /compendium-finding-group/);
  assert.match(builder, /CAPABILITY DOSSIER/);
  assert.doesNotMatch(builder, /Publication image not selected/);
  assert.doesNotMatch(builder, /project record remains fully publishable/);
});

test('phase 26 no-grouping index suppresses an artificial Projects section heading', () => {
  assert.match(builder, /showGroupHeadings/);
  assert.match(builder, /string\.Equals\(planned\.IndexGroups\[0\]\.CategoryName, "Projects"/);
});


test('phase 27 pins final output while the publication structure owns the rail scroll viewport', () => {
  assert.match(css, /grid-template-rows:\s*minmax\(0,\s*1fr\)\s*auto/);
  assert.match(css, /height:\s*calc\(100vh\s*-\s*128px\)/);
  assert.match(css, /compendium-order-list[\s\S]*flex:\s*1\s+1\s+auto/);
  assert.match(css, /compendium-final-card[\s\S]*flex:\s*0\s+0\s+auto/);
});

test('phase 27 uses wide monitors for a readable project publication register', () => {
  for (const heading of ['Lifecycle', 'Project category', 'Technical category', 'Narrative', 'Arms / Services', 'Cost', 'Photography']) {
    assert.match(view, new RegExp(heading.replace('/', '\\/')));
  }
  assert.match(css, /compendium-project-table/);
  assert.match(view, /compendium-narrative-readiness/);
  assert.match(view, /Arms \/ Services/);
});

test('phase 27 review includes a near-WYSIWYG dossier page and visible section rename affordance', () => {
  assert.match(view, /data-live-page-preview/);
  assert.match(view, /PDF preview .*authoritative/);
  assert.match(js, /renderLivePagePreview/);
  assert.match(css, /aspect-ratio:\s*595\.28\s*\/\s*841\.89/);
  assert.match(js, /compendium-section-name-editor/);
  assert.match(js, /bi-pencil-square/);
});

test('phase 27 removes repeated category and lifecycle from the PDF running header', () => {
  assert.match(builder, /ComposeRunningHeader\(header, publicationTitle\.ToUpperInvariant\(\), edition, marking\)/);
  assert.match(builder, /ResolveProjectKicker\(project\)/);
  assert.doesNotMatch(builder, /project\.LifecycleDisplay\.ToUpperInvariant\(\)/);
  assert.doesNotMatch(builder, /ComposeRunningHeader\(header, publicationKicker\.ToUpperInvariant\(\), project\.LifecycleDisplay/);
});

test('phase 27 adapts PDF fact geometry and narrative image pressure', () => {
  assert.match(builder, /BuildMetadataRows/);
  assert.match(builder, /4\s*=>\s*new\[\]\s*\{\s*2,\s*2\s*\}/);
  assert.match(builder, /5\s*=>\s*new\[\]\s*\{\s*3,\s*2\s*\}/);
  assert.match(dto, /EstimateNarrativeLines/);
  assert.match(dto, /Math\.Ceiling\(Math\.Max\(1, line\.Length\) \/ 90d\)/);
  assert.match(pagePlanner, /CompendiumPublicationImagePolicy\.ResolveFrameHeightPoints/);
});

test('phase 27 automatic cover selection prefers intentional project cover sources', () => {
  assert.match(exportService, /CoverHeroSourcePriority/);
  assert.match(exportService, /CompendiumPhotoSelectionSource\.ProjectCover\s*=>\s*4/);
  assert.match(exportService, /CompendiumPhotoSelectionSource\.FirstAvailable\s*=>\s*1/);
  assert.match(js, /projectcover:\s*4/);
  assert.match(js, /firstavailable:\s*1/);
  assert.match(exportService, /SuitableForCoverHero|PreferredForPublication/);
});

test('phase 27 latest chronology and technical taxonomy are authoritative and deterministic', () => {
  assert.match(service, /lifecycleStatus == ProjectLifecycleStatus\.Completed/);
  assert.match(service, /ResolveCompletionYear\(completedYear, completedOn\)/);
  assert.match(service, /TechnicalCategorySortOrder/);
  assert.match(service, /OrderBy\(group => group\.SortOrder\)/);
  assert.match(view, /Completed projects use completion chronology; ongoing projects use project\/development year/);
});

test('phase 28 makes Review proof-first with focus mode and explicit preview zoom', () => {
  assert.match(view, /data-review-focus-toggle/);
  assert.match(view, /data-live-page-zoom="fit"/);
  assert.match(view, /data-live-page-zoom="75"/);
  assert.match(view, /data-live-page-zoom="100"/);
  assert.match(js, /applyReviewFocusMode/);
  assert.match(js, /applyLivePreviewZoom/);
  assert.match(css, /is-review-focus[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s*350px/);
  assert.match(css, /data-preview-zoom="100"[\s\S]*595px/);
});

test('phase 28 consolidates Review scrolling and compresses duplicate image inspection', () => {
  assert.match(view, /compendium-review-image-summary/);
  assert.match(css, /compendium-review-inspector[\s\S]*max-height:\s*calc\(100dvh\s*-\s*260px\)/);
  assert.match(css, /compendium-review-description-text[\s\S]*max-height:\s*none\s*!important/);
  assert.match(css, /compendium-review-image-summary[\s\S]*grid-template-columns:\s*132px\s+minmax\(0,\s*1fr\)/);
});

test('phase 28 supports scalable section collapse without changing publication structure', () => {
  assert.match(view, /data-structure-collapse-all/);
  assert.match(view, /data-structure-expand-all/);
  assert.match(js, /collapsedGroupKeys/);
  assert.match(js, /data-section-toggle-collapse/);
  assert.match(js, /structureCollapseAll/);
  assert.match(js, /structureExpandAll/);
});

test('phase 28 keeps issue commands reachable with a viewport output dock', () => {
  assert.match(view, /data-output-dock/);
  assert.match(view, /data-output-dock-preview/);
  assert.match(view, /data-output-dock-generate/);
  assert.match(js, /setupOutputDockObserver/);
  assert.match(js, /IntersectionObserver/);
  assert.match(css, /compendium-output-dock[\s\S]*position:\s*fixed/);
});

test('phase 28 turns grouped readiness findings into review queues', () => {
  assert.match(js, /data-finding-group-review/);
  assert.match(js, /activeFindingQueue/);
  assert.match(js, /nextFindingQueueId/);
  assert.match(js, /Review affected projects/);
  assert.match(css, /compendium-finding-group__queue/);
});
