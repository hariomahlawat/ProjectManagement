// PRISM Photo Tools — Edit photo.
//
// One photo, changed by a list of steps (crop, turn, flip, straighten, resize, clean-ups,
// adjustments, filters, marks drawn on it and areas hidden). The original is never altered: the edited photo is the original with the steps applied in order, so
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
  // Above this much work (pixels times steps), "Working…" is shown while a step is made.
  const BUSY_PIXELS = 4_000_000;

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
  const busyBox = $('[data-edit-busy]');
  const busyStatus = $('[data-edit-busy-status]');
  const downloadButton = $('[data-action="download-edit"]');

  const cleanups = $('[data-cleanups]');
  const adjustSliders = $$('[data-adjust]');
  const adjustApply = $('[data-action="apply-adjust"]');
  const adjustReset = $('[data-action="reset-adjust"]');
  const filters = $('[data-filters]');

  const HINTS = {
    crop: 'Drag the box, or its corners and sides, over the part to keep. Then press Crop.',
    rotate: 'Turn or flip the photo, or move Straighten until the horizon is level.',
    resize: 'Type the new size in pixels, or pick a quick size. Then press Resize.',
    cleanup: 'One press fixes a common problem. Hold "see the original" to compare.',
    adjust: 'Move the sliders and watch the photo. Then press Apply.',
    filters: 'Each filter is shown on your photo. Press one to use it.',
    markup: 'Pick what to draw, then drag or press on the photo.',
    hide: 'Drag over the part of the photo to hide.'
  };
  const KEY_HINT = 'Arrow keys move the cross (Shift for small steps). Enter starts a mark and Enter again ends it; Escape cancels.';
  const MARK_NOTES = {
    box: 'Drag on the photo to draw a box.',
    circle: 'Drag on the photo to draw a circle.',
    arrow: 'Drag from the tail to the point of the arrow.',
    pen: 'Draw on the photo.',
    text: 'Type the words, then press the photo where they should start.'
  };
  const markKinds = $('[data-mark-kinds]');
  const markColours = $('[data-mark-colours]');
  const markSizes = $('[data-mark-sizes]');
  const markTextRow = $('[data-mark-text-row]');
  const markText = $('[data-mark-text]');
  const markNote = $('[data-mark-note]');
  const hideModes = $$('[data-hide-mode]');
  const FILTER_THUMB_SIDE = 132;

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
  // Download pressed with a change pending: the edit (its revision) to save once the change is
  // applied, or null. Any other edit, Undo, Redo, another photo or a failed save clears it.
  let downloadWhenReady = null;
  // Counts every change to the photo on the screen (a step, Undo, Redo, another photo).
  let revision = 0;
  let showingOriginal = false;
  let display = { width: 0, height: 0 };

  // What the next mark will be, and the mark or hidden area being dragged out right now.
  let mark = { kind: 'box', colour: 'red', size: 'medium' };
  let hideMode = 'cover';
  let drawing = null;
  // Where the keyboard's cross is on the photo, in the photo's own pixels. Null while the pointer is used.
  let keyPoint = null;

  let saved = null;
  let saveRun = 0;
  let saveTimer = null;
  const opener = shared.latestPhotoOpener();
  let working = false;
  // Counts the photos opened. A step waiting to start is dropped if another photo is opened meanwhile.
  let photoGeneration = 0;
  // The list of steps the last download was made from: Undo or Redo back to it is not unsaved work.
  let lastDownloaded = null;

  function downloadedSteps(list) {
    return lastDownloaded !== null && list.length === lastDownloaded.length
      && list.every((step, index) => step === lastDownloaded[index]);
  }

  // ---------------------------------------------------------------------------
  // Steps
  // ---------------------------------------------------------------------------

  /** One step applied to a photo, giving a new canvas. The same code serves screen and file. */
  /** Draws one mark. The context works in the photo's own pixels. */
  function drawShape(context, shape) {
    const colour = core.MARK_COLOURS.find(item => item.key === shape.colour) || core.MARK_COLOURS[0];
    context.save();
    context.strokeStyle = colour.value;
    context.fillStyle = colour.value;
    context.lineWidth = shape.line;
    context.lineCap = 'round';
    context.lineJoin = 'round';
    if (shape.kind === 'box') {
      context.lineJoin = 'miter';
      context.strokeRect(shape.x, shape.y, shape.width, shape.height);
    } else if (shape.kind === 'circle') {
      context.beginPath();
      context.ellipse(shape.x + shape.width / 2, shape.y + shape.height / 2, shape.width / 2, shape.height / 2, 0, 0, Math.PI * 2);
      context.stroke();
    } else if (shape.kind === 'arrow') {
      const head = core.arrowHead(shape.from, shape.to, shape.line);
      if (head) {
        context.lineCap = 'butt';
        context.beginPath();
        context.moveTo(shape.from.x, shape.from.y);
        context.lineTo(head.neck.x, head.neck.y);
        context.stroke();
        context.beginPath();
        context.moveTo(head.tip.x, head.tip.y);
        context.lineTo(head.left.x, head.left.y);
        context.lineTo(head.right.x, head.right.y);
        context.closePath();
        context.fill();
      }
    } else if (shape.kind === 'pen') {
      context.beginPath();
      context.moveTo(shape.points[0].x, shape.points[0].y);
      for (const point of shape.points) context.lineTo(point.x, point.y);
      context.stroke();
    } else if (shape.kind === 'text') {
      // An outline in a contrasting colour keeps the words readable on any photo.
      context.font = `700 ${shape.text}px Mulish, "Segoe UI", sans-serif`;
      context.textBaseline = 'middle';
      context.strokeStyle = colour.edge;
      context.lineWidth = Math.max(2, shape.text / 6);
      context.strokeText(shape.words, shape.x, shape.y);
      context.fillText(shape.words, shape.x, shape.y);
    } else if (shape.kind === 'number') {
      const radius = shape.text * 0.7;
      context.beginPath();
      context.arc(shape.x, shape.y, radius, 0, Math.PI * 2);
      context.fill();
      context.strokeStyle = colour.edge;
      context.lineWidth = Math.max(2, shape.text / 12);
      context.stroke();
      context.fillStyle = colour.edge;
      context.font = `700 ${shape.text * 0.8}px Mulish, "Segoe UI", sans-serif`;
      context.textAlign = 'center';
      context.textBaseline = 'middle';
      context.fillText(String(shape.number), shape.x, shape.y + shape.text * 0.04);
    }
    context.restore();
  }

  function applyStep(source, step) {
    const size = shared.sizeOf(source);
    if (step.type === 'shape' || step.type === 'hide') {
      const output = shared.newCanvas(size.width, size.height);
      const context = output.getContext('2d', { willReadFrequently: step.type === 'hide' });
      context.drawImage(source, 0, 0);
      if (step.type === 'shape') {
        drawShape(context, step);
      } else {
        const patch = context.getImageData(step.x, step.y, step.width, step.height);
        core.hidePixels(patch.data, step.width, step.height, step.mode);
        context.putImageData(patch, step.x, step.y);
      }
      return output;
    }
    if (core.isPixelStep(step)) {
      const output = shared.newCanvas(size.width, size.height);
      const context = output.getContext('2d', { willReadFrequently: true });
      context.drawImage(source, 0, 0);
      const pixels = context.getImageData(0, 0, size.width, size.height);
      core.applyPixelStep(pixels.data, size.width, size.height, step);
      context.putImageData(pixels, 0, 0);
      return output;
    }
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
    revision += 1;
    renderUnsaved();
    small = shrink(current);
    tilt = 0;
    straighten.value = '0';
    straightenValue.textContent = '0°';
    straightenApply.disabled = true;
    resetCrop();
    fillResize();
    resetAdjust();
    if (tool === 'filters') drawFilters();
    drawing = null;
    showMark();
    undoButton.disabled = steps.length === 0;
    redoButton.disabled = undone.length === 0;
    originalButton.disabled = steps.length === 0;
    const size = currentSize();
    sizeLabel.textContent = `${size.width} × ${size.height} px`;
    layout();
    queueSave();
  }

  /**
   * Makes a step, an Undo or a Redo. Every step is made on the full-size photo, which on a large
   * photo takes a moment: "Working…" is shown first, and the browser is given a frame to show it
   * before the work starts. Presses that come in meanwhile are ignored, so steps cannot overlap.
   */
  async function runStep(work, stepsToApply) {
    if (!current) return false;
    if (working) {
      // Redrawn so a mark that was being dragged out does not stay on the screen.
      layout();
      return false;
    }
    working = true;
    // The prepared file is of the photo before this step: it must not be downloaded meanwhile
    // (a Cover not yet made would still show what it hides). If the step fails, the file
    // queued here is the unchanged photo again.
    queueSave();
    const size = currentSize();
    const showBusy = size.width * size.height * stepsToApply > BUSY_PIXELS;
    undoButton.disabled = true;
    redoButton.disabled = true;
    const generation = photoGeneration;
    if (showBusy) {
      busyBox.hidden = false;
      busyStatus.textContent = 'Working…';
      await new Promise(resolve => requestAnimationFrame(() => setTimeout(resolve, 0)));
    }
    let done = false;
    try {
      // A new photo opened while waiting: the step was meant for the old one, so it is dropped.
      if (generation === photoGeneration) done = work();
    } finally {
      working = false;
      busyBox.hidden = true;
      busyStatus.textContent = showBusy ? (done ? 'Done.' : '') : '';
      undoButton.disabled = steps.length === 0;
      redoButton.disabled = undone.length === 0;
    }
    return done;
  }

  /** Applies one step to the photo as it is now. False, with a message, when it cannot be made. */
  function applyOne(step) {
    shared.clearError();
    try {
      current = applyStep(current, step);
    } catch (error) {
      console.error(error);
      shared.showError('This change could not be made. The photo may be too large for this browser.');
      return false;
    }
    steps.push(step);
    return true;
  }

  function addStep(step) {
    return runStep(() => {
      if (!applyOne(step)) return false;
      undone = [];
      changed();
      return true;
    }, 1);
  }

  /**
   * Makes the photo again from the original and the given steps. Nothing changes when that
   * fails (a very large photo can run out of memory), so the list of steps always matches the
   * photo on the screen and the file that is saved.
   */
  function replay(nextSteps) {
    shared.clearError();
    let next;
    try {
      next = nextSteps.reduce(applyStep, original);
    } catch (error) {
      console.error(error);
      shared.showError('This change could not be made. The photo may be too large for this browser.');
      return false;
    }
    current = next;
    steps = nextSteps;
    return true;
  }

  function undo() {
    if (!steps.length) return;
    runStep(() => {
      const step = steps[steps.length - 1];
      if (!replay(steps.slice(0, -1))) return false;
      undone.push(step);
      changed();
      return true;
    }, Math.max(1, steps.length - 1));
  }

  // Redo applies the one step again to the photo as it is, which is quick; only undo has to make
  // the photo again from the original.
  function redo() {
    if (!undone.length) return;
    runStep(() => {
      if (!applyOne(undone[undone.length - 1])) return false;
      undone.pop();
      changed();
      return true;
    }, 1);
  }

  // ---------------------------------------------------------------------------
  // Stage
  // ---------------------------------------------------------------------------

  /**
   * A straighten or adjustment the stage shows but that is not applied yet, as the step that would
   * apply it, or null. Owner decision (14 Nov 2026): such a change is applied by itself on Download
   * and on changing tool, so what is saved is always what the screen showed.
   */
  function pendingLook(forTool = tool) {
    if (!current) return null;
    if (forTool === 'rotate' && tilt) return { type: 'straighten', degrees: tilt };
    if (forTool === 'adjust' && adjustChanged()) return { type: 'adjust', ...adjustValues() };
    return null;
  }

  /**
   * Work that leaving the page would lose: steps not yet downloaded (none after Undo back to what
   * was downloaded), or an adjustment or straighten shown but not yet applied.
   */
  function renderUnsaved() {
    shared.setUnsaved('edit', Boolean(current)
      && ((steps.length > 0 && !downloadedSteps(steps)) || pendingLook() !== null));
  }

  /** The line under Download says when the change on the screen is still to be applied. */
  function renderPending() {
    renderUnsaved();
    if (!current || working || !saved) return;
    if (pendingLook()) {
      resultLine.className = 'phot-result';
      resultLine.textContent = 'The change on the screen is applied when you download or pick another tool.';
    } else {
      resultLine.className = 'phot-result is-ready';
      resultLine.textContent = saved.ready;
    }
  }

  /** What the stage shows now: the original while it is held, a straighten preview, or the photo. */
  function shown() {
    if (showingOriginal) return originalSmall;
    if (tool === 'rotate' && tilt) return applyStep(small, { type: 'straighten', degrees: tilt });
    if (tool === 'adjust' && adjustChanged()) return applyStep(small, { type: 'adjust', ...adjustValues() });
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
    if (drawing && !showingOriginal) drawPending(context);
    if (keyPoint && !showingOriginal) drawCross(context);
    placeCrop();
  }

  // ---------------------------------------------------------------------------
  // Tools
  // ---------------------------------------------------------------------------

  function setTool(next) {
    // A straighten or adjustment shown but not applied is applied, not dropped.
    const pending = next !== tool ? pendingLook(tool) : null;
    tool = next;
    for (const button of $$('[data-tool]')) {
      button.setAttribute('aria-pressed', String(button.dataset.tool === tool));
    }
    for (const panel of $$('[data-tool-panel]')) {
      panel.hidden = panel.dataset.toolPanel !== tool;
    }
    hint.textContent = HINTS[tool];
    if (!current) return;
    // A crop box that was not applied is dropped when the tool changes; a straighten or an
    // adjustment is applied (pending, above).
    tilt = 0;
    straighten.value = '0';
    straightenValue.textContent = '0°';
    straightenApply.disabled = true;
    resetCrop();
    fillResize();
    resetAdjust();
    if (tool === 'filters') drawFilters();
    drawing = null;
    keyPoint = null;
    showMark();
    layout();
    if (pending) addStep(pending);
  }

  // Mark up and hide --------------------------------------------------------------

  function numbersPlaced() {
    return steps.filter(step => step.type === 'shape' && step.kind === 'number').length;
  }

  function showMark() {
    const drawingTool = tool === 'markup' || tool === 'hide';
    photoBox.classList.toggle('is-drawing', drawingTool);
    // The photo can be reached with the keyboard only while there is something to do on it.
    if (drawingTool) {
      photoBox.tabIndex = 0;
      photoBox.setAttribute('role', 'group');
      photoBox.setAttribute('aria-label', `Photo. ${KEY_HINT}`);
    } else {
      photoBox.removeAttribute('tabindex');
      photoBox.removeAttribute('role');
      photoBox.removeAttribute('aria-label');
    }
    for (const button of Array.from(markKinds.children)) {
      button.setAttribute('aria-pressed', String(button.dataset.kind === mark.kind));
    }
    for (const button of Array.from(markColours.children)) {
      button.setAttribute('aria-pressed', String(button.dataset.colour === mark.colour));
    }
    for (const button of Array.from(markSizes.children)) {
      button.setAttribute('aria-pressed', String(button.dataset.size === mark.size));
    }
    for (const button of hideModes) {
      button.setAttribute('aria-pressed', String(button.dataset.hideMode === hideMode));
    }
    markTextRow.hidden = mark.kind !== 'text';
    markNote.className = 'phot-hint';
    markNote.textContent = mark.kind === 'number'
      ? `Press the photo to place number ${numbersPlaced() + 1}.`
      : MARK_NOTES[mark.kind];
  }

  /** The mark being dragged out, as the step it would become. Null when it is too small to mean anything. */
  function pendingStep() {
    if (!drawing) return null;
    const size = currentSize();
    if (drawing.hide) {
      const rect = core.dragRect(drawing.from, drawing.to, size);
      return rect.width >= 4 && rect.height >= 4 ? { type: 'hide', mode: hideMode, ...rect } : null;
    }
    const measures = core.markMeasures(size, mark.size);
    const shape = { type: 'shape', kind: mark.kind, colour: mark.colour, line: measures.line };
    if (mark.kind === 'box' || mark.kind === 'circle') {
      const rect = core.dragRect(drawing.from, drawing.to, size);
      return rect.width >= measures.line && rect.height >= measures.line ? { ...shape, ...rect } : null;
    }
    if (mark.kind === 'arrow') {
      const length = Math.hypot(drawing.to.x - drawing.from.x, drawing.to.y - drawing.from.y);
      return length >= measures.line * 3 ? { ...shape, from: drawing.from, to: drawing.to } : null;
    }
    const points = core.penStroke(drawing.points, measures.line);
    return points ? { ...shape, points } : null;
  }

  /** Shows the mark being dragged out. The context is the stage canvas, already holding the photo. */
  function drawPending(context) {
    const step = pendingStep();
    if (!step) return;
    const scale = canvas.width / currentSize().width;
    context.save();
    context.scale(scale, scale);
    if (step.type === 'shape') {
      drawShape(context, step);
    } else {
      // A dashed outline in two colours shows on light and dark photos alike.
      context.lineWidth = 2 / scale;
      context.strokeStyle = '#111827';
      context.strokeRect(step.x, step.y, step.width, step.height);
      context.setLineDash([8 / scale, 6 / scale]);
      context.strokeStyle = '#ffffff';
      context.strokeRect(step.x, step.y, step.width, step.height);
    }
    context.restore();
  }

  function roundPoint(point) {
    return { x: Math.round(point.x), y: Math.round(point.y) };
  }

  /** Places a text or a number at a point. False when the mark in hand is one that is dragged out. */
  function placeAt(point) {
    if (tool !== 'markup' || (mark.kind !== 'text' && mark.kind !== 'number')) return false;
    const measures = core.markMeasures(currentSize(), mark.size);
    const shape = { type: 'shape', kind: mark.kind, colour: mark.colour, text: measures.text, x: point.x, y: point.y };
    if (mark.kind === 'number') {
      addStep({ ...shape, number: numbersPlaced() + 1 });
    } else if (markText.value.trim()) {
      addStep({ ...shape, words: markText.value.trim() });
    } else {
      markNote.className = 'phot-hint phot-hint--warning';
      markNote.textContent = 'Type the words first, then press the photo.';
      markText.focus();
    }
    return true;
  }

  function finishDrawing() {
    const step = pendingStep();
    drawing = null;
    if (step) {
      addStep(step);
    } else {
      layout();
    }
  }

  // One pointer draws at a time: a second finger or a right-click does not add to the mark.
  photoBox.addEventListener('pointerdown', (event) => {
    if (!current || showingOriginal || (tool !== 'markup' && tool !== 'hide')) return;
    if ((drawing && !drawing.keys) || !shared.isMainPointer(event)) return;
    event.preventDefault();
    keyPoint = null;
    const point = roundPoint(photoPoint(event));
    if (placeAt(point)) return;
    photoBox.setPointerCapture(event.pointerId);
    drawing = { pointer: event.pointerId, hide: tool === 'hide', from: point, to: point, points: [point] };
  });

  // Keyboard: a cross on the photo stands in for the pointer. Enter presses where the cross is.
  function drawCross(context) {
    const scale = canvas.width / currentSize().width;
    const ratio = window.devicePixelRatio || 1;
    const x = keyPoint.x * scale;
    const y = keyPoint.y * scale;
    const arm = 12 * ratio;
    // Dark under light, so the cross shows on any photo.
    for (const [colour, width] of [['#111827', 4 * ratio], ['#ffffff', 2 * ratio]]) {
      context.strokeStyle = colour;
      context.lineWidth = width;
      context.beginPath();
      context.moveTo(x - arm, y);
      context.lineTo(x + arm, y);
      context.moveTo(x, y - arm);
      context.lineTo(x, y + arm);
      context.stroke();
    }
  }

  photoBox.addEventListener('keydown', (event) => {
    if (!current || showingOriginal || (tool !== 'markup' && tool !== 'hide')) return;
    const moves = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    const move = moves[event.key];
    const press = event.key === 'Enter' || event.key === ' ';
    const cancel = event.key === 'Escape' && drawing;
    if (!move && !press && !cancel) return;
    event.preventDefault();
    hint.textContent = KEY_HINT;
    const size = currentSize();
    const from = keyPoint || { x: Math.round(size.width / 2), y: Math.round(size.height / 2) };
    if (move) {
      const step = Math.max(1, Math.round(Math.max(size.width, size.height) / (event.shiftKey ? 400 : 50)));
      keyPoint = {
        x: Math.min(size.width, Math.max(0, from.x + move[0] * step)),
        y: Math.min(size.height, Math.max(0, from.y + move[1] * step))
      };
      if (drawing) {
        drawing.to = keyPoint;
        drawing.points.push(keyPoint);
      }
      layout();
    } else if (cancel) {
      drawing = null;
      layout();
    } else if (drawing) {
      finishDrawing();
    } else {
      keyPoint = from;
      if (!placeAt(keyPoint)) {
        drawing = { hide: tool === 'hide', keys: true, from: keyPoint, to: keyPoint, points: [keyPoint] };
        layout();
      }
    }
  });

  photoBox.addEventListener('blur', () => {
    if (!keyPoint) return;
    keyPoint = null;
    drawing = null;
    hint.textContent = HINTS[tool];
    layout();
  });

  // A mark started with the keyboard follows the cross only: a mouse passing over the photo
  // must not change it.
  photoBox.addEventListener('pointermove', (event) => {
    if (!drawing || drawing.keys || event.pointerId !== drawing.pointer) return;
    drawing.to = roundPoint(photoPoint(event));
    drawing.points.push(drawing.to);
    layout();
  });

  photoBox.addEventListener('pointerup', (event) => {
    if (drawing && !drawing.keys && event.pointerId === drawing.pointer) finishDrawing();
  });

  photoBox.addEventListener('pointercancel', (event) => {
    if (!drawing || drawing.keys || event.pointerId !== drawing.pointer) return;
    drawing = null;
    layout();
  });

  // Clean up, adjust, filters ---------------------------------------------------

  function adjustValues() {
    return Object.fromEntries(adjustSliders.map(slider => [slider.dataset.adjust, Number(slider.value)]));
  }

  function adjustChanged() {
    return adjustSliders.some(slider => Number(slider.value) !== 0);
  }

  function showAdjust() {
    for (const slider of adjustSliders) {
      const amount = Number(slider.value);
      $(`[data-adjust-value="${slider.dataset.adjust}"]`).textContent = `${amount > 0 ? '+' : ''}${amount}`;
    }
    adjustApply.disabled = !adjustChanged();
    adjustReset.disabled = !adjustChanged();
  }

  function resetAdjust() {
    for (const slider of adjustSliders) slider.value = '0';
    showAdjust();
  }

  /** Each filter button shows the filter on a small copy of the photo as it is now. */
  function drawFilters() {
    const fit = core.resizeDimensions(small.width, small.height, { type: 'longest', value: FILTER_THUMB_SIDE });
    const thumb = shared.drawPhoto(small, fit.width, fit.height, {});
    for (const button of Array.from(filters.children)) {
      const picture = button.querySelector('canvas');
      const result = applyStep(thumb, { type: 'filter', key: button.dataset.filter });
      picture.width = result.width;
      picture.height = result.height;
      picture.getContext('2d').drawImage(result, 0, 0);
    }
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

  // One pointer moves the crop: a second finger would otherwise make it jump.
  cropBox.addEventListener('pointerdown', (event) => {
    if (!crop || drag || !shared.isMainPointer(event)) return;
    event.preventDefault();
    cropBox.focus({ preventScroll: true });
    cropBox.setPointerCapture(event.pointerId);
    drag = { pointer: event.pointerId, handle: event.target.dataset.handle || null, from: photoPoint(event), start: { ...crop } };
  });

  cropBox.addEventListener('pointermove', (event) => {
    if (!drag || event.pointerId !== drag.pointer) return;
    const point = photoPoint(event);
    const size = currentSize();
    crop = drag.handle
      ? core.resizeCrop(drag.start, drag.handle, point, cropRatio(), size)
      : core.moveCrop(drag.start, point.x - drag.from.x, point.y - drag.from.y, size);
    placeCrop();
  });

  for (const type of ['pointerup', 'pointercancel']) {
    cropBox.addEventListener(type, (event) => {
      if (drag && event.pointerId === drag.pointer) drag = null;
    });
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
    const tooBig = Boolean(width && height) && width * height > core.EDIT_MAX_AREA;
    resizeApply.disabled = !width || !height || same || tooBig;
    resizeNote.className = 'phot-hint';
    if (!width || !height) {
      resizeNote.className = 'phot-hint phot-hint--warning';
      resizeNote.textContent = `Enter a width and height between ${core.EDIT_MIN_SIDE} and ${core.EDIT_MAX_SIDE} pixels.`;
    } else if (tooBig) {
      // Phones and iPads cannot draw a larger picture; the edit would fail there.
      resizeNote.className = 'phot-hint phot-hint--warning';
      resizeNote.textContent = 'That is too many pixels for some browsers. Keep width × height under 16.7 million, for example 4096 × 4096.';
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
      saved = { bytes, type, ready: `Ready: ${size.width} × ${size.height} px, ${core.formatBytes(bytes.length)}, ${type.label}` };
      resultLine.className = 'phot-result is-ready';
      resultLine.textContent = saved.ready;
      downloadButton.disabled = false;
      renderPending();
      if (downloadWhenReady !== null) {
        const wanted = downloadWhenReady === revision;
        downloadWhenReady = null;
        if (wanted) downloadSaved();
      }
    } catch (error) {
      if (mine !== saveRun) return;
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      // A file that could not be made is not downloaded later by a different save.
      downloadWhenReady = null;
      saved = null;
      downloadButton.disabled = true;
      resultLine.className = 'phot-result is-problem';
      resultLine.textContent = error instanceof core.PhotoToolError ? error.message : 'The photo could not be saved. Try a smaller size.';
    }
  }

  function downloadSaved() {
    shared.saveBlob(new Blob([saved.bytes], { type: saved.type.mime }), `${core.baseName(fileName)}-edited.${saved.type.extension}`);
    // The file holds the steps on the screen: Undo or Redo back to them is not unsaved work.
    lastDownloaded = steps.slice();
    // Worked out again, not cleared: a slider moved while the file was being made is still unsaved.
    renderUnsaved();
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

  // Kept in this browser once the user has picked them: the file type and the quality. Until then
  // a photo is saved in its own type, as before.
  const remembered = shared.rememberedChoices('edit');

  function rememberChoices() {
    remembered.save({ format: format.value, quality: Number(quality.value) });
  }

  function formatChanged() {
    const type = chosenType();
    qualityRow.hidden = !type.lossy;
    rememberChoices();
    queueSave();
  }

  // ---------------------------------------------------------------------------
  // Photo
  // ---------------------------------------------------------------------------

  async function openFile(file) {
    // Only the photo chosen last is used: an earlier one that opens late is thrown away.
    const source = await opener.open(file);
    if (!source) return;
    if (original && typeof original.close === 'function') original.close();
    photoGeneration += 1;
    revision += 1;
    // A download asked for on the last photo is not made of this one.
    downloadWhenReady = null;
    lastDownloaded = null;
    shared.setUnsaved('edit', false);
    fileName = file.name;
    original = source;
    originalSmall = shrink(original);
    current = original;
    steps = [];
    undone = [];
    const size = shared.sizeOf(source);
    fileLabel.textContent = `${file.name} · ${size.width} × ${size.height} px · ${core.formatBytes(file.size)}`;
    const kept = remembered.load();
    const keptFormat = kept && Array.from(format.options).some(option => option.value === kept.format) ? kept.format : null;
    format.value = keptFormat
      || Object.keys(core.TYPES).find(key => core.TYPES[key] === core.outputType(file.type, 'same'))
      || 'jpeg';
    if (kept && Number(kept.quality) >= Number(quality.min) && Number(kept.quality) <= Number(quality.max)) {
      quality.value = String(kept.quality);
      qualityValue.textContent = quality.value;
    }
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

  shared.wireDropZones($$('[data-drop]'), files => openFile(files[0]));

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

  shared.wireHoldButton(originalButton, holdOriginal);

  for (const item of core.CLEAN_UPS) {
    const row = document.createElement('div');
    row.className = 'phot-fix';
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'phot-toggle';
    button.textContent = item.label;
    button.addEventListener('click', () => {
      if (current) addStep({ type: 'cleanup', key: item.key });
    });
    const note = document.createElement('span');
    note.className = 'phot-hint';
    note.textContent = item.note;
    row.append(button, note);
    cleanups.append(row);
  }

  for (const slider of adjustSliders) {
    slider.addEventListener('input', () => {
      showAdjust();
      renderPending();
      if (current) layout();
    });
  }
  adjustApply.addEventListener('click', () => {
    if (current && adjustChanged()) addStep({ type: 'adjust', ...adjustValues() });
  });
  adjustReset.addEventListener('click', () => {
    resetAdjust();
    renderPending();
    if (current) layout();
  });

  for (const item of core.FILTERS) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'phot-filter';
    button.dataset.filter = item.key;
    const label = document.createElement('span');
    label.textContent = item.label;
    button.append(document.createElement('canvas'), label);
    button.addEventListener('click', () => {
      if (current) addStep({ type: 'filter', key: item.key });
    });
    filters.append(button);
  }

  for (const kind of core.MARK_KINDS) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.kind = kind.key;
    const icon = document.createElement('i');
    icon.className = `bi ${kind.icon}`;
    icon.setAttribute('aria-hidden', 'true');
    button.append(icon, ` ${kind.label}`);
    button.addEventListener('click', () => {
      mark.kind = kind.key;
      showMark();
    });
    markKinds.append(button);
  }
  for (const colour of core.MARK_COLOURS) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'phot-swatch';
    button.dataset.colour = colour.key;
    button.title = colour.label;
    button.setAttribute('aria-label', colour.label);
    button.style.setProperty('--phot-swatch', colour.value);
    button.addEventListener('click', () => {
      mark.colour = colour.key;
      showMark();
    });
    markColours.append(button);
  }
  for (const size of core.MARK_SIZES) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.size = size.key;
    button.textContent = size.label;
    button.addEventListener('click', () => {
      mark.size = size.key;
      showMark();
    });
    markSizes.append(button);
  }
  for (const button of hideModes) {
    button.addEventListener('click', () => {
      hideMode = button.dataset.hideMode;
      showMark();
    });
  }
  markText.addEventListener('input', showMark);

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
    renderPending();
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
    rememberChoices();
    queueSave();
  });

  downloadButton.addEventListener('click', (event) => {
    if (!saved || working || shared.isRepeatClick(event)) return;
    const pending = pendingLook();
    if (pending) {
      // The change on the screen is applied first; the file is saved once it has been made again.
      // Armed only once the step is made: a save that started before it (with the photo as it was)
      // can never fulfil it, even when a hidden tab holds the step back.
      const generation = photoGeneration;
      addStep(pending).then((done) => {
        // `revision` is now the edit this step made; Undo or another edit before its save moves it on.
        if (done && generation === photoGeneration) downloadWhenReady = revision;
      });
      return;
    }
    downloadSaved();
  });

  // The stage has no size while its screen is hidden, and changes size with the window.
  new ResizeObserver(layout).observe(stage);

  setTool('crop');
})();
