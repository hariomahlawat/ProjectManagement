// Keyboard and phone help for Review people and a person's page (photos-people-keys.js).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const source = fs.readFileSync(path.resolve(__dirname, 'photos-people-keys.js'), 'utf8');
const { nextIndex } = require('./photos-people-keys.js');
const read = (relative) => fs.readFileSync(path.resolve(__dirname, '../../..', relative), 'utf8');

// Three across, two rows, the second row one short.
const rects = [0, 1, 2, 0, 1].map((column, index) => ({ left: column * 100, top: index < 3 ? 0 : 120, width: 90, height: 110 }));

test('arrow keys move through the faces as a grid', () => {
    assert.equal(nextIndex(rects, 0, 'ArrowRight'), 1);
    assert.equal(nextIndex(rects, 0, 'ArrowLeft'), 0, 'nowhere to go: stays');
    assert.equal(nextIndex(rects, 4, 'ArrowRight'), 4);
    assert.equal(nextIndex(rects, 1, 'ArrowDown'), 4, 'the face below');
    assert.equal(nextIndex(rects, 2, 'ArrowDown'), 4, 'a short last row: the nearest face in it');
    assert.equal(nextIndex(rects, 4, 'ArrowUp'), 1);
    assert.equal(nextIndex(rects, 3, 'ArrowDown'), 3);
    assert.equal(nextIndex(rects, 3, 'Home'), 0);
    assert.equal(nextIndex(rects, 0, 'End'), 4);
    assert.equal(nextIndex(rects, 2, 'a'), 2);
});

function createPage(html, { phone = false } = {}) {
    const dom = new JSDOM(`<!doctype html><body>${html}</body>`, { runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    dom.window.matchMedia = () => ({ matches: phone, addEventListener() {} });
    dom.window.eval(source);
    return dom.window;
}

const press = (window, element, key, extra = {}) => {
    const event = new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...extra });
    element.dispatchEvent(event);
    return event;
};

const grid = `
    <form id="batch"><div data-name-field-ui><input id="name" data-name-field-text /></div></form>
    <article class="face-triage-card"><input id="a" type="checkbox" form="batch" data-batch-identity-face /></article>
    <article class="face-triage-card"><input id="b" type="checkbox" form="batch" data-batch-identity-face /></article>`;

test('Enter on a face goes on to the name field, ticking the face when nothing is ticked', () => {
    const window = createPage(grid);
    const [a, b] = ['a', 'b'].map(id => window.document.getElementById(id));
    let changes = 0;
    window.document.addEventListener('change', () => { changes += 1; });

    a.focus();
    const event = press(window, a, 'Enter');
    assert.equal(a.checked, true);
    assert.equal(changes, 1, 'the page hears the tick, so its count and buttons follow');
    assert.equal(event.defaultPrevented, true);
    assert.equal(window.document.activeElement.id, 'name');

    // Another face is already ticked: Enter goes on without changing this one.
    b.focus();
    press(window, b, 'Enter');
    assert.equal(b.checked, false);
    assert.equal(window.document.activeElement.id, 'name');
});

test('arrow keys move focus between the tick boxes and leave other keys and controls alone', () => {
    const window = createPage(grid);
    const [a, b, name] = ['a', 'b', 'name'].map(id => window.document.getElementById(id));
    a.focus();
    // jsdom lays nothing out, so every face is in one row: End reaches the last of them.
    assert.equal(press(window, a, 'End').defaultPrevented, true);
    assert.equal(window.document.activeElement, b);
    assert.equal(press(window, b, 'ArrowRight', { ctrlKey: true }).defaultPrevented, false, 'shortcuts with a modifier pass through');
    assert.equal(press(window, b, ' ').defaultPrevented, false, 'Space is the browser\'s own tick');
    name.focus();
    assert.equal(press(window, name, 'ArrowRight').defaultPrevented, false, 'typing in the name field is untouched');
});

test('on a person\'s page Enter goes to the button that acts on the ticked pictures', () => {
    const window = createPage(`
        <button id="act" data-correct-selected>Move or remove ticked</button>
        <article class="person-photo-card"><input id="p" type="checkbox" data-person-face-select /></article>`);
    const box = window.document.getElementById('p');
    box.focus();
    press(window, box, 'Enter');
    assert.equal(box.checked, true);
    assert.equal(window.document.activeElement.id, 'act');
});

test('sort and filter is closed on phones and open elsewhere', () => {
    const html = '<details open data-face-review-more><summary>Sort and filter</summary></details>';
    assert.equal(createPage(html, { phone: true }).document.querySelector('details').open, false);
    assert.equal(createPage(html).document.querySelector('details').open, true);
});

test('both pages load the script, and the phone rules use theme tokens and 44px targets', () => {
    const review = read('Pages/Photos/People/Review.cshtml');
    const person = read('Pages/Photos/People/Details.cshtml');
    [review, person].forEach(page => assert.match(page, /<script src="~\/js\/pages\/photos-people-keys\.js" asp-append-version="true"><\/script>/));
    // Without script the row is open, so nothing is out of reach.
    assert.match(review, /<details class="face-review-more" open data-face-review-more>/);
    const more = review.slice(review.indexOf('data-face-review-more'), review.indexOf('id="batchAssignKnownPersonForm"'));
    ['name="Source"', 'name="Year"', 'name="MatchStatus"', 'data-auto-navigate'].forEach(control => assert.ok(more.includes(control), control));
    assert.match(person, /<span data-selection-summary>0 ticked<\/span>/);

    const css = read('wwwroot/css/pages/photos-people.css');
    const rules = css.slice(css.indexOf('/* Sort and filter for the faces'));
    assert.doesNotMatch(rules, /#[0-9a-f]{3,8}\b|rgba?\(/i, 'theme tokens only');
    assert.match(rules, /@media \(max-width: 575\.98px\)/);
    assert.match(rules, /\.people-queues \.photos-chip \{ min-height:2\.75rem; \}/);
    assert.match(rules, /\.face-triage-card:has\(:focus-visible\)/);
});
