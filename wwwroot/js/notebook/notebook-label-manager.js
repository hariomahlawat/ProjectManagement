import { NotebookApi } from './notebook-api.js';
import { setNotebookLabelCatalog } from './notebook-label-picker.js';

export function initNotebookLabelManager(root, options = {}) {
  if (!root) return null;
  const feedback = root.querySelector('[data-label-manager-feedback]');
  const list = root.querySelector('.notebook-label-manager__list');
  const setFeedback = (text = '', error = false) => { feedback.textContent = text; feedback.hidden = !text; feedback.classList.toggle('is-error', error); };
  const open = () => { root.hidden = false; document.body.classList.add('notebook-modal-open'); queueMicrotask(() => root.querySelector('input')?.focus()); };
  const close = () => { root.hidden = true; document.body.classList.remove('notebook-modal-open'); setFeedback(''); };
  root.addEventListener('click', async (event) => {
    if (event.target.closest('[data-label-manager-close]')) { close(); return; }
    const row = event.target.closest('[data-label-id]'); if (!row) return;
    const id = Number(row.dataset.labelId); const input = row.querySelector('input');
    if (event.target.closest('[data-label-rename]')) {
      const name = input.value.trim().replace(/^#+/, '').trim();
      if (!name) { setFeedback('Enter a label name.', true); input.focus(); return; }
      try { setFeedback('Saving…'); const result = await NotebookApi.renameLabel(id, name); setNotebookLabelCatalog(result.labels || []); location.reload(); }
      catch (error) { setFeedback(error.message || 'Unable to rename the label.', true); }
    }
    if (event.target.closest('[data-label-delete]')) {
      if (!confirm(`Delete label “${input.value}” from all notes? Notes will not be deleted.`)) return;
      try { setFeedback('Deleting…'); const result = await NotebookApi.deleteLabel(id); setNotebookLabelCatalog(result.labels || []); location.reload(); }
      catch (error) { setFeedback(error.message || 'Unable to delete the label.', true); }
    }
  });
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape' && !root.hidden) close(); });
  return { open, close };
}
