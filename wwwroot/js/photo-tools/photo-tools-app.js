// PRISM Photo Tools — browser application shell and the Reduce size tool.
//
// Photos are read with the File API, drawn on a canvas and saved with a blob: link. Nothing
// is sent to the server. Saving through a canvas also leaves out the hidden data in a photo
// (location, camera details).
(() => {
  'use strict';

  const root = document.querySelector('[data-photo-tools]');
  const core = window.PrismPhotoTools;
  const zip = window.PrismZipWriter;
  const shared = window.PrismPhotoCanvas;
  if (!root || !core || !zip || !shared) {
    return;
  }

  const SETTINGS_KEY = 'prism.photoTools.reduce';
  const SETTLE_MS = 350;
  // Thumbnails are drawn once at a small size: showing every original in the list would decode
  // each full photo again on every update.
  const THUMB_SIDE = 144;

  const $ = (selector, scope = document) => scope.querySelector(selector);
  const $$ = (selector, scope = document) => Array.from(scope.querySelectorAll(selector));

  const errorBox = $('[data-error]');
  const { showError, clearError, saveBlob, openPhoto, sizeOf, drawPhoto, canvasToBytes } = shared;
  const fileInput = $('[data-file-input]');

  // ---------------------------------------------------------------------------
  // Views
  // ---------------------------------------------------------------------------

  const views = $$('[data-view]');
  let keepErrorOnce = false;

  // Screens that open another screen in a set way: Signature is Passport photo in Signature mode.
  const VIEW_ALIASES = { signature: 'passport' };

  function showView(name, { focus = true } = {}) {
    const wanted = VIEW_ALIASES[name] || name;
    const target = views.find(view => view.dataset.view === wanted) || views.find(view => view.dataset.view === 'home');
    for (const view of views) {
      view.hidden = view !== target;
    }
    if (!keepErrorOnce) clearError();
    keepErrorOnce = false;
    const heading = $('h1', target);
    document.title = target.dataset.view === 'home'
      ? 'Photo Tools | PRISM ERP'
      : `${heading?.textContent?.trim()} | Photo Tools`;
    window.scrollTo({ top: 0 });
    // The screen changed under the keyboard: start on its heading, which is read out, so the
    // next Tab is in the tool rather than back at the top of the page.
    if (focus && heading) {
      heading.tabIndex = -1;
      heading.focus({ preventScroll: true });
    }
  }

  function currentView() {
    return window.location.hash.replace(/^#/, '') || 'home';
  }

  window.addEventListener('hashchange', () => showView(currentView()));

  for (const link of $$('[data-nav-home]')) {
    link.addEventListener('click', (event) => {
      event.preventDefault();
      if (currentView() !== 'home') {
        window.location.hash = '';
      }
      showView('home');
    });
  }

  // ---------------------------------------------------------------------------
  // Reduce size
  // ---------------------------------------------------------------------------

  const view = $('[data-view="reduce"]');
  const emptyDrop = $('[data-empty]', view);
  const workspace = $('[data-workspace]', view);
  const rows = $('[data-rows]', view);
  const addButton = $('[data-action="add-files"]', view);
  const downloadButton = $('[data-action="download"]', view);
  const downloadLabel = $('[data-download-label]', view);
  const compare = $('[data-compare]', view);
  const compareBefore = $('[data-compare-before]', view);
  const compareAfter = $('[data-compare-after]', view);
  const compareLine = $('[data-compare-line]', view);
  const compareSlider = $('[data-compare-slider]', view);
  const targetValue = $('[data-target-value]', view);
  const unitButtons = $$('[data-target-unit]', view);
  const presetButtons = $$('[data-target-preset]', view);
  const dimensions = $('[data-dimensions]', view);
  const customSide = $('[data-custom-side]', view);
  const customSideValue = $('[data-custom-side-value]', view);
  const format = $('[data-format]', view);
  const greyscale = $('[data-greyscale]', view);
  const pngHint = $('[data-png-hint]', view);

  /** @type {{ id: number, file: File, url: string, thumb: HTMLImageElement, thumbUrl?: string, status: string, result?: object, error?: string }[]} */
  let photos = [];
  let nextId = 1;
  let selectedId = null;
  let unit = 'KB';
  let run = 0;
  let working = false;
  let settleTimer = null;
  // True from a change of settings until the user has stopped changing them: the worker does
  // not start the next photo meanwhile, so a half-typed size ("1" of "100") is never used.
  let settling = false;

  function loadSettings() {
    try {
      const saved = JSON.parse(window.localStorage.getItem(SETTINGS_KEY) || 'null');
      if (!saved) return;
      if (typeof saved.target === 'string') targetValue.value = saved.target;
      if (saved.unit === 'KB' || saved.unit === 'MB') unit = saved.unit;
      if ($$('option', dimensions).some(option => option.value === saved.dimensions)) dimensions.value = saved.dimensions;
      if (Number(saved.customSide) >= 16) customSideValue.value = String(saved.customSide);
      if ($$('option', format).some(option => option.value === saved.format)) format.value = saved.format;
      greyscale.checked = saved.greyscale === true;
    } catch {
      // Settings are a convenience; the defaults in the page are used instead.
    }
  }

  function saveSettings() {
    try {
      window.localStorage.setItem(SETTINGS_KEY, JSON.stringify({
        target: targetValue.value.trim(),
        unit,
        dimensions: dimensions.value,
        customSide: Number(customSideValue.value),
        format: format.value,
        greyscale: greyscale.checked
      }));
    } catch {
      // Private windows may refuse storage; nothing depends on it.
    }
  }

  function readSettings() {
    let resize = { type: 'keep' };
    const [kind, amount] = dimensions.value.split(':');
    if (kind === 'longest' || kind === 'percent') {
      resize = { type: kind, value: Number(amount) };
    } else if (kind === 'custom') {
      const side = Math.round(Number(customSideValue.value));
      resize = side >= 16 ? { type: 'longest', value: side } : { type: 'keep' };
    }
    return {
      targetBytes: core.parseTargetBytes(targetValue.value, unit),
      targetText: `${targetValue.value.trim()} ${unit}`,
      resize,
      format: format.value,
      greyscale: greyscale.checked
    };
  }

  function syncControls() {
    for (const button of unitButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.targetUnit === unit));
    }
    const current = `${targetValue.value.trim()}:${unit}`;
    for (const button of presetButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.targetPreset === current));
    }
    customSide.hidden = dimensions.value !== 'custom';
    // A size limit on a PNG costs far more detail than on a JPG; say so before the user is surprised.
    const writesPng = format.value === 'png'
      || (format.value === 'same' && photos.some(photo => photo.file.type === 'image/png'));
    pngHint.hidden = !(writesPng && core.parseTargetBytes(targetValue.value, unit));
  }

  // ----- Work -----

  /** Draws the list thumbnail from a photo that is already open. A thumbnail is only a convenience. */
  async function makeThumbnail(photo, source, original) {
    if (photo.thumbUrl) return;
    try {
      const fit = core.resizeDimensions(original.width, original.height, { type: 'longest', value: THUMB_SIDE });
      const bytes = await canvasToBytes(drawPhoto(source, fit.width, fit.height, { opaque: true }), core.TYPES.jpeg, 0.8);
      if (photo.thumbUrl || !photos.includes(photo)) return;
      photo.thumbUrl = URL.createObjectURL(new Blob([bytes], { type: core.TYPES.jpeg.mime }));
      photo.thumb.src = photo.thumbUrl;
    } catch {
      // The row simply shows no picture.
    }
  }

  // Thrown between two tries when the photo is no longer wanted as it is being made.
  const STOPPED = Symbol('stopped');

  /**
   * Reduces one photo. `wanted()` is asked before every try: when the settings change or the
   * photo is removed, the work stops there instead of finishing a result that is thrown away.
   */
  async function reducePhoto(photo, settings, wanted = () => true) {
    const source = await openPhoto(photo.file);
    try {
      const original = sizeOf(source);
      await makeThumbnail(photo, source, original);
      const start = core.resizeDimensions(original.width, original.height, settings.resize);
      const type = core.outputType(photo.file.type, settings.format);
      let drawn = null;
      const encode = (width, height, quality) => {
        if (!wanted()) throw STOPPED;
        if (!drawn || drawn.width !== width || drawn.height !== height) {
          drawn = drawPhoto(source, width, height, { greyscale: settings.greyscale, opaque: type.mime === 'image/jpeg' });
        }
        return canvasToBytes(drawn, type, quality);
      };
      let result = await core.encodeToTarget({
        encode,
        width: start.width,
        height: start.height,
        lossy: type.lossy,
        targetBytes: settings.targetBytes
      });
      // A photo that was already small can come out bigger at top quality. Saved in its own
      // type, a lower quality at the same dimensions gets it under the original.
      const under = core.retryUnderOriginal({
        resultBytes: result.bytes.length,
        originalBytes: photo.file.size,
        lossy: type.lossy,
        sameType: type.mime === photo.file.type
      });
      if (under) {
        const again = await core.encodeToTarget({
          encode,
          width: result.width,
          height: result.height,
          lossy: true,
          targetBytes: under,
          fixedSize: true
        });
        if (again.reached) result = { ...again, reached: result.reached };
      }
      return { ...result, type, bigger: result.bytes.length >= photo.file.size };
    } finally {
      if (typeof source.close === 'function') source.close();
    }
  }

  /**
   * Reduces every photo that is waiting, one at a time. There is only ever one worker:
   * photos added or settings changed while it runs are picked up by the same loop, so a
   * photo can never be left on "Working…" by a second run starting.
   */
  async function work() {
    if (working) return;
    working = true;
    try {
      for (;;) {
        if (settling) break;
        const photo = photos.find(item => item.status === 'waiting');
        if (!photo) break;
        const mine = run;
        photo.status = 'working';
        render();
        let result = null;
        let failure = null;
        try {
          result = await reducePhoto(photo, readSettings(), () => mine === run && photos.includes(photo));
        } catch (error) {
          failure = error;
        }
        // Settings changed meanwhile (the photo is waiting again) or the photo was removed.
        if (mine !== run || !photos.includes(photo)) continue;
        if (failure) {
          if (!(failure instanceof core.PhotoToolError)) console.error(failure);
          photo.error = failure instanceof core.PhotoToolError
            ? failure.message
            : 'This photo could not be reduced. It may be damaged or too large for this browser.';
          photo.status = 'error';
        } else {
          result.url = URL.createObjectURL(new Blob([result.bytes], { type: result.type.mime }));
          photo.result = result;
          photo.status = 'done';
        }
        render();
      }
    } finally {
      working = false;
    }
  }

  function clearResult(photo) {
    if (photo.result) URL.revokeObjectURL(photo.result.url);
    photo.result = undefined;
    photo.error = undefined;
    photo.status = 'waiting';
  }

  /** Settings changed: every photo is done again, once the user stops typing. */
  function settingsChanged() {
    syncControls();
    saveSettings();
    run += 1;
    for (const photo of photos) clearResult(photo);
    render();
    clearTimeout(settleTimer);
    settling = true;
    settleTimer = setTimeout(() => {
      settling = false;
      work();
    }, SETTLE_MS);
  }

  function addPhotos(files) {
    clearError();
    const accepted = files.filter(file => core.isSupportedPhoto(file.type, file.name));
    if (accepted.length < files.length) {
      showError(accepted.length
        ? 'Some files were skipped. Only JPG, PNG and WEBP photos can be reduced here.'
        : 'Only JPG, PNG and WEBP photos can be reduced here. iPhone HEIC photos are not supported.');
    }
    if (!accepted.length) return;
    for (const file of accepted) {
      const thumb = document.createElement('img');
      thumb.className = 'phot-row__thumb';
      thumb.alt = '';
      photos.push({ id: nextId++, file, url: URL.createObjectURL(file), thumb, status: 'waiting' });
    }
    if (selectedId === null) selectedId = photos[0].id;
    syncControls();
    if (currentView() !== 'reduce') {
      // Keep a 'some files were skipped' message through the change of screen.
      keepErrorOnce = !errorBox.hidden;
      window.location.hash = 'reduce';
    }
    render();
    work();
  }

  function removePhoto(id) {
    const photo = photos.find(item => item.id === id);
    if (!photo) return;
    URL.revokeObjectURL(photo.url);
    if (photo.thumbUrl) URL.revokeObjectURL(photo.thumbUrl);
    if (photo.result) URL.revokeObjectURL(photo.result.url);
    photos = photos.filter(item => item.id !== id);
    if (selectedId === id) selectedId = photos.length ? photos[0].id : null;
    render();
  }

  // ----- Rendering -----

  function cell(className, text) {
    const td = document.createElement('td');
    if (className) td.className = className;
    if (text) td.textContent = text;
    return td;
  }

  function renderRow(photo) {
    const row = document.createElement('tr');
    row.classList.toggle('is-selected', photo.id === selectedId);

    const pick = document.createElement('button');
    pick.type = 'button';
    pick.className = 'phot-row__pick';
    pick.setAttribute('aria-pressed', String(photo.id === selectedId));
    pick.title = 'Check the quality of this photo';
    const thumb = photo.thumb;
    const name = document.createElement('span');
    name.className = 'phot-row__name';
    name.textContent = photo.file.name;
    pick.append(thumb, name);
    pick.addEventListener('click', () => {
      selectedId = photo.id;
      render();
    });
    const first = cell('');
    first.append(pick);

    const after = cell('phot-num phot-after');
    const size = cell('phot-num');
    if (photo.status === 'done') {
      after.textContent = core.formatBytes(photo.result.bytes.length);
      after.classList.toggle('is-over', !photo.result.reached || photo.result.bigger);
      if (photo.result.bigger) {
        // Never shown as a saving: the file grew.
        after.textContent += ' (not smaller)';
        after.title = 'This is no smaller than the original. The photo was already small for its type.';
      } else if (!photo.result.reached) {
        after.title = 'This is the smallest this photo can be made.';
      }
      size.textContent = `${photo.result.width} × ${photo.result.height}`;
    } else if (photo.status === 'error') {
      after.textContent = photo.error;
      after.classList.add('is-error');
      after.colSpan = 2;
    } else {
      after.textContent = photo.status === 'working' ? 'Working…' : 'Waiting';
      after.classList.remove('phot-after');
    }

    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'phot-row__remove';
    remove.setAttribute('aria-label', `Remove ${photo.file.name}`);
    remove.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
    remove.addEventListener('click', () => removePhoto(photo.id));
    const last = cell('');
    last.append(remove);

    row.append(first, cell('phot-num', core.formatBytes(photo.file.size)), after);
    if (photo.status !== 'error') row.append(size);
    row.append(last);
    return row;
  }

  function renderCompare() {
    const photo = photos.find(item => item.id === selectedId);
    if (!photo || photo.status !== 'done') {
      compare.hidden = true;
      return;
    }
    compare.hidden = false;
    $('[data-compare-name]', view).textContent = photo.file.name;
    if (compareBefore.src !== photo.url) compareBefore.src = photo.url;
    if (compareAfter.src !== photo.result.url) compareAfter.src = photo.result.url;
    $('[data-compare-before-label]', view).textContent = `Original, ${core.formatBytes(photo.file.size)}`;
    $('[data-compare-after-label]', view).textContent = `Reduced, ${core.formatBytes(photo.result.bytes.length)}`;
    placeCompareLine();
  }

  function placeCompareLine() {
    const position = Number(compareSlider.value);
    compareLine.style.left = `${position}%`;
    compareAfter.style.clipPath = `inset(0 0 0 ${position}%)`;
  }

  function renderSummary() {
    const settings = readSettings();
    const done = photos.filter(photo => photo.status === 'done');
    const pending = photos.some(photo => photo.status === 'waiting' || photo.status === 'working');
    const failed = photos.filter(photo => photo.status === 'error').length;
    const reached = done.filter(photo => photo.result.reached).length;
    const noun = photos.length === 1 ? '1 photo' : `${photos.length} photos`;

    let count;
    if (pending) {
      count = `Reducing ${noun}…`;
    } else if (!settings.targetBytes) {
      count = `${noun} reduced`;
    } else if (reached === photos.length) {
      count = photos.length === 1 ? `1 photo, under ${settings.targetText}` : `${noun}, all under ${settings.targetText}`;
    } else {
      count = `${reached} of ${noun} under ${settings.targetText}`;
    }
    $('[data-summary-count]', view).textContent = count;

    const before = done.reduce((sum, photo) => sum + photo.file.size, 0);
    const after = done.reduce((sum, photo) => sum + photo.result.bytes.length, 0);
    $('[data-summary-before]', view).textContent = done.length ? core.formatBytes(before) : '';
    $('[data-summary-after]', view).textContent = done.length ? core.formatBytes(after) : '';

    const note = $('[data-summary-note]', view);
    const notes = [];
    if (!pending && settings.targetBytes && done.length > reached) {
      notes.push('Photos shown in amber are as small as they can be made.');
    }
    const bigger = done.filter(photo => photo.result.bigger).length;
    if (!pending && bigger) {
      notes.push(bigger === 1
        ? '1 photo came out no smaller than the original and is marked. It was already small; use the original, or choose smaller dimensions.'
        : `${bigger} photos came out no smaller than the originals and are marked. They were already small; use the originals, or choose smaller dimensions.`);
    }
    if (!pending && failed) {
      notes.push(failed === 1 ? '1 photo could not be reduced and is left out.' : `${failed} photos could not be reduced and are left out.`);
    }
    note.textContent = notes.join(' ');
    note.hidden = notes.length === 0;

    downloadButton.disabled = pending || done.length === 0;
    downloadLabel.textContent = done.length > 1 ? 'Download all as ZIP' : 'Download photo';
  }

  function render() {
    const any = photos.length > 0;
    emptyDrop.hidden = any;
    workspace.hidden = !any;
    addButton.hidden = !any;
    rows.replaceChildren(...photos.map(renderRow));
    renderCompare();
    renderSummary();
  }

  function download(event) {
    if (shared.isRepeatClick(event)) return;
    const done = photos.filter(photo => photo.status === 'done');
    if (!done.length) return;
    const names = core.outputNames(
      done.map(photo => ({ name: photo.file.name, extension: photo.result.type.extension })),
      '-small');
    if (done.length === 1) {
      saveBlob(new Blob([done[0].result.bytes], { type: done[0].result.type.mime }), names[0]);
      return;
    }
    const archive = zip.createZip(done.map((photo, index) => ({ name: names[index], data: photo.result.bytes })));
    saveBlob(new Blob([archive], { type: 'application/zip' }), 'photos-small.zip');
  }

  // ----- Events -----

  for (const button of $$('[data-action="choose-files"], [data-action="add-files"]', view)) {
    button.addEventListener('click', () => fileInput.click());
  }

  fileInput.addEventListener('change', () => {
    addPhotos(Array.from(fileInput.files || []));
    fileInput.value = '';
  });

  shared.wireDropZones($$('[data-drop]', view), addPhotos);

  targetValue.addEventListener('input', settingsChanged);
  for (const button of unitButtons) {
    button.addEventListener('click', () => {
      unit = button.dataset.targetUnit;
      settingsChanged();
    });
  }
  for (const button of presetButtons) {
    button.addEventListener('click', () => {
      const [value, presetUnit] = button.dataset.targetPreset.split(':');
      targetValue.value = value;
      unit = presetUnit;
      settingsChanged();
    });
  }
  for (const control of [dimensions, customSideValue, format, greyscale]) {
    control.addEventListener('change', settingsChanged);
  }
  compareSlider.addEventListener('input', placeCompareLine);
  downloadButton.addEventListener('click', download);

  // A photo dropped outside a drop zone would make the browser open it in place of this page,
  // losing the work on every screen.
  window.addEventListener('dragover', (event) => event.preventDefault());
  window.addEventListener('drop', (event) => event.preventDefault());

  loadSettings();
  syncControls();
  render();
  // On first load the page starts at the top as usual: nothing is focused for the user.
  showView(currentView(), { focus: false });
})();
