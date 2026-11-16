// Unit tests for the editor's parts (docs/ux/collage-review-3-fixes.md, PR 12, finding E5): the
// tray, the keys, the panels, the drafts screen, the actions, the toolbar, the start screen and the
// messages. Each part is made the way collage-app.js makes it, with the editor's values read
// through collage-state.js and stand-ins for the helpers it is handed.
const test = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const core = require('./collage-core.js');
const draw = require('./collage-draw.js');
const stickers = require('./collage-stickers.js');
const { createEditorState, createLaterParts } = require('./collage-state.js');
const { createModelEditor } = require('./collage-model.js');

const GLOBALS = ['window', 'document', 'Event', 'KeyboardEvent', 'MouseEvent', 'MutationObserver', 'HTMLElement'];

/** A page in jsdom whose window and document the parts use as globals until the test ends. */
function page(t, html) {
  const dom = new JSDOM(`<!doctype html><body>${html}</body>`, { pretendToBeVisual: true });
  const saved = GLOBALS.map((name) => [name, Object.getOwnPropertyDescriptor(globalThis, name)]);
  for (const name of GLOBALS) {
    Object.defineProperty(globalThis, name, { value: name === 'window' ? dom.window : dom.window[name], configurable: true, writable: true });
  }
  t.after(() => {
    for (const [name, descriptor] of saved) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
    dom.window.close();
  });
  const { document } = dom.window;
  return { window: dom.window, document, $: (selector) => document.querySelector(selector) };
}

/**
 * The editor's values as collage-app.js keeps them, read through createEditorState, with every
 * named change. `calls` counts what the stand-in helpers were asked to do.
 */
function editorValues(initial = {}) {
  const values = {
    collage: core.newCollage('a4l'),
    selectedId: null,
    targetId: null,
    adjustId: null,
    panel: null,
    propsTab: 'settings',
    zoom: 1,
    scale: 0.2,
    leaving: false,
    previewing: false,
    collageSession: 1,
    openRequest: 0,
    draftsExpanded: false,
    thumbnail: { url: '', at: 0, fresh: false, behind: false },
    transforming: false,
    ...initial
  };
  const read = Object.fromEntries(Object.keys(values).map((name) => [name, () => values[name]]));
  const { state, change } = createEditorState(read, {
    setCollage: (model) => { values.collage = model; },
    setPanel: (name) => { values.panel = name; },
    setPropsTab: (key) => { values.propsTab = key; },
    setTargetId: (id) => { values.targetId = id; },
    setThumbnail: (value) => { values.thumbnail = value; },
    nextOpenRequest: () => { values.openRequest += 1; return values.openRequest; },
    toggleDraftsExpanded: () => { values.draftsExpanded = !values.draftsExpanded; },
    choose: (id) => { values.selectedId = id; }
  });
  const itemById = (id) => values.collage.items.find((item) => item.id === id) || null;
  // The one way the collage changes (collage-model.js); each test says what a kept change calls.
  const keeps = {};
  const model = createModelEditor({
    getModel: () => values.collage,
    setModel: (next) => { values.collage = next; },
    keep: { commit: () => keeps.commit?.(), hold: () => keeps.hold?.(), record: () => keeps.record?.(), live: () => {} }
  });
  return { values, state, change, itemById, selectedItem: () => itemById(values.selectedId), edit: model.edit, editItem: model.editItem, keeps };
}

/** Records each call of the named stand-ins: calls.name is the list of argument lists. */
function recorder(names) {
  const calls = {};
  const fns = {};
  for (const name of names) {
    calls[name] = [];
    fns[name] = (...args) => { calls[name].push(args); };
  }
  return { calls, fns };
}

const slot = (id, extra = {}) => ({ ...core.slotItem(id, { x: 100, y: 100, width: 800, aspect: 1.5 }), ...extra });
const photoOf = (id, aspect = 1.5) => ({ id, name: `${id}.jpg`, aspect, width: 1500, height: 1000, thumbnail: 'data:image/png;base64,AA' });

// ---------------------------------------------------------------------------
// The tray (collage-tray.js)
// ---------------------------------------------------------------------------

const TRAY_HTML = `<main id="root">
  <div data-replace-banner hidden><span data-replace-text></span></div>
  <input type="file" data-file-input multiple>
  <div data-stage-box></div>
  <div data-tray></div><span data-photo-count></span>
  <span data-alert-text></span>
  <button data-action="choose">Add photos</button>
  <section data-view="editor"></section>
</main>`;

function makeTray(t, initial = {}, extra = {}) {
  const dom = page(t, TRAY_HTML);
  const editor = editorValues(initial);
  const { calls, fns } = recorder(['announce', 'showAlert', 'busy', 'commit', 'setPanel', 'scheduleDraft', 'layout']);
  const read = [];
  dom.window.PrismCollagePhotos = {
    createPhotoReader: () => ({
      readPhoto: async (entry) => { read.push(entry.name); return { ...photoOf(`p${read.length}`), name: entry.name }; },
      missingPhoto: (entry) => ({ ...entry, missing: true }),
      thumbnailOf: () => '',
      fitBudget: () => {}
    }),
    forgetUnreachable: () => {}
  };
  dom.window.PrismCollageLibraryDialog = { createLibraryDialog: () => ({ open() {}, close() {} }) };
  const photos = extra.photos || new Map();
  let ids = 0;
  const root = dom.$('#root');
  const tray = require('./collage-tray.js').createTray({
    state: editor.state,
    edit: editor.edit,
    editItem: editor.editItem,
    change: Object.freeze({ setTargetId: editor.change.setTargetId, choose: editor.change.choose }),
    $: dom.$,
    root,
    core,
    library: null,
    views: { editor: dom.$('[data-view="editor"]') },
    fileInput: dom.$('[data-file-input]'),
    stageBox: dom.$('[data-stage-box]'),
    tray: dom.$('[data-tray]'),
    photoCount: dom.$('[data-photo-count]'),
    replaceBanner: dom.$('[data-replace-banner]'),
    changes: { mentions: () => false },
    photos,
    nextId: (prefix) => `${prefix}${++ids}`,
    newItemId: () => `n${++ids}`,
    itemById: editor.itemById,
    selectedItem: editor.selectedItem,
    pageHeight: () => core.pageHeight(editor.values.collage.size),
    firstSlot: () => editor.values.collage.items.find((item) => item.kind === 'slot' && !item.locked) || null,
    keepingFocus: (render) => render(),
    session: {},
    stage: { setPointersPositions() {}, getPointerPosition: () => null, getIntersection: () => null },
    world: { getRelativePointerPosition: () => null },
    ...fns
  });
  return { ...dom, ...editor, tray, calls, photos, read };
}

