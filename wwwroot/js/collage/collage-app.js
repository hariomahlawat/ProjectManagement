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
  if (!root || !core || !Konva || !draw || !stickers) return;

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
  // A two-finger zoom in progress: no item is dragged or panned meanwhile.
  let pinch = null;
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
  // Edits still going on (typing, sliders, nudges): recorded once they pause (collage-edits.js).
  const edits = window.PrismCollageEdits.createHeldEdits({ record: () => recordQuietly(), keep: () => scheduleDraft() });
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
  const firstSlot = () => collage.items.find((item) => item.kind === 'slot') || null;
  const isFrame = (item) => item && (item.kind === 'photo' || item.kind === 'slot');

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
    edits.drop();
    window.clearTimeout(draftTimer);
    draftPending = false;
    photos.clear();
    storedPhotos.clear();
    forgetPhotoPreviews();
    selectedId = null;
    adjustId = null;
    targetId = null;
    zoom = 1;
  }

  function startCollage(sizeKey, layoutKey) {
    openRequest += 1;
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
    nameInput.value = '';
    draft.started = Date.now();
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
    // The editor is locked while the draft is written, so no change can slip in after the
    // last snapshot and be dropped when the editor is cleared.
    views.editor.inert = true;
    let kept = true;
    try {
      // Anything still waiting (a nudge, a wheel zoom, a change made just before) is saved too.
      for (let round = 0; round < 5; round += 1) {
        flushNudge();
        kept = await saveDraftNow();
        if (!draftPending && !edits.pending) break;
      }
    } finally {
      views.editor.inert = false;
      leaving = false;
    }
    // Only when the draft cannot be kept is the user asked: a collage with photos, or a saved
    // draft whose change (even removing its last photo) was not written.
    if (!kept && (hasContent() || draft?.created)) {
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

  // Phones rarely ask before a page goes away, so the draft is written whenever the page is
  // hidden (another app, another tab, the screen locked) or put away.
  function saveOnLeaving() {
    if (views.editor.hidden || !draft) return;
    flushNudge();
    if (draftPending) saveDraftNow();
  }
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') saveOnLeaving();
  });
  window.addEventListener('pagehide', saveOnLeaving);

  window.addEventListener('beforeunload', (event) => {
    if (views.editor.hidden || !draft) return;
    // A nudge or wheel zoom still waiting counts as a change: recorded now, it marks the draft
    // for saving, so the page asks while it is written.
    flushNudge();
    // Only a page that never had a photo has nothing to lose; a saved draft whose last photo
    // was just removed still has that change to write.
    if (!draft.created && !hasContent()) return;
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
  const keepDraftButton = $('[data-action="keep-draft"]');
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
    draftStatus.title = warning ? (draftProblem() || text) : '';
    draftStatus.classList.toggle('is-warning', warning);
    // A collage left out of the drafts can be kept again at any time.
    if (keepDraftButton) keepDraftButton.hidden = !draft?.off;
    // On a phone the status has room only for its mark, so the reason is shown once in full.
    if (warning && text !== draftWarningShown && window.matchMedia?.('(max-width: 1100px)').matches) {
      showAlert(draftProblem() || text);
    }
    draftWarningShown = warning ? text : '';
  }
  let draftWarningShown = '';

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
      // A draft starts once it has a photo or some text; an empty page is not worth keeping.
      if (!draft.created && !hasContent()) return true;
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
          title: collageTitle(),
          // Not named by the user: the title follows the first words, and the box stays empty.
          autoTitle: !nameInput.value.trim(),
          started: forDraft.started || Date.now(),
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

  let draftsExpanded = false;

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
    // One row of drafts unless asked for all, so starting a new collage stays in view.
    // As many as fit across the grid (cards are at least 200 pixels wide).
    const row = Math.max(1, Math.floor(((grid.clientWidth || section.parentElement?.clientWidth || 800) + 12) / 212));
    const allButton = $('[data-action="drafts-all"]');
    if (allButton) {
      allButton.hidden = shown.length <= row;
      allButton.textContent = draftsExpanded ? 'Show fewer drafts' : `Show all ${shown.length} drafts`;
      allButton.setAttribute('aria-expanded', String(draftsExpanded));
    }
    for (const item of draftsExpanded ? shown : shown.slice(0, row)) {
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

  // Counts draft opens and new collages: an open that is overtaken by another open, or by a
  // new collage, while the browser is still reading it is dropped instead of replacing it.
  let openRequest = 0;

  async function openDraft(id) {
    const request = ++openRequest;
    const stillWanted = () => request === openRequest && !views.start.hidden;
    let record;
    let stored;
    try {
      record = await drafts.get(id);
      stored = record ? await drafts.photosOf(id) : [];
    } catch {
      if (stillWanted()) showAlert('The draft could not be opened: this browser did not give it back.');
      return;
    }
    if (!stillWanted()) return;
    if (!record || record.deletedAt) { renderDrafts(); return; }
    resetEditor();
    const forCollage = collageSession;
    busy(stored.length ? `Opening the draft and its ${stored.length} photo${stored.length === 1 ? '' : 's'}…` : 'Opening the draft…');
    let unreadable = 0;
    try {
      // One photo at a time, as when they were first added.
      for (const entry of stored) {
        let photo;
        try {
          photo = await readPhoto({ file: entry.file, name: entry.name }, entry.id);
        } catch {
          // Kept as a grey stand-in with its file, so the draft keeps the photo's place, crop and
          // caption, and the photo comes back when the draft is opened again on a browser that
          // can read it. Save leaves it out and says so.
          photo = missingPhoto(entry);
          unreadable += 1;
        }
        if (forCollage !== collageSession) return;
        photos.set(photo.id, photo);
        storedPhotos.add(photo.id);
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
    nameInput.value = record.autoTitle ? '' : record.title;
    draft.started = record.started || record.saved;
    refreshTitle();
    sizeSelect.value = collage.size;
    setDraftStatus('Draft saved in this browser');
    showView('editor');
    setPanel('photos');
    syncAll();
    if (unreadable) showAlert(`${unreadable === 1 ? 'One photo' : `${unreadable} photos`} of this draft could not be opened just now and ${unreadable === 1 ? 'shows' : 'show'} grey. ${unreadable === 1 ? 'It keeps its' : 'They keep their'} place in the draft. Close other tabs and open the draft again; Save leaves ${unreadable === 1 ? 'it' : 'them'} out until then.`);
    announce(`Draft ${record.title} opened.`);
  }

  // ---------------------------------------------------------------------------
  // The page (Konva)
  // ---------------------------------------------------------------------------

  const stage = new Konva.Stage({ container: stageHost, width: 10, height: 10 });
  // The page and its background are on a layer of their own, under the items, so a busy
  // texture is drawn again only when the background or the page changes.
  const backLayer = new Konva.Layer({ listening: false });
  const layer = new Konva.Layer();
  stage.add(backLayer, layer);
  const backWorld = new Konva.Group();
  const pageRect = new Konva.Rect({
    x: 0, y: 0, width: core.PAGE_WIDTH, height: 10, fill: '#ffffff', name: 'page',
    shadowColor: 'rgba(16, 24, 40, 0.28)', shadowBlur: 28, shadowOffsetY: 6, shadowForStrokeEnabled: false
  });
  const backgroundShape = new Konva.Shape({
    x: 0, y: 0, width: core.PAGE_WIDTH, height: 10,
    sceneFunc: (context) => {
      const pixelsPerUnit = scale * (backLayer.getCanvas().getPixelRatio?.() || 1);
      draw.drawBackground(context._context, collage.background, core.PAGE_WIDTH, pageHeight(), { photo: backgroundPhoto(), pixelsPerUnit });
    }
  });
  backWorld.add(pageRect, backgroundShape);
  backLayer.add(backWorld);
  // `world` is the page: 1,000 units wide, scaled and centred on the work surface.
  const world = new Konva.Group();
  const itemsGroup = new Konva.Group();
  const guides = new Konva.Group({ listening: false });
  world.add(itemsGroup, guides);
  layer.add(world);

  /** The photo behind the page when the background is a blurred photo: its working copy. */
  function backgroundPhoto() {
    if (collage.background?.type !== 'photo') return null;
    const photo = photos.get(collage.background.photo);
    return photo ? { source: photo.canvas, width: photo.canvas.width, height: photo.canvas.height } : null;
  }
  const nodes = new Map();

  const accent = () => getComputedStyle(document.body).getPropertyValue('--bs-primary').trim() || '#0d6efd';
  const ADJUST_COLOUR = '#f2a200';
  const LOCKED_COLOUR = '#8f9aab';
  const NORMAL_SNAPS = [0, 45, 90, 135, 180, 225, 270, 315];
  const SHIFT_SNAPS = Array.from({ length: 24 }, (_, i) => i * 15);
  // Corners keep the shape; the middle of each side changes the frame's shape.
  const CORNER_ANCHORS = ['top-left', 'top-right', 'bottom-left', 'bottom-right'];
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
    // On a phone an open sheet covers the lower half: the page fits in what is left above it.
    const box = getComputedStyle(stageBox);
    const usableHeight = stageBox.clientHeight - (parseFloat(box.paddingTop) || 0) - (parseFloat(box.paddingBottom) || 0);
    const fit = Math.min((stageBox.clientWidth - 2 * pad) / core.PAGE_WIDTH, (usableHeight - 2 * pad) / height);
    scale = Math.max(0.05, fit * zoom);
    const pageW = core.PAGE_WIDTH * scale;
    const pageH = height * scale;
    const stageW = Math.max(stageBox.clientWidth, pageW + 2 * pad);
    const stageH = Math.max(usableHeight, pageH + 2 * pad);
    stage.size({ width: stageW, height: stageH });
    stageHost.style.width = `${stageW}px`;
    stageHost.style.height = `${stageH}px`;
    world.position({ x: (stageW - pageW) / 2, y: (stageH - pageH) / 2 });
    world.scale({ x: scale, y: scale });
    pageRect.height(height);
    backgroundShape.height(height);
    backWorld.position(world.position());
    backWorld.scale(world.scale());
    backLayer.batchDraw();
    itemsGroup.clip({ x: 0, y: 0, width: core.PAGE_WIDTH, height });
    zoomValue.textContent = `${Math.round(zoom * 100)}%`;
    layer.batchDraw();
    placeToolbar();
  }

  new ResizeObserver(() => layout()).observe(stageBox);

  let previewing = false;

  /** Shows the page alone, without panels or handles; Esc or Exit preview comes back. */
  function startPreview() {
    previewing = true;
    stopAdjust(false);
    select(null);
    document.body.classList.add('clg-previewing');
    layer.listening(false);
    root.querySelector('[data-preview-exit]').hidden = false;
    root.querySelector('[data-preview-exit]').focus();
    window.requestAnimationFrame(layout);
    window.setTimeout(layout, 50);
  }

  function stopPreview() {
    if (!previewing) return;
    previewing = false;
    document.body.classList.remove('clg-previewing');
    layer.listening(true);
    root.querySelector('[data-preview-exit]').hidden = true;
    window.requestAnimationFrame(layout);
    window.setTimeout(layout, 50);
  }

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
    if (item.kind === 'text') layoutTextItem(item);
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
    // A photo is drawn from the model by drawPhotoNode(); it only needs drawing again.
  }

  /**
   * Draws a photo node: its frame, shape, look and the part of the photo its frame shows, with
   * the same drawing Save uses, so the saved picture is what the page shows.
   */
  function drawPhotoNode(context, shape) {
    const item = itemById(shape.id());
    const photo = item && photos.get(item.photo);
    if (!photo) return;
    const pixelsPerUnit = scale * (layer.getCanvas().getPixelRatio?.() || 1);
    draw.drawPhoto(context._context, item, photo.canvas, photo.canvas.width, photo.canvas.height, pixelsPerUnit);
  }

  // Text is measured with the fonts PRISM ships (wwwroot/fonts/collage); a canvas measures it.
  const measureContext = document.createElement('canvas').getContext('2d');
  const measureText = (text, font) => {
    measureContext.font = font;
    if ('letterSpacing' in measureContext) measureContext.letterSpacing = '0px';
    return measureContext.measureText(text).width;
  };
  const textLayouts = new Map();
  const fontsAsked = new Set();

  /**
   * Lays a text item out and keeps its box (width and shape) in step with its words, so moving,
   * snapping and the handles fit the text. A font not loaded yet is asked for; the text is laid
   * out again once it arrives.
   */
  function layoutTextItem(item) {
    const font = core.fontOf(item, 40);
    if (document.fonts && !fontsAsked.has(font)) {
      fontsAsked.add(font);
      document.fonts.load(font, item.text || 'Aअ').then(() => { syncPage(); }).catch(() => {});
    }
    const layout = core.layoutText(item, measureText);
    item.width = layout.width;
    item.aspect = layout.width / Math.max(1, layout.height);
    textLayouts.set(item.id, layout);
    return layout;
  }

  /** Waits for every font the collage's text uses, so Save measures and draws the real fonts. */
  async function fontsReady() {
    if (!document.fonts) return;
    const fonts = new Set(collage.items.filter((item) => item.kind === 'text').map((item) => core.fontOf(item, 40)));
    // A polaroid's caption is written in Caveat (Kalam for Hindi).
    if (collage.items.some((item) => item.frame === 'polaroid' && item.caption)) fonts.add(`40px ${draw.CAPTION_FONT}`);
    await Promise.all([...fonts].map((font) => document.fonts.load(font, 'Aअ').catch(() => null)));
  }

  // The caption font is fetched once; captions drawn before it arrived are drawn again.
  if (document.fonts) document.fonts.load(`40px ${draw.CAPTION_FONT}`, 'Aअ').then(() => { if (!views.editor.hidden) syncPage(); }).catch(() => {});

  // Sticker pictures: emoji drawings from PRISM's own files, drawn stickers from their SVG in
  // the chosen colour. Each is loaded once; a sticker drawn before its picture arrived is drawn
  // again when it does.
  const stickerBase = root.dataset.stickerBase || '/img/collage/stickers/twemoji/';
  const stickerImages = new Map();

  function stickerSource(item) {
    const entry = stickers.find(item.sticker);
    if (!entry) return null;
    if (entry.kind === 'emoji') return `${stickerBase}${entry.code}.svg`;
    if (entry.kind === 'unit') return entry.url;
    return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(stickers.drawnSvg(entry.key, item.colour || entry.colour))}`;
  }

  /** The sticker's picture once it has loaded (a promise for Save), or null while loading. */
  function stickerImage(item, wait = false) {
    const src = stickerSource(item);
    if (!src) return wait ? Promise.resolve(null) : null;
    let entry = stickerImages.get(src);
    // A picture that failed (PRISM restarting, a dropped connection) is tried again: by Save at
    // once, by the page at most every ten seconds.
    if (entry?.failedAt && (wait || Date.now() - entry.failedAt > STICKER_RETRY_MS)) {
      stickerImages.delete(src);
      entry = null;
    }
    if (!entry) {
      const image = new Image();
      const made = { image, ready: false, failedAt: 0, done: null };
      made.done = new Promise((resolve) => {
        image.onload = () => { made.ready = true; resolve(image); if (!views.editor.hidden) layer.batchDraw(); };
        image.onerror = () => { made.failedAt = Date.now(); resolve(null); };
      });
      image.src = src;
      stickerImages.set(src, made);
      trimStickerImages();
      entry = made;
    }
    if (wait) return entry.done;
    return entry.ready ? entry.image : null;
  }

  const STICKER_RETRY_MS = 10000;
  // The picture each sticker showed last, kept on screen while a new colour loads.
  const lastStickerImage = new Map();

  /** Recoloured drawn stickers make a picture per colour tried; only the newest are kept. */
  function trimStickerImages() {
    if (stickerImages.size <= 80) return;
    // A picture a sticker on the page uses now is never let go, however many there are, or it
    // would be loaded again on every draw.
    const inUse = new Set(collage.items.filter((item) => item.kind === 'sticker').map((item) => stickerSource(item)));
    for (const key of stickerImages.keys()) {
      if (stickerImages.size <= 60) break;
      if (key.startsWith('data:') && !inUse.has(key)) stickerImages.delete(key);
    }
  }

  function drawStickerNode(context, shape) {
    const item = itemById(shape.id());
    if (!item) return;
    const ready = stickerImage(item);
    if (ready) lastStickerImage.set(item.id, ready);
    const image = ready || lastStickerImage.get(item.id);
    if (!image) return;
    const pixelsPerUnit = scale * (layer.getCanvas().getPixelRatio?.() || 1);
    draw.drawSticker(context._context, item, image, pixelsPerUnit);
  }

  function drawTextNode(context, shape) {
    const item = itemById(shape.id());
    if (!item) return;
    const layout = textLayouts.get(item.id) || layoutTextItem(item);
    const pixelsPerUnit = scale * (layer.getCanvas().getPixelRatio?.() || 1);
    draw.drawText(context._context, item, layout, pixelsPerUnit);
  }

  /** The area that takes clicks: the whole frame. */
  function hitPhotoNode(context, shape) {
    context.beginPath();
    context.rect(0, 0, shape.width(), shape.height());
    context.closePath();
    context.fillStrokeShape(shape);
  }

  function makeNode(item) {
    const node = item.kind === 'slot'
      ? makeSlotNode()
      : new Konva.Shape({ name: 'item', draggable: true, sceneFunc: item.kind === 'text' ? drawTextNode : item.kind === 'sticker' ? drawStickerNode : drawPhotoNode, hitFunc: hitPhotoNode });
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
      else if (target?.kind === 'text') focusTextField();
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
      if (target.kind === 'text') {
        const [low, high] = core.TEXT_LIMITS.size;
        target.size = Math.round(Math.min(high, Math.max(low, target.size * Math.abs(node.scaleX()))));
        target.angle = core.normaliseAngle(node.rotation());
        target.x = node.x();
        target.y = node.y();
        commit();
        return;
      }
      if (target.kind === 'sticker') {
        const [low, high] = core.STICKER_LIMITS.width;
        target.width = Math.min(high, Math.max(low, target.width * Math.abs(node.scaleX())));
        target.angle = core.normaliseAngle(node.rotation());
        target.x = node.x();
        target.y = node.y();
        commit();
        return;
      }
      const width = core.clampWidth(target.width * Math.abs(node.scaleX()));
      const height = core.itemHeight(target) * Math.abs(node.scaleY());
      target.width = width;
      target.aspect = width / Math.max(1, height);
      // A framed photo squeezed flat keeps a usable picture area inside its edges.
      if (target.kind === 'photo') target.aspect = core.limitFrameAspect(target);
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
    return photo ? core.clampCrop(photo.aspect, core.innerAspect(item), item.crop) : item.crop;
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
      node.draggable(adjustId !== item.id && !item.locked && !pinch);
      node.visible(!item.hidden);
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
    backLayer.batchDraw();
    layer.batchDraw();
    placeToolbar();
  }

  function attachTransformer() {
    // A hidden item keeps its settings and Layers row, but no handles on the page.
    const node = selectedId && !itemById(selectedId)?.hidden && nodes.get(selectedId);
    const adjusting = Boolean(adjustId && adjustId === selectedId);
    transformer.nodes(node ? [node] : []);
    const isText = Boolean(node && ['text', 'sticker'].includes(itemById(selectedId)?.kind));
    const locked = Boolean(node && itemById(selectedId)?.locked);
    transformer.enabledAnchors(adjusting || locked ? [] : isText ? CORNER_ANCHORS : ALL_ANCHORS);
    transformer.rotateEnabled(!adjusting && !locked);
    transformer.borderStroke(adjusting ? ADJUST_COLOUR : locked ? LOCKED_COLOUR : accent());
    transformer.borderDash(adjusting || locked ? [6, 4] : []);
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
    return nameInput.value.trim() || core.autoTitle(collage.items, draft?.started);
  }

  /** Shows the collage's own title where no name was typed: in the name box and the tab. */
  function refreshTitle() {
    nameInput.placeholder = core.autoTitle(collage.items, draft?.started);
    if (!views.editor.hidden || draft) document.title = `${collageTitle()} · Collage Studio | PRISM ERP`;
  }

  function commit() {
    history.record(collage);
    refreshTitle();
    if (panel === 'background') renderBackgroundPanel();
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

  /** Ready-made headings and the plain ways to start text, from the Text panel. */
  const TEXT_STARTS = Object.freeze({
    heading: ['Your heading', { font: 'elegant', size: 72 }],
    subheading: ['A subheading', { font: 'clean', size: 36 }],
    line: ['A line of text', { font: 'clean', size: 24, bold: false }],
    hindi: ['यहाँ लिखें', { font: 'hindiHand', size: 48 }],
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
      el.className = big ? 'clg-heading' : 'btn btn-outline-secondary btn-sm clg-text-start';
      el.dataset.addText = key;
      el.textContent = text;
      el.style.fontFamily = font.css;
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
    root.querySelector('[data-bg-photos]').replaceChildren(...onPage.map((id, n) => backgroundTile({ type: 'photo', photo: id }, `Photo ${n + 1}, blurred`)));
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
      button.className = 'clg-sticker';
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
    const count = collage.items.filter((i) => i.kind === 'sticker').length;
    const width = entry.kind === 'drawn' ? 180 : entry.kind === 'unit' ? 160 : 110;
    // Spread round the page, so stickers added one after another do not hide each other.
    const spots = [[0, 0], [-0.25, -0.25], [0.25, -0.25], [-0.25, 0.25], [0.25, 0.25], [0, -0.32], [-0.35, 0], [0.35, 0], [0, 0.32]];
    const [fx, fy] = spots[count % spots.length];
    const place = { x: core.PAGE_WIDTH * (0.5 + fx), y: pageHeight() * (0.5 + fy), width, aspect: entry.aspect };
    const item = core.stickerItem(newItemId(), id, place, entry.kind === 'drawn' ? { colour: entry.colour } : {});
    collage.items.push(item);
    selectedId = item.id;
    stopAdjust(false);
    commit();
    announce('Sticker added.');
  }

  function addText(key) {
    const start = TEXT_STARTS[key];
    if (!start) return;
    const count = collage.items.filter((i) => i.kind === 'text').length;
    const item = core.textItem(newItemId(), start[0], { x: core.PAGE_WIDTH / 2, y: pageHeight() * (0.2 + (count % 4) * 0.15) }, start[1]);
    collage.items.push(item);
    selectedId = item.id;
    stopAdjust(false);
    commit();
    announce('Text added. Type the words in the Words box.');
    window.requestAnimationFrame(focusTextField);
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
      case 'lock':
        if (item) {
          item.locked = !item.locked;
          if (item.locked && adjustId === item.id) stopAdjust(false);
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
      case 'preview': startPreview(); break;
      case 'edit-props': setPropsOpen(true); break;
      case 'toggle-props': setPropsOpen(!body.classList.contains('is-props-open')); break;
      case 'close-props': setPropsOpen(false); break;
      case 'close-drawer': setPanel(null); break;
      case 'preview-exit': stopPreview(); break;
      case 'delete':
        // A locked item is kept until it is unlocked.
        if (item?.locked) { announce('It is locked. Unlock it first.'); break; }
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
      case 'rotate-left':
      case 'rotate-right':
      case 'straighten':
        if (item?.locked) { announce('It is locked. Unlock it first.'); break; }
        if (action === 'rotate-left') changeSelected((i) => { i.angle = core.normaliseAngle(i.angle - 15); });
        else if (action === 'rotate-right') changeSelected((i) => { i.angle = core.normaliseAngle(i.angle + 15); });
        else changeSelected((i) => { i.angle = 0; });
        break;
      case 'adjust': if (item?.kind === 'photo') startAdjust(item.id); break;
      case 'adjust-done': stopAdjust(); break;
      case 'replace': if (isFrame(item)) { setTarget(item.id); setPanel('photos'); } break;
      case 'fill-slot': if (item?.kind === 'slot') chooseFor(item.id); break;
      case 'replace-cancel': setTarget(null); break;
      case 'save-open': openSave(); break;
      case 'credits': root.querySelector('[data-credits]').showModal(); break;
      case 'drafts-all': draftsExpanded = !draftsExpanded; renderDrafts(); break;
      case 'keep-draft':
        // Asked again: keeping it may still mean deleting the oldest draft to make room.
        if (draft?.off) {
          draft.off = false;
          if (keepDraftButton) keepDraftButton.hidden = true;
          draftPending = true;
          setDraftStatus('Saving draft…');
          saveDraftNow();
        }
        break;
      case 'library-open': libraryPicker.open(); break;
      case 'library-close': libraryPicker.close(); break;
      case 'library-more': libraryPicker.more(); break;
      case 'library-add': libraryPicker.add(); break;
      case 'credits-close': root.querySelector('[data-credits]').close(); break;
      case 'save-close': if (!saving) saveDialog.close(); break;
      case 'save-go': saveCollage(); break;
      case 'draft-undo': undoDeleteDraft(); break;
      case 'reset-look':
        changeSelected((i) => { Object.assign(i, { look: 'none', bright: 100, contrast: 100, warmth: 0, flip: false }); });
        break;
      case 'fit-photo':
        if (item?.locked) { announce('It is locked. Unlock it first.'); break; }
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
    const align = event.target.closest('[data-align]');
    if (align && !align.disabled) {
      changeSelected((target) => Object.assign(target, core.alignItem(target, collage.size, align.dataset.align)));
      return;
    }
    const layerPick = event.target.closest('[data-layer-select]');
    if (layerPick) {
      select(layerPick.dataset.layerSelect);
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
      if (target) { target.locked = !target.locked; if (target.locked && adjustId === target.id) stopAdjust(false); commit(); }
      return;
    }
    const backgroundChoice = event.target.closest('[data-background]');
    if (backgroundChoice) {
      setBackground(JSON.parse(backgroundChoice.dataset.background));
      announce('Background changed. Ctrl+Z puts the last one back.');
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

  // ---------------------------------------------------------------------------
  // Panels on the left
  // ---------------------------------------------------------------------------

  const phoneScreen = window.matchMedia?.('(max-width: 820px)');
  const layersButton = root.querySelector('[data-action="toggle-props"]');
  const onPhone = () => phoneScreen?.matches === true;

  /** On a phone, the settings sheet (for the selected item, or the page) open or closed. */
  function setPropsOpen(open) {
    body.classList.toggle('is-props-open', Boolean(open));
    layersButton?.setAttribute('aria-pressed', String(Boolean(open)));
    if (open && onPhone()) setPanel(null);
    syncSheets();
  }

  /** Marks whether a sheet covers the lower half, so the page fits above it. */
  function syncSheets() {
    const sheet = onPhone() && (body.classList.contains('is-props-open') || Boolean(panel));
    body.classList.toggle('is-sheet-open', sheet);
    window.requestAnimationFrame(layout);
    window.setTimeout(layout, 60);
  }

  phoneScreen?.addEventListener?.('change', syncSheets);

  function setPanel(name) {
    panel = name;
    if (name && onPhone()) {
      body.classList.remove('is-props-open');
      layersButton?.setAttribute('aria-pressed', 'false');
    }
    for (const button of root.querySelectorAll('[data-panel]')) {
      button.setAttribute('aria-pressed', String(button.dataset.panel === name));
    }
    for (const section of root.querySelectorAll('[data-drawer-panel]')) {
      section.hidden = section.dataset.drawerPanel !== name;
    }
    body.classList.toggle('is-drawer-closed', !name);
    if (name === 'layouts') renderLayoutPanel();
    if (name === 'background') renderBackgroundPanel();
    syncSheets();
  }

  for (const button of root.querySelectorAll('[data-panel]')) {
    button.addEventListener('click', () => setPanel(panel === button.dataset.panel ? null : button.dataset.panel));
  }

  function renderLayoutPanel() {
    keepingFocus(() => renderLayoutPanelNow());
  }

  function renderLayoutPanelNow() {
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
    const framed = item.kind === 'photo' || item.kind === 'slot';
    const [low, high] = FRAME_WIDTHS;
    const size = !framed ? '' : `
      <div class="clg-section">
        <label class="clg-field"><span class="clg-field__label">Size<output data-out="width">${Math.round(item.width)}</output></span>
          <input type="range" min="${low}" max="${high}" step="1" value="${Math.round(Math.min(high, Math.max(low, item.width)))}" data-prop="width" aria-label="Width of the ${item.kind === 'slot' ? 'frame' : 'photo'}, in page units"></label>
      </div>`;
    const corners = item.kind === 'text' || item.kind === 'sticker' ? '' : `
        <label class="clg-field"><span class="clg-field__label">Rounded corners<output data-out="radius">${item.radius || 0}</output></span>
          <input type="range" min="0" max="${core.LAYOUT_LIMITS.radiusMax}" step="1" value="${item.radius || 0}" data-prop="radius" aria-label="Rounded corners, in page units"></label>`;
    return `${size}
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
        ${corners}
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
        <p class="clg-section__title">Line up on the page</p>
        <div class="clg-buttons">
          ${core.ALIGN_PLACES.map((place) => `<button type="button" class="btn btn-outline-secondary btn-sm" data-align="${place}"${item.locked ? ' disabled' : ''}>${place.charAt(0).toUpperCase() + place.slice(1)}</button>`).join('')}
        </div>
      </div>
      <div class="clg-section">
        <div class="clg-buttons clg-buttons--two">
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="lock" aria-pressed="${Boolean(item.locked)}"><i class="bi ${item.locked ? 'bi-unlock' : 'bi-lock'}" aria-hidden="true"></i> ${item.locked ? 'Unlock' : 'Lock'}</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="hide" aria-pressed="${Boolean(item.hidden)}"><i class="bi ${item.hidden ? 'bi-eye' : 'bi-eye-slash'}" aria-hidden="true"></i> ${item.hidden ? 'Show' : 'Hide'}</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="duplicate"><i class="bi bi-copy" aria-hidden="true"></i> Duplicate</button>
          <button type="button" class="btn btn-outline-danger btn-sm" data-action="delete"${item.locked ? ' disabled' : ''}><i class="bi bi-trash3" aria-hidden="true"></i> Delete</button>
        </div>
        ${item.locked ? '<p class="clg-hint">Locked: it cannot be moved, resized, turned, given another frame or shape, or deleted until it is unlocked.</p>' : ''}
        ${item.hidden ? '<p class="clg-hint">Hidden: it is not on the page or in the saved picture until it is shown again.</p>' : ''}
      </div>`;
  }

  /** The Layers list: every item, the front one first, to select, hide or lock. */
  function layersSection() {
    if (!collage.items.length) return '';
    const rows = [...collage.items].reverse().map((item) => {
      let thumb = '<span class="clg-layer__thumb clg-layer__thumb--icon" aria-hidden="true"><i class="bi bi-square"></i></span>';
      let name = core.layerName(item);
      if (item.kind === 'photo') {
        const photo = photos.get(item.photo);
        thumb = `<img class="clg-layer__thumb" src="${photo?.thumbnail || ''}" alt="">`;
        name = photo?.name || name;
      } else if (item.kind === 'sticker') {
        const entry = stickers.find(item.sticker);
        thumb = `<img class="clg-layer__thumb clg-layer__thumb--contain" src="${stickerSource(item) || ''}" alt="">`;
        name = entry?.label || entry?.words?.split(' ')[0] || name;
      } else if (item.kind === 'text') {
        thumb = '<span class="clg-layer__thumb clg-layer__thumb--icon" aria-hidden="true"><i class="bi bi-fonts"></i></span>';
      }
      return `<li class="clg-layer${item.id === selectedId ? ' is-on' : ''}${item.hidden ? ' is-hidden' : ''}">
          <button type="button" class="clg-layer__pick" data-layer-select="${item.id}" aria-current="${item.id === selectedId}">${thumb}<span class="clg-layer__name">${escapeHtml(name)}</span></button>
          <button type="button" class="clg-layer__tool" data-layer-hide="${item.id}" aria-pressed="${Boolean(item.hidden)}" title="${item.hidden ? 'Show' : 'Hide'}" aria-label="${item.hidden ? 'Show' : 'Hide'} ${escapeHtml(name)}"><i class="bi ${item.hidden ? 'bi-eye-slash' : 'bi-eye'}" aria-hidden="true"></i></button>
          <button type="button" class="clg-layer__tool" data-layer-lock="${item.id}" aria-pressed="${Boolean(item.locked)}" title="${item.locked ? 'Unlock' : 'Lock'}" aria-label="${item.locked ? 'Unlock' : 'Lock'} ${escapeHtml(name)}"><i class="bi ${item.locked ? 'bi-lock-fill' : 'bi-unlock'}" aria-hidden="true"></i></button>
        </li>`;
    }).join('');
    return `
      <div class="clg-section">
        <p class="clg-section__title">Layers</p>
        <ul class="clg-layers">${rows}</ul>
        <p class="clg-hint">The top of the list is in front.</p>
      </div>`;
  }

  function renderProps() {
    keepingFocus(() => renderPropsNow());
  }

  function renderPropsNow() {
    renderPropsBody();
    if (selectedItem()?.locked) props.querySelectorAll(LOCKED_CONTROLS).forEach((control) => { control.disabled = true; });
    props.insertAdjacentHTML('afterbegin', '<button type="button" class="clg-sheet-close" data-action="close-props" aria-label="Close settings"><i class="bi bi-x-lg" aria-hidden="true"></i></button>');
    props.insertAdjacentHTML('beforeend', layersSection());
  }

  function renderPropsBody() {
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
    if (item.kind === 'text') {
      props.innerHTML = `<h2 class="clg-panel-title">Text</h2>${textSections(item)}${arrangeSections(item)}`;
      return;
    }
    if (item.kind === 'sticker') {
      const entry = stickers.find(item.sticker);
      const [low, high] = core.STICKER_LIMITS.width;
      props.innerHTML = `
        <h2 class="clg-panel-title">Sticker <small>${escapeHtml(entry?.label || entry?.emoji || '')}</small></h2>
        <div class="clg-section">
          <label class="clg-field"><span class="clg-field__label">Size<output data-out="width">${Math.round(item.width)}</output></span>
            <input type="range" min="${low}" max="${high}" step="1" value="${Math.round(item.width)}" data-prop="width" aria-label="Size of the sticker, in page units"></label>
          ${entry?.kind === 'drawn' ? `<p class="clg-field__label clg-field__label--plain">Colour</p>${swatches('colour', STICKER_COLOURS, item.colour || entry.colour)}` : ''}
          ${switchRow('flip', 'Mirror', item.flip)}
          ${switchRow('shadow', 'Shadow', item.shadow)}
        </div>
        ${arrangeSections(item)}`;
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
      ${finishSections(item, photo)}
      ${arrangeSections(item)}`;
  }

  const TEXT_COLOURS = ['#ffffff', '#172033', '#000000', '#c62f3a', '#e8c36a', '#ffd23f', '#1f6f43', '#0b2a8a', '#c2185b', '#7a3b1e', '#ff7043', '#6a1b9a'];
  const STICKER_COLOURS = ['#c62f3a', '#e2b13c', '#ffd23f', '#1f6f43', '#0b2a8a', '#c2185b', '#172033', '#ffffff', '#9fd3c7', '#ff7043'];
  const LABEL_COLOURS = ['#ffffff', '#fffbea', '#172033', '#ffd23f', '#c62f3a', '#1f6f43'];

  function swatches(name, colours, current, withNone = false) {
    const none = withNone ? `<button type="button" class="clg-swatch clg-swatch--none" data-text-set="${name}" data-value="" aria-pressed="${!current}" title="None" aria-label="None"></button>` : '';
    const list = colours.map((c) => `<button type="button" class="clg-swatch" data-text-set="${name}" data-value="${c}" aria-pressed="${current === c}" style="background:${c}" title="${c}" aria-label="Colour ${c}"></button>`).join('');
    const pick = `<input type="color" class="clg-swatch-pick" value="${/^#[0-9a-f]{6}$/i.test(current) ? current : '#ffffff'}" data-text-colour="${name}" aria-label="Any colour">`;
    return `<div class="clg-swatches">${none}${list}${pick}</div>`;
  }

  /** Words, font and effects of a text item. */
  function textSections(item) {
    const fonts = Object.entries(core.FONTS).map(([key, font]) => `<button type="button" class="clg-font${item.font === key ? ' is-on' : ''}" data-text-set="font" data-value="${key}" aria-pressed="${item.font === key}" style='font-family:${font.css}'>${escapeHtml(font.label)}</button>`).join('');
    const aligns = [['left', 'Left'], ['center', 'Centre'], ['right', 'Right']].map(([v, l]) => `<button type="button" class="btn btn-sm ${item.align === v ? 'btn-primary' : 'btn-outline-secondary'}" data-text-set="align" data-value="${v}" aria-pressed="${item.align === v}">${l}</button>`).join('');
    const range = (prop, label, [min, max], unit = '') => `<label class="clg-field"><span class="clg-field__label">${label}<output data-out="${prop}">${item[prop]}${unit}</output></span>
          <input type="range" min="${min}" max="${max}" step="1" value="${item[prop]}" data-prop="${prop}" aria-label="${label}"></label>`;
    return `
      <label class="clg-field"><span class="clg-field__label">Words</span>
        <textarea class="form-control form-control-sm clg-text-field" rows="3" maxlength="${core.TEXT_LIMITS.maxLength}" data-text>${escapeHtml(item.text)}</textarea></label>
      <p class="clg-hint">Type Hindi with the Windows Hindi keyboard, or paste it.</p>
      <div class="clg-section">
        <p class="clg-section__title">Font</p>
        <div class="clg-fonts">${fonts}</div>
        ${range('size', 'Size', core.TEXT_LIMITS.size)}
        <div class="clg-choices">
          <button type="button" class="btn btn-sm ${item.bold ? 'btn-primary' : 'btn-outline-secondary'}" data-text-toggle="bold" aria-pressed="${item.bold}"><b>B</b> Bold</button>
          <button type="button" class="btn btn-sm ${item.italic ? 'btn-primary' : 'btn-outline-secondary'}" data-text-toggle="italic" aria-pressed="${item.italic}"><i>I</i> Italic</button>
        </div>
        <div class="clg-choices clg-choices--gap">${aligns}</div>
        ${range('spacing', 'Letter spacing', core.TEXT_LIMITS.spacing)}
        <p class="clg-field__label clg-field__label--plain">Colour</p>
        ${swatches('color', TEXT_COLOURS, item.color)}
      </div>
      <div class="clg-section">
        <p class="clg-section__title">Effects</p>
        ${range('curve', 'Curve', core.TEXT_LIMITS.curve)}
        ${range('outline', 'Outline', core.TEXT_LIMITS.outline)}
        <p class="clg-field__label clg-field__label--plain">Outline colour</p>
        ${swatches('outlineColor', TEXT_COLOURS, item.outlineColor)}
        ${switchRow('shadow', 'Shadow', item.shadow)}
        ${switchRow('glow', 'Glow', item.glow)}
        <p class="clg-field__label clg-field__label--plain">Label behind the words${item.curve ? ' (straight text only)' : ''}</p>
        ${swatches('label', LABEL_COLOURS, item.label, true)}
      </div>`;
  }

  /** Puts the cursor in the selected text's words, opening its settings. */
  function focusTextField() {
    const field = props.querySelector('[data-text]');
    if (!field) return;
    // On a phone the Words field is in the settings sheet, which opens first.
    if (onPhone()) setPropsOpen(true);
    field.focus();
    field.select();
  }

  props.addEventListener('click', (event) => {
    const set = event.target.closest('[data-text-set]');
    if (set) {
      changeSelected((item) => { item[set.dataset.textSet] = set.dataset.value; });
      return;
    }
    const toggle = event.target.closest('[data-text-toggle]');
    if (toggle) changeSelected((item) => { item[toggle.dataset.textToggle] = !item[toggle.dataset.textToggle]; });
  });

  props.addEventListener('input', (event) => {
    if (event.target.matches('[data-text]')) {
      const text = event.target.value.slice(0, core.TEXT_LIMITS.maxLength);
      changeSelected((item) => { item.text = text; }, false);
    } else if (event.target.matches('[data-text-colour]')) {
      const value = event.target.value;
      changeSelected((item) => { item[event.target.dataset.textColour] = value; }, false);
    }
  });

  props.addEventListener('change', (event) => {
    if (event.target.matches('[data-text], [data-text-colour]')) flushNudge();
  });

  /** Buttons that choose one value of a photo's finish (frame, shape or look). */
  function choiceButtons(name, entries, current, disabled = () => false) {
    return entries.map(([value, label]) => `<button type="button" class="btn btn-sm ${value === current ? 'btn-primary' : 'btn-outline-secondary'}" data-finish="${name}" data-value="${value}" aria-pressed="${value === current}"${disabled(value) ? ' disabled' : ''}>${escapeHtml(label)}</button>`).join('');
  }

  function switchRow(name, label, on) {
    return `<label class="form-check form-switch clg-switch"><input class="form-check-input" type="checkbox" role="switch" data-toggle="${name}"${on ? ' checked' : ''}><span class="form-check-label">${escapeHtml(label)}</span></label>`;
  }

  /** Frame, shape and look: the photo's finish, the same on the page and in the saved picture. */
  function finishSections(item, photo) {
    const shape = item.shape || 'rect';
    const frame = item.frame || 'none';
    const square = shape === 'rect';
    const look = item.look || 'none';
    const filterOk = draw.canFilter(document.createElement('canvas').getContext('2d'));
    const looks = Object.entries(core.LOOKS).map(([value, entry]) => `
          <button type="button" class="clg-look${value === look ? ' is-on' : ''}" data-finish="look" data-value="${value}" aria-pressed="${value === look}">
            <img src="${photo?.thumbnail || ''}" alt="" style="filter:${entry.filter || 'none'}"><span>${escapeHtml(entry.label)}</span>
          </button>`).join('');
    return `
      <div class="clg-section">
        <p class="clg-section__title">Frame</p>
        <div class="clg-choices">${choiceButtons('frame', Object.entries(core.FRAMES).map(([k, v]) => [k, v.label]), frame, (v) => !square && v !== 'none')}</div>
        ${square ? '' : '<p class="clg-hint">Frames go with the Square shape.</p>'}
        ${square && frame === 'polaroid' ? `<label class="clg-field"><span class="clg-field__label">Caption under the photo</span>
          <input type="text" class="form-control form-control-sm" maxlength="${core.CAPTION_MAX}" value="${escapeHtml(item.caption || '')}" placeholder="Last day at office" data-caption></label>` : ''}
        ${switchRow('shadow', 'Shadow', item.shadow)}
      </div>
      <div class="clg-section">
        <p class="clg-section__title">Shape</p>
        <div class="clg-choices">${choiceButtons('shape', Object.entries(core.SHAPES), shape)}</div>
      </div>
      <div class="clg-section">
        <p class="clg-section__title">Look</p>
        ${filterOk ? '' : '<p class="clg-hint">This browser cannot show looks; use Edge or Chrome to change them.</p>'}
        <div class="clg-looks">${looks}</div>
        <label class="clg-field"><span class="clg-field__label">Brightness<output data-out="bright">${item.bright ?? 100}%</output></span>
          <input type="range" min="${core.ADJUST_LIMITS.bright[0]}" max="${core.ADJUST_LIMITS.bright[1]}" step="1" value="${item.bright ?? 100}" data-prop="bright" aria-label="Brightness, in percent"></label>
        <label class="clg-field"><span class="clg-field__label">Contrast<output data-out="contrast">${item.contrast ?? 100}%</output></span>
          <input type="range" min="${core.ADJUST_LIMITS.contrast[0]}" max="${core.ADJUST_LIMITS.contrast[1]}" step="1" value="${item.contrast ?? 100}" data-prop="contrast" aria-label="Contrast, in percent"></label>
        <label class="clg-field"><span class="clg-field__label">Warmth<output data-out="warmth">${item.warmth ?? 0}</output></span>
          <input type="range" min="${core.ADJUST_LIMITS.warmth[0]}" max="${core.ADJUST_LIMITS.warmth[1]}" step="1" value="${item.warmth ?? 0}" data-prop="warmth" aria-label="Warmth: below 0 cooler, above 0 warmer"></label>
        ${switchRow('flip', 'Mirror the photo', item.flip)}
        <button type="button" class="btn btn-outline-secondary btn-sm" data-action="reset-look">Reset look</button>
      </div>`;
  }

  /** Puts a frame on, takes it off or changes the shape, keeping the photo's own shape. */
  function setFinish(item, name, value) {
    const pictureAspect = core.innerAspect(item);
    item[name] = value;
    if (name === 'frame' && value !== 'none') item.shape = 'rect';
    if (name === 'frame' || name === 'shape') {
      item.aspect = core.limitFrameAspect({ ...item, aspect: core.outerAspectFor(item, pictureAspect) });
      item.crop = clampedCrop(item);
    }
  }

  props.addEventListener('click', (event) => {
    const choice = event.target.closest('[data-finish]');
    if (!choice || choice.disabled) return;
    if (selectedItem()?.locked && choice.matches(LOCKED_CONTROLS)) return;
    changeSelected((item) => setFinish(item, choice.dataset.finish, choice.dataset.value));
  });

  props.addEventListener('change', (event) => {
    const toggle = event.target.closest('[data-toggle]');
    if (toggle) changeSelected((item) => { item[toggle.dataset.toggle] = toggle.checked; });
  });

  props.addEventListener('input', (event) => {
    if (event.target.matches('[data-caption]')) {
      const text = event.target.value.slice(0, core.CAPTION_MAX);
      changeSelected((item) => { item.caption = text; }, false);
      return;
    }
    const prop = event.target.dataset.prop;
    if (!prop) return;
    if (selectedItem()?.locked && event.target.matches(LOCKED_CONTROLS)) return;
    const value = Number(event.target.value);
    changeSelected((item) => {
      if (prop === 'zoom') zoomCrop(item, value);
      else if (prop === 'angle') item.angle = core.normaliseAngle(value);
      else item[prop] = value;
    }, false);
    const out = props.querySelector(`[data-out="${prop}"]`);
    if (out) {
      out.textContent = prop === 'angle' ? `${Math.round(value)}°`
        : prop === 'opacity' || prop === 'bright' || prop === 'contrast' ? `${value}%`
          : prop === 'size' || prop === 'width' ? `${Math.round(value)}`
          : prop === 'zoom' ? `${value.toFixed(1)}×` : String(value);
    }
  });

  props.addEventListener('change', (event) => {
    if (event.target.matches('[data-caption]')) {
      flushNudge();
      return;
    }
    if (event.target.dataset.prop) {
      // A frame given its own corners is no longer bound to the layout's.
      if (event.target.dataset.prop === 'radius' && collage.layout) collage.layout = { ...collage.layout, radius: Number(event.target.value) };
      // Recorded even when the slider ends where it began, so the corners above are kept.
      edits.mark();
      flushNudge();
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
    const frame = (frameId && itemById(frameId)) || (!at ? (core.bestSlotFor(collage.items, photo.aspect) || firstSlot()) : null);
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
  /** The grey stand-in for a draft photo this browser cannot open now; its file is kept. */
  function missingPhoto(entry) {
    const canvas = document.createElement('canvas');
    canvas.width = 4;
    canvas.height = 3;
    const context = canvas.getContext('2d');
    if (context) {
      context.fillStyle = '#9aa3ad';
      context.fillRect(0, 0, 4, 3);
    }
    // The photo's own size, stored with it, so crops and frames are judged against the real photo
    // and the draft keeps them exactly; 4 by 3 only for a record from before sizes were stored.
    const known = Number(entry.width) > 0 && Number(entry.height) > 0;
    const width = known ? Number(entry.width) : 4;
    const height = known ? Number(entry.height) : 3;
    return {
      id: entry.id,
      name: entry.name,
      file: entry.file,
      canvas,
      thumbnail: canvas.toDataURL('image/png'),
      width,
      height,
      aspect: width / height,
      missing: true
    };
  }

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

  /**
   * A photo's size as it is shown (turned by its camera orientation), read from the file
   * without decoding all its pixels.
   */
  async function pictureSize(file) {
    const address = URL.createObjectURL(file);
    try {
      const image = new Image();
      image.decoding = 'async';
      await new Promise((resolve, reject) => {
        image.onload = resolve;
        image.onerror = () => reject(new Error('unreadable'));
        image.src = address;
      });
      return { width: image.naturalWidth, height: image.naturalHeight };
    } finally {
      URL.revokeObjectURL(address);
    }
  }

  /**
   * A photo read for the editor: a working copy at most 2000 pixels on its long side, decoded
   * straight at that size so a large phone photo never sits in memory at full size. The
   * original file is kept for Save. Throws a reason ('unreadable', 'too-many-pixels').
   */
  async function readPhoto(entry, keepId = null) {
    const natural = await pictureSize(entry.file);
    if (!(natural.width > 0) || !(natural.height > 0)) throw Object.assign(new Error('unreadable'), { reason: 'unreadable' });
    if (natural.width * natural.height > core.PHOTO_LIMITS.maxPixels) {
      throw Object.assign(new Error('too many pixels'), { reason: 'too-many-pixels' });
    }
    const size = core.workingSize(natural.width, natural.height);
    const bitmap = await createImageBitmap(entry.file, { resizeWidth: size.width, resizeHeight: size.height, resizeQuality: 'high' });
    try {
      const canvas = document.createElement('canvas');
      canvas.width = size.width;
      canvas.height = size.height;
      const context = canvas.getContext('2d');
      if (!context) throw Object.assign(new Error('no canvas'), { reason: 'unreadable' });
      context.drawImage(bitmap, 0, 0, size.width, size.height);
      return {
        id: keepId || nextId('p'),
        name: entry.name,
        file: entry.file,
        canvas,
        thumbnail: thumbnailOf(canvas),
        width: natural.width,
        height: natural.height,
        aspect: natural.width / natural.height
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
        } catch (error) {
          refused.push({ name: entry.name, reason: error?.reason === 'too-many-pixels' ? 'too-many-pixels' : 'unreadable' });
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

  // From PRISM Photos (collage-library-dialog.js): photos the user can see in /Photos, read in as
  // files and added like photos from the computer.
  const libraryPicker = window.PrismCollageLibraryDialog.createLibraryDialog({
    root,
    library,
    core,
    busy,
    showAlert,
    addFiles,
    photoCount: () => photos.size,
    session: () => collageSession,
    frameTarget: () => {
      const selected = selectedItem();
      return targetId || (selected?.kind === 'slot' ? selected.id : null);
    },
    currentAlert: () => (alertBox.hidden ? '' : $('[data-alert-text]').textContent)
  });

  fileInput.addEventListener('change', () => {
    const files = [...fileInput.files];
    fileInput.value = '';
    // An empty frame that is selected takes the photo, as it does from the tray or a paste.
    const selected = selectedItem();
    addFiles(files, { frameId: targetId || (selected?.kind === 'slot' ? selected.id : null) });
  });

  function renderTray() {
    keepingFocus(() => renderTrayNow());
  }

  function renderTrayNow() {
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
    if (views.editor.hidden || leaving) return;
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
    if (!hasContent()) {
      showAlert('Add a photo, some text or a sticker first; there is nothing to save yet.');
      return;
    }
    flushNudge();
    stopAdjust();
    saveResult.hidden = true;
    syncSaveDialog();
    saveDialog.showModal();
  }

  saveDialog.addEventListener('change', syncSaveDialog);

  // "What is it for?" chooses a type and quality that suit it; the user can still change them.
  const SAVE_PURPOSES = Object.freeze({
    share: { format: 'jpeg', quality: 'screen' },
    print: { format: 'pdf', quality: 'print' },
    slide: { format: 'png', quality: 'screen' }
  });
  saveDialog.addEventListener('click', (event) => {
    const button = event.target.closest('[data-save-purpose]');
    const choice = button && SAVE_PURPOSES[button.dataset.savePurpose];
    if (!choice) return;
    for (const [name, value] of Object.entries(choice)) {
      const radio = saveDialog.querySelector(`input[name="${name}"][value="${value}"]`);
      if (radio) radio.checked = true;
    }
    saveDialog.querySelectorAll('[data-save-purpose]').forEach((other) => other.setAttribute('aria-pressed', String(other === button)));
    syncSaveDialog();
    announce(`${saveDialog.querySelector('input[name="format"]:checked')?.closest('label')?.querySelector('b')?.textContent || ''}, ${choice.quality === 'print' ? 'print quality' : 'for the screen'}.`);
  });
  saveDialog.addEventListener('cancel', (event) => { if (saving) event.preventDefault(); });

  // Save draws the collage from the original photos (collage-render.js).
  const renderer = window.PrismCollageRender.createRenderer({ core, draw, measureText, fontsReady, stickerImage });
  // What the last Save left out: photos shown grey, stickers PRISM could not send.
  let saveLeftOut = 0;
  let saveStickersLeftOut = 0;

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
      const fits = renderer.largestCanvas(wanted.width, wanted.height);
      if (!fits) throw new Error('canvas');
      const drawn = await renderer.drawCollage(collage, photos, fits.width, fits.height);
      const canvas = drawn.canvas;
      saveLeftOut = drawn.leftOutPhotos;
      saveStickersLeftOut = drawn.leftOutStickers;
      const stem = core.fileStem(collageTitle());
      let bytes;
      let width = canvas.width;
      let height = canvas.height;
      let missed = false;
      let type;
      let name;
      if (format === 'png') {
        type = 'image/png';
        name = `${stem}.png`;
        bytes = await renderer.toBytes(canvas, type);
      } else if (format === 'jpeg') {
        type = 'image/jpeg';
        name = `${stem}.jpg`;
        if (targetBytes && photoTools) {
          const result = await photoTools.encodeToTarget({
            encode: (w, h, q) => renderer.toBytes(renderer.scaledCanvas(canvas, w, h), type, q),
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
          bytes = await renderer.toBytes(canvas, type, 0.92);
        }
      } else {
        type = 'application/pdf';
        name = `${stem}.pdf`;
        const page = core.pdfPage(collage.size);
        const jpeg = await renderer.toBytes(canvas, 'image/jpeg', 0.92);
        const pdf = await window.PDFLib.PDFDocument.create();
        pdf.setTitle(collageTitle());
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
      if (saveLeftOut) line += ` ${saveLeftOut === 1 ? 'One photo' : `${saveLeftOut} photos`} shown grey could not be opened in this browser and ${saveLeftOut === 1 ? 'was' : 'were'} left out.`;
      if (saveStickersLeftOut) line += ` ${saveStickersLeftOut === 1 ? 'One sticker' : `${saveStickersLeftOut} stickers`} could not be loaded and ${saveStickersLeftOut === 1 ? 'was' : 'were'} left out; check the connection to PRISM and save again.`;
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

  /**
   * Records the collage as it is now as one Undo step and keeps it in the draft, without
   * redrawing the panels. commit() does this and redraws; held edits (typing, sliders, colour
   * pickers, nudges) do it once they pause, through holdEdit() and flushNudge().
   */
  function recordQuietly() {
    history.record(collage);
    // An unnamed collage's title follows its first words, typed or not.
    refreshTitle();
    updateUndo();
    scheduleDraft();
  }

  /**
   * An edit already on the page that is still going on (typing, a slider, a colour picker, a
   * nudge): kept in the draft at once, recorded for Undo once it pauses, or as soon as anything
   * else happens (another item, Undo, leaving).
   */
  function holdEdit(pause = 600) {
    edits.hold(pause);
  }

  /** Records a held edit now, so Undo and Redo start from where things really are. */
  function flushNudge() {
    edits.flush();
  }

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Shift') setShift(true);
    if (views.editor.hidden || leaving || document.querySelector('dialog[open]')) return;
    const typing = event.target.closest?.('input, textarea, select, [contenteditable]');
    // Enter on a button, link or tile belongs to it; only Enter on the page itself acts on the item.
    const onPage = event.target === document.body || stageBox.contains(event.target);
    const key = event.key.toLowerCase();
    const mod = event.ctrlKey || event.metaKey;
    if (key === 'escape' && previewing) { stopPreview(); return; }
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
    else if (key === 'enter' && onPage && selectedItem()?.kind === 'photo' && !adjustId) act('adjust');
    else if (key === 'enter' && onPage && selectedItem()?.kind === 'text') { event.preventDefault(); focusTextField(); }
    else if (key.startsWith('arrow') && selectedId && selectedItem()?.hidden) { event.preventDefault(); announce('It is hidden. Show it from Layers first.'); }
    else if (key.startsWith('arrow') && selectedId && selectedItem()?.locked) { event.preventDefault(); announce('It is locked. Unlock it first.'); }
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
      holdEdit(400);
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
      holdEdit(300);
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
