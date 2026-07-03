const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'workspace-index.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

function createWorkspaceDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <nav class="po-section-nav" aria-label="Workspace sections">
            <a href="#action-queue" class="active">Actions</a>
            <a href="#assigned-projects">Projects</a>
            <a href="#follow-ups">Follow-ups</a>
        </nav>
        <section id="action-queue"></section>
        <section id="assigned-projects"></section>
        <section id="follow-ups"></section>
    </body></html>`, { url: 'https://example.test/Workspace', runScripts: 'dangerously' });

    const { window } = dom;
    window.HTMLElement.prototype.scrollIntoView = function scrollIntoView() {};
    window.IntersectionObserver = class {
        observe() {}
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);

    return { window, document: window.document };
}

test('workspace section navigation keeps one active destination', () => {
    const { window, document } = createWorkspaceDom();
    const links = Array.from(document.querySelectorAll('.po-section-nav a'));

    links[1].dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));

    assert.equal(document.querySelector('.po-section-nav a.active'), links[1]);
    assert.equal(links[1].getAttribute('aria-current'), 'location');
    assert.equal(links[0].hasAttribute('aria-current'), false);
});


test('workspace section navigation honours an initial section hash', () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <nav class="po-section-nav" aria-label="Workspace sections">
            <a href="#action-queue" class="active">Actions</a>
            <a href="#assigned-projects">Projects</a>
            <a href="#follow-ups">Follow-ups</a>
        </nav>
        <section id="action-queue"></section>
        <section id="assigned-projects"></section>
        <section id="follow-ups"></section>
    </body></html>`, { url: 'https://example.test/Workspace#assigned-projects', runScripts: 'dangerously' });

    const scriptEl = dom.window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    dom.window.document.body.appendChild(scriptEl);

    const active = dom.window.document.querySelector('.po-section-nav a.active');
    assert.equal(active?.getAttribute('href'), '#assigned-projects');
    assert.equal(active?.getAttribute('aria-current'), 'location');
});
