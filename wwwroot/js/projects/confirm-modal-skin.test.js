const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Glass confirmation modals (docs/ux/confirm-dialogs.md). Bootstrap keeps .fade on an open
// modal, so an entrance offset written for .fade would also move the open sheet: on phones it
// pushed the whole confirmation below the screen. Offsets belong on .fade:not(.show) only.
const css = fs.readFileSync(path.join(__dirname, '..', '..', 'css', 'components', 'prism-confirm-dialog.css'), 'utf8');

test('confirm modal entrance offsets apply only while the modal is closed', () => {
  const rules = [...css.matchAll(/([^{}]*prism-confirm-modal[^{}]*)\{([^{}]*)\}/g)]
    .map(([, selector, body]) => ({ selector: selector.trim(), body }));
  const offsetRules = rules.filter((rule) => /\.modal-dialog\s*$/.test(rule.selector) && /transform:\s*translate/.test(rule.body));

  assert.ok(offsetRules.length >= 2, 'desktop and phone entrance offsets are present');
  for (const rule of offsetRules) {
    assert.match(rule.selector, /\.fade:not\(\.show\)/, `${rule.selector} must not move an open modal`);
  }
  assert.match(css, /\.modal\.prism-confirm-modal\.show \.modal-dialog\s*\{\s*transform:\s*none;/);
});
