const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { JSDOM } = require('jsdom');

// The module is an ES module in a CommonJS package, so load a copy with an .mjs extension.
let modulePromise;
function loadModule() {
  if (!modulePromise) {
    const copy = path.join(os.tmpdir(), `prism-drawer-${process.pid}.mjs`);
    fs.copyFileSync(path.join(__dirname, 'drawer.js'), copy);
    modulePromise = import(pathToFileURL(copy).href);
  }
  return modulePromise;
}

// Mirrors Pages/Shared/Components/NavigationDrawer/Default.cshtml on /ProjectOfficeReports/Visits/New.
const markup = `
  <header>
    <button id="toggle" type="button" data-drawer-toggle data-drawer-target="pmNavigationDrawer"
            aria-controls="pmNavigationDrawer" aria-expanded="false">Menu</button>
    <div class="pm-drawer" data-drawer="pmNavigationDrawer" id="pmNavigationDrawer">
      <div data-drawer-overlay id="overlay"></div>
      <div data-drawer-panel role="dialog" aria-modal="true" aria-labelledby="title">
        <h2 id="title">PRISM ERP</h2>
        <button id="close" type="button" data-drawer-close>x</button>
        <div class="pm-drawer__body">
          <nav>
            <section class="pm-drawer__section" data-drawer-section="main">
              <h3 class="pm-drawer__section-title">Main</h3>
              <ul>
                <li data-drawer-item><a class="pm-drawer__link" id="dashboard" href="/Dashboard"><span class="pm-drawer__text">Dashboard</span></a></li>
                <li data-drawer-item><a class="pm-drawer__link" id="calendar" href="/Calendar"><span class="pm-drawer__text">Calendar</span></a></li>
              </ul>
            </section>
            <details class="pm-drawer__section" id="projects" data-drawer-group data-drawer-section="projects">
              <summary id="projects-summary" data-drawer-group-summary aria-expanded="false"><span class="pm-drawer__section-title">Projects</span></summary>
              <div data-drawer-group-panel aria-hidden="true">
                <ul>
                  <li data-drawer-item><a class="pm-drawer__link" id="ongoing" href="/Projects/Ongoing"><span class="pm-drawer__text">Ongoing projects</span></a></li>
                  <li data-drawer-item><a class="pm-drawer__link" id="ideas" href="/ProjectIdeas"><span class="pm-drawer__text">Project ideas</span></a></li>
                </ul>
              </div>
            </details>
            <details class="pm-drawer__section" id="trackers" data-drawer-group data-drawer-section="trackers" open>
              <summary data-drawer-group-summary aria-expanded="true"><span class="pm-drawer__section-title">Trackers</span></summary>
              <div data-drawer-group-panel aria-hidden="false">
                <ul>
                  <li data-drawer-item><a class="pm-drawer__link" id="review" href="/ProjectOfficeReports/ProgressReview"><span class="pm-drawer__text">Progress review</span></a></li>
                  <li data-drawer-item><a class="pm-drawer__link" id="visits" href="/ProjectOfficeReports/Visits" aria-current="page"><span class="pm-drawer__text">Visits</span></a></li>
                </ul>
              </div>
            </details>
          </nav>
        </div>
      </div>
    </div>
  </header>
  <main><a id="outside" href="#">Page content</a></main>`;

function createDom() {
  const dom = new JSDOM(`<!doctype html><html><body>${markup}</body></html>`, {
    url: 'https://prism.local/ProjectOfficeReports/Visits/New',
    pretendToBeVisual: true,
  });
  const { window } = dom;
  // drawer.js reads browser globals, as it does in the page.
  globalThis.window = window;
  globalThis.document = window.document;
  globalThis.HTMLElement = window.HTMLElement;
  globalThis.HTMLDetailsElement = window.HTMLDetailsElement;
  return { window, document: window.document };
}

const frame = (window) => new Promise((resolve) => window.requestAnimationFrame(() => resolve()));

