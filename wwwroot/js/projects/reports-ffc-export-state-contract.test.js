const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const root = process.cwd();
const view = fs.readFileSync(
  path.join(root, 'Pages', 'Projects', 'Reports', 'FfcProjectsUpdate.cshtml'),
  'utf8');
const script = fs.readFileSync(
  path.join(root, 'wwwroot', 'js', 'pages', 'projects-reports-ffc.js'),
  'utf8');
const css = fs.readFileSync(
  path.join(root, 'wwwroot', 'css', 'pages', 'projects-reports.css'),
  'utf8');
const controller = fs.readFileSync(
  path.join(root, 'wwwroot', 'js', 'pages', 'projects-report-controls.js'),
  'utf8');

function createReport(t) {
  const dom = new JSDOM(`<!doctype html><form data-report-controls data-ffc-report-controls>
    <input id="ffcSelectionMode" name="selectionMode" value="DefaultActive">
    <input id="ffcCountryYears" name="countryYears" value="1">
    <input type="checkbox" value="1" checked data-ffc-country-year data-default-included="true" data-report-setting data-report-setting-key="countryYears">
    <input type="checkbox" value="2" data-ffc-country-year data-default-included="false" data-report-setting data-report-setting-key="countryYears">
    <button type="button" data-ffc-country-action="all">All</button>
    <button type="button" data-ffc-country-action="default">Default</button>
    <button type="button" data-ffc-country-action="clear">Clear</button>
    <button data-report-update>Update report</button>
    <span data-report-update-required hidden>Update required</span>
  </form>
  <a href="/export" data-report-export data-ffc-export>Export</a>
  <a href="/unavailable" class="disabled" aria-disabled="true" data-report-export data-ffc-export>Unavailable export</a>`, {
    url: 'https://example.test', runScripts: 'outside-only'
  });
  t.after(() => dom.window.close());
  dom.window.eval(script);
  dom.window.eval(controller);
  return dom.window;
}

test('FFC report exposes explicit applied-state export protection', (t) => {
  assert.match(view, /data-ffc-export/);
  assert.match(view, /data-ffc-update-required/);
  assert.match(view, /data-report-controls/);
  assert.match(view, /data-report-export/);
  assert.match(view, /projects-report-controls\.js/);
  const window = createReport(t);
  const document = window.document;
  const link = document.querySelector('[data-ffc-export]');
  const checkboxes = document.querySelectorAll('[data-ffc-country-year]');
  assert.equal(link.getAttribute('aria-disabled'), 'false');
  checkboxes[1].checked = true;
  checkboxes[1].dispatchEvent(new window.Event('change'));
  assert.equal(link.getAttribute('aria-disabled'), 'true');
  assert.equal(link.dispatchEvent(new window.MouseEvent('click', { cancelable: true })), false);
  checkboxes[1].checked = false;
  checkboxes[1].dispatchEvent(new window.Event('change'));
  assert.equal(link.getAttribute('aria-disabled'), 'false');
  assert.equal(document.querySelectorAll('[data-ffc-export]')[1].getAttribute('aria-disabled'), 'true');
});

test('FFC report communicates that changed options require update', (t) => {
  assert.match(css, /\.report-update-state/);
  assert.match(css, /\[data-ffc-export\]\[aria-disabled="true"\]/);
  const document = createReport(t).document;
  const update = document.querySelector('[data-report-update]');
  const status = document.querySelector('[data-report-update-required]');
  document.querySelector('[data-ffc-country-action="all"]').click();
  assert.equal(update.classList.contains('report-refresh-button--pending'), true);
  assert.equal(status.hidden, false);
  assert.equal(document.querySelector('#ffcSelectionMode').value, 'Custom');
  document.querySelector('[data-ffc-country-action="default"]').click();
  assert.equal(update.classList.contains('report-refresh-button--pending'), false);
  assert.equal(status.hidden, true);
});

test('FFC report synchronizes country selection before every GET submit', (t) => {
  assert.doesNotMatch(script, /form\.submit\(\)/);
  const window = createReport(t);
  const form = window.document.querySelector('form');
  form.addEventListener('submit', event => event.preventDefault());
  window.document.querySelector('[data-ffc-country-action="all"]').click();
  form.dispatchEvent(new window.Event('submit', { cancelable: true }));
  assert.equal(form.querySelector('#ffcCountryYears').value, '1,2');
  window.document.querySelector('[data-ffc-country-action="clear"]').click();
  form.dispatchEvent(new window.Event('submit', { cancelable: true }));
  assert.equal(form.querySelector('#ffcCountryYears').value, '');
});
