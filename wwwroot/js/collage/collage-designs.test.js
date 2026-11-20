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
for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].poster)) {
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
  const odd = core.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: { occasion: 7, '<x>': 'y', venue: 'v'.repeat(1200) } } }, []);
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
  assert.deepEqual(designs.fieldsOf('reply', 'a7l').includes('replyCount'), false);
  assert.deepEqual(designs.fieldsOf('reply', 'a7p').includes('replyCount'), true);
});

test('a field the design never placed keeps its words when the page is turned', () => {
  // The RSVP card landscape has no place for the number attending; turned to portrait without
  // a new design, its words are still the card's, not taken as deleted.
  const wide = designs.applyDesign(core.newCollage('a7l'), 'reply', { replyCount: 'Number attending' }, { measure, newId });
  assert.ok(!wide.items.some((item) => item.field === 'replyCount'));
  assert.ok(!wide.card.placed.includes('replyCount'));
  const turned = core.changeSize(wide, 'a7p');
  assert.equal(designs.detailsOf(turned).replyCount, 'Number attending');
  const portrait = designs.applyDesign(turned, 'reply', designs.detailsOf(turned), { measure, newId });
  assert.equal(portrait.items.find((item) => item.field === 'replyCount').text, 'Number attending');
  // A draft keeps the placed fields.
  const read = core.readDraftModel(JSON.parse(JSON.stringify(portrait)), []);
  assert.ok(read.card.placed.includes('replyCount'));
  assert.deepEqual(core.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'ceremonial', details: {}, placed: ['at', '<x>', 7] } }, []).card.placed, ['at']);
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

test('a card keeps its logo when the design changes; a removed logo stays removed; a second logo goes beside it', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  // The user's own logo in place of the SDD one, and a second logo beside it.
  const own = card.items.map((item) => (item.role === 'logo' ? { ...core.photoItem(item.id, 'p9', 1.2, item), role: 'logo' } : item));
  const second = { ...core.stickerItem('l2', 'unit:5', { x: 800, y: 150, width: 100, aspect: 1 }), role: 'logo2' };
  const withOwn = { ...card, items: [...own, second] };
  const navy = designs.applyDesign(withOwn, 'navy', designs.detailsOf(withOwn), { measure, newId });
  const logo = navy.items.find((item) => item.role === 'logo');
  assert.deepEqual([logo.kind, logo.photo, logo.aspect], ['photo', 'p9', 1.2]);
  const logo2 = navy.items.find((item) => item.role === 'logo2');
  const navyLogo = navy.items.find((item) => item.role === 'logo');
  assert.equal(logo2.sticker, 'unit:5', 'the second logo is kept');
  assert.ok(Math.abs(logo2.y - navyLogo.y) < 0.5, 'and placed beside the logo of the design');
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
  // No empty photo frame on the back: one saved empty left a hole (cold review 6).
  assert.ok(!two.back.items.some((item) => item.kind === 'slot'));
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
  const read = core.readDraftModel(JSON.parse(JSON.stringify(core.draftModel(navy))), []);
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

// A long programme (code review 9, H1): twelve lines, some too long for one line.
const LONG_PROGRAMME = Array.from({ length: 12 }, (_, i) => (i % 3 === 1 ? `${1800 + i * 15} h · Change of Command Parade, Main Parade Ground, with the band` : `${1800 + i * 15} h · Item ${i + 1}`)).join(String.fromCharCode(10));

test('every design\'s back stays on the page with no overlapping words at every card size', () => {
  for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].pocket && !designs.DESIGNS[each].poster)) {
    for (const size of cardSizes) for (const programme of [undefined, LONG_PROGRAMME]) {
      const card = designs.addBack(designs.applyDesign(core.newCollage(size), key, programme ? { programme } : {}, { measure, newId }), { measure, newId });
      const height = core.pageHeight(size);
      // Every word inside the back's border, with a margin, and the programme centred on the page.
      const frame = box(card.back.items.find((item) => item.role === 'backborder'));
      for (const item of card.back.items) {
        const b = box(item);
        assert.ok(b.left >= -0.5 && b.right <= core.PAGE_WIDTH + 0.5 && b.top >= -0.5 && b.bottom <= height + 0.5, `${key} ${size}: back ${item.role} off the page`);
        if (item.kind === 'text') assert.ok(b.left > frame.left + 8 && b.right < frame.right - 8 && b.top > frame.top + 8 && b.bottom < frame.bottom - 8, `${key} ${size}: back ${item.field} crosses the border ${JSON.stringify(b)}`);
      }
      const block = card.back.items.find((item) => item.field === 'programme');
      assert.ok(Math.abs(block.x - core.PAGE_WIDTH / 2) < 1, `${key} ${size}: the programme is centred`);
      if (programme) assert.ok(core.layoutText(block, measure).width <= block.maxWidth + 0.5, `${key} ${size}: the long programme fits its width`);
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
  // A visiting card is too small for a whole pocket invitation; A7 has all four.
  assert.deepEqual(designs.designsFor('passl').map((d) => d.key), ['pass', 'reply', 'programmecard']);
  assert.deepEqual(designs.designsFor('a7p').map((d) => d.key), ['pass', 'reply', 'programmecard', 'pocketinvite']);
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

test('the car pass offers no unit colours; deleting the back of a programme card keeps the programme on its front tracked', () => {
  assert.equal(designs.usesColours('pass'), false);
  assert.equal(designs.usesColours('regimental'), true);
  const card = designs.addBack(designs.applyDesign(core.newCollage('a7p'), 'programmecard', {}, { measure, newId }), { measure, newId });
  const single = designs.deleteBack(card);
  assert.ok(single.card.placed.includes('programme'), 'still tracked');
  // The front's programme deleted on the page stays deleted.
  const without = { ...single, items: single.items.filter((item) => item.field !== 'programme') };
  assert.equal(designs.detailsOf(without).programme, '');
});

// Posters (PR 11).
test('posters: no logo until one is chosen, the heading moves up with No logo, every part on the page', () => {
  assert.deepEqual(designs.designsFor('a3p', 'poster').map((d) => d.key), ['event', 'notice', 'photoposter', 'talk']);
  assert.ok(designs.designsFor('a3p').every((d) => !d.poster), 'posters are not offered as cards');
  for (const key of ['event', 'notice', 'photoposter', 'talk']) {
    for (const size of ['a3p', 'a4p', 'a3l', 'a4l']) {
      const poster = designs.applyDesign(core.newCollage(size), key, designs.POSTER_DETAILS, { measure, newId });
      const place = poster.items.find((item) => item.role === 'logo');
      assert.equal(place.kind, 'slot', `${key}: an empty logo place, left out of Save`);
      const height = core.pageHeight(size);
      for (const item of poster.items) {
        const b = box(item);
        assert.ok(b.left >= -0.5 - core.PAGE_WIDTH * 0.12 && b.right <= core.PAGE_WIDTH * 1.12 + 0.5 && b.top >= -0.5 && b.bottom <= height + 0.5, `${key} ${size}: ${item.role} off the page`);
      }
      const texts = poster.items.filter((item) => item.kind === 'text');
      for (let i = 0; i < texts.length; i += 1) for (let j = i + 1; j < texts.length; j += 1) {
        assert.ok(!overlap(box(texts[i]), box(texts[j])), `${key} ${size}: ${texts[i].field} overlaps ${texts[j].field}`);
      }
      // The logo's place is clear of the words.
      for (const text of texts) assert.ok(!overlap(box(place), box(text)), `${key} ${size}: the logo place overlaps ${text.field}`);
      const none = designs.applyDesign(core.newCollage(size), key, { ...designs.POSTER_DETAILS, logoMode: 'none' }, { measure, newId });
      assert.ok(!none.items.some((item) => item.role === 'logo'), `${key}: no logo`);
      // The heading moves up into the logo's space where the design says so, and never down.
      const moves = [...designs.DESIGNS[key].portrait, ...(designs.DESIGNS[key].landscape || [])].some((part) => part.field === 'title' && part.yNoLogo !== undefined);
      const [after, before] = [none, poster].map((each) => each.items.find((item) => item.field === 'title').y);
      assert.ok(moves ? after < before - 1 : after <= before, `${key}: the heading ${moves ? 'moves up' : 'does not move down'}`);
      // A logo put back is placed where the design wants one, even after "No logo".
      assert.ok(designs.roleItem(none, 'logo', { measure, newId }));
    }
  }
  assert.equal(designs.POSTER_DETAILS.venue, 'Unit Sports Ground');
});

// Codex on PR 11: a poster's logo choice moves only the parts the logo moves.
test('a poster logo choice keeps the user fonts, colours, background and places; the heading moves', () => {
  const opts = { measure, newId };
  const poster = designs.applyDesign(core.newCollage('a4p'), 'notice', designs.POSTER_DETAILS, opts);
  const mine = {
    ...poster,
    background: { type: 'color', value: '#123456' },
    items: poster.items
      .filter((item) => item.role !== 'rule')
      .map((item) => (item.field === 'title' ? { ...item, font: 'typewriter', color: '#00ff00', x: item.x + 40 } : item))
  };
  const title = (c) => c.items.find((item) => item.field === 'title');
  const none = designs.setLogoMode(mine, 'none', opts);
  assert.equal(none.card.details.logoMode, 'none');
  assert.equal(none.items.some((item) => item.role === 'logo'), false);
  assert.deepEqual(none.background, mine.background);
  assert.equal(title(none).font, 'typewriter');
  assert.equal(title(none).color, '#00ff00');
  assert.equal(title(none).x, title(mine).x, 'across stays where the user put it');
  assert.ok(title(none).y < title(mine).y, 'the heading moves up into the space');
  assert.equal(none.items.length, mine.items.length - 1, 'only the logo place went; nothing deleted comes back');
  // Decide later brings the place back and the heading back down, still in the user's font.
  const later = designs.setLogoMode(none, 'later', opts);
  assert.ok(later.items.some((item) => item.role === 'logo' && item.kind === 'slot'));
  assert.ok(Math.abs(title(later).y - title(mine).y) < 1e-6);
  assert.equal(title(later).font, 'typewriter');
  // A locked heading stays put.
  const locked = { ...mine, items: mine.items.map((item) => (item.field === 'title' ? { ...item, locked: true } : item)) };
  assert.equal(title(designs.setLogoMode(locked, 'none', opts)).y, title(locked).y);
});

test('a photo added without a chosen frame never goes in a logo place', () => {
  const logo = { ...core.slotItem('l', { x: 100, y: 100, width: 100, aspect: 0.6 }), role: 'logo' };
  const frame = { ...core.slotItem('p', { x: 500, y: 500, width: 300, aspect: 1.5 }), role: 'photo' };
  assert.equal(core.bestSlotFor([logo, frame], 0.6).id, 'p');
  assert.equal(core.bestSlotFor([logo], 0.6), null);
  assert.equal(core.isLogoPlace(logo), true);
  assert.equal(core.isLogoPlace(frame), false);
  // The back's small logo place too (Codex on PR 11).
  const backLogo = { ...core.slotItem('b', { x: 100, y: 900, width: 60, aspect: 0.6 }), role: 'backlogo' };
  const backPhoto = { ...core.slotItem('q', { x: 500, y: 400, width: 300, aspect: 1.5 }), role: 'backphoto' };
  assert.equal(core.bestSlotFor([backLogo, backPhoto], 0.6).id, 'q');
});

// Folded cards (PR 9c).
test('a folded card has an outside and an inside in every design, each panel within its half', () => {
  for (const key of designs.designsFor('fold6').map((d) => d.key)) {
    for (const size of ['fold6', 'fold5']) {
      const card = designs.applyDesign(core.newCollage(size), key, {}, { measure, newId });
      assert.ok(card.back, `${key} ${size}: both sides`);
      // UX review 8, M2; cold review 9, M2: the cover's title shows the occasion; the host opens
      // the inside, and the occasion ends the invitation there.
      const title = card.items.find((item) => item.field === 'coverTitle');
      assert.ok(title.x > core.PAGE_WIDTH / 2, `${key}: the cover title on the front cover, the right panel`);
      assert.equal(title.text, designs.DEFAULT_DETAILS.occasion, `${key}: the cover title is the occasion`);
      assert.ok(card.back.items.some((item) => item.field === 'occasion' && item.x < core.PAGE_WIDTH / 2), `${key}: the occasion inside on the left`);
      assert.ok(card.back.items.some((item) => item.field === 'host'), `${key}: the host inside`);
      assert.ok(card.back.items.some((item) => item.field === 'when'), `${key}: the details inside`);
      // No words cross the fold.
      for (const item of [...card.items, ...card.back.items].filter((i) => i.kind === 'text')) {
        const b = box(item);
        assert.ok(b.right <= core.PAGE_WIDTH / 2 + 0.5 || b.left >= core.PAGE_WIDTH / 2 - 0.5, `${key} ${size}: ${item.field} crosses the fold`);
      }
      const texts = [card.items, card.back.items].map((side) => side.filter((i) => i.kind === 'text'));
      for (const side of texts) for (let i = 0; i < side.length; i += 1) for (let j = i + 1; j < side.length; j += 1) {
        assert.ok(!overlap(box(side[i]), box(side[j])), `${key} ${size}: ${side[i].field} overlaps ${side[j].field}`);
      }
      // An inside field filled again goes inside.
      assert.ok(designs.fieldItem(card, 'venue', { ...designs.detailsOf(card), venue: 'Gate 2' }, { measure, newId }).x > core.PAGE_WIDTH / 2);
    }
  }
  assert.ok(designs.fieldsOf('classic', 'fold6').includes('when'));
});

// Cold review fixes (19 Nov 2026).
test('a field\'s side comes from the design: a programme card\'s programme is on its front, a folded card\'s details inside', () => {
  const card = designs.applyDesign(core.newCollage('a7p'), 'programmecard', {}, { measure, newId });
  assert.equal(designs.sideOf(card, 'programme'), 'front');
  assert.equal(designs.fieldItem(card, 'programme', { ...designs.detailsOf(card), programme: '1900 h · Arrival' }, { measure, newId }).text, '1900 h · Arrival');
  const folded = designs.applyDesign(core.newCollage('fold6'), 'classic', {}, { measure, newId });
  assert.equal(designs.sideOf(folded, 'guest'), 'back');
  assert.equal(designs.sideOf(folded, 'occasion'), 'back');
  assert.equal(designs.sideOf(folded, 'coverTitle'), 'front');
  assert.equal(designs.sideOf(folded, 'host'), 'back');
  assert.ok(designs.fieldsOnCard(folded).includes('when'));
  const flat = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }), { measure, newId });
  assert.equal(designs.sideOf(flat, 'programme'), 'back');
  assert.equal(designs.usesColours('pass'), false, 'a role named band is not a unit colour');
  assert.equal(designs.usesColours('regimental'), true);
});

