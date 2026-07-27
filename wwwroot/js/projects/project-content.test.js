const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'project-content.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

function appendScript(document) {
    const script = document.createElement('script');
    script.textContent = scriptContent;
    document.body.appendChild(script);
}

function createBriefDom(fetchImpl, initialValue = 'Initial brief') {
    const dom = new JSDOM(`<!doctype html><html><body>
        <section data-project-content data-save-timeout-ms="1000" data-reload-recovery-ms="10">
            <div class="tab-pane" id="content-brief">
                <div data-content-view></div>
                <form action="/Projects/Overview/208?handler=SaveProjectBrief"
                      data-content-form data-content-kind="brief">
                    <input type="hidden" name="ContentBriefInput.ProjectId" value="208">
                    <input type="hidden" name="ContentBriefInput.RowVersion" value="row-version">
                    <textarea name="ContentBriefInput.Brief"
                              data-word-counter
                              data-word-concise-min="50"
                              data-word-min="100"
                              data-word-recommended-max="150"
                              data-word-hard-max="200">${initialValue}</textarea>
                    <span data-word-count></span>
                    <span data-word-status></span>
                    <div class="d-none" data-content-error></div>
                    <button type="button" data-content-cancel>Cancel</button>
                    <button type="submit" data-content-save>
                        <span class="d-none" data-content-spinner></span>
                        <span data-content-save-label>Save brief</span>
                    </button>
                </form>
            </div>
        </section>
    </body></html>`, {
        url: 'https://example.test/Projects/Overview/208?content=brief#content-brief',
        runScripts: 'dangerously'
    });

    dom.window.fetch = fetchImpl;
    appendScript(dom.window.document);
    return dom;
}

function jsonResponse(status, payload) {
    return {
        ok: status >= 200 && status < 300,
        status,
        text: async () => JSON.stringify(payload)
    };
}

function wait(milliseconds = 0) {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

test('successful save requests a real reload and recovers controls if unload does not occur', async () => {
    let requestCount = 0;
    const dom = createBriefDom(async () => {
        requestCount += 1;
        return jsonResponse(200, {
            ok: true,
            message: 'Project brief saved.',
            section: 'brief'
        });
    });

    const { window } = dom;
    const document = window.document;
    const root = document.querySelector('[data-project-content]');
    const form = document.querySelector('[data-content-form]');
    const textarea = document.querySelector('textarea');
    const label = document.querySelector('[data-content-save-label]');
    const error = document.querySelector('[data-content-error]');
    let reloadDetail = null;

    root.addEventListener('projectcontent:reload-requested', (event) => {
        reloadDetail = event.detail;
        event.preventDefault();
    });

    textarea.value = 'Updated project brief';
    textarea.dispatchEvent(new window.Event('input', { bubbles: true }));
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));

    await wait();
    await wait();

    assert.equal(requestCount, 1);
    assert.deepEqual(reloadDetail, {
        section: 'brief',
        message: 'Project brief saved.'
    });
    assert.equal(form.dataset.dirty, 'false');
    assert.equal(form.dataset.submitting, 'true');
    assert.equal(label.textContent, 'Saving…');
    assert.equal(textarea.disabled, true);

    await wait(20);

    assert.equal(form.dataset.submitting, 'false');
    assert.equal(label.textContent, 'Save brief');
    assert.equal(textarea.disabled, false);
    assert.equal(error.classList.contains('d-none'), false);
    assert.match(error.textContent, /changes were saved, but the page did not refresh/i);
});

test('pageshow restores a confirmed save that was suspended during pagehide', async () => {
    const dom = createBriefDom(async () => jsonResponse(200, {
        ok: true,
        message: 'Project brief saved.',
        section: 'brief'
    }));

    const { window } = dom;
    const document = window.document;
    const root = document.querySelector('[data-project-content]');
    const form = document.querySelector('[data-content-form]');
    const textarea = document.querySelector('textarea');
    const error = document.querySelector('[data-content-error]');

    root.addEventListener('projectcontent:reload-requested', (event) => event.preventDefault());
    textarea.value = 'Updated project brief';
    textarea.dispatchEvent(new window.Event('input', { bubbles: true }));
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));

    await wait();
    await wait();
    window.dispatchEvent(new window.Event('pagehide'));
    await wait(20);

    assert.equal(form.dataset.submitting, 'true');

    window.dispatchEvent(new window.Event('pageshow'));

    assert.equal(form.dataset.submitting, 'false');
    assert.equal(textarea.disabled, false);
    assert.match(error.textContent, /changes were saved, but the page did not refresh/i);
});

