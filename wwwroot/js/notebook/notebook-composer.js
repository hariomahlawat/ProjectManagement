import { NotebookApi, NotebookApiError } from './notebook-api.js';
import { createChecklistEditor } from './notebook-checklist-editor.js';
import { reconcileMutation } from './notebook-reconcile.js';
import { applyNotebookSurfaceColour, initNotebookColourPicker } from './notebook-colour-picker.js';
import { initNotebookLabelPicker, refreshNotebookLabelCatalog } from './notebook-label-picker.js';
import { resizeTextareaToContent } from './notebook-textarea-autosize.js';

const QUICK_DRAFT_PREFIX = 'prism.notebook.quickDraft';
const DRAFT_DEBOUNCE_MS = 250;

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
  const colourPickerRoot = root.querySelector('[data-notebook-colour-picker]');
  const labelPickerRoot = root.querySelector('[data-notebook-label-picker]');
  const showGlobalError = options.showGlobalError || (() => {});
  const applyCounts = options.applyCounts || (() => {});
  const currentUserId = root.closest('.notebook-shell')?.dataset.currentUserId || 'current';
  const draftKey = `${QUICK_DRAFT_PREFIX}:${currentUserId}`;

  let mode = 'collapsed';
  let isPinned = false;
  let created = null;
  let isSaving = false;
  let draftTimer = null;
  let clientRequestId = crypto.randomUUID();
  let colourPicker = null;
  let labelPicker = null;

  const checklist = createChecklistEditor(checklistRoot, { onChange: scheduleDraftSave });
  colourPicker = colourPickerRoot ? initNotebookColourPicker(colourPickerRoot, {
    value: '',
    onSelect: (value) => {
      applyNotebookSurfaceColour(root, value);
      scheduleDraftSave();
    }
  }) : null;
  labelPicker = labelPickerRoot ? initNotebookLabelPicker(labelPickerRoot, {
    value: [],
    onChange: () => scheduleDraftSave(),
    onError: (error) => showGlobalError(error?.message || 'Unable to create the label.')
  }) : null;

  const refreshBodyLayout = () => {
    if (!body || mode === 'checklist') return;
    resizeTextareaToContent(body, { minimumHeight: 56, maximumHeight: 200 });
  };

  const setStatus = (text) => { if (status) status.textContent = text || ''; };
  const setDisabled = (disabled) => {
    if (closeButton) closeButton.disabled = disabled;
    if (checklistButton) checklistButton.disabled = disabled;
    if (pin) pin.disabled = disabled;
    colourPicker?.setBusy(disabled);
    labelPicker?.setBusy(disabled);
  };
  const renderPinState = () => {
    if (!pin) return;
    pin.classList.toggle('is-active', isPinned);
    pin.setAttribute('aria-pressed', isPinned ? 'true' : 'false');
    const label = isPinned ? 'Unpin note' : 'Pin note';
    pin.setAttribute('aria-label', label);
    pin.title = label;
  };

  const setMode = (next, { persist = true } = {}) => {
    mode = next;
    root.dataset.state = next;
    collapsed.hidden = next !== 'collapsed';
    expanded.hidden = next === 'collapsed';
    body.hidden = next === 'checklist';
    checklistRoot.hidden = next !== 'checklist';
    if (next === 'checklist') checklist.refreshLayout?.();
    else queueMicrotask(refreshBodyLayout);
    if (persist) scheduleDraftSave();
  };

  // SECTION: Composer payload composition
  const payload = () => ({
    title: title.value.trim(),
    body: body.value.trim(),
    type: mode === 'checklist' ? 'Checklist' : 'Note',
    priority: 'Normal',
    reminderAtUtc: null,
    colorKey: colourPicker?.getValue() || null,
    isPinned,
    labels: labelPicker?.getValue() || [],
    clientRequestId,
    checklistRows: mode === 'checklist'
      ? checklist.getRows()
        .map((row, index) => ({ id: row.id, text: row.text.trim(), isDone: row.isDone, sortOrder: (index + 1) * 1000 }))
        .filter((row) => row.text.length > 0)
      : []
  });
  const meaningful = (data) => Boolean(data.title || data.body || data.checklistRows.length);

  function clearStoredDraft() {
    sessionStorage.removeItem(draftKey);
  }

  function writeDraft() {
    draftTimer = null;
    if (isSaving) return;
    const data = payload();
    if (!meaningful(data)) {
      clearStoredDraft();
      return;
    }
    sessionStorage.setItem(draftKey, JSON.stringify({
      title: title.value,
      body: body.value,
      mode: mode === 'checklist' ? 'checklist' : 'note',
      checklistRows: checklist.getRows(),
      isPinned,
      colorKey: colourPicker?.getValue() || null,
      labels: labelPicker?.getValue() || [],
      clientRequestId,
      savedAtUtc: new Date().toISOString()
    }));
  }

  function scheduleDraftSave() {
    if (draftTimer) window.clearTimeout(draftTimer);
    draftTimer = window.setTimeout(writeDraft, DRAFT_DEBOUNCE_MS);
  }

  function readStoredDraft() {
    const raw = sessionStorage.getItem(draftKey);
    if (!raw) return null;
    try {
      const parsed = JSON.parse(raw);
      if (!parsed || typeof parsed !== 'object') throw new Error('Invalid Notebook draft.');
      return parsed;
    } catch {
      clearStoredDraft();
      return null;
    }
  }

  const reset = ({ clearDraft = true } = {}) => {
    if (draftTimer) window.clearTimeout(draftTimer);
    draftTimer = null;
    title.value = '';
    body.value = '';
    checklist.clear();
    isPinned = false;
    colourPicker?.setValue('');
    labelPicker?.setValue([]);
    applyNotebookSurfaceColour(root, '');
    created = null;
    clientRequestId = crypto.randomUUID();
    renderPinState();
    setStatus('');
    refreshBodyLayout();
    if (clearDraft) clearStoredDraft();
  };

  function restoreDraft() {
    const draft = readStoredDraft();
    if (!draft) return false;
    title.value = String(draft.title || '');
    body.value = String(draft.body || '');
    checklist.setRows(Array.isArray(draft.checklistRows) ? draft.checklistRows : []);
    isPinned = Boolean(draft.isPinned);
    colourPicker?.setValue(draft.colorKey || '');
    labelPicker?.setValue(Array.isArray(draft.labels) ? draft.labels : []);
    applyNotebookSurfaceColour(root, draft.colorKey || '');
    renderPinState();
    if (typeof draft.clientRequestId === 'string' && draft.clientRequestId) clientRequestId = draft.clientRequestId;
    setMode(draft.mode === 'checklist' ? 'checklist' : 'note', { persist: false });
    return true;
  }

  // SECTION: Mutation and reconciliation lifecycle
  async function closeComposer() {
    const data = payload();
    if (!meaningful(data)) {
      reset();
      setMode('collapsed', { persist: false });
      return true;
    }
    if (isSaving) return false;
    colourPicker?.close();
    labelPicker?.close();
    if (draftTimer) window.clearTimeout(draftTimer);
    draftTimer = null;
    writeDraft();
    isSaving = true;
    setDisabled(true);
    setStatus('Saving…');
    try {
      if (!created) created = await NotebookApi.createItem(data);
      if (!created?.item) {
        throw new NotebookApiError('The create response did not contain the new note.', { code: 'notebook_invalid_mutation_response' });
      }

      await reconcileMutation({
        response: created,
        board,
        view: view || 'home',
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts,
        preservePosition: false,
        prepend: true,
        showGlobalError,
        renderFailureMessage: 'The note was saved, but its card could not be rendered. Reload the page.',
        reconcileFailureMessage: 'The note was saved, but the board could not refresh. Reload the page.'
      });
      if (data.labels.length > 0) {
        try { await refreshNotebookLabelCatalog(); }
        catch (error) { console.warn('Notebook label counts could not be refreshed after quick capture.', error); }
      }
      reset();
      setMode('collapsed', { persist: false });
      return true;
    } catch (error) {
      scheduleDraftSave();
      setStatus(error.message || 'Unable to save the note.');
      return false;
    } finally {
      isSaving = false;
      setDisabled(false);
    }
  }

  root.querySelector('[data-composer-open-note]')?.addEventListener('click', () => {
    if (isSaving) return;
    setMode('note');
    body.focus();
  });
  checklistButton?.addEventListener('click', () => {
    if (isSaving) return;
    setMode('checklist');
    if (checklist.getRows().length === 0) checklist.setRows([{ text: '' }]);
    checklist.focusFirst();
  });
  closeButton?.addEventListener('click', closeComposer);
  pin?.addEventListener('click', () => {
    if (isSaving) return;
    isPinned = !isPinned;
    renderPinState();
    scheduleDraftSave();
  });
  title?.addEventListener('input', scheduleDraftSave);
  body?.addEventListener('input', () => {
    refreshBodyLayout();
    scheduleDraftSave();
  });
  window.addEventListener('beforeunload', writeDraft);

  // A quick-capture draft survives an accidental refresh in the current browser session.
  if (!restoreDraft()) renderPinState();

  return {
    close: closeComposer,
    isOpen: () => mode !== 'collapsed',
    destroy() {
      if (draftTimer) window.clearTimeout(draftTimer);
      labelPicker?.destroy?.();
      window.removeEventListener('beforeunload', writeDraft);
    }
  };
}
