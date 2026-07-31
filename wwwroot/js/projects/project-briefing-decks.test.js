const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const assert = require('node:assert/strict');

const source = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-decks.js'),
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
  assert.match(source, /Discard unsaved deck settings/);
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

test('readiness indicators expose accessible labels and professional hover-focus tooltips', () => {
  assert.match(source, /data-pbd-readiness-tip/);
  assert.match(source, /setAttribute\('aria-label', title\)/);
  assert.match(source, /setAttribute\('role', 'img'\)/);
  assert.match(source, /Tooltip\.getOrCreateInstance/);
  assert.match(source, /trigger: 'hover focus'/);
});

test('unsaved settings protect explicit navigation as well as browser and generation paths', () => {
  assert.match(source, /confirmSettingsNavigation/);
  assert.match(source, /Discard unsaved deck settings and continue/);
  assert.match(source, /document\.addEventListener\('click'/);
  assert.match(source, /document\.addEventListener\('submit'/);
  assert.match(source, /beforeunload/);
  assert.match(source, /Save or discard settings before generating/);
});
