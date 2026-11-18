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
      <button type="button" data-pbd-filter-list-retry hidden>Try again</button>
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
  const state = { members: members.map((projectId) => ({ projectId })), submits: [], fetches: [], optionsReplies: [], listReplies: [], confirms: [] };
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
    return Promise.resolve(reply({ rows, ...(state.listPayloadExtra || {}) }));
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
      // As the page does: the deck's new members reach the dialog.
      controller.refreshMembership(state.members);
    },
    confirmRemoval: async (options) => { state.confirms.push(options); return true; },
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

test('adding everything from the review gives the filters back', async () => {
  const page = await setUp();
  page.tickRow(2);
  page.$('[data-pbd-filter-ticked]').click();
  assert.equal(page.$('[data-pbd-filter-query]').disabled, true);
  page.$('[data-pbd-filter-add]').click();
  page.settle({ skippedProjectIds: [] });
  assert.equal(page.$('[data-pbd-filter-ticked]').textContent, 'Nothing ticked');
  assert.equal(page.$('[data-pbd-filter-query]').disabled, false);
  assert.equal(page.$('[data-pbd-filter-open="arpp"]').disabled, false);
});

test('when none of the ticked projects can be added, they are unticked and named, not tried again', async () => {
  const page = await setUp();
  page.tickRow(1);
  page.tickRow(2);
  page.$('[data-pbd-filter-add]').click();
  page.status.textContent = 'None of the ticked projects can be added: they have been archived, deleted or cancelled.';
  page.settle({ message: 'refused', skippedProjectIds: [1, 2] }, false);
  // The server's message says why; only the names follow, not the reason a second time.
  assert.equal(page.status.textContent, 'None of the ticked projects can be added: they have been archived, deleted or cancelled. (Alpha, Bravo)');
  assert.equal(page.$('[data-pbd-filter-ticked]').textContent, 'Nothing ticked');
  assert.equal(page.$('[data-pbd-filter-tick="1"]'), null, 'no longer offered');
  assert.equal(page.$('[data-pbd-filter-add]').disabled, false);
});

test('a full deck: nothing can be ticked, Tick all is not offered, and the footer says why', async () => {
  const members = Array.from({ length: 120 }, (_, index) => 1000 + index);
  const page = await setUp({ members });
  assert.match(page.$('[data-pbd-filter-room]').textContent, /This deck is full: it holds 120 projects/);
  assert.equal(page.$('[data-pbd-filter-tick-all]').hidden, true);
  assert.ok([...page.document.querySelectorAll('[data-pbd-filter-tick]')].every((box) => box.disabled));
});

test('ticks that fill the room hold the other ticks back, and Tick all ticks only what fits', async () => {
  const members = Array.from({ length: 118 }, (_, index) => 1000 + index);
  const page = await setUp({ members });
  assert.equal(page.$('[data-pbd-filter-tick-all]').textContent, 'Tick the first 2 that fit');
  page.$('[data-pbd-filter-tick-all]').click();
  assert.equal(page.$('[data-pbd-filter-ticked]').textContent, '2 ticked · Review');
  assert.equal(page.$('[data-pbd-filter-tick="3"]').disabled, true, 'the third does not fit');
  assert.equal(page.$('[data-pbd-filter-tick="1"]').disabled, false, 'a ticked one can be unticked');
  assert.match(page.$('[data-pbd-filter-room]').textContent, /These ticks use the deck's last 2 places/);
  const box = page.$('[data-pbd-filter-tick="1"]');
  box.checked = false;
  box.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  assert.equal(page.$('[data-pbd-filter-tick="3"]').disabled, false, 'room again');
});

test('going over the room, and back, is announced once each through an always-live region', async () => {
  const members = Array.from({ length: 119 }, (_, index) => 1000 + index);
  const page = await setUp({ members });
  const announcer = page.$('[data-pbd-filter-announce]');
  assert.equal(announcer.getAttribute('aria-live'), 'polite');
  assert.equal(page.$('[data-pbd-filter-room]').hasAttribute('aria-live'), false, 'the room line itself is not read on every tick');
  page.tickRow(1);
  assert.equal(announcer.textContent, '');
  page.tickRow(2); // only possible by script: the box is held back
  assert.match(announcer.textContent, /untick 1/);
  announcer.textContent = '';
  page.tickRow(3);
  assert.equal(announcer.textContent, '', 'not again while still over');
  const box = page.$('[data-pbd-filter-tick="3"]');
  box.checked = false;
  box.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  const box2 = page.$('[data-pbd-filter-tick="2"]');
  box2.checked = false;
  box2.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  assert.match(announcer.textContent, /fit in the deck again/);
});

test('a held-back tick is described by the room line, and each tick is named by its project', async () => {
  const members = Array.from({ length: 119 }, (_, index) => 1000 + index);
  const page = await setUp({ members });
  page.tickRow(1);
  const held = page.$('[data-pbd-filter-tick="2"]');
  assert.equal(held.disabled, true);
  assert.match(held.getAttribute('aria-describedby'), /pbd-filter-room/);
  assert.equal(page.document.getElementById(held.getAttribute('aria-labelledby')).textContent, 'Bravo');
});

test('keyboard focus is never dropped: Tick the first N, the last untick in Review, a chip opened while loading', async () => {
  const members = Array.from({ length: 118 }, (_, index) => 1000 + index);
  let page = await setUp({ members });
  page.$('[data-pbd-filter-tick-all]').focus();
  page.$('[data-pbd-filter-tick-all]').click();
  assert.equal(page.$('[data-pbd-filter-tick-all]').hidden, true);
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-ticked]'));

  page = await setUp();
  page.tickRow(2);
  page.$('[data-pbd-filter-ticked]').click();
  const box = page.$('[data-pbd-filter-tick="2"]');
  box.checked = false;
  box.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-query]'));

  page = await setUp({ holdOptions: true });
  page.$('[data-pbd-filter-open="arpp"]').focus();
  page.$('[data-pbd-filter-open="arpp"]').click();
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-open="arpp"]'), 'the chip keeps focus while loading');
  page.state.optionsReplies.shift().resolve(reply({ arppYears: [{ value: 2026, label: 'FY 2026-27', count: 3 }] }));
  await tick();
  await tick();
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-panel] input'), 'then moves into the choices');
});

