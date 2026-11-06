// Behaviour tests for the Briefing Studio: the real script runs in jsdom against a small copy of
// the Studio markup, with the preview endpoint faked. Unlike the source-text checks in
// project-briefing-decks.test.js, these exercise what a person sees and does.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const studioSource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-studio.js'),
  'utf8');

const project = (projectId, projectName, presentStage, extra = {}) => ({
  projectId,
  projectName,
  presentStage,
  sortOrder: projectId * 10,
  isPresented: true,
  presentation: 'Slides',
  speakerNotes: '',
  capabilityStatements: [],
  ...extra
});

// Three projects in the PowerPoint, one left out. Gaps: two kinds among the presented projects
// (status twice, photo once); the left-out project's gap must not count anywhere.
const PROJECTS = [
  project(1, 'Alpha simulator', 'Development'),
  project(2, 'Bravo trainer', 'Development'),
  project(3, 'Charlie range', 'Trials'),
  project(4, 'Delta kit', 'Trials', { isPresented: false, presentation: 'LeftOut' })
];
const GAPS = { 1: ['status', 'photo'], 2: ['status'], 4: ['costRd'] };

const slide = (number, title) => ({ number, title, html: `<div class="pbd-slide-stage" style="width:1280px;height:720px;">${title}</div>` });
const payload = (extra = {}) => ({
  slideCount: 5,
  generatedAt: '04 Nov 2026, 16:54',
  slides: [slide(1, 'Cover'), slide(2, 'Alpha simulator'), slide(3, 'Bravo trainer'), slide(4, 'Charlie range'), slide(5, 'Closing')],
  images: {},
  outline: [
    { number: 1, role: 'Cover', projectId: null, listedProjectIds: [] },
    { number: 2, role: 'ProjectCapability', projectId: 1, listedProjectIds: [] },
    { number: 3, role: 'ProjectCapability', projectId: 2, listedProjectIds: [] },
    { number: 4, role: 'ProjectCapability', projectId: 3, listedProjectIds: [] },
    { number: 5, role: 'Closing', projectId: null, listedProjectIds: [] }
  ],
  projects: PROJECTS.map((item) => ({ ...item })),
  contentGaps: GAPS,
  ...extra
});

const MARKUP = `
<div data-pbd-root>
  <nav data-pbd-progress><ol>
    <li data-pbd-progress-step="projects"><button type="button"><span data-pbd-progress-projects></span></button></li>
    <li data-pbd-progress-step="checks"><button type="button" data-pbd-progress-tab="checks"><span data-pbd-progress-checks></span></button></li>
    <li data-pbd-progress-step="generate"><button type="button" data-pbd-progress-generate>Generate</button></li>
  </ol></nav>
  <div data-deck-id="7">
    <section data-pbd-studio data-preview-url="/preview" data-has-projects="true"
             data-layout="StandardBriefing" data-presentation-mode="Combined" data-uses-capability="false">
      <script type="application/json" data-pbd-studio-data>${JSON.stringify({ projects: PROJECTS, contentGaps: GAPS })}</script>
      <span data-pbd-studio-count></span>
      <input type="search" data-pbd-outline-search />
      <div data-pbd-outline-filter hidden><span data-pbd-outline-filter-text></span><button type="button" data-pbd-outline-filter-clear>Show all</button></div>
      <nav data-pbd-outline></nav>
      <span data-pbd-stage-meta></span><h3 data-pbd-stage-title></h3>
      <button type="button" data-pbd-stage-view="slide">Slide</button>
      <button type="button" data-pbd-stage-view="grid">All slides</button>
      <button type="button" data-pbd-stage-prev>Prev</button>
      <button type="button" data-pbd-stage-next>Next</button>
      <button type="button" data-pbd-preview-refresh>Rebuild</button>
      <button type="button" data-pbd-preview-open disabled>Present</button>
      <p data-pbd-preview-status></p>
      <div data-pbd-stage-slide></div><ol data-pbd-stage-grid hidden></ol>
      <div role="tablist">
        <button type="button" role="tab" data-pbd-inspector-tab="slide">This slide</button>
        <button type="button" role="tab" data-pbd-inspector-tab="deck">Deck</button>
        <button type="button" role="tab" data-pbd-inspector-tab="checks">Checks <span data-pbd-checks-badge hidden></span></button>
      </div>
      <div data-pbd-inspector-panel="slide"></div>
      <div data-pbd-inspector-panel="deck" hidden></div>
      <div data-pbd-inspector-panel="checks" hidden></div>
    </section>
    <dialog data-pbd-viewer>
      <h2 data-pbd-viewer-title></h2><span data-pbd-viewer-counter></span>
      <button type="button" data-pbd-viewer-first>From the start</button>
      <button type="button" aria-pressed="false" data-pbd-viewer-notes-toggle>Notes</button>
      <button type="button" aria-pressed="false" data-pbd-viewer-strip-toggle>Slides</button>
      <button type="button" data-pbd-viewer-close>End</button>
      <button type="button" data-pbd-viewer-prev>Prev</button>
      <div data-pbd-viewer-stage></div>
      <button type="button" data-pbd-viewer-next>Next</button>
      <section data-pbd-viewer-notes hidden><p data-pbd-viewer-notes-text></p></section>
      <ol data-pbd-viewer-strip hidden></ol>
    </dialog>
  </div>
</div>`;

