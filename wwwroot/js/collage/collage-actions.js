// PRISM Collage Studio — the actions: buttons, menu items and keys that change the collage
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the libraries it uses, `state` (the editor's values, read only), its own named
// changes, and its helpers in a few groups (ui, doc, history, messages, surface, selection,
// screens), each read when used (collage-state.js; docs/ux/collage-review-4-fixes.md, PR 11).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageActions = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createActions(deps) {
    const { state, parts, core, change, ui, history, doc, messages, screens, surface, selection } = deps;
    // The one Undo offered on the screen after a delete: good only while nothing else has changed.
    let undoOffer = null;

    function offerUndo(text) {
      undoOffer?.dismiss();
      const version = history.changes.version;
      messages.announce(`${text} Undo is on the screen; Ctrl+Z also brings it back.`);
      if (!window.PrismToast) return;
      undoOffer = window.PrismToast.show({
        tone: 'success',
        text,
        action: {
          label: 'Undo',
          run: () => {
            if (history.changes.version === version) act('undo');
            else messages.announce('Something else has changed since. Use Undo in the toolbar to step back.');
          }
        }
      });
    }

    function dropUndoOffer() {
      undoOffer?.dismiss();
      undoOffer = null;
    }

    function act(action) {
      const item = doc.selectedItem();
      if (core.lockBlocks(item, action)) { messages.sayLocked(); return; }
      switch (action) {
        case 'home': screens.goHome(); break;
        case 'choose': ui.fileInput.click(); break;
        // Undo and Redo end moving a photo inside its frame only when there is a step to take.
        // A step taken back (or forward again) leaves the Undo offer on the screen out of date.
        case 'undo': { const back = history.changes.undo(); if (back) { dropUndoOffer(); selection.stopAdjust(false); history.restore(back); } else history.updateUndo(); break; }
        case 'redo': { const again = history.changes.redo(); if (again) { dropUndoOffer(); selection.stopAdjust(false); history.restore(again); } else history.updateUndo(); break; }
        case 'zoom-in': surface.setZoom(state.zoom * 1.25); break;
        case 'zoom-out': surface.setZoom(state.zoom / 1.25); break;
        case 'zoom-fit': surface.setZoom(1); break;
        case 'alert-close': messages.clearAlert(); break;
        case 'duplicate':
          if (item) {
            // A copy is one frame more than the layout has, so the page is no longer a strict
            // grid; otherwise the next change of space or page size would drop the copy.
            const copy = { ...core.duplicateItem(item, doc.newItemId()), crop: item.crop ? { ...item.crop } : undefined };
            if (!copy.crop) delete copy.crop;
            doc.edit((draft) => {
              draft.layout = null;
              draft.items.splice(draft.items.findIndex((i) => i.id === item.id) + 1, 0, copy);
            }, 'none');
            change.choose(copy.id);
            history.commit();
            messages.announce('Duplicated.');
          }
          break;
        case 'lock':
          if (item) {
            const locked = !item.locked;
            doc.editItem(item.id, (it) => { it.locked = locked; }, 'none');
            if (locked) selection.lockedNow(item);
            history.commit();
            messages.announce(locked ? 'Locked.' : 'Unlocked.');
          }
          break;
        case 'hide':
          if (item) {
            const hidden = !item.hidden;
            // Shown or hidden by the user, a part is no longer one the design hides by itself (Codex on #3483).
            doc.editItem(item.id, (it) => { it.hidden = hidden; delete it.autoHidden; }, 'commit');
            messages.announce(hidden ? 'Hidden. Show it again from Layers.' : 'Shown.');
          }
          break;
        case 'preview':
          // Preview is the page alone: no message and no Undo offer over it.
          messages.clearAlert();
          undoOffer?.dismiss();
          surface.startPreview();
          break;
        case 'edit-props': parts.setPropsOpen(true, 'settings'); break;
        case 'toggle-props': parts.setPropsOpen(!(ui.body.classList.contains('is-props-open') && state.propsTab === 'layers'), 'layers'); break;
        case 'close-props': parts.setPropsOpen(false); break;
        case 'close-drawer': parts.setPanel(null); break;
        case 'preview-exit': surface.stopPreview(); break;
        case 'delete':
          if (item) {
            // A photo in a layout leaves its frame empty, so the layout keeps its shape; anything
            // else (and an empty frame) goes.
            const leavesFrame = item.kind === 'photo' && Boolean(state.collage.layout);
            doc.edit((draft) => {
              const at = draft.items.findIndex((i) => i.id === item.id);
              if (leavesFrame) draft.items[at] = core.emptyFrame(draft.items[at]);
              else draft.items.splice(at, 1);
              // A poster's date deleted on the page takes its band with it, as emptying it in
              // Poster details does (Codex on #3483).
              if (item.field === 'when') window.PrismCollageDesigns?.followBand?.(draft.items);
            }, 'none');
            change.choose(leavesFrame ? item.id : null);
            selection.stopAdjust(false);
            history.commit();
            offerUndo(leavesFrame ? 'Photo taken out; its frame is empty.' : `${core.layerName(item)} deleted.`);
            // Focus goes on to what is left, in the Layers list, rather than to the page.
            if (!leavesFrame) window.requestAnimationFrame(() => (ui.props.querySelector('[data-layer-select]') || ui.props.querySelector('[data-props-tab="layers"]'))?.focus());
          }
          break;
        case 'forward': case 'backward': case 'front': case 'back':
          if (item) doc.edit((draft) => { draft.items = core.reorder(draft.items, item.id, action); }, 'commit');
          break;
        case 'rotate-left':
        case 'rotate-right':
        case 'straighten':
          if (action === 'rotate-left') selection.changeSelected((i) => { i.angle = core.normaliseAngle(i.angle - 15); });
          else if (action === 'rotate-right') selection.changeSelected((i) => { i.angle = core.normaliseAngle(i.angle + 15); });
          else selection.changeSelected((i) => { i.angle = 0; });
          break;
        case 'adjust': if (item?.kind === 'photo') selection.startAdjust(item.id); break;
        case 'adjust-done': selection.stopAdjust(); break;
        case 'replace': if (doc.isFrame(item)) { parts.setTarget(item.id); parts.setPanel('photos'); } break;
        case 'fill-slot': if (item?.kind === 'slot') parts.chooseFor(item.id); break;
        case 'replace-cancel': parts.setTarget(null); break;
        case 'save-open': parts.saveAs.open(); break;
        case 'credits': ui.root.querySelector('[data-credits]').showModal(); break;
        // Every tip, from the one line of help (docs/ux/collage-usability-plan.md, PR 6).
        case 'help': {
          // Tips lead with what is open: cards and posters first on one, photos first on a collage (round 2, PR 5).
          const dialog = ui.root.querySelector('[data-help-dialog]');
          const cards = dialog.querySelector('[data-help-group="cards"]');
          const photos = dialog.querySelector('[data-help-group="photos"]');
          if (cards && photos) {
            const [first, second] = state.collage.card ? [cards, photos] : [photos, cards];
            first.parentNode.insertBefore(first, second);
          }
          dialog.showModal();
          break;
        }
        case 'help-close': ui.root.querySelector('[data-help-dialog]').close(); break;
        case 'drafts-all': change.toggleDraftsExpanded(); screens.renderDrafts(); break;
        case 'keep-draft':
          // Asked again: keeping it may still mean deleting the oldest draft to make room.
          history.session.keepAgain();
          break;
        case 'library-open': parts.libraryPicker.open(); break;
        case 'library-close': parts.libraryPicker.close(); break;
        case 'library-more': parts.libraryPicker.more(); break;
        case 'library-add': parts.libraryPicker.add(); break;
        case 'credits-close': ui.root.querySelector('[data-credits]').close(); break;
        case 'save-close': parts.saveAs.close(); break;
        case 'save-go': parts.saveAs.save(); break;
        case 'reset-look':
          selection.changeSelected((i) => { Object.assign(i, { look: 'none', bright: 100, contrast: 100, warmth: 0, flip: false }); });
          break;
        case 'fit-photo':
          selection.changeSelected((i) => {
            const photo = doc.photos.get(i.photo);
            // A grey stand-in has no shape of its own to fit.
            if (!photo || photo.missing) return;
            // The picture area takes the photo's own shape, inside whatever frame it has.
            i.aspect = core.outerAspectFor(i, photo.aspect);
            i.crop = { ...core.FULL_CROP };
          });
          break;
        case 'casual':
          if (!state.collage.items.some(doc.isFrame)) break;
          change.setCollage(core.makeCasual(state.collage));
          selection.stopAdjust(false);
          history.commit();
          offerUndo('Photos tilted and scattered. Frames and shapes you chose are kept.');
          break;
        case 'straighten-all':
          change.setCollage(core.straightenAll(state.collage));
          history.commit();
          messages.announce('All photos are straight.');
          break;
        case 'shuffle':
          change.setCollage(core.shufflePhotos(state.collage));
          history.commit();
          messages.announce('Photos shuffled.');
          break;
        default: break;
      }
    }

    ui.root.addEventListener('click', (event) => {
      // While a draft opens (or room is being made for a new one) the start screen waits.
      if (ui.views.start.inert && ui.views.start.contains(event.target)) return;
      const openOne = event.target.closest('[data-open-draft]');
      if (openOne) { screens.openDraft(openOne.dataset.openDraft); return; }
      const deleteOne = event.target.closest('[data-delete-draft]');
      if (deleteOne) { history.session.remove(deleteOne.dataset.deleteDraft); return; }
      const start = event.target.closest('[data-start-size]');
      if (start) { screens.startCollage(start.dataset.startSize); return; }
      const startLayout = event.target.closest('[data-start-layout]');
      if (startLayout) { screens.startCollage(screens.startSize(), startLayout.dataset.startLayout); return; }
      const layoutChoice = event.target.closest('[data-layout]');
      if (layoutChoice) { screens.applyLayoutKey(layoutChoice.dataset.layout); return; }
      const align = event.target.closest('[data-align]');
      if (align && !align.disabled) {
        selection.changeSelected((target) => Object.assign(target, core.alignItem(target, state.collage.size, align.dataset.align)));
        return;
      }
      const layerPick = event.target.closest('[data-layer-select]');
      if (layerPick) {
        selection.select(layerPick.dataset.layerSelect, { fromLayers: true });
        const name = layerPick.querySelector('.clg-layer__name')?.textContent || '';
        messages.announce(`Selected ${name}. Arrow keys move it; Delete removes it.`);
        ui.props.querySelector(`[data-layer-select="${layerPick.dataset.layerSelect}"]`)?.focus();
        return;
      }
      const layerHide = event.target.closest('[data-layer-hide]');
      if (layerHide) {
        const target = doc.itemById(layerHide.dataset.layerHide);
        // Shown or hidden by the user, a part is no longer one the design hides by itself (Codex on #3483).
        if (target) doc.editItem(target.id, (it) => { it.hidden = !it.hidden; delete it.autoHidden; }, 'commit');
        return;
      }
      const layerLock = event.target.closest('[data-layer-lock]');
      if (layerLock) {
        const target = doc.itemById(layerLock.dataset.layerLock);
        if (target) {
          const locked = !target.locked;
          doc.editItem(target.id, (it) => { it.locked = locked; }, 'none');
          if (locked) selection.lockedNow(target);
          history.commit();
        }
        return;
      }
      const button = event.target.closest('[data-action]');
      if (!button || button.disabled) return;
      if (button.tagName === 'A') event.preventDefault();
      if (button.dataset.action === 'home' && ui.views.editor.hidden) return;
      act(button.dataset.action);
    });

    ui.sizeSelect.addEventListener('change', () => {
      // "Custom size…" opens Page settings, where the size is typed in mm (PR 5b of
      // docs/ux/invitations-posters.md); the menu keeps showing the page's size meanwhile.
      if (ui.sizeSelect.value === 'custom') {
        ui.showSize(state.collage.size);
        selection.select(null);
        parts.setPropsOpen?.(true, 'settings');
        window.requestAnimationFrame(() => {
          const fold = ui.props.querySelector('[data-custom-fold]');
          if (fold) fold.open = true;
          ui.props.querySelector('[data-custom-size="width"]')?.focus();
        });
        return;
      }
      change.setCollage(core.changeSize(state.collage, ui.sizeSelect.value));
      // A card is laid out again by its design for the new shape, both sides, keeping its words,
      // logo, photos and locked parts (docs/ux/invitations-posters.md, cold review fixes).
      // A design the new size does not offer (a full invitation on a car pass) gives way to the
      // nearest one it does, and the user is told (cold review 5).
      const designs = window.PrismCollageDesigns;
      let designNote = '';
      if (state.collage.card && designs?.DESIGNS[state.collage.card.design]) {
        const was = state.collage.card.design;
        const next = designs.designOnSize(was, state.collage.size);
        if (next !== was) designNote = ` The ${designs.DESIGNS[was].label} design does not suit this size, so the card now uses ${designs.DESIGNS[next].label}.`;
        change.setCollage(designs.applyDesign(state.collage, next, designs.detailsOf(state.collage), { measure: surface.measureText, newId: doc.newItemId }));
      }
      // A page laid out in a grid is laid out again on the new page, every frame in its own place.
      if (state.collage.layout) change.setCollage(core.reflowLayout(state.collage, state.collage.layout.gap));
      // The side not showing has its own layout, laid out again on the new page too (PR 8).
      if (state.collage.back?.layout) {
        const other = core.flipSide(state.collage);
        change.setCollage(core.flipSide(core.reflowLayout(other, other.layout.gap)));
      }
      selection.stopAdjust(false);
      surface.layout();
      history.commit();
      // The design change is shown with Undo, which takes the card back to its old size and design.
      if (designNote) offerUndo(`The page is now ${core.sizeOf(state.collage.size).label}.${designNote}`);
      else messages.announce(`The page is now ${core.sizeOf(state.collage.size).label}.`);
    });

    ui.nameInput.addEventListener('input', () => {
      history.scheduleDraft();
      screens.refreshTitle();
    });


    return { offerUndo, act };
  }

  return Object.freeze({ createActions });
});
