const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { JSDOM } = require('jsdom');

// Add projects (docs/ux/briefing-add-projects.md) tested by what it does: a fake server answers
// the list and the choices, and the page's shared parts (the AddSelection form's handler, the
// deck's members) are played by the test (cold review 2: the earlier tests only matched text).

let modulePromise;
function loadModule() {
  if (!modulePromise) {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), `prism-add-projects-${process.pid}-`));
    const target = path.join(dir, 'project-briefing-add-projects.mjs');
    fs.copyFileSync(path.join(__dirname, 'project-briefing-add-projects.js'), target);
    modulePromise = import(pathToFileURL(target).href);
  }
  return modulePromise;
}

const MARKUP = `
<div data-pbd-root data-filter-url="/decks?handler=FilterProjects" data-filter-options-url="/decks?handler=FilterOptions" data-max-projects="120">
  <div class="modal" id="pbd-add-projects-modal">
    <div data-pbd-filter>
      <input type="search" data-pbd-filter-query />
      <div data-pbd-filter-chips></div>
      <div data-pbd-filter-panel hidden></div>
      <span data-pbd-filter-count></span>
      <button type="button" data-pbd-filter-tick-all hidden></button>
      <div data-pbd-filter-results></div>
      <form data-pbd-filter-add-form><input type="hidden" name="Kind" value="IndividualProjects" /></form>
    </div>
    <p data-pbd-add-projects-status></p>
    <button type="button" data-pbd-filter-ticked disabled>Nothing ticked</button>
    <span data-pbd-filter-room></span>
    <button type="button" data-pbd-filter-clear hidden></button>
    <button type="button" data-pbd-filter-add>Add projects</button>
  </div>
</div>`;

const row = (projectId, projectName, extra = {}) => ({ projectId, projectName, lifecycle: 'Ongoing', presentStage: 'Development', isSelected: false, ...extra });
const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((ok, fail) => { resolve = ok; reject = fail; });
  return { promise, resolve, reject };
};
const reply = (body, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

async function setUp({ members = [], rows = [row(1, 'Alpha'), row(2, 'Bravo'), row(3, 'Charlie')], options = { arppYears: [{ value: 2026, label: 'FY 2026-27', count: 3 }] }, holdOptions = false, holdList = false } = {}) {
  const { initAddProjects } = await loadModule();
  const dom = new JSDOM(`<!doctype html><html><body>${MARKUP}</body></html>`, { url: 'http://localhost/' });
  const { window } = dom;
  const { document } = window;
  const state = { members: members.map((projectId) => ({ projectId })), submits: [], fetches: [], optionsReplies: [], listReplies: [] };
  window.fetch = (url) => {
    const href = String(url);
    state.fetches.push(href);
    if (href.includes('FilterOptions')) {
      if (holdOptions) {
        const wait = deferred();
        state.optionsReplies.push(wait);
        return wait.promise;
      }
      return Promise.resolve(reply(options));
    }
    if (holdList) {
      const wait = deferred();
      state.listReplies.push(wait);
      return wait.promise;
    }
    return Promise.resolve(reply({ rows }));
  };
  const root = document.querySelector('[data-pbd-root]');
  const form = document.querySelector('[data-pbd-filter-add-form]');
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    state.submits.push([...form.querySelectorAll('input[name="ProjectIds"]')].map((input) => Number(input.value)));
  });
  const status = document.querySelector('[data-pbd-add-projects-status]');
  const controller = initAddProjects({
    root,
    deckId: 7,
    checkReply: () => {},
    setState: (element, message, kind) => { element.textContent = message; element.dataset.state = kind || ''; },
    statusElement: status,
    updateMembership: async (add, remove) => {
      state.members = state.members.filter((member) => !remove.includes(member.projectId)).concat(add.map((projectId) => ({ projectId })));
    },
    confirmRemoval: async () => true,
    modalElement: document.getElementById('pbd-add-projects-modal'),
    getMembers: () => state.members
  });
  const $ = (selector) => document.querySelector(selector);
  const tickRow = (projectId) => {
    const box = $(`[data-pbd-filter-tick="${projectId}"]`);
    box.checked = true;
    box.dispatchEvent(new window.Event('change', { bubbles: true }));
  };
  // The page's AddSelection handler: the deck gains the projects, then it says it has settled.
  const settle = (payload, ok = true) => {
    if (ok) {
      const added = state.submits.at(-1).filter((projectId) => !(payload?.skippedProjectIds || []).includes(projectId));
      state.members = state.members.concat(added.map((projectId) => ({ projectId })));
      controller.refreshMembership(state.members);
    }
    form.dispatchEvent(new window.CustomEvent('pbd:add-selection-settled', { bubbles: true, detail: { ok, payload } }));
  };
  controller.open();
  await new Promise((resolve) => setTimeout(resolve, 5));
  return { window, document, $, state, controller, tickRow, settle, status };
}

