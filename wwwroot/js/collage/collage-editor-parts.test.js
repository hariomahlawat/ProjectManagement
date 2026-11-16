// Tests for the editor's parts moved out of collage-app.js (docs/ux/collage-review-fixes.md, PR 12):
// held edits, focus kept on the control used, and Save's drawing.
const test = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const core = require('./collage-core.js');
const draw = require('./collage-draw.js');
const { createHeldEdits } = require('./collage-edits.js');
const { createFocusKeeper } = require('./collage-focus.js');
const { createRenderer } = require('./collage-render.js');

function fakeTimers() {
  let next = 1;
  const waiting = new Map();
  return {
    set: (fn, ms) => { const id = next++; waiting.set(id, { fn, ms }); return id; },
    clear: (id) => waiting.delete(id),
    fire: () => { const all = [...waiting.values()]; waiting.clear(); all.forEach((entry) => entry.fn()); },
    waiting
  };
}

test('a held edit is kept in the draft at once and recorded once, when it pauses', () => {
  const timers = fakeTimers();
  let records = 0;
  let keeps = 0;
  const edits = createHeldEdits({ record: () => { records += 1; }, keep: () => { keeps += 1; }, timers });
  edits.hold(600);
  edits.hold(600);
  edits.hold(600);
  assert.equal(keeps, 3);
  assert.equal(records, 0);
  assert.equal(timers.waiting.size, 1);
  timers.fire();
  assert.equal(records, 1);
  assert.equal(edits.pending, false);
  assert.equal(edits.flush(), false);
});

test('a held edit is recorded at once when something else happens, and can be dropped', () => {
  const timers = fakeTimers();
  let records = 0;
  const edits = createHeldEdits({ record: () => { records += 1; }, timers });
  edits.hold();
  assert.equal(edits.flush(), true);
  assert.equal(records, 1);
  assert.equal(timers.waiting.size, 0);
  edits.hold();
  edits.drop();
  assert.equal(edits.flush(), false);
  assert.equal(records, 1);
  edits.mark();
  assert.equal(edits.flush(), true);
  assert.equal(records, 2);
});

test('focus goes back to the same control in the panel that was drawn again, never elsewhere', () => {
  const dom = new JSDOM(`<main id="root">
    <div id="toolbar"><button data-action="lock">Toolbar lock</button></div>
    <section id="props"><button data-action="lock">Settings lock</button><textarea data-text>Hello</textarea></section>
  </main>`);
  const { document } = dom.window;
  const root = document.getElementById('root');
  const props = document.getElementById('props');
  const keeper = createFocusKeeper(root);
  const redraw = () => {
    props.innerHTML = '<button data-action="lock">Settings lock</button><textarea data-text>Hello there</textarea>';
  };

  props.querySelector('[data-action="lock"]').focus();
  keeper.keep(redraw);
  assert.equal(document.activeElement.textContent, 'Settings lock');
  assert.equal(document.activeElement.closest('#props'), props);

  const words = props.querySelector('[data-text]');
  words.focus();
  words.setSelectionRange(2, 4);
  keeper.keep(redraw);
  assert.equal(document.activeElement.matches('[data-text]'), true);
  assert.deepEqual([document.activeElement.selectionStart, document.activeElement.selectionEnd], [2, 4]);

  // A control kept by the redraw keeps focus as it is; one without a name is left alone.
  document.getElementById('toolbar').querySelector('button').focus();
  keeper.keep(redraw);
  assert.equal(document.activeElement.textContent, 'Toolbar lock');
  assert.equal(keeper.controlKey(document.body), null);
});

test('Save draws the photos, and counts what it had to leave out', async () => {
  const canvases = [];
  const makeCanvas = () => {
    const ctx = new Proxy({ calls: [] }, {
      get: (target, name) => (name in target ? target[name] : (...args) => { target.calls.push([name, ...args]); return { addColorStop() {} }; }),
      set: (target, name, value) => { target[name] = value; return true; }
    });
    const canvas = { width: 0, height: 0, getContext: () => ctx, ctx };
    canvases.push(canvas);
    return canvas;
  };
  const decoded = [];
  const renderer = createRenderer({
    core,
    draw,
    measureText: (text) => ({ width: String(text).length * 10, actualBoundingBoxAscent: 8, actualBoundingBoxDescent: 2 }),
    fontsReady: async () => {},
    stickerImage: async () => null,
    makeCanvas,
    decode: async (file, options) => { decoded.push({ file, options }); return { width: options.resizeWidth, height: options.resizeHeight, close() {} }; }
  });
  const shown = core.photoItem('a', 'p1', 1.5, { x: 300, y: 200, width: 300 });
  const grey = core.photoItem('b', 'p2', 1, { x: 700, y: 200, width: 200 });
  const crest = core.stickerItem('c', 'unit:5', { x: 500, y: 500, width: 120, aspect: 1 });
  const collage = { ...core.newCollage('a4l'), items: [shown, grey, crest] };
  const photos = new Map([
    ['p1', { id: 'p1', file: 'file-1', width: 3000, height: 2000, aspect: 1.5 }],
    ['p2', { id: 'p2', file: 'file-2', width: 4, height: 3, aspect: 4 / 3, missing: true }]
  ]);

  const result = await renderer.drawCollage(collage, photos, 1920, 1357);
  assert.equal(result.canvas.width, 1920);
  assert.equal(result.leftOutPhotos, 1);
  assert.equal(result.leftOutStickers, 1);
  // Only the photo that can be opened is read, at no more than its frame needs.
  assert.deepEqual(decoded.map((entry) => entry.file), ['file-1']);
  assert.ok(decoded[0].options.resizeWidth <= 3000);
  assert.ok(result.canvas.ctx.calls.some((call) => call[0] === 'drawImage'));
});

