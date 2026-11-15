// PRISM Collage Studio — edits that are still going on (typing, a slider, a colour picker, a
// nudge): kept in the draft at once and recorded for Undo once they pause, or as soon as
// anything else happens (docs/ux/collage-review-fixes.md, PRs 3 and 12). No page or timer of its
// own: the editor gives it what to call.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageEdits = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /**
   * record() makes one Undo step of what is on the page now; keep() asks for the draft to be
   * written. timers can be replaced in tests.
   */
  function createHeldEdits({ record, keep, timers = { set: (fn, ms) => setTimeout(fn, ms), clear: (id) => clearTimeout(id) } }) {
    let timer = 0;
    let pending = false;

    /** Records a held edit now; true when there was one. */
    function flush() {
      timers.clear(timer);
      if (!pending) return false;
      pending = false;
      record();
      return true;
    }

    /** An edit goes on: kept in the draft now, recorded once it pauses for this long. */
    function hold(pause = 600) {
      timers.clear(timer);
      pending = true;
      timer = timers.set(flush, pause);
      if (keep) keep();
    }

    /** The next flush records even if nothing is held (a change made without holding). */
    function mark() {
      pending = true;
    }

    /** Forgets a held edit without recording it (the collage is being closed). */
    function drop() {
      timers.clear(timer);
      pending = false;
    }

    return Object.freeze({ hold, flush, mark, drop, get pending() { return pending; } });
  }

  return Object.freeze({ createHeldEdits });
});
