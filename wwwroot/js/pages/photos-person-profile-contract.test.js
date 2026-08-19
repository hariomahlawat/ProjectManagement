const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const script = fs.readFileSync(path.resolve(__dirname, 'photos-person-profile.js'), 'utf8');
const page = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml'), 'utf8');

test('single-person profile exposes person-centric discovery without automatic confirmation', () => {
    assert.match(page, /Find more photos/);
    assert.match(page, /Possible photos of/);
    assert.match(page, /Yes, @personProfile\.DisplayName/);
    assert.match(page, /Not @personProfile\.DisplayName/);
    assert.doesNotMatch(page, /checked[^>]*data-person-candidate-select/);
});

test('candidate actions are progressive and preserve human selection', () => {
    assert.match(script, /data-person-candidate-select/);
    assert.match(page, /Confirm selected as @personProfile\.DisplayName/);
    assert.match(script, /window\.confirm/);
    assert.match(script, /X-Requested-With/);
});

test('single-person context no longer uses the redundant people-chip row', () => {
    assert.match(page, /@if \(Model\.IsMultiPersonGallery\)/);
    assert.match(page, /showPeopleFilterChip/);
    assert.match(page, /photos-control--select/);
});
