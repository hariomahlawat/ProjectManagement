// Behaviour tests for the briefing deck library: search, Mine/All and sorting run in jsdom
// against a copy of the library table markup.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const librarySource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-library.js'),
  'utf8');

const row = (name, search, mine, edited, generated) => `
  <tr data-pbd-library-row data-search="${search}" data-mine="${mine}" data-name="${name}" data-edited="${edited}" data-generated="${generated}">
    <td>${name}</td>
  </tr>`;

const startLibrary = () => {
  const dom = new JSDOM(`<!doctype html><body>
    <section data-pbd-library>
      <span data-pbd-library-count>3</span>
      <button type="button" data-pbd-library-scope="mine" aria-pressed="false">Mine</button>
      <button type="button" data-pbd-library-scope="all" aria-pressed="true" class="is-active">All</button>
      <input type="search" data-pbd-library-search>
      <select data-pbd-library-sort>
        <option value="edited">Last edited</option>
        <option value="generated">Last generated</option>
        <option value="name">Name</option>
      </select>
      <table><tbody data-pbd-library-rows>
        ${row('Monthly review', 'monthly review vr based map reading', 'true', 300, 100)}
        ${row('Visit of DG EME', 'visit of dg eme counter-uas system', 'false', 200, 250)}
        ${row('ARPP review', 'arpp review tunguska wpn', 'true', 100, 0)}
      </tbody></table>
      <p data-pbd-library-empty hidden>No decks match.</p>
    </section></body>`, { runScripts: 'outside-only' });
  dom.window.eval(librarySource);
  return dom.window;
};

const visibleNames = (document) => [...document.querySelectorAll('[data-pbd-library-row]')]
  .filter((node) => !node.hidden)
  .map((node) => node.dataset.name);

const type = (window, value) => {
  const input = window.document.querySelector('[data-pbd-library-search]');
  input.value = value;
  input.dispatchEvent(new window.Event('input'));
};

test('search finds a deck by the name of a project in it', () => {
  const window = startLibrary();

  type(window, 'counter-uas');

  assert.deepEqual(visibleNames(window.document), ['Visit of DG EME']);
  assert.equal(window.document.querySelector('[data-pbd-library-count]').textContent, '1');
});

test('Mine keeps the decks the person owns or last edited', () => {
  const window = startLibrary();
  const mine = window.document.querySelector('[data-pbd-library-scope="mine"]');

  mine.click();

  assert.deepEqual(visibleNames(window.document), ['Monthly review', 'ARPP review']);
  assert.equal(mine.getAttribute('aria-pressed'), 'true');
  assert.equal(window.document.querySelector('[data-pbd-library-scope="all"]').getAttribute('aria-pressed'), 'false');
});

test('no match shows the empty message', () => {
  const window = startLibrary();

  type(window, 'nothing like this');

  assert.deepEqual(visibleNames(window.document), []);
  assert.equal(window.document.querySelector('[data-pbd-library-empty]').hidden, false);
});

test('sorting reorders the rows by name or by last generated', () => {
  const window = startLibrary();
  const sort = window.document.querySelector('[data-pbd-library-sort]');

  sort.value = 'name';
  sort.dispatchEvent(new window.Event('change'));
  assert.deepEqual(visibleNames(window.document), ['ARPP review', 'Monthly review', 'Visit of DG EME']);

  sort.value = 'generated';
  sort.dispatchEvent(new window.Event('change'));
  assert.deepEqual(visibleNames(window.document), ['Visit of DG EME', 'Monthly review', 'ARPP review']);
});

test('delete waits for confirmation and goes ahead only when confirmed', async () => {
  const window = startLibrary();
  const document = window.document;
  document.querySelector('[data-pbd-library]').insertAdjacentHTML('beforeend', `
    <form method="post" data-pbd-library-delete data-deck-name="ARPP review" data-confirm="Delete “ARPP review”?" data-confirm-detail="It moves to Recently deleted.">
      <button type="submit">Delete</button>
    </form>`);
  const form = document.querySelector('[data-pbd-library-delete]');
  const sent = [];
  // Stands in for the browser sending the form: runs after the library's own listener.
  window.addEventListener('submit', (event) => {
    if (!event.defaultPrevented) sent.push(form.dataset.pbdConfirmed);
    event.preventDefault();
  });
  const settle = () => new Promise((resolve) => window.setTimeout(resolve, 20));

  window.confirm = () => false;
  form.requestSubmit();
  await settle();
  assert.deepEqual(sent, []);

  window.confirm = () => true;
  form.requestSubmit();
  await settle();
  assert.deepEqual(sent, ['true']);
});
