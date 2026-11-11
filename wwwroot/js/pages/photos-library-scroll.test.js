// Photos timeline (photos-library.js): the next part is added while scrolling, a split day is
// joined, and the page links take over, from the right place, when a part cannot be loaded.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const viewerScript = fs.readFileSync(path.resolve(__dirname, '../widgets/media-viewer.js'), 'utf8');
const libraryScript = fs.readFileSync(path.resolve(__dirname, 'photos-library.js'), 'utf8');

const tile = (id) => `<div role="button" tabindex="0" class="photos-tile" data-media-item data-media-key="asset-${id}" data-kind="photo"
    data-display-url="/m/${id}" data-title="Picture ${id}" data-aspect-ratio="1.5"><img data-media-image src="/t/${id}" alt=""></div>`;
const day = (key, label, ids) => `<section class="photos-day" aria-labelledby="day-${key}">
    <header class="photos-day__header"><h3 id="day-${key}">${label}</h3><span><span data-day-count="${ids.length}">${ids.length} items</span></span></header>
    <div class="photos-grid">${ids.map(tile).join('')}</div></section>`;

function createPage(responses) {
    const dom = new JSDOM(`<!doctype html><html><body>
      <div data-photos-library>
        <div class="photos-timeline" data-photos-timeline data-next-part-url="/Photos?View=photos&amp;PageNumber=2&amp;handler=TimelinePart">
          <h2 class="photos-month" id="month-202611">November 2026</h2>
          ${day('20261106', 'Friday 6 November', [1, 2])}
        </div>
        <div data-photos-more hidden>
          <span data-photos-more-loading>Loading</span>
          <span data-photos-more-error hidden><button type="button" data-photos-more-retry>Try again</button></span>
        </div>
        <nav data-photos-pagination>
          <span data-photos-page-label>Page 1</span>
          <a href="/Photos?View=photos&amp;PageNumber=2" data-photos-next-page>Next</a>
        </nav>
      </div>
      <div class="photos-viewer" data-photos-viewer hidden aria-hidden="true">
        <div role="dialog"><strong data-viewer-title></strong><span data-viewer-position></span>
          <button type="button" data-viewer-close></button>
          <button type="button" data-viewer-prev></button><div data-viewer-media></div><button type="button" data-viewer-next></button>
        </div>
      </div></body></html>`,
    { url: 'http://localhost/Photos', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    const { window } = dom;
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });

    // The page asks for the next part whenever the end marker is (still) near the screen.
    const observers = [];
    window.IntersectionObserver = class {
        constructor(callback) { this.callback = callback; observers.push(this); }
        observe() { window.setTimeout(() => this.callback([{ isIntersecting: true }]), 0); }
        unobserve() {}
    };
    const requested = [];
    window.fetch = (url) => {
        requested.push(url);
        const next = responses.shift();
        if (!next) return Promise.reject(new Error('offline'));
        return Promise.resolve({
            ok: true,
            status: 200,
            text: () => Promise.resolve(next.html),
            headers: { get: (name) => (name === 'X-Photos-Next-Part' ? next.nextUrl : null) }
        });
    };

    window.eval(viewerScript);
    window.eval(libraryScript);
    const settle = async () => { for (let i = 0; i < 12; i += 1) await new Promise(resolve => window.setTimeout(resolve, 0)); };
    return { window, requested, settle, q: (selector) => window.document.querySelector(selector), all: (selector) => [...window.document.querySelectorAll(selector)] };
}

test('parts are added until none is left; a day split across parts is joined', async () => {
    const { window, requested, settle, q, all } = createPage([
        { html: `<h2 class="photos-month" id="month-202611">November 2026</h2>${day('20261106', 'Friday 6 November', [3])}${day('20261102', 'Monday 2 November', [4])}`,
          nextUrl: '/Photos?View=photos&PageNumber=3&handler=TimelinePart' },
        { html: `<h2 class="photos-month" id="month-202610">October 2026</h2>${day('20261021', 'Wednesday 21 October', [5, 6])}`, nextUrl: '' }
    ]);
    await settle();

    assert.deepEqual(requested, ['/Photos?View=photos&PageNumber=2&handler=TimelinePart', '/Photos?View=photos&PageNumber=3&handler=TimelinePart']);
    assert.deepEqual(all('[data-media-item]').map(node => node.dataset.mediaKey), ['asset-1', 'asset-2', 'asset-3', 'asset-4', 'asset-5', 'asset-6']);
    assert.deepEqual(all('.photos-month').map(node => node.id), ['month-202611', 'month-202610'], 'a month heading is not repeated');
    assert.equal(all('.photos-day').length, 3, 'the part of 6 November joins the day already on the page');
    assert.equal(q('#day-20261106').closest('.photos-day').querySelector('[data-day-count]').textContent, '3 items');
    assert.equal(q('[data-photos-more]').hidden, true);
    assert.equal(q('[data-photos-pagination]').hidden, true, 'script is loading the timeline, so the page links stay away');

    // Pictures added later open in the viewer, in timeline order.
    all('[data-media-item]')[4].click();
    assert.equal(q('[data-viewer-title]').textContent, 'Picture 5');
    assert.equal(q('[data-viewer-position]').textContent, '5 of 6');
    window.close();
});

test('when a later part cannot be loaded, Next opens the part that failed', async () => {
    const { window, settle, q, all } = createPage([
        { html: day('20261102', 'Monday 2 November', [3]), nextUrl: '/Photos?View=photos&PageNumber=3&handler=TimelinePart' }
    ]);
    await settle();

    assert.equal(all('[data-media-item]').length, 3, 'the part that loaded stays');
    assert.equal(q('[data-photos-pagination]').hidden, false);
    assert.equal(q('[data-photos-next-page]').getAttribute('href'), '/Photos?View=photos&PageNumber=3');
    assert.equal(q('[data-photos-page-label]').hidden, true);
    assert.equal(q('[data-photos-more-error]').hidden, false);
    assert.equal(q('[data-photos-more-loading]').hidden, true);
    window.close();
});
