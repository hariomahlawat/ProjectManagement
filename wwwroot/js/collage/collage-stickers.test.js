const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const stickers = require('./collage-stickers.js');
const collage = require('./collage-core.js');

const twemojiDir = path.join(__dirname, '..', '..', 'img', 'collage', 'stickers', 'twemoji');

test('every emoji sticker ships with PRISM, with its licence beside it', () => {
  const ids = stickers.EMOJI_GROUPS.flatMap((group) => group.items.map((item) => item.id));
  assert.ok(ids.length > 100);
  assert.equal(new Set(ids).size, ids.length, 'no emoji twice');
  for (const id of ids) {
    assert.ok(fs.existsSync(path.join(twemojiDir, `${id.slice(3)}.svg`)), `${id} must ship`);
  }
  const licence = fs.readFileSync(path.join(twemojiDir, 'LICENSE-GRAPHICS.txt'), 'utf8');
  assert.match(licence, /CC BY 4\.0/);
  assert.match(licence, /Twitter, Inc\.\s+and\s+other\s+contributors/);
  // The drawings load nothing from elsewhere.
  for (const file of fs.readdirSync(twemojiDir).filter((name) => name.endsWith('.svg'))) {
    const svg = fs.readFileSync(path.join(twemojiDir, file), 'utf8');
    assert.doesNotMatch(svg, /href|<script|url\(/i, file);
  }
});

test('an emoji is named by its code points, without the emoji style mark', () => {
  assert.equal(stickers.codeOf('🎉'), '1f389');
  assert.equal(stickers.codeOf('❤️'), '2764');
  assert.equal(stickers.codeOf('🇮🇳'), '1f1ee-1f1f3');
});

test('drawn stickers take any colour and only a real one', () => {
  for (const key of Object.keys(stickers.DRAWN)) {
    const svg = stickers.drawnSvg(key, '#123456');
    assert.match(svg, /^<svg xmlns="http:\/\/www\.w3\.org\/2000\/svg"/, key);
    assert.ok(svg.includes('#123456'), key);
    assert.ok(!svg.includes('COLOUR'), key);
  }
  // A colour that is not a #rrggbb colour falls back to the sticker's own.
  assert.ok(stickers.drawnSvg('heart', 'red"/><script>').includes(stickers.DRAWN.heart.colour));
  assert.ok(!stickers.drawnSvg('heart', 'red"/><script>').includes('<script>'));
});

test('stickers are found by their words, in catalogue order', () => {
  assert.deepEqual(stickers.search('').length, 0);
  const cake = stickers.search('cake').map((item) => item.id);
  assert.deepEqual(cake, ['tw:1f382', 'tw:1f370']);
  assert.ok(stickers.search('thank').some((item) => item.id === 'draw:thanks'));
  assert.ok(stickers.search('diwali lamp').some((item) => item.id === 'tw:1fa94'));
  assert.equal(stickers.search('zzz').length, 0);
});

test('the catalogue knows each sticker and nothing else', () => {
  assert.equal(stickers.find('draw:ribbon').kind, 'drawn');
  assert.equal(stickers.find('tw:1f389').kind, 'emoji');
  assert.equal(stickers.find('tw:1f389').code, '1f389');
  assert.equal(stickers.find('tw:ffff'), null);
  assert.equal(stickers.find('draw:nope'), null);
  assert.equal(stickers.find(42), null);
  const groups = stickers.groups();
  assert.equal(groups[0].key, 'crests');
  assert.equal(groups[1].key, 'shapes');
  assert.equal(groups[1].items.length, Object.keys(stickers.DRAWN).length);
});

test('a draft keeps stickers, within their limits', () => {
  const sticker = collage.stickerItem('s', 'draw:heart', { x: 10, y: 10, width: 9999, aspect: 1.1 }, { colour: '#c62f3a', flip: true });
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [
    sticker,
    { ...sticker, id: 'bad', sticker: 'javascript:alert(1)' },
    { ...sticker, id: 'odd', colour: 'red', shadow: 'yes' }
  ] }, []);
  assert.deepEqual(read.items.map((i) => i.id), ['s', 'odd']);
  assert.deepEqual([read.items[0].width, read.items[0].colour, read.items[0].flip], [700, '#c62f3a', true]);
  assert.deepEqual([read.items[1].colour, read.items[1].shadow], ['', false]);
});

