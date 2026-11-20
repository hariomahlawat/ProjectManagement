// Save's pure rules (docs/ux/collage-review-3-fixes.md, PR 6): which purpose a type and quality
// match, the line saying what Save will make, and where Tab goes at the ends of the dialog.
const test = require('node:test');
const assert = require('node:assert/strict');
const save = require('./collage-save.js');

test('a PDF of cards on print sheets is summed up in print words', () => {
  assert.equal(save.saveSummary({ format: 'pdf', quality: 'print', pixels: { width: 1240, height: 1748 }, paper: 'A6 card', sheet: { count: 2, label: 'A4' } }),
    'A PDF at print quality: 2 × A6 card to each A4 sheet, at true size.');
  assert.equal(save.saveSummary({ format: 'pdf', quality: 'print', pixels: { width: 1, height: 1 }, paper: 'Entry card or car pass', sheet: { count: 10, label: 'A4' } }),
    'A PDF at print quality: 10 × Entry card or car pass to each A4 sheet, at true size.');
  assert.equal(save.saveSummary({ format: 'pdf', quality: 'print', pixels: { width: 10, height: 10 }, paper: 'A5 card', sheet: { count: 1, label: 'A4' } }),
    'A PDF at print quality: one A5 card to each A4 sheet, at true size.');
});

test('each purpose sets a type and quality, and a hand-made choice matches no purpose', () => {
  assert.equal(save.purposeFor('jpeg', 'screen'), 'share');
  assert.equal(save.purposeFor('pdf', 'print'), 'print');
  assert.equal(save.purposeFor('png', 'screen'), 'slide');
  assert.equal(save.purposeFor('png', 'print'), null);
  assert.equal(save.purposeFor('jpeg', 'print'), null);
  assert.equal(save.purposeFor(undefined, undefined), null);
  for (const [name, choice] of Object.entries(save.SAVE_PURPOSES)) {
    assert.equal(save.purposeFor(choice.format, choice.quality), name);
  }
});

test('the line under the purposes says what Save will make', () => {
  assert.equal(
    save.saveSummary({ format: 'jpeg', quality: 'screen', pixels: { width: 1920, height: 1358 } }),
    'A JPG picture at screen size: 1,920 × 1,358 pixels.');
  assert.equal(
    save.saveSummary({ format: 'jpeg', quality: 'screen', pixels: { width: 1920, height: 1358 }, target: '500 KB' }),
    'A JPG picture at screen size: 1,920 × 1,358 pixels, kept under 500 KB.');
  assert.equal(
    save.saveSummary({ format: 'pdf', quality: 'print', pixels: { width: 3508, height: 2480 }, paper: 'A4 landscape' }),
    'A PDF at print quality: A4 landscape at its true size.');
  assert.equal(
    save.saveSummary({ format: 'pdf', quality: 'screen', pixels: { width: 1920, height: 1080 } }),
    'A PDF at screen size: 1,920 × 1,080 pixels.');
  // A size to keep under belongs to a JPG only.
  assert.equal(
    save.saveSummary({ format: 'png', quality: 'print', pixels: { width: 3840, height: 2160 }, target: '500 KB' }),
    'A PNG picture at print quality: 3,840 × 2,160 pixels.');
});

test('Tab wraps at the ends of the dialog and is left alone in between', () => {
  const stops = ['close', 'share', 'more', 'save'];
  assert.equal(save.wrapFocus(stops, 'save', false), 'close');
  assert.equal(save.wrapFocus(stops, 'close', true), 'save');
  assert.equal(save.wrapFocus(stops, 'share', false), null);
  assert.equal(save.wrapFocus(stops, 'more', true), null);
  // Focus outside the stops (the dialog itself, or the page behind) comes back in.
  assert.equal(save.wrapFocus(stops, 'body', false), 'close');
  assert.equal(save.wrapFocus(stops, 'body', true), 'save');
  assert.equal(save.wrapFocus([], 'body', false), null);
});

test('Save says what it leaves out: empty frames and hidden items (docs/ux/collage-review-4-fixes.md, PR 6)', () => {
  assert.equal(save.leftOutLine(0, 0), '');
  assert.equal(save.leftOutLine(1, 0), 'The empty frame is left out of the saved picture, so its place stays blank. Add a photo to it, or remove it in Layers.');
  assert.equal(save.leftOutLine(3, 0), 'The 3 empty frames are left out of the saved picture, so their places stay blank. Add photos to them, or remove them in Layers.');
  assert.equal(save.leftOutLine(0, 1), '1 hidden item left out; show it from Layers to keep it.');
  assert.equal(save.leftOutLine(0, 2), '2 hidden items left out; show them from Layers to keep them.');
  assert.equal(save.leftOutLine(2, 2), 'The 2 empty frames are left out of the saved picture, so their places stay blank. Add photos to them, or remove them in Layers. 2 hidden items left out; show them from Layers to keep them.');
  // A design's photo frame points to No photo, where the design closes the gap (UX review 8).
  assert.equal(save.leftOutLine(1, 0, 1, 'Poster'), "The design's photo frame is empty, so its place stays blank in the saved picture. Add a photo to it, or choose No photo in Poster details and the design closes the gap.");
  assert.match(save.leftOutLine(2, 0, 1), /No photo in Card details.*Another empty frame is left out/);
});

