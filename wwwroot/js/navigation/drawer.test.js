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
        <nav>
          <section data-drawer-section="main">
            <ul><li><a id="dashboard" href="/Dashboard">Dashboard</a></li></ul>
          </section>
          <details id="projects" data-drawer-group data-drawer-section="projects">
            <summary id="projects-summary" data-drawer-group-summary aria-expanded="false">Projects</summary>
            <div data-drawer-group-panel aria-hidden="true">
              <ul><li><a href="/Projects/Ongoing">Ongoing projects</a></li></ul>
            </div>
          </details>
          <details id="trackers" data-drawer-group data-drawer-section="trackers" open>
            <summary data-drawer-group-summary aria-expanded="true">Trackers</summary>
            <div data-drawer-group-panel aria-hidden="false">
              <ul>
                <li><a href="/ProjectOfficeReports/ProgressReview">Progress review</a></li>
                <li><a id="visits" href="/ProjectOfficeReports/Visits" aria-current="page">Visits</a></li>
              </ul>
            </div>
          </details>
        </nav>
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
