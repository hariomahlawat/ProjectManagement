const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, 'photos-person-linkage.js'), 'utf8');

test('link confirmation requires explicit visual verification', () => {
    assert.match(source, /data-prism-link-verified/);
    assert.match(source, /submit\.disabled = !verified\.checked \|\| !userId\.value/);
    assert.match(source, /show\.bs\.modal/);
});

test('link modal is populated from the reviewed candidate instead of inferred data', () => {
    assert.match(source, /data-prism-link-candidate/);
    assert.match(source, /trigger\.dataset\.userId/);
    assert.match(source, /trigger\.dataset\.userName/);
    assert.doesNotMatch(source, /fetch\s*\(/);
});
