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

const pocketSizes = Object.keys(core.SIZES).filter((key) => core.SIZES[key].group === 'pocket');
for (const key of Object.keys(designs.DESIGNS)) {
  test(`${key}: at every size it is offered for, every part is on the page and no words overlap`, () => {
    for (const size of designs.DESIGNS[key].pocket ? pocketSizes : cardSizes) {
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
      if (logo) for (const words of texts) assert.ok(!overlap(box(logo), box(words)), `${key} ${size}: the logo covers ${words.field}`);
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
  assert.deepEqual([card.card.design, card.card.details], ['classic', { ...designs.DEFAULT_DETAILS }]);
  assert.ok(card.card.placed.includes('occasion') && !card.card.placed.includes('guest'), 'the fields the design placed');
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

test('a field deleted on the page stays empty when the design changes; a duplicate is the user\'s own', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  const note = card.items.find((item) => item.field === 'note');
  const without = { ...card, items: card.items.filter((item) => item !== note) };
  assert.equal(designs.detailsOf(without).note, '');
  const navy = designs.applyDesign(without, 'navy', designs.detailsOf(without), { measure, newId });
  assert.ok(!navy.items.some((item) => item.field === 'note'), 'the deleted note does not come back');
  // A field this design does not show keeps its words for a design that does.
  assert.equal(designs.detailsOf({ ...card, card: { ...card.card, details: { ...card.card.details, hostHi: 'x' } } }).hostHi, 'x');
  // A copy of a linked item shows no field, so editing the field changes one item only.
  const copy = core.duplicateItem(note, 'n2');
  assert.equal(copy.field, undefined);
  assert.equal(copy.role, undefined);
  assert.equal(copy.text, note.text);
  assert.deepEqual(designs.fieldsOf('classic', 'a5l').includes('at'), false);
  assert.deepEqual(designs.fieldsOf('classic', 'a5p').includes('at'), true);
});

test('a field the design never placed keeps its words when the page is turned', () => {
  // Classic landscape has no place for "at the"; turned to portrait without a new design, the
  // words are still the card's, not taken as deleted.
  const wide = designs.applyDesign(core.newCollage('a5l'), 'classic', { at: 'at the' }, { measure, newId });
  assert.ok(!wide.items.some((item) => item.field === 'at'));
  assert.ok(!wide.card.placed.includes('at'));
  const turned = core.changeSize(wide, 'a5p');
  assert.equal(designs.detailsOf(turned).at, 'at the');
  const portrait = designs.applyDesign(turned, 'classic', designs.detailsOf(turned), { measure, newId });
  assert.equal(portrait.items.find((item) => item.field === 'at').text, 'at the');
  // A draft keeps the placed fields.
  const read = core.readDraftModel(JSON.parse(JSON.stringify(portrait)), []);
  assert.ok(read.card.placed.includes('at'));
  assert.deepEqual(core.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {}, placed: ['at', '<x>', 7] } }, []).card.placed, ['at']);
});

test('a draft keeps the Hindi wording and the unit colours of a card', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'bilingual', { occasionHi: 'विदाई समारोह', colours: '#1f5b3a #c9a54a #111111' }, { measure, newId });
  const read = core.readDraftModel(JSON.parse(JSON.stringify(card)), []);
  assert.equal(read.card.details.occasionHi, 'विदाई समारोह');
  assert.equal(read.card.details.colours, '#1f5b3a #c9a54a #111111');
});

