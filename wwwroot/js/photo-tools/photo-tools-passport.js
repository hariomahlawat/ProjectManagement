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
  const checkBox = $('[data-passport-check]');
  const checkTitle = $('[data-check-title]');
  const checkList = $('[data-check-list]');
  // Finding the head's outline in a very large photo would hold the screen up.
  const CHECK_OUTLINE_PIXELS = 2_000_000;
  const handFixButton = $('[data-action="bg-fix"]');
  const fixLabel = $('[data-bg-fix-label]');
  const fixBox = $('[data-bg-fix]');
  const brushButtons = $$('[data-bg-brush]');
  const brushHint = $('[data-bg-brush-hint]');
  const brushSizes = $$('[data-bg-size]');
  const fixUndo = $('[data-action="bg-fix-undo"]');
  const fixClear = $('[data-action="bg-fix-clear"]');
  const fixDone = $('[data-action="bg-fix-done"]');

  // Brush sizes (the radius) as a share of the crop frame's width, so a brush is the same on any
  // screen. [ and ] step through them, as in common photo editors.
  const BRUSH_SHARES = { small: 0.02, medium: 0.04, large: 0.08 };
  const BRUSH_ORDER = ['small', 'medium', 'large'];
  // How far a stroke's edge fades, as a share of its radius on each side of the edge: a soft
  // brush. It is drawn as this many nested see-through strokes around a solid core.
  const BRUSH_SOFTNESS = 0.35;
  const BRUSH_RINGS = 6;
  const BRUSH_RING_ALPHA = 0.3;
  const BRUSH_HINTS = {
    refine: 'Paint over the old background left around hair or shoulders. Only what still looks like the old background takes the new colour; hair and skin under the brush stay.',
    erase: 'Paint over anything left in the background. Everything under the brush takes the new background colour.',
    restore: 'Paint where part of the person was changed. The original photo comes back there.'
  };
  const FIX_HINT = 'Drag on the photo to paint. With the keyboard, the arrow keys move the brush and Space paints; [ and ] change its size. Press Done to move the photo again.';
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

  // Touch-up strokes over the changed background (Refine edge, Erase, Restore). Each is a brush
  // stroke in the photo's own pixels, so it stays on the same part of the photo when the photo is
  // moved or zoomed.
  // `plain` and `changedPixels` are the finished photo before and after the background change,
  // kept so a stroke can be shown at once without finding the background again.
  let fixes = [];
  let fixing = false;
  let brush = 'refine';
  let brushSize = 'medium';
  let stroke = null;
  let ring = null;
  let plain = null;
  let changedPixels = null;
  // The old background's colours, as the background change found them. Refine edge needs them.
  let wall = null;
  // What the checks look at: the finished photo before any background change, and where its
  // background was found when the background change already looked.
  let checked = null;

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
  // False while the only changes since the photo was last made are brush strokes: the next save
  // lays them over the background change already found instead of finding it again, which on a
  // large photo takes seconds.
  let remake = true;

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
    if (fixing && ring) {
      // The brush: dark under light, so it shows on any photo.
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      for (const [colour, width] of [['#111827', 3], ['#ffffff', 1.5]]) {
        context.strokeStyle = colour;
        context.lineWidth = width;
        context.beginPath();
        context.arc(ring.x, ring.y, Math.max(2, frame.width * BRUSH_SHARES[brushSize]), 0, Math.PI * 2);
        context.stroke();
      }
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
    // A new photo, or the same one turned a quarter, is a different picture: the fixes painted
    // on the last one do not belong to it.
    fixes = [];
    fixing = false;
    stroke = null;
    ring = null;
    // The old photo's background result does not hold for this one: hide its brush, note and
    // compare button now, not only when the new background has been worked out.
    bgStatus = null;
    renderBackground();
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
    // The photo it replaces can be large; free it now rather than whenever the browser gets to it.
    if (full && full !== source && typeof full.close === 'function') full.close();
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
    shared.clearError();
    const fullSize = shared.sizeOf(full);
    let turned;
    try {
      turned = shared.newCanvas(fullSize.height, fullSize.width);
      const context = turned.getContext('2d');
      if (!context) throw new Error('No canvas');
      context.translate(turned.width / 2, turned.height / 2);
      context.rotate(Math.PI / 2);
      context.drawImage(full, -fullSize.width / 2, -fullSize.height / 2);
    } catch (error) {
      // A phone may not make a canvas as large as the photo. The photo stays as it was.
      console.error(error);
      shared.showError('This photo is too large for this browser to turn. Choose a smaller photo.');
      return;
    }
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
    // The tab names the screen too, so it follows the heading while this screen is showing.
    if (!view.hidden) document.title = `${words.title} | Photo Tools`;
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

    // Touching up needs a changed background.
    if (!changed && fixing) {
      fixing = false;
      stroke = null;
      ring = null;
    }
    handFixButton.hidden = !changed || fixing;
    handFixButton.setAttribute('aria-expanded', String(fixing));
    fixLabel.textContent = fixes.length
      ? `Touch up edges (${fixes.length} ${fixes.length === 1 ? 'stroke' : 'strokes'})`
      : 'Touch up edges';
    fixBox.hidden = !fixing;
    crop.classList.toggle('is-painting', fixing);
    for (const button of brushButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.bgBrush === brush));
    }
    for (const button of brushSizes) {
      button.setAttribute('aria-pressed', String(button.dataset.bgSize === brushSize));
    }
    brushHint.textContent = BRUSH_HINTS[brush];
    fixUndo.disabled = fixes.length === 0;
    fixClear.disabled = fixes.length === 0;
    stageHint.textContent = fixing ? FIX_HINT : MODES[mode].stageHint;
  }

  function setBackground(colour) {
    background = colour;
    if (!colour) bgStatus = null;
    renderBackground();
    if (full) queueSave();
  }

  function backgroundColour() {
    return [1, 3, 5].map(at => parseInt(background.slice(at, at + 2), 16));
  }

  /** Gives the finished photo its new background. Returns the status, or null when it is kept. */
  function applyBackground(context, width, height) {
    plain = null;
    changedPixels = null;
    wall = null;
    checked = null;
    if (mode !== 'photo') return null;
    const before = context.getImageData(0, 0, width, height);
    checked = { before, found: null };
    if (!background) return null;
    const after = new ImageData(new Uint8ClampedArray(before.data), width, height);
    const result = core.changeBackground(after.data, width, height, backgroundColour(), Number(bgAmount.value));
    checked.found = result;
    const { status } = result;
    if (status === core.BACKGROUND_STATUS.changed) {
      plain = before;
      changedPixels = after;
      wall = result.wall;
      composeFixes(context);
    }
    return status;
  }

  // ---------------------------------------------------------------------------
  // Checks
  // ---------------------------------------------------------------------------

  /** Looks at the finished photo for things a form is likely to reject, and lists them. */
  function renderChecks(output) {
    if (mode !== 'photo' || !checked || !output) {
      checkBox.hidden = true;
      return;
    }
    const { before } = checked;
    const { width, height } = before;
    const measured = core.measureBackground(before.data, width, height);
    let found = checked.found;
    if (!found && width * height <= CHECK_OUTLINE_PIXELS) {
      found = core.findBackground(before.data, width, height, Number(bgAmount.value));
      checked.found = found;
    }
    const problems = core.passportChecks({
      pixels: output.getContext('2d').getImageData(0, 0, width, height).data,
      width,
      height,
      found: width * height <= CHECK_OUTLINE_PIXELS ? found : null,
      plain: measured.plain,
      colour: measured.colour,
      replaced: bgStatus === core.BACKGROUND_STATUS.changed,
      enlarged: outputScale()
    });
    checkBox.hidden = false;
    checkBox.classList.toggle('has-problems', problems.length > 0);
    checkTitle.textContent = problems.length ? 'Check before you use this photo' : 'No problems found';
    checkList.replaceChildren(...problems.map((problem) => {
      const item = document.createElement('li');
      item.textContent = problem.text;
      return item;
    }));
  }

  // ---------------------------------------------------------------------------
  // Touching up the changed background: Refine edge, Erase and Restore brushes
  // ---------------------------------------------------------------------------

  /** How much the full photo is scaled to make the saved photo. */
  function outputScale() {
    return scale() * (base.width / shared.sizeOf(full).width) * (size.width / frame.width);
  }

  /**
   * A picture of the brush strokes at the saved size, each in its brush's colour (red Restore,
   * green Refine edge, blue Erase). The strokes are drawn through the same move, turn and zoom as
   * the photo, so they lie on the part of the photo they were painted on.
   *
   * Each stroke is soft on its own: it is drawn as a few nested strokes of falling width, each
   * part see-through, then a solid core, so its edge fades over a share of its own radius. A
   * stroke painted later, with another brush size, does not change the edge of an earlier one,
   * and a later stroke lies over an earlier one.
   */
  function fixMask() {
    const mask = shared.newCanvas(size.width, size.height);
    const context = mask.getContext('2d', { willReadFrequently: true });
    const toOutput = size.width / frame.width;
    const fullSize = shared.sizeOf(full);
    const zoomed = outputScale();
    context.translate(size.width / 2 + offset.x * toOutput, size.height / 2 + offset.y * toOutput);
    context.rotate(angle);
    context.scale(zoomed, zoomed);
    context.translate(-fullSize.width / 2, -fullSize.height / 2);
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
    for (const fix of fixes) {
      const colour = (core.BACKGROUND_BRUSHES[fix.kind] || core.BACKGROUND_BRUSHES.refine).mark;
      context.strokeStyle = colour;
      context.fillStyle = colour;
      // The soft edge spans from a share of the radius outside the brush's edge to as far inside.
      const soft = fix.radius * BRUSH_SOFTNESS;
      context.globalAlpha = BRUSH_RING_ALPHA;
      for (let ring = 0; ring < BRUSH_RINGS; ring += 1) {
        paint(fix, fix.radius + soft - (2 * soft * ring) / BRUSH_RINGS);
      }
      context.globalAlpha = 1;
      paint(fix, Math.max(fix.radius * 0.1, fix.radius - soft));
    }
    return context.getImageData(0, 0, size.width, size.height);
  }

  /** Writes the changed photo with the touch-up strokes laid over it. */
  function composeFixes(context) {
    const pixels = new ImageData(new Uint8ClampedArray(changedPixels.data), changedPixels.width, changedPixels.height);
    if (fixes.length) {
      core.applyBackgroundFixes(pixels.data, plain.data, fixMask().data, backgroundColour(), {
        width: pixels.width,
        wall,
        // The squares Refine edge reads the person's colours from: about 3% of the photo's width.
        cell: Math.max(4, Math.round(pixels.width * 0.03))
      });
    }
    context.putImageData(pixels, 0, 0);
  }

  /** [ and ] make the brush smaller or larger. */
  function stepBrushSize(by) {
    const at = BRUSH_ORDER.indexOf(brushSize);
    const next = BRUSH_ORDER[Math.min(BRUSH_ORDER.length - 1, Math.max(0, at + by))];
    if (next === brushSize) return;
    brushSize = next;
    renderBackground();
    draw();
  }

  /** Shows the fixes on the stage at once; the saved file follows shortly after. */
  function showFixes() {
    if (shown && changedPixels && changedPixels.width === shown.width && changedPixels.height === shown.height) {
      composeFixes(shown.getContext('2d'));
    }
    draw();
  }

  /** A point on the stage (pixels from the stage's corner) as a point in the full photo. */
  function photoPointAt(x, y) {
    const dx = x - (frame.left + frame.width / 2 + offset.x);
    const dy = y - (frame.top + frame.height / 2 + offset.y);
    const cos = Math.cos(-angle);
    const sin = Math.sin(-angle);
    const zoomed = scale();
    const toFull = shared.sizeOf(full).width / base.width;
    return {
      x: ((dx * cos - dy * sin) / zoomed + base.width / 2) * toFull,
      y: ((dx * sin + dy * cos) / zoomed + base.height / 2) * toFull
    };
  }

  function stagePoint(event) {
    const box = crop.getBoundingClientRect();
    return { x: event.clientX - box.left, y: event.clientY - box.top };
  }

  function startStroke(at) {
    const toFull = shared.sizeOf(full).width / base.width;
    stroke = {
      kind: brush,
      radius: Math.max(1, frame.width * BRUSH_SHARES[brushSize] / scale() * toFull),
      points: [photoPointAt(at.x, at.y)]
    };
    fixes.push(stroke);
    showFixes();
  }

  function endStroke() {
    if (!stroke) return;
    stroke = null;
    renderBackground();
    queueSave(true);
  }

  function setFixing(on) {
    // A stroke still being painted is finished first, so the saved file gets it too.
    endStroke();
    fixing = on;
    ring = on ? { x: frame.left + frame.width / 2, y: frame.top + frame.height / 2 } : null;
    renderBackground();
    draw();
    if (on) crop.focus();
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
    try {
      const reuse = !remake
        && bgStatus === core.BACKGROUND_STATUS.changed
        && changedPixels && changedPixels.width === size.width && changedPixels.height === size.height;
      remake = false;
      let output;
      if (reuse) {
        output = shared.newCanvas(size.width, size.height);
        composeFixes(output.getContext('2d', { willReadFrequently: true }));
      } else {
        output = renderOutput();
      }
      // A copy for the stage: the brush repaints it while this one is being turned into a file.
      shown = null;
      if (bgStatus === core.BACKGROUND_STATUS.changed) {
        shown = shared.newCanvas(output.width, output.height);
        shown.getContext('2d', { willReadFrequently: true }).drawImage(output, 0, 0);
      }
      renderBackground();
      renderChecks(output);
      draw();
      // The photo and its checks are shown either way; only the file waits for a usable limit.
      if (limitText && !targetBytes) {
        saved = null;
        downloadButton.disabled = true;
        resultLine.className = 'phot-result is-problem';
        resultLine.textContent = 'Enter the file size limit as a number of KB, or leave it empty.';
        return;
      }
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
      // Whatever was half made is not trusted: the next save starts again from the photo.
      remake = true;
      saved = null;
      downloadButton.disabled = true;
      resultLine.className = 'phot-result is-problem';
      resultLine.textContent = error instanceof core.PhotoToolError ? error.message : 'The photo could not be saved. Try a smaller size.';
    }
  }

  /** The saved file is prepared again shortly after the last change. */
  /** `keepShown` is for a change the stage already shows (a brush stroke). */
  function queueSave(keepShown) {
    renderSheet();
    // Anything but a brush stroke changes the photo itself, so it is made again in full.
    if (keepShown !== true) remake = true;
    if (shown && keepShown !== true) {
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
    if (fixing) {
      // One pointer paints; a second finger does nothing.
      if (stroke || !shared.isMainPointer(event)) return;
      event.preventDefault();
      ring = stagePoint(event);
      startStroke(ring);
      // Only this pointer continues and ends the stroke; another finger on the stage does not.
      stroke.pointer = event.pointerId;
      try {
        // Keeps the stroke going when the pointer leaves the stage. Painting works without it.
        crop.setPointerCapture(event.pointerId);
      } catch {
        // The pointer is already gone.
      }
      return;
    }
    crop.setPointerCapture(event.pointerId);
    pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointers.size === 2) {
      const [a, b] = [...pointers.values()];
      pinch = { distance: Math.hypot(a.x - b.x, a.y - b.y), zoom: zoomFactor };
    }
  });

  crop.addEventListener('pointermove', (event) => {
    if (fixing) {
      if (!event.isPrimary) return;
      ring = stagePoint(event);
      if (stroke && stroke.pointer === event.pointerId) {
        stroke.points.push(photoPointAt(ring.x, ring.y));
        showFixes();
      } else {
        draw();
      }
      return;
    }
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
      if (fixing && stroke && stroke.pointer === event.pointerId) endStroke();
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
    if (fixing) {
      // The ring stands in for the pointer; Space or Enter paints one dab where it is.
      if (moves[event.key]) {
        event.preventDefault();
        ring = {
          x: Math.min(frame.left + frame.width, Math.max(frame.left, ring.x + moves[event.key][0])),
          y: Math.min(frame.top + frame.height, Math.max(frame.top, ring.y + moves[event.key][1]))
        };
        draw();
      } else if ((event.key === ' ' || event.key === 'Enter') && !stroke) {
        event.preventDefault();
        startStroke(ring);
        endStroke();
      } else if (event.key === 'Escape') {
        event.preventDefault();
        setFixing(false);
      }
      return;
    }
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

  // [ and ] change the brush size while touching up, wherever the focus is on this screen,
  // except while typing in a box.
  view.addEventListener('keydown', (event) => {
    if (!fixing || (event.key !== '[' && event.key !== ']')) return;
    if (/^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName) && event.target.type !== 'range') return;
    event.preventDefault();
    stepBrushSize(event.key === '[' ? -1 : 1);
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
      if (button.dataset.mode === mode) return;
      setMode(button.dataset.mode);
      // The address follows the mode, so Back, a refresh or a bookmark opens the same one.
      history.replaceState(null, '', `#${mode === 'signature' ? 'signature' : 'passport'}`);
    });
  }

  // The home screen has a card for each: #passport opens Photo, #signature opens Signature.
  function modeFromAddress() {
    const wanted = { '#passport': 'photo', '#signature': 'signature' }[window.location.hash];
    if (wanted && wanted !== mode) setMode(wanted);
  }
  window.addEventListener('hashchange', modeFromAddress);

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
  handFixButton.addEventListener('click', () => setFixing(true));
  fixDone.addEventListener('click', () => {
    setFixing(false);
    handFixButton.focus();
  });
  fixUndo.addEventListener('click', () => {
    fixes.pop();
    renderBackground();
    showFixes();
    queueSave(true);
  });
  fixClear.addEventListener('click', () => {
    fixes = [];
    renderBackground();
    showFixes();
    queueSave(true);
  });
  for (const button of brushButtons) {
    button.addEventListener('click', () => {
      brush = button.dataset.bgBrush;
      renderBackground();
    });
  }
  for (const button of brushSizes) {
    button.addEventListener('click', () => {
      brushSize = button.dataset.bgSize;
      renderBackground();
      draw();
    });
  }
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

  downloadButton.addEventListener('click', (event) => {
    if (!saved || shared.isRepeatClick(event)) return;
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
  modeFromAddress();
})();