test('a sticker at its smallest size comes back from a draft at that size', () => {
  const smallest = collage.STICKER_LIMITS.width[0];
  const sticker = collage.stickerItem('s', 'tw:1f389', { x: 10, y: 10, width: smallest, aspect: 1 });
  const read = collage.readDraftModel({ version: 1, size: 'a4l', items: [sticker, { ...sticker, id: 't', width: 5 }] }, []);
  assert.deepEqual(read.items.map((i) => i.width), [smallest, smallest]);
});

test('crests and logos come first, take only PRISM picture addresses, and are found by name', () => {
  assert.equal(stickers.groups()[0].key, 'crests');
  const count = stickers.setUnit([
    { id: 3, name: 'Signals crest', words: 'corps', width: 200, height: 100, url: '/api/collage/stickers/3/image?v=2' },
    { id: 4, name: 'Elsewhere', width: 10, height: 10, url: 'https://example.com/logo.png' },
    { id: 5, name: 'Wrong id', width: 10, height: 10, url: '/api/collage/stickers/6/image?v=1' },
    { id: 7, name: '', width: 10, height: 10, url: '/api/collage/stickers/7/image?v=1' },
    { id: 3, name: 'Twice', width: 10, height: 10, url: '/api/collage/stickers/3/image?v=1' }
  ]);
  assert.equal(count, 1);
  assert.deepEqual(stickers.groups()[0].items.map((item) => item.id), ['unit:3']);
  const found = stickers.find('unit:3');
  assert.equal(found.kind, 'unit');
  assert.equal(found.label, 'Signals crest');
  assert.equal(found.aspect, 2);
  assert.deepEqual(stickers.search('signals').map((item) => item.id), ['unit:3']);
  assert.deepEqual(stickers.search('corps crest').map((item) => item.id), ['unit:3']);
});

test('a crest that is no longer listed still draws from its PRISM address', () => {
  stickers.setUnit([]);
  const retired = stickers.find('unit:12');
  assert.equal(retired.kind, 'unit');
  assert.equal(retired.url, '/api/collage/stickers/12/image');
  assert.equal(stickers.find('unit:0'), null);
  assert.equal(stickers.find('unit:abc'), null);
});

const library = require('./collage-library.js');

test('PRISM Photos is asked only for words, a project, a year and a page', () => {
  assert.equal(library.libraryQuery({ q: '  farewell party ', projectId: '12', year: '2026', page: 3 }), 'q=farewell+party&projectId=12&year=2026&page=3');
  assert.equal(library.libraryQuery({ q: '', projectId: '../x', year: '26', page: -1 }), 'page=1');
  assert.equal(library.libraryQuery({ q: 'x'.repeat(300) }).length, 'q='.length + 200 + '&page=1'.length);
});

const VISIT = '0f8fad5b-d9cb-469f-a165-70867728950e';
const PHOTO = '7c9e6679-7425-40de-944b-e07fc1f90ae7';

test('only a listed photo original from its own kind of page is ever fetched', async () => {
  // The addresses CollageApiHostTests checks the server gives, one per kind of photo.
  const originals = [
    '/Projects/12/Photos/41/View/xl?v=3',
    '/Projects/12/Photos/41/View/xl',
    `/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=original&v=a1b2`,
    `/ProjectOfficeReports/SocialMedia/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=original&v=c3d4`,
    `/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=original`,
    '/Photos/Media/7/original?v=3'
  ];
  for (const address of originals) assert.ok(library.LIBRARY_PICTURE.test(address), address);

  const refused = [
    '/Photos/Media/7/thumb?v=3',
    '/Projects/12/Photos/41/View/md?v=3',
    `/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=md&v=a1b2`,
    'https://example.com/Photos/Media/7/original?v=3',
    '//example.com/Photos/Media/7/original?v=3',
    '//example.com/Projects/12/Photos/41/View/original?v=3',
    'https://example.com/Projects/12/Photos/41/View/original?v=3',
    '/Photos/Media/7/original?v=3&x=1',
    // A project photo's page has no "original" size.
    '/Projects/12/Photos/41/View/original?v=3',
    '/Projects/12/Photos/41/View/original?v=3&x=1',
    '/Projects/12/Photos/41/View/original?x=1',
    `/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=original&v=a1b2&x=1`,
    `/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?v=a1b2&size=original`,
    `/ProjectOfficeReports/Other/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=original`,
    '/Projects/12/Photos/41/View/original/../../../../Admin?v=3',
    '/Projects/0/Photos/41/View/original?v=3',
    '/Photos/Media/7/original?v=3#x',
    '\\\\example.com/Photos/Media/7/original?v=3',
    ''
  ];
  for (const address of refused) assert.ok(!library.LIBRARY_PICTURE.test(address), address);
  await assert.rejects(library.readLibraryPhoto({ url: '//example.com/a.jpg' }));
  await assert.rejects(library.readLibraryPhoto({ url: '/Projects/12/Photos/41/View/original?v=3&x=1' }));
});

