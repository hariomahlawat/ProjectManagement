const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { JSDOM } = require('jsdom');

// When the menu does not fit the rail, groups without the current page start folded to their headings.
const rail = `
<aside class="cw-local-rail is-expanded" data-workspace-rail>
  <nav>
    <div class="cw-local-rail__group" data-rail-group="personal">
      <button type="button" data-rail-group-toggle aria-expanded="true">My work</button>
      <a href="/a">Briefing desk</a>
    </div>
    <div class="cw-local-rail__group" data-rail-group="command">
      <button type="button" data-rail-group-toggle aria-expanded="true">Command oversight</button>
      <a href="/b" class="is-active" aria-current="page">Briefing decks</a>
    </div>
    <div class="cw-local-rail__group" data-rail-group="system">
      <button type="button" data-rail-group-toggle aria-expanded="true">System adoption</button>
      <a href="/c">ERP adoption</a>
    </div>
  </nav>
</aside>`;

let modulePromise;
function loadModule() {
  if (!modulePromise) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), `prism-rail-groups-${process.pid}-`));
    const target = path.join(root, 'workspace-rail-groups.mjs');
    fs.copyFileSync(path.join(__dirname, 'workspace-rail-groups.js'), target);
    modulePromise = import(pathToFileURL(target).href);
  }
  return modulePromise;
}

function memoryStorage(initial = {}) {
  const values = new Map(Object.entries(initial));
  return {
    getItem: (key) => (values.has(key) ? values.get(key) : null),
    setItem: (key, value) => values.set(key, String(value)),
    values,
  };
}

async function setUp({ short = true, storage = memoryStorage(), markup = rail, overflows: measure } = {}) {
  const { initWorkspaceRailGroups } = await loadModule();
  const { document } = new JSDOM(`<!doctype html><html><body>${markup}</body></html>`).window;
  const space = { short };
  // jsdom has no layout: the menu "overflows" when the window is short and any group is open
  // beyond the page's own.
  const overflows = measure ? () => measure(document) : () => space.short;
  const controller = initWorkspaceRailGroups(document, { overflows, storage });
  const group = (name) => document.querySelector(`[data-rail-group="${name}"]`);
  const resize = (value) => { space.short = value; controller.apply(); };
  return { document, group, storage, resize };
}

const folded = (group) => group.classList.contains('is-folded');
const expanded = (group) => group.querySelector('[data-rail-group-toggle]').getAttribute('aria-expanded');

async function setUpReach({ content, window }) {
  const { initWorkspaceRailReach } = await loadModule();
  const { document } = new JSDOM(`<!doctype html><html><body><div class="command-workspace">${rail}<main class="cw-workspace-content"></main></div></body></html>`).window;
  const sizes = { content, window };
  const controller = initWorkspaceRailReach(document, { contentHeight: () => sizes.content, windowHeight: () => sizes.window });
  const workspace = document.querySelector('.command-workspace');
  return { workspace, sizes, controller };
}

test('beside a page taller than the window, the rail reaches to the bottom of the window', async () => {
  const { workspace } = await setUpReach({ content: 1800, window: 900 });
  assert.ok(workspace.classList.contains('cw-long-page'));
});

test('beside a page that fits the window, the rail keeps clear of the footer', async () => {
  const { workspace, sizes, controller } = await setUpReach({ content: 500, window: 900 });
  assert.equal(workspace.classList.contains('cw-long-page'), false);
  sizes.content = 1400;
  controller.apply();
  assert.ok(workspace.classList.contains('cw-long-page'), 'measured again when the content grows');
});

test('the long-page reach is a CSS variable the rail already uses', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', '..', 'css', 'command-workspace.css'), 'utf8');
  assert.match(css, /\.command-workspace\.cw-long-page \{ --cw-below:16px; \}/);
  assert.match(css, /max-height:calc\(100vh - 72px - var\(--cw-below\)\);/);
});

test('when the menu does not fit, only the group holding the page stays open', async () => {
  const { group } = await setUp();
  assert.equal(folded(group('command')), false);
  assert.equal(expanded(group('command')), 'true');
  assert.equal(folded(group('personal')), true);
  assert.equal(expanded(group('personal')), 'false');
  assert.equal(folded(group('system')), true);
});

test('when the menu fits, every group is open', async () => {
  const { group } = await setUp({ short: false });
  for (const name of ['personal', 'command', 'system']) assert.equal(folded(group(name)), false);
});

test('a heading opens its group and the choice is kept for the visit', async () => {
  const { group, storage } = await setUp();
  group('system').querySelector('[data-rail-group-toggle]').click();
  assert.equal(folded(group('system')), false);
  assert.equal(expanded(group('system')), 'true');
  assert.equal(storage.values.get('prism.workspace.railGroup.system'), 'open');

  const again = await setUp({ storage });
  assert.equal(folded(again.group('system')), false);
  assert.equal(folded(again.group('personal')), true);
});

test('the group holding the page opens even when it was folded before', async () => {
  const storage = memoryStorage({ 'prism.workspace.railGroup.command': 'folded' });
  const { group } = await setUp({ storage });
  assert.equal(folded(group('command')), false);
});

test('a page outside every group folds nothing', async () => {
  const markup = rail.replace(' class="is-active" aria-current="page"', '');
  const { group } = await setUp({ markup });
  for (const name of ['personal', 'command', 'system']) assert.equal(folded(group(name)), false);
});

test('growing the window opens the folded groups', async () => {
  const { group, resize } = await setUp();
  resize(false);
  assert.equal(folded(group('personal')), false);
});

test('storage that throws leaves the rail working', async () => {
  const broken = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  const { group } = await setUp({ storage: broken });
  assert.equal(folded(group('personal')), true);
  group('personal').querySelector('[data-rail-group-toggle]').click();
  assert.equal(folded(group('personal')), false);
});

test('a group the user opened stays open even when the menu does not fit', async () => {
  const storage = memoryStorage({ 'prism.workspace.railGroup.personal': 'open' });
  const { group } = await setUp({ storage });
  assert.equal(folded(group('personal')), false);
  assert.equal(folded(group('system')), true);
});

test('only as many groups fold as it takes to fit, from the bottom up', async () => {
  // Fits once one group is folded. (Counted by class list: jsdom caches class selectors.)
  const overflows = (document) => Array.from(document.querySelectorAll('[data-rail-group]'))
    .filter((group) => group.classList.contains('is-folded')).length < 1;
  const { group } = await setUp({ overflows });
  assert.equal(folded(group('system')), true);
  assert.equal(folded(group('personal')), false);
});
