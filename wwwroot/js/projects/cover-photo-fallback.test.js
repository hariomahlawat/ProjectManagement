const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const fallbackSource = fs.readFileSync(
  path.resolve(__dirname, 'cover-photo-fallback.js'),
  'utf8');
const multiJdpSource = fs.readFileSync(
  path.resolve(__dirname, 'overview-multi-jdp.js'),
  'utf8');

test('cover image failure has one owner and captures the host before removing media', () => {
  assert.doesNotMatch(multiJdpSource, /data-project-cover-image/);
  assert.match(fallbackSource, /const host = image\.closest\('\[data-project-cover-host\]'\)/);
  assert.match(fallbackSource, /picture\?\.remove\(\)/);
  assert.ok(
    fallbackSource.indexOf('const host =') < fallbackSource.indexOf('picture?.remove()'),
    'The fallback host must be captured before the picture is removed.');
  assert.match(fallbackSource, /if \(!host \|\| !fallback\)/);
  assert.match(fallbackSource, /fallback\.classList\.remove\('d-none'\)/);
});

test('header cover thumbnail falls back to the icon tile when it cannot load', () => {
  const { JSDOM } = require('jsdom');
  const dom = new JSDOM(`<!DOCTYPE html><body>
    <a class="project-command-header__cover" data-project-cover-host>
      <img src="missing.jpg" alt="" data-project-cover-image />
      <span class="project-command-header__cover-fallback d-none" data-project-cover-fallback></span>
    </a></body>`, { runScripts: 'outside-only' });
  dom.window.eval(fallbackSource);

  const doc = dom.window.document;
  doc.querySelector('[data-project-cover-image]').dispatchEvent(new dom.window.Event('error'));

  assert.equal(doc.querySelector('[data-project-cover-image]'), null);
  assert.ok(!doc.querySelector('[data-project-cover-fallback]').classList.contains('d-none'));
});