test('a design change leaves a locked part where it is and brings a hidden part back hidden', () => {
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  const edited = { ...card, items: card.items.map((item) => (item.field === 'occasion' ? { ...item, x: 111, locked: true } : item.field === 'note' ? { ...item, hidden: true } : item)) };
  const navy = designs.applyDesign(edited, 'navy', designs.detailsOf(edited), { measure, newId });
  const occasions = navy.items.filter((item) => item.field === 'occasion');
  assert.equal(occasions.length, 1, 'no second occasion');
  assert.deepEqual([occasions[0].x, occasions[0].locked], [111, true]);
  assert.equal(navy.items.find((item) => item.field === 'note').hidden, true);
});

// Codex on #3452: locked parts keep their stacking place, and are not made twice across sides.
test('a locked part keeps its place behind the logo; a field locked on the front is not made again inside', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('a5p'), 'regimental', {}, opts);
  const locked = { ...card, items: card.items.map((item) => (item.role === 'roundel' ? { ...item, locked: true } : item)) };
  const resized = designs.applyDesign({ ...locked, size: 'a6p' }, 'regimental', designs.detailsOf(locked), opts);
  const order = resized.items.map((item) => item.role);
  assert.ok(order.indexOf('roundel') < order.indexOf('logo'), 'the roundel is still drawn before the logo');
  // A guest field locked on a flat card, then the card made folded: one guest field only.
  const flat = designs.applyDesign(core.newCollage('a5p'), 'classic', { guest: 'Maj A' }, opts);
  const lockedGuest = { ...flat, items: flat.items.map((item) => (item.field === 'guest' ? { ...item, locked: true } : item)) };
  const folded = designs.applyDesign({ ...lockedGuest, size: 'fold6' }, 'classic', designs.detailsOf(lockedGuest), opts);
  const guests = [...folded.items, ...(folded.back?.items || [])].filter((item) => item.field === 'guest');
  assert.equal(guests.length, 1);
  assert.equal(guests[0].locked, true);
  // Card details finds it where it is, on the front, so typing changes it (Codex on #3452).
  assert.equal(designs.sideOf(folded, 'guest'), 'front');
  assert.equal(designs.sideOf(core.flipSide(folded), 'guest'), 'front');
  // A field not on the card: the side the design puts it on.
  assert.equal(designs.sideOf(designs.applyDesign(core.newCollage('fold6'), 'classic', {}, opts), 'venue'), 'back');
});

// Codex on #3452: a back logo the user locked keeps its lock and turn through a size change.
test('a locked back logo keeps its lock and turn when the card is resized', () => {
  const opts = { measure, newId };
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, opts), opts);
  const locked = { ...card, back: { ...card.back, items: card.back.items.map((item) => (item.role === 'backlogo' ? { ...item, locked: true, angle: 12 } : item)) } };
  const resized = designs.applyDesign({ ...locked, size: 'a6p' }, 'classic', designs.detailsOf(locked), opts);
  const backLogo = resized.back.items.find((item) => item.role === 'backlogo');
  assert.equal(backLogo.locked, true);
  assert.equal(backLogo.angle, 12);
});

// A draft reopened draws every design's words the same size (a ten-line programme stays ten lines).
test('every design and its back read back from a draft unchanged, a long programme included', () => {
  const programme = Array.from({ length: 10 }, (_, i) => `${18 + i}00 h  Item ${i + 1} of the evening`).join('\n');
  for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].poster)) {
    const size = designs.DESIGNS[key].pocket ? pocketSizes[0] : 'a5p';
    const front = designs.applyDesign(core.newCollage(size), key, { programme }, { measure, newId });
    const card = designs.DESIGNS[key].pocket ? front : designs.addBack(front, { measure, newId });
    const read = core.readDraftModel(JSON.parse(JSON.stringify(card)), []);
    assert.ok(read, `${key}: the draft reads back`);
    for (const [before, after] of [[card.items, read.items], [card.back?.items || [], read.back?.items || []]]) {
      for (const item of before.filter((each) => each.kind === 'text')) {
        const again = after.find((each) => each.id === item.id);
        assert.ok(again, `${key}: ${item.field} kept`);
        assert.equal(again.maxLines, item.maxLines, `${key}: ${item.field} keeps its lines`);
        const was = core.layoutText(item, measure);
        const now = core.layoutText(again, measure);
        assert.ok(Math.abs(was.size - now.size) < 0.01, `${key}: ${item.field} drawn at ${now.size}, was ${was.size}`);
      }
    }
  }
});

// Custom sizes (PR 5b): every card design stays on the page with no words overlapping, and at
// a card no smaller than A6 (core.smallCard) has letters at least 1.5 mm tall; smaller ones are warned of.
test('card designs at custom sizes stay on the page, and are readable from A6 up', () => {
  for (const size of ['mm:300x50', 'mm:50x300', 'mm:50x50', 'mm:300x300', 'mm:200x100', 'mm:100x200', 'mm:100x100', 'mm:105x148', 'mm:148x105']) {
    const mmWide = core.sizeOf(size).mm[0];
    const small = core.smallCard(size);
    for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].poster && !designs.DESIGNS[each].pocket)) {
      const card = designs.applyDesign(core.newCollage(size), key, {}, { measure, newId });
      const height = core.pageHeight(size);
      for (const item of card.items) {
        const b = box(item);
        assert.ok(b.left >= -0.5 && b.right <= core.PAGE_WIDTH + 0.5 && b.top >= -0.5 && b.bottom <= height + 0.5, `${key} ${size}: ${item.role || item.field} off the page`);
      }
      const texts = card.items.filter((item) => item.kind === 'text');
      // A custom card smaller than A6 is warned of on the start screen: its words, kept at least
      // 6 pt, may crowd; at A6 and up nothing overlaps.
      if (!small) {
        for (let i = 0; i < texts.length; i += 1) {
          for (let j = i + 1; j < texts.length; j += 1) assert.ok(!overlap(box(texts[i]), box(texts[j])), `${key} ${size}: ${texts[i].field} overlaps ${texts[j].field}`);
        }
      }
      if (!small) {
        const letters = Math.min(...texts.map((item) => core.layoutText(item, measure).size)) * mmWide / core.PAGE_WIDTH;
        assert.ok(letters >= 1.5, `${key} ${size}: letters ${letters.toFixed(2)} mm`);
      }
    }
  }
});

// A folded card prints on a landscape sheet folded down the middle (PR 9c): the outside's right
// half is the front cover (the logo and host), its left half the back; inside, the invitation
// reads on the left panel and the occasion and details on the right. No part crosses the fold.
test('folded cards: the cover is the outside sheet\'s right half, and inside reads left to right', () => {
  for (const size of ['fold6', 'fold5']) {
    for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].poster && !designs.DESIGNS[each].pocket)) {
      const card = designs.applyDesign(core.newCollage(size), key, {}, { measure, newId });
      const half = core.PAGE_WIDTH / 2;
      const outside = card.items;
      const inside = card.back.items;
      for (const role of ['logo']) assert.ok(outside.find((item) => item.role === role).x > half, `${key} ${size}: the ${role} on the cover`);
      assert.ok(outside.find((item) => item.field === 'coverTitle').x > half, `${key} ${size}: the cover title on the cover`);
      for (const field of ['host', 'invite', 'occasion']) assert.ok(inside.find((item) => item.field === field).x < half, `${key} ${size}: ${field} on the left panel`);
      assert.ok(inside.find((item) => item.role === 'guestline').x < half, `${key} ${size}: the guest's line on the left panel`);
      // "at the" leads into the occasion inside (no design but Bilingual leaves it out); never on the cover.
      assert.ok(!outside.some((item) => item.field === 'at'), `${key} ${size}: no "at the" on the cover`);
      if (key !== 'bilingual') assert.ok(inside.find((item) => item.field === 'at').y < inside.find((item) => item.field === 'occasion').y, `${key} ${size}: "at the" before the occasion`);
      for (const field of ['when', 'venue', 'dress', 'rsvp']) assert.ok(inside.find((item) => item.field === field).x > half, `${key} ${size}: ${field} on the right panel`);
      for (const item of [...outside, ...inside].filter((each) => each.kind === 'text')) {
        const b = box(item);
        assert.ok(b.right <= half + 0.5 || b.left >= half - 0.5, `${key} ${size}: ${item.field} crosses the fold`);
      }
    }
  }
});

// The logo keeps clear of the bands and rules around it (a car pass's logo touched its band).
test('at every size, the logo keeps at least 10 units clear of any band or rule it is not inside', () => {
  for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].poster)) {
    for (const size of designs.DESIGNS[key].pocket ? pocketSizes : cardSizes) {
      const card = designs.applyDesign(core.newCollage(size), key, {}, { measure, newId });
      const logo = card.items.find((item) => item.role === 'logo');
      // A logo in a roundel (Regimental) sits over the colour band by design.
      if (!logo || card.items.some((item) => item.role === 'roundel')) continue;
      const l = box(logo);
      for (const shape of card.items.filter((item) => item.kind === 'shape' && item.role !== 'roundel')) {
        const s = box(shape);
        const inside = s.left <= l.left && s.right >= l.right && s.top <= l.top && s.bottom >= l.bottom;
        if (inside) continue;
        const gap = Math.max(s.left - l.right, l.left - s.right, s.top - l.bottom, l.top - s.bottom);
        assert.ok(gap >= 10, `${key} ${size}: the logo is ${gap.toFixed(1)} from ${shape.role}`);
      }
    }
  }
});

// Words big enough to read (cold review 2): every design's words print at 6 pt or more, on every
// size it is offered for, both sides, after fitting.
test('every design prints its words at 6 pt or more at every size it is offered for', () => {
  const sizes = Object.keys(core.SIZES).filter((key) => ['card', 'pocket', 'folded'].includes(core.SIZES[key].group));
  for (const size of sizes) {
    const mmPerUnit = core.sizeOf(size).mm[0] / core.PAGE_WIDTH;
    for (const design of designs.designsFor(size, 'card')) {
      const card = designs.applyDesign(core.newCollage(size), design.key, {}, { measure, newId });
      for (const item of [...card.items, ...(card.back?.items || [])].filter((each) => each.kind === 'text')) {
        const pt = core.layoutText(item, measure).size * mmPerUnit * 72 / 25.4;
        assert.ok(pt >= 5.99, `${design.key} ${size}: ${item.field} prints at ${pt.toFixed(1)} pt`);
        // Script and italic lines start at 7 pt, before fitting to their width.
        if (['script', 'formal'].includes(item.font) || item.italic) assert.ok(item.size * mmPerUnit * 72 / 25.4 >= 6.99, `${design.key} ${size}: ${item.field} starts below 7 pt`);
      }
    }
  }
});