function clickTrayPhoto(env, id) {
  env.tray.renderTray();
  env.$(`[data-tray-photo="${id}"]`).click();
}

test('tray: a photo clicked goes into the selected empty frame, and is chosen', (t) => {
  const collage = { ...core.newCollage('a4l'), layout: { key: 'two', gap: 20, radius: 0 }, items: [slot('s1'), slot('s2')] };
  const env = makeTray(t, { collage, selectedId: 's2' }, { photos: new Map([['p1', photoOf('p1')]]) });
  clickTrayPhoto(env, 'p1');
  const filled = env.values.collage.items[1];
  assert.equal(filled.kind, 'photo');
  assert.equal(filled.photo, 'p1');
  assert.equal(filled.id, 's2');
  assert.equal(env.values.selectedId, 's2');
  assert.equal(env.values.collage.items[0].kind, 'slot');
  assert.equal(env.calls.commit.length, 1);
  assert.deepEqual(env.calls.announce.at(-1), ['Photo placed in the frame.']);
});

test('tray: a locked empty frame is skipped, and a locked frame aimed at is not replaced', (t) => {
  const collage = {
    ...core.newCollage('a4l'),
    layout: { key: 'two', gap: 20, radius: 0 },
    items: [slot('s1', { locked: true }), { ...core.photoItem('f1', 'old', 1.5, { x: 0, y: 0, width: 400 }), locked: true }, slot('s3')]
  };
  const env = makeTray(t, { collage, targetId: 'f1' }, { photos: new Map([['p1', photoOf('p1')], ['old', photoOf('old')]]) });
  clickTrayPhoto(env, 'p1');
  const [first, locked, last] = env.values.collage.items;
  assert.equal(first.kind, 'slot');
  assert.equal(locked.photo, 'old');
  assert.equal(last.kind, 'photo');
  assert.equal(last.photo, 'p1');
});

test('tray: on a layout with every frame full, a photo clicked changes nothing and says how to make room', (t) => {
  const collage = { ...core.newCollage('a4l'), layout: { key: 'one', gap: 20, radius: 0 }, items: [core.photoItem('f1', 'old', 1.5, { x: 0, y: 0, width: 400 })] };
  const env = makeTray(t, { collage }, { photos: new Map([['p1', photoOf('p1')], ['old', photoOf('old')]]) });
  const before = JSON.stringify(env.values.collage);
  clickTrayPhoto(env, 'p1');
  assert.equal(JSON.stringify(env.values.collage), before);
  assert.equal(env.calls.commit.length, 0);
  assert.match(env.calls.showAlert.at(-1)[0], /^Every frame of this layout has a photo/);
});

test('tray: on a page without a layout, a photo goes on its own, fully on the page', (t) => {
  const env = makeTray(t, {}, { photos: new Map([['p1', photoOf('p1')]]) });
  clickTrayPhoto(env, 'p1');
  const [item] = env.values.collage.items;
  assert.equal(item.kind, 'photo');
  assert.equal(item.photo, 'p1');
  assert.equal(env.values.selectedId, item.id);
  assert.ok(item.x >= 0 && item.x + item.width <= core.PAGE_WIDTH);
});

test('tray: a used photo is marked and cannot be removed; an unused one can, and is taken out', (t) => {
  const collage = { ...core.newCollage('a4l'), items: [core.photoItem('f1', 'p1', 1.5, { x: 0, y: 0, width: 400 })] };
  const env = makeTray(t, { collage }, { photos: new Map([['p1', photoOf('p1')], ['p2', photoOf('p2')]]) });
  env.tray.renderTray();
  assert.equal(env.$('[data-tray-photo="p1"] .clg-tray__used').textContent, 'Used');
  assert.equal(env.$('[data-remove-photo="p1"]'), null);
  assert.equal(env.$('[data-photo-count]').textContent, `2 of ${core.PHOTO_LIMITS.maxPhotos} photos`);
  env.$('[data-remove-photo="p2"]').click();
  assert.equal(env.photos.get('p2').removed, true);
  assert.equal(env.$('[data-tray-photo="p2"]'), null);
  assert.equal(env.calls.scheduleDraft.length, 1);
  assert.deepEqual(env.tray.keptPhotos().map((photo) => photo.id), ['p1']);
});

function pasteEvent(window, target, files) {
  const event = new window.Event('paste', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'clipboardData', { value: { files } });
  target.dispatchEvent(event);
  return event;
}

const jpegFile = (name) => new File([new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 16, 74, 70, 73, 70, 0, 1, 1, 0, 0, 1])], name, { type: 'image/jpeg' });

test('tray: a pasted photo is read in and fills the selected empty frame', async (t) => {
  const collage = { ...core.newCollage('a4l'), layout: { key: 'two', gap: 20, radius: 0 }, items: [slot('s1'), slot('s2')] };
  const env = makeTray(t, { collage, selectedId: 's2' });
  const event = pasteEvent(env.window, env.document.body, [jpegFile('shot.jpg')]);
  assert.equal(event.defaultPrevented, true);
  // The import is queued: let it run.
  for (let i = 0; i < 20 && !env.calls.commit.length; i += 1) await new Promise((resolve) => setTimeout(resolve, 0));
  assert.deepEqual(env.read, ['shot.jpg']);
  assert.equal(env.values.collage.items[1].kind, 'photo');
  assert.equal(env.values.collage.items[1].photo, 'p1');
  assert.deepEqual(env.calls.announce.at(-1), ['Photo added.']);
});

