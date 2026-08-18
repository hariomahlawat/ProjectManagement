const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const libraryScript = fs.readFileSync(path.resolve(__dirname, 'photos-library.js'), 'utf8');
const curationScript = fs.readFileSync(path.resolve(__dirname, 'photos-curation.js'), 'utf8');
const photosPage = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml'), 'utf8');

test('target-album selection mode auto-starts and preserves a dedicated cancel destination', () => {
    assert.match(libraryScript, /selectionAutoStart/);
    assert.match(libraryScript, /selectionCancelUrl/);
    assert.match(libraryScript, /let selecting = autoStart/);
    assert.match(libraryScript, /targetedCuration/);
    assert.match(photosPage, /data-selection-auto-start/);
    assert.match(photosPage, /data-selection-cancel-url/);
    assert.match(photosPage, /Add selected/);
});

test('media already in the target album is explicitly excluded from selection', () => {
    assert.match(photosPage, /alreadyInTargetAlbum/);
    assert.match(photosPage, /photos-tile--already-in-album/);
    assert.match(photosPage, /disabled="@\(alreadyInTargetAlbum/);
    assert.match(libraryScript, /!tile\.disabled/);
});

test('album-specific behaviour is isolated from the core gallery script', () => {
    assert.doesNotMatch(libraryScript, /Organisation-wide album forms/);
    assert.doesNotMatch(libraryScript, /Album ordering is intentionally confined/);
    assert.match(curationScript, /Organisation-wide album forms/);
    assert.match(curationScript, /Album ordering is intentionally confined/);
});
