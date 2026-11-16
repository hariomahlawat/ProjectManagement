// PRISM Collage Studio — drafts in the editor: the draft status, writing the draft, its small picture, and the drafts on the start screen (open, delete, Undo)
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the helpers it uses, `state` (the editor's values, read only) and the named
// changes this part may make (collage-state.js, docs/ux/collage-review-3-fixes.md, PR 10).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageDraftsScreen = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createDraftsScreen(deps) {
    const { state, parts, change, core, askConfirm, $, views, nameInput, sizeSelect, THUMBNAIL_EVERY_MS, changes, photos, pageHeight, hasContent, announce, showAlert, busy, showView, resetEditor, syncAll, collageTitle, refreshTitle } = deps;
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
        model: state.collage,
        size: state.collage.size,
        photos: parts.keptPhotos(),
        thumbnail: views.editor.hidden ? '' : draftThumbnail(),
        hasContent: hasContent()
      }),
      onStatus: (text, warning) => setDraftStatus(text, warning),
      onUndo: (deleted) => showDraftUndo(deleted),
      onChanged: () => { if (!views.start.hidden) renderDrafts(); }
    });

    // Undo for a deleted draft: PRISM's glass toast, as after a delete in the editor
    // (docs/ux/collage-review-3-fixes.md, PR 8). It goes when the session's Undo time is over or
    // the draft is brought back. It shows no countdown of its own: the toast's would pause on
    // hover while the draft's ten seconds do not, and so would promise time that is not there.
    let draftUndoToast = null;

    function showDraftUndo(deleted) {
      draftUndoToast?.dismiss();
      draftUndoToast = null;
      if (!deleted) return;
      announce(`Draft ${deleted.title} deleted. Undo is on the screen for ten seconds.`);
      if (!window.PrismToast) return;
      draftUndoToast = window.PrismToast.show({
        tone: 'success',
        text: `Draft “${deleted.title}” deleted.`,
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
      state.thumbnail.behind = true;
      session.changed();
    }


    function draftThumbnail() {
      const now = Date.now();
      if (state.thumbnail.fresh || !state.thumbnail.url || now - state.thumbnail.at >= THUMBNAIL_EVERY_MS) {
        change.setThumbnail({ url: pageThumbnail(), at: now, fresh: false, behind: false });
      }
      return state.thumbnail.url;
    }

    /** Leaving: the draft is written once more if its picture is older than the page. */
    function freshThumbnail() {
      state.thumbnail.fresh = true;
      if (state.thumbnail.behind && session.draft?.created) session.changed();
    }

    /** A small picture of the page for the start screen, without handles or guides. */
    function pageThumbnail() {
      const shown = parts.transformer.visible();
      parts.transformer.visible(false);
      parts.guides.visible(false);
      const width = core.PAGE_WIDTH * state.scale;
      try {
        return parts.stage.toDataURL({
          x: parts.world.x(), y: parts.world.y(), width, height: pageHeight() * state.scale,
          pixelRatio: Math.min(4, 320 / Math.max(1, width)), mimeType: 'image/jpeg', quality: 0.8
        });
      } catch {
        return '';
      } finally {
        parts.transformer.visible(shown);
        parts.guides.visible(true);
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
      // The last draft deleted, while its Undo is offered: the heading says what is going on.
      const empty = $('[data-drafts-empty]');
      if (empty) empty.hidden = shown.length > 0;
      $('[data-drafts-count]').textContent = shown.length ? String(shown.length) : '';
      // One row of drafts unless asked for all, so starting a new collage stays in view.
      // As many as fit across the grid (cards are at least 200 pixels wide).
      const row = Math.max(1, Math.floor(((grid.clientWidth || section.parentElement?.clientWidth || 800) + 12) / 212));
      const allButton = $('[data-action="drafts-all"]');
      if (allButton) {
        allButton.hidden = shown.length <= row;
        allButton.textContent = state.draftsExpanded ? 'Show fewer drafts' : `Show all ${shown.length} drafts`;
        allButton.setAttribute('aria-expanded', String(state.draftsExpanded));
      }
      for (const item of state.draftsExpanded ? shown : shown.slice(0, row)) {
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
        const details = [`Saved ${core.savedAgo(item.saved, Date.now())}`, size, `${item.photoCount} photo${item.photoCount === 1 ? '' : 's'}`].filter(Boolean);
        details.forEach((part, index) => {
          const span = document.createElement('span');
          span.className = 'clg-draft__meta';
          span.textContent = part;
          meta.append(span);
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
      const request = change.nextOpenRequest();
      const stillWanted = () => request === state.openRequest && !views.start.hidden;
      // The start screen cannot be used while a draft opens, so the draft cannot be deleted (or
      // another one opened) half way through.
      views.start.inert = true;
      try {
        await readDraft(id, request, stillWanted);
      } finally {
        if (request === state.openRequest) views.start.inert = false;
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
      const forCollage = state.collageSession;
      busy(stored.length ? `Opening the draft and its ${stored.length} photo${stored.length === 1 ? '' : 's'}…` : 'Opening the draft…');
      let unreadable = 0;
      try {
        // One photo at a time, as when they were first added.
        for (const entry of stored) {
          let photo;
          try {
            // Sized for every photo of the draft, so all working copies stay within the budget.
            photo = await parts.readPhoto({ file: entry.file, name: entry.name }, entry.id, { count: stored.length });
          } catch {
            // Kept as a grey stand-in with its file, so the draft keeps the photo's place, crop and
            // caption, and the photo comes back when the draft is opened again on a browser that
            // can read it. Save leaves it out and says so.
            photo = parts.missingPhoto(entry);
            unreadable += 1;
          }
          if (forCollage !== state.collageSession) { window.PrismCollagePhotos.releasePhoto(photo); return; }
          photos.set(photo.id, photo);
        }
      } finally {
        busy('');
      }
      // Deleted or purged (another tab) while its photos were read: it is not opened.
      if (!(await session.stillThere(id)) || forCollage !== state.collageSession || request !== state.openRequest) {
        if (forCollage === state.collageSession) { resetEditor(); renderDrafts(); }
        return;
      }
      const model = core.readDraftModel(record.model, [...photos.keys()]);
      if (!model) {
        // Its photos were read already: resetEditor lets them go now, not at the next collage.
        resetEditor();
        renderDrafts();
        showAlert('This draft was made by a newer version of PRISM, or is damaged, and cannot be opened here.');
        return;
      }
      change.setCollage(model);
      changes.start(state.collage);
      session.begin({ id: record.id, created: true, started: record.started || record.saved });
      nameInput.value = record.autoTitle ? '' : record.title;
      refreshTitle();
      sizeSelect.value = state.collage.size;
      showView('editor');
      parts.setPanel('photos');
      syncAll();
      if (unreadable) showAlert(`${unreadable === 1 ? 'One photo' : `${unreadable} photos`} of this draft could not be opened just now and ${unreadable === 1 ? 'shows' : 'show'} grey. ${unreadable === 1 ? 'It keeps its' : 'They keep their'} place in the draft. Close other tabs and open the draft again; Save leaves ${unreadable === 1 ? 'it' : 'them'} out until then.`);
      // Undo steps live only while the collage is open; the user is told once, as it opens
      // (docs/ux/collage-review-4-fixes.md, PR 6).
      announce(`Draft ${record.title} opened. Undo starts again from here.`);
      window.PrismToast?.show({ tone: 'info', text: 'Draft opened. Undo starts again from here: earlier steps are not kept with the draft.' });
    }


    return { drafts, session, setDraftStatus, scheduleDraft, freshThumbnail, renderDrafts, undoDeleteDraft, openDraft };
  }

  return Object.freeze({ createDraftsScreen });
});
