const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const libraryScript = fs.readFileSync(path.resolve(__dirname, 'photos-library.js'), 'utf8');
// The page and the timeline partial it renders (also returned on its own while scrolling).
const photosPage = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml'), 'utf8')
    + fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/_PhotosTimeline.cshtml'), 'utf8');
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

test('the timeline continues while scrolling and keeps the page links as a fallback', () => {
    assert.match(photosPage, /data-photos-timeline/);
    assert.match(photosPage, /data-next-part-url="@\(!Model\.IsAlbumDetail && Model\.HasNextPage \? Model\.BuildTimelinePartUrl\(Model\.CurrentPage \+ 1\) : null\)"/);
    assert.match(photosPage, /<partial name="_PhotosTimeline" model="Model" \/>/);
    assert.match(photosPage, /data-photos-pagination/);
    assert.match(pageModel, /OnGetTimelinePartAsync/);
    assert.match(pageModel, /await OnGetAsync\(cancellationToken\);/);
    assert.match(libraryScript, /X-Photos-Next-Part/);
    assert.match(libraryScript, /if \(pagination\) pagination\.hidden = false;/);
    assert.match(libraryScript, /photos:tiles-changed/);
});

test('the month list scrolls to a loaded month and otherwise follows its link', () => {
    assert.match(photosPage, /data-photos-scrubber/);
    assert.match(photosPage, /href="@month\.Url" data-month="@month\.Key"/);
    const scrubber = libraryScript.slice(libraryScript.indexOf("querySelector('[data-photos-scrubber]')"));
    assert.match(scrubber, /if \(!heading\) return;\s+event\.preventDefault\(\);/);
});

test('a tick on a picture or beside a day starts selecting; the top bar holds the actions', () => {
    assert.match(photosPage, /data-day-select/);
    assert.match(photosPage, /data-selection-cancel /);
    // The action bar is rendered inside the slim bar, not after the timeline.
    const bar = photosPage.slice(photosPage.indexOf('class="photos-bar" data-photos-bar'), photosPage.indexOf('class="photos-subbar"'));
    assert.match(bar, /data-photos-selection-bar/);
    assert.match(libraryScript, /if \(!selecting && \(!onTick \|\| !isSelectable\(tile\)\)\) return;/);
    assert.match(libraryScript, /selected\.size === 0 && !startedByButton && !targetedCuration/);
});

test('albums start with a New album tile and collections are grouped by where they were added', () => {
    assert.match(photosPage, /class="collection-card album-card album-card--new" data-bs-toggle="modal" data-bs-target="#createAlbumModal"/);
    assert.match(photosPage, /Model\.Collections\.GroupBy\(collection => collection\.TypeLabel\)/);
    assert.match(photosPage, /IndexModel\.CollectionGroupTitle\(collectionGroup\.Key\)/);
    assert.doesNotMatch(photosPage, /Organisation-wide media albums/);
});
