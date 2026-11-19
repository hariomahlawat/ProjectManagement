// PRISM Collage Studio — ready-made card designs as data (docs/ux/invitations-posters.md, PR 4b)
//
// A design says where each part of a card goes, as fractions of the page, and how it looks.
// Sizes (letters, logo, lines) scale with the page's shorter side, so one design fits every card
// size; each design has a portrait and a landscape arrangement. applyDesign() turns a design and
// the card's wording into ordinary collage items: everything stays editable, and a text item
// keeps the `field` it shows, so the wording carries over when the design changes.
(function (root, factory) {
  'use strict';
  const api = factory((root && root.PrismCollage) || (typeof require === 'function' ? require('./collage-core.js') : null));
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageDesigns = api;
})(typeof self !== 'undefined' ? self : typeof globalThis !== 'undefined' ? globalThis : this, function (core) {
  'use strict';

  // Tables looked up by a name from a draft or a file have no prototype (core.lookupTable).
  const { lookupTable } = core;

  // A card with its back showing is worked on from its front, then shown as it was: frontOf gives
  // the front's point of view, asShown turns the result back when the back was showing.
  const showsBack = (collage) => Boolean(collage?.side === 'back' && collage.back);
  const frontOf = (collage) => (showsBack(collage) ? core.flipSide(collage) : collage);
  const asShown = (collage, front) => (showsBack(collage) ? core.flipSide(front) : front);

  /** The wording a new card starts with: realistic, and every word can be changed. */
  const DEFAULT_DETAILS = Object.freeze({
    host: 'The Commandant and Officers\nSimulator Development Division',
    invite: 'request the pleasure of the company of',
    at: 'at the',
    occasion: 'Raising Day Dinner',
    when: 'Friday, 12 December 2026 at 1930 h',
    venue: 'Officers’ Mess, SDD Campus',
    dress: 'Dress: Lounge Suit',
    rsvp: 'R.S.V.P. The Adjutant · Ext 2241',
    note: 'Kindly bring this card',
    guest: '',
    // The Bilingual design's Hindi wording (PR 6).
    hostHi: 'कमांडेंट एवं अधिकारीगण\nसिम्युलेटर विकास प्रभाग',
    inviteHi: 'आपको सादर आमंत्रित करते हैं',
    occasionHi: 'स्थापना दिवस रात्रिभोज',
    // The Regimental design's band: the unit's three colours, top to bottom (PR 6).
    colours: '#7a1f2b #c9a54a #1d2c55',
    // The back of the card (PR 8).
    programmeTitle: 'Programme',
    programme: '1930 h · Guests arrive' + String.fromCharCode(10) + '2000 h · Address by the Commandant' + String.fromCharCode(10) + '2030 h · Cultural programme' + String.fromCharCode(10) + '2100 h · Dinner',
    backNote: 'Car passes at the main gate · Parking beside the Officers’ Mess',
    // Pocket cards (PR 9b).
    passTitle: 'CAR PASS',
    vehicle: 'Vehicle No.',
    replyTitle: 'R.S.V.P.',
    replyName: 'Name',
    attend: 'will / will not attend',
    replyCount: 'Number attending',
    replyBy: 'Kindly reply by Friday, 5 December 2026',
    // Posters (PR 11). logoMode: '' asks which logo, 'later' keeps the place, 'none' has none.
    kicker: 'SDD TALK SERIES',
    title: 'Annual Sports Meet 2026',
    subtitle: 'Inter-section championship',
    lines: 'Athletics · Volleyball · Tug of war' + String.fromCharCode(10) + 'Families welcome' + String.fromCharCode(10) + 'Prize distribution on 22 December, 1600 h',
    contact: 'Sports Officer · Ext 2210',
    logoMode: '',
    // A design with a photo frame (Ceremonial photo, the two photo posters): '' keeps the frame,
    // 'none' is "No photo": the frame goes and the design closes the gap (UX review 8). An older
    // PRISM ignores it and shows the frame, as before.
    photoMode: '',
    // A folded card's front cover title (cold review 9, M2): empty is "the occasion", and the
    // cover follows the occasion until other words are typed here. The occasion itself is inside,
    // so the opened card reads as a whole invitation. An older PRISM ignores it.
    coverTitle: ''
  });
  /**
   * Fields whose words, while empty, are another field's (FALLBACKS[field] names it): a folded
   * card's cover title shows the occasion. A fallback field the user deleted on the page holds a
   * single space, so the cover stays without one.
   */
  const FALLBACKS = Object.freeze({ coverTitle: 'occasion' });
  /** A field's words on the card: its own, or (a fallback field left empty) the field it follows. */
  const wordsFor = (wording, field) => (FALLBACKS[field] && wording[field] === '' ? String(wording[FALLBACKS[field]] ?? '') : String(wording[field] ?? ''));
  /** What a new poster starts with: the same fields, a poster's wording. */
  const POSTER_DETAILS = Object.freeze({ ...DEFAULT_DETAILS, when: '20 – 22 December 2026 · 0700 h onwards', venue: 'Unit Sports Ground' });
  /** The fields on the back of the card. */
  /** Fields that are not words on the card: they set something else about it. */
  const SETTINGS = Object.freeze(['colours']);
  /** The fields, in the order the Card details form shows them. */
  const FIELDS = Object.freeze(Object.keys(DEFAULT_DETAILS));

  const SDD_ASPECT = 1732 / 2940;
  const GOLD = '#a8842f';
  const PALE_GOLD = '#d4b25e';
  const MAROON = '#5c1a22';
  const INK = '#1d1d1d';
  const IVORY = '#f1e7cc';

  // Part helpers. x and y are the middle of the part as fractions of the page's width and
  // height; a text's w is the share of the width it may take; a logo's w, and sizes and stroke
  // widths, are per 1,000 of the card's scale (see unitOf). `anchor` sets text by its left or
  // right edge at x instead of its middle.
  const logo = (x, y, w) => ({ role: 'logo', kind: 'sticker', sticker: 'logo:sdd', x, y, w, aspect: SDD_ASPECT });
  const border = (role, inset, stroke, strokeWidth) => ({ role, kind: 'shape', form: 'rect', inset, look: { stroke, strokeWidth, fill: '' } });
  const rule = (role, x, y, w, stroke, strokeWidth) => ({ role, kind: 'shape', form: 'line', x, y, w, look: { stroke, strokeWidth } });
  const words = (field, x, y, w, size, look, extra = {}) => ({ role: field, kind: 'text', field, x, y, w, size, look, ...extra });
  // A rectangle by fractions of the page: w of its width and h of its height (a band, a strip).
  const block = (role, x, y, w, h, look) => ({ role, kind: 'shape', form: 'rect', x, y, w, h, look });
  // A shape sized by the card's scale (w per 1,000), of this aspect: a roundel behind the logo.
  const roundel = (role, form, x, y, w, aspect, look) => ({ role, kind: 'shape', form, x, y, scaled: w, aspect, look });
  // A line standing upright, `len` of the page's height long.
  const vline = (role, x, y, len, stroke, strokeWidth) => ({ role, kind: 'shape', form: 'line', x, y, len, look: { stroke, strokeWidth } });
  // An empty frame for the user's own photo, by fractions of the page.
  const photo = (x, y, w, h) => ({ role: 'photo', kind: 'slot', x, y, w, h });
  // A poster's logo place (PR 11): the logo chosen, or an empty frame until one is (Preview and
  // Save leave an empty frame out), or nothing with "No logo". `aspect` is the frame's shape.
  const logoPlace = (x, y, w, aspect) => ({ role: 'logo', kind: 'logoplace', x, y, w, aspect });
  // Colours the Regimental band sets: 'band1' to 'band3' in a look stand for the unit's colours.
  const NAVY = '#1d2c55';
  const GREY = '#55607a';
  // A light tile behind the logo on a dark design (UX review 8, M3): a dark crest (a navy shield)
  // vanished on the navy card and the Event poster. fitLogoTile() sizes it round the logo, and
  // hides it with no logo or with the SDD logo, whose gold reads on navy as it is.
  const NAVY_TILE = Object.freeze({ fill: '#fbf7ee', stroke: PALE_GOLD, strokeWidth: 1.2, radius: 10 });
  const EVENT_TILE = Object.freeze({ fill: '#ffffff', stroke: '', radius: 10 });
  const logoTile = (x, y, w, look, role = 'logotile') => ({ ...block(role, x, y, w, 0.08, look), tile: true });

  // The designs set in engraved capitals and formal script (Cinzel and Pinyon Script, #3447).
  const FORMAL_DESIGNS = Object.freeze(['classic', 'navy', 'regimental']);

  const DESIGNS = lookupTable({
    classic: Object.freeze({
      key: 'classic',
      label: 'Classic ivory',
      note: 'Gold double border',
      background: { type: 'color', value: '#fbf7ee' },
      portrait: [
        border('border', 0.035, GOLD, 3),
        border('border2', 0.05, GOLD, 1.2),
        logo(0.5, 0.135, 0.11),
        words('host', 0.5, 0.29, 0.76, 28, { font: 'engraved', color: INK, bold: true, spacing: 6 }, { lines: 3 }),
        words('invite', 0.5, 0.385, 0.8, 39, { font: 'formal', color: GOLD, bold: false }),
        rule('guestline', 0.5, 0.46, 0.62, INK, 1.5),
        words('guest', 0.5, 0.445, 0.62, 34, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('at', 0.5, 0.515, 0.4, 24, { font: 'elegant', color: INK, bold: false, italic: true }),
        words('occasion', 0.5, 0.585, 0.82, 56, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        rule('rule', 0.5, 0.655, 0.16, GOLD, 2),
        words('when', 0.5, 0.705, 0.82, 25, { font: 'clean', color: INK, bold: true }, { lines: 2 }),
        words('venue', 0.5, 0.752, 0.82, 24, { font: 'clean', color: INK, bold: false }, { lines: 2 }),
        words('dress', 0.5, 0.795, 0.82, 22, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.1, 0.895, 0.42, 18, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.9, 0.895, 0.36, 18, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ],
      landscape: [
        border('border', 0.035, GOLD, 3),
        border('border2', 0.05, GOLD, 1.2),
        logo(0.16, 0.5, 0.14),
        rule('divider', 0.29, 0.5, 0.0, GOLD, 1.2),
        words('host', 0.6, 0.17, 0.58, 26, { font: 'engraved', color: INK, bold: true, spacing: 6 }, { lines: 2 }),
        words('invite', 0.6, 0.29, 0.6, 37, { font: 'formal', color: GOLD, bold: false }),
        rule('guestline', 0.6, 0.38, 0.46, INK, 1.5),
        words('guest', 0.6, 0.36, 0.46, 32, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('at', 0.6, 0.425, 0.4, 20, { font: 'elegant', color: INK, bold: false, italic: true }),
        words('occasion', 0.6, 0.51, 0.6, 48, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        words('when', 0.6, 0.63, 0.6, 23, { font: 'clean', color: INK, bold: true }, { lines: 1 }),
        words('venue', 0.6, 0.7, 0.6, 22, { font: 'clean', color: INK, bold: false }, { lines: 1 }),
        words('dress', 0.6, 0.765, 0.6, 21, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.33, 0.88, 0.32, 17, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.92, 0.88, 0.3, 17, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ]
    }),
    navy: Object.freeze({
      key: 'navy',
      label: 'Navy and gold',
      note: "For dinners and Ladies' Nights",
      background: { type: 'gradient', key: 'navy' },
      portrait: [
        border('border', 0.04, PALE_GOLD, 1.5),
        border('border2', 0.055, PALE_GOLD, 0.8),
        logoTile(0.5, 0.14, 0.115, NAVY_TILE),
        logo(0.5, 0.14, 0.115),
        words('host', 0.5, 0.3, 0.76, 26, { font: 'engraved', color: PALE_GOLD, bold: false, spacing: 10 }, { lines: 3 }),
        words('invite', 0.5, 0.395, 0.8, 39, { font: 'formal', color: IVORY, bold: false }),
        rule('guestline', 0.5, 0.47, 0.6, IVORY, 1.2),
        words('guest', 0.5, 0.455, 0.6, 34, { font: 'script', color: IVORY, bold: false }, { lines: 1 }),
        words('at', 0.5, 0.515, 0.4, 22, { font: 'elegant', color: IVORY, bold: false, italic: true }),
        words('occasion', 0.5, 0.58, 0.82, 60, { font: 'elegant', color: PALE_GOLD, bold: true }, { lines: 2 }),
        rule('rule', 0.5, 0.65, 0.22, PALE_GOLD, 1.5),
        words('when', 0.5, 0.705, 0.82, 25, { font: 'clean', color: IVORY, bold: true }, { lines: 2 }),
        words('venue', 0.5, 0.752, 0.82, 24, { font: 'clean', color: IVORY, bold: false }, { lines: 2 }),
        words('dress', 0.5, 0.797, 0.82, 22, { font: 'clean', color: PALE_GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.5, 0.875, 0.8, 18, { font: 'clean', color: IVORY, bold: false }, { lines: 1 }),
        words('note', 0.5, 0.91, 0.8, 18, { font: 'elegant', color: IVORY, bold: false, italic: true }, { lines: 1 })
      ],
      landscape: [
        border('border', 0.04, PALE_GOLD, 1.5),
        border('border2', 0.055, PALE_GOLD, 0.8),
        logoTile(0.16, 0.5, 0.14, NAVY_TILE),
        logo(0.16, 0.5, 0.14),
        words('host', 0.6, 0.17, 0.58, 24, { font: 'engraved', color: PALE_GOLD, bold: false, spacing: 10 }, { lines: 2 }),
        words('invite', 0.6, 0.29, 0.6, 37, { font: 'formal', color: IVORY, bold: false }),
        rule('guestline', 0.6, 0.38, 0.46, IVORY, 1.2),
        words('guest', 0.6, 0.36, 0.46, 32, { font: 'script', color: IVORY, bold: false }, { lines: 1 }),
        words('at', 0.6, 0.42, 0.4, 20, { font: 'elegant', color: IVORY, bold: false, italic: true }),
        words('occasion', 0.6, 0.51, 0.6, 54, { font: 'elegant', color: PALE_GOLD, bold: true }, { lines: 2 }),
        words('when', 0.6, 0.63, 0.6, 23, { font: 'clean', color: IVORY, bold: true }, { lines: 1 }),
        words('venue', 0.6, 0.7, 0.6, 22, { font: 'clean', color: IVORY, bold: false }, { lines: 1 }),
        words('dress', 0.6, 0.765, 0.6, 21, { font: 'clean', color: PALE_GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.6, 0.86, 0.6, 17, { font: 'clean', color: IVORY, bold: false }, { lines: 1 }),
        words('note', 0.6, 0.905, 0.6, 17, { font: 'elegant', color: IVORY, bold: false, italic: true }, { lines: 1 })
      ]
    }),
    regimental: Object.freeze({
      key: 'regimental',
      label: 'Regimental',
      note: 'A band in your unit’s colours',
      background: { type: 'color', value: '#ffffff' },
      portrait: [
        block('band1', 0.5, 0.0175, 1, 0.035, { fill: 'band1', stroke: '' }),
        block('band2', 0.5, 0.0525, 1, 0.035, { fill: 'band2', stroke: '' }),
        block('band3', 0.5, 0.0875, 1, 0.035, { fill: 'band3', stroke: '' }),
        roundel('roundel', 'ellipse', 0.5, 0.135, 0.17, 0.72, { fill: '#ffffff', stroke: 'band2', strokeWidth: 3 }),
        logo(0.5, 0.135, 0.1),
        words('host', 0.5, 0.29, 0.8, 26, { font: 'engraved', color: 'band1', bold: true, spacing: 8 }, { lines: 3 }),
        words('invite', 0.5, 0.375, 0.8, 24, { font: 'elegant', color: INK, bold: false, italic: true }),
        rule('guestline', 0.5, 0.45, 0.62, INK, 1.5),
        words('guest', 0.5, 0.435, 0.62, 34, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('at', 0.5, 0.5, 0.4, 22, { font: 'elegant', color: INK, bold: false, italic: true }),
        words('occasion', 0.5, 0.57, 0.84, 56, { font: 'elegant', color: 'band3', bold: true }, { lines: 2 }),
        words('when', 0.5, 0.67, 0.82, 25, { font: 'clean', color: INK, bold: true }, { lines: 2 }),
        words('venue', 0.5, 0.715, 0.82, 24, { font: 'clean', color: INK, bold: false }, { lines: 2 }),
        words('dress', 0.5, 0.76, 0.82, 22, { font: 'clean', color: 'band1', bold: true }, { lines: 1 }),
        rule('rule', 0.5, 0.85, 0.84, 'band2', 3),
        words('rsvp', 0.08, 0.9, 0.44, 18, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.92, 0.9, 0.38, 18, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ],
      landscape: [
        block('band1', 0.5, 0.025, 1, 0.05, { fill: 'band1', stroke: '' }),
        block('band2', 0.5, 0.075, 1, 0.05, { fill: 'band2', stroke: '' }),
        block('band3', 0.5, 0.125, 1, 0.05, { fill: 'band3', stroke: '' }),
        roundel('roundel', 'ellipse', 0.16, 0.52, 0.2, 0.72, { fill: '#ffffff', stroke: 'band2', strokeWidth: 3 }),
        logo(0.16, 0.52, 0.12),
        words('host', 0.61, 0.25, 0.6, 24, { font: 'engraved', color: 'band1', bold: true, spacing: 8 }, { lines: 2 }),
        words('invite', 0.61, 0.36, 0.6, 22, { font: 'elegant', color: INK, bold: false, italic: true }),
        rule('guestline', 0.61, 0.44, 0.46, INK, 1.5),
        words('guest', 0.61, 0.42, 0.46, 30, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('at', 0.61, 0.48, 0.4, 18, { font: 'elegant', color: INK, bold: false, italic: true }),
        words('occasion', 0.61, 0.555, 0.62, 44, { font: 'elegant', color: 'band3', bold: true }, { lines: 2 }),
        words('when', 0.61, 0.65, 0.62, 22, { font: 'clean', color: INK, bold: true }, { lines: 1 }),
        words('venue', 0.61, 0.715, 0.62, 21, { font: 'clean', color: INK, bold: false }, { lines: 1 }),
        words('dress', 0.61, 0.775, 0.62, 20, { font: 'clean', color: 'band1', bold: true }, { lines: 1 }),
        rule('rule', 0.61, 0.835, 0.62, 'band2', 2.5),
        words('rsvp', 0.31, 0.9, 0.34, 17, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.93, 0.9, 0.3, 17, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ]
    }),
    ceremonial: Object.freeze({
      key: 'ceremonial',
      label: 'Ceremonial photo',
      note: 'Your photo at the top',
      background: { type: 'color', value: '#ffffff' },
      // `noPhoto` is where a part goes with "No photo" (UX review 8): a maroon band takes the
      // photo's place, the logo's box sits on its edge as it sat on the photo's, and the words
      // move up and grow into the space. `onlyNoPhoto` parts are there only then.
      portrait: [
        photo(0.5, 0.2, 1, 0.4),
        { ...block('photoband', 0.5, 0.07, 1, 0.14, { fill: MAROON, stroke: '' }), onlyNoPhoto: true },
        { ...block('logobox', 0.15, 0.4, 0.17, 0.135, { fill: '#ffffff', stroke: '#d9d4c7', strokeWidth: 1.5, radius: 10 }), noPhoto: { x: 0.165, y: 0.14 } },
        { ...logo(0.15, 0.4, 0.1), noPhoto: { x: 0.165, y: 0.14 } },
        words('occasion', 0.27, 0.465, 0.66, 46, { font: 'elegant', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 2, noPhoto: { x: 0.08, y: 0.3, w: 0.84, size: 54 } }),
        words('host', 0.08, 0.565, 0.84, 22, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 2, noPhoto: { y: 0.4, size: 25 } }),
        words('invite', 0.08, 0.63, 0.84, 22, { font: 'elegant', color: INK, bold: false, italic: true, align: 'left' }, { anchor: 'left', noPhoto: { y: 0.485, size: 25 } }),
        { ...rule('guestline', 0.4, 0.695, 0.64, INK, 1.5), noPhoto: { y: 0.585 } },
        words('guest', 0.08, 0.68, 0.64, 30, { font: 'script', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { y: 0.567, size: 34 } }),
        { ...vline('accent', 0.075, 0.8, 0.11, MAROON, 3), noPhoto: { x: 0.08, y: 0.725, len: 0.14 } },
        words('when', 0.1, 0.76, 0.8, 23, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.105, y: 0.675, size: 26 } }),
        words('venue', 0.1, 0.8, 0.8, 22, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.105, y: 0.725, size: 25 } }),
        words('dress', 0.1, 0.84, 0.8, 21, { font: 'clean', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.105, y: 0.775, size: 24 } }),
        { ...rule('photofoot', 0.5, 0.875, 0.84, '#d9d4c7', 1.2), onlyNoPhoto: true },
        words('rsvp', 0.08, 0.92, 0.44, 18, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.92, 0.92, 0.38, 18, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ],
      landscape: [
        photo(0.25, 0.5, 0.5, 1),
        { ...block('photoband', 0.035, 0.5, 0.07, 1, { fill: MAROON, stroke: '' }), onlyNoPhoto: true },
        { ...block('logobox', 0.58, 0.16, 0.12, 0.2, { fill: '#ffffff', stroke: '#d9d4c7', strokeWidth: 1.5, radius: 10 }), noPhoto: { x: 0.19, y: 0.17 } },
        { ...logo(0.58, 0.16, 0.1), noPhoto: { x: 0.19, y: 0.17 } },
        words('occasion', 0.66, 0.16, 0.3, 36, { font: 'elegant', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 2, noPhoto: { x: 0.13, y: 0.355, w: 0.79, size: 40 } }),
        words('host', 0.54, 0.36, 0.42, 20, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 2, noPhoto: { x: 0.13, y: 0.465, w: 0.79, size: 21 } }),
        words('invite', 0.54, 0.46, 0.42, 20, { font: 'elegant', color: INK, bold: false, italic: true, align: 'left' }, { anchor: 'left', noPhoto: { x: 0.13, y: 0.545, w: 0.79, size: 21 } }),
        { ...rule('guestline', 0.75, 0.55, 0.42, INK, 1.5), noPhoto: { x: 0.405, y: 0.635, w: 0.55 } },
        words('guest', 0.54, 0.53, 0.42, 26, { font: 'script', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.13, y: 0.617, w: 0.55, size: 28 } }),
        { ...vline('accent', 0.535, 0.7, 0.18, MAROON, 3), noPhoto: { x: 0.13, y: 0.77, len: 0.15 } },
        words('when', 0.555, 0.64, 0.4, 20, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.15, y: 0.72, w: 0.77 } }),
        words('venue', 0.555, 0.7, 0.4, 19, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.15, y: 0.77, w: 0.77 } }),
        words('dress', 0.555, 0.76, 0.4, 18, { font: 'clean', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.15, y: 0.82, w: 0.77 } }),
        words('rsvp', 0.54, 0.9, 0.42, 16, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.13, y: 0.915, w: 0.44 } }),
        words('note', 0.54, 0.945, 0.42, 16, { font: 'elegant', color: INK, bold: false, italic: true, align: 'left' }, { anchor: 'left', lines: 1, noPhoto: { x: 0.62, y: 0.915, w: 0.3 } })
      ]
    }),
    bilingual: Object.freeze({
      key: 'bilingual',
      label: 'Bilingual',
      note: 'Hindi and English side by side',
      background: { type: 'color', value: '#fffdf7' },
      portrait: [
        border('border', 0.03, GOLD, 1.5),
        logo(0.5, 0.11, 0.09),
        vline('divider', 0.5, 0.3, 0.17, GOLD, 1.2),
        words('hostHi', 0.26, 0.26, 0.42, 22, { font: 'hindiFormal', color: INK, bold: true }, { lines: 3 }),
        words('inviteHi', 0.26, 0.35, 0.42, 20, { font: 'hindiFormal', color: INK, bold: false }, { lines: 2 }),
        words('host', 0.74, 0.26, 0.42, 19, { font: 'elegant', color: INK, bold: true }, { lines: 3 }),
        words('invite', 0.74, 0.35, 0.42, 19, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 2 }),
        rule('guestline', 0.5, 0.45, 0.62, INK, 1.5),
        words('guest', 0.5, 0.435, 0.62, 30, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        // No "at the": it would stand under the English alone (UX review 8); the columns read without it.
        vline('divider2', 0.5, 0.6, 0.1, GOLD, 1.2),
        words('occasionHi', 0.26, 0.6, 0.42, 34, { font: 'hindiFormal', color: MAROON, bold: false }, { lines: 3 }),
        words('occasion', 0.74, 0.6, 0.42, 30, { font: 'elegant', color: MAROON, bold: true }, { lines: 3 }),
        words('when', 0.5, 0.76, 0.84, 24, { font: 'clean', color: INK, bold: true }, { lines: 1 }),
        words('venue', 0.5, 0.8, 0.84, 22, { font: 'clean', color: INK, bold: false }, { lines: 1 }),
        words('dress', 0.5, 0.84, 0.84, 21, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.08, 0.92, 0.44, 17, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.92, 0.92, 0.38, 17, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ],
      landscape: [
        border('border', 0.03, GOLD, 1.5),
        logo(0.5, 0.14, 0.11),
        vline('divider', 0.5, 0.355, 0.17, GOLD, 1.2),
        words('hostHi', 0.27, 0.29, 0.4, 20, { font: 'hindiFormal', color: INK, bold: true }, { lines: 2 }),
        words('inviteHi', 0.27, 0.4, 0.4, 18, { font: 'hindiFormal', color: INK, bold: false }, { lines: 1 }),
        words('host', 0.73, 0.29, 0.4, 18, { font: 'elegant', color: INK, bold: true }, { lines: 2 }),
        words('invite', 0.73, 0.4, 0.4, 18, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 1 }),
        // The guest's name across both columns, then "at the" before the occasion, as in portrait.
        rule('guestline', 0.5, 0.49, 0.5, INK, 1.2),
        words('guest', 0.5, 0.473, 0.5, 24, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        vline('divider2', 0.5, 0.635, 0.1, GOLD, 1.2),
        words('occasionHi', 0.27, 0.635, 0.4, 26, { font: 'hindiFormal', color: MAROON, bold: false }, { lines: 2 }),
        words('occasion', 0.73, 0.635, 0.4, 24, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        words('when', 0.5, 0.75, 0.84, 20, { font: 'clean', color: INK, bold: true }, { lines: 1 }),
        words('venue', 0.5, 0.805, 0.84, 19, { font: 'clean', color: INK, bold: false }, { lines: 1 }),
        words('dress', 0.5, 0.855, 0.84, 18, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.08, 0.925, 0.42, 15, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('note', 0.92, 0.925, 0.38, 15, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 1 })
      ]
    }),
    // Posters (PR 11): offered in the Posters row, A3 or A4, a logo only when the user picks one.
    // `yNoLogo` is where a part goes when the poster has no logo, so the heading uses the space.
    event: Object.freeze({
      key: 'event',
      label: 'Event',
      note: 'A big heading and a bold band',
      poster: true,
      background: { type: 'color', value: '#0f2c5c' },
      portrait: [
        logoTile(0.88, 0.09, 0.13, EVENT_TILE),
        logoPlace(0.88, 0.09, 0.13, 0.6),
        words('subtitle', 0.08, 0.07, 0.7, 26, { font: 'clean', color: '#e8b020', bold: true, spacing: 14, align: 'left' }, { anchor: 'left', lines: 1, yNoLogo: 0.06 }),
        words('title', 0.08, 0.34, 0.86, 140, { font: 'poster', color: '#ffffff', bold: false, align: 'left' }, { anchor: 'left', lines: 3, yNoLogo: 0.29 }),
        { ...block('band', 0.5, 0.61, 1.2, 0.07, { fill: '#e8b020', stroke: '' }), angle: -4 },
        { ...words('when', 0.5, 0.61, 0.84, 46, { font: 'poster', color: '#0f2c5c', bold: false }, { lines: 1 }), angle: -4 },
        words('venue', 0.08, 0.705, 0.84, 30, { font: 'clean', color: '#ffffff', bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('lines', 0.08, 0.8, 0.84, 26, { font: 'clean', color: '#dfe6f2', bold: false, align: 'left' }, { anchor: 'left', lines: 5 }),
        words('contact', 0.08, 0.94, 0.84, 20, { font: 'clean', color: '#c9d4e8', bold: false, align: 'left' }, { anchor: 'left', lines: 1 })
      ]
    }),
    notice: Object.freeze({
      key: 'notice',
      label: 'Notice',
      note: 'Plain and official',
      poster: true,
      sample: { title: 'Annual Medical Examination', subtitle: 'For all ranks of the Division', when: 'Monday, 8 December 2026 · 0900 h to 1300 h', venue: 'Medical Inspection Room, SDD Campus', lines: 'Bring your medical documents' + String.fromCharCode(10) + 'Report fasting for the blood test' + String.fromCharCode(10) + 'Section by section, as listed on the unit notice board', contact: 'Medical Officer · Ext 2205' },
      background: { type: 'color', value: '#ffffff' },
      // Cold review 9, M3: the words fill the page, a maroon bar at the top and the bottom; with
      // no logo only the heading's group moves up, so the page stays balanced.
      portrait: [
        block('bar', 0.5, 0.008, 1, 0.016, { fill: MAROON, stroke: '' }),
        logoPlace(0.5, 0.1, 0.13, 0.6),
        words('title', 0.5, 0.25, 0.86, 88, { font: 'elegant', color: MAROON, bold: true }, { lines: 2, yNoLogo: 0.18 }),
        words('subtitle', 0.5, 0.36, 0.84, 36, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 2, yNoLogo: 0.3 }),
        { ...rule('rule', 0.5, 0.415, 0.3, GOLD, 2.5), yNoLogo: 0.36 },
        words('when', 0.5, 0.49, 0.84, 42, { font: 'clean', color: INK, bold: true }, { lines: 2, yNoLogo: 0.43 }),
        words('venue', 0.5, 0.56, 0.84, 38, { font: 'clean', color: INK, bold: false }, { lines: 2, yNoLogo: 0.5 }),
        { ...rule('rule2', 0.5, 0.625, 0.12, GOLD, 1.5), yNoLogo: 0.565 },
        words('lines', 0.5, 0.745, 0.84, 34, { font: 'clean', color: INK, bold: false }, { lines: 6, yNoLogo: 0.69 }),
        words('contact', 0.5, 0.94, 0.84, 24, { font: 'clean', color: '#55607a', bold: false }, { lines: 1 }),
        block('bar2', 0.5, 0.992, 1, 0.016, { fill: MAROON, stroke: '' })
      ]
    }),
    photoposter: Object.freeze({
      key: 'photoposter',
      label: 'With a photo',
      note: 'Farewell or felicitation',
      poster: true,
      sample: { title: 'Farewell', subtitle: 'Col R. K. Verma, on superannuation', when: 'Friday, 19 December 2026 · 1830 h', venue: 'Officers’ Mess, SDD Campus', contact: 'Adjutant · Ext 2241' },
      background: { type: 'color', value: '#f6efe3' },
      portrait: [
        logoPlace(0.5, 0.07, 0.1, 0.6),
        { ...photo(0.5, 0.33, 0.72, 0.36), angle: -2 },
        // With "No photo" (UX review 8) the heading grows into the photo's space, between two
        // gold rules, and the details follow it up.
        { ...rule('photorule', 0.5, 0.23, 0.24, GOLD, 2.5), onlyNoPhoto: true },
        words('title', 0.5, 0.61, 0.86, 88, { font: 'script', color: MAROON, bold: false }, { lines: 2, noPhoto: { y: 0.36, size: 150 } }),
        { ...rule('photorule2', 0.5, 0.5, 0.24, GOLD, 2.5), onlyNoPhoto: true },
        words('subtitle', 0.5, 0.72, 0.84, 30, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 2, noPhoto: { y: 0.58, size: 40 } }),
        words('when', 0.5, 0.785, 0.84, 28, { font: 'clean', color: INK, bold: true }, { lines: 1, noPhoto: { y: 0.69, size: 36 } }),
        words('venue', 0.5, 0.83, 0.84, 26, { font: 'clean', color: INK, bold: false }, { lines: 1, noPhoto: { y: 0.75, size: 34 } }),
        words('contact', 0.5, 0.94, 0.84, 20, { font: 'clean', color: '#6b5a44', bold: false }, { lines: 1 })
      ]
    }),
    talk: Object.freeze({
      key: 'talk',
      label: 'Talk or seminar',
      note: 'Speaker and topic',
      poster: true,
      sample: { title: 'Simulation in Training: Lessons from the Field', subtitle: 'A talk by Col A. Sharma, Simulator Development Division', when: 'Thursday, 4 December 2026 · 1500 h', venue: 'Auditorium, SDD Campus', lines: '22 years of simulator design' + String.fromCharCode(10) + 'Questions after the talk' + String.fromCharCode(10) + 'Tea at 1630 h', contact: 'Training Officer · Ext 2230' },
      background: { type: 'color', value: '#ffffff' },
      // Cold review 9, M3: the pale panel is the lower quarter and holds the date, venue and
      // contact in larger type; the heading, speaker and points fill the white above.
      portrait: [
        block('lower', 0.5, 0.86, 1, 0.28, { fill: '#e9eef6', stroke: '' }),
        logoPlace(0.88, 0.08, 0.11, 0.6),
        words('kicker', 0.08, 0.07, 0.66, 24, { font: 'clean', color: NAVY, bold: true, spacing: 16, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('title', 0.08, 0.21, 0.74, 78, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 3, yNoLogo: 0.205 }),
        words('subtitle', 0.08, 0.36, 0.84, 34, { font: 'clean', color: '#55607a', bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        { ...photo(0.21, 0.53, 0.26, 0.19), radius: 999 },
        // With "No photo" (UX review 8) the points take the photo's place, the page's width.
        { ...vline('photoaccent', 0.08, 0.53, 0.15, NAVY, 4), onlyNoPhoto: true },
        words('lines', 0.4, 0.53, 0.54, 30, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 5, noPhoto: { x: 0.11, w: 0.81, size: 38 } }),
        rule('rule', 0.13, 0.765, 0.1, NAVY, 4),
        words('when', 0.08, 0.81, 0.84, 42, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('venue', 0.08, 0.865, 0.84, 36, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('contact', 0.08, 0.94, 0.84, 24, { font: 'clean', color: '#55607a', bold: false, align: 'left' }, { anchor: 'left', lines: 1 })
      ]
    }),
    // Pocket cards (PR 9b): offered for the pocket sizes; each fills from the same Card details.
    pass: Object.freeze({
      key: 'pass',
      label: 'Entry card or car pass',
      note: 'With a line for the vehicle number',
      pocket: true,
      background: { type: 'color', value: '#ffffff' },
      portrait: [
        block('band1', 0.5, 0.03, 1, 0.06, { fill: NAVY, stroke: '' }),
        logo(0.5, 0.175, 0.15),
        words('passTitle', 0.5, 0.34, 0.86, 64, { font: 'clean', color: MAROON, bold: true, spacing: 8 }, { lines: 1 }),
        words('occasion', 0.5, 0.46, 0.86, 40, { font: 'elegant', color: INK, bold: true }, { lines: 2 }),
        words('when', 0.5, 0.58, 0.86, 30, { font: 'clean', color: INK, bold: true }, { lines: 2 }),
        words('venue', 0.5, 0.66, 0.86, 28, { font: 'clean', color: INK, bold: false }, { lines: 2 }),
        words('vehicle', 0.12, 0.81, 0.4, 28, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        rule('vehicleline', 0.68, 0.83, 0.42, INK, 2),
        block('band3', 0.5, 0.97, 1, 0.06, { fill: GOLD, stroke: '' })
      ],
      landscape: [
        block('band1', 0.04, 0.5, 0.08, 1, { fill: NAVY, stroke: '' }),
        logo(0.2, 0.42, 0.16),
        words('passTitle', 0.32, 0.17, 0.62, 64, { font: 'clean', color: MAROON, bold: true, spacing: 8, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('occasion', 0.32, 0.37, 0.62, 44, { font: 'elegant', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('when', 0.32, 0.55, 0.62, 32, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('venue', 0.32, 0.67, 0.62, 30, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('vehicle', 0.32, 0.84, 0.3, 28, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        rule('vehicleline', 0.75, 0.86, 0.34, INK, 2)
      ]
    }),
    reply: Object.freeze({
      key: 'reply',
      label: 'RSVP card',
      note: 'Will or will not attend, and a name',
      pocket: true,
      background: { type: 'color', value: '#fbf7ee' },
      portrait: [
        border('border', 0.05, GOLD, 2),
        // A small logo at the top, so a card's logo is kept when it changes to the RSVP card.
        logo(0.5, 0.1, 0.07),
        words('replyTitle', 0.5, 0.21, 0.8, 40, { font: 'elegant', color: MAROON, bold: true, spacing: 6 }, { lines: 1 }),
        words('occasion', 0.5, 0.32, 0.8, 34, { font: 'elegant', color: INK, bold: true }, { lines: 2 }),
        rule('nameline', 0.5, 0.5, 0.76, INK, 1.5),
        words('replyName', 0.5, 0.54, 0.8, 22, { font: 'clean', color: '#55607a', bold: false }, { lines: 1 }),
        words('attend', 0.5, 0.655, 0.8, 30, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 2 }),
        rule('countline', 0.5, 0.77, 0.3, INK, 1.5),
        words('replyCount', 0.5, 0.81, 0.8, 22, { font: 'clean', color: '#55607a', bold: false }, { lines: 1 }),
        words('replyBy', 0.5, 0.91, 0.8, 22, { font: 'clean', color: GOLD, bold: true }, { lines: 2 })
      ],
      landscape: [
        border('border', 0.05, GOLD, 2),
        logo(0.1, 0.18, 0.07),
        words('replyTitle', 0.55, 0.15, 0.7, 36, { font: 'elegant', color: MAROON, bold: true, spacing: 6 }, { lines: 1 }),
        words('occasion', 0.55, 0.31, 0.7, 26, { font: 'elegant', color: INK, bold: true }, { lines: 1 }),
        rule('nameline', 0.5, 0.5, 0.7, INK, 1.5),
        words('replyName', 0.5, 0.56, 0.8, 16, { font: 'clean', color: '#55607a', bold: false }, { lines: 1 }),
        words('attend', 0.5, 0.7, 0.8, 22, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 1 }),
        words('replyBy', 0.5, 0.86, 0.8, 16, { font: 'clean', color: GOLD, bold: true }, { lines: 1 })
      ]
    }),
    programmecard: Object.freeze({
      key: 'programmecard',
      label: 'Programme card',
      note: 'The order of the evening',
      pocket: true,
      background: { type: 'color', value: '#fbf7ee' },
      portrait: [
        border('border', 0.05, GOLD, 2),
        logo(0.5, 0.11, 0.1),
        words('occasion', 0.5, 0.24, 0.8, 34, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        words('programmeTitle', 0.5, 0.33, 0.8, 26, { font: 'elegant', color: GOLD, bold: false, italic: true }, { lines: 1 }),
        words('programme', 0.5, 0.58, 0.84, 30, { font: 'clean', color: INK, bold: false, align: 'left' }, { lines: 10 }),
        words('when', 0.5, 0.88, 0.8, 22, { font: 'clean', color: INK, bold: true }, { lines: 2 })
      ],
      landscape: [
        border('border', 0.05, GOLD, 2),
        logo(0.14, 0.5, 0.12),
        words('occasion', 0.6, 0.18, 0.66, 30, { font: 'elegant', color: MAROON, bold: true }, { lines: 1 }),
        words('programmeTitle', 0.6, 0.31, 0.66, 22, { font: 'elegant', color: GOLD, bold: false, italic: true }, { lines: 1 }),
        words('programme', 0.6, 0.6, 0.66, 19, { font: 'clean', color: INK, bold: false, align: 'left' }, { lines: 10 }),
        words('when', 0.6, 0.88, 0.66, 16, { font: 'clean', color: INK, bold: true }, { lines: 1 })
      ]
    }),
    pocketinvite: Object.freeze({
      notFor: Object.freeze(['passl', 'passp']),
      key: 'pocketinvite',
      label: 'Pocket invitation',
      note: 'The invitation, small',
      pocket: true,
      background: { type: 'color', value: '#fbf7ee' },
      portrait: [
        border('border', 0.04, GOLD, 2.5),
        border('border2', 0.06, GOLD, 1),
        logo(0.5, 0.13, 0.11),
        words('host', 0.5, 0.26, 0.8, 21, { font: 'elegant', color: INK, bold: true }, { lines: 3 }),
        words('invite', 0.5, 0.34, 0.8, 21, { font: 'script', color: GOLD, bold: false }, { lines: 1 }),
        rule('guestline', 0.5, 0.445, 0.64, INK, 1.2),
        words('guest', 0.5, 0.425, 0.64, 22, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('at', 0.5, 0.49, 0.4, 17, { font: 'elegant', color: INK, bold: false, italic: true }),
        words('occasion', 0.5, 0.56, 0.84, 36, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        words('when', 0.5, 0.675, 0.84, 21, { font: 'clean', color: INK, bold: true }, { lines: 2 }),
        words('venue', 0.5, 0.75, 0.84, 20, { font: 'clean', color: INK, bold: false }, { lines: 2 }),
        words('dress', 0.5, 0.81, 0.84, 20, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.5, 0.89, 0.84, 17, { font: 'clean', color: INK, bold: false }, { lines: 2 })
      ],
      landscape: [
        border('border', 0.04, GOLD, 2.5),
        border('border2', 0.06, GOLD, 1),
        logo(0.17, 0.5, 0.13),
        words('host', 0.62, 0.14, 0.6, 17, { font: 'elegant', color: INK, bold: true }, { lines: 2 }),
        words('invite', 0.62, 0.245, 0.6, 18, { font: 'script', color: GOLD, bold: false }, { lines: 1 }),
        rule('guestline', 0.62, 0.375, 0.5, INK, 1.2),
        words('guest', 0.62, 0.35, 0.5, 20, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('at', 0.62, 0.425, 0.4, 15, { font: 'elegant', color: INK, bold: false, italic: true }),
        words('occasion', 0.62, 0.5, 0.62, 30, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        words('when', 0.62, 0.62, 0.62, 19, { font: 'clean', color: INK, bold: true }, { lines: 1 }),
        words('venue', 0.62, 0.71, 0.62, 18, { font: 'clean', color: INK, bold: false }, { lines: 1 }),
        words('dress', 0.62, 0.8, 0.62, 17, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.62, 0.89, 0.62, 15, { font: 'clean', color: INK, bold: false }, { lines: 1 })
      ]
    }),
    modern: Object.freeze({
      key: 'modern',
      label: 'Modern',
      note: 'Clean, set on the left',
      background: { type: 'color', value: '#ffffff' },
      portrait: [
        block('bar', 0.014, 0.5, 0.028, 1, { fill: NAVY, stroke: '' }),
        logo(0.86, 0.1, 0.09),
        words('host', 0.1, 0.1, 0.62, 20, { font: 'clean', color: GREY, bold: true, spacing: 6, align: 'left' }, { anchor: 'left', lines: 3 }),
        words('invite', 0.1, 0.22, 0.8, 21, { font: 'clean', color: GREY, bold: false, align: 'left' }, { anchor: 'left' }),
        rule('guestline', 0.45, 0.305, 0.7, INK, 1.5),
        words('guest', 0.1, 0.29, 0.7, 28, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('at', 0.1, 0.35, 0.5, 20, { font: 'clean', color: GREY, bold: false, italic: true, align: 'left' }, { anchor: 'left' }),
        words('occasion', 0.1, 0.435, 0.82, 66, { font: 'clean', color: NAVY, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        rule('rule', 0.2, 0.535, 0.2, NAVY, 4),
        words('when', 0.1, 0.61, 0.82, 26, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('venue', 0.1, 0.66, 0.82, 25, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('dress', 0.1, 0.71, 0.82, 23, { font: 'clean', color: NAVY, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('rsvp', 0.1, 0.875, 0.8, 18, { font: 'clean', color: GREY, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('note', 0.1, 0.91, 0.8, 17, { font: 'clean', color: GREY, bold: false, italic: true, align: 'left' }, { anchor: 'left', lines: 1 })
      ],
      landscape: [
        block('bar', 0.01, 0.5, 0.02, 1, { fill: NAVY, stroke: '' }),
        logo(0.9, 0.17, 0.11),
        words('host', 0.07, 0.12, 0.66, 18, { font: 'clean', color: GREY, bold: true, spacing: 6, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('invite', 0.07, 0.26, 0.7, 19, { font: 'clean', color: GREY, bold: false, align: 'left' }, { anchor: 'left' }),
        rule('guestline', 0.34, 0.365, 0.54, INK, 1.5),
        words('guest', 0.07, 0.345, 0.54, 26, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('at', 0.07, 0.415, 0.5, 18, { font: 'clean', color: GREY, bold: false, italic: true, align: 'left' }, { anchor: 'left' }),
        words('occasion', 0.07, 0.505, 0.84, 56, { font: 'clean', color: NAVY, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('when', 0.07, 0.66, 0.84, 22, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('venue', 0.07, 0.725, 0.84, 21, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('dress', 0.07, 0.785, 0.84, 20, { font: 'clean', color: NAVY, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('rsvp', 0.07, 0.885, 0.84, 16, { font: 'clean', color: GREY, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('note', 0.07, 0.93, 0.84, 15, { font: 'clean', color: GREY, bold: false, italic: true, align: 'left' }, { anchor: 'left', lines: 1 })
      ]
    })
  });

  /**
   * The card's scale: 1 on an A5 portrait card. It follows the page's area, so a tall, narrow DL
   * card gets larger words than its width alone would give, and is never more than 1.3 times the
   * shorter side, so nothing outgrows a narrow page.
   */
  function unitOf(sizeKey) {
    const width = core.PAGE_WIDTH;
    const height = core.pageHeight(sizeKey);
    return Math.min(Math.sqrt(width * height / Math.SQRT2), Math.min(width, height) * 1.3) / 1000;
  }

  /**
   * Where the card's design puts a part (its logo, say), as an item made from the details:
   * for a logo put back after it was removed. Null when the design has no such part.
   */
  /**
   * The one rule for "No photo" (code review 8): it applies only while the page has no design
   * frame. A page that still has its frame (a photo in it, an empty one, one the user locked, or
   * one an older PRISM put back) keeps the photo arrangement, whatever the details say. Returns
   * the details to lay a part out with, for a page built afresh from `items`.
   */
  function photoDetails(items, details) {
    const framed = (items || []).some((item) => item.role === 'photo');
    return framed && details?.photoMode === 'none' ? { ...details, photoMode: '' } : details;
  }

  function roleItem(collage, role, opts) {
    const key = collage?.card?.design;
    if (!DESIGNS[key]) return null;
    const { card, back, side, ...rest } = collage;
    // Where the design puts it with a logo wanted (a poster set to "No logo" still has a place),
    // with or without its photo as the page is (photoDetails).
    const made = applyDesign({ ...rest, items: [] }, key, photoDetails(collage.items, { ...detailsOf(collage), logoMode: '' }), opts);
    return made.items.find((item) => item.role === role) || null;
  }

  /** Where the design puts the part with `role` on one side ('front' or 'back') of the card. */
  function roleItemOn(collage, role, side, opts) {
    if (side !== 'back') return roleItem(collage, role, opts);
    const key = collage?.card?.design;
    if (!DESIGNS[key] || !collage.back) return null;
    const front = frontOf(collage);
    return backPage(front, key, detailsOf(front), opts).items.find((item) => item.role === role) || null;
  }

  /** The unit's three colours from the details ("#7a1f2b #c9a54a #1d2c55"), each checked. */
  function colourList(text) {
    const given = String(text || '').trim().split(/[\s,]+/);
    const fallback = DEFAULT_DETAILS.colours.split(' ');
    return [0, 1, 2].map((i) => (/^#[0-9a-f]{6}$/i.test(given[i] || '') ? given[i].toLowerCase() : fallback[i]));
  }

  /** What a new card or poster in this design starts with: the general wording, then the design's own. */
  function startDetails(designKey) {
    const design = DESIGNS[designKey];
    if (!design) return DEFAULT_DETAILS;
    return { ...(design.poster ? POSTER_DETAILS : DEFAULT_DETAILS), ...(design.sample || {}) };
  }

  /**
   * The designs offered for a size: the pocket designs for a pocket card, the others otherwise.
   * A custom size smaller than A6 is offered both, the pocket designs first (cold review 5).
   */
  function designsFor(sizeKey, kind) {
    if (kind === 'poster') return Object.values(DESIGNS).filter((design) => design.poster);
    const size = core.sizeOf(sizeKey);
    // A design may not suit some sizes (a full pocket invitation on a visiting card is too small to read).
    const cards = Object.values(DESIGNS).filter((design) => !design.poster && !(design.notFor || []).includes(sizeKey));
    if (size?.group === 'custom' && core.smallCard(sizeKey)) return [...cards.filter((design) => design.pocket), ...cards.filter((design) => !design.pocket)];
    const pocket = size?.group === 'pocket';
    return cards.filter((design) => Boolean(design.pocket) === pocket);
  }

  /**
   * The design a card keeps on a new size: its own when that size offers it, or the nearest one
   * offered (an invitation becomes the pocket invitation, a pocket card the classic invitation).
   */
  function designOnSize(designKey, sizeKey) {
    const design = DESIGNS[designKey];
    if (!design || design.poster) return designKey;
    const offered = designsFor(sizeKey).map((each) => each.key);
    if (offered.includes(designKey)) return designKey;
    const nearest = design.pocket ? ['classic'] : ['pocketinvite', 'pass'];
    return nearest.find((key) => offered.includes(key)) || offered[0] || designKey;
  }

  /** Whether a design follows the unit's colours: a part's colour is one of 'band1' to 'band3'. */
  function usesColours(designKey) {
    const design = DESIGNS[designKey];
    if (!design) return false;
    return [...design.portrait, ...(design.landscape || [])].some((part) => ['fill', 'stroke', 'color'].some((prop) => /^band[123]$/.test(part.look?.[prop] || '')));
  }

  /** Whether a page this shape takes a design's portrait arrangement (square counts as portrait). */
  const isPortrait = (sizeKey) => core.pageHeight(sizeKey) >= core.PAGE_WIDTH * 0.98;

  /**
   * The card's wording: what it was given when the design was last applied, with whatever the
   * user has since typed on the card itself (a text item's words are the field's words).
   */
  function detailsOf(collage) {
    const details = { ...DEFAULT_DETAILS, ...(collage?.card?.details || {}) };
    // A field the design placed on the card but which has no words now was emptied or deleted
    // on the page: it stays empty, so the next design does not bring it back. A field the design
    // never placed (the page was turned since, say) keeps its words for when it is. Words on
    // either side count.
    for (const field of collage?.card?.placed || []) if (FIELDS.includes(field)) details[field] = '';
    const onCard = new Set();
    for (const item of [...(collage?.items || []), ...(collage?.back?.items || [])]) {
      if (item.kind === 'text' && FIELDS.includes(item.field)) { details[item.field] = item.text; onCard.add(item.field); }
    }
    // A cover title showing the occasion's words follows the occasion (empty); one deleted on the
    // page stays deleted (a space), so a design change does not bring it back.
    // The occasion edited on the page itself counts too: a cover title still showing the occasion
    // as it was last given follows it.
    const given = collage?.card?.details || {};
    for (const [field, from] of Object.entries(FALLBACKS)) {
      const following = details[field] === details[from] || (given[field] === '' && details[field] === String(given[from] ?? ''));
      if (onCard.has(field) && following) details[field] = '';
      else if (!onCard.has(field) && (collage?.card?.placed || []).includes(field)) details[field] = ' ';
    }
    return details;
  }

  /**
   * The design's parts for each side of a card of this size: the one place that says which side
   * a field is on (a programme card has its programme on the front, a folded card its details
   * inside, a flat card its programme on the back).
   */
  function partsFor(designKey, sizeKey) {
    const design = DESIGNS[designKey];
    if (!design) return { front: [], back: [] };
    if (isFolded(sizeKey)) return { front: foldedParts(designKey, 'outside'), back: foldedParts(designKey, 'inside') };
    const front = isPortrait(sizeKey) ? design.portrait : (design.landscape || design.portrait);
    // A field the front already shows (a programme card's programme) is not on the back as well:
    // one field, one place, so the words typed are the words printed.
    const onFront = new Set(fieldsIn(front));
    return { front, back: backParts(designKey, isPortrait(sizeKey)).filter((part) => part.kind !== 'text' || !onFront.has(part.field)) };
  }
  function fieldsIn(parts) {
    return [...new Set(parts.filter((part) => part.kind === 'text').map((part) => part.field))];
  }

  /** Which side of the card a field is on, from the design: 'front', 'back', or null when it has none. */
  function sideOf(collage, field) {
    // A field already on the card is on its side, wherever the design would put it now (one the
    // user locked stays where it was through a size change: Codex on #3452).
    const showingBack = collage?.side === 'back' && collage.back;
    const frontItems = (showingBack ? collage.back.items : collage?.items) || [];
    const backItems = (showingBack ? collage.items : collage?.back?.items) || [];
    const has = (items) => items.some((item) => item.kind === 'text' && item.field === field);
    if (has(frontItems)) return 'front';
    if (collage?.back && has(backItems)) return 'back';
    const parts = partsFor(collage?.card?.design, collage?.size);
    if (fieldsIn(parts.front).includes(field)) return 'front';
    if (collage?.back && fieldsIn(parts.back).includes(field)) return 'back';
    return null;
  }

  /**
   * The edge a design field's words are set by when they change (code review 9, H1): 'left' or
   * 'right' for a part the design sets by that edge, null for one it centres. A left-aligned block
   * the design centres (the back's programme) keeps its middle as it grows, so a longer line
   * never pushes it past the border. A field the design does not place: null.
   */
  function fieldAnchor(collage, field) {
    const parts = partsFor(collage?.card?.design, collage?.size);
    const where = sideOf(collage, field);
    const part = (where === 'back' ? parts.back : parts.front).find((each) => each.kind === 'text' && each.field === field);
    return part?.anchor || null;
  }

  /** Every field the card shows words for, on either side. */
  function fieldsOnCard(collage) {
    const parts = partsFor(collage?.card?.design, collage?.size);
    return [...new Set([...fieldsIn(parts.front), ...(collage?.back ? fieldsIn(parts.back) : [])])];
  }

  /**
   * The fields a design shows words for: on a page of this size, both sides, from partsFor (the
   * one place that says which parts a card has: code review 7); with no size, in either
   * arrangement of its front.
   */
  function fieldsOf(designKey, sizeKey) {
    const design = DESIGNS[designKey];
    if (!design) return [];
    if (sizeKey) {
      const parts = partsFor(designKey, sizeKey);
      return [...new Set([...fieldsIn(parts.front), ...fieldsIn(parts.back)])];
    }
    return [...new Set([...design.portrait, ...(design.landscape || [])].filter((part) => part.kind === 'text').map((part) => part.field))];
  }

  /**
   * One side's items: the design's parts for it, made from the wording, then the user's own
   * items (and a second logo) from `items`, the side as it was. A photo in the side's frame
   * (`frameRole`) goes into the new frame, or stays as the user's own when there is none.
   */
  function makePage(items, parts, { width, height, unit, minSize = 0, wording, measure, newId, isCard, frameRole, lockedElsewhere = new Set() }) {
    // The unit's colours for the Regimental band: 'band1' to 'band3' in a look stand for them.
    const colours = colourList(wording.colours);
    const tinted = (look) => {
      const out = { ...look };
      const tints = {};
      for (const prop of ['fill', 'stroke', 'color']) {
        const match = /^band([123])$/.exec(out[prop] || '');
        if (match) {
          out[prop] = colours[Number(match[1]) - 1];
          tints[prop] = Number(match[1]);
        }
      }
      // Which colour each property follows, so the Card panel can recolour the band later.
      if (Object.keys(tints).length) out.tint = tints;
      return out;
    };
    // A photo the user put in the last design's frame goes into this design's frame, or stays
    // on the page as their own when this design has none.
    const framed = (items || []).filter((item) => item.role === frameRole && item.kind === 'photo' && !item.locked);
    // A locked part of the last design stays exactly as it is, and the new design leaves out its
    // own part for that role; a hidden one comes back hidden.
    const lockedRoles = new Set((items || []).filter((item) => item.role && item.locked).map((item) => item.role));
    // A locked part keeps its place in the stacking order: it goes where the new design has its
    // part for that role, so a locked roundel stays behind the logo (Codex on #3452).
    const lockedByRole = new Map((items || []).filter((item) => item.role && item.locked).map((item) => [item.role, item]));
    const placedLocked = new Set();
    const hiddenRoles = new Set((items || []).filter((item) => item.role && item.hidden).map((item) => item.role));
    // Hidden by the design for want of a logo (the roundel, the tile behind a crest), not by the
    // user: it stays marked so, to come back with a logo.
    const autoHiddenRoles = new Set((items || []).filter((item) => item.role && item.hidden && item.autoHidden).map((item) => item.role));
    const made = [];
    // A poster with "No logo" (or a logo place it has no logo for) moves parts up into the space.
    const chosenLogo = (items || []).find((item) => item.role === 'logo' && item.kind !== 'slot');
    const noLogo = wording.logoMode === 'none' && !chosenLogo;
    // "No photo" (UX review 8): the design's frame is left out and the parts that say where they
    // go without it (`noPhoto`) move into its space. A photo in the frame, or a frame the user
    // locked, or an empty frame still on the page (a photo taken out of it), keeps the photo
    // arrangement: the frame shows where a photo is wanted.
    const noPhoto = wording.photoMode === 'none' && !(items || []).some((item) => item.role === frameRole);
    for (const raw of parts) {
      if (raw.onlyNoPhoto && !noPhoto) continue;
      let part = noLogo && raw.yNoLogo !== undefined ? { ...raw, y: raw.yNoLogo } : raw;
      if (noPhoto && part.kind === 'slot') continue;
      if (noPhoto && raw.noPhoto) part = { ...part, ...raw.noPhoto };
      if (lockedRoles.has(part.role)) {
        const kept = lockedByRole.get(part.role);
        if (kept && !placedLocked.has(kept.id)) { made.push(kept); placedLocked.add(kept.id); }
        continue;
      }
      // A part locked on the card's other side (a guest field locked on the front of a card
      // turned folded) is not made again here, so the card never has two (Codex on #3452).
      if (lockedElsewhere.has(part.role)) continue;
      const before = made.length;
      if (part.kind === 'logoplace') {
        // The place starts inside the page's top edge, with a little room: on a landscape page,
        // where it would run over the edge, it is made smaller and keeps its bottom where it was,
        // clear of the heading.
        const room = height * 0.015;
        const full = part.w * 1000 * unit;
        const bottom = part.y * height + full / part.aspect / 2;
        const w = Math.min(full, (bottom - room) * part.aspect);
        const y = bottom - w / part.aspect / 2;
        // The chosen logo sized for its shape, never taller than the place (cold review 5: a tall
        // logo grew past the poster's top edge on a design change).
        const own = chosenLogo ? chosenLogo.width / core.itemHeight(chosenLogo) : 0;
        if (chosenLogo) made.push({ ...chosenLogo, id: newId(), x: part.x * width, y, width: posterLogoWidth(w, part.aspect, own), angle: 0 });
        else if (!noLogo) made.push({ ...core.slotItem(newId(), { x: part.x * width, y, width: w, aspect: part.aspect }), role: 'logo' });
        continue;
      }
      const look = part.look ? tinted(part.look) : {};
      if (part.kind === 'shape' && part.inset !== undefined) {
        const inset = part.inset * Math.min(width, height);
        made.push(core.shapeItem(newId(), 'rect', { x: width / 2, y: height / 2, width: width - inset * 2, aspect: (width - inset * 2) / (height - inset * 2) }, { ...look, strokeWidth: part.look.strokeWidth * unit, role: part.role }));
      } else if (part.kind === 'shape' && part.h !== undefined) {
        const w = part.w * width;
        const h = part.h * height;
        made.push(core.shapeItem(newId(), part.form, { x: part.x * width, y: part.y * height, width: w, aspect: w / h }, { ...look, strokeWidth: (part.look.strokeWidth || 0) * unit, radius: (part.look.radius || 0) * unit, role: part.role }));
      } else if (part.kind === 'shape' && part.scaled !== undefined) {
        const w = part.scaled * 1000 * unit;
        made.push(core.shapeItem(newId(), part.form, { x: part.x * width, y: part.y * height, width: w, aspect: part.aspect }, { ...look, strokeWidth: (part.look.strokeWidth || 0) * unit, role: part.role }));
      } else if (part.kind === 'shape') {
        // A rule across, or standing upright: w of 0 is a line down the page's middle third,
        // and `len` an upright line that long a share of the page's height.
        const upright = part.w === 0 || part.len !== undefined;
        const length = part.len !== undefined ? part.len * height : part.w === 0 ? height * 0.6 : part.w * width;
        const item = core.shapeItem(newId(), 'line', { x: part.x * width, y: part.y * height, width: length }, { ...look, strokeWidth: part.look.strokeWidth * unit, role: part.role });
        if (upright) item.angle = 90;
        made.push(item);
      } else if (part.kind === 'slot') {
        const place = { x: part.x * width, y: part.y * height, width: part.w * width, aspect: (part.w * width) / (part.h * height) };
        const slot = { ...core.slotItem(newId(), place), role: frameRole };
        const photo = framed.shift();
        made.push(photo ? { ...core.fillSlot(slot, photo.photo), crop: { ...(photo.crop || core.FULL_CROP) }, look: photo.look || 'none' } : slot);
      } else if (part.kind === 'sticker' && part.role === 'logo' && isCard) {
        // An existing card keeps its logo (the SDD logo, a crest or the user's own), placed and
        // sized by this design; a card whose logo was removed stays without one (PR 7).
        const logo = (items || []).find((item) => item.role === 'logo');
        if (logo) {
          // Sized by area for its own shape: a wide unit logo is not squeezed into a crest's width.
          const own = logo.kind === 'slot' ? part.aspect : logo.width / core.itemHeight(logo);
          const w = logoWidth(part.w * 1000 * unit, part.aspect || SDD_ASPECT, own);
          made.push({ ...logo, id: newId(), x: part.x * width, y: part.y * height, width: w, angle: 0 });
        }
      } else if (part.kind === 'sticker') {
        const w = part.w * 1000 * unit;
        made.push(core.stickerItem(newId(), part.sticker, { x: part.x * width, y: part.y * height, width: w, aspect: part.aspect }, { role: part.role }));
      } else {
        const text = wordsFor(wording, part.field).slice(0, core.TEXT_LIMITS.maxLength);
        if (!text.trim()) continue;
        const item = core.textItem(newId(), text, { x: part.x * width, y: part.y * height }, {
          ...look,
          // Script and italic lines are harder to read small: 7 pt for them, 6 pt for the rest.
          size: Math.max(Math.round(part.size * unit), Math.ceil(['script', 'formal'].includes(look.font) || look.italic ? minSize * 7 / 6 : minSize)),
          maxWidth: part.w * width,
          maxLines: part.lines || 1,
          field: part.field,
          role: part.role
        });
        // Words fitted to their width never go below 6 pt; past it they take another line. A
        // guest's name stops at 60% of its size.
        const floor = Math.ceil(minSize);
        if (floor > 0) item.minSize = floor;
        if (part.field === 'guest') item.minSize = Math.max(floor, Math.round(item.size * 0.6));
        // No word left alone on a wrapped line's last line (cold review 9, L1).
        if (item.maxLines > 1) item.maxWidth = balancedWidth(item, measure);
        const layout = core.layoutText(item, measure);
        item.width = layout.width;
        item.aspect = layout.width / layout.height;
        // A guest's name on two lines grows upwards, off the line drawn under it.
        if (part.field === 'guest' && layout.lines > 1) item.y -= (layout.height - core.layoutText({ ...item, text: 'X' }, measure).height) / 2;
        if (part.anchor === 'left') item.x = part.x * width + layout.width / 2;
        if (part.anchor === 'right') item.x = part.x * width - layout.width / 2;
        made.push(item);
      }
      // A part turned (a poster's band) or with rounded corners (a speaker's photo).
      if (made.length > before) {
        const item = made[made.length - 1];
        if (hiddenRoles.has(part.role)) item.hidden = true;
        if (autoHiddenRoles.has(part.role)) item.autoHidden = true;
        if (part.angle) item.angle = part.angle;
        if (part.radius && (item.kind === 'slot' || item.kind === 'photo')) item.radius = Math.min(part.radius, core.LAYOUT_LIMITS.radiusMax);
      }
    }
    // A logo in a white box (Ceremonial) fits inside it with a margin, whatever its shape.
    const box = made.find((item) => item.role === 'logobox');
    const boxed = made.find((item) => item.role === 'logo');
    if (box && boxed && !boxed.locked) {
      const aspect = boxed.width / core.itemHeight(boxed);
      boxed.width = Math.min(boxed.width, box.width * 0.84, core.itemHeight(box) * 0.86 * aspect);
      boxed.x = box.x;
      boxed.y = box.y;
    }
    // A logo inside a border keeps clear of its line (a very wide card squeezes the logo up to it):
    // made smaller about its middle until it is 12 units inside the frame.
    const frame = made.find((item) => item.role === 'border' && item.kind === 'shape');
    const framedLogo = made.find((item) => item.role === 'logo');
    if (frame && framedLogo && !framedLogo.locked) {
      const top = frame.y - core.itemHeight(frame) / 2 + 12;
      const half = core.itemHeight(framedLogo) / 2;
      if (framedLogo.y - half < top && framedLogo.y > top) framedLogo.width *= (framedLogo.y - top) / half;
    }
    // A poster's logo place ends above the words below it (on a landscape page the heading comes
    // up close): made smaller, keeping its top, so it never covers the heading (cold review 5).
    const posterLogo = parts.some((part) => part.kind === 'logoplace') ? made.find((item) => item.role === 'logo' && !item.locked) : null;
    if (posterLogo) {
      const logoTop = posterLogo.y - core.itemHeight(posterLogo) / 2;
      const left = posterLogo.x - posterLogo.width / 2;
      const right = posterLogo.x + posterLogo.width / 2;
      const below = made.filter((item) => item.kind === 'text' && !item.hidden).map(itemBox).filter((b) => b.top > logoTop && b.left < right && b.right > left);
      const limit = Math.min(...below.map((b) => b.top)) - height * 0.01;
      const tall = core.itemHeight(posterLogo);
      if (Number.isFinite(limit) && logoTop + tall > limit && limit - logoTop > tall * 0.3) {
        posterLogo.width *= (limit - logoTop) / tall;
        posterLogo.y = logoTop + core.itemHeight(posterLogo) / 2;
      }
    }
    // A second logo the user added goes beside the design's logo, as tall as it: the two share
    // the middle where the design centres its logo, or face each other across the card.
    const designLogo = made.find((item) => item.role === 'logo');
    const seconds = (items || []).filter((item) => item.role === 'logo2').map((item) => {
      if (!designLogo || item.locked) return item;
      const height = core.itemHeight(designLogo);
      const w2 = height * (item.width / core.itemHeight(item));
      const placed = { ...item, width: w2, y: designLogo.y, angle: 0 };
      const centred = Math.abs(designLogo.x - width / 2) < width * 0.05;
      if (centred && !designLogo.locked) {
        const gap = designLogo.width * 0.35;
        const left = width / 2 - (designLogo.width + gap + w2) / 2;
        designLogo.x = left + designLogo.width / 2;
        placed.x = left + designLogo.width + gap + w2 / 2;
      } else if (centred) {
        // A locked centred first logo stays put; the second goes beside it, on the right if it
        // fits, else on the left (Codex on #3453).
        const gap = designLogo.width * 0.35;
        const right = designLogo.x + designLogo.width / 2 + gap + w2 / 2;
        placed.x = right + w2 / 2 <= width ? right : designLogo.x - designLogo.width / 2 - gap - w2 / 2;
      } else {
        placed.x = width - designLogo.x;
      }
      return placed;
    });
    // The roundel behind the logo follows it: hidden with none, wide enough for a wide logo or two.
    const roundel = made.find((item) => item.role === 'roundel');
    if (roundel) fitRoundel([...made, ...seconds], { width: roundel.width, aspect: roundel.aspect }, { hadLogo: (items || []).some((item) => item.role === 'logo' && item.kind !== 'slot') });
    // Photos from the last design's frame with no frame here become the user's own.
    const leftOver = framed.map(({ role, ...item }) => item);
    // The user's own items, and a second logo they added, stay where they are.
    const own = (items || []).filter((item) => !item.role || (item.locked && !placedLocked.has(item.id))).map((item) => seconds.find((second) => second.id === item.id) || item);
    // The second logo, placed beside the design's logo, with the user's own items.
    for (const second of seconds) if (!own.some((item) => item.id === second.id)) own.push(second);
    return fitLogoTile([...made, ...own, ...leftOver], { width, height });
  }

  /**
   * The width a design's words wrap to so that no line they wrap onto ends with one word alone
   * ("…company / of", "…Development / Division": cold review 9, L1): a little narrower than
   * `maxWidth`, as little as will do, with the same number of lines at the same size. Lines the
   * user broke are kept; a paragraph that is one word is left as it is. Returns `maxWidth` when
   * nothing is alone or no narrower width keeps the lines and size.
   */
  function balancedWidth(item, measure) {
    const first = core.layoutText(item, measure);
    if (first.lines < 2) return item.maxWidth;
    const paragraphs = String(item.text || '').split(String.fromCharCode(10)).filter((line) => line.trim().split(/ +/).length > 3);
    // A paragraph wrapped at this width ends in a word alone (at the size the words print).
    const alone = (width) => paragraphs.some((text) => {
      const layout = core.layoutText({ ...item, text, maxWidth: width, size: first.size, minSize: first.size, maxLines: 99 }, measure);
      return layout.lines > 1 && !/ /.test(String(layout.glyphs[layout.glyphs.length - 1].ch).trim());
    });
    if (!alone(item.maxWidth)) return item.maxWidth;
    for (let step = 1; step <= 25; step += 1) {
      const width = item.maxWidth * (1 - step * 0.02);
      const layout = core.layoutText({ ...item, maxWidth: width }, measure);
      if (layout.lines !== first.lines || Math.abs(layout.size - first.size) > 0.01) break;
      if (!alone(width)) return width;
    }
    return item.maxWidth;
  }

  // The logos each tile goes behind.
  const TILE_LOGOS = Object.freeze({ logotile: Object.freeze(['logo', 'logo2']), backlogotile: Object.freeze(['backlogo']) });
  const isSddLogo = (item) => item.kind === 'sticker' && item.sticker === 'logo:sdd';

  /**
   * Where a second logo goes (code review 9, H1: it used to mirror an off-centre first logo onto
   * the words). `second` is the new logo already as tall as the first, `items` the page's items.
   * Places are tried in order: both sharing the middle (a centred first logo the user has not
   * locked), right beside the first, left beside it, one above the other about its middle, below
   * it, above it, the mirror place across the page; then
   * the same at 80%; then the two stacked in the first logo's own column, both made smaller to fit
   * (Ceremonial landscape, where words sit on every side); then the beside places at 60%. The first
   * place on the page and clear of the words, the photo and the first logo wins. What the design
   * already puts the first logo across (Ceremonial's photo edge) does not count. With no clear
   * place, beside the first on the page. Returns { firstX, firstY, firstWidth, x, y, width }.
   */
  function secondLogoPlace(items, first, second, { width: pageWidth, height: pageHeight }) {
    const margin = Math.min(pageWidth, pageHeight) * 0.02;
    const overlaps = (a, b) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1;
    const across = itemBox(first);
    // Words, the photo and the design's lines block it; the logo's own backings (box, tile, roundel)
    // and empty words do not.
    const backings = new Set(['logobox', 'logotile', 'backlogotile', 'roundel', 'logo2']);
    const blockers = (items || []).filter((item) => item.id !== first.id && !item.hidden && !backings.has(item.role) && (item.kind !== 'text' || String(item.text || '').trim()) && !overlaps(itemBox(item), across));
    const heightAt = (w) => core.itemHeight({ ...second, width: w });
    const gap = first.width * 0.35;
    const firstH = core.itemHeight(first);
    const spacing = Math.max(8, firstH * 0.12);
    const middle = !first.locked && Math.abs(first.x - pageWidth / 2) < pageWidth * 0.05;
    const own = { firstX: first.x, firstY: first.y, firstWidth: first.width };
    // A white box behind the logo (Ceremonial) grows round the pair (fitLogoBox): the grown box
    // must keep clear of the words too.
    const logoBox = (items || []).find((item) => item.role === 'logobox' && item.kind === 'shape' && !item.hidden && !item.locked);
    const boxNow = logoBox && itemBox(logoBox);
    const boxBlockers = logoBox ? blockers.filter((item) => !overlaps(itemBox(item), boxNow)) : [];
    const clear = (place) => {
      const box = itemBox({ ...second, x: place.x, y: place.y, width: place.width });
      const firstBox = itemBox({ ...first, x: place.firstX, y: place.firstY, width: place.firstWidth });
      if (box.left < margin || box.right > pageWidth - margin || box.top < margin || box.bottom > pageHeight - margin) return false;
      if (overlaps(box, firstBox)) return false;
      const moved = place.firstX !== first.x || place.firstY !== first.y || place.firstWidth !== first.width;
      if (blockers.some((item) => { const b = itemBox(item); return overlaps(b, box) || (moved && overlaps(b, firstBox)); })) return false;
      if (!logoBox) return true;
      const pad = Math.max(box.bottom - box.top, firstBox.bottom - firstBox.top) * 0.1;
      const grown = {
        left: Math.min(boxNow.left, box.left - pad, firstBox.left - pad),
        right: Math.max(boxNow.right, box.right + pad, firstBox.right + pad),
        top: Math.min(boxNow.top, box.top - pad, firstBox.top - pad),
        bottom: Math.max(boxNow.bottom, box.bottom + pad, firstBox.bottom + pad),
      };
      return grown.left >= 0 && grown.right <= pageWidth && grown.top >= 0 && grown.bottom <= pageHeight && !boxBlockers.some((item) => overlaps(itemBox(item), grown));
    };
    const beside = (w) => {
      const h = heightAt(w);
      const list = [];
      if (middle) {
        const left = pageWidth / 2 - (first.width + gap + w) / 2;
        list.push({ ...own, firstX: left + first.width / 2, x: left + first.width + gap + w / 2, y: first.y });
      }
      list.push({ ...own, x: first.x + first.width / 2 + gap + w / 2, y: first.y });
      list.push({ ...own, x: first.x - first.width / 2 - gap - w / 2, y: first.y });
      // One above the other, sharing the first logo's middle (a logo panel beside the words).
      if (!first.locked) list.push({ ...own, firstY: first.y - (h + spacing) / 2, x: first.x, y: first.y + (firstH + spacing) / 2 });
      list.push({ ...own, x: first.x, y: first.y + firstH / 2 + spacing + h / 2 });
      list.push({ ...own, x: first.x, y: first.y - firstH / 2 - spacing - h / 2 });
      // The mirror place last: across the page, it often lands among the words.
      if (!middle) list.push({ ...own, x: pageWidth - first.x, y: first.y });
      return list.map((place) => ({ ...place, width: w }));
    };
    const stacked = () => {
      if (first.locked) return [];
      const top = first.y - firstH / 2;
      const column = { left: first.x - first.width / 2, right: first.x + first.width / 2 };
      const below = blockers.map(itemBox).filter((b) => b.top >= top && Math.min(b.right, column.right) - Math.max(b.left, column.left) > 1).map((b) => b.top);
      // Room left below for the white box's margin round the pair (fitLogoBox).
      const room = Math.min(pageHeight - margin, ...below) - spacing * 2.5 - top;
      const each = Math.min(firstH, (room - spacing) / 2);
      if (each < firstH * 0.5) return [];
      // A wide second logo may be shorter than the first so it keeps clear of the words beside.
      return [1, 0.8, 0.6].map((scale) => {
        const tall = each * scale;
        return { firstX: first.x, firstY: top + each / 2, firstWidth: first.width * each / firstH, x: first.x, y: top + each + spacing + tall / 2, width: second.width * tall / heightAt(second.width) };
      });
    };
    const tries = [...beside(second.width), ...beside(second.width * 0.8), ...stacked(), ...beside(second.width * 0.6)];
    const found = tries.find(clear);
    if (found) return found;
    const w = second.width;
    const right = first.x + first.width / 2 + gap + w / 2;
    const x = right + w / 2 <= pageWidth ? right : Math.max(w / 2, first.x - first.width / 2 - gap - w / 2);
    return { ...own, x: Math.min(pageWidth - w / 2, Math.max(w / 2, x)), y: first.y, width: w };
  }

  /**
   * The white box behind the logo (Ceremonial) holds both logos when there are two: it grows from
   * the design's box (`base`) to take them in with a margin, and goes back to `base` with one logo
   * again, unless the user moved it away (then it is theirs and stays). A locked or hidden box
   * stays as it is. Returns the items with the box replaced, or the same array.
   */
  function fitLogoBox(items, base) {
    const box = items.find((item) => item.role === 'logobox');
    if (!box || !base || box.locked || box.hidden || box.kind !== 'shape') return items;
    const logos = items.filter((item) => (item.role === 'logo' || item.role === 'logo2') && item.kind !== 'slot' && !item.hidden);
    const b0 = itemBox(base);
    const now = itemBox(box);
    let next = null;
    if (logos.length > 1) {
      const boxes = logos.map(itemBox);
      const pad = Math.max(...boxes.map((b) => b.bottom - b.top)) * 0.1;
      const left = Math.min(b0.left, ...boxes.map((b) => b.left - pad));
      const right = Math.max(b0.right, ...boxes.map((b) => b.right + pad));
      const top = Math.min(b0.top, ...boxes.map((b) => b.top - pad));
      const bottom = Math.max(b0.bottom, ...boxes.map((b) => b.bottom + pad));
      next = { x: (left + right) / 2, y: (top + bottom) / 2, width: right - left, aspect: (right - left) / (bottom - top) };
    } else if (now.left <= b0.left + 0.5 && now.right >= b0.right - 0.5 && now.top <= b0.top + 0.5 && now.bottom >= b0.bottom - 0.5) {
      // Grown round two logos before (it still holds the design's box): back to the design's.
      next = { x: base.x, y: base.y, width: base.width, aspect: base.aspect };
    }
    if (!next || Object.keys(next).every((key) => Math.abs(next[key] - box[key]) < 0.01)) return items;
    return items.map((item) => (item === box ? { ...box, ...next } : item));
  }

  /**
   * The light tile behind the logo on a dark design (M3), fitted round the logos it goes behind
   * with a margin of 14% of their height. Where the page's edge is too near for that margin (a
   * poster's logo sits close to its corner), the logos are made smaller about their middle so the
   * tile keeps clear of the edge. Hidden (marked autoHidden, as the roundel is) when there is no
   * logo or only the SDD logo; one the user hid or locked stays as it is. Returns the items with
   * the tile (and any logo made smaller) replaced, never changing the items given, or the same
   * array when nothing changed. `resize: false` (a logo moved on the page) only moves the tile
   * with the logos, never making a logo the user placed smaller.
   */
  function fitLogoTile(items, { width, height, resize = true }) {
    let out = items;
    for (const [role, logoRoles] of Object.entries(TILE_LOGOS)) {
      const tile = out.find((item) => item.role === role);
      if (!tile || tile.locked || tile.kind !== 'shape') continue;
      const logos = out.filter((item) => logoRoles.includes(item.role) && item.kind !== 'slot' && !item.hidden);
      const wanted = logos.some((item) => !isSddLogo(item));
      const next = { ...tile };
      let resized = new Map();
      if (logos.length) {
        let boxes = logos.map(itemBox);
        const bounds = () => ({ left: Math.min(...boxes.map((b) => b.left)), right: Math.max(...boxes.map((b) => b.right)), top: Math.min(...boxes.map((b) => b.top)), bottom: Math.max(...boxes.map((b) => b.bottom)) });
        let b = bounds();
        const margin = Math.min(width, height) * 0.04;
        if (resize && wanted && !logos.some((item) => item.locked)) {
          // How much smaller the logos must be for the tile's margin to clear the page's edges.
          const cx = (b.left + b.right) / 2;
          const cy = (b.top + b.bottom) / 2;
          const halfW = (b.right - b.left) / 2;
          const halfH = (b.bottom - b.top) / 2;
          const pad = halfH * 2 * 0.14;
          const roomX = Math.min(cx, width - cx) - margin;
          const roomY = Math.min(cy, height - cy) - margin;
          const scale = Math.max(0.5, Math.min(1, roomX / (halfW + pad), roomY / (halfH + pad)));
          if (scale < 0.999) {
            resized = new Map(logos.map((item) => [item.id, { ...item, x: cx + (item.x - cx) * scale, y: cy + (item.y - cy) * scale, width: item.width * scale }]));
            boxes = [...resized.values()].map(itemBox);
            b = bounds();
          }
        }
        const room = Math.min(b.left, width - b.right, b.top, height - b.bottom) - 2;
        const pad = Math.max(0, Math.min((b.bottom - b.top) * 0.14, room));
        const w = b.right - b.left + pad * 2;
        const h = b.bottom - b.top + pad * 2;
        Object.assign(next, { x: (b.left + b.right) / 2, y: (b.top + b.bottom) / 2, width: w, aspect: w / h, radius: Math.min(pad * 0.9, w / 2, h / 2) });
      }
      if (!wanted) {
        if (!next.hidden) { next.hidden = true; next.autoHidden = true; }
      } else if (next.autoHidden) {
        delete next.hidden;
        delete next.autoHidden;
      }
      const same = !resized.size && Object.keys({ ...tile, ...next }).every((key) => tile[key] === next[key]);
      if (!same) out = out.map((item) => (item === tile ? next : resized.get(item.id) || item));
    }
    return out;
  }

  /**
   * The front's items with the logo tile fitted after a logo change (Card details). A card made
   * before the tile (an older draft of a dark design) gets one behind its logo when a logo that
   * needs it is chosen; nothing else changes.
   */
  function logoTileItems(collage, opts) {
    let items = collage.items || [];
    const logoAt = items.findIndex((item) => item.role === 'logo' && item.kind !== 'slot');
    const needs = items.some((item) => /^logo2?$/.test(item.role) && item.kind !== 'slot' && !isSddLogo(item));
    const hasTile = (partsFor(collage.card?.design, collage.size).front || []).some((part) => part.role === 'logotile');
    if (needs && hasTile && logoAt >= 0 && !items.some((item) => item.role === 'logotile')) {
      const base = roleItem(collage, 'logotile', opts);
      if (base) items = [...items.slice(0, logoAt), { ...base, id: opts.newId() }, ...items.slice(logoAt)];
    }
    return fitLogoTile(items, { width: core.PAGE_WIDTH, height: core.pageHeight(collage.size) });
  }

  /** The look of a design's back: its background and the colours of the title, rule and words. */
  const BACK_LOOKS = lookupTable({
    classic: { background: { type: 'color', value: '#fbf7ee' }, title: MAROON, accent: GOLD, ink: INK },
    navy: { background: { type: 'gradient', key: 'navy' }, title: PALE_GOLD, accent: PALE_GOLD, ink: IVORY, tile: NAVY_TILE },
    regimental: { background: { type: 'color', value: '#ffffff' }, title: 'band1', accent: 'band2', ink: INK },
    ceremonial: { background: { type: 'color', value: '#ffffff' }, title: MAROON, accent: MAROON, ink: INK },
    bilingual: { background: { type: 'color', value: '#fffdf7' }, title: MAROON, accent: GOLD, ink: INK },
    modern: { background: { type: 'color', value: '#ffffff' }, title: NAVY, accent: NAVY, ink: INK }
  });

  /** The back's parts for a design: the programme, a frame for a route sketch or venue photo, a note. */
  function backParts(designKey, portrait) {
    const look = BACK_LOOKS[designKey] || BACK_LOOKS.classic;
    // No empty photo frame (cold review 6: one saved empty left a hole): the programme has the
    // page, and a photo the user adds from Photos goes on it as their own; a photo from an older
    // back's frame stays on the back as theirs.
    return portrait
      ? [
        border('backborder', 0.04, look.accent, 1.2),
        words('programmeTitle', 0.5, 0.13, 0.7, 40, { font: 'elegant', color: look.title, bold: true }, { lines: 1 }),
        rule('backrule', 0.5, 0.19, 0.2, look.accent, 2),
        words('programme', 0.5, 0.46, 0.8, 32, { font: 'clean', color: look.ink, bold: false, align: 'left' }, { lines: 12 }),
        words('backNote', 0.5, 0.82, 0.8, 20, { font: 'clean', color: look.ink, bold: false, italic: true }, { lines: 2 }),
        ...(look.tile ? [logoTile(0.5, 0.91, 0.045, look.tile, 'backlogotile')] : []),
        { ...logo(0.5, 0.91, 0.045), role: 'backlogo' }
      ]
      : [
        border('backborder', 0.04, look.accent, 1.2),
        words('programmeTitle', 0.5, 0.14, 0.6, 36, { font: 'elegant', color: look.title, bold: true }, { lines: 1 }),
        rule('backrule', 0.5, 0.23, 0.16, look.accent, 2),
        words('programme', 0.5, 0.54, 0.7, 27, { font: 'clean', color: look.ink, bold: false, align: 'left' }, { lines: 10 }),
        words('backNote', 0.5, 0.88, 0.84, 18, { font: 'clean', color: look.ink, bold: false, italic: true }, { lines: 1 })
      ];
  }

  /** Whether a size is a folded card: a flat sheet, outside and inside, folded down the middle. */
  const isFolded = (sizeKey) => core.sizeOf(sizeKey).group === 'folded';

  // Folded cards (PR 9c; cold review 7). A folded sheet is landscape and each of its four panels
  // is half of it: the outside has the back cover on the left and the front cover on the right,
  // the inside the invitation on the left and the occasion and details on the right. Each design
  // gives its folded card its own look: its borders, bands, bars, photo, fonts and Hindi.
  //
  // onPanel() places a part on a panel: its x and w are given as fractions of the panel, not of
  // the sheet; y and h stay fractions of the sheet's height. Logos and roundels keep their size by
  // the card's scale.
  const PANEL_AT = Object.freeze({ back: 0, cover: 0.5, left: 0, right: 0.5 });
  // A panel's width over the sheet's height (a folded sheet is A5 or A4 on its side: 1 to 1.41),
  // so a frame can be inset by the same distance at its sides and at its top and bottom.
  const PANEL_SHAPE = 0.708;
  function onPanel(panel, part) {
    const out = { ...part, x: PANEL_AT[panel] + part.x * 0.5 };
    const byPanel = part.kind === 'text' || part.kind === 'slot' || (part.kind === 'shape' && part.scaled === undefined);
    if (byPanel && part.w !== undefined) out.w = part.w * 0.5;
    return out;
  }
  const onAll = (panel, parts) => parts.map((part) => onPanel(panel, part));
  // A frame round a panel, `inset` of the panel's width in from its edges; `top` moves its top
  // edge down (as a share of the sheet's height) to clear a band above it.
  function panelFrame(panel, role, inset, stroke, strokeWidth, top) {
    const y0 = top ?? inset * PANEL_SHAPE;
    const y1 = 1 - inset * PANEL_SHAPE;
    return block(role, PANEL_AT[panel] + 0.25, (y0 + y1) / 2, (1 - 2 * inset) * 0.5, y1 - y0, { stroke, strokeWidth, fill: '' });
  }
  // The back cover: framed like every other panel, with the note in its middle between two short
  // rules, so it reads as a finished panel rather than a stray line (cold review 7).
  const backCover = (frames, noteLook, accent) => [
    ...frames,
    ...onAll('back', [
      rule('backcoverrule', 0.5, 0.45, 0.14, accent, 1.2),
      words('note', 0.5, 0.5, 0.72, 18, noteLook, { lines: 2 }),
      rule('backcoverrule2', 0.5, 0.55, 0.14, accent, 1.2)
    ])
  ];

  // What each panel carries (UX review 8, M2; cold review 9, M2). The front cover: the logo and
  // the cover title, which shows the occasion until other words are typed for it (coverTitle,
  // FALLBACKS). The inside left: the whole invitation, "The Commandant and Officers request the
  // pleasure of the company of ____ at the Raising Day Dinner". The inside right: the date,
  // venue, dress and R.S.V.P. Each field is on one side only. Both inside pages are centred on
  // the panel's middle and fill it, so neither is top-heavy.

  /** Classic ivory and Navy and gold: a double border on every panel, engraved host, formal script. */
  function formalFold(c) {
    const frames = (panel, role) => [panelFrame(panel, role, 0.05, c.line, c.outer), panelFrame(panel, `${role}b`, 0.075, c.line, c.inner)];
    return {
      outside: [
        ...backCover(frames('back', 'backcover'), { font: 'elegant', color: c.ink, bold: false, italic: true }, c.accent),
        ...frames('cover', 'coverborder'),
        ...onAll('cover', [
          // A light tile behind a dark crest on a dark design (M3); hidden for the SDD logo.
          ...(c.tile ? [logoTile(0.5, 0.29, 0.12, c.tile)] : []),
          logo(0.5, 0.29, 0.12),
          words('coverTitle', 0.5, 0.54, 0.76, 40, { font: 'elegant', color: c.title, bold: true }, { lines: 3 }),
          rule('coverrule', 0.5, 0.68, 0.2, c.accent, 2)
        ])
      ],
      inside: [
        ...frames('left', 'innerborder'),
        ...frames('right', 'innerborder2'),
        ...onAll('left', [
          words('host', 0.5, 0.26, 0.76, 24, c.host, { lines: 3 }),
          words('invite', 0.5, 0.39, 0.76, 32, { font: 'formal', color: c.invite, bold: false }, { lines: 2 }),
          rule('guestline', 0.5, 0.525, 0.66, c.ink, 1.5),
          words('guest', 0.5, 0.505, 0.66, 30, { font: 'script', color: c.ink, bold: false }, { lines: 1 }),
          words('at', 0.5, 0.6, 0.4, 22, { font: 'elegant', color: c.ink, bold: false, italic: true }),
          words('occasion', 0.5, 0.705, 0.8, 40, { font: 'elegant', color: c.title, bold: true }, { lines: 3 }),
          rule('leftrule', 0.5, 0.83, 0.2, c.accent, 2)
        ]),
        ...onAll('right', [
          words('when', 0.5, 0.32, 0.78, 25, { font: 'clean', color: c.ink, bold: true }, { lines: 2 }),
          words('venue', 0.5, 0.425, 0.78, 24, { font: 'clean', color: c.ink, bold: false }, { lines: 2 }),
          words('dress', 0.5, 0.52, 0.78, 23, { font: 'clean', color: c.accent, bold: true }, { lines: 1 }),
          rule('innerrule', 0.5, 0.61, 0.2, c.accent, 2),
          words('rsvp', 0.5, 0.7, 0.78, 20, { font: 'clean', color: c.ink, bold: false }, { lines: 2 })
        ])
      ]
    };
  }

  /** Regimental: the three bands in the unit's colours across the cover, the roundel behind the logo. */
  function regimentalFold() {
    const frame = (panel, role, top) => panelFrame(panel, role, 0.05, 'band2', 1.2, top);
    return {
      outside: [
        ...backCover([frame('back', 'backcover')], { font: 'elegant', color: INK, bold: false, italic: true }, 'band2'),
        ...onAll('cover', [
          block('band1', 0.5, 0.017, 1, 0.034, { fill: 'band1', stroke: '' }),
          block('band2', 0.5, 0.051, 1, 0.034, { fill: 'band2', stroke: '' }),
          block('band3', 0.5, 0.085, 1, 0.034, { fill: 'band3', stroke: '' })
        ]),
        frame('cover', 'coverborder', 0.137),
        ...onAll('cover', [
          roundel('roundel', 'ellipse', 0.5, 0.35, 0.19, 0.72, { fill: '#ffffff', stroke: 'band2', strokeWidth: 3 }),
          logo(0.5, 0.35, 0.11),
          words('coverTitle', 0.5, 0.6, 0.78, 38, { font: 'elegant', color: 'band3', bold: true }, { lines: 3 }),
          rule('coverrule', 0.5, 0.74, 0.5, 'band2', 3)
        ])
      ],
      inside: [
        frame('left', 'innerborder'),
        frame('right', 'innerborder2'),
        ...onAll('left', [
          words('host', 0.5, 0.26, 0.78, 24, { font: 'engraved', color: 'band1', bold: true, spacing: 8 }, { lines: 3 }),
          words('invite', 0.5, 0.39, 0.76, 32, { font: 'formal', color: INK, bold: false }, { lines: 2 }),
          rule('guestline', 0.5, 0.525, 0.66, INK, 1.5),
          words('guest', 0.5, 0.505, 0.66, 30, { font: 'script', color: INK, bold: false }, { lines: 1 }),
          words('at', 0.5, 0.6, 0.4, 22, { font: 'elegant', color: INK, bold: false, italic: true }),
          words('occasion', 0.5, 0.705, 0.8, 40, { font: 'elegant', color: 'band3', bold: true }, { lines: 3 }),
          rule('leftrule', 0.5, 0.83, 0.5, 'band2', 3)
        ]),
        ...onAll('right', [
          words('when', 0.5, 0.32, 0.78, 25, { font: 'clean', color: INK, bold: true }, { lines: 2 }),
          words('venue', 0.5, 0.425, 0.78, 24, { font: 'clean', color: INK, bold: false }, { lines: 2 }),
          words('dress', 0.5, 0.52, 0.78, 23, { font: 'clean', color: 'band1', bold: true }, { lines: 1 }),
          rule('innerrule', 0.5, 0.61, 0.7, 'band2', 3),
          words('rsvp', 0.5, 0.7, 0.78, 20, { font: 'clean', color: INK, bold: false }, { lines: 2 })
        ])
      ]
    };
  }

  /** Ceremonial photo: the user's photo at the top of the cover, the logo in its white box, set on the left. */
  function ceremonialFold() {
    const frame = (panel, role) => panelFrame(panel, role, 0.05, MAROON, 1);
    const left = (field, x, y, w, size, look, lines, extra = {}) => words(field, x, y, w, size, { ...look, align: 'left' }, { anchor: 'left', lines, ...extra });
    // Every left-set line starts at 0.14 of the panel, the logo's box and the accent's line too
    // (M5): one left edge.
    return {
      outside: [
        ...backCover([frame('back', 'backcover')], { font: 'elegant', color: INK, bold: false, italic: true }, MAROON),
        frame('cover', 'coverborder'),
        ...onAll('cover', [
          photo(0.5, 0.285, 0.84, 0.44),
          // With "No photo" (UX review 8) a maroon band takes the photo's place and the logo's
          // box sits on its edge, as it sat on the photo's; the occasion moves up under it.
          { ...block('photoband', 0.5, 0.225, 0.84, 0.32, { fill: MAROON, stroke: '' }), onlyNoPhoto: true },
          { ...block('logobox', 0.25, 0.505, 0.22, 0.155, { fill: '#ffffff', stroke: '#d9d4c7', strokeWidth: 1.5, radius: 10 }), noPhoto: { y: 0.385 } },
          { ...logo(0.25, 0.505, 0.1), noPhoto: { y: 0.385 } },
          { ...vline('coveraccent', 0.14, 0.715, 0.12, MAROON, 3), noPhoto: { y: 0.6 } },
          left('coverTitle', 0.18, 0.715, 0.72, 34, { font: 'elegant', color: MAROON, bold: true }, 3, { noPhoto: { y: 0.6 } })
        ])
      ],
      inside: [
        frame('left', 'innerborder'),
        frame('right', 'innerborder2'),
        ...onAll('left', [
          left('host', 0.14, 0.26, 0.76, 24, { font: 'clean', color: INK, bold: true }, 3),
          left('invite', 0.14, 0.38, 0.76, 24, { font: 'elegant', color: INK, bold: false, italic: true }, 2),
          rule('guestline', 0.52, 0.515, 0.76, INK, 1.5),
          left('guest', 0.14, 0.495, 0.76, 30, { font: 'script', color: INK, bold: false }, 1),
          left('at', 0.14, 0.59, 0.5, 21, { font: 'elegant', color: INK, bold: false, italic: true }, 1),
          vline('leftaccent', 0.14, 0.715, 0.13, MAROON, 3),
          left('occasion', 0.18, 0.715, 0.72, 36, { font: 'elegant', color: MAROON, bold: true }, 3)
        ]),
        ...onAll('right', [
          vline('accent', 0.14, 0.43, 0.22, MAROON, 3),
          left('when', 0.18, 0.345, 0.72, 25, { font: 'clean', color: INK, bold: true }, 2),
          left('venue', 0.18, 0.43, 0.72, 24, { font: 'clean', color: INK, bold: false }, 2),
          left('dress', 0.18, 0.515, 0.72, 23, { font: 'clean', color: MAROON, bold: true }, 1),
          left('rsvp', 0.14, 0.69, 0.76, 20, { font: 'clean', color: INK, bold: false }, 2)
        ])
      ]
    };
  }

  /** Bilingual: Hindi above English on every panel, the occasion in both on the cover. */
  function bilingualFold() {
    const frame = (panel, role) => panelFrame(panel, role, 0.05, GOLD, 1.5);
    return {
      outside: [
        ...backCover([frame('back', 'backcover')], { font: 'elegant', color: INK, bold: false, italic: true }, GOLD),
        frame('cover', 'coverborder'),
        ...onAll('cover', [
          logo(0.5, 0.24, 0.1),
          words('occasionHi', 0.5, 0.46, 0.8, 32, { font: 'hindiFormal', color: MAROON, bold: false }, { lines: 2 }),
          rule('coverrule', 0.5, 0.555, 0.14, GOLD, 1.2),
          words('coverTitle', 0.5, 0.64, 0.8, 30, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 })
        ])
      ],
      // The Hindi occasion is on the cover; the English one ends the invitation inside.
      inside: [
        frame('left', 'innerborder'),
        frame('right', 'innerborder2'),
        ...onAll('left', [
          words('hostHi', 0.5, 0.18, 0.8, 21, { font: 'hindiFormal', color: INK, bold: true }, { lines: 3 }),
          words('host', 0.5, 0.285, 0.8, 19, { font: 'elegant', color: INK, bold: true }, { lines: 3 }),
          rule('innerrule', 0.5, 0.355, 0.14, GOLD, 1.2),
          words('inviteHi', 0.5, 0.41, 0.8, 21, { font: 'hindiFormal', color: INK, bold: false }, { lines: 2 }),
          words('invite', 0.5, 0.48, 0.8, 20, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 2 }),
          rule('guestline', 0.5, 0.605, 0.66, INK, 1.5),
          words('guest', 0.5, 0.585, 0.66, 28, { font: 'script', color: INK, bold: false }, { lines: 1 }),
          words('occasion', 0.5, 0.74, 0.8, 34, { font: 'elegant', color: MAROON, bold: true }, { lines: 3 })
        ]),
        ...onAll('right', [
          words('when', 0.5, 0.32, 0.78, 25, { font: 'clean', color: INK, bold: true }, { lines: 2 }),
          words('venue', 0.5, 0.425, 0.78, 24, { font: 'clean', color: INK, bold: false }, { lines: 2 }),
          words('dress', 0.5, 0.52, 0.78, 23, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
          rule('innerrule2', 0.5, 0.61, 0.16, GOLD, 1.2),
          words('rsvp', 0.5, 0.7, 0.78, 20, { font: 'clean', color: INK, bold: false }, { lines: 2 })
        ])
      ]
    };
  }

  /** Modern: the navy bar down the spine of the cover and the inside's outer edge, set on the left. */
  function modernFold() {
    const frame = (panel, role) => panelFrame(panel, role, 0.07, '#d5dae3', 1);
    const left = (field, x, y, w, size, look, lines) => words(field, x, y, w, size, { ...look, align: 'left' }, { anchor: 'left', lines });
    return {
      outside: [
        ...backCover([frame('back', 'backcover')], { font: 'clean', color: GREY, bold: false, italic: true }, NAVY),
        onPanel('cover', block('bar', 0.014, 0.5, 0.028, 1, { fill: NAVY, stroke: '' })),
        frame('cover', 'coverborder'),
        ...onAll('cover', [
          logo(0.8, 0.18, 0.08),
          rule('coverrule', 0.25, 0.58, 0.22, NAVY, 4),
          left('coverTitle', 0.14, 0.7, 0.74, 40, { font: 'clean', color: NAVY, bold: true }, 3)
        ])
      ],
      inside: [
        onPanel('left', block('bar', 0.014, 0.5, 0.028, 1, { fill: NAVY, stroke: '' })),
        frame('left', 'innerborder'),
        frame('right', 'innerborder2'),
        ...onAll('left', [
          left('host', 0.16, 0.26, 0.72, 22, { font: 'clean', color: GREY, bold: true, spacing: 4 }, 3),
          left('invite', 0.16, 0.38, 0.72, 23, { font: 'clean', color: GREY, bold: false }, 2),
          rule('guestline', 0.52, 0.515, 0.72, INK, 1.5),
          left('guest', 0.16, 0.495, 0.72, 28, { font: 'clean', color: INK, bold: true }, 1),
          left('at', 0.16, 0.59, 0.5, 21, { font: 'clean', color: GREY, bold: false, italic: true }, 1),
          left('occasion', 0.16, 0.705, 0.72, 40, { font: 'clean', color: NAVY, bold: true }, 3),
          rule('leftrule', 0.27, 0.82, 0.22, NAVY, 4)
        ]),
        ...onAll('right', [
          left('when', 0.14, 0.32, 0.76, 25, { font: 'clean', color: INK, bold: true }, 2),
          left('venue', 0.14, 0.425, 0.76, 24, { font: 'clean', color: INK, bold: false }, 2),
          left('dress', 0.14, 0.52, 0.76, 23, { font: 'clean', color: NAVY, bold: true }, 1),
          rule('innerrule', 0.25, 0.61, 0.22, NAVY, 4),
          left('rsvp', 0.14, 0.7, 0.76, 20, { font: 'clean', color: GREY, bold: false }, 2)
        ])
      ]
    };
  }

  /** Each card design's folded sides, made once. A design with none of its own folds as Classic ivory. */
  const FOLDED = lookupTable({
    classic: formalFold({ line: GOLD, outer: 3, inner: 1.2, host: { font: 'engraved', color: INK, bold: true, spacing: 6 }, invite: GOLD, ink: INK, title: MAROON, accent: GOLD }),
    navy: formalFold({ line: PALE_GOLD, outer: 1.5, inner: 0.8, host: { font: 'engraved', color: PALE_GOLD, bold: false, spacing: 10 }, invite: IVORY, ink: IVORY, title: PALE_GOLD, accent: PALE_GOLD, tile: NAVY_TILE }),
    regimental: regimentalFold(),
    ceremonial: ceremonialFold(),
    bilingual: bilingualFold(),
    modern: modernFold()
  });

  /** A folded card's parts for one side ('outside' or 'inside') in a design. */
  function foldedParts(designKey, side) {
    const fold = FOLDED[designKey] || FOLDED.classic;
    return side === 'outside' ? fold.outside : fold.inside;
  }

  /**
   * The width a logo of this shape (`aspect`, width over height) gets in a place made for one
   * `placeWidth` wide and `placeAspect` in shape: the same area, so a wide unit logo is not
   * squeezed to a sliver where a tall crest fits, and at most 2.4 times as wide as the place.
   */
  function logoWidth(placeWidth, placeAspect, aspect) {
    if (!(aspect > 0) || !(placeAspect > 0)) return placeWidth;
    return Math.min(placeWidth * 2.4, Math.sqrt(placeWidth * (placeWidth / placeAspect) * aspect));
  }

  /**
   * A poster's logo width: as logoWidth, but never taller than its place, which sits near the
   * poster's top edge.
   */
  function posterLogoWidth(placeWidth, placeAspect, aspect) {
    const width = logoWidth(placeWidth, placeAspect, aspect);
    return aspect > 0 && placeAspect > 0 ? Math.min(width, placeWidth / placeAspect * aspect) : width;
  }

  /**
   * The roundel behind the logo (Regimental) follows the logos, in `items` (changed in place):
   * hidden with no logo; around one logo, at least the design's size (`base`) and wide enough for
   * a wide logo; around two side by side. A locked roundel stays as it is.
   */
  function fitRoundel(items, base, { hadLogo = true } = {}) {
    const roundel = items.find((item) => item.role === 'roundel' && !item.locked);
    if (!roundel || !base) return;
    const logos = items.filter((item) => (item.role === 'logo' && item.kind !== 'slot') || item.role === 'logo2');
    // Hidden for want of a logo is marked so (autoHidden), and only that comes back with a logo:
    // a roundel the user hid stays hidden (Codex on #3453). One hidden while the card had no
    // logo (as an earlier PRISM hid it) counts as hidden for want of a logo.
    if (!logos.some((item) => item.role === 'logo')) {
      if (!roundel.hidden) { roundel.hidden = true; roundel.autoHidden = true; }
      return;
    }
    if (roundel.autoHidden || !hadLogo) { delete roundel.hidden; delete roundel.autoHidden; }
    const left = Math.min(...logos.map((item) => item.x - item.width / 2));
    const right = Math.max(...logos.map((item) => item.x + item.width / 2));
    const height = base.width / base.aspect;
    const width = Math.max(base.width, right - left + base.width * 0.35);
    roundel.x = logos.length > 1 ? (left + right) / 2 : logos[0].x;
    roundel.width = width;
    roundel.aspect = width / height;
    // Two logos sit in a rounded band, not a stretched oval whose narrowing ends touched them
    // (UX review 7); one logo keeps its oval.
    if (logos.length > 1) {
      roundel.form = 'rect';
      roundel.radius = height / 2;
    } else {
      roundel.form = 'ellipse';
      delete roundel.radius;
    }
  }

  /** The smallest a design's words print, in points: smaller is hard to read on a card. */
  const MIN_PRINT_PT = 6;

  /** MIN_PRINT_PT in page units on a page of this size (0 for a page with no paper size). */
  function minTextUnits(sizeKey) {
    const size = core.sizeOf(sizeKey);
    // A custom card smaller than A6 is warned of instead: its words could not all keep the floor.
    if (!size?.mm || (size.group === 'custom' && core.smallCard(sizeKey))) return 0;
    return MIN_PRINT_PT * 25.4 / 72 * core.PAGE_WIDTH / size.mm[0];
  }

  /** The measures one side of a card of this size is built with. */
  function pageContext(collage, wording, { measure, newId }, extra) {
    return { width: core.PAGE_WIDTH, height: core.pageHeight(collage.size), unit: unitOf(collage.size), minSize: minTextUnits(collage.size), wording, measure, newId, ...extra };
  }

  /**
   * The card's back for the design (PR 8): the programme and the rest, made from the details,
   * with the user's own items from the back as it was.
   */
  function backPage(collage, designKey, details, opts, items = []) {
    const look = BACK_LOOKS[designKey] || BACK_LOOKS.classic;
    const wording = { ...DEFAULT_DETAILS, ...(details || {}) };
    const parts = partsFor(designKey, collage.size).back;
    // A part locked on the front is not made again on the back (Codex on #3452).
    const lockedOnFront = new Set((collage.items || []).filter((item) => item.role && item.locked).map((item) => item.role));
    const made = makePage(items, parts, pageContext(collage, wording, opts, { isCard: false, frameRole: 'backphoto', lockedElsewhere: lockedOnFront }));
    // The back's small logo is the front's logo (the SDD logo, a crest or the user's own), or
    // none when the front has none. `collage` here shows its front.
    const front = (collage.items || []).find((item) => item.role === 'logo');
    const at = made.findIndex((item) => item.role === 'backlogo');
    if (at >= 0) {
      if (front) made.splice(at, 1, backLogoFrom(front, made[at]));
      else if (!made[at].locked) made.splice(at, 1);
    }
    return { background: { ...look.background }, items: fitLogoTile(made, { width: core.PAGE_WIDTH, height: core.pageHeight(collage.size) }) };
  }

  /**
   * The collage with `designKey` applied: its background, and a new set of design parts, made
   * from the details, in place of the parts of any design applied before, on the front and, when
   * the card has one, on the back. Items the user added (photos, stickers, their own words:
   * anything without a `role`) stay where they are, in front. A field left empty has no item.
   * `measure(text, font)` sizes the words as the page does; `newId()` gives each new item its
   * id. The page size never changes, and the side showing stays the side showing.
   */
  function applyDesign(collage, designKey, details, opts) {
    const design = DESIGNS[designKey];
    if (!design) return collage;
    const card = frontOf(collage);
    const wording = { ...DEFAULT_DETAILS, ...(details || {}) };
    // A folded card has its own outside and inside (PR 9c); any other the design's arrangement for
    // the page's shape (a poster has only one, used for either).
    const parts = isFolded(card.size) ? foldedParts(designKey, 'outside') : isPortrait(card.size) ? design.portrait : (design.landscape || design.portrait);
    const applied = {
      ...card,
      background: { ...design.background },
      layout: null,
      items: makePage(card.items, parts, pageContext(card, wording, opts, { isCard: Boolean(card.card), frameRole: 'photo', lockedElsewhere: new Set((card.back?.items || []).filter((item) => item.role && item.locked).map((item) => item.role)) })),
      // The guest list, and fields a newer PRISM keeps on the card, stay through a design change.
      card: { ...(card.card || {}), design: designKey, details: { ...(card.card?.details || {}), ...Object.fromEntries(FIELDS.map((field) => [field, wording[field]])) } }
    };
    // A folded card always has both sides.
    if (card.back || isFolded(card.size)) {
      applied.back = backPage(card, designKey, wording, opts, card.back?.items || []);
      // Parts a newer PRISM keeps on the back go with it, as the front's do.
      if (card.back?.kept) applied.back.kept = card.back.kept;
    }
    // The fields this design put on the card, both sides, so a missing one is known to be deleted.
    applied.card.placed = placedOn(applied);
    return asShown(collage, applied);
  }

  /**
   * The parts a poster's logo choice moves, moved and nothing else changed (Codex on PR 11): the
   * design laid out as `previous` was and as `collage` is now; each design part whose place moved
   * between the two (the heading moving up into a logo's space) moves by as much, from wherever
   * the user put it. Fonts, colours, the background, deleted parts and locked parts stay as they
   * are. The logo itself is left to the caller.
   */
  function moveForLogo(previous, collage, opts) {
    return moveParts(previous, collage, opts, (role) => /^logo2?$/.test(role));
  }

  /**
   * The design's parts moved from where the design put them for `previous` to where it puts them
   * for `collage`, by as much, from wherever the user put each one; `leftAlone(role)` names the
   * roles the caller looks after itself. A part the user has not resized (its width, and a text's
   * size and width limit, still the design's) also takes its new size: "No photo" widens the words
   * beside a photo's place. Fonts, colours, words, the background, deleted and locked parts stay.
   */
  function moveParts(previous, collage, opts, leftAlone) {
    const design = collage.card?.design;
    if (!DESIGNS[design]) return collage;
    const before = applyDesign(previous, design, detailsOf(previous), opts);
    const after = applyDesign(collage, design, detailsOf(collage), opts);
    const keyOf = (item) => (item.field ? `field:${item.field}` : item.role && !leftAlone(item.role) ? `role:${item.role}` : null);
    const placeOf = (items) => new Map(items.filter(keyOf).map((item) => [keyOf(item), item]));
    const was = placeOf(before.items);
    const now = placeOf(after.items);
    const same = (x, y) => Math.abs((x ?? 0) - (y ?? 0)) < 1e-6;
    // A shape the design has only one way (a band that stands in for the photo) comes or goes
    // with it: one the user locked stays.
    const onlyOneWay = (key, item) => key.startsWith('role:') && item.kind === 'shape';
    let kept = collage.items.filter((item) => {
      const key = keyOf(item);
      return !(key && !item.locked && onlyOneWay(key, item) && was.has(key) && !now.has(key));
    });
    for (const [key, item] of now) {
      if (was.has(key) || !onlyOneWay(key, item) || kept.some((each) => keyOf(each) === key)) continue;
      // In the design's stacking order: before the part that follows it there.
      const following = after.items.slice(after.items.indexOf(item) + 1).map(keyOf).filter(Boolean);
      let index = kept.findIndex((each) => following.includes(keyOf(each)));
      if (index < 0) index = kept.findIndex((each) => !each.role);
      const added = { ...item, id: opts.newId() };
      kept = index < 0 ? [...kept, added] : [...kept.slice(0, index), added, ...kept.slice(index)];
    }
    return {
      ...collage,
      items: kept.map((item) => {
        const key = keyOf(item);
        const a = key && was.get(key);
        const b = key && now.get(key);
        if (!a || !b || item.locked) return item;
        const dx = b.x - a.x;
        const dy = b.y - a.y;
        const resized = !same(item.width, a.width) || !same(item.aspect, a.aspect) || (item.kind === 'text' && (!same(item.size, a.size) || !same(item.maxWidth, a.maxWidth)));
        const grows = !resized && (!same(a.width, b.width) || !same(a.aspect, b.aspect) || (item.kind === 'text' && (!same(a.size, b.size) || !same(a.maxWidth, b.maxWidth) || a.maxLines !== b.maxLines)));
        if (same(dx, 0) && same(dy, 0) && !grows) return item;
        const moved = { ...item, x: item.x + dx, y: item.y + dy };
        if (grows) {
          moved.width = b.width;
          moved.aspect = b.aspect;
          if (item.kind === 'text') Object.assign(moved, { size: b.size, maxWidth: b.maxWidth, maxLines: b.maxLines });
        }
        return moved;
      })
    };
  }

  /**
   * A poster's logo choice (PR 11): 'later' keeps a place for the logo, 'none' has no logo and the
   * heading moves up into its space. Only the logo and the parts it moves change.
   */
  function setLogoMode(collage, mode, opts) {
    const details = { ...detailsOf(collage), logoMode: mode };
    let items = collage.items;
    if (mode === 'none') items = items.filter((item) => item.role !== 'logo' || item.locked);
    else if (!items.some((item) => item.role === 'logo')) {
      const place = roleItem({ ...collage, card: { ...collage.card, details } }, 'logo', opts);
      if (place) items = [...items, place];
    }
    const next = { ...collage, items, card: { ...collage.card, details: { ...collage.card.details, logoMode: mode } } };
    const moved = moveForLogo(collage, next, opts);
    // The light tile behind a crest goes with the logo (M3).
    return { ...moved, items: fitLogoTile(moved.items, { width: core.PAGE_WIDTH, height: core.pageHeight(collage.size) }) };
  }

  /**
   * The poster as Save prints it (cold review 9, M3): with no logo chosen yet ("Decide later", or
   * the question not answered) its empty logo place would print as a blank strip, so the poster
   * is laid out as with "No logo", the heading moving up into the space. The poster itself is not
   * changed: a logo can still be chosen. A place the user locked stays. Anything else: the same
   * collage.
   */
  function closeLogoPlace(collage, opts) {
    const front = frontOf(collage);
    if (!DESIGNS[front?.card?.design]?.poster) return collage;
    const place = front.items.find((item) => item.role === 'logo');
    if (!place || place.kind !== 'slot' || place.locked || place.hidden) return collage;
    return asShown(collage, setLogoMode(front, 'none', opts));
  }

  /** Whether the card's design has a photo frame on its front (Ceremonial photo, a photo poster). */
  function hasPhotoFrame(collage) {
    const front = frontOf(collage);
    if (!front?.card || !DESIGNS[front.card.design]) return false;
    return partsFor(front.card.design, front.size).front.some((part) => part.kind === 'slot');
  }

  /**
   * The design's photo frame (UX review 8): 'none' is "No photo", the empty frame goes and the
   * parts around it move into its space; '' brings an empty frame back in the design's place and
   * the parts back round it. Only the frame and the parts it moves change: the user's fonts,
   * colours, places, deleted and locked parts stay, and a photo in the frame stays in it. Works
   * on the front, whichever side shows.
   */
  function setPhotoMode(collage, mode, opts) {
    const front = frontOf(collage);
    if (!front?.card) return collage;
    const details = { ...(front.card.details || {}), photoMode: mode };
    let items = front.items;
    if (mode === 'none') items = items.filter((item) => !(item.role === 'photo' && item.kind === 'slot' && !item.locked));
    else if (!items.some((item) => item.role === 'photo')) {
      // The design laid out with its frame, for the frame's place and its place in the stacking
      // order: before the part that comes after it in the design (the Ceremonial logo's box).
      const { back, side, ...rest } = front;
      const made = applyDesign({ ...rest, items: [] }, front.card.design, { ...detailsOf(front), photoMode: '' }, opts).items;
      const at = made.findIndex((item) => item.role === 'photo');
      if (at >= 0) {
        const place = { ...made[at], id: opts.newId() };
        const nextRoles = made.slice(at + 1).map((item) => item.role).filter(Boolean);
        let index = items.findIndex((item) => item.role && nextRoles.includes(item.role));
        if (index < 0) index = items.findIndex((item) => !item.role);
        items = index < 0 ? [...items, place] : [...items.slice(0, index), place, ...items.slice(index)];
      }
    }
    const next = { ...front, items, card: { ...front.card, details } };
    return asShown(collage, moveParts(front, next, opts, (role) => role === 'photo'));
  }

  /** An item's box on the page (not turned): left, right, top and bottom. */
  function itemBox(item) {
    // A turned item (a divider stood upright) takes the room it really covers (code review 9).
    const half = core.turnedHalfBox(item.width, core.itemHeight(item), item.angle || 0);
    return { left: item.x - half.halfWidth, right: item.x + half.halfWidth, top: item.y - half.halfHeight, bottom: item.y + half.halfHeight };
  }

  /**
   * The Hindi wording kept in step with the English (cold review 6): an occasion quick pick has
   * its Hindi (`picks`, English to Hindi); a Hindi field still holding the sample's or a quick
   * pick's Hindi while its English is something else is emptied, so the card never prints a
   * Hindi line that says something different. Words the user typed in Hindi stay theirs.
   */
  function hindiInStep(details, picks = {}) {
    const out = { ...details };
    const known = new Set([DEFAULT_DETAILS.occasionHi, ...Object.values(picks)]);
    // Only a sample's or a quick pick's Hindi (or none) follows the English; Hindi the user typed
    // stays, whatever else is edited (Codex on #3453). Picking a quick pick sets its Hindi itself.
    const stock = !String(out.occasionHi || '').trim() || known.has(out.occasionHi);
    if (stock && picks[out.occasion]) out.occasionHi = picks[out.occasion];
    else if (stock && known.has(out.occasionHi) && out.occasion !== DEFAULT_DETAILS.occasion) out.occasionHi = '';
    for (const [field, hindi] of [['host', 'hostHi'], ['invite', 'inviteHi']]) {
      if (out[hindi] === DEFAULT_DETAILS[hindi] && out[field] !== DEFAULT_DETAILS[field]) out[hindi] = '';
    }
    return out;
  }

  /** The fields that have an item on either side of a card. */
  function placedOn(collage) {
    return [...new Set([...(collage.items || []), ...(collage.back?.items || [])].filter((item) => item.field).map((item) => item.field))];
  }

  /** The card with a back added in its design (PR 8); the front is untouched. */
  function addBack(collage, opts) {
    const key = collage?.card?.design;
    if (!DESIGNS[key] || collage.back) return collage;
    const added = { ...collage, back: backPage(collage, key, detailsOf(collage), opts) };
    added.card = { ...collage.card, placed: [...new Set([...(collage.card.placed || []), ...placedOn({ items: added.back.items })])] };
    return added;
  }

  /**
   * The back's small logo follows the front's logo (PR 8): the same picture in the back logo's
   * place, or none when the front has none. Without `add`, a back whose small logo was deleted
   * keeps none (the user took it off); with it (a logo chosen in Card details), it comes back in
   * the design's place. Returns the collage itself when nothing changes, so it can run after
   * every change. Works whichever side shows.
   */
  function followLogo(collage, opts, { add = false } = {}) {
    if (!collage?.back || !collage.card) return collage;
    const front = frontOf(collage);
    const logo = front.items.find((item) => item.role === 'logo' && item.kind !== 'slot');
    const items = front.back.items;
    const at = items.findIndex((item) => item.role === 'backlogo');
    let next;
    if (!logo) {
      // A back logo the user locked stays, even when the front's logo goes (Codex on #3453).
      if (at < 0 || items[at].locked) return collage;
      next = items.filter((_, i) => i !== at);
    } else {
      if (at < 0 && !add) return collage;
      const place = at >= 0 ? items[at] : backPage(front, front.card.design, detailsOf(front), opts).items.find((item) => item.role === 'backlogo');
      if (!place) return collage;
      const old = at >= 0 ? items[at] : null;
      if (old && old.kind === logo.kind && old.sticker === logo.sticker && old.photo === logo.photo && old.aspect === logo.aspect) return collage;
      if (old?.locked) return collage;
      const small = backLogoFrom(logo, old || { ...place, id: opts.newId(), hidden: false, locked: false });
      next = old ? items.map((item, i) => (i === at ? small : item)) : [...items, small];
    }
    const out = { ...front, back: { ...front.back, items: fitLogoTile(next, { width: core.PAGE_WIDTH, height: core.pageHeight(front.size) }) } };
    return asShown(collage, out);
  }

  /**
   * The back's small logo made from the front's logo, in `place` (the back logo as it is, or the
   * design's place for one): the front's picture at the place's position and size. A locked back
   * logo stays exactly as it is; a hidden one stays hidden; the front logo's own lock and hiding
   * are its own, not the back's.
   */
  function backLogoFrom(front, place) {
    if (place.locked) return place;
    const { locked, hidden, ...picture } = front;
    const small = { ...picture, id: place.id, x: place.x, y: place.y, width: place.width, angle: 0, role: 'backlogo' };
    if (place.hidden) small.hidden = true;
    return small;
  }

  /** The card without its back, showing its front. */
  function deleteBack(collage) {
    if (!collage?.back) return collage;
    const front = frontOf(collage);
    const { back, side, ...rest } = front;
    // The fields only the back had are no longer on the card: their words, as they are now on the
    // card (typed in Card details or in an item's Words box), are kept for a new back.
    const backOnly = new Set(fieldsIn(partsFor(rest.card?.design, rest.size).back));
    if (rest.card) rest.card = { ...rest.card, details: detailsOf(front), placed: (rest.card.placed || []).filter((field) => !backOnly.has(field)) };
    return rest;
  }

  /**
   * Whether a field is on the card's back: a back field, unless the design shows it on the
   * front (a programme card has its programme and title on the front).
   */
  function isBackField(collage, field) {
    // The design says which side a field is on (partsFor), a folded card's inside included.
    return fieldsIn(partsFor(collage?.card?.design, collage?.size).back).includes(field);
  }

  /**
   * The item one field would have in the card's design, made from the details: for a field the
   * user emptied and fills again, so it comes back in its design's place. Null when the design
   * has no place for it or its words are empty.
   */
  function fieldItem(collage, field, details, opts) {
    const key = collage?.card?.design;
    if (!DESIGNS[key]) return null;
    const where = sideOf(collage, field);
    if (!where) return null;
    if (where === 'back') {
      // Built from the front's point of view, so the back's logo follows the front's.
      const front = frontOf(collage);
      return backPage(front, key, details, opts).items.find((item) => item.field === field) || null;
    }
    const { back, side, ...front } = collage;
    // Placed as the page is laid out: with its photo arrangement while its frame is there.
    const made = applyDesign({ ...front, items: [] }, key, photoDetails(collage.items, details), opts);
    return made.items.find((item) => item.field === field) || null;
  }

  return Object.freeze({ DESIGNS, DEFAULT_DETAILS, FIELDS, SETTINGS, SDD_ASPECT, unitOf, isPortrait, detailsOf, applyDesign, fieldItem, roleItem, fieldsOf, usesColours, colourList, backPage, addBack, deleteBack, designsFor, POSTER_DETAILS, isBackField, moveForLogo, setLogoMode, setPhotoMode, hasPhotoFrame, isFolded, partsFor, sideOf, fieldsOnCard, startDetails, logoWidth, posterLogoWidth, fitRoundel, fitLogoTile, logoTileItems, secondLogoPlace, fitLogoBox, followLogo, roleItemOn, designOnSize, hindiInStep, fieldAnchor, FALLBACKS, closeLogoPlace });
});
