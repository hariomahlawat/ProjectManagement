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
  assert.match(source, /meta\.textContent = \[\.\.\.new Set\(\[project\.lifecycle/);
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

test('add projects opens as a dialog whose selections save in place', () => {
  assert.match(source, /addProjectsModal\?\.show\(\);/);
  assert.match(viewSource, /id="pbd-add-projects-modal"/);
  const addSelectionForms = viewSource.match(/asp-page-handler="AddSelection" class="pbd-(quick-card|inline-form|category-form)" data-pbd-add-selection-form/g) || [];
  // Five selections, and My projects for a Project Officer.
  assert.equal(addSelectionForms.length, 6);
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
  assert.match(save, /if \(reply\.status === 409\) \{\s*showConflict\(replyPayload\?\.message\);/);
  assert.match(source, /settingsForm\?\.addEventListener\('change', \(event\) => \{[\s\S]*?refreshSettingsSummary\(\);\s*refreshSettingsDirtyState\(400\);\s*\}\);/);
  assert.doesNotMatch(source, /data-pbd-settings-drawer|openSettingsDrawer|closeSettingsDrawer/);
  assert.doesNotMatch(viewSource, /data-pbd-settings-drawer|data-pbd-settings-save/);
});

test('deck settings hide the parts chips for fact sheets and persist collapsible section state', () => {
  assert.match(source, /if \(parts\) parts\.hidden = updateSheet;/);
  assert.match(source, /settingsCollapsibleSections/);
  assert.match(source, /settingsSections:\$\{currentSettingsLayout\(\)\}/);
  assert.match(source, /restoreSettingsSectionState/);
  assert.match(source, /defaultOpenSettingsSections/);
});

test('settings are numbered steps: closed by default, one open at a time, each with a summary', () => {
  assert.doesNotMatch(source, /appearanceTitle\.textContent/);
  assert.match(source, /defaultOpenSettingsSections = \(\) => new Set\(\);/);
  // Opening a step closes the others.
  assert.match(source, /if \(!restoringSettingsSections && section\.open\) \{\s*settingsCollapsibleSections\.forEach\(\(other\) => \{\s*if \(other !== section && other\.open\) other\.open = false;/);
  // Visible steps are numbered in order; hidden steps take no number.
  assert.match(source, /if \(!step\.hidden\) number \+= 1;/);
  assert.match(viewSource, /data-pbd-step-summary/);
  // Project slides is always a step: its design and cost chips apply to the status table too.
  assert.doesNotMatch(viewSource, /data-pbd-project-slides-step/);
  assert.doesNotMatch(viewSource, /data-pbd-step="cost"/);
});

test('pending settings saves finish before navigation, posts and generation', () => {
  assert.match(source, /if \(!settingsPending\(\) && !profileDirty && !notesUnsaved\(\)\) return;\s*event\.preventDefault\(\);\s*event\.returnValue = '';/);
  // A failed save keeps the form unsaved, and leaving then asks instead of going ahead.
  assert.match(source, /serializeSettings\(\) !== settingsSavedState\)\);/);
  assert.match(source, /if \(settingsPending\(\) && !\(await saveSettingsNow\(\)\)\) \{\s*const discard = await prismConfirm\(/);
  // Speaker notes the Studio could not save are protected the same way.
  assert.match(source, /return settleNotesBeforeLeaving\(\);/);
  assert.match(source, /const settleNotesBeforeLeaving = async \(\) => \{[\s\S]*?await deckMutationTail;\s*if \(!notesUnsaved\(\)\) return true;\s*const discard = await prismConfirm\(/);
  assert.match(source, /document\.addEventListener\('click'[\s\S]*?if \(!\(await settleSettingsBeforeLeaving\(\)\)\) return;/);
  assert.match(source, /document\.addEventListener\('submit'[\s\S]*?if \(!\(await settleSettingsBeforeLeaving\(\)\)\) return;/);
  assert.match(source, /if \(settingsPending\(\) && !\(await saveSettingsNow\(\)\)\) \{\s*setState\(generateStatus, 'Save the deck settings before generating the PowerPoint\.', 'error'\);/);
});

test('the content editors save in place, after a pending settings save (review 2 fixes, PR 4)', () => {
  // One save path for all three editors: it settles the settings first, then queues the post.
  assert.match(source, /async function sendEditor\(form\) \{[\s\S]*?if \(settingsPending\(\) && !\(await saveSettingsNow\(\)\)\) \{[\s\S]*?runDeckMutation/);
  // It stays on the page, so it never takes the leave-and-discard path.
  assert.doesNotMatch(source.match(/async function sendEditor\(form\) \{[\s\S]*?\n  \}\n/)?.[0] || '', /settleSettingsBeforeLeaving|settingsDiscarded/);
  // Locked while it saves, so an edit made mid-save is not marked saved without being sent.
  assert.match(source, /form\.toggleAttribute\('inert', true\);[\s\S]*?return await sendEditor\(form\);\s*\} finally \{\s*form\.toggleAttribute\('inert', false\);/);
  assert.match(source, /const result = await saveEditorInPlace\(profileForm\);/);
  assert.match(additionalSlidesSource, /const result = await root\.pbdSaveEditor\(form\);/);
  assert.match(additionalSlidesSource, /const result = await root\.pbdSaveEditor\(ffcForm\);/);
  // The drawers and the profile checks follow the slide list, not the page as it was drawn.
  assert.match(source, /label\.textContent = on \? 'In this deck' : 'Not in this deck';/);
  assert.match(source, /data-type="InstitutionalProfile"\] \[data-pbd-block-switch\]'\);\s*const enabled = blockSwitch instanceof HTMLInputElement/);
  // The old additional-slide forms are gone; slides are added and removed in the Deck tab (review fixes PR 3).
  assert.doesNotMatch(additionalSlidesSource, /additionalSlideOrderForm/);
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

test('the old additional-slide remove form and its submit guard are gone', () => {
  assert.doesNotMatch(source, /data-pbd-remove-additional-slide-form|data-pbd-additional-slide-toggle/);
  assert.match(source, /root\.pbdSettleSettings = settleSettingsBeforeLeaving;/);
  assert.doesNotMatch(additionalSlidesSource, /data-pbd-remove-additional-slide-form/);
});

test('the Template picker is gone: a template only starts a new deck', () => {
  assert.doesNotMatch(viewSource, /data-pbd-template-picker/);
  assert.doesNotMatch(viewSource, /customised/);
  assert.doesNotMatch(source, /data-pbd-preset\]/);
  // The new-deck dialog still offers the templates.
  assert.match(viewSource, /@foreach \(var \(preset, index\) in ProjectBriefingDeckPresets\.Templates/);
  assert.match(viewSource, /data-pbd-progress-template>@ProjectBriefingProjectSlideDesigns\.DisplayName\(deck\.ProjectDesign\)/);
});

test('the deck name and description are edited in the header and saved with the settings', () => {
  assert.match(viewSource, /data-pbd-rename-form/);
  assert.match(viewSource, /<input type="hidden" name="Name" value="@deck\.Name" data-pbd-settings-name \/>/);
  const rename = source.slice(source.indexOf("renameForm?.addEventListener('submit'"));
  assert.match(rename, /if \(settingsName\) settingsName\.value = name;[\s\S]*?if \(await saveSettingsNow\(\)\)/);
  // A refused rename restores the saved name so later settings saves are not blocked by it.
  assert.match(rename, /if \(settingsName\) settingsName\.value = previousName;/);
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

test('project slides: one design for every project, from cards that set the deck settings', () => {
  assert.match(viewSource, /@foreach \(var design in designChoices\)/);
  assert.match(viewSource, /name="ProjectDesign" value="@design"/);
  // Capability and brief stays only for decks that already use it.
  assert.match(viewSource, /if \(currentDesign == ProjectBriefingProjectSlideDesign\.CapabilityAndBrief\)/);
  // The card carries the design's rules (ProjectBriefingProjectSlideDesigns); the script reads them.
  assert.match(viewSource, /data-layout="@ProjectBriefingProjectSlideDesigns\.Layout\(design\)"/);
  assert.match(viewSource, /data-hint="@ProjectBriefingProjectSlideDesigns\.Hint\(design\)"/);
  assert.match(source, /setRadio\('Layout', card\.dataset\.layout \|\| 'StandardBriefing'\);/);
  assert.match(source, /keyPointsChip\.checked = card\.dataset\.keyPoints === 'on';/);
  assert.match(viewSource, /name="ShowPresentStatus"/);
});

test('design rules live on the server: the script names no design', () => {
  for (const name of ['Capability', 'KeyPoints', 'KeyPointsOnly', 'Brief', 'FactSheet', 'CapabilityAndBrief']) {
    assert.doesNotMatch(source, new RegExp(`'${name}'`), name);
  }
  assert.doesNotMatch(source, /No brief or capability text/);
  assert.doesNotMatch(studioSource, /NOTES_MAX = 4000/);
  assert.match(studioSource, /const NOTES_MAX = Number\.parseInt\(root\.dataset\.speakerNotesMax/);
  assert.match(viewSource, /data-speaker-notes-max="@ProjectBriefingDeckService\.SpeakerNotesMaxLength"/);
});

test('key points: a chip that swaps Capability and Key points, a band on Brief, fixed on Key points only', () => {
  assert.match(viewSource, /name="ShowKeyPoints"/);
  assert.match(viewSource, /name="KeyPointSlide" value="ProjectBriefSlide"/);
  assert.match(viewSource, /name="KeyPointSlide" value="CapabilitySlide"/);
  assert.match(source, /const swap = designRule\(keyPointsChip\.checked \? 'withKeyPoints' : 'withoutKeyPoints'\);/);
  assert.match(source, /keyPointsToggle\.disabled = designRule\('keyPointsLocked'\) === 'true';/);
  assert.match(source, /keyPointSlide\.hidden = !keyPointsOn \|\| designRule\('keyPointSlide'\) !== 'true';/);
});

test('the brief layout shows for brief designs only while key points are off (the key points layout draws its own)', () => {
  assert.match(viewSource, /data-pbd-project-brief-layout-settings hidden="@\(isUpdateSheet \|\| !ProjectBriefingProjectSlideDesigns\.UsesBriefLayout\(currentDesign\) \|\| keyPointsOn\)"/);
  assert.match(source, /projectBriefLayoutSettings\.hidden = updateSheet \|\| designRule\('briefLayout'\) !== 'true' \|\| keyPointsOn;/);
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
  assert.match(source, /syncInstitutionalProfileSettings/);
  assert.match(source, /syncInstitutionalModuleOrder/);
  assert.match(source, /restoreInstitutionalModuleOrder/);
  assert.match(source, /validateInstitutionalProfile/);
  assert.match(source, /syncInstitutionalHistoryEditor/);
  assert.match(source, /syncInstitutionalPartnershipEditor/);
  assert.match(source, /syncInstitutionalLayoutSummary/);
  assert.match(source, /footerValid/);
});


test('institution slides are blocks in the deck list; "+ Slide" opens the Add slide library', () => {
  // One way to add a slide (review fixes PR 3): the old Slide library dialog is gone.
  assert.doesNotMatch(viewSource, /pbd-additional-slide-library-modal|asp-page-handler="ToggleAdditionalSlide"/);
  assert.match(viewSource, /data-pbd-open-block-library/);
  assert.match(source, /root\.querySelectorAll\('\[data-pbd-open-block-library\]'\)/);
  // Adding the status table shows the table in the preview.
  assert.match(source, /StatusTable: 'StatusTableBlock',/);
  assert.match(studioSource, /StatusTableBlock: \(\) => \['StatusTable'\],/);
  assert.match(viewSource, /ProjectBriefingDeckBlockType\.InstitutionalProfile/);
  assert.match(viewSource, /ProjectBriefingDeckBlockType\.RoleAndCharter/);
  assert.match(viewSource, /data-pbd-block-zone/);
  assert.doesNotMatch(viewSource, /name="IncludeInstitutionalProfile"[\s\S]{0,250}pbd-settings-slide-toggle/);
});

test('Role and Charter uses an isolated structured editor with dirty-state and reorder protection', () => {
  assert.match(additionalSlidesSource, /data-pbd-role-charter-drawer/);
  assert.match(additionalSlidesSource, /confirmRoleCharterDiscard/);
  assert.match(additionalSlidesSource, /createEntry\('role'/);
  assert.match(additionalSlidesSource, /createEntry\('charter'/);
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
  // The shared PRISM dialog takes over when it is on the page, keeping Cancel focused.
  assert.match(confirmSource, /window\.PrismConfirm\?\.show/);
  assert.match(confirmSource, /focus: 'cancel'/);
});

test('a save keeps the row version it produced, not a re-read deck\'s (review 2 fixes, PR 3)', () => {
  assert.doesNotMatch(source, /updateRowVersion\([^)]*deck\?*\.rowVersion\)/);
  assert.match(source, /updateRowVersion\(replyPayload\?\.rowVersion\);/);
  // A conflict while adding projects shows the conflict banner like every other save.
  assert.match(source, /throw new RequestError\(reply\?\.message \|\| `Projects could not be added/);
  assert.match(source, /if \(error instanceof RequestError && error\.status === 409\) showConflict\(error\.message\);\s*setState\(addProjectsStatus/);
});

test('one word for the pieces of a deck: slide (review fixes PR 5)', () => {
  assert.doesNotMatch(viewSource, /Add block/);
  assert.match(viewSource, /<\/i> Add slide/);
  // The cost chips say they also set the status table's columns.
  assert.match(viewSource, /The cost choice also sets the status table's cost columns\./);
  assert.match(studioSource, /label: 'Category distribution'/);
});

test('a refused add returns focus to Add slide and points an institution slide to its Edit', () => {
  assert.match(source, /if \(refusedAdd\) blockList\?\.querySelector\('\[data-pbd-block-library-toggle\]'\)\?\.focus\(\);/);
  assert.match(source, /Use Edit beside it under Add slide to set it up first\./);
  // The editor still offers every module (Partnerships is only left out of the defaults).
  assert.match(fs.readFileSync(path.resolve(__dirname, '..', '..', '..', 'Pages', 'Workspace', 'BriefingDecks', '_InstitutionalProfileEditor.cshtml'), 'utf8'), /\.Concat\(ProjectBriefingInstitutionalProfileOptions\.AllModules\)/);
});

test('add block: optional blocks are removed from the list and added back from a library', () => {
  // Only optional blocks (not the cover or project slides) get Remove and wait in the library.
  assert.match(viewSource, /var optional = ProjectBriefingDeckBlocks\.IsOptional\(block\.Type\);/);
  assert.match(viewSource, /hidden="@\(optional && !block\.Enabled\)"/);
  assert.match(viewSource, /data-pbd-block-remove aria-label="Remove @name from the deck"/);
  assert.match(viewSource, /data-pbd-block-library-entry="@block\.Type" hidden="@block\.Enabled"/);
  // A group with nothing to add is hidden from the first render, not only after a change.
  assert.match(viewSource, /data-pbd-block-library-group hidden="@groupBlocks\.All\(block => block\.Enabled\)"/);
  assert.match(viewSource, /data-pbd-block-add="@block\.Type"/);
  // An institution slide can be edited from the library before it is added (the SDD profile must
  // pass its checks to be added).
  assert.match(viewSource, /data-pbd-profile-open aria-controls="pbd-institutional-profile-drawer" aria-expanded="false" aria-label="Edit @libraryName before adding it"/);
  assert.match(viewSource, /aria-controls="pbd-block-library" data-pbd-block-library-toggle/);
  // Both go through the block save a switch uses, so a refused change is put back the same way.
  assert.match(source, /input\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\);/);
  assert.match(source, /if \(row\.dataset\.optional === 'true'\) row\.hidden = !on;/);
  // Moving and dragging skip the blocks waiting in the library.
  assert.match(source, /while \(sibling instanceof HTMLElement && sibling\.hidden\) sibling = step\(sibling\);/);
  assert.match(source, /draggable: '\[data-pbd-block\]\[data-movable="true"\]:not\(\[hidden\]\)'/);
  // A save in progress refuses Remove and Add as it refuses a switch.
  assert.match(source, /\[data-pbd-block-remove\], \[data-pbd-block-add\]'\)\) \{\s*event\.preventDefault\(\);\s*event\.stopPropagation\(\);/);
});

test('slides in this deck: one switchable, reorderable block list saved in place', () => {
  assert.doesNotMatch(viewSource, /data-pbd-step="extra"/);
  const slidesStep = viewSource.slice(viewSource.indexOf('data-pbd-step="slides"'), viewSource.indexOf('data-pbd-step="project"'));
  assert.match(slidesStep, /data-pbd-blocks/);
  assert.match(slidesStep, /data-pbd-block-switch/);
  assert.match(slidesStep, /data-pbd-block-handle/);
  // The hidden mirror inputs are gone: nothing read them, and the server derives the settings (review fixes PR 8).
  assert.doesNotMatch(viewSource, /data-pbd-block-mirror/);
  assert.doesNotMatch(source, /The hidden inputs moved with a saved change/);
  assert.match(source, /event\.detail\.changed\.includes\('Layout'\)\) window\.location\.reload\(\)/);
  assert.match(viewSource, /data-blocks-url="@Url\.Page\("\/Workspace\/BriefingDecks\/Index", "UpdateBlocks"\)"/);
  assert.match(source, /body: JSON\.stringify\(\{ deckId, rowVersion: currentRowVersion\(\), blocks: readBlocks\(\) \}\)/);
  assert.match(source, /restoreBlocks\(before\)/);
  // One block save at a time, so a failed save cannot undo a newer edit.
  assert.match(source, /if \(blocksBusy\) return;/);
  assert.match(source, /sorter\.option\('disabled', busy\)/);
  assert.match(source, /finally \{\s*setBlocksBusy\(false\);/);
  assert.match(source, /event\.key === 'ArrowUp' \? -1 : 1/);
  assert.match(source, /slides: blocksOn\(\)\.join/);
});

test('FFC Global Footprint is an ERP-backed fixed concluding additional slide', () => {
  assert.match(viewSource, /ProjectBriefingDeckBlockType\.FfcGlobalFootprint/);
  assert.match(viewSource, /Label: "Opening"/);
  assert.match(viewSource, /Label: "Closing"/);
  assert.match(viewSource, /title="Fixed in its place"/);
  assert.match(viewSource, /data-pbd-ffc-footprint-open/);
  assert.match(ffcEditorSource, /FfcFootprintPreviewSummary/);
  assert.match(additionalSlidesSource, /data-pbd-ffc-footprint-drawer/);
  assert.match(additionalSlidesSource, /confirmFfcDiscard/);
});

test('the stage-wise distribution replaced the stage summary, its table and the portfolio summary', () => {
  assert.doesNotMatch(viewSource, /data-pbd-stage-table-option|IncludeStageDistributionTable/);
  assert.doesNotMatch(source, /syncStageSummaryOptions|PortfolioSummary/);
  assert.match(source, /StageDistribution: 'IncludeStageSummary',/);
  assert.doesNotMatch(source, /setChecked\(|const narrative = \{ Capability:/);
  assert.match(studioSource, /StageDistributionList: \{ section: 'portfolio'/);
  assert.match(studioSource, /IncludeStageSummary: \(\) => \['StageDistribution'\],/);
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
  assert.match(studioSource, /readStudioData\(\{ projects: event\.detail\.deck\.projects, contentGaps: event\.detail\.deck\.contentGaps, projectDesign: event\.detail\.deck\.projectDesign \}\)/);
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
  assert.ok(empty.indexOf('renderStage();') < empty.indexOf('slideBox?.replaceChildren(empty);'));
  // One empty state: a heading, why, and (with no projects at all) the one action.
  assert.match(empty, /el\('h4', null, 'Add the projects to brief'\),\s*el\('p', null, message\)\)/);
  assert.match(empty, /if \(!projects\.size\) \{[\s\S]*?add\.dataset\.pbdOpenSelector = '';/);
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

test('projects without slides of their own are listed against the table page that shows them', () => {
  const build = studioSource.slice(studioSource.indexOf('const buildEntries'), studioSource.indexOf('const presentationTag'));
  // Any project not yet listed (an executive-table deck, or a table row only project) points at its table page.
  assert.match(build, /\(item\.listedProjectIds \|\| \[\]\)\.forEach\(\(projectId\) => \{\s*if \(byProject\.has\(projectId\)\) return;/);
  assert.doesNotMatch(build, /if \(!result\.some\(\(entry\) => entry\.section === 'project'\)\)/);
  // The row the person chose stays selected while several projects share one slide.
  assert.match(studioSource, /row\.addEventListener\('click', \(\) => \{\s*selectEntry\(entry, \{ openSlideTab: true \}\);/);
  assert.match(studioSource, /selectIndex\(entry\.indices\[0\], \{ from: 'outline', key: entry\.key \}\);/);
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

test('each project can be shown as slides, a table row only, or left out', () => {
  // The Studio asks; the builder saves through the deck mutation queue.
  assert.match(studioSource, /const PRESENTATIONS = \['Slides', 'TableRowOnly', 'LeftOut'\];/);
  assert.match(studioSource, /new CustomEvent\('pbd:set-project-presentation', \{ detail: \{ projectId: project\.projectId, presentation: value, previous \} \}\)/);
  assert.match(source, /document\.addEventListener\('pbd:set-project-presentation'[\s\S]*?runDeckMutation\(async \(\) => \{[\s\S]*?root\.dataset\.itemPresentationUrl/);
  assert.match(viewSource, /data-item-presentation-url="@Url\.Page\("\/Workspace\/BriefingDecks\/Index", "UpdateItemPresentation"\)"/);
  // Projects not in the PowerPoint are listed last, with no slide, so they can be brought back.
  assert.match(studioSource, /\.filter\(\(project\) => !project\.isPresented && !byProject\.has\(project\.projectId\)\)/);
  assert.match(studioSource, /section: 'leftout', projectId: project\.projectId, role: '', indices: \[\]/);
  assert.match(studioSource, /if \(chosen && \(chosen\.indices\.includes\(index\) \|\| chosen\.indices\.length === 0\)\) return chosen;/);
  // Counts and checks follow what is actually in the PowerPoint.
  assert.match(studioSource, /filter\(\(project\) => project\.isPresented\)/);
  assert.match(source, /projects\.filter\(\(project\) => project\.isPresented\)\.length/);
});

test('a deck whose projects are all left out still lists them so they can be brought back', () => {
  const empty = studioSource.slice(studioSource.indexOf('const renderEmpty'), studioSource.indexOf('const loadPreview'));
  assert.match(empty, /entries = buildEntries\(\[\]\);/);
  assert.match(empty, /if \(entries\.length\) \{[\s\S]*?renderOutline\(\);/);
  assert.match(studioSource, /'Every project in this deck is left out of the PowerPoint\. Bring one back to build the slides\.'/);
  // The no-slide entry is shown before the empty-deck branch, and the inspector accepts it without a slide.
  const stage = studioSource.slice(studioSource.indexOf('const renderStage'), studioSource.indexOf('const selectEntry'));
  assert.ok(stage.indexOf('entry.indices.length === 0') < stage.indexOf('if (!slide) {'));
  assert.match(studioSource, /if \(!entry \|\| \(!slide && entry\.indices\.length\)\) \{/);
});

test('a refused presentation change puts back the saved choice before rebuilding', () => {
  assert.match(studioSource, /presentation: value, previous \} \}\)\);/);
  assert.match(studioSource, /document\.addEventListener\('pbd:project-presentation-failed'[\s\S]*?project\.presentation = event\.detail\.previous;/);
  assert.match(source, /new CustomEvent\('pbd:project-presentation-failed', \{\s*detail: \{ projectId, previous: event\.detail\?\.previous \}\s*\}\)\);\s*announceDeckChange\(\{\}\);/);
});

test('the Studio chooses the photograph a deck shows, saved through the deck queue', () => {
  // Only projects shown with their own slides in a format that uses photos get the picker.
  assert.match(studioSource, /const usesPhoto = \(project\) => !project\.isDeletedOrArchived\s*&& \(project\.presentation \|\| 'Slides'\) === 'Slides'/);
  assert.match(studioSource, /if \(usesPhoto\(project\)\) nodes\.push\(photoSection\(project\)\);/);
  // The cover is stored as no choice, so the deck follows the project's cover.
  assert.match(studioSource, /const photoId = photo\.isDefault \? null : photo\.photoId;/);
  assert.match(studioSource, /url\.searchParams\.set\('projectId', String\(project\.projectId\)\);/);
  assert.match(source, /document\.addEventListener\('pbd:set-project-photo'[\s\S]*?runDeckMutation\(async \(\) => \{[\s\S]*?root\.dataset\.itemPhotoUrl/);
  assert.match(source, /new CustomEvent\('pbd:project-photo-failed'/);
  assert.match(viewSource, /data-photo-options-url="@Url\.Page\("\/Workspace\/BriefingDecks\/Index", "PhotoOptions"\)"/);
});

test('speaker notes are edited in the inspector and saved through the deck queue', () => {
  assert.match(studioSource, /if \(hasOwnSlides\(project\)\) nodes\.push\(notesSection\(project\)\);/);
  // Saving on blur refreshes the notes box in place, so a click on the next control is not lost.
  assert.match(studioSource, /field\.addEventListener\('blur', \(\) => saveNotes\(project, refresh\)\);/);
  assert.match(studioSource, /new CustomEvent\('pbd:set-project-notes', \{ detail: \{ projectId: project\.projectId, notes: text \} \}\)/);
  // A draft survives re-rendering until the saved notes come back with the deck.
  assert.match(studioSource, /const notesText = \(project\) => notesDrafts\.get\(project\.projectId\)\?\.text \?\? \(project\.speakerNotes \|\| ''\);/);
  // Each project keeps its own draft, so a failed save is not lost by typing notes elsewhere.
  assert.match(studioSource, /const notesDrafts = new Map\(\);/);
  assert.match(studioSource, /const unsaved = \[\.\.\.notesDrafts\]\.some\(/);
  assert.match(source, /document\.addEventListener\('pbd:set-project-notes'[\s\S]*?runDeckMutation\(async \(\) => \{[\s\S]*?root\.dataset\.speakerNotesUrl/);
  assert.match(viewSource, /data-speaker-notes-url="@Url\.Page\("\/Workspace\/BriefingDecks\/Index", "UpdateSpeakerNotes"\)"/);
});

test('generating waits for in-place saves and refuses unsaved speaker notes', () => {
  const generate = source.slice(source.indexOf("generateForm?.addEventListener('submit'"));
  assert.match(generate, /await deckMutationTail;\s*if \(document\.querySelector\('\[data-pbd-studio\]\[data-unsaved-notes="true"\]'\)\) \{\s*setState\(generateStatus, 'Save the speaker notes before generating the PowerPoint\.', 'error'\);/);
  assert.match(studioSource, /root\.dataset\.unsavedNotes = String\(unsaved\);/);
  assert.match(studioSource, /notesDrafts\.set\(project\.projectId, \{ text: field\.value, saving: false, error: '' \}\);\s*markUnsavedNotes\(\);/);
});

test('segmented choices show help for the selected option only, refreshed with the summaries', () => {
  assert.match(source, /const refreshSegmentHelp = \(\) => \{/);
  assert.match(source, /help\.textContent = settingsForm\.querySelector\(`input\[name="\$\{name\}"\]:checked`\)\?\.dataset\.help \|\| '';/);
  assert.match(source, /if \(!settingsForm\) return;\s*refreshSegmentHelp\(\);/);
  // Cost is two chips that set the hidden CostMode choice.
  assert.match(viewSource, /data-pbd-cost-part="rd"/);
  assert.match(source, /setRadio\('CostMode', rd && proliferation \? 'Both' : rd \? 'CostRdOnly' : proliferation \? 'ProliferationOnly' : 'None'\);/);
});

test('step summaries read each choice by its label, not its description', () => {
  assert.match(source, /return input\?\.dataset\.label \|\| /);
  assert.match(viewSource, /name="PresentationTheme" value="EditorialLight"[^>]*data-label="Editorial Light"/);
  assert.match(viewSource, /name="PresentationTheme" value="GraphiteDark"[^>]*data-label="Graphite Dark"/);
});

test('a settings save names the settings that changed, for the preview to follow', () => {
  assert.match(source, /if \(name && event\.target\.form === settingsForm\) \{\s*changedSettings\.delete\(name\);\s*changedSettings\.add\(name\);/);
  assert.match(source, /if \(changed\.length\) \{\s*document\.dispatchEvent\(new CustomEvent\('pbd:settings-saved', \{ detail: \{ changed \} \}\)\);/);
});

test('settings changed by script (row order, Select all) count as changed for the preview', () => {
  const save = source.slice(source.indexOf('const saveSettingsOnce'), source.indexOf('const refreshSettingsDirtyState'));
  assert.match(save, /const moved = movedSettings\(settingsSavedState, state\);/);
  // Names moved by script first, then those edited by hand in the order they were edited.
  assert.match(save, /\.\.\.moved\.filter\(\(name\) => !changedSettings\.has\(name\)\),\s*\.\.\.\[\.\.\.changedSettings\]\.filter\(\(name\) => moved\.includes\(name\)\)/);
  // The diff itself: a sorted name=value list, compared per name.
  const diff = source.slice(source.indexOf('const movedSettings'), source.indexOf('const settingsDrainPending') > 0 ? source.indexOf('const settingsDrainPending') : undefined);
  const movedSettings = new Function(`${diff.slice(0, diff.indexOf('};', diff.indexOf('.filter((name)')) + 2)}; return movedSettings;`)();
  assert.deepEqual(
    movedSettings('A=1&UpdateSheetRowOrder=ProjectCost,AonDate&UpdateSheetRows=AonDate', 'A=1&UpdateSheetRowOrder=AonDate,ProjectCost&UpdateSheetRows=AonDate&UpdateSheetRows=ProjectCost'),
    ['UpdateSheetRowOrder', 'UpdateSheetRows']);
});
