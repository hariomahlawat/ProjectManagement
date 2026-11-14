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
  // Zoom goes to 4 times at least; further on a photo with pixels to spare, up to where the saved
  // photo would be enlarged 1.15 times (just short of the 1.2 where the checks warn it may look
  // soft), and never past 12 times.
  const MAX_ZOOM = 4;
  const MAX_ENLARGED = 1.15;
  const MAX_ZOOM_EVER = 12;
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
  // The same button in the bar under the photo, for phones, where the panel is far below.
  const bgCompareStage = $('[data-action="bg-original-stage"]');
  const phoneBar = $('[data-phone-bar]');
  const phoneChecks = $('[data-action="phone-checks"]');
  const phoneDownload = $('[data-action="phone-download"]');
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
  const bgModelNote = $('[data-bg-model-note]');
  const bgModelButton = $('[data-action="bg-model"]');
  const bgModelHint = $('[data-bg-model-hint]');
  const bgWait = $('[data-bg-wait]');
  const bgWaitText = $('[data-bg-wait-text]');
  const bgWaitCancel = $('[data-action="bg-model-cancel"]');
  const bgRetry = $('[data-bg-retry]');
  const bgRetryText = $('[data-bg-retry-text]');
  const bgRetryButton = $('[data-action="bg-model-retry"]');
  const bgWaitStatus = $('[data-bg-wait-status]');

  // The background model on the PRISM server (Remove background's), used when the colour method
  // cannot cope and on Better edges (owner decision, 14 Nov 2026).
  const MODEL_FINDING = 'Sending a copy to the PRISM server to find the person…';
  const MODEL_STILL_WORKING = 'Still working. The PRISM server does one photo at a time, so this can take a minute.';
  const MODEL_SLOW_AFTER_MS = 8000;
  const MODEL_GIVE_UP_MS = 180000;
  const MODEL_CHECK_AGAIN_MS = 10000;
  // Finding the face: a request that takes longer is given up (the photo is then placed by hand,
  // with Try again), and a busy answer is tried once more after a short wait.
  const FACE_GIVE_UP_MS = 20000;
  const FACE_BUSY_WAIT_MS = 2000;
  const FACE_WORDS = {
    asking: 'Placing the face…',
    failed: 'The face could not be placed. Place it by hand, or try again.',
    sideways: 'The photo looks turned on its side. Use Rotate, and the face is placed again.'
  };
  // What the colour method says when it could not change the background: the model is asked then.
  const COLOUR_GAVE_UP = new Set([
    core.BACKGROUND_STATUS.busy,
    core.BACKGROUND_STATUS.none,
    core.BACKGROUND_STATUS.tooMuch
  ]);

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

  // The model's cut-out of this photo, made once per photo and kept with it, so changing the colour
  // afterwards is instant: the photo's own pixels at the size Remove background works at (at most
  // 2048 px), the model's mask, the mask after the brushes, and the cut-out drawn from them. null
  // until the model has answered.
  let model = null;
  // Whether Remove background is set up on this server, so the model can be asked.
  let modelReady = false;
  // The request on its way: { request, cancelled, timedOut, slow }, or null.
  let modelAsk = null;
  let modelRun = 0;
  let modelSlowTimer = 0;
  let modelGiveUpTimer = 0;
  let modelCheckTimer = 0;
  let focusModelNote = false;
  // The model is asked by itself once per photo; after that only Better edges or Try again ask.
  let modelAutoTried = false;
  // Why the last request stopped: something that passes (Try again), or a plain problem.
  let modelRetry = '';
  let modelProblem = '';

  // Where the photo sits: the photo centre's distance from the frame centre (screen px), its
  // tilt, and how far it is zoomed past "just fills the frame".
  let offset = { x: 0, y: 0 };
  let angle = 0;
  let zoomFactor = 1;
  // Where the face detector found the face, in `base`'s pixels: undefined when it was not asked
  // (not set up, Signature, or no answer), false when it found none, else { box, points }.
  let faceFound;
  // True once the user has moved, zoomed or straightened this photo: a face found after that
  // does not move it again ("Place the face again" does).
  let touched = false;
  let faceRun = 0;
  // True while the face detector is being asked for this photo.
  let faceAsking = false;
  // What the stage bar says about the face: '', 'asking', 'failed' or 'sideways'.
  let faceState = '';
  let faceRequest = null;
  const faceStateLine = $('[data-face-state]');
  const faceRetryButton = $('[data-action="face-retry"]');
  const placeFaceButton = $('[data-action="place-face"]');
  let frame = { width: 0, height: 0, left: 0, top: 0 };

  let size = { width: core.PASSPORT_SIZES[0].width, height: core.PASSPORT_SIZES[0].height };
  let customOpen = false;
  // Kept in this browser: per mode the size (or the custom size) and the KB limit, and the
  // background colour. Whether Photo or Signature opens is decided by the card the user picked.
  const remembered = shared.rememberedChoices('passport');
  let restoring = false;
  let saved = null;
  let saveRun = 0;
  let saveTimer = null;
  const opener = shared.latestPhotoOpener();
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
    renderZoomRange();
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
    touched = true;
    shared.setUnsaved('passport', true);
    place();
    // The limit depends on the angle (a straightened photo is enlarged a little to fill the
    // frame), so the slider's end follows Straighten as well as the photo and its size.
    renderZoomRange();
    draw();
    queueSave();
  }

  /** How far this photo may be zoomed: see MAX_ZOOM. Does not depend on the size of the screen. */
  function zoomLimit() {
    if (!base || !full || !frame.width) return MAX_ZOOM;
    const enlargedAtOne = outputScale() / zoomFactor;
    if (!(enlargedAtOne > 0)) return MAX_ZOOM;
    return Math.min(MAX_ZOOM_EVER, Math.max(MAX_ZOOM, MAX_ENLARGED / enlargedAtOne));
  }

  /** The zoom slider reaches this photo's limit. */
  function renderZoomRange() {
    const max = String(Math.round(zoomLimit() * 100));
    if (zoom.max !== max) zoom.max = max;
    zoom.value = String(Math.round(zoomFactor * 100));
  }

  function setZoom(value) {
    zoomFactor = Math.min(zoomLimit(), Math.max(1, value));
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
    shared.setUnsaved('passport', false);
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
    forgetModel();
    checkModel();
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
    touched = false;
    findFace();
  }

  // ---------------------------------------------------------------------------
  // The face placed for you
  // ---------------------------------------------------------------------------

  /**
   * Asks the PRISM server where the face is (Photo mode only, when the Photos library's face
   * detector is set up) and, unless the user has already moved the photo, places it so the face
   * sits on the guide. Without the detector nothing changes: the photo is placed by hand.
   */
  async function findFace() {
    const mine = ++faceRun;
    // The last photo's (or mode's) request is no longer wanted: cancelling it also gives its
    // place on the server back, so it cannot crowd out this one.
    if (faceRequest) faceRequest.abort();
    faceRequest = null;
    faceFound = undefined;
    faceAsking = false;
    faceState = '';
    renderFace();
    const server = window.PrismPhotoServer;
    if (!server || mode !== 'photo' || !base) return;
    // Until the face is placed, the frame shows a crop the user will not keep: the colour method
    // may give up on it, and the model must not be asked for that.
    faceAsking = true;
    let placed = false;
    const request = new AbortController();
    faceRequest = request;
    const giveUp = setTimeout(() => request.abort(), FACE_GIVE_UP_MS);
    try {
      const status = await server.status();
      if (mine !== faceRun || !status || !status.faceAvailable) return;
      faceState = 'asking';
      renderFace();
      let answer = await server.locateFace(base, request.signal);
      if (mine === faceRun && answer && answer.problem === 'busy') {
        // The server's places for finding faces are taken: one more try shortly.
        await new Promise(resolve => setTimeout(resolve, FACE_BUSY_WAIT_MS));
        if (mine === faceRun && !request.signal.aborted) answer = await server.locateFace(base, request.signal);
      }
      if (mine !== faceRun || mode !== 'photo') return;
      if (!answer || answer.problem) {
        // Busy twice, too slow (given up), or an error: placed by hand, with Try again.
        faceState = 'failed';
        renderFace();
        return;
      }
      faceState = '';
      if (!answer.found) {
        faceFound = false;
      } else {
        // The copy sent is `base` itself, so the answer is in its pixels already.
        const ratio = base.width / Math.max(1, answer.imageWidth);
        faceFound = {
          box: {
            left: answer.box.left * ratio,
            top: answer.box.top * ratio,
            width: answer.box.width * ratio,
            height: answer.box.height * ratio
          },
          points: answer.points.map(([x, y]) => [x * ratio, y * ratio])
        };
        if (!touched) applyFace();
      }
      renderFace();
      queueSave(true);
      placed = true;
    } finally {
      clearTimeout(giveUp);
      if (mine === faceRun) {
        faceRequest = null;
        faceAsking = false;
        if (faceState === 'asking') faceState = '';
        renderFace();
        // No new save was queued: the last one's answer stands, so the model may be asked now.
        if (!placed) askModelIfColourGaveUp();
      }
    }
  }

  /** Places the photo so the face is on the guide: eyes level, head between the lines, centred. */
  function applyFace() {
    if (!faceFound || !base || !frame.width) return;
    const placeAt = (maxZoom) => core.placeFace({
      imageWidth: base.width,
      imageHeight: base.height,
      frameWidth: frame.width,
      frameHeight: frame.height,
      face: faceFound,
      maxAngle: 15 * Math.PI / 180,
      maxZoom
    });
    let placement = placeAt(zoomLimit());
    if (placement) {
      // Straightening enlarges the photo a little, so the limit is worked out again at the
      // angle it will be turned by, and the face placed again if that limit is lower.
      const was = angle;
      angle = placement.angle;
      const limit = zoomLimit();
      angle = was;
      if (limit < placement.zoom) placement = placeAt(limit);
    }
    if (!placement) {
      // On its side: Rotate first (a quarter turn opens the photo again and asks again).
      faceState = 'sideways';
      renderFace();
      return;
    }
    faceState = '';
    // The straighten slider moves in half degrees; the photo is turned by exactly what it shows.
    const degrees = Math.round(placement.angle * 180 / Math.PI * 2) / 2;
    angle = degrees * Math.PI / 180;
    straighten.value = String(degrees);
    straightenValue.textContent = `${degrees > 0 ? '+' : ''}${degrees}°`;
    zoomFactor = placement.zoom;
    renderZoomRange();
    offset = { x: placement.x, y: placement.y };
    touched = false;
    place();
    draw();
    queueSave();
  }

  /** What the hint under the photo says: painting, the face placed for you, or how to place it. */
  function stageHintText() {
    if (fixing) return FIX_HINT;
    if (mode === 'photo' && faceFound && faceState !== 'sideways') {
      return 'The face was placed for you. Check it against the guide, or drag and zoom to adjust.';
    }
    return MODES[mode].stageHint;
  }

  function renderFace() {
    stageHint.textContent = stageHintText();
    const photo = mode === 'photo';
    placeFaceButton.hidden = !(faceFound && photo && faceState !== 'sideways');
    const words = photo ? FACE_WORDS[faceState] || '' : '';
    if (faceStateLine.textContent !== words) faceStateLine.textContent = words;
    faceStateLine.hidden = !words;
    faceRetryButton.hidden = !(photo && faceState === 'failed');
  }

  /** Where the face is in the finished photo, for the checks: shares, false or undefined. */
  function faceForChecks() {
    if (mode !== 'photo' || faceFound === undefined) return undefined;
    if (faceFound === false) return false;
    return core.faceShares({
      imageWidth: base.width,
      imageHeight: base.height,
      frameWidth: frame.width,
      frameHeight: frame.height,
      placement: { angle, scale: scale(), x: offset.x, y: offset.y },
      face: faceFound
    });
  }

  async function openFile(file) {
    // Only the photo chosen last is used: an earlier one that opens late is thrown away.
    const source = await opener.open(file);
    if (!source) return;
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
    // Turning is a change to this photo, not a new one: it would be lost by leaving too.
    shared.setUnsaved('passport', true);
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
    rememberChoices();
  }

  /** Keeps this mode's size, custom size and KB limit, and the background, for the next visit. */
  function rememberChoices() {
    if (restoring) return;
    const all = remembered.load() || {};
    all[mode] = {
      width: size.width,
      height: size.height,
      customOpen,
      customWidth: customWidth.value,
      customHeight: customHeight.value,
      customUnit: customUnit.value,
      limit: outLimit.value.trim()
    };
    if (mode === 'photo') all.background = background;
    remembered.save(all);
  }

  /** Puts back what was kept for this mode. Anything that no longer fits is left at the default. */
  function restoreChoices() {
    const all = remembered.load();
    const kept = all && all[mode];
    // Each mode has its own limit: start from "No limit", so Signature never inherits Photo's.
    outLimit.value = '';
    if (!kept) return;
    restoring = true;
    try {
      // A limit is put back only if it is empty or a real size: a mistyped one ("0", "abc") would
      // block every download until it was noticed.
      if (typeof kept.limit === 'string' && kept.limit.length <= 6
        && (kept.limit === '' || core.parseTargetBytes(kept.limit, 'KB') !== null)) {
        outLimit.value = kept.limit;
      }
      const pixels = value => Number.isInteger(value) && value >= MIN_PIXELS && value <= MAX_PIXELS;
      // A custom size is put back only if it still makes a usable size: one left half-typed when
      // the choices were kept must not block the download next time. The last good size is used
      // instead.
      const customUsable = kept.customOpen && typeof kept.customWidth === 'string' && typeof kept.customHeight === 'string'
        && Array.from(customUnit.options).some(option => option.value === kept.customUnit)
        && pixels(core.lengthToPixels(kept.customWidth, kept.customUnit))
        && pixels(core.lengthToPixels(kept.customHeight, kept.customUnit));
      if (customUsable) {
        customWidth.value = kept.customWidth;
        customHeight.value = kept.customHeight;
        customUnit.value = kept.customUnit;
        customOpen = true;
        customChanged();
      } else if (pixels(kept.width) && pixels(kept.height)) {
        setSize(kept.width, kept.height);
      }
      if (mode === 'photo' && typeof all.background === 'string'
        && (all.background === '' || /^#[0-9a-f]{6}$/i.test(all.background))) {
        if (/^#[0-9a-f]{6}$/i.test(all.background)) bgColour.value = all.background;
        setBackground(all.background);
      }
    } finally {
      restoring = false;
    }
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
    rememberChoices();
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
    stageHint.textContent = stageHintText();
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
    // The default size first, without keeping it: it must not replace what was kept for this mode.
    restoring = true;
    try {
      setSize(words.sizes[0].width, words.sizes[0].height);
    } finally {
      restoring = false;
    }
    restoreChoices();
    // Signature has no face; back in Photo the face is asked for again (and placed if untouched).
    if (base) findFace();
  }

  // ---------------------------------------------------------------------------
  // Print sheet
  // ---------------------------------------------------------------------------

  function renderSheet() {
    sheetButton.disabled = !full || sizeProblem || modelWorking();
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
    bgColour.setAttribute('aria-label', preset ? 'Other background colour' : 'Other background colour, chosen');
    for (const button of bgButtons) {
      const dot = button.querySelector('.phot-bgchoice__dot');
      if (dot) dot.style.setProperty('--phot-swatch', button.dataset.bg);
    }
    const amount = Number(bgAmount.value);
    bgAmountValue.textContent = amount < 35 ? 'Less' : amount > 65 ? 'More' : 'Normal';
    const usingModel = modelInUse();
    // The slider tunes the colour method only.
    bgAmountRow.hidden = !background || usingModel;
    const changed = Boolean(background) && bgStatus === core.BACKGROUND_STATUS.changed;
    const photoBackground = mode === 'photo' && Boolean(background);
    const waiting = photoBackground && Boolean(modelAsk);
    const stopped = photoBackground && !modelAsk && Boolean(modelRetry);
    const problem = background && !waiting && !stopped
      ? modelProblem || (usingModel ? undefined : BG_PROBLEMS[bgStatus])
      : undefined;
    bgCompare.hidden = !changed;
    bgCompareStage.hidden = !changed;
    bgNote.hidden = !changed || usingModel;
    bgModelNote.hidden = !changed || !usingModel;
    bgProblem.hidden = !problem;
    bgProblem.textContent = problem || '';
    bgModelButton.hidden = !canAskModel() || modelAsk !== null;
    bgModelHint.hidden = bgModelButton.hidden;
    const waitText = modelAsk && modelAsk.slow ? MODEL_STILL_WORKING : MODEL_FINDING;
    bgWait.hidden = !waiting;
    bgWaitText.textContent = waitText;
    bgWaitCancel.hidden = !(waiting && modelAsk.slow);
    bgRetry.hidden = !stopped;
    bgRetryText.textContent = modelRetry;
    // Read out once when it changes, not on every redraw.
    const said = waiting ? waitText : stopped ? modelRetry : '';
    if (bgWaitStatus.textContent !== said) bgWaitStatus.textContent = said;

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
    stageHint.textContent = stageHintText();
  }

  function setBackground(colour) {
    background = colour;
    if (!colour) bgStatus = null;
    renderBackground();
    if (full) queueSave();
    rememberChoices();
  }

  function backgroundColour() {
    return [1, 3, 5].map(at => parseInt(background.slice(at, at + 2), 16));
  }

  /**
   * Gives the finished photo its new background. Returns what it made, for renderOutput to keep:
   * { status (null when the background is kept), plain, changedPixels, wall, checked }. It changes
   * nothing else, so a picture made only to be handed on (Print sheet) leaves the screen's state
   * alone. `placed(context, picture)` draws a picture of the whole photo through the frame's move,
   * turn and zoom.
   */
  function applyBackground(context, width, height, placed) {
    const made = { status: null, plain: null, changedPixels: null, wall: null, checked: null };
    if (mode !== 'photo') return made;
    const before = context.getImageData(0, 0, width, height);
    made.checked = { before, found: null };
    if (!background) return made;
    if (modelInUse()) {
      // The model's cut-out, placed like the photo and given the same look, over the plain colour.
      const layer = shared.newCanvas(width, height);
      const layerContext = layer.getContext('2d', { willReadFrequently: true });
      layerContext.imageSmoothingQuality = 'high';
      placed(layerContext, model.cutout);
      applyLook(layerContext, width, height);
      context.setTransform(1, 0, 0, 1, 0, 0);
      context.fillStyle = background;
      context.fillRect(0, 0, width, height);
      context.drawImage(layer, 0, 0);
      made.status = core.BACKGROUND_STATUS.changed;
      return made;
    }
    const after = new ImageData(new Uint8ClampedArray(before.data), width, height);
    const result = core.changeBackground(after.data, width, height, backgroundColour(), Number(bgAmount.value));
    made.checked.found = result;
    made.status = result.status;
    if (result.status === core.BACKGROUND_STATUS.changed) {
      made.plain = before;
      made.changedPixels = after;
      made.wall = result.wall;
      composeFixes(context, made);
    }
    return made;
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
      enlarged: outputScale(),
      face: faceForChecks(),
      atZoomLimit: zoomFactor >= zoomLimit() - 0.01
    });
    checkBox.hidden = false;
    checkBox.classList.toggle('has-problems', problems.length > 0);
    // With no face found and no outline from the background, head position and size were not
    // judged: "No problems found" alone would claim more than was looked at.
    const face = faceForChecks();
    const outlineFound = face === undefined && width * height <= CHECK_OUTLINE_PIXELS
      && found && found.status === core.BACKGROUND_STATUS.changed
      && Boolean(core.headOutline(found.mask, width, height));
    const headJudged = face !== undefined || outlineFound;
    checkTitle.textContent = problems.length
      ? 'Check before you use this photo'
      : headJudged ? 'No problems found' : 'No problems found, but the head position could not be checked';
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
    paintStrokes(context);
    return context.getImageData(0, 0, size.width, size.height);
  }

  /** Paints every stroke, in the full photo's pixels, onto a context already placed for them. */
  function paintStrokes(context) {
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
  }

  /**
   * Writes the changed photo with the touch-up strokes laid over it. `parts` is what the background
   * change made ({ changedPixels, plain, wall }); by default, the last one kept.
   */
  function composeFixes(context, parts = { changedPixels, plain, wall }) {
    const { changedPixels, plain, wall } = parts;
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
  let fixesFrame = 0;

  function showFixes() {
    if (fixesFrame) {
      cancelAnimationFrame(fixesFrame);
      fixesFrame = 0;
    }
    if (modelInUse()) {
      // The brushes work on the model's mask, as in Remove background; only the part a stroke
      // just reached is made again, then the small finished photo.
      rebuildCutout(strokeAreaOnModel());
      if (shown) shown = renderOutput();
      draw();
      return;
    }
    if (shown && changedPixels && changedPixels.width === shown.width && changedPixels.height === shown.height) {
      composeFixes(shown.getContext('2d'));
    }
    draw();
  }

  // While painting, every move adds a point but the picture is made again at most once a frame:
  // making it reads the whole photo, and a fast mouse sends many moves between two frames.
  function queueFixes() {
    if (!fixesFrame) {
      fixesFrame = requestAnimationFrame(() => {
        fixesFrame = 0;
        showFixes();
      });
    }
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
    shared.setUnsaved('passport', true);
    // The last moves are shown now, not on a frame that a hidden page may never draw.
    if (fixesFrame) showFixes();
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
    if (!full || !frame.width || sizeProblem || modelWorking()) return;
    document.dispatchEvent(new CustomEvent('phot:print-sheet', {
      detail: { photo: composeOutput().output, name: `${core.baseName(fileName)}-${MODES.photo.suffix}.jpg` }
    }));
  }

  // ---------------------------------------------------------------------------
  // The background model: when the colour method cannot cope, and on Better edges
  // ---------------------------------------------------------------------------

  /** True while the model is changing the background of the photo on screen. */
  function modelWorking() {
    return modelAsk !== null && mode === 'photo' && Boolean(background);
  }

  /** Download and Print sheet wait while the model works, and the line under them says why. */
  function showModelWait() {
    if (!modelWorking()) return;
    downloadButton.disabled = true;
    renderSheet();
    resultLine.className = 'phot-result';
    resultLine.textContent = 'Changing the background on the PRISM server. Download and Print sheet are ready when it is done.';
  }

  function modelInUse() {
    return mode === 'photo' && Boolean(background) && model !== null;
  }

  function canAskModel() {
    return modelReady && Boolean(window.PrismPhotoServer) && mode === 'photo'
      && Boolean(full) && Boolean(background) && model === null;
  }

  /**
   * Whether Remove background is set up on this server; asked again with each photo, and every
   * few seconds while the server is still getting the model ready (just after it starts), so
   * Better edges appears without opening the photo again.
   */
  function checkModel() {
    const server = window.PrismPhotoServer;
    if (!server) return;
    clearTimeout(modelCheckTimer);
    const run = modelRun;
    server.status().then((status) => {
      if (run !== modelRun) return;
      modelReady = Boolean(status && status.available);
      if (!modelReady && status && status.state === 'checking' && full) {
        modelCheckTimer = setTimeout(checkModel, MODEL_CHECK_AGAIN_MS);
      }
      renderBackground();
      // The answer can come after the colour method has already given up on this photo.
      askModelIfColourGaveUp();
    });
  }

  /** The colour method could not change this background: the model is asked, once by itself. */
  function askModelIfColourGaveUp() {
    if (faceAsking) return;
    if (background && COLOUR_GAVE_UP.has(bgStatus) && !modelAutoTried && canAskModel()) {
      modelAutoTried = true;
      askModel();
    }
  }

  /** A new photo: the last one's cut-out, and any request for it, are dropped. */
  function forgetModel() {
    modelRun += 1;
    if (modelAsk) modelAsk.request.abort();
    modelAsk = null;
    clearTimeout(modelSlowTimer);
    clearTimeout(modelGiveUpTimer);
    model = null;
    modelAutoTried = false;
    focusModelNote = false;
    modelRetry = '';
    modelProblem = '';
  }

  /**
   * Sends a copy of the photo to the model (the same copy and the same rules as Remove
   * background) and makes the cut-out. A request that stops for a reason that passes (busy, too
   * slow, no connection, Cancel) leaves the photo as it was, with Try again.
   */
  async function askModel() {
    if (!canAskModel() || modelAsk) return;
    const run = ++modelRun;
    const ask = { request: new AbortController(), cancelled: false, timedOut: false, slow: false };
    modelAsk = ask;
    modelRetry = '';
    modelProblem = '';
    clearTimeout(modelSlowTimer);
    clearTimeout(modelGiveUpTimer);
    modelSlowTimer = setTimeout(() => {
      if (modelAsk !== ask) return;
      ask.slow = true;
      renderBackground();
    }, MODEL_SLOW_AFTER_MS);
    modelGiveUpTimer = setTimeout(() => {
      ask.timedOut = true;
      ask.request.abort();
    }, MODEL_GIVE_UP_MS);
    // Better edges hides itself once pressed: keyboard focus moves to the note that says what is
    // happening, rather than being dropped.
    const fromButton = document.activeElement === bgModelButton;
    renderBackground();
    // Until the model answers, the photo would be saved with its old background.
    showModelWait();
    if (fromButton) bgWait.focus({ preventScroll: true });
    const source = full;
    try {
      const answer = await window.PrismPhotoServer.mask(source, ask.request.signal);
      if (run !== modelRun) return;
      model = makeCutout(source, answer);
      rebuildCutout();
    } catch (error) {
      if (run !== modelRun) return;
      if (ask.cancelled) {
        modelRetry = 'Stopped. The photo is still here.';
      } else if (ask.timedOut) {
        modelRetry = 'No answer came from the PRISM server in time. The photo is still here.';
      } else if (error instanceof core.PhotoToolError && error.code === 'retry') {
        modelRetry = error.message;
      } else {
        if (!(error instanceof core.PhotoToolError)) console.error(error);
        modelProblem = error instanceof core.PhotoToolError
          ? error.message
          : 'The PRISM server could not find the person in this photo.';
      }
    } finally {
      if (run === modelRun) {
        clearTimeout(modelSlowTimer);
        clearTimeout(modelGiveUpTimer);
        modelAsk = null;
        // Focus follows the note it was on (a keyboard user who pressed Better edges or Cancel).
        const followed = bgWait.contains(document.activeElement);
        renderBackground();
        renderSheet();
        if (model) {
          // The note about the model shows once the photo is made again; focus goes to it then.
          focusModelNote = followed;
          queueSave();
        } else {
          // Stopped or failed: the photo keeps the colour method's result, which can be saved.
          queueSave(true);
          if (modelRetry && bgRetry.hidden === false) bgRetryButton.focus({ preventScroll: true });
        }
      }
    }
  }

  /**
   * The cut-out's starting point, as Remove background makes it: the old background's colours
   * read from where the mask is sure, the person's colours, and the mask checked against them
   * where the model is unsure (the band around hair).
   */
  function makeCutout(source, answer) {
    const { width, height, alpha: mask } = answer;
    const work = shared.drawPhoto(source, width, height, {});
    const pixels = work.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, width, height);
    work.width = 0;
    work.height = 0;
    // The squares the person's colours are read from: about 3% of the photo's width.
    const cell = Math.max(4, Math.round(width * 0.03));
    const old = core.cutoutBackground(pixels.data, mask, width, height);
    const subjectColours = old ? core.cutoutPersonColours(pixels.data, mask, width, old, cell) : null;
    core.refineCutoutEdge(mask, pixels.data, width, old, cell, subjectColours);
    return {
      width,
      height,
      cell,
      pixels,
      background: old,
      subjectColours,
      modelAlpha: mask,
      alpha: new Uint8ClampedArray(mask),
      colourAlpha: new Uint8ClampedArray(mask),
      colours: new ImageData(new Uint8ClampedArray(pixels.data), width, height),
      cutout: shared.newCanvas(width, height)
    };
  }

  /** The brush strokes over one part of the model's photo, at its size. */
  function strokesOnModel(region) {
    const width = region.right - region.left;
    const height = region.bottom - region.top;
    const picture = shared.newCanvas(width, height);
    const context = picture.getContext('2d', { willReadFrequently: true });
    const toModel = model.width / shared.sizeOf(full).width;
    context.translate(-region.left, -region.top);
    context.scale(toModel, toModel);
    paintStrokes(context);
    return context.getImageData(0, 0, width, height).data;
  }

  /** The part of the model's photo the stroke being painted has reached since it was last shown. */
  function strokeAreaOnModel() {
    if (!stroke) return null;
    const toModel = model.width / shared.sizeOf(full).width;
    const points = stroke.points.slice(Math.max(0, (stroke.shownUpTo || 0) - 1));
    stroke.shownUpTo = stroke.points.length;
    const reach = (stroke.radius * (1 + BRUSH_SOFTNESS) + 2) * toModel + 2;
    const xs = points.map(point => point.x * toModel);
    const ys = points.map(point => point.y * toModel);
    const area = {
      left: Math.max(0, Math.floor(Math.min(...xs) - reach)),
      top: Math.max(0, Math.floor(Math.min(...ys) - reach)),
      right: Math.min(model.width, Math.ceil(Math.max(...xs) + reach)),
      bottom: Math.min(model.height, Math.ceil(Math.max(...ys) + reach))
    };
    return area.right > area.left && area.bottom > area.top ? area : null;
  }

  /**
   * The mask after the brushes, and the cut-out made from it, as in Remove background: the old
   * background's tint taken out of hair and edges. With `region`, only that part is made again
   * (every stroke is still applied there, so the result is the same as a full rebuild).
   */
  function rebuildCutout(region) {
    const { width, height } = model;
    const area = region || { left: 0, top: 0, right: width, bottom: height };
    const across = area.right - area.left;
    if (fixes.length) {
      core.applyCutoutStrokes(model.alpha, model.modelAlpha, model.pixels.data, strokesOnModel(area), model.background,
        { width, cell: model.cell, colours: model.subjectColours, region: area, colourAlpha: model.colourAlpha });
    } else {
      for (let y = area.top; y < area.bottom; y += 1) {
        const start = y * width + area.left;
        model.alpha.set(model.modelAlpha.subarray(start, start + across), start);
        model.colourAlpha.set(model.modelAlpha.subarray(start, start + across), start);
      }
    }
    for (let y = area.top; y < area.bottom; y += 1) {
      const start = (y * width + area.left) * 4;
      model.colours.data.set(model.pixels.data.subarray(start, start + across * 4), start);
    }
    core.cutoutColours(model.colours.data, model.colourAlpha, width, model.background, area, model.alpha);
    model.cutout.getContext('2d').putImageData(model.colours, 0, 0, area.left, area.top, across, area.bottom - area.top);
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

  /**
   * Draws exactly what the crop frame shows at the saved size, from the full photo, and keeps
   * what the background change made (for the brushes, the checks and the next save).
   */
  function renderOutput() {
    const { output, made } = composeOutput();
    bgStatus = made.status;
    plain = made.plain;
    changedPixels = made.changedPixels;
    wall = made.wall;
    checked = made.checked;
    return output;
  }

  /** The finished photo and what its background change made; changes nothing on the screen. */
  function composeOutput() {
    const output = shared.newCanvas(size.width, size.height);
    const context = output.getContext('2d', { willReadFrequently: true });
    context.fillStyle = '#fff';
    context.fillRect(0, 0, size.width, size.height);
    context.imageSmoothingQuality = 'high';

    const toOutput = size.width / frame.width;
    const fullSize = shared.sizeOf(full);
    // Any picture of the whole photo (the photo itself, a halved copy, the model's cut-out) is
    // drawn through the same move, turn and zoom.
    const placed = (target, picture) => {
      const pictureSize = shared.sizeOf(picture);
      const drawScale = scale() * (base.width / pictureSize.width) * toOutput;
      target.translate(size.width / 2 + offset.x * toOutput, size.height / 2 + offset.y * toOutput);
      target.rotate(angle);
      target.scale(drawScale, drawScale);
      target.drawImage(picture, -pictureSize.width / 2, -pictureSize.height / 2);
      target.setTransform(1, 0, 0, 1, 0, 0);
    };
    placed(context, sourceFor(scale() * (base.width / fullSize.width) * toOutput));

    applyLook(context, size.width, size.height);
    // After the lighting, so the new background is exactly the colour chosen.
    const made = applyBackground(context, size.width, size.height, placed);
    return { output, made };
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
      if (focusModelNote && !bgModelNote.hidden) {
        focusModelNote = false;
        bgModelNote.focus({ preventScroll: true });
      }
      askModelIfColourGaveUp();
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
      if (modelWorking()) {
        // This photo still has the old background: the file is made again once the model answers.
        saved = null;
        showModelWait();
        return;
      }
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

  shared.wireDropZones($$('[data-drop]'), files => openFile(files[0]));

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
        queueFixes();
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

  // The wheel scrolls the page, as everywhere else; Ctrl with the wheel zooms, and so does a
  // pinch on a touchpad (the browser sends it as a wheel with Ctrl held).
  crop.addEventListener('wheel', (event) => {
    if (!base || !event.ctrlKey) return;
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
  placeFaceButton.addEventListener('click', () => {
    // Asked for by the user, so it is a change to keep (the placement on opening is not).
    applyFace();
    shared.setUnsaved('passport', true);
  });
  faceRetryButton.addEventListener('click', () => {
    // Asked for: the photo is placed when the face is found, even if it was moved meanwhile.
    touched = false;
    findFace();
  });

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
  bgModelButton.addEventListener('click', () => askModel());
  bgRetryButton.addEventListener('click', () => {
    modelRetry = '';
    askModel();
  });
  bgWaitCancel.addEventListener('click', () => {
    if (!modelAsk) return;
    modelAsk.cancelled = true;
    modelAsk.request.abort();
  });
  fixUndo.addEventListener('click', () => {
    fixes.pop();
    // Any change after a download is unsaved again, Undo included.
    shared.setUnsaved('passport', true);
    renderBackground();
    showFixes();
    queueSave(true);
  });
  fixClear.addEventListener('click', () => {
    fixes = [];
    shared.setUnsaved('passport', true);
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
  shared.wireHoldButton(bgCompare, seeOriginal);
  shared.wireHoldButton(bgCompareStage, seeOriginal);

  /**
   * The phone bar mirrors the Save panel: the checks' summary (a tap goes to the list) and
   * Download, kept in step with them whatever changes them.
   */
  function renderPhoneBar() {
    phoneBar.hidden = !full;
    phoneDownload.disabled = downloadButton.disabled;
    const count = checkList.children.length;
    let summary = '';
    if (!checkBox.hidden) summary = count ? `${count} ${count === 1 ? 'thing' : 'things'} to check` : 'No problems found';
    else if (resultLine.textContent) summary = resultLine.textContent;
    phoneChecks.textContent = summary;
    phoneChecks.classList.toggle('has-problems', !checkBox.hidden && count > 0);
    phoneChecks.disabled = checkBox.hidden;
  }
  new MutationObserver(renderPhoneBar).observe(downloadButton, { attributes: true, attributeFilter: ['disabled'] });
  new MutationObserver(renderPhoneBar).observe(checkBox, { attributes: true, attributeFilter: ['hidden'] });
  new MutationObserver(renderPhoneBar).observe(checkList, { childList: true });
  new MutationObserver(renderPhoneBar).observe(resultLine, { childList: true, characterData: true, subtree: true });
  phoneChecks.addEventListener('click', () => {
    checkBox.scrollIntoView({ block: 'center' });
  });
  phoneDownload.addEventListener('click', (event) => {
    if (shared.isRepeatClick(event)) return;
    downloadButton.click();
  });

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
  outLimit.addEventListener('input', () => {
    queueSave();
    rememberChoices();
  });

  downloadButton.addEventListener('click', (event) => {
    if (!saved || modelWorking() || shared.isRepeatClick(event)) return;
    shared.saveBlob(new Blob([saved.bytes], { type: JPEG.mime }), `${core.baseName(fileName)}-${MODES[mode].suffix}.jpg`);
    shared.setUnsaved('passport', false);
  });

  // The stage has no size while its screen is hidden, and changes size with the window.
  new ResizeObserver(layout).observe(crop);

  buildSizes();
  outWidth.value = String(size.width);
  outHeight.value = String(size.height);
  renderSizes();
  renderSheet();
  renderBackground();
  restoreChoices();
  modeFromAddress();
})();