test('the choices are fetched again on an opening once they are two minutes old, and older completion years can be shown', async () => {
  const years = Array.from({ length: 15 }, (_, index) => ({ value: 2026 - index, label: `FY ${2026 - index}` }));
  const page = await setUp({ options: { completionFinancialYears: years } });
  const optionLoads = () => page.state.fetches.filter((href) => href.includes('FilterOptions')).length;
  const before = optionLoads();
  page.controller.open();
  await new Promise((resolve) => setTimeout(resolve, 5));
  assert.equal(optionLoads(), before, 'still fresh: not read again');
  const realNow = Date.now;
  Date.now = () => realNow() + 121000;
  try {
    page.controller.open();
    await new Promise((resolve) => setTimeout(resolve, 5));
  } finally {
    Date.now = realNow;
  }
  assert.equal(optionLoads(), before + 1);
  page.$('[data-pbd-filter-open="completed"]').click();
  assert.equal(page.$('[data-pbd-filter-range="completedFrom"]').options.length, 12);
  assert.match(page.$('[data-pbd-filter-panel]').textContent, /A completion year with no date counts from April of that year/);
  page.$('[data-pbd-filter-all-years]').click();
  assert.equal(page.$('[data-pbd-filter-range="completedFrom"]').options.length, 16);
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-range="completedFrom"]'));
});

test('some added, some not: the note comes before Undo', async () => {
  const page = await setUp();
  page.tickRow(1);
  page.tickRow(3);
  page.$('[data-pbd-filter-add]').click();
  page.status.textContent = '1 project added.';
  const undo = page.document.createElement('button');
  undo.textContent = 'Undo';
  page.status.append(' ', undo);
  page.settle({ skippedProjectIds: [3] });
  assert.match(page.status.textContent, /1 project added\.\s+One project was not added: Charlie \(archived, deleted or cancelled\)\. Undo$/);
});

test('a list that fails to load can be tried again', async () => {
  const page = await setUp({ holdList: true });
  page.state.listReplies.shift().resolve(reply({ message: 'The server is busy.' }, 500));
  await tick();
  await tick();
  assert.equal(page.$('[data-pbd-filter-count]').textContent, 'The server is busy.');
  assert.equal(page.$('[data-pbd-filter-list-retry]').hidden, false);
  page.$('[data-pbd-filter-list-retry]').click();
  page.state.listReplies.shift().resolve(reply({ rows: [row(1, 'Alpha')] }));
  await tick();
  await tick();
  assert.equal(page.$('[data-pbd-filter-count]').textContent, '1 project matches');
  assert.equal(page.$('[data-pbd-filter-list-retry]').hidden, true);
});

test('the list is loaded again each time the dialog opens, and the ticks stay', async () => {
  const page = await setUp();
  page.tickRow(1);
  const lists = () => page.state.fetches.filter((href) => href.includes('FilterProjects')).length;
  const before = lists();
  page.controller.open();
  await new Promise((resolve) => setTimeout(resolve, 5));
  assert.equal(lists(), before + 1);
  assert.equal(page.$('[data-pbd-filter-tick="1"]').checked, true);
});

test('Remove asks with the project name, then the row can be ticked again with focus on it', async () => {
  const page = await setUp({ members: [2] });
  page.$('[data-pbd-filter-remove="2"]').click();
  await tick();
  await tick();
  assert.equal(page.state.confirms[0].title, 'Remove Bravo?');
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-tick="2"]'));
  assert.match(page.status.textContent, /Removed Bravo\./);
});

