import { closestAction, getPassiveNotebookCardOpenTarget } from './notebook-utils.js';
import { NotebookApi } from './notebook-api.js';
import { createNotebookBoard } from './notebook-board.js';
import { initNotebookComposer } from './notebook-composer.js';
import { initNotebookEditor } from './notebook-editor.js';
import { initNotebookCreateEditor } from './notebook-create-editor.js';
import { reconcileMutation, requireMutationItem, updateCardConcurrencyState } from './notebook-reconcile.js';
import { closeNotebookColourPickers, normaliseNotebookColour } from './notebook-colour-picker.js';
import { initNotebookLabelManager } from './notebook-label-manager.js';
import { hydrateNotebookLabelCatalog, initNotebookLabelPicker, refreshNotebookLabelCatalog } from './notebook-label-picker.js';
import { confirmNotebookAction, initNotebookConfirmDialog } from './notebook-confirm-dialog.js';
import { initNotebookToastRegion, showNotebookToast } from './notebook-toast.js';
import { initNotebookDragOrder } from './notebook-drag-order.js';
import { initNotebookMasonryGrid } from './notebook-masonry-grid.js';
import { initNotebookCollaborators } from './notebook-collaborators.js';


export function renderNotebookLabelNavigation(shell, labels = []) {
  if (!shell) return;
  const safeLabels = Array.isArray(labels) ? labels : [];
  const rail = shell.querySelector('[data-notebook-label-rail]');
  if (rail) {
    rail.innerHTML = '';
    const currentTag = new URL(location.href).searchParams.get('tag');
    safeLabels.forEach((label) => {
      const link = document.createElement('a');
      link.href = `/Notebook?view=labels&tag=${encodeURIComponent(label.name)}`;
      link.className = 'notebook-rail__item notebook-rail__item--label';
      if (currentTag && currentTag.toLocaleLowerCase() === String(label.name).toLocaleLowerCase()) {
        link.classList.add('is-active');
      }
      link.innerHTML = `<i class="bi bi-tag"></i><span>${escapeLabelHtml(label.name)}</span><b>${Number(label.count || 0)}</b>`;
      rail.appendChild(link);
    });
  }

  const directory = shell.querySelector('[data-notebook-label-directory-list]');
  if (directory) {
    directory.innerHTML = '';
    safeLabels.forEach((label) => {
      const link = document.createElement('a');
      link.href = `/Notebook?view=labels&tag=${encodeURIComponent(label.name)}`;
      link.innerHTML = `<i class="bi bi-tag"></i>${escapeLabelHtml(label.name)} <span>${Number(label.count || 0)}</span>`;
      directory.appendChild(link);
    });
  }
  const empty = shell.querySelector('[data-notebook-label-directory-empty]');
  if (empty) empty.hidden = safeLabels.length !== 0;
}

function parseCardLabels(card) {
  try { return JSON.parse(card?.dataset?.labels || '[]'); }
  catch { return []; }
}

function isNotebookSystemCard(card) {
  return Boolean(card?.dataset?.notebookSystemCard);
}

function applySystemCardColour(card, colorKey) {
  if (!card) return;
  const resolved = normaliseNotebookColour(colorKey);
  [...card.classList].filter((name) => name.startsWith('notebook-card-color-')).forEach((name) => card.classList.remove(name));
  card.classList.add(`notebook-card-color-${resolved}`);
  card.querySelectorAll('[data-colour-choice]').forEach((choice) => {
    const selected = normaliseNotebookColour(choice.dataset.colourChoice) === resolved;
    choice.classList.toggle('is-selected', selected);
    choice.setAttribute('aria-checked', String(selected));
  });
}

function renderSystemCardTags(card, labels = []) {
  const root = card?.querySelector?.('[data-system-card-tags]');
  if (!root) return;
  const values = Array.isArray(labels) ? labels.filter(Boolean) : [];
  root.innerHTML = '';
  values.slice(0, 3).forEach((label) => {
    const link = document.createElement('a');
    link.className = 'notebook-tag-chip';
    link.href = `/Notebook?view=labels&tag=${encodeURIComponent(label)}`;
    link.setAttribute('aria-label', `Open label ${label}`);
    link.textContent = label;
    root.appendChild(link);
  });
  if (values.length > 3) {
    const more = document.createElement('span');
    more.className = 'notebook-tag-chip';
    more.textContent = `+${values.length - 3}`;
    root.appendChild(more);
  }
  root.hidden = values.length === 0;
}

