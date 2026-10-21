const test = require('node:test');
const assert = require('node:assert/strict');
const { analyseCss, countLiterals, stripVarFallbacks, measure, readBaseline, compare } = require('./theme-coverage');

test('counts hex, rgb and hsl literals', () => {
  assert.equal(countLiterals('#fff'), 1);
  assert.equal(countLiterals('0 1px 2px rgba(15, 23, 42, .1), inset 0 0 0 1px #e5e7eb'), 2);
  assert.equal(countLiterals('hsl(210 20% 98%)'), 1);
  assert.equal(countLiterals('var(--pm-card)'), 0);
});

test('a var() fallback is a token use, not a literal', () => {
  assert.equal(stripVarFallbacks('var(--pm-card, #fff)'), 'var(--pm-card)');
  assert.equal(countLiterals('var(--pm-border, rgba(15, 23, 42, .1))'), 0);
  assert.equal(countLiterals('color-mix(in srgb, var(--pm-accent, #3b82f6) 10%, #fff)'), 1);
});

test('dark rules and print media are not counted', () => {
  const css = `
    .a { color: #111; }
    [data-bs-theme="dark"] .a { color: #eee; }
    @media print { .a { color: #000; } }
    @media (max-width: 600px) { .a { background: #fafafa; } }`;
  // .a color is repainted by the dark rule; the media-query background is a gap.
  assert.deepEqual(analyseCss(css, 'wwwroot/css/x.css'), { literals: 2, gaps: 1, darkRules: 1 });
});

test('site.css token definitions in :root are not counted', () => {
  const css = ':root { --pm-card: #fff; } :root[data-bs-theme="dark"] { --pm-card: #16181b; } body { color: #0b1220; }';
  assert.equal(analyseCss(css, 'wwwroot/css/site.css').literals, 1);
  assert.equal(analyseCss(css, 'wwwroot/css/other.css').literals, 2);
});

test('no stylesheet or view has more literal colours than the baseline', () => {
  const { grown } = compare(measure(), readBaseline());
  assert.deepEqual(
    grown.map((g) => `${g.key}: ${g.allowed} -> ${g.now}`),
    [],
    'Use the site tokens (docs/ux/dark-theme-palette.md) instead of literal colours.'
  );
});

test('a dark rule inside a media query only covers that query', () => {
  const css = `
    .a { color: #111; }
    @media (max-width: 600px) { .a { color: #222; } }
    @media (max-width: 600px) { [data-bs-theme="dark"] .a { color: #eee; } }`;
  assert.deepEqual(analyseCss(css, 'wwwroot/css/x.css'), { literals: 2, gaps: 1, darkRules: 1 });
});

test('inline style literals are counted in both quote forms', () => {
  const { countInlineStyleLiterals } = require('./theme-coverage');
  assert.equal(countInlineStyleLiterals(`<div style="color: #fff"></div><span style='background: #000'></span><p data-style="#fff"></p>`), 2);
});

test('quoted strings are not counted as colours', () => {
  const { countLiterals } = require('./theme-coverage');
  assert.equal(countLiterals('"#000"'), 0);
  assert.equal(countLiterals("'#fff' #000"), 1);
});

test('named colours are counted case-insensitively', () => {
  const { countLiterals } = require('./theme-coverage');
  assert.equal(countLiterals('White'), 1);
  assert.equal(countLiterals('BLACK'), 1);
});
