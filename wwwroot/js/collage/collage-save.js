// PRISM Collage Studio — Save as…: the dialog (what it is for first, with what it makes; the
// type, the quality and a size to keep a JPG under folded into More options), then the picture made from the original photos and downloaded (PNG, JPG or
// a PDF at the paper's real size). Moved out of the editor in docs/ux/collage-review-fixes.md,
// PR 13. Nothing is sent anywhere: the file is made in the browser and downloaded.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageSave = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // "What is it for?" chooses a type and quality that suit it; the user can still change them
  // under More options.
  const SAVE_PURPOSES = Object.freeze({
    share: Object.freeze({ format: 'jpeg', quality: 'screen' }),
    print: Object.freeze({ format: 'pdf', quality: 'print' }),
    slide: Object.freeze({ format: 'png', quality: 'screen' })
  });

  /** The purpose a type and quality match, or null when the user chose them by hand. */
  function purposeFor(format, quality) {
    for (const [name, choice] of Object.entries(SAVE_PURPOSES)) {
      if (choice.format === format && choice.quality === quality) return name;
    }
    return null;
  }

  /**
   * The line under the purposes saying what Save will make, so the choice can be checked with
   * More options closed. pixels is { width, height }; paper is the page size's label when it is a
   * paper size (A4, A3), else null; target is a typed "keep it under" size such as "500 KB".
   */
  function saveSummary({ format, quality, pixels, paper = null, target = null, sheet = null }) {
    const size = `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels`;
    const how = quality === 'print' ? 'print quality' : 'screen size';
    // Cards on a print sheet: how many to which sheet, in print words.
    // "4 × RSVP card 3.5 × 5 in": a size's name is never made plural.
    if (format === 'pdf' && sheet) return `A PDF at ${how}: ${sheet.count === 1 ? `one ${paper || 'card'}` : `${sheet.count} × ${paper || 'card'}`} to each ${sheet.label} sheet, at true size.`;
    if (format === 'pdf') return `A PDF at ${how}: ${paper ? `${paper} at its true size, ` : ''}${size}.`;
    const kind = format === 'jpeg' ? 'JPG' : 'PNG';
    return `A ${kind} picture at ${how}: ${size}${format === 'jpeg' && target ? `, kept under ${target}` : ''}.`;
  }

  /**
   * The line in Save saying what the saved picture leaves out: empty frames, and items hidden
   * from Layers (docs/ux/collage-review-4-fixes.md, PR 6). Empty when nothing is left out.
   * `designFrames` of the empty frames are a card or poster design's photo frame: removing one in
   * Layers leaves its hole, so the line points to No photo in Card (or Poster) details, where the
   * design closes the gap (UX review 8). `word` is 'Card' or 'Poster'.
   */
  function leftOutLine(empty, hidden, designFrames = 0, word = 'Card') {
    const parts = [];
    const own = empty - designFrames;
    if (designFrames > 0) parts.push(`The design's photo frame is empty, so its place stays blank in the saved picture. Add a photo to it, or choose No photo in ${word} details and the design closes the gap.`);
    if (own === 1) parts.push(`${designFrames ? 'Another' : 'The'} empty frame is left out of the saved picture, so its place stays blank. Add a photo to it, or remove it in Layers.`);
    else if (own > 1) parts.push(`The ${own} ${designFrames ? 'other ' : ''}empty frames are left out of the saved picture, so their places stay blank. Add photos to them, or remove them in Layers.`);
    if (hidden === 1) parts.push('1 hidden item left out; show it from Layers to keep it.');
    else if (hidden > 1) parts.push(`${hidden} hidden items left out; show them from Layers to keep them.`);
    return parts.join(' ');
  }

  /**
   * The pages Save makes for a card (PR 8): the sides chosen, front first, with the name each
   * file gets. A page with one side is saved as it is.
   */
  function chosenSides(all, sides) {
    if (all.length < 2) return { pages: all, names: [''] };
    if (sides === 'front') return { pages: [all[0]], names: ['front'] };
    if (sides === 'back') return { pages: [all[1]], names: ['back'] };
    return { pages: all, names: ['front', 'back'] };
  }

  /**
   * One card for each guest (PR 10): { front, back } pages per guest, the name (by `namePage`)
   * on the side that holds it, `guestSide` (0 the front, 1 the back or a folded card's inside),
   * and only the sides chosen (`pages`). When the side with the names is not chosen, the other
   * side still comes once for each guest, so it matches the named side printed apart, and
   * `namesLeftOut` says the names were not saved.
   */
  function guestCards(all, pages, guests, guestSide, namePage) {
    if (!guests.length) return { cards: null, namesLeftOut: false };
    const chosen = (page) => pages.includes(page);
    const cards = guests.map((name) => {
      const named = namePage(all[guestSide], name);
      const front = guestSide === 0 ? named : all[0];
      const back = guestSide === 1 ? named : (all[1] || null);
      return { front: chosen(all[0]) ? front : null, back: all[1] && chosen(all[1]) ? back : null };
    });
    return { cards, namesLeftOut: !chosen(all[guestSide]) };
  }

  /**
   * Guest cards on sheets of `count` (PR 10): for each batch, its sheet of fronts, then its sheet
   * of backs; `pages[i]` goes in slot i (null leaves it empty).
   */
  function guestSheets(cards, count) {
    const sheets = [];
    for (let start = 0; start < cards.length; start += count) {
      const batch = cards.slice(start, start + count);
      for (const which of ['front', 'back']) {
        if (batch.some((card) => card[which])) sheets.push({ back: which === 'back', pages: batch.map((card) => card[which] || null) });
      }
    }
    return sheets;
  }

  /**
   * A guest's name placed by its design moved as far as the line drawn to write on was moved
   * (`line` where it is now, `designLine` where the design puts it); as it is without both.
   */
  function onTheLine(item, line, designLine) {
    if (!line || !designLine) return item;
    return { ...item, x: item.x + line.x - designLine.x, y: item.y + line.y - designLine.y };
  }

  /**
   * A guest's name in the design's guest field (PR 10): the field's words become the name,
   * laid out as `layout` says, and it stays set by the same edge as the field (left, right or
   * the middle).
   */
  /**
   * A guest's name as it is set in `item` (cold review 5): a name too long for one line breaks
   * before its "and" or "&" ("Brig P. Singh, VSM / and Mrs Singh") when that takes no more lines
   * than the words would wrap to anyway. `layout` is core.layoutText.
   */
  function guestText(item, name, layout) {
    const joined = /\s+(and|&)\s+/i;
    if (!joined.test(name)) return name;
    const plain = layout({ ...item, text: name });
    if (plain.lines < 2) return name;
    const broken = name.replace(joined, (_, word) => `${String.fromCharCode(10)}${word} `);
    return layout({ ...item, text: broken }).lines <= Math.max(2, plain.lines) ? broken : name;
  }

  function nameInField(field, name, layout) {
    const named = { ...field, text: name, width: layout.width, aspect: layout.width / layout.height };
    // A guest's card shows the name, even where the empty line was hidden.
    delete named.hidden;
    // The name's last line sits where the field's last line was, on the line drawn to write on:
    // a longer name grows upwards, and a one-line name in a field that held two moves down to it.
    const fieldHeight = field.width / field.aspect;
    named.y = field.y + fieldHeight / 2 - layout.height / 2;
    if (field.align === 'left') named.x = field.x - field.width / 2 + layout.width / 2;
    if (field.align === 'right') named.x = field.x + field.width / 2 - layout.width / 2;
    return named;
  }

  /**
   * Where Tab goes from the control that has focus, so it stays inside a dialog: from the last
   * stop to the first, and with Shift from the first (or from the dialog itself) to the last.
   * Null means the browser's own Tab is right. stops are the dialog's Tab stops in order.
   */
  function wrapFocus(stops, active, backwards) {
    if (!stops.length) return null;
    const at = stops.indexOf(active);
    if (backwards) return at <= 0 ? stops[stops.length - 1] : null;
    return at === -1 || at === stops.length - 1 ? stops[0] : null;
  }

  /** The dialog's Tab stops in order: shown, enabled, and one radio button for each group. */
  function tabStops(container) {
    const all = container.querySelectorAll('button, [href], input, select, textarea, summary, [tabindex]:not([tabindex="-1"])');
    return Array.from(all).filter((element) => {
      if (element.disabled || element.getClientRects().length === 0) return false;
      if (element.type !== 'radio') return true;
      if (element.checked) return true;
      const group = container.querySelectorAll(`input[type="radio"][name="${element.name}"]`);
      return !Array.from(group).some((radio) => radio.checked) && group[0] === element;
    });
  }

  /**
   * $ finds an element on the page; getCollage() is the collage now; photos is its photo Map;
   * the rest are the editor's own helpers (see collage-app.js).
   */
  function createSaveDialog({
    $, core, draw, stickers, measureText, fontsReady, stickerImage,
    getCollage, photos, announce, showAlert, collageTitle, hasContent, stopAdjust, flushNudge, designs = null,
    print = window.PrismCollagePrint
  }) {
    const saveDialog = $('[data-save-dialog]');
    const saveResult = $('[data-save-result]');
    const targetRow = $('[data-target-row]');
    const targetOn = $('[data-target-on]');
    const targetValue = $('[data-target-value]');
    const targetUnit = $('[data-target-unit]');
    const photoTools = window.PrismPhotoTools;
    let saving = false;

    const chosen = (name) => saveDialog.querySelector(`input[name="${name}"]:checked`)?.value;

    function syncSaveDialog() {
      // Sides are offered for a card with a back (PR 8).
      const sidesBox = $('[data-save-sides]');
      if (sidesBox) sidesBox.hidden = !getCollage().back;
      // Which edge to flip on when printing both sides: a folded card and a landscape card on its
      // own pages flip on the short edge, everything else on the long edge.
      const flipNote = $('[data-sides-flip]');
      if (flipNote && getCollage().back) {
        const size = core.sizeOf(getCollage().size);
        const onSheets = Boolean(sheetBoxChoice());
        const short = size.group === 'folded' || (!onSheets && size.mm && size.mm[0] > size.mm[1]);
        flipNote.textContent = `To print both on one card, flip on the ${short ? 'short' : 'long'} edge${size.group === 'folded' ? ', then fold down the middle' : ''}.`;
      }
      // A card with a guest list, as a PDF, can be one card for each guest (PR 10).
      const guestsBox = $('[data-save-guests]');
      if (guestsBox) {
        guestsBox.hidden = !guestCardsOffered(chosen('format'));
        const label = guestsBox.querySelector('[data-guest-label]');
        if (label) label.textContent = `One card for each guest (${getCollage().card?.guests?.length || 0})`;
      }
      // A card printed as a PDF can go several to an A4 or A3 sheet (PR 9).
      const sheetBox = $('[data-save-sheet]');
      if (sheetBox) {
        const group = core.sizeOf(getCollage().size).group;
        const isCard = ['card', 'pocket', 'custom'].includes(group);
        sheetBox.hidden = !(isCard && chosen('format') === 'pdf');
        const flip = getCollage().back ? '; the back sheet lines up when the printer flips on the long edge' : '';
        // Where edge to edge fits more on the same paper, the sheet with margins says why it fits fewer
        // (A5 on A4: one with room for cutting, two edge to edge).
        const edgeMore = (key, fits) => {
          const edge = Object.keys(print.SHEETS).find((other) => print.SHEETS[other].edge && print.SHEETS[other].mm.join() === print.SHEETS[key].mm.join());
          const more = edge ? print.sheetLayout(getCollage().size, edge)?.count || 0 : 0;
          return more > fits ? ` The margins and gaps for cutting leave room for only ${fits === 1 ? 'one' : fits}; ${print.SHEETS[edge].label} fits ${more}.` : '';
        };
        for (const key of Object.keys(print.SHEETS)) {
          const layout = print.sheetLayout(getCollage().size, key);
          const note = sheetBox.querySelector(`[data-sheet-note="${key}"]`);
          const input = sheetBox.querySelector(`input[name="sheet"][value="${key}"]`);
          const fits = layout?.count || 0;
          // Edge to edge is offered only where it fits more cards than A4 with room for cutting.
          const offered = !layout?.edge || fits > (print.sheetLayout(getCollage().size, 'a4')?.count || 0);
          const option = input?.closest('label');
          if (option) option.hidden = !offered;
          if (note) {
            note.textContent = !fits ? 'The card is too large for this sheet.'
              : layout.edge ? `${fits} cards touching, no cut marks: cut on the card edges${flip}. It needs a printer that prints to the paper's edge; most office printers leave a margin or shrink the page, so print one sheet to check.`
              : `${fits} card${fits === 1 ? '' : 's'} to a sheet, with cut marks${flip}.${edgeMore(key, fits)}`;
          }
          if (input) {
            input.disabled = !fits || !offered;
            if (input.disabled && input.checked) sheetBox.querySelector('input[name="sheet"][value="card"]').checked = true;
          }
        }
      }
      const size = core.sizeOf(getCollage().size);
      const pixels = core.savePixels(getCollage().size, 'print');
      // A PDF's quality in print words; a picture's in pixels.
      $('[data-print-note]').textContent = chosen('format') === 'pdf'
        ? `300 dots per inch: sharp in print.`
        : size.mm
        ? `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels, 300 dots per inch on ${size.label} paper.`
        : `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels.`;
      $('[data-pdf-note]').textContent = size.mm
        ? `${size.label} at its true size (${size.note}), ready to print.`
        : `One page shaped like the ${size.label.toLowerCase()} page.`;
      const jpeg = chosen('format') === 'jpeg';
      targetRow.hidden = !jpeg;
      targetValue.disabled = !targetOn.checked;
      targetUnit.disabled = !targetOn.checked;
      // Both sides of a card are counted. The roundel a design hides while there is no logo is
      // the design's, not something the user hid.
      const sides = [...getCollage().items, ...(getCollage().back?.items || [])];
      // A poster's empty logo place is not a photo frame: its own question says it is left out.
      const empty = sides.filter((item) => item.kind === 'slot' && item.role !== 'logo').length;
      const hidden = sides.filter((item) => item.kind !== 'slot' && item.hidden && item.role !== 'roundel').length;
      // A design's own photo frame (Ceremonial photo, a photo poster) has No photo in the details.
      const designFrames = getCollage().card ? sides.filter((item) => item.kind === 'slot' && item.role === 'photo').length : 0;
      const word = designs?.DESIGNS?.[getCollage().card?.design]?.poster ? 'Poster' : 'Card';
      const emptyLine = $('[data-save-empty]');
      // A card with Hindi lines whose Hindi is empty while its English is not prints half a line
      // (UX review 7): Save says so before the file is made.
      const details = designs?.detailsOf(getCollage()) || {};
      const onCard = designs?.fieldsOnCard(getCollage()) || [];
      const hindiNames = { occasionHi: 'occasion', hostHi: 'host', inviteHi: 'invitation line' };
      const emptyHindi = Object.keys(hindiNames).filter((field) => onCard.includes(field) && !String(details[field] || '').trim() && String(details[field.replace(/Hi$/, '')] || '').trim());
      const hindiNote = emptyHindi.length ? ` The Hindi ${emptyHindi.map((field) => hindiNames[field]).join(' and ')} ${emptyHindi.length === 1 ? 'is' : 'are'} empty, so the card shows only the English there: type ${emptyHindi.length === 1 ? 'it' : 'them'} in Card details.` : '';
      emptyLine.textContent = `${leftOutLine(empty, hidden, designFrames, word)}${hindiNote}`.trim();
      emptyLine.hidden = !emptyLine.textContent;
      const typed = targetValue.value.trim();
      saveSummaryLine.textContent = saveSummary({
        format: chosen('format'),
        quality: chosen('quality'),
        pixels: core.savePixels(getCollage().size, chosen('quality')),
        paper: size.mm ? size.label : null,
        target: targetOn.checked && typed ? `${typed} ${targetUnit.value}` : null,
        sheet: sheetSummary()
      });
    }

    // More options (the type and quality) starts closed, and stays open next time once the user
    // has opened it. Only a convenience, like the purpose: nothing breaks without storage.
    const saveSummaryLine = $('[data-save-summary]');
    const saveMore = $('[data-save-more]');
    const MORE_KEY = 'prism-collage-save-more';
    const rememberedMore = () => {
      try { return window.localStorage?.getItem(MORE_KEY) === 'open'; } catch { return false; }
    };
    saveMore.addEventListener('toggle', () => {
      try { window.localStorage?.setItem(MORE_KEY, saveMore.open ? 'open' : 'closed'); } catch { /* not kept; fine */ }
    });

    // The last purpose is remembered in this browser; the first time, WhatsApp or email, the
    // commonest job (a phone especially). Only a convenience: nothing breaks without storage.
    const PURPOSE_KEY = 'prism-collage-save-purpose';
    const rememberedPurpose = () => {
      try {
        const value = window.localStorage?.getItem(PURPOSE_KEY);
        return SAVE_PURPOSES[value] ? value : 'share';
      } catch {
        return 'share';
      }
    };
    const rememberPurpose = (value) => {
      try { window.localStorage?.setItem(PURPOSE_KEY, value); } catch { /* not kept; fine */ }
    };

    /** Sets the type and quality for a purpose and shows that purpose as chosen. */
    function choosePurpose(value) {
      const choice = SAVE_PURPOSES[value];
      if (!choice) return;
      for (const [name, wanted] of Object.entries(choice)) {
        const radio = saveDialog.querySelector(`input[name="${name}"][value="${wanted}"]`);
        if (radio) radio.checked = true;
      }
      markPurpose();
    }

    /** Shows which purpose the chosen type and quality match, if any. */
    function markPurpose() {
      const matched = purposeFor(chosen('format'), chosen('quality'));
      saveDialog.querySelectorAll('[data-save-purpose]').forEach((button) => {
        const on = matched !== null && button.dataset.savePurpose === matched;
        button.setAttribute('aria-pressed', String(on));
        button.classList.toggle('is-on', on);
      });
    }

    function openSave() {
      if (!hasContent()) {
        showAlert('Add a photo, some text or a sticker first; there is nothing to save yet.');
        return;
      }
      flushNudge();
      stopAdjust();
      saveResult.hidden = true;
      // The dialog names what is saved: a card, a poster, or a collage.
      const design = designs?.DESIGNS?.[getCollage()?.card?.design];
      const heading = saveDialog.querySelector('#clgSaveTitle');
      if (heading) heading.textContent = design ? (design.poster ? 'Save the poster' : 'Save the card') : 'Save the collage';
      choosePurpose(rememberedPurpose());
      saveMore.open = rememberedMore();
      syncSaveDialog();
      saveDialog.showModal();
    }

    saveDialog.addEventListener('change', () => { markPurpose(); syncSaveDialog(); });
    // Typing a "keep it under" size updates the line under the purposes.
    saveDialog.addEventListener('input', (event) => { if (event.target === targetValue) syncSaveDialog(); });
    // Tab stays inside the dialog: past Save it comes back to Close, not to the page behind.
    saveDialog.addEventListener('keydown', (event) => {
      if (event.key !== 'Tab' || event.altKey || event.ctrlKey || event.metaKey) return;
      const next = wrapFocus(tabStops(saveDialog), document.activeElement, event.shiftKey);
      if (!next) return;
      event.preventDefault();
      next.focus();
    });

    saveDialog.addEventListener('click', (event) => {
      const button = event.target.closest('[data-save-purpose]');
      const choice = button && SAVE_PURPOSES[button.dataset.savePurpose];
      if (!choice) return;
      choosePurpose(button.dataset.savePurpose);
      rememberPurpose(button.dataset.savePurpose);
      syncSaveDialog();
      announce(saveSummaryLine.textContent);
    });
    // Esc while saving does not close the dialog; while guest cards are made it stops them, and
    // nothing is saved (code review 7: 300 cards take a while).
    let stopGuests = false;
    saveDialog.addEventListener('cancel', (event) => {
      if (!saving) return;
      event.preventDefault();
      if (guestsInProgress) stopGuests = true;
    });
    let guestsInProgress = false;
    // The browser closes a modal dialog after a few Esc presses even when each is refused: while
    // saving, the dialog opens again at once, so the save never runs on unseen (code review 8).
    saveDialog.addEventListener('close', () => {
      if (saving && !saveDialog.open) saveDialog.showModal();
    });

    // Save draws the collage from the original photos (collage-render.js).
    const renderer = window.PrismCollageRender.createRenderer({ core, draw, measureText, fontsReady, stickerImage });
    // What the last Save left out: photos shown grey, stickers PRISM could not send.
    let saveLeftOut = 0;
    let saveStickersLeftOut = 0;

    function download(bytes, type, name) {
      const url = URL.createObjectURL(new Blob([bytes], { type }));
      const link = document.createElement('a');
      link.href = url;
      link.download = name;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 60000);
    }

    /**
     * A side of the card (the one that holds the guest's name) with a guest's name in the guest
     * field (PR 10): the field's words, or, for a design that leaves a line to write on, the field
     * its design has for the name. Null when that side is not part of what is saved.
     */
    function namedPage(page, name) {
      if (!page) return null;
      const linked = page.items.find((item) => item.field === 'guest');
      const layout = (item) => core.layoutText(item, measureText);
      if (linked) {
        const text = guestText(linked, name, layout);
        const named = nameInField(linked, text, layout({ ...linked, text }));
        return { ...page, items: page.items.map((item) => (item === linked ? named : item)) };
      }
      // A design that leaves a line to write on: the name in the place its design has for it.
      let n = 0;
      const opts = { measure: measureText, newId: () => `guest${++n}` };
      const makeWith = (guest) => designs?.fieldItem(getCollage(), 'guest', { ...designs.detailsOf(getCollage()), guest }, opts);
      let made = makeWith(name);
      if (!made) return page;
      const text = guestText(made, name, layout);
      if (text !== name) made = makeWith(text) || made;
      // On the line drawn to write on, wherever the user moved it: the name moves with it.
      const line = page.items.find((item) => item.role === 'guestline');
      const designLine = line && designs.roleItemOn(getCollage(), 'guestline', designs.sideOf(getCollage(), 'guest') || 'front', opts);
      const placed = onTheLine(made, line, designLine);
      return { ...page, items: [...page.items, placed] };
    }

    /**
     * One sheet page of cards with cut marks (PR 9): `images[i]` goes in slot i (none leaves the
     * slot empty); a back sheet's cards are placed and turned to line up with the fronts.
     */
    function addSheet(pdf, layout, images, back) {
      const pt = print.PT_PER_MM;
      const [sw, sh] = layout.sheet;
      const sheet = pdf.addPage([sw * pt, sh * pt]);
      print.sheetPlacements(layout, back).forEach((place, i) => {
        if (images[i]) sheet.drawImage(images[i], { ...place, rotate: window.PDFLib.degrees(place.rotate) });
      });
      const grey = window.PDFLib.rgb(0.35, 0.35, 0.35);
      // Marks only round the cards the sheet has (the last sheet of guests may not be full).
      const filled = { ...layout, cards: layout.cards.filter((_, i) => images[i]) };
      for (const [x1, y1, x2, y2] of [...print.cutMarks(filled), ...print.foldMarks(getCollage().size, filled)]) {
        sheet.drawLine({ start: { x: x1 * pt, y: (sh - y1) * pt }, end: { x: x2 * pt, y: (sh - y2) * pt }, thickness: 0.4, color: grey });
      }
    }

    /** One card on its own PDF page; a folded card has fold marks at the top and bottom of its fold. */
    function addCardPage(pdf, image) {
      const size = core.pdfPage(getCollage().size);
      const page = pdf.addPage([size.width, size.height]);
      page.drawImage(image, { x: 0, y: 0, width: size.width, height: size.height });
      const marks = print.foldMarks(getCollage().size);
      if (!marks.length) return;
      // The page's own height in mm, from its points, so the marks meet its edges exactly.
      const pt = size.width / core.sizeOf(getCollage().size).mm[0];
      const grey = window.PDFLib.rgb(0.35, 0.35, 0.35);
      for (const [x1, y1, x2, y2] of marks) {
        page.drawLine({ start: { x: x1 * pt, y: size.height - y1 * pt }, end: { x: x2 * pt, y: size.height - y2 * pt }, thickness: 0.4, color: grey });
      }
    }

    /**
     * Guest cards into the PDF (PR 10): `cards` are { front, back } pages per guest, the guest's
     * name on the side that holds it. Each card on its own pages, or filling sheets (each sheet of
     * fronts followed by its sheet of backs). A page every guest shares is drawn once; the others
     * one at a time, each let go before the next.
     */
    async function guestPdf(pdf, cards, fits, sheetKey) {
      const shared = new Map();
      // Progress: which card is being made, and a moment for the page to show it and hear Esc.
      let made = 0;
      // Every picture drawn counts, fronts and backs alike, so Esc is heard on any sheet, a folded
      // card's insides alone included (Codex on #3454).
      const total = cards.reduce((sum, card) => sum + (card.front ? 1 : 0) + (card.back ? 1 : 0), 0);
      const progress = async () => {
        if (stopGuests) throw new Error('stopped');
        made += 1;
        saveResult.textContent = `Making the guest cards: ${Math.min(made, total)} of ${total} pictures… Press Esc to stop.`;
        await new Promise((resolve) => window.setTimeout(resolve, 0));
        if (stopGuests) throw new Error('stopped');
      };
      const imageOf = async (page) => {
        if (shared.has(page)) return shared.get(page);
        const drawn = await renderer.drawCollage(page, photos, fits.width, fits.height);
        saveLeftOut += drawn.leftOutPhotos;
        saveStickersLeftOut += drawn.leftOutStickers;
        let image;
        try {
          image = await pdf.embedJpg(await renderer.toBytes(drawn.canvas, 'image/jpeg', 0.9));
        } finally {
          renderer.release(drawn.canvas);
        }
        if (cards.length > 1 && cards.every((card) => card.front === page || card.back === page)) shared.set(page, image);
        return image;
      };
      const layout = sheetKey ? print.sheetLayout(getCollage().size, sheetKey) : null;
      if (!layout?.count) {
        for (const card of cards) {
          for (const page of [card.front, card.back].filter(Boolean)) {
            await progress();
            addCardPage(pdf, await imageOf(page));
          }
        }
        return;
      }
      const backLayout = print.sheetLayout(getCollage().size, sheetKey, { back: true });
      for (const sheet of guestSheets(cards, layout.count)) {
        const images = [];
        for (const page of sheet.pages) {
          if (page) await progress();
          images.push(page ? await imageOf(page) : null);
        }
        addSheet(pdf, sheet.back ? backLayout : layout, images, sheet.back);
      }
    }

    /** The print sheet chosen and how many cards fit on it, for the summary; null for none. */
    function sheetSummary() {
      const key = sheetBoxChoice();
      const layout = key ? print.sheetLayout(getCollage().size, key) : null;
      return layout?.count ? { count: layout.count, label: print.SHEETS[key].label.replace(', edge to edge', ' (edge to edge)') } : null;
    }

    /**
     * Whether Save offers one card for each guest (PR 10): a PDF of a card with a guest list whose
     * design has a place for the guest's name. The one rule for the dialog and for Save itself.
     */
    function guestCardsOffered(format) {
      const collage = getCollage();
      return format === 'pdf' && Boolean(collage.card?.guests?.length) && Boolean(designs?.fieldsOnCard(collage).includes('guest'));
    }

    /** The sheet chosen for a card's PDF (PR 9), or null for each card on its own page. */
    function sheetBoxChoice() {
      const box = $('[data-save-sheet]');
      if (!box || box.hidden) return null;
      const value = chosen('sheet');
      return print.SHEETS[value] ? value : null;
    }

    async function saveCollage() {
      if (saving) return;
      const format = chosen('format');
      const quality = chosen('quality');
      let targetBytes = null;
      if (format === 'jpeg' && targetOn.checked) {
        targetBytes = photoTools?.parseTargetBytes(targetValue.value, targetUnit.value) ?? null;
        if (!targetBytes) {
          saveResult.hidden = false;
          saveResult.textContent = 'Type a size such as 500 KB or 2 MB.';
          targetValue.focus();
          return;
        }
      }
      saving = true;
      const goButton = $('[data-save-go]');
      goButton.disabled = true;
      saveResult.hidden = false;
      saveResult.textContent = 'Making the picture…';
      let canvas = null;
      try {
        const wanted = core.savePixels(getCollage().size, quality);
        const fits = renderer.largestCanvas(wanted.width, wanted.height);
        if (!fits) throw new Error('canvas');
        // A card with a back (docs/ux/invitations-posters.md, PR 8): the sides chosen, front
        // first; a PDF gets a page for each, a picture a file for each.
        let all = core.pagesOf(getCollage());
        const { pages, names } = chosenSides(all, all.length > 1 ? (chosen('sides') || 'both') : 'front');
        // One card for each guest (PR 10): the front once a guest, their name on it, then the back.
        const guests = guestCardsOffered(format) && chosen('guests') === 'each' ? getCollage().card.guests : [];
        // The name goes on the side the design puts the guest's name (a folded card's inside).
        // The side that holds a guest field (one the user locked stays where it is), else the
        // side the design puts it on.
        const holdsGuest = (page) => Boolean(page?.items.some((item) => item.field === 'guest'));
        const guestSide = holdsGuest(all[0]) ? 0 : holdsGuest(all[1]) ? 1 : designs?.sideOf(getCollage(), 'guest') === 'back' ? 1 : 0;
        // Each name is measured in the card's own fonts, so it keeps its edge: they load first.
        if (guests.length) {
          // The guest's name made from the design (where the card leaves a line) is in a font the
          // card may not use yet: it loads too.
          const sample = designs?.fieldItem(getCollage(), 'guest', { ...designs.detailsOf(getCollage()), guest: guests[0] }, { measure: measureText, newId: () => 'fontcheck' });
          await fontsReady?.(sample ? [sample] : []);
        }
        // The side that holds the names not being saved: one copy, as it is, and a word why.
        const { cards, namesLeftOut } = guestCards(all, pages, guests, guestSide, namedPage);
        const stem = core.fileStem(collageTitle());
        saveLeftOut = 0;
        saveStickersLeftOut = 0;
        let width = 0;
        let height = 0;
        let missed = false;
        const files = [];
        const pdf = format === 'pdf' ? await window.PDFLib.PDFDocument.create() : null;
        if (pdf) {
          pdf.setTitle(collageTitle());
          pdf.setCreator('PRISM Collage Studio');
        }
        const pdfSheet = format === 'pdf' ? sheetBoxChoice() : null;
        if (cards && cards.length) {
          stopGuests = false;
          guestsInProgress = true;
          await guestPdf(pdf, cards, fits, pdfSheet);
          // Esc pressed as the last picture was finished still stops before anything is saved.
          if (stopGuests) throw new Error('stopped');
          width = fits.width;
          height = fits.height;
        }
        for (const [index, collagePage] of (cards && cards.length ? [] : pages).entries()) {
          const drawn = await renderer.drawCollage(collagePage, photos, fits.width, fits.height);
          canvas = drawn.canvas;
          saveLeftOut += drawn.leftOutPhotos;
          saveStickersLeftOut += drawn.leftOutStickers;
          width = canvas.width;
          height = canvas.height;
          const suffix = names[index] ? `-${names[index]}` : '';
          if (format === 'png') {
            files.push({ bytes: await renderer.toBytes(canvas, 'image/png'), type: 'image/png', name: `${stem}${suffix}.png` });
          } else if (format === 'jpeg') {
            let bytes;
            if (targetBytes && photoTools) {
              const result = await photoTools.encodeToTarget({
                encode: (w, h, q) => renderer.scaledBytes(canvas, w, h, 'image/jpeg', q),
                lossy: true,
                width,
                height,
                targetBytes
              });
              bytes = result.bytes;
              width = result.width;
              height = result.height;
              missed = missed || !result.reached;
            } else {
              bytes = await renderer.toBytes(canvas, 'image/jpeg', 0.92);
            }
            files.push({ bytes, type: 'image/jpeg', name: `${stem}${suffix}.jpg` });
          } else {
            const image = await pdf.embedJpg(await renderer.toBytes(canvas, 'image/jpeg', 0.92));
            // The sheet chosen when Save was pressed, for every page of this PDF.
            const sheetKey = pdfSheet;
            const layout = sheetKey ? print.sheetLayout(getCollage().size, sheetKey, { back: names[index] === 'back' }) : null;
            if (layout?.count) {
              // As many as fit, with cut marks in the gaps.
              addSheet(pdf, layout, layout.cards.map(() => image), names[index] === 'back');
            } else {
              addCardPage(pdf, image);
            }
          }
          renderer.release(canvas);
          canvas = null;
        }
        const pageCount = pdf ? pdf.getPageCount() : 0;
        if (pdf) files.push({ bytes: await pdf.save(), type: 'application/pdf', name: `${stem}.pdf` });
        for (const file of files) download(file.bytes, file.type, file.name);
        const total = files.reduce((sum, file) => sum + file.bytes.length, 0);
        const sizeText = photoTools ? photoTools.formatBytes(total) : `${Math.ceil(total / 1000)} KB`;
        const saved = files.length === 1 ? `Saved “${files[0].name}”` : `Saved ${files.map((file) => `“${file.name}”`).join(' and ')}`;
        const pageNote = cards && cards.length ? `${cards.length} guest cards on ${pageCount} page${pageCount === 1 ? '' : 's'}` : pdf && pages.length > 1 ? `${pages.length} pages` : null;
        const namesNote = namesLeftOut ? `The guests' names go on the ${designs.isFolded(getCollage().size) ? 'inside' : guestSide ? 'back' : 'front'}, which was not saved, so the ${guestSide ? 'outside' : 'back'} was saved once for each guest, without names` : null;
        const pixelsNote = pdf ? null : `${width.toLocaleString('en-IN')} × ${height.toLocaleString('en-IN')} pixels`;
        const parts = [saved, pageNote, namesNote, pixelsNote, sizeText].filter(Boolean);
        let line = `${parts.join(' · ')}.`;
        if (fits.scaled) {
          const dpi = core.paperDpi(getCollage().size, fits.width);
          line += ` This browser cannot draw a picture that large, so it was made a little smaller${dpi ? ` (${dpi} dots per inch)` : ''}.`;
        }
        if (missed) line += ` It could not get under ${photoTools.formatBytes(targetBytes)}; this is the smallest it can be. Choose Screen or a larger size.`;
        if (saveLeftOut) line += ` ${saveLeftOut === 1 ? 'One photo' : `${saveLeftOut} photos`} shown grey could not be opened in this browser and ${saveLeftOut === 1 ? 'was' : 'were'} left out.`;
        if (saveStickersLeftOut) line += ` ${saveStickersLeftOut === 1 ? 'One sticker' : `${saveStickersLeftOut} stickers`} could not be loaded and ${saveStickersLeftOut === 1 ? 'was' : 'were'} left out; check the connection to PRISM and save again.`;
        saveResult.textContent = line;
        announce(line);
      } catch (error) {
        saveResult.textContent = error?.message === 'stopped'
          ? 'Stopped: the guest cards were not saved.'
          : 'The collage could not be saved. Try Screen quality, or close other tabs and try again.';
      } finally {
        // A failed save lets go of its picture too.
        renderer.release(canvas);
        guestsInProgress = false;
        stopGuests = false;
        saving = false;
        goButton.disabled = false;
      }
    }

    return Object.freeze({
      open: openSave,
      save: saveCollage,
      close: () => { if (!saving) saveDialog.close(); }
    });
  }

  return Object.freeze({ createSaveDialog, SAVE_PURPOSES, purposeFor, saveSummary, leftOutLine, wrapFocus, tabStops, chosenSides, guestCards, guestSheets, nameInField, guestText, onTheLine });
});
