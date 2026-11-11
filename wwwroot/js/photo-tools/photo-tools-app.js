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
  if (!root || !core || !zip) {
    return;
  }

  const SETTINGS_KEY = 'prism.photoTools.reduce';
  const ACCEPTED = /^image\/(jpeg|png|webp)$/;
  const ACCEPTED_NAME = /\.(jpe?g|png|webp)$/i;
  const SETTLE_MS = 350;

  const $ = (selector, scope = document) => scope.querySelector(selector);
  const $$ = (selector, scope = document) => Array.from(scope.querySelectorAll(selector));

  const errorBox = $('[data-error]');
  const fileInput = $('[data-file-input]');

  function showError(message) {
    errorBox.textContent = message;
    errorBox.hidden = false;
    errorBox.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  function clearError() {
    errorBox.hidden = true;
    errorBox.textContent = '';
  }

  function saveBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }

  // ---------------------------------------------------------------------------
  // Views
  // ---------------------------------------------------------------------------

  const views = $$('[data-view]');
  let keepErrorOnce = false;

  function showView(name) {
    const target = views.some(view => view.dataset.view === name) ? name : 'home';
    for (const view of views) {
      view.hidden = view.dataset.view !== target;
    }
    if (!keepErrorOnce) clearError();
    keepErrorOnce = false;
    window.scrollTo({ top: 0 });
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
  // Drawing
  // ---------------------------------------------------------------------------

  /** Opens a photo the right way up (phone photos carry their rotation as hidden data). */
  async function openPhoto(file) {
    try {
      return await createImageBitmap(file, { imageOrientation: 'from-image' });
    } catch {
      // Older browsers: decode through an <img>, which also honours the rotation.
    }
    const url = URL.createObjectURL(file);
    try {
      const image = new Image();
      image.src = url;
      await image.decode();
      return image;
    } catch {
      throw new core.PhotoToolError(
        'unreadable',
        'This file could not be opened as a photo. JPG, PNG and WEBP are supported; iPhone HEIC photos are not.');
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  function sizeOf(source) {
    return { width: source.naturalWidth || source.width, height: source.naturalHeight || source.height };
  }

  function newCanvas(width, height) {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return canvas;
  }

  /**
   * Draws a photo at a new size. Big reductions go down in halves first, which keeps fine
   * detail much better than one large jump.
   */
  function drawPhoto(source, width, height, options) {
    let current = source;
    let size = sizeOf(source);
    while (size.width / 2 > width && size.height / 2 > height) {
      const half = newCanvas(Math.ceil(size.width / 2), Math.ceil(size.height / 2));
      const context = half.getContext('2d');
      context.imageSmoothingQuality = 'high';
      context.drawImage(current, 0, 0, half.width, half.height);
      current = half;
      size = { width: half.width, height: half.height };
    }

    const canvas = newCanvas(width, height);
    const context = canvas.getContext('2d', { willReadFrequently: options.greyscale });
    if (options.opaque) {
      // JPG has no transparency; without this, see-through areas would turn black.
      context.fillStyle = '#fff';
      context.fillRect(0, 0, width, height);
    }
    context.imageSmoothingQuality = 'high';
    context.drawImage(current, 0, 0, width, height);
    if (options.greyscale) {
      const pixels = context.getImageData(0, 0, width, height);
      core.toGreyscale(pixels.data);
      context.putImageData(pixels, 0, 0);
    }
    return canvas;
  }

  function canvasToBytes(canvas, type, quality) {
    return new Promise((resolve, reject) => {
      canvas.toBlob(async (blob) => {
        if (!blob) {
          reject(new core.PhotoToolError('render', 'This photo is too large for this browser to save. Choose smaller dimensions.'));
          return;
        }
        if (blob.type !== type.mime) {
          reject(new core.PhotoToolError('format', `This browser cannot save ${type.label} files. Choose JPG or PNG.`));
          return;
        }
        resolve(new Uint8Array(await blob.arrayBuffer()));
      }, type.mime, type.lossy ? quality : undefined);
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

  /** @type {{ id: number, file: File, url: string, status: string, result?: object, error?: string }[]} */
  let photos = [];
  let nextId = 1;
  let selectedId = null;
  let unit = 'KB';
  let run = 0;
  let settleTimer = null;

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

  async function reducePhoto(photo, settings) {
    const source = await openPhoto(photo.file);
    try {
      const original = sizeOf(source);
      const start = core.resizeDimensions(original.width, original.height, settings.resize);
      const type = core.outputType(photo.file.type, settings.format);
      let drawn = null;
      const encode = (width, height, quality) => {
        if (!drawn || drawn.width !== width || drawn.height !== height) {
          drawn = drawPhoto(source, width, height, { greyscale: settings.greyscale, opaque: type.mime === 'image/jpeg' });
        }
        return canvasToBytes(drawn, type, quality);
      };
      const result = await core.encodeToTarget({
        encode,
        width: start.width,
        height: start.height,
        lossy: type.lossy,
        targetBytes: settings.targetBytes
      });
      return { ...result, type };
    } finally {
      if (typeof source.close === 'function') source.close();
    }
  }

  /** Reduces every photo that is waiting, one at a time. A newer run replaces an older one. */
  async function work() {
    const mine = ++run;
    const settings = readSettings();
    for (const photo of photos) {
      if (photo.status !== 'waiting') continue;
      photo.status = 'working';
      render();
      try {
        const result = await reducePhoto(photo, settings);
        if (mine !== run) return;
        result.url = URL.createObjectURL(new Blob([result.bytes], { type: result.type.mime }));
        photo.result = result;
        photo.status = 'done';
      } catch (error) {
        if (mine !== run) return;
        if (!(error instanceof core.PhotoToolError)) console.error(error);
        photo.error = error instanceof core.PhotoToolError
          ? error.message
          : 'This photo could not be reduced. It may be damaged or too large for this browser.';
        photo.status = 'error';
      }
      if (!photos.includes(photo)) continue;
      render();
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
    settleTimer = setTimeout(work, SETTLE_MS);
  }

  function addPhotos(files) {
    clearError();
    const accepted = files.filter(file => ACCEPTED.test(file.type) || ACCEPTED_NAME.test(file.name));
    if (accepted.length < files.length) {
      showError(accepted.length
        ? 'Some files were skipped. Only JPG, PNG and WEBP photos can be reduced here.'
        : 'Only JPG, PNG and WEBP photos can be reduced here. iPhone HEIC photos are not supported.');
    }
    if (!accepted.length) return;
    for (const file of accepted) {
      photos.push({ id: nextId++, file, url: URL.createObjectURL(file), status: 'waiting' });
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
    const thumb = document.createElement('img');
    thumb.className = 'phot-row__thumb';
    thumb.src = photo.url;
    thumb.alt = '';
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
      after.classList.toggle('is-over', !photo.result.reached);
      if (!photo.result.reached) after.title = 'This is the smallest this photo can be made.';
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

  function download() {
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

  for (const button of $$('[data-action="choose-files"], [data-action="add-files"]')) {
    button.addEventListener('click', () => fileInput.click());
  }

  fileInput.addEventListener('change', () => {
    addPhotos(Array.from(fileInput.files || []));
    fileInput.value = '';
  });

  for (const zone of $$('[data-drop]')) {
    zone.addEventListener('dragover', (event) => {
      event.preventDefault();
      zone.classList.add('is-dragging');
    });
    zone.addEventListener('dragleave', () => zone.classList.remove('is-dragging'));
    zone.addEventListener('drop', (event) => {
      event.preventDefault();
      zone.classList.remove('is-dragging');
      addPhotos(Array.from(event.dataTransfer?.files || []));
    });
  }

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

  loadSettings();
  syncControls();
  render();
  showView(currentView());
})();
