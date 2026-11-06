const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'declarative-actions.js'), 'utf8');

function createDom(body, { confirmResult = true, prism = false } = {}) {
  const calls = { confirm: [], prism: [], print: 0, close: 0, submits: 0, navigations: 0, submitters: [] };
  // jsdom does not implement navigation; it reports each attempt here instead.
  const virtualConsole = new VirtualConsole();
  virtualConsole.on('jsdomError', (error) => {
    if (/navigation/i.test(error.message)) calls.navigations += 1;
  });
  const dom = new JSDOM(`<!doctype html><html><body>${body}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/Photos/People/Review',
    virtualConsole,
  });
  const { window } = dom;

  window.confirm = (message) => {
    calls.confirm.push(message);
    return confirmResult;
  };
  if (prism) {
    // Stands in for the shared dialog (wwwroot/js/components/prism-confirm-dialog.js).
    window.PrismConfirm = {
      show(options) {
        calls.prism.push(options);
        return Promise.resolve(confirmResult);
      }
    };
  }
  window.print = () => { calls.print += 1; };
  window.close = () => { calls.close += 1; };
  window.eval(script);
  // jsdom's native requestSubmit runs validation and fires submit (it does not navigate);
  // count the submissions that reach the document without being cancelled.
  window.document.addEventListener('submit', (event) => {
    if (!event.defaultPrevented) {
      calls.submits += 1;
      calls.submitters.push(event.submitter ? event.submitter.id : null);
    }
  });
  return { window, document: window.document, calls };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

test('data-confirm holds the submit, asks in the PRISM dialog and replays it once when accepted', async () => {
  const { document, calls } = createDom('<form data-confirm="Merge this person?"><button id="go" type="submit">Merge</button></form>', { prism: true });
  const form = document.querySelector('form');
  let formListenerRuns = 0;
  form.addEventListener('submit', () => { formListenerRuns += 1; });

  form.requestSubmit(document.getElementById('go'));
  assert.equal(calls.submits, 0, 'held while the dialog is open');
  assert.equal(formListenerRuns, 0);

  await settle();
  assert.equal(calls.prism.length, 1);
  assert.equal(calls.prism[0].title, 'Merge this person?');
  assert.equal(calls.submits, 1);
  assert.deepEqual(calls.submitters, ['go'], 'the replay keeps the button that was pressed');
  assert.equal(formListenerRuns, 1);
  assert.deepEqual(calls.confirm, [], 'the browser box is not used');
});

test('declining data-confirm cancels the submit before form listeners run', async () => {
  const { document, calls } = createDom('<form data-confirm="Disconnect this folder?"><button type="submit">Go</button></form>', { confirmResult: false, prism: true });
  const form = document.querySelector('form');
  let formListenerRan = false;
  form.addEventListener('submit', () => { formListenerRan = true; });

  form.requestSubmit();
  await settle();

  assert.equal(calls.prism.length, 1);
  assert.equal(calls.submits, 0);
  assert.equal(formListenerRan, false);
});

test('data-confirm on the submitter takes precedence over the form', async () => {
  const { document, calls } = createDom(`<form data-confirm="Form message">
      <button id="force" type="submit" data-confirm="Force retry?">Force</button></form>`, { prism: true });

  document.querySelector('form').requestSubmit(document.getElementById('force'));
  await settle();

  assert.equal(calls.prism[0].title, 'Force retry?');
});

test('"Question? Consequence." becomes title and message, with a tone and a verb button', async () => {
  const { document, calls } = createDom('<form data-confirm="Delete this visit to SDD? The record and all its photos will be removed. This cannot be undone."><button type="submit"><i class="bi bi-trash"></i></button></form>', { prism: true });

  document.querySelector('form').requestSubmit();
  await settle();

  const options = calls.prism[0];
  assert.equal(options.title, 'Delete this visit to SDD?');
  assert.equal(options.message, 'The record and all its photos will be removed. This cannot be undone.');
  assert.equal(options.tone, 'danger');
  assert.equal(options.confirmText, 'Delete visit to SDD');
});

test('withdraw is a warning, and a long title keeps just its verb for the button', async () => {
  const { document, calls } = createDom('<form data-confirm="Withdraw your plan submission from HoD review? Your changes stay in a private draft."><button type="submit">Withdraw</button></form>', { prism: true });

  document.querySelector('form').requestSubmit();
  await settle();

  assert.equal(calls.prism[0].tone, 'warning');
  assert.equal(calls.prism[0].confirmText, 'Withdraw');
});

test('data-confirm-title, -detail, -action and -tone override the inferred wording', async () => {
  const { document, calls } = createDom(`<form data-confirm="Queue a full Search V2 rebuild? The active generation stays live."
      data-confirm-title="Rebuild the search index?" data-confirm-detail="Takes about ten minutes."
      data-confirm-action="Queue rebuild" data-confirm-tone="primary"><button type="submit">Go</button></form>`, { prism: true });

  document.querySelector('form').requestSubmit();
  await settle();

  const options = calls.prism[0];
  assert.equal(options.title, 'Rebuild the search index?');
  assert.equal(options.message, 'Queue a full Search V2 rebuild? The active generation stays live.');
  assert.equal(options.detail, 'Takes about ten minutes.');
  assert.equal(options.confirmText, 'Queue rebuild');
  assert.equal(options.tone, 'primary');
});

test('without the shared dialog the browser box still guards the action', async () => {
  const { document, calls } = createDom('<form data-confirm="Merge this person?"><button id="go" type="submit">Merge</button></form>');

  document.querySelector('form').requestSubmit(document.getElementById('go'));
  await settle();

  assert.deepEqual(calls.confirm, ['Merge this person?']);
  assert.equal(calls.submits, 1);
});

test('data-page-handled leaves confirmation to the page script', () => {
  const { document, calls } = createDom('<form data-page-handled data-confirm="Delete this visit?"><button type="submit">Delete</button></form>', { prism: true });

  document.querySelector('form').requestSubmit();

  assert.deepEqual(calls.confirm, []);
  assert.deepEqual(calls.prism, []);
  assert.equal(calls.submits, 1);
});

test('data-auto-submit submits the owning form on change, and data-page-handled opts out', () => {
  const { window, document, calls } = createDom(`<form id="a"><select data-auto-submit><option>1</option></select></form>
      <form id="b"><select data-page-handled data-auto-submit><option>1</option></select></form>`);

  document.querySelector('#a select').dispatchEvent(new window.Event('change', { bubbles: true }));
  document.querySelector('#b select').dispatchEvent(new window.Event('change', { bubbles: true }));

  assert.equal(calls.submits, 1);
});

test('data-auto-submit honours a named event', () => {
  const { window, document, calls } = createDom('<form><input data-auto-submit="input" /></form>');
  const input = document.querySelector('input');

  input.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.equal(calls.submits, 0);

  input.dispatchEvent(new window.Event('input', { bubbles: true }));
  assert.equal(calls.submits, 1);
});

test('data-print and data-close-window call the window APIs', () => {
  const { document, calls } = createDom('<button data-print>Print</button><button data-close-window><span>Close</span></button>');

  document.querySelector('[data-print]').click();
  document.querySelector('[data-close-window] span').click();

  assert.equal(calls.print, 1);
  assert.equal(calls.close, 1);
});

test('declining data-confirm on a link prevents navigation', async () => {
  const { window, document, calls } = createDom('<a href="/Admin/Purge" data-confirm="Purge?">Purge</a>', { confirmResult: false, prism: true });
  const event = new window.MouseEvent('click', { bubbles: true, cancelable: true });

  document.querySelector('a').dispatchEvent(event);
  await settle();

  assert.equal(calls.prism.length, 1);
  assert.equal(event.defaultPrevented, true);
  assert.equal(calls.navigations, 0);
});

test('accepting data-confirm on a link follows it once', async () => {
  const { window, document, calls } = createDom('<a href="/Admin/Purge" data-confirm="Purge?">Purge</a>', { prism: true });
  const event = new window.MouseEvent('click', { bubbles: true, cancelable: true });
  // jsdom does not follow links, so count the clicks that reach the page un-cancelled
  // (the browser follows exactly those).
  let followed = 0;
  document.addEventListener('click', (e) => { if (!e.defaultPrevented) followed += 1; });

  document.querySelector('a').dispatchEvent(event);
  assert.equal(event.defaultPrevented, true, 'held while the dialog is open');
  await settle();

  assert.equal(calls.prism.length, 1);
  assert.equal(followed, 1);
});

test('data-auto-navigate follows same-origin values and ignores empty or cross-origin ones', () => {
  const { window, document, calls } = createDom(`<select data-auto-navigate>
      <option value="">Pick</option>
      <option value="/Photos/People/Review?sort=newest">Newest</option>
      <option value="https://elsewhere.example/">Away</option></select>`);
  const select = document.querySelector('select');
  const choose = (value) => {
    select.value = value;
    select.dispatchEvent(new window.Event('change', { bubbles: true }));
  };

  choose('');
  choose('https://elsewhere.example/');
  assert.equal(calls.navigations, 0);

  choose('/Photos/People/Review?sort=newest');
  assert.equal(calls.navigations, 1);
});

test('data-auto-navigate ignores javascript:, data: and protocol-relative values', () => {
  const { window, document, calls } = createDom(`<select data-auto-navigate>
      <option value="">Pick</option>
      <option value="javascript:alert(1)">js</option>
      <option value="data:text/html,hi">data</option>
      <option value="//elsewhere.example/x">proto-relative</option></select>`);
  const select = document.querySelector('select');

  for (const value of ['javascript:alert(1)', 'data:text/html,hi', '//elsewhere.example/x']) {
    select.value = value;
    select.dispatchEvent(new window.Event('change', { bubbles: true }));
  }

  assert.equal(calls.navigations, 0);
});

test('requestSubmit keeps native constraint validation for auto-submitted forms', () => {
  const { window, document, calls } = createDom(`<form>
      <input name="q" required>
      <select data-auto-submit><option>1</option><option>2</option></select></form>`);
  const select = document.querySelector('select');

  select.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.equal(calls.submits, 0); // required field empty: the browser blocks the submit

  document.querySelector('input').value = 'x';
  select.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.equal(calls.submits, 1);
});

test('arrow-key browsing in an auto-submit select waits for Enter', () => {
  const { window, document, calls } = createDom('<form><select data-auto-submit><option>1</option><option>2</option><option>3</option></select></form>');
  const select = document.querySelector('select');
  const key = (k) => select.dispatchEvent(new window.KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
  const change = () => select.dispatchEvent(new window.Event('change', { bubbles: true }));

  key('ArrowDown'); change();
  key('ArrowDown'); change();
  assert.equal(calls.submits, 0);

  key('Enter');
  assert.equal(calls.submits, 1);
});

test('arrow-key browsing commits when focus leaves the select', () => {
  const { window, document, calls } = createDom(`<select data-auto-navigate>
      <option value="/a">A</option><option value="/b">B</option></select><button>next</button>`);
  const select = document.querySelector('select');

  select.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
  select.value = '/b';
  select.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.equal(calls.navigations, 0);

  select.dispatchEvent(new window.FocusEvent('focusout', { bubbles: true }));
  assert.equal(calls.navigations, 1);
});

test('a pointer pick after arrow browsing applies immediately', () => {
  const { window, document, calls } = createDom('<form><select data-auto-submit><option>1</option><option>2</option></select></form>');
  const select = document.querySelector('select');

  select.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
  select.dispatchEvent(new window.Event('pointerdown', { bubbles: true }));
  select.dispatchEvent(new window.Event('change', { bubbles: true }));

  assert.equal(calls.submits, 1);
});
