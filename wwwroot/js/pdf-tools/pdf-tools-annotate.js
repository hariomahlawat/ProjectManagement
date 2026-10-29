// PRISM PDF Tools — Sign & annotate editor.
//
// Pages are rendered by pdf.js; items (text, signatures, images, shapes) live in an HTML
// overlay positioned in PDF display points and are flattened into the PDF by
// PrismPdfTools.applyAnnotations when the user saves. Nothing leaves the browser.
(() => {
  'use strict';

  const ui = window.PrismPdfToolsUi;
  const core = window.PrismPdfTools;
  const PDFLib = window.PDFLib;
  const section = document.querySelector('[data-tool="annotate"]');
  if (!ui || !core || !PDFLib || !section) {
    return;
  }

  const { $, $$ } = ui;
  const editor = $('[data-editor]', section);
  const summary = $('[data-summary]', section);
  const colourInput = $('[data-annotate-color]', section);
  const fontSizeInput = $('[data-annotate-font-size]', section);
  const deleteButton = $('[data-annotate-delete]', section);
  const imageInput = $('[data-annotate-image-input]', section);
  const toolButtons = $$('[data-annotate-tool]', section);
  const dialog = $('[data-signature-dialog]', section);

  const MAX_PAGE_WIDTH = 900;
  const MIN_SIZE = 6;
  const SHAPE_DEFAULTS = {
    highlight: { width: 140, height: 18, colour: '#ffeb3b' },
    rect: { width: 140, height: 60 },
    whiteout: { width: 140, height: 24 }
  };

  let current = null; // { file, bytes, render, pages: [{ index, width, height, scale, overlay }] }
  let items = [];
  let selected = null;
  let activeTool = 'select';
  let pendingPlacement = null; // { type: 'signature' | 'image', image }
  let signature = null; // last signature, reused within this tab only
  let nextId = 1;
  const thumbnails = ui.createThumbnailQueue();

  // ---------------------------------------------------------------------------
  // Tool selection
  // ---------------------------------------------------------------------------

  function setTool(name) {
    activeTool = name;
    toolButtons.forEach((button) => {
      const pressed = button.dataset.annotateTool === name;
      button.setAttribute('aria-pressed', String(pressed));
      button.classList.toggle('active', pressed);
    });
    editor.dataset.activeTool = name;
    updateSummary();
  }

  function updateSummary() {
    const hints = {
      select: items.length ? `${items.length} item${items.length === 1 ? '' : 's'} added` : 'Pick a tool above, then click on a page',
      text: 'Click on a page to type',
      signature: 'Click on a page to place your signature',
      image: 'Click on a page to place the image',
      highlight: 'Drag across the text to highlight',
      rect: 'Drag to draw a box',
      whiteout: 'Drag over content to cover it'
    };
    summary.textContent = hints[activeTool] || '';
  }

  toolButtons.forEach((button) => button.addEventListener('click', () => {
    const name = button.dataset.annotateTool;
    pendingPlacement = null;
    if (name === 'signature') {
      openSignatureDialog();
      return;
    }
    if (name === 'image') {
      imageInput.click();
      return;
    }
    setTool(name);
  }));

  imageInput.addEventListener('change', async () => {
    const file = imageInput.files?.[0];
    imageInput.value = '';
    if (!file) return;
    try {
      pendingPlacement = { type: 'image', image: await loadImage(file) };
      setTool('image');
    } catch (error) {
      ui.showError(error);
    }
  });

  colourInput.addEventListener('input', () => {
    if (selected && (selected.type === 'text' || selected.type === 'rect' || selected.type === 'highlight')) {
      selected.color = colourInput.value;
      paintItem(selected);
    }
  });

  fontSizeInput.addEventListener('change', () => {
    if (selected?.type === 'text') {
      selected.fontSize = Number(fontSizeInput.value);
      paintItem(selected);
      measureText(selected);
    }
  });

  deleteButton.addEventListener('click', () => selected && removeItem(selected));

  document.addEventListener('keydown', (event) => {
    if (section.hidden || !selected || selected.editing) return;
    if (event.target.closest?.('input, select, textarea, [contenteditable="true"], [contenteditable="plaintext-only"], dialog')) return;
    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      removeItem(selected);
    } else if (event.key === 'Escape') {
      select(null);
    }
  });

  // ---------------------------------------------------------------------------
  // Images and signatures
  // ---------------------------------------------------------------------------

  /** Returns { bytes, type, url, width, height } with PNG for anything that is not JPG/PNG. */
  async function loadImage(file) {
    let bytes = await ui.readFile(file);
    let type = null;
    if (bytes[0] === 0xff && bytes[1] === 0xd8) type = 'jpg';
    if (bytes[0] === 0x89 && bytes[1] === 0x50) type = 'png';
    let bitmap;
    try {
      bitmap = await createImageBitmap(file);
    } catch {
      throw new core.PdfToolError('image-unsupported', `${file.name} could not be read as an image.`);
    }
    // Re-encode JPEGs with EXIF rotation and other formats so what is placed is what is seen.
    if (!type || (type === 'jpg' && core.jpegRotation(bytes) !== 0)) {
      const canvas = document.createElement('canvas');
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
      canvas.getContext('2d').drawImage(bitmap, 0, 0);
      bytes = await ui.canvasToBytes(canvas, 'image/png');
      type = 'png';
    }
    const image = {
      bytes,
      type,
      width: bitmap.width,
      height: bitmap.height,
      url: URL.createObjectURL(new Blob([bytes], { type: type === 'jpg' ? 'image/jpeg' : 'image/png' }))
    };
    bitmap.close?.();
    return image;
  }

  /** Crops a canvas to its non-transparent pixels and returns a PNG image record. */
  async function canvasToImage(canvas) {
    const context = canvas.getContext('2d');
    const { width, height } = canvas;
    const data = context.getImageData(0, 0, width, height).data;
    let minX = width;
    let minY = height;
    let maxX = -1;
    let maxY = -1;
    for (let y = 0; y < height; y += 1) {
      for (let x = 0; x < width; x += 1) {
        if (data[(y * width + x) * 4 + 3] > 8) {
          if (x < minX) minX = x;
          if (x > maxX) maxX = x;
          if (y < minY) minY = y;
          if (y > maxY) maxY = y;
        }
      }
    }
    if (maxX < 0) {
      return null;
    }
    const pad = 4;
    const cropX = Math.max(0, minX - pad);
    const cropY = Math.max(0, minY - pad);
    const cropW = Math.min(width, maxX + pad + 1) - cropX;
    const cropH = Math.min(height, maxY + pad + 1) - cropY;
    const cropped = document.createElement('canvas');
    cropped.width = cropW;
    cropped.height = cropH;
    cropped.getContext('2d').drawImage(canvas, cropX, cropY, cropW, cropH, 0, 0, cropW, cropH);
    const bytes = await ui.canvasToBytes(cropped, 'image/png');
    return {
      bytes,
      type: 'png',
      width: cropW,
      height: cropH,
      url: URL.createObjectURL(new Blob([bytes], { type: 'image/png' }))
    };
  }

  const pad = $('[data-signature-pad]', dialog);
  const padContext = pad.getContext('2d');
  const nameInput = $('[data-signature-name]', dialog);
  const signatureFile = $('[data-signature-file]', dialog);
  const signatureError = $('[data-signature-error]', dialog);
  const reuseButton = $('[data-signature-reuse]', dialog);
  let signatureTab = 'draw';
  let padDirty = false;

  function clearPad() {
    padContext.clearRect(0, 0, pad.width, pad.height);
    padDirty = false;
  }

  function showSignatureTab(name) {
    signatureTab = name;
    $$('[data-signature-tab]', dialog).forEach((tab) => tab.setAttribute('aria-selected', String(tab.dataset.signatureTab === name)));
    $$('[data-signature-panel]', dialog).forEach((panel) => { panel.hidden = panel.dataset.signaturePanel !== name; });
    if (name === 'type') nameInput.focus();
  }

  function openSignatureDialog() {
    signatureError.hidden = true;
    reuseButton.hidden = !signature;
    clearPad();
    showSignatureTab('draw');
    if (typeof dialog.showModal === 'function') {
      dialog.showModal();
    } else {
      dialog.setAttribute('open', '');
    }
  }

  $$('[data-signature-tab]', dialog).forEach((tab) => tab.addEventListener('click', () => showSignatureTab(tab.dataset.signatureTab)));
  $('[data-signature-clear]', dialog).addEventListener('click', clearPad);

  (function drawingPad() {
    let drawing = false;
    const point = (event) => {
      const rect = pad.getBoundingClientRect();
      return {
        x: ((event.clientX - rect.left) / rect.width) * pad.width,
        y: ((event.clientY - rect.top) / rect.height) * pad.height
      };
    };
    pad.addEventListener('pointerdown', (event) => {
      drawing = true;
      pad.setPointerCapture(event.pointerId);
      const { x, y } = point(event);
      padContext.strokeStyle = colourInput.value || '#1a3d8f';
      padContext.lineWidth = 3;
      padContext.lineCap = 'round';
      padContext.lineJoin = 'round';
      padContext.beginPath();
      padContext.moveTo(x, y);
      padContext.lineTo(x + 0.1, y + 0.1);
      padContext.stroke();
      padDirty = true;
    });
    pad.addEventListener('pointermove', (event) => {
      if (!drawing) return;
      const { x, y } = point(event);
      padContext.lineTo(x, y);
      padContext.stroke();
    });
    const stop = () => { drawing = false; };
    pad.addEventListener('pointerup', stop);
    pad.addEventListener('pointercancel', stop);
  })();

  async function signatureFromDialog() {
    if (signatureTab === 'draw') {
      return padDirty ? canvasToImage(pad) : null;
    }
    if (signatureTab === 'type') {
      const name = nameInput.value.trim();
      if (!name) return null;
      const canvas = document.createElement('canvas');
      canvas.width = 1200;
      canvas.height = 300;
      const context = canvas.getContext('2d');
      context.fillStyle = colourInput.value || '#1a3d8f';
      context.font = 'italic 120px "Segoe Script", "Brush Script MT", "Lucida Handwriting", cursive';
      context.textBaseline = 'middle';
      context.fillText(name, 20, 150, 1160);
      return canvasToImage(canvas);
    }
    const file = signatureFile.files?.[0];
    return file ? loadImage(file) : null;
  }

  function armSignature(image) {
    signature = image;
    pendingPlacement = { type: 'signature', image };
    dialog.close?.();
    dialog.removeAttribute('open');
    setTool('signature');
  }

  reuseButton.addEventListener('click', () => signature && armSignature(signature));

  $('[data-signature-use]', dialog).addEventListener('click', async () => {
    try {
      const image = await signatureFromDialog();
      if (!image) {
        signatureError.textContent = 'Draw, type or upload a signature first.';
        signatureError.hidden = false;
        return;
      }
      armSignature(image);
    } catch (error) {
      signatureError.textContent = error instanceof core.PdfToolError ? error.message : 'That signature could not be used.';
      signatureError.hidden = false;
    }
  });

  // ---------------------------------------------------------------------------
  // Items
  // ---------------------------------------------------------------------------

  function pageOf(item) {
    return current.pages[item.page];
  }

  function paintItem(item) {
    const page = pageOf(item);
    const element = item.element;
    const s = page.scale;
    element.style.left = `${item.left * s}px`;
    element.style.top = `${item.top * s}px`;
    if (item.type === 'text') {
      element.style.width = '';
      element.style.height = '';
      item.content.style.fontSize = `${item.fontSize * s}px`;
      item.content.style.color = item.color;
    } else {
      element.style.width = `${item.width * s}px`;
      element.style.height = `${item.height * s}px`;
    }
    if (item.type === 'highlight') {
      element.style.backgroundColor = item.color;
    } else if (item.type === 'rect') {
      element.style.borderColor = item.color;
    }
  }

  /** Records the rendered size and first baseline of a text item, in points. */
  function measureText(item) {
    const s = pageOf(item).scale;
    const marker = document.createElement('span');
    marker.className = 'pdft-baseline-probe';
    item.content.prepend(marker);
    item.baseline = marker.offsetTop / s;
    marker.remove();
    item.width = item.element.offsetWidth / s;
    item.height = item.element.offsetHeight / s;
  }

  function select(item) {
    if (selected && selected !== item) {
      selected.element.classList.remove('is-selected');
      stopEditing(selected);
    }
    selected = item;
    deleteButton.disabled = !item;
    if (item) {
      item.element.classList.add('is-selected');
      if (item.type === 'text') {
        fontSizeInput.value = String(item.fontSize);
      }
      // The picker holds the ink colour for text, boxes and signatures, so selecting a
      // highlight must not turn the next signature yellow.
      if (item.color && (item.type === 'text' || item.type === 'rect')) {
        colourInput.value = item.color;
      }
    }
  }

  function removeItem(item) {
    item.element.remove();
    items = items.filter((candidate) => candidate !== item);
    if (selected === item) select(null);
    updateSummary();
  }

  function startEditing(item) {
    if (item.type !== 'text' || item.editing) return;
    item.editing = true;
    item.content.contentEditable = 'plaintext-only';
    if (item.content.contentEditable !== 'plaintext-only') {
      item.content.contentEditable = 'true';
    }
    item.content.focus();
    const range = document.createRange();
    range.selectNodeContents(item.content);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
  }

  function stopEditing(item) {
    if (!item.editing) return;
    item.editing = false;
    item.content.contentEditable = 'false';
    item.text = item.content.innerText.replace(/\n$/, '');
    if (!item.text.trim()) {
      removeItem(item);
      return;
    }
    measureText(item);
  }

  function createItem(pageIndex, data) {
    const page = current.pages[pageIndex];
    const item = { id: nextId++, page: pageIndex, ...data };
    const element = document.createElement('div');
    element.className = `pdft-item pdft-item--${item.type}`;
    element.dataset.itemId = String(item.id);
    item.element = element;

    if (item.type === 'text') {
      const content = document.createElement('div');
      content.className = 'pdft-item__text';
      content.textContent = item.text;
      content.spellcheck = true;
      content.addEventListener('blur', () => stopEditing(item));
      content.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
          event.preventDefault();
          content.blur();
        }
      });
      element.append(content);
      item.content = content;
      element.addEventListener('dblclick', () => startEditing(item));
    } else if (item.type === 'image') {
      const img = document.createElement('img');
      img.src = item.url;
      img.alt = '';
      img.draggable = false;
      element.append(img);
    }

    if (item.type !== 'text') {
      const handle = document.createElement('span');
      handle.className = 'pdft-item__handle';
      handle.setAttribute('aria-hidden', 'true');
      element.append(handle);
    }

    page.overlay.append(element);
    items.push(item);
    paintItem(item);
    attachDrag(item);
    if (item.type === 'text') {
      measureText(item);
    }
    updateSummary();
    return item;
  }

  function clampToPage(item) {
    const page = pageOf(item);
    item.left = Math.min(Math.max(0, item.left), Math.max(0, page.width - Math.min(item.width, page.width)));
    item.top = Math.min(Math.max(0, item.top), Math.max(0, page.height - Math.min(item.height, page.height)));
  }

  function attachDrag(item) {
    const element = item.element;
    element.addEventListener('pointerdown', (event) => {
      // Keep every press on an item away from the page overlay, which would deselect it and
      // end editing; while editing, let the browser place the caret as usual.
      event.stopPropagation();
      if (item.editing) return;
      select(item);
      const resizing = event.target.classList.contains('pdft-item__handle');
      const s = pageOf(item).scale;
      const start = { x: event.clientX, y: event.clientY, left: item.left, top: item.top, width: item.width, height: item.height };
      const ratio = item.width / item.height;
      element.setPointerCapture(event.pointerId);

      const move = (moveEvent) => {
        const dx = (moveEvent.clientX - start.x) / s;
        const dy = (moveEvent.clientY - start.y) / s;
        if (resizing) {
          item.width = Math.max(MIN_SIZE, start.width + dx);
          item.height = item.type === 'image' ? item.width / ratio : Math.max(MIN_SIZE, start.height + dy);
        } else {
          item.left = start.left + dx;
          item.top = start.top + dy;
        }
        clampToPage(item);
        paintItem(item);
      };
      const end = () => {
        element.removeEventListener('pointermove', move);
        element.removeEventListener('pointerup', end);
        element.removeEventListener('pointercancel', end);
      };
      element.addEventListener('pointermove', move);
      element.addEventListener('pointerup', end);
      element.addEventListener('pointercancel', end);
    });
  }

  // ---------------------------------------------------------------------------
  // Placing items on a page
  // ---------------------------------------------------------------------------

  function onOverlayPointerDown(event, pageIndex) {
    const page = current.pages[pageIndex];
    const rect = page.overlay.getBoundingClientRect();
    const x = (event.clientX - rect.left) / page.scale;
    const y = (event.clientY - rect.top) / page.scale;

    if (activeTool === 'select') {
      select(null);
      return;
    }
    event.preventDefault();

    if (activeTool === 'text') {
      const item = createItem(pageIndex, {
        type: 'text',
        left: x,
        top: y,
        width: 1,
        height: 1,
        text: 'Type here',
        fontSize: Number(fontSizeInput.value) || 14,
        color: colourInput.value
      });
      select(item);
      setTool('select');
      // Defer so the pointer-up does not steal focus from the new text box.
      setTimeout(() => startEditing(item), 0);
      return;
    }

    if ((activeTool === 'signature' || activeTool === 'image') && pendingPlacement) {
      const image = pendingPlacement.image;
      const targetWidth = activeTool === 'signature' ? 160 : Math.min(240, page.width * 0.5);
      const width = Math.min(targetWidth, page.width);
      const height = width * (image.height / image.width);
      const item = createItem(pageIndex, {
        type: 'image',
        left: x - width / 2,
        top: y - height / 2,
        width,
        height,
        imageBytes: image.bytes,
        imageType: image.type,
        url: image.url
      });
      clampToPage(item);
      paintItem(item);
      select(item);
      if (activeTool === 'image') pendingPlacement = null;
      setTool('select');
      return;
    }

    if (SHAPE_DEFAULTS[activeTool]) {
      drawShape(event, pageIndex, x, y);
    }
  }

  function drawShape(event, pageIndex, x, y) {
    const type = activeTool;
    const defaults = SHAPE_DEFAULTS[type];
    const page = current.pages[pageIndex];
    const item = createItem(pageIndex, {
      type,
      left: x,
      top: y,
      width: MIN_SIZE,
      height: MIN_SIZE,
      color: type === 'highlight' ? (defaults.colour) : colourInput.value
    });
    select(item);
    const overlay = page.overlay;
    overlay.setPointerCapture(event.pointerId);
    let moved = false;

    const move = (moveEvent) => {
      const rect = overlay.getBoundingClientRect();
      const px = (moveEvent.clientX - rect.left) / page.scale;
      const py = (moveEvent.clientY - rect.top) / page.scale;
      item.left = Math.min(x, px);
      item.top = Math.min(y, py);
      item.width = Math.max(MIN_SIZE, Math.abs(px - x));
      item.height = Math.max(MIN_SIZE, Math.abs(py - y));
      moved = moved || item.width > MIN_SIZE || item.height > MIN_SIZE;
      clampToPage(item);
      paintItem(item);
    };
    const end = () => {
      overlay.removeEventListener('pointermove', move);
      overlay.removeEventListener('pointerup', end);
      overlay.removeEventListener('pointercancel', end);
      if (!moved) {
        item.width = defaults.width;
        item.height = defaults.height;
        clampToPage(item);
        paintItem(item);
      }
      if (type !== 'highlight') setTool('select');
    };
    overlay.addEventListener('pointermove', move);
    overlay.addEventListener('pointerup', end);
    overlay.addEventListener('pointercancel', end);
  }

  // ---------------------------------------------------------------------------
  // Loading and saving
  // ---------------------------------------------------------------------------

  async function buildPages() {
    const available = Math.min(MAX_PAGE_WIDTH, Math.max(280, editor.clientWidth - 32));
    const pages = [];
    for (let index = 0; index < current.render.numPages; index += 1) {
      const pdfPage = await current.render.getPage(index + 1);
      const viewport = pdfPage.getViewport({ scale: 1 });
      // pdf.js multiplies the viewport by /UserUnit; items are stored in PDF user units,
      // which is what pdf-lib draws in, so divide it back out.
      const userUnit = pdfPage.userUnit || 1;
      const width = viewport.width / userUnit;
      const height = viewport.height / userUnit;
      const scale = available / width;

      const sheet = document.createElement('div');
      sheet.className = 'pdft-sheet';
      sheet.style.width = `${width * scale}px`;
      sheet.style.height = `${height * scale}px`;
      const canvas = document.createElement('canvas');
      canvas.setAttribute('aria-hidden', 'true');
      const overlay = document.createElement('div');
      overlay.className = 'pdft-overlay';
      overlay.addEventListener('pointerdown', (event) => onOverlayPointerDown(event, index));
      const label = document.createElement('div');
      label.className = 'pdft-sheet__label';
      label.textContent = `Page ${index + 1} of ${current.render.numPages}`;
      sheet.append(canvas, overlay);
      editor.append(sheet, label);

      pages.push({ index, width, height, scale, overlay });
      thumbnails.observe(sheet, async () => {
        await ui.renderPage(pdfPage, canvas, { width: width * scale });
      });
    }
    return pages;
  }

  function reset() {
    thumbnails.reset();
    current?.render?.destroy();
    current = null;
    items = [];
    selected = null;
    pendingPlacement = null;
    deleteButton.disabled = true;
    editor.replaceChildren();
    setTool('select');
  }

  const tool = ui.createTool(section, {
    accept: ui.isPdf,
    async onFiles([file], state) {
      reset();
      await ui.withBusy('Opening PDF…', async () => {
        const bytes = await ui.readFile(file);
        await core.pageCount(PDFLib, bytes); // fail early on encrypted or damaged files
        const render = await ui.openForRender(bytes);
        current = { file, bytes, render, pages: [] };
        state.showWorkspace();
        current.pages = await buildPages();
        updateSummary();
      });
    },
    onRun() {
      if (!current) return;
      if (selected) stopEditing(selected);
      ui.withBusy('Saving PDF…', async () => {
        const annotations = items.map((item) => ({
          page: item.page,
          type: item.type,
          left: item.left,
          top: item.top,
          width: item.width,
          height: item.height,
          text: item.text,
          fontSize: item.fontSize,
          baseline: item.baseline,
          color: item.color,
          imageBytes: item.imageBytes,
          imageType: item.imageType
        }));
        const bytes = await core.applyAnnotations(PDFLib, current.bytes, annotations);
        await tool.finish(bytes, `${core.baseName(current.file.name)}-signed.pdf`, 'application/pdf');
      });
    },
    onReset: reset
  });

  setTool('select');
})();