test('a photo in the Ceremonial frame moves to the next design\'s frame, or stays as the user\'s own', () => {
  const ceremonial = designs.applyDesign(core.newCollage('a5p'), 'ceremonial', {}, { measure, newId });
  const slot = ceremonial.items.find((item) => item.role === 'photo');
  assert.equal(slot.kind, 'slot');
  // The user fills the frame.
  const filled = { ...ceremonial, items: ceremonial.items.map((item) => (item === slot ? { ...core.fillSlot(item, 'p1'), crop: { zoom: 1.5, x: 0.4, y: 0.5 } } : item)) };
  // Classic has no frame: the photo stays, now the user's own.
  const classic = designs.applyDesign(filled, 'classic', designs.detailsOf(filled), { measure, newId });
  const kept = classic.items.find((item) => item.kind === 'photo');
  assert.equal(kept.photo, 'p1');
  assert.equal(kept.role, undefined);
  // Ceremonial again from a filled frame: the photo goes into the new frame, crop and all.
  const again = designs.applyDesign(filled, 'ceremonial', designs.detailsOf(filled), { measure, newId });
  const moved = again.items.find((item) => item.role === 'photo');
  assert.deepEqual([moved.kind, moved.photo, moved.crop.zoom], ['photo', 'p1', 1.5]);
  assert.equal(again.items.filter((item) => item.kind === 'photo').length, 1);
  // A frame emptied stays the design's.
  assert.equal(core.emptyFrame(moved).role, 'photo');
});

test('the Regimental band takes the unit\'s colours, and the parts that follow them say which', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'regimental', { colours: '#1f5b3a #C9A54A bad' }, { measure, newId });
  const band = (role) => card.items.find((item) => item.role === role);
  assert.equal(band('band1').fill, '#1f5b3a');
  assert.equal(band('band2').fill, '#c9a54a');
  assert.equal(band('band3').fill, '#1d2c55', 'a bad colour falls back');
  assert.deepEqual(band('band1').tint, { fill: 1 });
  const host = card.items.find((item) => item.field === 'host');
  assert.deepEqual([host.color, host.tint], ['#1f5b3a', { color: 1 }]);
  assert.equal(designs.usesColours('regimental'), true);
  assert.equal(designs.usesColours('classic'), false);
  assert.ok(designs.fieldsOf('bilingual').includes('occasionHi'));
  assert.ok(!designs.fieldsOf('classic').includes('occasionHi'));
});

test('a card keeps its logo when the design changes; a removed logo stays removed; a second logo stays put', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  // The user's own logo in place of the SDD one, and a second logo beside it.
  const own = card.items.map((item) => (item.role === 'logo' ? { ...core.photoItem(item.id, 'p9', 1.2, item), role: 'logo' } : item));
  const second = { ...core.stickerItem('l2', 'unit:5', { x: 800, y: 150, width: 100, aspect: 1 }), role: 'logo2' };
  const withOwn = { ...card, items: [...own, second] };
  const navy = designs.applyDesign(withOwn, 'navy', designs.detailsOf(withOwn), { measure, newId });
  const logo = navy.items.find((item) => item.role === 'logo');
  assert.deepEqual([logo.kind, logo.photo, logo.aspect], ['photo', 'p9', 1.2]);
  const logo2 = navy.items.find((item) => item.role === 'logo2');
  assert.deepEqual([logo2.sticker, logo2.x], ['unit:5', 800], 'the second logo stays where the user put it');
  // Removed: no logo comes back with the next design.
  const without = { ...card, items: card.items.filter((item) => item.role !== 'logo') };
  const regimental = designs.applyDesign(without, 'regimental', designs.detailsOf(without), { measure, newId });
  assert.ok(!regimental.items.some((item) => item.role === 'logo'));
  // Where the design would put a logo, for putting one back: the SDD logo's place.
  const place = designs.roleItem(without, 'logo', { measure, newId });
  assert.deepEqual([place.kind, place.sticker], ['sticker', 'logo:sdd']);
});

