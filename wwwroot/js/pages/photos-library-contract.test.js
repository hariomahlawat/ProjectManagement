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
    // After a failed part the Next link continues from the part that failed.
    assert.match(photosPage, /data-photos-next-page/);
    const fallback = libraryScript.slice(libraryScript.indexOf('function showPaginationFallback'), libraryScript.indexOf('async function loadNextPart'));
    assert.match(fallback, /searchParams\.delete\('handler'\)/);
    assert.match(fallback, /nextLink\.setAttribute\('href'/);
    assert.match(fallback, /pagination\.hidden = false;/);
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

test('icon-only actions on phones keep their words for screen readers', () => {
    const styles = fs.readFileSync(path.resolve(__dirname, '../../css/pages/photos-library.css'), 'utf8');
    const rule = styles.match(/\.photos-action span \{([^}]*)\}/);
    assert.ok(rule, 'the phone rule for action labels exists');
    assert.doesNotMatch(rule[1], /display\s*:\s*none/, 'display:none would remove the button name');
    assert.match(rule[1], /clip\s*:\s*rect\(0, 0, 0, 0\)/);
});

test('phones: three pictures to a row, one row of search and icon controls, chips that scroll, a month picker', () => {
    const styles = fs.readFileSync(path.resolve(__dirname, '../../css/pages/photos-library.css'), 'utf8');
    assert.match(libraryScript, /\(max-width: 575\.98px\)'\)\.matches\) return 96;/, 'a row height that fits three pictures');
    assert.match(styles, /\.photos-tile \{ min-height:72px; \}/, 'and the stylesheet does not force them taller');
    assert.match(styles, /\.photos-bar \.photos-control span \{[^}]*clip:rect\(0, 0, 0, 0\)/, 'control words stay for screen readers');
    assert.match(styles, /\.photos-chips \{[^}]*flex-wrap:nowrap;[^}]*overflow-x:auto;/);
    assert.match(styles, /\.photos-sections a \{[^}]*min-height:2\.75rem;/, 'tabs are a full touch target');

    // The picker stands in for the month list wherever that list is not shown.
    assert.match(photosPage, /<select data-photos-month-jump>/);
    assert.match(photosPage, /<option value="@month\.Url" data-month="@month\.Key">/);
    assert.match(styles, /@media \(min-width: 1200px\) \{\s+\.photos-scrubber \{ display:flex; \}\s+\.photos-month-jump \{ display:none; \}/);
    assert.match(styles, /\.photos-month \{[^}]*scroll-margin-top:/, 'a month link does not land under the bar');
    assert.match(photosPage, /data-month="@month\.Key" aria-label="@month\.Label"/, 'month links are named in full');
});

test('the page shows pictures, not counts or the word media', () => {
    assert.doesNotMatch(photosPage, /photos-summary/, 'no line of counts above the pictures');
    // Two administration pages keep their names; everything else a user reads says pictures.
    const wording = photosPage.replace(/>Media (intelligence|sources)</g, '><');
    assert.doesNotMatch(wording, />[^<@]*\bmedia\b[^<]*</i, 'visible wording says pictures');
    assert.match(photosPage, /<h2>Nothing found<\/h2>/);
    assert.match(photosPage, /href="@Model\.BuildClearFiltersUrl\(\)">Clear search and filters<\/a>/);
    assert.match(photosPage, /data-photos-end hidden="@\(Model\.HasNextPage \? "hidden" : null\)"/);
    assert.match(libraryScript, /querySelector\('\[data-photos-end\]'\)\?\.removeAttribute\('hidden'\)/);
    // One way to start an album where the New album tile is shown.
    assert.match(photosPage, /Model\.IsAlbumsWorkspace && \(Model\.CurrentPage > 1 \|\| !string\.IsNullOrWhiteSpace\(Model\.Q\)\)/);
    // A project with one picture is a collection like any other; people with none are not suggested.
    assert.doesNotMatch(pageModel, /IncludeSingletonCollections \|\| group\.Rows\.Count > 1/);
    assert.doesNotMatch(photosPage, /name="IncludeSingletonCollections" value="true" checked/);
    assert.match(pageModel, /People\.Where\(person => person\.PhotoCount > 0\)\.Select/);
    assert.match(libraryScript, /setRow\('\[data-info-classification-row\]', Boolean\(kind\) && kind !== 'Not classified'\)/);
});
