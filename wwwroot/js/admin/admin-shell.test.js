const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'admin-shell.js'), 'utf8');

// Mirrors Pages/Shared/Components/AdminSidebar/_Navigation.cshtml: one sidebar group and the
// search-only links for pages that open from a hub (docs/ux/admin-module.md).
const sidebar = `
<aside class="admin-sidebar">
  <input data-admin-sidebar-search>
  <button data-admin-sidebar-search-clear hidden></button>
  <nav data-admin-sidebar-nav>
    <details data-admin-sidebar-group data-group-key="reference-lists" open>
      <summary>Reference lists</summary>
      <a class="admin-sidebar__item" href="/Admin/MasterData" data-admin-sidebar-label="lists used in forms master data, dropdown">Lists used in forms</a>
    </details>
    <div data-admin-sidebar-search-only hidden>
      <a id="units" class="admin-sidebar__item" href="/Admin/Lookups/SponsoringUnits" hidden data-admin-sidebar-search-only-link data-admin-sidebar-label="sponsoring units sponsoring unit, unit, formation, sponsor">Sponsoring units</a>
    </div>
    <p data-admin-sidebar-no-results hidden>Nothing matches.</p>
  </nav>
</aside>`;

function createDom() {
  const dom = new JSDOM(`<!doctype html><html><body>${sidebar}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/Admin'
  });
  dom.window.requestAnimationFrame = (callback) => callback();
  dom.window.eval(script);
  return dom.window;
}

function type(window, value) {
  const input = window.document.querySelector('[data-admin-sidebar-search]');
  input.value = value;
  input.dispatchEvent(new window.Event('input', { bubbles: true }));
}

test('pages that open from a hub stay hidden until the administrator searches', () => {
  const window = createDom();
  const extra = window.document.querySelector('[data-admin-sidebar-search-only]');

  assert.equal(extra.hidden, true);
  assert.equal(window.document.getElementById('units').hidden, true);
});

test('searching an everyday word finds a page that is not in the sidebar', () => {
  const window = createDom();

  type(window, 'formation');

  assert.equal(window.document.querySelector('[data-admin-sidebar-search-only]').hidden, false);
  assert.equal(window.document.getElementById('units').hidden, false);
  assert.equal(window.document.querySelector('[data-admin-sidebar-no-results]').hidden, true);
});

test('clearing the search hides the extra pages again', () => {
  const window = createDom();

  type(window, 'sponsoring');
  type(window, '');

  assert.equal(window.document.querySelector('[data-admin-sidebar-search-only]').hidden, true);
  assert.equal(window.document.getElementById('units').hidden, true);
});

test('a word that matches nothing says so', () => {
  const window = createDom();

  type(window, 'zzz');

  assert.equal(window.document.querySelector('[data-admin-sidebar-no-results]').hidden, false);
});
