// PRISM Collage Studio — photos: choosing, dropping and pasting them, reading them in, the tray, and From PRISM Photos
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the helpers it uses and `app`, the editor's view of its own state.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageTray = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createTray(deps) {
    const { app, $, root, core, library, views, fileInput, stageBox, tray, photoCount, replaceBanner, alertBox, changes, photos, nextId, newItemId, itemById, selectedItem, pageHeight, firstSlot, keepingFocus, announce, showAlert, busy, session, scheduleDraft, stage, world, layout, commit, setPanel } = deps;
    // ---------------------------------------------------------------------------
    // Photos: choose, drop, paste, and the tray
    // ---------------------------------------------------------------------------

    /**
     * The frame the next photo goes into: shown in the Photos panel ("Choose the photo for the
     * selected frame") until a photo is chosen or Cancel is pressed.
     */
    function setTarget(id) {
      app.targetId = id;
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
    /**
     * Puts a photo on the page: in the frame it was aimed at, else the empty frame closest to its
     * shape, else (on a page without a layout, or dropped at a point) on its own. On a layout with
     * every frame full it stays in the tray, and false is returned.
     */
    function placePhoto(photo, { frameId = null, at = null } = {}) {
      const frame = (frameId && itemById(frameId)) || (!at ? (core.bestSlotFor(app.collage.items, photo.aspect) || firstSlot()) : null);
      if (!frame && !at && app.collage.layout) return false;
      if (frame && frame.kind === 'slot') {
        const filled = core.fillSlot(frame, photo.id);
        app.collage.items[app.collage.items.indexOf(frame)] = filled;
        app.selectedId = filled.id;
        return true;
      }
      if (frame && frame.kind === 'photo') {
        frame.photo = photo.id;
        frame.crop = { ...core.FULL_CROP };
        app.selectedId = frame.id;
        return true;
      }
      // A photo placed freely means the page is no longer a strict grid, so changing the space
      // would not silently take it off the page.
      app.collage.layout = null;
      const count = app.collage.items.filter((i) => i.kind === 'photo').length;
      const place = core.placeNewPhoto(app.collage.size, photo.aspect, count);
      if (at) { place.x = at.x; place.y = at.y; }
      const item = core.photoItem(newItemId(), photo.id, photo.aspect, place);
      app.collage.items.push(item);
      app.selectedId = item.id;
      return true;
    }

    // Reading photos at their working size (collage-photos.js).
    const photoReader = window.PrismCollagePhotos.createPhotoReader({ core, nextId });
    const { readPhoto, missingPhoto, thumbnailOf } = photoReader;

    // Imports run one batch at a time: a second drop or paste waits for the first, so the photo
    // limit counts every photo already added and only one large photo is ever being decoded.
    let importQueue = Promise.resolve();

    function addFiles(fileList, options = {}) {
      const files = [...fileList];
      if (!files.length) return importQueue;
      const forCollage = app.collageSession;
      importQueue = importQueue.then(() => importFiles(files, options, forCollage)).catch(() => {
        if (forCollage === app.collageSession) showAlert('The photos could not be added. Try again, or add them one at a time.');
      });
      return importQueue;
    }

    async function importFiles(files, { at = null, frameId = null } = {}, forCollage) {
      // A collage left before its photos were read gets none of them, and neither does the next.
      const stillOpen = () => forCollage === app.collageSession;
      if (!stillOpen()) return;
      showAlert('');
      busy(files.length === 1 ? 'Adding the photo…' : `Adding ${files.length} photos…`);
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
          // Not placed yet: it cannot be removed from the tray until the whole batch is in.
          photo.importing = true;
          photos.set(photo.id, photo);
          added.push(photo);
        }
        // A photo removed meanwhile (it cannot be, but a closed collage drops them all) stays out.
        const placing = added.filter((photo) => photos.get(photo.id) === photo);
        placing.forEach((photo) => { photo.importing = false; });
        // The first photo goes where it was aimed (a frame, or the drop point); the rest fill
        // empty frames, then go near the centre.
        const waiting = placing.filter((photo, index) => !placePhoto(photo, index === 0 ? { frameId, at } : {}));
        if (placing.length) {
          setTarget(null);
          commit();
        }
        // Photos past the layout's frames wait in the tray rather than sit loose on the grid.
        const fitNote = waiting.length
          ? `${waiting.length === 1 ? 'One photo' : `${waiting.length} photos`} did not fit the layout's frames and ${waiting.length === 1 ? 'is' : 'are'} in Photos: drag ${waiting.length === 1 ? 'it' : 'one'} onto a frame, or choose a layout with more frames.`
          : '';
        showAlert([core.refusedMessage(refused), fitNote].filter(Boolean).join(' '));
        if (placing.length) announce(placing.length === 1 ? 'Photo added.' : `${placing.length} photos added.`);
      } finally {
        busy('');
        for (const photo of added) photo.importing = false;
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
      photoCount: () => photosInCollage(),
      session: () => app.collageSession,
      frameTarget: () => {
        const selected = selectedItem();
        return app.targetId || (selected?.kind === 'slot' ? selected.id : null);
      },
      currentAlert: () => (alertBox.hidden ? '' : $('[data-alert-text]').textContent)
    });

    fileInput.addEventListener('change', () => {
      const files = [...fileInput.files];
      fileInput.value = '';
      // An empty frame that is selected takes the photo, as it does from the tray or a paste.
      const selected = selectedItem();
      addFiles(files, { frameId: app.targetId || (selected?.kind === 'slot' ? selected.id : null) });
    });

    function renderTray() {
      keepingFocus(() => renderTrayNow());
    }

    function renderTrayNow() {
      const used = new Map();
      for (const item of app.collage.items) if (item.kind === 'photo') used.set(item.photo, (used.get(item.photo) || 0) + 1);
      const background = app.collage.background?.type === 'photo' ? app.collage.background.photo : null;
      tray.textContent = '';
      for (const photo of photos.values()) {
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
        tray.appendChild(tile);
      }
      const count = photosInCollage();
      photoCount.textContent = count ? `${count} of ${core.PHOTO_LIMITS.maxPhotos} photos` : '';
    }

    /** Photos in this collage: the tray's, without ones removed. */
    function photosInCollage() {
      let count = 0;
      for (const photo of photos.values()) if (!photo.removed) count += 1;
      return count;
    }

    /** The photos the draft keeps: every photo not removed, and any the page still shows. */
    function keptPhotos() {
      const inUse = core.photosInUse(app.collage);
      return [...photos.values()].filter((photo) => !photo.removed || inUse.has(photo.id));
    }

    /**
     * Takes a photo the page does not show out of the collage: out of the tray, the photo count and
     * the draft. While Undo can still bring back a step that shows it, it is kept in memory.
     */
    function removePhoto(id) {
      const photo = photos.get(id);
      if (!photo || photo.importing || core.photosInUse(app.collage).has(id)) return;
      photo.removed = true;
      forgetRemovedPhotos();
      scheduleDraft();
      renderTray();
      announce(`${photo.name} removed from this collage.`);
      // Focus goes to the next photo in the tray, or to "Add photos" when none is left.
      (tray.querySelector('[data-tray-photo]') || $('[data-action="choose"]'))?.focus();
    }

    /** Lets go of removed photos that no Undo or Redo step shows any more. */
    function forgetRemovedPhotos() {
      for (const photo of [...photos.values()]) {
        if (!photo.removed || changes.mentions(photo.id)) continue;
        photos.delete(photo.id);
      }
    }

    tray.addEventListener('click', (event) => {
      const removing = event.target.closest('[data-remove-photo]');
      if (removing) { removePhoto(removing.dataset.removePhoto); return; }
      const button = event.target.closest('[data-tray-photo]');
      const photo = button && photos.get(button.dataset.trayPhoto);
      if (!photo) return;
      const selected = selectedItem();
      // An empty frame that is selected takes the photo; so does the frame being replaced.
      const frameId = app.targetId || (selected?.kind === 'slot' ? selected.id : null);
      // Every frame of the layout has a photo: nothing changes, and the user is told how to make room.
      if (!placePhoto(photo, { frameId })) {
        showAlert('Every frame of this layout has a photo. Select a photo and use Replace, delete one to empty its frame, or choose a layout with more frames.');
        return;
      }
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
      if (views.editor.hidden || app.leaving) return;
      if (event.target.closest?.('input, textarea, [contenteditable]')) return;
      // Paste waits while a dialog (Save, Credits, PRISM Photos, a question) is open.
      if (root.querySelector('dialog[open]') || document.querySelector('.modal.show')) return;
      const files = [...(event.clipboardData?.files || [])].filter((file) => file.type.startsWith('image/'));
      if (!files.length) {
        showAlert('There is no photo on the clipboard. Copy a photo or a screenshot first.');
        return;
      }
      event.preventDefault();
      const selected = selectedItem();
      addFiles(files, { frameId: app.targetId || (selected?.kind === 'slot' ? selected.id : null) });
    });


    return { setTarget, chooseFor, readPhoto, missingPhoto, libraryPicker, renderTray, keptPhotos };
  }

  return Object.freeze({ createTray });
});