// Logos (cold review 2): a card with no logo gets no empty roundel when it changes to Regimental;
// a wide unit logo keeps the place's area, so it is not squeezed to a sliver, and the roundel
// grows round it.
test('Regimental with no logo hides its roundel; a wide logo keeps the place\'s area and the roundel fits it', () => {
  const classic = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  const noLogo = { ...classic, items: classic.items.filter((item) => item.role !== 'logo') };
  const regimental = designs.applyDesign(noLogo, 'regimental', designs.detailsOf(noLogo), { measure, newId });
  assert.equal(regimental.items.find((item) => item.role === 'roundel').hidden, true);

  // A logo four times as wide as tall, in place of the SDD logo.
  const wide = { ...classic, items: classic.items.map((item) => (item.role === 'logo' ? { ...item, kind: 'photo', photo: 'p1', sticker: undefined, aspect: 4 } : item)) };
  const sdd = classic.items.find((item) => item.role === 'logo');
  const applied = designs.applyDesign(wide, 'regimental', designs.detailsOf(wide), { measure, newId });
  const logo = applied.items.find((item) => item.role === 'logo');
  const sddPlace = designs.applyDesign(classic, 'regimental', designs.detailsOf(classic), { measure, newId }).items.find((item) => item.role === 'logo');
  const area = (item) => item.width * core.itemHeight(item);
  assert.ok(Math.abs(area(logo) - area(sddPlace)) / area(sddPlace) < 0.05 || logo.width >= sddPlace.width * 2.39, 'the same area, or as wide as allowed');
  assert.ok(logo.width > sdd.width * 1.5, 'wider than the crest place');
  const roundel = applied.items.find((item) => item.role === 'roundel');
  assert.ok(!roundel.hidden && roundel.width > logo.width, 'the roundel goes round the wide logo');
  assert.equal(designs.logoWidth(100, 0.5, 0.5), 100, 'a logo the place\'s shape keeps its width');
});

// A long guest's name (cold review 2) shrinks only so far, then takes a second line: it never
// prints smaller than 6 pt or 60% of the design's size.
test('a long guest name keeps a readable size and takes a second line', () => {
  const long = 'Lieutenant General Rajendra Kumar Mehta, PVSM, AVSM, VSM and Mrs Mehta';
  const card = designs.applyDesign(core.newCollage('a6p'), 'classic', { guest: long }, { measure, newId });
  const guest = card.items.find((item) => item.field === 'guest');
  const layout = core.layoutText(guest, measure);
  const pt = layout.size * core.sizeOf('a6p').mm[0] / core.PAGE_WIDTH * 72 / 25.4;
  assert.ok(pt >= 5.99, `prints at ${pt.toFixed(1)} pt`);
  assert.ok(layout.size >= guest.size * 0.6 - 0.01);
  assert.ok(layout.lines >= 2, 'on two lines');
  // It grows upwards, so its last line is not cut by the line drawn to write on.
  const line = card.items.find((item) => item.role === 'guestline');
  assert.ok(guest.y + core.itemHeight(guest) / 2 <= line.y + core.itemHeight(line) / 2 + 2, 'the name stays on its line');
  assert.ok(layout.width <= guest.maxWidth + 0.5);
  // It is kept by a draft.
  const read = core.readDraftModel(JSON.parse(JSON.stringify(card)), []);
  assert.equal(read.items.find((item) => item.field === 'guest').minSize, guest.minSize);
});

test('a design named like an object\'s own property is no design', () => {
  const plain = core.newCollage('a5p');
  for (const name of ['constructor', 'toString', '__proto__']) {
    assert.equal(designs.applyDesign(plain, name, {}, { measure, newId }), plain, name);
    assert.equal(designs.DESIGNS[name], undefined, name);
  }
});

test('a design change keeps the guest list, and the back keeps the front logo', () => {
  const card = designs.addBack({ ...designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }) }, { measure, newId });
  const withGuests = { ...card, card: { ...card.card, guests: ['Maj A', 'Capt B'] } };
  const navy = designs.applyDesign(withGuests, 'navy', designs.detailsOf(withGuests), { measure, newId });
  assert.deepEqual(navy.card.guests, ['Maj A', 'Capt B']);
  const front = navy.items.find((item) => item.role === 'logo');
  const back = navy.back.items.find((item) => item.role === 'backlogo');
  assert.equal(back.sticker, front.sticker, 'the back shows the front logo');
});

// One field, one place (cold review 3): a programme card's programme is on its front, so its back
// does not carry a second programme; parts and details a newer PRISM keeps survive a design change.
test('a programme card with a back has its programme once; newer parts and details survive a design change', () => {
  const front = designs.applyDesign(core.newCollage('a7p'), 'programmecard', {}, { measure, newId });
  const card = designs.addBack(front, { measure, newId });
  const fields = (items) => items.filter((item) => item.kind === 'text').map((item) => item.field);
  const onFront = fields(card.items);
  assert.ok(onFront.includes('programme'));
  assert.ok(!fields(card.back.items).some((field) => onFront.includes(field)), 'no field on both sides');
  assert.equal(designs.sideOf(card, 'programme'), 'front');

  const kept = { ...card, card: { ...card.card, details: { ...card.card.details, futureField: 'x' } }, back: { ...card.back, kept: { items: [{ at: 0, item: { id: 'h', kind: 'hologram' } }], order: [] } } };
  const changed = designs.applyDesign(kept, 'pocketinvite', designs.detailsOf(kept), { measure, newId });
  assert.equal(changed.card.details.futureField, 'x');
  assert.equal(core.keptCount(changed), 1);
  const resized = designs.applyDesign(core.changeSize(kept, 'a7l'), 'programmecard', designs.detailsOf(kept), { measure, newId });
  assert.equal(core.keptCount(resized), 1);
});

// Two logos through a design change (cold review 3): the second goes beside the design's logo,
// as tall as it, the pair centred where the design centres its logo.
test('a second logo is placed beside the design\'s logo, as tall, through a design change', () => {
  for (const key of ['classic', 'navy', 'regimental', 'modern', 'bilingual', 'ceremonial']) {
    const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
    const logo = card.items.find((item) => item.role === 'logo');
    const withTwo = { ...card, items: [...card.items, { ...logo, id: 'second', role: 'logo2', kind: 'photo', photo: 'p', sticker: undefined, width: logo.width * 3, aspect: 2, x: 900, y: 1300 }] };
    const changed = designs.applyDesign(withTwo, key, designs.detailsOf(withTwo), { measure, newId });
    const first = changed.items.find((item) => item.role === 'logo');
    const second = changed.items.find((item) => item.role === 'logo2');
    assert.ok(second, key);
    assert.ok(Math.abs(core.itemHeight(second) - core.itemHeight(first)) < 0.5, `${key}: as tall as the logo`);
    assert.ok(Math.abs(second.y - first.y) < 0.5, `${key}: in line with it`);
    const apart = Math.abs(second.x - first.x) - (first.width + second.width) / 2;
    assert.ok(apart > 0, `${key}: side by side, not overlapping`);
  }
});

// The back's small logo is the front's, or none (cold review 3).
test('a card whose front has no logo gets a back with no logo, through a design change', () => {
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }), { measure, newId });
  assert.ok(card.back.items.some((item) => item.role === 'backlogo'));
  const noLogo = { ...card, items: card.items.filter((item) => item.role !== 'logo') };
  const changed = designs.applyDesign(noLogo, 'navy', designs.detailsOf(noLogo), { measure, newId });
  assert.equal(changed.back.items.some((item) => item.role === 'backlogo'), false);
});

// The back's small logo follows the front's however it changes (cold review 4).
test('the back logo follows the front: replaced, removed, kept off when deleted, back when chosen', () => {
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }), { measure, newId });
  const backLogo = (c) => (c.side === 'back' ? c.items : c.back.items).find((item) => item.role === 'backlogo');
  assert.equal(designs.followLogo(card, { measure, newId }), card, 'nothing to change');
  const replaced = { ...card, items: card.items.map((item) => (item.role === 'logo' ? { ...item, kind: 'photo', photo: 'p1', sticker: undefined, aspect: 2 } : item)) };
  assert.equal(backLogo(designs.followLogo(replaced, { measure, newId })).photo, 'p1');
  const removed = { ...card, items: card.items.filter((item) => item.role !== 'logo') };
  assert.equal(backLogo(designs.followLogo(removed, { measure, newId })), undefined);
  // Turned over, the same.
  assert.equal(backLogo(designs.followLogo(core.flipSide(removed), { measure, newId })), undefined);
  // A back logo the user deleted stays off, unless a logo is chosen in Card details.
  const deleted = { ...card, back: { ...card.back, items: card.back.items.filter((item) => item.role !== 'backlogo') } };
  assert.equal(designs.followLogo(deleted, { measure, newId }), deleted);
  assert.ok(backLogo(designs.followLogo(deleted, { measure, newId }, { add: true })));
});

test('roleItemOn finds where the design puts a part on either side', () => {
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }), { measure, newId });
  assert.ok(designs.roleItemOn(card, 'guestline', 'front', { measure, newId }));
  assert.ok(designs.roleItemOn(card, 'backlogo', 'back', { measure, newId }));
  const folded = designs.applyDesign(core.newCollage('fold6'), 'classic', {}, { measure, newId });
  assert.ok(designs.roleItemOn(folded, 'guestline', 'back', { measure, newId }), 'a folded card\'s line is inside');
});

// The back logo the user hid or locked stays so (cold review 5); the front's lock is its own.
test('a hidden back logo stays hidden and a locked one stays as it is through a design change', () => {
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId }), { measure, newId });
  const setBack = (c, change) => ({ ...c, back: { ...c.back, items: c.back.items.map((item) => (item.role === 'backlogo' ? { ...item, ...change } : item)) } });
  const backLogo = (c) => c.back.items.find((item) => item.role === 'backlogo');
  assert.equal(backLogo(designs.applyDesign(setBack(card, { hidden: true }), 'navy', designs.detailsOf(card), { measure, newId })).hidden, true);
  const locked = setBack(card, { locked: true, x: 123 });
  const kept = backLogo(designs.applyDesign(locked, 'navy', designs.detailsOf(card), { measure, newId }));
  assert.equal(kept.locked, true);
  assert.equal(kept.x, 123);
  // A locked and hidden front logo does not make a new back logo locked or hidden.
  const front = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, { measure, newId });
  const lockedFront = { ...front, items: front.items.map((item) => (item.role === 'logo' ? { ...item, locked: true, hidden: true } : item)) };
  const withBack = designs.addBack(lockedFront, { measure, newId });
  assert.equal(backLogo(withBack).locked, undefined);
  assert.equal(backLogo(withBack).hidden, undefined);
});

// Backs and folded insides with full wording (cold review 5): an eight-line programme and a long
// guest's name stay on the page with no words overlapping.
test('backs and folded insides keep long wording on the page without overlaps', () => {
  const programme = Array.from({ length: 8 }, (_, i) => `${18 + i}00 h  Item ${i + 1} of the evening`).join(String.fromCharCode(10));
  const wording = { programme, guest: 'Lt Gen R. K. Mehta, PVSM, AVSM and Mrs Mehta' };
  const sizes = Object.keys(core.SIZES).filter((key) => ['card', 'folded'].includes(core.SIZES[key].group));
  for (const size of sizes) {
    for (const design of designs.designsFor(size, 'card')) {
      let card = designs.applyDesign(core.newCollage(size), design.key, wording, { measure, newId });
      if (!card.back) card = designs.addBack(card, { measure, newId });
      const height = core.pageHeight(size);
      const back = card.back.items;
      for (const item of back) {
        const b = box(item);
        assert.ok(b.left >= -0.5 && b.right <= core.PAGE_WIDTH + 0.5 && b.top >= -0.5 && b.bottom <= height + 0.5, `${design.key} ${size}: ${item.role || item.field} off the back`);
      }
      const texts = back.filter((item) => item.kind === 'text');
      for (let i = 0; i < texts.length; i += 1) {
        for (let j = i + 1; j < texts.length; j += 1) assert.ok(!overlap(box(texts[i]), box(texts[j])), `${design.key} ${size}: ${texts[i].field} overlaps ${texts[j].field} on the back`);
      }
    }
  }
});

// A size change keeps a design the new size offers, or moves to the nearest one (cold review 5).
test('a card on a size its design does not suit moves to the nearest design offered', () => {
  assert.equal(designs.designOnSize('classic', 'a5p'), 'classic');
  assert.equal(designs.designOnSize('classic', 'a7p'), 'pocketinvite');
  assert.equal(designs.designOnSize('navy', 'passl'), 'pass');
  assert.equal(designs.designOnSize('pocketinvite', 'passl'), 'pass');
  assert.equal(designs.designOnSize('reply', 'a5p'), 'classic');
  assert.equal(designs.designOnSize('event', 'a7p'), 'event', 'a poster keeps its design');
});

test('a custom size smaller than A6 offers the pocket designs first, then the others', () => {
  const small = core.customKey(80, 120).key;
  const keys = designs.designsFor(small).map((design) => design.key);
  assert.equal(designs.DESIGNS[keys[0]].pocket, true);
  assert.ok(keys.includes('classic'));
  assert.ok(keys.includes('pocketinvite'));
  assert.equal(designs.designOnSize('classic', small), 'classic');
  const big = core.customKey(150, 200).key;
  assert.ok(designs.designsFor(big).every((design) => !design.pocket));
});

