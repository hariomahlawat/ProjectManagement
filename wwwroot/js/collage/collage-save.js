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
  function saveSummary({ format, quality, pixels, paper = null, target = null }) {
    const size = `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels`;
    const how = quality === 'print' ? 'print quality' : 'screen size';
    if (format === 'pdf') return `A PDF at ${how}: ${paper ? `${paper} at its true size, ` : ''}${size}.`;
    const kind = format === 'jpeg' ? 'JPG' : 'PNG';
    return `A ${kind} picture at ${how}: ${size}${format === 'jpeg' && target ? `, kept under ${target}` : ''}.`;
  }

  /**
   * The line in Save saying what the saved picture leaves out: empty frames, and items hidden
   * from Layers (docs/ux/collage-review-4-fixes.md, PR 6). Empty when nothing is left out.
   */
  function leftOutLine(empty, hidden) {
    const parts = [];
    if (empty === 1) parts.push('The empty frame is left out of the saved picture.');
    else if (empty > 1) parts.push(`The ${empty} empty frames are left out of the saved picture.`);
    if (hidden === 1) parts.push('1 hidden item left out; show it from Layers to keep it.');
    else if (hidden > 1) parts.push(`${hidden} hidden items left out; show them from Layers to keep them.`);
    return parts.join(' ');
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
    getCollage, photos, announce, showAlert, collageTitle, hasContent, stopAdjust, flushNudge, designs = null
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
      // A card with a guest list, as a PDF, can be one card for each guest (PR 10).
      const guestsBox = $('[data-save-guests]');
      // Only where the design has a place for the guest's name (not a car pass or RSVP card).
      const guestPlace = Boolean(designs?.fieldsOf(getCollage().card?.design, getCollage().size).includes('guest'));
      const guestCount = guestPlace ? (getCollage().card?.guests?.length || 0) : 0;
      if (guestsBox) {
        guestsBox.hidden = !(guestCount && chosen('format') === 'pdf');
        const label = guestsBox.querySelector('[data-guest-label]');
        if (label) label.textContent = `One card for each guest (${guestCount})`;
      }
      // A card printed as a PDF can go several to an A4 or A3 sheet (PR 9).
      const sheetBox = $('[data-save-sheet]');
      if (sheetBox) {
        const group = core.sizeOf(getCollage().size).group;
        const isCard = ['card', 'pocket', 'custom'].includes(group);
        sheetBox.hidden = !(isCard && chosen('format') === 'pdf');
        for (const key of Object.keys(core.SHEETS)) {
          const layout = core.sheetLayout(getCollage().size, key);
          const note = sheetBox.querySelector(`[data-sheet-note="${key}"]`);
          const input = sheetBox.querySelector(`input[name="sheet"][value="${key}"]`);
          const fits = layout?.count || 0;
          if (note) note.textContent = fits ? `${fits} card${fits === 1 ? '' : 's'} to a sheet, with cut marks${getCollage().back ? '; the back sheet lines up when the printer flips on the long edge' : ''}.` : 'The card is too large for this sheet.';
          if (input) {
            input.disabled = !fits;
            if (!fits && input.checked) sheetBox.querySelector('input[name="sheet"][value="card"]').checked = true;
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
      const empty = sides.filter((item) => item.kind === 'slot').length;
      const hidden = sides.filter((item) => item.kind !== 'slot' && item.hidden && item.role !== 'roundel').length;
      const emptyLine = $('[data-save-empty]');
      emptyLine.textContent = leftOutLine(empty, hidden);
      emptyLine.hidden = !emptyLine.textContent;
      const typed = targetValue.value.trim();
      saveSummaryLine.textContent = saveSummary({
        format: chosen('format'),
        quality: chosen('quality'),
        pixels: core.savePixels(getCollage().size, chosen('quality')),
        paper: size.mm ? size.label : null,
        target: targetOn.checked && typed ? `${typed} ${targetUnit.value}` : null
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
    saveDialog.addEventListener('cancel', (event) => { if (saving) event.preventDefault(); });

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
     * A side of the card with a guest's name in the guest field (PR 10): the field's words, or,
     * for a design that leaves a line to write on, the field its design has for the name, on the
     * side that holds it (a folded card's inside).
     */
    function namedPage(page, name) {
      if (!page) return null;
      const linked = page.items.find((item) => item.field === 'guest');
      if (linked) {
        // The name in the guest field, set by the same edge as the field (left, right or middle).
        const named = { ...linked, text: name };
        const layout = core.layoutText(named, measureText);
        if (named.align === 'left') named.x = linked.x - linked.width / 2 + layout.width / 2;
        if (named.align === 'right') named.x = linked.x + linked.width / 2 - layout.width / 2;
        named.width = layout.width;
        named.aspect = layout.width / layout.height;
        return { ...page, items: page.items.map((item) => (item === linked ? named : item)) };
      }
      // A design that leaves a line to write on: the name in the place its design has for it.
      let n = 0;
      const made = designs?.fieldItem(getCollage(), 'guest', { ...designs.detailsOf(getCollage()), guest: name }, { measure: measureText, newId: () => `guest${++n}` });
      return made ? { ...page, items: [...page.items, made] } : page;
    }

    /** Cut marks for a sheet layout onto a PDF page `sh` mm tall. */
    function drawCutMarks(sheet, layout, sh) {
      const pt = 72 / 25.4;
      const grey = window.PDFLib.rgb(0.35, 0.35, 0.35);
      for (const [x1, y1, x2, y2] of core.cutMarks(layout)) {
        sheet.drawLine({ start: { x: x1 * pt, y: (sh - y1) * pt }, end: { x: x2 * pt, y: (sh - y2) * pt }, thickness: 0.4, color: grey });
      }
    }

    /**
     * Guest cards into the PDF (PR 10): `cards` are { front, back } pages per guest, the guest's
     * name on the side that holds it. Each card on its own pages, or filling sheets (each sheet of
     * fronts followed by its sheet of backs). A page every guest shares is drawn once; the others
     * one at a time, each let go before the next.
     */
    async function guestPdf(pdf, cards, fits, sheetKey) {
      const pt = 72 / 25.4;
      const shared = new Map();
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
      const layout = sheetKey ? core.sheetLayout(getCollage().size, sheetKey) : null;
      if (!layout?.count) {
        const size = core.pdfPage(getCollage().size);
        for (const card of cards) {
          for (const page of [card.front, card.back].filter(Boolean)) {
            pdf.addPage([size.width, size.height]).drawImage(await imageOf(page), { x: 0, y: 0, width: size.width, height: size.height });
          }
        }
        return;
      }
      const backLayout = core.sheetLayout(getCollage().size, sheetKey, { back: true });
      const [sw, sh] = layout.sheet;
      for (let start = 0; start < cards.length; start += layout.count) {
        const batch = cards.slice(start, start + layout.count);
        for (const [which, slots] of [['front', layout], ['back', backLayout]]) {
          if (!batch.some((card) => card[which])) continue;
          const sheet = pdf.addPage([sw * pt, sh * pt]);
          for (const [i, card] of batch.entries()) {
            if (!card[which]) continue;
            const slot = slots.cards[i];
            sheet.drawImage(await imageOf(card[which]), { x: slot.x * pt, y: (sh - slot.y - slot.h) * pt, width: slot.w * pt, height: slot.h * pt });
          }
          drawCutMarks(sheet, layout, sh);
        }
      }
    }

    /** The sheet chosen for a card's PDF (PR 9), or null for each card on its own page. */
    function sheetBoxChoice() {
      const box = $('[data-save-sheet]');
      if (!box || box.hidden) return null;
      const value = chosen('sheet');
      return core.SHEETS[value] ? value : null;
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
        const sides = all.length > 1 ? (chosen('sides') || 'both') : 'front';
        const pages = all.length > 1 ? (sides === 'front' ? [all[0]] : sides === 'back' ? [all[1]] : all) : all;
        const names = all.length > 1 ? (sides === 'both' ? ['front', 'back'] : [sides]) : [''];
        // One card for each guest (PR 10): the front once a guest, their name on it, then the back.
        const guests = format === 'pdf' && !$('[data-save-guests]')?.hidden && chosen('guests') === 'each' ? (getCollage().card?.guests || []) : [];
        // The name goes on the side the design puts the guest's name (a folded card's inside).
        // The side that holds a guest field (one the user locked stays where it is), else the
        // side the design puts it on.
        const holdsGuest = (page) => Boolean(page?.items.some((item) => item.field === 'guest'));
        const guestSide = holdsGuest(all[0]) ? 0 : holdsGuest(all[1]) ? 1 : designs?.sideOf(getCollage(), 'guest') === 'back' ? 1 : 0;
        const chosenPage = (page) => pages.includes(page);
        const cards = guests.length ? guests.map((name) => {
          const named = namedPage(all[guestSide], name);
          const front = guestSide === 0 ? named : all[0];
          const back = guestSide === 1 ? named : (all[1] || null);
          return { front: chosenPage(all[0]) ? front : null, back: all[1] && chosenPage(all[1]) ? back : null };
        }) : null;
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
          await guestPdf(pdf, cards, fits, pdfSheet);
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
            const sheetKey = sheetBoxChoice();
            const layout = sheetKey ? core.sheetLayout(getCollage().size, sheetKey, { back: names[index] === 'back' }) : null;
            if (layout?.count) {
              // As many as fit, with cut marks in the gaps; mm to PDF points, from the bottom.
              const pt = 72 / 25.4;
              const [sw, sh] = layout.sheet;
              const sheet = pdf.addPage([sw * pt, sh * pt]);
              for (const slot of layout.cards) {
                sheet.drawImage(image, { x: slot.x * pt, y: (sh - slot.y - slot.h) * pt, width: slot.w * pt, height: slot.h * pt });
              }
              drawCutMarks(sheet, layout, sh);
            } else {
              const page = core.pdfPage(getCollage().size);
              pdf.addPage([page.width, page.height]).drawImage(image, { x: 0, y: 0, width: page.width, height: page.height });
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
        const parts = [saved, pageNote, `${width.toLocaleString('en-IN')} × ${height.toLocaleString('en-IN')} pixels`, sizeText].filter(Boolean);
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
      } catch {
        saveResult.textContent = 'The collage could not be saved. Try Screen quality, or close other tabs and try again.';
      } finally {
        // A failed save lets go of its picture too.
        renderer.release(canvas);
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

  return Object.freeze({ createSaveDialog, SAVE_PURPOSES, purposeFor, saveSummary, leftOutLine, wrapFocus, tabStops });
});
