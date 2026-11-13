// PRISM Photo Tools — Remove background.
//
// A copy of the photo (at most 2048 px) is sent to the PRISM server, whose model returns only a
// mask: how much of each pixel is the subject. Everything else happens in this browser: the old
// background's tint is taken out of the soft edge, the touch-up brushes (Refine Edge, Erase,
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
    refine: 'Paint along hair, fur or a soft edge. What looks like the old background goes; strands and edges stay.',
    erase: 'Paint over anything that should not be in the cut-out.',
    restore: 'Paint where part of the subject was taken away. It comes back.'
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
  const bgColour = $('[data-remove-bg-colour]');
  const brushButtons = $$('[data-remove-brush]');
  const brushHint = $('[data-remove-brush-hint]');
  const sizeButtons = $$('[data-remove-size-choice]');
  const format = $('[data-remove-format]');
  const resultLine = $('[data-remove-result]');
  const downloadButton = $('[data-action="download-remove"]');
  const credit = $('[data-remove-credit]');

  let status = null;
  let statusLoading = null;

  // The photo: `full` as opened, `work` the copy the cut-out is made on, `pixels` its RGBA.
  let fileName = '';
  let full = null;
  let work = null;
  let pixels = null;
  // The model's mask, the mask after the touch-ups, and the old background's colours.
  let modelAlpha = null;
  let alpha = null;
  let background = null;
  let subjectColours = null;
  // The cut-out (RGBA, see-through where the mask says) and the blurred background.
  let cutout = null;
  let blurred = null;

  let backdrop = 'transparent';
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
  let display = { width: 0, height: 0 };

  // ---------------------------------------------------------------------------
  // Server
  // ---------------------------------------------------------------------------

  function csrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.getAttribute('content') || '';
  }

  /** Whether Remove background is set up on the server. Asked once, when the screen opens. */
  function loadStatus() {
    if (status) return Promise.resolve(status);
    if (!statusLoading) {
      statusLoading = fetch(STATUS_URL, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
        .then(response => (response.ok ? response.json() : null))
        .catch(() => null)
        .then((answer) => {
          status = answer || {
            available: false,
            message: 'The PRISM server could not be reached. Check the connection and open this screen again.'
          };
          if (!answer) statusLoading = null;
          renderStatus();
          return status;
        });
    }
    return statusLoading;
  }

  function renderStatus() {
    const available = Boolean(status && status.available);
    statusBox.hidden = !status || available;
    statusBox.textContent = status && !available ? status.message : '';
    chooseButton.disabled = !available;
    credit.hidden = !(available && status.attribution);
    credit.textContent = available && status.attribution ? status.attribution : '';
  }

  /** Sends a copy of the photo and gives back the model's mask at the work size. */
  async function fetchMask(source, size) {
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
        body: bytes
      });
    } catch {
      throw new core.PhotoToolError('network', 'The PRISM server could not be reached. Check the connection and try again.');
    }
    if (!response.ok) {
      let message = 'The background could not be removed from this photo.';
      try {
        const answer = await response.json();
        if (answer && answer.message) message = answer.message;
      } catch {
        // The plain message above is used.
      }
      throw new core.PhotoToolError('server', message);
    }
    const mask = await createImageBitmap(await response.blob());
    // The mask is enlarged to the work size by the browser, which blends between its pixels.
    const scaled = shared.newCanvas(work.width, work.height);
    const context = scaled.getContext('2d', { willReadFrequently: true });
    context.imageSmoothingQuality = 'high';
    context.drawImage(mask, 0, 0, work.width, work.height);
    if (typeof mask.close === 'function') mask.close();
    const data = context.getImageData(0, 0, work.width, work.height).data;
    const result = new Uint8ClampedArray(work.width * work.height);
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
    work = null;
    pixels = null;
    modelAlpha = null;
    alpha = null;
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
    if (!ready.available) return;
    if (!core.isSupportedPhoto(file.type, file.name)) {
      shared.showError('Only JPG, PNG and WEBP photos can be used here. iPhone HEIC photos are not supported.');
      return;
    }
    const mine = ++openRun;
    let source;
    try {
      source = await shared.openPhoto(file);
    } catch (error) {
      if (mine === openRun) shared.showError(error instanceof core.PhotoToolError ? error.message : 'This photo could not be opened.');
      return;
    }
    if (mine !== openRun) {
      if (typeof source.close === 'function') source.close();
      return;
    }
    if (full && full !== source && typeof full.close === 'function') full.close();
    full = source;
    fileName = file.name;
    resetPhoto();
    const size = shared.sizeOf(source);
    fileLabel.textContent = `${file.name} · ${size.width} × ${size.height} px`;
    emptyDrop.hidden = true;
    workspace.hidden = false;
    changeButton.hidden = false;
    resultLine.className = 'phot-result';
    resultLine.textContent = '';

    const workSize = core.resizeDimensions(size.width, size.height, { type: 'longest', value: WORK_SIDE });
    work = shared.drawPhoto(source, workSize.width, workSize.height, {});
    pixels = work.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, work.width, work.height);
    render();
    setBusy('Finding the subject…');
    try {
      const mask = await fetchMask(source, size);
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
      blurred = makeBlur(work, pixels.data, modelAlpha, background);
      cutout = shared.newCanvas(work.width, work.height);
      rebuild();
      const saved = savedSize();
      resultLine.className = 'phot-result is-ready';
      resultLine.textContent = `Ready. The saved photo will be ${saved.width} × ${saved.height} px.`;
    } catch (error) {
      if (mine !== openRun) return;
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      resetPhoto();
      emptyDrop.hidden = false;
      workspace.hidden = true;
      changeButton.hidden = true;
      fileLabel.textContent = '';
      shared.showError(error instanceof core.PhotoToolError ? error.message : 'The background could not be removed from this photo.');
    } finally {
      if (mine === openRun) {
        setBusy('');
        renderControls();
        render();
      }
    }
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
        { width, cell: cellSize(), colours: subjectColours, region: area });
    } else {
      for (let y = area.top; y < area.bottom; y += 1) {
        const start = y * width + area.left;
        alpha.set(modelAlpha.subarray(start, start + area.right - area.left), start);
      }
    }
    if (!colours || colours.width !== width || colours.height !== height) {
      colours = new ImageData(new Uint8ClampedArray(pixels.data), width, height);
    }
    for (let y = area.top; y < area.bottom; y += 1) {
      const start = (y * width + area.left) * 4;
      colours.data.set(pixels.data.subarray(start, start + (area.right - area.left) * 4), start);
    }
    core.cutoutColours(colours.data, alpha, width, background, area);
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

  function startStroke(at) {
    stroke = { kind: brush, radius: brushRadius(), points: [at] };
    strokes.push(stroke);
    rebuildSoon(strokeArea(at, at, stroke.radius));
  }

  function endStroke() {
    if (!stroke) return;
    stroke = null;
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
  function fullCutout(size) {
    if (size.width === work.width && size.height === work.height) {
      const colours = new Uint8ClampedArray(pixels.data);
      core.cutoutColours(colours, alpha, size.width, background);
      return { colours, data: pixels.data, alpha };
    }
    const photo = shared.drawPhoto(full, size.width, size.height, {});
    const data = photo.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, size.width, size.height).data;
    const maskSmall = shared.newCanvas(work.width, work.height);
    const grey = new Uint8ClampedArray(work.width * work.height * 4);
    for (let i = 0; i < alpha.length; i += 1) grey.set([alpha[i], alpha[i], alpha[i], 255], i * 4);
    maskSmall.getContext('2d').putImageData(new ImageData(grey, work.width, work.height), 0, 0);
    const maskLarge = shared.newCanvas(size.width, size.height);
    const largeContext = maskLarge.getContext('2d', { willReadFrequently: true });
    largeContext.imageSmoothingQuality = 'high';
    largeContext.drawImage(maskSmall, 0, 0, size.width, size.height);
    const largeGrey = largeContext.getImageData(0, 0, size.width, size.height).data;
    const largeAlpha = new Uint8ClampedArray(size.width * size.height);
    for (let i = 0; i < largeAlpha.length; i += 1) largeAlpha[i] = largeGrey[i * 4];
    // The background estimate made from the model's own mask, at this size: the touch-ups must
    // not count as background (an erased bit of the subject is not old background), and the
    // saved edges then match what the screen showed.
    const field = core.scaleBackground(background, size.width / work.width);
    const colours = new Uint8ClampedArray(data);
    core.cutoutColours(colours, largeAlpha, size.width, field);
    return { colours, data, alpha: largeAlpha };
  }

  async function download() {
    if (!cutout || busy) return;
    shared.clearError();
    setBusy('Making the file…');
    await new Promise(resolve => requestAnimationFrame(() => setTimeout(resolve, 0)));
    try {
      const size = savedSize();
      const made = fullCutout(size);
      const colours = made.colours;
      if (backdropColour()) {
        core.cutoutOnto(colours, backdropColour().match(/[0-9a-f]{2}/gi).map(part => parseInt(part, 16)));
      } else if (backdrop === 'blur') {
        const behind = shared.newCanvas(size.width, size.height);
        const behindContext = behind.getContext('2d', { willReadFrequently: true });
        behindContext.imageSmoothingQuality = 'high';
        behindContext.drawImage(blurred, 0, 0, size.width, size.height);
        core.cutoutOnto(colours, behindContext.getImageData(0, 0, size.width, size.height).data);
      }
      const type = core.TYPES[format.value] || core.TYPES.png;
      const picture = shared.newCanvas(size.width, size.height);
      picture.getContext('2d').putImageData(new ImageData(colours, size.width, size.height), 0, 0);
      const output = type === JPEG ? shared.drawPhoto(picture, size.width, size.height, { opaque: true }) : picture;
      const bytes = await shared.canvasToBytes(output, type, 0.92);
      const suffix = backdrop === 'transparent' ? 'cutout' : 'new-background';
      shared.saveBlob(new Blob([bytes], { type: type.mime }), `${core.baseName(fileName)}-${suffix}.${type.extension}`);
      resultLine.className = 'phot-result is-ready';
      resultLine.textContent = `Saved: ${size.width} × ${size.height} px, ${core.formatBytes(bytes.length)}, ${type.label}.`;
    } catch (error) {
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      shared.showError(error instanceof core.PhotoToolError ? error.message : 'The file could not be made. The photo may be too large for this browser.');
    } finally {
      setBusy('');
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
  for (const zone of $$('[data-drop]')) {
    zone.addEventListener('dragover', (event) => {
      event.preventDefault();
      zone.classList.add('is-dragging');
    });
    zone.addEventListener('dragleave', () => zone.classList.remove('is-dragging'));
    zone.addEventListener('drop', (event) => {
      event.preventDefault();
      zone.classList.remove('is-dragging');
      openFile(event.dataTransfer?.files?.[0]);
    });
  }

  photoBox.addEventListener('pointerdown', (event) => {
    if (!cutout || busy || showOriginal || stroke || !event.isPrimary) return;
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
      const last = stroke.points[stroke.points.length - 1];
      const next = workPointAt(event);
      stroke.points.push(next);
      rebuildSoon(strokeArea(last, next, stroke.radius));
    } else {
      render();
    }
  });
  for (const type of ['pointerup', 'pointercancel']) {
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
    renderControls();
    rebuild();
  });
  resetButton.addEventListener('click', () => {
    strokes = [];
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
  originalButton.addEventListener('pointerdown', () => holdOriginal(true));
  for (const type of ['pointerup', 'pointerleave', 'pointercancel', 'blur', 'keyup']) {
    originalButton.addEventListener(type, () => holdOriginal(false));
  }
  originalButton.addEventListener('keydown', (event) => {
    if (event.key === ' ' || event.key === 'Enter') holdOriginal(true);
  });

  for (const button of bgButtons) {
    button.addEventListener('click', () => {
      backdrop = button.dataset.removeBg;
      renderControls();
      render();
    });
  }
  bgColour.addEventListener('input', () => {
    backdrop = bgColour.value;
    renderControls();
    render();
  });
  bgColour.addEventListener('click', () => {
    backdrop = bgColour.value;
    renderControls();
    render();
  });
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

  // The status is asked for when this screen is first shown.
  const whenShown = () => {
    if (window.location.hash === '#remove') loadStatus();
  };
  window.addEventListener('hashchange', whenShown);
  new ResizeObserver(render).observe(stage);

  renderControls();
  whenShown();
})();