test('tray: a paste in a text field, with no photo, or behind a dialog is left alone', (t) => {
  const env = makeTray(t);
  const field = env.document.createElement('textarea');
  env.$('#root').append(field);
  assert.equal(pasteEvent(env.window, field, [jpegFile('a.jpg')]).defaultPrevented, false);
  assert.equal(env.calls.showAlert.length, 0);

  assert.equal(pasteEvent(env.window, env.document.body, [new File(['words'], 'a.txt', { type: 'text/plain' })]).defaultPrevented, false);
  assert.match(env.calls.showAlert.at(-1)[0], /no photo on the clipboard/);

  const dialog = env.document.createElement('dialog');
  dialog.setAttribute('open', '');
  env.$('#root').append(dialog);
  assert.equal(pasteEvent(env.window, env.document.body, [jpegFile('b.jpg')]).defaultPrevented, false);
  assert.deepEqual(env.read, []);
});

// ---------------------------------------------------------------------------
// The keys (collage-keys.js)
// ---------------------------------------------------------------------------

function makeKeys(t, initial = {}) {
  const dom = page(t, '<section data-view="editor"><div data-stage-box></div></section><div data-props><input data-text></div>');
  const editor = editorValues(initial);
  const { calls, fns } = recorder(['announce', 'setShift', 'stopPreview', 'setZoom', 'syncPage', 'select', 'stopAdjust', 'zoomCrop', 'holdEdit', 'act', 'focusTextField', 'setTarget', 'sayLocked']);
  const changeSelected = (fn, record) => {
    calls.changeSelected = (calls.changeSelected || 0) + 1;
    const item = editor.selectedItem();
    if (item) fn(item);
    assert.equal(record, false);
  };
  require('./collage-keys.js').createKeys({
    state: editor.state,
    edit: editor.edit,
    editItem: editor.editItem,
    change: Object.freeze({}),
    core,
    views: { editor: dom.$('[data-view="editor"]') },
    stageBox: dom.$('[data-stage-box]'),
    props: dom.$('[data-props]'),
    photos: new Map(),
    itemById: editor.itemById,
    selectedItem: editor.selectedItem,
    changeSelected,
    ...fns
  });
  const press = (key, options = {}, target = dom.document.body) => {
    const event = new dom.window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options });
    target.dispatchEvent(event);
    return event;
  };
  return { ...dom, ...editor, calls, press };
}

test('keys: Delete, Ctrl+Z, Ctrl+Y, Ctrl+D and the brackets go to their actions', (t) => {
  const collage = { ...core.newCollage('a4l'), items: [core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 })] };
  const env = makeKeys(t, { collage, selectedId: 'a' });
  assert.equal(env.press('Delete').defaultPrevented, true);
  env.press('z', { ctrlKey: true });
  env.press('y', { ctrlKey: true });
  env.press('Z', { ctrlKey: true, shiftKey: true });
  env.press('d', { metaKey: true });
  env.press(']');
  env.press(']', { shiftKey: true });
  env.press('[');
  assert.deepEqual(env.calls.act.map(([action]) => action), ['delete', 'undo', 'redo', 'redo', 'duplicate', 'forward', 'front', 'backward']);
});

test('keys: arrows nudge the selected item and hold the change; Shift moves ten', (t) => {
  const collage = { ...core.newCollage('a4l'), items: [core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 })] };
  const env = makeKeys(t, { collage, selectedId: 'a' });
  env.press('ArrowRight');
  env.press('ArrowDown', { shiftKey: true });
  const item = env.values.collage.items[0];
  assert.deepEqual([item.x, item.y], [11, 20]);
  assert.deepEqual(env.calls.holdEdit, [[400], [400]]);

  item.locked = true;
  env.press('ArrowLeft');
  assert.equal(item.x, 11);
  assert.equal(env.calls.sayLocked.length, 1, 'a lock says so (on screen and to a screen reader)');
});

test('keys: nothing reaches the page while typing in a field, but Ctrl+S still opens Save', (t) => {
  const collage = { ...core.newCollage('a4l'), items: [core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 })] };
  const env = makeKeys(t, { collage, selectedId: 'a' });
  const field = env.$('[data-text]');
  field.focus();
  env.press('Delete', {}, field);
  env.press('z', { ctrlKey: true }, field);
  env.press('ArrowLeft', {}, field);
  assert.deepEqual(env.calls.act, []);
  assert.equal(env.values.collage.items[0].x, 10);
  env.press('s', { ctrlKey: true }, field);
  assert.deepEqual(env.calls.act, [['save-open']]);
  assert.notEqual(env.document.activeElement, field);
});

test('keys: Escape ends Preview, then the frame being filled, then the selection; a hidden editor hears nothing', (t) => {
  const env = makeKeys(t, { previewing: true, targetId: 't1', selectedId: 'a' });
  env.press('Escape');
  assert.equal(env.calls.stopPreview.length, 1);
  env.values.previewing = false;
  env.press('Escape');
  assert.deepEqual(env.calls.setTarget, [[null]]);
  env.values.targetId = null;
  env.press('Escape');
  assert.deepEqual(env.calls.select, [[null]]);

  env.$('[data-view="editor"]').hidden = true;
  env.press('Delete');
  env.press('z', { ctrlKey: true });
  assert.deepEqual(env.calls.act, []);
});

// ---------------------------------------------------------------------------
// The panels (collage-panels.js)
// ---------------------------------------------------------------------------

