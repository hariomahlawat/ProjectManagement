const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const details = read('Areas/ProjectOfficeReports/Pages/Visits/Details.cshtml');
const photos = read('Areas/ProjectOfficeReports/Pages/Visits/_PhotosPartial.cshtml');
const script = read('wwwroot/js/pages/project-office-reports/visits.js');
const cssPath = path.join(root, 'wwwroot/css/project-office-reports/visits-details.css');

const compact = value => value.replace(/\s+/g, ' ');

test('visit details owns an in-page photo viewer and gallery jump target', () => {
  assert.match(details, /id="visit-photo-gallery"/);
  assert.match(details, /href="#visit-photo-gallery"/);
  assert.match(details, /data-visit-photo-viewer/);
  assert.match(details, /data-visit-photo-viewer-image/);
  assert.match(details, /data-visit-photo-viewer-prev/);
  assert.match(details, /data-visit-photo-viewer-next/);
  assert.match(details, /data-visit-photo-viewer-counter/);
  assert.match(details, /data-visit-photo-viewer-caption/);
});

test('gallery items carry progressive-enhancement metadata and no read-only empty-caption noise', () => {
  assert.match(photos, /data-visit-gallery-item/);
  assert.match(photos, /data-photo-url=/);
  assert.match(photos, /data-photo-index=/);
  assert.match(photos, /data-photo-cover=/);
  assert.match(photos, /data-photo-caption=/);
  assert.doesNotMatch(photos, /Open full-size photo in new tab/);
  assert.doesNotMatch(photos, /target="_blank"/);
  assert.match(compact(photos), /else if \(Model\.CanManage\)[\s\S]*No caption provided/);
});

test('viewer JavaScript supports modal navigation, keyboard controls, focus restore and adjacent preloading', () => {
  assert.match(script, /function initVisitPhotoViewer\(/);
  assert.match(script, /bootstrap\.Modal\.getOrCreateInstance/);
  assert.match(script, /ArrowLeft/);
  assert.match(script, /ArrowRight/);
  assert.match(script, /preloadAdjacent/);
  assert.match(script, /touchstart/);
  assert.match(script, /touchend/);
  assert.match(script, /lastTrigger/);
  assert.match(script, /\.focus\(/);
  assert.match(script, /data-visit-photo-viewer-stage/);
  assert.match(script, /initVisitPhotoViewer\(\)/);
});

test('visit details loads isolated viewer styling with a dark presentation stage', () => {
  assert.ok(fs.existsSync(cssPath), 'visits-details.css should exist');
  const css = fs.readFileSync(cssPath, 'utf8');
  assert.match(details, /~\/css\/project-office-reports\/visits-details\.css/);
  assert.match(css, /\.visit-photo-viewer__stage/);
  assert.match(css, /\.visit-photo-viewer__image/);
  assert.match(css, /\.visit-photo-viewer__nav/);
  assert.match(css, /scroll-margin-top/);
  assert.match(css, /prefers-reduced-motion/);
});
