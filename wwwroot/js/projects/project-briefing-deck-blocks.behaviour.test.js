// Behaviour tests for the Deck tab's slide list (saveBlocks in project-briefing-decks.js): the real
// script runs in jsdom against a small copy of the list markup, with the UpdateBlocks endpoint
// faked. The source-text checks in project-briefing-decks.test.js say the code is there; these
// say what a person sees when the server refuses a change (docs/ux/briefing-deck-review-fixes.md,
// PR 7).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

// The page loads as a module; its one import (the confirm dialog) is not used by the slide list.
const IMPORT_LINE = "import { prismConfirm } from './project-briefing-confirm.js';";
const rawSource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-decks.js'),
  'utf8');
assert.ok(rawSource.includes(IMPORT_LINE), 'the page script still has the import this test replaces');
const pageSource = rawSource.replace(IMPORT_LINE, 'const prismConfirm = async () => true;');

const blockRow = (type, name, { movable = true, optional = true, on = true } = {}) => `
  <li class="pbd-block ${on ? 'is-on' : 'is-off'}" data-pbd-block data-type="${type}" data-name="${name}"
      data-movable="${movable}" data-optional="${optional}">
    <button type="button" data-pbd-block-handle aria-label="Move ${name}">Move</button>
    <span>${name}</span>
    <input type="checkbox" role="switch" ${on ? 'checked' : ''} data-pbd-block-switch />
    <button type="button" data-pbd-block-remove aria-label="Remove ${name} from the deck">Remove</button>
  </li>`;

const MARKUP = `
<div data-pbd-root data-blocks-url="/blocks">
  <input type="hidden" name="__RequestVerificationToken" value="token" />
  <div data-deck-id="7">
    <span data-pbd-saved-chip data-saved-label="Saved"><span data-pbd-saved-chip-text>Saved</span></span>
    <div role="alert" data-pbd-conflict hidden>
      <span data-pbd-conflict-text>Someone else changed this deck.</span>
      <button type="button" data-pbd-conflict-reload>Reload</button>
    </div>
    <input type="hidden" name="RowVersion" value="v1" />
    <div data-pbd-blocks>
      <ul data-pbd-block-zone="Opening">
        ${blockRow('Cover', 'Cover', { movable: false, optional: false })}
      </ul>
      <ul data-pbd-block-zone="Overview">
        ${blockRow('StatusTable', 'Status table')}
        ${blockRow('StageDistribution', 'Stage-wise distribution')}
      </ul>
      <p data-pbd-block-zone-empty hidden>No slides here.</p>
      <button type="button" aria-expanded="false" data-pbd-block-library-toggle>Add block</button>
      <div data-pbd-block-library hidden>
        <section data-pbd-block-library-group hidden>
          <div data-pbd-block-library-entry="StatusTable" hidden><button type="button" data-pbd-block-add="StatusTable">Status table</button></div>
          <div data-pbd-block-library-entry="StageDistribution" hidden><button type="button" data-pbd-block-add="StageDistribution">Stage-wise distribution</button></div>
        </section>
        <p data-pbd-block-library-empty>Every slide is in the deck.</p>
      </div>
    </div>
    <p data-pbd-blocks-status></p>
  </div>
</div>`;