test('one Add at a time: a second click before the first settles sends nothing', async () => {
  const page = await setUp();
  page.tickRow(1);
  page.tickRow(2);
  page.$('[data-pbd-filter-add]').click();
  page.$('[data-pbd-filter-add]').click();
  assert.deepEqual(page.state.submits, [[1, 2]]);
  assert.equal(page.$('[data-pbd-filter-add]').disabled, true);
  page.settle({ skippedProjectIds: [] });
  assert.equal(page.$('[data-pbd-filter-add]').disabled, false);
  assert.equal(page.$('[data-pbd-filter-ticked]').textContent, 'Nothing ticked');
});

test('the deck limit: ticks beyond the room are refused before sending, with how many to untick', async () => {
  const members = Array.from({ length: 118 }, (_, index) => 1000 + index);
  const page = await setUp({ members });
  [1, 2, 3].forEach(page.tickRow);
  assert.match(page.$('[data-pbd-filter-room]').textContent, /room for 2 more: untick 1/);
  assert.ok(page.$('[data-pbd-filter-room]').classList.contains('is-over'));
  page.$('[data-pbd-filter-add]').click();
  assert.equal(page.state.submits.length, 0);
  assert.match(page.status.textContent, /Not added: more projects are ticked than this deck has room for\./);
});

test('a list reply that arrives after an Add does not offer the added projects again', async () => {
  const page = await setUp({ holdList: true });
  page.state.listReplies.shift().resolve(reply({ rows: [row(1, 'Alpha'), row(2, 'Bravo')] }));
  await tick();
  // A filter changes (a new list request goes out), then project 1 is added before it answers.
  page.$('[data-pbd-filter-query]').value = 'a';
  page.$('[data-pbd-filter-query]').dispatchEvent(new page.window.Event('input', { bubbles: true }));
  await new Promise((resolve) => setTimeout(resolve, 300));
  page.state.members = [{ projectId: 1 }];
  page.controller.refreshMembership(page.state.members);
  // The server answered from before the add: it still says project 1 is not in the deck.
  page.state.listReplies.shift().resolve(reply({ rows: [row(1, 'Alpha', { isSelected: false }), row(2, 'Bravo')] }));
  await tick();
  assert.equal(page.$('[data-pbd-filter-tick="1"]'), null, 'project 1 is not offered again');
  assert.ok(page.$('[data-pbd-filter-remove="1"]'), 'it reads In deck, with Remove');
});

test('projects the server could not add are unticked and named', async () => {
  const page = await setUp();
  page.tickRow(1);
  page.tickRow(3);
  page.$('[data-pbd-filter-add]').click();
  page.status.textContent = '1 project added.';
  page.settle({ skippedProjectIds: [3] });
  assert.match(page.status.textContent, /One project was not added: Charlie \(archived, deleted or cancelled\)\./);
  assert.equal(page.$('[data-pbd-filter-ticked]').textContent, 'Nothing ticked');
});

