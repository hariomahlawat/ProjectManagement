const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadOpenTargetHelper() {
  const source = fs.readFileSync(path.resolve(__dirname, 'notebook-utils.js'), 'utf8')
    .replace(/export const /g, 'const ')
    .replace('export function getPassiveNotebookCardOpenTarget', 'function getPassiveNotebookCardOpenTarget');
  const context = vm.createContext({});
  vm.runInContext(`${source}; globalThis.__helper = getPassiveNotebookCardOpenTarget;`, context);
  return context.__helper;
}

function createFixture({ interactive = false, openable = true, rearranging = false, pointerDragging = false } = {}) {
  const interactiveNode = {};
  const card = {
    querySelector: (selector) => selector === '[data-action="open-note"]' && openable ? {} : null
  };
  const shell = {
    contains: (node) => node === card,
    classList: {
      contains: (name) => (name === 'is-rearranging' && rearranging) || (name === 'is-pointer-dragging' && pointerDragging)
    }
  };
  const target = {
    closest: (selector) => {
      if (selector === '[data-note-id]') return card;
      if (selector.includes('button') || selector.includes('a,')) return interactive ? interactiveNode : null;
      return null;
    }
  };
  return { target, shell, card };
}

test('passive checklist text resolves to its card for opening', () => {
  const helper = loadOpenTargetHelper();
  const { target, shell, card } = createFixture();
  assert.equal(helper(target, shell), card);
});

test('interactive descendants keep their own action instead of opening the card', () => {
  const helper = loadOpenTargetHelper();
  const { target, shell } = createFixture({ interactive: true });
  assert.equal(helper(target, shell), null);
});

test('passive card opening is disabled while rearranging', () => {
  const helper = loadOpenTargetHelper();
  const { target, shell } = createFixture({ rearranging: true });
  assert.equal(helper(target, shell), null);
});

test('cards without an open-note route are not opened passively', () => {
  const helper = loadOpenTargetHelper();
  const { target, shell } = createFixture({ openable: false });
  assert.equal(helper(target, shell), null);
});
