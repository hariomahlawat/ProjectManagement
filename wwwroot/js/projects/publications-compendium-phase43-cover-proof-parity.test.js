const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

function ruleBodies(css, selectorNeedle) {
  const bodies = [];
  const rulePattern = /([^{}]+)\{([^{}]*)\}/g;
  let match;
  while ((match = rulePattern.exec(css)) !== null) {
    if (match[1].includes(selectorNeedle)) bodies.push(match[2]);
  }
  return bodies;
}

test('Institutional Hero remains a flow-based 491x300 proof frame', () => {
  const css = read('wwwroot', 'css', 'pages', 'projects-publications.css');
  const selector = '[data-template="InstitutionalHero"] .cover-proof-image--institutional';
  const bodies = ruleBodies(css, selector);
  assert.ok(bodies.length > 0, 'Institutional Hero image rule must exist.');

  const effective = bodies.at(-1);
  assert.match(effective, /position:\s*relative\s*;/);
  assert.match(effective, /width:\s*100%\s*;/);
  assert.match(effective, /height:\s*300px\s*;/);
  assert.match(effective, /margin:\s*22px\s+0\s+0\s*;/);
  assert.match(effective, /flex:\s*0\s+0\s+300px\s*;/);
});

test('pattern stacking never removes Institutional Hero from document flow', () => {
  const css = read('wwwroot', 'css', 'pages', 'projects-publications.css');
  const marker = '/* Cover pattern stacking:';
  const start = css.indexOf(marker);
  const end = css.indexOf('/* Phase 37.7 browser/PDF parity details. */', start);
  assert.ok(start >= 0 && end > start, 'Cover pattern stacking block must remain discoverable.');

  const stacking = css.slice(start, end);
  assert.doesNotMatch(stacking, /InstitutionalHero/);
  assert.match(stacking, /FullBleedHero/);
  assert.match(stacking, /ImageEcho/);
  assert.match(stacking, /position:\s*absolute/);
});

test('Institutional Hero content owns QuestPDF-equivalent spacing', () => {
  const css = read('wwwroot', 'css', 'pages', 'projects-publications.css');
  const selector = '[data-template="InstitutionalHero"] .compendium-cover-proof-content';
  const bodies = ruleBodies(css, selector);
  assert.ok(bodies.length > 0, 'Institutional Hero content rule must exist.');

  const effective = bodies.at(-1);
  assert.match(effective, /padding:\s*150px\s+52px\s+48px\s*;/);
  assert.match(effective, /display:\s*flex\s*;/);
  assert.match(effective, /flex-direction:\s*column\s*;/);
  assert.match(effective, /gap:\s*12px\s*;/);
});

test('QuestPDF identity rule is emitted before cover wording like the browser proof', () => {
  const builder = read('Utilities', 'Reporting', 'CompendiumPdfReportBuilder.cs');
  const start = builder.indexOf('private static void ComposeCoverIdentity');
  const end = builder.indexOf('private static void ComposeCoverTile', start);
  assert.ok(start >= 0 && end > start, 'ComposeCoverIdentity method must remain discoverable.');

  const method = builder.slice(start, end);
  const ruleIndex = method.indexOf('Width(128).Height(3).Background(Gold)');
  const eyebrowIndex = method.indexOf('eyebrow!.ToUpperInvariant()');
  const titleIndex = method.indexOf('Text(title!)');
  assert.ok(ruleIndex >= 0, 'Gold identity rule must be rendered.');
  assert.ok(eyebrowIndex >= 0 && titleIndex >= 0, 'Identity wording must be rendered.');
  assert.ok(ruleIndex < eyebrowIndex && ruleIndex < titleIndex,
    'Gold rule must precede eyebrow/title to match the browser proof.');
});

test('build identity identifies the parity-corrected PDF contract', () => {
  const identity = read('Utilities', 'Reporting', 'CompendiumBuildIdentity.cs');
  assert.match(identity, /Phase = "43"/);
  assert.match(identity, /CompendiumPdf_2026-08-23_phase43-cover-proof-parity/);
  assert.match(identity, /physical-a4-v43/);
});
