const test = require('node:test');
const assert = require('node:assert/strict');
const { convert, darkColour } = require('./theme-dark-codemod');

test('swaps a literal for a token only when the light value is identical', () => {
  const { output, swapped } = convert('.a { background: #fff; border: 1px solid #e5e7eb; color: #fff; }');
  assert.equal(swapped, 2);
  assert.match(output, /background: var\(--pm-card, #fff\)/);
  assert.match(output, /border: 1px solid var\(--pm-border, #e5e7eb\)/);
  assert.match(output, /color: #fff;/); // white text on a fill is not a surface
});

test('generates a dark rule with the same selector and leaves light rules alone', () => {
  const { output } = convert('.a, .b:hover { background: #f7f9fc; color: #10213b; }');
  assert.match(output, /^\.a, \.b:hover \{ background: #f7f9fc; color: #10213b; \}/);
  // :where() keeps the light rule's specificity.
  assert.match(output, /:where\(\[data-bs-theme="dark"\]\) \.a,\n:where\(\[data-bs-theme="dark"\]\) \.b:hover \{/);
});

test('keeps media queries around generated rules and skips print', () => {
  const { output } = convert('@media (max-width: 600px) { .a { background: #f7f9fc; } } @media print { .p { color: #333; } }');
  assert.match(output, /@media \(max-width: 600px\) \{\n {4}:where\(\[data-bs-theme="dark"\]\) \.a/);
  assert.doesNotMatch(output, /\]\) \.p/);
});

test('does not repeat a rule that a dark rule already repaints', () => {
  const { generated } = convert('.a { color: #333; } [data-bs-theme="dark"] .a { color: #eee; }');
  assert.equal(generated, 0);
});

test('maps colours by role onto the graphite palette', () => {
  assert.equal(darkColour('#ffffff', 'background'), '#16181b');
  assert.equal(darkColour('#f3f6fb', 'background', '', { property: '--db-bg' }), '#0e0f11'); // page canvas
  assert.equal(darkColour('#10213b', 'text'), '#f0f2f4'); // navy text -> off-white
  assert.equal(darkColour('#687890', 'text'), '#9aa0a6'); // slate -> muted
  assert.equal(darkColour('#1d4ed8', 'text'), '#6ea8fe'); // blue text -> palette accent
  assert.equal(darkColour('#dbe4f0', 'border'), '#2c3035'); // blue-grey hairline
  assert.match(darkColour('#edf3ff', 'background'), /^color-mix\(in srgb, #6ea8fe \d+%, #16181b\)$/); // pastel -> tint
  assert.equal(darkColour('#ffc107', 'background'), '#ffc107'); // solid status fill keeps its colour
  assert.equal(darkColour('rgba(23, 23, 23, .72)', 'background'), 'rgba(0, 0, 0, 0.72)'); // scrim stays dark
  assert.match(darkColour('rgba(49, 94, 232, .2)', 'shadow', '0 0 0 3px rgba(49, 94, 232, .2)'), /^rgba\(110, 168, 254, /); // focus ring
});

test('re-asserts state rules after a repainted base rule, in source order', () => {
  const { output, reasserted } = convert('.tabs a { color: #40516d; } .tabs a.active { color: #fff; background: var(--navy); } .tabs a:hover { color: var(--navy); }');
  assert.equal(reasserted, 2);
  const base = output.indexOf(':where([data-bs-theme="dark"]) .tabs a {');
  const active = output.indexOf(':where([data-bs-theme="dark"]) .tabs a.active {');
  const hover = output.indexOf(':where([data-bs-theme="dark"]) .tabs a:hover {');
  assert.ok(base > 0 && active > base && hover > active);
  assert.match(output.slice(active), /color: #fff;/);
});

test('only the selector subject marks a page canvas', () => {
  assert.equal(darkColour('#f2f6fc', 'background', '', { selector: '.ideas-page' }), '#0e0f11');
  assert.notEqual(darkColour('#f2f6fc', 'background', '', { selector: '.ideas-page .tabs a:hover' }), '#0e0f11');
});
