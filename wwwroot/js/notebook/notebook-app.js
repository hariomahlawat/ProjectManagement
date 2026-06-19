import { closestAction } from './notebook-utils.js';
import { NotebookApi } from './notebook-api.js';
import { createNotebookBoard } from './notebook-board.js';
import { initNotebookComposer } from './notebook-composer.js';
import { initNotebookEditor } from './notebook-editor.js';

// SECTION: Notebook app bootstrap and delegated interactions
export function initNotebookApp() {
  const shell = document.querySelector('.notebook-shell'); if (!shell) return;
  const view = new URL(location.href).searchParams.get('view') || 'home';
  const board = createNotebookBoard(document);
  const editor = initNotebookEditor(board, view);
  const composer = initNotebookComposer(document.querySelector('[data-notebook-composer]'), board, view);
  const storageKey = 'notebook.boardView';
  const viewButtons = [...document.querySelectorAll('[data-notebook-view]')];
  function applyBoardView(next) { const selected = next === 'list' ? 'list' : 'grid'; shell.dataset.boardView = selected; localStorage.setItem(storageKey, selected); viewButtons.forEach((button) => { const active = button.dataset.notebookView === selected; button.classList.toggle('is-active', active); button.setAttribute('aria-pressed', String(active)); }); }
  viewButtons.forEach((button) => button.addEventListener('click', () => applyBoardView(button.dataset.notebookView)));
  applyBoardView(localStorage.getItem(storageKey) || shell.dataset.boardView || 'grid');

  document.addEventListener('click', async (event) => {
    const action = closestAction(event); if (!action) return;
    const card = action.closest('[data-note-id]'); const id = card?.dataset.noteId;
    if (action.dataset.action === 'open-note' && id) { event.preventDefault(); editor.open(id); }
    if (['pin-note','archive-note','complete-note','reopen-note','restore-note','duplicate-note','delete-note','convert-note'].includes(action.dataset.action) && id) {
      event.preventDefault(); action.disabled = true;
      try {
        if (action.dataset.action === 'pin-note') { const updated = await NotebookApi.setPinned(id, card.dataset.isPinned !== 'true'); const html = await NotebookApi.getCardHtml(id, view); board.upsertCard(id, html, updated.isPinned, { prepend: true }); }
        if (action.dataset.action === 'archive-note') { await NotebookApi.archiveItem(id); board.removeCard(id); }
        if (action.dataset.action === 'complete-note') { await NotebookApi.completeItem(id); board.removeCard(id); }
        if (action.dataset.action === 'reopen-note') { await NotebookApi.reopenItem(id); board.removeCard(id); }
        if (action.dataset.action === 'restore-note') { await NotebookApi.restoreItem(id); board.removeCard(id); }
        if (action.dataset.action === 'duplicate-note') { const copy = await NotebookApi.duplicateItem(id); const html = await NotebookApi.getCardHtml(copy.id, view); board.upsertCard(copy.id, html, copy.isPinned, { prepend: true }); }
        if (action.dataset.action === 'delete-note') { await NotebookApi.deleteItem(id); board.removeCard(id); }
        if (action.dataset.action === 'convert-note') { const converted = action.dataset.convertTo === 'Checklist' ? await NotebookApi.showCheckboxes(id) : await NotebookApi.hideCheckboxes(id); const html = await NotebookApi.getCardHtml(id, view); board.upsertCard(id, html, converted.isPinned, { prepend: false }); }
      } catch (error) { shell.dataset.error = error.message || 'Notebook action failed.'; }
      finally { action.disabled = false; }
    }
  });
  document.addEventListener('keydown', async (event) => { if (event.key !== 'Escape') return; if (editor.isOpen()) { event.preventDefault(); await editor.requestClose(); return; } if (composer?.isOpen()) { event.preventDefault(); await composer.close(); } });
  window.addEventListener('popstate', async () => { const id = new URL(location.href).searchParams.get('note'); id ? await editor.open(id, false) : await editor.requestClose({ fromHistory: true }); });
  const directId = new URL(location.href).searchParams.get('note'); if (directId) editor.open(directId, false);
}
