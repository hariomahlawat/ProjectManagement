// Card designs as data (docs/ux/invitations-posters.md, PR 4b).
const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('./collage-core.js');
const designs = require('./collage-designs.js');

// Letters about as wide as Playfair's and Mulish's on average: a little over half the size.
const measure = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) * 0.56;
let sequence = 0;
const newId = () => `d${++sequence}`;
const cardSizes = Object.keys(core.SIZES).filter((key) => core.SIZES[key].group === 'card');

/** An item's box on the page, turned items included. */
function box(item) {
  const half = core.turnedHalfBox(item.width, core.itemHeight(item), item.angle || 0);
  return { left: item.x - half.halfWidth, right: item.x + half.halfWidth, top: item.y - half.halfHeight, bottom: item.y + half.halfHeight };
}
const overlap = (a, b) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1;

for (const key of Object.keys(designs.DESIGNS)) {
  test(`${key}: at every card size every part is on the page and no words overlap`, () => {
    for (const size of cardSizes) {
      const card = designs.applyDesign(core.newCollage(size), key, { guest: 'Brig P. Singh, SM and Mrs Singh' }, { measure, newId });
      const height = core.pageHeight(size);
      for (const item of card.items) {
        const b = box(item);
        assert.ok(b.left >= -0.5 && b.right <= core.PAGE_WIDTH + 0.5 && b.top >= -0.5 && b.bottom <= height + 0.5, `${key} ${size}: ${item.role} off the page ${JSON.stringify(b)}`);
      }
      const texts = card.items.filter((item) => item.kind === 'text');
      for (let i = 0; i < texts.length; i += 1) {
        for (let j = i + 1; j < texts.length; j += 1) {
          assert.ok(!overlap(box(texts[i]), box(texts[j])), `${key} ${size}: ${texts[i].field} overlaps ${texts[j].field}`);
        }
      }
      // The logo never sits on words.
      const logo = card.items.find((item) => item.role === 'logo');
      for (const words of texts) assert.ok(!overlap(box(logo), box(words)), `${key} ${size}: the logo covers ${words.field}`);
    }
  });
}

test('a design makes ordinary items: the SDD logo, borders, and words linked to their fields', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  assert.equal(card.size, 'a5p', 'the page size never changes');
  assert.deepEqual(card.background, { type: 'color', value: '#fbf7ee' });
  assert.equal(card.layout, null);
  const logo = card.items.find((item) => item.role === 'logo');
  assert.deepEqual([logo.kind, logo.sticker], ['sticker', 'logo:sdd']);
  assert.equal(card.items.filter((item) => item.kind === 'shape' && item.form === 'rect').length, 2);
  const occasion = card.items.find((item) => item.field === 'occasion');
  assert.equal(occasion.text, 'Raising Day Dinner');
  assert.ok(occasion.maxWidth > 0 && occasion.maxLines === 2);
  // No guest's name yet: a line to write it on, no words.
  assert.ok(card.items.some((item) => item.role === 'guestline'));
  assert.ok(!card.items.some((item) => item.field === 'guest'));
  assert.deepEqual(card.card, { design: 'classic', details: { ...designs.DEFAULT_DETAILS } });
  // Every item is one a draft reads back as it is.
  const read = core.readDraftModel(JSON.parse(JSON.stringify(card)), []);
  assert.equal(read.items.length, card.items.length);
  assert.equal(core.keptCount(read), 0);
  assert.deepEqual(read.card, card.card);
});