// Each fetch waits until the test answers it.
const startPage = () => {
  const dom = new JSDOM(`<!doctype html><html><body>${MARKUP}</body></html>`, {
    url: 'http://localhost/Workspace/BriefingDecks/7',
    runScripts: 'outside-only',
    pretendToBeVisual: true
  });
  const { window } = dom;
  const requests = [];
  window.fetch = (url, options = {}) => new Promise((resolve) => {
    requests.push({
      url,
      body: options.body ? JSON.parse(options.body) : null,
      answer: (body, status = 200) => resolve({
        ok: status >= 200 && status < 300,
        status,
        headers: { get: (name) => (name.toLowerCase() === 'content-type' ? 'application/json; charset=utf-8' : null) },
        json: async () => body
      })
    });
  });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  if (!window.Element.prototype.scrollIntoView) window.Element.prototype.scrollIntoView = () => {};
  if (!window.CSS) window.CSS = {};
  if (!window.CSS.escape) window.CSS.escape = (value) => String(value).replace(/["\\]/g, '\\$&');
  window.eval(pageSource);
  return { window, document: window.document, requests };
};

const settle = async (window) => {
  for (let i = 0; i < 5; i += 1) await new Promise((resolve) => window.setTimeout(resolve, 0));
};

const row = (document, type) => document.querySelector(`[data-pbd-block][data-type="${type}"]`);
const switchOf = (document, type) => row(document, type).querySelector('[data-pbd-block-switch]');
const order = (document) => [...document.querySelectorAll('[data-pbd-block]')].map((item) => item.dataset.type);

const VIEW_ONLY = "This is a Project Officer's own deck: you can download or duplicate it, but not change it.";

test('a refused switch-off puts the slide back and shows the server message', async () => {
  const { window, document, requests } = startPage();
  const cover = switchOf(document, 'Cover');

  cover.checked = false;
  cover.dispatchEvent(new window.Event('change', { bubbles: true }));
  await settle(window);

  assert.equal(requests.length, 1);
  assert.equal(requests[0].url, '/blocks');
  assert.deepEqual(requests[0].body.blocks.find((block) => block.type === 'Cover'), { type: 'Cover', enabled: false });
  assert.equal(document.querySelector('[data-pbd-blocks]').getAttribute('aria-busy'), 'true');

  requests[0].answer({ message: VIEW_ONLY }, 400);
  await settle(window);

  assert.equal(switchOf(document, 'Cover').checked, true, 'the switch is back on');
  assert.ok(row(document, 'Cover').classList.contains('is-on'));
  const status = document.querySelector('[data-pbd-blocks-status]');
  assert.equal(status.textContent, VIEW_ONLY);
  assert.ok(status.classList.contains('is-error'));
  assert.equal(document.querySelector('[data-pbd-conflict]').hidden, true, 'a refusal is not a conflict');
  assert.equal(document.querySelector('[data-pbd-blocks]').getAttribute('aria-busy'), 'false');
  assert.equal(document.querySelector('[data-pbd-saved-chip-text]').textContent, 'Saved', 'nothing is left unsaved');
});

test('a refused Remove brings the slide back into the list and keeps focus on Add block', async () => {
  const { window, document, requests } = startPage();
  const toggle = document.querySelector('[data-pbd-block-library-toggle]');

  row(document, 'StatusTable').querySelector('[data-pbd-block-remove]').click();
  await settle(window);
  assert.equal(row(document, 'StatusTable').hidden, true, 'removed at once, before the server answers');
  assert.equal(document.activeElement, toggle);

  requests[0].answer({ message: 'Add the status table or the project slides; a deck needs one.' }, 400);
  await settle(window);

  assert.equal(row(document, 'StatusTable').hidden, false, 'back in the list');
  assert.equal(switchOf(document, 'StatusTable').checked, true);
  assert.equal(
    document.querySelector('[data-pbd-block-library-entry="StatusTable"]').hidden,
    true,
    'and no longer offered in Add block');
  assert.equal(document.activeElement, toggle, 'focus stays on a control that is still there');
  assert.equal(
    document.querySelector('[data-pbd-blocks-status]').textContent,
    'Add the status table or the project slides; a deck needs one.');
});

test('a move that meets a conflict (409) puts the order back and shows the conflict banner', async () => {
  const { window, document, requests } = startPage();
  const handle = row(document, 'StatusTable').querySelector('[data-pbd-block-handle]');
  handle.focus();

  handle.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }));
  await settle(window);
  assert.deepEqual(order(document), ['Cover', 'StageDistribution', 'StatusTable']);
  assert.equal(document.activeElement, handle, 'the moved row keeps focus');

  requests[0].answer({ message: 'The deck was updated by another user. Reload before changing its slides.' }, 409);
  await settle(window);

  assert.deepEqual(order(document), ['Cover', 'StatusTable', 'StageDistribution']);
  const banner = document.querySelector('[data-pbd-conflict]');
  assert.equal(banner.hidden, false);
  assert.equal(
    document.querySelector('[data-pbd-conflict-text]').textContent,
    'The deck was updated by another user. Reload before changing its slides. Your last change was not saved.');
  assert.equal(document.querySelector('[data-pbd-saved-chip-text]').textContent, 'Not saved');
});

test('a saved change keeps the new state and takes the returned row version', async () => {
  const { window, document, requests } = startPage();
  const stage = switchOf(document, 'StageDistribution');

  stage.checked = false;
  stage.dispatchEvent(new window.Event('change', { bubbles: true }));
  await settle(window);
  assert.equal(requests[0].body.rowVersion, 'v1');
  requests[0].answer({ saved: true, rowVersion: 'v2', deck: null });
  await settle(window);

  assert.equal(switchOf(document, 'StageDistribution').checked, false);
  assert.equal(row(document, 'StageDistribution').hidden, true, 'an optional slide that is off waits in Add block');
  assert.equal(document.querySelector('[data-pbd-blocks-status]').textContent, 'Saved.');
  assert.equal(document.querySelector('input[name="RowVersion"]').value, 'v2');
});
