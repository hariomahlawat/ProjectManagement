// PRISM Collage Studio — photos: choosing, dropping and pasting them, reading them in, the tray, and From PRISM Photos
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the libraries it uses, `state` (the editor's values, read only), its own named
// changes, and its helpers in a few groups (ui, doc, history, messages, surface, selection,
// screens), each read when used (collage-state.js; docs/ux/collage-review-4-fixes.md, PR 11).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageTray = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createTray(deps) {
    const { state, change, core, library, ui, doc, history, messages, surface, screens } = deps;
    // ---------------------------------------------------------------------------
    // Photos: choose, drop, paste, and the tray
    // ---------------------------------------------------------------------------

    /**
     * The frame the next photo goes into: shown in the Photos panel ("Choose the photo for the
     * selected frame") until a photo is chosen or Cancel is pressed.
     */
    function setTarget(id) {
      change.setTargetId(id);
      const item = id && doc.itemById(id);
      ui.replaceBanner.hidden = !item;
      if (item) {
        ui.$('[data-replace-text]').textContent = item.kind === 'slot'
          ? 'Choose the photo for the empty frame.'
          : 'Choose the photo to put in the selected frame.';
      }
    }

    /** Opens the file chooser for one frame: an empty one, or a photo to replace. */
    function chooseFor(id) {
      setTarget(id);
      screens.setPanel('photos');
      ui.fileInput.click();
    }

    /**
     * Puts a photo on the page: in the frame it was aimed at, else the empty frame closest to its
     * shape, else (on a page without a layout, or dropped at a point) on its own. On a layout with
     * every frame full it stays in the tray, and false is returned.
     */
    function placePhoto(photo, { frameId = null, at = null, among = 1 } = {}) {
      // A locked frame keeps its photo: the new one goes where it would go with no frame chosen.
      const asked = frameId && doc.itemById(frameId);
      const frame = (asked && !asked.locked ? asked : null) || (!at ? (core.bestSlotFor(state.collage.items, photo.aspect) || doc.firstSlot()) : null);
      if (!frame && !at && state.collage.layout) return false;
      if (frame && frame.kind === 'slot') {
        const filled = core.fillSlot(frame, photo.id);
        doc.edit((draft) => { draft.items[draft.items.findIndex((i) => i.id === frame.id)] = filled; }, 'none');
        change.choose(filled.id);
        return true;
      }
      if (frame && frame.kind === 'photo') {
        doc.edit((draft) => {
          const target = draft.items.find((i) => i.id === frame.id);
          target.photo = photo.id;
          target.crop = { ...core.FULL_CROP };
        }, 'none');
        change.choose(frame.id);
        return true;
      }
      const count = state.collage.items.filter((i) => i.kind === 'photo').length;
      // Several photos on a page with no layout go into a neat grid, each fully in view.
      const place = (!at && core.gridPlace(state.collage.items, state.collage.size, photo.aspect, count + among))
        || core.placeNewPhoto(state.collage.size, photo.aspect, count);
      if (at) { place.x = at.x; place.y = at.y; }
      const item = core.photoItem(doc.newItemId(), photo.id, photo.aspect, place);
      doc.edit((draft) => {
        // A photo placed freely means the page is no longer a strict grid, so changing the space
        // would not silently take it off the page.
        draft.layout = null;
        draft.items.push(item);
      }, 'none');
      change.choose(item.id);
      return true;
    }

    // Reading photos at their working size (collage-photos.js).
    const photoReader = window.PrismCollagePhotos.createPhotoReader({ core, nextId: doc.nextId });
    const { readPhoto, missingPhoto } = photoReader;

    // Imports run one batch at a time: a second drop or paste waits for the first, so the photo
    // limit counts every photo already added and only one large photo is ever being decoded.
    let importQueue = Promise.resolve();

    function addFiles(fileList, options = {}) {
      const files = [...fileList];
      if (!files.length) return importQueue;
      const forCollage = state.collageSession;
      importQueue = importQueue.then(() => importFiles(files, options, forCollage)).catch(() => {
        if (forCollage === state.collageSession) messages.showAlert('The photos could not be added. Try again, or add them one at a time.');
      });
      return importQueue;
    }

    /**
     * Runs `task` after every import (and logo read) before it, so photo counts and the pixel
     * budget are checked one read at a time. Returns its promise.
     */
    function afterImports(task) {
      importQueue = importQueue.then(task).catch(() => {});
      return importQueue;
    }

    async function importFiles(files, { at = null, frameId = null } = {}, forCollage) {
      // A collage left before its photos were read gets none of them, and neither does the next.
      const stillOpen = () => forCollage === state.collageSession;
      if (!stillOpen()) return;
      messages.showAlert('');
      messages.busy(files.length === 1 ? 'Adding the photo…' : `Adding ${files.length} photos…`);
      const added = [];
      try {
        const entries = await Promise.all(files.map(async (file) => {
          const entry = { file, name: file.name || 'Pasted photo', size: file.size, type: null };
          try {
            entry.type = core.sniffImageType(new Uint8Array(await file.slice(0, 16).arrayBuffer()));
          } catch {
            // A file that changed or went away after it was picked (a phone's cloud file).
            entry.unreadable = true;
          }
          return entry;
        }));
        if (!stillOpen()) return;
        const { accepted, refused } = core.checkPhotos(entries, photosInCollage());
        // One at a time, so a pile of large phone photos is never all decoded at once. Each
        // working copy is sized for every photo the collage will keep after this batch.
        const willKeep = doc.photos.size + accepted.length;
        // Copies made while the collage had fewer photos are made smaller first, so the budget
        // holds while the batch is decoded, not only after it; the page is drawn again below.
        if (accepted.length) photoReader.fitBudget(doc.photos, willKeep);
        for (const entry of accepted) {
          let photo;
          try {
            photo = await readPhoto(entry, null, { count: willKeep });
          } catch (error) {
            refused.push({ name: entry.name, reason: error?.reason === 'too-many-pixels' ? 'too-many-pixels' : 'unreadable' });
            continue;
          }
          // The collage was closed while it was read: its working copy is let go at once.
          if (!stillOpen()) { window.PrismCollagePhotos.releasePhoto(photo); return; }
          // Not placed yet: it cannot be removed from the tray until the whole batch is in.
          photo.importing = true;
          doc.photos.set(photo.id, photo);
          added.push(photo);
        }
        // A photo removed meanwhile (it cannot be, but a closed collage drops them all) stays out.
        const placing = added.filter((photo) => doc.photos.get(photo.id) === photo);
        placing.forEach((photo) => { photo.importing = false; });
        // The first photo goes where it was aimed (a frame, or the drop point); the rest fill
        // empty frames, then go near the centre.
        // Photos placed freely are laid out in a grid for all of them at once (`among`: those still
        // to come in this batch, this one included).
        // On a layout, the batch is matched to the empty frames by shape (tall to tall) first.
        const matched = !frameId && !at && state.collage.layout ? core.assignToSlots(state.collage.items, placing) : null;
        // A photo the matching gave no frame waits in the tray; it never takes a frame meant for another.
        const waiting = placing.filter((photo, index) => (matched && !matched.has(photo.id)) || !placePhoto(photo, {
          ...(index === 0 ? { frameId, at } : {}),
          ...(matched?.has(photo.id) ? { frameId: matched.get(photo.id) } : {}),
          among: placing.length - index
        }));
        if (placing.length) {
          setTarget(null);
          history.commit();
        }
        // Photos past the layout's frames wait in the tray rather than sit loose on the grid.
        const fitNote = waiting.length
          ? `${waiting.length === 1 ? 'One photo' : `${waiting.length} photos`} did not fit the layout's frames and ${waiting.length === 1 ? 'is' : 'are'} in Photos: drag ${waiting.length === 1 ? 'it' : 'one'} onto a frame, or choose a layout with more frames.`
          : '';
        messages.showAlert([core.refusedMessage(refused), fitNote].filter(Boolean).join(' '));
        if (placing.length) messages.announce(placing.length === 1 ? 'Photo added.' : `${placing.length} photos added.`);
      } finally {
        messages.busy('');
        for (const photo of added) photo.importing = false;
      }
    }

    // From PRISM Photos (collage-library-dialog.js): photos the user can see in /Photos, read in as
    // files and added like photos from the computer.
    const libraryPicker = window.PrismCollageLibraryDialog.createLibraryDialog({
      root: ui.root,
      library,
      core,
      busy: messages.busy,
      showAlert: messages.showAlert,
      addFiles,
      photoCount: () => photosInCollage(),
      session: () => state.collageSession,
      frameTarget: () => {
        const selected = doc.selectedItem();
        return state.targetId || (selected?.kind === 'slot' ? selected.id : null);
      },
      currentAlert: () => ui.$('[data-alert-text]').textContent
    });

    ui.fileInput.addEventListener('change', () => {
      const files = [...ui.fileInput.files];
      ui.fileInput.value = '';
      // An empty frame that is selected takes the photo, as it does from the tray or a paste.
      const selected = doc.selectedItem();
      addFiles(files, { frameId: state.targetId || (selected?.kind === 'slot' ? selected.id : null) });
    });

    function renderTray() {
      messages.keepingFocus(() => renderTrayNow());
    }

    function renderTrayNow() {
      // Both sides of a card count: a photo on the back is in use, and cannot be removed here.
      const used = new Map();
      for (const item of [...state.collage.items, ...(state.collage.back?.items || [])]) if (item.kind === 'photo') used.set(item.photo, (used.get(item.photo) || 0) + 1);
      const background = state.collage.background?.type === 'photo' ? state.collage.background.photo
        : state.collage.back?.background?.type === 'photo' ? state.collage.back.background.photo : null;
      ui.tray.textContent = '';
      for (const photo of doc.photos.values()) {
        if (photo.removed) continue;
        const tile = document.createElement('div');
        tile.className = 'clg-tray__item';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'clg-tray__photo';
        button.draggable = true;
        button.dataset.trayPhoto = photo.id;
        button.title = `Add “${photo.name}” to the page`;
        button.setAttribute('aria-label', `Add ${photo.name} to the page`);
        const img = document.createElement('img');
        img.src = photo.thumbnail;
        img.alt = '';
        button.appendChild(img);
        const times = used.get(photo.id) || 0;
        if (times || photo.id === background) {
          const badge = document.createElement('span');
          badge.className = 'clg-tray__used';
          badge.textContent = times > 1 ? `${times}×` : times ? 'Used' : 'Background';
          button.appendChild(badge);
        }
        tile.appendChild(button);
        // Only a photo the page does not show can be taken out of the collage.
        if (!times && photo.id !== background && !photo.importing) {
          const remove = document.createElement('button');
          remove.type = 'button';
          remove.className = 'clg-tray__remove';
          remove.dataset.removePhoto = photo.id;
          remove.title = 'Remove from this collage';
          remove.setAttribute('aria-label', `Remove ${photo.name} from this collage`);
          remove.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
          tile.appendChild(remove);
        }
        ui.tray.appendChild(tile);
      }
      const count = photosInCollage();
      doc.photoCount.textContent = count ? `${count} of ${core.PHOTO_LIMITS.maxPhotos} photos` : '';
    }

    /** Photos in this collage: the tray's, without ones removed. */
    function photosInCollage() {
      let count = 0;
      for (const photo of doc.photos.values()) if (!photo.removed) count += 1;
      return count;
    }

    /** The photos the draft keeps: every photo not removed, and any the page still shows. */
    function keptPhotos() {
      const inUse = core.photosInUse(state.collage);
      return [...doc.photos.values()].filter((photo) => !photo.removed || inUse.has(photo.id));
    }

    /**
     * Takes a photo the page does not show out of the collage: out of the tray, the photo count and
     * the draft. While Undo can still bring back a step that shows it, it is kept in memory.
     */
    function removePhoto(id) {
      const photo = doc.photos.get(id);
      if (!photo || photo.importing || core.photosInUse(state.collage).has(id)) return;
      photo.removed = true;
      forgetRemovedPhotos();
      history.scheduleDraft();
      renderTray();
      messages.announce(`${photo.name} removed from this collage.`);
      // Focus goes to the next photo in the tray, or to "Add photos" when none is left.
      (ui.tray.querySelector('[data-tray-photo]') || ui.$('[data-action="choose"]'))?.focus();
    }

    /**
     * Lets go of removed photos that no Undo or Redo step shows any more. Called when a photo is
     * removed and after every Undo step is recorded, since a new step can drop the Redo steps or
     * the oldest step that still showed one.
     */
    function forgetRemovedPhotos() {
      window.PrismCollagePhotos.forgetUnreachable(doc.photos, history.changes.mentions);
    }

    ui.tray.addEventListener('click', (event) => {
      const removing = event.target.closest('[data-remove-photo]');
      if (removing) { removePhoto(removing.dataset.removePhoto); return; }
      const button = event.target.closest('[data-tray-photo]');
      const photo = button && doc.photos.get(button.dataset.trayPhoto);
      if (!photo) return;
      const selected = doc.selectedItem();
      // An empty frame that is selected takes the photo; so does the frame being replaced.
      const frameId = state.targetId || (selected?.kind === 'slot' ? selected.id : null);
      // Every frame of the layout has a photo: nothing changes, and the user is told how to make room.
      if (!placePhoto(photo, { frameId })) {
        messages.showAlert('Every frame of this layout has a photo. Select a photo and use Replace, delete one to empty its frame, or choose a layout with more frames.');
        return;
      }
      setTarget(null);
      history.commit();
      messages.announce(frameId ? 'Photo placed in the frame.' : 'Photo added.');
    });

    ui.tray.addEventListener('dragstart', (event) => {
      const button = event.target.closest('[data-tray-photo]');
      if (button) event.dataTransfer.setData('text/x-collage-photo', button.dataset.trayPhoto);
    });

    /** The page point under a drop, or null when it is off the page. */
    function pagePoint(event) {
      surface.stage.setPointersPositions(event);
      const point = surface.world.getRelativePointerPosition();
      if (!point) return null;
      if (point.x < 0 || point.y < 0 || point.x > core.PAGE_WIDTH || point.y > doc.pageHeight()) return null;
      return point;
    }

    /** The empty frame under a drop, so a photo dropped on it fills it. */
    function slotAt(event) {
      surface.stage.setPointersPositions(event);
      const position = surface.stage.getPointerPosition();
      let shape = position && surface.stage.getIntersection(position);
      while (shape && shape !== surface.stage) {
        const item = doc.itemById(shape.id());
        if (item) return item.kind === 'slot' ? item : null;
        shape = shape.getParent();
      }
      return null;
    }

    ui.stageBox.addEventListener('dragover', (event) => {
      const types = [...event.dataTransfer.types];
      if (!types.includes('Files') && !types.includes('text/x-collage-photo')) return;
      event.preventDefault();
      ui.stageBox.classList.add('is-dropping');
    });
    ui.stageBox.addEventListener('dragleave', (event) => {
      if (!ui.stageBox.contains(event.relatedTarget)) ui.stageBox.classList.remove('is-dropping');
    });
    ui.stageBox.addEventListener('drop', (event) => {
      ui.stageBox.classList.remove('is-dropping');
      const fromTray = event.dataTransfer.getData('text/x-collage-photo');
      if (!fromTray && !event.dataTransfer.files.length) return;
      event.preventDefault();
      const slot = slotAt(event);
      const at = slot ? null : pagePoint(event);
      if (fromTray) {
        const photo = doc.photos.get(fromTray);
        if (photo) {
          // Dropped beside the page with every frame full: nothing changes, and the user is told.
          if (!placePhoto(photo, { frameId: slot?.id || null, at })) {
            messages.showAlert('Every frame of this layout has a photo. Select a photo and use Replace, delete one to empty its frame, or choose a layout with more frames.');
            return;
          }
          history.commit();
          messages.announce(slot ? 'Photo placed in the frame.' : 'Photo added.');
        }
        return;
      }
      addFiles(event.dataTransfer.files, { frameId: slot?.id || null, at });
    });

    document.addEventListener('paste', (event) => {
      if (ui.views.editor.hidden || state.leaving) return;
      if (event.target.closest?.('input, textarea, [contenteditable]')) return;
      // Paste waits while a dialog (Save, Credits, PRISM Photos, a question) is open.
      if (ui.root.querySelector('dialog[open]') || document.querySelector('.modal.show')) return;
      const files = [...(event.clipboardData?.files || [])].filter((file) => file.type.startsWith('image/'));
      if (!files.length) {
        messages.showAlert('There is no photo on the clipboard. Copy a photo or a screenshot first.');
        return;
      }
      event.preventDefault();
      const selected = doc.selectedItem();
      addFiles(files, { frameId: state.targetId || (selected?.kind === 'slot' ? selected.id : null) });
    });


    /** Working copies made smaller first, so `count` photos stay within the pixel budget. */
    const fitBudget = (count) => photoReader.fitBudget(doc.photos, count);
    return { setTarget, chooseFor, readPhoto, missingPhoto, libraryPicker, renderTray, keptPhotos, forgetRemovedPhotos, photosInCollage, fitBudget, afterImports };
  }

  return Object.freeze({ createTray });
});