// Save's print rules (cold review 2): the sides chosen, which side gets each guest's name, the
// sheets of guest cards, and a name kept at its field's edge.
test('the sides chosen are saved front first, each named', () => {
  assert.deepEqual(save.chosenSides(['F', 'B'], 'both'), { pages: ['F', 'B'], names: ['front', 'back'] });
  assert.deepEqual(save.chosenSides(['F', 'B'], 'front'), { pages: ['F'], names: ['front'] });
  assert.deepEqual(save.chosenSides(['F', 'B'], 'back'), { pages: ['B'], names: ['back'] });
  assert.deepEqual(save.chosenSides(['F'], 'back'), { pages: ['F'], names: [''] });
});

test('each guest\'s name goes on the side that holds it, and only the sides chosen are kept', () => {
  const name = (page, who) => `${page}:${who}`;
  // A flat card: the name on the front, the shared back after it.
  assert.deepEqual(save.guestCards(['F', 'B'], ['F', 'B'], ['A', 'B2'], 0, name).cards,
    [{ front: 'F:A', back: 'B' }, { front: 'F:B2', back: 'B' }]);
  // A folded card: the name inside (the back), the outside shared.
  assert.deepEqual(save.guestCards(['O', 'I'], ['O', 'I'], ['A'], 1, name).cards, [{ front: 'O', back: 'I:A' }]);
  // Inside only: no outside page.
  assert.deepEqual(save.guestCards(['O', 'I'], ['I'], ['A'], 1, name).cards, [{ front: null, back: 'I:A' }]);
  // Outside only: the names are not saved, but the outside still comes once a guest, to match
  // the insides printed apart (Codex on PR 10).
  assert.deepEqual(save.guestCards(['O', 'I'], ['O'], ['A', 'B2'], 1, name), { cards: [{ front: 'O', back: null }, { front: 'O', back: null }], namesLeftOut: true });
  assert.deepEqual(save.guestCards(['F', 'B'], ['B'], ['A', 'B2', 'C'], 0, name).cards.map((card) => card.back), ['B', 'B', 'B']);
  assert.deepEqual(save.guestCards(['F'], ['F'], [], 0, name), { cards: null, namesLeftOut: false });
  // Fronts only: each guest's front, and no back (tests review 5, M4).
  assert.deepEqual(save.guestCards(['F', 'B'], ['F'], ['A', 'B2'], 0, name).cards, [{ front: 'F:A', back: null }, { front: 'F:B2', back: null }]);
});

test('guest cards fill sheets: each batch\'s fronts, then its backs, slot by slot', () => {
  const cards = [1, 2, 3].map((i) => ({ front: `f${i}`, back: `b${i}` }));
  assert.deepEqual(save.guestSheets(cards, 2), [
    { back: false, pages: ['f1', 'f2'] }, { back: true, pages: ['b1', 'b2'] },
    { back: false, pages: ['f3'] }, { back: true, pages: ['b3'] }
  ]);
  assert.deepEqual(save.guestSheets([{ front: 'f1', back: null }], 4), [{ back: false, pages: ['f1'] }]);
});

test('a guest\'s name keeps its field\'s edge: left, right or the middle', () => {
  const field = { x: 500, width: 400, text: '____', align: 'left' };
  const layout = { width: 200, height: 50 };
  assert.equal(save.nameInField(field, 'Maj A', layout).x, 400);
  assert.equal(save.nameInField({ ...field, align: 'right' }, 'Maj A', layout).x, 600);
  assert.equal(save.nameInField({ ...field, align: 'center' }, 'Maj A', layout).x, 500);
  assert.equal(save.nameInField(field, 'Maj A', layout).text, 'Maj A');
  assert.equal(save.nameInField(field, 'Maj A', layout).width, 200);
  assert.equal(save.nameInField({ ...field, hidden: true }, 'Maj A', layout).hidden, undefined, 'a hidden line still shows the name');
  // A name on two lines keeps its bottom where the field's was.
  const one = { ...field, y: 1000, aspect: 8 }; // 400 wide, 50 tall: from 975 to 1025
  const two = save.nameInField(one, 'A long name', { width: 380, height: 100 });
  assert.equal(two.y + 100 / 2, 1025, 'the bottom stays on the line');
  assert.equal(save.nameInField(one, 'Maj A', { width: 200, height: 50 }).y, 1000, 'one line stays put');
  // A field that held two lines (100 tall, 950 to 1050): one line moves down onto its line.
  const two2 = { ...field, y: 1000, aspect: 4 };
  assert.equal(save.nameInField(two2, 'Maj A', { width: 200, height: 50 }).y + 25, 1050, 'down to the line');
});

