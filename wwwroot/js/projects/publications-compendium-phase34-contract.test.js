const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const iconRoot = 'wwwroot/images/publications/compendium-icons';
const readIcon = name => read(`${iconRoot}/${name}.svg`);

const icons = {
  arms: readIcon('arms-services'),
  cost: readIcon('proliferation-cost'),
  filed: readIcon('ipr-filed'),
  granted: readIcon('ipr-granted'),
  mixed: readIcon('ipr-mixed'),
  transfer: readIcon('technology-transfer')
};

const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const planner = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const resolver = read('Services/Compendiums/CompendiumProgrammeInformation.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');

test('phase 34 uses one local, deterministic 24-pixel line-icon system', () => {
  for (const [name, svg] of Object.entries(icons)) {
    assert.match(svg, /<svg[^>]*width="24"[^>]*height="24"[^>]*viewBox="0 0 24 24"/,
      `${name} must use the shared 24-pixel canvas`);
    assert.doesNotMatch(svg, /<(?:image|filter|linearGradient|radialGradient)\b/,
      `${name} must remain a self-contained flat vector`);

    const strokeWidths = [...svg.matchAll(/stroke-width="([^"]+)"/g)].map(match => match[1]);
    assert.ok(strokeWidths.length > 0, `${name} must declare its stroke weight`);
    assert.deepEqual([...new Set(strokeWidths)], ['1.8'], `${name} must use the shared 1.8 stroke`);
  }
});

test('phase 34 preserves disciplined category colours without mixed clip-art accents', () => {
  assert.deepEqual([...new Set(icons.arms.match(/#[0-9A-F]{6}/gi))], ['#8B3A3A']);
  assert.deepEqual([...new Set(icons.cost.match(/#[0-9A-F]{6}/gi))], ['#27825B']);
  assert.deepEqual([...new Set(icons.transfer.match(/#[0-9A-F]{6}/gi))], ['#3275C7']);
  assert.deepEqual([...new Set(icons.filed.match(/#[0-9A-F]{6}/gi))], ['#A97712']);
  assert.deepEqual([...new Set(icons.mixed.match(/#[0-9A-F]{6}/gi))], ['#A97712']);
  assert.deepEqual([...new Set(icons.granted.match(/#[0-9A-F]{6}/gi))], ['#A97712', '#FFFFFF']);
  for (const svg of [icons.filed, icons.granted, icons.mixed]) {
    assert.doesNotMatch(svg, /#27825B/i, 'IPR state must not introduce an unrelated green badge');
  }
});

test('phase 34 differentiates filed, granted and mixed IPR within one document-seal family', () => {
  const sharedDocument = 'M6.25 2.75h7.15L18 7.35v11.4a2 2 0 0 1-2 2H6.25a2 2 0 0 1-2-2v-14a2 2 0 0 1 2-2Z';
  for (const svg of [icons.filed, icons.granted, icons.mixed]) {
    assert.ok(svg.includes(sharedDocument), 'all IPR states must share the same document silhouette');
    assert.match(svg, /cx="14\.65" cy="15\.25"/);
  }

  assert.match(icons.filed, /M14\.65 13\.7v1\.7l1\.1\.65/);
  assert.doesNotMatch(icons.filed, /fill="#A97712"/);
  assert.match(icons.granted, /fill="#A97712"/);
  assert.match(icons.granted, /stroke="#FFFFFF"/);
  assert.match(icons.mixed, /M14\.65 12\.05a3\.2 3\.2 0 0 0 0 6\.4v-6\.4Z/);
  assert.doesNotMatch(icons.mixed, /stroke="#FFFFFF"/);
});

test('phase 34 keeps browser proof and generated PDF programme furniture in parity', () => {
  assert.match(mainJs, /const\s+programmeIconVersion\s*=\s*"v1[56]"/);
  assert.match(mainJs, /compendium-icons\/\$\{key\}\.svg\?v=\$\{programmeIconVersion\}/);
  assert.match(mainJs, /compendium-live-page__programme-heading">PROJECT PARTICULARS/);
  assert.match(mainJs, /classList\.toggle\(\s*"is-compact-single"/s);

  assert.match(css, /\.compendium-live-page__programme\{[^}]*border-top:2px solid #205244/s);
  assert.match(css, /\.compendium-live-page__programme-heading\{[^}]*grid-column:1\/-1[^}]*letter-spacing:\.1em/s);
  assert.match(css, /\.compendium-live-page__programme-icon\{[^}]*width:22px[^}]*height:22px/s);
  assert.match(css, /\.compendium-live-page__programme-icon img\{[^}]*width:(?:16|18)px[^}]*height:(?:16|18)px/s);
  assert.match(css, /\.compendium-live-page__programme\.is-compact-single\{[^}]*grid-template-columns:minmax\(0,1fr\) minmax\(0,1fr\)/s);

  assert.match(builder, /private const float ProgrammeTopRuleHeight = 2\.25f;/);
  assert.match(builder, /Height\(ProgrammeTopRuleHeight\)\.Background\(Forest800\)/);
  assert.match(builder, /IsCompactSingleProgrammeModule\(modules\[0\]\)/);
  assert.match(builder, /if \(useHalfWidthSingleModule\)\s*\{\s*row\.RelativeItem\(\);/s);
  assert.match(planner, /moduleCount switch|return 30\.25f \+ rows \* 38f;/);
});

test('phase 34 keeps semantic keys stable across subsequent programme refinements', () => {
  for (const key of [
    'arms-services',
    'proliferation-cost',
    'ipr-filed',
    'ipr-granted',
    'ipr-mixed',
    'technology-transfer'
  ]) {
    assert.ok(resolver.includes(`"${key}"`), `resolver must retain ${key}`);
  }

  assert.match(readService, /(?:CompendiumPdf_2026-08-15_(?:programme-iconography-v15|programme-semantics-v16|programme-particulars-v17|final-composition-v18|composition-hardening-v19)|CompendiumPdf_2026-08-16_(?:physical-composition-v20|editorial-constraints-v21|final-editorial-v22))/);
  assert.match(fingerprint, /compendium-review-v(?:9-programme-iconography|10-sponsoring-line-directorate|11-balanced-text-flow|12-professional-typesetting|13-physical-measurement|14-editorial-constraints|15-additional-note-final-hardening)/);
});
