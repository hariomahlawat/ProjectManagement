// PRISM Collage Studio — the one way the collage changes (docs/ux/collage-review-4-fixes.md, PR 9).
//
// The collage the editor holds is frozen: nothing can change it in place. A change is made with
// edit(): the collage is copied, the change is made to the copy, the copy is frozen and becomes the
// collage, and the change log is told how to keep it (a finished change, one still going on, a
// quiet one, or none yet while something is being dragged). So every change reaches Undo and the
// draft the same way, and a part that tried to change the collage directly would fail at once.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageModel = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /** The value and everything in it frozen, in place; returns it. */
  function deepFreeze(value) {
    if (value && typeof value === 'object' && !Object.isFrozen(value)) {
      Object.freeze(value);
      for (const key of Object.keys(value)) deepFreeze(value[key]);
    }
    return value;
  }

  /** A copy that can be changed (the collage is plain JSON: no dates, maps or functions). */
  function editable(model) {
    return JSON.parse(JSON.stringify(model));
  }

  /** How a change is kept: the names edit() takes. */
  const HOW = Object.freeze(['commit', 'hold', 'record', 'live', 'none']);

  /**
   * getModel() and setModel(model) read and replace the editor's collage. keep is the change
   * log's side: { commit, hold, record, live } — each called after a change made that way ('none'
   * calls nothing: the caller records it, as a new item placed and then committed).
   */
  function createModelEditor({ getModel, setModel, keep }) {
    /**
     * Makes a change: change(draft) changes a copy of the collage (or returns a new collage), and
     * the result becomes the collage, frozen. Returns what change returned when it is not a
     * collage (for a change that also answers, such as "was the photo placed?").
     */
    function edit(change, how = 'commit') {
      if (!HOW.includes(how)) throw new Error(`Unknown way to keep a change: ${how}`);
      const draft = editable(getModel());
      const answer = change(draft);
      const isModel = answer && typeof answer === 'object' && Array.isArray(answer.items);
      setModel(deepFreeze(isModel ? answer : draft));
      if (how !== 'none') keep[how]?.();
      return isModel ? undefined : answer;
    }

    /** A change to one item, found by id in the copy; nothing happens when it is gone. */
    function editItem(id, change, how = 'commit') {
      return edit((draft) => {
        const item = draft.items.find((each) => each.id === id);
        return item ? change(item, draft) : undefined;
      }, how);
    }

    return Object.freeze({ edit, editItem });
  }

  return Object.freeze({ deepFreeze, editable, createModelEditor, HOW });
});
