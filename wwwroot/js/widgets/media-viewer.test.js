const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const root = path.resolve(__dirname, '..', '..', '..');
const viewerScript = fs.readFileSync(path.join(__dirname, 'media-viewer.js'), 'utf8');
const galleryScript = fs.readFileSync(path.join(root, 'wwwroot', 'js', 'pages', 'photos-library.js'), 'utf8');
// The project pages' markup, as shipped (Razor comments removed).
const viewerMarkup = fs.readFileSync(path.join(root, 'Pages', 'Shared', '_MediaViewer.cshtml'), 'utf8')
  .replace(/@\*[\s\S]*?\*@/g, '');

const photos = [
  { key: 'photo-1', kind: 'photo', displayUrl: '/p/1/xl', title: 'Cover', context: 'Driving Simulator', date: 'Added 2 Mar 2026', originalUrl: '/p/1/xl', downloadUrl: '/p/1/download' },
  { key: 'photo-2', kind: 'photo', displayUrl: '/p/2/xl', title: 'Trials', context: 'Driving Simulator', date: 'Added 3 Mar 2026', originalUrl: '/p/2/xl', downloadUrl: '/p/2/download' },
  { key: 'photo-3', kind: 'photo', displayUrl: '/p/3/xl', title: 'Handover', context: 'Driving Simulator', originalUrl: '/p/3/xl' },
];

function page(body) {
  const virtualConsole = new VirtualConsole(); // jsdom does not implement media playback
  const dom = new JSDOM(`<!doctype html><html><body>${body}</body></html>`, {
    url: 'http://localhost/Projects/Overview/1#timeline',
    runScripts: 'outside-only',
    virtualConsole,
  });
  dom.window.eval(viewerScript);
  // In a page the script runs before DOMContentLoaded; here the document is still loading.
  dom.window.PMMediaViewer.autoWire(dom.window.document);
  return dom;
}

function overview(extra = '') {
  return page(`
    <main id="content">
      <a id="t1" href="/Projects/1/Photos" data-media-viewer-open="project-photos" data-media-viewer-key="photo-1">1</a>
      <a id="t2" href="/Projects/1/Photos" data-media-viewer-open="project-photos" data-media-viewer-key="photo-2">2</a>
      <a id="v1" href="/Projects/1/Videos" data-media-viewer-open="project-videos" data-media-viewer-key="video-7">video</a>
      <video id="inline"></video>
      ${extra}
    </main>
    <script type="application/json" data-media-viewer-items="project-photos">${JSON.stringify(photos)}</script>
    <script type="application/json" data-media-viewer-items="project-videos">${JSON.stringify([
      { key: 'video-7', kind: 'video', displayUrl: '/v/7/stream', title: 'Demo', poster: '/v/7/poster', originalUrl: '/v/7/stream' },
    ])}</script>
    ${viewerMarkup}`);
}

