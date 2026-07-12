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

// SECTION: Native assignee select placeholder behavior.
test('native Direct Task assignee select preserves the configured placeholder', () => {
    const { document } = createSearchableSelectDom();
    const select = document.querySelector('select[data-at-searchable-select="true"]');
    const placeholderOption = select.querySelector('option[value=""]');

    assert.ok(select);
    assert.equal(document.querySelector('.at-select-search-input'), null);
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
        <div class="at-task-command-shell" data-at-action-shell="true">
            <section class="at-action-form-section">
                <details data-at-action-panel="update">
                    <summary>Update Progress</summary>
                    <textarea name="UpdateInput.Body"></textarea>
                    <select name="UpdateInput.NewStatus" data-at-progress-status-select data-current-status="${currentStatus}">
                        <option value="">No status change</option>
                        <option value="Open">Open</option>
                        <option value="In Progress">In Progress</option>
                        <option value="Submitted">Submitted</option>
                    </select>
                    <button type="submit">Save Update</button>
                </details>
            </section>
            <button type="button" data-at-open-action="update" data-at-target-status="In Progress" data-test-action="mark-progress">Mark In Progress</button>
            <button type="button" data-at-open-action="update" data-at-target-status="In Progress" data-test-action="return-rework">Return for Rework</button>
            <section class="at-more-actions-panel" data-test-more-panel>
                <button type="button">Other action</button>
            </section>
        </div>
    </body></html>`, { url: 'https://example.test/ActionTasks?TaskId=1', runScripts: 'dangerously' });

    const { window } = dom;
    const document = window.document;
    document.querySelector('[data-at-progress-status-select]').value = currentStatus;
    const scriptEl = document.createElement('script');
    scriptEl.textContent = scriptContent;
    document.body.appendChild(scriptEl);
    document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));

    return { window, document };
}


// SECTION: Inspector action panel behavior with inline More Actions panel.
test('inspector actions keep inline More Actions visible while Escape closes action panels', () => {
    const { window, document } = createInspectorActionDom();
    const shell = document.querySelector('[data-at-action-shell]');
    const panel = document.querySelector('[data-at-action-panel="update"]');
    const morePanel = document.querySelector('[data-test-more-panel]');
    const openButton = document.querySelector('[data-test-action="mark-progress"]');

    openButton.dispatchEvent(new window.Event('click', { bubbles: true }));

    assert.equal(panel.hasAttribute('open'), true);
    assert.ok(morePanel);

    shell.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    assert.equal(panel.hasAttribute('open'), false);
    assert.ok(morePanel);
});

// SECTION: Status intent action behavior.
test('intent-specific status actions preselect In Progress and refresh save guard', () => {
    [
        { action: 'mark-progress', currentStatus: 'Open' },
        { action: 'return-rework', currentStatus: 'Submitted' }
    ].forEach(({ action, currentStatus }) => {
        const { window, document } = createInspectorActionDom(currentStatus);
        const shell = document.querySelector('[data-at-action-shell]');
        const panel = document.querySelector('[data-at-action-panel="update"]');
        const select = document.querySelector('[data-at-progress-status-select]');
        const openButton = document.querySelector(`[data-test-action="${action}"]`);
        let bubbledChangeCount = 0;

        assert.equal(select.value, currentStatus);
        shell.addEventListener('change', () => {
            bubbledChangeCount += 1;
        });

        openButton.dispatchEvent(new window.Event('click', { bubbles: true }));

        assert.equal(panel.hasAttribute('open'), true);
        assert.equal(select.value, 'In Progress');
        assert.equal(bubbledChangeCount, 1);
    });
});

// SECTION: Direct close remarks guard behavior.
test('direct close submit stays disabled until closure remarks are entered', () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <form data-at-direct-close-form="true">
            <textarea data-at-direct-close-remarks="true"></textarea>
            <button type="submit" data-at-direct-close-submit="true">Close Task</button>
        </form>
    </body></html>`, { url: 'https://example.test/ActionTasks?TaskId=1', runScripts: 'dangerously' });

    const { window } = dom;
    const document = window.document;
    const scriptEl = document.createElement('script');
    scriptEl.textContent = scriptContent;
    document.body.appendChild(scriptEl);
    document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));

    const remarks = document.querySelector('[data-at-direct-close-remarks]');
    const submit = document.querySelector('[data-at-direct-close-submit]');

    assert.equal(submit.disabled, true);
    remarks.value = 'Closed after command review.';
    remarks.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.equal(submit.disabled, false);
    remarks.value = '   ';
    remarks.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.equal(submit.disabled, true);
});