// Each fetch waits until the test answers it, so the order of responses can be controlled.
const startStudio = () => {
  const dom = new JSDOM(`<!doctype html><html><body>${MARKUP}</body></html>`, {
    url: 'http://localhost/Workspace/BriefingDecks/7',
    runScripts: 'outside-only',
    pretendToBeVisual: true
  });
  const { window } = dom;
  const requests = [];
  window.fetch = (url, options = {}) => new Promise((resolve) => {
    requests.push({
      url,
      headers: options.headers || {},
      answer: (body, status = 200) => resolve({
        ok: status >= 200 && status < 300,
        status,
        json: async () => body
      })
    });
  });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  // jsdom has no modal dialogs or full screen: a plain open/close stand-in, and no requestFullscreen.
  if (!window.Element.prototype.scrollIntoView) window.Element.prototype.scrollIntoView = () => {};
  window.HTMLDialogElement.prototype.showModal = function showModal() { this.open = true; };
  window.HTMLDialogElement.prototype.close = function close() {
    this.open = false;
    this.dispatchEvent(new window.Event('close'));
  };
  if (!window.CSS) window.CSS = {};
  if (!window.CSS.escape) window.CSS.escape = (value) => String(value).replace(/["\\]/g, '\\$&');
  window.eval(studioSource);
  return { window, document: window.document, requests };
};

const settle = async (window) => {
  for (let i = 0; i < 5; i += 1) await new Promise((resolve) => window.setTimeout(resolve, 0));
};

const outlineRow = (document, name) => [...document.querySelectorAll('.pbd-outline__row')]
  .find((row) => row.textContent.includes(name));

test('the outline groups slides by section and present stage, and lists left-out projects last', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);

  const groups = [...document.querySelectorAll('.pbd-outline__group-label')].map((node) => node.textContent);
  assert.deepEqual(groups, ['Opening', 'Development', 'Trials', 'Closing', 'Not in the PowerPoint']);
  assert.ok(outlineRow(document, 'Delta kit'), 'the left-out project is still listed so it can be brought back');
});

test('the Checks badge counts the checks to review, not projects, and ignores left-out projects', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);

  const badge = document.querySelector('[data-pbd-checks-badge]');
  assert.equal(badge.hidden, false);
  assert.equal(badge.textContent, '2');
  assert.equal(badge.getAttribute('aria-label'), '2 checks to review');
  const alphaBadge = outlineRow(document, 'Alpha simulator').querySelector('.pbd-outline__gaps');
  assert.equal(alphaBadge.querySelector('[aria-hidden="true"]').textContent, '2');
  assert.equal(alphaBadge.querySelector('.visually-hidden').textContent, ', 2 items missing: status remark, photograph');
  assert.match(alphaBadge.title, /^2 items missing: status remark, photograph\. The PowerPoint shows a placeholder/);
  assert.equal(outlineRow(document, 'Delta kit').querySelector('.pbd-outline__gaps'), null);
});

