// PRISM Photo Tools — Remove background.
//
// A copy of the photo (at most 2048 px) is sent to the PRISM server, whose model returns only a
// mask: how much of each pixel is the subject. Everything else happens in this browser: the old
// background's tint is taken out of the soft edge, the touch-up brushes (Refine edge, Erase,
// Restore) change the mask, and the cut-out is saved on a see-through, plain colour or blurred
// background. The server keeps nothing.
(() => {
  'use strict';

  const core = window.PrismPhotoTools;
  const shared = window.PrismPhotoCanvas;
  const view = document.querySelector('[data-view="remove"]');
  if (!core || !shared || !view) {
    return;
  }

  const STATUS_URL = '/api/photo-tools/background/status';
  const MASK_URL = '/api/photo-tools/background/mask';
  const FACE_URL = '/api/photo-tools/face';
  // The cut-out is worked on at this size; the saved photo is made again at full size.
  const WORK_SIDE = 2048;
  // The largest saved photo, so memory stays reasonable in any browser.
  const SAVE_SIDE = 4096;
  // Blur: the photo with the subject painted out is drawn this small, then enlarged.
  const BLUR_SIDE = 40;
  // Brush sizes (the radius) as a share of the photo's width; [ and ] step through them.
  const BRUSH_SHARES = { small: 0.015, medium: 0.03, large: 0.06 };
  const BRUSH_ORDER = ['small', 'medium', 'large'];
  // Each stroke fades over a share of its radius on each side of its edge: nested see-through
  // strokes around a solid core, as in Passport photo.
  const BRUSH_SOFTNESS = 0.35;
  const BRUSH_RINGS = 6;
  const BRUSH_RING_ALPHA = 0.3;
  const BRUSH_HINTS = {
    refine: 'Drag on the photo to paint along hair, fur or a soft edge. What looks like the old background goes; strands and edges stay.',
    erase: 'Drag on the photo to paint over anything that should not be in the cut-out.',
    restore: 'Drag on the photo to paint where part of the person or object was taken away. It comes back.'
  };
  const JPEG = core.TYPES.jpeg;

  const $ = (selector) => view.querySelector(selector);
  const $$ = (selector) => Array.from(view.querySelectorAll(selector));

  const input = $('[data-remove-input]');
  const statusBox = $('[data-remove-status]');
  const emptyDrop = $('[data-empty]');
  const workspace = $('[data-workspace]');
  const chooseButton = $('[data-action="choose-photo"]');
  const changeButton = $('[data-action="change-photo"]');
  const fileLabel = $('[data-remove-file]');
  const stage = $('[data-remove-stage]');
  const photoBox = $('[data-remove-photo]');
  const canvas = $('[data-remove-canvas]');
  const busyBox = $('[data-remove-busy]');
  const busyText = $('[data-remove-busy-text]');
  const busyStatus = $('[data-remove-busy-status]');
  const sizeLabel = $('[data-remove-size]');
  const undoButton = $('[data-action="remove-undo"]');
  const resetButton = $('[data-action="remove-reset"]');
  const originalButton = $('[data-action="remove-original"]');
  const bgButtons = $$('[data-remove-bg]');
  const bgOther = $('[data-remove-bg-other]');
  const jpegHint = $('[data-remove-jpeg-hint]');
  const bgColour = $('[data-remove-bg-colour]');
  const brushButtons = $$('[data-remove-brush]');
  const brushHint = $('[data-remove-brush-hint]');
  const sizeButtons = $$('[data-remove-size-choice]');
  const format = $('[data-remove-format]');
  const resultLine = $('[data-remove-result]');
  const downloadButton = $('[data-action="download-remove"]');
  const credit = $('[data-remove-credit]');
  const cancelButton = $('[data-action="remove-cancel"]');
  const retryBox = $('[data-remove-retry]');
  const retryText = $('[data-remove-retry-text]');
  const retryButton = $('[data-action="remove-retry"]');
  const readyOnly = $$('[data-remove-ready-only]');
  // The home screen's card, outside this screen: dimmed when the tool is not set up.
  const homeCard = document.querySelector('[data-remove-card]');
  const homeCardNote = document.querySelector('[data-remove-card-note]');

  const FINDING = 'Finding the person or object…';
  const STILL_WORKING = 'Still working. The first photo after the PRISM server starts can take up to a minute.';
  const SLOW_AFTER_MS = 8000;
  // The browser gives up on a mask after this long, in case the connection stalls without an
  // answer: longer than the server's own limits (upload, then the work) so they answer first.
  const MASK_GIVE_UP_MS = 180000;
  const CHECK_AGAIN_MS = 10000;
  const SIGNED_OUT = 'Your PRISM session has ended. Reload the page and sign in again.';
  // States that will pass without anyone doing anything: the card is not dimmed for these.
  const PASSING_STATES = new Set(['checking', 'model-unreadable']);

  let status = null;
  let statusLoading = null;

  // The photo: `full` a copy at most SAVE_SIDE (the largest saved size; a 48 MP original is not
  // kept), `work` the copy the cut-out is made on, `pixels` its RGBA.
  let fileName = '';
  let full = null;
  let work = null;
  let pixels = null;
  // The model's mask, the mask after the touch-ups, and the old background's colours.
  let modelAlpha = null;
  let alpha = null;
  // How much of each pixel's colour is subject: the mask without Erase, which only hides. The
  // edge is cleaned with this, so an erased area keeps its colour as it fades.
  let colourAlpha = null;
  let background = null;
  let subjectColours = null;
  // The cut-out (RGBA, see-through where the mask says) and the blurred background.
  let cutout = null;
  let blurred = null;

  let backdrop = 'transparent';
  // Kept in this browser: the background (see-through, a colour or the blur) and the file type.
  const remembered = shared.rememberedChoices('remove');

  function rememberChoices() {
    remembered.save({ backdrop, format: format.value });
  }
  let brush = 'refine';
  let brushSize = 'medium';
  let strokes = [];
  let stroke = null;
  let ring = null;
  let showOriginal = false;
  let busy = false;
  let dirty = false;
  let pending = null;
  // The cut-out's colours, kept so a stroke remakes only the part it changed.
  let colours = null;
  let openRun = 0;
  let pickRun = 0;
  let maskRequest = null;
  let display = { width: 0, height: 0 };
  let slowTimer = 0;
  let checkTimer = 0;
  let cancelledByUser = false;
  let maskPending = false;
  let maskTimedOut = false;

  // ---------------------------------------------------------------------------
  // Server
  // ---------------------------------------------------------------------------

  function csrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.getAttribute('content') || '';
  }

  /**
   * Whether Remove background is set up on the server. Only a "ready" answer is kept: a failed
   * request, or "not set up", is asked again the next time the screen opens or a photo is chosen,
   * so a server that is back, or a model an administrator has just installed, is found without
   * reloading the page.
   */
  function loadStatus() {
    if (status && status.available) return Promise.resolve(status);
    if (!statusLoading) {
      statusLoading = fetch(STATUS_URL, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
        .then((response) => {
          if (response.status === 401) return { available: false, state: 'signed-out', message: SIGNED_OUT };
          return response.ok ? response.json() : null;
        })
        .catch(() => null)
        .then((answer) => {
          status = answer || {
            available: false,
            message: 'The PRISM server could not be reached. Check the connection and open this screen again.'
          };
          statusLoading = null;
          renderStatus();
          checkAgainSoon();
          return status;
        });
      // Says "checking" until the answer comes.
      renderStatus();
    }
    return statusLoading;
  }

  /** While the server is getting ready, it is asked again every few seconds the screen is open. */
  function checkAgainSoon() {
    clearTimeout(checkTimer);
    if (!status || status.available || !PASSING_STATES.has(status.state)) return;
    checkTimer = setTimeout(() => {
      if (window.location.hash === '#remove') loadStatus();
    }, CHECK_AGAIN_MS);
  }

  function renderStatus() {
    const available = Boolean(status && status.available);
    const checking = Boolean(statusLoading) && !status;
    statusBox.hidden = available || (!status && !checking);
    statusBox.classList.toggle('alert-warning', Boolean(status) && !available);
    statusBox.classList.toggle('alert-secondary', checking);
    statusBox.textContent = checking
      ? 'Checking that Remove background is available…'
      : status && !available ? status.message : '';
    chooseButton.disabled = !available;
    // While the tool cannot be used, the drop hint and privacy note would only promise it.
    for (const element of readyOnly) element.hidden = !available;
    credit.hidden = !(available && status.attribution);
    credit.textContent = available && status.attribution ? status.attribution : '';
    renderHomeCard();
  }

  /** Dims the home card, with a note, when the server says the tool is not set up. */
  function renderHomeCard() {
    if (!homeCard || !homeCardNote) return;
    const off = Boolean(status) && !status.available && !PASSING_STATES.has(status.state) && status.state !== 'signed-out' && 'state' in status;
    homeCard.classList.toggle('is-off', off);
    homeCardNote.hidden = !off;
    if (off) {
      homeCardNote.id = homeCardNote.id || 'photRemoveCardNote';
      homeCard.setAttribute('aria-describedby', homeCardNote.id);
    } else {
      homeCard.removeAttribute('aria-describedby');
    }
  }

  /** Sends a copy of the photo and gives back the model's mask at the work size. `signal` cancels it. */
  function fetchMask(source, size, signal) {
    return requestMask(source, size, work, signal);
  }

  /**
   * Sends a copy of the photo (at most the server's side, 2048 px) and gives back the model's mask
   * enlarged to `into`'s size, one byte per pixel. Throws PhotoToolError 'retry' for what passes
   * (busy, too slow, no connection), 'signed-out', or 'server' for a photo the server cannot use.
   */
  async function requestMask(source, size, into, signal) {
    const side = Math.min(WORK_SIDE, (status && status.maximumSide) || WORK_SIDE);
    const sent = core.resizeDimensions(size.width, size.height, { type: 'longest', value: side });
    const copy = shared.drawPhoto(source, sent.width, sent.height, { opaque: true });
    const bytes = await shared.canvasToBytes(copy, JPEG, 0.92);
    let response;
    try {
      response = await fetch(MASK_URL, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': JPEG.mime, Accept: 'image/png', 'X-CSRF-TOKEN': csrfToken() },
        body: bytes,
        signal
      });
    } catch (error) {
      if (signal && signal.aborted) throw error;
      throw new core.PhotoToolError('retry', 'The PRISM server could not be reached. Check the connection and try again.');
    }
    if (response.status === 401) {
      throw new core.PhotoToolError('signed-out', SIGNED_OUT);
    }
    if (!response.ok) {
      let message = 'The background could not be removed from this photo.';
      try {
        const answer = await response.json();
        if (answer && answer.message) message = answer.message;
      } catch {
        // The plain message above is used.
      }
      // Busy, too slow, or the server briefly unavailable: worth trying the same photo again.
      const passing = response.status === 503 || response.status === 408 || response.status === 502 || response.status === 504;
      throw new core.PhotoToolError(passing ? 'retry' : 'server', message);
    }
    const mask = await createImageBitmap(await response.blob());
    // The mask is enlarged to the work size by the browser, which blends between its pixels.
    const scaled = shared.newCanvas(into.width, into.height);
    const context = scaled.getContext('2d', { willReadFrequently: true });
    context.imageSmoothingQuality = 'high';
    context.drawImage(mask, 0, 0, into.width, into.height);
    if (typeof mask.close === 'function') mask.close();
    const data = context.getImageData(0, 0, into.width, into.height).data;
    const result = new Uint8ClampedArray(into.width * into.height);
    for (let i = 0; i < result.length; i += 1) result[i] = data[i * 4];
    return result;
  }

  // ---------------------------------------------------------------------------
  // Photo
  // ---------------------------------------------------------------------------

  function setBusy(text) {
    busy = Boolean(text);
    busyBox.hidden = !busy;
    if (text) busyText.textContent = text;
    busyStatus.textContent = text || '';
    downloadButton.disabled = busy || !cutout;
  }

  function resetPhoto() {
    shared.setUnsaved('remove', false);
    work = null;
    pixels = null;
    modelAlpha = null;
    alpha = null;
    colourAlpha = null;
    background = null;
    subjectColours = null;
    cutout = null;
    blurred = null;
    strokes = [];
    stroke = null;
    ring = null;
    colours = null;
    pending = null;
  }

  async function openFile(file) {
    shared.clearError();
    if (!file) return;
    const ready = await loadStatus();
    if (!ready.available) {
      // A photo dropped while the tool cannot be used is not ignored without a word: the note
      // that says why gets the focus, so it is read out again.
      statusBox.tabIndex = -1;
      statusBox.focus();
      return;
    }
    if (!core.isSupportedPhoto(file.type, file.name)) {
      shared.showError('Only JPG, PNG and WEBP photos can be used here. iPhone HEIC photos are not supported.');
      return;
    }
    // Only the photo picked last is used. A photo that cannot be opened leaves the one already
    // being worked on alone: only a photo that opens takes over (openRun), so a mask still on its
    // way for the earlier photo finishes, or is dropped, and its busy state is always cleared.
    const pick = ++pickRun;
    let source;
    try {
      source = await shared.openPhoto(file);
    } catch (error) {
      if (pick === pickRun) shared.showError(error instanceof core.PhotoToolError ? error.message : 'This photo could not be opened.');
      return;
    }
    if (pick !== pickRun) {
      if (typeof source.close === 'function') source.close();
      return;
    }
    const mine = ++openRun;
    const size = shared.sizeOf(source);
    // Only what can be saved is kept: a copy no larger than SAVE_SIDE. The original (190 MB for a
    // 48 MP photo) is closed at once. A save still running keeps its own reference to the copy it
    // started with, so the earlier copy is left to the browser rather than cleared here.
    const keep = core.resizeDimensions(size.width, size.height, { type: 'longest', value: SAVE_SIDE });
    full = shared.drawPhoto(source, keep.width, keep.height, {});
    if (typeof source.close === 'function') source.close();
    fileName = file.name;
    resetPhoto();
    fileLabel.textContent = `${file.name} · ${size.width} × ${size.height} px`;
    emptyDrop.hidden = true;
    workspace.hidden = false;
    changeButton.hidden = false;
    resultLine.className = 'phot-result';
    resultLine.textContent = '';

    const workSize = core.resizeDimensions(size.width, size.height, { type: 'longest', value: WORK_SIDE });
    work = shared.drawPhoto(full, workSize.width, workSize.height, {});
    pixels = work.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, work.width, work.height);
    render();
    await findSubject(mine);
  }

  /**
   * Asks the server for the photo's mask and makes the cut-out. A step that stops for a reason that
   * passes (busy, too slow, no connection, or the user pressed Cancel) keeps the photo on screen
   * with Try again; only a photo the server cannot use takes the screen back to "Choose a photo".
   */
  async function findSubject(mine) {
    // The earlier photo's mask is no longer wanted: its request is cancelled, which also stops the
    // server working on it, so the server's queue is not filled with photos already replaced.
    if (maskRequest) maskRequest.abort();
    const request = new AbortController();
    maskRequest = request;
    cancelledByUser = false;
    maskTimedOut = false;
    maskPending = true;
    const giveUp = setTimeout(() => {
      maskTimedOut = true;
      request.abort();
    }, MASK_GIVE_UP_MS);
    retryBox.hidden = true;
    shared.clearError();
    const source = full;
    const size = shared.sizeOf(source);
    setBusy(FINDING);
    clearTimeout(slowTimer);
    slowTimer = setTimeout(() => {
      if (mine !== openRun || !busy) return;
      busyText.textContent = STILL_WORKING;
      busyStatus.textContent = STILL_WORKING;
      cancelButton.hidden = false;
    }, SLOW_AFTER_MS);
    try {
      const mask = await fetchMask(source, size, request.signal);
      if (mine !== openRun) return;
      background = core.cutoutBackground(pixels.data, mask, work.width, work.height);
      // The subject's colours are read once; every stroke on this photo uses them.
      subjectColours = background
        ? core.cutoutPersonColours(pixels.data, mask, work.width, background, cellSize())
        : null;
      // Where the model is unsure (the band around hair), its mask is checked against the colours.
      core.refineCutoutEdge(mask, pixels.data, work.width, background, cellSize(), subjectColours);
      modelAlpha = mask;
      alpha = new Uint8ClampedArray(mask);
      colourAlpha = new Uint8ClampedArray(mask);
      blurred = makeBlur(work, pixels.data, modelAlpha, background);
      cutout = shared.newCanvas(work.width, work.height);
      rebuild();
      const saved = savedSize();
      resultLine.className = 'phot-result is-ready';
      resultLine.textContent = `Ready. The saved photo will be ${saved.width} × ${saved.height} px.`;
    } catch (error) {
      if (mine !== openRun) return;
      if (cancelledByUser) {
        showRetry('Stopped. The photo is still here.');
        return;
      }
      if (maskTimedOut) {
        showRetry('No answer came from the PRISM server in time. The photo is still here.');
        return;
      }
      if (error instanceof core.PhotoToolError && error.code === 'retry') {
        showRetry(error.message);
        return;
      }
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      resetPhoto();
      emptyDrop.hidden = false;
      workspace.hidden = true;
      changeButton.hidden = true;
      fileLabel.textContent = '';
      shared.showError(error instanceof core.PhotoToolError ? error.message : 'The background could not be removed from this photo.');
    } finally {
      clearTimeout(giveUp);
      if (mine === openRun) {
        maskPending = false;
        clearTimeout(slowTimer);
        cancelButton.hidden = true;
        setBusy('');
        renderControls();
        render();
      }
    }
  }

  /** Keeps the photo on screen with the reason it stopped and a Try again button. */
  function showRetry(message) {
    retryText.textContent = message;
    retryBox.hidden = false;
    busyStatus.textContent = message;
    retryButton.focus({ preventScroll: true });
  }

  /**
   * The blurred background: the photo with the subject painted out in the old background's
   * colours (so the subject does not smear into its own background), drawn small and enlarged.
   */
  function makeBlur(source, data, mask, field) {
    const plate = new Uint8ClampedArray(data);
    if (field) {
      const was = [0, 0, 0];
      for (let i = 0; i < mask.length; i += 1) {
        const share = mask[i] / 255;
        if (!share) continue;
        const x = i % source.width;
        core.wallColourAt(field, x, (i - x) / source.width, was);
        for (let channel = 0; channel < 3; channel += 1) {
          plate[i * 4 + channel] = Math.round(plate[i * 4 + channel] * (1 - share) + was[channel] * share);
        }
        plate[i * 4 + 3] = 255;
      }
    }
    const flat = shared.newCanvas(source.width, source.height);
    flat.getContext('2d').putImageData(new ImageData(plate, source.width, source.height), 0, 0);
    const small = core.resizeDimensions(source.width, source.height, { type: 'longest', value: BLUR_SIDE });
    const tiny = shared.drawPhoto(flat, small.width, small.height, {});
    const result = shared.newCanvas(source.width, source.height);
    const context = result.getContext('2d');
    context.imageSmoothingQuality = 'high';
    context.drawImage(tiny, 0, 0, source.width, source.height);
    return result;
  }

  function savedSize() {
    const size = shared.sizeOf(full);
    return core.resizeDimensions(size.width, size.height, { type: 'longest', value: SAVE_SIDE });
  }

  // ---------------------------------------------------------------------------
  // Touch-ups
  // ---------------------------------------------------------------------------

  /** A picture of the strokes over one part of the photo, each in its brush's colour, each soft on its own. */
  function strokeMask(region) {
    const width = region.right - region.left;
    const height = region.bottom - region.top;
    const mask = shared.newCanvas(width, height);
    const context = mask.getContext('2d', { willReadFrequently: true });
    context.translate(-region.left, -region.top);
    context.lineCap = 'round';
    context.lineJoin = 'round';
    const paint = (fix, radius) => {
      context.lineWidth = radius * 2;
      context.beginPath();
      if (fix.points.length === 1) {
        context.arc(fix.points[0].x, fix.points[0].y, radius, 0, Math.PI * 2);
        context.fill();
      } else {
        context.moveTo(fix.points[0].x, fix.points[0].y);
        for (const point of fix.points.slice(1)) context.lineTo(point.x, point.y);
        context.stroke();
      }
    };
    for (const fix of strokes) {
      // A stroke nowhere near this part cannot change it: painting stays quick however many
      // strokes there are.
      const reach = fix.radius * (1 + BRUSH_SOFTNESS) + 2;
      const box = fix.bounds;
      if (box && (box.right + reach < region.left || box.left - reach > region.right
        || box.bottom + reach < region.top || box.top - reach > region.bottom)) {
        continue;
      }
      const colour = (core.BACKGROUND_BRUSHES[fix.kind] || core.BACKGROUND_BRUSHES.refine).mark;
      context.strokeStyle = colour;
      context.fillStyle = colour;
      const soft = fix.radius * BRUSH_SOFTNESS;
      context.globalAlpha = BRUSH_RING_ALPHA;
      for (let ringIndex = 0; ringIndex < BRUSH_RINGS; ringIndex += 1) {
        paint(fix, fix.radius + soft - (2 * soft * ringIndex) / BRUSH_RINGS);
      }
      context.globalAlpha = 1;
      paint(fix, Math.max(fix.radius * 0.1, fix.radius - soft));
    }
    return context.getImageData(0, 0, width, height).data;
  }

  /**
   * The mask after the touch-ups, the cut-out made from it, and the stage. With `region`, only
   * that part is made again (every stroke is still applied there, so the result is the same);
   * painting stays quick because a pointer move changes only the area around it.
   */
  function rebuild(region) {
    if (!modelAlpha) return;
    const width = work.width;
    const height = work.height;
    const area = region || { left: 0, top: 0, right: width, bottom: height };
    if (strokes.length) {
      core.applyCutoutStrokes(alpha, modelAlpha, pixels.data, strokeMask(area), background,
        { width, cell: cellSize(), colours: subjectColours, region: area, colourAlpha });
    } else {
      for (let y = area.top; y < area.bottom; y += 1) {
        const start = y * width + area.left;
        alpha.set(modelAlpha.subarray(start, start + area.right - area.left), start);
        colourAlpha.set(modelAlpha.subarray(start, start + area.right - area.left), start);
      }
    }
    if (!colours || colours.width !== width || colours.height !== height) {
      colours = new ImageData(new Uint8ClampedArray(pixels.data), width, height);
    }
    for (let y = area.top; y < area.bottom; y += 1) {
      const start = (y * width + area.left) * 4;
      colours.data.set(pixels.data.subarray(start, start + (area.right - area.left) * 4), start);
    }
    core.cutoutColours(colours.data, colourAlpha, width, background, area, alpha);
    cutout.getContext('2d').putImageData(colours, 0, 0, area.left, area.top, area.right - area.left, area.bottom - area.top);
    render();
  }

  /** The part of the photo a stroke from one point to another can change, soft edge included. */
  function strokeArea(from, to, radius) {
    const reach = radius * (1 + BRUSH_SOFTNESS) + 2;
    return {
      left: Math.max(0, Math.floor(Math.min(from.x, to.x) - reach)),
      top: Math.max(0, Math.floor(Math.min(from.y, to.y) - reach)),
      right: Math.min(work.width, Math.ceil(Math.max(from.x, to.x) + reach)),
      bottom: Math.min(work.height, Math.ceil(Math.max(from.y, to.y) + reach))
    };
  }

  /** Strokes are made once a frame at most, however fast the pointer moves, and only where they changed. */
  function rebuildSoon(area) {
    pending = pending
      ? {
          left: Math.min(pending.left, area.left),
          top: Math.min(pending.top, area.top),
          right: Math.max(pending.right, area.right),
          bottom: Math.max(pending.bottom, area.bottom)
        }
      : area;
    if (dirty) return;
    dirty = true;
    requestAnimationFrame(() => {
      dirty = false;
      const region = pending;
      pending = null;
      if (region && region.right > region.left && region.bottom > region.top) rebuild(region);
    });
  }

  /** The squares the subject's colours are read from: about 3% of the photo's width. */
  function cellSize() {
    return Math.max(4, Math.round(work.width * 0.03));
  }

  function brushRadius() {
    return Math.max(1, BRUSH_SHARES[brushSize] * work.width);
  }

  function workPointAt(event) {
    const box = photoBox.getBoundingClientRect();
    return {
      x: (event.clientX - box.left) / box.width * work.width,
      y: (event.clientY - box.top) / box.height * work.height
    };
  }

  /** Adds a point to a stroke and widens the box it covers. */
  function addPoint(fix, point) {
    fix.points.push(point);
    const box = fix.bounds;
    fix.bounds = box
      ? {
          left: Math.min(box.left, point.x),
          top: Math.min(box.top, point.y),
          right: Math.max(box.right, point.x),
          bottom: Math.max(box.bottom, point.y)
        }
      : { left: point.x, top: point.y, right: point.x, bottom: point.y };
  }

  function startStroke(at) {
    stroke = { kind: brush, radius: brushRadius(), points: [], bounds: null };
    addPoint(stroke, at);
    strokes.push(stroke);
    rebuildSoon(strokeArea(at, at, stroke.radius));
  }

  function endStroke() {
    if (!stroke) return;
    stroke = null;
    shared.setUnsaved('remove', true);
    renderControls();
  }

  function stepBrushSize(by) {
    const at = BRUSH_ORDER.indexOf(brushSize);
    brushSize = BRUSH_ORDER[Math.min(BRUSH_ORDER.length - 1, Math.max(0, at + by))];
    renderControls();
    render();
  }

  // ---------------------------------------------------------------------------
  // Screen
  // ---------------------------------------------------------------------------

  function backdropColour() {
    return backdrop.startsWith('#') ? backdrop : null;
  }

  function renderControls() {
    for (const button of bgButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.removeBg === backdrop));
      const dot = button.querySelector('.phot-bgchoice__dot:not(.phot-bgchoice__dot--clear)');
      if (dot) dot.style.setProperty('--phot-swatch', button.dataset.removeBg);
    }
    const preset = bgButtons.some(button => button.dataset.removeBg === backdrop);
    bgOther.classList.toggle('is-pressed', !preset);
    // The colour picker is not a button, so its name says when it is the one chosen.
    bgColour.setAttribute('aria-label', preset ? 'Other background colour' : 'Other background colour, chosen');
    for (const button of brushButtons) button.setAttribute('aria-pressed', String(button.dataset.removeBrush === brush));
    for (const button of sizeButtons) button.setAttribute('aria-pressed', String(button.dataset.removeSizeChoice === brushSize));
    brushHint.textContent = BRUSH_HINTS[brush];
    undoButton.disabled = !strokes.length;
    resetButton.disabled = !strokes.length;
    originalButton.disabled = !cutout;
    // A JPG has no see-through areas: with See-through chosen it is offered no more.
    const jpeg = format.querySelector('option[value="jpeg"]');
    jpeg.disabled = backdrop === 'transparent';
    if (jpeg.disabled && format.value === 'jpeg') format.value = 'png';
    // Says why JPG cannot be picked, instead of leaving it greyed out without a reason.
    jpegHint.hidden = !jpeg.disabled;
    downloadButton.disabled = busy || !cutout;
    sizeLabel.textContent = work ? `${savedSize().width} × ${savedSize().height} px` : '';
    photoBox.classList.toggle('is-clear', backdrop === 'transparent' && !showOriginal);
  }

  function render() {
    if (!work) return;
    const style = getComputedStyle(stage);
    const room = {
      width: stage.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight),
      height: stage.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom)
    };
    if (room.width <= 0 || room.height <= 0) return;
    const fit = Math.min(room.width / work.width, room.height / work.height);
    display = { width: Math.max(1, Math.floor(work.width * fit)), height: Math.max(1, Math.floor(work.height * fit)) };
    photoBox.style.width = `${display.width}px`;
    photoBox.style.height = `${display.height}px`;

    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.round(display.width * ratio);
    canvas.height = Math.round(display.height * ratio);
    const context = canvas.getContext('2d');
    context.imageSmoothingQuality = 'high';
    context.clearRect(0, 0, canvas.width, canvas.height);
    photoBox.classList.toggle('is-clear', backdrop === 'transparent' && !showOriginal && Boolean(cutout));
    if (showOriginal || !cutout) {
      context.drawImage(work, 0, 0, canvas.width, canvas.height);
    } else {
      if (backdropColour()) {
        context.fillStyle = backdropColour();
        context.fillRect(0, 0, canvas.width, canvas.height);
      } else if (backdrop === 'blur' && blurred) {
        context.drawImage(blurred, 0, 0, canvas.width, canvas.height);
      }
      context.drawImage(cutout, 0, 0, canvas.width, canvas.height);
    }
    if (ring && cutout && !showOriginal) {
      // The brush: dark under light, so it shows on any photo.
      const radius = Math.max(2, BRUSH_SHARES[brushSize] * display.width) * ratio;
      for (const [colour, width] of [['#111827', 3 * ratio], ['#ffffff', 1.5 * ratio]]) {
        context.strokeStyle = colour;
        context.lineWidth = width;
        context.beginPath();
        context.arc(ring.x * ratio, ring.y * ratio, radius, 0, Math.PI * 2);
        context.stroke();
      }
    }
  }

  // ---------------------------------------------------------------------------
  // Save
  // ---------------------------------------------------------------------------

  /**
   * The cut-out at the saved size: the mask enlarged, the edge cleaned again at full size with the
   * same background estimate the screen used.
   */
  function fullCutout(job, size) {
    const { work: small, alpha: maskAlpha, colourAlpha: maskColour } = job;
    if (size.width === small.width && size.height === small.height) {
      const colours = new Uint8ClampedArray(job.pixels);
      core.cutoutColours(colours, maskColour, size.width, job.background, null, maskAlpha);
      return { colours };
    }
    const photo = shared.drawPhoto(job.photo, size.width, size.height, {});
    const data = photo.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, size.width, size.height).data;
    release(photo);
    const maskSmall = shared.newCanvas(small.width, small.height);
    const grey = new Uint8ClampedArray(small.width * small.height * 4);
    // Both are enlarged together: red holds the mask after the touch-ups, green the colour share.
    for (let i = 0; i < maskAlpha.length; i += 1) grey.set([maskAlpha[i], maskColour[i], 0, 255], i * 4);
    maskSmall.getContext('2d').putImageData(new ImageData(grey, small.width, small.height), 0, 0);
    const maskLarge = shared.newCanvas(size.width, size.height);
    const largeContext = maskLarge.getContext('2d', { willReadFrequently: true });
    largeContext.imageSmoothingQuality = 'high';
    largeContext.drawImage(maskSmall, 0, 0, size.width, size.height);
    release(maskSmall);
    const largeGrey = largeContext.getImageData(0, 0, size.width, size.height).data;
    release(maskLarge);
    const largeAlpha = new Uint8ClampedArray(size.width * size.height);
    const largeColourAlpha = new Uint8ClampedArray(size.width * size.height);
    for (let i = 0; i < largeAlpha.length; i += 1) {
      largeAlpha[i] = largeGrey[i * 4];
      largeColourAlpha[i] = largeGrey[i * 4 + 1];
    }
    // The background estimate made from the model's own mask, at this size: the touch-ups must
    // not count as background (an erased bit of the subject is not old background), and the
    // saved edges then match what the screen showed.
    const field = core.scaleBackground(job.background, size.width / small.width);
    // The photo's own pixels become the cut-out's colours: no second copy of them is made.
    core.cutoutColours(data, largeColourAlpha, size.width, field, null, largeAlpha);
    return { colours: data };
  }

  /** Gives a temporary canvas's memory back at once instead of when the browser gets round to it. */
  function release(canvasToFree) {
    canvasToFree.width = 0;
    canvasToFree.height = 0;
  }

  async function download() {
    if (!cutout || busy) return;
    shared.clearError();
    // Everything the file is made from is taken now. Another photo may be opened while it is made:
    // the file is still this photo's, under its name, and the other photo's screen is left alone.
    const job = {
      run: openRun,
      photo: full,
      work,
      pixels: pixels.data,
      alpha: new Uint8ClampedArray(alpha),
      colourAlpha: new Uint8ClampedArray(colourAlpha),
      background,
      blurred,
      backdrop,
      colour: backdropColour(),
      type: core.TYPES[format.value] || core.TYPES.png,
      fileName
    };
    setBusy('Making the file…');
    // A moment for "Making the file…" to show. A timer, not an animation frame: a page in a tab
    // that is not showing gets no frames, and the save would wait until it is shown again.
    await new Promise(resolve => setTimeout(resolve, 30));
    const current = () => job.run === openRun;
    try {
      const size = core.resizeDimensions(job.photo.width, job.photo.height, { type: 'longest', value: SAVE_SIDE });
      const { colours } = fullCutout(job, size);
      if (job.colour) {
        core.cutoutOnto(colours, job.colour.match(/[0-9a-f]{2}/gi).map(part => parseInt(part, 16)));
      } else if (job.backdrop === 'blur') {
        const behind = shared.newCanvas(size.width, size.height);
        const behindContext = behind.getContext('2d', { willReadFrequently: true });
        behindContext.imageSmoothingQuality = 'high';
        behindContext.drawImage(job.blurred, 0, 0, size.width, size.height);
        core.cutoutOnto(colours, behindContext.getImageData(0, 0, size.width, size.height).data);
        release(behind);
      }
      // A JPG is only offered on a colour or the blur, where every pixel is already solid, so the
      // picture is saved as it is, without another full-size copy.
      const picture = shared.newCanvas(size.width, size.height);
      picture.getContext('2d').putImageData(new ImageData(colours, size.width, size.height), 0, 0);
      const bytes = await shared.canvasToBytes(picture, job.type, 0.92);
      release(picture);
      const suffix = job.backdrop === 'transparent' ? 'cutout' : 'new-background';
      shared.saveBlob(new Blob([bytes], { type: job.type.mime }), `${core.baseName(job.fileName)}-${suffix}.${job.type.extension}`);
      shared.setUnsaved('remove', false);
      if (current()) {
        resultLine.className = 'phot-result is-ready';
        resultLine.textContent = `Saved: ${size.width} × ${size.height} px, ${core.formatBytes(bytes.length)}, ${job.type.label}.`;
      }
    } catch (error) {
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      if (current()) {
        shared.showError(error instanceof core.PhotoToolError ? error.message : 'The file could not be made. The photo may be too large for this browser.');
      }
    } finally {
      // A newer photo has its own busy state ("Finding the person or object…"): it is not cleared.
      if (current()) setBusy('');
    }
  }

  // ---------------------------------------------------------------------------
  // Events
  // ---------------------------------------------------------------------------

  for (const button of $$('[data-action="choose-photo"], [data-action="change-photo"]')) {
    button.addEventListener('click', () => input.click());
  }
  input.addEventListener('change', () => {
    openFile(input.files && input.files[0]);
    input.value = '';
  });
  shared.wireDropZones($$('[data-drop]'), files => openFile(files[0]));

  photoBox.addEventListener('pointerdown', (event) => {
    if (!cutout || busy || showOriginal || stroke || !shared.isMainPointer(event)) return;
    event.preventDefault();
    const box = photoBox.getBoundingClientRect();
    ring = { x: event.clientX - box.left, y: event.clientY - box.top };
    startStroke(workPointAt(event));
    stroke.pointer = event.pointerId;
    try {
      photoBox.setPointerCapture(event.pointerId);
    } catch {
      // The pointer is already gone; painting works without capture.
    }
  });
  photoBox.addEventListener('pointermove', (event) => {
    if (!cutout || !event.isPrimary) return;
    const box = photoBox.getBoundingClientRect();
    ring = { x: event.clientX - box.left, y: event.clientY - box.top };
    if (stroke && stroke.pointer === event.pointerId) {
      // A mouse that moves with no button held was let go somewhere the release was not seen.
      if (event.pointerType === 'mouse' && event.buttons === 0) {
        endStroke();
        return;
      }
      const last = stroke.points[stroke.points.length - 1];
      const next = workPointAt(event);
      addPoint(stroke, next);
      rebuildSoon(strokeArea(last, next, stroke.radius));
    } else {
      render();
    }
  });
  for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) {
    photoBox.addEventListener(type, (event) => {
      if (stroke && stroke.pointer === event.pointerId) endStroke();
    });
  }
  photoBox.addEventListener('pointerleave', () => {
    if (stroke) return;
    ring = null;
    render();
  });
  photoBox.addEventListener('keydown', (event) => {
    if (!cutout || busy) return;
    const step = event.shiftKey ? 2 : 10;
    const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    const from = ring || { x: display.width / 2, y: display.height / 2 };
    if (moves[event.key]) {
      event.preventDefault();
      ring = {
        x: Math.min(display.width, Math.max(0, from.x + moves[event.key][0])),
        y: Math.min(display.height, Math.max(0, from.y + moves[event.key][1]))
      };
      render();
    } else if ((event.key === ' ' || event.key === 'Enter') && !stroke) {
      event.preventDefault();
      ring = from;
      startStroke({ x: from.x / display.width * work.width, y: from.y / display.height * work.height });
      endStroke();
    }
  });
  photoBox.addEventListener('blur', () => {
    ring = null;
    render();
  });
  // [ and ] change the brush size anywhere on this screen, except while typing in a box.
  view.addEventListener('keydown', (event) => {
    if (!cutout || (event.key !== '[' && event.key !== ']')) return;
    if (/^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName) && event.target.type !== 'range') return;
    event.preventDefault();
    stepBrushSize(event.key === '[' ? -1 : 1);
  });

  undoButton.addEventListener('click', () => {
    strokes.pop();
    // Any change after a download is unsaved again, Undo included.
    shared.setUnsaved('remove', true);
    renderControls();
    rebuild();
  });
  resetButton.addEventListener('click', () => {
    strokes = [];
    shared.setUnsaved('remove', true);
    renderControls();
    rebuild();
  });
  const holdOriginal = (on) => {
    if (showOriginal === on || !cutout) return;
    showOriginal = on;
    originalButton.classList.toggle('active', on);
    renderControls();
    render();
  };
  shared.wireHoldButton(originalButton, holdOriginal);

  for (const button of bgButtons) {
    button.addEventListener('click', () => {
      backdrop = button.dataset.removeBg;
      renderControls();
      render();
      rememberChoices();
    });
  }
  bgColour.addEventListener('input', () => {
    backdrop = bgColour.value;
    renderControls();
    render();
    rememberChoices();
  });
  bgColour.addEventListener('click', () => {
    backdrop = bgColour.value;
    renderControls();
    render();
    rememberChoices();
  });
  format.addEventListener('change', rememberChoices);

  (function restoreChoices() {
    const kept = remembered.load();
    if (!kept) return;
    const known = bgButtons.some(button => button.dataset.removeBg === kept.backdrop);
    if (known || (typeof kept.backdrop === 'string' && /^#[0-9a-f]{6}$/i.test(kept.backdrop))) {
      backdrop = kept.backdrop;
      if (!known) bgColour.value = kept.backdrop;
    }
    // Set before the controls are drawn: JPG is then refused again if See-through came back.
    if (Array.from(format.options).some(option => option.value === kept.format)) format.value = kept.format;
  })();
  for (const button of brushButtons) {
    button.addEventListener('click', () => {
      brush = button.dataset.removeBrush;
      renderControls();
    });
  }
  for (const button of sizeButtons) {
    button.addEventListener('click', () => {
      brushSize = button.dataset.removeSizeChoice;
      renderControls();
      render();
    });
  }
  downloadButton.addEventListener('click', download);
  cancelButton.addEventListener('click', () => {
    if (!maskRequest) return;
    cancelledByUser = true;
    maskRequest.abort();
  });
  retryButton.addEventListener('click', () => {
    if (!full || busy) return;
    findSubject(openRun);
  });

  // The status is asked for when this screen is shown, and once on the home screen so its card
  // can say when the tool is not set up.
  const whenShown = () => {
    const hash = window.location.hash;
    if (hash === '#remove' || ((hash === '' || hash === '#') && !status)) loadStatus();
    // Leaving the screen stops a mask still on its way, so it does not hold the server's place;
    // the photo waits with Try again for when the user comes back.
    if (hash !== '#remove' && maskPending && maskRequest) {
      cancelledByUser = true;
      maskRequest.abort();
    }
  };
  window.addEventListener('hashchange', whenShown);
  new ResizeObserver(render).observe(stage);

  /**
   * Where the face is in a photo, for Passport photo: { found, imageWidth, imageHeight, box, points }
   * in the canvas's pixels. When the server cannot say, { problem: 'busy' } (its places are taken,
   * worth trying again shortly) or { problem: 'failed' } (not set up, no connection, an error);
   * null only when `signal` cancelled it. The same rules as the mask: a JPG copy, the antiforgery
   * token, nothing stored.
   */
  async function locateFace(canvas, signal) {
    const bytes = await shared.canvasToBytes(canvas, JPEG, 0.9);
    let response;
    try {
      response = await fetch(FACE_URL, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': JPEG.mime, Accept: 'application/json', 'X-CSRF-TOKEN': csrfToken() },
        body: bytes,
        signal
      });
    } catch {
      return signal && signal.aborted ? null : { problem: 'failed' };
    }
    if (response.status === 503 && response.headers.get('Retry-After')) return { problem: 'busy' };
    if (!response.ok) return { problem: 'failed' };
    const answer = await response.json().catch(() => null);
    if (!answer) return signal && signal.aborted ? null : { problem: 'failed' };
    return {
      found: Boolean(answer.found && answer.face && Array.isArray(answer.points) && answer.points.length >= 5),
      imageWidth: answer.imageWidth,
      imageHeight: answer.imageHeight,
      box: answer.face ? { left: answer.face.left, top: answer.face.top, width: answer.face.width, height: answer.face.height } : null,
      points: answer.points || null
    };
  }

  /**
   * The model's mask for a photo, for Passport photo: { width, height, alpha } at the size Remove
   * background works at (at most 2048 px), one byte per pixel. Throws as requestMask does.
   */
  async function maskFor(source, signal) {
    await loadStatus();
    const size = shared.sizeOf(source);
    const into = core.resizeDimensions(size.width, size.height, { type: 'longest', value: WORK_SIDE });
    const alpha = await requestMask(source, size, into, signal);
    return { width: into.width, height: into.height, alpha };
  }

  // Passport photo asks the PRISM server through here, so this stays the only script that does.
  window.PrismPhotoServer = Object.freeze({ status: loadStatus, locateFace, mask: maskFor });

  renderControls();
  whenShown();
})();
