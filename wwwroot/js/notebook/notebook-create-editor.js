import { NotebookApi, NotebookApiError } from './notebook-api.js';
import { createChecklistEditor } from './notebook-checklist-editor.js';
import { reconcileMutation } from './notebook-reconcile.js';
import { cloneNotebookEditorTemplate, requireEditorElement, EditorSelectors } from './notebook-editor.js';
import { initNotebookColourPicker, applyNotebookSurfaceColour } from './notebook-colour-picker.js';
import { initNotebookLabelPicker } from './notebook-label-picker.js';
import { confirmNotebookAction } from './notebook-confirm-dialog.js';
import { createNotebookCreateDraftStore, hasMeaningfulCreateDraft } from './notebook-create-draft.js';
import { createReminderScheduler, isFutureIstSchedule, toIstIsoFromParts } from './notebook-reminder-scheduler.js';

const ALLOWED_TYPES = new Set(['Note', 'Checklist', 'Reminder']);
const DRAFT_SAVE_DELAY_MS = 300;
const FOCUSABLE_SELECTOR = 'a[href],button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])';

export function toIstIso(localValue) {
  const value = String(localValue || '').trim();
  if (!value) return null;
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})$/.exec(value);
  return match ? toIstIsoFromParts(match[1], match[2]) : null;
}