test('toggling a group from the keyboard keeps focus on that group header', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);

  const header = [...document.querySelectorAll('.pbd-outline__group-toggle')]
    .find((node) => node.textContent.includes('Development'));
  header.focus();
  header.click();

  const focused = document.activeElement;
  assert.notEqual(focused, document.body);
  assert.equal(focused.dataset.pbdFocusKey, header.dataset.pbdFocusKey);
  assert.equal(focused.getAttribute('aria-expanded'), 'false');
  assert.equal(outlineRow(document, 'Alpha simulator'), undefined, 'the group folded');
});

test('a rebuild that lands while typing speaker notes keeps the text, focus and caret', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);
  outlineRow(document, 'Bravo trainer').click();

  let notes = document.querySelector('#pbd-speaker-notes-2');
  assert.ok(notes, 'the notes box is shown for a project with its own slides');
  notes.focus();
  notes.value = 'Mention the field trial';
  notes.dispatchEvent(new window.Event('input', { bubbles: true }));
  notes.setSelectionRange(8, 8);

  document.querySelector('[data-pbd-preview-refresh]').click();
  requests[1].answer(payload());
  await settle(window);

  notes = document.querySelector('#pbd-speaker-notes-2');
  assert.equal(document.activeElement, notes);
  assert.equal(notes.value, 'Mention the field trial');
  assert.equal(notes.selectionStart, 8);
});

test('leaving the notes box saves without rebuilding the panel under the pointer', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);
  outlineRow(document, 'Bravo trainer').click();

  const saved = [];
  // Copy out of jsdom's realm so deepEqual compares values, not prototypes.
  document.addEventListener('pbd:set-project-notes', (event) => saved.push(JSON.parse(JSON.stringify(event.detail))));
  const notes = document.querySelector('#pbd-speaker-notes-2');
  const removeButton = document.querySelector('[data-pbd-focus-key="remove-project"]');
  notes.focus();
  notes.value = 'Short note';
  notes.dispatchEvent(new window.Event('input', { bubbles: true }));
  notes.blur();

  assert.deepEqual(saved, [{ projectId: 2, notes: 'Short note' }]);
  assert.ok(removeButton.isConnected, 'the other inspector controls were not replaced');
  assert.equal(notes.disabled, true, 'the box shows it is saving');
});

test('an older preview response that arrives last does not replace newer slides', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);

  // A rebuild starts, then an in-place save announces a change before it finishes: the Studio
  // starts a newer rebuild (after its short delay) and must ignore the first one's late answer.
  document.querySelector('[data-pbd-preview-refresh]').click();
  document.dispatchEvent(new window.CustomEvent('pbd:deck-changed', { detail: {} }));
  await new Promise((resolve) => window.setTimeout(resolve, 950));
  assert.equal(requests.length, 3);
  const newer = payload({ slides: [slide(1, 'Newer cover')], outline: [{ number: 1, role: 'Cover', projectId: null, listedProjectIds: [] }] });
  requests[2].answer(newer);
  await settle(window);
  requests[1].answer(payload());
  await settle(window);

  assert.match(document.querySelector('[data-pbd-stage-slide]').textContent, /Newer cover/);
});

test('a rebuild tells the server which images it already holds and keeps them', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload({ images: { aaaaaaaaaaaaaaaa: 'data:image/png;base64,AAAA' } }));
  await settle(window);
  assert.equal(requests[0].headers['X-Pbd-Known-Images'], '');

  document.querySelector('[data-pbd-preview-refresh]').click();
  assert.equal(requests[1].headers['X-Pbd-Known-Images'], 'aaaaaaaaaaaaaaaa');
  // The server leaves known images out; the slide still shows the one already held.
  const withPicture = slide(1, 'Cover');
  withPicture.html = '<div class="pbd-slide-stage" style="width:1280px;height:720px;"><img data-pbd-preview-image="aaaaaaaaaaaaaaaa" alt=""></div>';
  requests[1].answer(payload({ slides: [withPicture], outline: [{ number: 1, role: 'Cover', projectId: null, listedProjectIds: [] }], images: {} }));
  await settle(window);

  assert.equal(
    document.querySelector('[data-pbd-stage-slide] [data-pbd-preview-image]').getAttribute('src'),
    'data:image/png;base64,AAAA');
});

