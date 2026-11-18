// Behaviour test for the page's side of Add projects (project-briefing-decks.js): an Add posted in
// place offers Undo for exactly the projects it brought in, and Undo keeps the keyboard in the
// dialog and says the projects are ticked again (cold review 5, M1 and L9). The dialog module is
// played by a stand-in that answers the pbd:add-undone event as the real one does.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const rawSource = fs.readFileSync(path.resolve(__dirname, '..', 'pages', 'project-briefing-decks.js'), 'utf8');
const pageSource = rawSource
  .replace("import { prismConfirm } from './project-briefing-confirm.js';", 'const prismConfirm = async () => true;')
  .replace("import { initAddProjects } from './project-briefing-add-projects.js';", 'const initAddProjects = () => null;');

const MARKUP = `
<div data-pbd-root data-membership-url="/membership">
  <input type="hidden" name="__RequestVerificationToken" value="token" />
  <script type="application/json" data-pbd-studio-data>{"projects":[{"projectId":9}]}</script>
  <div data-deck-id="7">
    <form action="/settings" data-pbd-settings-form><input type="hidden" name="RowVersion" value="v1" /></form>
    <p data-pbd-membership-status></p>
    <div class="modal show" id="pbd-add-projects-modal">
      <form action="/add" data-pbd-add-selection-form>
        <input type="hidden" name="DeckId" value="7" />
        <input type="hidden" name="RowVersion" value="v1" />
        <input type="hidden" name="Kind" value="IndividualProjects" />
        <input type="hidden" name="ProjectIds" value="1" />
        <input type="hidden" name="ProjectIds" value="2" />
        <button type="submit">Add</button>
      </form>
      <p data-pbd-add-projects-status></p>
      <button type="button" data-pbd-filter-ticked disabled>Nothing ticked</button>
      <button type="button" data-pbd-filter-add>Add projects</button>
    </div>
  </div>
</div>`;

const startPage = () => {
  const dom = new JSDOM(`<!doctype html><html><body>${MARKUP}</body></html>`, {
    url: 'http://localhost/Workspace/BriefingDecks/7',
    runScripts: 'outside-only',
    pretendToBeVisual: true
  });
  const { window } = dom;
  const requests = [];
  window.fetch = (url, options) => new Promise((resolve) => {
    requests.push({
      url: String(url),
      options,
      answer: (body, status = 200) => resolve({
        ok: status >= 200 && status < 300,
        status,
        headers: { get: (name) => (name.toLowerCase() === 'content-type' ? 'application/json; charset=utf-8' : null) },
        json: async () => body
      })
    });
  });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  window.PrismToast = { show: () => ({ dismiss() {} }) };
  if (!window.Element.prototype.scrollIntoView) window.Element.prototype.scrollIntoView = () => {};
  if (!window.CSS) window.CSS = {};
  if (!window.CSS.escape) window.CSS.escape = (value) => String(value).replace(/["\\]/g, '\\$&');
  // The dialog module's part: it ticks the undone projects again and says how many.
  const undoneEvents = [];
  window.document.addEventListener('pbd:add-undone', (event) => {
    undoneEvents.push(event.detail.projectIds);
    event.detail.reticked = event.detail.projectIds.length;
    const ticked = window.document.querySelector('[data-pbd-filter-ticked]');
    ticked.disabled = false;
    ticked.textContent = `${event.detail.projectIds.length} ticked · Review`;
  });
  window.eval(pageSource);
  return { window, document: window.document, requests, undoneEvents };
};

const settle = async (window) => {
  for (let i = 0; i < 6; i += 1) await new Promise((resolve) => window.setTimeout(resolve, 0));
};

test('Undo after an Add takes out only what it added, keeps the keyboard in the dialog and says the projects are ticked again', async () => {
  const { window, document, requests, undoneEvents } = startPage();
  const form = document.querySelector('[data-pbd-add-selection-form]');
  const status = document.querySelector('[data-pbd-add-projects-status]');
  const settled = [];
  form.addEventListener('pbd:add-selection-settled', (event) => settled.push(event.detail));

  form.requestSubmit();
  await settle(window);
  assert.equal(new URL(requests[0].url, 'http://localhost').pathname, '/add');
  // Project 9 was already in the deck; 1 and 2 are new.
  requests[0].answer({ saved: true, message: '2 projects added.', rowVersion: 'v2', deck: { projects: [{ projectId: 9 }, { projectId: 1 }, { projectId: 2 }] } });
  await settle(window);
  assert.equal(settled.length, 1);
  assert.equal(settled[0].ok, true);

  const undo = status.querySelector('button');
  assert.equal(undo.textContent, 'Undo');
  undo.focus();
  undo.click();
  await settle(window);
  const membership = requests[1];
  assert.equal(new URL(membership.url, 'http://localhost').pathname, '/membership');
  assert.deepEqual(JSON.parse(membership.options.body).removeProjectIds, [1, 2]);
  membership.answer({ saved: true, removedCount: 2, rowVersion: 'v3', deck: { projects: [{ projectId: 9 }] } });
  await settle(window);

  assert.deepEqual(undoneEvents, [[1, 2]]);
  assert.equal(status.textContent, 'Removed the 2 projects just added. They are ticked again.');
  assert.equal(document.activeElement, document.querySelector('[data-pbd-filter-ticked]'), 'the keyboard lands on the ticked projects, not the page');
});

test('a refused Add says why, sends its reply to the dialog, and offers no Undo', async () => {
  const { window, document, requests } = startPage();
  const form = document.querySelector('[data-pbd-add-selection-form]');
  const status = document.querySelector('[data-pbd-add-projects-status]');
  const settled = [];
  form.addEventListener('pbd:add-selection-settled', (event) => settled.push(event.detail));

  form.requestSubmit();
  await settle(window);
  requests[0].answer({ message: 'None of the ticked projects can be added: they have been archived, deleted or cancelled.', skippedProjectIds: [1, 2] }, 400);
  await settle(window);

  assert.equal(settled[0].ok, false);
  assert.deepEqual(settled[0].payload.skippedProjectIds, [1, 2]);
  assert.match(status.textContent, /None of the ticked projects can be added/);
  assert.equal(status.querySelector('button'), null);
});
