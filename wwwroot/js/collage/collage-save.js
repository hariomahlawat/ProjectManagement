// PRISM Collage Studio — Save as…: the dialog (what it is for, the type, the quality, a size to
// keep a JPG under), then the picture made from the original photos and downloaded (PNG, JPG or
// a PDF at the paper's real size). Moved out of the editor in docs/ux/collage-review-fixes.md,
// PR 13. Nothing is sent anywhere: the file is made in the browser and downloaded.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageSave = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

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
    }

    function openSave() {
      if (!hasContent()) {
        showAlert('Add a photo, some text or a sticker first; there is nothing to save yet.');
        return;
      }
      flushNudge();
      stopAdjust();
      saveResult.hidden = true;
      syncSaveDialog();
      saveDialog.showModal();
    }

    saveDialog.addEventListener('change', syncSaveDialog);

    // "What is it for?" chooses a type and quality that suit it; the user can still change them.
    const SAVE_PURPOSES = Object.freeze({
      share: { format: 'jpeg', quality: 'screen' },
      print: { format: 'pdf', quality: 'print' },
      slide: { format: 'png', quality: 'screen' }
    });
    saveDialog.addEventListener('click', (event) => {
      const button = event.target.closest('[data-save-purpose]');
      const choice = button && SAVE_PURPOSES[button.dataset.savePurpose];
      if (!choice) return;
      for (const [name, value] of Object.entries(choice)) {
        const radio = saveDialog.querySelector(`input[name="${name}"][value="${value}"]`);
        if (radio) radio.checked = true;
      }
      saveDialog.querySelectorAll('[data-save-purpose]').forEach((other) => other.setAttribute('aria-pressed', String(other === button)));
      syncSaveDialog();
      announce(`${saveDialog.querySelector('input[name="format"]:checked')?.closest('label')?.querySelector('b')?.textContent || ''}, ${choice.quality === 'print' ? 'print quality' : 'for the screen'}.`);
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
      try {
        const wanted = core.savePixels(getCollage().size, quality);
        const fits = renderer.largestCanvas(wanted.width, wanted.height);
        if (!fits) throw new Error('canvas');
        const drawn = await renderer.drawCollage(getCollage(), photos, fits.width, fits.height);
        const canvas = drawn.canvas;
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
              encode: (w, h, q) => renderer.toBytes(renderer.scaledCanvas(canvas, w, h), type, q),
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
        canvas.width = 0;
        canvas.height = 0;
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

  return Object.freeze({ createSaveDialog });
});