// Preview follows the change (Plan: Deck settings, PR 5).
const briefPayload = () => payload({
  slideCount: 7,
  slides: [slide(1, 'Cover'), slide(2, 'Stage summary'), slide(3, 'Alpha simulator'), slide(4, 'Alpha simulator brief'),
    slide(5, 'Bravo trainer'), slide(6, 'Bravo trainer brief'), slide(7, 'Closing')],
  outline: [
    { number: 1, role: 'Cover', projectId: null, listedProjectIds: [] },
    { number: 2, role: 'StageSummary', projectId: null, listedProjectIds: [] },
    { number: 3, role: 'ProjectCapability', projectId: 1, listedProjectIds: [] },
    { number: 4, role: 'ProjectBrief', projectId: 1, listedProjectIds: [] },
    { number: 5, role: 'ProjectCapability', projectId: 2, listedProjectIds: [] },
    { number: 6, role: 'ProjectBrief', projectId: 2, listedProjectIds: [] },
    { number: 7, role: 'Closing', projectId: null, listedProjectIds: [] }
  ]
});

const saveSettings = async (window, document, changed) => {
  document.dispatchEvent(new window.CustomEvent('pbd:deck-changed', { detail: {} }));
  document.dispatchEvent(new window.CustomEvent('pbd:settings-saved', { detail: { changed } }));
  await new Promise((resolve) => window.setTimeout(resolve, 950));
};

test('after a settings change the preview shows a slide the change affects', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(briefPayload());
  await settle(window);
  assert.match(document.querySelector('[data-pbd-stage-meta]').textContent, /^Slide 1 of 7/);

  await saveSettings(window, document, ['IncludeStageSummary']);
  requests[1].answer(briefPayload());
  await settle(window);

  assert.match(document.querySelector('[data-pbd-stage-meta]').textContent, /^Slide 2 of 7 · Stage summary/);
});

test('a project-slide change stays on the project in view, and a theme change keeps the slide', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(briefPayload());
  await settle(window);
  outlineRow(document, 'Bravo trainer').click();
  await settle(window);

  await saveSettings(window, document, ['ProjectBriefLayout']);
  requests[1].answer(briefPayload());
  await settle(window);
  // The Bravo trainer's brief slide (the project's label names its first slide).
  assert.match(document.querySelector('[data-pbd-stage-meta]').textContent, /^Slide 6 of 7/);

  await saveSettings(window, document, ['PresentationTheme']);
  requests[2].answer(briefPayload());
  await settle(window);
  assert.match(document.querySelector('[data-pbd-stage-meta]').textContent, /^Slide 6 of 7/);
});

test('turning on the technical-category summary shows it, not the project-category one before it', async () => {
  const categories = () => payload({
    slideCount: 4,
    slides: [slide(1, 'Cover'), slide(2, 'Project-category summary'), slide(3, 'Technical-category summary'), slide(4, 'Closing')],
    outline: [
      { number: 1, role: 'Cover', projectId: null, listedProjectIds: [] },
      { number: 2, role: 'CategorySummary', projectId: null, listedProjectIds: [] },
      { number: 3, role: 'CategorySummary', projectId: null, listedProjectIds: [] },
      { number: 4, role: 'Closing', projectId: null, listedProjectIds: [] }
    ]
  });
  const { window, document, requests } = startStudio();
  requests[0].answer(categories());
  await settle(window);

  await saveSettings(window, document, ['IncludeTechnicalCategorySummary']);
  requests[1].answer(categories());
  await settle(window);

  assert.match(document.querySelector('[data-pbd-stage-meta]').textContent, /^Slide 3 of 4/);
});

