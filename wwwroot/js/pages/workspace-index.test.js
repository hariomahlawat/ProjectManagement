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
            <a href="#record-gaps">Record gaps</a>
        </nav>
        <section id="action-queue"></section>
        <section id="assigned-projects"></section>
        <section id="record-gaps"></section>
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
    assert.equal(links[1].getAttribute('aria-current'), 'page');
    assert.equal(links[0].hasAttribute('aria-current'), false);
});
