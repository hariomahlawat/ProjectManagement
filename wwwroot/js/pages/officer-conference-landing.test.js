// Conference review, choosing an officer: the find box, the picture fallback, and the page's contract.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const source = fs.readFileSync(path.resolve(__dirname, 'officer-conference-landing.js'), 'utf8');
const { matches, countText } = require('./officer-conference-landing.js');
const read = (relative) => fs.readFileSync(path.resolve(__dirname, '../../..', relative), 'utf8');

test('an officer matches when every word typed is in the rank and name', () => {
    assert.equal(matches('Lt Col Anirudh Pathak', ''), true);
    assert.equal(matches('Lt Col Anirudh Pathak', '  PATH '), true);
    assert.equal(matches('Lt Col Anirudh Pathak', 'pathak anirudh'), true, 'any order');
    assert.equal(matches('Lt Col Anirudh Pathak', 'col'), true, 'the rank counts');
    assert.equal(matches('Lt Col Anirudh Pathak', 'anirudh rao'), false);
    assert.equal(countText(14, 14), '14 officers');
    assert.equal(countText(1, 1), '1 officer');
    assert.equal(countText(3, 14), '3 of 14');
});

function createPage() {
    const dom = new JSDOM(`<!doctype html><body>
        <label hidden><input type="search" data-officer-find /></label>
        <span data-officer-count>3 officers</span>
        <div data-officer-list>
            <a href="/a" data-officer-card data-officer-name="Col Harish Malik"><span><img data-officer-portrait src="/p/1" /></span></a>
            <a href="/b" data-officer-card data-officer-name="Lt Col Anirudh Pathak"></a>
            <a href="/c" data-officer-card data-officer-name="Lt Col Nikhil Rao"></a>
        </div>
        <p data-officer-no-match hidden>No officer matches that name.</p>
    </body>`, { runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    dom.window.eval(source);
    return dom.window;
}

const type = (window, text) => {
    const find = window.document.querySelector('[data-officer-find]');
    find.value = text;
    find.dispatchEvent(new window.Event('input', { bubbles: true }));
    return find;
};

test('typing narrows the tiles, says how many are left, and Escape brings them back', () => {
    const window = createPage();
    const { document } = window;
    assert.equal(document.querySelector('label').hidden, false, 'the find box appears once it can work');
    const find = type(window, 'lt col');
    const hidden = () => [...document.querySelectorAll('[data-officer-card]')].map(card => card.hidden);
    assert.deepEqual(hidden(), [true, false, false]);
    assert.equal(document.querySelector('[data-officer-count]').textContent, '2 of 3');

    type(window, 'nobody');
    assert.equal(document.querySelector('[data-officer-no-match]').hidden, false);
    assert.equal(document.querySelector('[data-officer-list]').hidden, true);

    find.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    assert.equal(find.value, '');
    assert.deepEqual(hidden(), [false, false, false]);
    assert.equal(document.querySelector('[data-officer-count]').textContent, '3 officers');
});

test('Enter opens the first officer left', () => {
    const window = createPage();
    const opened = [];
    window.document.querySelectorAll('[data-officer-card]').forEach(card =>
        card.addEventListener('click', event => { event.preventDefault(); opened.push(card.getAttribute('href')); }));
    const find = type(window, 'rao');
    const enter = new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    find.dispatchEvent(enter);
    assert.deepEqual(opened, ['/c']);
    assert.equal(enter.defaultPrevented, true);
});

test('a picture that cannot load is taken away, leaving the initials', () => {
    const window = createPage();
    const image = window.document.querySelector('[data-officer-portrait]');
    image.dispatchEvent(new window.Event('error'));
    assert.equal(window.document.querySelector('[data-officer-portrait]'), null);
});

test('the page: initials from the name, the same count wording, columns from the panel width', () => {
    const page = read('Pages/Workspace/Conference.cshtml');
    const landing = page.slice(page.indexOf('class="oc-landing"'), page.indexOf('class="oc-antiforgery"'));
    assert.match(landing, /@option\.Initials/);
    assert.doesNotMatch(landing, /DisplayName\.Trim\(\)\[0\]/, 'not the first letter of the rank');
    assert.match(landing, /Url\.Page\("\/Photos\/People\/Portrait", new \{ area = "", id = portraitPersonId \}\)/);
    // The wording the owner asked to keep, zeros included.
    ['active {(option.ActiveProjectCount == 1 ? "project" : "projects")}', 'recently completed',
        '{(option.IdeaCount == 1 ? "idea" : "ideas")}', '{(option.OtherTaskCount == 1 ? "other task" : "other tasks")}',
        '{moduleCount.Count} {moduleCount.Label}'].forEach(text => assert.ok(landing.includes(text), text));
    assert.match(landing, /if \(option\.RecentlyCompletedProjectCount > 0\)/, 'recently completed only when there are some, as before');
    assert.match(page, /<script src="~\/js\/pages\/officer-conference-landing\.js" asp-append-version="true"><\/script>/);

    const model = read('ViewModels/Workspace/OfficerConferenceViewModels.cs');
    assert.match(model, /public string Initials/);

    const css = read('wwwroot/css/officer-conference.css');
    assert.match(css, /\.oc-landing \{[^}]*container-type: inline-size;/);
    const columns = [...css.matchAll(/@container \(min-width: [\d.]+rem\) \{\s+\.oc-landing__officers \{ grid-template-columns: repeat\((\d), minmax\(0, 1fr\)\); \}/g)].map(match => match[1]);
    assert.deepEqual(columns, ['2', '3', '4', '5'], 'one column to start, then two to five as the panel widens');
});