const PANELS_HTML = `<main id="root">
  <nav><button data-panel="layouts">Layouts</button><button data-panel="text">Text</button></nav>
  <section data-drawer-panel="layouts" hidden>
    <div data-layout-grid><button data-layout="two">Two</button><button data-layout="three">Three</button></div>
    <input type="range" data-layout-input="gap"><output data-out="gap"></output>
    <input type="range" data-layout-input="radius"><output data-out="radius"></output>
    <p data-layout-hint></p>
  </section>
  <section data-drawer-panel="text" hidden></section>
  <select data-size-select></select>
  <aside class="clg-props" data-props></aside>
</main>`;

function makePanels(t, initial = {}) {
  const dom = page(t, PANELS_HTML);
  dom.window.PrismCollageSettings = require('./collage-settings.js');
  // jsdom has no canvas; the settings ask one whether it can show a look.
  dom.window.HTMLCanvasElement.prototype.getContext = () => ({ filter: 'none' });
  const editor = editorValues(initial);
  const { calls, fns } = recorder(['layout', 'syncPage', 'select', 'zoomCrop', 'flushNudge', 'commit', 'renderBackgroundPanel', 'offerUndo', 'changeSelected']);
  const panels = require('./collage-panels.js').createPanels({
    state: editor.state,
    edit: editor.edit,
    editItem: editor.editItem,
    change: Object.freeze({ setPanel: editor.change.setPanel, setPropsTab: editor.change.setPropsTab, setCollage: editor.change.setCollage }),
    $: dom.$,
    root: dom.$('#root'),
    core,
    draw,
    stickers,
    sizeSelect: dom.$('[data-size-select]'),
    body: dom.document.body,
    layoutGrid: dom.$('[data-layout-grid]'),
    props: dom.$('[data-props]'),
    changes: { mark() {} },
    photos: new Map([['p', photoOf('p')]]),
    selectedItem: editor.selectedItem,
    isFrame: (item) => item && (item.kind === 'photo' || item.kind === 'slot'),
    keepingFocus: (render) => render(),
    stickerSource: () => '',
    clampedCrop: (item) => item.crop,
    FRAME_WIDTHS: [60, 1400],
    LOCKED_CONTROLS: '[data-prop="angle"], [data-prop="width"], [data-action="delete"], [data-prop="radius"]',
    ...fns
  });
  return { ...dom, ...editor, panels, calls };
}

test('panels: a rail button opens its drawer panel, and pressed again closes it', (t) => {
  const env = makePanels(t);
  const layouts = env.$('[data-panel="layouts"]');
  layouts.click();
  assert.equal(env.values.panel, 'layouts');
  assert.equal(layouts.getAttribute('aria-pressed'), 'true');
  assert.equal(env.$('[data-drawer-panel="layouts"]').hidden, false);
  assert.equal(env.$('[data-drawer-panel="text"]').hidden, true);
  // Opening Layouts draws its sliders: no layout yet, so the space slider waits.
  assert.equal(env.$('[data-layout-input="gap"]').disabled, true);
  env.$('[data-panel="text"]').click();
  assert.equal(env.values.panel, 'text');
  assert.equal(env.$('[data-drawer-panel="layouts"]').hidden, true);
  env.$('[data-panel="text"]').click();
  assert.equal(env.values.panel, null);
  assert.equal(env.document.body.classList.contains('is-drawer-closed'), true);
});

test('panels: the corners slider rounds every frame but a locked one, and is recorded when let go', (t) => {
  const collage = {
    ...core.newCollage('a4l'),
    layout: { key: 'two', gap: 20, radius: 0 },
    items: [slot('s1'), { ...core.photoItem('f1', 'p', 1.5, { x: 0, y: 0, width: 400 }), locked: true }, core.photoItem('f2', 'p', 1.5, { x: 500, y: 0, width: 400 })]
  };
  const env = makePanels(t, { collage });
  const radius = env.$('[data-layout-input="radius"]');
  radius.value = '40';
  radius.dispatchEvent(new env.window.Event('input'));
  assert.deepEqual(env.values.collage.items.map((item) => item.radius), [40, 0, 40]);
  assert.equal(env.values.collage.layout.radius, 40);
  assert.equal(env.calls.syncPage.length, 1);
  assert.equal(env.calls.commit.length, 0);
  radius.dispatchEvent(new env.window.Event('change'));
  assert.equal(env.calls.commit.length, 1);
  assert.equal(env.calls.offerUndo.length, 0);
});

test('panels: Settings and Layers are tabs of one tab panel; arrows switch them', (t) => {
  const collage = { ...core.newCollage('a4l'), items: [core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 })] };
  const env = makePanels(t, { collage });
  env.panels.renderProps();
  assert.equal(env.$('[data-props] h2').textContent, 'Page');
  assert.equal(env.$('[data-props-tab="settings"]').getAttribute('aria-selected'), 'true');
  assert.equal(env.$('#clgPropsPanel').getAttribute('aria-labelledby'), 'clgTab-settings');
  env.$('[data-props-tab="layers"]').click();
  assert.equal(env.values.propsTab, 'layers');
  assert.equal(env.$('#clgPropsPanel').getAttribute('aria-labelledby'), 'clgTab-layers');
  assert.ok(env.$('[data-layer-select="a"]'));
  assert.equal(env.document.activeElement, env.$('[data-props-tab="layers"]'));
  const key = new env.window.KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true });
  env.document.activeElement.dispatchEvent(key);
  assert.equal(key.defaultPrevented, true);
  assert.equal(env.values.propsTab, 'settings');
});

test('panels: a locked photo shows its settings with the controls that move it turned off', (t) => {
  const collage = { ...core.newCollage('a4l'), items: [{ ...core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 }), locked: true }] };
  const env = makePanels(t, { collage, selectedId: 'a' });
  env.panels.renderProps();
  assert.match(env.$('[data-props] h2').textContent, /^Photo/);
  const width = env.$('[data-prop="width"]');
  assert.ok(width, 'the Size slider is shown');
  assert.equal(width.disabled, true);
});

// ---------------------------------------------------------------------------
// The drafts screen (collage-drafts-screen.js)
// ---------------------------------------------------------------------------