test('changing the design keeps the wording, typed on the card or given, and the user\'s own items', () => {
  const first = designs.applyDesign(core.newCollage('a5p'), 'classic', { occasion: 'Change of Command' }, { measure, newId });
  // The user types on the card, and adds a photo of their own.
  const typed = {
    ...first,
    items: [
      ...first.items.map((item) => (item.field === 'venue' ? { ...item, text: 'Sudarshan Hall' } : item)),
      core.photoItem('mine', 'p1', 1.5, { x: 500, y: 900, width: 300 })
    ]
  };
  const details = designs.detailsOf(typed);
  assert.equal(details.occasion, 'Change of Command');
  assert.equal(details.venue, 'Sudarshan Hall');
  const second = designs.applyDesign(typed, 'navy', details, { measure, newId });
  assert.equal(second.items.find((item) => item.field === 'occasion').text, 'Change of Command');
  assert.equal(second.items.find((item) => item.field === 'venue').text, 'Sudarshan Hall');
  assert.deepEqual(second.background, { type: 'gradient', key: 'navy' });
  // The old design's parts are gone, the photo stays, in front.
  assert.equal(second.items.filter((item) => item.role === 'logo').length, 1);
  assert.equal(second.items.at(-1).id, 'mine');
  assert.equal(second.card.design, 'navy');
});

test('a field left empty has no item; a long one wraps and shrinks to its width', () => {
  const card = designs.applyDesign(core.newCollage('a6p'), 'classic', { note: '', at: '  ', occasion: 'Annual Regimental Dinner Night with Ladies and Distinguished Guests' }, { measure, newId });
  assert.ok(!card.items.some((item) => item.field === 'note' || item.field === 'at'));
  const occasion = card.items.find((item) => item.field === 'occasion');
  assert.ok(occasion.width <= occasion.maxWidth + 0.5);
  const layout = core.layoutText(occasion, measure);
  assert.ok(layout.lines <= 2);
});

test('portrait and square pages take the portrait arrangement; landscape ones the landscape', () => {
  assert.equal(designs.isPortrait('a5p'), true);
  assert.equal(designs.isPortrait('sq5'), true);
  assert.equal(designs.isPortrait('a5l'), false);
  assert.equal(designs.isPortrait('dll'), false);
  const wide = designs.applyDesign(core.newCollage('a5l'), 'classic', {}, { measure, newId });
  const logo = wide.items.find((item) => item.role === 'logo');
  assert.ok(logo.x < core.PAGE_WIDTH / 3, 'the logo stands on the left of a landscape card');
});

test('an unknown design changes nothing; a draft drops a bad card', () => {
  const plain = core.newCollage('a5p');
  assert.equal(designs.applyDesign(plain, 'nope', {}, { measure, newId }), plain);
  const read = core.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: '<b>', details: { occasion: 'x' } } }, []);
  assert.equal(read.card, undefined);
  const odd = core.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: { occasion: 7, '<x>': 'y', venue: 'v'.repeat(400) } } }, []);
  assert.deepEqual(Object.keys(odd.card.details), ['venue']);
  assert.equal(odd.card.details.venue.length, core.TEXT_LIMITS.maxLength);
});

test('the card scale is 1 on A5, follows the area on tall cards, and never outgrows a narrow page', () => {
  assert.ok(Math.abs(designs.unitOf('a5p') - 1) < 0.01);
  assert.ok(Math.abs(designs.unitOf('a5l') - core.pageHeight('a5l') / 1000) < 0.01, 'landscape A5 follows its height');
  assert.ok(designs.unitOf('dlp') > 1.15, 'a DL card gets larger words than its width alone gives');
  for (const size of cardSizes) assert.ok(designs.unitOf(size) <= Math.min(core.PAGE_WIDTH, core.pageHeight(size)) * 1.3 / 1000 + 1e-9, size);
});

test('a field filled again gets the item its design has for it', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', { note: '' }, { measure, newId });
  assert.ok(!card.items.some((item) => item.field === 'note'));
  const note = designs.fieldItem(card, 'note', { ...card.card.details, note: 'Please be seated by 1915 h' }, { measure, newId });
  assert.equal(note.text, 'Please be seated by 1915 h');
  assert.equal(note.role, 'note');
  assert.equal(designs.fieldItem(card, 'note', { note: '' }, { measure, newId }), null);
  assert.equal(designs.fieldItem(core.newCollage('a5p'), 'note', { note: 'x' }, { measure, newId }), null, 'not a card');
});
