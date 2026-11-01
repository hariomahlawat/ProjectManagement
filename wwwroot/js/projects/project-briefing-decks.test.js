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

test('studio outline reorders projects only within their present stage', () => {
  // Drag (grip handle) and Alt+Arrow keys move a project inside its stage group; the full order
  // goes to the builder, which saves it and asks the Studio to rebuild.
  assert.match(studioSource, /window\.Sortable\.create\(list, \{\s*handle: '\.pbd-outline__grip'/);
  assert.match(studioSource, /if \(!event\.altKey \|\| \(event\.key !== 'ArrowUp' && event\.key !== 'ArrowDown'\)\) return;/);
  assert.match(studioSource, /new CustomEvent\('pbd:reorder-projects', \{ detail: \{ projectIds: order \} \}\)/);
  assert.match(studioSource, /const canReorder = \(\) => !query && !filterGap;/);
  assert.match(source, /document\.addEventListener\('pbd:reorder-projects'/);
  assert.match(source, /body: JSON\.stringify\(\{ deckId, projectIds, rowVersion: currentRowVersion\(\) \}\)/);
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

test('membership changes refresh the studio from the returned deck', () => {
  assert.match(source, /applyEditorState\(payload\?\.deck\);/);
  assert.match(source, /announceDeckChange\(\{ hasProjects: presented > 0, deck \}\);/);
});

test('briefing deck shell folds the shared-deck list at every width to make room for the Studio', () => {
  assert.match(source, /matchMedia\('\(min-width: 0px\)'\)/);
  assert.match(source, /is-decks-collapsed/);
  assert.match(source, /savedDecksOpen/);
  assert.match(source, /aria-expanded/);
});

test('add projects opens as a dialog whose selections save in place', () => {
  assert.match(source, /addProjectsModal\?\.show\(\);/);
  assert.match(viewSource, /id="pbd-add-projects-modal"/);
  const addSelectionForms = viewSource.match(/asp-page-handler="AddSelection" class="pbd-(quick-card|inline-form|category-form)" data-pbd-add-selection-form/g) || [];
  assert.equal(addSelectionForms.length, 5);
  assert.match(source, /event\.target\.closest\('\[data-pbd-add-selection-form\]'\)/);
  assert.match(source, /'X-Requested-With': 'XMLHttpRequest'/);
  assert.doesNotMatch(viewSource, /data-pbd-selector-details/);
});

test('deck settings save themselves in place, one save at a time', () => {
  const save = source.slice(source.indexOf('const saveSettingsOnce'), source.indexOf('const refreshSettingsDirtyState'));
  // A save already running is joined, and the shared promise settles only after queued changes are saved.
  assert.match(save, /if \(settingsDrain\) \{\s*settingsQueued = true;\s*return settingsDrain;/);
  assert.match(save, /\} while \(settingsQueued\);\s*return saved && serializeSettings\(\) === settingsSavedState;/);
  assert.match(save, /if \(state === settingsSavedState\)/);
  assert.match(save, /'X-Requested-With': 'XMLHttpRequest'/);
  assert.match(save, /applyEditorState\(payload\?\.deck\);/);
  // A collision with someone else's change shows the one conflict banner.
  assert.match(save, /if \(response\.status === 409\) \{\s*showConflict\(payload\?\.message\);/);
  assert.match(source, /settingsForm\?\.addEventListener\('change', \(\) => refreshSettingsDirtyState\(400\)\);/);
  assert.doesNotMatch(source, /data-pbd-settings-drawer|openSettingsDrawer|closeSettingsDrawer/);
  assert.doesNotMatch(viewSource, /data-pbd-settings-drawer|data-pbd-settings-save/);
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

test('pending settings saves finish before navigation, posts and generation', () => {
  assert.match(source, /if \(!settingsPending\(\) && !profileDirty\) return;\s*event\.preventDefault\(\);\s*event\.returnValue = '';/);
  // A failed save keeps the form unsaved, and leaving then asks instead of going ahead.
  assert.match(source, /serializeSettings\(\) !== settingsSavedState\)\);/);
  assert.match(source, /if \(!settingsPending\(\) \|\| await saveSettingsNow\(\)\) return true;\s*const discard = await prismConfirm\(/);
  assert.match(source, /document\.addEventListener\('click'[\s\S]*?if \(!\(await settleSettingsBeforeLeaving\(\)\)\) return;/);
  assert.match(source, /document\.addEventListener\('submit'[\s\S]*?if \(!\(await settleSettingsBeforeLeaving\(\)\)\) return;/);
  assert.match(source, /if \(settingsPending\(\) && !\(await saveSettingsNow\(\)\)\) \{\s*setState\(generateStatus, 'Save the deck settings before generating the PowerPoint\.', 'error'\);/);
});

test('profile and additional-slide posts wait for a pending settings save', () => {
  // The profile posts with the deck's row version, so it waits for the settings save to finish.
  assert.match(source, /if \(settingsPending\(\)\) \{\s*event\.preventDefault\(\);\s*if \(!\(await saveSettingsNow\(\)\)\) \{/);
  assert.match(source, /profileDirty = false;\s*HTMLFormElement\.prototype\.submit\.call\(profileForm\);/);
  // Programmatic slide posts go through the submit guard rather than around it.
  assert.match(source, /if \(form instanceof HTMLFormElement\) form\.requestSubmit\(\);/);
  assert.match(additionalSlidesSource, /additionalSlideOrderForm\.requestSubmit\(\);/);
  assert.doesNotMatch(additionalSlidesSource, /additionalSlideOrderForm\.submit\(\)/);
});

test('in-place deck changes run one at a time with the latest row version', () => {
  assert.match(source, /const run = deckMutationTail\.then\(\(\) => mutate\(\)\);\s*deckMutationTail = run\.catch\(\(\) => \{\}\);/);
  // Settings, presets, membership, quick selections, reorder and capability overviews all queue.
  const queued = source.match(/runDeckMutation\(/g) || [];
  assert.ok(queued.length >= 6, `expected six queued deck changes, found ${queued.length}`);
  const rowVersionReads = source.match(/currentRowVersion\(\)/g) || [];
  // Each read that sends a row version sits inside a queued change (the helper itself defines one).
  for (const match of source.matchAll(/body\.set\('RowVersion', currentRowVersion\(\)\)|rowVersion: currentRowVersion\(\)/g)) {
    const before = source.slice(Math.max(0, match.index - 400), match.index);
    assert.match(before, /runDeckMutation\(/, `row version sent outside the queue near: ${source.slice(match.index, match.index + 60)}`);
  }
  assert.ok(rowVersionReads.length > 0);
});

test('purpose presets apply on the server and the deck tab shows the current one', () => {
  assert.match(viewSource, /@foreach \(var preset in ProjectBriefingDeckPresets\.All\)/);
  assert.match(viewSource, /data-pbd-preset="@preset\.Preset"/);
  assert.match(viewSource, /data-preset-url=/);
  assert.match(source, /body: JSON\.stringify\(\{ deckId, preset: button\.dataset\.pbdPreset, rowVersion: currentRowVersion\(\) \}\)/);
  assert.match(source, /\$\{deck\.presetAdjusted \? ', adjusted' : ''\}/);
});

test('the deck name and description are edited in the header and saved with the settings', () => {
  assert.match(viewSource, /data-pbd-rename-form/);
  assert.match(viewSource, /<input type="hidden" name="Name" value="@deck\.Name" data-pbd-settings-name \/>/);
  const rename = source.slice(source.indexOf("renameForm?.addEventListener('submit'"));
  assert.match(rename, /if \(settingsName\) settingsName\.value = name;[\s\S]*?await saveSettingsNow\(\);/);
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

test('presentation theme previews follow the selected Standard or Project Update Sheet template', () => {
  assert.match(source, /data-pbd-theme-preview/);
  assert.match(source, /preview\.classList\.toggle\('is-update-sheet', updateSheet\)/);
});

test('Standard detailed slides expose project-brief design and independent context choices', () => {
  assert.match(source, /data-pbd-detailed-settings/);
  assert.match(source, /data-pbd-project-brief-layout-settings/);
  assert.match(viewSource, /name="ShowPresentStatus"/);
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

test('stage distribution table remains an explicit dependent option', () => {
  assert.match(viewSource, /data-pbd-stage-summary-toggle/);
  assert.match(viewSource, /data-pbd-stage-table-option/);
  assert.match(source, /syncStageSummaryOptions/);
  assert.match(source, /stageTableToggle\.disabled = !enabled/);
});

const studioSource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-studio.js'),
  'utf8');

test('studio inserts only server-rendered slide HTML and writes every other text as text', () => {
  const innerHtmlUses = studioSource.match(/\.innerHTML\s*=/g) || [];
  assert.equal(innerHtmlUses.length, 1);
  assert.match(studioSource, /frame\.innerHTML = slide\.html;/);
  assert.match(studioSource, /node\.textContent = text;/);
  assert.match(studioSource, /credentials: 'same-origin'/);
  assert.match(viewSource, /data-pbd-studio\s/);
  assert.match(viewSource, /project-briefing-studio\.js/);
  assert.doesNotMatch(viewSource, /project-briefing-preview\.js/);
});

test('in-place deck edits ask the studio to rebuild with the refreshed deck', () => {
  assert.match(source, /new CustomEvent\('pbd:deck-changed'/);
  const announcements = source.match(/announceDeckChange\(/g) || [];
  assert.ok(announcements.length >= 3, 'membership, order and overview saves');
  assert.match(source, /announceDeckChange\(\{ hasProjects: .*, deck \}\)/);
  assert.match(studioSource, /readStudioData\(\{ projects: event\.detail\.deck\.projects, contentGaps: event\.detail\.deck\.contentGaps \}\)/);
});

test('studio drops superseded responses and rebuilds an open viewer strip', () => {
  const load = studioSource.slice(studioSource.indexOf('const loadPreview'), studioSource.indexOf('// Present:'));
  // Any running request is cancelled before the empty-deck path returns.
  assert.ok(load.indexOf('controller?.abort();') < load.indexOf('if (!hasProjects)'));
  assert.match(load, /if \(request\.signal\.aborted\) return;/);
  assert.match(load, /if \(controller === request\)/);
  // An open viewer gets a strip built from the new slide list.
  assert.match(load, /buildStrip\(\);\s*showViewerSlide\(Math\.min\(viewerIndex, slides\.length - 1\)\);/);
});

test('studio reuses the builder save paths instead of its own', () => {
  // Removing a project and editing the deck-specific overview go through the delegated handlers
  // in project-briefing-decks.js, so concurrency and audit stay in one place.
  assert.match(studioSource, /remove\.dataset\.pbdRemoveProject = '';/);
  assert.match(studioSource, /button\.dataset\.pbdEditDescription = '';/);
  assert.doesNotMatch(studioSource, /method: 'POST'/);
  assert.match(source, /event\.target\.closest\('\[data-pbd-remove-project\]'\)/);
  assert.match(source, /event\.target\.closest\('\[data-pbd-edit-description\]'\)/);
});

test('saving a capability overview refreshes the studio project data', () => {
  // The save returns the refreshed deck, every editor button for the project is updated, and the
  // studio re-renders from the new projects and gaps.
  const handler = fs.readFileSync(
    path.resolve(__dirname, '..', '..', '..', 'Pages', 'Workspace', 'BriefingDecks', 'Index.cshtml.cs'),
    'utf8');
  assert.match(handler, /return new JsonResult\(new \{ saved = true, rowVersion, deck \}\);/);
  assert.match(source, /querySelectorAll\(`\[data-pbd-edit-description\]\[data-project-id="\$\{projectId\}"\]`\)\.forEach/);
  assert.match(source, /announceDeckChange\(payload\?\.deck \? \{ deck: payload\.deck \} : \{\}\);/);
  const changed = studioSource.slice(studioSource.indexOf("addEventListener('pbd:deck-changed'"));
  assert.match(changed, /readStudioData\([\s\S]*?renderOutline\(\);\s*renderStage\(\);\s*renderChecksPanel\(\);/);
});

test('an emptied deck clears the slide grid and says why', () => {
  const empty = studioSource.slice(studioSource.indexOf('const renderEmpty'), studioSource.indexOf('const loadPreview'));
  assert.match(empty, /releaseFrames\(gridBox\);\s*gridBox\?\.replaceChildren\(\);/);
  assert.ok(empty.indexOf('renderStage();') < empty.indexOf("slideBox?.replaceChildren(el('p', 'pbd-stage__empty', message));"));
});

test('every slide rebuild also refreshes project facts and gaps', () => {
  const handler = fs.readFileSync(
    path.resolve(__dirname, '..', '..', '..', 'Pages', 'Workspace', 'BriefingDecks', 'Index.cshtml.cs'),
    'utf8');
  const preview = handler.slice(handler.indexOf('OnGetPreviewAsync'), handler.indexOf('OnPostGenerateAsync'));
  assert.match(preview, /projects = deck\?\.Projects,\s*contentGaps = deck\?\.ContentGaps/);
  const load = studioSource.slice(studioSource.indexOf('const loadPreview'), studioSource.indexOf('// Present:'));
  assert.match(load, /readStudioData\(\{ projects: payload\.projects, contentGaps: payload\.contentGaps \}\)/);
  // Facts are read before the outline and checks are drawn from them.
  assert.ok(load.indexOf('readStudioData(') < load.indexOf('renderOutline();'));
});

test('table-only decks list each project against its status-table page', () => {
  const build = studioSource.slice(studioSource.indexOf('const buildEntries'), studioSource.indexOf('const entryTitle'));
  assert.match(build, /if \(!result\.some\(\(entry\) => entry\.section === 'project'\)\)/);
  assert.match(build, /item\.listedProjectIds \|\| \[\]/);
  // The row the person chose stays selected while several projects share one slide.
  assert.match(studioSource, /row\.addEventListener\('click', \(\) => selectIndex\(entry\.indices\[0\], \{ from: 'outline', key: entry\.key \}\)\);/);
  assert.match(studioSource, /if \(chosen && chosen\.indices\.includes\(index\)\) return chosen;/);
});

test('deleted or archived members can still be removed from the deck', () => {
  // They have no slides, so the outline cannot reach them; the notice offers the removal.
  assert.match(viewSource, /data-pbd-left-out-notice[\s\S]*?data-pbd-remove-left-out/);
  assert.match(source, /latestProjects\.filter\(\(project\) => project\.isDeletedOrArchived\)/);
  assert.match(source, /await updateMembership\(\[\], leftOut\.map\(\(project\) => project\.projectId\)\);/);
});

test('individual search follows every membership change and keeps unapplied ticks', () => {
  const apply = source.slice(source.indexOf('const applyEditorState'), source.indexOf('const updateMembership'));
  assert.match(apply, /reconcileSearchMembership\(projects\);/);
  const reconcile = source.slice(source.indexOf('const reconcileSearchMembership'), source.indexOf('const renderSearchResults'));
  assert.match(reconcile, /if \(desired !== resultBaseline\.get\(projectId\)\) pending\.set\(projectId, desired\);/);
  assert.match(reconcile, /isSelected: members\.has\(Number\(project\.projectId\)\)/);
});
