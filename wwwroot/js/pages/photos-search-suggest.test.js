// Photos search suggestions (photos-search-suggest.js): people, projects and years offered
// from the lists the page already holds.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const source = fs.readFileSync(path.resolve(__dirname, 'photos-search-suggest.js'), 'utf8');
const photosPage = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml'), 'utf8');
const pageModel = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml.cs'), 'utf8');

const suggestions = {
    people: [
        { name: 'Arjun Mehra', count: 12, url: '/Photos?PersonIds=1', portrait: '/Photos/People/Portrait?id=1' },
        { name: 'Kavita Rao', count: 30, url: '/Photos?PersonIds=2', portrait: null },
        { name: 'Meera Pillai', count: 1, url: '/Photos?PersonIds=3', portrait: null }
    ],
    projects: [
        { name: 'Driving Simulator', url: '/Photos?ProjectId=7' },
        { name: 'Tank Gunnery Trainer (Mehra pattern)', url: '/Photos?ProjectId=8' }
    ],
    years: [{ year: 2026, url: '/Photos?Year=2026' }, { year: 2025, url: '/Photos?Year=2025' }]
};

function createPage(data = suggestions) {
    const dom = new JSDOM(`
      <form method="get" data-form>
        <input type="search" name="Q" data-photos-search />
        <div id="photosSuggest" data-photos-suggest role="listbox" hidden></div>
        <script type="application/json" data-photos-suggest-data>${JSON.stringify(data)}</script>
      </form>
      <button type="button" id="elsewhere"></button>`,
    { url: 'http://localhost/Photos', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    const { window } = dom;
    window.eval(source);
    const input = window.document.querySelector('[data-photos-search]');
    const panel = window.document.querySelector('[data-photos-suggest]');
    const type = (text) => {
        input.value = text;
        input.dispatchEvent(new window.Event('input', { bubbles: true }));
    };
    const press = (key) => {
        const event = new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
        input.dispatchEvent(event);
        return event;
    };
    const labels = () => [...panel.querySelectorAll('[role="option"]')].map(option => option.querySelector('.photos-suggest__label').textContent);
    return { window, input, panel, type, press, labels };
}

test('focusing the empty box offers the people with most photos, then the years', () => {
    const { window, input, panel, labels } = createPage();
    input.dispatchEvent(new window.Event('focus'));

    assert.equal(panel.hidden, false);
    assert.deepEqual(labels(), ['Kavita Rao', 'Arjun Mehra', 'Meera Pillai', '2026', '2025']);
    assert.equal(input.getAttribute('aria-expanded'), 'true');
    assert.equal(input.getAttribute('aria-controls'), 'photosSuggest');
    window.close();
});

test('typing narrows people, projects and years; a name that starts with the text comes first', () => {
    const { window, panel, type, labels } = createPage();

    type('meh');
    assert.deepEqual(labels(), ['Arjun Mehra', 'Tank Gunnery Trainer (Mehra pattern)']);
    assert.equal(panel.querySelector('.photos-suggest__detail').textContent, '12 photos');
    assert.equal(panel.querySelector('.photos-suggest__face img').getAttribute('src'), '/Photos/People/Portrait?id=1');

    type('2025');
    assert.deepEqual(labels(), ['2025']);

    type('zzz');
    assert.equal(panel.hidden, true, 'nothing to offer: the panel closes and Enter searches as before');
    window.close();
});

test('arrow keys move through the suggestions and Enter opens the highlighted one', () => {
    const { window, input, panel, type, press } = createPage();
    type('me');

    assert.equal(press('Enter').defaultPrevented, false, 'with nothing highlighted Enter submits the search');

    press('ArrowDown');
    const first = panel.querySelector('[role="option"]');
    assert.equal(first.classList.contains('is-active'), true);
    assert.equal(input.getAttribute('aria-activedescendant'), first.id);

    let opened = '';
    first.addEventListener('click', (event) => { event.preventDefault(); opened = first.getAttribute('href'); });
    assert.equal(press('Enter').defaultPrevented, true);
    assert.equal(opened, '/Photos?PersonIds=1');

    press('ArrowUp');
    assert.equal(panel.querySelectorAll('[role="option"]')[2].classList.contains('is-active'), true, 'wraps to the last suggestion');

    assert.equal(press('Escape').defaultPrevented, true);
    assert.equal(panel.hidden, true);
    assert.equal(input.getAttribute('aria-expanded'), 'false');
    window.close();
});

test('a click elsewhere closes the suggestions; names are shown as text, never as markup', () => {
    const { window, input, panel, type } = createPage({
        people: [{ name: '<img src=x onerror=alert(1)>', count: 1, url: '/Photos?PersonIds=9', portrait: null }],
        projects: [],
        years: []
    });
    type('img');
    assert.equal(panel.querySelector('.photos-suggest__label').textContent, '<img src=x onerror=alert(1)>');
    assert.equal(panel.querySelector('.photos-suggest__label img'), null);

    window.document.getElementById('elsewhere').click();
    assert.equal(panel.hidden, true);
    input.dispatchEvent(new window.Event('focus'));
    assert.equal(panel.hidden, false);
    window.close();
});

test('the page gives the suggestions from the lists the filter drawer already shows', () => {
    assert.match(photosPage, /data-photos-suggest-data>@Html\.Raw\(Model\.SerializeSearchSuggestions\(\)\)<\/script>/);
    assert.match(photosPage, /photos-search-suggest\.js/);
    assert.match(pageModel, /public string SerializeSearchSuggestions\(\)/);
    // Every suggestion is a fresh link; none carries the page's current text search or filters.
    const serializer = pageModel.slice(pageModel.indexOf('public string SerializeSearchSuggestions()'), pageModel.indexOf('public string SerializeViewerPeople'));
    assert.match(serializer, /url = Url\.Page\("\/Photos\/Index", new \{ View = "photos", PersonIds = person\.Id \}\)/);
    assert.doesNotMatch(serializer, /BuildSinglePersonUrl|BuildPhotosUrl/);
    // No new handler: the suggestions add no server endpoint.
    assert.doesNotMatch(pageModel, /OnGetSuggest/);
});
