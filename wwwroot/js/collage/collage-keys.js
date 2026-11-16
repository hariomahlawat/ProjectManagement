// PRISM Collage Studio — the keyboard (shortcuts, nudges, Enter and Esc) and the mouse wheel
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the libraries it uses, `state` (the editor's values, read only), its own named
// changes, and its helpers in a few groups (ui, doc, history, messages, surface, selection,
// screens), each read when used (collage-state.js; docs/ux/collage-review-4-fixes.md, PR 11).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageKeys = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createKeys(deps) {
    const { state, change, core, ui, doc, messages, surface, selection, history } = deps;
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
      if (event.key === 'Shift') surface.setShift(true);
      if (ui.views.editor.hidden || state.leaving || document.querySelector('dialog[open]')) return;
      const typing = event.target.closest?.('input, textarea, select, [contenteditable]');
      // Enter on a button, link or tile belongs to it; only Enter on the page itself acts on the item.
      const onPage = event.target === document.body || ui.stageBox.contains(event.target);
      const key = event.key.toLowerCase();
      const mod = event.ctrlKey || event.metaKey;
      if (key === 'escape' && state.previewing) { surface.stopPreview(); return; }
      if (key === 'escape') {
        if (typing) event.target.blur();
        else if (state.adjustId) selection.stopAdjust();
        else if (state.targetId) selection.setTarget(null);
        else selection.select(null);
        return;
      }
      if (mod && key === 's') { event.preventDefault(); if (typing) event.target.blur(); selection.act('save-open'); return; }
      if (typing) return;
      if (mod && key === 'z' && !event.shiftKey) { event.preventDefault(); selection.act('undo'); }
      else if (mod && (key === 'y' || (key === 'z' && event.shiftKey))) { event.preventDefault(); selection.act('redo'); }
      else if (mod && key === 'd') { event.preventDefault(); selection.act('duplicate'); }
      else if ((key === 'delete' || key === 'backspace') && state.selectedId && deletesItem(event.target, onPage)) { event.preventDefault(); selection.act('delete'); }
      else if (key === ']') selection.act(event.shiftKey ? 'front' : 'forward');
      else if (key === '[') selection.act(event.shiftKey ? 'back' : 'backward');
      else if (key === 'enter' && onPage && doc.selectedItem()?.kind === 'photo' && !state.adjustId) selection.act('adjust');
      else if (key === 'enter' && onPage && doc.selectedItem()?.kind === 'text') { event.preventDefault(); selection.focusTextField(); }
      else if (key.startsWith('arrow') && state.selectedId && doc.selectedItem()?.hidden) { event.preventDefault(); messages.announce('It is hidden. Show it from Layers first.'); }
      else if (key.startsWith('arrow') && state.selectedId && doc.selectedItem()?.locked) { event.preventDefault(); messages.sayLocked(); }
      else if (key.startsWith('arrow') && state.selectedId) {
        event.preventDefault();
        const step = event.shiftKey ? 10 : 1;
        const item = doc.selectedItem();
        if (state.adjustId === item.id) {
          // Arrows move the photo inside its frame while it is being adjusted.
          const photo = doc.photos.get(item.photo);
          const dx = key === 'arrowleft' ? -step : key === 'arrowright' ? step : 0;
          const dy = key === 'arrowup' ? -step : key === 'arrowdown' ? step : 0;
          if (photo) doc.editItem(item.id, (it) => { it.crop = core.panCrop(photo.aspect, it, dx, dy); }, 'hold');
        } else {
          selection.changeSelected((target) => {
            if (key === 'arrowleft') target.x -= step;
            if (key === 'arrowright') target.x += step;
            if (key === 'arrowup') target.y -= step;
            if (key === 'arrowdown') target.y += step;
          }, false);
        }
        history.holdEdit(400);
      }
    });
    document.addEventListener('keyup', (event) => { if (event.key === 'Shift') surface.setShift(false); });
    window.addEventListener('blur', () => surface.setShift(false));

    ui.stageBox.addEventListener('wheel', (event) => {
      const item = state.adjustId && doc.itemById(state.adjustId);
      if (item) {
        // The wheel zooms the photo inside its frame while it is being adjusted.
        event.preventDefault();
        doc.editItem(item.id, (it) => selection.zoomCrop(it, (it.crop?.zoom || 1) * (event.deltaY < 0 ? 1.06 : 1 / 1.06)), 'none');
        surface.syncPage();
        const zoomed = doc.itemById(item.id);
        const out = ui.props.querySelector('[data-out="zoom"]');
        const input = ui.props.querySelector('[data-prop="zoom"]');
        if (out) out.textContent = `${zoomed.crop.zoom.toFixed(1)}×`;
        if (input) input.value = zoomed.crop.zoom;
        // Recorded for Undo once the wheel stops, the same way as a keyboard nudge, so Undo
        // and Redo flush it first and never skip it.
        history.holdEdit(300);
        return;
      }
      if (!event.ctrlKey) return;
      event.preventDefault();
      surface.setZoom(state.zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1));
    }, { passive: false });
  }

  return Object.freeze({ createKeys });
});