test('the proliferation chip and Clear filters change what the list asks for', async () => {
  const page = await setUp();
  page.$('[data-pbd-filter-open="proliferation"]').click();
  await new Promise((resolve) => setTimeout(resolve, 5));
  assert.match(page.state.fetches.at(-1), /proliferation=true/);
  assert.equal(page.$('[data-pbd-filter-open="proliferation"]').getAttribute('aria-pressed'), 'true');
  page.$('[data-pbd-filter-reset]').click();
  await new Promise((resolve) => setTimeout(resolve, 5));
  assert.doesNotMatch(page.state.fetches.at(-1), /proliferation/);
  assert.equal(page.$('[data-pbd-filter-reset]'), null);
});

test('"Ongoing" with "Completed between" says why nothing matches', async () => {
  const page = await setUp({ rows: [], options: { completionFinancialYears: [{ value: 2025, label: 'FY 2025-26' }] } });
  page.$('[data-pbd-filter-open="status"]').click();
  const ongoing = page.$('[data-pbd-filter-value="status"][value="Active"]');
  ongoing.checked = true;
  ongoing.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  page.$('[data-pbd-filter-open="completed"]').click();
  const from = page.$('[data-pbd-filter-range="completedFrom"]');
  from.value = '2025';
  from.dispatchEvent(new page.window.Event('change', { bubbles: true }));
  await new Promise((resolve) => setTimeout(resolve, 150));
  assert.match(page.$('[data-pbd-filter-count]').textContent, /ongoing projects have no completion year/);
});

test('a long list draws the first 200, with Show all; the count and Tick all cover every match', async () => {
  const rows = Array.from({ length: 230 }, (_, index) => row(index + 1, `Project ${String(index + 1).padStart(3, '0')}`));
  const page = await setUp({ rows });
  assert.equal(page.document.querySelectorAll('.pbd-filter__row').length, 200);
  assert.equal(page.$('[data-pbd-filter-count]').textContent, '230 projects match');
  assert.equal(page.$('[data-pbd-filter-show-all]').textContent, 'Show all 230 projects');
  page.$('[data-pbd-filter-show-all]').click();
  assert.equal(page.document.querySelectorAll('.pbd-filter__row').length, 230);
  assert.equal(page.$('[data-pbd-filter-show-all]'), null);
  assert.equal(page.document.activeElement, page.$('[data-pbd-filter-tick="201"]'), 'the keyboard carries on at the first new row');
});

test('choice counts show only when no other filter or search is set', async () => {
  const page = await setUp({ options: { arppYears: [{ value: 2026, label: 'FY 2026-27', count: 3 }] } });
  page.$('[data-pbd-filter-open="arpp"]').click();
  assert.match(page.$('[data-pbd-filter-panel]').textContent, /FY 2026-27 · 3/);
  page.$('[data-pbd-filter-query]').value = 'alpha';
  page.$('[data-pbd-filter-query]').dispatchEvent(new page.window.Event('input', { bubbles: true }));
  assert.doesNotMatch(page.$('[data-pbd-filter-panel]').textContent, /· 3/);
  assert.match(page.$('[data-pbd-filter-panel]').textContent, /Counts show when no other filter or search is set/);
});

test('Escape in the search closes an open filter, not the dialog', async () => {
  const page = await setUp();
  page.$('[data-pbd-filter-open="arpp"]').click();
  const event = new page.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
  page.$('[data-pbd-filter-query]').dispatchEvent(event);
  assert.equal(page.$('[data-pbd-filter-panel]').hidden, true);
  assert.equal(event.defaultPrevented, true);
});

test('Undo ticks the projects again from any earlier Add, and reports how many', async () => {
  const page = await setUp();
  page.tickRow(1);
  page.$('[data-pbd-filter-add]').click();
  page.settle({ skippedProjectIds: [] });
  // A later Add is refused: the first Add's projects can still be ticked again by its Undo.
  page.tickRow(2);
  page.$('[data-pbd-filter-add]').click();
  page.settle({ message: 'refused' }, false);
  page.state.members = [];
  page.controller.refreshMembership(page.state.members);
  const undone = new page.window.CustomEvent('pbd:add-undone', { detail: { projectIds: [1], reticked: 0 } });
  page.document.dispatchEvent(undone);
  assert.equal(undone.detail.reticked, 1);
  assert.equal(page.$('[data-pbd-filter-tick="1"]').checked, true);
});

test('the room follows the deck size the server reports, until the page has a newer deck', async () => {
  const page = await setUp({ members: [] });
  assert.match(page.$('[data-pbd-filter-room]').textContent, /Room for 120 more/);
  page.state.listPayloadExtra = { deckProjectCount: 120 };
  page.controller.open();
  await new Promise((resolve) => setTimeout(resolve, 5));
  assert.match(page.$('[data-pbd-filter-room]').textContent, /This deck is full/);
  page.controller.refreshMembership([]);
  assert.match(page.$('[data-pbd-filter-room]').textContent, /Room for 120 more/);
});
