// PRISM PDF Tools — re-encoding one picture smaller, for Reduce size.
//
// The one part of Reduce size that needs a canvas. It runs on the page and in the background
// worker (pdf-tools-compress-worker.js) alike: an OffscreenCanvas where the browser has one, a
// page canvas otherwise. The rules of what to re-save, and how far, are in pdf-tools-core.js.
(function (root) {
  'use strict';

  const core = root.PrismPdfTools;
  if (!core) {
    return;
  }

  function canvasOf(width, height) {
    if (typeof OffscreenCanvas === 'function') {
      return new OffscreenCanvas(width, height);
    }
    const canvas = root.document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return canvas;
  }

  function release(canvas) {
    if (!canvas) return;
    canvas.width = 0;
    canvas.height = 0;
  }

  async function jpegBytes(canvas, quality) {
    const blob = typeof canvas.convertToBlob === 'function'
      ? await canvas.convertToBlob({ type: 'image/jpeg', quality })
      : await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
    if (!blob || blob.type !== 'image/jpeg') {
      throw new core.PdfToolError('render', 'This picture could not be re-saved.');
    }
    return new Uint8Array(await blob.arrayBuffer());
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
    made.forEach(release);
    return target;
  }

  /**
   * The encoder `reducePdf` asks for. Returns null when the browser cannot read the picture,
   * which leaves it as it is. A JPG is read as stored: a PDF viewer ignores a JPG's own
   * orientation and colour profile, so the browser must not apply them either. Current browsers
   * turn a JPG by its orientation whatever they are asked, so a copy that says "as stored" is
   * decoded instead (`jpegClearOrientation`).
   */
  async function encodeImage(input) {
    let source = null;
    let pixels = null;
    try {
      if (input.source === 'jpeg') {
        const stored = core.jpegClearOrientation(input.bytes);
        source = await createImageBitmap(new Blob([stored], { type: 'image/jpeg' }), { colorSpaceConversion: 'none' });
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
      const jpeg = await jpegBytes(target, input.quality);
      let grey = null;
      if (input.grey) {
        const data = target.getContext('2d').getImageData(0, 0, input.targetWidth, input.targetHeight).data;
        grey = new Uint8Array(input.targetWidth * input.targetHeight);
        for (let i = 0; i < grey.length; i += 1) grey[i] = data[i * 4];
      }
      release(target);
      return { jpeg, grey };
    } catch {
      // Too large for this browser's canvas, or not readable: the picture is left as it is.
      return null;
    } finally {
      if (source && typeof source.close === 'function') source.close();
      release(pixels);
    }
  }

  root.PrismPdfEncode = Object.freeze({ encodeImage });
})(typeof self !== 'undefined' ? self : this);
