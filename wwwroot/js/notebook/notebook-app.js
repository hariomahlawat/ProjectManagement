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
  initNotebookComposer(document.querySelector('[data-notebook-composer]'), board, view);
  document.addEventListener('click', async (event) => {
    const action = closestAction(event); if (!action) return;
    const card = action.closest('[data-note-id]'); const id = card?.dataset.noteId;
    if (action.dataset.action === 'open-note' && id) { event.preventDefault(); editor.open(id); }
    if (['pin-note','archive-note','complete-note','reopen-note'].includes(action.dataset.action) && id) {
      event.preventDefault();
      if (action.dataset.action === 'pin-note') { const isPinned = action.dataset.isPinned !== 'true'; await NotebookApi.setPinned(id, isPinned); const html = await NotebookApi.getCardHtml(id, view); board.replaceCard(id, html); }
      if (action.dataset.action === 'archive-note') { await NotebookApi.archiveItem(id); board.removeCard(id); }
      if (action.dataset.action === 'complete-note') { await NotebookApi.completeItem(id); board.removeCard(id); }
      if (action.dataset.action === 'reopen-note') { await NotebookApi.reopenItem(id); board.removeCard(id); }
    }
  });
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape') editor.close(); });
  window.addEventListener('popstate', () => { const id = new URL(location.href).searchParams.get('note'); id ? editor.open(id, false) : editor.close(); });
  const directId = new URL(location.href).searchParams.get('note'); if (directId) editor.open(directId, false);
}
