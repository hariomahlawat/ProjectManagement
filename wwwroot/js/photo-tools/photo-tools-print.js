// PRISM Photo Tools — Print sheet.
//
// Copies of one photo on a sheet of paper, with cut lines, saved as a PDF whose page is the
// paper's true size, or as one picture at 300 dpi. The photo comes from Passport photo (already
// cropped to its size) or from a file, whose middle is then used. The preview and the saved
// sheet come from the same layout. Everything happens in this browser; the PDF is written by
// pdf-lib, which ships with the application.
(() => {
  'use strict';

  const core = window.PrismPhotoTools;
  const shared = window.PrismPhotoCanvas;
  // Without the PDF library the sheet is still offered as a JPG.
  const PDFLib = window.PDFLib;
  const view = document.querySelector('[data-view="sheet"]');
  if (!core || !shared || !view) {
    return;
  }

  const JPEG = core.TYPES.jpeg;
  const PAPER_MARGIN = 20;
  const PAPER_COLOUR = '#ffffff';
  const CUT_COLOUR = '#9a9a9a';
  const CUT_GREY = 0x9a / 255;
  const FORMAT_NOTES = {
    pdf: 'Recommended. The PDF page is the exact paper size, so the photos print at true size.',
    jpg: 'One picture of the whole sheet at 300 dpi. Use it when a photo shop asks for a JPG.'
  };

  const $ = (selector) => view.querySelector(selector);
  const $$ = (selector) => Array.from(view.querySelectorAll(selector));

  const input = $('[data-sheet-input]');
  const emptyDrop = $('[data-empty]');
  const workspace = $('[data-workspace]');
  const changeButton = $('[data-action="change-photo"]');
  const fileLabel = $('[data-sheet-file]');
  const stage = $('[data-sheet-stage]');
  const canvas = $('[data-sheet-canvas]');
  const caption = $('[data-sheet-caption]');
  const thumb = $('[data-sheet-thumb]');
  const origin = $('[data-sheet-origin]');
  const placeBox = $('[data-sheet-place]');
  const zoomSlider = $('[data-sheet-zoom]');
  const centreButton = $('[data-action="centre-photo"]');
  const sizes = $('[data-sheet-sizes]');
  const papers = $('[data-sheet-papers]');
  const copiesValue = $('[data-sheet-copies]');
  const fewerButton = $('[data-action="fewer-copies"]');
  const moreButton = $('[data-action="more-copies"]');
  const fillButton = $('[data-action="fill-sheet"]');
  const fitNote = $('[data-sheet-fit]');
  const cutLines = $('[data-sheet-cut]');
  const downloadButton = $('[data-action="download-sheet"]');
  const downloadLabel = $('[data-sheet-download-label]');
  const formats = $('[data-sheet-formats]');
  const formatButtons = $$('[data-sheet-format]');
  const formatNote = $('[data-sheet-format-note]');

  // The photo as it was given, the size one copy is printed at (pixels at 300 dpi), and the
  // copy itself: the photo's middle at that size, drawn once and reused.
  let source = null;
  let fileName = '';
  let fromPassport = false;
  let made = null;
  let size = { width: core.PASSPORT_SIZES[0].width, height: core.PASSPORT_SIZES[0].height };
  let copy = null;
  // Where the copy is taken from a photo of another shape: the middle, until the user moves it.
  let place = { zoom: 1, x: 0.5, y: 0.5 };
  let movable = false;
  let sheet = core.SHEET_SIZES[0];
  // Null fills the sheet; a number is what the user asked for.
  let copies = null;
  let format = PDFLib ? 'pdf' : 'jpg';
  const opener = shared.latestPhotoOpener();
  // True while a sheet is being made, so Download cannot be pressed again until it is saved.
  let downloading = false;

  function sizeOptions() {
    const options = [...core.PASSPORT_SIZES];
    if (made) {
      options.push({
        key: 'made',
        label: `${core.pixelsToMillimetres(made.width)} × ${core.pixelsToMillimetres(made.height)} mm`,
        note: 'As made in Passport photo',
        width: made.width,
        height: made.height
      });
    }
    return options;
  }

  function plan() {
    return core.sheetLayout(sheet, size.width, size.height, copies);
  }

  /**
   * One copy at its print size. A photo of another shape gives the piece the user placed, the
   * middle to begin with; the small picture of the copy is then where it is dragged.
   */
  function makeCopy() {
    const from = shared.sizeOf(source);
    const part = core.coverCrop(from.width, from.height, size.width, size.height, place);
    // Read straight from the photo: copying the part out first would make a full-size canvas
    // on every move while the photo is dragged.
    copy = shared.drawPhoto(source, size.width, size.height, { opaque: true, area: part });

    movable = !part.whole;
    placeBox.hidden = !movable;
    thumb.classList.toggle('is-movable', movable);
    if (movable) {
      thumb.tabIndex = 0;
      thumb.setAttribute('role', 'group');
      thumb.setAttribute('aria-label', 'Photo position. Drag to move it, or use the arrow keys; plus and minus zoom.');
      thumb.removeAttribute('aria-hidden');
    } else {
      thumb.removeAttribute('tabindex');
      thumb.removeAttribute('role');
      thumb.removeAttribute('aria-label');
      thumb.setAttribute('aria-hidden', 'true');
    }
    zoomSlider.value = String(Math.round(place.zoom * 100));
    centreButton.disabled = place.zoom === 1 && place.x === 0.5 && place.y === 0.5;

    thumb.width = size.width;
    thumb.height = size.height;
    thumb.getContext('2d').drawImage(copy, 0, 0);
  }

  // Moving the photo makes the copy again from the full photo, at most once every frame or so.
  // A timer, not an animation frame: the copy must also be made while the page is not showing.
  let placing = 0;
  function placeChanged(next) {
    place = next;
    if (placing) return;
    placing = setTimeout(() => {
      placing = 0;
      if (!source) return;
      makeCopy();
      drawPreview(plan());
    }, 16);
  }

  function resetPlace() {
    place = { zoom: 1, x: 0.5, y: 0.5 };
  }

  // ---------------------------------------------------------------------------
  // Drawing
  // ---------------------------------------------------------------------------

  /** What a sheet is made of, taken at once so later changes on the screen cannot mix in. */
  function sheetParts() {
    return { copy, width: size.width, height: size.height, cuts: cutLines.checked };
  }

  /** Draws a sheet: white paper, the copies, and a thin line to cut along around each. */
  function drawSheet(context, layout, scale, parts = sheetParts()) {
    context.fillStyle = PAPER_COLOUR;
    context.fillRect(0, 0, layout.width * scale, layout.height * scale);
    context.imageSmoothingQuality = 'high';
    context.strokeStyle = CUT_COLOUR;
    const line = Math.max(1, 2 * scale);
    context.lineWidth = line;
    for (const { x, y } of layout.positions) {
      context.drawImage(parts.copy, x * scale, y * scale, parts.width * scale, parts.height * scale);
      if (parts.cuts) {
        context.strokeRect(
          x * scale - line / 2, y * scale - line / 2,
          parts.width * scale + line, parts.height * scale + line);
      }
    }
  }

  function drawPreview(layout) {
    const width = stage.clientWidth;
    const height = stage.clientHeight;
    if (!copy || !width || !height) return;
    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    const context = canvas.getContext('2d');
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, width, height);

    const scale = Math.min((width - PAPER_MARGIN * 2) / layout.width, (height - PAPER_MARGIN * 2) / layout.height);
    context.translate((width - layout.width * scale) / 2, (height - layout.height * scale) / 2);
    drawSheet(context, layout, scale);
  }

  // ---------------------------------------------------------------------------
  // Screen
  // ---------------------------------------------------------------------------

  /**
   * A group of choice buttons. The buttons are built once for a list of options and only their
   * pressed state changes after that, so the button just pressed keeps the keyboard focus.
   */
  function choices(host, options, current, pick) {
    const list = options.map(option => `${option.key}:${option.width}x${option.height}`).join('|');
    if (host.dataset.options === list) {
      Array.from(host.children).forEach((button, index) => {
        button.setAttribute('aria-pressed', String(options[index] === current));
      });
      return;
    }
    host.dataset.options = list;
    host.replaceChildren();
    for (const option of options) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'phot-size';
      button.setAttribute('aria-pressed', String(option === current));
      const label = document.createElement('span');
      label.className = 'phot-size__label';
      label.textContent = option.label;
      const note = document.createElement('span');
      note.className = 'phot-size__note';
      note.textContent = option.note;
      button.append(label, note);
      button.addEventListener('click', () => pick(option));
      host.append(button);
    }
  }

  function render() {
    const sizeList = sizeOptions();
    const currentSize = sizeList.find(item => item.width === size.width && item.height === size.height);
    choices(sizes, sizeList, currentSize, (option) => {
      size = { width: option.width, height: option.height };
      copies = null;
      resetPlace();
      makeCopy();
      render();
    });
    choices(papers, core.SHEET_SIZES, sheet, (option) => {
      sheet = option;
      copies = null;
      render();
    });
    origin.textContent = fromPassport ? 'From Passport photo' : 'From your file';

    const layout = plan();
    const placed = layout.positions.length;
    copiesValue.textContent = String(placed);
    fewerButton.disabled = placed <= 1;
    moreButton.disabled = placed >= layout.count;
    fillButton.disabled = placed >= layout.count;
    downloadButton.disabled = downloading || !copy || placed === 0;
    for (const button of formatButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.sheetFormat === format));
    }
    formats.hidden = !PDFLib;
    formatNote.textContent = FORMAT_NOTES[format];
    downloadLabel.textContent = `Download sheet (${format.toUpperCase()})`;

    if (!layout.count) {
      caption.textContent = `A ${currentSize.label} photo is too big for ${sheet.label} paper. Choose a larger paper.`;
      fitNote.textContent = '';
    } else {
      const noun = placed === 1 ? '1 photo' : `${placed} photos`;
      const turned = layout.width > layout.height ? ', paper on its side' : '';
      caption.textContent = `${noun} of ${currentSize.label} on ${sheet.label} paper${turned}`;
      fitNote.textContent = placed === layout.count
        ? `The sheet is full: ${layout.count} fit on this paper.`
        : `${layout.count} fit on this paper. The unused part stays in one piece, so it can be cut off and used again.`;
    }
    drawPreview(layout);
  }

  function usePhoto(photo, name, passport) {
    if (source && source !== photo && typeof source.close === 'function') source.close();
    source = photo;
    fileName = name;
    fromPassport = passport;
    const given = shared.sizeOf(photo);
    if (passport) {
      // A passport photo is already the size it prints at.
      size = given;
      made = core.PASSPORT_SIZES.some(item => item.width === given.width && item.height === given.height) ? null : given;
    } else {
      made = null;
      if (!core.PASSPORT_SIZES.some(item => item.width === size.width && item.height === size.height)) {
        size = { width: core.PASSPORT_SIZES[0].width, height: core.PASSPORT_SIZES[0].height };
      }
    }
    copies = null;
    resetPlace();
    fileLabel.textContent = name;
    emptyDrop.hidden = true;
    workspace.hidden = false;
    changeButton.hidden = false;
    makeCopy();
    render();
  }

  async function openFile(file) {
    // Only the photo chosen last is used: an earlier one that opens late is thrown away.
    const photo = await opener.open(file);
    if (!photo) return;
    usePhoto(photo, file.name, false);
  }

  /** One picture of the whole sheet at 300 dpi. */
  async function sheetJpg(layout, parts) {
    const page = shared.newCanvas(layout.width, layout.height);
    drawSheet(page.getContext('2d'), layout, 1, parts);
    return core.setJpegDpi(await shared.canvasToBytes(page, JPEG, core.BEST_QUALITY), core.PRINT_DPI);
  }

  /**
   * One PDF page the size of the paper. The photo is stored once and placed at each position at
   * its true size, and the cut lines are drawn as lines, so the file is small and stays sharp.
   */
  async function sheetPdf(layout, parts) {
    const pdf = core.sheetPdfPlan(layout, parts.width, parts.height);
    const file = await PDFLib.PDFDocument.create();
    file.setTitle('Print sheet');
    const photo = await file.embedJpg(await shared.canvasToBytes(parts.copy, JPEG, core.BEST_QUALITY));
    const page = file.addPage([pdf.pageWidth, pdf.pageHeight]);
    pdf.photos.forEach((box, index) => {
      page.drawImage(photo, box);
      if (parts.cuts) {
        page.drawRectangle({
          ...pdf.cuts[index],
          borderColor: PDFLib.rgb(CUT_GREY, CUT_GREY, CUT_GREY),
          borderWidth: pdf.lineWidth
        });
      }
    });
    return file.save();
  }

  async function download() {
    if (downloading) return;
    const layout = plan();
    if (!copy || !layout.positions.length) return;
    shared.clearError();
    downloading = true;
    downloadButton.disabled = true;
    // Everything the file needs is taken now: the settings may change while it is being made.
    const parts = sheetParts();
    const asPdf = format === 'pdf' && PDFLib;
    const name = `${core.baseName(fileName)}-print-${sheet.key}.${asPdf ? 'pdf' : 'jpg'}`;
    try {
      const bytes = asPdf ? await sheetPdf(layout, parts) : await sheetJpg(layout, parts);
      shared.saveBlob(new Blob([bytes], { type: asPdf ? 'application/pdf' : JPEG.mime }), name);
    } catch (error) {
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      shared.showError(error instanceof core.PhotoToolError ? error.message : 'The print sheet could not be made.');
    } finally {
      downloading = false;
      render();
    }
  }

  // ---------------------------------------------------------------------------
  // Events
  // ---------------------------------------------------------------------------

  // Passport photo hands its finished photo over, already at the size it prints at.
  document.addEventListener('phot:print-sheet', (event) => {
    const { photo, name } = event.detail || {};
    if (!photo) return;
    opener.cancel();
    shared.clearError();
    usePhoto(photo, name || 'photo.jpg', true);
    window.location.hash = 'sheet';
  });

  for (const button of $$('[data-action="choose-photo"], [data-action="change-photo"]')) {
    button.addEventListener('click', () => input.click());
  }

  input.addEventListener('change', () => {
    openFile(input.files && input.files[0]);
    input.value = '';
  });

  shared.wireDropZones($$('[data-drop]'), files => openFile(files[0]));

  fewerButton.addEventListener('click', () => {
    copies = Math.max(1, plan().positions.length - 1);
    render();
  });
  moreButton.addEventListener('click', () => {
    const layout = plan();
    const next = layout.positions.length + 1;
    copies = next >= layout.count ? null : next;
    render();
  });
  fillButton.addEventListener('click', () => {
    copies = null;
    render();
  });
  cutLines.addEventListener('change', render);

  // The small picture of the copy is dragged to choose which part of the photo is printed.
  // Only one pointer drags: a second finger or a right-click is ignored.
  let dragFrom = null;
  thumb.addEventListener('pointerdown', (event) => {
    if (!movable || dragFrom || !shared.isMainPointer(event)) return;
    event.preventDefault();
    thumb.setPointerCapture(event.pointerId);
    dragFrom = { id: event.pointerId, x: event.clientX, y: event.clientY };
  });
  thumb.addEventListener('pointermove', (event) => {
    if (!dragFrom || !movable || event.pointerId !== dragFrom.id) return;
    const box = thumb.getBoundingClientRect();
    const from = shared.sizeOf(source);
    const next = core.dragCover(
      from.width, from.height, size.width, size.height, place,
      (event.clientX - dragFrom.x) / box.width, (event.clientY - dragFrom.y) / box.height);
    dragFrom = { id: dragFrom.id, x: event.clientX, y: event.clientY };
    placeChanged(next);
  });
  for (const type of ['pointerup', 'pointercancel']) {
    thumb.addEventListener(type, (event) => {
      if (dragFrom && event.pointerId === dragFrom.id) dragFrom = null;
    });
  }
  thumb.addEventListener('keydown', (event) => {
    if (!movable) return;
    const step = event.shiftKey ? 0.02 : 0.1;
    // The arrow moves the frame over the photo, so the photo slides the other way.
    const moves = { ArrowLeft: [step, 0], ArrowRight: [-step, 0], ArrowUp: [0, step], ArrowDown: [0, -step] };
    const from = shared.sizeOf(source);
    if (moves[event.key]) {
      event.preventDefault();
      placeChanged(core.dragCover(from.width, from.height, size.width, size.height, place, moves[event.key][0], moves[event.key][1]));
    } else if (event.key === '+' || event.key === '=' || event.key === '-') {
      event.preventDefault();
      const zoom = Math.min(core.COVER_MAX_ZOOM, Math.max(1, place.zoom * (event.key === '-' ? 1 / 1.1 : 1.1)));
      placeChanged({ ...place, zoom });
    }
  });
  zoomSlider.addEventListener('input', () => {
    placeChanged({ ...place, zoom: Number(zoomSlider.value) / 100 });
  });
  centreButton.addEventListener('click', () => {
    resetPlace();
    placeChanged(place);
  });
  for (const button of formatButtons) {
    button.addEventListener('click', () => {
      format = button.dataset.sheetFormat;
      render();
    });
  }
  downloadButton.addEventListener('click', download);

  // The stage has no size while its screen is hidden, and changes size with the window.
  new ResizeObserver(() => {
    if (copy) drawPreview(plan());
  }).observe(stage);
})();
