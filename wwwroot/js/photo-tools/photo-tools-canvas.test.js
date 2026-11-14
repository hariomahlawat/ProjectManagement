const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// The canvas helpers run in the page; a small stand-in window and document are enough here.
function loadShared() {
  const window = { PrismPhotoTools: {} };
  const context = { window, document: { querySelector: () => ({}) } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'photo-tools-canvas.js'), 'utf8'), context);
  return window.PrismPhotoCanvas;
}

const shared = loadShared();

test('isMainPointer takes the first finger or pen and the left mouse button only', () => {
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'touch', button: 0 }), true);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'pen', button: 0 }), true);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'mouse', button: 0 }), true);
  assert.equal(shared.isMainPointer({ isPrimary: false, pointerType: 'touch', button: 0 }), false);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'mouse', button: 2 }), false);
  assert.equal(shared.isMainPointer({ isPrimary: true, pointerType: 'mouse', button: 1 }), false);
});

test('isRepeatClick skips the second click of a double click but not a key press', () => {
  assert.equal(shared.isRepeatClick({ detail: 1 }), false);
  assert.equal(shared.isRepeatClick({ detail: 0 }), false);
  assert.equal(shared.isRepeatClick({ detail: 2 }), true);
  assert.equal(shared.isRepeatClick(undefined), false);
});

// Every place that draws or drags with a pointer starts only for the main pointer. Passport
// photo's own crop is left out: it reads two fingers on purpose, to pinch.
test('drawing and dragging start only for the main pointer', () => {
  const starts = [
    ['photo-tools-editor.js', "photoBox.addEventListener('pointerdown'"],
    ['photo-tools-editor.js', "cropBox.addEventListener('pointerdown'"],
    ['photo-tools-print.js', "thumb.addEventListener('pointerdown'"],
    ['photo-tools-background.js', "photoBox.addEventListener('pointerdown'"]
  ];
  for (const [file, start] of starts) {
    const source = fs.readFileSync(path.join(__dirname, file), 'utf8');
    const at = source.indexOf(start);
    assert.ok(at >= 0, `${file}: ${start} not found`);
    const handler = source.slice(at, source.indexOf('});', at));
    assert.match(handler, /shared\.isMainPointer\(event\)/, `${file}: ${start} must check isMainPointer`);
  }
});

// ----- Shared helpers for the tools (PR 7 of docs/ux/photo-tools-review-fixes.md) -----

// A page with the real core, an error box to read, and photos that open when a test says so.
function loadWithPhotos() {
  const core = require('./photo-tools-core.js');
  const errorBox = { textContent: '', hidden: true, scrollIntoView() {} };
  const pending = new Map();
  const window = { PrismPhotoTools: core };
  const context = {
    window,
    document: { querySelector: () => errorBox },
    createImageBitmap: file => new Promise((resolve, reject) => pending.set(file.name, { resolve, reject }))
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'photo-tools-canvas.js'), 'utf8'), context);
  return { shared: window.PrismPhotoCanvas, errorBox, pending };
}

const photoFile = name => ({ name, type: 'image/jpeg' });
const tick = () => new Promise(resolve => setImmediate(resolve));

test('latestPhotoOpener gives only the photo chosen last and closes one that opened late', async () => {
  const { shared, pending } = loadWithPhotos();
  const opener = shared.latestPhotoOpener();
  const first = opener.open(photoFile('a.jpg'));
  const second = opener.open(photoFile('b.jpg'));
  await tick();
  let closed = false;
  pending.get('b.jpg').resolve({ id: 'b', close() {} });
  pending.get('a.jpg').resolve({ id: 'a', close() { closed = true; } });
  assert.equal((await second).id, 'b');
  assert.equal(await first, null);
  assert.equal(closed, true, 'the late photo is closed at once');
});

