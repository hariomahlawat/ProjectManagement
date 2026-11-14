const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { JSDOM } = require('jsdom');

// search.js boots against the global document when it is imported, and imports the shared
// combobox by a relative path. Load copies with .mjs extensions, in the same folder layout,
// after pointing the globals at a page built for each test.
const markup = `
  <header id="siteHeader"><a href="/">PRISM</a></header>
  <div data-search-page>
    <nav class="pm-gs-tabs" data-search-tabs><a href="?q=x" aria-current="page">All</a></nav>
    <section class="pm-gs-toolbar">
      <details class="pm-gs-filter" data-search-filter data-facets-loaded="true">
        <summary id="filtersToggle">Filters</summary>
        <div class="pm-gs-filter__backdrop" data-filter-dismiss></div>
        <form class="pm-gs-filter__panel" data-search-filter-form>
          <button type="button" data-filter-dismiss>Close</button>
          <p data-filter-pending></p>
          <button type="submit">Apply</button>
        </form>
      </details>
    </section>
    <section data-search-results>
      <article data-search-hit><h2><a href="/a" data-search-result data-search-rank="1">A</a></h2></article>
    </section>
    <nav data-search-pagination>
      <a id="more" href="/Common/Search?q=x&amp;Cursor=abc" data-search-load-more>Show more results <i class="bi bi-arrow-down"></i></a>
    </nav>
  </div>
  <footer id="siteFooter">Footer</footer>`;

let loaded = 0;
async function loadPage({ narrow = false } = {}) {
  const dom = new JSDOM(`<!doctype html><html><body>${markup}</body></html>`, {
    url: 'https://prism.local/Common/Search?q=x',
    pretendToBeVisual: true,
  });
  const { window } = dom;
  const media = {
    matches: narrow,
    listeners: [],
    addEventListener(type, listener) { if (type === 'change') this.listeners.push(listener); },
    removeEventListener() {},
    set(value) { this.matches = value; this.listeners.forEach((listener) => listener({ matches: value })); },
  };
  window.matchMedia = () => media;
  for (const name of ['window', 'document', 'Element', 'HTMLElement', 'HTMLAnchorElement', 'HTMLInputElement', 'HTMLDetailsElement', 'HTMLFormElement', 'DOMParser', 'FormData', 'Event', 'MouseEvent', 'KeyboardEvent', 'Node', 'location', 'localStorage', 'navigator']) {
    Object.defineProperty(globalThis, name, { configurable: true, writable: true, value: window[name] });
  }

  const root = fs.mkdtempSync(path.join(os.tmpdir(), `prism-search-page-${process.pid}-${loaded++}-`));
  fs.mkdirSync(path.join(root, 'pages'));
  fs.mkdirSync(path.join(root, 'navigation'));
  fs.copyFileSync(path.join(__dirname, '..', 'navigation', 'search-combobox.js'), path.join(root, 'navigation', 'search-combobox.mjs'));
  const source = fs.readFileSync(path.join(__dirname, 'search.js'), 'utf8')
    .replace("'../navigation/search-combobox.js'", "'../navigation/search-combobox.mjs'");
  fs.writeFileSync(path.join(root, 'pages', 'search.mjs'), source);
  const module = await import(pathToFileURL(path.join(root, 'pages', 'search.mjs')).href);
  return { window, document: window.document, module, media };
}

test('a second tap on Show more while it loads does not follow the link', async () => {
  const { window, document } = await loadPage();
  const more = document.getElementById('more');
  more.setAttribute('aria-disabled', 'true');

  const click = new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
  more.dispatchEvent(click);

  assert.equal(click.defaultPrevented, true);
});

test('rotating to a wide screen while the sheet is open gives the page back, and back again', async () => {
  const { window, document, media } = await loadPage({ narrow: true });
  const details = document.querySelector('[data-search-filter]');
  const form = document.querySelector('[data-search-filter-form]');
  details.open = true;
  details.dispatchEvent(new window.Event('toggle'));
  assert.equal(document.getElementById('siteHeader').inert, true);

  media.set(false);

  assert.equal(document.getElementById('siteHeader').inert, false);
  assert.equal(form.hasAttribute('aria-modal'), false);
  assert.equal(document.documentElement.classList.contains('pm-gs-filter-sheet-open'), false);

  media.set(true);

  assert.equal(document.getElementById('siteHeader').inert, true);
  assert.equal(form.getAttribute('aria-modal'), 'true');
});

test('the page outside the filter sheet is everything but the sheet and its backdrop', async () => {
  const { document, module } = await loadPage();
  const form = document.querySelector('[data-search-filter-form]');

  const outside = module.pageOutside(form);

  const ids = outside.map((node) => node.id || node.getAttribute('class') || node.tagName.toLowerCase());
  assert.ok(outside.includes(document.getElementById('siteHeader')));
  assert.ok(outside.includes(document.getElementById('siteFooter')));
  assert.ok(outside.includes(document.getElementById('filtersToggle')));
  assert.ok(outside.includes(document.querySelector('[data-search-results]')));
  assert.ok(!outside.includes(document.querySelector('.pm-gs-filter__backdrop')), ids.join(','));
  assert.ok(!outside.some((node) => node.contains(form)));
});

test('on a phone the filter sheet is a modal dialog, and closing it gives the page back', async () => {
  const { window, document } = await loadPage({ narrow: true });
  const details = document.querySelector('[data-search-filter]');
  const form = document.querySelector('[data-search-filter-form]');

  details.open = true;
  details.dispatchEvent(new window.Event('toggle'));

  assert.equal(form.getAttribute('role'), 'dialog');
  assert.equal(form.getAttribute('aria-modal'), 'true');
  assert.equal(document.getElementById('siteHeader').inert, true);
  assert.equal(document.querySelector('[data-search-results]').inert, true);

  document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

  assert.equal(details.open, false);
  assert.equal(document.activeElement, document.getElementById('filtersToggle'), 'focus returns to the Filters button');
  assert.equal(form.hasAttribute('role'), false);
  assert.equal(document.getElementById('siteHeader').inert, false);
  assert.equal(document.querySelector('[data-search-results]').inert, false);
});
