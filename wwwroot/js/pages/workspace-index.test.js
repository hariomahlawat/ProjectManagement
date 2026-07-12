const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'workspace-index.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

function createWorkspaceDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <header class="pm-topbar"></header>
        <nav class="po-section-nav" aria-label="Workspace sections">
            <a href="#action-queue" class="active" aria-current="page">Actions</a>
            <a href="#assigned-projects">Projects</a>
            <a href="#follow-ups">Follow-ups</a>
        </nav>
        <section id="action-queue"><div class="po-panel__head"></div></section>
        <section id="assigned-projects"><div class="po-panel__head"></div></section>
        <section id="follow-ups"><div class="po-panel__head"></div></section>
    </body></html>`, { url: 'https://example.test/Workspace', runScripts: 'dangerously' });

    const { window } = dom;
    window.matchMedia = () => ({
        matches: false,
        addEventListener() {},
        removeEventListener() {}
    });
    window.requestAnimationFrame = callback => callback();
    window.scrollTo = options => {
        window.__lastScrollOptions = options;
    };
    window.ResizeObserver = class {
        observe() {}
        disconnect() {}
    };

    const topbar = window.document.querySelector('.pm-topbar');
    const nav = window.document.querySelector('.po-section-nav');
    topbar.getBoundingClientRect = () => ({ top: 0, height: 68 });
    nav.getBoundingClientRect = () => ({ top: 68, height: 44 });

    const sections = Array.from(window.document.querySelectorAll('section'));
    sections.forEach((section, index) => {
        const top = 140 + (index * 420);
        section.getBoundingClientRect = () => ({ top, height: 300 });
        section.querySelector('.po-panel__head').getBoundingClientRect = () => ({ top, height: 50 });
    });

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
    assert.equal(window.location.hash, '#assigned-projects');
});

test('workspace script publishes measured sticky offsets', () => {
    const { document } = createWorkspaceDom();
    const style = document.documentElement.style;

    assert.equal(style.getPropertyValue('--po-topbar-height'), '68px');
    assert.equal(style.getPropertyValue('--po-section-nav-height'), '44px');
});