test('concurrency failure restores controls and preserves the entered content', async () => {
    const dom = createBriefDom(async () => jsonResponse(409, {
        ok: false,
        error: 'This project was changed by another user. Reload the page and review the latest content before saving again.'
    }));

    const { window } = dom;
    const document = window.document;
    const form = document.querySelector('[data-content-form]');
    const textarea = document.querySelector('textarea');
    const label = document.querySelector('[data-content-save-label]');
    const error = document.querySelector('[data-content-error]');

    textarea.value = 'Keep this unsaved entry available';
    textarea.dispatchEvent(new window.Event('input', { bubbles: true }));
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));

    await wait();
    await wait();

    assert.equal(form.dataset.submitting, 'false');
    assert.equal(form.dataset.dirty, 'true');
    assert.equal(textarea.value, 'Keep this unsaved entry available');
    assert.equal(textarea.disabled, false);
    assert.equal(label.textContent, 'Save brief');
    assert.match(error.textContent, /changed by another user/i);
});

test('brief readiness uses needs-expansion, concise, recommended and maximum bands', () => {
    const dom = createBriefDom(async () => jsonResponse(200, { ok: true }));
    const { window } = dom;
    const document = window.document;
    const textarea = document.querySelector('textarea');
    const status = document.querySelector('[data-word-status]');

    const setWordCount = (count) => {
        textarea.value = Array.from({ length: count }, (_, index) => `word${index + 1}`).join(' ');
        textarea.dispatchEvent(new window.Event('input', { bubbles: true }));
    };

    setWordCount(2);
    assert.equal(status.textContent, 'Needs expansion');

    setWordCount(49);
    assert.equal(status.textContent, 'Needs expansion');

    setWordCount(50);
    assert.equal(status.textContent, 'Concise');

    setWordCount(99);
    assert.equal(status.textContent, 'Concise');

    setWordCount(100);
    assert.equal(status.textContent, 'Recommended length');

    setWordCount(151);
    assert.equal(status.textContent, 'Consider shortening');

    setWordCount(201);
    assert.equal(status.textContent, 'Maximum exceeded');
});

test('description preview uses the server renderer and marks preview stale after editing', async () => {
    const dom = new JSDOM(`<!doctype html><html><body>
        <section data-project-content>
            <div class="tab-pane" id="content-description">
                <div data-content-view></div>
                <form action="/Projects/Overview/208?handler=SaveProjectDescription"
                      data-content-form data-content-kind="description"
                      data-description-editor
                      data-description-preview-url="/Projects/Overview/208?handler=PreviewProjectDescription">
                    <input type="hidden" name="__RequestVerificationToken" value="token">
                    <textarea name="ContentDescriptionInput.Description"
                              maxlength="5000"
                              data-character-counter>## Heading</textarea>
                    <span data-character-count></span>
                    <div class="d-none" data-description-preview-panel>
                        <button type="button" data-description-preview-close>Hide preview</button>
                        <div data-description-preview-content></div>
                        <div class="d-none" data-description-preview-error></div>
                    </div>
                    <button type="button" data-description-preview-trigger>
                        <span data-description-preview-label>Preview</span>
                    </button>
                    <div class="d-none" data-content-error></div>
                    <button type="submit" data-content-save>
                        <span class="d-none" data-content-spinner></span>
                        <span data-content-save-label>Save description</span>
                    </button>
                </form>
            </div>
        </section>
    </body></html>`, {
        url: 'https://example.test/Projects/Overview/208?content=description#content-description',
        runScripts: 'dangerously'
    });

    const { window } = dom;
    const document = window.document;
    let requestedUrl = null;
    window.fetch = async (url) => {
        requestedUrl = String(url);
        return jsonResponse(200, { ok: true, html: '<h2>Heading</h2>' });
    };
    appendScript(document);

    const trigger = document.querySelector('[data-description-preview-trigger]');
    const label = document.querySelector('[data-description-preview-label]');
    const panel = document.querySelector('[data-description-preview-panel]');
    const content = document.querySelector('[data-description-preview-content]');
    const textarea = document.querySelector('textarea');
    const counter = document.querySelector('[data-character-count]');

    trigger.dispatchEvent(new window.Event('click', { bubbles: true }));
    await wait();
    await wait();

    assert.match(requestedUrl, /PreviewProjectDescription/);
    assert.equal(panel.classList.contains('d-none'), false);
    assert.equal(content.innerHTML, '<h2>Heading</h2>');
    assert.equal(label.textContent, 'Refresh preview');
    assert.equal(counter.textContent, '10 / 5,000 characters');

    textarea.value = '## Revised heading';
    textarea.dispatchEvent(new window.Event('input', { bubbles: true }));

    assert.equal(label.textContent, 'Update preview');
    assert.equal(counter.textContent, '18 / 5,000 characters');
});

test('save implementation no longer navigates to the already-current URL', () => {
    assert.match(scriptContent, /window\.location\.reload\(\)/);
    assert.doesNotMatch(scriptContent, /window\.location\.(?:replace|assign)\(/);
    assert.match(scriptContent, /projectcontent:reload-requested/);
});
