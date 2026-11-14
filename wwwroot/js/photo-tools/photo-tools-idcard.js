// PRISM Photo Tools — ID card copy.
//
// The front and back of a card (85.6 × 54 mm: Aadhaar, PAN, a driving licence, a bank card) on one
// A4 page at true size, saved as a PDF whose page is A4, or as one picture at 300 dpi. Each side is
// framed at the card's shape from its photo: dragged and zoomed so the table around the card is
// left out, and turned a quarter when the card was photographed on its side. The layout is
// core.idCardLayout; the PDF uses the same page plan as Print sheet. Everything happens in this
// browser; the PDF is written by pdf-lib, which ships with the application.
(() => {
  'use strict';

  const core = window.PrismPhotoTools;
  const shared = window.PrismPhotoCanvas;
  // Without the PDF library the page is still offered as a JPG.
  const PDFLib = window.PDFLib;
  const view = document.querySelector('[data-view="idcard"]');
  if (!core || !shared || !view) {
    return;
  }

  const JPEG = core.TYPES.jpeg;
  const PAPER_MARGIN = 20;
  const PAPER_COLOUR = '#ffffff';
  const OUTLINE_COLOUR = '#9a9a9a';
  const OUTLINE_GREY = 0x9a / 255;
  const EMPTY_COLOUR = '#b8b8b8';
  const FORMAT_NOTES = {
    pdf: 'Recommended. The PDF page is A4, so the cards print at their true size.',
    jpg: 'One picture of the whole page at 300 dpi. Use it when a JPG is asked for.'
  };

  const $ = (selector) => view.querySelector(selector);
  const $$ = (selector) => Array.from(view.querySelectorAll(selector));

  const input = $('[data-idcard-input]');
  const stage = $('[data-idcard-stage]');
  const canvas = $('[data-idcard-canvas]');
  const caption = $('[data-idcard-caption]');
  const shapeButtons = $$('[data-idcard-shape]');
  const outline = $('[data-idcard-outline]');
  const grey = $('[data-idcard-grey]');
  const formats = $('[data-idcard-formats]');
  const formatButtons = $$('[data-idcard-format]');
  const formatNote = $('[data-idcard-format-note]');
  const resultLine = $('[data-idcard-result]');
  const downloadButton = $('[data-action="download-idcard"]');
  const downloadLabel = $('[data-idcard-download-label]');
  // A copy of Download at the foot of the screen while this one is out of view.
  shared.wireSaveBar($('[data-save-bar]'), downloadButton, { line: resultLine });

  let tall = false;
  let format = PDFLib ? 'pdf' : 'jpg';
  // Kept in this browser: the card's shape, PDF or JPG, the outline and black and white.
  const remembered = shared.rememberedChoices('idcard');

  function rememberChoices() {
    remembered.save({ tall, format, outline: outline.checked, grey: grey.checked });
  }

  (function restoreChoices() {
    const kept = remembered.load();
    if (!kept) return;
    if (typeof kept.tall === 'boolean') tall = kept.tall;
    if (kept.format === 'jpg' || (kept.format === 'pdf' && PDFLib)) format = kept.format;
    if (typeof kept.outline === 'boolean') outline.checked = kept.outline;
    if (typeof kept.grey === 'boolean') grey.checked = kept.grey;
  })();

  // Each side: its photo (turned as asked), the file's name, where the card is taken from in the
  // photo (as in Print sheet: zoom 1 to 3, and x and y from 0 to 1), and the card drawn at its
  // print size, made once and reused.
  const sides = $$('[data-card-side]').map((panel) => {
    const key = panel.dataset.cardSide;
    return {
      key,
      title: key === 'front' ? 'Front' : 'Back',
      panel,
      source: null,
      name: '',
      place: { zoom: 1, x: 0.5, y: 0.5 },
      card: null,
      placing: 0,
      opener: shared.latestPhotoOpener(),
      opening: 0,
      empty: panel.querySelector('[data-side-empty]'),
      photo: panel.querySelector('[data-side-photo]'),
      thumb: panel.querySelector('[data-side-thumb]'),
      file: panel.querySelector('[data-side-file]'),
      zoom: panel.querySelector('[data-side-zoom]'),
      centre: panel.querySelector('[data-side-action="centre"]')
    };
  });
  const [front, back] = sides;
  // The side a single photo replaces when both have one: the one last chosen or moved.
  let lastSide = back;
  // The side the file chooser is open for; null for the page as a whole.
  let chooseFor = null;
  // True while the page is being made, so Download cannot be pressed again until it is saved.
  let downloading = false;

  function layout() {
    return core.idCardLayout(tall);
  }

  function hasCard() {
    return sides.some(side => side.card);
  }

  // ---------------------------------------------------------------------------
  // Each side
  // ---------------------------------------------------------------------------

  /** The card at its print size: the part of the photo the user placed, in colour or grey. */
  function makeCard(side) {
    if (!side.source) {
      side.card = null;
      return;
    }
    const { card } = layout();
    const from = shared.sizeOf(side.source);
    const part = core.coverCrop(from.width, from.height, card.width, card.height, side.place);
    // Read straight from the photo: copying the part out first would make a full-size canvas on
    // every move while the photo is dragged.
    side.card = shared.drawPhoto(side.source, card.width, card.height, { opaque: true, area: part, greyscale: grey.checked });
    side.thumb.width = card.width;
    side.thumb.height = card.height;
    side.thumb.getContext('2d').drawImage(side.card, 0, 0);
  }

  function renderSide(side) {
    side.empty.hidden = Boolean(side.source);
    side.photo.hidden = !side.source;
    if (!side.source) return;
    const size = shared.sizeOf(side.source);
    side.file.textContent = `${side.name} · ${size.width} × ${size.height} px`;
    side.zoom.value = String(Math.round(side.place.zoom * 100));
    side.centre.disabled = side.place.zoom === 1 && side.place.x === 0.5 && side.place.y === 0.5;
  }

  // Counts every change to what the page would hold, so a download only clears "unsaved" when
  // nothing changed while it was being made.
  let revision = 0;

  function changed() {
    revision += 1;
    shared.setUnsaved('idcard', hasCard());
    render();
  }

  // Moving the photo makes the card again from the full photo, at most once every frame or so.
  // A timer, not an animation frame: the card must also be made while the page is not showing.
  function placeChanged(side, next) {
    side.place = next;
    lastSide = side;
    if (side.placing) return;
    side.placing = setTimeout(() => {
      side.placing = 0;
      if (!side.source) return;
      makeCard(side);
      changed();
    }, 16);
  }

  async function openInto(side, file) {
    // Only the photo chosen last for this side is used: an earlier one that opens late is dropped.
    // The side counts as taken while its photo opens, so a second photo given meanwhile goes to
    // the other side instead of replacing this one.
    side.opening += 1;
    let photo;
    try {
      photo = await side.opener.open(file);
    } finally {
      side.opening -= 1;
    }
    if (!photo) return;
    if (side.source && typeof side.source.close === 'function') side.source.close();
    side.source = photo;
    side.name = file.name;
    side.place = { zoom: 1, x: 0.5, y: 0.5 };
    lastSide = side;
    makeCard(side);
    changed();
  }

  /**
   * Photos given to the screen: two (or more) fill the front and the back; one goes to the side it
   * was given to, else the first side without a photo, else the side last worked on.
   */
  function give(files, side = null) {
    const list = Array.from(files || []);
    if (!list.length) return;
    if (!side && list.length >= 2) {
      openInto(front, list[0]);
      openInto(back, list[1]);
      return;
    }
    const target = side || sides.find(item => !item.source && !item.opening) || lastSide;
    openInto(target, list[0]);
  }

  /** Turns a side's photo a quarter to the right, for a card photographed on its side. */
  function turn(side) {
    if (!side.source) return;
    shared.clearError();
    const size = shared.sizeOf(side.source);
    let turned;
    try {
      turned = shared.newCanvas(size.height, size.width);
      const context = turned.getContext('2d');
      if (!context) throw new Error('No canvas');
      context.translate(turned.width / 2, turned.height / 2);
      context.rotate(Math.PI / 2);
      context.drawImage(side.source, -size.width / 2, -size.height / 2);
    } catch (error) {
      // A phone may not make a canvas as large as the photo. The photo stays as it was.
      console.error(error);
      shared.showError('This photo is too large for this browser to turn. Choose a smaller photo.');
      return;
    }
    if (typeof side.source.close === 'function') side.source.close();
    side.source = turned;
    side.place = { zoom: 1, x: 0.5, y: 0.5 };
    lastSide = side;
    makeCard(side);
    changed();
  }

  function removeSide(side) {
    side.opener.cancel();
    if (side.source && typeof side.source.close === 'function') side.source.close();
    side.source = null;
    side.card = null;
    side.name = '';
    changed();
  }

  // ---------------------------------------------------------------------------
  // The page
  // ---------------------------------------------------------------------------

  /** What a page is made of, taken at once so later changes on the screen cannot mix in. */
  function pageParts() {
    return { cards: sides.map(side => side.card), outline: outline.checked };
  }

  /**
   * Draws the page: white paper, each card at its place with a thin outline around it. On the
   * screen a side without a photo is shown as a dashed box with its name; it is left out of a file.
   */
  function drawPage(context, page, scale, parts, onScreen) {
    context.fillStyle = PAPER_COLOUR;
    context.fillRect(0, 0, page.width * scale, page.height * scale);
    context.imageSmoothingQuality = 'high';
    const line = Math.max(1, 2 * scale);
    page.positions.forEach(({ x, y }, index) => {
      const card = parts.cards[index];
      const left = x * scale;
      const top = y * scale;
      const width = page.card.width * scale;
      const height = page.card.height * scale;
      if (card) {
        context.drawImage(card, left, top, width, height);
        if (parts.outline) {
          context.strokeStyle = OUTLINE_COLOUR;
          context.lineWidth = line;
          context.setLineDash([]);
          context.strokeRect(left - line / 2, top - line / 2, width + line, height + line);
        }
      } else if (onScreen) {
        context.strokeStyle = EMPTY_COLOUR;
        context.lineWidth = 1;
        context.setLineDash([6, 4]);
        context.strokeRect(left + 0.5, top + 0.5, width - 1, height - 1);
        context.setLineDash([]);
        context.fillStyle = EMPTY_COLOUR;
        context.font = `600 ${Math.max(11, Math.round(height / 6))}px Mulish, system-ui, sans-serif`;
        context.textAlign = 'center';
        context.textBaseline = 'middle';
        context.fillText(sides[index].title, left + width / 2, top + height / 2);
      }
    });
  }

  function drawPreview() {
    const width = stage.clientWidth;
    const height = stage.clientHeight;
    if (!width || !height) return;
    const page = layout();
    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    const context = canvas.getContext('2d');
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, width, height);
    const scale = Math.min((width - PAPER_MARGIN * 2) / page.width, (height - PAPER_MARGIN * 2) / page.height);
    context.translate((width - page.width * scale) / 2, (height - page.height * scale) / 2);
    drawPage(context, page, scale, pageParts(), true);
  }

  function render() {
    for (const side of sides) renderSide(side);
    for (const button of shapeButtons) {
      button.setAttribute('aria-pressed', String((button.dataset.idcardShape === 'tall') === tall));
    }
    for (const button of formatButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.idcardFormat === format));
    }
    formats.hidden = !PDFLib;
    formatNote.textContent = FORMAT_NOTES[format];
    downloadLabel.textContent = `Download page (${format.toUpperCase()})`;
    downloadButton.disabled = downloading || !hasCard();

    const what = front.card && back.card ? 'Front and back' : front.card ? 'Front only' : back.card ? 'Back only' : '';
    const both = front.card && back.card;
    caption.textContent = what
      ? `${what} on A4 paper, ${both ? 'each' : 'at'} ${core.ID_CARD.label}.`
      : 'Add a photo of the front of the card, and of the back.';
    resultLine.className = 'phot-result';
    resultLine.textContent = what ? `Ready: ${what.toLowerCase()}, A4.` : '';
    drawPreview();
  }

  /** One picture of the whole page at 300 dpi. */
  async function pageJpg(page, parts) {
    const picture = shared.newCanvas(page.width, page.height);
    drawPage(picture.getContext('2d'), page, 1, parts, false);
    return core.setJpegDpi(await shared.canvasToBytes(picture, JPEG, core.BEST_QUALITY), core.PRINT_DPI);
  }

  /** One A4 PDF page; each card is stored once at its true size and its outline drawn as lines. */
  async function pagePdf(page, parts) {
    const plan = core.sheetPdfPlan(page, page.card.width, page.card.height);
    const file = await PDFLib.PDFDocument.create();
    file.setTitle('ID card copy');
    const sheet = file.addPage([plan.pageWidth, plan.pageHeight]);
    for (let index = 0; index < parts.cards.length; index += 1) {
      const card = parts.cards[index];
      if (!card) continue;
      const image = await file.embedJpg(await shared.canvasToBytes(card, JPEG, core.BEST_QUALITY));
      sheet.drawImage(image, plan.photos[index]);
      if (parts.outline) {
        sheet.drawRectangle({
          ...plan.cuts[index],
          borderColor: PDFLib.rgb(OUTLINE_GREY, OUTLINE_GREY, OUTLINE_GREY),
          borderWidth: plan.lineWidth
        });
      }
    }
    return file.save();
  }

  async function download() {
    if (downloading || !hasCard()) return;
    shared.clearError();
    downloading = true;
    render();
    // Everything the file needs is taken now: the settings may change while it is being made.
    const page = layout();
    const parts = pageParts();
    const madeAt = revision;
    const asPdf = format === 'pdf' && PDFLib;
    const name = `id-card-copy.${asPdf ? 'pdf' : 'jpg'}`;
    let saved = '';
    try {
      const bytes = asPdf ? await pagePdf(page, parts) : await pageJpg(page, parts);
      shared.saveBlob(new Blob([bytes], { type: asPdf ? 'application/pdf' : JPEG.mime }), name);
      // A change made while the file was being made is not in it, and is still unsaved.
      if (revision === madeAt) shared.setUnsaved('idcard', false);
      saved = `Saved: ${name}, ${core.formatBytes(bytes.length)}.`;
    } catch (error) {
      if (!(error instanceof core.PhotoToolError)) console.error(error);
      shared.showError(error instanceof core.PhotoToolError ? error.message : 'The page could not be made.');
    } finally {
      downloading = false;
      render();
    }
    if (saved) {
      resultLine.className = 'phot-result is-ready';
      resultLine.textContent = saved;
    }
  }

  // ---------------------------------------------------------------------------
  // Events
  // ---------------------------------------------------------------------------

  input.addEventListener('change', () => {
    const files = Array.from(input.files || []);
    input.value = '';
    give(files, files.length === 1 ? chooseFor : null);
  });

  // Photos pasted with Ctrl+V, or dropped on the page: one or both sides.
  shared.receivePhotos('idcard', files => give(files));
  shared.wireDropZones($$('[data-idcard-drop]'), files => give(files));

  for (const side of sides) {
    shared.wireDropZones([side.panel], files => give(files.slice(0, 1), side));
    for (const button of side.panel.querySelectorAll('[data-side-action="choose"]')) {
      button.addEventListener('click', () => {
        chooseFor = side;
        input.click();
      });
    }
    side.panel.querySelector('[data-side-action="turn"]').addEventListener('click', () => turn(side));
    side.panel.querySelector('[data-side-action="remove"]').addEventListener('click', () => removeSide(side));
    side.centre.addEventListener('click', () => placeChanged(side, { zoom: 1, x: 0.5, y: 0.5 }));
    side.zoom.addEventListener('input', () => {
      placeChanged(side, { ...side.place, zoom: Number(side.zoom.value) / 100 });
    });

    // The picture of the card is dragged to choose which part of the photo is printed. Only one
    // pointer drags: a second finger or a right-click is ignored.
    let dragFrom = null;
    const { thumb } = side;
    thumb.addEventListener('pointerdown', (event) => {
      if (!side.source || dragFrom || !shared.isMainPointer(event)) return;
      event.preventDefault();
      thumb.setPointerCapture(event.pointerId);
      dragFrom = { id: event.pointerId, x: event.clientX, y: event.clientY };
    });
    thumb.addEventListener('pointermove', (event) => {
      if (!dragFrom || !side.source || event.pointerId !== dragFrom.id) return;
      const box = thumb.getBoundingClientRect();
      const from = shared.sizeOf(side.source);
      const { card } = layout();
      const next = core.dragCover(
        from.width, from.height, card.width, card.height, side.place,
        (event.clientX - dragFrom.x) / box.width, (event.clientY - dragFrom.y) / box.height);
      dragFrom = { id: dragFrom.id, x: event.clientX, y: event.clientY };
      placeChanged(side, next);
    });
    for (const type of ['pointerup', 'pointercancel']) {
      thumb.addEventListener(type, (event) => {
        if (dragFrom && event.pointerId === dragFrom.id) dragFrom = null;
      });
    }
    thumb.addEventListener('keydown', (event) => {
      if (!side.source) return;
      const step = event.shiftKey ? 0.02 : 0.1;
      // The arrow moves the frame over the photo, so the photo slides the other way.
      const moves = { ArrowLeft: [step, 0], ArrowRight: [-step, 0], ArrowUp: [0, step], ArrowDown: [0, -step] };
      const from = shared.sizeOf(side.source);
      const { card } = layout();
      if (moves[event.key]) {
        event.preventDefault();
        placeChanged(side, core.dragCover(from.width, from.height, card.width, card.height, side.place, moves[event.key][0], moves[event.key][1]));
      } else if (event.key === '+' || event.key === '=' || event.key === '-') {
        event.preventDefault();
        const zoom = Math.min(core.COVER_MAX_ZOOM, Math.max(1, side.place.zoom * (event.key === '-' ? 1 / 1.1 : 1.1)));
        placeChanged(side, { ...side.place, zoom });
      }
    });
  }

  for (const button of shapeButtons) {
    button.addEventListener('click', () => {
      tall = button.dataset.idcardShape === 'tall';
      rememberChoices();
      for (const side of sides) makeCard(side);
      changed();
    });
  }
  outline.addEventListener('change', () => {
    rememberChoices();
    changed();
  });
  grey.addEventListener('change', () => {
    rememberChoices();
    for (const side of sides) makeCard(side);
    changed();
  });
  for (const button of formatButtons) {
    button.addEventListener('click', () => {
      format = button.dataset.idcardFormat;
      rememberChoices();
      render();
    });
  }
  downloadButton.addEventListener('click', (event) => {
    if (shared.isRepeatClick(event)) return;
    download();
  });

  // The stage has no size while its screen is hidden, and changes size with the window.
  new ResizeObserver(drawPreview).observe(stage);

  render();
})();
