const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, '..', 'pages', 'project-office-reports', 'tot-register.js'), 'utf8');

const page = `
  <form method="get" data-tot-search>
    <input type="hidden" name="TotStatusFilter" value="Completed" />
    <input type="hidden" name="LongWaiting" value="" />
    <input name="SearchTerm" data-tot-live-search />
  </form>
  <div data-tot-register><span class="count">139 projects</span></div>`;

function run(respond) {
  const dom = new JSDOM(`<!doctype html><body>${page}</body>`, {
    url: 'http://localhost/ProjectOfficeReports/Tot?TotStatusFilter=Completed',
    runScripts: 'outside-only'
  });
  const { window } = dom;
  const calls = [];
  window.fetch = (url) => {
    calls.push(String(url));
    return respond(String(url), calls.length);
  };
  window.eval(script);
  return { window, calls };
}

const html = (count) => `<div data-tot-register><span class="count">${count}</span></div>`;
const ok = (body) => Promise.resolve({ ok: true, text: () => Promise.resolve(body) });
const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

test('typing refreshes the register in place and keeps the address current', async () => {
  const { window, calls } = run(() => ok(html('3 projects')));
  const input = window.document.querySelector('[data-tot-live-search]');

  input.value = 'drona';
  input.dispatchEvent(new window.Event('input'));
  await wait(350);

  assert.equal(calls.length, 1, 'one request after the pause');
  const url = new URL(calls[0], 'http://localhost');
  assert.equal(url.searchParams.get('handler'), 'Register');
  assert.equal(url.searchParams.get('SearchTerm'), 'drona');
  assert.equal(url.searchParams.get('TotStatusFilter'), 'Completed', 'other filters are kept');
  assert.equal(url.searchParams.has('LongWaiting'), false, 'empty fields are left out');
  assert.equal(window.document.querySelector('.count').textContent, '3 projects');
  assert.equal(window.location.search, '?TotStatusFilter=Completed&SearchTerm=drona');
  assert.equal(window.document.activeElement === input || window.document.body.contains(input), true, 'the search box is not replaced');
});

test('an answer for an older search never replaces a newer one', async () => {
  const resolvers = [];
  const { window } = run(() => new Promise((resolve) => resolvers.push(resolve)));
  const form = window.document.querySelector('[data-tot-search]');
  const input = window.document.querySelector('[data-tot-live-search]');

  input.value = 'dr';
  form.dispatchEvent(new window.Event('submit', { cancelable: true }));
  input.value = 'drona';
  form.dispatchEvent(new window.Event('submit', { cancelable: true }));

  resolvers[1]({ ok: true, text: () => Promise.resolve(html('newer')) });
  await wait(10);
  resolvers[0]({ ok: true, text: () => Promise.resolve(html('older')) });
  await wait(10);

  assert.equal(window.document.querySelector('.count').textContent, 'newer');
});
