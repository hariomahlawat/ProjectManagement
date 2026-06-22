const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadHelpers() {
  const source = fs.readFileSync(path.join(__dirname, 'notebook-colour-picker.js'), 'utf8')
    .replace(/export function /g, 'function ')
    .concat('\nmodule.exports = { normaliseNotebookColour, applyNotebookSurfaceColour, setNotebookColourSelection };');
  const context = { module: { exports: {} }, exports: {}, String, Object };
  vm.createContext(context);
  vm.runInContext(source, context);
  return context.module.exports;
}

test('normaliseNotebookColour accepts approved colours and rejects unknown values', () => {
  const { normaliseNotebookColour } = loadHelpers();
  assert.equal(normaliseNotebookColour(' Amber '), 'amber');
  assert.equal(normaliseNotebookColour(''), '');
  assert.equal(normaliseNotebookColour('purple'), '');
});

test('applyNotebookSurfaceColour replaces the previous surface colour class', () => {
  const { applyNotebookSurfaceColour } = loadHelpers();
  const classes = new Set(['notebook-surface-colour-blue', 'other']);
  const element = {
    dataset: {},
    classList: {
      remove: (...names) => names.forEach((name) => classes.delete(name)),
      add: (...names) => names.forEach((name) => classes.add(name))
    }
  };

  applyNotebookSurfaceColour(element, 'green');

  assert.equal(classes.has('notebook-surface-colour-blue'), false);
  assert.equal(classes.has('notebook-surface-colour-green'), true);
  assert.equal(element.dataset.colourValue, 'green');
});