test('a thumbnail is shown only from its own kind of page', () => {
  assert.ok(library.LIBRARY_THUMB.test('/Projects/12/Photos/41/View/md?v=3'));
  assert.ok(library.LIBRARY_THUMB.test(`/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=md&v=a1b2`));
  assert.ok(library.LIBRARY_THUMB.test(`/ProjectOfficeReports/SocialMedia/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=feed&v=c3d4`));
  assert.ok(library.LIBRARY_THUMB.test('/Photos/Media/7/thumb?v=3'));
  assert.ok(!library.LIBRARY_THUMB.test('/Photos/Media/7/original?v=3'));
  assert.ok(!library.LIBRARY_THUMB.test('//example.com/Photos/Media/7/thumb?v=3'));
  assert.ok(!library.LIBRARY_THUMB.test('/Photos/Media/7/thumb?v=3&x=1'));
});

test('a listed photo with an address from elsewhere is dropped from the page', async (t) => {
  const real = globalThis.fetch;
  t.after(() => { globalThis.fetch = real; });
  globalThis.fetch = async () => ({
    ok: true,
    json: async () => ({
      available: true,
      photos: [
        { id: 1, url: '/Projects/12/Photos/41/View/xl?v=3', thumbUrl: '/Projects/12/Photos/41/View/md?v=3' },
        { id: 2, url: `/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=original&v=a1`, thumbUrl: `/ProjectOfficeReports/Visits/ViewPhoto/${VISIT}/Photos/${PHOTO}?size=md&v=a1` },
        { id: 3, url: 'https://example.com/a.jpg', thumbUrl: '/Photos/Media/3/thumb?v=1' },
        { id: 4, url: '/Photos/Media/4/original?v=1', thumbUrl: '//example.com/t.jpg' }
      ]
    })
  });
  const page = await library.loadLibraryPage({});
  assert.deepEqual(page.photos.map((photo) => photo.id), [1, 2]);
});

test('a library photo is named by its title with the ending of its type', () => {
  assert.equal(library.fileNameFor('Farewell: Col Rao', 'image/jpeg'), 'Farewell  Col Rao.jpg');
  assert.equal(library.fileNameFor('IMG_0042.JPG', 'image/jpeg'), 'IMG_0042.JPG');
  assert.equal(library.fileNameFor('', 'image/png'), 'PRISM photo.png');
});

test('a library photo larger than a collage takes is refused before its body is read', async (t) => {
  const real = globalThis.fetch;
  t.after(() => { globalThis.fetch = real; });
  let bodyRead = false;
  let cancelled = false;
  globalThis.fetch = async () => ({
    ok: true,
    headers: new Map([['Content-Length', String(60 * 1024 * 1024)]]),
    body: { cancel: async () => { cancelled = true; } },
    blob: async () => { bodyRead = true; return new Blob(['x']); }
  });
  await assert.rejects(
    library.readLibraryPhoto({ url: '/Photos/Media/7/original?v=3', title: 'Big' }, { maxBytes: 50 * 1024 * 1024 }),
    (error) => error.reason === 'too-large');
  assert.equal(bodyRead, false);
  assert.equal(cancelled, true);

  // No size given by the server: the body is read, then checked.
  globalThis.fetch = async () => ({ ok: true, headers: new Map(), blob: async () => new Blob([new Uint8Array(10)], { type: 'image/jpeg' }) });
  await assert.rejects(library.readLibraryPhoto({ url: '/Photos/Media/7/original?v=3' }, { maxBytes: 5 }), (error) => error.reason === 'too-large');
  const file = await library.readLibraryPhoto({ url: '/Photos/Media/7/original?v=3', title: 'Small' }, { maxBytes: 50 });
  assert.equal(file.name, 'Small.jpg');
});
