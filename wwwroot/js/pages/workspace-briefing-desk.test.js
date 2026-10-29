const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptContent = fs.readFileSync(path.resolve(__dirname, 'workspace-briefing-desk.js'), 'utf8');

function card(id, { open = false } = {}) {
    return `
<article class="bd-card ${open ? 'is-expanded' : ''}" id="${id}" data-bd-card>
    <div class="bd-card__head">
        <h3><button type="button" data-bd-toggle aria-expanded="${open}" aria-controls="${id}-body">Title</button></h3>
        <span class="bd-pill">2 to do</span>
    </div>
    <div class="bd-card__body" id="${id}-body" ${open ? '' : 'hidden'}>
        <div class="bd-card__work">
            <form><textarea name="body"></textarea><button type="submit">Post update</button></form>
        </div>
        <div class="bd-preview" id="${id}-preview" hidden></div>
        <button type="button" data-bd-preview-toggle aria-expanded="false" aria-controls="${id}-preview"><span data-bd-preview-label>See as the Comdt sees it</span></button>
    </div>
</article>`;
}

function createDom(body, url = 'https://example.test/Workspace?mode=project-officer') {
    const dom = new JSDOM(`<!DOCTYPE html><html><body><div data-briefing-desk>${body}</div></body></html>`, {
        url,
        runScripts: 'dangerously'
    });
    const script = dom.window.document.createElement('script');
    script.textContent = scriptContent;
    dom.window.document.body.appendChild(script);
    return dom.window;
}

test('toggle expands and collapses a card body', () => {
    const window = createDom(card('desk-project-1'));
    const doc = window.document;
    const toggle = doc.querySelector('[data-bd-toggle]');
    const body = doc.getElementById('desk-project-1-body');

    toggle.click();
    assert.equal(toggle.getAttribute('aria-expanded'), 'true');
    assert.equal(body.hidden, false);
    assert.ok(doc.getElementById('desk-project-1').classList.contains('is-expanded'));

    toggle.click();
    assert.equal(toggle.getAttribute('aria-expanded'), 'false');
    assert.equal(body.hidden, true);
});

test('clicking the card head outside controls toggles the card', () => {
    const window = createDom(card('desk-task-4'));
    const doc = window.document;

    doc.querySelector('.bd-pill').dispatchEvent(new window.MouseEvent('click', { bubbles: true }));

    assert.equal(doc.querySelector('[data-bd-toggle]').getAttribute('aria-expanded'), 'true');
});

test('the conference preview replaces the working area and back', () => {
    const window = createDom(card('desk-idea-2', { open: true }));
    const doc = window.document;
    const previewToggle = doc.querySelector('[data-bd-preview-toggle]');

    previewToggle.click();
    assert.equal(doc.getElementById('desk-idea-2-preview').hidden, false);
    assert.equal(doc.querySelector('.bd-card__work').hidden, true);
    assert.equal(doc.querySelector('[data-bd-preview-label]').textContent, 'Back to preparing');

    previewToggle.click();
    assert.equal(doc.getElementById('desk-idea-2-preview').hidden, true);
    assert.equal(doc.querySelector('.bd-card__work').hidden, false);
});

test('a card named in the URL hash opens on load', () => {
    const window = createDom(card('desk-project-1') + card('desk-project-2'), 'https://example.test/Workspace#desk-project-2');
    const doc = window.document;

    assert.equal(doc.getElementById('desk-project-2-body').hidden, false);
    assert.equal(doc.getElementById('desk-project-1-body').hidden, true);
});

test('submitting a form disables its button to prevent a double post', () => {
    const window = createDom(card('desk-project-1', { open: true }));
    const doc = window.document;
    const form = doc.querySelector('form');

    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));

    assert.equal(form.querySelector('button[type="submit"]').disabled, true);
});

// SECTION: Weekly round
const roundMarkup = `
<button type="button" data-bd-round-start>Start weekly round</button>
<section data-bd-round hidden>
    <h2 data-bd-round-title></h2>
    <progress data-bd-round-bar max="1" value="0"></progress>
    <button type="button" data-bd-round-prev>Previous</button>
    <button type="button" data-bd-round-next>Next item</button>
    <button type="button" data-bd-round-exit>Exit round</button>
</section>
<section data-bd-round-done hidden tabindex="-1"><p data-bd-round-summary></p><button type="button" data-bd-round-close>Back</button></section>`;

function roundCard(id, ready, title) {
    return card(id).replace('data-bd-card>', `data-bd-card data-bd-ready="${ready}" data-bd-title="${title}">`);
}

function roundDesk(url, storage) {
    const body = roundMarkup
        + roundCard('desk-project-1', 'false', 'BFSR')
        + roundCard('desk-project-2', 'true', 'UIFOS')
        + roundCard('desk-task-3', 'false', 'TEC report');
    const dom = new JSDOM(`<!DOCTYPE html><html><body><div data-briefing-desk>${body}</div></body></html>`, {
        url: url || 'https://example.test/Workspace?mode=project-officer',
        runScripts: 'dangerously'
    });
    if (storage) {
        dom.window.sessionStorage.setItem('prism.briefingDesk.round', JSON.stringify(storage));
    }
    const script = dom.window.document.createElement('script');
    script.textContent = scriptContent;
    dom.window.document.body.appendChild(script);
    return dom.window;
}

