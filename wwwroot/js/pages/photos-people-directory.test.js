// People directory (photos-people-directory.js): ticked people survive search, sort and paging.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const source = fs.readFileSync(path.resolve(__dirname, 'photos-people-directory.js'), 'utf8');

function createDirectory({ persisted = [] } = {}) {
    const card = (id) => `
      <article data-person-card>
        <input type="checkbox" name="PersonIds" value="${id}" form="peopleSelectionForm" data-person-selection />
        <button type="button" data-person-selection-control></button>
        <a href="/Photos?PersonIds=${id}" data-person-gallery-link></a>
      </article>`;
    const dom = new JSDOM(`
      <div data-people-directory>
        <button type="button" data-people-select-toggle></button>
        <form method="get" data-people-search-form><input type="search" name="Q" data-people-search-input /></form>
        <form method="get" class="people-sort"><select name="Sort"><option value="name">Name</option></select></form>
        <form id="peopleSelectionForm">
          <input type="hidden" name="PeopleMatch" value="all" data-people-match-value />
          ${persisted.map(id => `<input type="hidden" name="PersonIds" value="${id}" data-person-persisted-selection />`).join('')}
        </form>
        ${card('alice')}${card('bob')}
        <div data-people-selection-bar hidden>
          <strong data-people-selection-count></strong>
          <button type="button" data-people-match="all"></button>
          <button type="button" data-people-match="any"></button>
          <button type="submit" data-people-selection-submit><span data-people-selection-submit-label></span></button>
          <button type="button" data-people-selection-cancel></button>
        </div>
        <nav class="people-pagination"><a href="/Photos/People?Q=a&amp;PageNumber=2&amp;SelectedIds%5B0%5D=old">Next</a></nav>
      </div>`, { url: 'http://localhost/Photos/People', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    dom.window.eval(source);
    return dom.window;
}

const hiddenValues = (form, name) =>
    [...form.querySelectorAll(`input[type="hidden"][name="${name}"]`)].map(input => input.value);

test('a ticked person is carried into the search form, the sort form and page links', () => {
    const window = createDirectory();
    const document = window.document;
    document.querySelectorAll('[data-person-selection-control]')[0].click();

    assert.deepEqual(hiddenValues(document.querySelector('[data-people-search-form]'), 'SelectedIds'), ['alice']);
    assert.deepEqual(hiddenValues(document.querySelector('.people-sort'), 'SelectedIds'), ['alice']);
    const next = new URL(document.querySelector('.people-pagination a').getAttribute('href'), 'http://localhost');
    assert.deepEqual(next.searchParams.getAll('SelectedIds'), ['alice']);
    assert.equal(next.searchParams.get('SelectedIds[0]'), null, 'stale indexed selection is replaced');
    assert.equal(next.searchParams.get('PageNumber'), '2');
});

test('selections from earlier pages are kept, with the matching mode once two are chosen', () => {
    const window = createDirectory({ persisted: ['carol'] });
    const document = window.document;
    document.querySelectorAll('[data-person-selection-control]')[1].click();
    document.querySelector('[data-people-match="any"]').click();

    const search = document.querySelector('[data-people-search-form]');
    assert.deepEqual(hiddenValues(search, 'SelectedIds').sort(), ['bob', 'carol']);
    assert.deepEqual(hiddenValues(search, 'PeopleMatch'), ['any']);
});

test('cancelling the selection clears it from navigation', () => {
    const window = createDirectory();
    const document = window.document;
    document.querySelectorAll('[data-person-selection-control]')[0].click();
    document.querySelector('[data-people-selection-cancel]').click();

    assert.deepEqual(hiddenValues(document.querySelector('[data-people-search-form]'), 'SelectedIds'), []);
    const next = new URL(document.querySelector('.people-pagination a').getAttribute('href'), 'http://localhost');
    assert.deepEqual(next.searchParams.getAll('SelectedIds'), []);
});

test('the People page uses the same slim bar and sections as Photos', () => {
    const page = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/People/Index.cshtml'), 'utf8');
    assert.match(page, /class="photos-bar" data-photos-bar/);
    const sections = page.slice(page.indexOf('<nav class="photos-sections"'), page.indexOf('</nav>', page.indexOf('<nav class="photos-sections"')));
    for (const label of ['>Photos<', '>Albums<', '>People<', '>Collections<']) assert.ok(sections.includes(label), label);
    assert.match(page, /class="photos-search" role="search" data-people-search-form/);
    assert.doesNotMatch(page, /people-primary-tabs|class="people-header"/);
    assert.match(source, /--photos-bar-top/);
});

test('arrow keys on a select in the Sort and filter menu stay with the select', () => {
    const dom = new JSDOM(`
      <div data-people-directory>
        <div class="dropdown-menu people-filter__menu">
          <form class="people-filter__form"><select name="Sort"><option>Name</option><option>Most photos</option></select>
            <input type="checkbox" name="IncludeHidden" /></form>
        </div>
      </div>`, { url: 'http://localhost/Photos/People', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    const { window } = dom;
    window.eval(source);
    // Bootstrap listens for these keys on the document and cancels them inside a menu.
    const reachedDocument = [];
    window.document.addEventListener('keydown', (event) => reachedDocument.push(`${event.target.name}:${event.key}`));
    const press = (target, key) => target.dispatchEvent(new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

    const select = window.document.querySelector('select');
    press(select, 'ArrowDown');
    press(select, 'ArrowUp');
    assert.deepEqual(reachedDocument, [], 'the dropdown never sees them, so the choice can change');

    press(select, 'Escape');
    press(window.document.querySelector('[name="IncludeHidden"]'), 'ArrowDown');
    assert.deepEqual(reachedDocument, ['Sort:Escape', 'IncludeHidden:ArrowDown'], 'Escape still closes the menu; other controls are untouched');
    window.close();
});