function toggleDetails(window, details, open) {
  details.open = open;
  // jsdom queues the toggle event; dispatch it now so the test does not depend on that timing.
  details.dispatchEvent(new window.Event('toggle'));
}

test('opening focuses the current page link and closing returns focus to the toggle', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();

  const toggle = document.getElementById('toggle');
  const drawer = document.getElementById('pmNavigationDrawer');
  assert.equal(drawer.parentElement, document.body, 'the drawer is moved to <body>');
  assert.equal(drawer.getAttribute('aria-hidden'), 'true');

  toggle.focus();
  toggle.click();
  await frame(window);

  assert.ok(drawer.classList.contains('is-open'));
  assert.equal(toggle.getAttribute('aria-expanded'), 'true');
  assert.equal(document.activeElement.id, 'visits');

  document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

  assert.ok(!drawer.classList.contains('is-open'));
  assert.equal(drawer.getAttribute('aria-hidden'), 'true');
  assert.equal(document.activeElement, toggle);
});

test('without a current page link, focus starts at the first control', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  document.getElementById('visits').removeAttribute('aria-current');
  initDrawer();

  document.getElementById('toggle').click();
  await frame(window);

  assert.equal(document.activeElement.id, 'close');
});

test('sections are an accordion: opening one closes the others and syncs ARIA state', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();

  const projects = document.getElementById('projects');
  const trackers = document.getElementById('trackers');

  toggleDetails(window, projects, true);

  assert.equal(projects.open, true);
  assert.equal(trackers.open, false);
  assert.equal(document.getElementById('projects-summary').getAttribute('aria-expanded'), 'true');
  assert.equal(projects.querySelector('[data-drawer-group-panel]').getAttribute('aria-hidden'), 'false');

  // Closing a section leaves the others alone.
  toggleDetails(window, projects, false);
  assert.equal(projects.open, false);
  assert.equal(trackers.open, false);
});

test('Tab is trapped inside the panel and skips links in closed sections', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();

  document.getElementById('toggle').click();
  await frame(window);

  // Visits is the last focusable element: the Projects links are inside a closed section.
  document.getElementById('visits').focus();
  const event = new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
  document.dispatchEvent(event);

  assert.ok(event.defaultPrevented);
  assert.equal(document.activeElement.id, 'close');
});

test('the overlay closes the drawer', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();

  document.getElementById('toggle').click();
  await frame(window);
  document.getElementById('overlay').click();

  assert.ok(!document.getElementById('pmNavigationDrawer').classList.contains('is-open'));
  assert.ok(!document.body.classList.contains('pm-drawer-open'));
});

test('with every section closed, Tab reaches the section headers and Shift+Tab wraps to the last one', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  // A Main page such as the Dashboard: no section holds the current page, so all start closed.
  const trackers = document.getElementById('trackers');
  trackers.open = false;
  trackers.querySelector('[data-drawer-group-summary]').id = 'trackers-summary';
  document.getElementById('visits').removeAttribute('aria-current');
  document.getElementById('dashboard').setAttribute('aria-current', 'page');
  initDrawer();

  document.getElementById('toggle').click();
  await frame(window);
  assert.equal(document.activeElement.id, 'dashboard');

  // Tab from the last Main link must not wrap: the browser moves on to the Projects header.
  const forward = new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
  document.dispatchEvent(forward);
  assert.equal(forward.defaultPrevented, false);

  // Shift+Tab from Close wraps to the last focusable control: the Trackers header.
  document.getElementById('close').focus();
  const backward = new window.KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true });
  document.dispatchEvent(backward);
  assert.ok(backward.defaultPrevented);
  assert.equal(document.activeElement.id, 'trackers-summary');

  // Tab from the last header wraps to Close.
  const wrap = new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
  document.dispatchEvent(wrap);
  assert.ok(wrap.defaultPrevented);
  assert.equal(document.activeElement.id, 'close');
});

// ---------- "Find a page" filter ----------

function typeFilter(window, input, value) {
  input.value = value;
  input.dispatchEvent(new window.Event('input', { bubbles: true }));
}

