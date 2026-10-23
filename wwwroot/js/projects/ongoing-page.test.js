const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptContent = fs.readFileSync(path.resolve(__dirname, 'ongoing.js'), 'utf8');

// The script runs as soon as it is appended, so each test arranges the page first.
function createDom(cards) {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
<form id="ongoingProjectsFilterForm"><input type="hidden" name="StageBucket"></form>
${cards}
</body></html>`, { url: 'https://example.test/Projects/Ongoing', runScripts: 'dangerously' });
    return dom.window;
}

function run(window) {
    const script = window.document.createElement('script');
    script.textContent = scriptContent;
    window.document.body.appendChild(script);
}

function rect(left) {
    return { left, right: left, top: 0, bottom: 0, width: 0, height: 0, x: left, y: 0 };
}

test('current stage is scrolled into view relative to its own rail', () => {
    const window = createDom(`
<article class="ongoing-card">
  <div class="briefing-timeline__rail" data-auto-focus-current="true">
    <div class="briefing-stage" data-current-stage="false"></div>
    <div class="briefing-stage" data-current-stage="true"></div>
  </div>
</article>`);
    const rail = window.document.querySelector('.briefing-timeline__rail');
    const current = window.document.querySelector('[data-current-stage="true"]');

    // The rail starts 300px into the page; the current stage sits 900px from the rail's edge.
    Object.defineProperty(rail, 'clientWidth', { value: 1000 });
    rail.getBoundingClientRect = () => rect(300);
    current.getBoundingClientRect = () => rect(1200);
    // offsetLeft measured from the card would put it elsewhere; it must not be used.
    Object.defineProperty(current, 'offsetLeft', { value: 5000 });

    run(window);

    assert.equal(rail.scrollLeft, 900 - 320);
});

test('a cover photo that fails to load is replaced by the placeholder', () => {
    const window = createDom(`
<div class="ongoing-card__thumb"><img class="ongoing-card__thumb-img" alt=""></div>
<div class="ongoing-card__thumb"><img class="ongoing-card__thumb-img" alt=""></div>`);
    const [broken, pending] = window.document.querySelectorAll('.ongoing-card__thumb-img');
    // The first image has already failed; the second fails after the script runs.
    Object.defineProperty(broken, 'complete', { value: true });
    Object.defineProperty(broken, 'naturalWidth', { value: 0 });
    Object.defineProperty(pending, 'complete', { value: false });

    run(window);
    pending.dispatchEvent(new window.Event('error'));

    const thumbs = window.document.querySelectorAll('.ongoing-card__thumb');
    assert.equal(window.document.querySelectorAll('.ongoing-card__thumb-img').length, 0);
    for (const thumb of thumbs) {
        const placeholder = thumb.querySelector('.ongoing-card__thumb-placeholder');
        assert.ok(placeholder);
        assert.equal(placeholder.getAttribute('aria-hidden'), 'true');
        assert.ok(placeholder.querySelector('i.bi.bi-image'));
    }
});

test('a loaded cover photo is kept', () => {
    const window = createDom(`<div class="ongoing-card__thumb"><img class="ongoing-card__thumb-img" alt=""></div>`);
    const img = window.document.querySelector('.ongoing-card__thumb-img');
    Object.defineProperty(img, 'complete', { value: true });
    Object.defineProperty(img, 'naturalWidth', { value: 80 });

    run(window);

    assert.ok(window.document.querySelector('.ongoing-card__thumb-img'));
});

test('stage and category chips add and remove their own hidden values, then submit', () => {
    const window = createDom(`
<button type="button" class="js-stage-bucket-chip" data-stage-bucket="procurement">Tender</button>
<button type="button" class="js-stage-bucket-chip" data-stage-bucket="development">Development</button>
<button type="button" class="js-kpi-chip" data-category-id="3">DCD</button>`);
    const form = window.document.getElementById('ongoingProjectsFilterForm');
    // One stage is already selected when the page loads.
    form.insertAdjacentHTML('beforeend', '<input type="hidden" name="Stage" value="procurement">');
    let submits = 0;
    form.requestSubmit = () => { submits += 1; };

    run(window);
    const values = (name) => Array.from(form.querySelectorAll(`input[type="hidden"][name="${name}"]`)).map((input) => input.value);
    const [tender, development] = window.document.querySelectorAll('.js-stage-bucket-chip');

    development.click();
    assert.deepEqual(values('Stage'), ['procurement', 'development']);

    tender.click();
    assert.deepEqual(values('Stage'), ['development']);

    window.document.querySelector('.js-kpi-chip').click();
    assert.deepEqual(values('Category'), ['3']);
    assert.equal(submits, 3);
});

test('a table thumbnail that fails to load is replaced by the table placeholder', () => {
    const window = createDom(`<div class="projcell"><img class="projcell__thumb" alt=""></div>`);
    const img = window.document.querySelector('img.projcell__thumb');
    Object.defineProperty(img, 'complete', { value: false });

    run(window);
    img.dispatchEvent(new window.Event('error'));

    assert.equal(window.document.querySelector('img.projcell__thumb'), null);
    const placeholder = window.document.querySelector('.projcell__thumb.projcell__thumb--placeholder');
    assert.ok(placeholder);
    assert.ok(placeholder.querySelector('span.bi.bi-image'));
});

test('the phone Filters button opens and closes the filter fields', () => {
    const window = createDom('');
    const form = window.document.getElementById('ongoingProjectsFilterForm');
    form.insertAdjacentHTML('beforeend', `
<button type="button" aria-expanded="false" data-ongoing-filters-toggle>Filters</button>
<div class="ongoing-head__filters" data-ongoing-filters><input type="search" name="Search"></div>`);

    run(window);
    const toggle = form.querySelector('[data-ongoing-filters-toggle]');
    const panel = form.querySelector('[data-ongoing-filters]');

    toggle.click();
    assert.ok(panel.classList.contains('is-open'));
    assert.equal(toggle.getAttribute('aria-expanded'), 'true');
    assert.equal(window.document.activeElement, panel.querySelector('input'));

    toggle.click();
    assert.ok(!panel.classList.contains('is-open'));
    assert.equal(toggle.getAttribute('aria-expanded'), 'false');
});
