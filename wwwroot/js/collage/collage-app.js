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

  /** Resets the editor to an empty page; the caller fills it in. */
  function resetEditor() {
    collageSession += 1;
    window.clearTimeout(nudgeTimer);
    window.clearTimeout(draftTimer);
    nudgePending = false;
    draftPending = false;
    photos.clear();
    storedPhotos.clear();
    selectedId = null;
    adjustId = null;
    targetId = null;
    zoom = 1;
  }

  function startCollage(sizeKey, layoutKey) {
    resetEditor();
    collage = core.newCollage(sizeKey);
    if (layoutKey) {
      collage = core.applyLayout(collage, layoutKey, core.LAYOUT_LIMITS.defaultGap, 0, [], newItemId).collage;
    }
    history = core.createHistory(collage);
    // A new draft: it is first stored once it has a photo.
    draft = { id: nextId('d'), created: false, off: false };
    draftFailed = '';
    setDraftStatus('');
    nameInput.value = 'Untitled collage';
    sizeSelect.value = collage.size;
    showView('editor');
    setPanel('photos');
    syncAll();
  }

  async function goHome() {
    // A nudge or wheel zoom still waiting is recorded and marked for the draft first.
    flushNudge();
    // The draft is saved before leaving; only when it cannot be is the user asked.
    const kept = await saveDraftNow();
    if (!kept && hasPhotos()) {
      const leave = await askConfirm({
        title: 'Leave this collage?',
        message: draftProblem() || 'This collage could not be kept as a draft, so it cannot be opened again once you leave. Pictures and PDFs you saved are kept.',
        confirmText: 'Leave collage',
        cancelText: 'Keep working',
        tone: 'warning'
      });
      if (!leave) return;
    }
    resetEditor();
    collage = core.newCollage(core.DEFAULT_SIZE);
    history = core.createHistory(collage);
    draft = null;
    showView('start');
    renderDrafts();
  }

  window.addEventListener('beforeunload', (event) => {
    if (views.editor.hidden || !draft) return;
    // A nudge or wheel zoom still waiting counts as a change: recorded now, it marks the draft
    // for saving, so the page asks while it is written.
    flushNudge();
    // Only a page that never had a photo has nothing to lose; a saved draft whose last photo
    // was just removed still has that change to write.
    if (!draft.created && !hasPhotos()) return;
    // Saved as a draft already, and not being written now: nothing is lost by leaving.
    if (draft && draft.created && !draftPending && !draftWriting && !draftFailed) return;
    saveDraftNow();
    event.preventDefault();
    // Older browsers need a value here to ask; the text itself is not shown.
    event.returnValue = '';
  });

  // ---------------------------------------------------------------------------
  // Drafts (kept in this browser)
  // ---------------------------------------------------------------------------

  const drafts = window.PrismCollageDrafts || null;
  const draftStatus = $('[data-draft-status]');
  // The draft being edited: { id, created, off }; null on the start screen.
  let draft = null;
  let draftTimer = 0;
  let draftPending = false;
  // True while a draft is being written; leaving then would lose the latest change.
  let draftWriting = false;
  let draftFailed = '';
  let draftSaving = Promise.resolve(true);
  let askedToKeep = false;
  // Photos of this draft already in the browser's database, by id.
  const storedPhotos = new Set();

  function setDraftStatus(text, warning = false) {
    draftStatus.textContent = text;
    draftStatus.classList.toggle('is-warning', warning);
  }

  /** Why the draft is not kept, in a sentence, or '' when it is. */
  function draftProblem() {
    if (!drafts) return 'Drafts are not available in this browser.';
    if (draft?.off) return 'This collage is not kept as a draft.';
    if (draftFailed === 'full') return 'The draft was not saved: this browser has no room left. Delete old drafts on the start screen, or save a picture or a PDF.';
    if (draftFailed) return 'The draft was not saved: this browser does not keep data here (a private window, or storage turned off). Save a picture or a PDF to keep your work.';
    return '';
  }

  /** Saves the draft half a second after the last change. */
  function scheduleDraft() {
    if (!draft || draft.off || !drafts) return;
    draftPending = true;
    window.clearTimeout(draftTimer);
    draftTimer = window.setTimeout(() => { saveDraftNow(); }, 500);
  }

  /** A small picture of the page for the start screen, without handles or guides. */
  function pageThumbnail() {
    const shown = transformer.visible();
    transformer.visible(false);
    guides.visible(false);
    const width = core.PAGE_WIDTH * scale;
    try {
      return stage.toDataURL({
        x: world.x(), y: world.y(), width, height: pageHeight() * scale,
        pixelRatio: Math.min(4, 320 / Math.max(1, width)), mimeType: 'image/jpeg', quality: 0.8
      });
    } catch {
      return '';
    } finally {
      transformer.visible(shown);
      guides.visible(true);
    }
  }

  /**
   * Saves the draft now: the collage, its picture and any photos not stored yet, in one go.
   * Resolves true when the draft is kept (or there is nothing to keep yet).
   */
  function saveDraftNow() {
    window.clearTimeout(draftTimer);
    draftSaving = draftSaving.then(async () => {
      if (!draft || !drafts) return false;
      if (draft.off) return false;
      if (!draftPending && draft.created) return !draftFailed;
      draftPending = false;
      // A draft starts once it has a photo; an empty page is not worth keeping.
      if (!draft.created && !hasPhotos()) return true;
      const forDraft = draft;
      try {
        if (!forDraft.created) {
          const { shown } = core.sortDrafts(await drafts.list(), Date.now());
          const oldest = core.draftToMakeRoom(shown);
          if (oldest) {
            const ok = await askConfirm({
              title: `Delete the oldest draft, “${oldest.title}”?`,
              message: `Collage Studio keeps up to ${core.MAX_DRAFTS} drafts in this browser. Deleting the oldest makes room for this one.`,
              confirmText: 'Delete oldest draft',
              cancelText: 'Do not keep this one',
              tone: 'warning'
            });
            if (!ok) {
              forDraft.off = true;
              setDraftStatus('Not kept as a draft', true);
              return false;
            }
            await drafts.purge(oldest.id);
          }
        }
        const newPhotos = [...photos.values()].filter((photo) => !storedPhotos.has(photo.id));
        const record = {
          id: forDraft.id,
          title: nameInput.value.trim() || 'Untitled collage',
          saved: Date.now(),
          deletedAt: null,
          model: collage,
          size: collage.size,
          photoCount: photos.size,
          thumbnail: views.editor.hidden ? '' : pageThumbnail()
        };
        setDraftStatus('Saving draft…');
        draftWriting = true;
        try {
          await drafts.save(record, newPhotos);
        } finally {
          draftWriting = false;
        }
        if (forDraft !== draft) return true;
        newPhotos.forEach((photo) => storedPhotos.add(photo.id));
        forDraft.created = true;
        draftFailed = '';
        setDraftStatus('Draft saved in this browser');
        if (!askedToKeep) { askedToKeep = true; drafts.keep(); }
        return true;
      } catch (error) {
        draftFailed = drafts.reasonOf(error);
        if (forDraft === draft) setDraftStatus(draftFailed === 'full' ? 'Draft not saved: no room left' : 'Draft not saved in this browser', true);
        return false;
      }
    });
    return draftSaving;
  }

  let undoTimer = 0;
  let undoDraftId = null;

  async function renderDrafts() {
    const section = $('[data-drafts-section]');
    const grid = $('[data-drafts]');
    if (!drafts) { section.hidden = true; return; }
    let all;
    try {
      all = await drafts.list();
    } catch {
      section.hidden = true;
      return;
    }
    const { shown, purge } = core.sortDrafts(all, Date.now());
    for (const id of purge) if (id !== undoDraftId) drafts.purge(id).catch(() => {});
    grid.textContent = '';
    section.hidden = !shown.length && !undoDraftId;
    $('[data-drafts-count]').textContent = shown.length ? String(shown.length) : '';
    for (const item of shown) {
      const card = document.createElement('div');
      card.className = 'clg-draft';
      const open = document.createElement('button');
      open.type = 'button';
      open.className = 'clg-draft__open';
      open.dataset.openDraft = item.id;
      const picture = document.createElement('img');
      picture.alt = '';
      if (item.thumbnail) picture.src = item.thumbnail;
      const title = document.createElement('b');
      title.textContent = item.title;
      const meta = document.createElement('small');
      const size = core.SIZES[item.size]?.label || '';
      meta.textContent = `Saved ${core.savedAgo(item.saved, Date.now())} · ${size} · ${item.photoCount} photo${item.photoCount === 1 ? '' : 's'}`;
      open.append(picture, title, meta);
      open.setAttribute('aria-label', `Open draft ${item.title}, saved ${core.savedAgo(item.saved, Date.now())}`);
      const remove = document.createElement('button');
      remove.type = 'button';
      remove.className = 'btn btn-sm btn-outline-danger clg-draft__delete';
      remove.dataset.deleteDraft = item.id;
      remove.title = 'Delete draft';
      remove.setAttribute('aria-label', `Delete draft ${item.title}`);
      remove.innerHTML = '<i class="bi bi-trash3" aria-hidden="true"></i>';
      card.append(open, remove);
      grid.appendChild(card);
    }
    const used = await drafts.usedBytes();
    $('[data-drafts-space]').textContent = used && window.PrismPhotoTools
      ? `Drafts and this site's other saved data use ${window.PrismPhotoTools.formatBytes(used)} of this browser's storage.`
      : '';
  }

  async function deleteDraft(id) {
    const record = await drafts.get(id).catch(() => null);
    if (!record) return;
    // Hidden at once, removed for good once Undo has gone.
    if (undoDraftId && undoDraftId !== id) drafts.purge(undoDraftId).catch(() => {});
    await drafts.markDeleted(id, Date.now());
    undoDraftId = id;
    $('[data-draft-undo-text]').textContent = `Draft “${record.title}” deleted.`;
    $('[data-draft-undo]').hidden = false;
    window.clearTimeout(undoTimer);
    undoTimer = window.setTimeout(() => {
      $('[data-draft-undo]').hidden = true;
      drafts.purge(id).catch(() => {});
      if (undoDraftId === id) undoDraftId = null;
      renderDrafts();
    }, core.DRAFT_UNDO_MS);
    announce(`Draft ${record.title} deleted. Undo is on the screen for ten seconds.`);
    renderDrafts();
  }

  async function undoDeleteDraft() {
    if (!undoDraftId) return;
    window.clearTimeout(undoTimer);
    await drafts.restore(undoDraftId);
    undoDraftId = null;
    $('[data-draft-undo]').hidden = true;
    announce('Draft brought back.');
    renderDrafts();
  }

  async function openDraft(id) {
    let record;
    let stored;
    try {
      record = await drafts.get(id);
      stored = record ? await drafts.photosOf(id) : [];
    } catch {
      showAlert('The draft could not be opened: this browser did not give it back.');
      return;
    }
    if (!record || record.deletedAt) { renderDrafts(); return; }
    resetEditor();
    const forCollage = collageSession;
    busy(stored.length ? `Opening the draft and its ${stored.length} photo${stored.length === 1 ? '' : 's'}…` : 'Opening the draft…');
    let unreadable = 0;
    try {
      // One photo at a time, as when they were first added.
      for (const entry of stored) {
        try {
          const photo = await readPhoto({ file: entry.file, name: entry.name }, entry.id);
          if (forCollage !== collageSession) return;
          photos.set(photo.id, photo);
          storedPhotos.add(photo.id);
        } catch {
          unreadable += 1;
        }
      }
    } finally {
      busy('');
    }
    const model = core.readDraftModel(record.model, [...photos.keys()]);
    if (!model) {
      showAlert('This draft was made by a newer version of PRISM, or is damaged, and cannot be opened here.');
      return;
    }
    collage = model;
    history = core.createHistory(collage);
    draft = { id: record.id, created: true, off: false };
    draftFailed = '';
    nameInput.value = record.title;
    sizeSelect.value = collage.size;
    setDraftStatus('Draft saved in this browser');
    showView('editor');
    setPanel('photos');
    syncAll();
    if (unreadable) showAlert(`${unreadable} photo${unreadable === 1 ? '' : 's'} of this draft could not be read back and ${unreadable === 1 ? 'was' : 'were'} left out.`);
    announce(`Draft ${record.title} opened.`);
  }

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
      scheduleDraft();
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
    scheduleDraft();
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
    scheduleDraft();
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
      // Undo and Redo end moving a photo inside its frame only when there is a step to take.
      case 'undo': flushNudge(); if (history.canUndo()) { stopAdjust(false); restore(history.undo()); } break;
      case 'redo': flushNudge(); if (history.canRedo()) { stopAdjust(false); restore(history.redo()); } break;
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
      case 'save-open': openSave(); break;
      case 'save-close': if (!saving) saveDialog.close(); break;
      case 'save-go': saveCollage(); break;
      case 'draft-undo': undoDeleteDraft(); break;
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
    const openOne = event.target.closest('[data-open-draft]');
    if (openOne) { openDraft(openOne.dataset.openDraft); return; }
    const deleteOne = event.target.closest('[data-delete-draft]');
    if (deleteOne) { deleteDraft(deleteOne.dataset.deleteDraft); return; }
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
    // A page laid out in a grid is laid out again on the new page, every frame in its own place.
    if (collage.layout) collage = core.reflowLayout(collage, collage.layout.gap);
    stopAdjust(false);
    layout();
    commit();
    announce(`The page is now ${core.SIZES[collage.size].label}.`);
  });

  nameInput.addEventListener('input', () => {
    scheduleDraft();
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
        collage = core.reflowLayout(collage, value);
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
      scheduleDraft();
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

  async function readPhoto(entry, keepId = null) {
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
        id: keepId || nextId('p'),
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
    // An empty frame that is selected takes the photo, as it does from the tray or a paste.
    const selected = selectedItem();
    addFiles(files, { frameId: targetId || (selected?.kind === 'slot' ? selected.id : null) });
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
  // Save as…
  // ---------------------------------------------------------------------------

  const saveDialog = $('[data-save-dialog]');
  const saveResult = $('[data-save-result]');
  const targetRow = $('[data-target-row]');
  const targetOn = $('[data-target-on]');
  const targetValue = $('[data-target-value]');
  const targetUnit = $('[data-target-unit]');
  const photoTools = window.PrismPhotoTools;
  let saving = false;

  const chosen = (name) => saveDialog.querySelector(`input[name="${name}"]:checked`)?.value;

  function syncSaveDialog() {
    const size = core.SIZES[collage.size];
    const pixels = core.savePixels(collage.size, 'print');
    $('[data-print-note]').textContent = size.mm
      ? `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels, 300 dots per inch on ${size.label} paper.`
      : `${pixels.width.toLocaleString('en-IN')} × ${pixels.height.toLocaleString('en-IN')} pixels.`;
    $('[data-pdf-note]').textContent = size.mm
      ? `${size.label} at its true size (${size.note}), ready to print.`
      : `One page shaped like the ${size.label.toLowerCase()} page.`;
    const jpeg = chosen('format') === 'jpeg';
    targetRow.hidden = !jpeg;
    targetValue.disabled = !targetOn.checked;
    targetUnit.disabled = !targetOn.checked;
    const empty = collage.items.filter((item) => item.kind === 'slot').length;
    const emptyLine = $('[data-save-empty]');
    emptyLine.hidden = !empty;
    emptyLine.textContent = empty === 1
      ? 'The empty frame is left out of the saved picture.'
      : `The ${empty} empty frames are left out of the saved picture.`;
  }

  function openSave() {
    if (!collage.items.some((item) => item.kind === 'photo')) {
      showAlert('Add a photo first; there is nothing to save yet.');
      return;
    }
    flushNudge();
    stopAdjust();
    saveResult.hidden = true;
    syncSaveDialog();
    saveDialog.showModal();
  }

  saveDialog.addEventListener('change', syncSaveDialog);
  saveDialog.addEventListener('cancel', (event) => { if (saving) event.preventDefault(); });

  /**
   * The largest canvas this browser will really draw, up to `width` × `height`: some browsers
   * give a blank canvas past their limit instead of an error, so a pixel is drawn and read back.
   */
  function largestCanvas(width, height) {
    let size = { width, height, scaled: false };
    for (let attempt = 0; attempt < 10; attempt += 1) {
      const probe = document.createElement('canvas');
      probe.width = size.width;
      probe.height = size.height;
      let works = false;
      try {
        const context = probe.getContext('2d');
        context.fillStyle = '#000';
        context.fillRect(size.width - 1, size.height - 1, 1, 1);
        works = context.getImageData(size.width - 1, size.height - 1, 1, 1).data[3] === 255;
      } catch {
        works = false;
      }
      probe.width = 0;
      probe.height = 0;
      if (works) return size;
      size = { ...core.fitArea(size.width, size.height, size.width * size.height * 0.7), scaled: true };
    }
    return null;
  }

  /** Draws the collage from the original photos, each read only as large as its frame needs. */
  async function drawCollage(width, height) {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    context.imageSmoothingQuality = 'high';
    const s = width / core.PAGE_WIDTH;
    context.fillStyle = collage.background?.value || '#ffffff';
    context.fillRect(0, 0, width, height);
    for (const item of core.itemsToSave(collage)) {
      const photo = photos.get(item.photo);
      if (!photo) continue;
      const frameW = item.width * s;
      const frameH = frameW / item.aspect;
      const want = core.decodeSize(photo, item.aspect, item.crop, frameW);
      // One original at a time, read at the size this frame needs and closed straight after.
      const bitmap = await createImageBitmap(photo.file, { resizeWidth: want.width, resizeHeight: want.height, resizeQuality: 'high' });
      try {
        const region = core.cropRegion(bitmap.width / bitmap.height, item.aspect, item.crop);
        context.save();
        context.globalAlpha = item.opacity / 100;
        context.translate(item.x * s, item.y * s);
        context.rotate(item.angle * Math.PI / 180);
        if (item.radius) {
          context.beginPath();
          context.roundRect(-frameW / 2, -frameH / 2, frameW, frameH, Math.min(item.radius * s, frameW / 2, frameH / 2));
          context.clip();
        }
        context.drawImage(bitmap,
          region.x * bitmap.width, region.y * bitmap.height, region.width * bitmap.width, region.height * bitmap.height,
          -frameW / 2, -frameH / 2, frameW, frameH);
        context.restore();
      } finally {
        bitmap.close?.();
      }
    }
    return canvas;
  }

  const toBytes = (canvas, type, quality) => new Promise((resolve, reject) => {
    canvas.toBlob((blob) => (blob ? blob.arrayBuffer().then((buffer) => resolve(new Uint8Array(buffer)), reject) : reject(new Error('encode'))), type, quality);
  });

  /** The canvas drawn smaller, for the size search when a JPG must fit a file size. */
  function scaledCanvas(source, width, height) {
    if (width === source.width && height === source.height) return source;
    const small = document.createElement('canvas');
    small.width = width;
    small.height = height;
    const context = small.getContext('2d');
    context.imageSmoothingQuality = 'high';
    context.drawImage(source, 0, 0, width, height);
    return small;
  }

  function download(bytes, type, name) {
    const url = URL.createObjectURL(new Blob([bytes], { type }));
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 60000);
  }

  async function saveCollage() {
    if (saving) return;
    const format = chosen('format');
    const quality = chosen('quality');
    let targetBytes = null;
    if (format === 'jpeg' && targetOn.checked) {
      targetBytes = photoTools?.parseTargetBytes(targetValue.value, targetUnit.value) ?? null;
      if (!targetBytes) {
        saveResult.hidden = false;
        saveResult.textContent = 'Type a size such as 500 KB or 2 MB.';
        targetValue.focus();
        return;
      }
    }
    saving = true;
    const goButton = $('[data-save-go]');
    goButton.disabled = true;
    saveResult.hidden = false;
    saveResult.textContent = 'Making the picture…';
    try {
      const wanted = core.savePixels(collage.size, quality);
      const fits = largestCanvas(wanted.width, wanted.height);
      if (!fits) throw new Error('canvas');
      const canvas = await drawCollage(fits.width, fits.height);
      const stem = core.fileStem(nameInput.value);
      let bytes;
      let width = canvas.width;
      let height = canvas.height;
      let missed = false;
      let type;
      let name;
      if (format === 'png') {
        type = 'image/png';
        name = `${stem}.png`;
        bytes = await toBytes(canvas, type);
      } else if (format === 'jpeg') {
        type = 'image/jpeg';
        name = `${stem}.jpg`;
        if (targetBytes && photoTools) {
          const result = await photoTools.encodeToTarget({
            encode: (w, h, q) => toBytes(scaledCanvas(canvas, w, h), type, q),
            lossy: true,
            width,
            height,
            targetBytes
          });
          bytes = result.bytes;
          width = result.width;
          height = result.height;
          missed = !result.reached;
        } else {
          bytes = await toBytes(canvas, type, 0.92);
        }
      } else {
        type = 'application/pdf';
        name = `${stem}.pdf`;
        const page = core.pdfPage(collage.size);
        const jpeg = await toBytes(canvas, 'image/jpeg', 0.92);
        const pdf = await window.PDFLib.PDFDocument.create();
        pdf.setTitle(nameInput.value.trim() || 'Collage');
        pdf.setCreator('PRISM Collage Studio');
        const image = await pdf.embedJpg(jpeg);
        pdf.addPage([page.width, page.height]).drawImage(image, { x: 0, y: 0, width: page.width, height: page.height });
        bytes = await pdf.save();
      }
      canvas.width = 0;
      canvas.height = 0;
      download(bytes, type, name);
      const sizeText = photoTools ? photoTools.formatBytes(bytes.length) : `${Math.ceil(bytes.length / 1000)} KB`;
      const parts = [`Saved “${name}”`, `${width.toLocaleString('en-IN')} × ${height.toLocaleString('en-IN')} pixels`, sizeText];
      let line = `${parts.join(' · ')}.`;
      if (fits.scaled) {
        const dpi = core.paperDpi(collage.size, fits.width);
        line += ` This browser cannot draw a picture that large, so it was made a little smaller${dpi ? ` (${dpi} dots per inch)` : ''}.`;
      }
      if (missed) line += ` It could not get under ${photoTools.formatBytes(targetBytes)}; this is the smallest it can be. Choose Screen or a larger size.`;
      saveResult.textContent = line;
      announce(line);
    } catch {
      saveResult.textContent = 'The collage could not be saved. Try Screen quality, or close other tabs and try again.';
    } finally {
      saving = false;
      goButton.disabled = false;
    }
  }

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
    scheduleDraft();
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
    if (mod && key === 's') { event.preventDefault(); act('save-open'); }
    else if (mod && key === 'z' && !event.shiftKey) { event.preventDefault(); act('undo'); }
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
  renderDrafts();
  updateUndo();
  showView('start');
})();
