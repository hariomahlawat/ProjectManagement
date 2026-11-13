// PRISM PDF Tools — Reduce size.
//
// The rules (what to change, how far, what to leave) are in pdf-tools-core.js (`reducePdf`) and
// tested in Node. This file is the screen and the one part that needs the browser: decoding an
// image and re-saving it smaller with a canvas. Nothing leaves the browser.
// Plan: docs/ux/pdf-tools-reduce-size.md.
(() => {
  'use strict';

  const ui = window.PrismPdfToolsUi;
  const core = window.PrismPdfTools;
  const PDFLib = window.PDFLib;
  const section = document.querySelector('[data-tool="compress"]');
  if (!ui || !core || !PDFLib || !section || !core.reducePdf) {
    return;
  }

  const { $ } = ui;
  const about = $('[data-compress-about]', section);
  const outcome = $('[data-compress-outcome]', section);
  const presetsBox = $('[data-compress-presets]', section);
  // Below this share of the file, pictures are not what makes it large.
  const MOSTLY_TEXT_SHARE = 0.25;

  // The presets come from the core, so the screen and the rules cannot disagree.
  for (const preset of core.PDF_SIZE_PRESETS) {
    const label = document.createElement('label');
    label.className = 'pdft-preset';
    const input = document.createElement('input');
    input.type = 'radio';
    input.name = 'compressPreset';
    input.value = preset.key;
    input.className = 'pdft-preset__input';
    input.checked = preset.key === 'standard';
    const title = document.createElement('span');
    title.className = 'pdft-preset__title';
    title.textContent = preset.label;
    const note = document.createElement('span');
    note.className = 'pdft-preset__note';
    note.textContent = `${preset.note} · pictures at ${preset.dpi} dpi`;
    label.append(input, title, note);
    presetsBox.append(label);
  }

  let current = null;

  function say(element, text) {
    element.textContent = text || '';
    element.hidden = !text;
  }

  /** What the summary says before any work, so a text-only file does not promise a saving. */
  function describe(info) {
    const share = info.bytes ? info.imageBytes / info.bytes : 0;
    if (!info.imageCount || share < MOSTLY_TEXT_SHARE) {
      return 'This PDF is mostly text, so it may not get much smaller. Repeated fonts and unused parts can still be removed.';
    }
    return `Pictures are ${ui.formatBytes(info.imageBytes)} of this ${ui.formatBytes(info.bytes)} file (${info.imageCount} picture${info.imageCount === 1 ? '' : 's'}).`;
  }

  // ---------------------------------------------------------------------------
  // Re-saving one image (the part that needs a canvas)
  // ---------------------------------------------------------------------------

  function canvasOf(width, height) {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return canvas;
  }

  /** Draws a picture at a smaller size, halving first so fine detail is kept. */
  function drawSmaller(source, width, height, targetWidth, targetHeight) {
    let current = source;
    let size = { width, height };
    const made = [];
    while (size.width / 2 > targetWidth && size.height / 2 > targetHeight) {
      const half = canvasOf(Math.ceil(size.width / 2), Math.ceil(size.height / 2));
      const context = half.getContext('2d');
      context.imageSmoothingQuality = 'high';
      context.drawImage(current, 0, 0, half.width, half.height);
      made.push(half);
      current = half;
      size = { width: half.width, height: half.height };
    }
    const target = canvasOf(targetWidth, targetHeight);
    const context = target.getContext('2d', { willReadFrequently: true });
    // JPG has no transparency; a soft mask, if any, stays a separate part of the PDF.
    context.fillStyle = '#fff';
    context.fillRect(0, 0, targetWidth, targetHeight);
    context.imageSmoothingQuality = 'high';
    context.drawImage(current, 0, 0, targetWidth, targetHeight);
    made.forEach((canvas) => { canvas.width = 0; canvas.height = 0; });
    return target;
  }

  /**
   * The encoder `reducePdf` asks for. Returns null when the browser cannot read the image, which
   * leaves it as it is. A JPG is read as stored: a PDF viewer ignores a JPG's own orientation and
   * colour profile, so the browser must not apply them either. Current browsers turn a JPG by its
   * orientation whatever they are asked, so a copy that says "as stored" is decoded instead.
   */
  async function encodeImage(input) {
    let source = null;
    let pixels = null;
    try {
      if (input.source === 'jpeg') {
        const stored = core.jpegClearOrientation(input.bytes);
        source = await createImageBitmap(new Blob([stored], { type: 'image/jpeg' }), {
          colorSpaceConversion: 'none'
        });
        // A size other than the PDF says means the browser read it differently: leave it.
        if (source.width !== input.width || source.height !== input.height) return null;
      } else {
        const rgba = new Uint8ClampedArray(input.width * input.height * 4);
        const { data, channels } = input;
        for (let i = 0, j = 0, k = 0; i < input.width * input.height; i += 1, j += channels, k += 4) {
          rgba[k] = data[j];
          rgba[k + 1] = channels === 1 ? data[j] : data[j + 1];
          rgba[k + 2] = channels === 1 ? data[j] : data[j + 2];
          rgba[k + 3] = 255;
        }
        pixels = canvasOf(input.width, input.height);
        pixels.getContext('2d').putImageData(new ImageData(rgba, input.width, input.height), 0, 0);
        source = pixels;
      }
      const target = drawSmaller(source, input.width, input.height, input.targetWidth, input.targetHeight);
      const jpeg = await ui.canvasToBytes(target, 'image/jpeg', input.quality);
      let grey = null;
      if (input.grey) {
        const data = target.getContext('2d').getImageData(0, 0, input.targetWidth, input.targetHeight).data;
        grey = new Uint8Array(input.targetWidth * input.targetHeight);
        for (let i = 0; i < grey.length; i += 1) grey[i] = data[i * 4];
      }
      target.width = 0;
      target.height = 0;
      return { jpeg, grey };
    } catch {
      // Too large for this browser's canvas, or not readable: the image is left as it is.
      return null;
    } finally {
      if (source && typeof source.close === 'function') source.close();
      if (pixels) { pixels.width = 0; pixels.height = 0; }
    }
  }

  // ---------------------------------------------------------------------------
  // What the result says
  // ---------------------------------------------------------------------------

  const SKIP_WORDS = {
    [core.REDUCE_SKIP.printColour]: ['print-colour picture', 'print-colour pictures'],
    [core.REDUCE_SKIP.blackWhite]: ['black-and-white scan', 'black-and-white scans'],
    [core.REDUCE_SKIP.mask]: ['transparency mask', 'transparency masks'],
    [core.REDUCE_SKIP.small]: ['small picture', 'small pictures'],
    [core.REDUCE_SKIP.other]: ['picture in a format this tool does not change', 'pictures in a format this tool does not change'],
    [core.REDUCE_SKIP.notSmaller]: ['picture already as small as it can be made', 'pictures already as small as they can be made']
  };

  const plural = (count, [one, many]) => `${count} ${count === 1 ? one : many}`;

  function resultDetail(before, after, report) {
    const saved = before - after;
    const parts = [`${ui.formatBytes(before)} → ${ui.formatBytes(after)}, ${Math.round((saved / before) * 100)}% smaller.`];
    const done = [];
    const preset = core.PDF_SIZE_PRESETS.find((item) => item.key === report.preset);
    if (report.resaved) done.push(`${plural(report.resaved, ['picture', 'pictures'])} re-saved${report.resized && preset ? ` (${report.resized} at ${preset.dpi} dpi)` : ''}`);
    if (report.merged) done.push(`${plural(report.merged, ['repeated item', 'repeated items'])} stored once`);
    if (report.compressed) done.push(`${plural(report.compressed, ['part', 'parts'])} compressed`);
    if (report.thumbnails) done.push(`${plural(report.thumbnails, ['page thumbnail', 'page thumbnails'])} left out`);
    if (done.length) parts.push(`${done.join(', ')}.`);
    const left = Object.entries(report.skipped)
      .filter(([reason, count]) => count && SKIP_WORDS[reason])
      .map(([reason, count]) => plural(count, SKIP_WORDS[reason]));
    if (left.length) parts.push(`Left as they were: ${left.join(', ')}.`);
    return parts.join(' ');
  }

  // ---------------------------------------------------------------------------
  // The tool
  // ---------------------------------------------------------------------------

  const tool = ui.createTool(section, {
    accept: ui.isPdf,
    async onFiles([file], state) {
      say(outcome, '');
      await ui.withBusy('Opening PDF…', async () => {
        try {
          const bytes = await ui.readFile(file);
          const info = await core.describePdf(PDFLib, bytes);
          // Refused before any choice is made: changing a signed PDF breaks its signature.
          if (info.signed) throw new core.PdfToolError('signed', core.SIGNED_PDF_MESSAGE);
          current = { file, bytes, info };
          ui.fileSummary(section, file, info.pages);
          say(about, describe(info));
          state.showWorkspace();
        } catch (error) {
          // A file chosen earlier must not stay on the screen under this one's message, ready to
          // be reduced instead of it.
          state.reset();
          throw error;
        }
      });
    },
    onRun(options, state) {
      if (!current) return;
      say(outcome, '');
      const { file, bytes } = current;
      ui.withBusy('Reducing size…', async (progress) => {
        const result = await core.reducePdf(PDFLib, bytes, {
          preset: options.mode,
          encodeImage,
          onProgress: (done, total) => progress(`Re-saving picture ${done} of ${total}…`)
        });
        if (result.unchanged) {
          // Never a bigger (or equal) file offered as a saving, and no new file at all.
          say(outcome, 'This PDF is already as small as this tool can make it, so no new file was made.');
          ui.moveFocus(outcome);
          return;
        }
        await state.finish(result.bytes, `${core.baseName(file.name)}-smaller.pdf`, 'application/pdf', {
          detail: resultDetail(bytes.length, result.bytes.length, result.report),
          warning: result.report.pdfA
            ? 'This file said it was PDF/A. After the change it may no longer meet PDF/A, so check before sending it where PDF/A is required.'
            : ''
        });
      });
    },
    onReset() {
      current = null;
      say(about, '');
      say(outcome, '');
    }
  });
})();
