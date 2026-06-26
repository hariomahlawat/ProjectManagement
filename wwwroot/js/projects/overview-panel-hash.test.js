const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.resolve(__dirname, 'overview.js'), 'utf8');

test('timeline and remarks panel state is synchronized with the URL hash', () => {
  assert.match(source, /hash === '#remarks'/);
  assert.match(source, /desiredHash = target === 'remarks' \? '#remarks' : '#timeline'/);
  assert.match(source, /window\.history\.replaceState/);
  assert.match(source, /window\.addEventListener\('hashchange'/);
});