const DRAFTS_HTML = `<main id="root">
  <section data-view="start">
    <section data-drafts-section hidden><span data-drafts-count></span><div data-drafts></div>
      <button data-action="drafts-all" hidden></button><p data-drafts-space></p></section>
  </section>
  <section data-view="editor" hidden>
    <span data-draft-status></span><button data-action="keep-draft" hidden></button>
    <input data-name><select data-size-select><option value="a4l">A4</option><option value="square">Square</option></select>
  </section>
</main>`;

function makeDraftsScreen(t, session, initial = {}) {
  const dom = page(t, DRAFTS_HTML);
  const editor = editorValues(initial);
  const { calls, fns } = recorder(['announce', 'showAlert', 'busy', 'showView', 'syncAll', 'refreshTitle', 'askConfirm', 'setPanel']);
  let sessionOptions = null;
  dom.window.PrismCollageDraftSession = { createDraftSession: (options) => { sessionOptions = options; return session; } };
  dom.window.PrismCollageDrafts = { usedBytes: async () => null };
  dom.window.PrismCollagePhotos = { releasePhoto: () => {} };
  const photos = new Map();
  const readers = { readPhoto: async (entry, id) => ({ ...photoOf(id), name: entry.name }), missingPhoto: (entry) => ({ ...photoOf(entry.id), missing: true }) };
  let resets = 0;
  const screen = require('./collage-drafts-screen.js').createDraftsScreen({
    state: editor.state,
    parts: createLaterParts({
      keptPhotos: () => () => [...photos.values()],
      readPhoto: () => (...args) => readers.readPhoto(...args),
      missingPhoto: () => (entry) => readers.missingPhoto(entry),
      setPanel: () => fns.setPanel
    }),
    change: Object.freeze({ setCollage: editor.change.setCollage, setThumbnail: editor.change.setThumbnail, nextOpenRequest: editor.change.nextOpenRequest }),
    core,
    askConfirm: fns.askConfirm,
    $: dom.$,
    views: { start: dom.$('[data-view="start"]'), editor: dom.$('[data-view="editor"]') },
    nameInput: dom.$('[data-name]'),
    sizeSelect: dom.$('[data-size-select]'),
    THUMBNAIL_EVERY_MS: 5000,
    changes: { start() {} },
    photos,
    pageHeight: () => 2480,
    hasContent: () => true,
    resetEditor: () => { resets += 1; photos.clear(); },
    collageTitle: () => 'Collage',
    ...fns
  });
  return { ...dom, ...editor, screen, calls, photos, readers, resets: () => resets, sessionOptions: () => sessionOptions };
}

const draftRecord = (id, title, extra = {}) => ({ id, title, size: 'a4l', saved: Date.now() - 60000, photoCount: 2, thumbnail: '', ...extra });

test('drafts screen: one row of draft cards, each named, with its delete button and "Show all"', async (t) => {
  const shown = ['a', 'b', 'c', 'd', 'e'].map((id, index) => draftRecord(id, `Farewell ${index + 1}`, { photoCount: index === 0 ? 1 : 3 }));
  const env = makeDraftsScreen(t, { list: async () => shown, undoing: false });
  await env.screen.renderDrafts();
  assert.equal(env.$('[data-drafts-section]').hidden, false);
  assert.equal(env.$('[data-drafts-count]').textContent, '5');
  const cards = [...env.document.querySelectorAll('[data-open-draft]')];
  // jsdom lays nothing out, so the row is worked out for an 800-pixel screen: three cards.
  assert.deepEqual(cards.map((card) => card.dataset.openDraft), ['a', 'b', 'c']);
  assert.equal(cards[0].querySelector('b').textContent, 'Farewell 1');
  assert.match(cards[0].textContent, /1 photo$/);
  assert.match(cards[1].textContent, /3 photos$/);
  assert.match(cards[0].getAttribute('aria-label'), /^Open draft Farewell 1, saved /);
  assert.equal(env.$('[data-delete-draft="a"]').getAttribute('aria-label'), 'Delete draft Farewell 1');
  const all = env.$('[data-action="drafts-all"]');
  assert.equal(all.hidden, false);
  assert.equal(all.textContent, 'Show all 5 drafts');
  env.values.draftsExpanded = true;
  await env.screen.renderDrafts();
  assert.equal(env.document.querySelectorAll('[data-open-draft]').length, 5);
  assert.equal(all.getAttribute('aria-expanded'), 'true');
});

test('drafts screen: with no drafts, or a browser that will not list them, the section is hidden', async (t) => {
  const env = makeDraftsScreen(t, { list: async () => { throw new Error('blocked'); } });
  env.$('[data-drafts-section]').hidden = false;
  await env.screen.renderDrafts();
  assert.equal(env.$('[data-drafts-section]').hidden, true);
});

function openableSession(records) {
  const waits = new Map();
  return {
    begun: [],
    read: (id) => new Promise((resolve) => waits.set(id, () => resolve(records[id]))),
    release: (id) => waits.get(id)(),
    stillThere: async () => true,
    begin(draft) { this.begun.push(draft.id); },
    list: async () => []
  };
}

function storedDraft(id, title, photoIds) {
  const items = photoIds.map((photo, index) => core.photoItem(`i${index + 1}`, photo, 1.5, { x: 100 + index * 500, y: 100, width: 400 }));
  return {
    record: { ...draftRecord(id, title), autoTitle: false, model: { ...core.newCollage('square'), items } },
    photos: photoIds.map((photo) => ({ id: photo, name: `${photo}.jpg`, file: `file-${photo}` }))
  };
}