function pressKey(window, target, name, extra = {}) {
  const event = new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...extra });
  target.dispatchEvent(event);
  return event;
}

const visibleTexts = (document) => Array.from(document.querySelectorAll('[data-drawer-item]'))
  .filter((item) => !item.hidden && !item.closest('[data-drawer-section]').hidden)
  .map((item) => item.textContent.trim());

async function openDrawer(window, document) {
  document.getElementById('toggle').click();
  await frame(window);
}

test('the filter is a labelled search field above the sections', async () => {
  const { initDrawer } = await loadModule();
  const { document } = createDom();
  initDrawer();

  const input = document.getElementById('pmDrawerFilter');
  assert.ok(input, 'drawer.js creates the field');
  assert.equal(input.type, 'search');
  assert.equal(document.querySelector('label[for="pmDrawerFilter"]').textContent, 'Find a page in the menu');
  assert.equal(input.closest('.pm-drawer__filter').nextElementSibling.className, 'pm-drawer__body');
  assert.equal(input.getAttribute('aria-describedby'), 'pmDrawerFilterStatus');
});

test('markup without sections (an older page) gets no filter', async () => {
  const { initDrawer } = await loadModule();
  const { document } = createDom();
  document.querySelectorAll('[data-drawer-section]').forEach((section) => section.removeAttribute('data-drawer-section'));
  initDrawer();

  assert.equal(document.getElementById('pmDrawerFilter'), null);
});

test('filtering shows matching links only, opens their sections and restores everything when cleared', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  const input = document.getElementById('pmDrawerFilter');
  const projects = document.getElementById('projects');
  const trackers = document.getElementById('trackers');

  typeFilter(window, input, 'IDEAS');

  assert.deepEqual(visibleTexts(document), ['Project ideas']);
  assert.equal(projects.open, true, 'the matching section opens');
  assert.equal(trackers.hidden, true, 'sections without a match are hidden');
  assert.equal(document.getElementById('pmDrawerFilterStatus').textContent, '1 page matches.');

  typeFilter(window, input, '');

  assert.equal(visibleTexts(document).length, 6);
  assert.equal(projects.open, false, 'sections return to their state before filtering');
  assert.equal(trackers.open, true);
  assert.equal(trackers.hidden, false);
  assert.equal(document.getElementById('pmDrawerFilterStatus').textContent, '');
});

test('every word must match, and a section name counts as part of each link', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  const input = document.getElementById('pmDrawerFilter');

  typeFilter(window, input, 'trackers vis');
  assert.deepEqual(visibleTexts(document), ['Visits']);

  // Matches in two sections: the accordion stands aside so both stay open.
  typeFilter(window, input, 'o');
  const projects = document.getElementById('projects');
  const trackers = document.getElementById('trackers');
  projects.dispatchEvent(new window.Event('toggle'));
  assert.equal(projects.open, true);
  assert.equal(trackers.open, true);
  assert.equal(document.getElementById('pmDrawerFilterStatus').textContent, '4 pages match.');
});

test('no match shows a message', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  typeFilter(window, document.getElementById('pmDrawerFilter'), 'zzz');

  const empty = document.querySelector('.pm-drawer__filter-empty');
  assert.equal(empty.hidden, false);
  assert.equal(empty.textContent, 'No pages match “zzz”.');
  assert.deepEqual(visibleTexts(document), []);
  assert.equal(document.getElementById('pmDrawerFilterStatus').textContent, 'No pages match.');
});

test('Enter opens the first match', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  const opened = [];
  document.querySelectorAll('a.pm-drawer__link').forEach((link) => link.addEventListener('click', (event) => {
    event.preventDefault();
    opened.push(link.id);
  }));

  const input = document.getElementById('pmDrawerFilter');
  typeFilter(window, input, 'review');
  const event = pressKey(window, input, 'Enter');

  assert.ok(event.defaultPrevented);
  assert.deepEqual(opened, ['review']);
});