// SECTION: Typed task update composer behavior.
function createTypedUpdateComposerDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div class="at-task-command-shell" data-at-action-shell="true">
            <details data-at-action-panel="update" open>
                <summary>Update</summary>
                <form>
                    <input type="hidden" value="Progress" data-at-update-type />
                    <div data-at-update-panel-title>Add update</div>
                    <div role="group" aria-label="Update type">
                        <button type="button" class="is-active" data-at-update-type-option="Progress" aria-pressed="true">Progress</button>
                        <button type="button" data-at-update-type-option="Conference" aria-pressed="false">Conference</button>
                    </div>
                    <label data-at-update-body-label>Update</label>
                    <textarea data-at-update-body data-default-placeholder="Write a clear progress update" placeholder="Write a clear progress update"></textarea>
                    <div class="d-none" data-at-conference-guidance>Conference guidance</div>
                    <div data-at-update-status-fields>
                        <select data-at-progress-status-select data-current-status="Open">
                            <option value="">No status change</option>
                            <option value="In Progress">In Progress</option>
                        </select>
                    </div>
                    <button type="submit"><span data-at-update-submit-label>Post update</span></button>
                </form>
            </details>
        </div>
    </body></html>`, { url: 'https://example.test/ActionTasks?TaskId=1', runScripts: 'dangerously' });

    const { window } = dom;
    const document = window.document;
    const scriptEl = document.createElement('script');
    scriptEl.textContent = scriptContent;
    document.body.appendChild(scriptEl);
    document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));

    return { window, document };
}

test('typed update composer keeps conference remarks in the common update form without permitting a status change', () => {
    const { window, document } = createTypedUpdateComposerDom();
    const form = document.querySelector('[data-at-action-panel="update"] form');
    const typeInput = document.querySelector('[data-at-update-type]');
    const progressButton = document.querySelector('[data-at-update-type-option="Progress"]');
    const conferenceButton = document.querySelector('[data-at-update-type-option="Conference"]');
    const title = document.querySelector('[data-at-update-panel-title]');
    const bodyLabel = document.querySelector('[data-at-update-body-label]');
    const body = document.querySelector('[data-at-update-body]');
    const guidance = document.querySelector('[data-at-conference-guidance]');
    const statusFields = document.querySelector('[data-at-update-status-fields]');
    const statusSelect = document.querySelector('[data-at-progress-status-select]');
    const submitLabel = document.querySelector('[data-at-update-submit-label]');

    statusSelect.value = 'In Progress';
    conferenceButton.dispatchEvent(new window.Event('click', { bubbles: true }));

    assert.equal(typeInput.value, 'Conference');
    assert.equal(form.classList.contains('is-conference'), true);
    assert.equal(conferenceButton.getAttribute('aria-pressed'), 'true');
    assert.equal(progressButton.getAttribute('aria-pressed'), 'false');
    assert.equal(title.textContent, 'Add conference remark');
    assert.equal(bodyLabel.textContent, 'Conference direction');
    assert.equal(body.placeholder, 'Record the direction or observation issued during the conference');
    assert.equal(guidance.classList.contains('d-none'), false);
    assert.equal(statusFields.classList.contains('d-none'), true);
    assert.equal(statusSelect.value, '');
    assert.equal(submitLabel.textContent, 'Add conference remark');

    progressButton.dispatchEvent(new window.Event('click', { bubbles: true }));

    assert.equal(typeInput.value, 'Progress');
    assert.equal(form.classList.contains('is-conference'), false);
    assert.equal(progressButton.getAttribute('aria-pressed'), 'true');
    assert.equal(conferenceButton.getAttribute('aria-pressed'), 'false');
    assert.equal(title.textContent, 'Add update');
    assert.equal(bodyLabel.textContent, 'Update');
    assert.equal(body.placeholder, 'Write a clear progress update');
    assert.equal(guidance.classList.contains('d-none'), true);
    assert.equal(statusFields.classList.contains('d-none'), false);
    assert.equal(submitLabel.textContent, 'Post update');
});
