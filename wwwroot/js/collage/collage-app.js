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
    'PrismCollageTray', 'PrismCollageKeys'
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
  const app = {
    get pinch() { return pinch; }, set pinch(value) { pinch = value; },
    get shiftHeld() { return shiftHeld; }, set shiftHeld(value) { shiftHeld = value; },
    get transforming() { return transforming; }, set transforming(value) { transforming = value; },
    get pan() { return pan; }, set pan(value) { pan = value; },
    get renderLayoutPanel() { return renderLayoutPanel; },
    get renderProps() { return renderProps; },
    get focusTextField() { return focusTextField; },
    get setTarget() { return setTarget; },
    get chooseFor() { return chooseFor; },
    get renderTray() { return renderTray; },
    get thumbnail() { return thumbnail; }, set thumbnail(value) { thumbnail = value; },
    get draftsExpanded() { return draftsExpanded; }, set draftsExpanded(value) { draftsExpanded = value; },
    get openRequest() { return openRequest; }, set openRequest(value) { openRequest = value; },
    get scale() { return scale; }, set scale(value) { scale = value; },
    get stage() { return stage; },
    get world() { return world; },
    get guides() { return guides; },
    get transformer() { return transformer; },
    get setPanel() { return setPanel; },
    get readPhoto() { return readPhoto; },
    get missingPhoto() { return missingPhoto; },
    get keptPhotos() { return keptPhotos; },
    get panel() { return panel; }, set panel(value) { panel = value; },
    get propsTab() { return propsTab; }, set propsTab(value) { propsTab = value; },
    get collage() { return collage; }, set collage(value) { collage = value; },
    get collageSession() { return collageSession; }, set collageSession(value) { collageSession = value; },
    get selectedId() { return selectedId; }, set selectedId(value) { selectedId = value; },
    get adjustId() { return adjustId; }, set adjustId(value) { adjustId = value; },
    get targetId() { return targetId; }, set targetId(value) { targetId = value; },
    get zoom() { return zoom; }, set zoom(value) { zoom = value; },
    get leaving() { return leaving; }, set leaving(value) { leaving = value; },
    get previewing() { return previewing; }, set previewing(value) { previewing = value; },
  };

  // ---------------------------------------------------------------------------
  // Messages
  // ---------------------------------------------------------------------------

  // Focus stays on the control that was used when a panel is drawn again (collage-focus.js).
  const focusKeeper = window.PrismCollageFocus.createFocusKeeper(root);
  const keepingFocus = (render) => focusKeeper.keep(render);

  function announce(text) {
    announcer.textContent = '';
    window.setTimeout(() => { announcer.textContent = text; }, 30);
  }

  /**
   * A message the user should read: shown in PRISM's toast stack with the Undo offers (one place at
   * the bottom, never over Preview), gone after a few seconds or at the user's next change. The
   * text also stays in the page's own alert line, which shows only when the toasts are missing.
   */
  const ALERT_LIFE = 9000;
  let alertToast = null;
  let alertShown = 0;

  function showAlert(text) {
    alertToast?.dismiss();
    alertToast = null;
    $('[data-alert-text]').textContent = text;
    alertShown = text ? Date.now() : 0;
    if (text && window.PrismToast) {
      alertBox.hidden = true;
      const toast = window.PrismToast.show({ tone: 'warning', text, life: ALERT_LIFE });
      alertToast = toast;
      // The toast decides when it goes (it waits while the tab is hidden or someone reads it); the
      // page's own text follows it.
      const host = toast.element?.parentNode;
      if (host) {
        const watch = new MutationObserver(() => {
          if (toast.element.isConnected) return;
          watch.disconnect();
          if (alertToast === toast) clearAlert();
        });
        watch.observe(host, { childList: true });
      }
      return;
    }
    alertBox.hidden = !text;
  }

  /** The message is done with: the user has moved on, or it has faded. */
  function clearAlert() {
    if (!alertShown) return;
    alertToast?.dismiss();
    alertToast = null;
    alertShown = 0;
    $('[data-alert-text]').textContent = '';
    alertBox.hidden = true;
  }

  /** The user's next change ends a message shown before it (not one shown by this change). */
  function changeMadeAfterAlert() {
    if (alertShown && Date.now() - alertShown > 400) clearAlert();
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
      ? `${collageTitle()} · Collage Studio | PRISM ERP`
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
    namedFromWords = null;
    changes.drop();
    session.end();
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
    try {
      // Anything still waiting (a nudge, a wheel zoom, a change made just before) is saved too.
      for (let round = 0; round < 5; round += 1) {
        flushNudge();
        kept = await session.saveNow();
        if (!session.pending && !changes.pending) break;
      }
    } finally {
      views.editor.inert = false;
      leaving = false;
    }
    // Only when the draft cannot be kept is the user asked: a collage with photos, or a saved
    // draft whose change (even removing its last photo) was not written.
    if (!kept && (hasContent() || session.draft?.created)) {
      const leave = await askConfirm({
        title: 'Leave this collage?',
        message: session.problem() || 'This collage could not be kept as a draft, so it cannot be opened again once you leave. Pictures and PDFs you saved are kept.',
        confirmText: 'Leave collage',
        cancelText: 'Keep working',
        tone: 'warning'
      });
      if (!leave) return;
    }
    resetEditor();
    collage = core.newCollage(core.DEFAULT_SIZE);
    changes.start(collage);
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
    app, root, core, Konva, draw, stickers, $, views, sizeSelect, stageBox, stageHost, angleTag,
    adjustHint, emptyHint, zoomValue, coarsePointer, photos, itemById, pageHeight, select,
    hideToolbar, placeToolbar, showAngle, startAdjust, stopAdjust, startPan, commit, updateUndo
  });
  // Drafts in the editor: the draft status, writing the draft, its small picture, and the drafts
  // on the start screen (open, delete, Undo) (collage-drafts-screen.js).
  const {
    drafts, session, setDraftStatus, scheduleDraft, freshThumbnail, renderDrafts, undoDeleteDraft,
    openDraft
  } = window.PrismCollageDraftsScreen.createDraftsScreen({
    app, core, askConfirm, $, views, nameInput, sizeSelect, THUMBNAIL_EVERY_MS, changes, photos,
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

  function hideToolbar() {
    toolbar.hidden = true;
  }

  function placeToolbar() {
    const node = selectedId && nodes.get(selectedId);
    const item = selectedItem();
    if (!node || !item || item.hidden || transforming || node.isDragging()) { hideToolbar(); return; }
    const adjusting = adjustId === item.id;
    for (const part of toolbar.querySelectorAll('[data-for]')) {
      const forWhat = part.dataset.for;
      part.hidden = adjusting
        ? forWhat !== 'adjusting'
        : !(forWhat === item.kind || forWhat === 'item');
    }
    for (const button of toolbar.querySelectorAll('[data-action]')) {
      button.disabled = core.lockBlocks(item, button.dataset.action);
    }
    const lockButton = toolbar.querySelector('[data-action="lock"]');
    if (lockButton) {
      lockButton.setAttribute('aria-pressed', String(Boolean(item.locked)));
      lockButton.setAttribute('aria-label', item.locked ? 'Unlock' : 'Lock');
      lockButton.title = item.locked ? 'Unlock' : 'Lock';
      lockButton.querySelector('i').className = `bi ${item.locked ? 'bi-lock-fill' : 'bi-unlock'}`;
    }
    const rect = node.getClientRect();
    toolbar.hidden = false;
    const width = toolbar.offsetWidth;
    const above = rect.y - toolbar.offsetHeight - (adjusting ? 12 : transformer.rotateAnchorOffset() + 18);
    const below = rect.y + rect.height + 14;
    const left = Math.min(
      Math.max(rect.x + rect.width / 2, stageBox.scrollLeft + width / 2 + 8),
      stageBox.scrollLeft + stageBox.clientWidth - width / 2 - 8);
    // Above the item, unless that is off the top or would cover another item and below would not.
    const covers = (top) => collage.items.some((other) => {
      if (other.id === item.id || other.hidden) return false;
      const box = nodes.get(other.id)?.getClientRect();
      return box && box.x < left + width / 2 && box.x + box.width > left - width / 2
        && box.y < top + toolbar.offsetHeight && box.y + box.height > top;
    });
    // Only where it can be seen: not off the top, and not behind a sheet open over the page.
    const sheetTop = visibleSheetTop();
    const visibleBottom = stageBox.scrollTop + (sheetTop === null ? stageBox.clientHeight : sheetTop - stageBox.getBoundingClientRect().top);
    const height = toolbar.offsetHeight;
    const fits = (t) => t >= stageBox.scrollTop + 6 && t + height <= visibleBottom - 6;
    const top = [above, below].find((t) => fits(t) && !covers(t))
      ?? [above, below].find(fits)
      ?? Math.min(Math.max(above, stageBox.scrollTop + 6), visibleBottom - height - 6);
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
      const join = await askConfirm({
        title: one ? '1 photo is locked' : `${locked.length} photos are locked`,
        message: one
          ? 'Put it in the new layout too, or keep it where it is? If it stays, the layout leaves out the frames it covers.'
          : 'Put them in the new layout too, or keep them where they are? If they stay, the layout leaves out the frames they cover.',
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

  /** Ready-made headings and the plain ways to start text, from the Text panel. */
  // What the four plain starts say on their buttons (the words they add are the placeholders).
  const TEXT_START_LABELS = Object.freeze({ heading: 'A heading', subheading: 'A subheading', line: 'A line of text', hindi: 'हिंदी में लिखें' });

  const TEXT_STARTS = Object.freeze({
    heading: [core.TEXT_PLACEHOLDERS.heading, { font: 'elegant', size: 72 }],
    subheading: [core.TEXT_PLACEHOLDERS.subheading, { font: 'clean', size: 36 }],
    line: [core.TEXT_PLACEHOLDERS.line, { font: 'clean', size: 24, bold: false }],
    hindi: [core.TEXT_PLACEHOLDERS.hindi, { font: 'hindiHand', size: 48 }],
    farewell: ['Farewell', { font: 'script', size: 96, color: '#7a3b1e' }],
    birthday: ['Happy Birthday', { font: 'rounded', size: 80, color: '#c2185b', outline: 4, outlineColor: '#ffffff', curve: 35 }],
    thanks: ['Thank you!', { font: 'hand', size: 80, color: '#1f6f43' }],
    congrats: ['Congratulations', { font: 'elegant', size: 64, color: '#e8c36a', shadow: true }],
    picnic: ['PICNIC DAY', { font: 'poster', size: 96, color: '#ffffff', outline: 6, outlineColor: '#1f5f2c', shadow: true, bold: false }],
    wishes: ['Best wishes', { font: 'script', size: 80, color: '#ffffff', label: '#c62f3a' }],
    hShubh: ['शुभकामनाएँ', { font: 'hindiHand', size: 72, color: '#b0412e' }],
    hVidai: ['विदाई समारोह', { font: 'hindiFormal', size: 64, color: '#0b2a6f' }],
    hJanm: ['जन्मदिन मुबारक', { font: 'hindiHand', size: 72, color: '#c2185b' }],
    hSeva: ['सेवानिवृत्ति की शुभकामनाएँ', { font: 'hindiFormal', size: 48, color: '#e8c36a', shadow: true }],
    hDiwali: ['शुभ दीपावली', { font: 'hindiHand', size: 80, color: '#ffcc4d', glow: true }],
    hDhanya: ['धन्यवाद', { font: 'hindiHand', size: 72, color: '#1f6f43' }]
  });

  /** The Text panel: plain starts and ready-made headings, each shown in its own font. */
  function renderTextPanel() {
    keepingFocus(() => renderTextPanelNow());
  }

  function renderTextPanelNow() {
    const button = (key, big) => {
      const [text, look] = TEXT_STARTS[key];
      const font = core.FONTS[look.font || 'clean'];
      const el = document.createElement('button');
      el.type = 'button';
      el.className = big ? 'clg-heading' : `clg-text-start clg-text-start--${key}`;
      el.dataset.addText = key;
      if (big) el.textContent = text;
      else {
        // A plus sign, and the words in their own font and size, as an "add" button.
        const plus = document.createElement('i');
        plus.className = 'bi bi-plus-lg';
        plus.setAttribute('aria-hidden', 'true');
        const words = document.createElement('span');
        words.textContent = TEXT_START_LABELS[key] || text;
        el.append(plus, words);
        el.setAttribute('aria-label', `Add ${(TEXT_START_LABELS[key] || text).toLowerCase()}`);
      }
      (big ? el : el.lastChild).style.fontFamily = font.css;
      el.style.fontWeight = look.bold === false ? '400' : '700';
      if (big) {
        el.style.color = look.color || '#172033';
        el.style.background = look.label || (look.color === '#ffffff' || look.color === '#ffcc4d' || look.color === '#e8c36a' ? '#1d2b45' : '#f6f2ea');
      }
      return el;
    };
    const starts = root.querySelector('[data-text-starts]');
    starts.replaceChildren(...['heading', 'subheading', 'line', 'hindi'].map((key) => button(key, false)));
    root.querySelector('[data-text-headings]').replaceChildren(...['farewell', 'birthday', 'thanks', 'congrats', 'picnic', 'wishes'].map((key) => button(key, true)));
    root.querySelector('[data-text-headings-hi]').replaceChildren(...['hShubh', 'hVidai', 'hJanm', 'hSeva', 'hDiwali', 'hDhanya'].map((key) => button(key, true)));
  }

  /** A small picture of a background, for its button in the panel. */
  function backgroundPreview(background) {
    const canvas = document.createElement('canvas');
    canvas.width = 96;
    canvas.height = 68;
    const context = canvas.getContext('2d');
    context.scale(96 / 300, 68 / 212);
    let photo = null;
    if (background.type === 'photo') {
      const source = photos.get(background.photo);
      if (source) photo = { source: source.canvas, width: source.canvas.width, height: source.canvas.height };
    }
    draw.drawBackground(context, background, 300, 212, { photo, pixelsPerUnit: 96 / 300 });
    return canvas.toDataURL('image/png');
  }

  const backgroundPreviews = new Map();

  /**
   * A closed collage's photo previews are let go, with the working copies they point at, so
   * opening one collage after another never keeps old photos in memory. Colour, gradient,
   * pattern and texture previews stay: they are small and the same for every collage.
   */
  function forgetPhotoPreviews() {
    for (const [key, preview] of backgroundPreviews) {
      if (preview.source) backgroundPreviews.delete(key);
    }
    backgroundPanelShown = '';
  }

  function backgroundTile(background, label) {
    const key = JSON.stringify(background);
    // A photo's preview is made again only if its working copy changed.
    const source = background.type === 'photo' ? photos.get(background.photo)?.canvas : null;
    const cached = backgroundPreviews.get(key);
    if (!cached || cached.source !== source) backgroundPreviews.set(key, { url: backgroundPreview(background), source });
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'clg-bg-tile';
    button.dataset.background = key;
    button.setAttribute('aria-pressed', String(core.sameBackground(background, collage.background)));
    button.setAttribute('aria-label', label);
    button.title = label;
    const img = document.createElement('img');
    img.alt = '';
    img.src = backgroundPreviews.get(key).url;
    const name = document.createElement('span');
    name.textContent = label;
    button.append(img, name);
    return button;
  }

  /** The Background panel: colours, gradients, patterns, textures and the page's photos. */
  function renderBackgroundPanel() {
    keepingFocus(() => renderBackgroundPanelNow());
  }

  let backgroundPanelShown = '';

  function renderBackgroundPanelNow() {
    const current = collage.background || { type: 'color', value: '#ffffff' };
    // Nothing it shows changed (the background and the page's photos): it is left as it is.
    const shownPhotos = [...new Set(collage.items.filter((item) => item.kind === 'photo').map((item) => item.photo))].slice(0, 9);
    const shown = JSON.stringify([current, shownPhotos.map((id) => [id, photos.get(id)?.missing === true])]);
    if (shown === backgroundPanelShown) return;
    backgroundPanelShown = shown;
    const colours = root.querySelector('[data-bg-colours]');
    colours.replaceChildren(...core.BACKGROUND_COLOURS.map((value) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'clg-swatch';
      button.dataset.background = JSON.stringify({ type: 'color', value });
      button.style.background = value;
      button.title = value;
      button.setAttribute('aria-label', `Colour ${value}`);
      button.setAttribute('aria-pressed', String(current.type === 'color' && current.value === value));
      return button;
    }));
    const pick = root.querySelector('[data-bg-pick]');
    pick.value = current.type === 'color' ? current.value : '#ffffff';
    root.querySelector('[data-bg-gradients]').replaceChildren(...Object.entries(core.GRADIENTS).map(([key, entry]) => backgroundTile({ type: 'gradient', key }, entry.label)));
    root.querySelector('[data-bg-patterns]').replaceChildren(...Object.entries(core.PATTERNS).map(([key, entry]) => backgroundTile({ type: 'pattern', key }, entry.label)));
    root.querySelector('[data-bg-textures]').replaceChildren(...Object.entries(core.TEXTURES).map(([key, entry]) => backgroundTile({ type: 'texture', key }, entry.label)));
    const onPage = [...new Set(collage.items.filter((item) => item.kind === 'photo').map((item) => item.photo))].slice(0, 9);
    // Each photo by its own name, so the user knows which one goes behind the page.
    root.querySelector('[data-bg-photos]').replaceChildren(...onPage.map((id, n) => backgroundTile({ type: 'photo', photo: id }, `${photos.get(id)?.name || `Photo ${n + 1}`}, blurred`)));
    root.querySelector('[data-bg-photos-none]').hidden = onPage.length > 0;
  }

  function setBackground(background, record = true) {
    collage.background = background;
    if (record) commit();
    else backLayer.batchDraw();
    if (panel === 'background') renderBackgroundPanel();
  }

  let stickerGroup = 'celebration';
  let stickerGroupChosen = false;
  // 'loading', 'ready' or 'failed': whether the crests and logos have come from PRISM.
  let unitState = 'loading';

  const STICKER_NONE = {
    search: 'No sticker matches. Try another word.',
    loading: 'Getting the crests and logos from PRISM…',
    failed: 'The crests and logos could not be read from PRISM. The other stickers still work.',
    empty: "An administrator can add your unit's crests and logos."
  };

  /** Asks PRISM for the crests and logos once; the other stickers never wait for them. */
  function loadUnitStickers() {
    if (!library) { unitState = 'failed'; return; }
    library.loadStickers().then((list) => {
      const count = stickers.setUnit(list);
      unitState = 'ready';
      if (count > 0 && !stickerGroupChosen) stickerGroup = 'crests';
    }).catch(() => {
      unitState = 'failed';
    }).finally(() => {
      renderStickerPanel();
      // A collage opened before the list arrived draws its crests at their listed address now.
      if (!views.editor.hidden) syncPage();
    });
  }

  /** The Stickers panel: search, groups, and the stickers of the group or the search. */
  function renderStickerPanel() {
    keepingFocus(() => renderStickerPanelNow());
  }

  function renderStickerPanelNow() {
    const box = root.querySelector('[data-sticker-grid]');
    const chips = root.querySelector('[data-sticker-groups]');
    const query = root.querySelector('[data-sticker-search]').value.trim();
    const groups = stickers.groups();
    chips.replaceChildren(...groups.map((group) => {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'clg-chip';
      chip.dataset.stickerGroup = group.key;
      chip.textContent = group.label;
      chip.setAttribute('aria-pressed', String(!query && group.key === stickerGroup));
      return chip;
    }));
    const list = query ? stickers.search(query) : (groups.find((group) => group.key === stickerGroup) || groups[0]).items;
    box.replaceChildren(...list.map((item) => {
      const entry = stickers.find(item.id);
      const button = document.createElement('button');
      button.type = 'button';
      button.className = entry.aspect >= 1.6 ? 'clg-sticker clg-sticker--wide' : 'clg-sticker';
      button.dataset.addSticker = item.id;
      const name = entry.label || item.words.split(' ')[0];
      button.title = name;
      button.setAttribute('aria-label', `Add ${name}`);
      const img = document.createElement('img');
      img.alt = '';
      img.loading = 'lazy';
      img.src = stickerSource({ sticker: item.id, colour: '' });
      button.appendChild(img);
      return button;
    }));
    const none = root.querySelector('[data-sticker-none]');
    none.hidden = list.length > 0;
    if (!list.length) {
      none.textContent = query ? STICKER_NONE.search
        : stickerGroup !== 'crests' ? STICKER_NONE.search
          : unitState === 'ready' ? STICKER_NONE.empty : STICKER_NONE[unitState];
    }
  }

  function addSticker(id) {
    const entry = stickers.find(id);
    if (!entry) return;
    const width = entry.kind === 'drawn' ? 180 : entry.kind === 'unit' ? 160 : 110;
    // Where it covers nothing yet, so stickers added one after another do not hide each other.
    const spot = core.freeSpot(collage.items, collage.size, width / 2, width / entry.aspect / 2);
    const place = { x: spot.x, y: spot.y, width, aspect: entry.aspect };
    const item = core.stickerItem(newItemId(), id, place, entry.kind === 'drawn' ? { colour: entry.colour } : {});
    collage.items.push(item);
    selectedId = item.id;
    propsTab = 'settings';
    stopAdjust(false);
    commit();
    announce('Sticker added.');
  }

  function addText(key) {
    const start = TEXT_STARTS[key];
    if (!start) return;
    const item = core.textItem(newItemId(), start[0], { x: core.PAGE_WIDTH / 2, y: pageHeight() / 2 }, start[1]);
    // Measured first, then put where it covers nothing yet (not on top of the heading before it).
    layoutTextItem(item);
    const spot = core.freeSpot(collage.items, collage.size, item.width / 2, core.itemHeight(item) / 2);
    item.x = spot.x;
    item.y = spot.y;
    collage.items.push(item);
    selectedId = item.id;
    propsTab = 'settings';
    stopAdjust(false);
    commit();
    announce('Text added. Type the words in the Words box.');
    window.requestAnimationFrame(focusTextField);
  }

  // The one Undo offered on the screen after a delete: good only while nothing else has changed.
  let undoOffer = null;

  function offerUndo(text) {
    undoOffer?.dismiss();
    const version = changes.version;
    announce(`${text} Undo is on the screen; Ctrl+Z also brings it back.`);
    if (!window.PrismToast) return;
    undoOffer = window.PrismToast.show({
      tone: 'success',
      text,
      action: {
        label: 'Undo',
        run: () => {
          if (changes.version === version) act('undo');
          else announce('Something else has changed since. Use Undo in the toolbar to step back.');
        }
      }
    });
  }

  function act(action) {
    const item = selectedItem();
    if (core.lockBlocks(item, action)) { announce('It is locked. Unlock it first.'); return; }
    switch (action) {
      case 'home': goHome(); break;
      case 'choose': fileInput.click(); break;
      // Undo and Redo end moving a photo inside its frame only when there is a step to take.
      case 'undo': { const back = changes.undo(); if (back) { stopAdjust(false); restore(back); } else updateUndo(); break; }
      case 'redo': { const again = changes.redo(); if (again) { stopAdjust(false); restore(again); } else updateUndo(); break; }
      case 'zoom-in': setZoom(zoom * 1.25); break;
      case 'zoom-out': setZoom(zoom / 1.25); break;
      case 'zoom-fit': setZoom(1); break;
      case 'alert-close': clearAlert(); break;
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
      case 'lock':
        if (item) {
          item.locked = !item.locked;
          if (item.locked) lockedNow(item);
          commit();
          announce(item.locked ? 'Locked.' : 'Unlocked.');
        }
        break;
      case 'hide':
        if (item) {
          item.hidden = !item.hidden;
          commit();
          announce(item.hidden ? 'Hidden. Show it again from Layers.' : 'Shown.');
        }
        break;
      case 'preview':
        // Preview is the page alone: no message and no Undo offer over it.
        clearAlert();
        undoOffer?.dismiss();
        startPreview();
        break;
      case 'edit-props': setPropsOpen(true, 'settings'); break;
      case 'toggle-props': setPropsOpen(!(body.classList.contains('is-props-open') && propsTab === 'layers'), 'layers'); break;
      case 'close-props': setPropsOpen(false); break;
      case 'close-drawer': setPanel(null); break;
      case 'preview-exit': stopPreview(); break;
      case 'delete':
        if (item) {
          // A photo in a layout leaves its frame empty, so the layout keeps its shape; anything
          // else (and an empty frame) goes.
          const leavesFrame = item.kind === 'photo' && Boolean(collage.layout);
          if (leavesFrame) collage.items[collage.items.indexOf(item)] = core.emptyFrame(item);
          else collage.items = collage.items.filter((i) => i !== item);
          selectedId = leavesFrame ? item.id : null;
          stopAdjust(false);
          commit();
          offerUndo(leavesFrame ? 'Photo taken out; its frame is empty.' : `${core.layerName(item)} deleted.`);
          // Focus goes on to what is left, in the Layers list, rather than to the page.
          if (!leavesFrame) window.requestAnimationFrame(() => (props.querySelector('[data-layer-select]') || props.querySelector('[data-props-tab="layers"]'))?.focus());
        }
        break;
      case 'forward': case 'backward': case 'front': case 'back':
        if (item) { collage.items = core.reorder(collage.items, item.id, action); commit(); }
        break;
      case 'rotate-left':
      case 'rotate-right':
      case 'straighten':
        if (action === 'rotate-left') changeSelected((i) => { i.angle = core.normaliseAngle(i.angle - 15); });
        else if (action === 'rotate-right') changeSelected((i) => { i.angle = core.normaliseAngle(i.angle + 15); });
        else changeSelected((i) => { i.angle = 0; });
        break;
      case 'adjust': if (item?.kind === 'photo') startAdjust(item.id); break;
      case 'adjust-done': stopAdjust(); break;
      case 'replace': if (isFrame(item)) { setTarget(item.id); setPanel('photos'); } break;
      case 'fill-slot': if (item?.kind === 'slot') chooseFor(item.id); break;
      case 'replace-cancel': setTarget(null); break;
      case 'save-open': saveAs.open(); break;
      case 'credits': root.querySelector('[data-credits]').showModal(); break;
      case 'drafts-all': draftsExpanded = !draftsExpanded; renderDrafts(); break;
      case 'keep-draft':
        // Asked again: keeping it may still mean deleting the oldest draft to make room.
        session.keepAgain();
        break;
      case 'library-open': libraryPicker.open(); break;
      case 'library-close': libraryPicker.close(); break;
      case 'library-more': libraryPicker.more(); break;
      case 'library-add': libraryPicker.add(); break;
      case 'credits-close': root.querySelector('[data-credits]').close(); break;
      case 'save-close': saveAs.close(); break;
      case 'save-go': saveAs.save(); break;
      case 'draft-undo': undoDeleteDraft(); break;
      case 'reset-look':
        changeSelected((i) => { Object.assign(i, { look: 'none', bright: 100, contrast: 100, warmth: 0, flip: false }); });
        break;
      case 'fit-photo':
        changeSelected((i) => {
          const photo = photos.get(i.photo);
          // A grey stand-in has no shape of its own to fit.
          if (!photo || photo.missing) return;
          // The picture area takes the photo's own shape, inside whatever frame it has.
          i.aspect = core.outerAspectFor(i, photo.aspect);
          i.crop = { ...core.FULL_CROP };
        });
        break;
      case 'casual':
        if (!collage.items.some(isFrame)) break;
        collage = core.makeCasual(collage);
        stopAdjust(false);
        commit();
        offerUndo('Photos tilted and scattered. Frames and shapes you chose are kept.');
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
    // While a draft opens (or room is being made for a new one) the start screen waits.
    if (views.start.inert && views.start.contains(event.target)) return;
    const openOne = event.target.closest('[data-open-draft]');
    if (openOne) { openDraft(openOne.dataset.openDraft); return; }
    const deleteOne = event.target.closest('[data-delete-draft]');
    if (deleteOne) { session.remove(deleteOne.dataset.deleteDraft); return; }
    const start = event.target.closest('[data-start-size]');
    if (start) { startCollage(start.dataset.startSize); return; }
    const startLayout = event.target.closest('[data-start-layout]');
    if (startLayout) { startCollage(core.DEFAULT_SIZE, startLayout.dataset.startLayout); return; }
    const layoutChoice = event.target.closest('[data-layout]');
    if (layoutChoice) { applyLayoutKey(layoutChoice.dataset.layout); return; }
    const align = event.target.closest('[data-align]');
    if (align && !align.disabled) {
      changeSelected((target) => Object.assign(target, core.alignItem(target, collage.size, align.dataset.align)));
      return;
    }
    const layerPick = event.target.closest('[data-layer-select]');
    if (layerPick) {
      select(layerPick.dataset.layerSelect, { fromLayers: true });
      const name = layerPick.querySelector('.clg-layer__name')?.textContent || '';
      announce(`Selected ${name}. Arrow keys move it; Delete removes it.`);
      props.querySelector(`[data-layer-select="${layerPick.dataset.layerSelect}"]`)?.focus();
      return;
    }
    const layerHide = event.target.closest('[data-layer-hide]');
    if (layerHide) {
      const target = itemById(layerHide.dataset.layerHide);
      if (target) { target.hidden = !target.hidden; commit(); }
      return;
    }
    const layerLock = event.target.closest('[data-layer-lock]');
    if (layerLock) {
      const target = itemById(layerLock.dataset.layerLock);
      if (target) { target.locked = !target.locked; if (target.locked) lockedNow(target); commit(); }
      return;
    }
    const backgroundChoice = event.target.closest('[data-background]');
    if (backgroundChoice) {
      setBackground(JSON.parse(backgroundChoice.dataset.background));
      announce(`Background: ${backgroundChoice.getAttribute('aria-label')}. Ctrl+Z puts the last one back.`);
      return;
    }
    const stickerButton = event.target.closest('[data-add-sticker]');
    if (stickerButton) { addSticker(stickerButton.dataset.addSticker); return; }
    const groupChip = event.target.closest('[data-sticker-group]');
    if (groupChip) {
      stickerGroup = groupChip.dataset.stickerGroup;
      stickerGroupChosen = true;
      root.querySelector('[data-sticker-search]').value = '';
      renderStickerPanel();
      return;
    }
    const textStart = event.target.closest('[data-add-text]');
    if (textStart) { addText(textStart.dataset.addText); return; }
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
    refreshTitle();
  });

  // The panels: the left drawer (layouts and the rest) and the settings panel with its Settings
  // and Layers tabs (collage-panels.js).
  const {
    setPropsOpen, setPanel, renderLayoutPanel, renderProps, focusTextField
  } = window.PrismCollagePanels.createPanels({
    app, $, root, core, draw, stickers, sizeSelect, body, layoutGrid, props, changes, photos,
    selectedItem, isFrame, keepingFocus, layout, stickerSource, clampedCrop, syncPage, select,
    zoomCrop, flushNudge, commit, FRAME_WIDTHS, LOCKED_CONTROLS, changeSelected,
    renderBackgroundPanel, offerUndo
  });
  // Photos: choosing, dropping and pasting them, reading them in, the tray, and From PRISM Photos
  // (collage-tray.js).
  const {
    setTarget, chooseFor, readPhoto, missingPhoto, libraryPicker, renderTray, keptPhotos
  } = window.PrismCollageTray.createTray({
    app, $, root, core, library, views, fileInput, stageBox, tray, photoCount, replaceBanner,
    alertBox, changes, photos, nextId, newItemId, itemById, selectedItem, pageHeight, firstSlot,
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
    app, core, views, stageBox, props, photos, itemById, selectedItem, announce, setShift,
    stopPreview, setZoom, syncPage, select, stopAdjust, zoomCrop, holdEdit, changeSelected, act,
    focusTextField, setTarget
  });
  // ---------------------------------------------------------------------------
  // Start
  // ---------------------------------------------------------------------------

  renderStart();
  renderTextPanel();
  renderStickerPanel();
  loadUnitStickers();
  root.querySelector('[data-sticker-search]').addEventListener('input', renderStickerPanel);
  root.querySelector('[data-bg-pick]').addEventListener('input', (event) => setBackground({ type: 'color', value: event.target.value }, false));
  root.querySelector('[data-bg-pick]').addEventListener('change', (event) => setBackground({ type: 'color', value: event.target.value }));
  renderDrafts();
  updateUndo();
  showView('start');
})();
