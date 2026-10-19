const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'declarative-actions.js'), 'utf8');

function createDom(body, { confirmResult = true } = {}) {
  const calls = { confirm: [], print: 0, close: 0, submits: 0, navigations: 0 };
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
  window.print = () => { calls.print += 1; };
  window.close = () => { calls.close += 1; };
  window.eval(script);
  // jsdom's native requestSubmit runs validation and fires submit (it does not navigate);
  // count the submissions that reach the document without being cancelled.
  window.document.addEventListener('submit', (event) => {
    if (!event.defaultPrevented) calls.submits += 1;
  });
  return { window, document: window.document, calls };
}

test('data-confirm on a form lets the submit through when accepted', () => {
  const { document, calls } = createDom('<form data-confirm="Merge this person?"><button id="go" type="submit">Merge</button></form>');

  document.querySelector('form').requestSubmit(document.getElementById('go'));

  assert.deepEqual(calls.confirm, ['Merge this person?']);
  assert.equal(calls.submits, 1);
});

test('declining data-confirm cancels the submit before form listeners run', () => {
  const { document, calls } = createDom('<form data-confirm="Disconnect this folder?"><button type="submit">Go</button></form>', { confirmResult: false });
  const form = document.querySelector('form');
  let formListenerRan = false;
  form.addEventListener('submit', () => { formListenerRan = true; });

  form.requestSubmit();

  assert.deepEqual(calls.confirm, ['Disconnect this folder?']);
  assert.equal(calls.submits, 0);
  assert.equal(formListenerRan, false);
});

test('data-confirm on the submitter takes precedence over the form', () => {
  const { document, calls } = createDom(`<form data-confirm="Form message">
      <button id="force" type="submit" data-confirm="Force retry?">Force</button></form>`);

  document.querySelector('form').requestSubmit(document.getElementById('force'));

  assert.deepEqual(calls.confirm, ['Force retry?']);
});

test('data-page-handled leaves confirmation to the page script', () => {
  const { document, calls } = createDom('<form data-page-handled data-confirm="Delete this visit?"><button type="submit">Delete</button></form>');

  document.querySelector('form').requestSubmit();

  assert.deepEqual(calls.confirm, []);
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

test('declining data-confirm on a link prevents navigation', () => {
  const { window, document, calls } = createDom('<a href="/Admin/Purge" data-confirm="Purge?">Purge</a>', { confirmResult: false });
  const event = new window.MouseEvent('click', { bubbles: true, cancelable: true });

  document.querySelector('a').dispatchEvent(event);

  assert.deepEqual(calls.confirm, ['Purge?']);
  assert.equal(event.defaultPrevented, true);
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
