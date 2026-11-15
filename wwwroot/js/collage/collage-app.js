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
  const adjustHint = $('[data-adjust-hint]');
  const emptyHint = $('[data-empty]');
  const tray = $('[data-tray]');
  const photoCount = $('[data-photo-count]');
  const replaceBanner = $('[data-replace-banner]');
  const layoutGrid = $('[data-layout-grid]');
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
  // The photo whose picture is being moved inside its frame, or null.
  let adjustId = null;
  // The frame the next chosen photo goes into (Replace, or Add photo on an empty frame), or null.
  let targetId = null;
  let panel = 'photos';
  let zoom = 1;
  let scale = 1;
  let shiftHeld = false;
  let transforming = false;
  let sequence = 0;
  // Counts the collages opened in this tab; photo imports belong to the one they started in.
  let collageSession = 0;
  // A keyboard nudge is recorded for Undo once the keys stop; until then it is pending here.
  let nudgeTimer = 0;
  let nudgePending = false;
  // The photos the collage uses, by id: { id, name, file, canvas, thumbnail, width, height, aspect }.
  const photos = new Map();

  const nextId = (prefix) => `${prefix}${Date.now().toString(36)}${(++sequence).toString(36)}`;
  const newItemId = () => nextId('i');
  const itemById = (id) => collage.items.find((item) => item.id === id) || null;
  const selectedItem = () => itemById(selectedId);
  const pageHeight = () => core.pageHeight(collage.size);
  const hasPhotos = () => collage.items.some((item) => item.kind === 'photo');
  const firstSlot = () => collage.items.find((item) => item.kind === 'slot') || null;
  const isFrame = (item) => item && (item.kind === 'photo' || item.kind === 'slot');

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

  /** A small drawing of a layout's frames, for its button. */
  function layoutPicture(key) {
    const cells = core.LAYOUTS[key].cells
      .map(([x, y, w, h]) => `<rect x="${(4 + x * 9.33 + 1.5).toFixed(1)}" y="${(4 + y * 6.33 + 1.5).toFixed(1)}" width="${(w * 9.33 - 3).toFixed(1)}" height="${(h * 6.33 - 3).toFixed(1)}" rx="2.5"/>`)
      .join('');
    return `<svg viewBox="0 0 120 84" aria-hidden="true" focusable="false"><rect class="clg-layout-pic__page" width="120" height="84" rx="6"/>${cells}</svg>`;
  }

  function layoutButton(key, dataName) {
    const layout = core.LAYOUTS[key];
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'clg-layout-card';
    button.dataset[dataName] = key;
    button.innerHTML = `${layoutPicture(key)}<span></span>`;
    button.querySelector('span').textContent = layout.label;
    button.setAttribute('aria-label', `${layout.label}, ${layout.cells.length} photos`);
    return button;
  }

  function renderStart() {
    const sizes = $('[data-sizes]');
    sizes.textContent = '';
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
      sizes.appendChild(button);
    }
    const layouts = $('[data-start-layouts]');
    layouts.textContent = '';
    for (const key of Object.keys(core.LAYOUTS)) layouts.appendChild(layoutButton(key, 'startLayout'));
    layoutGrid.textContent = '';
    for (const key of Object.keys(core.LAYOUTS)) layoutGrid.appendChild(layoutButton(key, 'layout'));
    sizeSelect.innerHTML = '';
    for (const [key, size] of Object.entries(core.SIZES)) {
      const option = document.createElement('option');
      option.value = key;
      option.textContent = size.label;
      sizeSelect.appendChild(option);
    }
  }

  function startCollage(sizeKey, layoutKey) {
    collageSession += 1;
    window.clearTimeout(nudgeTimer);
    nudgePending = false;
    collage = core.newCollage(sizeKey);
    if (layoutKey) {
      collage = core.applyLayout(collage, layoutKey, core.LAYOUT_LIMITS.defaultGap, 0, [], newItemId).collage;
    }
    history = core.createHistory(collage);
    photos.clear();
    selectedId = null;
    adjustId = null;
    targetId = null;
    zoom = 1;
    nameInput.value = 'Untitled collage';
    sizeSelect.value = collage.size;
    showView('editor');
    setPanel('photos');
    syncAll();
  }

  async function goHome() {
    if (hasPhotos()) {
      const leave = await askConfirm({
        title: 'Leave this collage?',
        message: 'Drafts and saving come in a later update, so this collage is lost when you leave it.',
        confirmText: 'Leave collage',
        cancelText: 'Keep working',
        tone: 'warning'
      });
      if (!leave) return;
    }
    collageSession += 1;
    collage = core.newCollage(core.DEFAULT_SIZE);
    history = core.createHistory(collage);
    selectedId = null;
    adjustId = null;
    targetId = null;
    showView('start');
  }

  window.addEventListener('beforeunload', (event) => {
    if (views.editor.hidden || !hasPhotos()) return;
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
  const ADJUST_COLOUR = '#f2a200';
  const NORMAL_SNAPS = [0, 45, 90, 135, 180, 225, 270, 315];
  const SHIFT_SNAPS = Array.from({ length: 24 }, (_, i) => i * 15);
  // Corners keep the shape; the middle of each side changes the frame's shape.
  const ALL_ANCHORS = ['top-left', 'top-center', 'top-right', 'middle-right', 'bottom-right', 'bottom-center', 'bottom-left', 'middle-left'];
  const transformer = new Konva.Transformer({
    rotateEnabled: true,
    keepRatio: true,
    shiftBehavior: 'none',
    flipEnabled: false,
    enabledAnchors: ALL_ANCHORS,
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
    boundBoxFunc: (oldBox, newBox) => (Math.abs(newBox.width) < core.ITEM_LIMITS.minWidth * scale
      || Math.abs(newBox.height) < core.ITEM_LIMITS.minWidth * scale ? oldBox : newBox)
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

  /** The Konva node for an empty frame: a dashed box saying a photo can go here. */
  function makeSlotNode() {
    const group = new Konva.Group({ name: 'item', draggable: true });
    group.add(new Konva.Rect({ name: 'slot-box', fill: '#eef1f6', stroke: '#8f9aab', strokeWidth: 1.5, dash: [8, 6], strokeScaleEnabled: false }));
    group.add(new Konva.Text({ name: 'slot-label', text: '＋\nAdd photo', align: 'center', verticalAlign: 'middle', fill: '#5d6878', fontFamily: 'Mulish, "Segoe UI", sans-serif', fontStyle: '700', listening: false }));
    return group;
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
    if (item.kind === 'slot') {
      node.findOne('.slot-box').setAttrs({ width: item.width, height, cornerRadius: item.radius || 0 });
      const size = Math.max(10, Math.min(item.width, height) * 0.12);
      node.findOne('.slot-label').setAttrs({ width: item.width, height, fontSize: size, lineHeight: 1.25 });
      return;
    }
    const photo = photos.get(item.photo);
    if (!photo) return;
    if (node.image() !== photo.canvas) node.image(photo.canvas);
    const region = core.cropRegion(photo.aspect, item.aspect, item.crop);
    node.crop({
      x: region.x * photo.canvas.width,
      y: region.y * photo.canvas.height,
      width: region.width * photo.canvas.width,
      height: region.height * photo.canvas.height
    });
    node.cornerRadius(item.radius || 0);
  }

  function makeNode(item) {
    const node = item.kind === 'slot'
      ? makeSlotNode()
      : new Konva.Image({ name: 'item', draggable: true, image: photos.get(item.photo)?.canvas });
    node.id(item.id);
    node.setAttr('kind', item.kind);
    node.on('pointerdown', (event) => {
      if (adjustId === node.id()) { startPan(node, event); return; }
      if (adjustId) stopAdjust();
      select(node.id());
    });
    node.on('dblclick dbltap', () => {
      const target = itemById(node.id());
      if (target?.kind === 'photo') startAdjust(target.id);
      else if (target?.kind === 'slot') chooseFor(target.id);
    });
    node.on('dragstart', () => { select(node.id()); hideToolbar(); });
    node.on('dragmove', (event) => snapWhileMoving(node, event.evt));
    node.on('dragend', () => {
      guides.destroyChildren();
      const target = itemById(node.id());
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
      const target = itemById(node.id());
      if (!target) return;
      const width = core.clampWidth(target.width * Math.abs(node.scaleX()));
      const height = core.itemHeight(target) * Math.abs(node.scaleY());
      target.width = width;
      target.aspect = width / Math.max(1, height);
      if (target.crop) target.crop = clampedCrop(target);
      target.angle = core.normaliseAngle(node.rotation());
      target.x = node.x();
      target.y = node.y();
      commit();
    });
    return node;
  }

  /** The item's crop, kept inside its photo for the frame's current shape. */
  function clampedCrop(item) {
    const photo = photos.get(item.photo);
    return photo ? core.clampCrop(photo.aspect, item.aspect, item.crop) : item.crop;
  }

  function snapWhileMoving(node, event) {
    guides.destroyChildren();
    const item = itemById(node.id());
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
    // Moving a photo inside its frame belongs to the selected photo only: whatever selected
    // another item (a click, a photo placed from Photos, a duplicate) ends it.
    if (adjustId && adjustId !== selectedId) {
      adjustId = null;
      pan = null;
      adjustHint.hidden = true;
    }
    const seen = new Set();
    collage.items.forEach((item) => {
      let node = nodes.get(item.id);
      // A filled empty frame becomes a photo with the same id: its node is made again.
      if (node && node.getAttr('kind') !== item.kind) { node.destroy(); node = null; }
      if (!node) {
        node = makeNode(item);
        nodes.set(item.id, node);
        itemsGroup.add(node);
      }
      applyItem(node, item);
      node.draggable(adjustId !== item.id);
      seen.add(item.id);
    });
    collage.items.forEach((item) => nodes.get(item.id).moveToTop());
    for (const [id, node] of nodes) {
      if (!seen.has(id)) { node.destroy(); nodes.delete(id); }
    }
    if (selectedId && !seen.has(selectedId)) selectedId = null;
    if (adjustId && !seen.has(adjustId)) stopAdjust(false);
    if (targetId && !seen.has(targetId)) setTarget(null);
    attachTransformer();
    emptyHint.hidden = collage.items.length > 0;
    layer.batchDraw();
    placeToolbar();
  }

  function attachTransformer() {
    const node = selectedId && nodes.get(selectedId);
    const adjusting = Boolean(adjustId && adjustId === selectedId);
    transformer.nodes(node ? [node] : []);
    transformer.enabledAnchors(adjusting ? [] : ALL_ANCHORS);
    transformer.rotateEnabled(!adjusting);
    transformer.borderStroke(adjusting ? ADJUST_COLOUR : accent());
    transformer.borderDash(adjusting ? [6, 4] : []);
  }

  function syncAll() {
    sizeSelect.value = collage.size;
    layout();
    syncPage();
    renderTray();
    renderLayoutPanel();
    renderProps();
    updateUndo();
  }

  // ---------------------------------------------------------------------------
  // Selection, toolbar and the angle while turning
  // ---------------------------------------------------------------------------

  function select(id) {
    if (selectedId === id) return;
    selectedId = id;
    if (adjustId && adjustId !== id) stopAdjust(false);
    attachTransformer();
    layer.batchDraw();
    placeToolbar();
    renderProps();
  }

  stage.on('pointerdown', (event) => {
    if (event.target === stage || event.target === pageRect) {
      if (adjustId) stopAdjust();
      select(null);
    }
  });

  function hideToolbar() {
    toolbar.hidden = true;
  }

  function placeToolbar() {
    const node = selectedId && nodes.get(selectedId);
    const item = selectedItem();
    if (!node || !item || transforming || node.isDragging()) { hideToolbar(); return; }
    const adjusting = adjustId === item.id;
    for (const part of toolbar.querySelectorAll('[data-for]')) {
      const forWhat = part.dataset.for;
      part.hidden = adjusting
        ? forWhat !== 'adjusting'
        : !(forWhat === item.kind || forWhat === 'item');
    }
    const rect = node.getClientRect();
    toolbar.hidden = false;
    const width = toolbar.offsetWidth;
    const above = rect.y - toolbar.offsetHeight - (adjusting ? 12 : transformer.rotateAnchorOffset() + 18);
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
  // Moving a photo inside its frame
  // ---------------------------------------------------------------------------

  let pan = null;

  function startAdjust(id) {
    const item = itemById(id);
    if (!item || item.kind !== 'photo') return;
    adjustId = id;
    selectedId = id;
    // Zoomed in a little to begin with, so there is something to move.
    if ((item.crop?.zoom || 1) < 1.2) {
      item.crop = clampedCrop({ ...item, crop: { ...item.crop, zoom: 1.2 } });
      history.record(collage);
      updateUndo();
    }
    adjustHint.hidden = false;
    syncPage();
    renderProps();
    announce('Drag to move the photo inside its frame. Press Esc when done.');
  }

  function stopAdjust(redraw = true) {
    if (!adjustId) return;
    adjustId = null;
    pan = null;
    adjustHint.hidden = true;
    if (redraw) { syncPage(); renderProps(); }
  }

  function startPan(node, event) {
    const item = itemById(node.id());
    const point = world.getRelativePointerPosition();
    if (!item || !point) return;
    event.cancelBubble = true;
    pan = { id: item.id, start: point, crop: { ...item.crop } };
  }

  stage.on('pointermove', (event) => {
    if (!pan) return;
    // A mouse whose button is no longer held was let go somewhere the page did not hear it.
    if (event.evt?.pointerType === 'mouse' && event.evt.buttons === 0) { endPan(); return; }
    const item = itemById(pan.id);
    const photo = item && photos.get(item.photo);
    const point = world.getRelativePointerPosition();
    if (!item || !photo || !point) return;
    // The drag measured along the frame's own sides, so a turned frame pans the way it looks.
    const rad = -item.angle * Math.PI / 180;
    const dx = point.x - pan.start.x;
    const dy = point.y - pan.start.y;
    const along = dx * Math.cos(rad) - dy * Math.sin(rad);
    const across = dx * Math.sin(rad) + dy * Math.cos(rad);
    item.crop = core.panCrop(photo.aspect, { ...item, crop: pan.crop }, along, across);
    applyItem(nodes.get(item.id), item);
    layer.batchDraw();
  });

  function endPan() {
    if (!pan) return;
    pan = null;
    commit();
  }

  // Released anywhere, on the page or off it: the drag inside the frame ends there.
  window.addEventListener('pointerup', endPan);
  window.addEventListener('pointercancel', endPan);
  window.addEventListener('blur', endPan);

  function zoomCrop(item, value) {
    item.crop = clampedCrop({ ...item, crop: { ...item.crop, zoom: value } });
  }

  // ---------------------------------------------------------------------------
  // Changes and Undo
  // ---------------------------------------------------------------------------

  function commit() {
    history.record(collage);
    syncPage();
    renderTray();
    renderLayoutPanel();
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
    renderLayoutPanel();
    renderProps();
    updateUndo();
  }

  function changeSelected(change, record = true) {
    const item = selectedItem();
    if (!item) return;
    change(item);
    if (record) commit();
    else syncPage();
  }

  /** Photos in the tray that are not on the page, in the order they were added. */
  const sparePhotos = () => {
    const used = new Set(collage.items.filter((item) => item.kind === 'photo').map((item) => item.photo));
    return [...photos.keys()].filter((id) => !used.has(id));
  };

  function applyLayoutKey(key) {
    const gap = collage.layout ? collage.layout.gap : core.LAYOUT_LIMITS.defaultGap;
    const radius = collage.layout ? collage.layout.radius : 0;
    const result = core.applyLayout(collage, key, gap, radius, sparePhotos(), newItemId);
    collage = result.collage;
    selectedId = null;
    stopAdjust(false);
    commit();
    const left = result.removed.length;
    announce(left
      ? `Layout applied. ${left} photo${left > 1 ? 's' : ''} did not fit and ${left > 1 ? 'are' : 'is'} still in Photos.`
      : 'Layout applied.');
    if (left) showAlert(`${left} photo${left > 1 ? 's' : ''} did not fit this layout. ${left > 1 ? 'They are' : 'It is'} still in Photos; Ctrl+Z brings the page back.`);
  }

  function act(action) {
    const item = selectedItem();
    switch (action) {
      case 'home': goHome(); break;
      case 'choose': fileInput.click(); break;
      case 'undo': flushNudge(); stopAdjust(false); if (history.canUndo()) restore(history.undo()); break;
      case 'redo': flushNudge(); stopAdjust(false); if (history.canRedo()) restore(history.redo()); break;
      case 'zoom-in': setZoom(zoom * 1.25); break;
      case 'zoom-out': setZoom(zoom / 1.25); break;
      case 'zoom-fit': setZoom(1); break;
      case 'alert-close': showAlert(''); break;
      case 'duplicate':
        if (item) {
          // A copy is one frame more than the layout has, so the page is no longer a strict
          // grid; otherwise the next change of space or page size would drop the copy.
          collage.layout = null;
          const copy = { ...core.duplicateItem(item, newItemId()), crop: item.crop ? { ...item.crop } : undefined };
          if (!copy.crop) delete copy.crop;
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
      case 'adjust': if (item?.kind === 'photo') startAdjust(item.id); break;
      case 'adjust-done': stopAdjust(); break;
      case 'replace': if (isFrame(item)) { setTarget(item.id); setPanel('photos'); } break;
      case 'fill-slot': if (item?.kind === 'slot') chooseFor(item.id); break;
      case 'replace-cancel': setTarget(null); break;
      case 'fit-photo':
        changeSelected((i) => {
          const photo = photos.get(i.photo);
          if (!photo) return;
          i.aspect = photo.aspect;
          i.crop = { ...core.FULL_CROP };
        });
        break;
      case 'casual':
        if (!collage.items.some(isFrame)) break;
        collage = core.makeCasual(collage);
        stopAdjust(false);
        commit();
        announce('Photos tilted. Ctrl+Z puts them back.');
        break;
      case 'straighten-all':
        collage = core.straightenAll(collage);
        commit();
        announce('All photos are straight.');
        break;
      case 'shuffle':
        collage = core.shufflePhotos(collage);
        commit();
        announce('Photos shuffled.');
        break;
      default: break;
    }
  }

  root.addEventListener('click', (event) => {
    const start = event.target.closest('[data-start-size]');
    if (start) { startCollage(start.dataset.startSize); return; }
    const startLayout = event.target.closest('[data-start-layout]');
    if (startLayout) { startCollage(core.DEFAULT_SIZE, startLayout.dataset.startLayout); return; }
    const layoutChoice = event.target.closest('[data-layout]');
    if (layoutChoice) { applyLayoutKey(layoutChoice.dataset.layout); return; }
    const button = event.target.closest('[data-action]');
    if (!button || button.disabled) return;
    if (button.tagName === 'A') event.preventDefault();
    if (button.dataset.action === 'home' && views.editor.hidden) return;
    act(button.dataset.action);
  });

  sizeSelect.addEventListener('change', () => {
    collage = core.changeSize(collage, sizeSelect.value);
    // A page laid out in a grid is laid out again on the new page.
    if (collage.layout) {
      const { key, gap, radius } = collage.layout;
      collage = core.applyLayout(collage, key, gap, radius, [], newItemId).collage;
    }
    stopAdjust(false);
    layout();
    commit();
    announce(`The page is now ${core.SIZES[collage.size].label}.`);
  });

  nameInput.addEventListener('input', () => {
    document.title = `${nameInput.value.trim() || 'Untitled collage'} · Collage Studio | PRISM ERP`;
  });

  // ---------------------------------------------------------------------------
  // Panels on the left
  // ---------------------------------------------------------------------------

  function setPanel(name) {
    panel = name;
    for (const button of root.querySelectorAll('[data-panel]')) {
      button.setAttribute('aria-pressed', String(button.dataset.panel === name));
    }
    for (const section of root.querySelectorAll('[data-drawer-panel]')) {
      section.hidden = section.dataset.drawerPanel !== name;
    }
    body.classList.toggle('is-drawer-closed', !name);
    if (name === 'layouts') renderLayoutPanel();
    window.requestAnimationFrame(layout);
  }

  for (const button of root.querySelectorAll('[data-panel]')) {
    button.addEventListener('click', () => setPanel(panel === button.dataset.panel ? null : button.dataset.panel));
  }

  function renderLayoutPanel() {
    const current = collage.layout;
    for (const button of layoutGrid.querySelectorAll('[data-layout]')) {
      button.setAttribute('aria-pressed', String(current?.key === button.dataset.layout));
    }
    const gap = current ? current.gap : core.LAYOUT_LIMITS.defaultGap;
    const radius = current ? current.radius : (selectedItem()?.radius ?? 0);
    const gapInput = $('[data-layout-input="gap"]');
    const radiusInput = $('[data-layout-input="radius"]');
    gapInput.value = gap;
    gapInput.disabled = !current;
    radiusInput.value = radius;
    $('[data-out="gap"]').textContent = current ? String(gap) : '—';
    $('[data-out="radius"]').textContent = String(radius);
    $('[data-layout-hint]').textContent = current
      ? 'Changing the space puts the photos back into the layout.'
      : 'Choose a layout to set the space between photos. Rounded corners apply to every photo.';
  }

  for (const input of root.querySelectorAll('[data-layout-input]')) {
    input.addEventListener('input', () => {
      const value = Number(input.value);
      const which = input.dataset.layoutInput;
      $(`[data-out="${which}"]`).textContent = String(value);
      if (which === 'gap' && collage.layout) {
        // The space lays the page out again; that is what the hint under the sliders says.
        const next = { ...collage.layout, gap: value };
        collage = core.applyLayout(collage, next.key, next.gap, next.radius, [], newItemId).collage;
      } else if (which === 'radius') {
        // Corners change only the corners: turned photos and their crops stay as they are.
        collage.items.forEach((item) => { if (isFrame(item)) item.radius = value; });
        if (collage.layout) collage.layout = { ...collage.layout, radius: value };
      }
      syncPage();
    });
    input.addEventListener('change', () => commit());
  }

  // ---------------------------------------------------------------------------
  // Settings panel
  // ---------------------------------------------------------------------------

  const escapeHtml = (text) => String(text).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  function arrangeSections(item) {
    return `
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
        <label class="clg-field"><span class="clg-field__label">Rounded corners<output data-out="radius">${item.radius || 0}</output></span>
          <input type="range" min="0" max="${core.LAYOUT_LIMITS.radiusMax}" step="1" value="${item.radius || 0}" data-prop="radius" aria-label="Rounded corners, in page units"></label>
        <label class="clg-field"><span class="clg-field__label">See-through<output data-out="opacity">${item.opacity}%</output></span>
          <input type="range" min="10" max="100" step="1" value="${item.opacity}" data-prop="opacity" aria-label="How solid it is, in percent"></label>
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
            <li>Drag a corner to resize, or the middle of a side to change the frame's shape. Drag the round handle above it to turn it; it clicks to straight and 45°, and Shift gives 15° steps.</li>
            <li>Double-click a photo to move it inside its frame.</li>
            <li>Pink lines show when a photo lines up with the page or another photo. Hold Alt to move freely.</li>
            <li>Ctrl+Z undoes, Delete removes, Ctrl+D duplicates, and the arrow keys nudge.</li>
          </ul>
        </div>`;
      return;
    }
    if (item.kind === 'slot') {
      props.innerHTML = `
        <h2 class="clg-panel-title">Empty frame</h2>
        <p class="clg-hint">A photo goes here. Choose one, drag one from Photos, or drop one from your computer onto this frame.</p>
        <div class="clg-stack">
          <button type="button" class="btn btn-primary btn-sm" data-action="fill-slot"><i class="bi bi-plus-lg" aria-hidden="true"></i> Add a photo here</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="replace"><i class="bi bi-images" aria-hidden="true"></i> Use a photo already added</button>
        </div>
        ${arrangeSections(item)}`;
      return;
    }
    const photo = photos.get(item.photo);
    const adjusting = adjustId === item.id;
    const zoomValueNow = item.crop?.zoom || 1;
    props.innerHTML = `
      <h2 class="clg-panel-title">Photo <small title="${escapeHtml(photo?.name || '')}">${escapeHtml(photo?.name || '')}</small></h2>
      <div class="clg-section">
        <p class="clg-section__title">In its frame</p>
        <div class="clg-buttons clg-buttons--two">
          <button type="button" class="btn btn-sm ${adjusting ? 'btn-warning' : 'btn-outline-secondary'}" data-action="${adjusting ? 'adjust-done' : 'adjust'}" aria-pressed="${adjusting}"><i class="bi bi-arrows-move" aria-hidden="true"></i> ${adjusting ? 'Done moving' : 'Move photo'}</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="replace"><i class="bi bi-arrow-left-right" aria-hidden="true"></i> Replace</button>
        </div>
        <label class="clg-field"><span class="clg-field__label">Zoom<output data-out="zoom">${zoomValueNow.toFixed(1)}×</output></span>
          <input type="range" min="1" max="${core.MAX_CROP_ZOOM}" step="0.05" value="${zoomValueNow}" data-prop="zoom" aria-label="Zoom inside the frame"></label>
        <button type="button" class="btn btn-outline-secondary btn-sm w-100" data-action="fit-photo">Fit the frame to the whole photo</button>
      </div>
      ${arrangeSections(item)}`;
  }

  props.addEventListener('input', (event) => {
    const prop = event.target.dataset.prop;
    if (!prop) return;
    const value = Number(event.target.value);
    changeSelected((item) => {
      if (prop === 'zoom') zoomCrop(item, value);
      else if (prop === 'angle') item.angle = core.normaliseAngle(value);
      else item[prop] = value;
    }, false);
    const out = props.querySelector(`[data-out="${prop}"]`);
    if (out) {
      out.textContent = prop === 'angle' ? `${Math.round(value)}°`
        : prop === 'opacity' ? `${value}%`
          : prop === 'zoom' ? `${value.toFixed(1)}×` : String(value);
    }
  });

  props.addEventListener('change', (event) => {
    if (event.target.dataset.prop) {
      // A frame given its own corners is no longer bound to the layout's.
      if (event.target.dataset.prop === 'radius' && collage.layout) collage.layout = { ...collage.layout, radius: Number(event.target.value) };
      history.record(collage);
      updateUndo();
      renderLayoutPanel();
      return;
    }
    if (event.target.matches('[data-page-size]')) {
      sizeSelect.value = event.target.value;
      sizeSelect.dispatchEvent(new Event('change'));
    }
  });

  // ---------------------------------------------------------------------------
  // Photos: choose, drop, paste, and the tray
  // ---------------------------------------------------------------------------

  /**
   * The frame the next photo goes into: shown in the Photos panel ("Choose the photo for the
   * selected frame") until a photo is chosen or Cancel is pressed.
   */
  function setTarget(id) {
    targetId = id;
    const item = id && itemById(id);
    replaceBanner.hidden = !item;
    if (item) {
      $('[data-replace-text]').textContent = item.kind === 'slot'
        ? 'Choose the photo for the empty frame.'
        : 'Choose the photo to put in the selected frame.';
    }
  }

  /** Opens the file chooser for one frame: an empty one, or a photo to replace. */
  function chooseFor(id) {
    setTarget(id);
    setPanel('photos');
    fileInput.click();
  }

  /**
   * Puts a photo on the page: into `frameId` when given (an empty frame filled, or a photo
   * replaced), else into the first empty frame, else as a new photo near the centre (or at `at`).
   */
  function placePhoto(photo, { frameId = null, at = null } = {}) {
    const frame = (frameId && itemById(frameId)) || (!at ? firstSlot() : null);
    if (frame && frame.kind === 'slot') {
      const filled = core.fillSlot(frame, photo.id);
      collage.items[collage.items.indexOf(frame)] = filled;
      selectedId = filled.id;
      return;
    }
    if (frame && frame.kind === 'photo') {
      frame.photo = photo.id;
      frame.crop = { ...core.FULL_CROP };
      selectedId = frame.id;
      return;
    }
    // A photo placed freely means the page is no longer a strict grid, so changing the space
    // would not silently take it off the page.
    collage.layout = null;
    const count = collage.items.filter((i) => i.kind === 'photo').length;
    const place = core.placeNewPhoto(collage.size, photo.aspect, count);
    if (at) { place.x = at.x; place.y = at.y; }
    const item = core.photoItem(newItemId(), photo.id, photo.aspect, place);
    collage.items.push(item);
    selectedId = item.id;
  }

  /**
   * The tray's picture of a photo: a small JPEG drawn once from the working copy, so the tray
   * never makes the browser decode the original file again.
   */
  function thumbnailOf(canvas) {
    const size = core.workingSize(canvas.width, canvas.height, core.PHOTO_LIMITS.thumbnailSide);
    const small = document.createElement('canvas');
    small.width = size.width;
    small.height = size.height;
    const context = small.getContext('2d');
    context.imageSmoothingQuality = 'high';
    context.drawImage(canvas, 0, 0, size.width, size.height);
    return small.toDataURL('image/jpeg', 0.8);
  }

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
        thumbnail: thumbnailOf(canvas),
        width: bitmap.width,
        height: bitmap.height,
        aspect: bitmap.width / bitmap.height
      };
    } finally {
      bitmap.close?.();
    }
  }

  // Imports run one batch at a time: a second drop or paste waits for the first, so the photo
  // limit counts every photo already added and only one large photo is ever being decoded.
  let importQueue = Promise.resolve();

  function addFiles(fileList, options = {}) {
    const files = [...fileList];
    if (!files.length) return importQueue;
    const forCollage = collageSession;
    importQueue = importQueue.then(() => importFiles(files, options, forCollage)).catch(() => {});
    return importQueue;
  }

  async function importFiles(files, { at = null, frameId = null } = {}, forCollage) {
    // A collage left before its photos were read gets none of them, and neither does the next.
    const stillOpen = () => forCollage === collageSession;
    if (!stillOpen()) return;
    showAlert('');
    busy(files.length === 1 ? 'Adding the photo…' : `Adding ${files.length} photos…`);
    const added = [];
    try {
      const entries = await Promise.all(files.map(async (file) => ({
        file,
        name: file.name || 'Pasted photo',
        size: file.size,
        type: core.sniffImageType(new Uint8Array(await file.slice(0, 16).arrayBuffer()))
      })));
      if (!stillOpen()) return;
      const { accepted, refused } = core.checkPhotos(entries, photos.size);
      // One at a time, so a pile of large phone photos is never all decoded at once.
      for (const entry of accepted) {
        let photo;
        try {
          photo = await readPhoto(entry);
        } catch {
          refused.push({ name: entry.name, reason: 'unreadable' });
          continue;
        }
        if (!stillOpen()) return;
        photos.set(photo.id, photo);
        added.push(photo);
      }
      // The first photo goes where it was aimed (a frame, or the drop point); the rest fill
      // empty frames, then go near the centre.
      added.forEach((photo, index) => placePhoto(photo, index === 0 ? { frameId, at } : {}));
      if (added.length) {
        setTarget(null);
        commit();
      }
      showAlert(core.refusedMessage(refused));
      if (added.length) announce(added.length === 1 ? 'Photo added.' : `${added.length} photos added.`);
    } finally {
      busy('');
    }
  }

  fileInput.addEventListener('change', () => {
    const files = [...fileInput.files];
    fileInput.value = '';
    addFiles(files, { frameId: targetId });
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
      img.src = photo.thumbnail;
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
    const selected = selectedItem();
    // An empty frame that is selected takes the photo; so does the frame being replaced.
    const frameId = targetId || (selected?.kind === 'slot' ? selected.id : null);
    placePhoto(photo, { frameId });
    setTarget(null);
    commit();
    announce(frameId ? 'Photo placed in the frame.' : 'Photo added.');
  });

  tray.addEventListener('dragstart', (event) => {
    const button = event.target.closest('[data-tray-photo]');
    if (button) event.dataTransfer.setData('text/x-collage-photo', button.dataset.trayPhoto);
  });

  /** The page point under a drop, or null when it is off the page. */
  function pagePoint(event) {
    stage.setPointersPositions(event);
    const point = world.getRelativePointerPosition();
    if (!point) return null;
    if (point.x < 0 || point.y < 0 || point.x > core.PAGE_WIDTH || point.y > pageHeight()) return null;
    return point;
  }

  /** The empty frame under a drop, so a photo dropped on it fills it. */
  function slotAt(event) {
    stage.setPointersPositions(event);
    const position = stage.getPointerPosition();
    let shape = position && stage.getIntersection(position);
    while (shape && shape !== stage) {
      const item = itemById(shape.id());
      if (item) return item.kind === 'slot' ? item : null;
      shape = shape.getParent();
    }
    return null;
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
    const slot = slotAt(event);
    const at = slot ? null : pagePoint(event);
    if (fromTray) {
      const photo = photos.get(fromTray);
      if (photo) {
        placePhoto(photo, { frameId: slot?.id || null, at });
        commit();
        announce(slot ? 'Photo placed in the frame.' : 'Photo added.');
      }
      return;
    }
    addFiles(event.dataTransfer.files, { frameId: slot?.id || null, at });
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
    const selected = selectedItem();
    addFiles(files, { frameId: targetId || (selected?.kind === 'slot' ? selected.id : null) });
  });

  // ---------------------------------------------------------------------------
  // Keyboard and the mouse wheel
  // ---------------------------------------------------------------------------

  /** Records a pending nudge now, so Undo and Redo start from where the photo really is. */
  function flushNudge() {
    window.clearTimeout(nudgeTimer);
    if (!nudgePending) return;
    nudgePending = false;
    history.record(collage);
    updateUndo();
  }

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Shift') setShift(true);
    if (views.editor.hidden || document.querySelector('dialog[open]')) return;
    const typing = event.target.closest?.('input, textarea, select, [contenteditable]');
    const key = event.key.toLowerCase();
    const mod = event.ctrlKey || event.metaKey;
    if (key === 'escape') {
      if (typing) event.target.blur();
      else if (adjustId) stopAdjust();
      else if (targetId) setTarget(null);
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
    else if (key === 'enter' && selectedItem()?.kind === 'photo' && !adjustId) act('adjust');
    else if (key.startsWith('arrow') && selectedId) {
      event.preventDefault();
      const step = event.shiftKey ? 10 : 1;
      const item = selectedItem();
      if (adjustId === item.id) {
        // Arrows move the photo inside its frame while it is being adjusted.
        const photo = photos.get(item.photo);
        const dx = key === 'arrowleft' ? -step : key === 'arrowright' ? step : 0;
        const dy = key === 'arrowup' ? -step : key === 'arrowdown' ? step : 0;
        if (photo) item.crop = core.panCrop(photo.aspect, item, dx, dy);
        syncPage();
      } else {
        changeSelected((target) => {
          if (key === 'arrowleft') target.x -= step;
          if (key === 'arrowright') target.x += step;
          if (key === 'arrowup') target.y -= step;
          if (key === 'arrowdown') target.y += step;
        }, false);
      }
      window.clearTimeout(nudgeTimer);
      nudgePending = true;
      nudgeTimer = window.setTimeout(flushNudge, 400);
    }
  });
  document.addEventListener('keyup', (event) => { if (event.key === 'Shift') setShift(false); });
  window.addEventListener('blur', () => setShift(false));

  stageBox.addEventListener('wheel', (event) => {
    const item = adjustId && itemById(adjustId);
    if (item) {
      // The wheel zooms the photo inside its frame while it is being adjusted.
      event.preventDefault();
      zoomCrop(item, (item.crop?.zoom || 1) * (event.deltaY < 0 ? 1.06 : 1 / 1.06));
      syncPage();
      const out = props.querySelector('[data-out="zoom"]');
      const input = props.querySelector('[data-prop="zoom"]');
      if (out) out.textContent = `${item.crop.zoom.toFixed(1)}×`;
      if (input) input.value = item.crop.zoom;
      // Recorded for Undo once the wheel stops, the same way as a keyboard nudge, so Undo
      // and Redo flush it first and never skip it.
      window.clearTimeout(nudgeTimer);
      nudgePending = true;
      nudgeTimer = window.setTimeout(flushNudge, 300);
      return;
    }
    if (!event.ctrlKey) return;
    event.preventDefault();
    setZoom(zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1));
  }, { passive: false });

  // ---------------------------------------------------------------------------
  // Start
  // ---------------------------------------------------------------------------

  renderStart();
  updateUndo();
  showView('start');
})();
