import { NotebookApi } from './notebook-api.js';
import { createChecklistEditor } from './notebook-checklist-editor.js';

// SECTION: Expandable notebook composer component
export function initNotebookComposer(root, board, view) {
  if (!root) return null;
  const collapsed = root.querySelector('[data-composer-collapsed]');
  const expanded = root.querySelector('[data-composer-expanded]');
  const title = root.querySelector('[data-composer-title]');
  const body = root.querySelector('[data-composer-body]');
  const checklistRoot = root.querySelector('[data-composer-checklist]');
  const status = root.querySelector('[data-composer-status]');
  const pin = root.querySelector('[data-composer-pin]');
  const closeButton = root.querySelector('[data-composer-close]');
  const checklistButton = root.querySelector('[data-composer-open-checklist]');
  const checklist = createChecklistEditor(checklistRoot);
  let mode = 'collapsed'; let isPinned = false; let createdItem = null; let isSaving = false;
  const setStatus = (text) => { if (status) status.textContent = text || ''; };
  const setDisabled = (disabled) => { if (closeButton) closeButton.disabled = disabled; if (checklistButton) checklistButton.disabled = disabled; if (pin) pin.disabled = disabled; };
  const setMode = (next) => { mode = next; root.dataset.state = next; collapsed.hidden = next !== 'collapsed'; expanded.hidden = next === 'collapsed'; body.hidden = next === 'checklist'; checklistRoot.hidden = next !== 'checklist'; };
  const reset = () => { title.value = ''; body.value = ''; checklist.clear(); isPinned = false; createdItem = null; pin.classList.remove('is-active'); setStatus(''); };
  const payload = () => ({ title: title.value.trim(), body: body.value.trim(), type: mode === 'checklist' ? 'Checklist' : 'Note', isPinned, checklistRows: mode === 'checklist' ? checklist.getRows() : [] });
  const meaningful = (p) => Boolean(p.title || p.body || p.checklistRows.length);
  async function closeComposer() {
    const data = payload();
    if (!meaningful(data)) { reset(); setMode('collapsed'); return true; }
    if (isSaving) return false;
    isSaving = true; setDisabled(true); setStatus('Saving…');
    try {
      if (!createdItem) createdItem = await NotebookApi.createItem(data);
      const html = await NotebookApi.getCardHtml(createdItem.id, 'home');
      board.upsertCard(createdItem.id, html, createdItem.isPinned, { prepend: true });
      reset(); setMode('collapsed'); return true;
    } catch (error) { setStatus(error.message || 'Unable to save. Retry.'); return false; }
    finally { isSaving = false; setDisabled(false); }
  }
  root.querySelector('[data-composer-open-note]')?.addEventListener('click', () => { if (isSaving) return; setMode('note'); body.focus(); });
  checklistButton?.addEventListener('click', () => { if (isSaving) return; setMode('checklist'); checklist.setRows([{ text: '' }]); checklist.focusFirst(); });
  closeButton?.addEventListener('click', closeComposer);
  pin?.addEventListener('click', () => { if (isSaving) return; isPinned = !isPinned; pin.classList.toggle('is-active', isPinned); });
  return { close: closeComposer, isOpen: () => mode !== 'collapsed' };
}