test('the settings panel HTML escapes names and offers Size only for photos and frames', () => {
  const { createSettingsHtml } = require('./collage-settings.js');
  const stickers = require('./collage-stickers.js');
  const photos = new Map([['p1', { id: 'p1', name: '<b>Team</b>.jpg', thumbnail: 'data:image/png;base64,AA' }]]);
  const html = createSettingsHtml({
    core,
    draw,
    stickers,
    stickerSource: () => '',
    escapeHtml: (text) => String(text).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])),
    photos,
    FRAME_WIDTHS: [60, 1400],
    TEXT_COLOURS: ['#ffffff'],
    LABEL_COLOURS: ['#ffffff']
  });
  const photo = core.photoItem('i1', 'p1', 1.5, { x: 10, y: 10, width: 300 });
  const words = core.textItem('i2', 'Farewell <script>', { x: 10, y: 10 }, {});
  assert.match(html.arrangeSections(photo), /data-prop="width"/);
  assert.doesNotMatch(html.arrangeSections(words), /aria-label="Width of the/);

  const layers = html.layersSection({ items: [photo, words] }, 'i2');
  assert.ok(layers.includes('&lt;b&gt;Team&lt;/b&gt;.jpg'));
  assert.ok(!layers.includes('<b>Team</b>'));
  assert.ok(layers.includes('Farewell &lt;script&gt;'));
  // The top of the list is in front: the text, added last, comes first, and is marked chosen.
  assert.ok(layers.indexOf('Farewell') < layers.indexOf('Team'));
  assert.match(layers, /data-layer-select="i2" aria-current="true"/);
  assert.equal(html.layersSection({ items: [] }, null), '');
});

function fakeCanvasMaker(canvases) {
  return () => {
    const ctx = new Proxy({ calls: [] }, {
      get: (target, name) => (name in target ? target[name] : (...args) => { target.calls.push([name, ...args]); return { addColorStop() {} }; }),
      set: (target, name, value) => { target[name] = value; return true; }
    });
    const canvas = {
      width: 0, height: 0, getContext: () => ctx, ctx,
      toBlob: (done) => done({ arrayBuffer: async () => new ArrayBuffer(4) })
    };
    canvases.push(canvas);
    return canvas;
  };
}

test('a Save that fails lets go of its picture; smaller copies for a JPG size are let go too', async () => {
  const canvases = [];
  const renderer = createRenderer({
    core, draw,
    measureText: () => ({ width: 10, actualBoundingBoxAscent: 8, actualBoundingBoxDescent: 2 }),
    fontsReady: async () => {},
    stickerImage: async () => null,
    makeCanvas: fakeCanvasMaker(canvases),
    decode: async () => { throw new Error('out of memory'); }
  });
  const collage = { ...core.newCollage('a4l'), items: [core.photoItem('a', 'p1', 1.5, { x: 300, y: 200, width: 300 })] };
  const photos = new Map([['p1', { id: 'p1', file: 'f', width: 3000, height: 2000, aspect: 1.5 }]]);
  await assert.rejects(renderer.drawCollage(collage, photos, 1920, 1357), /out of memory/);
  assert.equal(canvases[0].width, 0);
  assert.equal(canvases[0].height, 0);

  const source = { width: 1000, height: 800, getContext: () => null, toBlob: (done) => done({ arrayBuffer: async () => new ArrayBuffer(8) }) };
  const bytes = await renderer.scaledBytes(source, 500, 400, 'image/jpeg', 0.8);
  assert.equal(bytes.length, 4);
  const copy = canvases.at(-1);
  assert.equal(copy.width, 0, 'the smaller copy is let go');
  assert.equal(source.width, 1000, 'the picture itself is kept for the next try');
});
