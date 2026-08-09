const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const cssPath = path.resolve(__dirname, '../../css/notebook.css');

function readCss() {
  return fs.readFileSync(cssPath, 'utf8');
}

test('direct reminder creation uses the compact expanded-card layout', () => {
  const css = readCss();

  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-modal__dialog\s*\{/);
  assert.match(css, /width:\s*min\(590px,\s*calc\(100vw - 32px\)\)/);
  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-create-details\s*\{[\s\S]*?border:\s*0;/);
  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-reminder-scheduler\s*\{[\s\S]*?background:\s*transparent;/);
});

test('direct reminder creation combines metadata and actions into one bottom bar', () => {
  const css = readCss();

  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-editor-bottom-bar\s*\{[\s\S]*?display:\s*grid;/);
  assert.match(css, /grid-template-columns:\s*auto minmax\(0, 1fr\);/);
  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-editor-bottom-bar > footer\s*\{[\s\S]*?grid-column:\s*2;[\s\S]*?position:\s*static;/);
  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-create-submit\s*\{[\s\S]*?min-height:\s*34px;/);
});

test('direct reminder refinement is scoped and responsive', () => {
  const css = readCss();

  assert.match(css, /@media \(max-width: 640px\)[\s\S]*?data-direct-reminder="true"/);
  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-create-details__grid/);
  assert.match(css, /\.notebook-modal\.is-create-mode\[data-direct-reminder="true"\] \.notebook-editor-bottom-bar > footer/);
});
