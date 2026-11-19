// Behaviour tests for the briefing deck library: search, Mine/All and sorting run in jsdom
// against a copy of the library table markup.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const librarySource = fs.readFileSync(
  path.resolve(__dirname, '..', 'pages', 'project-briefing-library.js'),
  'utf8');

const row = (name, search, mine, edited, generated) => `
  <tr data-pbd-library-row data-search="${search}" data-mine="${mine}" data-name="${name}" data-edited="${edited}" data-generated="${generated}">
    <td>${name}</td>
  </tr>`;

const startLibrary = () => {
  const dom = new JSDOM(`<!doctype html><body>
    <section data-pbd-library>
      <span data-pbd-library-count>3</span>
      <button type="button" data-pbd-library-scope="mine" aria-pressed="false">Mine</button>
      <button type="button" data-pbd-library-scope="all" aria-pressed="true" class="is-active">All</button>
      <input type="search" data-pbd-library-search>
      <select data-pbd-library-sort>
        <option value="edited">Last edited</option>
        <option value="generated">Last generated</option>
        <option value="name">Name</option>
      </select>
      <table><tbody data-pbd-library-rows>
        ${row('Monthly review', 'monthly review vr based map reading', 'true', 300, 100)}
        ${row('Visit of DG EME', 'visit of dg eme counter-uas system', 'false', 200, 250)}
        ${row('ARPP review', 'arpp review tunguska wpn', 'true', 100, 0)}
      </tbody></table>
      <p data-pbd-library-empty hidden>No decks match.</p>
    </section></body>`, { runScripts: 'outside-only' });
  dom.window.eval(librarySource);
  return dom.window;
};

const visibleNames = (document) => [...document.querySelectorAll('[data-pbd-library-row]')]
  .filter((node) => !node.hidden)
  .map((node) => node.dataset.name);

const type = (window, value) => {
  const input = window.document.querySelector('[data-pbd-library-search]');
  input.value = value;
  input.dispatchEvent(new window.Event('input'));
};

test('search finds a deck by the name of a project in it', () => {
  const window = startLibrary();

  type(window, 'counter-uas');

  assert.deepEqual(visibleNames(window.document), ['Visit of DG EME']);
  assert.equal(window.document.querySelector('[data-pbd-library-count]').textContent, '1');
});

test('Mine keeps the decks the person owns or last edited', () => {
  const window = startLibrary();
  const mine = window.document.querySelector('[data-pbd-library-scope="mine"]');

  mine.click();

  assert.deepEqual(visibleNames(window.document), ['Monthly review', 'ARPP review']);
  assert.equal(mine.getAttribute('aria-pressed'), 'true');
  assert.equal(window.document.querySelector('[data-pbd-library-scope="all"]').getAttribute('aria-pressed'), 'false');
});

test('no match shows the empty message', () => {
  const window = startLibrary();

  type(window, 'nothing like this');

  assert.deepEqual(visibleNames(window.document), []);
  assert.equal(window.document.querySelector('[data-pbd-library-empty]').hidden, false);
});

test('sorting reorders the rows by name or by last generated', () => {
  const window = startLibrary();
  const sort = window.document.querySelector('[data-pbd-library-sort]');

  sort.value = 'name';
  sort.dispatchEvent(new window.Event('change'));
  assert.deepEqual(visibleNames(window.document), ['ARPP review', 'Monthly review', 'Visit of DG EME']);

  sort.value = 'generated';
  sort.dispatchEvent(new window.Event('change'));
  assert.deepEqual(visibleNames(window.document), ['Visit of DG EME', 'Monthly review', 'ARPP review']);
});

test('delete waits for confirmation and goes ahead only when confirmed', async () => {
  const window = startLibrary();
  const document = window.document;
  document.querySelector('[data-pbd-library]').insertAdjacentHTML('beforeend', `
    <form method="post" data-pbd-library-delete data-deck-name="ARPP review" data-confirm="Delete “ARPP review”?" data-confirm-detail="It moves to Recently deleted.">
      <button type="submit">Delete</button>
    </form>`);
  const form = document.querySelector('[data-pbd-library-delete]');
  const sent = [];
  // Stands in for the browser sending the form: runs after the library's own listener.
  window.addEventListener('submit', (event) => {
    if (!event.defaultPrevented) sent.push(form.dataset.pbdConfirmed);
    event.preventDefault();
  });
  const settle = () => new Promise((resolve) => window.setTimeout(resolve, 20));

  window.confirm = () => false;
  form.requestSubmit();
  await settle();
  assert.deepEqual(sent, []);

  window.confirm = () => true;
  form.requestSubmit();
  await settle();
  assert.deepEqual(sent, ['true']);
});

