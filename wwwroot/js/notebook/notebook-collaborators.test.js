const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { JSDOM } = require('jsdom');

async function loadCollaboratorsModule(apiSource) {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'notebook-collaborators-test-'));
  fs.writeFileSync(path.join(tempDir, 'package.json'), '{"type":"module"}');
  fs.writeFileSync(path.join(tempDir, 'notebook-api.mjs'), apiSource);
  fs.writeFileSync(path.join(tempDir, 'notebook-reconcile.mjs'), `
    export async function reconcileMutation() {}
    export function requireMutationItem(value) { return value?.item ?? value; }
    export function updateCardConcurrencyState(card, item) { if (card && item?.version) card.dataset.version = item.version; }
  `);
  fs.writeFileSync(path.join(tempDir, 'notebook-confirm-dialog.mjs'), 'export async function confirmNotebookAction() { return true; }');
  const source = fs.readFileSync(path.resolve(__dirname, 'notebook-collaborators.js'), 'utf8')
    .replace('./notebook-api.js', './notebook-api.mjs')
    .replace('./notebook-reconcile.js', './notebook-reconcile.mjs')
    .replace('./notebook-confirm-dialog.js', './notebook-confirm-dialog.mjs');
  const modulePath = path.join(tempDir, 'notebook-collaborators.mjs');
  fs.writeFileSync(modulePath, source);
  return import(`file://${modulePath}`);
}

function dialogMarkup() {
  return `<!doctype html><body>
    <article data-note-id="note-1" data-access-level="Owner" data-version="v1"></article>
    <div data-notebook-collaborators-dialog hidden>
      <div data-collaborators-close></div>
      <section class="notebook-collaborators-dialog__panel">
        <p data-collaborators-intro></p>
        <div data-collaborators-management>
          <div data-collaborators-search-wrap><input data-collaborator-search><span data-collaborator-search-spinner hidden></span></div>
          <div data-collaborator-search-results hidden></div>
          <section data-collaborator-share-panel hidden>
            <span data-share-avatar></span><strong data-share-name></strong><small data-share-email></small>
            <select data-share-role><option value="Viewer">View only</option><option value="Editor">Can edit</option></select>
            <button data-share-cancel>Cancel</button><button data-share-confirm>Share</button>
          </section>
        </div>
        <div data-collaborator-list></div><div data-collaborators-empty hidden></div>
        <span data-collaborators-status></span><button data-collaborators-close>Done</button>
      </section>
    </div>
  </body>`;
}

const wait = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

test('collaborator dialog defaults new shares to View only and supports role changes', async () => {
  const dom = new JSDOM(dialogMarkup(), { url: 'https://example.test/Notebook', pretendToBeVisual: true });
  global.window = dom.window;
  global.document = dom.window.document;
  global.AbortController = dom.window.AbortController;
  global.CustomEvent = dom.window.CustomEvent;

  const apiSource = `
    globalThis.__notebookCollaboratorCalls = [];
    const calls = globalThis.__notebookCollaboratorCalls;
    let current = { id: 'note-1', version: 'v1', accessLevel: 'Owner', canManageCollaborators: true, collaborators: [] };
    const owner = { userId: 'owner', displayName: 'Owner One', email: 'owner@example.test', initials: 'OO', role: 'Editor', isOwner: true };
    let collaborator = null;
    export const NotebookApi = {
      getItem: async () => current,
      getCollaborators: async () => [owner, ...(collaborator ? [collaborator] : [])],
      searchCollaborators: async () => [{ userId: 'user-2', displayName: 'Viewer Two', email: 'viewer@example.test', initials: 'VT' }],
      addCollaborator: async (id, userId, role, version) => {
        calls.push(['add', id, userId, role, version]);
        collaborator = { userId, displayName: 'Viewer Two', email: 'viewer@example.test', initials: 'VT', role, isOwner: false };
        current = { ...current, version: 'v2', collaborators: [owner, collaborator] };
        return { item: current };
      },
      updateCollaboratorRole: async (id, userId, role, version) => {
        calls.push(['role', id, userId, role, version]);
        collaborator = { ...collaborator, role };
        current = { ...current, version: 'v3', collaborators: [owner, collaborator] };
        return { item: current };
      },
      removeCollaborator: async () => ({ item: current }),
      getCardHtml: async () => ''
    };
  `;
  const module = await loadCollaboratorsModule(apiSource);
  const collaborators = module.initNotebookCollaborators(document, {
    board: {}, view: 'home', applyCounts: () => {}, showError: (message) => assert.fail(message)
  });
  const card = document.querySelector('[data-note-id="note-1"]');
  await collaborators.open(card);

  const search = document.querySelector('[data-collaborator-search]');
  search.value = 'Vie';
  search.dispatchEvent(new window.Event('input', { bubbles: true }));
  await wait(300);
  document.querySelector('[data-select-collaborator="user-2"]').click();

  assert.equal(document.querySelector('[data-share-role]').value, 'Viewer');
  assert.equal(document.querySelector('[data-collaborator-share-panel]').hidden, false);
  document.querySelector('[data-share-confirm]').click();
  await wait(20);

  const roleSelect = document.querySelector('[data-collaborator-role="user-2"]');
  assert.ok(roleSelect);
  assert.equal(roleSelect.value, 'Viewer');
  roleSelect.value = 'Editor';
  roleSelect.dispatchEvent(new window.Event('change', { bubbles: true }));
  await wait(20);

  assert.deepEqual(global.__notebookCollaboratorCalls[0], ['add', 'note-1', 'user-2', 'Viewer', 'v1']);
  assert.deepEqual(global.__notebookCollaboratorCalls[1], ['role', 'note-1', 'user-2', 'Editor', 'v2']);
  assert.equal(document.querySelector('[data-collaborator-role="user-2"]').value, 'Editor');
  assert.match(document.querySelector('[data-collaborators-status]').textContent, /Can edit/i);

  delete global.window;
  delete global.document;
  delete global.AbortController;
  delete global.CustomEvent;
  delete global.__notebookCollaboratorCalls;
});