// Cold review 5 (tests M1): a tall logo on a poster stays inside its place, and on the page,
// through design changes.
test('a tall own logo on a poster stays no taller than its place through design changes', () => {
  const opts = { measure, newId };
  for (const size of ['a4p', 'a3p', 'a4l', 'a3l']) {
    let poster = designs.applyDesign(core.newCollage(size), 'event', designs.POSTER_DETAILS, opts);
    const place = poster.items.find((item) => item.role === 'logo');
    const tall = { ...core.photoItem('mine', 'p1', 0.5, { x: place.x, y: place.y, width: place.width / 2 }), role: 'logo' };
    poster = { ...poster, items: poster.items.map((item) => (item.role === 'logo' ? tall : item)) };
    for (const key of ['notice', 'photoposter', 'talk', 'event']) {
      poster = designs.applyDesign(poster, key, designs.detailsOf(poster), opts);
      const logo = poster.items.find((item) => item.role === 'logo');
      const slot = designs.roleItem(poster, 'logo', opts);
      const h = core.itemHeight(logo);
      assert.ok(h <= core.itemHeight(slot) + 1e-6, `${size} ${key}: no taller than its place`);
      assert.ok(logo.y - h / 2 >= -1e-6, `${size} ${key}: inside the top edge`);
    }
  }
  assert.equal(designs.posterLogoWidth(100, 0.6, 0.5), 100 / 0.6 * 0.5);
  assert.equal(designs.posterLogoWidth(100, 0.6, 2), designs.logoWidth(100, 0.6, 2), 'a wide logo keeps its area');
});

// Tests review 5, L1: the size rules at their limits.
test('long words shrink to the 6 pt floor and no further, at the smallest sizes', () => {
  const long = 'Annual Regimental Raising Day Dinner and Ceremonial Parade of the Battalion '.repeat(3);
  for (const size of ['a7p', 'passl', 'a6p', 'fold6']) {
    const mmPerUnit = core.sizeOf(size).mm[0] / core.PAGE_WIDTH;
    for (const design of designs.designsFor(size, 'card')) {
      const wording = Object.fromEntries(designs.FIELDS.filter((field) => !/Hi$|colours|logoMode/.test(field)).map((field) => [field, long]));
      const card = designs.applyDesign(core.newCollage(size), design.key, wording, { measure, newId });
      const texts = [...card.items, ...(card.back?.items || [])].filter((each) => each.kind === 'text');
      let shrank = false;
      for (const item of texts) {
        const layout = core.layoutText(item, measure);
        const pt = layout.size * mmPerUnit * 72 / 25.4;
        assert.ok(pt >= 5.99, `${design.key} ${size}: ${item.field} prints at ${pt.toFixed(1)} pt`);
        if (layout.size < item.size - 0.5) shrank = true;
      }
      assert.ok(shrank, `${design.key} ${size}: long words did shrink, so the floor was reached for`);
    }
  }
});

test('a guest name\'s floor is 60% of its design size; a wide logo is at most 2.4 times its place', () => {
  for (const size of ['a5p', 'a6p']) {
    const card = designs.applyDesign(core.newCollage(size), 'classic', { guest: 'Maj A' }, { measure, newId });
    const guest = card.items.find((item) => item.field === 'guest');
    assert.ok(Math.abs(guest.minSize - Math.max(Math.ceil(designs.minTextUnits ? designs.minTextUnits(size) : 0), Math.round(guest.size * 0.8))) <= 1, `${size}: floor ${guest.minSize} for ${guest.size}`);
  }
  assert.equal(designs.logoWidth(100, 1, 100), 240, 'a very wide logo stops at 2.4 times the place');
  assert.ok(designs.logoWidth(100, 1, 2) < 240);
});

// Cold review 6, H1: Bilingual never prints Hindi that says something else than the English.
test('the Hindi follows an occasion quick pick and is emptied where it would contradict the English', () => {
  const picks = { 'Regimental Dinner': 'रेजिमेंटल रात्रिभोज' };
  const base = { ...designs.DEFAULT_DETAILS };
  // A quick pick: its Hindi.
  assert.equal(designs.hindiInStep({ ...base, occasion: 'Regimental Dinner' }, picks).occasionHi, 'रेजिमेंटल रात्रिभोज');
  // Typed English: the sample Hindi (or a quick pick's) is emptied.
  assert.equal(designs.hindiInStep({ ...base, occasion: 'Mess Night' }, picks).occasionHi, '');
  assert.equal(designs.hindiInStep({ ...base, occasion: 'Mess Night', occasionHi: 'रेजिमेंटल रात्रिभोज' }, picks).occasionHi, '');
  // Hindi the user typed stays.
  assert.equal(designs.hindiInStep({ ...base, occasion: 'Mess Night', occasionHi: 'मेस नाइट' }, picks).occasionHi, 'मेस नाइट');
  // The host and invitation line: the sample's Hindi goes when the English is someone else's.
  const typed = designs.hindiInStep({ ...base, host: 'Lt Gen R.K. Sharma, AVSM', invite: 'requests the pleasure of' }, picks);
  assert.equal(typed.hostHi, '');
  assert.equal(typed.inviteHi, '');
  // The sample as it is keeps its Hindi.
  const sample = designs.hindiInStep(base, picks);
  assert.equal(sample.hostHi, base.hostHi);
  assert.equal(sample.occasionHi, base.occasionHi);
});

// Cold review 6, M1 and M2: No logo leaves no empty band; a poster's top line lines up with its
// heading whether or not a logo is chosen.
test('No logo on the Notice moves its details up with the heading; the Event top line starts on the heading edge', () => {
  const opts = { measure, newId };
  const at = (c, field) => c.items.find((item) => item.field === field);
  const withLogo = designs.applyDesign(core.newCollage('a4p'), 'notice', designs.POSTER_DETAILS, opts);
  const none = designs.applyDesign(core.newCollage('a4p'), 'notice', { ...designs.POSTER_DETAILS, logoMode: 'none' }, opts);
  const gap = (c) => (at(c, 'when').y - core.itemHeight(at(c, 'when')) / 2) - (at(c, 'subtitle').y + core.itemHeight(at(c, 'subtitle')) / 2);
  assert.ok(gap(none) <= gap(withLogo) + 1, 'the gap under the subtitle is no bigger without a logo');
  assert.ok(at(none, 'when').y < at(withLogo, 'when').y);
  const event = designs.applyDesign(core.newCollage('a4p'), 'event', designs.POSTER_DETAILS, opts);
  const left = (item) => item.x - item.width / 2;
  assert.ok(Math.abs(left(at(event, 'subtitle')) - left(at(event, 'title'))) < 2, 'top line and heading start together');
});

// Codex on #3453: locked logos and typed Hindi are left alone.
test('a locked back logo stays when the front logo goes; a second logo sits beside a locked centred first', () => {
  const opts = { measure, newId };
  const card = designs.addBack(designs.applyDesign(core.newCollage('a5p'), 'classic', {}, opts), opts);
  const locked = { ...card, back: { ...card.back, items: card.back.items.map((item) => (item.role === 'backlogo' ? { ...item, locked: true } : item)) } };
  const noFront = { ...locked, items: locked.items.filter((item) => item.role !== 'logo') };
  assert.ok(designs.followLogo(noFront, opts).back.items.some((item) => item.role === 'backlogo'));
  // A centred first logo locked, a second added, the design changed: side by side, not on top.
  const front = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, opts);
  const first = front.items.find((item) => item.role === 'logo');
  const withTwo = { ...front, items: [...front.items.map((item) => (item.role === 'logo' ? { ...item, locked: true } : item)), { ...first, id: 'second', role: 'logo2', locked: false }] };
  const changed = designs.applyDesign(withTwo, 'navy', designs.detailsOf(withTwo), opts);
  const [a, b] = ['logo', 'logo2'].map((role) => changed.items.find((item) => item.role === role));
  const apart = Math.abs(a.x - b.x) >= (a.width + b.width) / 2;
  assert.ok(apart, 'the two logos do not overlap');
});

test('Hindi the user typed stays through unrelated edits; a stock Hindi follows a quick pick', () => {
  const picks = { 'Regimental Dinner': 'रेजिमेंटल रात्रिभोज' };
  const typed = { ...designs.DEFAULT_DETAILS, occasion: 'Regimental Dinner', occasionHi: 'रेजिमेंट का भोज' };
  assert.equal(designs.hindiInStep(typed, picks).occasionHi, 'रेजिमेंट का भोज');
  const stock = { ...designs.DEFAULT_DETAILS, occasion: 'Regimental Dinner' };
  assert.equal(designs.hindiInStep(stock, picks).occasionHi, 'रेजिमेंटल रात्रिभोज');
});

// Codex on #3453: a roundel the user hid stays hidden through a size or design change; one hidden
// for want of a logo comes back with a logo.
test('a roundel the user hid stays hidden; one hidden for want of a logo comes back with one', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('a5p'), 'regimental', {}, opts);
  const hid = { ...card, items: card.items.map((item) => (item.role === 'roundel' ? { ...item, hidden: true } : item)) };
  const turned = designs.applyDesign({ ...hid, size: 'a5l' }, 'regimental', designs.detailsOf(hid), opts);
  assert.equal(turned.items.find((item) => item.role === 'roundel').hidden, true, 'the user\'s hiding is kept');
  // No logo: hidden for want of one; a logo put back brings it back.
  const noLogo = { ...card, items: card.items.filter((item) => item.role !== 'logo') };
  const bare = designs.applyDesign(noLogo, 'regimental', designs.detailsOf(noLogo), opts);
  const roundel = bare.items.find((item) => item.role === 'roundel');
  assert.equal(roundel.hidden, true);
  const logo = card.items.find((item) => item.role === 'logo');
  const items = [...bare.items.map((item) => ({ ...item })), { ...logo }];
  designs.fitRoundel(items, { width: roundel.width, aspect: roundel.aspect }, { hadLogo: false });
  assert.ok(!items.find((item) => item.role === 'roundel').hidden);
  // An earlier PRISM's roundel, hidden without the mark while there was no logo, comes back too.
  const old = [{ ...roundel, autoHidden: undefined, hidden: true }, { ...logo }];
  designs.fitRoundel(old, { width: roundel.width, aspect: roundel.aspect }, { hadLogo: false });
  assert.ok(!old[0].hidden);
});

// UX review 7, M5: two logos on Regimental sit in a rounded band that holds both clear of its edge.
test('two logos sit in a rounded band; one logo keeps its oval', () => {
  const items = [
    { id: 'r', role: 'roundel', kind: 'shape', form: 'ellipse', x: 500, y: 100, width: 170, aspect: 0.72 },
    { id: 'l', role: 'logo', kind: 'sticker', x: 440, y: 100, width: 100, aspect: 0.6 },
    { id: 'm', role: 'logo2', kind: 'photo', x: 600, y: 100, width: 160, aspect: 4 }
  ];
  designs.fitRoundel(items, { width: 170, aspect: 0.72 });
  const roundel = items[0];
  assert.equal(roundel.form, 'rect');
  assert.ok(roundel.radius > 0);
  for (const logo of items.slice(1)) {
    assert.ok(logo.x - logo.width / 2 >= roundel.x - roundel.width / 2, `${logo.id} inside on the left`);
    assert.ok(logo.x + logo.width / 2 <= roundel.x + roundel.width / 2, `${logo.id} inside on the right`);
  }
  const one = [{ ...items[0] }, items[1]];
  designs.fitRoundel(one, { width: 170, aspect: 0.72 });
  assert.equal(one[0].form, 'ellipse');
  assert.equal(one[0].radius, undefined);
});

