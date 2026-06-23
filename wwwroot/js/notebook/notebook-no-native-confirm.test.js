const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const files = [
  'notebook-app.js',
  'notebook-editor.js',
  'notebook-label-manager.js'
];

test('Notebook modules do not use native browser confirm dialogs', () => {
  for (const file of files) {
    const source = fs.readFileSync(path.join(__dirname, file), 'utf8');
    assert.equal(/\bwindow\.confirm\s*\(|(^|[^\w.])confirm\s*\(/m.test(source), false, `${file} still uses native confirm()`);
  }
});