const click = (window, element, init = {}) => {
  const event = new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ...init });
  element.dispatchEvent(event);
  return event;
};
const key = (window, name, target) => (target || window.document).dispatchEvent(
  new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
const viewerOf = (window) => window.document.querySelector('[data-media-viewer]');
const text = (window, selector) => viewerOf(window).querySelector(selector).textContent;

test('a thumbnail opens the viewer at that photo, over all of the group, and Esc returns to it', () => {
  const { window } = overview();
  const viewer = viewerOf(window);
  const event = click(window, window.document.getElementById('t2'));

  assert.equal(event.defaultPrevented, true, 'the link is not followed');
  assert.equal(viewer.hidden, false);
  assert.equal(text(window, '[data-viewer-title]'), 'Trials');
  assert.equal(text(window, '[data-viewer-position]'), '2 of 3');
  assert.equal(text(window, '[data-viewer-context]'), 'Driving Simulator · Added 3 Mar 2026');
  assert.equal(viewer.querySelector('[data-viewer-media] img').getAttribute('src'), '/p/2/xl');
  assert.equal(viewer.querySelector('[data-viewer-download]').getAttribute('href'), '/p/2/download');
  assert.equal(window.document.body.classList.contains('photos-viewer-open'), true);
  assert.equal(window.document.getElementById('content').inert, true, 'the page behind is inert');

  key(window, 'ArrowRight');
  assert.equal(text(window, '[data-viewer-title]'), 'Handover');
  assert.equal(viewer.querySelector('[data-viewer-download]').hidden, true, 'no download offered when there is none');
  key(window, 'ArrowRight');
  assert.equal(text(window, '[data-viewer-position]'), '1 of 3', 'wraps round');
  key(window, 'ArrowLeft');
  assert.equal(text(window, '[data-viewer-position]'), '3 of 3');

  key(window, 'Escape');
  assert.equal(viewer.hidden, true);
  assert.equal(window.document.activeElement.id, 't2', 'focus goes back to the thumbnail');
  assert.equal(window.document.body.classList.contains('photos-viewer-open'), false);
  assert.equal(window.document.getElementById('content').getAttribute('aria-hidden'), null);
  assert.equal(window.location.hash, '#timeline', 'the project pages keep their address');
  window.close();
});

test('a modified click keeps the link (open in a new tab)', () => {
  const { window } = overview();
  const event = click(window, window.document.getElementById('t1'), { ctrlKey: true });
  assert.equal(event.defaultPrevented, false);
  assert.equal(viewerOf(window).hidden, true);
  window.close();
});

test('a video plays in the viewer with its poster, and the page video is paused', () => {
  const { window } = overview();
  let paused = 0;
  window.document.getElementById('inline').pause = () => { paused += 1; };

  click(window, window.document.getElementById('v1'));

  const video = viewerOf(window).querySelector('[data-viewer-media] video');
  assert.ok(video);
  assert.equal(video.getAttribute('src'), '/v/7/stream');
  assert.equal(video.getAttribute('poster'), '/v/7/poster');
  assert.equal(video.controls, true);
  assert.equal(paused, 1);
  assert.equal(viewerOf(window).querySelector('[data-viewer-next]').hidden, true, 'one video: no previous/next');
  assert.equal(text(window, '[data-viewer-position]'), '');
  window.close();
});

test('Tab reaches the video controls and wraps from them back to the toolbar', () => {
  const { window } = overview();
  // jsdom lays nothing out; treat every element as visible.
  window.HTMLElement.prototype.getClientRects = () => [{ width: 1, height: 1 }];
  click(window, window.document.getElementById('v1'));
  const viewer = viewerOf(window);
  const video = viewer.querySelector('[data-viewer-media] video');

  video.focus();
  const tab = new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
  window.document.dispatchEvent(tab);

  assert.equal(tab.defaultPrevented, true, 'the video is the last stop in the loop');
  assert.equal(window.document.activeElement, viewer.querySelector('[data-viewer-zoom-out]'));
  window.close();
});

test('without a JSON list the group is read from the page, in its current order', () => {
  const item = (id, title) => `<a href="/p/${id}/xl" data-media-viewer-item data-media-viewer-group="project-photos"
      data-media-viewer-open="project-photos" data-media-viewer-key="photo-${id}" data-kind="photo"
      data-display-url="/p/${id}/xl" data-title="${title}"><img alt=""></a>`;
  const { window } = page(`<ul id="grid"><li>${item(1, 'One')}</li><li>${item(2, 'Two')}</li><li>${item(3, 'Three')}</li></ul>${viewerMarkup}`);
  const grid = window.document.getElementById('grid');
  grid.prepend(grid.lastElementChild); // dragged: Three, One, Two

  click(window, window.document.querySelector('[data-media-viewer-key="photo-1"]'));
  assert.equal(text(window, '[data-viewer-position]'), '2 of 3');
  key(window, 'ArrowLeft');
  assert.equal(text(window, '[data-viewer-title]'), 'Three');
  window.close();
});

test('swiping moves to the next or previous item, except while zoomed in', () => {
  const { window } = overview();
  click(window, window.document.getElementById('t1'));
  const media = viewerOf(window).querySelector('[data-viewer-media]');
  const swipe = (fromX, toX) => {
    const touch = (x) => ({ clientX: x, clientY: 100, identifier: 1, target: media });
    const start = new window.Event('touchstart', { bubbles: true });
    Object.assign(start, { touches: [touch(fromX)], changedTouches: [touch(fromX)] });
    media.dispatchEvent(start);
    const end = new window.Event('touchend', { bubbles: true });
    Object.assign(end, { touches: [], changedTouches: [touch(toX)] });
    media.dispatchEvent(end);
  };

  swipe(300, 120);
  assert.equal(text(window, '[data-viewer-title]'), 'Trials');
  swipe(120, 300);
  assert.equal(text(window, '[data-viewer-title]'), 'Cover');

  key(window, '+');
  swipe(300, 120);
  assert.equal(text(window, '[data-viewer-title]'), 'Cover', 'a drag while zoomed is for panning');
  window.close();
});

test('keys typed into a field are not viewer commands', () => {
  const { window } = overview('<input id="field">');
  click(window, window.document.getElementById('t1'));
  const field = window.document.getElementById('field');
  key(window, 'ArrowRight', field);
  assert.equal(text(window, '[data-viewer-title]'), 'Cover');
  window.close();
});

test('the Photos gallery uses the shared viewer, with its information panel and #media= links', (t) => {
  // Not <button>: jsdom loads no images, and the gallery disables a tile whose image failed.
  const tile = (id, title, extra = '') => `<div role="button" tabindex="0" data-media-item data-media-key="asset-${id}" data-kind="photo"
      data-display-url="/m/${id}" data-title="${title}" data-context="Project Alpha" data-date="2 Mar 2026"
      data-original-url="/m/${id}/original" data-caption="${title} caption"
      data-people='[{"name":"Asha","url":"/people/1"}]' ${extra}><img data-media-image alt=""></div>`;
  const dom = new JSDOM(`<!doctype html><html><body>
    <div data-photos-library>
      <div class="photos-grid">${tile(1, 'Alpha')}${tile(2, 'Beta')}</div>
    </div>
    <div class="photos-viewer" data-photos-viewer hidden aria-hidden="true">
      <div data-viewer-close></div>
      <div role="dialog">
        <span data-viewer-context></span><strong data-viewer-title></strong><span data-viewer-position></span>
        <a href="#" data-viewer-original></a><a href="#" data-viewer-download></a>
        <button type="button" data-viewer-info aria-pressed="false"></button>
        <button type="button" data-viewer-close></button>
        <button type="button" data-viewer-prev></button><div data-viewer-media></div><button type="button" data-viewer-next></button>
        <aside data-viewer-info-panel aria-hidden="true">
          <dd data-info-context></dd><dd data-info-date></dd><dd data-info-source></dd><dd data-info-classification></dd>
          <div data-info-people-row><dd data-info-people></dd></div>
          <div data-info-caption-row><p data-info-caption></p></div>
        </aside>
      </div>
    </div>
  </body></html>`, { url: 'http://localhost/Photos#media=asset-2', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
  const { window } = dom;
  t.after(() => window.close());
  window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  window.eval(viewerScript);
  window.eval(galleryScript);

  const viewer = window.document.querySelector('[data-photos-viewer]');
  assert.equal(viewer.hidden, false, 'a #media= link opens that item');
  assert.equal(viewer.querySelector('[data-viewer-title]').textContent, 'Beta');
  assert.equal(viewer.querySelector('[data-info-caption]').textContent, 'Beta caption');
  assert.equal(viewer.querySelector('[data-info-people] a').textContent, 'Asha');
  assert.equal(viewer.querySelector('[data-viewer-download]').getAttribute('href'), '/m/2/original', 'downloads the original');

  key(window, 'ArrowRight');
  assert.equal(window.location.hash, '#media=asset-1');
  key(window, 'i');
  assert.equal(viewer.classList.contains('is-info-open'), true);

  key(window, 'Escape');
  assert.equal(viewer.hidden, true);
  assert.equal(viewer.classList.contains('is-info-open'), false);
  assert.equal(window.location.hash, '');

  window.document.querySelector('[data-media-key="asset-1"]').click();
  assert.equal(viewer.hidden, false);
  assert.equal(window.location.hash, '#media=asset-1');
  window.close();
});

test('a list marked data-media-viewer-links keeps the open photo in the address and opens it from a link (Visits)', () => {
  const linkedList = (hash) => {
    const virtualConsole = new VirtualConsole();
    const dom = new JSDOM(`<!doctype html><html><body>
      <main id="content">
        <a id="g1" href="/v/1/xl" data-media-viewer-open="visit-photos" data-media-viewer-key="photo-1">1</a>
        <a id="g2" href="/v/2/xl" data-media-viewer-open="visit-photos" data-media-viewer-key="photo-2">2</a>
      </main>
      <script type="application/json" data-media-viewer-items="visit-photos" data-media-viewer-links="#media=">${JSON.stringify(photos)}</script>
      ${viewerMarkup}</body></html>`, {
      url: `http://localhost/ProjectOfficeReports/Visits/Details/abc${hash}`,
      runScripts: 'outside-only',
      virtualConsole,
    });
    dom.window.eval(viewerScript);
    dom.window.PMMediaViewer.autoWire(dom.window.document);
    return dom.window;
  };

  const window = linkedList('#visit-photo-gallery');
  click(window, window.document.getElementById('g2'));
  assert.equal(window.location.hash, '#media=photo-2', 'the open photo is in the address');
  key(window, 'ArrowRight');
  assert.equal(window.location.hash, '#media=photo-3');
  key(window, 'Escape');
  assert.equal(window.location.hash, '#visit-photo-gallery', 'closing restores the address');
  window.close();

  const shared = linkedList('#media=photo-2');
  assert.equal(viewerOf(shared).hidden, false, 'a shared link opens the viewer');
  assert.equal(text(shared, '[data-viewer-title]'), 'Trials');
  shared.close();
});

test('the mouse wheel zooms a photo, never below its fitted size, and arrows mark the slide direction', () => {
  const { window } = overview();
  click(window, window.document.getElementById('t1'));
  const viewer = viewerOf(window);
  const media = viewer.querySelector('[data-viewer-media]');
  const wheel = (deltaY) => {
    const event = new window.WheelEvent('wheel', { deltaY, bubbles: true, cancelable: true });
    media.dispatchEvent(event);
    return event;
  };

  // jsdom has no layout, so the photo fades in instead of growing out of the thumbnail.
  assert.equal(media.dataset.enter, 'fade');

  assert.equal(wheel(-100).defaultPrevented, true, 'the page behind does not scroll');
  assert.equal(media.classList.contains('is-zoomed'), true);
  assert.ok(Number(media.style.getPropertyValue('--viewer-zoom')) > 1);

  for (let i = 0; i < 10; i += 1) wheel(100);
  assert.equal(media.style.getPropertyValue('--viewer-zoom'), '1');
  assert.equal(media.classList.contains('is-zoomed'), false);
  assert.equal(media.style.getPropertyValue('--viewer-pan-x'), '0px');

  key(window, 'ArrowRight');
  assert.equal(media.dataset.enter, 'next');
  assert.equal(media.querySelector('img').draggable, false);
  key(window, 'ArrowLeft');
  assert.equal(media.dataset.enter, 'previous');

  key(window, 'Escape');
  assert.equal(viewer.hidden, true);
  assert.equal(media.dataset.enter, undefined);
  assert.equal(viewer.classList.contains('is-idle'), false);
  window.close();
});

test('the controls fade after a still moment and return on a key press', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const { window } = overview();
  // The viewer schedules with the page's own timer; route it through the mocked one.
  window.setTimeout = setTimeout;
  window.clearTimeout = clearTimeout;
  click(window, window.document.getElementById('t1'));
  const viewer = viewerOf(window);

  t.mock.timers.tick(2700);
  assert.equal(viewer.classList.contains('is-idle'), false);
  t.mock.timers.tick(200);
  assert.equal(viewer.classList.contains('is-idle'), true);

  key(window, 'ArrowRight');
  assert.equal(viewer.classList.contains('is-idle'), false);
  window.close();
});

test('a gallery that gives thumbnails gets a strip of the nearby ones, and can add items while open', () => {
  const dom = new JSDOM(`<!doctype html><html><body>
    <div class="photos-viewer" data-photos-viewer hidden aria-hidden="true">
      <div data-viewer-close></div>
      <div role="dialog">
        <strong data-viewer-title></strong><span data-viewer-position></span>
        <button type="button" data-viewer-close></button>
        <button type="button" data-viewer-prev></button><div data-viewer-media></div><button type="button" data-viewer-next></button>
        <div data-viewer-strip hidden></div>
      </div>
    </div></body></html>`, { url: 'http://localhost/Photos', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
  const { window } = dom;
  window.eval(viewerScript);
  const viewer = window.document.querySelector('[data-photos-viewer]');
  const make = (count) => Array.from({ length: count }, (_, i) => ({
    key: `asset-${i + 1}`, kind: 'photo', displayUrl: `/m/${i + 1}`, thumbUrl: `/t/${i + 1}`, title: `Picture ${i + 1}` }));
  const nearEnd = [];
  const controller = window.PMMediaViewer.attach(viewer, { items: make(30), onNearEnd: (index, count) => nearEnd.push([index, count]) });
  const strip = viewer.querySelector('[data-viewer-strip]');

  controller.open(0);
  assert.equal(strip.hidden, false);
  assert.equal(strip.children.length, 11, 'the current item and ten after it');
  assert.equal(strip.querySelector('.is-current img').getAttribute('src'), '/t/1');
  assert.deepEqual(nearEnd, []);

  strip.children[5].click();
  assert.equal(viewer.querySelector('[data-viewer-title]').textContent, 'Picture 6');
  assert.equal(viewer.querySelector('[data-viewer-media]').dataset.enter, 'next');
  assert.equal(strip.children.length, 16, 'five before, the current item, ten after');

  controller.open(28);
  assert.deepEqual(nearEnd, [[28, 30]], 'the gallery is told when the end is near');
  controller.setItems(make(60));
  assert.equal(viewer.querySelector('[data-viewer-position]').textContent, '29 of 60');
  assert.equal(strip.children.length, 21);
  assert.equal(viewer.querySelector('[data-viewer-title]').textContent, 'Picture 29', 'the open item stays');

  controller.setItems([{ key: 'only', kind: 'photo', displayUrl: '/m/only' }]);
  assert.equal(strip.hidden, true, 'no strip for one item or without thumbnails');
  window.close();
});

test('a pinch that ends with one finger down carries on as a drag; a video keeps a scrolling stage', () => {
  const { window } = overview();
  click(window, window.document.getElementById('t1'));
  const viewer = viewerOf(window);
  const media = viewer.querySelector('[data-viewer-media]');
  const pointer = (type, pointerId, clientX) => {
    const event = new window.Event(type, { bubbles: true, cancelable: true });
    Object.assign(event, { pointerId, clientX, clientY: 100 });
    media.dispatchEvent(event);
  };

  assert.equal(media.classList.contains('has-video'), false);
  pointer('pointerdown', 1, 100);
  pointer('pointerdown', 2, 200);
  pointer('pointermove', 2, 300);
  assert.equal(media.style.getPropertyValue('--viewer-zoom'), '2', 'fingers twice as far apart: twice the size');
  assert.equal(media.classList.contains('is-panning'), false);

  pointer('pointerup', 2, 300);
  assert.equal(media.classList.contains('is-panning'), true, 'the finger still down now drags the photo');
  pointer('pointerup', 1, 100);
  assert.equal(media.classList.contains('is-panning'), false);

  key(window, 'Escape');
  click(window, window.document.getElementById('v1'));
  assert.equal(media.classList.contains('has-video'), true);
  window.close();
});
