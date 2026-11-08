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
  <p data-admin-sidebar-search-status></p>
  <button data-admin-sidebar-search-clear hidden></button>
  <nav data-admin-sidebar-nav>
    <details data-admin-sidebar-group data-group-key="reference-lists" open>
      <summary>Reference lists</summary>
      <a id="lists" class="admin-sidebar__item" href="/Admin/MasterData" data-admin-sidebar-label="lists used in forms master data, dropdown every list that fills a dropdown in a form.">Lists used in forms</a>
      <a id="holidays" class="admin-sidebar__item" href="/Settings/Holidays" data-admin-sidebar-label="holidays holiday, leave, calendar public holidays shown on the calendar.">Holidays</a>
    </details>
    <div data-admin-sidebar-search-only hidden>
      <a id="units" class="admin-sidebar__item" href="/Admin/Lookups/SponsoringUnits" hidden data-admin-sidebar-search-only-link data-admin-sidebar-label="sponsoring units sponsoring unit, unit, formation, sponsor">Sponsoring units</a>
    </div>
    <p data-admin-sidebar-no-results hidden>Nothing matches.</p>
  </nav>
</aside>`;

// Mirrors Areas/Admin/Pages/AllPages/Index.cshtml.
const pageIndex = `
<section data-admin-index>
  <input data-admin-index-search>
  <p data-admin-index-count data-total="3">3 pages</p>
  <section id="people" data-admin-index-group>
    <ul>
      <li id="users" data-admin-index-item data-admin-index-label="users accounts, password, locked out create accounts, reset passwords."><a href="/Admin/Users">Users</a></li>
      <li id="roles" data-admin-index-item data-admin-index-label="who has which role roles, permissions which role grants each admin power."><a href="/Admin/AccessGovernance">Who has which role</a></li>
    </ul>
  </section>
  <section id="system" data-admin-index-group>
    <ul>
      <li id="chat" data-admin-index-item data-admin-index-label="chat file storage attachments, space how much space chat files use."><a href="/Admin/Chat/Storage">Chat file storage</a></li>
    </ul>
  </section>
  <p data-admin-index-empty hidden>Nothing matches.</p>
</section>`;

function createDom(body = sidebar) {
  const dom = new JSDOM(`<!doctype html><html><body>${body}<button id="outside">Outside</button></body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/Admin'
  });
  dom.window.requestAnimationFrame = (callback) => callback();
  // JSDOM does no layout; treat every element as on screen.
  dom.window.HTMLElement.prototype.getClientRects = () => [{ width: 1, height: 1 }];
  dom.window.eval(script);
  return dom.window;
}

function press(window, target, key) {
  const event = new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  target.dispatchEvent(event);
  return event;
}

