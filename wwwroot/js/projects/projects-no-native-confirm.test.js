const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Confirm dialogs PR 3 (docs/ux/confirm-dialogs.md): the Projects pages ask in the shared
// PRISM dialog. The browser box is allowed only inside each file's askConfirm fallback, used
// when the dialog script is missing.
const root = path.join(__dirname, '..');
const files = [
  'pages/projects-brochure.js',
  'pages/projects-compendium-structure-editor.js',
  'pages/projects-completed-summary-edit.js',
  'projects/overview-multi-jdp.js',
  'projects/overview-tot.js',
  'projects/plan-edit.js',
  'projects/project-content.js',
  'projects/remarks-panel.js',
  'widgets/project-photo-reorder.js'
];

const fallback = "Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' ')))";

for (const file of files) {
  test(`${file} asks in the PRISM dialog, not the browser box`, () => {
    const source = fs.readFileSync(path.join(root, file), 'utf8');
    assert.match(source, /window\.PrismConfirm\.show\(options\)/, 'uses the shared dialog');
    const withoutFallback = source.split(fallback).join('');
    assert.equal(/\bwindow\.confirm\s*\(|(^|[^\w.])confirm\s*\(/m.test(withoutFallback), false, `${file} still calls confirm() directly`);
  });
}