function renderSystemHomeControl(card, showInHome) {
  const host = card?.querySelector?.('[data-system-home-control]');
  if (!host || card.dataset.notebookSystemHomeCard) return;

  host.innerHTML = '';
  if (!showInHome) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'notebook-system-home-toggle';
    button.dataset.action = 'system-add-home';
    button.title = 'Add this live PRISM note to All Notes';
    button.innerHTML = '<i class="bi bi-journal-plus" aria-hidden="true"></i><span>Add to My Notebook</span>';
    host.appendChild(button);
    return;
  }

  const details = document.createElement('details');
  details.className = 'notebook-card-more';
  details.innerHTML = `
    <summary class="notebook-action-button" aria-label="More actions" aria-expanded="false"><i class="bi bi-three-dots"></i></summary>
    <div class="notebook-card-more__menu" role="menu">
      <button type="button" role="menuitem" data-action="system-remove-home">
        <i class="bi bi-journal-minus" aria-hidden="true"></i> Remove from My Notebook
      </button>
    </div>`;
  host.appendChild(details);
}

function applySystemPreference(card, preference) {
  if (!card || !preference) return;
  card.dataset.systemShowHome = String(Boolean(preference.showInHome));
  card.dataset.systemIsPinned = String(Boolean(preference.isPinned));
  card.dataset.systemHomePosition = String(Number(preference.homePosition || 0));
  card.dataset.systemPreferenceVersion = preference.version || '';
  card.dataset.labels = JSON.stringify(Array.isArray(preference.labels) ? preference.labels : []);
  applySystemCardColour(card, preference.colorKey || 'white');
  renderSystemCardTags(card, preference.labels || []);

  const showInHome = Boolean(preference.showInHome);
  const homeState = card.querySelector('[data-system-home-state]');
  if (homeState) homeState.hidden = !showInHome;
  renderSystemHomeControl(card, showInHome);

  card.classList.toggle('is-system-pinned', Boolean(preference.isPinned));
  const pinState = card.querySelector('[data-system-pin-state]');
  if (pinState) pinState.hidden = !Boolean(preference.isPinned);

  const pin = card.querySelector('[data-action="system-pin-note"]');
  if (pin) {
    const pinned = Boolean(preference.isPinned);
    pin.classList.toggle('is-active', pinned);
    pin.title = pinned ? 'Unpin' : 'Pin';
    pin.setAttribute('aria-label', pinned ? 'Unpin system note' : 'Pin system note');
    const icon = pin.querySelector('i');
    if (icon) icon.className = `bi ${pinned ? 'bi-pin-angle-fill' : 'bi-pin-angle'}`;
  }
}

