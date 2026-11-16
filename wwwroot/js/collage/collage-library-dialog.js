// PRISM Collage Studio — From PRISM Photos: pick photos the user can see in /Photos. They are
// read into the browser as files and go through exactly the same checks and steps as photos
// from the computer (docs/ux/collage-studio-library-and-unit-stickers.md, PR 6; moved out of the
// editor in docs/ux/collage-review-fixes.md, PR 12).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageLibraryDialog = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /**
   * root is the page; library is collage-library.js; addFiles(files, { frameId }) adds files like
   * photos from the computer; photoCount() is how many photos the collage has; session() changes
   * when the collage is left; frameTarget() is the empty frame aimed at, if any; currentAlert()
   * is the page's alert text, if one is shown.
   */
  function createLibraryDialog({ root, library, core, busy, showAlert, addFiles, photoCount, session, frameTarget, currentAlert }) {
    const libraryDialog = root.querySelector('[data-library-dialog]');
    const libraryGrid = libraryDialog?.querySelector('[data-library-grid]');
    const library_ = {
      filters: { q: '', projectId: '', year: '' },
      page: 0,
      hasMore: false,
      listed: [],
      chosen: new Map(),
      request: 0,
      listsFilled: false
    };
    let libraryTyping = 0;

    function openLibrary() {
      if (!libraryDialog || !library) return;
      library_.chosen.clear();
      renderLibraryChosen();
      libraryDialog.showModal();
      if (!library_.page) loadLibrary(true);
      else renderLibraryGrid();
      libraryDialog.querySelector('[data-library-q]').focus();
    }

    /** Room left for photos in this collage, less the ones already ticked. */
    const libraryRoom = () => Math.max(0, core.PHOTO_LIMITS.maxPhotos - photoCount());

    /** Reads the first page for the current filters (reset), or the next page. */
    async function loadLibrary(reset) {
      const asked = reset ? 1 : library_.page + 1;
      const request = ++library_.request;
      const status = libraryDialog.querySelector('[data-library-status]');
      status.textContent = 'Getting photos from PRISM Photos…';
      libraryDialog.querySelector('[data-action="library-more"]').hidden = true;
      let page;
      try {
        page = await library.loadLibraryPage({ ...library_.filters, page: asked });
      } catch {
        if (request !== library_.request) return;
        status.textContent = 'PRISM Photos cannot be read right now. Try again in a minute, or add photos from your computer.';
        return;
      }
      // A newer search has started; this answer is for the old one.
      if (request !== library_.request) return;
      // Off: only the message shows, without search, filters or an Add that can do nothing.
      libraryDialog.classList.toggle('is-off', !page.available);
      if (!page.available) {
        library_.listed = [];
        library_.page = 0;
        renderLibraryGrid();
        status.textContent = page.message;
        return;
      }
      if (page.projects && !library_.listsFilled) fillLibraryLists(page.projects, page.years || []);
      // A page past the end comes back as the last page: add only the page that was asked for.
      if (reset) library_.listed = page.photos;
      else if (page.page === asked) library_.listed = library_.listed.concat(page.photos);
      library_.page = reset ? 1 : (page.page === asked ? asked : library_.page);
      library_.hasMore = page.hasMore && page.page === asked;
      renderLibraryGrid();
      status.textContent = library_.listed.length ? '' : (library_.filters.q || library_.filters.projectId || library_.filters.year
        ? 'No photo matches. Try other words, or another project or year.'
        : 'There are no photos in PRISM Photos yet.');
    }

    function fillLibraryLists(projects, years) {
      library_.listsFilled = true;
      const projectList = libraryDialog.querySelector('[data-library-project]');
      for (const project of projects) {
        if (!Number.isInteger(project?.id)) continue;
        projectList.add(new Option(String(project.name || `Project ${project.id}`), String(project.id)));
      }
      const yearList = libraryDialog.querySelector('[data-library-year]');
      for (const year of years) {
        if (Number.isInteger(year)) yearList.add(new Option(String(year), String(year)));
      }
    }

    function renderLibraryGrid() {
      const room = libraryRoom();
      libraryGrid.replaceChildren(...library_.listed.map((photo) => {
        const tile = document.createElement('button');
        tile.type = 'button';
        tile.className = 'clg-library-tile';
        tile.dataset.libraryPhoto = String(photo.id);
        const chosen = library_.chosen.has(photo.id);
        tile.setAttribute('aria-pressed', String(chosen));
        const label = [photo.title, photo.context].filter(Boolean).join(' · ');
        tile.title = label;
        tile.setAttribute('aria-label', label || 'Photo');
        tile.disabled = !chosen && library_.chosen.size >= room;
        const img = document.createElement('img');
        img.alt = '';
        img.loading = 'lazy';
        img.src = String(photo.thumbUrl || '');
        const tick = document.createElement('span');
        tick.className = 'clg-library-tile__tick';
        tick.setAttribute('aria-hidden', 'true');
        tick.innerHTML = '<i class="bi bi-check-lg"></i>';
        tile.append(img, tick);
        return tile;
      }));
      libraryDialog.querySelector('[data-action="library-more"]').hidden = !library_.hasMore;
    }

    function renderLibraryChosen() {
      if (!libraryDialog) return;
      const count = library_.chosen.size;
      const room = libraryRoom();
      libraryDialog.querySelector('[data-library-chosen]').textContent = !room
        ? `This collage already has ${core.PHOTO_LIMITS.maxPhotos} photos.`
        : count === 0 ? 'None ticked'
          : count >= room ? `${count} ticked, the most this collage can take`
            : count === 1 ? '1 ticked' : `${count} ticked`;
      libraryDialog.querySelector('[data-action="library-add"]').disabled = count === 0;
    }

    libraryDialog?.addEventListener('click', (event) => {
      const tile = event.target.closest('[data-library-photo]');
      if (!tile || tile.disabled) return;
      const id = Number(tile.dataset.libraryPhoto);
      if (library_.chosen.has(id)) library_.chosen.delete(id);
      else {
        const photo = library_.listed.find((item) => item.id === id);
        if (photo && library_.chosen.size < libraryRoom()) library_.chosen.set(id, photo);
      }
      renderLibraryGrid();
      renderLibraryChosen();
      libraryGrid.querySelector(`[data-library-photo="${id}"]`)?.focus();
    });

    libraryDialog?.querySelector('[data-library-q]').addEventListener('input', (event) => {
      window.clearTimeout(libraryTyping);
      libraryTyping = window.setTimeout(() => {
        library_.filters.q = event.target.value.trim();
        loadLibrary(true);
      }, 350);
    });
    for (const [selector, key] of [['[data-library-project]', 'projectId'], ['[data-library-year]', 'year']]) {
      libraryDialog?.querySelector(selector).addEventListener('change', (event) => {
        library_.filters[key] = event.target.value;
        loadLibrary(true);
      });
    }

    /** Reads the ticked photos one at a time, then adds them like photos from the computer. */
    async function addFromLibrary() {
      const chosen = [...library_.chosen.values()];
      if (!chosen.length) return;
      libraryDialog.close();
      const forCollage = session();
      const frameId = frameTarget();
      const files = [];
      const failed = [];
      const tooBig = [];
      const tooLarge = [];
      busy(chosen.length === 1 ? 'Getting the photo from PRISM Photos…' : `Getting ${chosen.length} photos from PRISM Photos…`);
      try {
        for (const photo of chosen) {
          // Refused before it is downloaded, as the same photo from the computer would be.
          if (Number(photo.width) * Number(photo.height) > core.PHOTO_LIMITS.maxPixels) {
            tooBig.push(photo.title || 'A photo');
            continue;
          }
          try {
            files.push(await library.readLibraryPhoto(photo, { maxBytes: core.PHOTO_LIMITS.maxBytes }));
          } catch (error) {
            (error?.reason === 'too-large' ? tooLarge : failed).push(photo.title || 'A photo');
          }
          if (forCollage !== session()) return;
        }
      } finally {
        busy('');
      }
      library_.chosen.clear();
      renderLibraryChosen();
      if (files.length) await addFiles(files, { frameId });
      if (forCollage !== session() || (!tooBig.length && !tooLarge.length && !failed.length)) return;
      // One message for everything left out, after the import's own message if it had one.
      const notes = [currentAlert()];
      if (tooBig.length) notes.push(core.refusedMessage(tooBig.map((name) => ({ name, reason: 'too-many-pixels' }))));
      if (tooLarge.length) notes.push(core.refusedMessage(tooLarge.map((name) => ({ name, reason: 'too-large' }))));
      if (failed.length) {
        const names = failed.length === 1 ? `“${failed[0]}”` : `${failed.length} photos`;
        notes.push(`${names} could not be read from PRISM Photos. Try again, or add ${failed.length === 1 ? 'it' : 'them'} from your computer.`);
      }
      showAlert(notes.filter(Boolean).join(' '));
    }


    return Object.freeze({
      open: openLibrary,
      close: () => libraryDialog?.close(),
      more: () => loadLibrary(false),
      add: addFromLibrary
    });
  }

  return Object.freeze({ createLibraryDialog });
});
