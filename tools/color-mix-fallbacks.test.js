'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { parseColour, formatColour, mixSrgb, evaluateMix, generate, BEGIN, END } = require('./color-mix-fallbacks');

const ROOT = path.resolve(__dirname, '..');

test('colours parse and print', () => {
  assert.deepEqual(parseColour('#6ea8fe'), [110, 168, 254, 1]);
  assert.deepEqual(parseColour('#fff'), [255, 255, 255, 1]);
  assert.deepEqual(parseColour('transparent'), [0, 0, 0, 0]);
  assert.deepEqual(parseColour('rgba(13, 110, 253, .5)'), [13, 110, 253, 0.5]);
  assert.equal(formatColour([36.08, 47.2, 63.3, 1]), '#242f3f');
  assert.equal(formatColour([13, 110, 253, 0.28]), 'rgba(13, 110, 253, 0.28)');
});

test('color-mix in srgb follows CSS Color 5 (premultiplied alpha, normalised percentages)', () => {
  // Dark --pm-primary-soft: 16% #6ea8fe into the dark card.
  assert.equal(formatColour(evaluateMix('color-mix(in srgb, #6ea8fe 16%, #16181b)', new Map())), '#242f3f');
  // Mixing with transparent keeps the colour and scales the alpha.
  assert.equal(formatColour(mixSrgb([13, 110, 253, 1], 28, [0, 0, 0, 0], undefined)), 'rgba(13, 110, 253, 0.28)');
  // Percentages that add to less than 100% scale the alpha.
  assert.equal(formatColour(mixSrgb([255, 0, 0, 1], 20, [0, 0, 255, 1], 30)), 'rgba(102, 0, 153, 0.5)');
  // Tokens and nested var() resolve through the map.
  const tokens = new Map([['--a', '#ffffff'], ['--b', 'var(--a)']]);
  assert.equal(formatColour(evaluateMix('color-mix(in srgb, var(--b) 50%, #000000)', tokens)), '#808080');
  // A colour it cannot work out is reported, not guessed.
  assert.equal(evaluateMix('color-mix(in srgb, currentColor 50%, transparent)', new Map()), null);
  assert.equal(evaluateMix('color-mix(in oklab, #fff 50%, #000)', new Map()), null);
});

test('the generated block applies only without color-mix() and sits after the rules it repeats', () => {
  const source = '.a { background: color-mix(in srgb, var(--x) 50%, transparent); }\n.b { color: color-mix(in srgb, #000 50%, #fff); }\n.b { color: red; }\n';
  const bootstrap = ':root,[data-bs-theme=light]{--x:#ff0000}[data-bs-theme=dark]{--x:#0000ff}';
  const { css, unresolved } = generate(source, bootstrap);
  assert.deepEqual(unresolved, []);
  const block = css.slice(css.indexOf(BEGIN));
  assert.match(block, /@supports not \(color: color-mix\(in srgb, currentColor 50%, transparent\)\)/);
  assert.match(block, /\.a \{\n\s+background: var\(--pm-cm-1\);/);
  assert.match(block, /:root \{[^}]*--pm-cm-1: rgba\(255, 0, 0, 0\.5\);/);
  assert.match(block, /:root\[data-bs-theme="dark"\] \{[^}]*--pm-cm-1: rgba\(0, 0, 255, 0\.5\);/);
  // .b's mix is overridden by a later plain declaration, so it gets no fallback.
  assert.doesNotMatch(block, /\.b \{/);
  // Generating again from the output gives the same output.
  assert.equal(generate(css, bootstrap).css, css);
});

test('site.css has an up-to-date color-mix() fallback block', () => {
  const site = fs.readFileSync(path.join(ROOT, 'wwwroot/css/site.css'), 'utf8');
  const bootstrap = fs.readFileSync(path.join(ROOT, 'wwwroot/lib/bootstrap/dist/css/bootstrap.min.css'), 'utf8');
  assert.ok(site.includes(BEGIN) && site.includes(END), 'site.css has no generated block; run node tools/color-mix-fallbacks.js --write');
  const { css, unresolved } = generate(site, bootstrap);
  assert.deepEqual(unresolved, [], 'some color-mix() values in site.css cannot be worked out');
  assert.ok(css === site, 'site.css color-mix() fallbacks are out of date: run node tools/color-mix-fallbacks.js --write');
});
