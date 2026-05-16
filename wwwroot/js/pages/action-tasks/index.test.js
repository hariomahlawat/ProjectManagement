const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'index.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');


// SECTION: Searchable select fixture.
function createSearchableSelectDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <select data-at-searchable-select="true" data-at-placeholder="Select user">
            <option value="">Select user</option>
            <option value="user-1">User One</option>
        </select>
    </body></html>`, { url: 'https://example.test/ActionTasks', runScripts: 'dangerously' });

    const { window } = dom;
    const document = window.document;
    const scriptEl = document.createElement('script');
    scriptEl.textContent = scriptContent;
    document.body.appendChild(scriptEl);
    document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));

    return { document };
}

// SECTION: Searchable select placeholder behavior.
test('searchable select uses the configured Direct Task assignee placeholder', () => {
    const { document } = createSearchableSelectDom();
    const searchInput = document.querySelector('.at-select-search-input');
    const placeholderOption = document.querySelector('select option[value=""]');

    assert.ok(searchInput);
    assert.equal(searchInput.placeholder, 'Select user');
    assert.equal(placeholderOption.textContent, 'Select user');
});

function createReportsFilterDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <form method="get" data-at-reports-filter-form="true">
            <input type="hidden" name="ViewMode" value="Reports" />
            <select name="ReportSprintId" data-at-reports-filter-control="true">
                <option value="">All</option>
                <option value="1">Sprint 1</option>
            </select>
            <select name="ReportAssigneeUserId" data-at-reports-filter-control="true">
                <option value="">All</option>
                <option value="user-1">User One</option>
            </select>
            <input type="date" name="ReportFromDate" data-at-reports-date-filter="true" />
            <input type="date" name="ReportToDate" value="2026-05-01" data-at-reports-date-filter="true" />
            <select name="ReportStatus" data-at-reports-filter-control="true">
                <option value="">All</option>
                <option value="Blocked">Blocked</option>
            </select>
            <select name="ReportPriority" data-at-reports-filter-control="true">
                <option value="">All</option>
                <option value="High">High</option>
            </select>
            <span data-at-reports-filter-loading="true"></span>
            <a href="/ActionTasks?ViewMode=Reports">Reset / Clear all</a>
        </form>
    </body></html>`, { url: 'https://example.test/ActionTasks?ViewMode=Reports', runScripts: 'dangerously' });

    const { window } = dom;
    const document = window.document;
    const submissions = [];
    const form = document.querySelector('[data-at-reports-filter-form]');
    form.requestSubmit = () => {
        submissions.push(new window.URLSearchParams(new window.FormData(form)).toString());
    };

    const scriptEl = document.createElement('script');
    scriptEl.textContent = scriptContent;
    document.body.appendChild(scriptEl);
    document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));

    return { window, document, form, submissions };
}

test('reports dropdown filters auto-submit with query-state fields intact', () => {
    const { window, document, form, submissions } = createReportsFilterDom();
    const sprint = document.querySelector('select[name="ReportSprintId"]');

    sprint.value = '1';
    sprint.dispatchEvent(new window.Event('change', { bubbles: true }));

    assert.equal(submissions.length, 1);
    assert.ok(form.classList.contains('is-loading'));
    assert.equal(form.getAttribute('aria-busy'), 'true');
    assert.ok(submissions[0].includes('ViewMode=Reports'));
    assert.ok(submissions[0].includes('ReportSprintId=1'));
});

test('reports date filters submit on change or blur but not every typed input event', () => {
    const { window, document, submissions } = createReportsFilterDom();
    const fromDate = document.querySelector('input[name="ReportFromDate"]');

    fromDate.value = '2026-05-07';
    fromDate.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.equal(submissions.length, 0);

    fromDate.dispatchEvent(new window.Event('blur', { bubbles: true }));
    assert.equal(submissions.length, 1);
    assert.ok(submissions[0].includes('ReportFromDate=2026-05-07'));

    fromDate.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(submissions.length, 1);
});

// SECTION: Inspector status action fixture.
function createInspectorActionDom(currentStatus = 'Open') {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div data-at-action-shell="true">
            <details data-at-action-panel="status">
                <summary>Status</summary>
                <select name="status" data-at-status-select data-current-status="${currentStatus}">
                    <option value="Open">Open</option>
                    <option value="In Progress">In Progress</option>
                    <option value="Submitted">Submitted</option>
                </select>
                <button type="submit" data-at-status-submit>Save Status</button>
            </details>
            <button type="button" data-at-open-action="status" data-at-target-status="In Progress" data-test-action="mark-progress">Mark In Progress</button>
            <button type="button" data-at-open-action="status" data-at-target-status="In Progress" data-test-action="return-rework">Return for Rework</button>
        </div>
    </body></html>`, { url: 'https://example.test/ActionTasks?TaskId=1', runScripts: 'dangerously' });

    const { window } = dom;
    const document = window.document;
    document.querySelector('[data-at-status-select]').value = currentStatus;
    const scriptEl = document.createElement('script');
    scriptEl.textContent = scriptContent;
    document.body.appendChild(scriptEl);
    document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));

    return { window, document };
}

// SECTION: Status intent action behavior.
test('intent-specific status actions preselect In Progress and refresh save guard', () => {
    [
        { action: 'mark-progress', currentStatus: 'Open' },
        { action: 'return-rework', currentStatus: 'Submitted' }
    ].forEach(({ action, currentStatus }) => {
        const { window, document } = createInspectorActionDom(currentStatus);
        const shell = document.querySelector('[data-at-action-shell]');
        const panel = document.querySelector('[data-at-action-panel="status"]');
        const select = document.querySelector('[data-at-status-select]');
        const saveButton = document.querySelector('[data-at-status-submit]');
        const openButton = document.querySelector(`[data-test-action="${action}"]`);
        let bubbledChangeCount = 0;

        assert.equal(select.value, currentStatus);
        assert.equal(saveButton.disabled, true);

        shell.addEventListener('change', () => {
            bubbledChangeCount += 1;
        });

        openButton.dispatchEvent(new window.Event('click', { bubbles: true }));

        assert.equal(panel.hasAttribute('open'), true);
        assert.equal(select.value, 'In Progress');
        assert.equal(saveButton.disabled, false);
        assert.equal(bubbledChangeCount, 1);
    });
});
