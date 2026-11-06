const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Confirm dialogs (docs/ux/confirm-dialogs.md): every "are you sure?" in PRISM asks in the shared
// PRISM dialog (window.PrismConfirm). The browser's confirm() box is allowed only as the fallback
// for when the dialog script is missing: the one-line askConfirm helper in each script, and the
// data-confirm handler in core/declarative-actions.js.
const jsRoot = path.join(__dirname, '..');
const allowedFallbacks = [
  "Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' ')))",
  'return Promise.resolve(window.confirm(text));' // core/declarative-actions.js
];
const nativeConfirm = /\bwindow\.confirm\s*\(|(^|[^\w.])confirm\s*\(/m;

function scripts(directory, found = []) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name === 'dist') continue;
      scripts(full, found);
    } else if (/\.(m?js)$/.test(entry.name) && !entry.name.endsWith('.test.js') && !entry.name.endsWith('.min.js')) {
      found.push(full);
    }
  }
  return found;
}

test('no PRISM script calls the browser confirm() box directly', () => {
  const offenders = [];
  for (const file of scripts(jsRoot)) {
    let source = fs.readFileSync(file, 'utf8');
    allowedFallbacks.forEach((allowed) => { source = source.split(allowed).join(''); });
    if (nativeConfirm.test(source)) offenders.push(path.relative(jsRoot, file));
  }
  assert.deepEqual(offenders, [], `Ask with window.PrismConfirm.show (see docs/ux/confirm-dialogs.md): ${offenders.join(', ')}`);
});

test('the layout no longer carries the old Bootstrap "Are you sure?" modal', () => {
  const layout = fs.readFileSync(path.join(jsRoot, '..', '..', 'Pages', 'Shared', '_Layout.cshtml'), 'utf8');
  assert.doesNotMatch(layout, /appConfirmModal/);
  assert.match(layout, /<partial name="_PrismConfirmDialog" \/>/);
});
