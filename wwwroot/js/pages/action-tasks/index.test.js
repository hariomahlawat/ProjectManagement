const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'index.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

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
