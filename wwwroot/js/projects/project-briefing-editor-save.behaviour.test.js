// Behaviour tests for the content editors' in-place save (saveEditorInPlace in
// project-briefing-decks.js): the real script runs in jsdom with the settings and editor endpoints
// faked (docs/ux/briefing-deck-review-3-fixes.md, Low 7). They check what a person sees when an
// editor saves, and that a failed settings save neither sends the editor nor drops the page's
// unsaved-change guard.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const IMPORT_LINE = "import { prismConfirm } from './project-briefing-confirm.js';";
const rawSource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-decks.js'),
  'utf8');
assert.ok(rawSource.includes(IMPORT_LINE), 'the page script still has the import this test replaces');
// Any confirm dialog would be the leave-the-page question, which an editor save must never ask.
const pageSource = rawSource.replace(IMPORT_LINE, 'const prismConfirm = async () => { window.__confirmAsked = true; return true; };');

const MARKUP = `
<div data-pbd-root>
  <input type="hidden" name="__RequestVerificationToken" value="token" />
  <div data-deck-id="7">
    <div role="alert" data-pbd-conflict hidden><span data-pbd-conflict-text></span><button type="button" data-pbd-conflict-reload>Reload</button></div>
    <form action="/settings" data-pbd-settings-form>
      <input type="hidden" name="RowVersion" value="v1" />
      <input type="text" name="Name" value="Monthly review" data-pbd-settings-name />
      <p data-pbd-settings-status></p>
    </form>
    <div data-pbd-blocks><ul data-pbd-block-zone="Closing"></ul></div>
    <p data-pbd-blocks-status></p>
    <form action="/ffc" data-pbd-ffc-footprint-form>
      <input type="text" name="FfcGlobalFootprintTitle" value="FFC footprint" />
    </form>
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
  window.fetch = (url) => new Promise((resolve) => {
    requests.push({
      url,
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

const leaveIsGuarded = (window) => {
  const event = new window.Event('beforeunload', { cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
};

test('an editor save sends the form in place and says it saved in the slide list', async () => {
  const { window, document, requests } = startPage();
  const root = document.querySelector('[data-pbd-root]');
  const form = document.querySelector('[data-pbd-ffc-footprint-form]');

  const saving = root.pbdSaveEditor(form);
  await settle(window);

  assert.equal(requests.length, 1);
  assert.equal(new URL(requests[0].url).pathname, '/ffc');
  assert.ok(form.hasAttribute('inert'), 'the form cannot be edited while it saves');

  requests[0].answer({ saved: true, message: 'FFC footprint saved.', rowVersion: 'v2', deck: null });
  const result = await saving;

  assert.equal(result.ok, true);
  assert.equal(result.message, 'FFC footprint saved.');
  assert.ok(!form.hasAttribute('inert'));
  assert.equal(document.querySelector('[data-pbd-blocks-status]').textContent, 'FFC footprint saved.');
});

test('when the pending settings cannot be saved, the editor is not sent and leaving is still guarded', async () => {
  const { window, document, requests } = startPage();
  const root = document.querySelector('[data-pbd-root]');
  const name = document.querySelector('[data-pbd-settings-name]');
  name.value = 'Monthly review, November';
  name.dispatchEvent(new window.Event('input', { bubbles: true }));
  assert.ok(leaveIsGuarded(window), 'the unsaved name is guarded before the editor save');

  const saving = root.pbdSaveEditor(document.querySelector('[data-pbd-ffc-footprint-form]'));
  await settle(window);
  assert.equal(requests.length, 1);
  assert.equal(new URL(requests[0].url).pathname, '/settings');
  requests[0].answer({ message: 'The database is not available.' }, 500);
  const result = await saving;
  await settle(window);

  assert.equal(result.ok, false);
  assert.match(result.message, /settings could not be saved/);
  assert.equal(requests.length, 1, 'the editor was not sent');
  assert.equal(window.__confirmAsked, undefined, 'no leave-the-page question');
  assert.ok(leaveIsGuarded(window), 'the unsaved name is still guarded');
});