// Cold review 7, M1: a folded card carries its design's own look, not one plain layout in the
// design's colours. The front cover is the outside's right half.
test('a folded card\'s front cover carries its design: bands, bar, photo frame, Hindi host, double border', () => {
  const half = core.PAGE_WIDTH / 2;
  const onCover = (item) => box(item).left >= half - 0.5;
  for (const size of ['fold6', 'fold5']) {
    const make = (key, details = {}) => designs.applyDesign(core.newCollage(size), key, details, { measure, newId });
    // Regimental: the three bands on the cover follow the unit's colours, the roundel behind the logo.
    const regimental = make('regimental', { colours: '#112233 #445566 #778899' });
    const bands = ['band1', 'band2', 'band3'].map((role) => regimental.items.find((item) => item.role === role));
    bands.forEach((band, i) => {
      assert.ok(band && onCover(band), `${size}: Regimental ${'band' + (i + 1)} on the cover`);
      assert.equal(band.fill, ['#112233', '#445566', '#778899'][i]);
      assert.deepEqual(band.tint, { fill: i + 1 }, 'a unit colour changed later recolours the band');
    });
    const roundelAt = regimental.items.findIndex((item) => item.role === 'roundel');
    const logoAt = regimental.items.findIndex((item) => item.role === 'logo');
    assert.ok(roundelAt >= 0 && roundelAt < logoAt && onCover(regimental.items[roundelAt]), `${size}: the roundel behind the logo on the cover`);
    assert.equal(regimental.back.items.find((item) => item.field === 'host').color, '#112233', 'the host in the first unit colour');
    // Modern: the navy bar down the spine of the cover and the inside's outer edge, words set left.
    const modern = make('modern');
    const bar = modern.items.find((item) => item.role === 'bar');
    assert.ok(bar && box(bar).left >= half - 0.5 && box(bar).right <= half + 20, `${size}: Modern's bar along the cover's spine`);
    assert.equal(modern.items.find((item) => item.field === 'coverTitle').align, 'left');
    assert.ok(box(modern.back.items.find((item) => item.role === 'bar')).left <= 0.5, `${size}: Modern's bar on the inside's outer edge`);
    for (const field of ['host', 'invite', 'when']) assert.equal(modern.back.items.find((item) => item.field === field).align, 'left', `${size}: Modern's ${field} set left`);
    // Ceremonial: a frame for the user's photo on the cover, the logo in its white box.
    const ceremonial = make('ceremonial');
    const frame = ceremonial.items.find((item) => item.role === 'photo');
    assert.ok(frame && frame.kind === 'slot' && onCover(frame), `${size}: Ceremonial's photo frame on the cover`);
    assert.ok(ceremonial.items.some((item) => item.role === 'logobox' && onCover(item)));
    // Bilingual: the occasion in Hindi with the occasion on the cover; the Hindi host and invitation inside on the left.
    const bilingual = make('bilingual');
    const occasionHi = bilingual.items.find((item) => item.field === 'occasionHi');
    assert.ok(occasionHi && onCover(occasionHi) && occasionHi.font === 'hindiFormal', `${size}: Bilingual's Hindi occasion on the cover`);
    for (const field of ['hostHi', 'inviteHi']) assert.ok(box(bilingual.back.items.find((item) => item.field === field)).right <= half + 0.5, `the Hindi ${field} on the left`);
    assert.equal(designs.sideOf(bilingual, 'occasionHi'), 'front');
    assert.equal(designs.sideOf(bilingual, 'hostHi'), 'back');
    // Classic and Navy: a double border round the cover, the host engraved, the invitation in formal script.
    for (const key of ['classic', 'navy']) {
      const card = make(key);
      assert.ok(['coverborder', 'coverborderb'].every((role) => card.items.some((item) => item.role === role && onCover(item))), `${size}: ${key}'s double border`);
      assert.equal(card.back.items.find((item) => item.field === 'host').font, 'engraved');
      assert.equal(card.back.items.find((item) => item.field === 'invite').font, 'formal');
    }
    // Regimental's invitation in formal script too.
    assert.equal(regimental.back.items.find((item) => item.field === 'invite').font, 'formal');
    // Each design's cover is its own: no two designs make the same set of parts.
    const looks = designs.designsFor(size).map((design) => make(design.key).items.map((item) => `${item.role}:${item.kind}`).sort().join());
    // Each its own: Navy and gold differs from Classic ivory in its colours and lines, and in the
    // light tile it keeps for a dark crest (UX review 8, M3).
    assert.equal(new Set(looks).size, looks.length, `${size}: each design's cover is its own`);
  }
});

// Cold review 7, M2: the back cover is framed like every other panel, with its note in the middle.
test('a folded card\'s back cover has a border and its note in the middle of the panel', () => {
  const half = core.PAGE_WIDTH / 2;
  for (const size of ['fold6', 'fold5']) {
    const height = core.pageHeight(size);
    for (const design of designs.designsFor(size)) {
      const card = designs.applyDesign(core.newCollage(size), design.key, { note: 'Kindly bring this card and your identity card' }, { measure, newId });
      const frame = card.items.find((item) => item.role === 'backcover');
      assert.ok(frame && frame.kind === 'shape' && frame.stroke, `${design.key} ${size}: the back cover has a border`);
      const f = box(frame);
      assert.ok(f.left > 0 && f.right < half, `${design.key} ${size}: the border within the back cover`);
      // As far in from the panel's edges as the cover's own border.
      const cover = box(card.items.find((item) => item.role === 'coverborder'));
      assert.ok(Math.abs(f.left - (cover.left - half)) < 1 && Math.abs(f.bottom - cover.bottom) < 1, `${design.key} ${size}: the back cover's border matches the cover's`);
      const note = card.items.find((item) => item.field === 'note');
      const n = box(note);
      assert.ok(n.left > f.left && n.right < f.right && n.top > f.top && n.bottom < f.bottom, `${design.key} ${size}: the note inside the border`);
      assert.ok(Math.abs(note.x - half / 2) < 1, `${design.key} ${size}: the note centred across the panel`);
      assert.ok(Math.abs(note.y - height / 2) < height * 0.05, `${design.key} ${size}: the note in the middle of the panel`);
      // Every panel of the card has a border, outside and inside.
      for (const role of ['innerborder', 'innerborder2']) assert.ok(card.back.items.some((item) => item.role === role), `${design.key} ${size}: ${role}`);
    }
  }
});

// UX review 8: a photo design saved without a photo left a hole. "No photo" leaves the frame out
// and the parts round it move into its space.
const NO_PHOTO_CASES = [
  ...['a5p', 'a5l', 'a6p', 'a6l', 'dlp', 'dll', 'sq5', 'sq6', 'in57p', 'in57l', 'mm:120x180', 'mm:180x120', 'mm:150x150'].map((size) => ['ceremonial', size]),
  ['ceremonial', 'fold6'], ['ceremonial', 'fold5'],
  ...['a3p', 'a4p', 'a4l'].flatMap((size) => [['photoposter', size], ['talk', size]])
];
const startFor = (key) => ({ ...designs.startDetails(key), guest: 'Brig P. Singh, SM and Mrs Singh' });
for (const [key, size] of NO_PHOTO_CASES) {
  test(`No photo: ${key} at ${size} has no frame, every part on the page, no words overlapping, and the parts move into the space`, () => {
    const opts = { measure, newId };
    const poster = designs.DESIGNS[key].poster;
    const withPhoto = designs.applyDesign(core.newCollage(size), key, startFor(key), opts);
    assert.ok(designs.hasPhotoFrame(withPhoto), 'the design has a photo frame');
    const frame = withPhoto.items.find((item) => item.role === 'photo' && item.kind === 'slot');
    assert.ok(frame, 'an empty frame to start with');
    const none = designs.setPhotoMode(withPhoto, 'none', opts);
    const fresh = designs.applyDesign(core.newCollage(size), key, { ...startFor(key), photoMode: 'none' }, opts);
    for (const card of [none, fresh]) {
      assert.equal(card.card.details.photoMode, 'none');
      assert.ok(!card.items.some((item) => item.kind === 'slot' && item.role === 'photo'), 'no photo frame');
      const height = core.pageHeight(size);
      const slack = poster ? core.PAGE_WIDTH * 0.12 : 0;
      for (const item of card.items) {
        const b = box(item);
        assert.ok(b.left >= -0.5 - slack && b.right <= core.PAGE_WIDTH + slack + 0.5 && b.top >= -0.5 && b.bottom <= height + 0.5, `${key} ${size}: ${item.role} off the page ${JSON.stringify(b)}`);
      }
      const texts = card.items.filter((item) => item.kind === 'text');
      for (let i = 0; i < texts.length; i += 1) for (let j = i + 1; j < texts.length; j += 1) {
        assert.ok(!overlap(box(texts[i]), box(texts[j])), `${key} ${size}: ${texts[i].field} overlaps ${texts[j].field}`);
      }
      const logo = card.items.find((item) => item.role === 'logo' && item.kind !== 'slot');
      if (logo) for (const words of texts) assert.ok(!overlap(box(logo), box(words)), `${key} ${size}: the logo covers ${words.field}`);
    }
    // Choosing No photo on the page lays it out as a card started with No photo.
    const placeOf = (card) => Object.fromEntries(card.items.filter((item) => item.role).map((item) => [item.role, [Math.round(item.x), Math.round(item.y), Math.round(item.width)]]));
    assert.deepEqual(placeOf(none), placeOf(fresh));
    // The parts round the frame moved into its space: up, or across and wider beside it.
    const moved = withPhoto.items.filter((item) => item.kind === 'text').filter((item) => {
      const after = none.items.find((each) => each.field === item.field);
      return after && (after.y < item.y - 1 || Math.abs(after.x - item.x) > 1 || after.width > item.width + 1);
    });
    assert.ok(moved.length >= 1, `${key} ${size}: something moved into the space`);
    for (const item of moved) {
      const after = none.items.find((each) => each.field === item.field);
      // Words beside the photo move across into its place; the rest only ever move up.
      if (Math.abs(after.x - item.x) <= 1) assert.ok(after.y <= item.y + 1, `${key} ${size}: ${item.field} never moves down`);
    }
    // A photo added afterwards brings the frame back where it was, and the parts back round it.
    const back = designs.setPhotoMode(none, '', opts);
    const again = back.items.find((item) => item.role === 'photo');
    assert.equal(again.kind, 'slot');
    assert.deepEqual([again.x, again.y, again.width], [frame.x, frame.y, frame.width]);
    assert.deepEqual(placeOf(back), placeOf(withPhoto));
    assert.equal(back.items.length, withPhoto.items.length, 'nothing extra left behind');
  });
}

test('No photo keeps the user edits: fonts, colours, places, deleted and locked parts; a photo in the frame stays', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('a5p'), 'ceremonial', startFor('ceremonial'), opts);
  const venue = (c) => c.items.find((item) => item.field === 'venue');
  const mine = {
    ...card,
    background: { type: 'color', value: '#fafafa' },
    items: card.items
      .filter((item) => item.field !== 'dress')
      .map((item) => (item.field === 'venue' ? { ...item, font: 'typewriter', color: '#123456', x: item.x + 30 } : item.field === 'note' ? { ...item, locked: true } : item))
  };
  const none = designs.setPhotoMode(mine, 'none', opts);
  const fresh = designs.setPhotoMode(card, 'none', opts);
  assert.deepEqual(none.background, mine.background);
  assert.equal(venue(none).font, 'typewriter');
  assert.equal(venue(none).color, '#123456');
  assert.ok(venue(none).y < venue(mine).y - 1, 'moved up into the space');
  assert.ok(Math.abs((venue(none).x - venue(mine).x) - (venue(fresh).x - venue(card).x)) < 1, 'moved by as much as the design moved it, from where the user put it');
  assert.ok(!none.items.some((item) => item.field === 'dress'), 'a deleted part stays deleted');
  const note = (c) => c.items.find((item) => item.field === 'note');
  assert.deepEqual(note(none), note(mine), 'a locked part stays exactly as it is');
  // The band that stands in for the photo is added, and goes when the frame comes back.
  assert.ok(none.items.some((item) => item.role === 'photoband'));
  const back = designs.setPhotoMode(none, '', opts);
  assert.ok(!back.items.some((item) => item.role === 'photoband'));
  assert.equal(venue(back).font, 'typewriter');
  assert.ok(Math.abs(venue(back).y - venue(mine).y) < 1, 'back where it was');

  // A photo in the frame: No photo leaves it, and a design change keeps it in the frame.
  const filled = { ...card, items: card.items.map((item) => (item.role === 'photo' ? core.fillSlot(item, 'p1') : item)) };
  const kept = designs.setPhotoMode(filled, 'none', opts);
  assert.equal(kept.items.find((item) => item.role === 'photo').photo, 'p1');
  const redrawn = designs.applyDesign(kept, 'ceremonial', designs.detailsOf(kept), opts);
  assert.equal(redrawn.items.find((item) => item.role === 'photo').photo, 'p1');
  // A design change keeps No photo: back to Ceremonial from Classic, still no frame.
  const classic = designs.applyDesign(none, 'classic', designs.detailsOf(none), opts);
  const ceremonial = designs.applyDesign(classic, 'ceremonial', designs.detailsOf(classic), opts);
  assert.ok(!ceremonial.items.some((item) => item.role === 'photo'));
  // A logo put back after No photo goes in the box where it is with no photo.
  const logoPlace = designs.roleItem(none, 'logo', opts);
  const logoBox = none.items.find((item) => item.role === 'logobox');
  assert.ok(Math.abs(logoPlace.y - logoBox.y) < 1);
});

test('No photo: a poster logo choice afterwards keeps No photo; a folded card shows its inside and still changes its cover', () => {
  const opts = { measure, newId };
  const poster = designs.setPhotoMode(designs.applyDesign(core.newCollage('a3p'), 'photoposter', startFor('photoposter'), opts), 'none', opts);
  const noLogo = designs.setLogoMode(poster, 'none', opts);
  assert.ok(!noLogo.items.some((item) => item.role === 'photo' || item.role === 'logo'));
  assert.equal(noLogo.card.details.photoMode, 'none');
  const folded = designs.applyDesign(core.newCollage('fold6'), 'ceremonial', startFor('ceremonial'), opts);
  const inside = core.flipSide({ ...folded, side: 'front' });
  const none = designs.setPhotoMode(inside, 'none', opts);
  assert.equal(none.side, 'back', 'still shows its inside');
  assert.ok(!none.back.items.some((item) => item.role === 'photo'), 'the cover has no frame');
  assert.ok(none.back.items.some((item) => item.role === 'photoband'));
});

