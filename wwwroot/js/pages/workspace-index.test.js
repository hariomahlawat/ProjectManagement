const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'workspace-index.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

// SECTION: Workspace rail fixture with shared action queue anchors.
function createWorkspaceRailDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <nav aria-label="Workspace sections">
            <a href="#today" class="workspace-rail-link active" data-workspace-section="today" aria-current="true">Today</a>
            <a href="#action-queue" class="workspace-rail-link" data-workspace-section="action-queue">Remarks Due</a>
            <a href="#action-queue" class="workspace-rail-link" data-workspace-section="action-queue">Other Assigned Tasks</a>
            <a href="#action-queue" class="workspace-rail-link" data-workspace-section="action-queue">AOTS</a>
        </nav>
        <div id="today"></div>
        <section id="action-queue"></section>
    </body></html>`, { url: 'https://example.test/Workspace', runScripts: 'dangerously' });

    const { window } = dom;
    window.IntersectionObserver = class {
        observe() { }
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);

    return { window, document: window.document };
}

// SECTION: Shared target click behavior.
test('workspace rail preserves the clicked item when multiple links share a section', () => {
    const { window, document } = createWorkspaceRailDom();
    const links = Array.from(document.querySelectorAll('.workspace-rail-link'));
    const remarksLink = links[1];
    const tasksLink = links[2];

    remarksLink.dispatchEvent(new window.Event('click', { bubbles: true }));
    assert.equal(document.querySelector('.workspace-rail-link.active'), remarksLink);
    assert.equal(remarksLink.getAttribute('aria-current'), 'true');

    tasksLink.dispatchEvent(new window.Event('click', { bubbles: true }));
    assert.equal(document.querySelector('.workspace-rail-link.active'), tasksLink);
    assert.equal(tasksLink.getAttribute('aria-current'), 'true');
    assert.equal(remarksLink.hasAttribute('aria-current'), false);
});
