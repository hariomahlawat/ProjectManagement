import { closestAction } from './notebook-utils.js';
import { NotebookApi } from './notebook-api.js';
import { createNotebookBoard } from './notebook-board.js';
import { initNotebookComposer } from './notebook-composer.js';
import { initNotebookEditor } from './notebook-editor.js';
import { reconcileMutation, requireMutationItem, updateCardConcurrencyState } from './notebook-reconcile.js';

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
  composer = initNotebookComposer(shell.querySelector('[data-notebook-composer]'), board, view, { showGlobalError, applyCounts });
  document.querySelector('[data-notebook-global-error-close]')?.addEventListener('click', () => { globalError.hidden = true; globalErrorText.textContent = ''; });
  const storageKey = 'notebook.boardView';
  const viewButtons = [...shell.querySelectorAll('[data-notebook-view]')];
  function applyBoardView(next) { const selected = next === 'list' ? 'list' : 'grid'; shell.dataset.boardView = selected; localStorage.setItem(storageKey, selected); viewButtons.forEach((button) => { const active = button.dataset.notebookView === selected; button.classList.toggle('is-active', active); button.setAttribute('aria-pressed', String(active)); }); }
  viewButtons.forEach((button) => button.addEventListener('click', () => applyBoardView(button.dataset.notebookView)));
  applyBoardView(localStorage.getItem(storageKey) || shell.dataset.boardView || 'grid');

  document.addEventListener('click', async (event) => {
    const action = closestAction(event); if (!action) return;
    const card = action.closest('[data-note-id]'); const id = card?.dataset.noteId;
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
        if (action.dataset.action === 'restore-note') { const response = await NotebookApi.restoreItem(id, card.dataset.version); const updated = requireMutationItem(response); updateCardConcurrencyState(card, updated); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError, existingCard: card }); }
        if (action.dataset.action === 'duplicate-note') { const response = await NotebookApi.duplicateItem(id); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError }); }
        if (action.dataset.action === 'delete-note') { const response = await NotebookApi.deleteItem(id, card.dataset.version); board.removeCard(response?.removedItemId || id); applyCounts(response?.counts); }
        if (action.dataset.action === 'convert-note') { const response = action.dataset.convertTo === 'Checklist' ? await NotebookApi.showCheckboxes(id, card.dataset.version) : await NotebookApi.hideCheckboxes(id, card.dataset.version); const converted = requireMutationItem(response); updateCardConcurrencyState(card, converted); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: true, showGlobalError, existingCard: card }); }
      } catch (error) { showGlobalError(error.message || 'Notebook action failed.'); }
      finally { action.disabled = false; }
    }
  });
  document.addEventListener('keydown', async (event) => { if (event.key !== 'Escape') return; if (editor.isOpen()) { event.preventDefault(); await editor.requestClose(); return; } if (composer?.isOpen()) { event.preventDefault(); await composer.close(); } });
  window.addEventListener('popstate', async () => { try { const id = new URL(location.href).searchParams.get('note'); id ? await editor.open(id, { pushHistory: false }) : await editor.requestClose({ fromHistory: true }); } catch (error) { showGlobalError(error.message || 'Unable to open the note.'); } });
  const directId = new URL(location.href).searchParams.get('note'); if (directId) editor.open(directId, { pushHistory: false }).catch((error) => { showGlobalError(error.message || 'Unable to open the note.'); const url = new URL(location.href); url.searchParams.delete('note'); history.replaceState(history.state, '', url); });
}