test('latestPhotoOpener refuses other file types, and cancel() drops a photo still opening', async () => {
  const { shared, errorBox, pending } = loadWithPhotos();
  const opener = shared.latestPhotoOpener();
  assert.equal(await opener.open({ name: 'scan.heic', type: 'image/heic' }), null);
  assert.equal(errorBox.hidden, false);
  assert.match(errorBox.textContent, /HEIC/);

  const opening = opener.open(photoFile('c.jpg'));
  assert.equal(errorBox.hidden, true, 'a new choice clears the last error');
  await tick();
  opener.cancel();
  pending.get('c.jpg').resolve({ id: 'c', close() {} });
  assert.equal(await opening, null);
});

// A stand-in element that records its listeners, so a test can fire them.
function fakeElement() {
  const listeners = {};
  return {
    listeners,
    classList: { set: new Set(), add(name) { this.set.add(name); }, remove(name) { this.set.delete(name); } },
    addEventListener(type, handler) { (listeners[type] ||= []).push(handler); },
    fire(type, event = {}) { for (const handler of listeners[type] || []) handler(event); }
  };
}

test('wireDropZones lights a zone while dragging and hands on the dropped files', () => {
  const zone = fakeElement();
  let got = null;
  shared.wireDropZones([zone], files => { got = files; });
  let prevented = 0;
  zone.fire('dragover', { preventDefault: () => { prevented += 1; } });
  assert.ok(zone.classList.set.has('is-dragging'));
  zone.fire('drop', { preventDefault: () => { prevented += 1; }, dataTransfer: { files: [photoFile('x.jpg')] } });
  assert.equal(zone.classList.set.has('is-dragging'), false);
  assert.equal(prevented, 2);
  assert.equal(got.map(file => file.name).join(), 'x.jpg');
});

test('wireHoldButton holds with a pointer, Space or Enter and lets go on release, leave or blur', () => {
  const button = fakeElement();
  const seen = [];
  shared.wireHoldButton(button, on => seen.push(on));
  button.fire('pointerdown');
  button.fire('pointerup');
  button.fire('keydown', { key: ' ' });
  button.fire('keyup', { key: ' ' });
  button.fire('keydown', { key: 'a' });
  button.fire('keydown', { key: 'Enter' });
  button.fire('blur');
  assert.deepEqual(seen, [true, false, true, false, true, false]);
});

// ----- Remembered choices (PR 2 of docs/ux/photo-tools-passport-done-for-you.md) -----

function loadWithStorage(storage) {
  const window = { PrismPhotoTools: {}, localStorage: storage };
  const context = { window, document: { querySelector: () => ({}) } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'photo-tools-canvas.js'), 'utf8'), context);
  return window.PrismPhotoCanvas;
}

test('rememberedChoices keeps a screen\'s choices under its own name', () => {
  const store = new Map();
  const storage = { getItem: key => (store.has(key) ? store.get(key) : null), setItem: (key, value) => store.set(key, value) };
  const shared = loadWithStorage(storage);
  const sheet = shared.rememberedChoices('sheet');

  assert.equal(sheet.load(), null);
  sheet.save({ paper: 'a4', cutLines: false });
  assert.equal(JSON.parse(store.get('prism.photoTools.sheet')).paper, 'a4');
  assert.equal(sheet.load().cutLines, false);
  assert.equal(shared.rememberedChoices('edit').load(), null, 'another screen has its own');
});

test('rememberedChoices never throws: a refused or broken store remembers nothing', () => {
  const refusing = { getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); } };
  const shared = loadWithStorage(refusing);
  const choices = shared.rememberedChoices('passport');
  assert.equal(choices.load(), null);
  choices.save({ limit: '50' });

  const broken = loadWithStorage({ getItem: () => '{not json', setItem() {} });
  assert.equal(broken.rememberedChoices('passport').load(), null);
  const notAnObject = loadWithStorage({ getItem: () => '42', setItem() {} });
  assert.equal(notAnObject.rememberedChoices('passport').load(), null);
});
