const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'workspace-index.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

function createDom(body, options = {}) {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>${body}</body></html>`, {
        url: 'https://example.test/Workspace?mode=project-officer',
        runScripts: 'dangerously'
    });

    const { window } = dom;
    const desktop = options.desktop ?? true;
    const mobile = options.mobile ?? false;
    window.matchMedia = (query) => ({
        matches: query.includes('min-width: 992px') ? desktop : query.includes('max-width: 767.98px') ? mobile : false,
        addEventListener() {},
        removeEventListener() {}
    });

    if (options.navExpanded !== undefined) {
        window.localStorage.setItem('prism.projectOfficerWorkspace.navExpanded', String(options.navExpanded));
    }

    const script = window.document.createElement('script');
    script.textContent = scriptContent;
    window.document.body.appendChild(script);
    return { window, document: window.document };
}

const workspaceShell = `
<div class="command-workspace project-officer-workspace is-nav-expanded" data-project-officer-workspace>
    <aside class="cw-local-rail is-expanded" data-workspace-rail>
        <button type="button" data-workspace-rail-toggle aria-expanded="true"></button>
    </aside>
    <main class="cw-workspace-content"></main>
</div>`;

test('project officer rail restores the desktop preference and persists changes', () => {
    const { window, document } = createDom(workspaceShell, { desktop: true, navExpanded: false });
    const workspace = document.querySelector('[data-project-officer-workspace]');
    const rail = document.querySelector('[data-workspace-rail]');
    const toggle = document.querySelector('[data-workspace-rail-toggle]');

    assert.equal(workspace.classList.contains('is-nav-expanded'), false);
    assert.equal(rail.classList.contains('is-expanded'), false);
    assert.equal(toggle.getAttribute('aria-expanded'), 'false');

    toggle.click();

    assert.equal(workspace.classList.contains('is-nav-expanded'), true);
    assert.equal(rail.classList.contains('is-expanded'), true);
    assert.equal(window.localStorage.getItem('prism.projectOfficerWorkspace.navExpanded'), 'true');
});

test('action queue filters by text and action type and exposes the empty state', () => {
    const { window, document } = createDom(`${workspaceShell}
<div class="po-dedicated-page">
    <section class="po-filter-bar" data-po-action-filters>
        <input data-po-action-search />
        <select data-po-action-type><option value=""></option><option value="conference">Conference</option></select>
        <button type="button" data-po-clear-filters>Clear</button>
    </section>
    <article data-po-action-row data-filter-text="astrae timeline pdc" data-action-types="timeline"></article>
    <article data-po-action-row data-filter-text="idea concept paper" data-action-types="conference"></article>
    <div data-po-filter-empty hidden></div>
</div>`);

    const search = document.querySelector('[data-po-action-search]');
    const type = document.querySelector('[data-po-action-type]');
    const rows = [...document.querySelectorAll('[data-po-action-row]')];
    const empty = document.querySelector('[data-po-filter-empty]');

    type.value = 'conference';
    type.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(rows[0].hidden, true);
    assert.equal(rows[1].hidden, false);

    search.value = 'no match';
    search.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.equal(rows.every((row) => row.hidden), true);
    assert.equal(empty.hidden, false);

    document.querySelector('[data-po-clear-filters]').click();
    assert.equal(rows.every((row) => !row.hidden), true);
    assert.equal(empty.hidden, true);
});

test('document tabs update aria state, focus, and visible panel', () => {
    const { window, document } = createDom(`${workspaceShell}
<section data-po-document-tabs>
    <div role="tablist">
        <button role="tab" aria-selected="true" data-document-tab="favourites">Favourites</button>
        <button role="tab" aria-selected="false" data-document-tab="aots">AOTS</button>
    </div>
    <div role="tabpanel" data-document-panel="favourites"></div>
    <div role="tabpanel" data-document-panel="aots" hidden></div>
</section>`);

    const tabs = [...document.querySelectorAll('[data-document-tab]')];
    const panels = [...document.querySelectorAll('[data-document-panel]')];

    tabs[0].dispatchEvent(new window.KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));

    assert.equal(tabs[0].getAttribute('aria-selected'), 'false');
    assert.equal(tabs[0].tabIndex, -1);
    assert.equal(tabs[1].getAttribute('aria-selected'), 'true');
    assert.equal(tabs[1].tabIndex, 0);
    assert.equal(document.activeElement, tabs[1]);
    assert.equal(panels[0].hidden, true);
    assert.equal(panels[1].hidden, false);
});
