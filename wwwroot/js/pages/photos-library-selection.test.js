// Photos selecting (photos-library.js): Escape ends selecting only when nothing lies over the
// page, and a drag that ended away from a picture does not swallow the next click.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const libraryScript = fs.readFileSync(path.resolve(__dirname, 'photos-library.js'), 'utf8');

const tile = (id) => `<button type="button" class="photos-tile" data-media-item data-media-key="asset-${id}" data-asset-id="${id}" data-kind="photo">
    <span class="photos-tile__select"></span><img alt=""></button>`;

function createPage() {
    const dom = new JSDOM(`<!doctype html><html><body>
      <div data-photos-library>
        <button type="button" data-photos-select-toggle aria-pressed="false"><span>Select</span></button>
        <div data-photos-selection-bar hidden><span data-selection-count></span></div>
        <div class="photos-grid">${[1, 2, 3].map(tile).join('')}</div>
      </div>
      <div class="modal" id="albumModal"></div>
      <div class="offcanvas" id="filters"></div>
    </body></html>`, { url: 'http://localhost/Photos', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    const { window } = dom;
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.addEventListener('error', (event) => { throw event.error || new Error(event.message); });
    window.eval(libraryScript);
    const doc = window.document;
    const tiles = [...doc.querySelectorAll('[data-media-item]')];
    const click = (element, init = {}) => element.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ...init }));
    const pointer = (type, target, init = {}) => {
        const event = new window.MouseEvent(type, { bubbles: true, cancelable: true, button: 0, ...init });
        Object.defineProperty(event, 'pointerType', { value: 'mouse' });
        target.dispatchEvent(event);
    };
    const escape = (target = doc.body) => target.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    const chosen = () => tiles.filter(node => node.classList.contains('is-selected')).length;
    return { window, doc, tiles, click, pointer, escape, chosen };
}

test('Escape closes what lies over the page first; only then does it end selecting', () => {
    const { window, doc, tiles, click, escape, chosen } = createPage();
    click(doc.querySelector('[data-photos-select-toggle]'));
    click(tiles[0]);
    click(tiles[1]);
    assert.equal(chosen(), 2);

    const modal = doc.getElementById('albumModal');
    modal.classList.add('show');
    escape(modal);
    assert.equal(chosen(), 2, 'Escape pressed to close the album dialog keeps the selection');
    modal.classList.remove('show');

    const drawer = doc.getElementById('filters');
    drawer.classList.add('show');
    escape(drawer);
    assert.equal(chosen(), 2, 'and so does Escape pressed to close the filter drawer');
    drawer.classList.remove('show');

    escape();
    assert.equal(chosen(), 0, 'with nothing open, Escape ends selecting');
    assert.equal(doc.querySelector('[data-photos-selection-bar]').hidden, true);
    window.close();
});

test('after a drag that ends away from a picture, the next click on a picture counts', () => {
    const { window, doc, tiles, click, pointer, chosen } = createPage();
    click(doc.querySelector('[data-photos-select-toggle]'));

    // Drag a box from inside the grid to empty space: no click on a picture follows it.
    pointer('pointerdown', tiles[0], { clientX: 10, clientY: 10 });
    pointer('pointermove', doc, { clientX: 60, clientY: 60 });
    pointer('pointerup', doc.body, { clientX: 60, clientY: 60 });
    assert.equal(doc.querySelector('.photos-selection-lasso'), null, 'the box is removed');
    const before = chosen();

    pointer('pointerdown', tiles[2], { clientX: 5, clientY: 5 });
    pointer('pointerup', tiles[2], { clientX: 5, clientY: 5 });
    click(tiles[2]);
    assert.equal(tiles[2].classList.contains('is-selected'), true, 'the click is not swallowed');
    assert.equal(chosen(), before + 1);
    window.close();
});
