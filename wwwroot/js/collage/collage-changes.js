// PRISM Collage Studio — the one way a change reaches Undo and the draft
// (docs/ux/collage-review-2-fixes.md, PR 4).
//
// The editor changes the collage, then tells this change log what kind of change it was:
// - commit(): a finished change (a click, a drag let go): one Undo step, kept in the draft.
// - hold(): a change still going on (typing, a slider, a colour picker, a nudge, the wheel): kept in
//   the draft at once, made into one Undo step once it pauses.
// - record(): a finished change that needs no redraw (one step, kept in the draft).
// Nothing else in the editor touches the Undo history.
//
// A held change always becomes its own step before anything else changes the collage: the
// editor calls boundary() at the start of every user action (a pointer pressed, focus moved, a
// key that is not part of the held change), so typing followed at once by a click on Bold is two
// Undo steps, not one.
(function (root, factory) {
  'use strict';
  const edits = typeof module === 'object' && module.exports ? require('./collage-edits.js') : root.PrismCollageEdits;
  const api = factory(edits);
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageChanges = api;
})(typeof self !== 'undefined' ? self : this, function (edits) {
  'use strict';

  /**
   * core is collage-core.js; getModel() is the collage now. onRecorded(kind) is told after each
   * Undo step ('commit', or 'quiet' for record() and held changes); onKeep() asks for the draft to
   * be written. timers can be replaced in tests.
   */
  function createChangeLog({ core, getModel, onRecorded = () => {}, onKeep = () => {}, timers }) {
    let history = core.createHistory(getModel());
    const held = edits.createHeldEdits({
      record: () => { history.record(getModel()); onRecorded('quiet'); },
      keep: () => onKeep(),
      ...(timers ? { timers } : {})
    });

    /** A new collage, or an opened draft: Undo starts again from it. */
    function start(model) {
      held.drop();
      history = core.createHistory(model);
    }

    /** A finished change: one Undo step, kept in the draft. */
    function commit() {
      // Anything held was already made its own step at the start of this action; a change that
      // still finds one held (made without a user action) joins it in one step.
      held.drop();
      history.record(getModel());
      onKeep();
      onRecorded('commit');
    }

    /** A finished change that needs no redraw. */
    function record() {
      held.drop();
      history.record(getModel());
      onKeep();
      onRecorded('quiet');
    }

    /** A change still going on: kept in the draft now, one Undo step once it pauses. */
    function hold(pause = 600) {
      held.hold(pause);
    }

    /** Makes a held change its own Undo step now; true when there was one. */
    function flush() {
      return held.flush();
    }

    /** The collage one step back, or null when there is none. Held changes are recorded first. */
    function undo() {
      flush();
      if (!history.canUndo()) return null;
      const model = history.undo();
      onKeep();
      return model;
    }

    function redo() {
      flush();
      if (!history.canRedo()) return null;
      const model = history.redo();
      onKeep();
      return model;
    }

    return Object.freeze({
      start, commit, record, hold, flush, undo, redo,
      drop: () => held.drop(),
      /** The next flush records even if nothing is held (a change made without holding). */
      mark: () => held.mark(),
      canUndo: () => history.canUndo(),
      canRedo: () => history.canRedo(),
      mentions: (photoId) => history.mentions(photoId),
      get pending() { return held.pending; }
    });
  }

  const NUDGE_KEYS = new Set(['arrowleft', 'arrowright', 'arrowup', 'arrowdown', 'shift', 'control', 'alt', 'meta']);

  /**
   * True when an event starts a new user action, so a held change ends before it: a pointer
   * pressed, focus moved to another control, or a key that is neither typing in a field nor a
   * nudge of the selected item.
   */
  function isBoundary(event) {
    if (!event) return false;
    if (event.type === 'pointerdown' || event.type === 'focusin') return true;
    if (event.type !== 'keydown') return false;
    const key = String(event.key || '').toLowerCase();
    if (NUDGE_KEYS.has(key)) return false;
    const target = event.target;
    const editable = Boolean(target && target.closest && target.closest('input, textarea, select, [contenteditable]'));
    // Typing in a field is the held change itself; Ctrl shortcuts in a field are not.
    if (editable && !(event.ctrlKey || event.metaKey)) return false;
    return true;
  }

  return Object.freeze({ createChangeLog, isBoundary });
});
