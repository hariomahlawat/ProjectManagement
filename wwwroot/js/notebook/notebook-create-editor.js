import { NotebookApi, NotebookApiError } from './notebook-api.js';
import { createChecklistEditor } from './notebook-checklist-editor.js';
import { reconcileMutation } from './notebook-reconcile.js';
import { cloneNotebookEditorTemplate, requireEditorElement, EditorSelectors } from './notebook-editor.js';
import { initNotebookColourPicker, applyNotebookSurfaceColour } from './notebook-colour-picker.js';

const ALLOWED_TYPES = new Set(['Note', 'Checklist', 'Reminder', 'Idea', 'Draft', 'Sticky']);

export function toIstIso(localValue) {
  const value = String(localValue || '').trim();
  if (!value) return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) return null;
  return `${match[1]}-${match[2]}-${match[3]}T${match[4]}:${match[5]}:00+05:30`;
}

export function parseLabels(value) {
  const seen = new Set();
  return String(value || '')
    .split(',')
    .map((label) => label.trim())
    .filter((label) => {
      if (!label) return false;
      const key = label.toLocaleLowerCase();
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    });
}

export function getCreateTypeUi(type) {
  const safeType = ALLOWED_TYPES.has(type) ? type : 'Note';
  const names = {
    Note: 'note',
    Checklist: 'checklist',
    Reminder: 'reminder',
    Idea: 'idea',
    Draft: 'draft',
    Sticky: 'sticky note'
  };
  return {
    type: safeType,
    actionLabel: `Create ${names[safeType]}`,
    titlePlaceholder: safeType === 'Reminder' ? 'Reminder title' : 'Title',
    bodyPlaceholder: safeType === 'Reminder' ? 'Add notes…' : 'Take a note…',
    showChecklist: safeType === 'Checklist',
    showBody: safeType !== 'Checklist',
    openDetails: safeType === 'Reminder'
  };
}

export function buildCreatePayload({ type, title, body, reminderLocal, priority, colorKey, labels, isPinned, checklistRows, clientRequestId }) {
  const safeType = ALLOWED_TYPES.has(type) ? type : 'Note';
  return {
    title: String(title || '').trim(),
    body: String(body || '').trim() || null,
    type: safeType,
    priority: priority || 'Normal',
    reminderAtUtc: safeType === 'Reminder' || reminderLocal ? toIstIso(reminderLocal) : null,
    colorKey: colorKey || null,
    isPinned: Boolean(isPinned),
    labels: parseLabels(labels),
    clientRequestId,
    checklistRows: safeType === 'Checklist'
      ? (checklistRows || []).map((row, index) => ({
          id: row.id ?? null,
          clientKey: row.clientKey || null,
          text: String(row.text || '').trim(),
          isDone: Boolean(row.isDone),
          sortOrder: index
        })).filter((row) => row.text.length > 0)
      : []
  };
}