function escapeLabelHtml(value) {
  return String(value || '').replace(/[&<>'"]/g, (character) => ({ '&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;' }[character]));
}

async function updateSystemPreferenceWithSingleRetry(key, payload) {
  try {
    return await NotebookApi.updateSystemItemPreference(key, payload);
  } catch (error) {
    // Presentation preferences are independent of Conference content. A single retry
    // safely resolves a rare simultaneous colour/label/pin update from another tab.
    if (error?.status === 409) return NotebookApi.updateSystemItemPreference(key, payload);
    throw error;
  }
}

// SECTION: Notebook app bootstrap and delegated interactions
export function initNotebookApp() {
  const shell = document.querySelector('.notebook-shell'); if (!shell) return;
  initNotebookConfirmDialog();
  initNotebookToastRegion();
  const view = new URL(location.href).searchParams.get('view') || 'home';
  const board = createNotebookBoard(shell);
  let composer;
  const globalError = document.querySelector('[data-notebook-global-error]');
  const globalErrorText = document.querySelector('[data-notebook-global-error-text]');
  const showGlobalError = (message) => { if (!globalError || !globalErrorText) { shell.dataset.error = message || 'Notebook action failed.'; return; } globalErrorText.textContent = message || 'Notebook action failed.'; globalError.hidden = false; };
  const systemSharedCount = Math.max(0, Number.parseInt(shell.dataset.systemSharedCount || '0', 10) || 0);
  let systemHomeCount = Math.max(0, Number.parseInt(shell.dataset.systemHomeCount || '0', 10) || 0);
  const applyCounts = (counts) => {
    if (!counts) return;
    Object.entries(counts).forEach(([key, value]) => {
      const numericValue = Number(value) || 0;
      const normalizedKey = key.toLowerCase();
      const displayValue = normalizedKey === 'shared'
        ? numericValue + systemSharedCount
        : normalizedKey === 'home'
          ? numericValue + systemHomeCount
          : numericValue;
      shell.querySelectorAll(`[data-notebook-count="${key}"]`).forEach((el) => {
        el.textContent = String(displayValue);
        if (key.toLowerCase() === 'overdue') {
          const isCurrentView = String(shell.dataset.view || '').toLowerCase() === 'overdue';
          el.closest('.notebook-rail__item')?.toggleAttribute('hidden', displayValue <= 0 && !isCurrentView);
        }
      });
    });
  };
  const refreshCounts = async () => applyCounts(await NotebookApi.getCounts());
  const labels = hydrateNotebookLabelCatalog(document);
  const editor = initNotebookEditor(board, view, { shell, showGlobalError, applyCounts });
  const createEditor = initNotebookCreateEditor(board, view, { shell, showGlobalError, applyCounts, showToast: showNotebookToast });
  const labelManager = initNotebookLabelManager(document.querySelector('[data-notebook-label-manager]'), {
    showGlobalError,
    onCatalogChange: (labels) => renderNotebookLabelNavigation(shell, labels)
  });
  document.querySelectorAll('[data-open-label-manager]').forEach((button) => button.addEventListener('click', () => labelManager?.open()));
  if (shell.dataset.openLabelManagerOnLoad === 'true') {
    queueMicrotask(() => labelManager?.open());
  }

  let activeLabelCard = null;
  const cardLabelPicker = initNotebookLabelPicker(
    document.querySelector('[data-notebook-card-label-host] [data-notebook-label-picker]'),
    {
      value: [],
      onError: (error) => showGlobalError(error?.message || 'Unable to create the label.'),
      onChange: async (labels) => {
        const card = activeLabelCard;
        if (!card) return;

        if (isNotebookSystemCard(card)) {
          const key = card.dataset.systemItemKey || card.dataset.notebookSystemCard;
          const response = await updateSystemPreferenceWithSingleRetry(key, { labels });
          applySystemPreference(card, response?.preference);
          const catalogue = await refreshNotebookLabelCatalog();
          renderNotebookLabelNavigation(shell, catalogue);
          return;
        }

        if (!card.dataset.noteId) return;
        const apply = async (version) => {
          const response = await NotebookApi.setLabels(card.dataset.noteId, labels, version);
          const updated = requireMutationItem(response);
          updateCardConcurrencyState(card, updated);
          await reconcileMutation({
            response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts,
            preservePosition: true, showGlobalError, existingCard: card
          });
          editor.syncExternalUpdate?.(updated);
          activeLabelCard = shell.querySelector(`[data-note-id="${updated.id}"]`) ?? card;
          const catalogue = await refreshNotebookLabelCatalog();
          renderNotebookLabelNavigation(shell, catalogue);
        };

        try { await apply(card.dataset.version); }
        catch (error) {
          if (error?.status === 409 && await confirmNotebookAction({ title: 'Apply labels to the latest version?', message: 'This note changed elsewhere. Your selected labels can be applied to the latest saved version.', confirmText: 'Apply labels', tone: 'warning' })) {
            const latest = error.currentItem ?? await NotebookApi.getItem(card.dataset.noteId);
            await apply(latest.version);
            return;
          }
          throw error;
        }
      }
    }
  );

  document.addEventListener('notebook:labels-changed', async (event) => {
    const nextLabels = Array.isArray(event.detail?.labels) ? event.detail.labels : [];
    renderNotebookLabelNavigation(shell, nextLabels);

    // A label rename/delete can affect the virtual PRISM note even though it is not a
    // NotebookItem. Refresh its personal association by stable system-item key.
    const cards = [...shell.querySelectorAll('[data-notebook-system-card]')];
    if (cards.length === 0) return;
    const key = cards[0].dataset.systemItemKey || cards[0].dataset.notebookSystemCard;
    if (!key) return;
    try {
      const response = await NotebookApi.getSystemItemPreference(key);
      cards.forEach((card) => applySystemPreference(card, response?.preference));
    } catch {
      // Label catalogue refresh remains useful even if the system-note preference
      // cannot be refreshed (for example after a role change in another session).
    }
  });
  renderNotebookLabelNavigation(shell, labels);
  composer = initNotebookComposer(shell.querySelector('[data-notebook-composer]'), board, view, { showGlobalError, applyCounts });
  document.querySelector('[data-notebook-global-error-close]')?.addEventListener('click', () => { globalError.hidden = true; globalErrorText.textContent = ''; });
  const storageKey = 'prism.notebook.view';
  const legacyStorageKeys = ['notebook.boardView', 'notebook-board-view'];
  const viewButtons = [...shell.querySelectorAll('[data-notebook-view]')];
  const storedView = localStorage.getItem(storageKey)
    || legacyStorageKeys.map((key) => localStorage.getItem(key)).find(Boolean)
    || shell.dataset.boardView
    || 'grid';
  legacyStorageKeys.forEach((key) => localStorage.removeItem(key));
  function applyBoardView(next) { const selected = next === 'list' ? 'list' : 'grid'; shell.dataset.boardView = selected; localStorage.setItem(storageKey, selected); viewButtons.forEach((button) => { const active = button.dataset.notebookView === selected; button.classList.toggle('is-active', active); button.setAttribute('aria-pressed', String(active)); }); document.dispatchEvent(new CustomEvent('notebook:board-view-changed', { detail: { view: selected } })); }
  viewButtons.forEach((button) => button.addEventListener('click', () => applyBoardView(button.dataset.notebookView)));
  applyBoardView(storedView);
  const masonryGrid = initNotebookMasonryGrid(shell);
  const dragOrder = initNotebookDragOrder(shell, board, { api: NotebookApi, showError: showGlobalError, showToast: showNotebookToast });
  const collaborators = initNotebookCollaborators(document, { board, view, applyCounts, showError: showGlobalError, onItemUpdated: (updated) => editor.syncExternalUpdate?.(updated) });

  // SECTION: Accessible, single-open card action menus / popovers
  const resetSystemColourPopoverPosition = (picker) => {
    if (!picker?.closest?.('[data-notebook-system-card]')) return;
    const popover = picker.querySelector('[data-colour-picker-popover]');
    if (!popover) return;
    ['left', 'right', 'top', 'bottom'].forEach((property) => popover.style.removeProperty(property));
    delete popover.dataset.floatingPlacement;
  };

  const positionSystemColourPopover = (picker) => {
    const card = picker?.closest?.('[data-notebook-system-card]');
    const popover = picker?.querySelector?.('[data-colour-picker-popover]');
    const toggle = picker?.querySelector?.('[data-colour-picker-toggle]');
    if (!card || !popover || !toggle || popover.hidden) return;

    // The mobile picker is intentionally viewport-fixed by CSS. Do not override it.
    if (window.matchMedia?.('(max-width: 700px)').matches) {
      resetSystemColourPopoverPosition(picker);
      return;
    }

    resetSystemColourPopoverPosition(picker);
    const pickerRect = picker.getBoundingClientRect();
    const popoverRect = popover.getBoundingClientRect();
    const mainRect = shell.querySelector('.notebook-main')?.getBoundingClientRect();
    const viewportGutter = 12;
    const contentGutter = 8;
    const gap = 8;
    const leftBound = Math.max(viewportGutter, (mainRect?.left ?? 0) + contentGutter);
    const rightBound = Math.min(window.innerWidth - viewportGutter, (mainRect?.right ?? window.innerWidth) - contentGutter);
    const topBound = viewportGutter;
    const bottomBound = window.innerHeight - viewportGutter;
    const maxLeft = Math.max(leftBound, rightBound - popoverRect.width);
    const clamp = (value, minimum, maximum) => Math.min(Math.max(value, minimum), maximum);

    // Prefer right-alignment with the colour button, then clamp into the actual Notebook
    // content viewport so the Shared surface never projects over the left navigation rail.
    const preferredLeft = pickerRect.right - popoverRect.width;
    const globalLeft = clamp(preferredLeft, leftBound, maxLeft);

    const aboveTop = pickerRect.top - popoverRect.height - gap;
    const belowTop = pickerRect.bottom + gap;
    const canOpenAbove = aboveTop >= topBound;
    const canOpenBelow = belowTop + popoverRect.height <= bottomBound;
    let globalTop;
    let placement;
    if (canOpenAbove || !canOpenBelow) {
      globalTop = clamp(aboveTop, topBound, Math.max(topBound, bottomBound - popoverRect.height));
      placement = 'above';
    } else {
      globalTop = clamp(belowTop, topBound, Math.max(topBound, bottomBound - popoverRect.height));
      placement = 'below';
    }

    popover.style.left = `${Math.round(globalLeft - pickerRect.left)}px`;
    popover.style.right = 'auto';
    popover.style.top = `${Math.round(globalTop - pickerRect.top)}px`;
    popover.style.bottom = 'auto';
    popover.dataset.floatingPlacement = placement;
  };

  let systemColourRepositionFrame = 0;
  const scheduleOpenSystemColourReposition = () => {
    if (systemColourRepositionFrame) cancelAnimationFrame(systemColourRepositionFrame);
    systemColourRepositionFrame = requestAnimationFrame(() => {
      systemColourRepositionFrame = 0;
      shell.querySelectorAll('[data-notebook-system-card] [data-notebook-colour-picker]').forEach((picker) => {
        const popover = picker.querySelector('[data-colour-picker-popover]');
        if (popover && !popover.hidden) positionSystemColourPopover(picker);
      });
    });
  };

  window.addEventListener('resize', scheduleOpenSystemColourReposition, { passive: true });
  document.addEventListener('scroll', scheduleOpenSystemColourReposition, { passive: true, capture: true });

  const syncCardFloatingState = () => {
    shell.querySelectorAll('.notebook-card').forEach((card) => {
      const hasOpenColour = [...card.querySelectorAll('[data-colour-picker-popover]')].some((popover) => !popover.hidden);
      card.classList.toggle('has-open-popover', hasOpenColour);
    });
  };

  const closeCardColourPickers = (except = null) => {
    closeNotebookColourPickers(document, except);
    shell.querySelectorAll('[data-notebook-system-card] [data-notebook-colour-picker]').forEach((picker) => {
      if (picker !== except) resetSystemColourPopoverPosition(picker);
    });
    syncCardFloatingState();
  };

  const closeNotebookMenus = (except = null, { restoreFocus = false } = {}) => {
    shell.querySelectorAll('.notebook-card-more[open]').forEach((menu) => {
      if (menu === except) return;
      menu.removeAttribute('open');
      menu.querySelector('summary')?.setAttribute('aria-expanded', 'false');
      menu.closest('.notebook-card')?.classList.remove('has-open-menu');
      if (restoreFocus) menu.querySelector('summary')?.focus?.();
    });
  };

  document.addEventListener('toggle', (event) => {
    const menu = event.target?.matches?.('.notebook-card-more') ? event.target : null;
    if (!menu || !shell.contains(menu)) return;
    const summary = menu.querySelector('summary');
    summary?.setAttribute('aria-expanded', String(menu.open));
    menu.closest('.notebook-card')?.classList.toggle('has-open-menu', menu.open);
    if (menu.open) {
      closeNotebookMenus(menu);
      closeCardColourPickers();
    }
  }, true);

  document.addEventListener('pointerdown', (event) => {
    if (!event.target.closest('.notebook-card-more')) closeNotebookMenus();
  }, true);

  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    const openMenu = shell.querySelector('.notebook-card-more[open]');
    if (!openMenu) return;
    event.preventDefault();
    closeNotebookMenus(null, { restoreFocus: true });
  });

  // Dragging starts from passive card content. Close any floating action surface before
  // cloning the card so palettes/menus never become part of the drag preview.
  shell.addEventListener('notebook:drag-start', () => {
    closeNotebookMenus();
    closeCardColourPickers();
  });

  document.addEventListener('click', async (event) => {
    const cardColourToggle = event.target.closest('.notebook-card [data-colour-picker-toggle]');
    if (cardColourToggle) {
      event.preventDefault();
      event.stopPropagation();
      const picker = cardColourToggle.closest('[data-notebook-colour-picker]');
      const popover = picker?.querySelector('[data-colour-picker-popover]');
      if (!picker || !popover) return;
      const shouldOpen = popover.hidden;
      closeNotebookMenus();
      closeCardColourPickers(shouldOpen ? picker : null);
      popover.hidden = !shouldOpen;
      cardColourToggle.setAttribute('aria-expanded', String(shouldOpen));
      if (shouldOpen) positionSystemColourPopover(picker);
      else resetSystemColourPopoverPosition(picker);
      syncCardFloatingState();
      if (shouldOpen) popover.querySelector('.is-selected,[data-colour-choice]')?.focus?.();
      return;
    }

    const cardColourChoice = event.target.closest('.notebook-card [data-colour-choice]');
    if (cardColourChoice) {
      event.preventDefault();
      event.stopPropagation();
      const card = cardColourChoice.closest('.notebook-card');
      if (!card) return;
      const picker = cardColourChoice.closest('[data-notebook-colour-picker]');
      const colorKey = normaliseNotebookColour(cardColourChoice.dataset.colourChoice);
      cardColourChoice.disabled = true;
      try {
        if (isNotebookSystemCard(card)) {
          const key = card.dataset.systemItemKey || card.dataset.notebookSystemCard;
          const response = await updateSystemPreferenceWithSingleRetry(key, { colorKey });
          applySystemPreference(card, response?.preference);
          return;
        }

        const response = await NotebookApi.setColour(card.dataset.noteId, colorKey, card.dataset.version);
        const updated = requireMutationItem(response);
        updateCardConcurrencyState(card, updated);
        await reconcileMutation({
          response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts,
          preservePosition: true, showGlobalError, existingCard: card,
          reconcileFailureMessage: 'The note colour was changed, but the board could not refresh. Reload the page.'
        });
        editor.syncExternalUpdate?.(updated);
      } catch (error) {
        if (error?.status === 409 && await confirmNotebookAction({ title: 'Apply colour to the latest version?', message: 'This note changed elsewhere. The selected colour can be applied to the latest saved version.', confirmText: 'Apply colour', tone: 'warning' })) {
          try {
            const latest = error.currentItem ?? await NotebookApi.getItem(card.dataset.noteId);
            const retryResponse = await NotebookApi.setColour(card.dataset.noteId, colorKey, latest.version);
            const updated = requireMutationItem(retryResponse);
            await reconcileMutation({
              response: retryResponse, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts,
              preservePosition: true, showGlobalError, existingCard: card
            });
            editor.syncExternalUpdate?.(updated);
          } catch (retryError) {
            showGlobalError(retryError.message || 'Unable to change the note colour.');
          }
        } else {
          showGlobalError(error.message || 'Unable to change the note colour.');
        }
      } finally {
        cardColourChoice.disabled = false;
        closeCardColourPickers();
      }
      return;
    }

    if (!event.target.closest('[data-notebook-colour-picker]')) closeCardColourPickers();

    const createTrigger = event.target.closest('[data-notebook-create-type]');
    if (createTrigger) {
      event.preventDefault();
      createEditor.open(createTrigger.dataset.notebookCreateType || 'Note');
      return;
    }
    const action = closestAction(event);
    if (!action) {
      const card = getPassiveNotebookCardOpenTarget(event.target, shell);
      if (card) {
        event.preventDefault();
        try { await editor.open(card.dataset.noteId); }
        catch (error) { showGlobalError(error.message || 'Unable to open the note.'); }
      }
      return;
    }
    if (action.closest('.notebook-card-more__menu')) closeNotebookMenus();
    const card = action.closest('[data-note-id]');
    const systemCard = action.closest('[data-notebook-system-card]');
    const actionCard = card || systemCard;
    const id = card?.dataset.noteId;
    if (action.dataset.action === 'label-note' && actionCard) {
      event.preventDefault();
      action.closest('details')?.removeAttribute('open');
      closeNotebookMenus();
      closeCardColourPickers();
      activeLabelCard = actionCard;
      cardLabelPicker?.configure({ value: parseCardLabels(actionCard) });
      cardLabelPicker?.open(action);
      return;
    }
    if (systemCard && ['system-add-home', 'system-remove-home', 'system-pin-note'].includes(action.dataset.action)) {
      event.preventDefault();
      action.closest('details')?.removeAttribute('open');
      action.disabled = true;
      const key = systemCard.dataset.systemItemKey || systemCard.dataset.notebookSystemCard;
      try {
        if (action.dataset.action === 'system-add-home') {
          await updateSystemPreferenceWithSingleRetry(key, { showInHome: true });
          window.location.assign('/Notebook?view=home');
          return;
        }

        if (action.dataset.action === 'system-remove-home') {
          const response = await updateSystemPreferenceWithSingleRetry(key, { showInHome: false });
          applySystemPreference(systemCard, response?.preference);
          if (view === 'home') {
            systemCard.remove();
            systemHomeCount = 0;
            shell.dataset.systemHomeCount = '0';
            board.refreshSectionVisibility();
            board.refreshEmptyState();
            dragOrder?.refresh?.();
            await refreshCounts();
          }
          showNotebookToast({ message: 'Removed from All Notes. It remains available in Shared with me.', tone: 'neutral' });
          return;
        }

        if (action.dataset.action === 'system-pin-note') {
          const nextPinned = systemCard.dataset.systemIsPinned !== 'true';
          const response = await updateSystemPreferenceWithSingleRetry(key, { isPinned: nextPinned });
          applySystemPreference(systemCard, response?.preference);
          if (view === 'home') {
            const target = board.getBoard(nextPinned);
            target?.prepend(systemCard);
            systemCard.dataset.notebookSystemHomeCard = key;
            systemCard.dataset.reorderable = 'true';
            board.refreshSectionVisibility();
            board.refreshEmptyState();
            dragOrder?.refresh?.();
          }
          return;
        }
      } catch (error) {
        showGlobalError(error?.message || 'Unable to update the PRISM note.');
      } finally {
        action.disabled = false;
      }
      return;
    }

    if (action.dataset.action === 'share-note' && card) { event.preventDefault(); action.closest('details')?.removeAttribute('open'); collaborators?.open(card); return; }
    if (action.dataset.action === 'share-note-editor') {
      event.preventDefault();
      const current = editor.getCurrentItem?.();
      if (!current?.id) return;
      const editorCard = shell.querySelector(`[data-note-id="${current.id}"]`) || { dataset: { noteId: current.id, accessLevel: current.accessLevel || 'Owner', version: current.version } };
      collaborators?.open(editorCard);
      return;
    }
    if (action.dataset.action === 'leave-note' && card) {
      event.preventDefault();
      action.closest('details')?.removeAttribute('open');
      const confirmed = await confirmNotebookAction({ title: 'Leave shared note?', message: 'The note will be removed from your notebook. The owner and other collaborators will keep access.', confirmText: 'Leave note', tone: 'warning' });
      if (!confirmed) return;
      try { const response = await NotebookApi.leaveCollaboration(id); board.removeCard(id); applyCounts(response?.counts); showNotebookToast({ message: 'You left the shared note.', tone: 'neutral' }); }
      catch (error) { showGlobalError(error?.message || 'Unable to leave the shared note.'); }
      return;
    }
    if (action.dataset.action === 'open-note' && id) {
      event.preventDefault();
      try { await editor.open(id); }
      catch (error) { showGlobalError(error.message || 'Unable to open the note.'); }
      return;
    }
    if (action.dataset.action === 'toggle-checklist' && card) {
      event.preventDefault(); action.disabled = true;
      try {
        const response = await NotebookApi.toggleChecklistItem(card.dataset.noteId, action.dataset.rowId, action.dataset.isDone !== 'true', card.dataset.version);
        const updated = requireMutationItem(response);
        updateCardConcurrencyState(card, updated);
        await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: true, showGlobalError, existingCard: card });
        editor.syncExternalUpdate?.(updated);
      } catch (error) { showGlobalError(error.message || 'Checklist update failed.'); }
      finally { action.disabled = false; }
    }
    if (['pin-note','archive-note','complete-note','reopen-note','restore-note','duplicate-note','delete-note','convert-note','restore-trash-note','delete-permanently'].includes(action.dataset.action) && id) {
      event.preventDefault(); action.disabled = true;
      try {
        if (action.dataset.action === 'pin-note') {
          const response = await NotebookApi.setPinned(id, card.dataset.isPinned !== 'true', card.dataset.version);
          const updated = requireMutationItem(response);
          updateCardConcurrencyState(card, updated);
          await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError, existingCard: card, reconcileFailureMessage: `The note was ${updated.isPinned ? 'pinned' : 'unpinned'}, but the board could not refresh. Reload the page.` });
        }
        if (action.dataset.action === 'archive-note') { const response = await NotebookApi.archiveItem(id, card.dataset.version); board.removeCard(id); applyCounts(response?.counts); }
        if (action.dataset.action === 'complete-note') { const response = await NotebookApi.completeItem(id, card.dataset.version); board.removeCard(id); applyCounts(response?.counts); }
        if (action.dataset.action === 'reopen-note') { const response = await NotebookApi.reopenItem(id, card.dataset.version); board.removeCard(id); applyCounts(response?.counts); }
        if (action.dataset.action === 'restore-note') { const response = await NotebookApi.restoreItem(id, card.dataset.version); const updated = requireMutationItem(response); updateCardConcurrencyState(card, updated); if (view === 'archive' || view === 'archived') { board.removeCard(id); applyCounts(response?.counts); } else { await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError, existingCard: card }); } }
        if (action.dataset.action === 'duplicate-note') { const response = await NotebookApi.duplicateItem(id); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError }); }
        if (action.dataset.action === 'delete-note') {
          const response = await NotebookApi.moveToTrash(id, card.dataset.version);
          board.removeCard(response?.removedItemId || id);
          applyCounts(response?.counts);
          showNotebookToast({
            message: 'Note moved to Trash.',
            tone: 'neutral',
            actionText: 'Undo',
            onAction: async () => {
              const restoreVersion = response?.item?.version || card.dataset.version;
              const restored = await NotebookApi.restoreFromTrash(id, restoreVersion);
              applyCounts(restored?.counts);
              if (view !== 'trash') await reconcileMutation({ response: restored, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError });
            }
          });
        }
        if (action.dataset.action === 'restore-trash-note') {
          const response = await NotebookApi.restoreFromTrash(id, card.dataset.version);
          board.removeCard(id);
          applyCounts(response?.counts);
          showNotebookToast({ message: 'Note restored.', tone: 'success' });
        }
        if (action.dataset.action === 'delete-permanently') {
          const confirmed = await confirmNotebookAction({ title: 'Delete permanently?', message: 'This note and its checklist data will be permanently removed.', detail: 'This action cannot be undone.', confirmText: 'Delete permanently', tone: 'danger', backdropCancels: false });
          if (!confirmed) return;
          const response = await NotebookApi.deletePermanently(id, card.dataset.version);
          board.removeCard(response?.removedItemId || id);
          applyCounts(response?.counts);
          showNotebookToast({ message: 'Note permanently deleted.', tone: 'neutral' });
        }
        if (action.dataset.action === 'convert-note') { const response = action.dataset.convertTo === 'Checklist' ? await NotebookApi.showCheckboxes(id, card.dataset.version) : await NotebookApi.hideCheckboxes(id, card.dataset.version); const converted = requireMutationItem(response); updateCardConcurrencyState(card, converted); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: true, showGlobalError, existingCard: card }); }
      } catch (error) { showGlobalError(error.message || 'Notebook action failed.'); }
      finally { action.disabled = false; }
    }
  });
  document.querySelector('[data-empty-notebook-trash]')?.addEventListener('click', async (event) => {
    const button = event.currentTarget;
    const confirmed = await confirmNotebookAction({ title: 'Empty Trash?', message: 'All notes in Trash will be permanently deleted.', detail: 'This action cannot be undone.', confirmText: 'Empty Trash', tone: 'danger', backdropCancels: false });
    if (!confirmed) return;
    button.disabled = true;
    try {
      const response = await NotebookApi.emptyTrash();
      document.querySelectorAll('[data-notebook-board] [data-note-id]').forEach(card => card.remove());
      document.querySelectorAll('[data-notebook-board]').forEach(boardElement => { boardElement.dataset.itemCount = '0'; });
      applyCounts(response?.counts);
      button.hidden = true;
      showNotebookToast({ message: `${response?.removed || 0} item(s) permanently deleted.`, tone: 'neutral' });
      location.reload();
    } catch (error) { showGlobalError(error.message || 'Trash could not be emptied.'); button.disabled = false; }
  });

  document.addEventListener('keydown', async (event) => { if (event.key !== 'Escape') return; if (createEditor.isOpen()) { event.preventDefault(); await createEditor.requestClose(); return; } if (editor.isOpen()) { event.preventDefault(); await editor.requestClose(); return; } if (composer?.isOpen()) { event.preventDefault(); await composer.close(); } });
  window.addEventListener('popstate', async () => { try { const id = new URL(location.href).searchParams.get('note'); id ? await editor.open(id, { pushHistory: false }) : await editor.requestClose({ fromHistory: true }); } catch (error) { showGlobalError(error.message || 'Unable to open the note.'); } });
  const initialUrl = new URL(location.href);
  if (initialUrl.searchParams.get('mode') === 'new') {
    createEditor.open(initialUrl.searchParams.get('type') || 'Note');
  }
  const directId = initialUrl.searchParams.get('note'); if (directId) editor.open(directId, { pushHistory: false }).catch((error) => { showGlobalError(error.message || 'Unable to open the note.'); const url = new URL(location.href); url.searchParams.delete('note'); history.replaceState(history.state, '', url); });
}