test('No photo: an older draft with no photoMode opens as before, and the detail survives draft reading', () => {
  const opts = { measure, newId };
  const old = designs.applyDesign(core.newCollage('a5p'), 'ceremonial', designs.startDetails('ceremonial'), opts);
  const { photoMode, ...olderDetails } = old.card.details;
  const older = { ...old, card: { ...old.card, details: olderDetails } };
  const opened = designs.applyDesign(older, 'ceremonial', designs.detailsOf(older), opts);
  assert.ok(opened.items.some((item) => item.role === 'photo' && item.kind === 'slot'), 'the frame as before');
  const none = designs.setPhotoMode(old, 'none', opts);
  const read = core.readDraftModel(JSON.parse(JSON.stringify({ version: 1, ...none })), []);
  assert.equal(read.card.details.photoMode, 'none');
  assert.ok(!read.items.some((item) => item.role === 'photo'), 'opened with no frame');
  assert.ok(read.items.some((item) => item.role === 'photoband'), 'and with the band in the photo place');
});

// Code review 8, H1: with No photo recorded but the frame still on the page, a guest's name and a
// field typed back go where the photo arrangement puts them.
test('a field made for a page that still has its frame follows the photo arrangement, whatever photoMode says', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('a5p'), 'ceremonial', {}, opts);
  const framedButNone = { ...card, card: { ...card.card, details: { ...card.card.details, photoMode: 'none' } } };
  const line = card.items.find((item) => item.role === 'guestline');
  const guest = designs.fieldItem(framedButNone, 'guest', { ...designs.detailsOf(framedButNone), guest: 'Maj A' }, opts);
  const withPhoto = designs.fieldItem(card, 'guest', { ...designs.detailsOf(card), guest: 'Maj A' }, opts);
  assert.ok(Math.abs(guest.y - withPhoto.y) < 1, 'the name where the photo arrangement has it');
  assert.ok(guest.y < line.y + 1 && guest.y > line.y - 120, 'just above its line');
  const occasion = designs.fieldItem(framedButNone, 'occasion', designs.detailsOf(framedButNone), opts);
  const frame = card.items.find((item) => item.role === 'photo');
  assert.ok(occasion.y - core.itemHeight(occasion) / 2 >= frame.y + core.itemHeight(frame) / 2 - 1, 'the occasion below the frame, not in it');
});

// UX review 8, M2: an opened folded card reads as an invitation. The cover carries the logo and
// the occasion; the inside left the host, the invitation line and the guest's line; the inside
// right the details. Each field is on one side only, and both inside pages are balanced.
test('folded cards: the cover has the occasion, the inside reads host, invitation, guest, then the details, both pages balanced', () => {
  const half = core.PAGE_WIDTH / 2;
  for (const size of ['fold6', 'fold5']) {
    const height = core.pageHeight(size);
    for (const design of designs.designsFor(size)) {
      const key = design.key;
      const card = designs.applyDesign(core.newCollage(size), key, { ...designs.startDetails(key), guest: 'Brig P. Singh, SM and Mrs Singh' }, { measure, newId });
      const parts = designs.partsFor(key, size);
      const front = new Set(parts.front.filter((part) => part.kind === 'text').map((part) => part.field));
      const back = parts.back.filter((part) => part.kind === 'text').map((part) => part.field);
      assert.deepEqual(back.filter((field) => front.has(field)), [], `${key} ${size}: a field on both sides`);
      const cover = card.items.filter((item) => item.kind === 'text' && box(item).left >= half - 0.5);
      assert.ok(cover.some((item) => item.field === 'coverTitle'), `${key} ${size}: the cover title on the cover`);
      assert.ok(!cover.some((item) => ['host', 'invite', 'guest', 'when'].includes(item.field)), `${key} ${size}: only the title on the cover`);
      const texts = card.back.items.filter((item) => item.kind === 'text' && !item.hidden);
      const leftPage = texts.filter((item) => box(item).right <= half + 0.5);
      const rightPage = texts.filter((item) => box(item).left >= half - 0.5);
      // The left page reads in order: host, invitation, guest; then the right page's details.
      const y = (field) => texts.find((item) => item.field === field).y;
      assert.ok(y('host') < y('invite') && y('invite') < y('guest') && y('guest') < y('occasion'), `${key} ${size}: host, invitation, guest, occasion in order`);
      assert.deepEqual(leftPage.map((item) => item.field).filter((field) => ['host', 'invite', 'guest', 'occasion'].includes(field)).sort(), ['guest', 'host', 'invite', 'occasion']);
      for (const field of ['when', 'venue', 'dress', 'rsvp']) assert.ok(rightPage.some((item) => item.field === field), `${key} ${size}: ${field} on the right`);
      // Balanced: each page's words fill at least a third of its height, and their middles are
      // within 8% of the page's height of each other; no page is half empty at the bottom.
      const span = (list) => {
        const boxes = list.map(box);
        return { top: Math.min(...boxes.map((b) => b.top)), bottom: Math.max(...boxes.map((b) => b.bottom)) };
      };
      const l = span(leftPage);
      const r = span(rightPage);
      // Cold review 9, M2: centred on the page's middle (within 6%), the left page filling
      // more than half of it, and neither empty below 70%.
      for (const [name, s] of [['left', l], ['right', r]]) {
        assert.ok(s.bottom - s.top >= height / 3, `${key} ${size}: the ${name} page's words fill a third of it`);
        assert.ok(s.bottom >= height * 0.7, `${key} ${size}: the ${name} page is not empty below ${Math.round((s.bottom / height) * 100)}%`);
        assert.ok(Math.abs((s.top + s.bottom) / 2 - height / 2) <= height * 0.06, `${key} ${size}: the ${name} page is centred (${Math.round(((s.top + s.bottom) / 2 / height) * 100)}%)`);
      }
      assert.ok(l.bottom - l.top >= height / 2, `${key} ${size}: the left page's words fill half of it`);
      assert.ok(Math.abs((l.top + l.bottom) / 2 - (r.top + r.bottom) / 2) <= height * 0.08, `${key} ${size}: the two inside pages are balanced`);
    }
  }
});

// UX review 8, M3: a dark crest disappeared on the navy card and the Event poster. A light tile
// goes behind any logo but the SDD logo on those designs.
test('a light tile goes behind a crest on dark designs, and stays hidden for the SDD logo', () => {
  const opts = { measure, newId };
  const crest = (place) => core.stickerItem(newId(), 'unit:7', { ...place, aspect: 0.8 }, { role: 'logo' });
  const cases = [['navy', 'a5p'], ['navy', 'a5l'], ['navy', 'fold6'], ['event', 'a3p'], ['event', 'a4p']];
  for (const [key, size] of cases) {
    const sdd = designs.applyDesign(core.newCollage(size), key, designs.startDetails(key), opts);
    const tileOf = (card) => card.items.find((item) => item.role === 'logotile');
    // With the SDD logo (or no logo yet on a poster) the tile is there but hidden.
    assert.ok(tileOf(sdd) && tileOf(sdd).hidden === true && tileOf(sdd).autoHidden === true, `${key} ${size}: the tile hidden for the SDD logo`);
    // A crest: the design applied again keeps it, with the tile shown behind it.
    const place = sdd.items.find((item) => item.role === 'logo');
    const withCrest = { ...sdd, items: sdd.items.map((item) => (item.role === 'logo' ? crest({ x: place.x, y: place.y, width: place.width }) : item)) };
    const again = designs.applyDesign(withCrest, key, designs.detailsOf(withCrest), opts);
    const tile = tileOf(again);
    const logo = again.items.find((item) => item.role === 'logo');
    assert.ok(tile && !tile.hidden, `${key} ${size}: the tile shows behind a crest`);
    assert.ok(again.items.indexOf(tile) < again.items.indexOf(logo), `${key} ${size}: the tile is behind the logo`);
    const t = box(tile);
    const b = box(logo);
    assert.ok(t.left < b.left && t.right > b.right && t.top < b.top && t.bottom > b.bottom, `${key} ${size}: the tile round the logo`);
    assert.ok(t.top >= 0 && t.left >= 0 && t.right <= core.PAGE_WIDTH, `${key} ${size}: the tile on the page`);
    assert.ok(/^#f|^#ffffff$/i.test(tile.fill), `${key} ${size}: a light tile`);
    // Card details changing the logo: logoTileItems fits the tile; the SDD logo hides it again.
    const backToSdd = { ...again, items: again.items.map((item) => (item.role === 'logo' ? { ...item, kind: 'sticker', sticker: 'logo:sdd' } : item)) };
    assert.equal(designs.logoTileItems(backToSdd, opts).find((item) => item.role === 'logotile').hidden, true);
    assert.equal(designs.logoTileItems(again, opts).find((item) => item.role === 'logotile').hidden, undefined);
  }
  // A light design has no tile.
  const classic = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, opts);
  assert.ok(!classic.items.some((item) => item.role === 'logotile'));
  // A navy card made before the tile (an older draft) gets one when a crest is chosen, and keeps
  // opening as it was until then.
  const navy = designs.applyDesign(core.newCollage('a5p'), 'navy', {}, opts);
  const older = { ...navy, items: navy.items.filter((item) => item.role !== 'logotile') };
  assert.equal(designs.logoTileItems(older, opts).length, older.items.length, 'nothing added for the SDD logo');
  const olderCrest = { ...older, items: older.items.map((item) => (item.role === 'logo' ? crest({ x: item.x, y: item.y, width: item.width }) : item)) };
  const fitted = designs.logoTileItems(olderCrest, opts);
  const added = fitted.find((item) => item.role === 'logotile');
  assert.ok(added && !added.hidden && fitted.indexOf(added) === fitted.findIndex((item) => item.role === 'logo') - 1, 'a tile added just behind the crest');
  // The navy back's small logo gets its own tile, following the front's logo.
  const withBack = designs.addBack(olderCrest, opts);
  const backTile = withBack.back.items.find((item) => item.role === 'backlogotile');
  assert.ok(backTile && !backTile.hidden, 'the back\'s small crest has its tile');
  // A poster's No logo hides the tile with the logo.
  const poster = designs.applyDesign(core.newCollage('a3p'), 'event', designs.startDetails('event'), opts);
  const posterCrest = { ...poster, items: designs.logoTileItems({ ...poster, items: poster.items.map((item) => (item.role === 'logo' ? crest({ x: item.x, y: item.y, width: item.width }) : item)) }, opts) };
  assert.equal(posterCrest.items.find((item) => item.role === 'logotile').hidden, undefined);
  assert.equal(designs.setLogoMode(posterCrest, 'none', opts).items.find((item) => item.role === 'logotile').hidden, true);
});

// UX review 8, M5: Ceremonial photo with No photo: the logo's box, the occasion and the words
// share one left edge, and the gaps between the occasion, host, invitation and guest's line are even.
test('No photo on Ceremonial: one left edge and an even rhythm, portrait, landscape and folded', () => {
  const opts = { measure, newId };
  for (const size of ['a5p', 'a6p', 'a5l', 'a6l', 'in57p', 'fold6', 'fold5']) {
    const card = designs.applyDesign(core.newCollage(size), 'ceremonial', { ...designs.startDetails('ceremonial'), guest: 'Brig P. Singh, SM and Mrs Singh', photoMode: 'none' }, opts);
    const find = (items, test) => items.find(test);
    if (designs.isFolded(size)) {
      // The cover: the logo's box and the accent beside the occasion share the panel's left edge.
      const logoBox = box(find(card.items, (item) => item.role === 'logobox'));
      const accent = find(card.items, (item) => item.role === 'coveraccent');
      assert.ok(Math.abs(logoBox.left - accent.x) < 1.5, `${size}: the logo's box and the accent line up`);
      assert.ok(box(find(card.items, (item) => item.field === 'coverTitle')).top > logoBox.bottom, `${size}: the cover title under the logo's box`);
      const inside = card.back.items;
      const lefts = ['host', 'invite', 'guest', 'at'].map((field) => box(find(inside, (item) => item.field === field)).left);
      assert.ok(Math.max(...lefts) - Math.min(...lefts) < 1.5, `${size}: the inside left page shares one left edge`);
      continue;
    }
    const items = card.items;
    const logoBox = box(find(items, (item) => item.role === 'logobox'));
    const occasion = box(find(items, (item) => item.field === 'occasion'));
    const host = box(find(items, (item) => item.field === 'host'));
    const invite = box(find(items, (item) => item.field === 'invite'));
    const line = box(find(items, (item) => item.role === 'guestline'));
    // One left edge: the logo's box, the occasion (under the box, not beside it) and the words.
    for (const [name, b] of [['occasion', occasion], ['host', host], ['invite', invite], ['guest line', line]]) {
      assert.ok(Math.abs(b.left - logoBox.left) < 1.5, `${size}: the ${name} starts at the logo box's left edge (${b.left.toFixed(1)} and ${logoBox.left.toFixed(1)})`);
    }
    assert.ok(occasion.top > logoBox.bottom, `${size}: the occasion under the logo's box`);
    // An even rhythm: the gaps between them differ by at most half the smallest.
    const gaps = [occasion.top - logoBox.bottom, host.top - occasion.bottom, invite.top - host.bottom, line.top - invite.bottom];
    assert.ok(Math.min(...gaps) > 0, `${size}: nothing touches (${gaps.map((g) => g.toFixed(0))})`);
    assert.ok(Math.max(...gaps) <= Math.min(...gaps) * 1.8, `${size}: even gaps (${gaps.map((g) => g.toFixed(0))})`);
  }
});

