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