// Front and back (docs/ux/invitations-posters.md, PR 8).
test('a card gets a back in its design, turns over, keeps both sides through a design change, and loses it', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  const two = designs.addBack(card, { measure, newId });
  assert.equal(two.side, undefined, 'the front still shows');
  assert.deepEqual(two.items, card.items, 'the front is untouched');
  const backField = (c, field) => [...c.items, ...(c.back?.items || [])].find((item) => item.field === field);
  assert.equal(backField(two, 'programmeTitle').text, 'Programme');
  assert.match(backField(two, 'programme').text, /Guests arrive/);
  assert.ok(two.back.items.some((item) => item.role === 'backphoto' && item.kind === 'slot'));
  assert.equal(two.back.items.find((item) => item.role === 'backlogo').sticker, 'logo:sdd', 'the back shows the front\'s logo');
  // Turned over: the back is the side shown and edited; turned again, as it was.
  const over = core.flipSide(two);
  assert.equal(over.side, 'back');
  assert.ok(over.items.some((item) => item.field === 'programme'));
  assert.deepEqual(core.flipSide(over).items, two.items);
  // Pages for Save: front first, whichever side shows.
  assert.equal(core.pagesOf(over)[0].items.some((item) => item.field === 'occasion'), true);
  assert.equal(core.pagesOf(over)[1].items.some((item) => item.field === 'programme'), true);
  // Words on both sides count; a design change while the back shows rebuilds both and keeps the back showing.
  const edited = { ...over, items: over.items.map((item) => (item.field === 'programme' ? { ...item, text: '1900 h · Arrival' } : item)) };
  assert.equal(designs.detailsOf(edited).programme, '1900 h · Arrival');
  assert.equal(designs.detailsOf(edited).occasion, 'Raising Day Dinner');
  const navy = designs.applyDesign(edited, 'navy', designs.detailsOf(edited), { measure, newId });
  assert.equal(navy.side, 'back');
  assert.deepEqual(navy.background, { type: 'gradient', key: 'navy' }, 'the navy back');
  assert.equal(navy.items.find((item) => item.field === 'programme').text, '1900 h · Arrival');
  assert.deepEqual(navy.back.background, { type: 'gradient', key: 'navy' }, 'the navy front, now behind');
  assert.equal(navy.back.items.find((item) => item.field === 'occasion').text, 'Raising Day Dinner');
  // A draft keeps both sides and the side showing.
  const read = core.readDraftModel(JSON.parse(JSON.stringify(navy)), []);
  assert.equal(read.side, 'back');
  assert.equal(read.back.items.length, navy.back.items.length);
  // Deleted, the card shows its front alone.
  const single = designs.deleteBack(navy);
  assert.equal(single.back, undefined);
  assert.equal(single.side, undefined);
  assert.ok(single.items.some((item) => item.field === 'occasion'));
  // A back field filled again comes back on the back.
  assert.equal(designs.fieldItem(two, 'backNote', { ...designs.detailsOf(two), backNote: 'Gate 2' }, { measure, newId }).text, 'Gate 2');
  assert.equal(designs.fieldItem(card, 'backNote', { backNote: 'x' }, { measure, newId }), null, 'no back, no back field');
});

test('every design\'s back stays on the page with no overlapping words at every card size', () => {
  for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].pocket)) {
    for (const size of cardSizes) {
      const card = designs.addBack(designs.applyDesign(core.newCollage(size), key, {}, { measure, newId }), { measure, newId });
      const height = core.pageHeight(size);
      for (const item of card.back.items) {
        const b = box(item);
        assert.ok(b.left >= -0.5 && b.right <= core.PAGE_WIDTH + 0.5 && b.top >= -0.5 && b.bottom <= height + 0.5, `${key} ${size}: back ${item.role} off the page`);
      }
      const texts = card.back.items.filter((item) => item.kind === 'text');
      for (let i = 0; i < texts.length; i += 1) for (let j = i + 1; j < texts.length; j += 1) {
        assert.ok(!overlap(box(texts[i]), box(texts[j])), `${key} ${size}: back ${texts[i].field} overlaps ${texts[j].field}`);
      }
    }
  }
});

// The back keeps the parts a newer PRISM made through a design change (PR 8).
test('a design change keeps the back\'s parts from a newer PRISM, through a save', () => {
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }), { measure, newId });
  const raw = JSON.parse(JSON.stringify(core.draftModel(card)));
  raw.back.items.push({ id: 'h', kind: 'hologram', x: 1, y: 1 });
  const read = core.readDraftModel(raw, []);
  assert.equal(core.keptCount(read), 1);
  const changed = designs.applyDesign(read, 'navy', designs.detailsOf(read), { measure, newId });
  const again = core.readDraftModel(JSON.parse(JSON.stringify(core.draftModel(changed))), []);
  assert.equal(core.keptCount(again), 1, 'the hologram is still there');
});