test('drafts screen: a draft opens into the editor with its photos, name and size', async (t) => {
  const session = openableSession({ a: storedDraft('a', 'Sports day', ['p1', 'p2']) });
  const env = makeDraftsScreen(t, session);
  const opening = env.screen.openDraft('a');
  assert.equal(env.$('[data-view="start"]').inert, true);
  session.release('a');
  await opening;
  assert.deepEqual(session.begun, ['a']);
  assert.deepEqual([...env.photos.keys()], ['p1', 'p2']);
  assert.equal(env.values.collage.size, 'square');
  assert.equal(env.values.collage.items.length, 2);
  assert.equal(env.$('[data-name]').value, 'Sports day');
  assert.equal(env.$('[data-size-select]').value, 'square');
  assert.deepEqual(env.calls.showView, [['editor']]);
  assert.equal(env.$('[data-view="start"]').inert, false);
  assert.deepEqual(env.calls.announce.at(-1), ['Draft Sports day opened. Undo starts again from here.']);
});

test('drafts screen: an open asked for before a slower one finishes wins; the slower one stands down', async (t) => {
  const session = openableSession({ a: storedDraft('a', 'Slow', ['p1']), b: storedDraft('b', 'Fast', ['p2']) });
  const env = makeDraftsScreen(t, session);
  const slow = env.screen.openDraft('a');
  const fast = env.screen.openDraft('b');
  session.release('b');
  await fast;
  session.release('a');
  await slow;
  assert.deepEqual(session.begun, ['b']);
  assert.equal(env.resets(), 1);
  assert.deepEqual([...env.photos.keys()], ['p2']);
  assert.equal(env.$('[data-name]').value, 'Fast');
});

test('drafts screen: a photo that cannot be read stays as a grey stand-in, and the user is told', async (t) => {
  const session = openableSession({ a: storedDraft('a', 'Damaged', ['p1', 'p2']) });
  const env = makeDraftsScreen(t, session);
  env.readers.readPhoto = async (entry, id) => {
    if (id === 'p2') throw new Error('unreadable');
    return { ...photoOf(id), name: entry.name };
  };
  const opening = env.screen.openDraft('a');
  session.release('a');
  await opening;
  assert.equal(env.photos.get('p2').missing, true);
  assert.equal(env.values.collage.items.length, 2);
  assert.match(env.calls.showAlert.at(-1)[0], /^One photo of this draft could not be opened just now and shows grey/);
});

test('drafts screen: a draft the browser will not give back says so, and the start screen stays', async (t) => {
  const env = makeDraftsScreen(t, { read: async () => { throw new Error('blocked'); } });
  await env.screen.openDraft('a');
  assert.match(env.calls.showAlert.at(-1)[0], /could not be opened: this browser did not give it back/);
  assert.deepEqual(env.calls.showView, []);
  assert.equal(env.$('[data-view="start"]').inert, false);
});

test('drafts screen: "Keep as a draft" shows only for a collage left out of the drafts', (t) => {
  const session = { draft: { off: 'full' }, problem: () => 'No room for another draft.' };
  const env = makeDraftsScreen(t, session);
  env.screen.setDraftStatus('Not kept as a draft', true);
  const status = env.$('[data-draft-status]');
  assert.equal(status.textContent, 'Not kept as a draft');
  assert.equal(status.title, 'No room for another draft.');
  assert.equal(status.classList.contains('is-warning'), true);
  assert.equal(env.$('[data-action="keep-draft"]').hidden, false);
  session.draft = { off: null };
  env.screen.setDraftStatus('Draft saved in this browser');
  assert.equal(env.$('[data-action="keep-draft"]').hidden, true);
  assert.equal(status.classList.contains('is-warning'), false);
});

// ---------------------------------------------------------------------------
// The actions (collage-actions.js)
// ---------------------------------------------------------------------------

function makeActions(t, initial = {}) {
  const dom = page(t, `<main id="root"><section data-view="start"></section><section data-view="editor">
    <button data-action="duplicate">Duplicate</button><button data-action="delete" disabled>Delete</button></section>
    <input data-file-input type="file"><input data-name><select data-size-select></select><div data-props></div></main>`);
  const editor = editorValues(initial);
  const names = ['announce', 'clearAlert', 'startCollage', 'goHome', 'layer', 'layout', 'startPreview', 'stopPreview', 'setZoom', 'scheduleDraft', 'renderDrafts', 'undoDeleteDraft', 'openDraft', 'select', 'startAdjust', 'stopAdjust', 'refreshTitle', 'commit', 'updateUndo', 'restore', 'lockedNow', 'applyLayoutKey', 'sayLocked'];
  const { calls, fns } = recorder(names);
  let ids = 0;
  const actions = require('./collage-actions.js').createActions({
    state: editor.state,
    edit: editor.edit,
    editItem: editor.editItem,
    parts: createLaterParts({}),
    root: dom.$('#root'),
    core,
    $: dom.$,
    views: { start: dom.$('[data-view="start"]'), editor: dom.$('[data-view="editor"]') },
    fileInput: dom.$('[data-file-input]'),
    nameInput: dom.$('[data-name]'),
    sizeSelect: dom.$('[data-size-select]'),
    body: dom.document.body,
    props: dom.$('[data-props]'),
    changes: { version: 1, undo: () => null, redo: () => null },
    photos: new Map(),
    newItemId: () => `n${++ids}`,
    itemById: editor.itemById,
    selectedItem: editor.selectedItem,
    isFrame: (item) => item && (item.kind === 'photo' || item.kind === 'slot'),
    change: Object.freeze({ setCollage: editor.change.setCollage, choose: editor.change.choose, toggleDraftsExpanded: editor.change.toggleDraftsExpanded }),
    session: { remove() {}, keepAgain() {} },
    changeSelected: (fn) => { const item = editor.selectedItem(); if (item) { fn(item); fns.commit(); } },
    ...fns
  });
  return { ...dom, ...editor, actions, calls };
}

