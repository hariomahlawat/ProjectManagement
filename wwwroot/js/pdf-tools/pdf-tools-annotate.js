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
  // Pages this far outside the window are drawn ahead of scrolling; further than this they are
  // released again, so a long PDF does not keep a full-size picture of every page it has shown.
  const KEEP_MARGIN = '1500px 0px';
  const STEP = 2;
  const BIG_STEP = 10;
  const WHITEOUT_WARNING = 'White-out only covers what is under it. The covered text stays in the file and can still be selected, copied or searched, so do not use it to hide private information.';
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
  // Object URLs made for signatures and images; freed when the file is closed.
  const objectUrls = new Set();
  const whiteoutNote = $('[data-annotate-whiteout-note]', section);
  if (whiteoutNote) whiteoutNote.textContent = WHITEOUT_WARNING;

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
      whiteout: 'Drag over content to cover it with white'
    };
    summary.textContent = hints[activeTool] || '';
    if (whiteoutNote) {
      whiteoutNote.hidden = activeTool !== 'whiteout' && !items.some((item) => item.type === 'whiteout');
    }
    // A page can be reached with the Tab key while a tool waits to be placed on it.
    const placing = activeTool !== 'select';
    for (const page of current?.pages || []) {
      page.overlay.tabIndex = placing ? 0 : -1;
    }
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
      focusPageInView();
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

  // Delete, Backspace and Escape act on the selected item only while it has the focus. With the
  // focus on a toolbar button or anywhere else, they must not remove an item the user is not
  // looking at (there is no undo).
  document.addEventListener('keydown', (event) => {
    if (section.hidden || !selected || selected.editing) return;
    if (!selected.element.contains(event.target)) return;
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

  function makeUrl(blob) {
    const url = URL.createObjectURL(blob);
    objectUrls.add(url);
    return url;
  }

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
      url: makeUrl(new Blob([bytes], { type: type === 'jpg' ? 'image/jpeg' : 'image/png' }))
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
      url: makeUrl(new Blob([bytes], { type: 'image/png' }))
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

  function showSignatureTab(name, { focusTab = false } = {}) {
    signatureTab = name;
    // Tabs pattern: only the chosen tab is a Tab stop; the arrow keys move between tabs.
    $$('[data-signature-tab]', dialog).forEach((tab) => {
      const chosen = tab.dataset.signatureTab === name;
      tab.setAttribute('aria-selected', String(chosen));
      tab.tabIndex = chosen ? 0 : -1;
      if (chosen && focusTab) tab.focus();
    });
    $$('[data-signature-panel]', dialog).forEach((panel) => { panel.hidden = panel.dataset.signaturePanel !== name; });
    if (name === 'type' && !focusTab) nameInput.focus();
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
  $('[role="tablist"]', dialog).addEventListener('keydown', (event) => {
    const tabs = $$('[data-signature-tab]', dialog);
    const index = tabs.findIndex((tab) => tab.dataset.signatureTab === signatureTab);
    const next = {
      ArrowRight: (index + 1) % tabs.length,
      ArrowLeft: (index - 1 + tabs.length) % tabs.length,
      Home: 0,
      End: tabs.length - 1
    }[event.key];
    if (next === undefined) return;
    event.preventDefault();
    showSignatureTab(tabs[next].dataset.signatureTab, { focusTab: true });
  });
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
    focusPageInView();
  }

  /**
   * Puts the keyboard on the first page that is on the screen, where Enter places the waiting
   * signature or image, instead of leaving it on the toolbar several Tab presses away.
   */
  function focusPageInView() {
    const pages = current?.pages || [];
    const inView = pages.find((page) => {
      const box = page.overlay.getBoundingClientRect();
      return box.bottom > 0 && box.top < window.innerHeight;
    }) || pages[0];
    if (inView) ui.moveFocus(inView.overlay);
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
    const page = pageOf(item);
    // From the toolbar Delete button, which is disabled below, the focus would fall to the top
    // of the page; keep it on the page instead.
    const hadFocus = item.element.contains(document.activeElement) || document.activeElement === deleteButton;
    item.element.remove();
    items = items.filter((candidate) => candidate !== item);
    if (selected === item) select(null);
    updateSummary();
    // Keep the keyboard on the page rather than dropping it to the top of the document.
    if (hadFocus && page) {
      page.overlay.focus({ preventScroll: true });
    }
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
    // Text typed near the edge must not be saved running off the page.
    clampToPage(item);
    paintItem(item);
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
          // Ends editing only: the item stays selected, which the page-wide Escape would undo.
          event.stopPropagation();
          // Back to the item (to move it with the arrow keys), or to its page if it was left empty.
          const page = pageOf(item);
          content.blur();
          if (items.includes(item)) {
            item.element.focus({ preventScroll: true });
          } else {
            page.overlay.focus({ preventScroll: true });
          }
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

    element.tabIndex = 0;
    element.setAttribute('role', 'group');
    element.setAttribute('aria-roledescription', 'item');
    element.setAttribute('aria-label', `${ITEM_NAMES[item.signature ? 'signature' : item.type] || 'Item'} on page ${pageIndex + 1}`);
    element.setAttribute('aria-describedby', keyHelp.id);
    element.addEventListener('focus', () => {
      if (selected !== item) select(item);
    });
    element.addEventListener('keydown', (event) => onItemKey(event, item));

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

  const ITEM_NAMES = {
    text: 'Text',
    image: 'Image',
    signature: 'Signature',
    highlight: 'Highlight',
    rect: 'Box',
    whiteout: 'White-out'
  };
  const keyHelp = document.createElement('p');
  keyHelp.id = 'pdftAnnotateKeys';
  keyHelp.className = 'visually-hidden';
  keyHelp.textContent = 'Arrow keys move it, with Shift in bigger steps. Alt and the arrow keys change its size. Enter edits text. Delete removes it.';
  editor.before(keyHelp);

  /** Moves or resizes the item that has the keyboard focus. */
  function onItemKey(event, item) {
    if (item.editing || event.target !== item.element) return;
    const moves = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    const move = moves[event.key];
    if (event.key === 'Enter' && item.type === 'text') {
      event.preventDefault();
      startEditing(item);
      return;
    }
    if (!move) return;
    event.preventDefault();
    const step = event.shiftKey ? BIG_STEP : STEP;
    if (event.altKey && item.type !== 'text') {
      const ratio = item.width / item.height;
      item.width = Math.max(MIN_SIZE, item.width + move[0] * step);
      item.height = item.type === 'image'
        ? item.width / ratio
        : Math.max(MIN_SIZE, item.height + move[1] * step);
      if (item.type === 'image' && move[1]) {
        item.height = Math.max(MIN_SIZE, item.height + move[1] * step);
        item.width = item.height * ratio;
      }
    } else {
      item.left += move[0] * step;
      item.top += move[1] * step;
    }
    clampToPage(item);
    paintItem(item);
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
      if (item.editing || !isPrimaryPress(event)) return;
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

  /** The main button or the first finger: right-click and a second finger do not place or move. */
  function isPrimaryPress(event) {
    return event.isPrimary !== false && (event.button === 0 || event.button === undefined);
  }

  function onOverlayPointerDown(event, pageIndex) {
    if (!isPrimaryPress(event)) return;
    const page = current.pages[pageIndex];
    const rect = page.overlay.getBoundingClientRect();
    const x = (event.clientX - rect.left) / page.scale;
    const y = (event.clientY - rect.top) / page.scale;

    if (activeTool === 'select') {
      select(null);
      return;
    }
    event.preventDefault();
    placeAt(pageIndex, x, y, event);
  }

  /**
   * The keyboard's way to place: Enter or Space on a page puts the waiting tool in the middle of
   * the part of the page that is on the screen. The item then has the focus and moves with the
   * arrow keys.
   */
  function onOverlayKey(event, pageIndex) {
    if (event.target !== current.pages[pageIndex].overlay || activeTool === 'select') return;
    if (event.key !== 'Enter' && event.key !== ' ') return;
    event.preventDefault();
    const page = current.pages[pageIndex];
    const box = page.overlay.getBoundingClientRect();
    const top = Math.max(box.top, 0);
    const bottom = Math.min(box.bottom, window.innerHeight);
    const x = page.width / 2;
    const y = bottom > top ? ((top + bottom) / 2 - box.top) / page.scale : page.height / 2;
    placeAt(pageIndex, x, y, null);
  }

  function placeAt(pageIndex, x, y, event) {
    const page = current.pages[pageIndex];
    const fromKeyboard = !event;

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
        url: image.url,
        signature: activeTool === 'signature'
      });
      clampToPage(item);
      paintItem(item);
      select(item);
      if (activeTool === 'image') pendingPlacement = null;
      setTool('select');
      if (fromKeyboard) item.element.focus();
      return;
    }

    if (SHAPE_DEFAULTS[activeTool]) {
      if (fromKeyboard) {
        const type = activeTool;
        const defaults = SHAPE_DEFAULTS[type];
        const item = createItem(pageIndex, {
          type,
          left: x - defaults.width / 2,
          top: y - defaults.height / 2,
          width: defaults.width,
          height: defaults.height,
          color: type === 'highlight' ? defaults.colour : colourInput.value
        });
        clampToPage(item);
        paintItem(item);
        select(item);
        if (type !== 'highlight') setTool('select');
        item.element.focus();
        updateSummary();
        return;
      }
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
      updateSummary();
    };
    overlay.addEventListener('pointermove', move);
    overlay.addEventListener('pointerup', end);
    overlay.addEventListener('pointercancel', end);
  }

  // ---------------------------------------------------------------------------
  // Loading and saving
  // ---------------------------------------------------------------------------

  // ---------------------------------------------------------------------------
  // Drawing pages
  //
  // A page is drawn when it comes near the window and its picture is released when it moves
  // far away; it is drawn again if it comes back. One page is drawn at a time.
  // ---------------------------------------------------------------------------

  let observer = null;
  const queue = [];
  let drawing = false;

  function want(page) {
    page.near = true;
    if (page.drawn || page.queued) return;
    page.queued = true;
    queue.push(page);
    pump();
  }

  function release(page) {
    page.near = false;
    if (!page.drawn) return;
    page.drawn = false;
    // Zero size frees the picture; the sheet keeps its size, so nothing on the page moves.
    page.canvas.width = 0;
    page.canvas.height = 0;
    page.pdfPage.cleanup();
  }

  async function pump() {
    if (drawing) return;
    drawing = true;
    const generation = current;
    try {
      while (queue.length) {
        const page = queue.shift();
        page.queued = false;
        if (!page.near || page.drawn || current !== generation) continue;
        try {
          const scale = page.scale;
          await ui.renderPage(page.pdfPage, page.canvas, { width: page.width * scale });
          page.drawn = true;
          page.sheet.classList.remove('is-failed');
          // The pages were refitted while it was drawn: draw it again at the new size.
          if (page.scale !== scale) {
            page.drawn = false;
            if (page.near) want(page);
            continue;
          }
          // It may have scrolled away while it was being drawn.
          if (!page.near) release(page);
        } catch (error) {
          console.warn('PDF Tools: page could not be drawn', error);
          // Say so on the sheet rather than showing a blank page that looks empty.
          page.sheet.classList.add('is-failed');
        }
      }
    } finally {
      drawing = false;
    }
  }

  function availableWidth() {
    return Math.min(MAX_PAGE_WIDTH, Math.max(280, editor.clientWidth - 32));
  }

  /** Fits the pages to the editor again after the window is resized or a phone is turned. */
  function refit() {
    if (!current?.pages.length) return;
    const available = availableWidth();
    if (Math.abs(available - current.fittedWidth) < 1) return;
    current.fittedWidth = available;
    for (const page of current.pages) {
      page.scale = available / page.width;
      page.sheet.style.width = `${page.width * page.scale}px`;
      page.sheet.style.height = `${page.height * page.scale}px`;
      // renderPage sizes the canvas in pixels; let it fill the new sheet until it is redrawn,
      // so the picture and the items on it stay lined up.
      page.canvas.style.width = '100%';
      page.canvas.style.height = '100%';
      if (page.drawn) {
        // Draw it again at the new size; the old picture stays, stretched, until then.
        page.drawn = false;
        if (page.near) want(page);
      }
    }
    items.forEach(paintItem);
  }

  let refitTimer = 0;
  if ('ResizeObserver' in window) {
    new ResizeObserver(() => {
      clearTimeout(refitTimer);
      refitTimer = setTimeout(refit, 150);
    }).observe(editor);
  }

  async function buildPages() {
    const available = availableWidth();
    current.fittedWidth = available;
    const pages = [];
    observer = 'IntersectionObserver' in window
      ? new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          const page = entry.target.__page;
          if (entry.isIntersecting) want(page);
          else release(page);
        });
      }, { rootMargin: KEEP_MARGIN })
      : null;
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
      sheet.dataset.failedText = 'This page could not be shown.';
      sheet.style.width = `${width * scale}px`;
      sheet.style.height = `${height * scale}px`;
      const canvas = document.createElement('canvas');
      canvas.setAttribute('aria-hidden', 'true');
      // No picture until the page is drawn (a new canvas would otherwise hold a 300 x 150 one).
      canvas.width = 0;
      canvas.height = 0;
      canvas.style.width = '100%';
      canvas.style.height = '100%';
      const overlay = document.createElement('div');
      overlay.className = 'pdft-overlay';
      overlay.tabIndex = -1;
      overlay.setAttribute('role', 'group');
      overlay.setAttribute('aria-label', `Page ${index + 1} of ${current.render.numPages}. Press Enter to place the chosen tool here.`);
      overlay.addEventListener('pointerdown', (event) => onOverlayPointerDown(event, index));
      overlay.addEventListener('keydown', (event) => onOverlayKey(event, index));
      const label = document.createElement('div');
      label.className = 'pdft-sheet__label';
      label.textContent = `Page ${index + 1} of ${current.render.numPages}`;
      sheet.append(canvas, overlay);
      editor.append(sheet, label);

      const page = { index, width, height, scale, sheet, overlay, canvas, pdfPage, drawn: false, queued: false, near: false };
      pages.push(page);
      if (observer) {
        sheet.__page = page;
        observer.observe(sheet);
      } else {
        want(page);
      }
    }
    return pages;
  }

  function reset() {
    observer?.disconnect();
    observer = null;
    queue.length = 0;
    current?.render?.destroy();
    current = null;
    items = [];
    selected = null;
    pendingPlacement = null;
    deleteButton.disabled = true;
    editor.replaceChildren();
    for (const url of objectUrls) {
      if (url === signature?.url) continue; // the signature is kept for the next file
      URL.revokeObjectURL(url);
      objectUrls.delete(url);
    }
    setTool('select');
  }

  const tool = ui.createTool(section, {
    accept: ui.isPdf,
    async onFiles([file], state) {
      if (current && items.length) {
        const go = await ui.askConfirm({
          tone: 'warning',
          icon: 'bi-file-earmark-pdf',
          title: 'Open another PDF?',
          message: `The ${items.length} item${items.length === 1 ? '' : 's'} you added to ${current.file.name} will be lost.`,
          confirmText: 'Open another PDF',
          cancelText: 'Keep editing'
        });
        if (!go) return;
      }
      reset();
      await ui.withBusy('Opening PDF…', async () => {
        const bytes = await ui.readFile(file);
        const pageCount = await core.pageCount(PDFLib, bytes); // fail early on encrypted or damaged files
        const render = await ui.openForRender(bytes);
        current = { file, bytes, render, pages: [], fittedWidth: 0 };
        ui.fileSummary(section, file, pageCount);
        state.showWorkspace();
        current.pages = await buildPages();
        updateSummary();
      });
    },
    async onRun() {
      if (!current) return;
      if (selected) stopEditing(selected);
      if (items.some((item) => item.type === 'whiteout')) {
        const go = await ui.askConfirm({
          tone: 'warning',
          icon: 'bi-eye',
          title: 'The covered text is still in the file',
          message: `${WHITEOUT_WARNING} Save anyway?`,
          confirmText: 'Save anyway',
          cancelText: 'Go back'
        });
        if (!go) return;
      }
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
