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