test('actions: a photo deleted from a layout leaves its frame empty; a locked one is not deleted', (t) => {
  const collage = { ...core.newCollage('a4l'), layout: { key: 'two', gap: 20, radius: 0 }, items: [core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 }), { ...core.photoItem('b', 'p', 1.5, { x: 400, y: 10, width: 300 }), locked: true }] };
  const env = makeActions(t, { collage, selectedId: 'b' });
  env.actions.act('delete');
  assert.equal(env.values.collage.items[1].kind, 'photo');
  assert.equal(env.calls.sayLocked.length, 1, 'a lock says so (on screen and to a screen reader)');
  assert.equal(env.calls.commit.length, 0);

  env.values.selectedId = 'a';
  env.actions.act('delete');
  assert.equal(env.values.collage.items[0].kind, 'slot');
  assert.equal(env.values.collage.items[0].id, 'a');
  assert.equal(env.values.selectedId, 'a');
  assert.equal(env.calls.commit.length, 1);
});

test('actions: Duplicate puts a copy after the item, chosen, and the page is no longer a strict layout', (t) => {
  const collage = { ...core.newCollage('a4l'), layout: { key: 'one', gap: 20, radius: 0 }, items: [core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 }), core.textItem('w', 'Hello', { x: 10, y: 600 }, {})] };
  const env = makeActions(t, { collage, selectedId: 'a' });
  env.$('[data-action="duplicate"]').click();
  assert.deepEqual(env.values.collage.items.map((item) => item.id), ['a', 'n1', 'w']);
  assert.equal(env.values.selectedId, 'n1');
  assert.equal(env.values.collage.layout, null);
  assert.notEqual(env.values.collage.items[1].crop, env.values.collage.items[0].crop);
  // A button that is turned off does nothing.
  env.$('[data-action="delete"]').click();
  assert.equal(env.values.collage.items.length, 3);
});

test('actions: Undo with nothing to take back only brings the buttons up to date', (t) => {
  const env = makeActions(t);
  env.actions.act('undo');
  assert.equal(env.calls.restore.length, 0);
  assert.equal(env.calls.updateUndo.length, 1);
});

// ---------------------------------------------------------------------------
// The toolbar (collage-toolbar.js)
// ---------------------------------------------------------------------------

test('toolbar: shows the parts for the chosen kind, and a locked item\'s blocked buttons are off', (t) => {
  const dom = page(t, `<div data-stage-box></div><div data-toolbar hidden>
    <span data-for="photo"><button data-action="adjust">Move</button></span><span data-for="text"><button data-action="edit-props">Edit</button></span>
    <span data-for="item"><button data-action="delete">Delete</button><button data-action="lock"><i></i></button></span>
    <span data-for="adjusting"><button data-action="adjust-done">Done</button></span></div><span data-angle hidden></span>`);
  const collage = { ...core.newCollage('a4l'), items: [{ ...core.photoItem('a', 'p', 1.5, { x: 10, y: 10, width: 300 }), locked: true }] };
  const editor = editorValues({ collage });
  const node = { isDragging: () => false, getClientRect: () => ({ x: 100, y: 200, width: 300, height: 200 }), rotation: () => 372 };
  const toolbar = require('./collage-toolbar.js').createToolbar({
    state: editor.state,
    core,
    $: dom.$,
    stageBox: dom.$('[data-stage-box]'),
    toolbar: dom.$('[data-toolbar]'),
    angleTag: dom.$('[data-angle]'),
    selectedItem: editor.selectedItem,
    nodes: new Map([['a', node]]),
    transformer: { rotateAnchorOffset: () => 30 },
    visibleSheetTop: () => null
  });
  toolbar.placeToolbar();
  assert.equal(dom.$('[data-toolbar]').hidden, true, 'nothing chosen, no toolbar');
  editor.values.selectedId = 'a';
  toolbar.placeToolbar();
  assert.equal(dom.$('[data-toolbar]').hidden, false);
  assert.equal(dom.$('[data-for="photo"]').hidden, false);
  assert.equal(dom.$('[data-for="text"]').hidden, true);
  assert.equal(dom.$('[data-for="adjusting"]').hidden, true);
  assert.equal(dom.$('[data-action="delete"]').disabled, true);
  assert.equal(dom.$('[data-action="lock"]').disabled, false);
  assert.equal(dom.$('[data-action="lock"]').getAttribute('aria-label'), 'Unlock');
  assert.equal(dom.$('[data-action="lock"] i').className, 'bi bi-lock-fill');
  toolbar.showAngle(node);
  assert.equal(dom.$('[data-angle]').textContent, '12°');
  toolbar.hideToolbar();
  assert.equal(dom.$('[data-toolbar]').hidden, true);
});

// ---------------------------------------------------------------------------
// The start screen (collage-start.js) and the messages (collage-messages.js)
// ---------------------------------------------------------------------------

test('start: a card for every page size and layout, and the size list of the editor', (t) => {
  const dom = page(t, '<div data-sizes></div><div data-start-layouts></div><div data-layout-grid></div><select data-size-select></select>');
  const start = require('./collage-start.js').createStart({ core, $: dom.$, sizeSelect: dom.$('[data-size-select]'), layoutGrid: dom.$('[data-layout-grid]'), photos: new Map() });
  start.renderStart();
  start.renderStart();
  const sizes = Object.keys(core.SIZES);
  const layouts = Object.keys(core.LAYOUTS);
  assert.deepEqual([...dom.document.querySelectorAll('[data-start-size]')].map((b) => b.dataset.startSize), sizes);
  assert.deepEqual([...dom.document.querySelectorAll('[data-start-layout]')].map((b) => b.dataset.startLayout), layouts);
  assert.deepEqual([...dom.document.querySelectorAll('[data-layout-grid] [data-layout]')].map((b) => b.dataset.layout), layouts);
  assert.deepEqual([...dom.$('[data-size-select]').options].map((o) => o.value), sizes);
  const first = core.LAYOUTS[layouts[0]];
  assert.equal(dom.$('[data-start-layout]').getAttribute('aria-label'), `${first.label}, ${first.cells.length} photos`);
});

