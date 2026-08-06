const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const assert = require('node:assert/strict');

const source = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-decks.js'),
  'utf8');

const additionalSlidesSource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-additional-slides.js'),
  'utf8');
const confirmSource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-confirm.js'),
  'utf8');
const viewSource = fs.readFileSync(
  path.resolve(__dirname, '..', '..', '..', 'Pages', 'Workspace', 'BriefingDecks', 'Index.cshtml'),
  'utf8');

const ffcEditorSource = fs.readFileSync(
  path.resolve(__dirname, '..', '..', '..', 'Pages', 'Workspace', 'BriefingDecks', '_FfcGlobalFootprintEditor.cshtml'),
  'utf8');

test('briefing deck JSON mutations use the configured antiforgery header and same-origin credentials', () => {
  assert.match(source, /'X-CSRF-TOKEN': token/);
  assert.match(source, /credentials: 'same-origin'/);
  assert.match(source, /'X-Requested-With': 'XMLHttpRequest'/);
});

test('briefing deck project search safely renders server values with textContent', () => {
  assert.match(source, /name\.textContent = project\.projectName/);
  assert.match(source, /meta\.textContent = \[project\.lifecycle/);
  assert.doesNotMatch(source, /project\.projectName.*innerHTML/);
});

test('briefing deck selection tabs support keyboard navigation', () => {
  assert.match(source, /ArrowLeft/);
  assert.match(source, /ArrowRight/);
  assert.match(source, /event\.key === 'Home'/);
  assert.match(source, /event\.key === 'End'/);
  assert.match(source, /aria-selected/);
});

test('briefing deck slide order supports drag and keyboard reordering within stage groups', () => {
  assert.match(source, /window\.Sortable\.create/);
  assert.match(source, /ArrowUp/);
  assert.match(source, /ArrowDown/);
  assert.match(source, /saveProjectOrder/);
  assert.match(source, /sameStage/);
  assert.match(source, /Projects remain grouped by maturity/);
});

test('briefing-specific descriptions are saved without leaving the builder', () => {
  assert.match(source, /data-pbd-description-form/);
  assert.match(source, /requestJson\(root\.dataset\.descriptionUrl/);
  assert.match(source, /Deck-specific capability overview saved/);
});

test('PowerPoint generation downloads the returned pptx and reports slide count', () => {
  assert.match(source, /application\/vnd\.openxmlformats-officedocument\.presentationml\.presentation/);
  assert.match(source, /URL\.createObjectURL/);
  assert.match(source, /X-Project-Briefing-Slides/);
  assert.match(source, /PowerPoint generated successfully/);
});

test('PowerPoint generation prevents duplicate submission and restores the button', () => {
  assert.match(source, /generateButton\.disabled = true/);
  assert.match(source, /generateButton\.disabled = false/);
  assert.match(source, /Generating…|Building editable PowerPoint slides/);
});

test('briefing deck client updates optimistic concurrency after inline mutations', () => {
  assert.match(source, /updateRowVersion\(payload\?\.rowVersion\)/);
  assert.match(source, /input\[name="RowVersion"\]/);
});

test('selected-project filtering reveals the first matching row without changing slide order', () => {
  assert.match(source, /revealFirstFilterMatch/);
  assert.match(source, /applySelectedFilters\(\{ revealFirstMatch: true \}\)/);
  assert.match(source, /sortable\.option\('disabled', filtered\)/);
  assert.match(source, /matching \$\{noun\}/);
});

test('selected-project stage filtering uses canonical stage codes rather than display labels', () => {
  assert.match(source, /row\.dataset\.stageCode === stage/);
  assert.match(source, /new Option\(stage\.label, stage\.code\)/);
});

test('inline membership changes preserve the current page position', () => {
  assert.match(source, /applyEditorState\(payload\?\.deck, \{ preserveScroll: true \}\)/);
  assert.match(source, /window\.scrollTo\(\{ top: scrollTop, behavior: 'auto' \}\)/);
});

test('briefing deck shell collapses the secondary saved-deck rail at laptop widths', () => {
  assert.match(source, /matchMedia\('\(max-width: 1499px\)'\)/);
  assert.match(source, /is-decks-collapsed/);
  assert.match(source, /savedDecksOpen/);
  assert.match(source, /aria-expanded/);
});

test('briefing deck preflight is template-aware and separates supporting metadata', () => {
  assert.match(source, /syncPreflightRequirementVisibility/);
  assert.match(source, /data-pbd-requirement/);
  assert.match(source, /usedGapList/);
  assert.match(source, /additionalGapList/);
  assert.match(source, /Supporting project metadata/);
  assert.doesNotMatch(source, /metric\('update-facts'\)/);
});

test('selected-project management can open the collapsed add-projects workflow', () => {
  assert.match(source, /data-pbd-open-selector/);
  assert.match(source, /selectorDetails\.open = true/);
  assert.match(source, /scrollIntoView\(\{ behavior: 'smooth', block: 'start' \}\)/);
});

test('deck settings use an isolated drawer with canonical dirty-state protection', () => {
  assert.match(source, /data-pbd-settings-drawer/);
  assert.match(source, /serializeSettings/);
  assert.match(source, /setSettingsDirty/);
  assert.match(source, /beforeunload/);
  assert.match(source, /Save or discard settings before generating/);
  assert.match(source, /confirmSettingsDiscard/);
});

test('preflight requirements filter the selected-project list directly', () => {
  assert.match(source, /data-pbd-readiness-filter/);
  assert.match(source, /selectedReadiness\.value = filter/);
  assert.match(source, /applySelectedFilters\(\{ revealFirstMatch: true \}\)/);
  assert.match(source, /selectedSection\?\.scrollIntoView/);
});

test('preflight headline reports affected projects rather than summed metadata values', () => {
  assert.match(source, /affectedProjectCount/);
  assert.match(source, /projects have.*content gaps|project has.*content gaps/);
  assert.doesNotMatch(source, /Review \$\{totalGapCount\} content and metadata gaps/);
});

test('deck settings hide standard-only sections for update sheets and persist collapsible section state', () => {
  assert.match(source, /data-pbd-standard-section/);
  assert.match(source, /settingsCollapsibleSections/);
  assert.match(source, /settingsSections:\$\{currentSettingsLayout\(\)\}/);
  assert.match(source, /restoreSettingsSectionState/);
  assert.match(source, /defaultOpenSettingsSections/);
});

test('settings drawer keeps a consistent appearance section and compact default state', () => {
  assert.doesNotMatch(source, /appearanceTitle\.textContent/);
  assert.match(source, /defaultOpenSettingsSections = \(\) => new Set\(\['content'\]\)/);
});

test('readiness indicators expose accessible labels and professional hover-focus tooltips', () => {
  assert.match(source, /data-pbd-readiness-tip/);
  assert.match(source, /setAttribute\('aria-label', title\)/);
  assert.match(source, /setAttribute\('role', 'img'\)/);
  assert.match(source, /Tooltip\.getOrCreateInstance/);
  assert.match(source, /trigger: 'hover focus'/);
});

test('unsaved settings protect explicit navigation as well as browser and generation paths', () => {
  assert.match(source, /confirmSettingsNavigation/);
  assert.match(source, /Your unsaved deck settings will be lost if you continue/);
  assert.match(source, /document\.addEventListener\('click'/);
  assert.match(source, /document\.addEventListener\('submit'/);
  assert.match(source, /beforeunload/);
  assert.match(source, /Save or discard settings before generating/);
});


test('Project Update Sheet rows can be selected, reordered, validated and reset professionally', () => {
  assert.match(source, /selectedUpdateSheetRowKeys/);
  assert.match(source, /syncUpdateRowOrder/);
  assert.match(source, /restoreUpdateRowOrder/);
  assert.match(source, /validateUpdateSheetRows/);
  assert.match(source, /data-pbd-update-row-up/);
  assert.match(source, /data-pbd-update-row-down/);
  assert.match(source, /dragstart/);
  assert.match(source, /updateRowRecommendedOrder/);
});

test('Project Update Sheet preflight follows selected rows and hide-empty policy', () => {
  assert.match(source, /updateSheetRowIsSelected\('PresentStatus'\)/);
  assert.match(source, /updateSheetRowIsSelected\('ProjectCost'\)/);
  assert.match(source, /HideEmptyUpdateSheetValues/);
  assert.match(source, /arppReferenceAvailableCount/);
  assert.match(source, /PdcOrCompletionStatus/);
});

test('presentation theme previews follow the selected Standard or Project Update Sheet template', () => {
  assert.match(source, /data-pbd-theme-preview/);
  assert.match(source, /preview\.classList\.toggle\('is-update-sheet', updateSheet\)/);
});

test('Standard detailed slides expose project-brief design and independent context choices', () => {
  assert.match(source, /data-pbd-detailed-settings/);
  assert.match(source, /data-pbd-project-brief-layout-settings/);
  assert.match(source, /ShowPresentStatus/);
  assert.match(source, /executiveUsesStatus/);
  assert.match(source, /includesDetailedSlides\(\)/);
  assert.match(source, /includesProjectBrief\(\)/);
});



test('settings initialization does not call institutional functions before declaration', () => {
  const updateRowBlock = source.match(/const syncUpdateRowOrder = \(\) => \{[\s\S]*?const restoreUpdateRowOrder/)?.[0] || '';
  assert.doesNotMatch(updateRowBlock, /syncInstitutionalLayoutSummary/);
  assert.match(source, /syncInstitutionalProfileSettings\(\);/);
});

test('SDD institutional profile uses a dedicated additional-slide editor with isolated dirty state', () => {
  assert.match(source, /data-pbd-profile-drawer/);
  assert.match(source, /openProfileDrawer/);
  assert.match(source, /closeProfileDrawer/);
  assert.match(source, /serializeForm\(profileForm\)/);
  assert.match(source, /setProfileDirty/);
  assert.match(source, /confirmProfileDiscard/);
  assert.match(source, /data-pbd-additional-slide-toggle/);
  assert.match(source, /syncInstitutionalProfileSettings/);
  assert.match(source, /syncInstitutionalModuleOrder/);
  assert.match(source, /restoreInstitutionalModuleOrder/);
  assert.match(source, /validateInstitutionalProfile/);
  assert.match(source, /syncInstitutionalHistoryEditor/);
  assert.match(source, /syncInstitutionalPartnershipEditor/);
  assert.match(source, /syncInstitutionalLayoutSummary/);
  assert.match(source, /footerValid/);
  assert.match(source, /institutionalProfileSlides/);
});


test('additional-slide workspace exposes a registered slide library and ordered slide instances', () => {
  assert.match(viewSource, /pbd-additional-slide-library-modal/);
  assert.match(viewSource, /ProjectBriefingAdditionalSlideType\.InstitutionalProfile/);
  assert.match(viewSource, /ProjectBriefingAdditionalSlideType\.RoleAndCharter/);
  assert.match(viewSource, /data-pbd-additional-slide-list/);
  assert.match(viewSource, /asp-page-handler="ReorderAdditionalSlides"/);
  assert.match(viewSource, /asp-page-handler="RemoveAdditionalSlide"/);
  assert.doesNotMatch(viewSource, /name="IncludeInstitutionalProfile"[\s\S]{0,250}pbd-settings-slide-toggle/);
});

test('Role and Charter uses an isolated structured editor with dirty-state and reorder protection', () => {
  assert.match(additionalSlidesSource, /data-pbd-role-charter-drawer/);
  assert.match(additionalSlidesSource, /confirmRoleCharterDiscard/);
  assert.match(additionalSlidesSource, /createEntry\('role'/);
  assert.match(additionalSlidesSource, /createEntry\('charter'/);
  assert.match(additionalSlidesSource, /data-pbd-additional-slide-handle/);
  assert.match(additionalSlidesSource, /new window\.Sortable/);
  assert.match(additionalSlidesSource, /title: `Remove \$\{name\}\?`/);
});


test('briefing workflows use the reusable PRISM confirmation dialog instead of browser confirmations', () => {
  assert.match(source, /prismConfirm/);
  assert.match(additionalSlidesSource, /prismConfirm/);
  assert.doesNotMatch(source, /window\.confirm|window\.alert/);
  assert.doesNotMatch(additionalSlidesSource, /window\.confirm|window\.alert/);
  assert.match(confirmSource, /data-pbd-confirm-dialog/);
  assert.match(confirmSource, /cancelText = 'Cancel'/);
  assert.match(confirmSource, /ui\.cancel\?\.focus/);
  assert.match(confirmSource, /event\.target === ui\.dialog/);
  assert.match(viewSource, /data-pbd-confirm-dialog/);
  assert.match(viewSource, /data-pbd-confirm-cancel autofocus/);
});

test('additional-slide workspace explains the exhausted slide-library state', () => {
  assert.match(viewSource, /data-pbd-add-slide-disabled-tip/);
  assert.match(viewSource, /All available slide types have been added/);
  assert.match(additionalSlidesSource, /Tooltip\?\.getOrCreateInstance/);
});

test('FFC Global Footprint is an ERP-backed fixed concluding additional slide', () => {
  assert.match(viewSource, /ProjectBriefingAdditionalSlideType\.FfcGlobalFootprint/);
  assert.match(viewSource, /Opening slides/);
  assert.match(viewSource, /Before closing/);
  assert.match(viewSource, /pbd-additional-slide-card__placement/);
  assert.match(viewSource, /Each approved slide type can be added once/);
  assert.match(viewSource, /data-pbd-ffc-footprint-open/);
  assert.match(ffcEditorSource, /FfcFootprintPreviewSummary/);
  assert.match(additionalSlidesSource, /data-pbd-ffc-footprint-drawer/);
  assert.match(additionalSlidesSource, /confirmFfcDiscard/);
  assert.match(additionalSlidesSource, /onMove/);
  assert.match(additionalSlidesSource, /data-can-reorder/);
});
