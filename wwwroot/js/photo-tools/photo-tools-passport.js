// PRISM Photo Tools — Passport photo and signature.
//
// The crop frame stays still and the photo moves under it: drag, zoom, straighten, rotate.
// The saved file is an exact number of pixels (413 x 531 for 35 x 45 mm at 300 dpi) and can
// be held under a file size. Signature is the same screen with a wide frame and a clean-up
// that makes the paper white. The finished photo can be handed to Print sheet. Everything
// happens on canvases in this browser.
(() => {
  'use strict';

  const core = window.PrismPhotoTools;
  const shared = window.PrismPhotoCanvas;
  const view = document.querySelector('[data-view="passport"]');
  if (!core || !shared || !view) {
    return;
  }

  const PREVIEW_SIDE = 1600;
  const FRAME_MARGIN = 28;
  const MAX_ZOOM = 4;
  const MIN_PIXELS = 50;
  const MAX_PIXELS = 4000;
  const SETTLE_MS = 300;
  const JPEG = core.TYPES.jpeg;

  const $ = (selector) => view.querySelector(selector);
  const $$ = (selector) => Array.from(view.querySelectorAll(selector));

  const input = $('[data-passport-input]');
  const emptyDrop = $('[data-empty]');
  const workspace = $('[data-workspace]');
  const changeButton = $('[data-action="change-photo"]');
  const fileLabel = $('[data-passport-file]');
  const crop = $('[data-crop]');
  const canvas = $('[data-crop-canvas]');
  const frameBox = $('[data-crop-frame]');
  const guide = $('[data-crop-guide]');
  const zoom = $('[data-zoom]');
  const straighten = $('[data-straighten]');
  const straightenValue = $('[data-straighten-value]');
  const guideToggle = $('[data-guide]');
  const sizes = $('[data-sizes]');
  const customBox = $('[data-custom-size]');
  const customWidth = $('[data-custom-width]');
  const customHeight = $('[data-custom-height]');
  const customUnit = $('[data-custom-unit]');
  const fixButton = $('[data-action="fix-lighting"]');
  const brightness = $('[data-brightness]');
  const brightnessValue = $('[data-brightness-value]');
  const outWidth = $('[data-out-width]');
  const outHeight = $('[data-out-height]');
  const outLimit = $('[data-out-limit]');
  const resultLine = $('[data-passport-result]');
  const downloadButton = $('[data-action="download-passport"]');
  const downloadLabel = $('[data-download-passport-label]');
  const title = $('[data-passport-title]');
  const dropHint = $('[data-passport-drop-hint]');
  const stageHint = $('[data-stage-hint]');
  const sizeTitle = $('[data-size-title]');
  const paperButton = $('[data-action="clean-paper"]');
  const paperHint = $('[data-paper-hint]');
  const sheetButton = $('[data-action="print-sheet"]');
  const bgButtons = $$('[data-bg]');
  const bgOther = $('[data-bg-other]');
  const bgColour = $('[data-bg-colour]');
  const bgAmountRow = $('[data-bg-amount-row]');
  const bgAmount = $('[data-bg-amount]');
  const bgAmountValue = $('[data-bg-amount-value]');
  const bgCompare = $('[data-action="bg-original"]');
  const bgProblem = $('[data-bg-problem]');
  const bgNote = $('[data-bg-note]');

  const BG_PROBLEMS = {
    [core.BACKGROUND_STATUS.busy]: 'This background is not plain enough to change, so the photo is left as it is. Take the photo against a plain wall.',
    [core.BACKGROUND_STATUS.none]: 'No plain background was found at the edges of this photo, so it is left as it is.',
    [core.BACKGROUND_STATUS.tooMuch]: 'Too much of the photo was taken as background, so it is left as it is. Move the slider towards Less.'
  };

  // What each mode says. The screen is otherwise the same.
  const MODES = {
    photo: {
      title: 'Passport photo',
      sizeTitle: 'Photo size',
      sizeLabel: 'Photo size',
      sizes: core.PASSPORT_SIZES,
      custom: ['35', '45'],
      dropHint: 'or drop a photo here. JPG, PNG or WEBP. A plain, light background works best.',
      stageHint: 'Drag the photo so the face fills the oval, with the top of the head and the chin near the two lines.',
      download: 'Download photo',
      suffix: 'passport'
    },
    signature: {
      title: 'Signature',
      sizeTitle: 'Signature size',
      sizeLabel: 'Signature size',
      sizes: core.SIGNATURE_SIZES,
      custom: ['35', '15'],
      dropHint: 'or drop a photo of your signature here. Sign in dark ink on plain white paper and photograph it from straight above.',
      stageHint: 'Drag and zoom so the signature fills the frame with a little space around it.',
      download: 'Download signature',
      suffix: 'signature'
    }
  };

  // The photo: `full` is every pixel, `base` a smaller copy for the screen, `preview` the
  // base with the lighting fix and brightness applied.
  let fileName = '';
  let full = null;
  let mips = [];
  let base = null;
  let preview = null;
  let lighting = null;
  let fixOn = false;
  let mode = 'photo';
  let paper = null;
  let paperOn = true;

  // The new background colour as '#rrggbb', or '' to keep the photo's own. `bgStatus` is what
  // happened the last time the photo was made; `shown` is that photo when its background was
  // changed, laid over the frame so the stage shows the saved result.
  let background = '';
  let bgStatus = null;
  let shown = null;
  let showOriginal = false;

  // Where the photo sits: the photo centre's distance from the frame centre (screen px), its
  // tilt, and how far it is zoomed past "just fills the frame".
  let offset = { x: 0, y: 0 };
  let angle = 0;
  let zoomFactor = 1;
  let frame = { width: 0, height: 0, left: 0, top: 0 };

  let size = { width: core.PASSPORT_SIZES[0].width, height: core.PASSPORT_SIZES[0].height };
  let customOpen = false;
  let saved = null;
  let saveRun = 0;
  let saveTimer = null;
  let openRun = 0;

  // ---------------------------------------------------------------------------
  // Placement
  // ---------------------------------------------------------------------------

  function scale() {
    return core.coverScale(base.width, base.height, frame.width, frame.height, angle) * zoomFactor;
  }

  function place() {
    offset = core.clampOffset({
      imageWidth: base.width,
      imageHeight: base.height,
      frameWidth: frame.width,
      frameHeight: frame.height,
      angle,
      scale: scale(),
      x: offset.x,
      y: offset.y
    });
  }

  /** Fits the crop frame (the shape of the saved photo) into the stage. */
  function layout() {
    const width = crop.clientWidth;
    const height = crop.clientHeight;
    if (!base || !width || !height) return;

    const room = { width: width - FRAME_MARGIN * 2, height: height - FRAME_MARGIN * 2 };
    const fit = Math.min(room.width / size.width, room.height / size.height);
    const next = { width: size.width * fit, height: size.height * fit };
    // Keep the same part of the photo in the frame when the stage changes size.
    if (frame.width) {
      const change = next.width / frame.width;
      offset = { x: offset.x * change, y: offset.y * change };
    }
    frame = { ...next, left: (width - next.width) / 2, top: (height - next.height) / 2 };
    frameBox.style.left = `${frame.left}px`;
    frameBox.style.top = `${frame.top}px`;
    frameBox.style.width = `${frame.width}px`;
    frameBox.style.height = `${frame.height}px`;

    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    place();
    draw();
  }

  function draw() {
    if (!preview || !frame.width) return;
    const ratio = window.devicePixelRatio || 1;
    const context = canvas.getContext('2d');
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.imageSmoothingQuality = 'high';
    context.translate(frame.left + frame.width / 2 + offset.x, frame.top + frame.height / 2 + offset.y);
    context.rotate(angle);
    const zoomed = scale();
    context.scale(zoomed, zoomed);
    context.drawImage(preview, -preview.width / 2, -preview.height / 2);
    if (shown && !showOriginal) {
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      context.drawImage(shown, frame.left, frame.top, frame.width, frame.height);
    }
  }

  function moved() {
    place();
    draw();
    queueSave();
  }

  function setZoom(value) {
    zoomFactor = Math.min(MAX_ZOOM, Math.max(1, value));
    zoom.value = String(Math.round(zoomFactor * 100));
    moved();
  }

  // ---------------------------------------------------------------------------
  // Photo
  // ---------------------------------------------------------------------------

  /** The clean-up chosen for this mode, applied to a canvas. Preview and saved file share it. */
  function applyLook(context, width, height) {
    const amount = Number(brightness.value);
    const wanted = mode === 'signature' ? paperOn && paper : fixOn || amount;
    if (!wanted) return;
    const pixels = context.getImageData(0, 0, width, height);
    if (mode === 'signature') {
      core.cleanPaper(pixels.data, paper);
    } else {
      core.applyLighting(pixels.data, fixOn ? lighting : null, amount);
    }
    context.putImageData(pixels, 0, 0);
  }

  function rebuildPreview() {
    const context = preview.getContext('2d', { willReadFrequently: true });
    // Cleared first: where a PNG or WEBP is see-through, the last look must not show through.
    context.clearRect(0, 0, preview.width, preview.height);
    context.drawImage(base, 0, 0);
    applyLook(context, preview.width, preview.height);
  }

  function showPaperHint() {
    paperHint.textContent = full && !paper
      ? 'No clear ink was found on this photo, so it is left as it is.'
      : 'Makes the paper white and the ink black.';
  }

  /** Takes a new full-size photo (a new file, or the same one turned a quarter). */
  function usePhoto(source) {
    full = source;
    mips = [];
    const fullSize = shared.sizeOf(full);
    const small = core.resizeDimensions(fullSize.width, fullSize.height, { type: 'longest', value: PREVIEW_SIDE });
    base = shared.drawPhoto(full, small.width, small.height, {});
    const pixels = base.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, base.width, base.height);
    lighting = core.measureLighting(pixels.data);
    paper = core.measurePaper(pixels.data);
    showPaperHint();
    preview = shared.newCanvas(base.width, base.height);
    rebuildPreview();

    offset = { x: 0, y: 0 };
    zoomFactor = 1;
    zoom.value = '100';
    frame = { width: 0, height: 0, left: 0, top: 0 };
    layout();
    queueSave();
  }

  async function openFile(file) {
    shared.clearError();
    if (!file) return;
    if (!core.isSupportedPhoto(file.type, file.name)) {
      shared.showError('Only JPG, PNG and WEBP photos can be used here. iPhone HEIC photos are not supported.');
      return;
    }
    // Only the photo chosen last is used: an earlier one that opens late is thrown away.
    const mine = ++openRun;
    let source;
    try {
      source = await shared.openPhoto(file);
    } catch (error) {
      if (mine === openRun) {
        shared.showError(error instanceof core.PhotoToolError ? error.message : 'This photo could not be opened.');
      }
      return;
    }
    if (mine !== openRun) {
      if (typeof source.close === 'function') source.close();
      return;
    }
    fileName = file.name;
    const dimensions = shared.sizeOf(source);
    fileLabel.textContent = `${file.name} · ${dimensions.width} × ${dimensions.height} px · ${core.formatBytes(file.size)}`;
    angle = 0;
    straighten.value = '0';
    straightenValue.textContent = '0°';
    emptyDrop.hidden = true;
    workspace.hidden = false;
    changeButton.hidden = false;
    usePhoto(source);
  }

  function rotateQuarter() {
    if (!full) return;
    const fullSize = shared.sizeOf(full);
    const turned = shared.newCanvas(fullSize.height, fullSize.width);
    const context = turned.getContext('2d');
    context.translate(turned.width / 2, turned.height / 2);
    context.rotate(Math.PI / 2);
    context.drawImage(full, -fullSize.width / 2, -fullSize.height / 2);
    if (typeof full.close === 'function') full.close();
    usePhoto(turned);
  }

  // ---------------------------------------------------------------------------
  // Size
  // ---------------------------------------------------------------------------

  function readPixels(field) {
    const value = Number(field.value.trim());
    return Number.isInteger(value) && value >= MIN_PIXELS && value <= MAX_PIXELS ? value : null;
  }

  function renderSizes() {
    const matching = customOpen ? null : MODES[mode].sizes.find(item => item.width === size.width && item.height === size.height);
    for (const button of $$('[data-size]')) {
      const pressed = button.dataset.size === 'custom' ? !matching : button.dataset.size === matching?.key;
      button.setAttribute('aria-pressed', String(pressed));
    }
    customBox.hidden = !customOpen;
  }

  function setSize(width, height) {
    sizeProblem = false;
    size = { width, height };
    outWidth.value = String(width);
    outHeight.value = String(height);
    renderSizes();
    layout();
    queueSave();
  }

  function buildSizes() {
    sizes.replaceChildren();
    const options = [
      ...MODES[mode].sizes,
      { key: 'custom', label: 'Custom', note: 'mm, inch or pixels' }
    ];
    for (const option of options) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'phot-size';
      button.dataset.size = option.key;
      const label = document.createElement('span');
      label.className = 'phot-size__label';
      label.textContent = option.label;
      const note = document.createElement('span');
      note.className = 'phot-size__note';
      note.textContent = option.note;
      button.append(label, note);
      button.addEventListener('click', () => {
        customOpen = option.key === 'custom';
        if (customOpen) {
          customChanged();
        } else {
          setSize(option.width, option.height);
        }
      });
      sizes.append(button);
    }
  }

  function customChanged() {
    const width = core.lengthToPixels(customWidth.value, customUnit.value);
    const height = core.lengthToPixels(customHeight.value, customUnit.value);
    if (width >= MIN_PIXELS && width <= MAX_PIXELS && height >= MIN_PIXELS && height <= MAX_PIXELS) {
      setSize(width, height);
    } else {
      renderSizes();
      showSizeProblem();
    }
  }

  function pixelsChanged() {
    const width = readPixels(outWidth);
    const height = readPixels(outHeight);
    if (!width || !height) {
      showSizeProblem();
      return;
    }
    sizeProblem = false;
    size = { width, height };
    renderSizes();
    layout();
    queueSave();
  }

  /**
   * The typed size is not usable. Nothing can be downloaded until it is put right, whatever else
   * changes in the meantime, because the last good size is no longer what the page shows.
   */
  let sizeProblem = false;

  function showSizeProblem() {
    sizeProblem = true;
    clearTimeout(saveTimer);
    saveRun += 1;
    saved = null;
    downloadButton.disabled = true;
    resultLine.className = 'phot-result is-problem';
    resultLine.textContent = `Enter a width and height between ${MIN_PIXELS} and ${MAX_PIXELS} pixels.`;
    renderSheet();
  }

  // ---------------------------------------------------------------------------
  // Photo or signature
  // ---------------------------------------------------------------------------

  function setMode(next) {
    mode = next;
    const words = MODES[mode];
    title.textContent = words.title;
    sizeTitle.textContent = words.sizeTitle;
    sizes.setAttribute('aria-label', words.sizeLabel);
    dropHint.textContent = words.dropHint;
    stageHint.textContent = words.stageHint;
    downloadLabel.textContent = words.download;
    for (const button of $$('[data-mode]')) {
      button.setAttribute('aria-pressed', String(button.dataset.mode === mode));
    }
    for (const element of $$('[data-photo-only]')) element.hidden = mode !== 'photo';
    for (const element of $$('[data-signature-only]')) element.hidden = mode !== 'signature';
    guide.hidden = mode !== 'photo' || !guideToggle.checked;

    customOpen = false;
    [customWidth.value, customHeight.value] = words.custom;
    customUnit.value = 'mm';
    buildSizes();
    if (preview) rebuildPreview();
    setSize(words.sizes[0].width, words.sizes[0].height);
  }

  // ---------------------------------------------------------------------------
  // Print sheet
  // ---------------------------------------------------------------------------

  function renderSheet() {
    sheetButton.disabled = !full || sizeProblem;
  }

  // ---------------------------------------------------------------------------
  // Background colour
  // ---------------------------------------------------------------------------

  function renderBackground() {
    const preset = bgButtons.some(button => button.dataset.bg === background);
    for (const button of bgButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.bg === background));
    }
    bgOther.classList.toggle('is-pressed', !preset);
    for (const button of bgButtons) {
      const dot = button.querySelector('.phot-bgchoice__dot');
      if (dot) dot.style.setProperty('--phot-swatch', button.dataset.bg);
    }
    const amount = Number(bgAmount.value);
    bgAmountValue.textContent = amount < 35 ? 'Less' : amount > 65 ? 'More' : 'Normal';
    bgAmountRow.hidden = !background;
    const changed = Boolean(background) && bgStatus === core.BACKGROUND_STATUS.changed;
    const problem = background ? BG_PROBLEMS[bgStatus] : undefined;
    bgCompare.hidden = !changed;
    bgNote.hidden = !changed;
    bgProblem.hidden = !problem;
    bgProblem.textContent = problem || '';
  }

  function setBackground(colour) {
    background = colour;
    if (!colour) bgStatus = null;
    renderBackground();
    if (full) queueSave();
  }

  /** Gives the finished photo its new background. Returns the status, or null when it is kept. */
  function applyBackground(context, width, height) {
    if (mode !== 'photo' || !background) return null;
    const colour = [1, 3, 5].map(at => parseInt(background.slice(at, at + 2), 16));
    const pixels = context.getImageData(0, 0, width, height);
    const { status } = core.changeBackground(pixels.data, width, height, colour, Number(bgAmount.value));
    if (status === core.BACKGROUND_STATUS.changed) context.putImageData(pixels, 0, 0);
    return status;
  }

  function seeOriginal(on) {
    if (showOriginal === on) return;
    showOriginal = on;
    draw();
  }

  /** Hands the finished photo, at the size it prints at, to the Print sheet tool. */
  function openPrintSheet() {
    if (!full || !frame.width || sizeProblem) return;
    document.dispatchEvent(new CustomEvent('phot:print-sheet', {
      detail: { photo: renderOutput(), name: `${core.baseName(fileName)}-${MODES.photo.suffix}.jpg` }
    }));
  }

  // ---------------------------------------------------------------------------
  // Save
  // ---------------------------------------------------------------------------

  /** The full photo, halved as often as needed so the final draw never shrinks by more than half. */
  function sourceFor(scaleToOutput) {
    let level = 0;
    let needed = scaleToOutput;
    while (needed < 0.5) {
      needed *= 2;
      level += 1;
    }
    let source = full;
    for (let i = 0; i < level; i += 1) {
      if (!mips[i]) {
        const from = shared.sizeOf(source);
        if (Math.min(from.width, from.height) < 2) break;
        const half = shared.newCanvas(Math.ceil(from.width / 2), Math.ceil(from.height / 2));
        const context = half.getContext('2d');
        context.imageSmoothingQuality = 'high';
        context.drawImage(source, 0, 0, half.width, half.height);
        mips[i] = half;
      }
      source = mips[i];
    }
    return source;
  }

  /** Draws exactly what the crop frame shows at the saved size, from the full photo. */
  function renderOutput() {
    const output = shared.newCanvas(size.width, size.height);
    const context = output.getContext('2d', { willReadFrequently: true });
    context.fillStyle = '#fff';
    context.fillRect(0, 0, size.width, size.height);
    context.imageSmoothingQuality = 'high';

    const toOutput = size.width / frame.width;
    const fullSize = shared.sizeOf(full);
    const source = sourceFor(scale() * (base.width / fullSize.width) * toOutput);
    const sourceSize = shared.sizeOf(source);
    const drawScale = scale() * (base.width / sourceSize.width) * toOutput;
    context.translate(size.width / 2 + offset.x * toOutput, size.height / 2 + offset.y * toOutput);
    context.rotate(angle);
    context.scale(drawScale, drawScale);
    context.drawImage(source, -sourceSize.width / 2, -sourceSize.height / 2);

    context.setTransform(1, 0, 0, 1, 0, 0);
    applyLook(context, size.width, size.height);
    // After the lighting, so the new background is exactly the colour chosen.
    bgStatus = applyBackground(context, size.width, size.height);
    return output;
  }

  async function save() {
    if (!full || !frame.width || sizeProblem) return;
    const mine = ++saveRun;
    const limitText = outLimit.value.trim();
    const targetBytes = core.parseTargetBytes(limitText, 'KB');
    if (limitText && !targetBytes) {
      saved = null;
      downloadButton.disabled = true;
      resultLine.className = 'phot-result is-problem';
      resultLine.textContent = 'Enter the file size limit as a number of KB, or leave it empty.';
      return;
    }
    try {
      const output = renderOutput();
      shown = bgStatus === core.BACKGROUND_STATUS.changed ? output : null;
      renderBackground();
      draw();
      const result = await core.encodeToTarget({
        encode: (width, height, quality) => shared.canvasToBytes(output, JPEG, quality),
        width: size.width,
        height: size.height,
        lossy: true,
        targetBytes,
        fixedSize: true,
        lowestQuality: 0.2
      });
      if (mine !== saveRun) return;
      saved = { bytes: core.setJpegDpi(result.bytes, core.PRINT_DPI), reached: result.reached };
      const facts = `${size.width} × ${size.height} px, ${core.formatBytes(saved.bytes.length)}, JPG`;
      resultLine.className = result.reached ? 'phot-result is-ready' : 'phot-result is-over';
      resultLine.textContent = result.reached
        ? `Ready: ${facts}`
        : `This size cannot be made smaller than ${core.formatBytes(saved.bytes.length)}, which is over ${limitText} KB. Use fewer pixels or a higher limit.`;
      downloadButton.disabled = false;
    } catch (error) {
      if (mine !== saveRun) return;
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      saved = null;
      downloadButton.disabled = true;
      resultLine.className = 'phot-result is-problem';
      resultLine.textContent = error instanceof core.PhotoToolError ? error.message : 'The photo could not be saved. Try a smaller size.';
    }
  }

  /** The saved file is prepared again shortly after the last change. */
  function queueSave() {
    renderSheet();
    if (shown) {
      // The photo laid over the frame is out of date until it is made again.
      shown = null;
      draw();
    }
    if (!full) return;
    if (sizeProblem) {
      showSizeProblem();
      return;
    }
    saveRun += 1;
    saved = null;
    downloadButton.disabled = true;
    resultLine.className = 'phot-result';
    resultLine.textContent = 'Preparing the photo…';
    clearTimeout(saveTimer);
    saveTimer = setTimeout(save, SETTLE_MS);
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

  // Drag with a mouse, pen or one finger; two fingers zoom.
  const pointers = new Map();
  let pinch = null;

  crop.addEventListener('pointerdown', (event) => {
    if (!base) return;
    crop.setPointerCapture(event.pointerId);
    pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointers.size === 2) {
      const [a, b] = [...pointers.values()];
      pinch = { distance: Math.hypot(a.x - b.x, a.y - b.y), zoom: zoomFactor };
    }
  });

  crop.addEventListener('pointermove', (event) => {
    const last = pointers.get(event.pointerId);
    if (!last) return;
    const now = { x: event.clientX, y: event.clientY };
    pointers.set(event.pointerId, now);
    if (pointers.size === 2 && pinch) {
      const [a, b] = [...pointers.values()];
      setZoom(pinch.zoom * (Math.hypot(a.x - b.x, a.y - b.y) / Math.max(1, pinch.distance)));
    } else if (pointers.size === 1) {
      offset = { x: offset.x + now.x - last.x, y: offset.y + now.y - last.y };
      moved();
    }
  });

  for (const type of ['pointerup', 'pointercancel']) {
    crop.addEventListener(type, (event) => {
      pointers.delete(event.pointerId);
      pinch = null;
    });
  }

  crop.addEventListener('wheel', (event) => {
    if (!base) return;
    event.preventDefault();
    setZoom(zoomFactor * Math.exp(-event.deltaY * 0.0015));
  }, { passive: false });

  crop.addEventListener('keydown', (event) => {
    if (!base) return;
    const step = event.shiftKey ? 1 : 10;
    const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    if (moves[event.key]) {
      event.preventDefault();
      offset = { x: offset.x + moves[event.key][0], y: offset.y + moves[event.key][1] };
      moved();
    } else if (event.key === '+' || event.key === '=') {
      event.preventDefault();
      setZoom(zoomFactor * 1.1);
    } else if (event.key === '-') {
      event.preventDefault();
      setZoom(zoomFactor / 1.1);
    }
  });

  zoom.addEventListener('input', () => setZoom(Number(zoom.value) / 100));

  straighten.addEventListener('input', () => {
    const degrees = Number(straighten.value);
    angle = degrees * Math.PI / 180;
    straightenValue.textContent = `${degrees > 0 ? '+' : ''}${degrees}°`;
    moved();
  });

  $('[data-action="rotate"]').addEventListener('click', rotateQuarter);

  guideToggle.addEventListener('change', () => {
    guide.hidden = mode !== 'photo' || !guideToggle.checked;
  });

  for (const button of $$('[data-mode]')) {
    button.addEventListener('click', () => {
      if (button.dataset.mode !== mode) setMode(button.dataset.mode);
    });
  }

  paperButton.addEventListener('click', () => {
    paperOn = !paperOn;
    paperButton.setAttribute('aria-pressed', String(paperOn));
    if (!preview) return;
    rebuildPreview();
    draw();
    queueSave();
  });

  sheetButton.addEventListener('click', openPrintSheet);

  for (const button of bgButtons) {
    button.addEventListener('click', () => setBackground(button.dataset.bg));
  }
  // Picking a colour chooses it; pressing Other again with the same colour chooses it too.
  bgColour.addEventListener('input', () => setBackground(bgColour.value));
  bgColour.addEventListener('click', () => {
    if (background !== bgColour.value) setBackground(bgColour.value);
  });
  bgAmount.addEventListener('input', () => {
    renderBackground();
    if (full && background) queueSave();
  });
  bgCompare.addEventListener('pointerdown', () => seeOriginal(true));
  bgCompare.addEventListener('keydown', (event) => {
    if (event.key === ' ' || event.key === 'Enter') seeOriginal(true);
  });
  for (const type of ['pointerup', 'pointerleave', 'pointercancel', 'keyup', 'blur']) {
    bgCompare.addEventListener(type, () => seeOriginal(false));
  }

  fixButton.addEventListener('click', () => {
    fixOn = !fixOn;
    fixButton.setAttribute('aria-pressed', String(fixOn));
    if (!preview) return;
    rebuildPreview();
    draw();
    queueSave();
  });

  brightness.addEventListener('input', () => {
    const amount = Number(brightness.value);
    brightnessValue.textContent = `${amount > 0 ? '+' : ''}${amount}`;
    if (!preview) return;
    rebuildPreview();
    draw();
    queueSave();
  });

  for (const field of [customWidth, customHeight]) {
    field.addEventListener('input', customChanged);
  }
  customUnit.addEventListener('change', customChanged);
  for (const field of [outWidth, outHeight]) {
    field.addEventListener('input', () => {
      customOpen = false;
      pixelsChanged();
    });
  }
  outLimit.addEventListener('input', queueSave);

  downloadButton.addEventListener('click', () => {
    if (!saved) return;
    shared.saveBlob(new Blob([saved.bytes], { type: JPEG.mime }), `${core.baseName(fileName)}-${MODES[mode].suffix}.jpg`);
  });

  // The stage has no size while its screen is hidden, and changes size with the window.
  new ResizeObserver(layout).observe(crop);

  buildSizes();
  outWidth.value = String(size.width);
  outHeight.value = String(size.height);
  renderSizes();
  renderSheet();
  renderBackground();
})();
