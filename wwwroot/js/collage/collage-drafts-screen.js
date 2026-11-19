// PRISM Collage Studio — drafts in the editor: the draft status, writing the draft, its small picture, and the drafts on the start screen (open, delete, Undo)
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the libraries it uses, `state` (the editor's values, read only), its own named
// changes, and its helpers in a few groups (ui, doc, history, messages, surface, selection,
// screens), each read when used (collage-state.js; docs/ux/collage-review-4-fixes.md, PR 11).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageDraftsScreen = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createDraftsScreen(deps) {
    const { state, parts, change, core, messages, ui, history, doc, screens, surface, untouched = () => false } = deps;
    // ---------------------------------------------------------------------------
    // Drafts (kept in this browser)
    // ---------------------------------------------------------------------------

    const drafts = window.PrismCollageDrafts || null;
    const draftStatus = ui.$('[data-draft-status]');
    const keepDraftButton = ui.$('[data-action="keep-draft"]');

    // The draft being edited and the drafts on the start screen (collage-draft-session.js).
    const session = window.PrismCollageDraftSession.createDraftSession({
      drafts,
      core,
      ask: messages.askConfirm,
      snapshot: () => ({
        title: doc.collageTitle(),
        autoTitle: !ui.nameInput.value.trim(),
        // Parts made by a newer PRISM go back in unchanged (docs/ux/invitations-posters.md, PR 2).
        model: core.draftModel(state.collage),
        size: state.collage.size,
        kind: state.collage.card ? (window.PrismCollageDesigns?.DESIGNS[state.collage.card.design]?.poster ? 'poster' : 'card') : '',
        photos: parts.keptPhotos(),
        thumbnail: ui.views.editor.hidden ? '' : draftThumbnail(),
        // A design opened and not changed yet is not worth a draft (it starts at the first change).
        hasContent: doc.hasContent() && !untouched()
      }),
      onStatus: (text, warning) => setDraftStatus(text, warning),
      onUndo: (deleted) => showDraftUndo(deleted),
      onChanged: () => { if (!ui.views.start.hidden) renderDrafts(); }
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
      messages.announce(`Draft ${deleted.title} deleted. Undo is on the screen for ten seconds.`);
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
        messages.showAlert(session.problem() || text);
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
      if (state.thumbnail.fresh || !state.thumbnail.url || now - state.thumbnail.at >= ui.THUMBNAIL_EVERY_MS) {
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
      // The faint photo shown while a photo is moved in its frame is not part of the collage.
      // Nor the dashed fold of a folded card: it is only a guide on screen.
      const ghosts = parts.stage.find('.adjust-ghost, .fold').filter((node) => node.visible());
      ghosts.forEach((node) => node.visible(false));
      const width = core.PAGE_WIDTH * state.scale;
      try {
        return parts.stage.toDataURL({
          x: parts.world.x(), y: parts.world.y(), width, height: doc.pageHeight() * state.scale,
          pixelRatio: Math.min(4, 320 / Math.max(1, width)), mimeType: 'image/jpeg', quality: 0.8
        });
      } catch {
        return '';
      } finally {
        parts.transformer.visible(shown);
        parts.guides.visible(true);
        ghosts.forEach((node) => node.visible(true));
      }
    }


    async function renderDrafts() {
      const section = ui.$('[data-drafts-section]');
      const grid = ui.$('[data-drafts]');
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
      const empty = ui.$('[data-drafts-empty]');
      if (empty) empty.hidden = shown.length > 0;
      ui.$('[data-drafts-count]').textContent = shown.length ? String(shown.length) : '';
      // One row of drafts unless asked for all, so starting a new collage stays in view.
      // As many as fit across the grid (cards are at least 200 pixels wide).
      const row = Math.max(1, Math.floor(((grid.clientWidth || section.parentElement?.clientWidth || 800) + 12) / 212));
      const allButton = ui.$('[data-action="drafts-all"]');
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
        const size = core.isSize(item.size) ? core.sizeOf(item.size).label : '';
        // Each part stays whole; a narrow card breaks the line only between them.
        // A card or poster says what it is; its photos are counted only when it has some.
        const kind = item.kind === 'card' ? 'Card' : item.kind === 'poster' ? 'Poster' : '';
        const photosText = kind && !item.photoCount ? '' : `${item.photoCount} photo${item.photoCount === 1 ? '' : 's'}`;
        const details = [kind, `Saved ${core.savedAgo(item.saved, Date.now())}`, size, photosText].filter(Boolean);
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
      ui.$('[data-drafts-space]').textContent = used && window.PrismPhotoTools
        ? `Drafts and this site's other saved data use ${window.PrismPhotoTools.formatBytes(used)} of this browser's storage.`
        : '';
    }

    async function undoDeleteDraft() {
      if (await session.restore()) messages.announce('Draft brought back.');
    }


    async function openDraft(id) {
      const request = change.nextOpenRequest();
      const stillWanted = () => request === state.openRequest && !ui.views.start.hidden;
      // The start screen cannot be used while a draft opens, so the draft cannot be deleted (or
      // another one opened) half way through.
      ui.views.start.inert = true;
      try {
        await readDraft(id, request, stillWanted);
      } finally {
        if (request === state.openRequest) ui.views.start.inert = false;
      }
    }

    async function readDraft(id, request, stillWanted) {
      let found;
      try {
        found = await session.read(id);
      } catch {
        if (stillWanted()) messages.showAlert('The draft could not be opened: this browser did not give it back.');
        return;
      }
      if (!stillWanted()) return;
      if (!found) { renderDrafts(); return; }
      const { record, photos: stored } = found;
      screens.resetEditor();
      const forCollage = state.collageSession;
      messages.busy(stored.length ? `Opening the draft and its ${stored.length} photo${stored.length === 1 ? '' : 's'}…` : 'Opening the draft…');
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
          doc.photos.set(photo.id, photo);
        }
      } finally {
        messages.busy('');
      }
      // Deleted or purged (another tab) while its photos were read: it is not opened.
      if (!(await session.stillThere(id)) || forCollage !== state.collageSession || request !== state.openRequest) {
        if (forCollage === state.collageSession) { screens.resetEditor(); renderDrafts(); }
        return;
      }
      const model = core.readDraftModel(record.model, [...doc.photos.keys()]);
      if (!model) {
        // Its photos were read already: resetEditor lets them go now, not at the next collage.
        screens.resetEditor();
        renderDrafts();
        messages.showAlert('This draft was made by a newer version of PRISM, or is damaged, and cannot be opened here.');
        return;
      }
      change.setCollage(model);
      history.changes.start(state.collage);
      session.begin({ id: record.id, created: true, started: record.started || record.saved });
      ui.nameInput.value = record.autoTitle ? '' : record.title;
      screens.refreshTitle();
      ui.showSize(state.collage.size);
      screens.showView('editor');
      // A card or poster reopens on its Card details, as it opened from its design.
      parts.setPanel(state.collage.card ? 'card' : 'photos');
      surface.syncAll();
      if (unreadable) messages.showAlert(`${unreadable === 1 ? 'One photo' : `${unreadable} photos`} of this draft could not be opened just now and ${unreadable === 1 ? 'shows' : 'show'} grey. ${unreadable === 1 ? 'It keeps its' : 'They keep their'} place in the draft. Close other tabs and open the draft again; Save leaves ${unreadable === 1 ? 'it' : 'them'} out until then.`);
      // Undo steps live only while the collage is open; the user is told once, as it opens
      // (docs/ux/collage-review-4-fixes.md, PR 6).
      // Parts a newer PRISM made are kept in the draft but not shown here (PR 2 of
      // docs/ux/invitations-posters.md); the user is told once, with the Undo note.
      const kept = core.keptCount(state.collage);
      const keptNote = kept ? ` ${kept === 1 ? 'One part was' : `${kept} parts were`} made by a newer PRISM: ${kept === 1 ? 'it is' : 'they are'} kept in the draft but not shown here.` : '';
      messages.announce(`Draft ${record.title} opened. Undo starts again from here.${keptNote}`);
      window.PrismToast?.show({ tone: 'info', text: `Draft opened. Undo starts again from here: earlier steps are not kept with the draft.${keptNote}` });
    }


    return { drafts, session, setDraftStatus, scheduleDraft, freshThumbnail, renderDrafts, undoDeleteDraft, openDraft };
  }

  return Object.freeze({ createDraftsScreen });
});
