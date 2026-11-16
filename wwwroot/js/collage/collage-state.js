// PRISM Collage Studio — the editor's shared state: what every part may read, and the named
// changes each part is given (docs/ux/collage-review-3-fixes.md, PR 10).
//
// The editor keeps its values (the collage, what is selected, the zoom...) in one place. The parts
// made from it (the page, the panels, the tray, the keys, the drafts screen) read them through
// `state`, which has no way to set anything: assigning to it throws. A part changes a value only
// through a named change it was handed, so each value has one owner, and every other part asks
// that owner (for example, the tray chooses the photo it placed with choose(id); it never writes
// the selection itself).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageState = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /**
   * `values` maps each name to a function that reads it. `changes` maps each change's name to the
   * function that makes it. Returns { state, change }: `state` reads every value (read only, and
   * frozen), `change` holds the named changes (frozen).
   */
  function createEditorState(values, changes = {}) {
    const state = {};
    for (const [name, read] of Object.entries(values)) {
      if (typeof read !== 'function') throw new TypeError(`State value ${name} needs a function that reads it.`);
      Object.defineProperty(state, name, { get: read, enumerable: true });
    }
    for (const [name, make] of Object.entries(changes)) {
      if (typeof make !== 'function') throw new TypeError(`Change ${name} needs a function.`);
    }
    return { state: Object.freeze(state), change: Object.freeze({ ...changes }) };
  }

  /**
   * Parts made later than the parts that use them (the settings panel, the tray...): `later` maps
   * each name to a function returning it once it exists. Reading one before then throws, so a part
   * can never act on a part that is not there yet.
   */
  function createLaterParts(later) {
    const parts = {};
    for (const [name, find] of Object.entries(later)) {
      Object.defineProperty(parts, name, {
        enumerable: true,
        get() {
          const found = find();
          if (found === undefined) throw new Error(`The editor part ${name} is not ready yet.`);
          return found;
        }
      });
    }
    return Object.freeze(parts);
  }

  return { createEditorState, createLaterParts };
});
