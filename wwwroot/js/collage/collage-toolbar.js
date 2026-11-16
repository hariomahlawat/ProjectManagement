// PRISM Collage Studio — the selected item's toolbar and the angle shown while it turns
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the helpers it uses, `state` (the editor's values, read only) and the parts
// made later (collage-state.js).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageToolbar = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createToolbar(deps) {
    const { state, core, $, stageBox, toolbar, angleTag, selectedItem, nodes, transformer, visibleSheetTop } = deps;
    function hideToolbar() {
      toolbar.hidden = true;
    }

    function placeToolbar() {
      const node = state.selectedId && nodes.get(state.selectedId);
      const item = selectedItem();
      if (!node || !item || item.hidden || state.transforming || node.isDragging()) { hideToolbar(); return; }
      const adjusting = state.adjustId === item.id;
      for (const part of toolbar.querySelectorAll('[data-for]')) {
        const forWhat = part.dataset.for;
        part.hidden = adjusting
          ? forWhat !== 'adjusting'
          : !(forWhat === item.kind || forWhat === 'item');
      }
      for (const button of toolbar.querySelectorAll('[data-action]')) {
        button.disabled = core.lockBlocks(item, button.dataset.action);
      }
      const lockButton = toolbar.querySelector('[data-action="lock"]');
      if (lockButton) {
        lockButton.setAttribute('aria-pressed', String(Boolean(item.locked)));
        lockButton.setAttribute('aria-label', item.locked ? 'Unlock' : 'Lock');
        lockButton.title = item.locked ? 'Unlock' : 'Lock';
        lockButton.querySelector('i').className = `bi ${item.locked ? 'bi-lock-fill' : 'bi-unlock'}`;
      }
      const rect = node.getClientRect();
      toolbar.hidden = false;
      const width = toolbar.offsetWidth;
      const above = rect.y - toolbar.offsetHeight - (adjusting ? 12 : transformer.rotateAnchorOffset() + 18);
      const below = rect.y + rect.height + 14;
      const left = Math.min(
        Math.max(rect.x + rect.width / 2, stageBox.scrollLeft + width / 2 + 8),
        stageBox.scrollLeft + stageBox.clientWidth - width / 2 - 8);
      // Above the item, unless that is off the top or would cover another item and below would not.
      const covers = (top) => state.collage.items.some((other) => {
        if (other.id === item.id || other.hidden) return false;
        const box = nodes.get(other.id)?.getClientRect();
        return box && box.x < left + width / 2 && box.x + box.width > left - width / 2
          && box.y < top + toolbar.offsetHeight && box.y + box.height > top;
      });
      // Only where it can be seen: not off the top, and not behind a sheet open over the page.
      const sheetTop = visibleSheetTop();
      const visibleBottom = stageBox.scrollTop + (sheetTop === null ? stageBox.clientHeight : sheetTop - stageBox.getBoundingClientRect().top);
      const height = toolbar.offsetHeight;
      const fits = (t) => t >= stageBox.scrollTop + 6 && t + height <= visibleBottom - 6;
      const top = [above, below].find((t) => fits(t) && !covers(t))
        ?? [above, below].find(fits)
        ?? Math.min(Math.max(above, stageBox.scrollTop + 6), visibleBottom - height - 6);
      toolbar.style.left = `${left}px`;
      toolbar.style.top = `${top}px`;
    }

    function showAngle(node) {
      const rect = node.getClientRect();
      angleTag.hidden = false;
      angleTag.textContent = `${Math.round(core.normaliseAngle(node.rotation()))}°`;
      angleTag.style.left = `${rect.x + rect.width / 2}px`;
      angleTag.style.top = `${Math.max(stageBox.scrollTop + 28, rect.y - 12)}px`;
    }

    stageBox.addEventListener('scroll', placeToolbar, { passive: true });


    return { hideToolbar, placeToolbar, showAngle };
  }

  return Object.freeze({ createToolbar });
});
