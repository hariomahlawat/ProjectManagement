// PRISM Collage Studio — the keyboard (shortcuts, nudges, Enter and Esc) and the mouse wheel
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the helpers it uses, `state` (the editor's values, read only) and the named
// changes this part may make (collage-state.js, docs/ux/collage-review-3-fixes.md, PR 10).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageKeys = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createKeys(deps) {
    const { state, change, core, views, stageBox, props, photos, itemById, selectedItem, announce, setShift, stopPreview, setZoom, syncPage, select, stopAdjust, zoomCrop, holdEdit, changeSelected, act, focusTextField, setTarget, sayLocked } = deps;
    // ---------------------------------------------------------------------------
    // Keyboard and the mouse wheel
    // ---------------------------------------------------------------------------

    document.addEventListener('keydown', (event) => {
      if (event.key === 'Shift') setShift(true);
      if (views.editor.hidden || state.leaving || document.querySelector('dialog[open]')) return;
      const typing = event.target.closest?.('input, textarea, select, [contenteditable]');
      // Enter on a button, link or tile belongs to it; only Enter on the page itself acts on the item.
      const onPage = event.target === document.body || stageBox.contains(event.target);
      const key = event.key.toLowerCase();
      const mod = event.ctrlKey || event.metaKey;
      if (key === 'escape' && state.previewing) { stopPreview(); return; }
      if (key === 'escape') {
        if (typing) event.target.blur();
        else if (state.adjustId) stopAdjust();
        else if (state.targetId) setTarget(null);
        else select(null);
        return;
      }
      if (mod && key === 's') { event.preventDefault(); if (typing) event.target.blur(); act('save-open'); return; }
      if (typing) return;
      if (mod && key === 'z' && !event.shiftKey) { event.preventDefault(); act('undo'); }
      else if (mod && (key === 'y' || (key === 'z' && event.shiftKey))) { event.preventDefault(); act('redo'); }
      else if (mod && key === 'd') { event.preventDefault(); act('duplicate'); }
      else if ((key === 'delete' || key === 'backspace') && state.selectedId) { event.preventDefault(); act('delete'); }
      else if (key === ']') act(event.shiftKey ? 'front' : 'forward');
      else if (key === '[') act(event.shiftKey ? 'back' : 'backward');
      else if (key === 'enter' && onPage && selectedItem()?.kind === 'photo' && !state.adjustId) act('adjust');
      else if (key === 'enter' && onPage && selectedItem()?.kind === 'text') { event.preventDefault(); focusTextField(); }
      else if (key.startsWith('arrow') && state.selectedId && selectedItem()?.hidden) { event.preventDefault(); announce('It is hidden. Show it from Layers first.'); }
      else if (key.startsWith('arrow') && state.selectedId && selectedItem()?.locked) { event.preventDefault(); sayLocked(); }
      else if (key.startsWith('arrow') && state.selectedId) {
        event.preventDefault();
        const step = event.shiftKey ? 10 : 1;
        const item = selectedItem();
        if (state.adjustId === item.id) {
          // Arrows move the photo inside its frame while it is being adjusted.
          const photo = photos.get(item.photo);
          const dx = key === 'arrowleft' ? -step : key === 'arrowright' ? step : 0;
          const dy = key === 'arrowup' ? -step : key === 'arrowdown' ? step : 0;
          if (photo) item.crop = core.panCrop(photo.aspect, item, dx, dy);
          syncPage();
        } else {
          changeSelected((target) => {
            if (key === 'arrowleft') target.x -= step;
            if (key === 'arrowright') target.x += step;
            if (key === 'arrowup') target.y -= step;
            if (key === 'arrowdown') target.y += step;
          }, false);
        }
        holdEdit(400);
      }
    });
    document.addEventListener('keyup', (event) => { if (event.key === 'Shift') setShift(false); });
    window.addEventListener('blur', () => setShift(false));

    stageBox.addEventListener('wheel', (event) => {
      const item = state.adjustId && itemById(state.adjustId);
      if (item) {
        // The wheel zooms the photo inside its frame while it is being adjusted.
        event.preventDefault();
        zoomCrop(item, (item.crop?.zoom || 1) * (event.deltaY < 0 ? 1.06 : 1 / 1.06));
        syncPage();
        const out = props.querySelector('[data-out="zoom"]');
        const input = props.querySelector('[data-prop="zoom"]');
        if (out) out.textContent = `${item.crop.zoom.toFixed(1)}×`;
        if (input) input.value = item.crop.zoom;
        // Recorded for Undo once the wheel stops, the same way as a keyboard nudge, so Undo
        // and Redo flush it first and never skip it.
        holdEdit(300);
        return;
      }
      if (!event.ctrlKey) return;
      event.preventDefault();
      setZoom(state.zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1));
    }, { passive: false });
  }

  return Object.freeze({ createKeys });
});
