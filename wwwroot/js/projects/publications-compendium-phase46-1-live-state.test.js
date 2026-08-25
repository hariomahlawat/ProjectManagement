const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const mainJs = fs.readFileSync(path.join(root, 'wwwroot/js/pages/projects-compendium.js'), 'utf8');

const block = name => {
  const pattern = new RegExp(`const ${name} = value => \\{[\\s\\S]*?\\n    \\};`);
  const match = mainJs.match(pattern);
  assert.ok(match, `${name} handler must exist`);
  return match[0];
};

test('phase 46.1 dossier-default handlers complete the live render pipeline without calling an undefined renderer', () => {
  for (const name of ['changeDefaultDossierLayout', 'changeDefaultTextFlow', 'changeDefaultImageFit']) {
    const source = block(name);
    assert.doesNotMatch(source, /\brenderComposer\s*\(/, `${name} must not call the removed renderComposer path`);
    assert.match(source, /\brenderOrder\s*\(\s*\)/, `${name} must repaint publication controls and structure immediately`);
    assert.match(source, /\brefreshReviewProgress\s*\(\s*\)/, `${name} must refresh review progress`);
    assert.match(source, /\bupdateReviewNavigation\s*\(\s*\)/, `${name} must refresh review navigation state`);
    assert.match(source, /\bschedulePreflight\s*\(\s*\)/, `${name} must schedule publication preflight`);
  }
});

test('phase 46.1 dossier-default button rendering keeps visual and accessibility state synchronized', () => {
  const render = mainJs.match(/const renderEditorialControls = \(\) => \{[\s\S]*?\n    \};/);
  assert.ok(render, 'renderEditorialControls must exist');
  const source = render[0];

  for (const collection of [
    'narrativeAlignmentButtons',
    'defaultDossierLayoutButtons',
    'defaultTextFlowButtons',
    'defaultImageFitButtons'
  ]) {
    const collectionBlock = source.match(new RegExp(`${collection}\\.forEach\\(button => \\{[\\s\\S]*?\\n        \\}\\);`));
    assert.ok(collectionBlock, `${collection} must render through a state-aware block`);
    assert.match(collectionBlock[0], /classList\.toggle\("active",\s*active\)/, `${collection} must update the visible active state`);
    assert.match(collectionBlock[0], /setAttribute\("aria-pressed",\s*active \? "true" : "false"\)/, `${collection} must update aria-pressed with the same state`);
  }
});

test('phase 46.1 leaves no renderComposer reference in the Compendium workspace', () => {
  assert.doesNotMatch(mainJs, /\brenderComposer\b/);
});
