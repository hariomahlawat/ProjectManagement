const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'remarks-panel.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

function createPanelDom() {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div data-panel-project-id="1" data-config="{}">
            <div data-remarks-items></div>
            <div data-remarks-empty></div>
            <div data-remarks-pagination></div>
            <form data-remarks-composer>
                <textarea data-remarks-body></textarea>
                <button type="submit" data-remarks-submit>Submit</button>
            </form>
        </div>
    </body></html>`, { url: 'https://example.test/', runScripts: 'dangerously' });

    const { window } = dom;
    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);

    const root = window.document.querySelector('[data-panel-project-id]');
    const panel = window.ProjectRemarks.createRemarksPanel(root, () => { });
    return { dom, window, document: window.document, root, panel };
}

function makeRemark(overrides = {}) {
    return {
        id: 1,
        rowVersion: 'rv',
        isDeleted: false,
        authorInitials: 'AB',
        authorDisplayName: 'Alice Bob',
        authorUserId: 'user-123',
        type: 'Internal',
        body: '<p>Test remark</p>',
        eventDate: null,
        stageRef: null,
        stageName: null,
        createdAtUtc: new Date().toISOString(),
        lastEditedAtUtc: null,
        ...overrides
    };
}

test('inserting a mention hides the user id but serializes placeholders', () => {
    const { panel } = createPanelDom();
    const textarea = panel.bodyField;
    const autocomplete = panel.mentionAutocompletes.get(textarea);

    textarea.value = '@';
    textarea.selectionStart = 1;
    textarea.selectionEnd = 1;
    autocomplete.triggerIndex = 0;

    autocomplete.insertPlaceholder({ id: 'user-1', displayName: 'John Doe' });

    assert.equal(textarea.value, '@John Doe ');
    const mentionMap = panel.mentionMaps.get(textarea);
    assert.ok(mentionMap);
    const storedMention = mentionMap.get('John Doe');
    assert.ok(storedMention);
    assert.equal(storedMention.id, 'user-1');
    assert.equal(storedMention.label, 'John Doe');

    const serialized = panel.serializeTextWithMentions(textarea);
    assert.equal(serialized.trim(), '@[John Doe](user:user-1)');
});

test('decodeBody restores mentions map for editing', () => {
    const { panel } = createPanelDom();
    const editMap = new Map();
    const bodyHtml = '<p>Hello <span class="remark-mention" data-user-id="user-2">Jane Smith</span></p>';

    const decoded = panel.decodeBody(bodyHtml, editMap);

    assert.equal(decoded, 'Hello @Jane Smith');
    const restoredMention = editMap.get('Jane Smith');
    assert.ok(restoredMention);
    assert.equal(restoredMention.id, 'user-2');
    assert.equal(restoredMention.label, 'Jane Smith');

    const serialized = panel.serializeTextWithMentions(null, decoded, editMap);
    assert.equal(serialized, 'Hello @[Jane Smith](user:user-2)');
});

test('saveEdit serializes mentions using the stored mapping', async () => {
    const { panel, window, document } = createPanelDom();
    panel.actorHasOverride = true;
    panel.toastHandler = () => { };
    panel.buildHeaders = () => ({ });

    const remark = {
        id: 1,
        projectId: 1,
        type: 'Internal',
        authorRole: '',
        authorUserId: 'author-1',
        authorDisplayName: 'Author One',
        authorInitials: 'AO',
        body: '<p>@[John Doe](user:user-1)</p>',
        eventDate: panel.today,
        stageRef: null,
        stageName: null,
        createdAtUtc: new Date().toISOString(),
        lastEditedAtUtc: null,
        isDeleted: false,
        deletedAtUtc: null,
        deletedByUserId: null,
        deletedByRole: null,
        deletedByDisplayName: null,
        rowVersion: 'rv1'
    };

    panel.currentUserId = 'author-1';
    panel.state.items = [remark];
    panel.editingId = 1;
    panel.editDraft = '@John Doe';

    const article = document.createElement('article');
    article.setAttribute('data-remark-id', '1');
    const textarea = document.createElement('textarea');
    textarea.setAttribute('data-remark-edit', 'body');
    textarea.value = '@John Doe';
    article.appendChild(textarea);
    panel.listContainer.appendChild(article);

    const editMap = new Map();
    panel.registerMentionInMap(editMap, { id: 'user-1', label: 'John Doe' });
    panel.mentionMaps.set(textarea, editMap);
    panel.editMentionMap = editMap;

    let requestBody = null;
    window.fetch = async (url, options) => {
        requestBody = JSON.parse(options.body);
        return {
            ok: true,
            status: 200,
            json: async () => ({
                id: 1,
                projectId: 1,
                type: 'Internal',
                authorRole: '',
                authorUserId: 'author-1',
                authorDisplayName: 'Author One',
                authorInitials: 'AO',
                body: '@[John Doe](user:user-1)',
                eventDate: panel.today,
                stageRef: null,
                stageName: null,
                createdAtUtc: remark.createdAtUtc,
                lastEditedAtUtc: new Date().toISOString(),
                isDeleted: false,
                deletedAtUtc: null,
                deletedByUserId: null,
                deletedByRole: null,
                deletedByDisplayName: null,
                rowVersion: 'rv2'
            })
        };
    };

    await panel.saveEdit(1);

    assert.ok(requestBody);
    assert.equal(requestBody.body, '@[John Doe](user:user-1)');
});

test('buildRemarkElement applies role accent classes for canonical roles', () => {
    const { panel } = createPanelDom();

    const baseRemark = makeRemark();

    const commandantArticle = panel.buildRemarkElement({
        ...baseRemark,
        id: 101,
        authorRole: 'Commandant'
    });
    const commandantBadge = commandantArticle.querySelector('.remarks-role-badge');
    assert.ok(commandantBadge);
    assert.ok(commandantBadge.classList.contains('remarks-role-comdt'));
    assert.ok(commandantArticle.classList.contains('remarks-role-comdt'));

    const hodArticle = panel.buildRemarkElement({
        ...baseRemark,
        id: 102,
        authorRole: 'HeadOfDepartment'
    });
    const hodBadge = hodArticle.querySelector('.remarks-role-badge');
    assert.ok(hodBadge);
    assert.ok(hodBadge.classList.contains('remarks-role-hod'));
    assert.ok(hodArticle.classList.contains('remarks-role-hod'));
});

test('buildRemarkElement uses helper layout classes for header structure', () => {
    const { panel } = createPanelDom();

    const remark = makeRemark();
    const article = panel.buildRemarkElement(remark);

    assert.ok(article.classList.contains('remarks-item'));
    assert.ok(article.classList.contains('remarks-item-compact'));

    const header = article.querySelector('.remarks-header');
    assert.ok(header);
    assert.ok(header.classList.contains('d-flex'));
    assert.ok(header.classList.contains('flex-sm-nowrap'));

    const identity = header.querySelector('.remarks-identity');
    assert.ok(identity);

    const nameRow = identity.querySelector('.remarks-name');
    assert.ok(nameRow);
    assert.ok(nameRow.classList.contains('d-flex'));

    const timestamp = identity.querySelector('.remarks-timestamp');
    assert.ok(timestamp);
    assert.equal(timestamp.tagName, 'TIME');
    assert.equal(timestamp.parentElement, identity);
    assert.ok(!nameRow.contains(timestamp));
});

test('non-override author sees inline action buttons within edit window', () => {
    const { panel } = createPanelDom();
    panel.actorHasOverride = false;
    panel.currentUserId = 'user-123';

    const article = panel.buildRemarkElement(makeRemark());
    const actions = article.querySelector('.remarks-actions');
    assert.ok(actions);

    const header = actions.parentElement;
    assert.ok(header);
    assert.ok(header.classList.contains('remarks-header'));
    assert.ok(actions.classList.contains('remarks-actions'));

    const editButton = actions.querySelector('button[data-remark-action="edit"]');
    const deleteButton = actions.querySelector('button[data-remark-action="delete"]');
    assert.ok(editButton);
    assert.ok(deleteButton);
    assert.strictEqual(editButton.textContent.trim(), 'Edit');
    assert.strictEqual(deleteButton.textContent.trim(), 'Delete');
});

test('override actor within edit window gets icon action buttons', () => {
    const { panel } = createPanelDom();
    panel.actorHasOverride = true;
    panel.currentUserId = 'other-user';

    const article = panel.buildRemarkElement(makeRemark());
    const actions = article.querySelector('.remarks-actions');
    assert.ok(actions);

    const buttons = actions.querySelectorAll('button[data-remark-action]');
    assert.equal(buttons.length, 2);

    buttons.forEach((button) => {
        assert.ok(button.classList.contains('btn-icon'));
        const icon = button.querySelector('i');
        assert.ok(icon);
        const srText = button.querySelector('.visually-hidden');
        assert.ok(srText);
        assert.ok(srText.textContent.trim().length > 0);
        assert.ok(button.getAttribute('aria-label'));
    });
});

test('override actor outside edit window gets dropdown actions', () => {
    const { panel } = createPanelDom();
    panel.actorHasOverride = true;
    panel.currentUserId = 'other-user';

    const oldTimestamp = new Date(Date.now() - (4 * 60 * 60 * 1000)).toISOString();
    const article = panel.buildRemarkElement(makeRemark({ createdAtUtc: oldTimestamp }));
    const actions = article.querySelector('.remarks-actions');
    assert.ok(actions);

    const dropdown = actions.querySelector('.dropdown');
    assert.ok(dropdown);

    const toggle = dropdown.querySelector('button.dropdown-toggle');
    assert.ok(toggle);
    assert.ok(toggle.classList.contains('btn-icon'));
    assert.strictEqual(toggle.getAttribute('data-bs-toggle'), 'dropdown');
    assert.strictEqual(toggle.getAttribute('aria-expanded'), 'false');

    const menu = dropdown.querySelector('.dropdown-menu');
    assert.ok(menu);
    assert.ok(menu.classList.contains('dropdown-menu-end'));
    assert.ok(menu.id);
    assert.strictEqual(toggle.getAttribute('aria-controls'), menu.id);

    const items = menu.querySelectorAll('[data-remark-action]');
    assert.equal(items.length, 2);
    const editItem = menu.querySelector('[data-remark-action="edit"]');
    const deleteItem = menu.querySelector('[data-remark-action="delete"]');
    assert.ok(editItem);
    assert.ok(deleteItem);
});