// Codex on #3456: a crest moved on the page takes its tile along, and is never made smaller there.
test('fitLogoTile with resize false moves the tile with a moved logo and keeps the logo size', () => {
  const page = { width: core.PAGE_WIDTH, height: core.pageHeight('a5p') };
  const crest = { id: 'l', role: 'logo', kind: 'photo', photo: 'p', x: 500, y: 150, width: 120, aspect: 0.6 };
  const tile = { id: 't', role: 'logotile', kind: 'shape', form: 'rect', x: 500, y: 150, width: 160, aspect: 0.7, hidden: true, autoHidden: true };
  const fitted = designs.fitLogoTile([tile, crest], page);
  // Nothing changes on a second pass: the same array comes back.
  assert.equal(designs.fitLogoTile(fitted, page), fitted);
  const moved = fitted.map((item) => (item.role === 'logo' ? { ...item, x: 40, y: 60 } : item));
  const after = designs.fitLogoTile(moved, { ...page, resize: false });
  const logo = after.find((item) => item.role === 'logo');
  const shown = after.find((item) => item.role === 'logotile');
  assert.equal(logo.width, fitted.find((item) => item.role === 'logo').width, 'the logo the user placed keeps its size');
  assert.equal(shown.x, 40);
  assert.equal(shown.y, 60);
  assert.ok(!shown.hidden);
  // Deleted: the tile hides.
  const gone = designs.fitLogoTile(after.filter((item) => item.role !== 'logo'), { ...page, resize: false });
  assert.equal(gone.find((item) => item.role === 'logotile').hidden, true);
});

// Code review 9, H1: a second logo (square or 2:1) goes on the page, clear of the words, the photo
// and the first logo, on every design and size.
test('secondLogoPlace keeps a second logo on the page and clear of the words on every design and size', () => {
  const opts = { measure, newId };
  const box = (item) => { const half = core.turnedHalfBox(item.width, core.itemHeight(item), item.angle || 0); return { left: item.x - half.halfWidth, right: item.x + half.halfWidth, top: item.y - half.halfHeight, bottom: item.y + half.halfHeight }; };
  const overlap = (a, b) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1;
  const bad = [];
  let checked = 0;
  for (const key of Object.keys(designs.DESIGNS)) {
    const design = designs.DESIGNS[key];
    const group = design.poster ? 'page' : design.pocket ? 'pocket' : 'card';
    const sizes = Object.keys(core.SIZES).filter((size) => core.SIZES[size].group === group);
    for (const size of sizes) {
      for (const aspect of [1, 2]) {
        const card = designs.applyDesign(core.newCollage(size), key, designs.startDetails(key), opts);
        const first = card.items.find((item) => item.role === 'logo');
        if (!first || first.kind === 'slot') continue;
        checked += 1;
        const made = core.photoItem('own', 'ph', aspect, { x: first.x, y: first.y, width: first.width });
        const second = { ...made, role: 'logo2', y: first.y, width: core.itemHeight(first) * (made.width / core.itemHeight(made)), angle: 0 };
        const height = core.pageHeight(size);
        const place = designs.secondLogoPlace(card.items, first, second, { width: core.PAGE_WIDTH, height });
        // With the second in, Ceremonial's white box grows round both: never over the words.
        const two = card.items.map((item) => (item === first ? { ...item, x: place.firstX, y: place.firstY, width: place.firstWidth } : item));
        two.push({ ...second, x: place.x, y: place.y, width: place.width });
        const grownBox = designs.fitLogoBox(two, card.items.find((item) => item.role === 'logobox')).find((item) => item.role === 'logobox');
        if (grownBox && card.items.filter((item) => item.kind === 'text' && item.text && !item.hidden).some((item) => overlap(box(item), box(grownBox)))) bad.push(`${key} ${size} ${aspect}:1 box over the words`);
        const b = box({ ...second, x: place.x, y: place.y, width: place.width });
        const f = box({ ...first, x: place.firstX, y: place.firstY, width: place.firstWidth });
        // Never shrunk to a token: the first at half its size or more, the second at least 40% of the first's height.
        if (place.firstWidth < first.width * 0.5 || core.itemHeight({ ...second, width: place.width }) < core.itemHeight(first) * 0.4) bad.push(key + ' ' + size + ' ' + aspect + ':1 too small');
        // What the design itself puts the first logo across (Ceremonial's photo edge) is allowed.
        const words = card.items.filter((item) => !item.hidden && !['logo', 'logobox', 'logotile', 'roundel'].includes(item.role) && (item.kind !== 'text' || item.text) && !overlap(box(item), box(first)));
        const problem = b.left < 0 || b.right > core.PAGE_WIDTH || b.top < 0 || b.bottom > height ? 'off the page'
          : overlap(b, f) ? 'on the first logo'
          : words.some((item) => overlap(box(item), b) || overlap(box(item), f)) ? 'on the words' : '';
        if (problem) bad.push(`${key} ${size} ${aspect}:1 ${problem}`);
      }
    }
  }
  assert.ok(checked > 100, `checked ${checked}`);
  assert.deepEqual(bad, []);
});

// Code review 9, H1: Ceremonial's white box takes in both logos, and goes back with one.
test('fitLogoBox grows the white box round two logos and returns it to the design size with one', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('a5l'), 'ceremonial', designs.startDetails('ceremonial'), opts);
  const base = card.items.find((item) => item.role === 'logobox');
  const first = card.items.find((item) => item.role === 'logo');
  const made = core.photoItem('own', 'ph', 1, { x: first.x, y: first.y, width: first.width });
  const second = { ...made, role: 'logo2', angle: 0, width: core.itemHeight(first) * (made.width / core.itemHeight(made)) };
  const place = designs.secondLogoPlace(card.items, first, second, { width: core.PAGE_WIDTH, height: core.pageHeight('a5l') });
  const two = card.items.map((item) => (item === first ? { ...item, x: place.firstX, y: place.firstY, width: place.firstWidth } : item));
  two.push({ ...second, x: place.x, y: place.y, width: place.width });
  const grown = designs.fitLogoBox(two, base);
  const box = grown.find((item) => item.role === 'logobox');
  const inside = (outer, inner) => { const half = (i) => core.itemHeight(i) / 2; return inner.x - inner.width / 2 >= outer.x - outer.width / 2 && inner.x + inner.width / 2 <= outer.x + outer.width / 2 && inner.y - half(inner) >= outer.y - half(outer) && inner.y + half(inner) <= outer.y + half(outer); };
  for (const logo of grown.filter((item) => /^logo2?$/.test(item.role))) assert.ok(inside(box, logo), `${logo.role} inside the box`);
  assert.equal(designs.fitLogoBox(grown, base), grown, 'nothing more to change');
  const back = designs.fitLogoBox(grown.filter((item) => item.role !== 'logo2'), base).find((item) => item.role === 'logobox');
  assert.deepEqual([back.x, back.y, back.width, back.aspect], [base.x, base.y, base.width, base.aspect]);
  // A box the user moved away stays where they put it.
  const moved = grown.filter((item) => item.role !== 'logo2').map((item) => (item.role === 'logobox' ? { ...item, x: item.x + 200 } : item));
  assert.equal(designs.fitLogoBox(moved, base), moved);
});

// Cold review 9 (layout).
test('a design field keeps the edge its design sets it by: the back\'s programme its middle, a left-set occasion its left', () => {
  const opts = { measure, newId };
  const card = designs.addBack(designs.applyDesign(core.newCollage('a6p'), 'classic', {}, opts), opts);
  assert.equal(designs.fieldAnchor(card, 'programme'), null, 'the programme block is centred');
  assert.equal(designs.fieldAnchor(card, 'note'), 'right');
  assert.equal(designs.fieldAnchor(card, 'rsvp'), 'left');
  assert.equal(designs.fieldAnchor(designs.applyDesign(core.newCollage('a5p'), 'ceremonial', {}, opts), 'occasion'), 'left');
  assert.equal(designs.fieldAnchor(card, 'title'), null, 'a field the design does not place');
});

test('a folded card\'s cover title follows the occasion until other words are typed, and stays deleted when deleted', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('fold6'), 'classic', { occasion: 'Barakhana' }, opts);
  const cover = card.items.find((item) => item.field === 'coverTitle');
  assert.equal(cover.text, 'Barakhana');
  assert.equal(designs.detailsOf(card).coverTitle, '', 'showing the occasion, it follows it');
  // The occasion changed (inside), then the design changed: the cover shows the new occasion.
  const inside = { ...card, back: { ...card.back, items: card.back.items.map((item) => (item.field === 'occasion' ? { ...item, text: 'At Home' } : item)) } };
  assert.equal(designs.applyDesign(inside, 'navy', designs.detailsOf(inside), opts).items.find((item) => item.field === 'coverTitle').text, 'At Home');
  // Its own words stay its own.
  const own = { ...card, items: card.items.map((item) => (item.field === 'coverTitle' ? { ...item, text: 'You are invited' } : item)) };
  assert.equal(designs.detailsOf(own).coverTitle, 'You are invited');
  assert.equal(designs.applyDesign(own, 'modern', designs.detailsOf(own), opts).items.find((item) => item.field === 'coverTitle').text, 'You are invited');
  // Deleted on the page: no cover title after a design change.
  const gone = { ...card, items: card.items.filter((item) => item.field !== 'coverTitle') };
  assert.ok(!designs.applyDesign(gone, 'regimental', designs.detailsOf(gone), opts).items.some((item) => item.field === 'coverTitle'));
  // A flat card has no cover title.
  assert.ok(!designs.fieldsOf('classic', 'a5p').includes('coverTitle'));
});

test('Save closes a poster\'s empty logo place: "Decide later" prints as No logo, a chosen logo or a card is left as it is', () => {
  const opts = { measure, newId };
  const later = designs.applyDesign(core.newCollage('a4p'), 'notice', { ...designs.startDetails('notice'), logoMode: 'later' }, opts);
  assert.ok(later.items.some((item) => item.role === 'logo' && item.kind === 'slot'));
  const printed = designs.closeLogoPlace(later, opts);
  const none = designs.applyDesign(core.newCollage('a4p'), 'notice', { ...designs.startDetails('notice'), logoMode: 'none' }, opts);
  assert.ok(!printed.items.some((item) => item.role === 'logo'), 'no logo place printed');
  const y = (c, field) => c.items.find((item) => item.field === field).y;
  assert.ok(Math.abs(y(printed, 'title') - y(none, 'title')) < 0.5, 'the heading moves up as with No logo');
  assert.ok(later.items.some((item) => item.role === 'logo'), 'the poster itself keeps its place');
  const card = designs.applyDesign(core.newCollage('a5p'), 'classic', {}, opts);
  assert.equal(designs.closeLogoPlace(card, opts), card);
  const chosen = { ...later, items: later.items.map((item) => (item.role === 'logo' ? core.stickerItem(item.id, 'logo:sdd', { x: item.x, y: item.y, width: item.width, aspect: designs.SDD_ASPECT }, { role: 'logo' }) : item)) };
  assert.equal(designs.closeLogoPlace(chosen, opts), chosen);
});

test('no wrapped design line ends with one word alone, on every design at every size it is offered', () => {
  const opts = { measure, newId };
  const posterSizes = ['a4p', 'a3p', 'a4l', 'a3l'].filter((size) => core.SIZES[size]);
  for (const key of Object.keys(designs.DESIGNS)) {
    const design = designs.DESIGNS[key];
    const sizes = design.poster ? posterSizes : Object.keys(core.SIZES).filter((size) => designs.designsFor(size).some((each) => each.key === key));
    for (const size of sizes) {
      const card = designs.applyDesign(core.newCollage(size), key, designs.startDetails(key), opts);
      for (const item of [...card.items, ...(card.back?.items || [])].filter((each) => each.kind === 'text' && each.maxLines > 1)) {
        const size0 = core.layoutText(item, measure).size;
        for (const text of item.text.split(String.fromCharCode(10)).filter((line) => line.trim().split(/ +/).length > 3)) {
          const layout = core.layoutText({ ...item, text, size: size0, minSize: size0, maxLines: 99 }, measure);
          const last = layout.glyphs[layout.glyphs.length - 1].ch.trim();
          assert.ok(layout.lines < 2 || / /.test(last), `${key} ${size}: ${item.field} ends with "${last}" alone`);
        }
      }
    }
  }
});

