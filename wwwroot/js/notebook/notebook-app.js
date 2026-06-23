import { closestAction } from './notebook-utils.js';
import { NotebookApi } from './notebook-api.js';
import { createNotebookBoard } from './notebook-board.js';
import { initNotebookComposer } from './notebook-composer.js';
import { initNotebookEditor } from './notebook-editor.js';
import { initNotebookCreateEditor } from './notebook-create-editor.js';
import { reconcileMutation, requireMutationItem, updateCardConcurrencyState } from './notebook-reconcile.js';
import { closeNotebookColourPickers, normaliseNotebookColour } from './notebook-colour-picker.js';
import { initNotebookLabelManager } from './notebook-label-manager.js';
import { getNotebookLabelCatalog, initNotebookLabelPicker, refreshNotebookLabelCatalog, setNotebookLabelCatalog } from './notebook-label-picker.js';


export function renderNotebookLabelNavigation(shell, labels = []) {
  if (!shell) return;
  const safeLabels = Array.isArray(labels) ? labels : [];
  const rail = shell.querySelector('[data-notebook-label-rail]');
  if (rail) {
    rail.innerHTML = '';
    safeLabels.slice(0, 20).forEach((label) => {
      const link = document.createElement('a');
      link.href = `/Notebook?view=labels&tag=${encodeURIComponent(label.name)}`;
      const currentTag = new URL(location.href).searchParams.get('tag');
      if (currentTag && currentTag.toLocaleLowerCase() === String(label.name).toLocaleLowerCase()) link.classList.add('is-active');
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
  shell.querySelectorAll('[data-notebook-count="labels"]').forEach((element) => { element.textContent = String(safeLabels.length); });
}

function parseCardLabels(card) {
  try { return JSON.parse(card?.dataset?.labels || '[]'); }
  catch { return []; }
}

function escapeLabelHtml(value) {
  return String(value || '').replace(/[&<>'"]/g, (character) => ({ '&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;' }[character]));
}

// SECTION: Notebook app bootstrap and delegated interactions
export function initNotebookApp() {
  const shell = document.querySelector('.notebook-shell'); if (!shell) return;
  const view = new URL(location.href).searchParams.get('view') || 'home';
  const board = createNotebookBoard(shell);
  let composer;
  const globalError = document.querySelector('[data-notebook-global-error]');
  const globalErrorText = document.querySelector('[data-notebook-global-error-text]');
  const showGlobalError = (message) => { if (!globalError || !globalErrorText) { shell.dataset.error = message || 'Notebook action failed.'; return; } globalErrorText.textContent = message || 'Notebook action failed.'; globalError.hidden = false; };
  const applyCounts = (counts) => { if (!counts) return; Object.entries(counts).forEach(([key, value]) => shell.querySelectorAll(`[data-notebook-count="${key}"]`).forEach((el) => { el.textContent = String(value); })); };
  const refreshCounts = async () => applyCounts(await NotebookApi.getCounts());
  const editor = initNotebookEditor(board, view, { shell, showGlobalError, applyCounts });
  getNotebookLabelCatalog(document);
  const createEditor = initNotebookCreateEditor(board, view, { shell, showGlobalError, applyCounts });
  const labelManager = initNotebookLabelManager(document.querySelector('[data-notebook-label-manager]'), {
    showGlobalError,
    onCatalogChange: (labels) => renderNotebookLabelNavigation(shell, labels)
  });
  document.querySelectorAll('[data-open-label-manager]').forEach((button) => button.addEventListener('click', () => labelManager?.open()));

  let activeLabelCard = null;
  const cardLabelPicker = initNotebookLabelPicker(
    document.querySelector('[data-notebook-card-label-host] [data-notebook-label-picker]'),
    {
      value: [],
      onError: (error) => showGlobalError(error?.message || 'Unable to create the label.'),
      onChange: async (labels) => {
        const card = activeLabelCard;
        if (!card?.dataset?.noteId) return;
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
          if (error?.status === 409 && window.confirm('This note changed elsewhere. Apply these labels to the latest saved version?')) {
            const latest = error.currentItem ?? await NotebookApi.getItem(card.dataset.noteId);
            await apply(latest.version);
            return;
          }
          throw error;
        }
      }
    }
  );

  document.addEventListener('notebook:labels-changed', (event) => {
    renderNotebookLabelNavigation(shell, event.detail?.labels || getNotebookLabelCatalog());
  });
  renderNotebookLabelNavigation(shell, getNotebookLabelCatalog());
  composer = initNotebookComposer(shell.querySelector('[data-notebook-composer]'), board, view, { showGlobalError, applyCounts });
  document.querySelector('[data-notebook-global-error-close]')?.addEventListener('click', () => { globalError.hidden = true; globalErrorText.textContent = ''; });
  const storageKey = 'notebook.boardView';
  const viewButtons = [...shell.querySelectorAll('[data-notebook-view]')];
  function applyBoardView(next) { const selected = next === 'list' ? 'list' : 'grid'; shell.dataset.boardView = selected; localStorage.setItem(storageKey, selected); viewButtons.forEach((button) => { const active = button.dataset.notebookView === selected; button.classList.toggle('is-active', active); button.setAttribute('aria-pressed', String(active)); }); }
  viewButtons.forEach((button) => button.addEventListener('click', () => applyBoardView(button.dataset.notebookView)));
  applyBoardView(localStorage.getItem(storageKey) || shell.dataset.boardView || 'grid');

  document.addEventListener('click', async (event) => {
    const cardColourToggle = event.target.closest('.notebook-card [data-colour-picker-toggle]');
    if (cardColourToggle) {
      event.preventDefault();
      event.stopPropagation();
      const picker = cardColourToggle.closest('[data-notebook-colour-picker]');
      const popover = picker?.querySelector('[data-colour-picker-popover]');
      if (!picker || !popover) return;
      const shouldOpen = popover.hidden;
      closeNotebookColourPickers(document, shouldOpen ? picker : null);
      popover.hidden = !shouldOpen;
      cardColourToggle.setAttribute('aria-expanded', String(shouldOpen));
      if (shouldOpen) popover.querySelector('.is-selected,[data-colour-choice]')?.focus?.();
      return;
    }

    const cardColourChoice = event.target.closest('.notebook-card [data-colour-choice]');
    if (cardColourChoice) {
      event.preventDefault();
      event.stopPropagation();
      const card = cardColourChoice.closest('[data-note-id]');
      if (!card) return;
      const picker = cardColourChoice.closest('[data-notebook-colour-picker]');
      const colorKey = normaliseNotebookColour(cardColourChoice.dataset.colourChoice);
      cardColourChoice.disabled = true;
      try {
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
        if (error?.status === 409 && window.confirm('This note changed elsewhere. Apply the selected colour to the latest saved version?')) {
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
        closeNotebookColourPickers(document);
      }
      return;
    }

    if (!event.target.closest('[data-notebook-colour-picker]')) closeNotebookColourPickers(document);

    const createTrigger = event.target.closest('[data-notebook-create-type]');
    if (createTrigger) {
      event.preventDefault();
      createEditor.open(createTrigger.dataset.notebookCreateType || 'Note');
      return;
    }
    const action = closestAction(event); if (!action) return;
    const card = action.closest('[data-note-id]'); const id = card?.dataset.noteId;
    if (action.dataset.action === 'label-note' && card) {
      event.preventDefault();
      action.closest('details')?.removeAttribute('open');
      activeLabelCard = card;
      cardLabelPicker?.configure({ value: parseCardLabels(card) });
      cardLabelPicker?.open(action);
      return;
    }
    if (action.dataset.action === 'open-note' && id) { event.preventDefault(); try { await editor.open(id); } catch (error) { showGlobalError(error.message || 'Unable to open the note.'); } }
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
    if (['pin-note','archive-note','complete-note','reopen-note','restore-note','duplicate-note','delete-note','convert-note'].includes(action.dataset.action) && id) {
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
        if (action.dataset.action === 'delete-note') { const response = await NotebookApi.deleteItem(id, card.dataset.version); board.removeCard(response?.removedItemId || id); applyCounts(response?.counts); }
        if (action.dataset.action === 'convert-note') { const response = action.dataset.convertTo === 'Checklist' ? await NotebookApi.showCheckboxes(id, card.dataset.version) : await NotebookApi.hideCheckboxes(id, card.dataset.version); const converted = requireMutationItem(response); updateCardConcurrencyState(card, converted); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: true, showGlobalError, existingCard: card }); }
      } catch (error) { showGlobalError(error.message || 'Notebook action failed.'); }
      finally { action.disabled = false; }
    }
  });
  document.addEventListener('keydown', async (event) => { if (event.key !== 'Escape') return; if (createEditor.isOpen()) { event.preventDefault(); createEditor.close(); return; } if (editor.isOpen()) { event.preventDefault(); await editor.requestClose(); return; } if (composer?.isOpen()) { event.preventDefault(); await composer.close(); } });
  window.addEventListener('popstate', async () => { try { const id = new URL(location.href).searchParams.get('note'); id ? await editor.open(id, { pushHistory: false }) : await editor.requestClose({ fromHistory: true }); } catch (error) { showGlobalError(error.message || 'Unable to open the note.'); } });
  const initialUrl = new URL(location.href);
  if (initialUrl.searchParams.get('mode') === 'new') {
    createEditor.open(initialUrl.searchParams.get('type') || 'Note');
  }
  const directId = initialUrl.searchParams.get('note'); if (directId) editor.open(directId, { pushHistory: false }).catch((error) => { showGlobalError(error.message || 'Unable to open the note.'); const url = new URL(location.href); url.searchParams.delete('note'); history.replaceState(history.state, '', url); });
}
