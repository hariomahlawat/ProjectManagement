// PRISM Collage Studio — drafts in the editor: the draft status, writing the draft, its small picture, and the drafts on the start screen (open, delete, Undo)
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the helpers it uses and `app`, the editor's view of its own state.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageDraftsScreen = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createDraftsScreen(deps) {
    const { app, core, askConfirm, $, views, nameInput, sizeSelect, THUMBNAIL_EVERY_MS, changes, photos, pageHeight, hasContent, announce, showAlert, busy, showView, resetEditor, syncAll, collageTitle, refreshTitle } = deps;
    // ---------------------------------------------------------------------------
    // Drafts (kept in this browser)
    // ---------------------------------------------------------------------------

    const drafts = window.PrismCollageDrafts || null;
    const draftStatus = $('[data-draft-status]');
    const keepDraftButton = $('[data-action="keep-draft"]');

    // The draft being edited and the drafts on the start screen (collage-draft-session.js).
    const session = window.PrismCollageDraftSession.createDraftSession({
      drafts,
      core,
      ask: askConfirm,
      snapshot: () => ({
        title: collageTitle(),
        autoTitle: !nameInput.value.trim(),
        model: app.collage,
        size: app.collage.size,
        photos: app.keptPhotos(),
        thumbnail: views.editor.hidden ? '' : draftThumbnail(),
        hasContent: hasContent()
      }),
      onStatus: (text, warning) => setDraftStatus(text, warning),
      onUndo: (state) => showDraftUndo(state),
      onChanged: () => { if (!views.start.hidden) renderDrafts(); }
    });

    // Undo for a deleted draft: PRISM's glass toast, as after a delete in the editor
    // (docs/ux/collage-review-3-fixes.md, PR 8). It goes when the session's Undo time is over or
    // the draft is brought back. It shows no countdown of its own: the toast's would pause on
    // hover while the draft's ten seconds do not, and so would promise time that is not there.
    let draftUndoToast = null;

    function showDraftUndo(state) {
      draftUndoToast?.dismiss();
      draftUndoToast = null;
      if (!state) return;
      announce(`Draft ${state.title} deleted. Undo is on the screen for ten seconds.`);
      if (!window.PrismToast) return;
      draftUndoToast = window.PrismToast.show({
        tone: 'success',
        text: `Draft “${state.title}” deleted.`,
        sticky: true,
        action: { label: 'Undo', run: () => { undoDeleteDraft(); } }
      });
      draftUndoToast.element.dataset.draftUndo = '';
    }

    function setDraftStatus(text, warning = false) {
      draftStatus.textContent = text;
      draftStatus.title = warning ? (session.problem() || text) : '';
      draftStatus.classList.toggle('is-warning', warning);
      // A collage left out of the drafts can be kept again at any time.
      if (keepDraftButton) keepDraftButton.hidden = !session.draft?.off;
      // On a phone the status has room only for its mark, so the reason is shown once in full.
      if (warning && text !== draftWarningShown && window.matchMedia?.('(max-width: 1100px)').matches) {
        showAlert(session.problem() || text);
      }
      draftWarningShown = warning ? text : '';
    }
    let draftWarningShown = '';

    /** Saves the draft half a second after the last change. */
    function scheduleDraft() {
      app.thumbnail.behind = true;
      session.changed();
    }


    function draftThumbnail() {
      const now = Date.now();
      if (app.thumbnail.fresh || !app.thumbnail.url || now - app.thumbnail.at >= THUMBNAIL_EVERY_MS) {
        app.thumbnail = { url: pageThumbnail(), at: now, fresh: false, behind: false };
      }
      return app.thumbnail.url;
    }

    /** Leaving: the draft is written once more if its picture is older than the page. */
    function freshThumbnail() {
      app.thumbnail.fresh = true;
      if (app.thumbnail.behind && session.draft?.created) session.changed();
    }

    /** A small picture of the page for the start screen, without handles or guides. */
    function pageThumbnail() {
      const shown = app.transformer.visible();
      app.transformer.visible(false);
      app.guides.visible(false);
      const width = core.PAGE_WIDTH * app.scale;
      try {
        return app.stage.toDataURL({
          x: app.world.x(), y: app.world.y(), width, height: pageHeight() * app.scale,
          pixelRatio: Math.min(4, 320 / Math.max(1, width)), mimeType: 'image/jpeg', quality: 0.8
        });
      } catch {
        return '';
      } finally {
        app.transformer.visible(shown);
        app.guides.visible(true);
      }
    }


    async function renderDrafts() {
      const section = $('[data-drafts-section]');
      const grid = $('[data-drafts]');
      if (!drafts) { section.hidden = true; return; }
      let shown;
      try {
        shown = await session.list();
      } catch {
        section.hidden = true;
        return;
      }
      grid.textContent = '';
      section.hidden = !shown.length && !session.undoing;
      $('[data-drafts-count]').textContent = shown.length ? String(shown.length) : '';
      // One row of drafts unless asked for all, so starting a new collage stays in view.
      // As many as fit across the grid (cards are at least 200 pixels wide).
      const row = Math.max(1, Math.floor(((grid.clientWidth || section.parentElement?.clientWidth || 800) + 12) / 212));
      const allButton = $('[data-action="drafts-all"]');
      if (allButton) {
        allButton.hidden = shown.length <= row;
        allButton.textContent = app.draftsExpanded ? 'Show fewer drafts' : `Show all ${shown.length} drafts`;
        allButton.setAttribute('aria-expanded', String(app.draftsExpanded));
      }
      for (const item of app.draftsExpanded ? shown : shown.slice(0, row)) {
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
        // Each part stays whole; a narrow card breaks the line only between them.
        const parts = [`Saved ${core.savedAgo(item.saved, Date.now())}`, size, `${item.photoCount} photo${item.photoCount === 1 ? '' : 's'}`].filter(Boolean);
        parts.forEach((part, index) => {
          const span = document.createElement('span');
          span.className = 'clg-draft__meta';
          span.textContent = part;
          meta.append(...(index ? [' · ', span] : [span]));
        });
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

    async function undoDeleteDraft() {
      if (await session.restore()) announce('Draft brought back.');
    }


    async function openDraft(id) {
      const request = ++app.openRequest;
      const stillWanted = () => request === app.openRequest && !views.start.hidden;
      // The start screen cannot be used while a draft opens, so the draft cannot be deleted (or
      // another one opened) half way through.
      views.start.inert = true;
      try {
        await readDraft(id, request, stillWanted);
      } finally {
        if (request === app.openRequest) views.start.inert = false;
      }
    }

    async function readDraft(id, request, stillWanted) {
      let found;
      try {
        found = await session.read(id);
      } catch {
        if (stillWanted()) showAlert('The draft could not be opened: this browser did not give it back.');
        return;
      }
      if (!stillWanted()) return;
      if (!found) { renderDrafts(); return; }
      const { record, photos: stored } = found;
      resetEditor();
      const forCollage = app.collageSession;
      busy(stored.length ? `Opening the draft and its ${stored.length} photo${stored.length === 1 ? '' : 's'}…` : 'Opening the draft…');
      let unreadable = 0;
      try {
        // One photo at a time, as when they were first added.
        for (const entry of stored) {
          let photo;
          try {
            photo = await app.readPhoto({ file: entry.file, name: entry.name }, entry.id);
          } catch {
            // Kept as a grey stand-in with its file, so the draft keeps the photo's place, crop and
            // caption, and the photo comes back when the draft is opened again on a browser that
            // can read it. Save leaves it out and says so.
            photo = app.missingPhoto(entry);
            unreadable += 1;
          }
          if (forCollage !== app.collageSession) return;
          photos.set(photo.id, photo);
        }
      } finally {
        busy('');
      }
      // Deleted or purged (another tab) while its photos were read: it is not opened.
      if (!(await session.stillThere(id)) || forCollage !== app.collageSession || request !== app.openRequest) {
        if (forCollage === app.collageSession) { resetEditor(); renderDrafts(); }
        return;
      }
      const model = core.readDraftModel(record.model, [...photos.keys()]);
      if (!model) {
        showAlert('This draft was made by a newer version of PRISM, or is damaged, and cannot be opened here.');
        return;
      }
      app.collage = model;
      changes.start(app.collage);
      session.begin({ id: record.id, created: true, started: record.started || record.saved });
      nameInput.value = record.autoTitle ? '' : record.title;
      refreshTitle();
      sizeSelect.value = app.collage.size;
      showView('editor');
      app.setPanel('photos');
      syncAll();
      if (unreadable) showAlert(`${unreadable === 1 ? 'One photo' : `${unreadable} photos`} of this draft could not be opened just now and ${unreadable === 1 ? 'shows' : 'show'} grey. ${unreadable === 1 ? 'It keeps its' : 'They keep their'} place in the draft. Close other tabs and open the draft again; Save leaves ${unreadable === 1 ? 'it' : 'them'} out until then.`);
      announce(`Draft ${record.title} opened.`);
    }


    return { drafts, session, setDraftStatus, scheduleDraft, freshThumbnail, renderDrafts, undoDeleteDraft, openDraft };
  }

  return Object.freeze({ createDraftsScreen });
});