test('Undo after an Add ticks those projects again', async () => {
  const page = await setUp();
  page.tickRow(1);
  page.tickRow(2);
  page.$('[data-pbd-filter-add]').click();
  page.settle({ skippedProjectIds: [] });
  // The page's Undo removes them and says so.
  page.state.members = [];
  page.controller.refreshMembership(page.state.members);
  page.document.dispatchEvent(new page.window.CustomEvent('pbd:add-undone', { detail: { projectIds: [1, 2] } }));
  assert.equal(page.$('[data-pbd-filter-ticked]').textContent, '2 ticked · Review');
  assert.equal(page.$('[data-pbd-filter-tick="1"]').checked, true);
});

test('the choices say they are loading, and a failed load can be tried again', async () => {
  const page = await setUp({ holdOptions: true });
  page.$('[data-pbd-filter-open="arpp"]').click();
  assert.match(page.$('[data-pbd-filter-panel]').textContent, /Loading the choices/);
  page.state.optionsReplies.shift().resolve(reply({}, 500));
  await tick();
  await tick();
  assert.match(page.$('[data-pbd-filter-panel]').textContent, /could not be loaded/);
  page.$('[data-pbd-filter-retry]').click();
  assert.equal(page.state.fetches.filter((href) => href.includes('FilterOptions')).length, 2);
  page.state.optionsReplies.shift().resolve(reply({ arppYears: [{ value: 2026, label: 'FY 2026-27', count: 3 }] }));
  await tick();
  await tick();
  assert.match(page.$('[data-pbd-filter-panel]').textContent, /FY 2026-27 · 3/);
});

test('reviewing the ticks: the filters wait, and the review ends when nothing is ticked', async () => {
  const page = await setUp();
  page.tickRow(2);
  page.$('[data-pbd-filter-ticked]').click();
  assert.equal(page.$('[data-pbd-filter-ticked]').textContent, '1 ticked · Back to the list');
  assert.equal(page.$('[data-pbd-filter-open="arpp"]').disabled, true);
  assert.equal(page.$('[data-pbd-filter-query]').disabled, true);
  const box = page.$('[data-pbd-filter-tick="2"]');
  box.checked = false;
  box.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  assert.equal(page.document.querySelectorAll('.pbd-filter__row').length, 3, 'back to the full list');
  assert.equal(page.$('[data-pbd-filter-open="arpp"]').disabled, false);
});

test('a completed range chosen the wrong way round is put the right way round', async () => {
  const page = await setUp({ options: { completionFinancialYears: [2026, 2025, 2024].map((value) => ({ value, label: `FY ${value}` })) } });
  page.$('[data-pbd-filter-open="completed"]').click();
  const from = page.$('[data-pbd-filter-range="completedFrom"]');
  const to = page.$('[data-pbd-filter-range="completedTo"]');
  to.value = '2024';
  to.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  from.value = '2026';
  from.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  assert.equal(page.$('[data-pbd-filter-range="completedFrom"]').value, '2024');
  assert.equal(page.$('[data-pbd-filter-range="completedTo"]').value, '2026');
  assert.match(page.$('[data-pbd-filter-open="completed"]').textContent, /Completed FY 2024 to FY 2026/);
});

test('Escape on an open filter closes it and returns to its chip', async () => {
  const page = await setUp();
  page.$('[data-pbd-filter-open="arpp"]').click();
  const event = new page.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
  page.$('[data-pbd-filter-panel] input').dispatchEvent(event);
  assert.equal(page.$('[data-pbd-filter-panel]').hidden, true);
  assert.equal(event.defaultPrevented, true);
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-open="arpp"]'));
});

test('server values are written as text, never as markup', async () => {
  const page = await setUp({ rows: [row(9, '<img src=x onerror=alert(1)>')] });
  assert.equal(page.document.querySelector('img'), null);
  assert.match(page.$('[data-pbd-filter-results]').textContent, /<img src=x/);
});
