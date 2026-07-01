const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, '..', 'pages', 'project-ideas-index.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

function createBoardDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <form class="pi-filter-form">
            <input name="Query" value="radar" />
            <button type="button" class="js-clear-search">Clear</button>
            <select id="projectOfficerFilter" class="js-auto-submit">
                <option value="">All</option>
                <option value="po-1" selected>Officer</option>
            </select>
            <select id="assignmentFilter" class="js-auto-submit">
                <option value="all">All</option>
                <option value="unassigned" selected>Unassigned</option>
            </select>
        </form>
        <table><tbody>
            <tr class="js-idea-row" data-href="#idea-1" tabindex="0">
                <td><a href="#title-link">Idea one</a></td>
                <td class="row-background">Open</td>
            </tr>
        </tbody></table>
    </body></html>`, {
        url: 'https://example.test/ProjectIdeas',
        runScripts: 'dangerously'
    });

    const { window } = dom;
    const form = window.document.querySelector('.pi-filter-form');
    let submitCount = 0;
    form.requestSubmit = () => { submitCount += 1; };

    const script = window.document.createElement('script');
    script.textContent = scriptContent;
    window.document.body.appendChild(script);

    return {
        window,
        document: window.document,
        submitCount: () => submitCount
    };
}

test('table row opens from row background and keyboard without hijacking links', () => {
    const { window, document } = createBoardDom();
    const row = document.querySelector('.js-idea-row');
    const background = document.querySelector('.row-background');
    const titleLink = row.querySelector('a');

    background.dispatchEvent(new window.MouseEvent('click', { bubbles: true }));
    assert.equal(window.location.hash, '#idea-1');

    window.location.hash = '';
    row.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    assert.equal(window.location.hash, '#idea-1');

    window.location.hash = '';
    titleLink.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true }));
    assert.notEqual(window.location.hash, '#idea-1');
});

test('officer and assignment filters remain mutually coherent before auto-submit', () => {
    const { window, document, submitCount } = createBoardDom();
    const officer = document.getElementById('projectOfficerFilter');
    const assignment = document.getElementById('assignmentFilter');

    officer.value = 'po-1';
    assignment.value = 'unassigned';
    officer.dispatchEvent(new window.Event('change', { bubbles: true }));

    assert.equal(assignment.value, 'all');
    assert.equal(submitCount(), 1);

    officer.value = 'po-1';
    assignment.value = 'unassigned';
    assignment.dispatchEvent(new window.Event('change', { bubbles: true }));

    assert.equal(officer.value, '');
    assert.equal(submitCount(), 2);
});

test('clear search empties the query and submits the current filters', () => {
    const { window, document, submitCount } = createBoardDom();
    const query = document.querySelector('input[name="Query"]');
    const clear = document.querySelector('.js-clear-search');

    clear.dispatchEvent(new window.MouseEvent('click', { bubbles: true }));

    assert.equal(query.value, '');
    assert.equal(submitCount(), 1);
});
