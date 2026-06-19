import { NotebookApi } from './notebook-api.js';

// SECTION: Inline composer component
export function initNotebookComposer(root, board, view) {
  if (!root) return;
  const form = root.querySelector('form');
  const input = root.querySelector('input[name="QuickCaptureText"]');
  const checklist = root.querySelector('.notebook-composer__checklist');
  form?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const text = (input?.value || '').trim();
    if (!text) return;
    const type = event.submitter === checklist ? 'Checklist' : 'Note';
    const payload = type === 'Checklist' ? { title: '', type, checklistRows: [{ text, sortOrder: 0 }] } : { title: text, body: '', type };
    event.submitter.disabled = true;
    try { const created = await NotebookApi.createItem(payload); const html = await NotebookApi.getCardHtml(created.id, view); board.insertCard(html, created.isPinned); input.value = ''; }
    catch (error) { root.dataset.error = error.message; }
    finally { event.submitter.disabled = false; }
  });
}
