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
  assert.equal(groups[0].key, 'shapes');
  assert.equal(groups[0].items.length, Object.keys(stickers.DRAWN).length);
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
