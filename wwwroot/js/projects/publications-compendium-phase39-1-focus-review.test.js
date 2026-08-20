const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

const css = read('wwwroot', 'css', 'pages', 'projects-publications.css');
const js = read('wwwroot', 'js', 'pages', 'projects-compendium.js');

const phase391Marker = 'Simulators Compendium - Phase 39.1 focus-review reliability';
const phase391Index = css.indexOf(phase391Marker);
const phase391Css = phase391Index >= 0 ? css.slice(phase391Index) : '';

test('phase 39.1 aligns Focus Review with the desktop breakpoint used by the output dock', () => {
  assert.ok(phase391Index >= 0, 'Phase 39.1 focus-review section must be present');
  assert.match(js, /window\.innerWidth\s*>=\s*1200/);
  assert.match(phase391Css, /@media \(min-width:\s*1200px\)/);
  assert.match(css, /@media \(max-width:\s*1199\.98px\)[\s\S]*\.compendium-review-focus-toggle\s*\{\s*display:\s*none;/);
});

test('phase 39.1 makes Focus Review genuinely proof-first at every supported desktop width', () => {
  assert.match(
    phase391Css,
    /\.compendium-builder-page\.is-review-focus \.compendium-builder-layout,[\s\S]*?grid-template-columns:\s*minmax\(0,\s*1fr\);/
  );
  assert.match(
    phase391Css,
    /\.compendium-builder-page\.is-review-focus \.compendium-builder-rail\s*\{\s*display:\s*none;/
  );
  assert.match(
    phase391Css,
    /\.compendium-builder-page\.is-review-focus \.compendium-review-workspace--live\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0,\s*1\.52fr\)\s*minmax\(310px,\s*\.72fr\);/
  );
});

test('phase 39.1 overrides the ordinary narrow-desktop stacked review only after it is declared', () => {
  const stackedIndex = css.indexOf('@media (max-width: 1549.98px)');
  assert.ok(stackedIndex >= 0, 'Ordinary narrow-desktop stacking rule must remain available');
  assert.ok(phase391Index > stackedIndex, 'Focus Review overrides must follow the ordinary stacked layout');
  assert.match(phase391Css, /\.compendium-review-inspector\s*\{[\s\S]*?max-height:\s*calc\(100dvh\s*-\s*230px\);[\s\S]*?overflow:\s*auto;/);
  assert.match(phase391Css, /\.compendium-live-page__viewport\s*\{[\s\S]*?max-height:\s*calc\(100dvh\s*-\s*230px\);/);
});

test('phase 39.1 reserves space for the fixed output dock while the canonical rail is hidden', () => {
  assert.match(
    phase391Css,
    /\.compendium-builder-page\.is-review-focus \.compendium-builder-main\s*\{[\s\S]*?padding-bottom:\s*6rem;/
  );
  assert.match(js, /if \(reviewFocusMode\) \{ setVisible\(true\); return; \}/);
});

test('phase 39.1 scales the proof progressively instead of requiring a 1600px viewport', () => {
  assert.match(phase391Css, /@media \(min-width:\s*1400px\)[\s\S]*?width:\s*min\(100%,\s*750px\)/);
  assert.match(phase391Css, /@media \(min-width:\s*1700px\)[\s\S]*?width:\s*min\(100%,\s*810px\)/);
  assert.match(phase391Css, /@media \(min-width:\s*1900px\)[\s\S]*?width:\s*min\(100%,\s*860px\)/);
  assert.doesNotMatch(css, /Review mode can deliberately reclaim rail width without hiding structure/);
});
