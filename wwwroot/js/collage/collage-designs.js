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
    guest: ''
  });
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
    // never placed (the page was turned since, say) keeps its words for when it is.
    for (const field of collage?.card?.placed || []) if (FIELDS.includes(field)) details[field] = '';
    for (const item of collage?.items || []) {
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
  function applyDesign(collage, designKey, details, { measure, newId }) {
    const design = DESIGNS[designKey];
    if (!design) return collage;
    const width = core.PAGE_WIDTH;
    const height = core.pageHeight(collage.size);
    const unit = unitOf(collage.size);
    const wording = { ...DEFAULT_DETAILS, ...(details || {}) };
    const parts = isPortrait(collage.size) ? design.portrait : design.landscape;
    const made = [];
    for (const part of parts) {
      if (part.kind === 'shape' && part.inset !== undefined) {
        const inset = part.inset * Math.min(width, height);
        const look = { ...part.look, strokeWidth: part.look.strokeWidth * unit, role: part.role };
        made.push(core.shapeItem(newId(), 'rect', { x: width / 2, y: height / 2, width: width - inset * 2, aspect: (width - inset * 2) / (height - inset * 2) }, look));
      } else if (part.kind === 'shape') {
        // A divider standing upright: w of 0 means a line down the page's middle third.
        const upright = part.w === 0;
        const length = upright ? height * 0.6 : part.w * width;
        const item = core.shapeItem(newId(), 'line', { x: part.x * width, y: part.y * height, width: length }, { ...part.look, strokeWidth: part.look.strokeWidth * unit, role: part.role });
        if (upright) item.angle = 90;
        made.push(item);
      } else if (part.kind === 'sticker') {
        const w = part.w * 1000 * unit;
        made.push(core.stickerItem(newId(), part.sticker, { x: part.x * width, y: part.y * height, width: w, aspect: part.aspect }, { role: part.role }));
      } else {
        const text = String(wording[part.field] ?? '').slice(0, core.TEXT_LIMITS.maxLength);
        if (!text.trim()) continue;
        const item = core.textItem(newId(), text, { x: part.x * width, y: part.y * height }, {
          ...part.look,
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
    const own = (collage.items || []).filter((item) => !item.role);
    return {
      ...collage,
      background: { ...design.background },
      layout: null,
      items: [...made, ...own],
      card: {
        design: designKey,
        details: Object.fromEntries(FIELDS.map((field) => [field, wording[field]])),
        // The fields this design put on the card, so a missing one is known to be deleted.
        placed: [...new Set(made.filter((item) => item.field).map((item) => item.field))]
      }
    };
  }

  /**
   * The item one field would have in the card's design, made from the details: for a field the
   * user emptied and fills again, so it comes back in its design's place. Null when the design
   * has no place for it or its words are empty.
   */
  function fieldItem(collage, field, details, opts) {
    const key = collage?.card?.design;
    if (!DESIGNS[key]) return null;
    const made = applyDesign({ ...collage, items: [] }, key, details, opts);
    return made.items.find((item) => item.field === field) || null;
  }

  return Object.freeze({ DESIGNS, DEFAULT_DETAILS, FIELDS, unitOf, isPortrait, detailsOf, applyDesign, fieldItem, fieldsOf });
});