function visibleCards(doc) {
    return Array.from(doc.querySelectorAll('[data-bd-card]')).filter((c) => !c.classList.contains('bd-round-hidden')).map((c) => c.id);
}

test('starting a round shows only the first not-ready card', () => {
    const window = roundDesk();
    const doc = window.document;

    doc.querySelector('[data-bd-round-start]').click();

    assert.deepEqual(visibleCards(doc), ['desk-project-1']);
    assert.equal(doc.querySelector('[data-bd-round]').hidden, false);
    assert.equal(doc.querySelector('[data-bd-round-title]').textContent, 'Item 1 of 2: BFSR');
    assert.equal(doc.getElementById('desk-project-1-body').hidden, false);
    assert.equal(doc.querySelector('[data-bd-round-prev]').disabled, true);
    assert.deepEqual(JSON.parse(window.sessionStorage.getItem('prism.briefingDesk.round')), { ids: ['desk-project-1', 'desk-task-3'], index: 0 });
});

test('next skips ready cards and the last step finishes the round', () => {
    const window = roundDesk();
    const doc = window.document;
    doc.querySelector('[data-bd-round-start]').click();

    doc.querySelector('[data-bd-round-next]').click();
    assert.deepEqual(visibleCards(doc), ['desk-task-3']);
    assert.equal(doc.querySelector('[data-bd-round-next]').textContent, 'Finish round');

    doc.querySelector('[data-bd-round-next]').click();
    assert.equal(doc.querySelector('[data-bd-round]').hidden, true);
    assert.equal(doc.querySelector('[data-bd-round-done]').hidden, false);
    assert.match(doc.querySelector('[data-bd-round-summary]').textContent, /0 of 2 items/);
    assert.equal(visibleCards(doc).length, 3);
    assert.equal(window.sessionStorage.getItem('prism.briefingDesk.round'), null);
});

test('a round resumes after a post on the card named in the hash', () => {
    const window = roundDesk(
        'https://example.test/Workspace?mode=project-officer&focus=desk-task-3#desk-task-3',
        { ids: ['desk-project-1', 'desk-task-3'], index: 0 });
    const doc = window.document;

    assert.deepEqual(visibleCards(doc), ['desk-task-3']);
    assert.equal(doc.querySelector('[data-bd-round-title]').textContent, 'Item 2 of 2: TEC report');
});

test('a card that became brief-ready during the round is labelled so', () => {
    const window = roundDesk(undefined, { ids: ['desk-project-2'], index: 0 });

    assert.equal(window.document.querySelector('[data-bd-round-title]').textContent, 'Item 1 of 1: UIFOS (brief-ready)');
});

test('exiting a round restores every card and forgets the round', () => {
    const window = roundDesk();
    const doc = window.document;
    doc.querySelector('[data-bd-round-start]').click();

    doc.querySelector('[data-bd-round-exit]').click();

    assert.equal(visibleCards(doc).length, 3);
    assert.equal(doc.querySelector('[data-bd-round]').hidden, true);
    assert.equal(window.sessionStorage.getItem('prism.briefingDesk.round'), null);
});

test('recently completed cards are not part of the weekly round', () => {
    const completed = `<details class="bd-completed" open>${roundCard('desk-project-40', 'false', 'VROP Myanmar')}</details>`;
    const dom = new JSDOM(`<!DOCTYPE html><html><body><div data-briefing-desk>${roundMarkup}${roundCard('desk-project-1', 'false', 'BFSR')}${completed}</div></body></html>`, {
        url: 'https://example.test/Workspace?mode=project-officer',
        runScripts: 'dangerously'
    });
    const script = dom.window.document.createElement('script');
    script.textContent = scriptContent;
    dom.window.document.body.appendChild(script);
    const doc = dom.window.document;

    doc.querySelector('[data-bd-round-start]').click();

    assert.equal(doc.querySelector('[data-bd-round-title]').textContent, 'Item 1 of 1: BFSR');
});

test('opening "Add another remark" puts the cursor in the box', () => {
    const window = createDom(`
<article class="bd-card is-expanded" id="desk-project-9" data-bd-card>
    <div class="bd-card__head"><h3><button type="button" data-bd-toggle aria-expanded="true" aria-controls="desk-project-9-body">Title</button></h3></div>
    <div class="bd-card__body" id="desk-project-9-body">
        <details class="bd-say__more" data-bd-say-more>
            <summary>Add another remark</summary>
            <form><textarea name="body"></textarea><button type="submit">Post remark</button></form>
        </details>
    </div>
</article>`);
    const doc = window.document;
    const more = doc.querySelector('[data-bd-say-more]');

    more.open = true;
    more.dispatchEvent(new window.Event('toggle'));

    assert.equal(doc.activeElement, more.querySelector('textarea'));
});
