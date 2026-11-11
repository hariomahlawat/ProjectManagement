const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const libraryScript = fs.readFileSync(path.resolve(__dirname, 'photos-library.js'), 'utf8');
const photosPage = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml'), 'utf8');
const pageModel = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml.cs'), 'utf8');

test('timeline tiles ask for the small picture and keep the larger thumbnail as a fallback', () => {
    assert.match(photosPage, /src="@\(item\.GridThumbnailUrl \?\? item\.ThumbnailUrl\)"/);
    assert.match(photosPage, /data-fallback-src="@\(item\.GridThumbnailUrl is null \? null : item\.ThumbnailUrl\)"/);
    assert.match(pageModel, /GridThumbnailSize = "sm"/);
});

test('a tile is marked unavailable only after the fallback picture has also failed', () => {
    assert.match(libraryScript, /dataset\.fallbackSrc/);
    assert.match(libraryScript, /fallbackTried/);
    const handler = libraryScript.slice(libraryScript.indexOf('const handleImageFailure'));
    assert.ok(handler.indexOf('image.src = fallback') < handler.indexOf('markUnavailable(image)'));
});

test('timeline tiles carry no cover or people badges', () => {
    assert.doesNotMatch(photosPage, /photos-tile__cover/);
    assert.doesNotMatch(photosPage, /photos-tile__people/);
});

test('one slim bar holds the four sections, search and controls', () => {
    assert.match(photosPage, /class="photos-bar" data-photos-bar/);
    const sections = photosPage.slice(photosPage.indexOf('<nav class="photos-sections"'), photosPage.indexOf('</nav>', photosPage.indexOf('<nav class="photos-sections"')));
    for (const label of ['>Photos<', '>Albums<', '>People<', '>Collections<']) assert.ok(sections.includes(label), label);
    assert.doesNotMatch(photosPage, /photos-primary-tabs|photos-collection-tabs|Unified organisational media/);
    assert.match(libraryScript, /--photos-bar-top/);
});

test('type and source are chips; the timeline has month and day headings', () => {
    assert.match(photosPage, /class="photos-chips"/);
    assert.match(photosPage, /Model\.BuildSourceUrl\(Model\.Source == sourceKey \? "all" : sourceKey\)/);
    assert.doesNotMatch(photosPage, /photos-segments|id="sourceFilter"|id="kindFilter"/);
    assert.match(photosPage, /<h2 class="photos-month" id="month-@monthKey">/);
    assert.match(photosPage, /IndexModel\.DaySourceSummary\(dayItems\)/);
    assert.match(pageModel, /targetSource is "all" or "projects" \? ProjectId : null/);
});
