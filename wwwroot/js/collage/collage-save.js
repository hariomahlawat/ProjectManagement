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
    getCollage, photos, announce, showAlert, collageTitle, hasContent, stopAdjust, flushNudge
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
      const size = core.SIZES[getCollage().size];
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
      const emptyLine = $('[data-save-empty]');
      emptyLine.hidden = !empty;
      emptyLine.textContent = empty === 1
        ? 'The empty frame is left out of the saved picture.'
        : `The ${empty} empty frames are left out of the saved picture.`;
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
        const drawn = await renderer.drawCollage(getCollage(), photos, fits.width, fits.height);
        canvas = drawn.canvas;
        saveLeftOut = drawn.leftOutPhotos;
        saveStickersLeftOut = drawn.leftOutStickers;
        const stem = core.fileStem(collageTitle());
        let bytes;
        let width = canvas.width;
        let height = canvas.height;
        let missed = false;
        let type;
        let name;
        if (format === 'png') {
          type = 'image/png';
          name = `${stem}.png`;
          bytes = await renderer.toBytes(canvas, type);
        } else if (format === 'jpeg') {
          type = 'image/jpeg';
          name = `${stem}.jpg`;
          if (targetBytes && photoTools) {
            const result = await photoTools.encodeToTarget({
              encode: (w, h, q) => renderer.scaledBytes(canvas, w, h, type, q),
              lossy: true,
              width,
              height,
              targetBytes
            });
            bytes = result.bytes;
            width = result.width;
            height = result.height;
            missed = !result.reached;
          } else {
            bytes = await renderer.toBytes(canvas, type, 0.92);
          }
        } else {
          type = 'application/pdf';
          name = `${stem}.pdf`;
          const page = core.pdfPage(getCollage().size);
          const jpeg = await renderer.toBytes(canvas, 'image/jpeg', 0.92);
          const pdf = await window.PDFLib.PDFDocument.create();
          pdf.setTitle(collageTitle());
          pdf.setCreator('PRISM Collage Studio');
          const image = await pdf.embedJpg(jpeg);
          pdf.addPage([page.width, page.height]).drawImage(image, { x: 0, y: 0, width: page.width, height: page.height });
          bytes = await pdf.save();
        }
        renderer.release(canvas);
        canvas = null;
        download(bytes, type, name);
        const sizeText = photoTools ? photoTools.formatBytes(bytes.length) : `${Math.ceil(bytes.length / 1000)} KB`;
        const parts = [`Saved “${name}”`, `${width.toLocaleString('en-IN')} × ${height.toLocaleString('en-IN')} pixels`, sizeText];
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

  return Object.freeze({ createSaveDialog, SAVE_PURPOSES, purposeFor, saveSummary, wrapFocus, tabStops });
});