test('the name of a guest moves with the line drawn to write on', () => {
  const name = { x: 500, y: 600, text: 'Maj A' };
  assert.deepEqual(save.onTheLine(name, { x: 500, y: 740 }, { x: 500, y: 620 }), { x: 500, y: 720, text: 'Maj A' });
  assert.equal(save.onTheLine(name, null, { x: 1, y: 1 }), name);
});

// Cold review 5: a long guest name breaks before its "and", not in the middle of a name.
test('a guest name too long for one line breaks before its "and"', () => {
  // Ten units a letter, 200 wide: a line holds 20 letters.
  const layout = (item) => {
    const lines = String(item.text).split(String.fromCharCode(10)).flatMap((line) => {
      const out = [];
      let current = '';
      for (const word of line.split(' ')) {
        const next = current ? `${current} ${word}` : word;
        if (next.length * 10 > item.maxWidth && current) { out.push(current); current = word; } else current = next;
      }
      return [...out, current];
    });
    return { lines: lines.length, width: Math.max(...lines.map((line) => line.length * 10)), height: lines.length * 12 };
  };
  const item = { maxWidth: 200 };
  const nl = String.fromCharCode(10);
  assert.equal(save.guestText(item, 'Maj A Rao and Mrs Rao', layout), 'Maj A Rao and Mrs Rao'.replace(' and ', nl + 'and '));
  assert.equal(save.guestText(item, 'Col R. Mehta & Mrs Mehta', layout), 'Col R. Mehta' + nl + '& Mrs Mehta');
  assert.equal(save.guestText(item, 'Maj A and Mrs B', layout), 'Maj A and Mrs B', 'one line stays one line');
  assert.equal(save.guestText(item, 'Brig P. Singh Commander', layout), 'Brig P. Singh Commander', 'no "and", no change');
  // Breaking at "and" would take a third line where the words wrap to two: they wrap as they would.
  const long = 'Maj A and Mrs Bhattacharya Rao';
  assert.equal(save.guestText(item, long, layout), long);
});

// Cold review 9: facts about a design's parts live with the designs; Save and Card details use them.
const core = require('./collage-core.js');
const designs = require('./collage-designs.js');
const measure = (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) * 0.56;
let sequence = 0;
const opts = { measure, newId: () => `s${++sequence}` };
const made = (key, size, details = {}) => designs.applyDesign(core.newCollage(size), key, details, opts);

test('every part that takes a unit colour has words for it, and the parts a design hides itself are named by the designs', () => {
  for (const key of Object.keys(designs.DESIGNS)) {
    for (const size of ['a5p', 'a5l', 'fold5']) {
      const card = designs.addBack(made(key, size), opts);
      for (const item of [...card.items, ...(card.back?.items || [])]) {
        if (!item.tint || item.field || /^band\d$/.test(item.role || '')) continue;
        assert.equal(typeof designs.COLOUR_USES[item.role], 'string', `${key} ${size}: ${item.role}`);
      }
    }
  }
  assert.ok(['roundel', 'logotile', 'backlogotile'].every((role) => designs.AUTO_PARTS.includes(role)));
  assert.equal(designs.COLOUR_USES.constructor, undefined, 'a role from a draft never reads a built-in');
});

// UX review 9, L2: every Hindi line on the card left empty is named, the host as well as the occasion.
test('Save names every empty Hindi line on a Bilingual card, and none on a card without Hindi', () => {
  const card = made('bilingual', 'a5p');
  assert.equal(save.hindiLine(designs, card), '');
  // As the card is after an English change empties its Hindi (designs.hindiInStep).
  const empty = (fields) => made('bilingual', 'a5p', Object.fromEntries(fields.map((field) => [field, ''])));
  assert.match(save.hindiLine(designs, empty(['hostHi'])), /The Hindi host is empty/);
  assert.match(save.hindiLine(designs, empty(['occasionHi', 'hostHi'])), /The Hindi host and occasion are empty|The Hindi occasion and host are empty/);
  assert.match(save.hindiLine(designs, empty(['occasionHi', 'hostHi', 'inviteHi'])), /, .* and .* are empty, so the card shows only the English there: type them in Card details\.$/);
  // An English line left empty too prints nothing in either column: nothing to say.
  assert.equal(save.hindiLine(designs, empty(['hostHi', 'host'])), '');
  assert.equal(save.hindiLine(designs, made('classic', 'a5p')), '');
  assert.equal(save.hindiLine(null, card), '');
});