export function parseLabels(value) {
  const seen = new Set();
  const source = Array.isArray(value) ? value : String(value || '').split(',');
  return source
    .map((label) => String(label || '').trim().replace(/^#+/, '').trim())
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
  const names = { Note: 'note', Checklist: 'checklist', Reminder: 'reminder' };
  return {
    type: safeType,
    actionLabel: `Create ${names[safeType]}`,
    titlePlaceholder: safeType === 'Reminder' ? 'Reminder title' : 'Title',
    bodyPlaceholder: safeType === 'Reminder' ? 'Add notes…' : 'Take a note…',
    showChecklist: safeType === 'Checklist',
    showBody: safeType !== 'Checklist',
    showReminderScheduler: safeType === 'Reminder',
    openDetails: safeType === 'Reminder'
  };
}

export function buildCreatePayload({
  type,
  title,
  body,
  reminderDate,
  reminderTime,
  reminderLocal,
  priority,
  colorKey,
  labels,
  isPinned,
  checklistRows,
  clientRequestId
}) {
  const safeType = ALLOWED_TYPES.has(type) ? type : 'Note';
  const reminderAtUtc = safeType === 'Reminder'
    ? (toIstIsoFromParts(reminderDate, reminderTime) || toIstIso(reminderLocal))
    : null;
  return {
    title: String(title || '').trim(),
    body: String(body || '').trim() || null,
    type: safeType,
    priority: priority || 'Normal',
    reminderAtUtc,
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
  const showToast = options.showToast || (() => {});
  const applyCounts = options.applyCounts || (() => {});
  const shell = options.shell || document.querySelector('.notebook-shell');
  const nowProvider = options.nowProvider || (() => new Date());
  const draftStore = createNotebookCreateDraftStore({
    storage: options.storage || globalThis.sessionStorage,
    userId: shell?.dataset?.currentUserId,
    nowProvider
  });

  let modal = null;
  let checklist = null;
  let scheduler = null;
  let isOpen = false;
  let isSubmitting = false;
  let isPinned = false;
  let clientRequestId = crypto.randomUUID();
  let colourPicker = null;
  let labelPicker = null;
  let activeDraftType = 'Note';
  let draftTimer = null;
  let suppressDraftEvents = false;
  let trigger = null;
  let directReminderMode = false;

  function clearCreateQuery() {
    const url = new URL(location.href);
    url.searchParams.delete('mode');
    url.searchParams.delete('type');
    history.replaceState(history.state, '', url);
  }

  function getElements() {
    return {
      dialog: requireEditorElement(modal, '.notebook-modal__dialog'),
      title: requireEditorElement(modal, EditorSelectors.title),
      body: requireEditorElement(modal, EditorSelectors.body),
      checklistRoot: requireEditorElement(modal, EditorSelectors.checklist),
      pin: requireEditorElement(modal, EditorSelectors.pin),
      detailsToggle: requireEditorElement(modal, '[data-notebook-create-details-toggle]'),
      details: requireEditorElement(modal, '[data-notebook-create-details]'),
      typeField: requireEditorElement(modal, '[data-create-type-field]'),
      type: requireEditorElement(modal, '[data-create-type]'),
      schedulerRoot: requireEditorElement(modal, '[data-reminder-scheduler]'),
      priority: requireEditorElement(modal, '[data-create-priority]'),
      colourPickerRoot: requireEditorElement(modal, '[data-notebook-colour-picker]'),
      labelPickerRoot: requireEditorElement(modal, '[data-notebook-label-picker]'),
      feedback: requireEditorElement(modal, '[data-notebook-create-feedback]'),
      submit: requireEditorElement(modal, '[data-notebook-create-submit]'),
      discard: requireEditorElement(modal, '[data-notebook-create-discard]'),
      draftStatus: requireEditorElement(modal, '[data-notebook-create-draft-status]')
    };
  }

  function setFeedback(message = '', isError = false) {
    const { feedback } = getElements();
    feedback.textContent = message;
    feedback.hidden = !message;
    feedback.classList.toggle('is-error', isError);
  }

  function setDraftStatus(message = '') {
    const { draftStatus } = getElements();
    draftStatus.textContent = message;
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
    elements.schedulerRoot.hidden = !ui.showReminderScheduler;
    elements.typeField.hidden = directReminderMode;
    elements.detailsToggle.hidden = ui.showReminderScheduler;
    elements.title.placeholder = ui.titlePlaceholder;
    elements.body.placeholder = ui.bodyPlaceholder;
    elements.submit.textContent = ui.actionLabel;
    modal.dataset.createType = ui.type.toLowerCase();
    modal.dataset.directReminder = String(directReminderMode);
    if (ui.showChecklist && checklist.getRows().length === 0) checklist.setRows([{ text: '' }]);
    if (ui.showReminderScheduler && !scheduler.getValue().date) scheduler.setDefault();
    if (!preserveDetails || ui.openDetails) setDetailsExpanded(ui.openDetails);
  }

  function readDraft() {
    const elements = getElements();
    const schedule = scheduler.getValue();
    return {
      type: elements.type.value,
      title: elements.title.value,
      body: elements.body.value,
      reminderDate: schedule.date,
      reminderTime: schedule.time,
      scheduleTouched: schedule.touched,
      priority: elements.priority.value,
      colorKey: colourPicker?.getValue() || null,
      labels: labelPicker?.getValue() || [],
      isPinned,
      checklistRows: checklist.getRows(),
      clientRequestId
    };
  }

  function updateDraftControls(draft = readDraft()) {
    const meaningful = hasMeaningfulCreateDraft(draft);
    const { discard } = getElements();
    discard.hidden = !meaningful;
    return meaningful;
  }

  function clearDraftTimer() {
    if (draftTimer) window.clearTimeout(draftTimer);
    draftTimer = null;
  }

  function persistDraftNow(type = activeDraftType) {
    clearDraftTimer();
    if (!modal || suppressDraftEvents) return false;
    const draft = readDraft();
    const meaningful = updateDraftControls(draft);
    if (meaningful) draftStore.save(type, draft);
    else draftStore.remove(type);
    return meaningful;
  }

  function scheduleDraftSave() {
    if (suppressDraftEvents || !isOpen || isSubmitting) return;
    updateDraftControls();
    clearDraftTimer();
    draftTimer = window.setTimeout(() => {
      persistDraftNow();
      setDraftStatus('Draft saved');
      window.setTimeout(() => {
        if (isOpen) setDraftStatus('');
      }, 1200);
    }, DRAFT_SAVE_DELAY_MS);
  }

  function reset(type = 'Note') {
    const elements = getElements();
    suppressDraftEvents = true;
    activeDraftType = getCreateTypeUi(type).type;
    elements.title.value = '';
    elements.body.value = '';
    elements.priority.value = 'Normal';
    labelPicker?.setValue([]);
    checklist.clear();
    scheduler.clear();
    isPinned = false;
    clientRequestId = crypto.randomUUID();
    elements.pin.classList.remove('is-active');
    elements.pin.setAttribute('aria-label', 'Pin item');
    colourPicker?.setValue('');
    applyNotebookSurfaceColour(elements.dialog, '');
    setFeedback('');
    setDraftStatus('');
    applyType(activeDraftType);
    suppressDraftEvents = false;
    updateDraftControls();
  }

  function applyDraft(draft) {
    if (!draft) return;
    const elements = getElements();
    suppressDraftEvents = true;
    activeDraftType = getCreateTypeUi(draft.type).type;
    elements.title.value = draft.title || '';
    elements.body.value = draft.body || '';
    elements.priority.value = draft.priority || 'Normal';
    labelPicker?.setValue(Array.isArray(draft.labels) ? draft.labels : []);
    checklist.setRows(Array.isArray(draft.checklistRows) ? draft.checklistRows : []);
    scheduler.setValue(
      { date: draft.reminderDate, time: draft.reminderTime },
      { markTouched: Boolean(draft.scheduleTouched), validate: draft.type === 'Reminder' }
    );
    isPinned = Boolean(draft.isPinned);
    clientRequestId = draft.clientRequestId || crypto.randomUUID();
    elements.pin.classList.toggle('is-active', isPinned);
    elements.pin.setAttribute('aria-label', isPinned ? 'Unpin item' : 'Pin item');
    colourPicker?.setValue(draft.colorKey || '');
    applyNotebookSurfaceColour(elements.dialog, draft.colorKey || '');
    applyType(activeDraftType);
    suppressDraftEvents = false;
    updateDraftControls();
  }

  function setBackgroundInert(inert) {
    if (shell) shell.inert = inert;
    if (modal) modal.inert = false;
    document.body.classList.toggle('notebook-modal-open', inert);
  }

  function trapFocus(event) {
    if (event.key !== 'Tab' || modal.hidden) return;
    const focusable = [...modal.querySelectorAll(FOCUSABLE_SELECTOR)].filter((element) => !element.hidden && element.offsetParent !== null);
    if (!focusable.length) {
      event.preventDefault();
      getElements().dialog.focus();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  function closeInternal() {
    if (!modal) return;
    clearDraftTimer();
    modal.hidden = true;
    modal.classList.remove('is-create-mode');
    setBackgroundInert(false);
    isOpen = false;
    clearCreateQuery();
    trigger?.focus?.();
    trigger = null;
  }

  async function requestClose() {
    if (!modal || !isOpen || isSubmitting) return;
    const meaningful = persistDraftNow();
    closeInternal();
    if (meaningful) showToast({ message: `${activeDraftType} draft saved.`, tone: 'neutral' });
  }

  async function discardDraft() {
    const draft = readDraft();
    if (hasMeaningfulCreateDraft(draft)) {
      const confirmed = await confirmNotebookAction({
        title: 'Discard this draft?',
        message: 'The content entered in this new notebook item will be removed.',
        confirmText: 'Discard draft',
        tone: 'danger',
        backdropCancels: false
      });
      if (!confirmed) return;
    }
    draftStore.remove(activeDraftType);
    reset(activeDraftType);
    closeInternal();
    showToast({ message: 'Draft discarded.', tone: 'neutral' });
  }

  async function submit() {
    if (isSubmitting) return;
    const elements = getElements();
    const schedule = scheduler.getValue();
    const payload = buildCreatePayload({
      type: elements.type.value,
      title: elements.title.value,
      body: elements.body.value,
      reminderDate: schedule.date,
      reminderTime: schedule.time,
      priority: elements.priority.value,
      colorKey: colourPicker?.getValue() || null,
      labels: labelPicker?.getValue() || [],
      isPinned,
      checklistRows: checklist.getRows(),
      clientRequestId
    });

    if (!payload.title && !payload.body && payload.checklistRows.length === 0) {
      setFeedback('Add a title, note or checklist item before creating.', true);
      elements.title.focus();
      return;
    }
    if (payload.type === 'Reminder') {
      setDetailsExpanded(true);
      const scheduleValidation = scheduler.validate({ focus: true });
      if (!scheduleValidation.valid || !payload.reminderAtUtc || !isFutureIstSchedule(schedule.date, schedule.time, nowProvider())) {
        setFeedback(scheduleValidation.message || 'Choose a future reminder date and time.', true);
        return;
      }
    }

    isSubmitting = true;
    elements.submit.disabled = true;
    elements.discard.disabled = true;
    setFeedback('Creating…');
    setDraftStatus('');
    persistDraftNow();
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
      draftStore.remove(activeDraftType);
      closeInternal();
      reset('Note');
      showToast({ message: `${payload.type} created.`, tone: 'success' });
    } catch (error) {
      setFeedback(error.message || 'Unable to create the notebook item.', true);
      persistDraftNow();
    } finally {
      isSubmitting = false;
      elements.submit.disabled = false;
      elements.discard.disabled = false;
    }
  }

  function build() {
    modal = cloneNotebookEditorTemplate(document);
    modal.classList.add('is-create-mode');
    document.body.appendChild(modal);
    const elements = getElements();
    checklist = createChecklistEditor(elements.checklistRoot, { onChange: scheduleDraftSave });
    scheduler = createReminderScheduler(elements.schedulerRoot, { nowProvider, onChange: scheduleDraftSave });
    colourPicker = initNotebookColourPicker(elements.colourPickerRoot, {
      value: '',
      onSelect: (value) => {
        applyNotebookSurfaceColour(elements.dialog, value);
        scheduleDraftSave();
      }
    });
    labelPicker = initNotebookLabelPicker(elements.labelPickerRoot, {
      value: [],
      onChange: scheduleDraftSave
    });

    elements.detailsToggle.hidden = false;
    elements.submit.hidden = false;
    modal.querySelector('[data-notebook-save-state]')?.closest('.notebook-save-feedback')?.setAttribute('hidden', '');
    modal.querySelector('[data-notebook-conflict]')?.setAttribute('hidden', '');

    elements.type.addEventListener('change', () => {
      const previousType = activeDraftType;
      activeDraftType = getCreateTypeUi(elements.type.value).type;
      draftStore.remove(previousType);
      applyType(activeDraftType, { preserveDetails: true });
      scheduleDraftSave();
    });
    elements.detailsToggle.addEventListener('click', () => setDetailsExpanded(elements.detailsToggle.getAttribute('aria-expanded') !== 'true'));
    elements.pin.addEventListener('click', () => {
      if (isSubmitting) return;
      isPinned = !isPinned;
      elements.pin.classList.toggle('is-active', isPinned);
      elements.pin.setAttribute('aria-label', isPinned ? 'Unpin item' : 'Pin item');
      scheduleDraftSave();
    });
    [elements.title, elements.body, elements.priority].forEach((element) => {
      element.addEventListener('input', scheduleDraftSave);
      element.addEventListener('change', scheduleDraftSave);
    });
    elements.submit.addEventListener('click', submit);
    elements.discard.addEventListener('click', discardDraft);
    modal.addEventListener('click', (event) => {
      const closeTarget = event.target.closest('[data-close]');
      if (!closeTarget) return;
      if (closeTarget.classList.contains('notebook-modal__backdrop')) {
        event.preventDefault();
        elements.dialog.focus();
        return;
      }
      requestClose();
    });
    modal.addEventListener('keydown', (event) => {
      trapFocus(event);
      if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
        event.preventDefault();
        submit();
      }
    });
  }

  function open(type = 'Note') {
    if (!modal) build();
    trigger = document.activeElement;
    const safeType = getCreateTypeUi(type).type;
    directReminderMode = safeType === 'Reminder';
    reset(safeType);
    const restored = draftStore.load(safeType);
    if (restored) applyDraft(restored);
    modal.hidden = false;
    modal.classList.add('is-create-mode');
    setBackgroundInert(true);
    isOpen = true;
    if (restored) {
      const restoredSchedule = scheduler.getValue();
      const reminderNeedsAttention = safeType === 'Reminder'
        && !isFutureIstSchedule(restoredSchedule.date, restoredSchedule.time, nowProvider());
      setFeedback(
        reminderNeedsAttention ? 'Draft restored. Choose a future reminder date and time.' : 'Draft restored.',
        reminderNeedsAttention
      );
      showToast({ message: `${safeType} draft restored.`, tone: 'neutral' });
    }
    const elements = getElements();
    queueMicrotask(() => elements.title.focus());
  }

  return {
    open,
    requestClose,
    close: requestClose,
    discardDraft,
    isOpen: () => isOpen
  };
}