test('No photo on Ceremonial landscape uses the whole width: words on both halves, balanced across the page', () => {
  const opts = { measure, newId };
  for (const size of cardSizes.filter((key) => !designs.isPortrait(key))) {
    const card = designs.applyDesign(core.newCollage(size), 'ceremonial', { ...designs.startDetails('ceremonial'), photoMode: 'none' }, opts);
    const boxes = card.items.filter((item) => item.kind === 'text' && !['rsvp', 'note'].includes(item.field)).map(box);
    const left = Math.min(...boxes.map((b) => b.left));
    const right = Math.max(...boxes.map((b) => b.right));
    assert.ok(right > core.PAGE_WIDTH * 0.85, `${size}: words reach the right of the page (${right.toFixed(0)})`);
    assert.ok(Math.abs((left + right) / 2 - core.PAGE_WIDTH * 0.535) < core.PAGE_WIDTH * 0.06, `${size}: the words are balanced across the page beside the band`);
    assert.ok(boxes.some((b) => b.left > core.PAGE_WIDTH / 2), `${size}: details on the right half`);
  }
});

test('a long guest name with rank and decorations stays readable on A6 and A5 in every design, clear of the words around it', () => {
  const opts = { measure, newId };
  for (const name of ['Lt Gen A. K. Singh, PVSM, AVSM, VSM, ADC and Mrs Sunita Singh', 'Maj Gen Hari Narayan Venkataraghavan Subramaniam, AVSM, SM, VSM and Mrs Subramaniam']) {
    for (const size of ['a6p', 'a5p']) {
      const mmPerUnit = core.sizeOf(size).mm[0] / core.PAGE_WIDTH;
      for (const design of designs.designsFor(size)) {
        const card = designs.applyDesign(core.newCollage(size), design.key, { ...designs.startDetails(design.key), guest: name, photoMode: 'none' }, opts);
        const side = card.items.some((item) => item.field === 'guest') ? card.items : card.back.items;
        const guest = side.find((item) => item.field === 'guest');
        const layout = core.layoutText(guest, measure);
        const pt = layout.size * mmPerUnit * 72 / 25.4;
        assert.ok(pt >= 6.5, `${design.key} ${size}: the name prints at ${pt.toFixed(1)} pt`);
        assert.ok(layout.lines <= 2 && layout.width <= guest.maxWidth + 0.5, `${design.key} ${size}: the name on at most two lines`);
        for (const other of side.filter((item) => item.kind === 'text' && item !== guest)) assert.ok(!overlap(box(guest), box(other)), `${design.key} ${size}: the name overlaps ${other.field}`);
      }
    }
  }
  // A short name keeps the design's face, one line, full size.
  const short = designs.applyDesign(core.newCollage('a6p'), 'classic', { guest: 'Col P. Singh' }, opts).items.find((item) => item.field === 'guest');
  assert.deepEqual([short.font, short.maxLines], ['script', 1]);
});

test('a two-line occasion keeps "at the" and the accent rule clear of it, on every card size', () => {
  const opts = { measure, newId };
  for (const key of ['classic', 'navy', 'regimental', 'modern', 'pocketinvite']) {
    for (const size of designs.DESIGNS[key].pocket ? pocketSizes.filter((each) => designs.designsFor(each).some((d) => d.key === key)) : cardSizes) {
      const card = designs.applyDesign(core.newCollage(size), key, { occasion: 'Change of Command Parade and Reception', guest: 'Brig P. Singh' }, opts);
      const occasion = card.items.find((item) => item.field === 'occasion');
      const o = box(occasion);
      const gap = core.layoutText(occasion, measure).size * 0.2;
      const at = card.items.find((item) => item.field === 'at');
      if (at && at.y < occasion.y) assert.ok(box(at).bottom + gap <= o.top + 0.5, `${key} ${size}: "at the" crowds the occasion`);
      const accent = card.items.find((item) => item.role === 'rule' && item.y > occasion.y);
      if (accent) assert.ok(accent.y >= o.bottom + gap - 0.5, `${key} ${size}: the rule crowds the occasion`);
      const others = card.items.filter((item) => item.kind === 'text' && item !== at && item !== occasion);
      if (at) for (const other of others) assert.ok(!overlap(box(at), box(other)), `${key} ${size}: "at the" overlaps ${other.field}`);
      const line = card.items.find((item) => item.role === 'guestline');
      if (at && line) assert.ok(box(at).top >= line.y, `${key} ${size}: "at the" reaches the guest's line`);
      if (accent) for (const other of others) assert.ok(box(other).top > accent.y || box(other).bottom < accent.y, `${key} ${size}: the rule crosses ${other.field}`);
    }
  }
});

test('a landscape back has the small logo too, inside its border and clear of the words', () => {
  const opts = { measure, newId };
  for (const key of Object.keys(designs.DESIGNS).filter((each) => !designs.DESIGNS[each].pocket && !designs.DESIGNS[each].poster)) {
    for (const size of cardSizes.filter((each) => !designs.isPortrait(each))) {
      const card = designs.addBack(designs.applyDesign(core.newCollage(size), key, {}, opts), opts);
      const logo = card.back.items.find((item) => item.role === 'backlogo');
      assert.ok(logo, `${key} ${size}: a small logo on the back`);
      const frame = box(card.back.items.find((item) => item.role === 'backborder'));
      const b = box(logo);
      assert.ok(b.top > frame.top && b.bottom < frame.bottom, `${key} ${size}: the logo inside the border`);
      for (const words of card.back.items.filter((item) => item.kind === 'text')) assert.ok(!overlap(b, box(words)), `${key} ${size}: the logo covers ${words.field}`);
    }
  }
});

// Codex on #3456: a turned crest is covered by its tile across the room it really takes.
test('the logo tile covers a crest turned on the page', () => {
  const page = { width: core.PAGE_WIDTH, height: core.pageHeight('a5p'), resize: false };
  const crest = { id: 'l', role: 'logo', kind: 'photo', photo: 'p', x: 500, y: 400, width: 100, aspect: 0.5, angle: 90 };
  const tile = { id: 't', role: 'logotile', kind: 'shape', form: 'rect', x: 500, y: 400, width: 120, aspect: 0.6 };
  const fitted = designs.fitLogoTile([tile, crest], page).find((item) => item.role === 'logotile');
  assert.ok(fitted.width >= 200, `tile ${fitted.width} wide for a crest 200 wide when turned`);
  assert.ok(core.itemHeight(fitted) >= 100);
});

// Codex on #3460: a locked guest's name is never changed by laying the page out again.
test('a locked two-line guest name is left as it is when the page is laid out', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('a5l'), 'classic', designs.startDetails('classic'), opts);
  const long = 'Lt Gen A. K. Singh, PVSM, AVSM, VSM, ADC and Mrs Sunita Singh, with their daughter';
  const guest = designs.fieldItem(card, 'guest', { ...designs.detailsOf(card), guest: long }, opts);
  const locked = { ...guest, locked: true, y: guest.y - 30, minSize: 8 };
  const withGuest = { ...card, items: [...card.items, locked] };
  const before = JSON.stringify(locked);
  designs.fieldItem(withGuest, 'occasion', designs.detailsOf(withGuest), opts);
  designs.applyDesign(withGuest, 'classic', designs.detailsOf(withGuest), opts);
  assert.equal(JSON.stringify(locked), before, 'the locked item of the collage is unchanged');
});

// Codex on #3460: a folded card made before the cover title (the occasion itself on the cover)
// gets no second title on its cover.
test('mayFollow: no following cover title where the occasion itself is on the cover', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('fold6'), 'classic', designs.startDetails('classic'), opts);
  assert.equal(designs.mayFollow(card, 'coverTitle'), true, 'a card of today: the occasion is inside');
  // As an older PRISM saved it: the cover's title is the occasion, and the inside has none.
  const cover = card.items.find((item) => item.field === 'coverTitle');
  const legacy = {
    ...card,
    items: card.items.map((item) => (item === cover ? { ...item, field: 'occasion' } : item)),
    back: { ...card.back, items: card.back.items.filter((item) => item.field !== 'occasion') },
    card: { ...card.card, placed: (card.card.placed || []).filter((field) => field !== 'coverTitle') }
  };
  const noCover = { ...legacy, items: legacy.items };
  assert.equal(designs.mayFollow(noCover, 'coverTitle'), false);
  assert.equal(designs.mayFollow(card, 'occasion'), false, 'only a field that follows another');
});

// Codex on #3460: a following cover title takes the occasion's words however they were changed.
test('followFallbacks: the cover still following takes the occasion edited on the page; own words stay', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('fold6'), 'classic', { ...designs.startDetails('classic'), occasion: 'Barakhana' }, opts);
  const coverOf = (collage) => collage.items.find((item) => item.field === 'coverTitle');
  assert.equal(coverOf(card).text, 'Barakhana');
  assert.equal(designs.followFallbacks(card, measure), card, 'nothing to follow: the same card');
  const typed = { ...card, back: { ...card.back, items: card.back.items.map((item) => (item.field === 'occasion' ? { ...item, text: 'Regimental Dinner' } : item)) } };
  const followed = designs.followFallbacks(typed, measure);
  assert.equal(coverOf(followed).text, 'Regimental Dinner');
  assert.equal(followed.card.details.occasion, 'Regimental Dinner');
  assert.ok(coverOf(followed).width > coverOf(card).width, 'measured for its new words');
  // Words the user typed on the cover itself are theirs.
  const own = { ...typed, items: typed.items.map((item) => (item.field === 'coverTitle' ? { ...item, text: 'You are invited' } : item)) };
  assert.equal(designs.followFallbacks(own, measure), own);
  // A cover title with words of its own in the details does not follow.
  const set = { ...typed, card: { ...typed.card, details: { ...typed.card.details, coverTitle: 'Dinner Night' } } };
  assert.equal(designs.followFallbacks(set, measure), set);
});

// Codex on #3460: a left-set cover title keeps its left edge when it follows longer words.
test('followFallbacks keeps the edge the design sets the cover title by', () => {
  const opts = { measure, newId };
  const card = designs.applyDesign(core.newCollage('fold6'), 'ceremonial', { ...designs.startDetails('ceremonial'), occasion: 'Tea' }, opts);
  const coverOf = (collage) => collage.items.find((item) => item.field === 'coverTitle');
  const before = coverOf(card);
  assert.equal(designs.fieldAnchor(card, 'coverTitle'), 'left', 'Ceremonial sets its cover title by its left edge');
  const typed = { ...card, back: { ...card.back, items: card.back.items.map((item) => (item.field === 'occasion' ? { ...item, text: 'Change of Command Parade and Reception' } : item)) } };
  const after = coverOf(designs.followFallbacks(typed, measure));
  assert.ok(Math.abs((after.x - after.width / 2) - (before.x - before.width / 2)) < 0.5, 'the same left edge');
  assert.ok(after.x - after.width / 2 >= core.PAGE_WIDTH / 2, 'never across the fold');
});

// docs/ux/collage-usability-plan.md, round 3, PR 3: a poster's date band follows its date.
test('the Event poster hides its date band while the date is empty, and shows it once dated', () => {
  const band = (poster) => poster.items.find((item) => item.role === 'band');
  const empty = designs.applyDesign(core.newCollage('a3p'), 'event', { ...designs.startDetails('event'), when: '' }, { measure, newId });
  assert.equal(band(empty).hidden, true);
  assert.equal(band(empty).autoHidden, true);
  const dated = designs.applyDesign(core.newCollage('a3p'), 'event', designs.startDetails('event'), { measure, newId });
  assert.equal(band(dated).hidden, undefined);
  // Typed later: the band comes back; emptied again: it goes.
  const items = empty.items.map((item) => ({ ...item }));
  items.push({ id: 'w', kind: 'text', field: 'when', text: 'Saturday, 10 January 2027' });
  designs.followBand(items);
  assert.equal(items.find((item) => item.role === 'band').hidden, undefined);
  items.find((item) => item.field === 'when').text = '  ';
  designs.followBand(items);
  assert.equal(items.find((item) => item.role === 'band').hidden, true);
  // A band the user hid stays hidden; a locked band is left as it is.
  const own = [{ role: 'band', hidden: true }, { kind: 'text', field: 'when', text: 'Today' }];
  designs.followBand(own);
  assert.equal(own[0].hidden, true);
  const locked = [{ role: 'band', locked: true }];
  designs.followBand(locked);
  assert.equal(locked[0].hidden, undefined);
  assert.ok(designs.AUTO_PARTS.includes('band'));
});
