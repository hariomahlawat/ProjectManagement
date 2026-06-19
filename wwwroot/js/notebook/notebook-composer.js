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
  const checklist = createChecklistEditor(checklistRoot);
  let mode = 'collapsed'; let isPinned = false;
  const setStatus = (text) => { if (status) status.textContent = text || ''; };
  const setMode = (next) => { mode = next; root.dataset.state = next; collapsed.hidden = next !== 'collapsed'; expanded.hidden = next === 'collapsed'; body.hidden = next === 'checklist'; checklistRoot.hidden = next !== 'checklist'; };
  const reset = () => { title.value = ''; body.value = ''; checklist.clear(); isPinned = false; pin.classList.remove('is-active'); setStatus(''); };
  const payload = () => ({ title: title.value.trim(), body: body.value.trim(), type: mode === 'checklist' ? 'Checklist' : 'Note', isPinned, checklistRows: mode === 'checklist' ? checklist.getRows() : [] });
  const meaningful = (p) => Boolean(p.title || p.body || p.checklistRows.length);
  async function closeComposer() { const data = payload(); if (!meaningful(data)) { reset(); setMode('collapsed'); return true; } setStatus('Saving…'); try { const created = await NotebookApi.createItem(data); const html = await NotebookApi.getCardHtml(created.id, view); board.upsertCard(created.id, html, created.isPinned, { prepend: true }); reset(); setMode('collapsed'); return true; } catch (error) { setStatus(error.status === 409 ? 'This note was changed in another tab. Reload the latest version before continuing.' : 'Unable to save. Retry.'); return false; } }
  root.querySelector('[data-composer-open-note]')?.addEventListener('click', () => { setMode('note'); body.focus(); });
  root.querySelector('[data-composer-open-checklist]')?.addEventListener('click', () => { setMode('checklist'); checklist.setRows([{ text: '' }]); checklist.focusFirst(); });
  root.querySelector('[data-composer-close]')?.addEventListener('click', closeComposer);
  pin?.addEventListener('click', () => { isPinned = !isPinned; pin.classList.toggle('is-active', isPinned); });
  return { close: closeComposer, isOpen: () => mode !== 'collapsed' };
}
