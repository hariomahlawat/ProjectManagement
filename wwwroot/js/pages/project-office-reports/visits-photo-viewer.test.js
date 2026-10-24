const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const details = read('Areas/ProjectOfficeReports/Pages/Visits/Details.cshtml');
const edit = read('Areas/ProjectOfficeReports/Pages/Visits/Edit.cshtml');
const photos = read('Areas/ProjectOfficeReports/Pages/Visits/_PhotosPartial.cshtml');
const items = read('Areas/ProjectOfficeReports/Pages/Visits/VisitMediaViewerItems.cs');
const script = read('wwwroot/js/pages/project-office-reports/visits.js');
const css = read('wwwroot/css/project-office-reports/visits-details.css');

const compact = value => value.replace(/\s+/g, ' ');

test('visit photos open the shared media viewer, with links to one photo', () => {
  for (const page of [details, edit]) {
    assert.match(page, /<partial name="_MediaViewer" \/>/);
    assert.match(page, /data-media-viewer-items="@VisitMediaViewerItems\.Group" data-media-viewer-links="#media="/);
    assert.match(page, /~\/css\/widgets\/media-viewer\.css/);
    assert.match(page, /~\/js\/widgets\/media-viewer\.js/);
  }

  // The Visits-only viewer is gone: one viewer for Photos, projects and Visits.
  assert.equal(fs.existsSync(path.join(root, 'Areas/ProjectOfficeReports/Pages/Visits/_PhotoViewer.cshtml')), false);
  assert.doesNotMatch(script, /initVisitPhotoViewer|data-visit-photo-viewer/);
  assert.doesNotMatch(css, /\.visit-photo-viewer/);
});

test('gallery thumbnails and the cover open the viewer at their photo; without JavaScript they stay links', () => {
  assert.match(photos, /<a href="@versionedFull"/);
  assert.match(photos, /data-media-viewer-open="@VisitMediaViewerItems\.Group"/);
  assert.match(photos, /data-media-viewer-key="@VisitMediaViewerItems\.KeyFor\(photo\.Id\)"/);
  assert.doesNotMatch(photos, /target="_blank"/);
  assert.match(compact(photos), /else if \(Model\.CanManage\)[\s\S]*No caption provided/);

  assert.match(details, /data-visit-cover data-media-viewer-open="@VisitMediaViewerItems\.Group" data-media-viewer-key="@VisitMediaViewerItems\.KeyFor\(Model\.CoverPhoto!\.Id\)"/);
  assert.match(details, /id="visit-photo-gallery"/);
  assert.match(details, /href="#visit-photo-gallery"[^>]*class="visit-cover__all"/);
  assert.match(css, /#visit-photo-gallery[\s\S]*scroll-margin-top/);
  assert.match(css, /\.visit-cover\.is-missing/);
});

test('viewer items give the visit as context, the date added, and the original and a download', () => {
  assert.match(items, /public const string Group = "visit-photos";/);
  assert.match(items, /size = "xl"/);
  assert.match(items, /size = "original", v = version/);
  assert.match(items, /size = "original", download = true/);
  assert.match(items, /\$"\{visit\.VisitorName\} · \{visit\.DateOfVisit/);
  assert.match(items, /"Added "/);
  assert.match(items, /isCover \? "Cover photo"/);
});

test('visit pages only ask ViewPhoto for the sizes it serves', () => {
  const viewPhoto = read('Areas/ProjectOfficeReports/Pages/Visits/ViewPhoto.cshtml.cs');
  const served = new Set(['xs', 'sm', 'md', 'xl', 'original']);
  assert.match(viewPhoto, /"xs" or "sm" or "md" or "xl" or "original"/);

  const sources = ['Index.cshtml', 'Details.cshtml', '_PhotosPartial.cshtml', 'VisitMediaViewerItems.cs']
    .map(name => read(`Areas/ProjectOfficeReports/Pages/Visits/${name}`))
    .join('\n');
  const requested = [...sources.matchAll(/ViewPhoto"[^)]*size = "([a-z]+)"/g)].map(match => match[1]);
  assert.ok(requested.length >= 7);
  for (const size of requested) {
    assert.ok(served.has(size), `ViewPhoto does not serve size "${size}"`);
  }
});