test('messages: without the toasts an alert shows in the page and goes when cleared; busy shows its line', async (t) => {
  const dom = page(t, '<p data-announcer></p><div data-alert hidden><span data-alert-text></span></div><div data-busy hidden><span data-busy-text></span></div>');
  const messages = require('./collage-messages.js').createMessages({ $: dom.$, announcer: dom.$('[data-announcer]'), alertBox: dom.$('[data-alert]'), busyBox: dom.$('[data-busy]') });
  messages.showAlert('The photo could not be added.');
  assert.equal(dom.$('[data-alert]').hidden, false);
  assert.equal(dom.$('[data-alert-text]').textContent, 'The photo could not be added.');
  // A change made at once does not end the message it caused.
  messages.changeMadeAfterAlert();
  assert.equal(dom.$('[data-alert]').hidden, false);
  messages.clearAlert();
  assert.equal(dom.$('[data-alert]').hidden, true);
  assert.equal(dom.$('[data-alert-text]').textContent, '');
  messages.busy('Adding 3 photos…');
  assert.equal(dom.$('[data-busy]').hidden, false);
  assert.equal(dom.$('[data-busy-text]').textContent, 'Adding 3 photos…');
  messages.busy('');
  assert.equal(dom.$('[data-busy]').hidden, true);
  messages.announce('Photo added.');
  await new Promise((resolve) => setTimeout(resolve, 50));
  assert.equal(dom.$('[data-announcer]').textContent, 'Photo added.');
});

test('messages: with the toasts an alert goes to a toast, and the page line follows it away', async (t) => {
  const dom = page(t, '<p data-announcer></p><div data-alert hidden><span data-alert-text></span></div><div data-busy hidden><span data-busy-text></span></div><div id="toasts"></div>');
  const host = dom.$('#toasts');
  const shown = [];
  dom.window.PrismToast = {
    show: (options) => {
      const element = dom.document.createElement('div');
      host.append(element);
      const toast = { options, element, dismiss: () => element.remove() };
      shown.push(toast);
      return toast;
    }
  };
  const messages = require('./collage-messages.js').createMessages({ $: dom.$, announcer: dom.$('[data-announcer]'), alertBox: dom.$('[data-alert]'), busyBox: dom.$('[data-busy]') });
  messages.showAlert('Draft not saved.');
  assert.equal(shown.length, 1);
  assert.equal(shown[0].options.tone, 'warning');
  assert.equal(dom.$('[data-alert]').hidden, true);
  assert.equal(dom.$('[data-alert-text]').textContent, 'Draft not saved.');
  shown[0].element.remove();
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(dom.$('[data-alert-text]').textContent, '');
});

// ---------------------------------------------------------------------------
// The Text and Stickers panels (collage-add-panels.js)
// ---------------------------------------------------------------------------

function makeAddPanels(t, library) {
  const dom = page(t, `<main id="root"><section data-view="editor"></section>
    <div data-text-starts></div><div data-text-headings></div><div data-text-headings-hi></div>
    <input data-sticker-search><div data-sticker-groups></div><div data-sticker-grid></div><p data-sticker-none hidden></p>
    <div data-bg-photos></div><p data-bg-photos-none></p></main>`);
  const editor = editorValues();
  const { calls, fns } = recorder(['announce', 'syncPage', 'commit']);
  let ids = 0;
  const panels = require('./collage-add-panels.js').createAddPanels({
    state: editor.state,
    edit: editor.edit,
    editItem: editor.editItem,
    parts: createLaterParts({ focusTextField: () => () => {} }),
    root: dom.$('#root'),
    core,
    draw,
    stickers,
    library,
    $: dom.$,
    views: { editor: dom.$('[data-view="editor"]') },
    photos: new Map(),
    newItemId: () => `n${++ids}`,
    pageHeight: () => core.pageHeight(editor.values.collage.size),
    keepingFocus: (render) => render(),
    backLayer: { batchDraw() {} },
    layoutTextItem: (item) => { item.width = 600; },
    stickerSource: () => 'data:image/png;base64,AA',
    placeNewItem: (item) => { editor.values.collage.items.push(item); editor.values.selectedId = item.id; },
    ...fns
  });
  return { ...dom, ...editor, panels, calls };
}

test('add panels: the Text panel offers its starts, and a heading added does not land on the one before', (t) => {
  const env = makeAddPanels(t, null);
  env.panels.renderTextPanel();
  assert.deepEqual([...env.document.querySelectorAll('[data-text-starts] [data-add-text]')].map((b) => b.dataset.addText), ['heading', 'subheading', 'line', 'hindi']);
  assert.equal(env.$('[data-add-text="heading"]').getAttribute('aria-label'), 'Add a heading');
  env.$('[data-add-text="heading"]').click();
  env.$('[data-add-text="heading"]').click();
  const [first, second] = env.values.collage.items;
  assert.equal(first.kind, 'text');
  assert.equal(env.values.selectedId, second.id);
  assert.notDeepEqual([first.x, first.y], [second.x, second.y]);
  assert.equal(env.calls.commit.length, 2);
  assert.deepEqual(env.calls.announce.at(-1), ['Text added. Type the words in the Words box.']);
});

test('add panels: without PRISM\'s crests the other stickers still work, and the crests say why', async (t) => {
  const env = makeAddPanels(t, { loadStickers: async () => { throw new Error('offline'); } });
  env.panels.loadUnitStickers();
  await new Promise((resolve) => setTimeout(resolve, 0));
  env.$('[data-sticker-group="crests"]').click();
  assert.equal(env.$('[data-sticker-none]').hidden, false);
  assert.match(env.$('[data-sticker-none]').textContent, /could not be read from PRISM/);
  const other = [...env.document.querySelectorAll('[data-sticker-group]')].find((chip) => chip.dataset.stickerGroup !== 'crests');
  other.click();
  const sticker = env.$('[data-add-sticker]');
  assert.ok(sticker, 'the group shows its stickers');
  sticker.click();
  assert.equal(env.values.collage.items[0].kind, 'sticker');
  assert.deepEqual(env.calls.announce.at(-1), ['Sticker added.']);
});
