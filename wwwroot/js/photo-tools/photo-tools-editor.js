// PRISM Photo Tools — Edit photo.
//
// One photo, changed by a list of steps (crop, turn, flip, straighten, resize). The original
// is never altered: the edited photo is the original with the steps applied in order, so
// Undo simply drops the last step. Everything happens on canvases in this browser.
(() => {
  'use strict';

  const core = window.PrismPhotoTools;
  const shared = window.PrismPhotoCanvas;
  const view = document.querySelector('[data-view="edit"]');
  if (!core || !shared || !view) {
    return;
  }

  const PREVIEW_SIDE = 1600;
  const SETTLE_MS = 300;

  const $ = (selector) => view.querySelector(selector);
  const $$ = (selector) => Array.from(view.querySelectorAll(selector));

  const input = $('[data-edit-input]');
  const emptyDrop = $('[data-empty]');
  const workspace = $('[data-workspace]');
  const changeButton = $('[data-action="change-photo"]');
  const fileLabel = $('[data-edit-file]');
  const stage = $('[data-edit-stage]');
  const photoBox = $('[data-edit-photo]');
  const canvas = $('[data-edit-canvas]');
  const cropBox = $('[data-cropbox]');
  const hint = $('[data-edit-hint]');
  const sizeLabel = $('[data-edit-size]');
  const undoButton = $('[data-action="undo"]');
  const redoButton = $('[data-action="redo"]');
  const originalButton = $('[data-action="see-original"]');
  const cropShape = $('[data-crop-shape]');
  const cropSize = $('[data-crop-size]');
  const cropApply = $('[data-action="apply-crop"]');
  const straighten = $('[data-edit-straighten]');
  const straightenValue = $('[data-edit-straighten-value]');
  const straightenApply = $('[data-action="apply-straighten"]');
  const resizeWidth = $('[data-resize-width]');
  const resizeHeight = $('[data-resize-height]');
  const resizeKeep = $('[data-resize-keep]');
  const resizeNote = $('[data-resize-note]');
  const resizeApply = $('[data-action="apply-resize"]');
  const format = $('[data-edit-format]');
  const quality = $('[data-edit-quality]');
  const qualityRow = $('[data-edit-quality-row]');
  const qualityValue = $('[data-edit-quality-value]');
  const resultLine = $('[data-edit-result]');
  const downloadButton = $('[data-action="download-edit"]');

  const HINTS = {
    crop: 'Drag the box, or its corners and sides, over the part to keep. Then press Crop.',
    rotate: 'Turn or flip the photo, or move Straighten until the horizon is level.',
    resize: 'Type the new size in pixels, or pick a quick size. Then press Resize.'
  };

  // `original` is the opened photo; `current` is the original with every step applied;
  // `small` and `originalSmall` are copies no larger than the screen needs.
  let fileName = '';
  let original = null;
  let originalSmall = null;
  let current = null;
  let small = null;
  let steps = [];
  let undone = [];

  let tool = 'crop';
  let crop = null;          // in pixels of `current`
  let tilt = 0;             // straighten not yet applied, degrees
  let showingOriginal = false;
  let display = { width: 0, height: 0 };

  let saved = null;
  let saveRun = 0;
  let saveTimer = null;
  let openRun = 0;

  // ---------------------------------------------------------------------------
  // Steps
  // ---------------------------------------------------------------------------

  /** One step applied to a photo, giving a new canvas. The same code serves screen and file. */
  function applyStep(source, step) {
    const size = shared.sizeOf(source);
    if (step.type === 'resize') {
      return shared.drawPhoto(source, step.width, step.height, {});
    }
    const after = core.editedSize(size.width, size.height, [step]);
    const output = shared.newCanvas(after.width, after.height);
    const context = output.getContext('2d');
    context.imageSmoothingQuality = 'high';
    if (step.type === 'crop') {
      context.drawImage(source, step.x, step.y, step.width, step.height, 0, 0, step.width, step.height);
      return output;
    }
    context.translate(after.width / 2, after.height / 2);
    if (step.type === 'rotate') {
      context.rotate(step.turns * Math.PI / 2);
    } else if (step.type === 'flip') {
      context.scale(step.axis === 'h' ? -1 : 1, step.axis === 'v' ? -1 : 1);
    } else if (step.type === 'straighten') {
      context.rotate(step.degrees * Math.PI / 180);
    }
    context.drawImage(source, -size.width / 2, -size.height / 2);
    return output;
  }

  function shrink(source) {
    const size = shared.sizeOf(source);
    const fit = core.resizeDimensions(size.width, size.height, { type: 'longest', value: PREVIEW_SIDE });
    return shared.drawPhoto(source, fit.width, fit.height, {});
  }

  function currentSize() {
    return shared.sizeOf(current);
  }

  /** Called after the list of steps changes: fresh screen copy, fresh tool state, fresh file. */
  function changed() {
    small = shrink(current);
    tilt = 0;
    straighten.value = '0';
    straightenValue.textContent = '0°';
    straightenApply.disabled = true;
    resetCrop();
    fillResize();
    undoButton.disabled = steps.length === 0;
    redoButton.disabled = undone.length === 0;
    originalButton.disabled = steps.length === 0;
    const size = currentSize();
    sizeLabel.textContent = `${size.width} × ${size.height} px`;
    layout();
    queueSave();
  }

  function addStep(step) {
    shared.clearError();
    try {
      current = applyStep(current, step);
    } catch (error) {
      console.error(error);
      shared.showError('This change could not be made. The photo may be too large for this browser.');
      return;
    }
    steps.push(step);
    undone = [];
    changed();
  }

  function replay() {
    current = steps.reduce(applyStep, original);
    changed();
  }

  function undo() {
    if (!steps.length) return;
    undone.push(steps.pop());
    replay();
  }

  function redo() {
    if (!undone.length) return;
    steps.push(undone.pop());
    replay();
  }

  // ---------------------------------------------------------------------------
  // Stage
  // ---------------------------------------------------------------------------

  /** What the stage shows now: the original while it is held, a straighten preview, or the photo. */
  function shown() {
    if (showingOriginal) return originalSmall;
    if (tool === 'rotate' && tilt) return applyStep(small, { type: 'straighten', degrees: tilt });
    return small;
  }

  function layout() {
    if (!small) return;
    const picture = shown();
    const style = getComputedStyle(stage);
    const room = {
      width: stage.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight),
      height: stage.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom)
    };
    if (room.width <= 0 || room.height <= 0) return;
    const fit = Math.min(room.width / picture.width, room.height / picture.height);
    display = { width: Math.max(1, Math.floor(picture.width * fit)), height: Math.max(1, Math.floor(picture.height * fit)) };
    photoBox.style.width = `${display.width}px`;
    photoBox.style.height = `${display.height}px`;

    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.round(display.width * ratio);
    canvas.height = Math.round(display.height * ratio);
    const context = canvas.getContext('2d');
    context.imageSmoothingQuality = 'high';
    context.drawImage(picture, 0, 0, canvas.width, canvas.height);
    placeCrop();
  }

  // ---------------------------------------------------------------------------
  // Tools
  // ---------------------------------------------------------------------------

  function setTool(next) {
    tool = next;
    for (const button of $$('[data-tool]')) {
      button.setAttribute('aria-pressed', String(button.dataset.tool === tool));
    }
    for (const panel of $$('[data-tool-panel]')) {
      panel.hidden = panel.dataset.toolPanel !== tool;
    }
    hint.textContent = HINTS[tool];
    if (!current) return;
    // A straighten or crop that was not applied is dropped when the tool changes.
    tilt = 0;
    straighten.value = '0';
    straightenValue.textContent = '0°';
    straightenApply.disabled = true;
    resetCrop();
    fillResize();
    layout();
  }

  // Crop ----------------------------------------------------------------------

  function cropRatio() {
    const shape = core.CROP_SHAPES.find(item => item.key === cropShape.value) || core.CROP_SHAPES[0];
    if (shape.ratio !== 'original') return shape.ratio;
    const size = currentSize();
    return size.width / size.height;
  }

  function resetCrop() {
    const size = currentSize();
    crop = core.defaultCrop(size, cropRatio());
    placeCrop();
  }

  function placeCrop() {
    const visible = Boolean(current) && tool === 'crop' && !showingOriginal;
    cropBox.hidden = !visible;
    if (!visible || !crop) return;
    const size = currentSize();
    const scale = display.width / size.width;
    cropBox.style.left = `${crop.x * scale}px`;
    cropBox.style.top = `${crop.y * scale}px`;
    cropBox.style.width = `${crop.width * scale}px`;
    cropBox.style.height = `${crop.height * scale}px`;
    const whole = crop.width === size.width && crop.height === size.height;
    cropApply.disabled = whole;
    cropSize.textContent = whole
      ? 'The box covers the whole photo. Make it smaller to crop.'
      : `The cropped photo will be ${crop.width} × ${crop.height} px.`;
  }

  /** A pointer position as a point on the photo, in the photo's own pixels. */
  function photoPoint(event) {
    const box = photoBox.getBoundingClientRect();
    const scale = currentSize().width / box.width;
    return { x: (event.clientX - box.left) * scale, y: (event.clientY - box.top) * scale };
  }

  let drag = null;

  cropBox.addEventListener('pointerdown', (event) => {
    if (!crop) return;
    event.preventDefault();
    cropBox.focus({ preventScroll: true });
    cropBox.setPointerCapture(event.pointerId);
    drag = { handle: event.target.dataset.handle || null, from: photoPoint(event), start: { ...crop } };
  });

  cropBox.addEventListener('pointermove', (event) => {
    if (!drag) return;
    const point = photoPoint(event);
    const size = currentSize();
    crop = drag.handle
      ? core.resizeCrop(drag.start, drag.handle, point, cropRatio(), size)
      : core.moveCrop(drag.start, point.x - drag.from.x, point.y - drag.from.y, size);
    placeCrop();
  });

  for (const type of ['pointerup', 'pointercancel']) {
    cropBox.addEventListener(type, () => { drag = null; });
  }

  cropBox.addEventListener('keydown', (event) => {
    const moves = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    const move = moves[event.key];
    if (event.key === 'Enter' && !cropApply.disabled) {
      event.preventDefault();
      applyCrop();
      return;
    }
    if (!move || !crop) return;
    event.preventDefault();
    const size = currentSize();
    const step = Math.max(1, Math.round(Math.max(size.width, size.height) / 100));
    if (event.shiftKey) {
      const ratio = cropRatio();
      // With a fixed shape both sides change together: left and up shrink, right and down grow.
      const width = crop.width + (move[0] || move[1]) * step;
      const corner = ratio
        ? { x: crop.x + width, y: crop.y + width / ratio }
        : { x: crop.x + crop.width + move[0] * step, y: crop.y + crop.height + move[1] * step };
      crop = core.resizeCrop(crop, ratio ? 'se' : move[0] ? 'e' : 's', corner, ratio, size);
    } else {
      crop = core.moveCrop(crop, move[0] * step, move[1] * step, size);
    }
    placeCrop();
  });

  function applyCrop() {
    const size = currentSize();
    if (!crop || (crop.width === size.width && crop.height === size.height)) return;
    addStep({ type: 'crop', ...crop });
  }

  // Resize --------------------------------------------------------------------

  function fillResize() {
    const size = currentSize();
    resizeWidth.value = String(size.width);
    resizeHeight.value = String(size.height);
    checkResize();
  }

  function readSide(field) {
    const value = Number(field.value.trim());
    return Number.isInteger(value) && value >= core.EDIT_MIN_SIDE && value <= core.EDIT_MAX_SIDE ? value : null;
  }

  function checkResize() {
    const size = currentSize();
    const width = readSide(resizeWidth);
    const height = readSide(resizeHeight);
    const same = width === size.width && height === size.height;
    resizeApply.disabled = !width || !height || same;
    resizeNote.className = 'phot-hint';
    if (!width || !height) {
      resizeNote.className = 'phot-hint phot-hint--warning';
      resizeNote.textContent = `Enter a width and height between ${core.EDIT_MIN_SIDE} and ${core.EDIT_MAX_SIDE} pixels.`;
    } else if (width > size.width || height > size.height) {
      resizeNote.textContent = 'Making a photo bigger does not add detail; it will look softer.';
    } else if (Math.abs(width / height - size.width / size.height) > 0.01) {
      resizeNote.textContent = 'This changes the shape, so the photo will look stretched.';
    } else {
      resizeNote.textContent = `Now ${size.width} × ${size.height} px.`;
    }
    return { width, height };
  }

  function resizeTyped(field) {
    if (resizeKeep.checked) {
      const size = currentSize();
      const value = readSide(field);
      if (value) {
        if (field === resizeWidth) {
          resizeHeight.value = String(Math.max(1, Math.round(value * size.height / size.width)));
        } else {
          resizeWidth.value = String(Math.max(1, Math.round(value * size.width / size.height)));
        }
      }
    }
    checkResize();
  }

  function applyResize() {
    const { width, height } = checkResize();
    if (!width || !height || resizeApply.disabled) return;
    addStep({ type: 'resize', width, height });
  }

  // ---------------------------------------------------------------------------
  // Save
  // ---------------------------------------------------------------------------

  function chosenType() {
    return core.TYPES[format.value] || core.TYPES.jpeg;
  }

  async function save() {
    if (!current) return;
    const mine = ++saveRun;
    const type = chosenType();
    const size = currentSize();
    try {
      // JPG has no see-through areas; they are filled with white. A photo with no changes yet
      // is still the opened file, which has to be drawn before it can be saved.
      const opaque = type === core.TYPES.jpeg;
      const output = opaque || current === original ? shared.drawPhoto(current, size.width, size.height, { opaque }) : current;
      const bytes = await shared.canvasToBytes(output, type, Number(quality.value) / 100);
      if (mine !== saveRun) return;
      saved = { bytes, type };
      resultLine.className = 'phot-result is-ready';
      resultLine.textContent = `Ready: ${size.width} × ${size.height} px, ${core.formatBytes(bytes.length)}, ${type.label}`;
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

  /** The file is prepared again shortly after the last change. */
  function queueSave() {
    if (!current) return;
    saveRun += 1;
    saved = null;
    downloadButton.disabled = true;
    resultLine.className = 'phot-result';
    resultLine.textContent = 'Preparing the photo…';
    clearTimeout(saveTimer);
    saveTimer = setTimeout(save, SETTLE_MS);
  }

  function formatChanged() {
    const type = chosenType();
    qualityRow.hidden = !type.lossy;
    queueSave();
  }

  // ---------------------------------------------------------------------------
  // Photo
  // ---------------------------------------------------------------------------

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
    if (original && typeof original.close === 'function') original.close();
    fileName = file.name;
    original = source;
    originalSmall = shrink(original);
    current = original;
    steps = [];
    undone = [];
    const size = shared.sizeOf(source);
    fileLabel.textContent = `${file.name} · ${size.width} × ${size.height} px · ${core.formatBytes(file.size)}`;
    format.value = Object.keys(core.TYPES).find(key => core.TYPES[key] === core.outputType(file.type, 'same')) || 'jpeg';
    qualityRow.hidden = !chosenType().lossy;
    emptyDrop.hidden = true;
    workspace.hidden = false;
    changeButton.hidden = false;
    changed();
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

  for (const button of $$('[data-tool]')) {
    button.addEventListener('click', () => setTool(button.dataset.tool));
  }

  undoButton.addEventListener('click', undo);
  redoButton.addEventListener('click', redo);

  // Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z) while this screen is open, except while typing.
  document.addEventListener('keydown', (event) => {
    if (view.hidden || !current || !(event.ctrlKey || event.metaKey)) return;
    if (/^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName) && event.target.type !== 'range') return;
    const key = event.key.toLowerCase();
    if (key === 'z' && !event.shiftKey) {
      event.preventDefault();
      undo();
    } else if (key === 'y' || (key === 'z' && event.shiftKey)) {
      event.preventDefault();
      redo();
    }
  });

  function holdOriginal(on) {
    if (showingOriginal === on || !current) return;
    showingOriginal = on;
    originalButton.classList.toggle('active', on);
    layout();
  }

  originalButton.addEventListener('pointerdown', () => holdOriginal(true));
  for (const type of ['pointerup', 'pointerleave', 'pointercancel', 'blur']) {
    originalButton.addEventListener(type, () => holdOriginal(false));
  }
  originalButton.addEventListener('keydown', (event) => {
    if (event.key === ' ' || event.key === 'Enter') holdOriginal(true);
  });
  originalButton.addEventListener('keyup', () => holdOriginal(false));

  for (const shape of core.CROP_SHAPES) {
    cropShape.append(new Option(shape.label, shape.key));
  }
  cropShape.addEventListener('change', () => {
    if (current) resetCrop();
  });
  cropApply.addEventListener('click', applyCrop);

  for (const button of $$('[data-turn]')) {
    button.addEventListener('click', () => addStep({ type: 'rotate', turns: Number(button.dataset.turn) }));
  }
  for (const button of $$('[data-flip]')) {
    button.addEventListener('click', () => addStep({ type: 'flip', axis: button.dataset.flip }));
  }

  straighten.addEventListener('input', () => {
    tilt = Number(straighten.value);
    straightenValue.textContent = `${tilt > 0 ? '+' : ''}${tilt}°`;
    straightenApply.disabled = !tilt;
    layout();
  });
  straightenApply.addEventListener('click', () => {
    if (tilt) addStep({ type: 'straighten', degrees: tilt });
  });

  resizeWidth.addEventListener('input', () => resizeTyped(resizeWidth));
  resizeHeight.addEventListener('input', () => resizeTyped(resizeHeight));
  resizeKeep.addEventListener('change', () => {
    if (resizeKeep.checked) resizeTyped(resizeWidth);
  });
  for (const button of $$('[data-resize-percent]')) {
    button.addEventListener('click', () => {
      const size = currentSize();
      const next = core.scaleDimensions(size.width, size.height, Number(button.dataset.resizePercent) / 100);
      resizeWidth.value = String(next.width);
      resizeHeight.value = String(next.height);
      checkResize();
    });
  }
  resizeApply.addEventListener('click', applyResize);

  format.addEventListener('change', formatChanged);
  quality.addEventListener('input', () => {
    qualityValue.textContent = quality.value;
    queueSave();
  });

  downloadButton.addEventListener('click', () => {
    if (!saved) return;
    shared.saveBlob(new Blob([saved.bytes], { type: saved.type.mime }), `${core.baseName(fileName)}-edited.${saved.type.extension}`);
  });

  // The stage has no size while its screen is hidden, and changes size with the window.
  new ResizeObserver(layout).observe(stage);

  setTool('crop');
})();
