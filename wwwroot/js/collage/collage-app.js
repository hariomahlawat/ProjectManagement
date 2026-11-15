// PRISM Collage Studio — the editor (docs/ux/collage-studio.md).
//
// The collage is a plain model (PrismCollage, collage-core.js); Konva draws it and reports what
// the user moved, resized or turned, and the model is changed and recorded for Undo. Photos are
// read in this browser: a working copy, at most 2,000 pixels long, is drawn on the page and the
// original file is kept for saving. Nothing is sent to the server.
(() => {
  'use strict';

  const root = document.querySelector('[data-collage]');
  const core = window.PrismCollage;
  const Konva = window.Konva;
  if (!root || !core || !Konva) return;

  // Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
  const askConfirm = (options) => (window.PrismConfirm?.show
    ? window.PrismConfirm.show(options)
    : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));

  const $ = (selector) => root.querySelector(selector);
  const views = { start: $('[data-view="start"]'), editor: $('[data-view="editor"]') };
  const fileInput = $('[data-file-input]');
  const nameInput = $('[data-name]');
  const sizeSelect = $('[data-size]');
  const body = $('[data-body]');
  const stageBox = $('[data-stage]');
  const stageHost = $('[data-konva]');
  const toolbar = $('[data-toolbar]');
  const angleTag = $('[data-angle]');
  const emptyHint = $('[data-empty]');
  const tray = $('[data-tray]');
  const photoCount = $('[data-photo-count]');
  const props = $('[data-props]');
  const announcer = $('[data-announce]');
  const alertBox = $('[data-alert]');
  const busyBox = $('[data-busy]');
  const zoomValue = $('[data-zoom-value]');
  const coarsePointer = window.matchMedia?.('(pointer: coarse)').matches === true;

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  let collage = core.newCollage(core.DEFAULT_SIZE);
  let history = core.createHistory(collage);
  let selectedId = null;
  let zoom = 1;
  let scale = 1;
  let shiftHeld = false;
  let transforming = false;
  let sequence = 0;
  // The photos the collage uses, by id: { id, name, file, canvas, url, width, height, aspect }.
  const photos = new Map();

  const nextId = (prefix) => `${prefix}${Date.now().toString(36)}${(++sequence).toString(36)}`;
  const selectedItem = () => collage.items.find((item) => item.id === selectedId) || null;
  const pageHeight = () => core.pageHeight(collage.size);

  // ---------------------------------------------------------------------------
  // Messages
  // ---------------------------------------------------------------------------

  function announce(text) {
    announcer.textContent = '';
    window.setTimeout(() => { announcer.textContent = text; }, 30);
  }

  function showAlert(text) {
    $('[data-alert-text]').textContent = text;
    alertBox.hidden = !text;
  }

  function busy(text) {
    busyBox.hidden = !text;
    if (text) $('[data-busy-text]').textContent = text;
  }

  // ---------------------------------------------------------------------------
  // Views
  // ---------------------------------------------------------------------------

  function showView(name) {
    for (const [key, view] of Object.entries(views)) view.hidden = key !== name;
    document.body.classList.toggle('is-editing', name === 'editor');
    document.title = name === 'editor'
      ? `${nameInput.value.trim() || 'Untitled collage'} · Collage Studio | PRISM ERP`
      : 'Collage Studio | PRISM ERP';
    const heading = views[name].querySelector('h1');
    heading?.focus({ preventScroll: true });
    if (name === 'editor') window.requestAnimationFrame(layout);
  }

  function renderSizes() {
    const box = $('[data-sizes]');
    box.textContent = '';
    for (const [key, size] of Object.entries(core.SIZES)) {
      const ratio = size.width / size.height;
      const w = ratio >= 1 ? 34 : 34 * ratio;
      const h = ratio >= 1 ? 34 / ratio : 34;
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'clg-size-card';
      button.dataset.startSize = key;
      button.innerHTML = `<span class="clg-size-card__shape" style="width:${w.toFixed(1)}px;height:${h.toFixed(1)}px" aria-hidden="true"></span><span><b></b><small></small></span>`;
      button.querySelector('b').textContent = size.label;
      button.querySelector('small').textContent = size.note;
      box.appendChild(button);
    }
    sizeSelect.innerHTML = '';
    for (const [key, size] of Object.entries(core.SIZES)) {
      const option = document.createElement('option');
      option.value = key;
      option.textContent = size.label;
      sizeSelect.appendChild(option);
    }
  }

  function startCollage(sizeKey) {
    collage = core.newCollage(sizeKey);
    history = core.createHistory(collage);
    for (const photo of photos.values()) URL.revokeObjectURL(photo.url);
    photos.clear();
    selectedId = null;
    zoom = 1;
    nameInput.value = 'Untitled collage';
    sizeSelect.value = collage.size;
    showView('editor');
    syncAll();
  }

  async function goHome() {
    if (collage.items.length) {
      const leave = await askConfirm({
        title: 'Leave this collage?',
        message: 'Drafts and saving come in a later update, so this collage is lost when you leave it.',
        confirmText: 'Leave collage',
        cancelText: 'Keep working',
        tone: 'warning'
      });
      if (!leave) return;
    }
    collage = core.newCollage(core.DEFAULT_SIZE);
    history = core.createHistory(collage);
    selectedId = null;
    showView('start');
  }

  window.addEventListener('beforeunload', (event) => {
    if (views.editor.hidden || !collage.items.length) return;
    event.preventDefault();
    // Older browsers need a value here to ask; the text itself is not shown.
    event.returnValue = '';
  });

  // ---------------------------------------------------------------------------
  // The page (Konva)
  // ---------------------------------------------------------------------------

  const stage = new Konva.Stage({ container: stageHost, width: 10, height: 10 });
  const layer = new Konva.Layer();
  stage.add(layer);
  // `world` is the page: 1,000 units wide, scaled and centred on the work surface.
  const world = new Konva.Group();
  const pageRect = new Konva.Rect({
    x: 0, y: 0, width: core.PAGE_WIDTH, height: 10, fill: '#ffffff', name: 'page',
    shadowColor: 'rgba(16, 24, 40, 0.28)', shadowBlur: 28, shadowOffsetY: 6, shadowForStrokeEnabled: false
  });
  const itemsGroup = new Konva.Group();
  const guides = new Konva.Group({ listening: false });
  world.add(pageRect, itemsGroup, guides);
  layer.add(world);
  const nodes = new Map();

  const accent = () => getComputedStyle(document.body).getPropertyValue('--bs-primary').trim() || '#0d6efd';
  const NORMAL_SNAPS = [0, 45, 90, 135, 180, 225, 270, 315];
  const SHIFT_SNAPS = Array.from({ length: 24 }, (_, i) => i * 15);
  const transformer = new Konva.Transformer({
    rotateEnabled: true,
    keepRatio: true,
    shiftBehavior: 'none',
    flipEnabled: false,
    enabledAnchors: ['top-left', 'top-right', 'bottom-left', 'bottom-right'],
    rotationSnaps: NORMAL_SNAPS,
    rotationSnapTolerance: core.ANGLE_SNAP.within,
    rotateAnchorOffset: coarsePointer ? 40 : 30,
    anchorSize: coarsePointer ? 22 : 12,
    anchorCornerRadius: coarsePointer ? 11 : 6,
    anchorFill: '#ffffff',
    anchorStroke: accent(),
    borderStroke: accent(),
    borderStrokeWidth: 1.5,
    ignoreStroke: true,
    boundBoxFunc: (oldBox, newBox) => (Math.abs(newBox.width) < core.ITEM_LIMITS.minWidth * scale ? oldBox : newBox)
  });
  layer.add(transformer);

  function setShift(held) {
    if (held === shiftHeld) return;
    shiftHeld = held;
    transformer.rotationSnaps(held ? SHIFT_SNAPS : NORMAL_SNAPS);
    transformer.rotationSnapTolerance(held ? core.ANGLE_SNAP.stepWithShift / 2 : core.ANGLE_SNAP.within);
  }

  function layout() {
    if (views.editor.hidden) return;
    const height = pageHeight();
    const narrow = stageBox.clientWidth < 520;
    const pad = narrow ? 16 : 48;
    const fit = Math.min((stageBox.clientWidth - 2 * pad) / core.PAGE_WIDTH, (stageBox.clientHeight - 2 * pad) / height);
    scale = Math.max(0.05, fit * zoom);
    const pageW = core.PAGE_WIDTH * scale;
    const pageH = height * scale;
    const stageW = Math.max(stageBox.clientWidth, pageW + 2 * pad);
    const stageH = Math.max(stageBox.clientHeight, pageH + 2 * pad);
    stage.size({ width: stageW, height: stageH });
    stageHost.style.width = `${stageW}px`;
    stageHost.style.height = `${stageH}px`;
    world.position({ x: (stageW - pageW) / 2, y: (stageH - pageH) / 2 });
    world.scale({ x: scale, y: scale });
    pageRect.height(height);
    itemsGroup.clip({ x: 0, y: 0, width: core.PAGE_WIDTH, height });
    zoomValue.textContent = `${Math.round(zoom * 100)}%`;
    layer.batchDraw();
    placeToolbar();
  }

  new ResizeObserver(() => layout()).observe(stageBox);

  function setZoom(value) {
    zoom = Math.min(4, Math.max(0.25, value));
    layout();
  }

  function applyItem(node, item) {
    const height = core.itemHeight(item);
    node.setAttrs({
      x: item.x,
      y: item.y,
      width: item.width,
      height,
      offsetX: item.width / 2,
      offsetY: height / 2,
      rotation: item.angle,
      opacity: item.opacity / 100,
      scaleX: 1,
      scaleY: 1
    });
    const photo = photos.get(item.photo);
    if (photo && node.image() !== photo.canvas) node.image(photo.canvas);
  }

  function makeNode(item) {
    const node = new Konva.Image({ id: item.id, name: 'item', draggable: true, image: photos.get(item.photo)?.canvas });
    node.on('pointerdown', () => select(item.id));
    node.on('dragstart', () => { select(node.id()); hideToolbar(); });
    node.on('dragmove', (event) => snapWhileMoving(node, event.evt));
    node.on('dragend', () => {
      guides.destroyChildren();
      const target = collage.items.find((i) => i.id === node.id());
      if (!target) return;
      target.x = node.x();
      target.y = node.y();
      commit();
    });
    node.on('transformstart', () => { transforming = true; hideToolbar(); });
    node.on('transform', () => showAngle(node));
    node.on('transformend', () => {
      transforming = false;
      angleTag.hidden = true;
      const target = collage.items.find((i) => i.id === node.id());
      if (!target) return;
      target.width = core.clampWidth(node.width() * Math.abs(node.scaleX()));
      target.angle = core.normaliseAngle(node.rotation());
      target.x = node.x();
      target.y = node.y();
      commit();
    });
    return node;
  }

  function snapWhileMoving(node, event) {
    guides.destroyChildren();
    const item = collage.items.find((i) => i.id === node.id());
    if (!item || event?.altKey) { layer.batchDraw(); return; }
    const box = core.turnedHalfBox(item.width, core.itemHeight(item), item.angle);
    const others = collage.items.filter((i) => i.id !== item.id);
    const snapped = core.snapMove({ x: node.x(), y: node.y(), ...box }, collage.size, others, core.SNAP_PIXELS / scale);
    node.position({ x: snapped.x, y: snapped.y });
    for (const guide of snapped.guides) {
      const points = guide.axis === 'x' ? [guide.at, -40, guide.at, pageHeight() + 40] : [-40, guide.at, core.PAGE_WIDTH + 40, guide.at];
      guides.add(new Konva.Line({ points, stroke: '#ff3d8b', strokeWidth: 1, strokeScaleEnabled: false, dash: [6, 4] }));
    }
    layer.batchDraw();
  }

  /** Draws the model: adds, updates, orders and removes Konva nodes to match it. */
  function syncPage() {
    const seen = new Set();
    collage.items.forEach((item) => {
      let node = nodes.get(item.id);
      if (!node) {
        node = makeNode(item);
        nodes.set(item.id, node);
        itemsGroup.add(node);
      }
      applyItem(node, item);
      seen.add(item.id);
    });
    collage.items.forEach((item) => nodes.get(item.id).moveToTop());
    for (const [id, node] of nodes) {
      if (!seen.has(id)) { node.destroy(); nodes.delete(id); }
    }
    if (selectedId && !seen.has(selectedId)) selectedId = null;
    transformer.nodes(selectedId ? [nodes.get(selectedId)] : []);
    emptyHint.hidden = collage.items.length > 0;
    layer.batchDraw();
    placeToolbar();
  }

  function syncAll() {
    sizeSelect.value = collage.size;
    layout();
    syncPage();
    renderTray();
    renderProps();
    updateUndo();
  }

  // ---------------------------------------------------------------------------
  // Selection, toolbar and the angle while turning
  // ---------------------------------------------------------------------------

  function select(id) {
    if (selectedId === id) return;
    selectedId = id;
    transformer.nodes(id && nodes.get(id) ? [nodes.get(id)] : []);
    layer.batchDraw();
    placeToolbar();
    renderProps();
  }

  stage.on('pointerdown', (event) => {
    if (event.target === stage || event.target === pageRect) select(null);
  });

  function hideToolbar() {
    toolbar.hidden = true;
  }

  function placeToolbar() {
    const node = selectedId && nodes.get(selectedId);
    if (!node || transforming || node.isDragging()) { hideToolbar(); return; }
    const rect = node.getClientRect();
    toolbar.hidden = false;
    const width = toolbar.offsetWidth;
    const above = rect.y - toolbar.offsetHeight - (transformer.rotateAnchorOffset() + 18);
    const top = above >= stageBox.scrollTop + 6 ? above : rect.y + rect.height + 14;
    const left = Math.min(
      Math.max(rect.x + rect.width / 2, stageBox.scrollLeft + width / 2 + 8),
      stageBox.scrollLeft + stageBox.clientWidth - width / 2 - 8);
    toolbar.style.left = `${left}px`;
    toolbar.style.top = `${top}px`;
  }

  function showAngle(node) {
    const rect = node.getClientRect();
    angleTag.hidden = false;
    angleTag.textContent = `${Math.round(core.normaliseAngle(node.rotation()))}°`;
    angleTag.style.left = `${rect.x + rect.width / 2}px`;
    angleTag.style.top = `${Math.max(stageBox.scrollTop + 28, rect.y - 12)}px`;
  }

  stageBox.addEventListener('scroll', placeToolbar, { passive: true });

  // ---------------------------------------------------------------------------
  // Changes and Undo
  // ---------------------------------------------------------------------------

  function commit() {
    history.record(collage);
    syncPage();
    renderTray();
    renderProps();
    updateUndo();
  }

  function updateUndo() {
    $('[data-action="undo"]').disabled = !history.canUndo();
    $('[data-action="redo"]').disabled = !history.canRedo();
  }

  function restore(next) {
    collage = next;
    sizeSelect.value = collage.size;
    layout();
    syncPage();
    renderTray();
    renderProps();
    updateUndo();
  }

  function changeSelected(change, record = true) {
    const item = selectedItem();
    if (!item) return;
    change(item);
    if (record) commit();
    else { syncPage(); }
  }

  function act(action) {
    const item = selectedItem();
    switch (action) {
      case 'home': goHome(); break;
      case 'choose': fileInput.click(); break;
      case 'undo': if (history.canUndo()) restore(history.undo()); break;
      case 'redo': if (history.canRedo()) restore(history.redo()); break;
      case 'zoom-in': setZoom(zoom * 1.25); break;
      case 'zoom-out': setZoom(zoom / 1.25); break;
      case 'zoom-fit': setZoom(1); break;
      case 'alert-close': showAlert(''); break;
      case 'duplicate':
        if (item) {
          const copy = core.duplicateItem(item, nextId('i'));
          collage.items.splice(collage.items.indexOf(item) + 1, 0, copy);
          selectedId = copy.id;
          commit();
          announce('Duplicated.');
        }
        break;
      case 'delete':
        if (item) {
          collage.items = collage.items.filter((i) => i !== item);
          selectedId = null;
          commit();
          announce('Deleted. Ctrl+Z brings it back.');
        }
        break;
      case 'forward': case 'backward': case 'front': case 'back':
        if (item) { collage.items = core.reorder(collage.items, item.id, action); commit(); }
        break;
      case 'rotate-left': changeSelected((i) => { i.angle = core.normaliseAngle(i.angle - 15); }); break;
      case 'rotate-right': changeSelected((i) => { i.angle = core.normaliseAngle(i.angle + 15); }); break;
      case 'straighten': changeSelected((i) => { i.angle = 0; }); break;
      default: break;
    }
  }

  root.addEventListener('click', (event) => {
    const start = event.target.closest('[data-start-size]');
    if (start) { startCollage(start.dataset.startSize); return; }
    const button = event.target.closest('[data-action]');
    if (!button || button.disabled) return;
    if (button.tagName === 'A') event.preventDefault();
    if (button.dataset.action === 'home' && views.editor.hidden) return;
    act(button.dataset.action);
  });

  sizeSelect.addEventListener('change', () => {
    collage = core.changeSize(collage, sizeSelect.value);
    layout();
    commit();
    announce(`The page is now ${core.SIZES[collage.size].label}.`);
  });

  nameInput.addEventListener('input', () => {
    document.title = `${nameInput.value.trim() || 'Untitled collage'} · Collage Studio | PRISM ERP`;
  });

  $('[data-panel="photos"]').addEventListener('click', (event) => {
    const open = body.classList.toggle('is-drawer-closed') === false;
    event.currentTarget.setAttribute('aria-pressed', String(open));
    window.requestAnimationFrame(layout);
  });

  // ---------------------------------------------------------------------------
  // Settings panel
  // ---------------------------------------------------------------------------

  const escapeHtml = (text) => String(text).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  function renderProps() {
    const item = selectedItem();
    if (!item) {
      props.innerHTML = `
        <h2 class="clg-panel-title">Page</h2>
        <label class="clg-field"><span class="clg-field__label">Size</span>
          <select class="form-select form-select-sm" data-page-size>${Object.entries(core.SIZES)
            .map(([key, size]) => `<option value="${key}"${key === collage.size ? ' selected' : ''}>${escapeHtml(size.label)} · ${escapeHtml(size.note)}</option>`).join('')}</select></label>
        <div class="clg-section">
          <p class="clg-section__title">How to</p>
          <ul class="clg-tips">
            <li>Click a photo to change it; drag it to move it.</li>
            <li>Drag a corner to resize. Drag the round handle above it to turn it; it clicks to straight and 45°, and Shift gives 15° steps.</li>
            <li>Pink lines show when a photo lines up with the page or another photo. Hold Alt to move freely.</li>
            <li>Ctrl+Z undoes, Delete removes, Ctrl+D duplicates, and the arrow keys nudge.</li>
          </ul>
        </div>`;
      return;
    }
    const photo = photos.get(item.photo);
    props.innerHTML = `
      <h2 class="clg-panel-title">Photo <small title="${escapeHtml(photo?.name || '')}">${escapeHtml(photo?.name || '')}</small></h2>
      <div class="clg-section">
        <p class="clg-section__title">Angle</p>
        <label class="clg-field"><span class="clg-field__label">Turn<output data-out="angle">${Math.round(item.angle)}°</output></span>
          <input type="range" min="-180" max="180" step="1" value="${Math.round(item.angle)}" data-prop="angle" aria-label="Angle in degrees"></label>
        <div class="clg-buttons">
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="rotate-left">−15°</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="straighten">Straight</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="rotate-right">+15°</button>
        </div>
      </div>
      <div class="clg-section">
        <p class="clg-section__title">Look</p>
        <label class="clg-field"><span class="clg-field__label">See-through<output data-out="opacity">${item.opacity}%</output></span>
          <input type="range" min="10" max="100" step="1" value="${item.opacity}" data-prop="opacity" aria-label="How solid the photo is, in percent"></label>
      </div>
      <div class="clg-section">
        <p class="clg-section__title">Order</p>
        <div class="clg-buttons clg-buttons--two">
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="forward">Bring forward</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="backward">Send backward</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="front">To the front</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="back">To the back</button>
        </div>
      </div>
      <div class="clg-section">
        <div class="clg-buttons clg-buttons--two">
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="duplicate"><i class="bi bi-copy" aria-hidden="true"></i> Duplicate</button>
          <button type="button" class="btn btn-outline-danger btn-sm" data-action="delete"><i class="bi bi-trash3" aria-hidden="true"></i> Delete</button>
        </div>
      </div>`;
  }

  props.addEventListener('input', (event) => {
    const prop = event.target.dataset.prop;
    if (!prop) return;
    const value = Number(event.target.value);
    changeSelected((item) => { item[prop] = prop === 'angle' ? core.normaliseAngle(value) : value; }, false);
    const out = props.querySelector(`[data-out="${prop}"]`);
    if (out) out.textContent = prop === 'angle' ? `${Math.round(value)}°` : `${value}%`;
  });

  props.addEventListener('change', (event) => {
    if (event.target.dataset.prop) { history.record(collage); updateUndo(); return; }
    if (event.target.matches('[data-page-size]')) {
      sizeSelect.value = event.target.value;
      sizeSelect.dispatchEvent(new Event('change'));
    }
  });

  // ---------------------------------------------------------------------------
  // Photos: choose, drop, paste, and the tray
  // ---------------------------------------------------------------------------

  async function readPhoto(entry) {
    const bitmap = await createImageBitmap(entry.file);
    try {
      const size = core.workingSize(bitmap.width, bitmap.height);
      const canvas = document.createElement('canvas');
      canvas.width = size.width;
      canvas.height = size.height;
      const context = canvas.getContext('2d');
      context.imageSmoothingQuality = 'high';
      context.drawImage(bitmap, 0, 0, size.width, size.height);
      return {
        id: nextId('p'),
        name: entry.name,
        file: entry.file,
        canvas,
        url: URL.createObjectURL(entry.file),
        width: bitmap.width,
        height: bitmap.height,
        aspect: bitmap.width / bitmap.height
      };
    } finally {
      bitmap.close?.();
    }
  }

  function addPhotoItem(photo, at) {
    const count = collage.items.filter((i) => i.kind === 'photo').length;
    const place = core.placeNewPhoto(collage.size, photo.aspect, count);
    if (at) { place.x = at.x; place.y = at.y; }
    const item = core.photoItem(nextId('i'), photo.id, photo.aspect, place);
    collage.items.push(item);
    selectedId = item.id;
  }

  async function addFiles(fileList, at) {
    const files = [...fileList];
    if (!files.length) return;
    showAlert('');
    busy(files.length === 1 ? 'Adding the photo…' : `Adding ${files.length} photos…`);
    try {
      const entries = await Promise.all(files.map(async (file) => ({
        file,
        name: file.name || 'Pasted photo',
        size: file.size,
        type: core.sniffImageType(new Uint8Array(await file.slice(0, 16).arrayBuffer()))
      })));
      const { accepted, refused } = core.checkPhotos(entries, photos.size);
      const added = [];
      // One at a time, so a pile of large phone photos is never all decoded at once.
      for (const entry of accepted) {
        try {
          const photo = await readPhoto(entry);
          photos.set(photo.id, photo);
          added.push(photo);
        } catch {
          refused.push({ name: entry.name, reason: 'unreadable' });
        }
      }
      added.forEach((photo, index) => addPhotoItem(photo, index === 0 ? at : null));
      if (added.length) commit();
      showAlert(core.refusedMessage(refused));
      if (added.length) announce(added.length === 1 ? 'Photo added.' : `${added.length} photos added.`);
    } finally {
      busy('');
    }
  }

  fileInput.addEventListener('change', () => {
    const files = [...fileInput.files];
    fileInput.value = '';
    addFiles(files);
  });

  function renderTray() {
    const used = new Map();
    for (const item of collage.items) if (item.kind === 'photo') used.set(item.photo, (used.get(item.photo) || 0) + 1);
    tray.textContent = '';
    for (const photo of photos.values()) {
      const button = document.createElement('button');
      button.type = 'button';
      button.draggable = true;
      button.dataset.trayPhoto = photo.id;
      button.title = `Add “${photo.name}” to the page`;
      button.setAttribute('aria-label', `Add ${photo.name} to the page`);
      const img = document.createElement('img');
      img.src = photo.url;
      img.alt = '';
      button.appendChild(img);
      const times = used.get(photo.id) || 0;
      if (times) {
        const badge = document.createElement('span');
        badge.className = 'clg-tray__used';
        badge.textContent = times > 1 ? `${times}×` : 'Used';
        button.appendChild(badge);
      }
      tray.appendChild(button);
    }
    photoCount.textContent = photos.size ? `${photos.size} of ${core.PHOTO_LIMITS.maxPhotos} photos` : '';
  }

  tray.addEventListener('click', (event) => {
    const button = event.target.closest('[data-tray-photo]');
    const photo = button && photos.get(button.dataset.trayPhoto);
    if (!photo) return;
    addPhotoItem(photo);
    commit();
    announce('Photo added.');
  });

  tray.addEventListener('dragstart', (event) => {
    const button = event.target.closest('[data-tray-photo]');
    if (button) event.dataTransfer.setData('text/x-collage-photo', button.dataset.trayPhoto);
  });

  function pagePoint(event) {
    stage.setPointersPositions(event);
    const point = world.getRelativePointerPosition();
    if (!point) return null;
    const height = pageHeight();
    if (point.x < 0 || point.y < 0 || point.x > core.PAGE_WIDTH || point.y > height) return null;
    return point;
  }

  stageBox.addEventListener('dragover', (event) => {
    const types = [...event.dataTransfer.types];
    if (!types.includes('Files') && !types.includes('text/x-collage-photo')) return;
    event.preventDefault();
    stageBox.classList.add('is-dropping');
  });
  stageBox.addEventListener('dragleave', (event) => {
    if (!stageBox.contains(event.relatedTarget)) stageBox.classList.remove('is-dropping');
  });
  stageBox.addEventListener('drop', (event) => {
    stageBox.classList.remove('is-dropping');
    const fromTray = event.dataTransfer.getData('text/x-collage-photo');
    if (!fromTray && !event.dataTransfer.files.length) return;
    event.preventDefault();
    const at = pagePoint(event);
    if (fromTray) {
      const photo = photos.get(fromTray);
      if (photo) { addPhotoItem(photo, at); commit(); announce('Photo added.'); }
      return;
    }
    addFiles(event.dataTransfer.files, at);
  });

  document.addEventListener('paste', (event) => {
    if (views.editor.hidden) return;
    if (event.target.closest?.('input, textarea, [contenteditable]')) return;
    const files = [...(event.clipboardData?.files || [])].filter((file) => file.type.startsWith('image/'));
    if (!files.length) {
      showAlert('There is no photo on the clipboard. Copy a photo or a screenshot first.');
      return;
    }
    event.preventDefault();
    addFiles(files);
  });

  // ---------------------------------------------------------------------------
  // Keyboard and the mouse wheel
  // ---------------------------------------------------------------------------

  let nudgeTimer = 0;
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Shift') setShift(true);
    if (views.editor.hidden || document.querySelector('dialog[open]')) return;
    const typing = event.target.closest?.('input, textarea, select, [contenteditable]');
    const key = event.key.toLowerCase();
    const mod = event.ctrlKey || event.metaKey;
    if (key === 'escape') {
      if (typing) event.target.blur();
      else select(null);
      return;
    }
    if (typing) return;
    if (mod && key === 'z' && !event.shiftKey) { event.preventDefault(); act('undo'); }
    else if (mod && (key === 'y' || (key === 'z' && event.shiftKey))) { event.preventDefault(); act('redo'); }
    else if (mod && key === 'd') { event.preventDefault(); act('duplicate'); }
    else if ((key === 'delete' || key === 'backspace') && selectedId) { event.preventDefault(); act('delete'); }
    else if (key === ']') act(event.shiftKey ? 'front' : 'forward');
    else if (key === '[') act(event.shiftKey ? 'back' : 'backward');
    else if (key.startsWith('arrow') && selectedId) {
      event.preventDefault();
      const step = event.shiftKey ? 10 : 1;
      changeSelected((item) => {
        if (key === 'arrowleft') item.x -= step;
        if (key === 'arrowright') item.x += step;
        if (key === 'arrowup') item.y -= step;
        if (key === 'arrowdown') item.y += step;
      }, false);
      window.clearTimeout(nudgeTimer);
      nudgeTimer = window.setTimeout(() => { history.record(collage); updateUndo(); }, 400);
    }
  });
  document.addEventListener('keyup', (event) => { if (event.key === 'Shift') setShift(false); });
  window.addEventListener('blur', () => setShift(false));

  stageBox.addEventListener('wheel', (event) => {
    if (!event.ctrlKey) return;
    event.preventDefault();
    setZoom(zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1));
  }, { passive: false });

  // ---------------------------------------------------------------------------
  // Start
  // ---------------------------------------------------------------------------

  renderSizes();
  updateUndo();
  showView('start');
})();
