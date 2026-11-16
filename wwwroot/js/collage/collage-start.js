// PRISM Collage Studio — the start screen: blank pages, layouts and the drafts
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the few elements and helpers it uses.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageStart = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createStart(deps) {
    const { core, $, sizeSelect, layoutGrid } = deps;
    /** A small drawing of a layout's frames, for its button. */
    function layoutPicture(key) {
      const cells = core.LAYOUTS[key].cells
        .map(([x, y, w, h]) => `<rect x="${(4 + x * 9.33 + 1.5).toFixed(1)}" y="${(4 + y * 6.33 + 1.5).toFixed(1)}" width="${(w * 9.33 - 3).toFixed(1)}" height="${(h * 6.33 - 3).toFixed(1)}" rx="2.5"/>`)
        .join('');
      return `<svg viewBox="0 0 120 84" aria-hidden="true" focusable="false"><rect class="clg-layout-pic__page" width="120" height="84" rx="6"/>${cells}</svg>`;
    }

    function layoutButton(key, dataName) {
      const entry = core.LAYOUTS[key];
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'clg-layout-card';
      button.dataset[dataName] = key;
      button.innerHTML = `${layoutPicture(key)}<span></span>`;
      button.querySelector('span').textContent = entry.label;
      button.setAttribute('aria-label', `${entry.label}, ${entry.cells.length} photos`);
      return button;
    }

    function renderStart() {
      const sizes = $('[data-sizes]');
      sizes.textContent = '';
      for (const [key, size] of Object.entries(core.SIZES)) {
        const ratio = size.width / size.height;
        const w = ratio >= 1 ? 34 : 34 * ratio;
        const h = ratio >= 1 ? 34 / ratio : 34;
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'clg-size-card';
        button.dataset.startSize = key;
        button.innerHTML = `<span class="clg-size-card__shape" style="width:${w.toFixed(1)}px;height:${h.toFixed(1)}px" aria-hidden="true"></span><span><b></b><small></small></span>`;
        button.querySelector('b').textContent = size.label;
        button.querySelector('small').textContent = size.note;
        sizes.appendChild(button);
      }
      const layouts = $('[data-start-layouts]');
      layouts.textContent = '';
      for (const key of Object.keys(core.LAYOUTS)) layouts.appendChild(layoutButton(key, 'startLayout'));
      layoutGrid.textContent = '';
      for (const key of Object.keys(core.LAYOUTS)) layoutGrid.appendChild(layoutButton(key, 'layout'));
      sizeSelect.innerHTML = '';
      for (const [key, size] of Object.entries(core.SIZES)) {
        const option = document.createElement('option');
        option.value = key;
        option.textContent = size.label;
        sizeSelect.appendChild(option);
      }
    }

    return { renderStart };
  }

  return Object.freeze({ createStart });
});
