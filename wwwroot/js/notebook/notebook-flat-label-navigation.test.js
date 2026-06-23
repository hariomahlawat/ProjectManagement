const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { JSDOM } = require('jsdom');

function loadRenderer(dom) {
  let source = fs.readFileSync(path.join(__dirname, 'notebook-app.js'), 'utf8');
  const start = source.indexOf('export function renderNotebookLabelNavigation');
  const end = source.indexOf('\n// SECTION: Notebook app bootstrap', start);
  source = source.slice(start, end)
    .replace('export function ', 'function ');
  source += '\nmodule.exports={renderNotebookLabelNavigation};';
  const context = {
    module: { exports: {} },
    exports: {},
    document: dom.window.document,
    location: dom.window.location,
    URL,
    encodeURIComponent,
    console
  };
  vm.runInNewContext(source, context);
  return context.module.exports;
}

test('label navigation renders labels as flat peer rail items', () => {
  const dom = new JSDOM(
    '<div class="notebook-shell"><div data-notebook-label-rail></div></div>',
    { url: 'https://example.test/Notebook?view=home' }
  );
  const { renderNotebookLabelNavigation } = loadRenderer(dom);
  const shell = dom.window.document.querySelector('.notebook-shell');

  renderNotebookLabelNavigation(shell, [
    { id: 1, name: 'Ideas', count: 2 },
    { id: 2, name: 'Projects', count: 0 }
  ]);

  const links = [...shell.querySelectorAll('[data-notebook-label-rail] > a')];
  assert.equal(links.length, 2);
  assert.ok(links.every((link) => link.classList.contains('notebook-rail__item')));
  assert.ok(links.every((link) => link.classList.contains('notebook-rail__item--label')));
  assert.equal(links[0].querySelector('span').textContent, 'Ideas');
  assert.equal(links[0].querySelector('b').textContent, '2');
});

test('selected label is highlighted directly in the flat rail', () => {
  const dom = new JSDOM(
    '<div class="notebook-shell"><div data-notebook-label-rail></div></div>',
    { url: 'https://example.test/Notebook?view=labels&tag=Projects' }
  );
  const { renderNotebookLabelNavigation } = loadRenderer(dom);
  const shell = dom.window.document.querySelector('.notebook-shell');

  renderNotebookLabelNavigation(shell, [
    { id: 1, name: 'Ideas', count: 2 },
    { id: 2, name: 'Projects', count: 1 }
  ]);

  const selected = shell.querySelector('[data-notebook-label-rail] .is-active');
  assert.equal(selected.querySelector('span').textContent, 'Projects');
});

test('Razor rail exposes Edit labels as an in-place action and no nested Labels parent', () => {
  const source = fs.readFileSync(
    path.join(__dirname, '../../..', 'Pages', 'Notebook', 'Index.cshtml'),
    'utf8'
  );

  assert.match(source, /data-open-label-manager[\s\S]*Edit labels/);
  assert.doesNotMatch(source, /item\.Key == "labels"/);
  assert.match(source, /notebook-rail-labels--flat/);
});

test('legacy Labels root is redirected to Home with the editor requested', () => {
  const source = fs.readFileSync(
    path.join(__dirname, '../../..', 'Pages', 'Notebook', 'Index.cshtml.cs'),
    'utf8'
  );

  assert.match(source, /View,\s*"labels"/);
  assert.match(source, /view = "home", editLabels = true/);
});
