'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { parseColour, formatColour, mixSrgb, mixOklab, evaluateMix, approximateMix, resolvePercent, tokenMaps, generate, generateAll, BEGIN, END, HAND_WRITTEN } = require('./color-mix-fallbacks');
const { readRules } = require('./lib/css-rules');

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
  assert.equal(evaluateMix('color-mix(in hsl, #fff 50%, #000)', new Map()), null);
});

test('color-mix in oklab matches CSS Color 4 (values checked against Chromium)', () => {
  // Chromium: color-mix(in oklab, #2563d6 10%, white) = rgb(232, 240, 253).
  assert.equal(formatColour(mixOklab(parseColour('#2563d6'), 10, parseColour('white'), undefined)), '#e8f0fd');
  assert.equal(formatColour(mixOklab(parseColour('#000'), 50, parseColour('#fff'), undefined)), '#636363');
  assert.equal(formatColour(evaluateMix('color-mix(in oklab, #fff 100%, #000)', new Map())), '#ffffff');
});

test('percentages can come from tokens and simple calc()', () => {
  const tokens = new Map([['--mix', '46%'], ['--alias', 'var(--mix)']]);
  assert.equal(resolvePercent('var(--alias)', tokens), 46);
  assert.equal(resolvePercent('calc(var(--mix) / 2)', tokens), 23);
  assert.ok(Number.isNaN(resolvePercent('calc(var(--mix) + 2%)', tokens)));
});

test('a mix that depends on the element falls back to its dominant colour, as written', () => {
  const tokens = new Map();
  assert.equal(approximateMix('color-mix(in srgb, var(--hue) 14%, transparent)', tokens), 'transparent');
  assert.equal(approximateMix('color-mix(in srgb, var(--hue) 78%, var(--line))', tokens), 'var(--hue)');
  assert.equal(approximateMix('color-mix(in srgb, var(--hue) 40%, color-mix(in srgb, var(--a) 70%, white))', tokens), 'var(--a)');
});

test('component tokens are used only when their value is certain, per theme', () => {
  const rules = readRules(`
    .card { --tone: #ff0000; --edge: #00ff00; }
    .tile-a { --hue: #111111; } .tile-b { --hue: #222222; }
    [data-bs-theme="dark"] .card { --tone: #0000ff; }
    [data-bs-theme="dark"] .card .light-preview { --tone: #ff0000; }`);
  const { light, dark } = tokenMaps([], rules);
  assert.equal(light.get('--tone'), '#ff0000');
  assert.equal(dark.has('--tone'), false); // two Dark values: depends on the element
  assert.equal(dark.get('--edge'), '#00ff00'); // not redefined in Dark: the Light value holds
  assert.equal(light.has('--hue'), false);
});

test('the generated block applies only without color-mix() and sits after the rules it repeats', () => {
  const source = '.a { background: color-mix(in srgb, var(--x) 50%, transparent); }\n.b { color: color-mix(in srgb, #000 50%, #fff); }\n.b { color: red; }\n';
  const bootstrap = ':root,[data-bs-theme=light]{--x:#ff0000}[data-bs-theme=dark]{--x:#0000ff}';
  const { css, approximated } = generate(source, { rootSources: [readRules(bootstrap)] });
  assert.deepEqual(approximated, []);
  const block = css.slice(css.indexOf(BEGIN));
  assert.match(block, /@supports not \(color: color-mix\(in srgb, currentColor 50%, transparent\)\)/);
  assert.match(block, /\.a \{\n\s+background: var\(--pm-cm-1\);/);
  assert.match(block, /:root \{[^}]*--pm-cm-1: rgba\(255, 0, 0, 0\.5\);/);
  assert.match(block, /:root\[data-bs-theme="dark"\] \{[^}]*--pm-cm-1: rgba\(0, 0, 255, 0\.5\);/);
  // .b's mix is overridden by a later plain declaration, so it gets no fallback.
  assert.doesNotMatch(block, /\.b \{/);
  // Generating again from the output gives the same output.
  assert.equal(generate(css, { rootSources: [readRules(bootstrap)] }).css, css);
});

test('a later conditional override of a mixed colour still wins after the generated block', () => {
  // Codex review on #3421: prism-confirm-dialog.css sets a solid surface in
  // @supports not (backdrop-filter) after the translucent mix. A browser missing both features
  // must keep the solid surface, so the block repeats that rule after the replacement.
  const source = [
    '.surface { background: color-mix(in srgb, #ffffff 70%, transparent); }',
    '.other, .surface { color: red; }',
    '@supports not (backdrop-filter: blur(1px)) { .surface, .unrelated { background: var(--card); } }',
    '.unrelated { background: blue; }',
  ].join('\n');
  const { css } = generate(source, { rootSources: [] });
  const block = css.slice(css.indexOf(BEGIN));
  const replaced = block.indexOf('.surface {\n        background: var(--pm-cm-1);');
  const repeated = block.indexOf('@supports not (backdrop-filter: blur(1px)) {\n        .surface {\n            background: var(--card);');
  assert.ok(replaced > 0, 'the mix is replaced');
  assert.ok(repeated > replaced, 'the conditional override is repeated after the replacement');
  // Only the matching selector is repeated, so a later plain rule for .unrelated is not undone,
  // and properties the block did not replace are left alone.
  assert.doesNotMatch(block, /\.unrelated/);
  assert.doesNotMatch(block, /color: red/);
});

test('every stylesheet has an up-to-date color-mix() fallback block', () => {
  const results = generateAll();
  assert.ok(results.size > 1, 'expected the stylesheets to be found');
  const stale = [...results].filter(([, r]) => r.css !== r.source).map(([file]) => path.relative(ROOT, file));
  assert.deepEqual(stale, [], 'color-mix() fallbacks are out of date: run node tools/color-mix-fallbacks.js --write');
  for (const [file, r] of results) {
    if (/color-mix\(/.test(r.source.slice(0, r.source.includes(BEGIN) ? r.source.indexOf(BEGIN) : undefined))) {
      assert.ok(r.source.includes(BEGIN) && r.source.includes(END), `${path.relative(ROOT, file)} has no generated block`);
    }
  }
  // site.css needs no approximation: every mix there is worked out exactly.
  const site = [...results].find(([file]) => file.endsWith(`${path.sep}site.css`));
  assert.deepEqual(site[1].approximated, []);
  // The hand-written public pages are left to their own fallbacks.
  for (const name of HAND_WRITTEN) assert.ok(![...results.keys()].some((file) => file.endsWith(`${path.sep}${name}`)));
});