// ---- Download from the library (Plan: The Studio is optional, PR 2) -------------------------

const fakeReply = ({ status = 200, type = 'application/json', body = null, headers = {}, redirected = false, url = '' } = {}) => ({
  ok: status >= 200 && status < 300,
  status,
  redirected,
  url,
  headers: { get: (name) => ({ 'content-type': type, ...headers })[name.toLowerCase()] ?? null },
  json: async () => body,
  blob: async () => new Blob(['pptx'])
});

const PPTX = 'application/vnd.openxmlformats-officedocument.presentationml.presentation';

const startDownloadCard = ({ gaps, file, generate = true }) => {
  const window = startLibrary();
  const document = window.document;
  document.querySelector('[data-pbd-library]').insertAdjacentHTML('beforeend', `
    <article data-pbd-library-row data-name="Monthly" data-search="monthly" data-mine="true" data-edited="1" data-generated="0">
      <p data-pbd-library-status><span>Not generated</span></p>
      <form method="post" action="/Workspace/BriefingDecks?handler=${generate ? 'Generate' : 'Download'}"
            data-pbd-library-download ${generate ? 'data-pbd-library-generate' : ''} data-gaps-url="/Workspace/BriefingDecks?handler=Gaps&amp;deckId=7">
        <input type="hidden" name="deckId" value="7">
        <button type="submit">Download</button>
      </form>
    </article>`);
  const calls = [];
  window.fetch = async (url, options = {}) => {
    calls.push({ url: String(url), method: options.method || 'GET' });
    return /handler=Gaps/.test(String(url)) ? gaps() : file();
  };
  const toasts = [];
  window.PrismToast = { show: (options) => { toasts.push(options); return { dismiss() {} }; } };
  window.URL.createObjectURL = () => 'blob:pptx';
  window.URL.revokeObjectURL = () => {};
  const asked = [];
  window.confirm = (text) => { asked.push(text); return window.answer; };
  const submit = async () => {
    document.querySelector('[data-pbd-library-download]').requestSubmit();
    await new Promise((resolve) => window.setTimeout(resolve, 30));
  };
  return { window, document, calls, toasts, asked, submit };
};

test('a deck without gaps downloads at once, and its card says it is up to date', async () => {
  const page = startDownloadCard({
    gaps: () => fakeReply({ body: { projects: 6, projectsWithGaps: 0, kinds: [] } }),
    file: () => fakeReply({ type: PPTX, headers: { 'x-project-briefing-slides': '11', 'content-disposition': 'attachment; filename=Monthly_20261119.pptx' } })
  });

  await page.submit();

  assert.deepEqual(page.asked, [], 'nothing to ask');
  assert.deepEqual(page.calls.map((call) => call.method), ['GET', 'POST']);
  assert.match(page.document.querySelector('[data-pbd-library-status]').textContent, /^Today, \d\d:\d\d · 11 slides · up to date$/);
  assert.notEqual(page.document.querySelector('[data-pbd-library-row][data-name="Monthly"]').dataset.generated, '0');
  assert.equal(page.toasts.at(-1).text, 'PowerPoint downloaded (11 slides).');
});

test('a deck with gaps asks first; Cancel downloads nothing, Download anyway does', async () => {
  const page = startDownloadCard({
    gaps: () => fakeReply({ body: { projects: 6, projectsWithGaps: 4, kinds: ['Status remark', 'Cost (R&D)'] } }),
    file: () => fakeReply({ type: PPTX, headers: { 'x-project-briefing-slides': '11' } })
  });

  page.window.answer = false;
  await page.submit();
  assert.equal(page.asked.length, 1);
  assert.match(page.asked[0], /Download with gaps\?/);
  assert.match(page.asked[0], /4 of 6 projects have gaps: status remark, cost \(R&D\)\. /);
  assert.match(page.asked[0], /Open the deck to fill them first/);
  assert.deepEqual(page.calls.map((call) => call.method), ['GET'], 'declined: no PowerPoint built');
  assert.equal(page.document.querySelector('[data-pbd-library-status]').textContent.trim(), 'Not generated');

  page.window.answer = true;
  await page.submit();
  assert.deepEqual(page.calls.map((call) => call.method), ['GET', 'GET', 'POST']);
  assert.match(page.document.querySelector('[data-pbd-library-status]').textContent, /up to date/);
});

