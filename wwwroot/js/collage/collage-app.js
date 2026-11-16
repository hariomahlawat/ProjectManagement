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
  const draw = window.PrismCollageDraw;
  const stickers = window.PrismCollageStickers;
  const library = window.PrismCollageLibrary;
  if (!root) return;
  // Every part of the editor; a stale or missing script would otherwise leave a dead page.
  const parts = [
    'PrismCollage', 'Konva', 'PrismCollageDraw', 'PrismCollageStickers', 'PrismCollageDrafts',
    'PrismCollageDraftSession', 'PrismCollageEdits', 'PrismCollageChanges', 'PrismCollageFocus',
    'PrismCollageRender', 'PrismCollageLibraryDialog', 'PrismCollageSettings', 'PrismCollagePhotos',
    'PrismCollageSave', 'PrismCollagePage', 'PrismCollageDraftsScreen', 'PrismCollagePanels',
    'PrismCollageTray', 'PrismCollageKeys', 'PrismCollageState', 'PrismCollageAddPanels', 'PrismCollageActions', 'PrismCollageStart', 'PrismCollageToolbar',
    'PrismCollageMessages', 'PDFLib', 'PrismPhotoTools'
  ];
  const missing = parts.filter((name) => !window[name]);
  if (missing.length) {
    const box = root.querySelector('[data-alert]');
    const text = root.querySelector('[data-alert-text]');
    if (box && text) {
      text.textContent = 'Collage Studio could not load all its parts. Reload the page; if this keeps '
        + 'happening, tell the PRISM administrator.';
      box.hidden = false;
    }
    root.querySelectorAll('[data-view="start"] button').forEach((button) => { button.disabled = true; });
    if (window.console) window.console.error('Collage Studio: missing scripts', missing);
    return;
  }

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
  // A two-finger zoom in progress: no item is dragged or panned meanwhile.
  let pinch = null;
  let selectedId = null;
  // The photo whose picture is being moved inside its frame, or null.
  let adjustId = null;
  // The frame the next chosen photo goes into (Replace, or Add photo on an empty frame), or null.
  let targetId = null;
  let panel = 'photos';
  // The settings panel has two tabs: the selected item's settings, and Layers (every item, to
  // pick, hide or lock), so Layers is never far below a long list of settings.
  let propsTab = 'settings';
  // Preview: the page alone, as Save makes it.
  let previewing = false;
  // The start screen's picture of the draft is made again at most every few seconds while
  // editing (drawing the whole page for it each half second was a cost on phones), and always
  // when leaving the collage.
  const THUMBNAIL_EVERY_MS = 5000;
  let thumbnail = { url: '', at: 0, fresh: false, behind: false };
  // The start screen shows one row of drafts until "Show all".
  let draftsExpanded = false;
  // Counts draft opens and new collages: an open that is overtaken by another open, or by a
  // new collage, while the browser is still reading it is dropped instead of replacing it.
  let openRequest = 0;
  let zoom = 1;
  let scale = 1;
  let shiftHeld = false;
  let transforming = false;
  let sequence = 0;
  // Counts the collages opened in this tab; photo imports belong to the one they started in.
  let collageSession = 0;
  // Whether the open collage is named after its words (null until its title is first shown), and
  // the collage that has said so: the note comes once, when the words first take over the name.
  let namedFromWords = null;
  let namedFromWordsSaidFor = null;
  // Every change reaches Undo and the draft through the change log (collage-changes.js): a
  // finished change is committed, one still going on (typing, sliders, nudges) is held until it
  // pauses, and a held change ends before any other user action begins.
  const changes = window.PrismCollageChanges.createChangeLog({
    core,
    getModel: () => collage,
    onKeep: () => scheduleDraft(),
    onRecorded: (kind) => afterRecorded(kind)
  });
  for (const type of ['pointerdown', 'focusin', 'keydown']) {
    document.addEventListener(type, (event) => {
      if (window.PrismCollageChanges.isBoundary(event)) changes.flush();
    }, true);
  }
  // The photos the collage uses, by id: { id, name, file, canvas, thumbnail, width, height, aspect }.
  const photos = new Map();

  const nextId = (prefix) => `${prefix}${Date.now().toString(36)}${(++sequence).toString(36)}`;
  const newItemId = () => nextId('i');
  const itemById = (id) => collage.items.find((item) => item.id === id) || null;
  const selectedItem = () => itemById(selectedId);
  const pageHeight = () => core.pageHeight(collage.size);
  const hasPhotos = () => collage.items.some((item) => item.kind === 'photo');
  // Work worth keeping: a photo, some text or a sticker (an empty frame alone is not).
  // A photo used only as the blurred background still makes a picture worth saving.
  const hasContent = () => collage.items.some((item) => ['photo', 'text', 'sticker'].includes(item.kind))
    || (collage.background?.type === 'photo' && photos.has(collage.background.photo));
  const firstSlot = () => collage.items.find((item) => item.kind === 'slot' && !item.locked) || null;
  const isFrame = (item) => item && (item.kind === 'photo' || item.kind === 'slot');

  // The editor's own state and later parts, as the modules made from it see them: read when used,
  // never before they exist (docs/ux/collage-review-2-fixes.md, PR 13).
  // The editor's values, read by its parts but changed only through the named changes each part
  // is given (collage-state.js); and the parts made later than the parts that use them.
  const { state, change: changeOf } = window.PrismCollageState.createEditorState({
    pinch: () => pinch,
    shiftHeld: () => shiftHeld,
    transforming: () => transforming,
    pan: () => pan,
    thumbnail: () => thumbnail,
    draftsExpanded: () => draftsExpanded,
    openRequest: () => openRequest,
    scale: () => scale,
    panel: () => panel,
    propsTab: () => propsTab,
    collage: () => collage,
    collageSession: () => collageSession,
    selectedId: () => selectedId,
    adjustId: () => adjustId,
    targetId: () => targetId,
    zoom: () => zoom,
    leaving: () => leaving,
    previewing: () => previewing
  }, {
    setShiftHeld: (value) => { shiftHeld = value; },
    setTransforming: (value) => { transforming = value; },
    setScale: (value) => { scale = value; },
    setPreviewing: (value) => { previewing = value; },
    setZoomLevel: (value) => { zoom = value; },
    setThumbnail: (value) => { thumbnail = value; },
    setCollage: (model) => { collage = model; },
    setPanel: (name) => { panel = name; },
    setPropsTab: (key) => { propsTab = key; },
    setTargetId: (id) => { targetId = id; },
    // A new request to open a collage: any slower open still under way then stands down.
    nextOpenRequest: () => { openRequest += 1; return openRequest; },
    toggleDraftsExpanded: () => { draftsExpanded = !draftsExpanded; },
    // The selection without redrawing anything: for a part that places an item (the next commit
    // redraws) or finds the selected item gone.
    choose: (id) => { selectedId = id; }
  });
  /** The named changes a part may make, and no others. */
  const changesFor = (...names) => Object.freeze(Object.fromEntries(names.map((name) => [name, changeOf[name]])));
  const laterParts = window.PrismCollageState.createLaterParts({
    hideToolbar: () => hideToolbar,
    placeToolbar: () => placeToolbar,
    showAngle: () => showAngle,
    renderLayoutPanel: () => renderLayoutPanel,
    renderProps: () => renderProps,
    focusTextField: () => focusTextField,
    setTarget: () => setTarget,
    chooseFor: () => chooseFor,
    renderTray: () => renderTray,
    stage: () => stage,
    world: () => world,
    guides: () => guides,
    transformer: () => transformer,
    setPanel: () => setPanel,
    readPhoto: () => readPhoto,
    missingPhoto: () => missingPhoto,
    keptPhotos: () => keptPhotos,
    setPropsOpen: () => setPropsOpen,
    libraryPicker: () => libraryPicker,
    saveAs: () => saveAs
  });

  // ---------------------------------------------------------------------------
  // Messages
  // ---------------------------------------------------------------------------

  // Focus stays on the control that was used when a panel is drawn again (collage-focus.js).
  const focusKeeper = window.PrismCollageFocus.createFocusKeeper(root);
  const keepingFocus = (render) => focusKeeper.keep(render);

  // messages: the live line for screen readers, alerts in the toast stack, and the busy line (collage-messages.js).
  // PRISM's toasts stay clear of the selected item's toolbar and the phone's bottom bar.
  const { announce, showAlert, clearAlert, changeMadeAfterAlert, busy, clearEditorToasts } = window.PrismCollageMessages.createMessages({ $, announcer, alertBox, busyBox, views, obstacles: [toolbar, $('.clg-rail')] });
  // ---------------------------------------------------------------------------
  // Views
  // ---------------------------------------------------------------------------

  function showView(name) {
    for (const [key, view] of Object.entries(views)) view.hidden = key !== name;
    document.body.classList.toggle('is-editing', name === 'editor');
    document.title = name === 'editor'
      ? `${collageTitle()} · Collage Studio | PRISM ERP`
      : 'Collage Studio | PRISM ERP';
    const heading = views[name].querySelector('h1');
    heading?.focus({ preventScroll: true });
    if (name === 'editor') window.requestAnimationFrame(layout);
  }

  // the start screen: blank pages, layouts and the drafts (collage-start.js).
  const { renderStart } = window.PrismCollageStart.createStart({ core, $, sizeSelect, layoutGrid, photos });
  function resetEditor() {
    collageSession += 1;
    namedFromWords = null;
    changes.drop();
    session.end();
    // Every working copy is let go now, not when the browser next tidies up.
    for (const photo of photos.values()) window.PrismCollagePhotos.releasePhoto(photo);
    photos.clear();
    forgetPhotoPreviews();
    textLayouts.clear();
    lastStickerImage.clear();
    thumbnail = { url: '', at: 0, fresh: false, behind: false };
    selectedId = null;
    adjustId = null;
    targetId = null;
    zoom = 1;
  }

  async function startCollage(sizeKey, layoutKey) {
    const request = ++openRequest;
    // When this browser already keeps the most drafts it can, the user is asked now, while
    // choosing, rather than later from the autosave in the middle of their work.
    views.start.inert = true;
    let room = true;
    try {
      room = drafts ? await session.prepareNew() : true;
    } finally {
      views.start.inert = false;
    }
    if (request !== openRequest || views.start.hidden) return;
    resetEditor();
    collage = core.newCollage(sizeKey);
    if (layoutKey) {
      collage = core.applyLayout(collage, layoutKey, core.LAYOUT_LIMITS.defaultGap, 0, [], newItemId).collage;
    }
    changes.start(collage);
    // A new draft: it is first stored once it has a photo.
    session.begin({ id: nextId('d'), off: !room });
    if (!room) setDraftStatus('Not kept as a draft', true);
    nameInput.value = '';
    refreshTitle();
    sizeSelect.value = collage.size;
    showView('editor');
    setPanel('photos');
    syncAll();
  }

  let leaving = false;

  async function goHome() {
    if (leaving) return;
    leaving = true;
    freshThumbnail();
    // The editor is locked while the draft is written, so no change can slip in after the
    // last snapshot and be dropped when the editor is cleared.
    views.editor.inert = true;
    let kept = true;
    // Whether every change was written; one still waiting after five rounds is never dropped
    // without asking.
    let settled = false;
    try {
      // Anything still waiting (a nudge, a wheel zoom, a change made just before) is saved too.
      for (let round = 0; round < 5; round += 1) {
        flushNudge();
        kept = await session.saveNow();
        if (!session.pending && !changes.pending) { settled = true; break; }
      }
    } finally {
      views.editor.inert = false;
      leaving = false;
    }
    // Only when the draft cannot be kept is the user asked: a collage with photos, or a saved
    // draft whose change (even removing its last photo) was not written.
    if ((!kept || !settled) && (hasContent() || session.draft?.created)) {
      const leave = await askConfirm({
        title: 'Leave this collage?',
        message: session.problem() || (kept
          ? 'The last change is still being saved as a draft. Keep working a moment, or leave now and it may not be kept.'
          : 'This collage could not be kept as a draft, so it cannot be opened again once you leave. Pictures and PDFs you saved are kept.'),
        confirmText: 'Leave collage',
        cancelText: 'Keep working',
        tone: 'warning'
      });
      if (!leave) return;
    }
    resetEditor();
    collage = core.newCollage(core.DEFAULT_SIZE);
    changes.start(collage);
    // The editor's messages and Undo offers do not follow the user to the start screen.
    clearEditorToasts();
    showView('start');
    renderDrafts();
  }

  // Phones rarely ask before a page goes away, so the draft is written whenever the page is
  // hidden (another app, another tab, the screen locked) or put away.
  function saveOnLeaving() {
    if (views.editor.hidden || !session.draft) return;
    flushNudge();
    freshThumbnail();
    if (session.pending) session.saveNow();
  }
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') saveOnLeaving();
  });
  window.addEventListener('pagehide', saveOnLeaving);

  window.addEventListener('beforeunload', (event) => {
    const draft = session.draft;
    if (views.editor.hidden || !draft) return;
    // A nudge or wheel zoom still waiting counts as a change: recorded now, it marks the draft
    // for saving, so the page asks while it is written.
    flushNudge();
    // Only a page that never had a photo has nothing to lose; a saved draft whose last photo
    // was just removed still has that change to write.
    if (!draft.created && !hasContent()) return;
    // Saved as a draft already, and not being written now: nothing is lost by leaving.
    if (draft.created && !session.pending && !session.writing && !session.failed) return;
    session.saveNow();
    event.preventDefault();
    // Older browsers need a value here to ask; the text itself is not shown.
    event.returnValue = '';
  });

  // The page: the Konva stage and layers, drawing each item (photos, frames, text, stickers), the
  // handles, zoom, Preview and the page's size on screen (collage-page.js).
  const {
    stage, backLayer, layer, pageRect, world, guides, nodes, transformer, setShift, layout, visibleSheetTop,
    startPreview, stopPreview, setZoom, applyItem, measureText, textLayouts, layoutTextItem,
    fontsReady, stickerSource, stickerImage, lastStickerImage, clampedCrop, syncPage,
    attachTransformer, syncAll
  } = window.PrismCollagePage.createPage({
    state, parts: laterParts, change: changesFor('setShiftHeld', 'setTransforming', 'setScale', 'setPreviewing', 'setZoomLevel', 'choose'), root, core, Konva, draw, stickers, $, views, sizeSelect, stageBox, stageHost, angleTag,
    emptyHint, zoomValue, coarsePointer, photos, itemById, pageHeight, select,
    startAdjust, stopAdjust, startPan, commit, updateUndo
  });
  // Drafts in the editor: the draft status, writing the draft, its small picture, and the drafts
  // on the start screen (open, delete, Undo) (collage-drafts-screen.js).
  const {
    drafts, session, setDraftStatus, scheduleDraft, freshThumbnail, renderDrafts, undoDeleteDraft,
    openDraft
  } = window.PrismCollageDraftsScreen.createDraftsScreen({
    state, parts: laterParts, change: changesFor('setThumbnail', 'setCollage', 'nextOpenRequest'), core, askConfirm, $, views, nameInput, sizeSelect, THUMBNAIL_EVERY_MS, changes, photos,
    pageHeight, hasContent, announce, showAlert, busy, showView, resetEditor, syncAll, collageTitle,
    refreshTitle
  });
  // ---------------------------------------------------------------------------
  // Selection, toolbar and the angle while turning
  // ---------------------------------------------------------------------------

  function select(id, { fromLayers = false } = {}) {
    // Choosing an item anywhere but the Layers list shows its settings, even when it was already
    // selected from Layers (so double-click and Enter can reach the Words box).
    if (id && !fromLayers && propsTab !== 'settings') {
      propsTab = 'settings';
      if (selectedId === id) { renderProps(); return; }
    }
    if (selectedId === id) return;
    flushNudge();
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

  // the selected item's toolbar and the angle shown while it turns (collage-toolbar.js).
  const { hideToolbar, placeToolbar, showAngle } = window.PrismCollageToolbar.createToolbar({ state, core, $, stageBox, toolbar, angleTag, selectedItem, nodes, transformer, visibleSheetTop });
  // Two fingers on the page zoom it; one finger still moves items. Pointers are followed on the
  // work surface, so a pinch starting on a photo zooms instead of dragging it.
  const touches = new Map();
  stageBox.addEventListener('pointerdown', (event) => {
    if (event.pointerType !== 'touch') return;
    touches.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (touches.size === 2) {
      const [a, b] = [...touches.values()];
      pinch = { distance: Math.hypot(a.x - b.x, a.y - b.y) || 1, zoom };
      // stopDrag also cancels a drag Konva is holding ready for the first finger's next move.
      for (const node of nodes.values()) { node.stopDrag(); node.draggable(false); }
      // A move inside a frame begun by the first finger is put back and not kept.
      if (pan) {
        const panned = itemById(pan.id);
        if (panned) panned.crop = { ...pan.crop };
        pan = null;
        syncPage();
      }
      // The handles are not used either until every finger has lifted, even if one lands on them.
      transformer.stopTransform?.();
      transformer.listening(false);
    }
  }, true);
  stageBox.addEventListener('pointermove', (event) => {
    if (!touches.has(event.pointerId)) return;
    touches.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pinch && touches.size === 2) {
      const [a, b] = [...touches.values()];
      const middle = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
      // The page point between the fingers, before and after the zoom, so it stays under them.
      const before = stageHost.getBoundingClientRect();
      const point = { x: (middle.x - before.left - world.x()) / scale, y: (middle.y - before.top - world.y()) / scale };
      setZoom(pinch.zoom * Math.hypot(a.x - b.x, a.y - b.y) / pinch.distance);
      const after = stageHost.getBoundingClientRect();
      stageBox.scrollLeft += after.left + world.x() + point.x * scale - middle.x;
      stageBox.scrollTop += after.top + world.y() + point.y * scale - middle.y;
    }
  }, true);
  const endTouch = (event) => {
    touches.delete(event.pointerId);
    // Items can be dragged again once every finger has lifted.
    if (touches.size === 0 && pinch) { pinch = null; transformer.listening(true); syncPage(); }
  };
  stageBox.addEventListener('pointerup', endTouch, true);
  stageBox.addEventListener('pointercancel', endTouch, true);

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
      recordQuietly();
    }
    adjustHint.hidden = false;
    syncPage();
    renderProps();
    announce('Drag to move the photo inside its frame. Press Esc when done.');
  }

  function stopAdjust(redraw = true) {
    if (!adjustId) return;
    // The photo can be dragged again at once, even when the page is not redrawn now.
    const ended = itemById(adjustId);
    nodes.get(adjustId)?.draggable(Boolean(ended) && !ended.locked);
    adjustId = null;
    pan = null;
    adjustHint.hidden = true;
    if (redraw) { syncPage(); renderProps(); }
  }

  function startPan(node, event) {
    const item = itemById(node.id());
    const point = world.getRelativePointerPosition();
    // Two fingers zoom the page; they never move the photo inside its frame.
    if (!item || !point || pinch) return;
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

  /** The collage's name: what the user typed, or its own title from its words or start time. */
  function collageTitle() {
    return nameInput.value.trim() || core.autoTitle(collage.items, session.draft?.started);
  }

  /** Shows the collage's own title where no name was typed: in the name box and the tab. */
  function refreshTitle() {
    nameInput.placeholder = core.autoTitle(collage.items, session.draft?.started);
    // Until the user names it, the collage is called by its first words; the field says so.
    const fromWords = !nameInput.value.trim() && core.autoTitleFromText(collage.items);
    nameInput.title = fromWords ? 'Named after your first words until you type a name here' : '';
    // Said once for each collage, the first time its words name it.
    const before = namedFromWords;
    namedFromWords = Boolean(fromWords);
    if (before === false && namedFromWords && namedFromWordsSaidFor !== collageSession) {
      namedFromWordsSaidFor = collageSession;
      window.PrismToast?.show({ tone: 'info', text: 'This collage is named after its first words until you type a name at the top.' });
    }
    if (!views.editor.hidden || session.draft) document.title = `${collageTitle()} · Collage Studio | PRISM ERP`;
  }

  /** Records the collage as it is now as one Undo step, kept in the draft, without redrawing. */
  function recordQuietly() {
    changes.record();
  }

  /**
   * An edit already on the page that is still going on (typing, a slider, a colour picker, a
   * nudge): kept in the draft at once, recorded for Undo once it pauses, or as soon as anything
   * else happens (another item, Undo, leaving).
   */
  function holdEdit(pause = 600) {
    changes.hold(pause);
  }

  /** Records a held edit now, so Undo and Redo start from where things really are. */
  function flushNudge() {
    changes.flush();
  }

  /** A finished change: one Undo step, kept in the draft, and the page and panels redrawn. */
  function commit() {
    changeMadeAfterAlert();
    changes.commit();
  }

  /** After an Undo step is recorded: everything for a commit, the title and buttons otherwise. */
  function afterRecorded(kind) {
    // An unnamed collage's title follows its first words, typed or not.
    refreshTitle();
    updateUndo();
    // A new step can drop the Redo steps, or the oldest step, that last showed a removed photo:
    // its working copy is let go now rather than kept until another photo is removed.
    forgetRemovedPhotos();
    if (kind !== 'commit') return;
    if (panel === 'background') renderBackgroundPanel();
    syncPage();
    renderTray();
    renderLayoutPanel();
    renderProps();
  }

  function updateUndo() {
    $('[data-action="undo"]').disabled = !changes.canUndo();
    $('[data-action="redo"]').disabled = !changes.canRedo();
  }

  function restore(next) {
    collage = next;
    // Undo or Redo bringing back a removed photo puts it back in the tray.
    for (const id of core.photosInUse(collage)) {
      const photo = photos.get(id);
      if (photo?.removed) photo.removed = false;
    }
    refreshTitle();
    if (panel === 'background') renderBackgroundPanel();
    scheduleDraft();
    sizeSelect.value = collage.size;
    layout();
    syncPage();
    renderTray();
    renderLayoutPanel();
    renderProps();
    updateUndo();
  }

  /** Widths a photo or empty frame can be given with its Size slider, in page units. */
  const FRAME_WIDTHS = [60, 1400];

  /**
   * Settings that move, resize, reshape or turn an item; a locked item keeps them as they are.
   * A frame or shape changes the box round the photo, so those wait for Unlock too.
   */
  const LOCKED_CONTROLS = [
    '[data-prop="angle"]', '[data-prop="width"]', '[data-prop="size"]',
    '[data-action="rotate-left"]', '[data-action="rotate-right"]', '[data-action="straighten"]',
    '[data-action="fit-photo"]', '[data-finish="frame"]', '[data-finish="shape"]',
    '[data-prop="radius"]', '[data-prop="zoom"]', '[data-action="adjust"]', '[data-action="replace"]', '[data-action="delete"]',
    '[data-prop="spacing"]', '[data-prop="curve"]', '[data-text-set="font"]', '[data-text-toggle]'
  ].join(', ');

  function changeSelected(change, record = true) {
    const item = selectedItem();
    if (!item) return;
    change(item);
    if (record) commit();
    else {
      syncPage();
      // The panel is not drawn again while the user types, so the item's Layers row and the
      // collage's own title follow the words here.
      const row = props.querySelector(`[data-layer-select="${item.id}"] .clg-layer__name`);
      if (row && item.kind === 'text') row.textContent = core.layerName(item);
      refreshTitle();
      holdEdit();
    }
  }

  /** An action a lock stops: said on the screen as well as to a screen reader. */
  function sayLocked() {
    announce('It is locked. Unlock it first.');
    window.PrismToast?.show({ tone: 'info', text: 'It is locked. Unlock it first (the lock on its toolbar or in Layers).' });
  }

  /** A new item on the page (words, a sticker): selected, with its settings shown. */
  function placeNewItem(item) {
    collage.items.push(item);
    selectedId = item.id;
    propsTab = 'settings';
    stopAdjust(false);
  }

  /** Photos in the tray that are not on the page, in the order they were added. */
  const sparePhotos = () => {
    const used = new Set(collage.items.filter((item) => item.kind === 'photo').map((item) => item.photo));
    return [...photos.keys()].filter((id) => !used.has(id) && !photos.get(id).removed);
  };

  /** A photo or frame just locked: it stops moving inside its frame and waits for no photo. */
  function lockedNow(item) {
    if (adjustId === item.id) stopAdjust(false);
    if (targetId === item.id) setTarget(null);
  }

  /**
   * A layout for the page. When photos or frames are locked, the user is asked first whether they
   * go into the new layout too or stay where they are (the layout then leaves out the frames they
   * cover); closing the question keeps them where they are.
   */
  async function applyLayoutKey(key) {
    const locked = core.lockedFrames(collage);
    let choice = 'keep';
    if (locked.length) {
      const one = locked.length === 1;
      const gapNow = collage.layout ? collage.layout.gap : core.LAYOUT_LIMITS.defaultGap;
      const { out, of } = core.framesLeftOut(collage, key, gapNow);
      const staying = out === 0
        ? `If ${one ? 'it stays' : 'they stay'}, all ${of} frames of the new layout are still used.`
        : `If ${one ? 'it stays' : 'they stay'}, ${out} of the new layout's ${of} frames ${out === 1 ? 'is' : 'are'} left out, because ${one ? 'it covers' : 'they cover'} most of ${out === 1 ? 'it' : 'them'}.`;
      const join = await askConfirm({
        title: one ? '1 photo is locked' : `${locked.length} photos are locked`,
        message: `${one ? 'Put it in the new layout too, or keep it where it is?' : 'Put them in the new layout too, or keep them where they are?'} ${staying}`,
        confirmText: one ? 'Put it in the layout' : 'Put them in the layout',
        cancelText: one ? 'Keep it where it is' : 'Keep them where they are',
        tone: 'primary',
        icon: 'bi-lock'
      });
      if (views.editor.hidden) return;
      choice = join ? 'join' : 'keep';
    }
    const gap = collage.layout ? collage.layout.gap : core.LAYOUT_LIMITS.defaultGap;
    const radius = collage.layout ? collage.layout.radius : 0;
    const result = core.applyLayout(collage, key, gap, radius, sparePhotos(), newItemId, { locked: choice });
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

  // the Text, Stickers and Background panels (collage-add-panels.js).
  const { renderTextPanel, forgetPhotoPreviews, renderBackgroundPanel, setBackground, loadUnitStickers, renderStickerPanel } = window.PrismCollageAddPanels.createAddPanels({ state, parts: laterParts, root, core, draw, stickers, library, $, views, photos, newItemId, pageHeight, keepingFocus, announce, backLayer, layoutTextItem, stickerSource, syncPage, commit, placeNewItem });
  // the actions: buttons, menu items and keys that change the collage (collage-actions.js).
  const { offerUndo, act } = window.PrismCollageActions.createActions({ state, parts: laterParts, root, core, $, views, fileInput, nameInput, sizeSelect, body, props, changes, photos, newItemId, itemById, selectedItem, isFrame, change: changesFor('setCollage', 'choose', 'toggleDraftsExpanded'), announce, clearAlert, startCollage, goHome, layer, layout, startPreview, stopPreview, setZoom, session, scheduleDraft, renderDrafts, undoDeleteDraft, openDraft, select, startAdjust, stopAdjust, refreshTitle, commit, updateUndo, restore, changeSelected, lockedNow, applyLayoutKey, sayLocked
  });
  // The panels: the left drawer (layouts and the rest) and the settings panel with its Settings
  // and Layers tabs (collage-panels.js).
  const {
    setPropsOpen, setPanel, renderLayoutPanel, renderProps, focusTextField
  } = window.PrismCollagePanels.createPanels({
    state, change: changesFor('setPanel', 'setPropsTab', 'setCollage'), $, root, core, draw, stickers, sizeSelect, body, layoutGrid, props, changes, photos,
    selectedItem, isFrame, keepingFocus, layout, stickerSource, clampedCrop, syncPage, select,
    zoomCrop, flushNudge, holdEdit, commit, FRAME_WIDTHS, LOCKED_CONTROLS, changeSelected,
    renderBackgroundPanel, offerUndo
  });
  // Photos: choosing, dropping and pasting them, reading them in, the tray, and From PRISM Photos
  // (collage-tray.js).
  const {
    setTarget, chooseFor, readPhoto, missingPhoto, libraryPicker, renderTray, keptPhotos,
    forgetRemovedPhotos
  } = window.PrismCollageTray.createTray({
    state, change: changesFor('setTargetId', 'choose'), $, root, core, library, views, fileInput, stageBox, tray, photoCount, replaceBanner,
    changes, photos, nextId, newItemId, itemById, selectedItem, pageHeight, firstSlot,
    keepingFocus, announce, showAlert, busy, session, scheduleDraft, stage, world, layout, commit,
    setPanel
  });
  // ---------------------------------------------------------------------------
  // Save as… (collage-save.js)
  // ---------------------------------------------------------------------------

  const saveAs = window.PrismCollageSave.createSaveDialog({
    $, core, draw, stickers, measureText, fontsReady, stickerImage,
    getCollage: () => collage,
    photos, announce, showAlert, collageTitle, hasContent, stopAdjust, flushNudge
  });

  // The keyboard (shortcuts, nudges, Enter and Esc) and the mouse wheel (collage-keys.js).
  window.PrismCollageKeys.createKeys({
    state, change: changesFor(), core, views, stageBox, props, photos, itemById, selectedItem, announce, setShift,
    stopPreview, setZoom, syncPage, select, stopAdjust, zoomCrop, holdEdit, changeSelected, act,
    focusTextField, setTarget, sayLocked
  });
  // ---------------------------------------------------------------------------
  // Start
  // ---------------------------------------------------------------------------

  renderStart();
  renderTextPanel();
  renderStickerPanel();
  loadUnitStickers();
  root.querySelector('[data-sticker-search]').addEventListener('input', renderStickerPanel);
  // Held like every other slider, so a draft written mid-pick (the page hidden) keeps the colour.
  root.querySelector('[data-bg-pick]').addEventListener('input', (event) => { setBackground({ type: 'color', value: event.target.value }, false); holdEdit(); });
  root.querySelector('[data-bg-pick]').addEventListener('change', (event) => setBackground({ type: 'color', value: event.target.value }));
  renderDrafts();
  updateUndo();
  showView('start');
})();