test('the progress line counts the projects and the checks to review, and opens Checks', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);

  assert.equal(document.querySelector('[data-pbd-progress-projects]').textContent, '4');
  assert.equal(document.querySelector('[data-pbd-progress-checks]').textContent, '2 to review');
  assert.ok(document.querySelector('[data-pbd-progress-step="checks"]').classList.contains('is-open'));
  assert.ok(document.querySelector('[data-pbd-progress-step="generate"]').classList.contains('is-ready'));

  document.querySelector('[data-pbd-progress-tab="checks"]').click();
  assert.equal(document.querySelector('[data-pbd-inspector-panel="checks"]').hidden, false);
  assert.equal(document.querySelector('[data-pbd-inspector-tab="checks"]').getAttribute('aria-selected'), 'true');
});

test('picking a slide in the outline opens This slide, with each gap listed once and where its fix lands', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);
  document.querySelector('[data-pbd-inspector-tab="deck"]').click();
  assert.equal(document.querySelector('[data-pbd-inspector-panel="slide"]').hidden, true);

  outlineRow(document, 'Alpha simulator').click();

  const panel = document.querySelector('[data-pbd-inspector-panel="slide"]');
  assert.equal(panel.hidden, false);
  const gaps = [...panel.querySelectorAll('.pbd-inspector__gaps li')];
  assert.deepEqual(gaps.map((item) => item.querySelector('strong').textContent), ['Status remark', 'Photograph']);
  assert.ok(gaps.every((item) => item.querySelector('a').textContent.trim() === 'Fix in project'));
  // The status gap is not repeated as its own section further down.
  assert.ok(![...panel.querySelectorAll('h5')].some((heading) => heading.textContent === 'Present status'));
});

test('a project with nothing missing says so in This slide', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload());
  await settle(window);

  outlineRow(document, 'Charlie range').click();

  const panel = document.querySelector('[data-pbd-inspector-panel="slide"]');
  assert.equal(panel.querySelector('.pbd-inspector__gaps'), null);
  assert.match(panel.querySelector('.pbd-inspector__ready').textContent, /Everything these slides show is recorded/);
});

test('Present starts at the slide in view, with notes and the strip on request', async () => {
  const { window, document, requests } = startStudio();
  requests[0].answer(payload({
    projects: PROJECTS.map((item) => (item.projectId === 2 ? { ...item, speakerNotes: 'Mention the field trial' } : { ...item }))
  }));
  await settle(window);
  outlineRow(document, 'Bravo trainer').click();

  document.querySelector('[data-pbd-preview-open]').click();
  const viewer = document.querySelector('[data-pbd-viewer]');
  assert.equal(viewer.open, true);
  assert.equal(document.querySelector('[data-pbd-viewer-counter]').textContent, '3 / 5');
  assert.equal(document.querySelector('[data-pbd-viewer-strip]').hidden, true, 'the strip waits until asked for');
  assert.equal(document.querySelector('[data-pbd-viewer-notes]').hidden, true);

  viewer.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'n', bubbles: true }));
  assert.equal(document.querySelector('[data-pbd-viewer-notes]').hidden, false);
  assert.equal(document.querySelector('[data-pbd-viewer-notes-text]').textContent, 'Mention the field trial');
  assert.equal(document.querySelector('[data-pbd-viewer-notes-toggle]').getAttribute('aria-pressed'), 'true');

  viewer.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
  assert.equal(document.querySelector('[data-pbd-viewer-counter]').textContent, '4 / 5');
  assert.match(document.querySelector('[data-pbd-viewer-notes-text]').textContent, /No speaker notes for Charlie range/);

  document.querySelector('[data-pbd-viewer-first]').click();
  assert.equal(document.querySelector('[data-pbd-viewer-counter]').textContent, '1 / 5');
  assert.match(document.querySelector('[data-pbd-viewer-notes-text]').textContent, /not about one project/);

  document.querySelector('[data-pbd-viewer-strip-toggle]').click();
  assert.equal(document.querySelector('[data-pbd-viewer-strip]').hidden, false);
  assert.equal(document.querySelectorAll('[data-pbd-viewer-strip] button').length, 5);

  document.querySelector('[data-pbd-viewer-close]').click();
  assert.equal(viewer.open, false);
  assert.match(document.querySelector('[data-pbd-stage-meta]').textContent, /^Slide 1 of 5/, 'the Studio shows the slide Present ended on');
});
