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

  /** The wording a new card starts with: realistic, and every word can be changed. */
  const DEFAULT_DETAILS = Object.freeze({
    host: 'The Commandant and Officers\nSimulator Development Division',
    invite: 'request the pleasure of the company of',
    at: 'at the',
    occasion: 'Raising Day Dinner',
    when: 'Friday, 12 December 2026 at 1930 h',
    venue: "Officers' Mess, SDD Campus",
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
    replyBy: 'Kindly reply by Friday, 5 December 2026'
  });
  /** The fields on the back of the card. */
  const BACK_FIELDS = Object.freeze(['programmeTitle', 'programme', 'backNote']);
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
  // Colours the Regimental band sets: 'band1' to 'band3' in a look stand for the unit's colours.
  const NAVY = '#1d2c55';
  const GREY = '#55607a';

  const DESIGNS = Object.freeze({
    classic: Object.freeze({
      key: 'classic',
      label: 'Classic ivory',
      note: 'Gold double border',
      background: { type: 'color', value: '#fbf7ee' },
      portrait: [
        border('border', 0.035, GOLD, 3),
        border('border2', 0.05, GOLD, 1.2),
        logo(0.5, 0.135, 0.11),
        words('host', 0.5, 0.29, 0.76, 28, { font: 'elegant', color: INK, bold: true, spacing: 6 }, { lines: 3 }),
        words('invite', 0.5, 0.385, 0.8, 34, { font: 'script', color: GOLD, bold: false }),
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
        words('host', 0.6, 0.17, 0.58, 26, { font: 'elegant', color: INK, bold: true, spacing: 6 }, { lines: 2 }),
        words('invite', 0.6, 0.29, 0.6, 32, { font: 'script', color: GOLD, bold: false }),
        rule('guestline', 0.6, 0.38, 0.46, INK, 1.5),
        words('guest', 0.6, 0.36, 0.46, 32, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('occasion', 0.6, 0.5, 0.6, 50, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
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
        logo(0.5, 0.14, 0.115),
        words('host', 0.5, 0.3, 0.76, 26, { font: 'elegant', color: PALE_GOLD, bold: false, spacing: 10 }, { lines: 3 }),
        words('invite', 0.5, 0.395, 0.8, 34, { font: 'script', color: IVORY, bold: false }),
        rule('guestline', 0.5, 0.47, 0.6, IVORY, 1.2),
        words('guest', 0.5, 0.455, 0.6, 34, { font: 'script', color: IVORY, bold: false }, { lines: 1 }),
        words('occasion', 0.5, 0.575, 0.82, 60, { font: 'elegant', color: PALE_GOLD, bold: true }, { lines: 2 }),
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
        logo(0.16, 0.5, 0.14),
        words('host', 0.6, 0.17, 0.58, 24, { font: 'elegant', color: PALE_GOLD, bold: false, spacing: 10 }, { lines: 2 }),
        words('invite', 0.6, 0.29, 0.6, 32, { font: 'script', color: IVORY, bold: false }),
        rule('guestline', 0.6, 0.38, 0.46, IVORY, 1.2),
        words('guest', 0.6, 0.36, 0.46, 32, { font: 'script', color: IVORY, bold: false }, { lines: 1 }),
        words('occasion', 0.6, 0.5, 0.6, 54, { font: 'elegant', color: PALE_GOLD, bold: true }, { lines: 2 }),
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
        words('host', 0.5, 0.29, 0.8, 26, { font: 'clean', color: 'band1', bold: true, spacing: 8 }, { lines: 3 }),
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
        words('host', 0.61, 0.25, 0.6, 24, { font: 'clean', color: 'band1', bold: true, spacing: 8 }, { lines: 2 }),
        words('invite', 0.61, 0.36, 0.6, 22, { font: 'elegant', color: INK, bold: false, italic: true }),
        rule('guestline', 0.61, 0.44, 0.46, INK, 1.5),
        words('guest', 0.61, 0.42, 0.46, 30, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('occasion', 0.61, 0.54, 0.62, 48, { font: 'elegant', color: 'band3', bold: true }, { lines: 2 }),
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
      portrait: [
        photo(0.5, 0.2, 1, 0.4),
        block('logobox', 0.15, 0.4, 0.17, 0.135, { fill: '#ffffff', stroke: '#d9d4c7', strokeWidth: 1.5, radius: 10 }),
        logo(0.15, 0.4, 0.1),
        words('occasion', 0.27, 0.465, 0.66, 46, { font: 'elegant', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('host', 0.08, 0.565, 0.84, 22, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('invite', 0.08, 0.63, 0.84, 22, { font: 'elegant', color: INK, bold: false, italic: true, align: 'left' }, { anchor: 'left' }),
        rule('guestline', 0.4, 0.695, 0.64, INK, 1.5),
        words('guest', 0.08, 0.68, 0.64, 30, { font: 'script', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        vline('accent', 0.075, 0.8, 0.11, MAROON, 3),
        words('when', 0.1, 0.76, 0.8, 23, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('venue', 0.1, 0.8, 0.8, 22, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('dress', 0.1, 0.84, 0.8, 21, { font: 'clean', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('rsvp', 0.08, 0.92, 0.44, 18, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.92, 0.92, 0.38, 18, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ],
      landscape: [
        photo(0.25, 0.5, 0.5, 1),
        block('logobox', 0.58, 0.16, 0.12, 0.2, { fill: '#ffffff', stroke: '#d9d4c7', strokeWidth: 1.5, radius: 10 }),
        logo(0.58, 0.16, 0.1),
        words('occasion', 0.66, 0.16, 0.3, 36, { font: 'elegant', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('host', 0.54, 0.36, 0.42, 20, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('invite', 0.54, 0.46, 0.42, 20, { font: 'elegant', color: INK, bold: false, italic: true, align: 'left' }, { anchor: 'left' }),
        rule('guestline', 0.75, 0.55, 0.42, INK, 1.5),
        words('guest', 0.54, 0.53, 0.42, 26, { font: 'script', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        vline('accent', 0.535, 0.7, 0.18, MAROON, 3),
        words('when', 0.555, 0.64, 0.4, 20, { font: 'clean', color: INK, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('venue', 0.555, 0.7, 0.4, 19, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('dress', 0.555, 0.76, 0.4, 18, { font: 'clean', color: MAROON, bold: true, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('rsvp', 0.54, 0.9, 0.42, 16, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('note', 0.54, 0.945, 0.42, 16, { font: 'elegant', color: INK, bold: false, italic: true, align: 'left' }, { anchor: 'left', lines: 1 })
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
        vline('divider', 0.5, 0.42, 0.42, GOLD, 1.2),
        words('hostHi', 0.26, 0.27, 0.42, 24, { font: 'hindiFormal', color: INK, bold: false }, { lines: 3 }),
        words('inviteHi', 0.26, 0.37, 0.42, 22, { font: 'hindiFormal', color: INK, bold: false }, { lines: 2 }),
        words('occasionHi', 0.26, 0.48, 0.42, 38, { font: 'hindiFormal', color: MAROON, bold: false }, { lines: 2 }),
        words('host', 0.74, 0.27, 0.42, 20, { font: 'elegant', color: INK, bold: true }, { lines: 3 }),
        words('invite', 0.74, 0.37, 0.42, 20, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 2 }),
        words('occasion', 0.74, 0.48, 0.42, 34, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        rule('guestline', 0.5, 0.7, 0.62, INK, 1.5),
        words('guest', 0.5, 0.685, 0.62, 32, { font: 'script', color: INK, bold: false }, { lines: 1 }),
        words('when', 0.5, 0.76, 0.84, 24, { font: 'clean', color: INK, bold: true }, { lines: 1 }),
        words('venue', 0.5, 0.8, 0.84, 22, { font: 'clean', color: INK, bold: false }, { lines: 1 }),
        words('dress', 0.5, 0.84, 0.84, 21, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.08, 0.92, 0.44, 17, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 2 }),
        words('note', 0.92, 0.92, 0.38, 17, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 2 })
      ],
      landscape: [
        border('border', 0.03, GOLD, 1.5),
        logo(0.5, 0.14, 0.11),
        vline('divider', 0.5, 0.47, 0.36, GOLD, 1.2),
        words('hostHi', 0.27, 0.3, 0.4, 22, { font: 'hindiFormal', color: INK, bold: false }, { lines: 2 }),
        words('inviteHi', 0.27, 0.42, 0.4, 20, { font: 'hindiFormal', color: INK, bold: false }, { lines: 1 }),
        words('occasionHi', 0.27, 0.54, 0.4, 34, { font: 'hindiFormal', color: MAROON, bold: false }, { lines: 2 }),
        words('host', 0.73, 0.3, 0.4, 19, { font: 'elegant', color: INK, bold: true }, { lines: 2 }),
        words('invite', 0.73, 0.42, 0.4, 19, { font: 'elegant', color: INK, bold: false, italic: true }, { lines: 1 }),
        words('occasion', 0.73, 0.54, 0.4, 31, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        words('when', 0.5, 0.72, 0.84, 21, { font: 'clean', color: INK, bold: true }, { lines: 1 }),
        words('venue', 0.5, 0.78, 0.84, 20, { font: 'clean', color: INK, bold: false }, { lines: 1 }),
        words('dress', 0.5, 0.835, 0.84, 19, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.08, 0.91, 0.42, 16, { font: 'clean', color: INK, bold: false, align: 'left' }, { anchor: 'left', lines: 1 }),
        words('note', 0.92, 0.91, 0.38, 16, { font: 'elegant', color: INK, bold: false, italic: true, align: 'right' }, { anchor: 'right', lines: 1 })
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
        logo(0.5, 0.16, 0.16),
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
        words('programme', 0.5, 0.58, 0.84, 30, { font: 'clean', color: INK, bold: false }, { lines: 10 }),
        words('when', 0.5, 0.88, 0.8, 22, { font: 'clean', color: INK, bold: true }, { lines: 2 })
      ],
      landscape: [
        border('border', 0.05, GOLD, 2),
        logo(0.14, 0.5, 0.12),
        words('occasion', 0.6, 0.18, 0.66, 30, { font: 'elegant', color: MAROON, bold: true }, { lines: 1 }),
        words('programmeTitle', 0.6, 0.31, 0.66, 22, { font: 'elegant', color: GOLD, bold: false, italic: true }, { lines: 1 }),
        words('programme', 0.6, 0.6, 0.66, 19, { font: 'clean', color: INK, bold: false }, { lines: 10 }),
        words('when', 0.6, 0.88, 0.66, 16, { font: 'clean', color: INK, bold: true }, { lines: 1 })
      ]
    }),
    pocketinvite: Object.freeze({
      key: 'pocketinvite',
      label: 'Pocket invitation',
      note: 'The invitation, small',
      pocket: true,
      background: { type: 'color', value: '#fbf7ee' },
      portrait: [
        border('border', 0.04, GOLD, 2.5),
        border('border2', 0.06, GOLD, 1),
        logo(0.5, 0.13, 0.11),
        words('host', 0.5, 0.29, 0.8, 22, { font: 'elegant', color: INK, bold: true }, { lines: 3 }),
        words('invite', 0.5, 0.39, 0.8, 24, { font: 'script', color: GOLD, bold: false }, { lines: 2 }),
        words('occasion', 0.5, 0.52, 0.84, 40, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
        words('when', 0.5, 0.66, 0.84, 22, { font: 'clean', color: INK, bold: true }, { lines: 2 }),
        words('venue', 0.5, 0.74, 0.84, 21, { font: 'clean', color: INK, bold: false }, { lines: 2 }),
        words('dress', 0.5, 0.81, 0.84, 20, { font: 'clean', color: GOLD, bold: true }, { lines: 1 }),
        words('rsvp', 0.5, 0.89, 0.84, 17, { font: 'clean', color: INK, bold: false }, { lines: 2 })
      ],
      landscape: [
        border('border', 0.04, GOLD, 2.5),
        border('border2', 0.06, GOLD, 1),
        logo(0.17, 0.5, 0.13),
        words('host', 0.62, 0.17, 0.6, 19, { font: 'elegant', color: INK, bold: true }, { lines: 2 }),
        words('invite', 0.62, 0.3, 0.6, 21, { font: 'script', color: GOLD, bold: false }, { lines: 1 }),
        words('occasion', 0.62, 0.45, 0.62, 34, { font: 'elegant', color: MAROON, bold: true }, { lines: 2 }),
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
        words('occasion', 0.1, 0.43, 0.82, 66, { font: 'clean', color: NAVY, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
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
        words('occasion', 0.07, 0.49, 0.84, 56, { font: 'clean', color: NAVY, bold: true, align: 'left' }, { anchor: 'left', lines: 2 }),
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
  function roleItem(collage, role, opts) {
    const key = collage?.card?.design;
    if (!DESIGNS[key]) return null;
    const { card, back, side, ...rest } = collage;
    const made = applyDesign({ ...rest, items: [] }, key, detailsOf(collage), opts);
    return made.items.find((item) => item.role === role) || null;
  }

  /** The unit's three colours from the details ("#7a1f2b #c9a54a #1d2c55"), each checked. */
  function colourList(text) {
    const given = String(text || '').trim().split(/[\s,]+/);
    const fallback = DEFAULT_DETAILS.colours.split(' ');
    return [0, 1, 2].map((i) => (/^#[0-9a-f]{6}$/i.test(given[i] || '') ? given[i].toLowerCase() : fallback[i]));
  }

  /** The designs offered for a size: the pocket designs for a pocket card, the others otherwise. */
  function designsFor(sizeKey) {
    const pocket = core.sizeOf(sizeKey).group === 'pocket';
    return Object.values(DESIGNS).filter((design) => Boolean(design.pocket) === pocket);
  }

  /** Whether a design follows the unit's colours. */
  function usesColours(designKey) {
    const design = DESIGNS[designKey];
    return Boolean(design && JSON.stringify(design.portrait).includes('"band'));
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
    for (const item of [...(collage?.items || []), ...(collage?.back?.items || [])]) {
      if (item.kind === 'text' && FIELDS.includes(item.field)) details[item.field] = item.text;
    }
    return details;
  }

  /** The fields a design shows words for: on a page of this size, or in either arrangement. */
  function fieldsOf(designKey, sizeKey) {
    const design = DESIGNS[designKey];
    if (!design) return [];
    const parts = sizeKey ? (isPortrait(sizeKey) ? design.portrait : design.landscape) : [...design.portrait, ...design.landscape];
    return [...new Set(parts.filter((part) => part.kind === 'text').map((part) => part.field))];
  }

  /**
   * The collage with `designKey` applied: its background, and a new set of design parts, made
   * from the details, in place of the parts of any design applied before. Items the user added
   * (photos, stickers, their own words: anything without a `role`) stay where they are, in front.
   * A field left empty has no item. `measure(text, font)` sizes the words as the page does;
   * `newId()` gives each new item its id. The page size never changes.
   */
  /**
   * One side's items: the design's parts for it, made from the wording, then the user's own
   * items (and a second logo) from `items`, the side as it was. A photo in the side's frame
   * (`frameRole`) goes into the new frame, or stays as the user's own when there is none.
   */
  function makePage(items, parts, { width, height, unit, wording, measure, newId, isCard, frameRole }) {
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
    const framed = (items || []).filter((item) => item.role === frameRole && item.kind === 'photo');
    const made = [];
    for (const part of parts) {
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
          const w = part.w * 1000 * unit;
          made.push({ ...logo, id: newId(), x: part.x * width, y: part.y * height, width: w, angle: 0 });
        }
      } else if (part.kind === 'sticker') {
        const w = part.w * 1000 * unit;
        made.push(core.stickerItem(newId(), part.sticker, { x: part.x * width, y: part.y * height, width: w, aspect: part.aspect }, { role: part.role }));
      } else {
        const text = String(wording[part.field] ?? '').slice(0, core.TEXT_LIMITS.maxLength);
        if (!text.trim()) continue;
        const item = core.textItem(newId(), text, { x: part.x * width, y: part.y * height }, {
          ...look,
          size: Math.round(part.size * unit),
          maxWidth: part.w * width,
          maxLines: part.lines || 1,
          field: part.field,
          role: part.role
        });
        const layout = core.layoutText(item, measure);
        item.width = layout.width;
        item.aspect = layout.width / layout.height;
        if (part.anchor === 'left') item.x = part.x * width + layout.width / 2;
        if (part.anchor === 'right') item.x = part.x * width - layout.width / 2;
        made.push(item);
      }
    }
    // Photos from the last design's frame with no frame here become the user's own.
    const leftOver = framed.map(({ role, ...item }) => item);
    // The user's own items, and a second logo they added, stay where they are.
    const own = (items || []).filter((item) => !item.role || item.role === 'logo2');
    return [...made, ...own, ...leftOver];
  }

  /** The look of a design's back: its background and the colours of the title, rule and words. */
  const BACK_LOOKS = Object.freeze({
    classic: { background: { type: 'color', value: '#fbf7ee' }, title: MAROON, accent: GOLD, ink: INK },
    navy: { background: { type: 'gradient', key: 'navy' }, title: PALE_GOLD, accent: PALE_GOLD, ink: IVORY },
    regimental: { background: { type: 'color', value: '#ffffff' }, title: 'band1', accent: 'band2', ink: INK },
    ceremonial: { background: { type: 'color', value: '#ffffff' }, title: MAROON, accent: MAROON, ink: INK },
    bilingual: { background: { type: 'color', value: '#fffdf7' }, title: MAROON, accent: GOLD, ink: INK },
    modern: { background: { type: 'color', value: '#ffffff' }, title: NAVY, accent: NAVY, ink: INK }
  });

  /** The back's parts for a design: the programme, a frame for a route sketch or venue photo, a note. */
  function backParts(designKey, portrait) {
    const look = BACK_LOOKS[designKey] || BACK_LOOKS.classic;
    const backPhoto = (x, y, w, h) => ({ role: 'backphoto', kind: 'slot', x, y, w, h });
    return portrait
      ? [
        border('backborder', 0.04, look.accent, 1.2),
        words('programmeTitle', 0.5, 0.11, 0.7, 40, { font: 'elegant', color: look.title, bold: true }, { lines: 1 }),
        rule('backrule', 0.5, 0.165, 0.2, look.accent, 2),
        words('programme', 0.5, 0.33, 0.8, 31, { font: 'clean', color: look.ink, bold: false }, { lines: 10 }),
        backPhoto(0.5, 0.63, 0.76, 0.22),
        words('backNote', 0.5, 0.82, 0.8, 20, { font: 'clean', color: look.ink, bold: false, italic: true }, { lines: 2 }),
        { ...logo(0.5, 0.91, 0.045), role: 'backlogo' }
      ]
      : [
        border('backborder', 0.04, look.accent, 1.2),
        words('programmeTitle', 0.5, 0.14, 0.6, 36, { font: 'elegant', color: look.title, bold: true }, { lines: 1 }),
        rule('backrule', 0.5, 0.23, 0.16, look.accent, 2),
        words('programme', 0.29, 0.54, 0.46, 26, { font: 'clean', color: look.ink, bold: false }, { lines: 10 }),
        backPhoto(0.73, 0.54, 0.4, 0.5),
        words('backNote', 0.5, 0.88, 0.84, 18, { font: 'clean', color: look.ink, bold: false, italic: true }, { lines: 1 })
      ];
  }

  /** The measures one side of a card of this size is built with. */
  function pageContext(collage, wording, { measure, newId }, extra) {
    return { width: core.PAGE_WIDTH, height: core.pageHeight(collage.size), unit: unitOf(collage.size), wording, measure, newId, ...extra };
  }

  /**
   * The card's back for the design (PR 8): the programme and the rest, made from the details,
   * with the user's own items from the back as it was.
   */
  function backPage(collage, designKey, details, opts, items = []) {
    const look = BACK_LOOKS[designKey] || BACK_LOOKS.classic;
    const wording = { ...DEFAULT_DETAILS, ...(details || {}) };
    // A field the front already shows (a programme card's programme) is not on the back again.
    const onFront = new Set(fieldsOf(designKey, collage.size));
    const parts = backParts(designKey, isPortrait(collage.size)).filter((part) => part.kind !== 'text' || !onFront.has(part.field));
    const made = makePage(items, parts, pageContext(collage, wording, opts, { isCard: false, frameRole: 'backphoto' }));
    // The back's small logo is the front's logo (the SDD logo, a crest or the user's own), or
    // none when the front has none. `collage` here shows its front.
    const front = (collage.items || []).find((item) => item.role === 'logo');
    const at = made.findIndex((item) => item.role === 'backlogo');
    if (at >= 0) {
      if (front) made.splice(at, 1, { ...front, id: made[at].id, x: made[at].x, y: made[at].y, width: made[at].width, angle: 0, role: 'backlogo' });
      else made.splice(at, 1);
    }
    return { background: { ...look.background }, items: made };
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
    const showingBack = collage.side === 'back' && collage.back;
    const card = showingBack ? core.flipSide(collage) : collage;
    const wording = { ...DEFAULT_DETAILS, ...(details || {}) };
    const parts = isPortrait(card.size) ? design.portrait : design.landscape;
    const applied = {
      ...card,
      background: { ...design.background },
      layout: null,
      items: makePage(card.items, parts, pageContext(card, wording, opts, { isCard: Boolean(card.card), frameRole: 'photo' })),
      card: { design: designKey, details: Object.fromEntries(FIELDS.map((field) => [field, wording[field]])) }
    };
    if (card.back) {
      applied.back = backPage(card, designKey, wording, opts, card.back.items);
      // Parts a newer PRISM keeps on the back go with it, as the front's do.
      if (card.back.kept) applied.back.kept = card.back.kept;
    }
    // The fields this design put on the card, both sides, so a missing one is known to be deleted.
    applied.card.placed = placedOn(applied);
    return showingBack ? core.flipSide(applied) : applied;
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

  /** The card without its back, showing its front. */
  function deleteBack(collage) {
    if (!collage?.back) return collage;
    const front = collage.side === 'back' ? core.flipSide(collage) : collage;
    const { back, side, ...rest } = front;
    // The back's fields are no longer on the card: their words, as they are now on the card
    // (typed in Card details or in an item's Words box), are kept for a new back.
    if (rest.card) rest.card = { ...rest.card, details: detailsOf(front), placed: (rest.card.placed || []).filter((field) => !BACK_FIELDS.includes(field)) };
    return rest;
  }

  /**
   * The item one field would have in the card's design, made from the details: for a field the
   * user emptied and fills again, so it comes back in its design's place. Null when the design
   * has no place for it or its words are empty.
   */
  /**
   * Whether a field is on the card's back: a back field, unless the design shows it on the
   * front (a programme card has its programme and title on the front).
   */
  function isBackField(collage, field) {
    return BACK_FIELDS.includes(field) && !fieldsOf(collage?.card?.design, collage?.size).includes(field);
  }

  function fieldItem(collage, field, details, opts) {
    const key = collage?.card?.design;
    if (!DESIGNS[key]) return null;
    if (isBackField(collage, field)) {
      if (!collage.back) return null;
      return backPage(collage, key, details, opts).items.find((item) => item.field === field) || null;
    }
    const { back, side, ...front } = collage;
    const made = applyDesign({ ...front, items: [] }, key, details, opts);
    return made.items.find((item) => item.field === field) || null;
  }

  return Object.freeze({ DESIGNS, DEFAULT_DETAILS, FIELDS, SETTINGS, BACK_FIELDS, SDD_ASPECT, unitOf, isPortrait, detailsOf, applyDesign, fieldItem, roleItem, fieldsOf, usesColours, colourList, backPage, addBack, deleteBack, designsFor, isBackField });
});
