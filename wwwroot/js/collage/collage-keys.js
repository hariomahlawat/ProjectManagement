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
    const { state, change, core, views, stageBox, props, photos, itemById, selectedItem, announce, setShift, stopPreview, setZoom, syncPage, select, stopAdjust, zoomCrop, holdEdit, changeSelected, act, focusTextField, setTarget, sayLocked, editItem } = deps;
    // ---------------------------------------------------------------------------
    // Keyboard and the mouse wheel
    // ---------------------------------------------------------------------------

    /**
     * Whether Delete or Backspace here removes the selected item: on the page itself, or on that
     * item's own row in Layers (where a keyboard user picks it). On any other button or control
     * (the toolbar, a panel, a tile) the key belongs to that control and removes nothing.
     */
    function deletesItem(target, onPage) {
      const row = target.closest?.('[data-layer-select]');
      if (row) return row.dataset.layerSelect === state.selectedId;
      return onPage && !target.closest?.('button, a[href], summary, [role="button"], [tabindex]:not([tabindex="-1"])');
    }

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
      else if ((key === 'delete' || key === 'backspace') && state.selectedId && deletesItem(event.target, onPage)) { event.preventDefault(); act('delete'); }
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
          if (photo) editItem(item.id, (it) => { it.crop = core.panCrop(photo.aspect, it, dx, dy); }, 'hold');
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
        editItem(item.id, (it) => zoomCrop(it, (it.crop?.zoom || 1) * (event.deltaY < 0 ? 1.06 : 1 / 1.06)), 'none');
        syncPage();
        const zoomed = itemById(item.id);
        const out = props.querySelector('[data-out="zoom"]');
        const input = props.querySelector('[data-prop="zoom"]');
        if (out) out.textContent = `${zoomed.crop.zoom.toFixed(1)}×`;
        if (input) input.value = zoomed.crop.zoom;
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