export function initNotebookCreateEditor(board, view, options = {}) {
  const showGlobalError = options.showGlobalError || (() => {});
  const applyCounts = options.applyCounts || (() => {});
  let modal = null;
  let checklist = null;
  let isOpen = false;
  let isSubmitting = false;
  let isPinned = false;
  let clientRequestId = crypto.randomUUID();
  let colourPicker = null;

  function clearCreateQuery() {
    const url = new URL(location.href);
    url.searchParams.delete('mode');
    url.searchParams.delete('type');
    history.replaceState(history.state, '', url);
  }

  function getElements() {
    return {
      title: requireEditorElement(modal, EditorSelectors.title),
      body: requireEditorElement(modal, EditorSelectors.body),
      checklistRoot: requireEditorElement(modal, EditorSelectors.checklist),
      pin: requireEditorElement(modal, EditorSelectors.pin),
      detailsToggle: requireEditorElement(modal, '[data-notebook-create-details-toggle]'),
      details: requireEditorElement(modal, '[data-notebook-create-details]'),
      type: requireEditorElement(modal, '[data-create-type]'),
      reminderField: requireEditorElement(modal, '[data-create-reminder-field]'),
      reminder: requireEditorElement(modal, '[data-create-reminder]'),
      priority: requireEditorElement(modal, '[data-create-priority]'),
      colourPickerRoot: requireEditorElement(modal, '[data-notebook-colour-picker]'),
      labels: requireEditorElement(modal, '[data-create-labels]'),
      feedback: requireEditorElement(modal, '[data-notebook-create-feedback]'),
      submit: requireEditorElement(modal, '[data-notebook-create-submit]')
    };
  }

  function setFeedback(message = '', isError = false) {
    const { feedback } = getElements();
    feedback.textContent = message;
    feedback.hidden = !message;
    feedback.classList.toggle('is-error', isError);
  }

  function setDetailsExpanded(expanded) {
    const elements = getElements();
    const open = Boolean(expanded);
    elements.details.hidden = !open;
    elements.detailsToggle.setAttribute('aria-expanded', String(open));
    elements.detailsToggle.classList.toggle('is-expanded', open);
  }


  function applyType(type, { preserveDetails = false } = {}) {
    const elements = getElements();
    const ui = getCreateTypeUi(type);
    elements.type.value = ui.type;
    elements.checklistRoot.hidden = !ui.showChecklist;
    elements.body.hidden = !ui.showBody;
    elements.reminderField.hidden = ui.type !== 'Reminder';
    elements.title.placeholder = ui.titlePlaceholder;
    elements.body.placeholder = ui.bodyPlaceholder;
    elements.submit.textContent = ui.actionLabel;
    modal.dataset.createType = ui.type.toLowerCase();
    if (ui.showChecklist && checklist.getRows().length === 0) checklist.setRows([{ text: '' }]);
    if (!preserveDetails || ui.openDetails) setDetailsExpanded(ui.openDetails);
  }

  function reset(type = 'Note') {
    const elements = getElements();
    elements.title.value = '';
    elements.body.value = '';
    elements.reminder.value = '';
    elements.priority.value = 'Normal';
    elements.labels.value = '';
    checklist.clear();
    isPinned = false;
    clientRequestId = crypto.randomUUID();
    elements.pin.classList.remove('is-active');
    elements.pin.setAttribute('aria-label', 'Pin item');
    colourPicker?.setValue('');
    applyNotebookSurfaceColour(modal.querySelector('.notebook-modal__dialog'), '');
    setFeedback('');
    applyType(type);
  }

  function close() {
    if (!modal) return;
    modal.hidden = true;
    modal.classList.remove('is-create-mode');
    document.body.classList.remove('notebook-modal-open');
    isOpen = false;
    clearCreateQuery();
  }

  async function submit() {
    if (isSubmitting) return;
    const elements = getElements();
    const payload = buildCreatePayload({
      type: elements.type.value,
      title: elements.title.value,
      body: elements.body.value,
      reminderLocal: elements.reminder.value,
      priority: elements.priority.value,
      colorKey: colourPicker?.getValue() || null,
      labels: elements.labels.value,
      isPinned,
      checklistRows: checklist.getRows(),
      clientRequestId
    });

    if (!payload.title && !payload.body && payload.checklistRows.length === 0) {
      setFeedback('Add a title, note or checklist item before creating.', true);
      elements.title.focus();
      return;
    }
    if (payload.type === 'Reminder' && !payload.reminderAtUtc) {
      setFeedback('Choose a reminder date and time.', true);
      setDetailsExpanded(true);
      elements.reminder.focus();
      return;
    }

    isSubmitting = true;
    elements.submit.disabled = true;
    setFeedback('Creating…');
    try {
      const response = await NotebookApi.createItem(payload);
      if (!response?.item) {
        throw new NotebookApiError('The create response did not contain the new notebook item.', { code: 'notebook_invalid_mutation_response' });
      }
      await reconcileMutation({
        response,
        board,
        view: view || 'home',
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts,
        preservePosition: false,
        prepend: true,
        showGlobalError,
        renderFailureMessage: 'The item was created, but its card could not be rendered. Reload the page.',
        reconcileFailureMessage: 'The item was created, but the board could not refresh. Reload the page.'
      });
      close();
      reset('Note');
    } catch (error) {
      setFeedback(error.message || 'Unable to create the notebook item.', true);
    } finally {
      isSubmitting = false;
      elements.submit.disabled = false;
    }
  }

  function build() {
    modal = cloneNotebookEditorTemplate(document);
    modal.classList.add('is-create-mode');
    document.body.appendChild(modal);
    const elements = getElements();
    checklist = createChecklistEditor(elements.checklistRoot);
    colourPicker = initNotebookColourPicker(elements.colourPickerRoot, {
      value: '',
      onSelect: (value) => {
        applyNotebookSurfaceColour(modal.querySelector('.notebook-modal__dialog'), value);
      }
    });

    elements.detailsToggle.hidden = false;
    elements.submit.hidden = false;
    modal.querySelector('[data-notebook-save-state]')?.closest('.notebook-save-feedback')?.setAttribute('hidden', '');
    modal.querySelector('[data-notebook-conflict]')?.setAttribute('hidden', '');

    elements.type.addEventListener('change', () => applyType(elements.type.value));
    elements.detailsToggle.addEventListener('click', () => setDetailsExpanded(elements.detailsToggle.getAttribute('aria-expanded') !== 'true'));
    elements.pin.addEventListener('click', () => {
      if (isSubmitting) return;
      isPinned = !isPinned;
      elements.pin.classList.toggle('is-active', isPinned);
      elements.pin.setAttribute('aria-label', isPinned ? 'Unpin item' : 'Pin item');
    });
    elements.submit.addEventListener('click', submit);
    modal.addEventListener('click', (event) => {
      if (event.target.closest('[data-close]')) close();
    });
  }

  function open(type = 'Note') {
    if (!modal) build();
    reset(type);
    modal.hidden = false;
    modal.classList.add('is-create-mode');
    document.body.classList.add('notebook-modal-open');
    isOpen = true;
    const elements = getElements();
    queueMicrotask(() => elements.title.focus());
  }

  return { open, close, isOpen: () => isOpen };
}
