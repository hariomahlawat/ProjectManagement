const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

// PO Timeline discovery C6.1: settled stages collapse into one <details> group.
// Deep links and status-line links must still reach a stage inside it.
const scriptContent = fs.readFileSync(path.resolve(__dirname, '..', 'pages', 'project-portfolio.js'), 'utf8');

function stage(code, extraClass = '') {
    return `<div id="timeline-stage-${code}" class="pm-item ${extraClass}" data-stage-row="${code}" data-timeline-stage>
        <div class="pm-item-card" data-timeline-stage-card><span data-stage-name>${code}</span></div>
    </div>`;
}

function createDom(url) {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div class="project-portfolio">
            <div class="pm-items">
                <details class="pm-completed-group" data-timeline-completed-group>
                    <summary>2 completed stages</summary>
                    <div class="pm-completed-group__items">
                        ${stage('FS', 'is-complete')}
                        ${stage('SOW', 'is-complete')}
                    </div>
                </details>
                ${stage('IPA', 'is-active')}
            </div>
        </div>
    </body></html>`, { url, runScripts: 'dangerously', pretendToBeVisual: true });

    const scrolled = [];
    dom.window.Element.prototype.scrollIntoView = function scrollIntoView() {
        scrolled.push(this.getAttribute('data-stage-row'));
    };

    const script = dom.window.document.createElement('script');
    script.textContent = scriptContent;
    dom.window.document.body.appendChild(script);
    const group = dom.window.document.querySelector('[data-timeline-completed-group]');
    return { dom, window: dom.window, group, scrolled };
}

test('the completed group stays collapsed when the page opens without a stage link', () => {
    const { group } = createDom('https://prism.test/Projects/Overview/214');
    assert.equal(group.open, false);
});

test('a deep link to a stage inside the group opens the group on load', () => {
    const { group } = createDom('https://prism.test/Projects/Overview/214#timeline-stage-SOW');
    assert.equal(group.open, true);
});

test('the timeline-stage query parameter also opens the group (case-insensitive)', () => {
    const { group } = createDom('https://prism.test/Projects/Overview/214?timeline-stage=fs');
    assert.equal(group.open, true);
});

test('a link to a stage outside the group leaves the group collapsed', () => {
    const { group } = createDom('https://prism.test/Projects/Overview/214#timeline-stage-IPA');
    assert.equal(group.open, false);
});

test('following an in-page link to a grouped stage opens the group and scrolls to the stage', () => {
    const { window, group, scrolled } = createDom('https://prism.test/Projects/Overview/214');

    window.location.hash = '#timeline-stage-SOW';
    window.dispatchEvent(new window.HashChangeEvent('hashchange'));

    assert.equal(group.open, true);
    assert.deepEqual(scrolled, ['SOW']);
});