// UX review 9, L7: colour to the page's edge is decided once, from what the page shows.
test('colour runs to the edge on Navy and gold, Regimental and Modern; not on Classic or Bilingual', () => {
  assert.equal(core.colourToEdge(made('navy', 'a5p')), true, 'a navy background');
  assert.equal(core.colourToEdge(made('regimental', 'a5l')), true, 'bands to the edge');
  assert.equal(core.colourToEdge(made('modern', 'a6p')), true, 'a bar down the edge');
  assert.equal(core.colourToEdge(made('classic', 'a5p')), false, 'cream paper with an inset border');
  assert.equal(core.colourToEdge(made('bilingual', 'a5l')), false);
  // Either side: a Classic front with a dark back.
  const classic = designs.addBack(made('classic', 'a5p'), opts);
  assert.equal(core.colourToEdge({ ...classic, back: { ...classic.back, background: { type: 'color', value: '#0f1f3d' } } }), true);
  // A photo collage: a photo touching the edge, or a gradient behind.
  const page = core.newCollage('a4p');
  assert.equal(core.colourToEdge(page), false);
  assert.equal(core.colourToEdge({ ...page, background: { type: 'gradient', key: 'ocean' } }), true);
  const photo = core.photoItem('p', 'photo1', 1.5, { x: 200, y: 200, width: 400 });
  assert.equal(core.colourToEdge({ ...page, items: [photo] }), true);
  assert.equal(core.colourToEdge({ ...page, items: [{ ...photo, x: 500, y: 500 }] }), false);
  assert.equal(core.colourToEdge({ ...page, items: [{ ...photo, hidden: true }] }), false);
  assert.match(save.EDGE_NOTE, /white margin/);
});

// Code review 9, L3: a design's place for a field or a part is the front's, whichever side shows.
test('the place of a front field or part is the same with the back showing', () => {
  // A Ceremonial card whose details say No photo while its photo frame is still on the front (an
  // older PRISM put it back): the front is laid out with its photo, from either side.
  const card = designs.addBack(made('ceremonial', 'a5p'), opts);
  const framed = { ...card, card: { ...card.card, details: { ...card.card.details, photoMode: 'none' } } };
  assert.ok(framed.items.some((item) => item.role === 'photo'));
  for (const showing of [card, framed]) {
    const turned = core.flipSide(showing);
    for (const field of ['host', 'occasion', 'invite']) {
      const front = designs.fieldItem(showing, field, designs.detailsOf(showing), opts);
      const back = designs.fieldItem(turned, field, designs.detailsOf(turned), opts);
      assert.deepEqual([back.x, back.y, back.width], [front.x, front.y, front.width], field);
    }
    const logo = designs.roleItem(showing, 'logo', opts);
    const logoTurned = designs.roleItem(turned, 'logo', opts);
    assert.deepEqual([logoTurned.x, logoTurned.y, logoTurned.width], [logo.x, logo.y, logo.width]);
  }
});

// docs/ux/collage-usability-plan.md, round 2, PR 3: a new card's date starts empty, and Save says so.
test('Save names an empty date on a card or poster, and says nothing once it has words', () => {
  assert.equal(save.dateLine(designs, made('classic', 'a5p')), '');
  assert.equal(save.dateLine(designs, made('classic', 'a5p', { when: '' })),
    ' The date and time are empty: type them in Card details, or save without them.');
  assert.equal(save.dateLine(designs, made('classic', 'a5p', { when: '   ' })),
    ' The date and time are empty: type them in Card details, or save without them.');
  assert.match(save.dateLine(designs, made('notice', 'a3p', { when: '' })), /Poster details/);
  // A plain collage, and nothing at all, have no date to name.
  assert.equal(save.dateLine(designs, { size: 'a4l', items: [] }), '');
  assert.equal(save.dateLine(null, null), '');
});

// docs/ux/collage-usability-plan.md, round 3, PR 1: Save opens on the purpose that fits.
test('Save opens on Printing for a card or poster, and for a collage on what its page is for', () => {
  const core = { isSize: (key) => typeof key === 'string' && key.length > 0 };
  assert.equal(save.fittingPurpose({ size: 'a5p', card: { design: 'classic' } }, core), 'print');
  assert.equal(save.fittingPurpose({ size: 'square', card: { design: 'event' } }, core), 'print');
  assert.equal(save.fittingPurpose({ size: 'square', items: [] }, core), 'share');
  assert.equal(save.fittingPurpose({ size: 'story', items: [] }, core), 'share');
  assert.equal(save.fittingPurpose({ size: 'slide', items: [] }, core), 'slide');
  assert.equal(save.fittingPurpose({ size: 'a4l', items: [] }, core), 'print');
  assert.equal(save.fittingPurpose({ size: 'mm:120x180', items: [] }, core), 'print');
  assert.equal(save.fittingPurpose(null, core), 'share');
});
