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

  // ---------------------------------------------------------------------------
  // A tool that could not start says so, instead of showing an empty screen. The usual cause is
  // a page and scripts from different versions of PRISM: an update part-way through, or a
  // browser holding an old copy. Only errors while the page is loading count, and only from a
  // Photo Tools script: an error later, while a tool is used, is the tool's own to report.
  // ---------------------------------------------------------------------------

  // Which screens each script starts. The shell (app) starts the page itself and Reduce size.
  const SCRIPT_VIEWS = Object.freeze({
    'photo-tools-app.js': ['home', 'reduce', 'passport', 'sheet', 'idcard', 'edit', 'remove'],
    'photo-tools-passport.js': ['passport'],
    'photo-tools-print.js': ['sheet'],
    'photo-tools-idcard.js': ['idcard'],
    'photo-tools-editor.js': ['edit'],
    'photo-tools-background.js': ['remove']
  });
  const COULD_NOT_START = 'This tool could not start. Reload the page (Ctrl+F5). If it happens again, PRISM may be part-way through an update: try again in a few minutes, or tell your PRISM administrator.';

  /**
   * The screens a failed script leaves without a working tool, from the error's file name and its
   * stack: a screen's script that fails inside a shared helper (this file) is named only further
   * down the stack.
   */
  function viewsForFailedScript(fileName, stack = '') {
    const views = new Set();
    const text = `${fileName || ''}\n${stack || ''}`;
    for (const match of text.matchAll(/\/js\/photo-tools\/(photo-tools-[a-z]+\.js)/g)) {
      for (const view of SCRIPT_VIEWS[match[1]] || []) views.add(view);
    }
    return [...views];
  }

  function markCouldNotStart(views) {
    for (const name of views) {
      const view = document.querySelector(`[data-view="${name}"]`);
      if (!view || view.querySelector('[data-could-not-start]')) continue;
      const note = document.createElement('div');
      note.className = 'alert alert-danger';
      note.setAttribute('role', 'alert');
      note.dataset.couldNotStart = '';
      note.textContent = COULD_NOT_START;
      // Under the screen's heading row, above the tool.
      const head = view.querySelector('.phot-head');
      if (head) head.after(note); else view.prepend(note);
    }
  }

  if (typeof window.addEventListener === 'function') {
    window.addEventListener('error', (event) => {
      if (document.readyState === 'complete') return;
      markCouldNotStart(viewsForFailedScript(event && event.filename, event && event.error && event.error.stack));
    });
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

  /**
   * Draws a photo at an exact size from a plan made by core.exactFit: the part of the photo in
   * plan.area goes to plan.place, and any space left round it is white.
   */
  function drawExact(source, plan, options) {
    const { place } = plan;
    const filled = place.x === 0 && place.y === 0 && place.width === plan.width && place.height === plan.height;
    if (filled) return drawPhoto(source, plan.width, plan.height, { ...options, area: plan.area });
    const inner = drawPhoto(source, place.width, place.height, { ...options, area: plan.area });
    const canvas = newCanvas(plan.width, plan.height);
    const context = canvas.getContext('2d');
    context.fillStyle = '#fff';
    context.fillRect(0, 0, plan.width, plan.height);
    context.drawImage(inner, place.x, place.y);
    return canvas;
  }

  const WATERMARK_FONT = 'Mulish, "Segoe UI", Arial, sans-serif';

  /**
   * Draws a watermark on a canvas, in place: white letters with a soft dark edge, so the text
   * reads on light and dark photos alike. mark = { text, place, strength }; empty text adds
   * nothing.
   */
  function drawWatermark(canvas, mark) {
    const text = String(mark?.text || '').trim().slice(0, core.WATERMARK_MAX_LENGTH);
    if (!text) return canvas;
    const context = canvas.getContext('2d');
    context.save();
    context.font = `700 100px ${WATERMARK_FONT}`;
    const plan = core.watermarkPlan({
      width: canvas.width,
      height: canvas.height,
      place: mark.place,
      textWidth100: context.measureText(text).width
    });
    context.font = `700 ${plan.fontSize}px ${WATERMARK_FONT}`;
    context.textAlign = plan.align;
    context.textBaseline = plan.baseline;
    context.globalAlpha = core.WATERMARK_STRENGTH[mark.strength] || core.WATERMARK_STRENGTH.light;
    context.fillStyle = '#fff';
    context.shadowColor = 'rgba(0, 0, 0, 0.65)';
    context.shadowBlur = Math.max(2, plan.fontSize * 0.12);
    if (plan.angle) {
      context.translate(plan.x, plan.y);
      context.rotate(plan.angle);
      const rows = Math.ceil(plan.reach / plan.stepY);
      const columns = Math.ceil(plan.reach / plan.stepX) + 1;
      for (let row = -rows; row <= rows; row += 1) {
        // Every other row moves half a step, so the marks do not line up in columns.
        const shift = (row % 2) * plan.stepX / 2;
        for (let column = -columns; column <= columns; column += 1) {
          context.fillText(text, column * plan.stepX + shift, row * plan.stepY);
        }
      }
    } else {
      context.fillText(text, plan.x, plan.y);
    }
    context.restore();
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

  /**
   * Choices a screen keeps between visits, in this browser only (local storage): never the photos,
   * never sent anywhere. Reading or writing never throws: a private window or a full store simply
   * means nothing is remembered.
   */
  function rememberedChoices(name) {
    const key = `prism.photoTools.${name}`;
    return Object.freeze({
      load() {
        try {
          const value = JSON.parse(window.localStorage.getItem(key) || 'null');
          return value && typeof value === 'object' ? value : null;
        } catch {
          return null;
        }
      },
      save(value) {
        try {
          window.localStorage.setItem(key, JSON.stringify(value));
        } catch {
          // Storage is a convenience; nothing depends on it.
        }
      }
    });
  }

  // Work that would be lost by leaving the page: each screen says when it has some (moved,
  // painted or edited since its last download), and the browser then asks before the page is left,
  // reloaded or closed. Moving between the tools inside the page is not leaving it.
  const unsaved = new Map();

  function setUnsaved(screen, value) {
    unsaved.set(screen, Boolean(value));
  }

  if (typeof window.addEventListener === 'function') {
    window.addEventListener('beforeunload', (event) => {
      if (![...unsaved.values()].some(Boolean)) return;
      event.preventDefault();
      // Older browsers need a value here to ask; the text itself is not shown.
      event.returnValue = '';
    });
  }

  /**
   * The Save bar at the foot of a screen: a copy of the screen's Download, shown while the real
   * button is out of view (long settings on a laptop, or a phone, where the photo and Download
   * are a screen or more apart) and there is something to download. It hides while the real
   * button can be seen, so there is never a second Download in sight. `line`, when given, is the "Ready: …" line whose words the bar repeats;
   * `hide()` keeps the bar away (no photo yet); `render()` adds a screen's own parts. Returns the
   * function that brings the bar up to date.
   */
  function wireSaveBar(bar, button, { line = null, hide = () => false, render = () => {} } = {}) {
    const copy = bar.querySelector('[data-save-bar-download]');
    const text = bar.querySelector('[data-save-bar-text]');
    let buttonInView = false;

    function update() {
      bar.hidden = hide() || buttonInView || button.disabled;
      copy.disabled = button.disabled;
      if (text && line) text.textContent = line.textContent;
      render();
    }

    if (typeof IntersectionObserver === 'function') {
      // Seen in full: a button half under the bar counts as out of view, so the bar shows.
      new IntersectionObserver((entries) => {
        buttonInView = entries[entries.length - 1].intersectionRatio >= 0.99;
        update();
      }, { threshold: [0, 0.99, 1] }).observe(button);
    }
    new MutationObserver(update).observe(button, { attributes: true, attributeFilter: ['disabled'] });
    if (line) new MutationObserver(update).observe(line, { childList: true, characterData: true, subtree: true });
    copy.addEventListener('click', (event) => {
      if (isRepeatClick(event)) return;
      button.click();
    });
    update();
    return update;
  }

  // ---------------------------------------------------------------------------
  // Photos given to a screen other than by its Choose button or drop area: pasted with Ctrl+V.
  // Each screen says which function takes its photos (the one its drop area uses), so a pasted
  // photo goes through the same checks as a chosen one. One listener serves the whole page.
  // ---------------------------------------------------------------------------

  const receivers = new Map();
  // Screens that open another screen in a set way (VIEW_ALIASES in photo-tools-app.js).
  const SCREEN_OF = Object.freeze({ signature: 'passport' });
  const NOTHING_PASTED = 'There is no photo on the clipboard to paste. Copy a photo, or take a screenshot (Windows key + Shift + S), then press Ctrl+V here.';

  /** `take(files)` gets the photos given to `screen` (a data-view name), as a list of files. */
  function receivePhotos(screen, take) {
    receivers.set(screen, take);
  }

  function shownScreen() {
    const name = window.location.hash.replace(/^#/, '') || 'home';
    return SCREEN_OF[name] || name;
  }

  /**
   * The name a pasted file is saved under later. A copied file keeps its own name; a screenshot
   * or an image copied from a page comes as "image.png" or with no name, and gets a plain one.
   */
  function pastedName(name, type, index, count) {
    if (name && !/^image\.[a-z]+$/i.test(name)) return name;
    const extension = { 'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp' }[type] || 'png';
    return count > 1 ? `pasted-photo-${index + 1}.${extension}` : `pasted-photo.${extension}`;
  }

  /** The files on a clipboard, named. Whether each is a photo the screen can use is the screen's check. */
  function pastedFiles(clipboard) {
    if (!clipboard) return [];
    let files = Array.from(clipboard.items || [])
      .filter(item => item.kind === 'file')
      .map(item => item.getAsFile())
      .filter(Boolean);
    // Some browsers fill only the file list.
    if (!files.length) files = Array.from(clipboard.files || []);
    return files.map((file, index) => {
      const name = pastedName(file.name, file.type, index, files.length);
      return name === file.name ? file : new File([file], name, { type: file.type, lastModified: Date.now() });
    });
  }

  /** A paste into something that takes text: a size, a mark's text. That paste is the field's own. */
  function isTextField(target) {
    return Boolean(target && typeof target.closest === 'function' && target.closest(
      'textarea, select, [contenteditable=""], [contenteditable="true"], '
      + 'input:not([type="range"]):not([type="checkbox"]):not([type="radio"]):not([type="button"]):not([type="file"]):not([type="color"])'));
  }

  if (typeof document.addEventListener === 'function') {
    document.addEventListener('paste', (event) => {
      if (isTextField(event.target)) return;
      const take = receivers.get(shownScreen());
      if (!take) return;
      event.preventDefault();
      const files = pastedFiles(event.clipboardData);
      if (!files.length) {
        showError(NOTHING_PASTED);
        return;
      }
      take(files);
    });
  }

  // ---------------------------------------------------------------------------
  // Hand-offs: a finished photo goes on to another tool without being downloaded and chosen again.
  // ---------------------------------------------------------------------------

  /**
   * Shows `screen` and gives it `file`, as if it had been chosen there. The photo is handed over
   * once the screen is shown, after the page has switched to it (a screen with two modes, such as
   * Passport photo and Signature, picks its mode from the address first). False when the screen
   * takes no photos.
   */
  function sendPhoto(screen, file) {
    const take = receivers.get(screen);
    if (!take) return false;
    if (window.location.hash === `#${screen}`) {
      take([file]);
      return true;
    }
    window.addEventListener('hashchange', () => take([file]), { once: true });
    window.location.hash = screen;
    return true;
  }

  /**
   * The "Continue in" buttons under a screen's Download (`[data-send-to]` in `host`). They are on
   * while Download is, and a press calls `send(screen, label)` to make the photo Download would
   * save and hand it on with sendPhoto.
   */
  function wireSendButtons(host, downloadButton, send) {
    const buttons = Array.from(host.querySelectorAll('[data-send-to]'));
    const update = () => {
      for (const button of buttons) button.disabled = downloadButton.disabled;
    };
    new MutationObserver(update).observe(downloadButton, { attributes: true, attributeFilter: ['disabled'] });
    for (const button of buttons) {
      button.addEventListener('click', (event) => {
        if (button.disabled || isRepeatClick(event)) return;
        send(button.dataset.sendTo, button.textContent.trim());
      });
    }
    update();
  }

  window.PrismPhotoCanvas = Object.freeze({
    showError, clearError, saveBlob, openPhoto, sizeOf, newCanvas, drawPhoto, drawExact, drawWatermark, canvasToBytes,
    isMainPointer, isRepeatClick, latestPhotoOpener, wireDropZones, wireHoldButton, rememberedChoices,
    setUnsaved, wireSaveBar, viewsForFailedScript, markCouldNotStart, receivePhotos, pastedName,
    sendPhoto, wireSendButtons
  });
})();
