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

// SECTION: Posting without a reload
function ajaxCard(id, { open = true, title = 'Night sight', body = '' } = {}) {
    return `
<article class="bd-card ${open ? 'is-expanded' : ''}" id="${id}" data-bd-card data-bd-title="${title}">
    <div class="bd-card__head"><h3><button type="button" data-bd-toggle aria-expanded="${open}" aria-controls="${id}-body">${title}</button></h3></div>
    <div class="bd-card__body" id="${id}-body" ${open ? '' : 'hidden'}>
        <form method="post" action="/Workspace?handler=DeskProjectUpdate" data-bd-ajax>
            <input type="hidden" name="itemId" value="1" />
            <textarea name="body">${body}</textarea>
            <button type="submit">Post remark</button>
        </form>
    </div>
</article>`;
}

function fragmentResponse(html, { message = 'Update posted.', ready = false } = {}) {
    const headers = { 'X-Desk-Fragment': '1', 'X-Desk-Message': encodeURIComponent(message), 'X-Desk-Item-Ready': ready ? 'true' : 'false' };
    return { ok: true, status: 200, redirected: false, headers: { get: (name) => headers[name] ?? null }, text: async () => html };
}

function jsonResponse(status, data) {
    return { ok: false, status, redirected: false, headers: { get: () => null }, json: async () => data };
}

async function settle(window) {
    for (let i = 0; i < 5; i++) {
        await new Promise((resolve) => window.setTimeout(resolve, 0));
    }
}

function submitFirstForm(window) {
    const form = window.document.querySelector('form[data-bd-ajax]');
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    return form;
}

test('a desk post is sent with fetch and the desk is swapped in place, card highlighted', async () => {
    const window = createDom(ajaxCard('desk-project-1', { body: 'Vendor demo held.' }));
    const calls = [];
    window.fetch = async (url, options) => {
        calls.push({ url, options });
        return fragmentResponse(`<div data-briefing-desk data-fresh>${ajaxCard('desk-project-1')}</div>`);
    };

    submitFirstForm(window);
    await settle(window);

    assert.equal(calls.length, 1);
    assert.equal(calls[0].options.method, 'POST');
    assert.equal(calls[0].options.headers['X-Briefing-Desk'], 'fetch');
    assert.equal(calls[0].options.body.get('body'), 'Vendor demo held.');
    assert.ok(window.document.querySelector('[data-briefing-desk][data-fresh]'), 'the desk is replaced by the server render');
    assert.ok(window.document.getElementById('desk-project-1').classList.contains('bd-card--updated'));
    assert.match(window.document.querySelector('[data-bd-toasts] .toast-body').textContent, /Update posted\./);
});

test('a card that became ready says so and the next open card gets focus', async () => {
    const window = createDom(ajaxCard('desk-project-1'));
    const fresh = `<div data-briefing-desk>${ajaxCard('desk-project-2', { title: 'Radar' })}`
        + `<details data-bd-ready-group><summary>1 ready for conference</summary>${ajaxCard('desk-project-1', { open: false })}</details></div>`;
    window.fetch = async () => fragmentResponse(fresh, { ready: true });

    submitFirstForm(window);
    await settle(window);

    const doc = window.document;
    assert.match(doc.querySelector('[data-bd-toasts] .toast-body').textContent, /Night sight is ready for conference\./);
    assert.ok(doc.querySelector('[data-bd-ready-group] summary').classList.contains('bd-ready--updated'));
    assert.equal(doc.activeElement, doc.querySelector('#desk-project-2 [data-bd-toggle]'));
});

test('a refused post shows the reason, keeps the text and frees the button', async () => {
    const window = createDom(ajaxCard('desk-project-1', { body: 'My remark' }));
    window.fetch = async () => jsonResponse(422, { message: 'Write something before posting.' });

    const form = submitFirstForm(window);
    await settle(window);

    assert.equal(form.querySelector('textarea').value, 'My remark');
    assert.equal(form.querySelector('button[type="submit"]').disabled, false);
    const toast = window.document.querySelector('[data-bd-toasts] .toast');
    assert.ok(toast.classList.contains('app-toast-danger'));
    assert.match(toast.textContent, /Write something before posting\./);
});

test('a network failure is never resent: the text stays and the button is freed', async () => {
    const window = createDom(ajaxCard('desk-project-1', { body: 'My remark' }));
    let calls = 0;
    window.fetch = async () => { calls += 1; throw new Error('offline'); };

    const form = submitFirstForm(window);
    await settle(window);

    assert.equal(calls, 1);
    assert.equal(form.querySelector('textarea').value, 'My remark');
    assert.equal(form.querySelector('button[type="submit"]').disabled, false);
    assert.match(window.document.querySelector('[data-bd-toasts]').textContent, /could not be reached/);
});
