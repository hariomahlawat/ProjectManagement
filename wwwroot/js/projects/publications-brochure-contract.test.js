const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = process.cwd();
const view = fs.readFileSync(path.join(root, 'Pages', 'Projects', 'Publications', 'Brochure', 'Index.cshtml'), 'utf8');
const css = fs.readFileSync(path.join(root, 'wwwroot', 'css', 'pages', 'projects-publications.css'), 'utf8');
const js = fs.readFileSync(path.join(root, 'wwwroot', 'js', 'pages', 'projects-brochure.js'), 'utf8');

const criticalClasses = [
  'brochure-filter-toolbar',
  'brochure-search-field',
  'brochure-project-table-wrap',
  'brochure-project-table',
  'brochure-copy-status',
  'brochure-photo-summary',
  'brochure-selection-panel',
  'brochure-preflight-grid',
  'brochure-preflight-issues',
  'brochure-photo-choice',
  'brochure-focal-stage',
  'brochure-generate-actions'
];

test('brochure critical Razor classes have explicit stylesheet coverage', () => {
  const markupOrClient = `${view}\n${js}`;
  for (const className of criticalClasses) {
    assert.match(markupOrClient, new RegExp(`\\b${className}\\b`), `markup/client should use ${className}`);
    assert.match(css, new RegExp(`\\.${className}(?:[^a-zA-Z0-9_-]|$)`), `css should style ${className}`);
  }
});

test('brochure uses the publication-photo handler instead of fixed project derivatives', () => {
  assert.match(view, /"Photo"/);
  assert.match(view, /mode = "thumb"/);
  assert.match(view, /mode = "source"/);
  assert.doesNotMatch(view, /\/Projects\/Photos\/View/);
});

test('brochure selection workspace has bounded filtering and readiness controls', () => {
  assert.match(view, /data-brochure-filter="readiness"/);
  assert.match(view, /data-brochure-selected-only/);
  assert.match(view, /data-brochure-match-count/);
  assert.match(css, /max-height:\s*min\(58vh,\s*620px\)/);
  assert.match(css, /position:\s*sticky;\s*top:\s*0;/);
});

test('brochure client renders all preflight findings on demand and offers actions', () => {
  assert.match(js, /data-preflight-show-all/);
  assert.match(js, /Show all \$\{ordered\.length\} findings/);
  assert.match(js, /Open project brief/);
  assert.match(js, /Configure image/);
  assert.doesNotMatch(js, /slice\(0,\s*8\)/);
});

test('brochure Gallery 2 remains an explicit second-image editorial choice', () => {
  assert.match(js, /does not auto-pick a second image/);
  assert.doesNotMatch(js, /find\(photo => Number\(photo\.photoId\).*secondaryPhotoId/);
});
