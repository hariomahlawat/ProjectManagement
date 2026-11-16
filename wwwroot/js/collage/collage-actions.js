// PRISM Collage Studio — the actions: buttons, menu items and keys that change the collage
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the helpers it uses, `state` (the editor's values, read only) and the parts
// made later (collage-state.js).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageActions = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createActions(deps) {
    const { state, parts, root, core, $, views, fileInput, nameInput, sizeSelect, body, props, changes, photos, newItemId, itemById, selectedItem, isFrame, change, announce, clearAlert, startCollage, goHome, layer, layout, startPreview, stopPreview, setZoom, session, scheduleDraft, renderDrafts, undoDeleteDraft, openDraft, select, startAdjust, stopAdjust, refreshTitle, commit, updateUndo, restore, changeSelected, lockedNow, applyLayoutKey, sayLocked } = deps;
    // The one Undo offered on the screen after a delete: good only while nothing else has changed.
    let undoOffer = null;

    function offerUndo(text) {
      undoOffer?.dismiss();
      const version = changes.version;
      announce(`${text} Undo is on the screen; Ctrl+Z also brings it back.`);
      if (!window.PrismToast) return;
      undoOffer = window.PrismToast.show({
        tone: 'success',
        text,
        action: {
          label: 'Undo',
          run: () => {
            if (changes.version === version) act('undo');
            else announce('Something else has changed since. Use Undo in the toolbar to step back.');
          }
        }
      });
    }

    function dropUndoOffer() {
      undoOffer?.dismiss();
      undoOffer = null;
    }

    function act(action) {
      const item = selectedItem();
      if (core.lockBlocks(item, action)) { sayLocked(); return; }
      switch (action) {
        case 'home': goHome(); break;
        case 'choose': fileInput.click(); break;
        // Undo and Redo end moving a photo inside its frame only when there is a step to take.
        // A step taken back (or forward again) leaves the Undo offer on the screen out of date.
        case 'undo': { const back = changes.undo(); if (back) { dropUndoOffer(); stopAdjust(false); restore(back); } else updateUndo(); break; }
        case 'redo': { const again = changes.redo(); if (again) { dropUndoOffer(); stopAdjust(false); restore(again); } else updateUndo(); break; }
        case 'zoom-in': setZoom(state.zoom * 1.25); break;
        case 'zoom-out': setZoom(state.zoom / 1.25); break;
        case 'zoom-fit': setZoom(1); break;
        case 'alert-close': clearAlert(); break;
        case 'duplicate':
          if (item) {
            // A copy is one frame more than the layout has, so the page is no longer a strict
            // grid; otherwise the next change of space or page size would drop the copy.
            state.collage.layout = null;
            const copy = { ...core.duplicateItem(item, newItemId()), crop: item.crop ? { ...item.crop } : undefined };
            if (!copy.crop) delete copy.crop;
            state.collage.items.splice(state.collage.items.indexOf(item) + 1, 0, copy);
            change.choose(copy.id);
            commit();
            announce('Duplicated.');
          }
          break;
        case 'lock':
          if (item) {
            item.locked = !item.locked;
            if (item.locked) lockedNow(item);
            commit();
            announce(item.locked ? 'Locked.' : 'Unlocked.');
          }
          break;
        case 'hide':
          if (item) {
            item.hidden = !item.hidden;
            commit();
            announce(item.hidden ? 'Hidden. Show it again from Layers.' : 'Shown.');
          }
          break;
        case 'preview':
          // Preview is the page alone: no message and no Undo offer over it.
          clearAlert();
          undoOffer?.dismiss();
          startPreview();
          break;
        case 'edit-props': parts.setPropsOpen(true, 'settings'); break;
        case 'toggle-props': parts.setPropsOpen(!(body.classList.contains('is-props-open') && state.propsTab === 'layers'), 'layers'); break;
        case 'close-props': parts.setPropsOpen(false); break;
        case 'close-drawer': parts.setPanel(null); break;
        case 'preview-exit': stopPreview(); break;
        case 'delete':
          if (item) {
            // A photo in a layout leaves its frame empty, so the layout keeps its shape; anything
            // else (and an empty frame) goes.
            const leavesFrame = item.kind === 'photo' && Boolean(state.collage.layout);
            if (leavesFrame) state.collage.items[state.collage.items.indexOf(item)] = core.emptyFrame(item);
            else state.collage.items = state.collage.items.filter((i) => i !== item);
            change.choose(leavesFrame ? item.id : null);
            stopAdjust(false);
            commit();
            offerUndo(leavesFrame ? 'Photo taken out; its frame is empty.' : `${core.layerName(item)} deleted.`);
            // Focus goes on to what is left, in the Layers list, rather than to the page.
            if (!leavesFrame) window.requestAnimationFrame(() => (props.querySelector('[data-layer-select]') || props.querySelector('[data-props-tab="layers"]'))?.focus());
          }
          break;
        case 'forward': case 'backward': case 'front': case 'back':
          if (item) { state.collage.items = core.reorder(state.collage.items, item.id, action); commit(); }
          break;
        case 'rotate-left':
        case 'rotate-right':
        case 'straighten':
          if (action === 'rotate-left') changeSelected((i) => { i.angle = core.normaliseAngle(i.angle - 15); });
          else if (action === 'rotate-right') changeSelected((i) => { i.angle = core.normaliseAngle(i.angle + 15); });
          else changeSelected((i) => { i.angle = 0; });
          break;
        case 'adjust': if (item?.kind === 'photo') startAdjust(item.id); break;
        case 'adjust-done': stopAdjust(); break;
        case 'replace': if (isFrame(item)) { parts.setTarget(item.id); parts.setPanel('photos'); } break;
        case 'fill-slot': if (item?.kind === 'slot') parts.chooseFor(item.id); break;
        case 'replace-cancel': parts.setTarget(null); break;
        case 'save-open': parts.saveAs.open(); break;
        case 'credits': root.querySelector('[data-credits]').showModal(); break;
        case 'drafts-all': change.toggleDraftsExpanded(); renderDrafts(); break;
        case 'keep-draft':
          // Asked again: keeping it may still mean deleting the oldest draft to make room.
          session.keepAgain();
          break;
        case 'library-open': parts.libraryPicker.open(); break;
        case 'library-close': parts.libraryPicker.close(); break;
        case 'library-more': parts.libraryPicker.more(); break;
        case 'library-add': parts.libraryPicker.add(); break;
        case 'credits-close': root.querySelector('[data-credits]').close(); break;
        case 'save-close': parts.saveAs.close(); break;
        case 'save-go': parts.saveAs.save(); break;
        case 'reset-look':
          changeSelected((i) => { Object.assign(i, { look: 'none', bright: 100, contrast: 100, warmth: 0, flip: false }); });
          break;
        case 'fit-photo':
          changeSelected((i) => {
            const photo = photos.get(i.photo);
            // A grey stand-in has no shape of its own to fit.
            if (!photo || photo.missing) return;
            // The picture area takes the photo's own shape, inside whatever frame it has.
            i.aspect = core.outerAspectFor(i, photo.aspect);
            i.crop = { ...core.FULL_CROP };
          });
          break;
        case 'casual':
          if (!state.collage.items.some(isFrame)) break;
          change.setCollage(core.makeCasual(state.collage));
          stopAdjust(false);
          commit();
          offerUndo('Photos tilted and scattered. Frames and shapes you chose are kept.');
          break;
        case 'straighten-all':
          change.setCollage(core.straightenAll(state.collage));
          commit();
          announce('All photos are straight.');
          break;
        case 'shuffle':
          change.setCollage(core.shufflePhotos(state.collage));
          commit();
          announce('Photos shuffled.');
          break;
        default: break;
      }
    }

    root.addEventListener('click', (event) => {
      // While a draft opens (or room is being made for a new one) the start screen waits.
      if (views.start.inert && views.start.contains(event.target)) return;
      const openOne = event.target.closest('[data-open-draft]');
      if (openOne) { openDraft(openOne.dataset.openDraft); return; }
      const deleteOne = event.target.closest('[data-delete-draft]');
      if (deleteOne) { session.remove(deleteOne.dataset.deleteDraft); return; }
      const start = event.target.closest('[data-start-size]');
      if (start) { startCollage(start.dataset.startSize); return; }
      const startLayout = event.target.closest('[data-start-layout]');
      if (startLayout) { startCollage(core.DEFAULT_SIZE, startLayout.dataset.startLayout); return; }
      const layoutChoice = event.target.closest('[data-layout]');
      if (layoutChoice) { applyLayoutKey(layoutChoice.dataset.layout); return; }
      const align = event.target.closest('[data-align]');
      if (align && !align.disabled) {
        changeSelected((target) => Object.assign(target, core.alignItem(target, state.collage.size, align.dataset.align)));
        return;
      }
      const layerPick = event.target.closest('[data-layer-select]');
      if (layerPick) {
        select(layerPick.dataset.layerSelect, { fromLayers: true });
        const name = layerPick.querySelector('.clg-layer__name')?.textContent || '';
        announce(`Selected ${name}. Arrow keys move it; Delete removes it.`);
        props.querySelector(`[data-layer-select="${layerPick.dataset.layerSelect}"]`)?.focus();
        return;
      }
      const layerHide = event.target.closest('[data-layer-hide]');
      if (layerHide) {
        const target = itemById(layerHide.dataset.layerHide);
        if (target) { target.hidden = !target.hidden; commit(); }
        return;
      }
      const layerLock = event.target.closest('[data-layer-lock]');
      if (layerLock) {
        const target = itemById(layerLock.dataset.layerLock);
        if (target) { target.locked = !target.locked; if (target.locked) lockedNow(target); commit(); }
        return;
      }
      const button = event.target.closest('[data-action]');
      if (!button || button.disabled) return;
      if (button.tagName === 'A') event.preventDefault();
      if (button.dataset.action === 'home' && views.editor.hidden) return;
      act(button.dataset.action);
    });

    sizeSelect.addEventListener('change', () => {
      change.setCollage(core.changeSize(state.collage, sizeSelect.value));
      // A page laid out in a grid is laid out again on the new page, every frame in its own place.
      if (state.collage.layout) change.setCollage(core.reflowLayout(state.collage, state.collage.layout.gap));
      stopAdjust(false);
      layout();
      commit();
      announce(`The page is now ${core.SIZES[state.collage.size].label}.`);
    });

    nameInput.addEventListener('input', () => {
      scheduleDraft();
      refreshTitle();
    });


    return { offerUndo, act };
  }

  return Object.freeze({ createActions });
});
