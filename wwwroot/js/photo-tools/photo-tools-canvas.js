// PRISM Photo Tools — browser helpers shared by the tools on the page: opening a photo,
// drawing it at a new size, turning a canvas into file bytes, saving a file and showing an
// error. Nothing here talks to the server.
(() => {
  'use strict';

  const core = window.PrismPhotoTools;
  const errorBox = document.querySelector('[data-error]');
  if (!core || !errorBox) {
    return;
  }

  function showError(message) {
    errorBox.textContent = message;
    errorBox.hidden = false;
    errorBox.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  function clearError() {
    errorBox.hidden = true;
    errorBox.textContent = '';
  }

  function saveBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }


  /** Opens a photo the right way up (phone photos carry their rotation as hidden data). */
  async function openPhoto(file) {
    try {
      return await createImageBitmap(file, { imageOrientation: 'from-image' });
    } catch {
      // Older browsers: decode through an <img>, which also honours the rotation.
    }
    const url = URL.createObjectURL(file);
    try {
      const image = new Image();
      image.src = url;
      await image.decode();
      return image;
    } catch {
      throw new core.PhotoToolError(
        'unreadable',
        'This file could not be opened as a photo. JPG, PNG and WEBP are supported; iPhone HEIC photos are not.');
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  function sizeOf(source) {
    return { width: source.naturalWidth || source.width, height: source.naturalHeight || source.height };
  }

  function newCanvas(width, height) {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return canvas;
  }

  /**
   * Draws a photo at a new size. Big reductions go down in halves first, which keeps fine
   * detail much better than one large jump.
   */
  function drawPhoto(source, width, height, options) {
    let current = source;
    let size = sizeOf(source);
    while (size.width / 2 > width && size.height / 2 > height) {
      const half = newCanvas(Math.ceil(size.width / 2), Math.ceil(size.height / 2));
      const context = half.getContext('2d');
      context.imageSmoothingQuality = 'high';
      context.drawImage(current, 0, 0, half.width, half.height);
      current = half;
      size = { width: half.width, height: half.height };
    }

    const canvas = newCanvas(width, height);
    const context = canvas.getContext('2d', { willReadFrequently: options.greyscale });
    if (options.opaque) {
      // JPG has no transparency; without this, see-through areas would turn black.
      context.fillStyle = '#fff';
      context.fillRect(0, 0, width, height);
    }
    context.imageSmoothingQuality = 'high';
    context.drawImage(current, 0, 0, width, height);
    if (options.greyscale) {
      const pixels = context.getImageData(0, 0, width, height);
      core.toGreyscale(pixels.data);
      context.putImageData(pixels, 0, 0);
    }
    return canvas;
  }

  function canvasToBytes(canvas, type, quality) {
    return new Promise((resolve, reject) => {
      canvas.toBlob(async (blob) => {
        if (!blob) {
          reject(new core.PhotoToolError('render', 'This photo is too large for this browser to save. Choose smaller dimensions.'));
          return;
        }
        if (blob.type !== type.mime) {
          reject(new core.PhotoToolError('format', `This browser cannot save ${type.label} files. Choose JPG or PNG.`));
          return;
        }
        try {
          resolve(new Uint8Array(await blob.arrayBuffer()));
        } catch {
          reject(new core.PhotoToolError('render', 'This photo is too large for this browser to save. Choose smaller dimensions.'));
        }
      }, type.mime, type.lossy ? quality : undefined);
    });
  }

  window.PrismPhotoCanvas = Object.freeze({
    showError, clearError, saveBlob, openPhoto, sizeOf, newCanvas, drawPhoto, canvasToBytes
  });
})();
