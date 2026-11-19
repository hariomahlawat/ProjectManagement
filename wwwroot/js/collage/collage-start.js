// PRISM Collage Studio — the start screen: the three choices, blank pages, layouts and the drafts
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the few elements and helpers it uses.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageStart = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // What the start screen offers first: cards, posters or a collage (docs/ux/collage-usability-plan.md,
  // PR 2). Only the chosen one's section shows; the choice is remembered in this browser.
  const CHOICES = Object.freeze(['card', 'poster', 'collage']);
  const CHOICE_KEY = 'prism-collage-start';
  /** A remembered choice, or cards when there is none or it is not one of the three. */
  const choiceOf = (value) => (CHOICES.includes(value) ? value : 'card');

  function createStart(deps) {
    const { core, $, sizeSelect, layoutGrid } = deps;

    const choices = $('[data-start-choices]');
    const startView = $('[data-view="start"]');
    function readChoice() {
      try { return choiceOf(window.localStorage?.getItem(CHOICE_KEY)); } catch { return 'card'; }
    }
    /** Shows one section of the start screen; `keep` remembers it for the next visit. */
    function showChoice(value, { keep = false } = {}) {
      const chosen = choiceOf(value);
      choices?.querySelectorAll('[data-start-choice]').forEach((button) => {
        button.setAttribute('aria-pressed', String(button.dataset.startChoice === chosen));
      });
      startView?.querySelectorAll('[data-start-section]').forEach((section) => {
        section.hidden = section.dataset.startSection !== chosen;
      });
      if (keep) {
        try { window.localStorage?.setItem(CHOICE_KEY, chosen); } catch { /* not kept; fine */ }
      }
      return chosen;
    }
    choices?.addEventListener('click', (event) => {
      const button = event.target.closest('[data-start-choice]');
      if (button) showChoice(button.dataset.startChoice, { keep: true });
    });
    showChoice(readChoice());

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
      // Blank pages here are the pages and screens; cards and pocket cards are in the size menu
      // (and get their own row with the card designs, docs/ux/invitations-posters.md).
      for (const [key, size] of Object.entries(core.SIZES).filter(([, each]) => each.group === 'page')) {
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
      for (const group of core.sizesByGroup()) {
        const list = document.createElement('optgroup');
        list.label = group.label;
        for (const key of group.keys) {
          const option = document.createElement('option');
          option.value = key;
          option.textContent = core.SIZES[key].label;
          list.appendChild(option);
        }
        sizeSelect.appendChild(list);
      }
      // A size typed in mm (docs/ux/invitations-posters.md, PR 5b): chosen here, typed in Page settings.
      const custom = document.createElement('optgroup');
      custom.label = 'Custom';
      custom.dataset.customSizes = '';
      const typed = document.createElement('option');
      typed.value = 'custom';
      typed.textContent = 'Custom size…';
      custom.appendChild(typed);
      sizeSelect.appendChild(custom);
    }

    /** Shows the page's size in the size menu, adding a size typed in mm when it is not listed. */
    function showSize(key) {
      if (core.isSize(key) && !core.SIZES[key] && ![...sizeSelect.options].some((option) => option.value === key)) {
        const option = document.createElement('option');
        option.value = key;
        option.textContent = core.sizeOf(key).label;
        const group = sizeSelect.querySelector('[data-custom-sizes]') || sizeSelect;
        group.insertBefore(option, group.firstChild);
      }
      sizeSelect.value = key;
    }

    return { renderStart, showSize, showChoice };
  }

  return Object.freeze({ createStart, choiceOf, CHOICES, CHOICE_KEY });
});
