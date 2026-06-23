const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function load() {
  let source = fs.readFileSync(path.join(__dirname, 'notebook-label-picker.js'), 'utf8');
  source = source
    .replace("import { NotebookApi } from './notebook-api.js';", 'const NotebookApi = {};')
    .replace(/export async function /g, 'async function ')
    .replace(/export function /g, 'function ');
  source += '\nmodule.exports={normaliseLabelName,normaliseLabels};';
  const context = {
    module: { exports: {} }, exports: {},
    document: { querySelector: () => null, dispatchEvent: () => {}, addEventListener: () => {} },
    CustomEvent: class CustomEvent { constructor(type, init){ this.type=type; this.detail=init?.detail; } }
  };
  vm.runInNewContext(source, context);
  return context.module.exports;
}

test('label names are trimmed and hashes removed', () => {
  const { normaliseLabelName } = load();
  assert.equal(normaliseLabelName(' ## Procurement '), 'Procurement');
});

test('labels are deduplicated case-insensitively', () => {
  const { normaliseLabels } = load();
  assert.deepEqual(Array.from(normaliseLabels(['Docs', ' docs ', '#OPS', 'ops'])), ['Docs', 'OPS']);
});

test('empty labels are removed from picker values', () => {
  const { normaliseLabels } = load();
  assert.deepEqual(Array.from(normaliseLabels(['', '  ', '#', 'Work'])), ['Work']);
});
