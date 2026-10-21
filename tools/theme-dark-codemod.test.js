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

test('colour words inside quoted strings are left alone', () => {
  const { output, generated } = convert('.x::before { content: "black"; color: black; }');
  assert.equal(generated, 1);
  assert.doesNotMatch(output, /content: "#/);
  assert.match(output, /:where\(\[data-bs-theme="dark"\]\) \.x::before \{\n {4}color: #f0f2f4;/);
});

test('light islands and their BEM parts and descendants keep their Light colours', () => {
  const css = `
    .proof { background: #fff; }
    .proof--wide { background: #f8fafc; }
    .proof__title, .proof .caption { color: #333; }
    .card { background: #f7f9fc; }
    [data-bs-theme="dark"] :is(.proof) { color-scheme: light; --pm-card: #ffffff; }`;
  const { output } = convert(css);
  // Island rules are only restated with their Light values, never mapped to graphite.
  assert.doesNotMatch(output, /\]\) \.proof[^{]*\{\n {4}(background|color): #(16181b|1e2125|25282d|b4b9bf)/);
  assert.match(output, /\]\) \.proof \{\n {4}background: #fff;/);
  assert.match(output, /:where\(\[data-bs-theme="dark"\]\) \.card \{/);
});

test('light islands restore the page tokens that Dark redefines', () => {
  const css = `
    .page { --ink: #15231f; --paper: #fbfaf6; }
    .proof { background: var(--paper); color: var(--ink); }
    [data-bs-theme="dark"] :is(.proof) { color-scheme: light; }`;
  const { output } = convert(css);
  assert.match(output, /:root\[data-bs-theme="dark"\] :is\(\.proof\) \{\n {4}--ink: #15231f;\n {4}--paper: #fbfaf6;/);
});

test('island selectors are dropped from grouped rules; the rest still convert', () => {
  const css = `
    .proof, .ordinary { background: #f7f9fc; }
    [data-bs-theme="dark"] :is(.proof) { color-scheme: light; }`;
  const { output } = convert(css);
  assert.match(output, /:where\(\[data-bs-theme="dark"\]\) \.ordinary \{\n {4}background: #16181b;/);
  assert.doesNotMatch(output, /\]\) \.proof,/);
  assert.match(output, /\]\) \.proof \{\n {4}background: #f7f9fc;/);
});

test('a light island keeps its own palette over a dark rule for its shared base class', () => {
  const css = `
    .thumb { background: #f3f3f3; }
    .thumb--light { background: #f7f7f5; }
    .thumb--light .thumb__bar { background: #7a263a; }
    [data-bs-theme="dark"] :is(.thumb--light) { color-scheme: light; }`;
  const { output } = convert(css);
  const base = output.indexOf(':where([data-bs-theme="dark"]) .thumb {');
  const island = output.indexOf(':where([data-bs-theme="dark"]) .thumb--light {');
  assert.ok(base > 0 && island > base, 'island restated after the base dark rule');
  assert.match(output.slice(island), /^:where\(\[data-bs-theme="dark"\]\) \.thumb--light \{\n {4}background: #f7f7f5;/);
});
