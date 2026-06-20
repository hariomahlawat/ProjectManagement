import { NotebookApi } from './notebook-api.js';
import { createChecklistEditor } from './notebook-checklist-editor.js';

// SECTION: Expandable notebook composer component
export function initNotebookComposer(root, board, view, options = {}) {
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
  const showGlobalError = options.showGlobalError || (() => {});
  const applyCounts = options.applyCounts || (() => {});
  let mode = 'collapsed'; let isPinned = false; let created = null; let isSaving = false; let clientRequestId = crypto.randomUUID();
  const setStatus = (text) => { if (status) status.textContent = text || ''; };
  const setDisabled = (disabled) => { if (closeButton) closeButton.disabled = disabled; if (checklistButton) checklistButton.disabled = disabled; if (pin) pin.disabled = disabled; };
  const setMode = (next) => { mode = next; root.dataset.state = next; collapsed.hidden = next !== 'collapsed'; expanded.hidden = next === 'collapsed'; body.hidden = next === 'checklist'; checklistRoot.hidden = next !== 'checklist'; };
  const reset = () => { title.value = ''; body.value = ''; checklist.clear(); isPinned = false; created = null; clientRequestId = crypto.randomUUID(); pin.classList.remove('is-active'); setStatus(''); };

  // SECTION: Composer payload composition
  const payload = () => ({
    title: title.value.trim(),
    body: body.value.trim(),
    type: mode === 'checklist' ? 'Checklist' : 'Note',
    priority: 'Normal',
    reminderAtUtc: null,
    colorKey: null,
    isPinned,
    labels: [],
    clientRequestId,
    checklistRows: mode === 'checklist' ? checklist.getRows().map((row, index) => ({ id: row.id, text: row.text.trim(), isDone: row.isDone, sortOrder: (index + 1) * 1000 })).filter((row) => row.text.length > 0) : []
  });
  const meaningful = (p) => Boolean(p.title || p.body || p.checklistRows.length);

  // SECTION: Mutation and reconciliation lifecycle
  async function closeComposer() {
    const data = payload();
    if (!meaningful(data)) { reset(); setMode('collapsed'); return true; }
    if (isSaving) return false;
    isSaving = true; setDisabled(true); setStatus('Saving…');
    try {
      if (!created) created = await NotebookApi.createItem(data);
    } catch (error) { setStatus(error.message || 'Unable to save. Retry.'); return false; }

    try {
      const item = created.item || created;
      const html = created.cardHtml || await NotebookApi.getCardHtml(item.id, view || 'home');
      board.upsertCard(item.id, html, item.isPinned, { prepend: true, preservePosition: false });
      applyCounts(created.counts);
      reset(); setMode('collapsed'); return true;
    } catch (error) {
      reset(); setMode('collapsed');
      showGlobalError('The note was saved, but the board could not refresh. Reload the page to view the latest state.');
      return true;
    } finally { isSaving = false; setDisabled(false); }
  }
  root.querySelector('[data-composer-open-note]')?.addEventListener('click', () => { if (isSaving) return; setMode('note'); body.focus(); });
  checklistButton?.addEventListener('click', () => { if (isSaving) return; setMode('checklist'); checklist.setRows([{ text: '' }]); checklist.focusFirst(); });
  closeButton?.addEventListener('click', closeComposer);
  pin?.addEventListener('click', () => { if (isSaving) return; isPinned = !isPinned; pin.classList.toggle('is-active', isPinned); });
  return { close: closeComposer, isOpen: () => mode !== 'collapsed' };
}