function type(window, value, selector = '[data-admin-sidebar-search]') {
  const input = window.document.querySelector(selector);
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

test('every word typed must match, and descriptions count', () => {
  const window = createDom();

  type(window, 'public calendar');

  assert.equal(window.document.getElementById('holidays').hidden, false);
  assert.equal(window.document.getElementById('lists').hidden, true);
  assert.equal(window.document.querySelector('[data-admin-sidebar-search-status]').textContent, '1 page match.');
});

test('Enter opens the first match', () => {
  const window = createDom();
  let opened = null;
  window.document.getElementById('units').addEventListener('click', (event) => {
    event.preventDefault();
    opened = event.currentTarget.getAttribute('href');
  });

  type(window, 'sponsor');
  press(window, window.document.querySelector('[data-admin-sidebar-search]'), 'Enter');

  assert.equal(opened, '/Admin/Lookups/SponsoringUnits');
});

test('arrow keys move through the visible results and back to the box', () => {
  const window = createDom();
  const search = window.document.querySelector('[data-admin-sidebar-search]');

  type(window, 'l');
  search.focus();
  press(window, search, 'ArrowDown');
  assert.equal(window.document.activeElement.id, 'lists');

  press(window, window.document.activeElement, 'ArrowDown');
  assert.equal(window.document.activeElement.id, 'holidays');

  press(window, window.document.activeElement, 'ArrowUp');
  press(window, window.document.activeElement, 'ArrowUp');
  assert.equal(window.document.activeElement, search);
});

test('Escape clears the search', () => {
  const window = createDom();
  const search = window.document.querySelector('[data-admin-sidebar-search]');

  type(window, 'formation');
  press(window, search, 'Escape');

  assert.equal(search.value, '');
  assert.equal(window.document.getElementById('units').hidden, true);
});

test('"/" outside a text box goes to Find a page or setting', () => {
  const window = createDom();
  const event = press(window, window.document.getElementById('outside'), '/');

  assert.equal(event.defaultPrevented, true);
  assert.equal(window.document.activeElement, window.document.querySelector('[data-admin-sidebar-search]'));
});

test('"/" typed inside a text box stays a character', () => {
  const window = createDom();
  const event = press(window, window.document.querySelector('[data-admin-sidebar-search]'), '/');

  assert.equal(event.defaultPrevented, false);
});

test('Every admin page: typing narrows the list and counts what is left', () => {
  const window = createDom(pageIndex);

  type(window, 'locked', '[data-admin-index-search]');

  assert.equal(window.document.getElementById('users').hidden, false);
  assert.equal(window.document.getElementById('roles').hidden, true);
  assert.equal(window.document.getElementById('system').hidden, true);
  assert.equal(window.document.querySelector('[data-admin-index-count]').textContent, '1 of 3 pages');
});

test('Every admin page: no match says so; clearing restores the list', () => {
  const window = createDom(pageIndex);

  type(window, 'zzz', '[data-admin-index-search]');
  assert.equal(window.document.querySelector('[data-admin-index-empty]').hidden, false);

  type(window, '', '[data-admin-index-search]');
  assert.equal(window.document.querySelector('[data-admin-index-empty]').hidden, true);
  assert.equal(window.document.getElementById('system').hidden, false);
  assert.equal(window.document.querySelector('[data-admin-index-count]').textContent, '3 pages');
});

test('"/" on Every admin page goes to the page filter, not the sidebar', () => {
  const window = createDom(sidebar + pageIndex);

  const event = press(window, window.document.getElementById('outside'), '/');

  assert.equal(event.defaultPrevented, true);
  assert.equal(window.document.activeElement, window.document.querySelector('[data-admin-index-search]'));
});

test('"/" ignores the search box in a closed admin drawer (narrow screens)', () => {
  const drawer = `<div class="offcanvas" id="adminSidebarOffcanvas"><div class="admin-sidebar-offcanvas__body">
    <input data-admin-sidebar-search><nav data-admin-sidebar-nav></nav></div></div>`;
  const window = createDom(drawer);

  const event = press(window, window.document.getElementById('outside'), '/');

  assert.equal(event.defaultPrevented, false);
  assert.notEqual(window.document.activeElement, window.document.querySelector('[data-admin-sidebar-search]'));
});

test('"/" ignores a search box hidden with visibility:hidden', () => {
  const window = createDom(sidebar.replace('<aside class="admin-sidebar">', '<aside class="admin-sidebar" style="visibility:hidden">'));

  const event = press(window, window.document.getElementById('outside'), '/');

  assert.equal(event.defaultPrevented, false);
});

// Mirrors Areas/Admin/Pages/Index.cshtml: Home's "Find a page or setting".
const homeFind = `
<section data-admin-home-find>
  <input data-admin-home-search>
  <p data-admin-home-find-status></p>
  <ul data-admin-home-find-results hidden>
    <li id="h-users" hidden data-admin-home-find-entry data-title="users" data-keywords="add user, password, locked out, unlock" data-label="users add user, password, locked out, unlock create accounts, reset a password or clear a lockout."><a href="/Admin/Users">Users</a></li>
    <li id="h-logins" hidden data-admin-home-find-entry data-title="sign-in activity" data-keywords="login, failed login" data-label="sign-in activity login, failed login who signed in, when, and every failed or locked out attempt."><a href="/Admin/Logins">Sign-in activity</a></li>
    <li id="h-restore" hidden data-admin-home-find-entry data-title="restore deleted items" data-keywords="trash, recycle bin" data-label="restore deleted items trash, recycle bin bring back deleted projects."><a href="/Admin/Recovery">Restore deleted items</a></li>
  </ul>
  <p data-admin-home-find-empty hidden>Nothing matches.</p>
  <button type="button" data-admin-home-try="trash">trash</button>
</section>`;

test('Home search: results appear only while typing, best match first', () => {
  const window = createDom(sidebar + homeFind);
  const results = window.document.querySelector('[data-admin-home-find-results]');
  assert.equal(results.hidden, true);

  type(window, 'locked out', '[data-admin-home-search]');

  // "locked out" is a search word for Users (scores 2 per word) and only in the description of
  // Sign-in activity (1 per word), so Users comes first.
  const shown = Array.from(results.querySelectorAll('li:not([hidden])')).map((li) => li.id);
  assert.deepEqual(shown, ['h-users', 'h-logins']);
  assert.equal(results.hidden, false);
  assert.equal(window.document.querySelector('[data-admin-home-find-status]').textContent, '2 pages found.');

  type(window, '', '[data-admin-home-search]');
  assert.equal(results.hidden, true);
});

test('Home search: Enter opens the best match; no match says so', () => {
  const window = createDom(sidebar + homeFind);
  let opened = null;
  window.document.querySelectorAll('[data-admin-home-find-results] a').forEach((link) => {
    link.addEventListener('click', (event) => {
      event.preventDefault();
      opened = event.currentTarget.getAttribute('href');
    });
  });
  const input = window.document.querySelector('[data-admin-home-search]');

  type(window, 'recycle', '[data-admin-home-search]');
  press(window, input, 'Enter');
  assert.equal(opened, '/Admin/Recovery');

  type(window, 'zzz', '[data-admin-home-search]');
  assert.equal(window.document.querySelector('[data-admin-home-find-empty]').hidden, false);
});

test('Home search: the example words fill the box and show results', () => {
  const window = createDom(sidebar + homeFind);

  window.document.querySelector('[data-admin-home-try]').click();

  assert.equal(window.document.querySelector('[data-admin-home-search]').value, 'trash');
  assert.equal(window.document.getElementById('h-restore').hidden, false);
});

test('"/" on Home goes to the Home search box first', () => {
  const window = createDom(sidebar + homeFind);

  press(window, window.document.getElementById('outside'), '/');

  assert.equal(window.document.activeElement, window.document.querySelector('[data-admin-home-search]'));
});

// Restore deleted items on a phone (docs/ux/admin-review-3-fixes.md, PR 8).
const restoreTabs = `
<nav class="admin-restore-tabs" data-admin-restore-tabs>
  <a id="t-overview" href="/Admin/Recovery">Overview</a>
  <a id="t-projects" href="/Admin/Projects/Trash">Projects</a>
  <a id="t-calendar" href="/Admin/Calendar/Deleted" aria-current="page">Calendar events</a>
</nav>`;

function createRestoreDom({ scrollWidth, clientWidth }) {
  const dom = new JSDOM(`<!doctype html><html><body>${restoreTabs}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/Admin/Calendar/Deleted'
  });
  const calls = [];
  const strip = dom.window.document.querySelector('[data-admin-restore-tabs]');
  Object.defineProperty(strip, 'scrollWidth', { value: scrollWidth });
  Object.defineProperty(strip, 'clientWidth', { value: clientWidth });
  dom.window.HTMLElement.prototype.scrollIntoView = function (options) { calls.push({ id: this.id, options: { ...options } }); };
  dom.window.eval(script);
  return calls;
}

test('a restore tab strip that scrolls brings the open tab into view, without moving the page', () => {
  const calls = createRestoreDom({ scrollWidth: 600, clientWidth: 343 });

  assert.deepEqual(calls, [{ id: 't-calendar', options: { inline: 'center', block: 'nearest' } }]);
});

test('a restore tab strip that fits is left alone', () => {
  const calls = createRestoreDom({ scrollWidth: 900, clientWidth: 900 });

  assert.equal(calls.length, 0);
});
