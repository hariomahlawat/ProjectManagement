// PRISM Collage Studio — the draft being edited, and the drafts on the start screen: when a draft
// is written, when the user is asked about room, deleting with Undo, and what is purged
// (docs/ux/collage-review-2-fixes.md, PR 2). It keeps no pictures and draws nothing; the editor
// gives it a snapshot of the collage when it saves.
//
// The rules it keeps:
// - The open draft is never purged from this tab, and a draft deleted or purged by another tab
//   while it is open here is written back whole (its photos too) at once.
// - "Delete the oldest draft?" is asked only when the user acts (starting a collage, or "Keep as
//   a draft"), never from the autosave timer.
// - Drafts are written one at a time, in order.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageDraftSession = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  const CHANNEL = 'prism-collage-drafts';
  const SAVE_DELAY_MS = 500;

  /**
   * drafts is collage-drafts.js (or a stand-in with the same calls); core is collage-core.js;
   * ask(options) resolves true or false; snapshot() gives what is saved:
   * { title, autoTitle, model, size, photos: [{ id, name, file, width, height }], thumbnail,
   *   hasContent }. onStatus(text, warning) shows the draft status; onUndo(state) shows or hides
   * the Undo bar ({ title } or null); onChanged() asks for the drafts list to be drawn again.
   */
  function createDraftSession({
    drafts, core, ask, snapshot,
    onStatus = () => {}, onUndo = () => {}, onChanged = () => {},
    now = () => Date.now(), setTimer = setTimeout, clearTimer = clearTimeout,
    channel = openChannel()
  }) {
    // The draft being edited: { id, created, off, started }; null on the start screen.
    let current = null;
    let timer = 0;
    let pending = false;
    let writing = false;
    let failed = '';
    let chain = Promise.resolve(true);
    let askedToKeep = false;
    let undo = null;
    let undoTimer = 0;

    if (channel) {
      channel.onmessage = (event) => {
        const message = event?.data;
        if (!message || message.type !== 'gone' || !current?.created || message.id !== current.id) return;
        // Another tab deleted or purged the draft open here: it is written back, photos and all,
        // so the work in this tab is not lost.
        pending = true;
        saveNow();
      };
    }

    function tell(id) {
      try { channel?.postMessage({ type: 'gone', id }); } catch { /* another tab may not hear */ }
    }

    /** Why the draft is not kept, in a sentence, or '' when it is. */
    function problem() {
      if (!drafts) return 'Drafts are not available in this browser.';
      if (current?.off === 'full') return `This collage is not kept as a draft: ${core.MAX_DRAFTS} drafts are kept already. Use “Keep as a draft” to make room.`;
      if (current?.off) return 'This collage is not kept as a draft.';
      if (failed === 'full') return 'The draft was not saved: this browser has no room left. Delete old drafts on the start screen, or save a picture or a PDF.';
      if (failed) return 'The draft was not saved: this browser does not keep data here (a private window, or storage turned off). Save a picture or a PDF to keep your work.';
      return '';
    }

    function status(text, warning = false) { onStatus(text, warning); }

    /** A new collage, or an opened draft, is now being edited. */
    function begin({ id, created = false, started = now(), off = false }) {
      clearTimer(timer);
      current = { id, created, off, started };
      pending = false;
      failed = '';
      status(created ? 'Draft saved in this browser' : '');
    }

    /** Back on the start screen: nothing is being edited. */
    function end() {
      clearTimer(timer);
      current = null;
      pending = false;
    }

    /** Something changed: the draft is written half a second after the last change. */
    function changed() {
      if (!current || current.off || !drafts) return;
      pending = true;
      clearTimer(timer);
      timer = setTimer(() => { saveNow(); }, SAVE_DELAY_MS);
    }

    /**
     * Asks to delete the oldest draft when this browser keeps the most drafts it can; only ever
     * called when the user acts. Resolves true when there is room for the draft being edited.
     */
    async function makeRoom() {
      if (!drafts) return false;
      let all;
      try {
        all = await drafts.list();
      } catch {
        return true;
      }
      const { shown } = core.sortDrafts(all.filter((d) => d.id !== current?.id), now());
      const oldest = core.draftToMakeRoom(shown);
      if (!oldest) return true;
      const ok = await ask({
        title: `Delete the oldest draft, “${oldest.title}”?`,
        message: `Collage Studio keeps up to ${core.MAX_DRAFTS} drafts in this browser. Deleting the oldest makes room for this one.`,
        confirmText: 'Delete oldest draft',
        cancelText: 'Do not keep this one',
        tone: 'warning'
      });
      if (!ok) return false;
      await purge(oldest.id);
      return true;
    }

    /** Starting a collage: room is made first, when needed, while the user is choosing. */
    const prepareNew = makeRoom;

    /** "Keep as a draft" on a collage that was not kept. */
    async function keepAgain() {
      if (!current?.off) return false;
      const room = await makeRoom();
      if (!current) return false;
      if (!room) { status('Not kept as a draft', true); return false; }
      current.off = false;
      pending = true;
      status('Saving draft…');
      return saveNow();
    }

    /**
     * Writes the draft now. Resolves true when the draft is kept, or there is nothing to keep
     * yet. Never asks anything.
     */
    function saveNow() {
      clearTimer(timer);
      chain = chain.then(() => write(), () => write());
      return chain;
    }

    async function write() {
      if (!current || !drafts || current.off) return false;
      if (!pending && current.created) return !failed;
      pending = false;
      const forDraft = current;
      const shot = snapshot();
      // A draft starts once it has a photo or some text; an empty page is not worth keeping.
      if (!forDraft.created && !shot.hasContent) return true;
      try {
        if (!forDraft.created) {
          const { shown } = core.sortDrafts(await drafts.list(), now());
          if (core.draftToMakeRoom(shown.filter((d) => d.id !== forDraft.id))) {
            // Full since the collage was started (another tab): the user is not asked from a
            // timer, so the collage is left out until "Keep as a draft".
            forDraft.off = 'full';
            if (forDraft === current) status('Not kept as a draft', true);
            return false;
          }
        }
        const record = {
          id: forDraft.id,
          title: shot.title,
          // Not named by the user: the title follows the first words, and the name box stays empty.
          autoTitle: shot.autoTitle,
          started: forDraft.started || now(),
          saved: now(),
          deletedAt: null,
          model: shot.model,
          size: shot.size,
          photoCount: shot.photos.length,
          thumbnail: shot.thumbnail || ''
        };
        if (forDraft === current) status('Saving draft…');
        writing = true;
        try {
          // Photos the database does not hold for this draft are written with it, so a draft
          // purged elsewhere comes back whole.
          await drafts.save(record, shot.photos);
        } finally {
          writing = false;
        }
        forDraft.created = true;
        if (forDraft !== current) return true;
        failed = '';
        status('Draft saved in this browser');
        if (!askedToKeep) { askedToKeep = true; drafts.keep?.(); }
        return true;
      } catch (error) {
        if (forDraft !== current) return false;
        failed = drafts.reasonOf ? drafts.reasonOf(error) : 'blocked';
        status(failed === 'full' ? 'Draft not saved: no room left' : 'Draft not saved in this browser', true);
        return false;
      }
    }

    /** Removes a draft and its photos for good, unless it is the one open here. */
    async function purge(id) {
      if (!drafts || id === current?.id) return;
      await drafts.purge(id);
      tell(id);
    }

    /** The drafts for the start screen; ones whose Undo has gone are purged. */
    async function list() {
      const all = await drafts.list();
      const { shown, purge: old } = core.sortDrafts(all, now());
      for (const id of old) if (id !== undo?.id) purge(id).catch(() => {});
      return shown;
    }

    /** Hides a draft at once; it is removed for good once Undo has gone. */
    async function remove(id) {
      if (!drafts || id === current?.id) return null;
      const record = await drafts.get(id).catch(() => null);
      if (!record) return null;
      if (undo && undo.id !== id) {
        const earlier = undo.id;
        purge(earlier).catch(() => {});
      }
      await drafts.markDeleted(id, now());
      tell(id);
      clearTimer(undoTimer);
      undo = { id, title: record.title };
      onUndo(undo);
      undoTimer = setTimer(() => {
        if (undo?.id === id) { undo = null; onUndo(null); }
        purge(id).catch(() => {}).then(() => onChanged());
      }, core.DRAFT_UNDO_MS);
      onChanged();
      return record;
    }

    async function restore() {
      if (!undo) return false;
      const id = undo.id;
      clearTimer(undoTimer);
      undo = null;
      onUndo(null);
      await drafts.restore(id);
      onChanged();
      return true;
    }

    /** A draft and its photos, read for opening; null when it has gone or was deleted. */
    async function read(id) {
      const record = await drafts.get(id);
      if (!record || record.deletedAt) return null;
      const photos = await drafts.photosOf(id);
      return { record, photos };
    }

    /** True while the draft is still there (not deleted or purged since it was read). */
    async function stillThere(id) {
      try {
        const record = await drafts.get(id);
        return Boolean(record && !record.deletedAt);
      } catch {
        return false;
      }
    }

    return Object.freeze({
      begin, end, changed, saveNow, keepAgain, prepareNew, problem,
      list, remove, restore, read, stillThere, purge,
      get draft() { return current; },
      get pending() { return pending; },
      get writing() { return writing; },
      get failed() { return failed; },
      get undoing() { return undo; }
    });
  }

  function openChannel() {
    try {
      return typeof BroadcastChannel === 'function' ? new BroadcastChannel(CHANNEL) : null;
    } catch {
      return null;
    }
  }

  return Object.freeze({ createDraftSession, SAVE_DELAY_MS });
});
