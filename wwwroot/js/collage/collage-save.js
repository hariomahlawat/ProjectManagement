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
      $('[data-print-note]').textContent = size.mm
        ? `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels, 300 dots per inch on ${size.label} paper.`
        : `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels.`;
      $('[data-pdf-note]').textContent = size.mm
        ? `${size.label} at its true size (${size.note}), ready to print.`
        : `One page shaped like the ${size.label.toLowerCase()} page.`;
      const jpeg = chosen('format') === 'jpeg';
      targetRow.hidden = !jpeg;
      targetValue.disabled = !targetOn.checked;
      targetUnit.disabled = !targetOn.checked;
      const empty = getCollage().items.filter((item) => item.kind === 'slot').length;
      const hidden = getCollage().items.filter((item) => item.kind !== 'slot' && item.hidden).length;
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
     * The card's front with a guest's name in the guest field (PR 10): the field's words, or,
     * for a design that leaves a line to write on, the field its design has for the name.
     * Null when the front is not part of what is saved.
     */
    function namedFront(front, name) {
      if (!front) return null;
      if (front.items.some((item) => item.field === 'guest')) {
        return { ...front, items: front.items.map((item) => (item.field === 'guest' ? nameInField(item, name) : item)) };
      }
      let n = 0;
      const made = designs?.fieldItem({ ...getCollage(), items: front.items }, 'guest', { ...designs.detailsOf(getCollage()), guest: name }, { measure: measureText, newId: () => `guest${++n}` });
      return made ? { ...front, items: [...front.items, made] } : front;
    }

    /**
     * The guest field with a guest's name, laid out again, keeping the edge it is aligned to: a
     * left-aligned field's left edge stays put as the name grows, a right-aligned one's right edge.
     */
    function nameInField(field, name) {
      const layout = core.layoutText({ ...field, text: name }, measureText);
      const named = { ...field, text: name, width: layout.width, aspect: layout.width / layout.height };
      if (field.align === 'left') named.x = field.x - field.width / 2 + layout.width / 2;
      if (field.align === 'right') named.x = field.x + field.width / 2 - layout.width / 2;
      return named;
    }

    /**
     * Guest cards into the PDF: each on its own page (with the back after it), or filling sheets.
     * `fronts` has one entry a guest; an entry is null when only the back is saved, so the backs
     * still come one a guest, on as many pages or sheet places as the fronts would take.
     */
    async function guestPdf(pdf, fronts, backPage, fits, sheetKey) {
      const pt = 72 / 25.4;
      const drawImage = async (page) => {
        const drawn = await renderer.drawCollage(page, photos, fits.width, fits.height);
        saveLeftOut += drawn.leftOutPhotos;
        saveStickersLeftOut += drawn.leftOutStickers;
        try {
          return await pdf.embedJpg(await renderer.toBytes(drawn.canvas, 'image/jpeg', 0.9));
        } finally {
          renderer.release(drawn.canvas);
        }
      };
      const backImage = backPage ? await drawImage(backPage) : null;
      const layout = sheetKey ? core.sheetLayout(getCollage().size, sheetKey) : null;
      if (!layout?.count) {
        const size = core.pdfPage(getCollage().size);
        for (const page of fronts) {
          if (page) pdf.addPage([size.width, size.height]).drawImage(await drawImage(page), { x: 0, y: 0, width: size.width, height: size.height });
          if (backImage) pdf.addPage([size.width, size.height]).drawImage(backImage, { x: 0, y: 0, width: size.width, height: size.height });
        }
        return;
      }
      const backLayout = core.sheetLayout(getCollage().size, sheetKey, { back: true });
      const grey = window.PDFLib.rgb(0.35, 0.35, 0.35);
      const [sw, sh] = layout.sheet;
      const marks = (sheet) => {
        for (const [x1, y1, x2, y2] of core.cutMarks(layout)) {
          sheet.drawLine({ start: { x: x1 * pt, y: (sh - y1) * pt }, end: { x: x2 * pt, y: (sh - y2) * pt }, thickness: 0.4, color: grey });
        }
      };
      for (let start = 0; start < fronts.length; start += layout.count) {
        const batch = fronts.slice(start, start + layout.count);
        if (batch.some(Boolean)) {
          const sheet = pdf.addPage([sw * pt, sh * pt]);
          for (const [i, page] of batch.entries()) {
            const slot = layout.cards[i];
            if (page) sheet.drawImage(await drawImage(page), { x: slot.x * pt, y: (sh - slot.y - slot.h) * pt, width: slot.w * pt, height: slot.h * pt });
          }
          marks(sheet);
        }
        if (backImage) {
          const backSheet = pdf.addPage([sw * pt, sh * pt]);
          for (const i of batch.keys()) {
            const slot = backLayout.cards[i];
            backSheet.drawImage(backImage, { x: slot.x * pt, y: (sh - slot.y - slot.h) * pt, width: slot.w * pt, height: slot.h * pt });
          }
          marks(backSheet);
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
        // Backs only still come one a guest, so they match the fronts printed apart.
        const frontSaved = pages[0] === all[0];
        const fronts = guests.length && (frontSaved || pages.length) ? guests.map((name) => (frontSaved ? namedFront(all[0], name) : null)) : null;
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
        if (fronts && fronts.length) {
          // Each guest's card drawn and placed one at a time, so memory stays at one card.
          const backPage = pages.find((page) => page !== all[0]) || null;
          await guestPdf(pdf, fronts, backPage, fits, pdfSheet);
          width = fits.width;
          height = fits.height;
        }
        for (const [index, collagePage] of (fronts && fronts.length ? [] : pages).entries()) {
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
              const grey = window.PDFLib.rgb(0.35, 0.35, 0.35);
              for (const [x1, y1, x2, y2] of core.cutMarks(layout)) {
                sheet.drawLine({ start: { x: x1 * pt, y: (sh - y1) * pt }, end: { x: x2 * pt, y: (sh - y2) * pt }, thickness: 0.4, color: grey });
              }
            } else {
              const page = core.pdfPage(getCollage().size);
              pdf.addPage([page.width, page.height]).drawImage(image, { x: 0, y: 0, width: page.width, height: page.height });
            }
          }
          renderer.release(canvas);
          canvas = null;
        }
        if (pdf) files.push({ bytes: await pdf.save(), type: 'application/pdf', name: `${stem}.pdf` });
        for (const file of files) download(file.bytes, file.type, file.name);
        const total = files.reduce((sum, file) => sum + file.bytes.length, 0);
        const sizeText = photoTools ? photoTools.formatBytes(total) : `${Math.ceil(total / 1000)} KB`;
        const saved = files.length === 1 ? `Saved “${files[0].name}”` : `Saved ${files.map((file) => `“${file.name}”`).join(' and ')}`;
        const pageCount = pdf ? (await window.PDFLib.PDFDocument.load(files.at(-1).bytes)).getPageCount() : 0;
        const pageNote = fronts && fronts.length ? `${fronts.length} guest cards on ${pageCount} page${pageCount === 1 ? '' : 's'}` : pdf && pages.length > 1 ? `${pages.length} pages` : null;
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
