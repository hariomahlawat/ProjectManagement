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

test('a card named in the hash inside the folded ready group opens the group', () => {
    const window = createDom(
        `<details class="bd-ready" data-bd-ready-group><summary>Ready for conference</summary>${card('desk-project-7')}</details>`,
        'https://example.test/Workspace#desk-project-7');
    const doc = window.document;

    assert.equal(doc.querySelector('[data-bd-ready-group]').open, true);
    assert.equal(doc.getElementById('desk-project-7-body').hidden, false);
});