// Deleting the back keeps its words as they are on the card, however they were typed (PR 8).
test('deleting the back keeps the programme as it was edited, for a new back', () => {
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }), { measure, newId });
  // Edited in the item itself, not in Card details.
  const edited = { ...card, back: { ...card.back, items: card.back.items.map((item) => (item.field === 'programme' ? { ...item, text: '1900 h · Arrival' } : item)) } };
  const single = designs.deleteBack(edited);
  const again = designs.addBack(single, { measure, newId });
  assert.equal(again.back.items.find((item) => item.field === 'programme').text, '1900 h · Arrival');
});

// Pocket cards (PR 9b).
test('pocket sizes are offered the pocket designs, and cards the others', () => {
  assert.deepEqual(designs.designsFor('passl').map((d) => d.key), ['pass', 'reply', 'programmecard', 'pocketinvite']);
  assert.ok(designs.designsFor('a5p').every((d) => !d.pocket));
  assert.ok(designs.designsFor('mm:120x180').every((d) => !d.pocket));
  const pass = designs.applyDesign(core.newCollage('passl'), 'pass', {}, { measure, newId });
  assert.equal(pass.items.find((item) => item.field === 'passTitle').text, 'CAR PASS');
  assert.ok(pass.items.some((item) => item.role === 'vehicleline'));
  const reply = designs.applyDesign(core.newCollage('in355p'), 'reply', {}, { measure, newId });
  assert.equal(reply.items.find((item) => item.field === 'attend').text, 'will / will not attend');
  // The pocket cards share the card's wording: the occasion typed once is on every one.
  const details = { occasion: 'Barakhana' };
  for (const key of ['pass', 'reply', 'programmecard', 'pocketinvite']) {
    const card = designs.applyDesign(core.newCollage('a7p'), key, details, { measure, newId });
    assert.equal(card.items.find((item) => item.field === 'occasion').text, 'Barakhana', key);
  }
});

// A programme card shows its programme on the front: its field is found there (PR 9b).
test('the programme and title of a programme card are front fields, in both arrangements', () => {
  for (const size of ['a7p', 'a7l']) {
    const card = designs.applyDesign(core.newCollage(size), 'programmecard', {}, { measure, newId });
    assert.equal(designs.isBackField(card, 'programme'), false, size);
    assert.equal(designs.isBackField(card, 'programmeTitle'), false, size);
    const made = designs.fieldItem(card, 'programme', { ...designs.detailsOf(card), programme: '1900 h Arrival' }, { measure, newId });
    assert.equal(made.text, '1900 h Arrival', size);
  }
  const classic = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  assert.equal(designs.isBackField(classic, 'programme'), true);
});

test('a programme card with a back has its programme once; a logo is kept through the RSVP card', () => {
  const card = designs.addBack(designs.applyDesign(core.newCollage('a7p'), 'programmecard', {}, { measure, newId }), { measure, newId });
  const onFront = card.items.filter((item) => item.kind === 'text').map((item) => item.field);
  assert.ok(!card.back.items.some((item) => item.kind === 'text' && onFront.includes(item.field)), 'no field on both sides');
  const edited = { ...card, items: card.items.map((item) => (item.field === 'programme' ? { ...item, text: '1900 h Arrival' } : item)) };
  assert.equal(designs.detailsOf(edited).programme, '1900 h Arrival');
  // A logo of the user's own, through the RSVP card and back to the pass.
  const pass = designs.applyDesign(core.newCollage('a7p'), 'pass', {}, { measure, newId });
  const own = { ...pass, items: pass.items.map((item) => (item.role === 'logo' ? { ...item, kind: 'photo', photo: 'p1', sticker: undefined } : item)) };
  const reply = designs.applyDesign(own, 'reply', designs.detailsOf(own), { measure, newId });
  const back = designs.applyDesign(reply, 'pass', designs.detailsOf(reply), { measure, newId });
  assert.equal(back.items.find((item) => item.role === 'logo').photo, 'p1');
});