test('a view-only deck asks too, without offering to fix, and is not marked generated', async () => {
  const page = startDownloadCard({
    generate: false,
    gaps: () => fakeReply({ body: { projects: 2, projectsWithGaps: 1, kinds: ['Photograph'] } }),
    file: () => fakeReply({ type: PPTX })
  });
  page.window.answer = true;

  await page.submit();

  assert.doesNotMatch(page.asked[0], /Open the deck/);
  assert.deepEqual(page.calls.map((call) => call.method), ['GET', 'POST']);
  assert.equal(page.document.querySelector('[data-pbd-library-status]').textContent.trim(), 'Not generated');
});

test('an expired sign-in while checking stops before building and says so', async () => {
  const page = startDownloadCard({
    gaps: () => fakeReply({ type: 'text/html', redirected: true, url: '/Identity/Account/Login' }),
    file: () => { throw new Error('not reached'); }
  });

  await page.submit();

  assert.deepEqual(page.calls.map((call) => call.method), ['GET']);
  assert.equal(page.toasts.at(-1).tone, 'danger');
  assert.match(page.toasts.at(-1).text, /sign-in has expired/);
});

test('a refusal from the server is shown, and the button can be used again', async () => {
  const page = startDownloadCard({
    gaps: () => fakeReply({ body: { projects: 1, projectsWithGaps: 0, kinds: [] } }),
    file: () => fakeReply({ status: 400, body: { message: 'Add at least one project before generating.' } })
  });

  await page.submit();

  assert.equal(page.toasts.at(-1).text, 'Add at least one project before generating.');
  const button = page.document.querySelector('[data-pbd-library-download] button');
  assert.equal(button.dataset.pbdDownloading, undefined);
  assert.equal(button.getAttribute('aria-disabled'), null);
});

// ---- What a Download ended in, for Create and download (cold review, 19 Nov 2026) ---------------

const settledOutcome = async (page) => {
  let detail = null;
  page.document.addEventListener('pbd:download-settled', (event) => { detail = event.detail; });
  await page.submit();
  return detail;
};

test('a Download reports how it ended: downloaded with its slides, declined, or failed with the message', async () => {
  const downloaded = startDownloadCard({
    gaps: () => fakeReply({ body: { projects: 1, projectsWithGaps: 0, kinds: [] } }),
    file: () => fakeReply({ type: PPTX, headers: { 'x-project-briefing-slides': '9' } })
  });
  assert.deepEqual({ ...(await settledOutcome(downloaded)) }, { outcome: 'downloaded', message: '', slides: 9 });

  const declined = startDownloadCard({
    gaps: () => fakeReply({ body: { projects: 2, projectsWithGaps: 1, kinds: ['Photograph'] } }),
    file: () => { throw new Error('not reached'); }
  });
  declined.window.answer = false;
  assert.equal((await settledOutcome(declined)).outcome, 'declined');

  const failed = startDownloadCard({
    gaps: () => fakeReply({ body: { projects: 1, projectsWithGaps: 0, kinds: [] } }),
    file: () => fakeReply({ status: 422, body: { message: 'PowerPoint integrity check failed.' } })
  });
  const detail = await settledOutcome(failed);
  assert.equal(detail.outcome, 'failed');
  assert.equal(detail.message, 'PowerPoint integrity check failed.');
});

test('a message carried across the reload is shown once on the library, then forgotten', async () => {
  const dom = new JSDOM('<!doctype html><body><section data-pbd-library><div data-pbd-library-rows></div></section></body>', { runScripts: 'outside-only', url: 'http://localhost/Workspace/BriefingDecks' });
  const shown = [];
  dom.window.PrismToast = { show: (options) => { shown.push(options); return { dismiss() {} }; } };
  dom.window.sessionStorage.setItem('prism.briefingDecks.flash', JSON.stringify({ tone: 'danger', text: 'Deck created, but the PowerPoint could not be made.' }));

  dom.window.eval(librarySource);
  await new Promise((resolve) => setTimeout(resolve, 20)); // shown once the page has loaded

  assert.equal(shown.length, 1);
  assert.equal(shown[0].tone, 'danger');
  assert.equal(shown[0].text, 'Deck created, but the PowerPoint could not be made.');
  assert.equal(shown[0].sticky, true, 'an error stays until dismissed');
  assert.equal(dom.window.sessionStorage.getItem('prism.briefingDecks.flash'), null);
});
