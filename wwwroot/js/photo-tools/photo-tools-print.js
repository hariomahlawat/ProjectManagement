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
  const shapeNote = $('[data-sheet-shape]');
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
  let sheet = core.SHEET_SIZES[0];
  // Null fills the sheet; a number is what the user asked for.
  let copies = null;
  let format = PDFLib ? 'pdf' : 'jpg';
  let openRun = 0;

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

  /** One copy at its print size. A photo of another shape gives its middle. */
  function makeCopy() {
    const from = shared.sizeOf(source);
    const part = core.coverCrop(from.width, from.height, size.width, size.height);
    const cut = shared.newCanvas(part.width, part.height);
    cut.getContext('2d').drawImage(source, part.x, part.y, part.width, part.height, 0, 0, part.width, part.height);
    copy = shared.drawPhoto(cut, size.width, size.height, { opaque: true });
    shapeNote.hidden = part.whole;

    thumb.width = size.width;
    thumb.height = size.height;
    thumb.getContext('2d').drawImage(copy, 0, 0);
  }

  // ---------------------------------------------------------------------------
  // Drawing
  // ---------------------------------------------------------------------------

  /** Draws a sheet: white paper, the copies, and a thin line to cut along around each. */
  function drawSheet(context, layout, scale) {
    context.fillStyle = PAPER_COLOUR;
    context.fillRect(0, 0, layout.width * scale, layout.height * scale);
    context.imageSmoothingQuality = 'high';
    context.strokeStyle = CUT_COLOUR;
    const line = Math.max(1, 2 * scale);
    context.lineWidth = line;
    for (const { x, y } of layout.positions) {
      context.drawImage(copy, x * scale, y * scale, size.width * scale, size.height * scale);
      if (cutLines.checked) {
        context.strokeRect(
          x * scale - line / 2, y * scale - line / 2,
          size.width * scale + line, size.height * scale + line);
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
    downloadButton.disabled = !copy || placed === 0;
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
    fileLabel.textContent = name;
    emptyDrop.hidden = true;
    workspace.hidden = false;
    changeButton.hidden = false;
    makeCopy();
    render();
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
    let photo;
    try {
      photo = await shared.openPhoto(file);
    } catch (error) {
      if (mine === openRun) {
        shared.showError(error instanceof core.PhotoToolError ? error.message : 'This photo could not be opened.');
      }
      return;
    }
    if (mine !== openRun) {
      if (typeof photo.close === 'function') photo.close();
      return;
    }
    usePhoto(photo, file.name, false);
  }

  /** One picture of the whole sheet at 300 dpi. */
  async function sheetJpg(layout) {
    const page = shared.newCanvas(layout.width, layout.height);
    drawSheet(page.getContext('2d'), layout, 1);
    return core.setJpegDpi(await shared.canvasToBytes(page, JPEG, core.BEST_QUALITY), core.PRINT_DPI);
  }

  /**
   * One PDF page the size of the paper. The photo is stored once and placed at each position at
   * its true size, and the cut lines are drawn as lines, so the file is small and stays sharp.
   */
  async function sheetPdf(layout) {
    const pdf = core.sheetPdfPlan(layout, size.width, size.height);
    const file = await PDFLib.PDFDocument.create();
    file.setTitle('Print sheet');
    const photo = await file.embedJpg(await shared.canvasToBytes(copy, JPEG, core.BEST_QUALITY));
    const page = file.addPage([pdf.pageWidth, pdf.pageHeight]);
    pdf.photos.forEach((box, index) => {
      page.drawImage(photo, box);
      if (cutLines.checked) {
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
    const layout = plan();
    if (!copy || !layout.positions.length) return;
    shared.clearError();
    downloadButton.disabled = true;
    const asPdf = format === 'pdf' && PDFLib;
    try {
      const bytes = asPdf ? await sheetPdf(layout) : await sheetJpg(layout);
      shared.saveBlob(
        new Blob([bytes], { type: asPdf ? 'application/pdf' : JPEG.mime }),
        `${core.baseName(fileName)}-print-${sheet.key}.${asPdf ? 'pdf' : 'jpg'}`);
    } catch (error) {
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      shared.showError(error instanceof core.PhotoToolError ? error.message : 'The print sheet could not be made.');
    } finally {
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
    openRun += 1;
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
