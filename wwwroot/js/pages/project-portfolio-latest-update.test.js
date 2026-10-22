const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptContent = fs.readFileSync(path.resolve(__dirname, 'project-portfolio.js'), 'utf8');

// jsdom has no layout, so each test states how tall the clamped text is and how tall it wants to be.
function createDom({ clientHeight, scrollHeight }) {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
<div class="project-portfolio">
    <section class="project-latest-update" data-latest-update>
        <a href="#remarks" data-project-open-remarks>All remarks</a>
        <p id="project-latest-update-text" data-latest-update-text>Long remark</p>
        <button type="button" aria-expanded="false" data-latest-update-toggle hidden>Show more</button>
    </section>
    <button type="button" data-panel-target="remarks">Remarks</button>
    <span id="remarks"></span>
</div>
</body></html>`, { url: 'https://example.test/Projects/Overview/1', runScripts: 'dangerously' });
    const window = dom.window;
    window.matchMedia = () => ({ matches: false });
    window.HTMLElement.prototype.scrollIntoView = () => {};
    const text = window.document.querySelector('[data-latest-update-text]');
    Object.defineProperty(text, 'clientHeight', { configurable: true, get: () => clientHeight });
    Object.defineProperty(text, 'scrollHeight', { configurable: true, get: () => scrollHeight });
    return window;
}

function run(window) {
    const script = window.document.createElement('script');
    script.textContent = scriptContent;
    window.document.body.appendChild(script);
}

test('Show more stays hidden when the whole update fits in three lines', () => {
    const window = createDom({ clientHeight: 66, scrollHeight: 66 });
    run(window);

    assert.equal(window.document.querySelector('[data-latest-update-toggle]').hidden, true);
});

test('Show more appears for a cut update and expands it in place', () => {
    const window = createDom({ clientHeight: 66, scrollHeight: 240 });
    run(window);

    const band = window.document.querySelector('[data-latest-update]');
    const toggle = window.document.querySelector('[data-latest-update-toggle]');
    assert.equal(toggle.hidden, false);

    toggle.click();
    assert.equal(band.classList.contains('is-expanded'), true);
    assert.equal(toggle.getAttribute('aria-expanded'), 'true');
    assert.equal(toggle.textContent, 'Show less');
    assert.equal(toggle.hidden, false, 'Show less stays available while expanded.');

    toggle.click();
    assert.equal(band.classList.contains('is-expanded'), false);
    assert.equal(toggle.textContent, 'Show more');
});

test('All remarks opens the Remarks panel', () => {
    const window = createDom({ clientHeight: 66, scrollHeight: 66 });
    run(window);

    let opened = false;
    window.document.querySelector('[data-panel-target="remarks"]').addEventListener('click', () => { opened = true; });
    window.document.querySelector('[data-project-open-remarks]').click();

    assert.equal(opened, true);
});