test('Escape clears the filter first and closes the drawer the second time', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  const drawer = document.getElementById('pmNavigationDrawer');
  const input = document.getElementById('pmDrawerFilter');
  input.focus();
  typeFilter(window, input, 'ideas');

  pressKey(window, input, 'Escape');
  assert.equal(input.value, '');
  assert.ok(drawer.classList.contains('is-open'));

  pressKey(window, input, 'Escape');
  assert.ok(!drawer.classList.contains('is-open'));
});

test('closing the drawer clears the filter', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  const input = document.getElementById('pmDrawerFilter');
  typeFilter(window, input, 'ideas');
  document.getElementById('close').click();

  assert.equal(input.value, '');
  assert.equal(visibleTexts(document).length, 6);
});

test('/ focuses the filter, but types normally inside a field', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  const input = document.getElementById('pmDrawerFilter');
  const slash = pressKey(window, document.getElementById('visits'), '/');
  assert.ok(slash.defaultPrevented);
  assert.equal(document.activeElement, input);

  const typed = pressKey(window, input, '/');
  assert.equal(typed.defaultPrevented, false);
});

test('arrow keys move between the filter and the visible links', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  initDrawer();
  await openDrawer(window, document);

  const input = document.getElementById('pmDrawerFilter');
  input.focus();
  typeFilter(window, input, 'proj');

  pressKey(window, input, 'ArrowDown');
  assert.equal(document.activeElement.id, 'ongoing');
  pressKey(window, document.activeElement, 'ArrowDown');
  assert.equal(document.activeElement.id, 'ideas');
  pressKey(window, document.activeElement, 'ArrowUp');
  assert.equal(document.activeElement.id, 'ongoing');
  pressKey(window, document.activeElement, 'ArrowUp');
  assert.equal(document.activeElement, input);
});

// ---------- Remembered section ----------

test('opening a section remembers it, and it reopens when no section holds the current page', async () => {
  const { initDrawer } = await loadModule();
  const first = createDom();
  initDrawer();
  toggleDetails(first.window, first.document.getElementById('projects'), true);
  assert.equal(first.window.localStorage.getItem('prism.navDrawer.section'), 'projects');

  // A Main page: nothing holds the current page, so the remembered section opens.
  const second = createDom();
  second.window.localStorage.setItem('prism.navDrawer.section', 'projects');
  second.document.getElementById('trackers').removeAttribute('open');
  initDrawer();
  assert.equal(second.document.getElementById('projects').open, true);

  // The section holding the current page wins over the remembered one.
  const third = createDom();
  third.window.localStorage.setItem('prism.navDrawer.section', 'projects');
  initDrawer();
  assert.equal(third.document.getElementById('projects').open, false);
  assert.equal(third.document.getElementById('trackers').open, true);
});

test('blocked storage does not stop the drawer', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    get() { throw new window.DOMException('blocked', 'SecurityError'); },
  });
  document.getElementById('trackers').removeAttribute('open');
  initDrawer();

  toggleDetails(window, document.getElementById('projects'), true);
  await openDrawer(window, document);
  assert.ok(document.getElementById('pmNavigationDrawer').classList.contains('is-open'));
});

test('while open, the drawer handles "/" before page-wide shortcuts, and Ctrl+K closes it for the global search', async () => {
  const { initDrawer } = await loadModule();
  const { window, document } = createDom();
  // A page-wide shortcut registered first, as search-combobox.js does: it skips handled events.
  const globalSlash = [];
  document.addEventListener('keydown', (event) => {
    if (!event.defaultPrevented && event.key === '/') globalSlash.push(event);
  });
  initDrawer();
  await openDrawer(window, document);

  pressKey(window, document.getElementById('visits'), '/');
  assert.equal(document.activeElement.id, 'pmDrawerFilter');
  assert.equal(globalSlash.length, 0, 'the global "/" shortcut does not also fire');

  pressKey(window, document.activeElement, 'k', { ctrlKey: true });
  assert.ok(!document.getElementById('pmNavigationDrawer').classList.contains('is-open'));
});
