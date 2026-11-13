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

  /** Draws all of an image, or only `area` of it ({ x, y, width, height }), into width x height. */
  function drawInto(context, image, area, width, height) {
    if (area) {
      context.drawImage(image, area.x, area.y, area.width, area.height, 0, 0, width, height);
    } else {
      context.drawImage(image, 0, 0, width, height);
    }
  }

  /**
   * Draws a photo at a new size. Big reductions go down in halves first, which keeps fine
   * detail much better than one large jump. `options.area` takes only that part of the photo,
   * read straight from it, so no full-size copy of the part is made first.
   */
  function drawPhoto(source, width, height, options) {
    let current = source;
    let area = options.area || null;
    let size = area ? { width: area.width, height: area.height } : sizeOf(source);
    while (size.width / 2 > width && size.height / 2 > height) {
      const half = newCanvas(Math.ceil(size.width / 2), Math.ceil(size.height / 2));
      const context = half.getContext('2d');
      context.imageSmoothingQuality = 'high';
      drawInto(context, current, area, half.width, half.height);
      current = half;
      area = null;
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
    drawInto(context, current, area, width, height);
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

  /**
   * True for the pointer that should draw or drag: the first finger or pen, or the left mouse
   * button. A second finger and a right or middle click are left alone.
   */
  function isMainPointer(event) {
    return event.isPrimary && (event.pointerType !== 'mouse' || event.button === 0);
  }

  /**
   * True for the second and later clicks of a double click, so a double-clicked Download saves
   * one file, not two. A key press counts as a first click.
   */
  function isRepeatClick(event) {
    return Boolean(event) && event.detail > 1;
  }

  const UNSUPPORTED = 'Only JPG, PNG and WEBP photos can be used here. iPhone HEIC photos are not supported.';

  /**
   * Opens photos for one tool, where only the photo chosen last counts. open(file) gives the
   * photo, or null when the file is not a JPG, PNG or WEBP or could not be opened (the error is
   * shown) or when a later choice took over while it opened (it is closed at once). cancel()
   * is for a photo handed over another way, so one still opening does not replace it.
   */
  function latestPhotoOpener() {
    let run = 0;
    return Object.freeze({
      async open(file) {
        clearError();
        if (!file) return null;
        if (!core.isSupportedPhoto(file.type, file.name)) {
          showError(UNSUPPORTED);
          return null;
        }
        const mine = ++run;
        let photo;
        try {
          photo = await openPhoto(file);
        } catch (error) {
          if (mine === run) {
            showError(error instanceof core.PhotoToolError ? error.message : 'This photo could not be opened.');
          }
          return null;
        }
        if (mine !== run) {
          if (typeof photo.close === 'function') photo.close();
          return null;
        }
        return photo;
      },
      cancel() {
        run += 1;
      }
    });
  }

  /** Drop areas: they light up while files are dragged over and hand the dropped files on. */
  function wireDropZones(zones, onFiles) {
    for (const zone of zones) {
      zone.addEventListener('dragover', (event) => {
        event.preventDefault();
        zone.classList.add('is-dragging');
      });
      zone.addEventListener('dragleave', () => zone.classList.remove('is-dragging'));
      zone.addEventListener('drop', (event) => {
        event.preventDefault();
        zone.classList.remove('is-dragging');
        onFiles(Array.from(event.dataTransfer?.files || []));
      });
    }
  }

  /**
   * A "hold to see the original" button: onHold(true) while it is pressed with a pointer, Space
   * or Enter, onHold(false) when it is let go, left or loses the focus.
   */
  function wireHoldButton(button, onHold) {
    button.addEventListener('pointerdown', () => onHold(true));
    button.addEventListener('keydown', (event) => {
      if (event.key === ' ' || event.key === 'Enter') onHold(true);
    });
    for (const type of ['pointerup', 'pointerleave', 'pointercancel', 'blur', 'keyup']) {
      button.addEventListener(type, () => onHold(false));
    }
  }

  window.PrismPhotoCanvas = Object.freeze({
    showError, clearError, saveBlob, openPhoto, sizeOf, newCanvas, drawPhoto, canvasToBytes,
    isMainPointer, isRepeatClick, latestPhotoOpener, wireDropZones, wireHoldButton
  });
})();
