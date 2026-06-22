import { NotebookApi, NotebookApiError } from './notebook-api.js';
import { createAutosave } from './notebook-autosave.js';
import { createChecklistEditor } from './notebook-checklist-editor.js';
import { reconcileMutation, requireMutationItem } from './notebook-reconcile.js';
import { getFirstValidationMessage, getValidationMessages } from './notebook-errors.js';

export const ConflictType = Object.freeze({
  StaleDraft: 'stale-draft',
  ExternalUpdate: 'external-update',
  VersionConflict: 'version-conflict'
});

export const SaveState = Object.freeze({
  Idle: 'idle',
  Saving: 'saving',
  Saved: 'saved',
  Error: 'error'
});

export function shouldTreatDraftAsConflict(draft, currentItem) {
  return Boolean(draft?.sourceVersion && currentItem?.version && draft.sourceVersion !== currentItem.version);
}

export function serialiseNotebookContent({ title = '', body = '', type = 'Note', checklistRows = [] } = {}) {
  const sections = [String(title).trim(), String(body).trim()].filter(Boolean);
  if (type === 'Checklist') {
    const rows = (Array.isArray(checklistRows) ? checklistRows : [])
      .filter((row) => String(row?.text ?? '').trim().length > 0)
      .map((row) => `${row?.isDone ? '☑' : '☐'} ${String(row.text).trim()}`);
    if (rows.length) sections.push(rows.join('\n'));
  }
  return sections.join('\n\n');
}

// SECTION: Notebook modal editor lifecycle and coordinated mutations
export function initNotebookEditor(board, view, options = {}) {
  let modal;
  let item;
  let autosave;
  let checklist;
  let trigger;
  let openedByPushState = false;
  let currentSaveError = null;
  let draftSourceVersion = null;
  let blockedByValidation = false;
  let lastValidationFingerprint = null;

  const conflictState = {
    active: false,
    type: null,
    pendingServerItem: null,
    localDraft: null,
    message: null
  };

  const shell = options.shell || document.querySelector('.notebook-shell');
  const dirtyState = { title: false, body: false, checklist: false };
  const editRevision = { title: 0, body: 0, checklist: 0 };
  const buildNoteUrl = (id) => {
    const url = new URL(location.href);
    id ? url.searchParams.set('note', id) : url.searchParams.delete('note');
    return url;
  };
  const focusableSelector = 'a[href],button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])';

  function setSaveStatus(text, state = SaveState.Idle) {
    const el = modal?.querySelector('[data-notebook-save-state]');
    if (el) {
      el.textContent = text || '';
      el.dataset.state = state;
    }

    const retry = modal?.querySelector('[data-notebook-retry]');
    const reloadApplication = modal?.querySelector('[data-notebook-reload-application]');
    const discard = modal?.querySelector('[data-modal-discard]');
    const signIn = modal?.querySelector('[data-notebook-sign-in]');
    const copy = modal?.querySelector('[data-notebook-copy-unsaved]');

    if (retry) retry.hidden = !['network', 'server', 'error'].includes(state);
    if (reloadApplication) reloadApplication.hidden = state !== 'client-version';
    if (discard) discard.hidden = !['network', 'server', 'error', 'client-version', 'session-expired', 'forbidden'].includes(state);
    if (signIn) signIn.hidden = state !== 'session-expired';
    if (copy) copy.hidden = !['session-expired', 'forbidden', 'network', 'server', 'error'].includes(state);
  }

  function renderConflictState() {
    const panel = modal?.querySelector('[data-notebook-conflict]');
    const message = modal?.querySelector('[data-notebook-conflict-message]');
    const pin = modal?.querySelector('[data-modal-pin]');
    if (!panel) return;

    panel.hidden = !conflictState.active;
    if (message) message.textContent = conflictState.message || 'This note changed elsewhere.';
    if (pin) pin.disabled = conflictState.active;

    if (conflictState.active) setSaveStatus('', SaveState.Idle);
  }

  function clearConflictState() {
    conflictState.active = false;
    conflictState.type = null;
    conflictState.pendingServerItem = null;
    conflictState.localDraft = null;
    conflictState.message = null;
    renderConflictState();
  }

  function isConflictBlocked() {
    return conflictState.active;
  }

  function activateConflict({ type, pendingServerItem = null, localDraft = null, message }) {
    conflictState.active = true;
    conflictState.type = type;
    conflictState.pendingServerItem = pendingServerItem;
    conflictState.localDraft = localDraft;
    conflictState.message = message || 'This note changed elsewhere.';
    autosave?.cancel?.();
    preserveUnsavedDraft();
    renderConflictState();
  }

  function buildCurrentPayload() {
    return buildUpdatePayload({
      title: modal.querySelector('[data-modal-title]').value,
      body: modal.querySelector('[data-modal-body]').value,
      type: item.type,
      checklistRows: item.type === 'Checklist' ? checklist.getRows() : []
    });
  }

  function markChanged(field) {
    editRevision[field] += 1;
    dirtyState[field] = true;
    if (!draftSourceVersion) draftSourceVersion = item?.version ?? null;
  }

  function resetDirtyState() {
    dirtyState.title = false;
    dirtyState.body = false;
    dirtyState.checklist = false;
  }

  function resetEditRevision() {
    editRevision.title = 0;
    editRevision.body = 0;
    editRevision.checklist = 0;
  }

  function applySubmittedRevision(submittedRevision = {}) {
    dirtyState.title = editRevision.title !== submittedRevision.title;
    dirtyState.body = editRevision.body !== submittedRevision.body;
    dirtyState.checklist = editRevision.checklist !== submittedRevision.checklist;
  }

  function hasDirtyChanges() {
    return dirtyState.title || dirtyState.body || dirtyState.checklist;
  }

  function scheduleAutosave() {
    if (isConflictBlocked()) {
      preserveUnsavedDraft();
      return;
    }

    const nextPayload = buildCurrentPayload();
    const nextFingerprint = validationFingerprint(nextPayload);
    if (blockedByValidation && nextFingerprint === lastValidationFingerprint) return;
    if (blockedByValidation) clearValidationBlock();
    autosave?.schedule(nextPayload);
  }

  function clearValidationBlock() {
    blockedByValidation = false;
    lastValidationFingerprint = null;
    renderValidationErrors([]);
  }

  function configureAutosave() {
    autosave?.stop();
    autosave = createAutosave({
      save: saveEditorPayload,
      onSaving: () => {
        if (!isConflictBlocked()) setSaveStatus('Saving…', SaveState.Saving);
      },
      onPersisted: applyPersistedResponse,
      onSaveError: handleEditorError,
      onReconcileError: handleReconcileError
    });
  }

  function renderPin() {
    const pin = modal.querySelector('[data-modal-pin]');
    pin?.classList.toggle('is-active', Boolean(item?.isPinned));
    if (pin) {
      pin.setAttribute('aria-label', item?.isPinned ? 'Unpin note' : 'Pin note');
      pin.disabled = conflictState.active;
    }
  }

  function applyAuthoritativeItem(updated) {
    item = updated;
    modal.querySelector('[data-modal-title]').value = updated.title || '';
    modal.querySelector('[data-modal-body]').value = updated.body || '';
    modal.querySelector('[data-modal-checklist]').hidden = updated.type !== 'Checklist';
    if (updated.type === 'Checklist') checklist.reconcileRows(updated.checklistRows || []);
    else checklist.setRows([]);
    resetDirtyState();
    resetEditRevision();
    draftSourceVersion = updated.version ?? null;
    clearValidationBlock();
    renderPin();
  }

  function renderMode() {
    applyAuthoritativeItem(item);
    clearConflictState();
    currentSaveError = null;
    setSaveStatus('', SaveState.Idle);
  }

  function build() {
    modal = document.createElement('div');
    modal.className = 'notebook-modal';
    modal.hidden = true;
    modal.setAttribute('role', 'dialog');
    modal.setAttribute('aria-modal', 'true');
    modal.setAttribute('aria-labelledby', 'notebook-modal-title');
    modal.innerHTML = `
      <div class="notebook-modal__backdrop" data-close></div>
      <section class="notebook-modal__dialog">
        <header>
          <input id="notebook-modal-title" data-modal-title class="notebook-modal__title" maxlength="220">
          <button type="button" class="notebook-action-icon" data-modal-pin aria-label="Pin note"><i class="bi bi-pin-angle"></i></button>
        </header>
        <textarea data-modal-body class="notebook-modal__body" maxlength="20000" placeholder="Take a note…"></textarea>
        <div class="notebook-editor__validation" data-notebook-validation-summary role="alert" hidden></div>
        <div data-modal-checklist class="notebook-checklist-editor" hidden></div>
        <div class="notebook-save-feedback">
          <span class="notebook-modal__save-state" data-notebook-save-state aria-live="polite"></span>
          <button type="button" data-notebook-retry hidden>Retry</button>
          <button type="button" data-notebook-reload-application hidden>Reload application</button>
          <button type="button" data-notebook-sign-in hidden>Sign in again</button>
          <button type="button" data-notebook-copy-unsaved hidden>Copy note text</button>
          <button type="button" data-modal-discard hidden>Discard changes</button>
        </div>
        <section class="notebook-conflict" data-notebook-conflict hidden role="status" aria-live="polite">
          <div class="notebook-conflict__message" data-notebook-conflict-message></div>
          <div class="notebook-conflict__actions">
            <button type="button" class="notebook-conflict__action" data-notebook-use-local>Use my changes</button>
            <button type="button" class="notebook-conflict__action" data-notebook-reload-latest>Reload latest</button>
            <button type="button" class="notebook-conflict__action notebook-conflict__action--secondary" data-notebook-copy-local>Copy my changes</button>
          </div>
        </section>
        <footer><button type="button" class="btn btn-sm btn-link" data-close aria-label="Close note editor">Close</button></footer>
      </section>`;

    document.body.appendChild(modal);
    checklist = createChecklistEditor(modal.querySelector('[data-modal-checklist]'), {
      onChange: () => {
        markChanged('checklist');
        scheduleAutosave();
      }
    });

    modal.addEventListener('click', (event) => {
      if (event.target.matches('[data-close]')) requestClose();
    });
    modal.addEventListener('keydown', trapFocus);
    modal.querySelector('[data-modal-title]').addEventListener('input', () => {
      markChanged('title');
      scheduleAutosave();
    });
    modal.querySelector('[data-modal-body]').addEventListener('input', () => {
      markChanged('body');
      scheduleAutosave();
    });
    modal.querySelector('[data-modal-pin]').addEventListener('click', pinItem);
    modal.querySelector('[data-notebook-retry]')?.addEventListener('click', retrySave);
    modal.querySelector('[data-notebook-reload-application]')?.addEventListener('click', () => window.location.reload());
    modal.querySelector('[data-notebook-sign-in]')?.addEventListener('click', signInAgain);
    modal.querySelector('[data-notebook-copy-unsaved]')?.addEventListener('click', copyUnsavedContent);
    modal.querySelector('[data-modal-discard]')?.addEventListener('click', discardChangesAndClose);
    modal.querySelector('[data-notebook-use-local]')?.addEventListener('click', useMyChanges);
    modal.querySelector('[data-notebook-reload-latest]')?.addEventListener('click', reloadLatest);
    modal.querySelector('[data-notebook-copy-local]')?.addEventListener('click', copyLocalChanges);
  }

  async function saveEditorPayload(data) {
    const submittedRows = item.type === 'Checklist' ? structuredCloneSafe(data.checklistRows || []) : [];
    const submittedRevision = { ...editRevision };
    const requestPayload = { ...data, version: item.version };
    assertValidVersion(requestPayload.version);
    const response = item.type === 'Checklist'
      ? await NotebookApi.updateChecklist(item.id, requestPayload)
      : await NotebookApi.updateContent(item.id, requestPayload);
    return { response, submittedRows, submittedRevision };
  }

  async function applyPersistedResponse(saveResult) {
    const response = saveResult?.response ?? saveResult;
    const submittedRows = Array.isArray(saveResult?.submittedRows) ? saveResult.submittedRows : [];
    const submittedRevision = saveResult?.submittedRevision ?? { ...editRevision };
    item = requireMutationItem(response);
    if (item.type === 'Checklist' && Array.isArray(item.checklistRows)) {
      checklist.reconcileRows(item.checklistRows, submittedRows);
    }
    applySubmittedRevision(submittedRevision);
    clearValidationBlock();
    draftSourceVersion = item.version ?? draftSourceVersion;
    if (hasDirtyChanges() || conflictState.active) preserveUnsavedDraft();
    else clearStoredDraft(item?.id);
    if (conflictState.active) {
      conflictState.pendingServerItem = item;
      renderConflictState();
    } else {
      setSaveStatus('Saved', SaveState.Saved);
    }
    await reconcileMutation({
      response,
      board,
      view,
      getCardHtml: NotebookApi.getCardHtml,
      applyCounts: options.applyCounts,
      preservePosition: true,
      showGlobalError: options.showGlobalError,
      renderFailureMessage: 'The note was saved, but its card could not refresh. Reload the page.',
      reconcileFailureMessage: 'The note was saved, but the board could not refresh. Reload the page.'
    });
  }

  function handleReconcileError() {
    options.showGlobalError?.('The note was saved, but the board could not refresh. Reload the page.');
    setSaveStatus('Saved', SaveState.Saved);
  }

  function handleEditorError(error) {
    currentSaveError = classifyNotebookSaveError(error);

    if (currentSaveError.kind === 'conflict') {
      activateConflict({
        type: ConflictType.VersionConflict,
        message: 'This note changed elsewhere.'
      });
    } else {
      setSaveStatus(currentSaveError.message, currentSaveError.kind);
    }

    if (currentSaveError.kind === 'validation') {
      renderValidationErrors(currentSaveError.validationErrors);
      const submittedPayload = buildCurrentPayload();
      blockedByValidation = true;
      lastValidationFingerprint = validationFingerprint(submittedPayload);
      autosave?.cancel?.();
    }

    if (isDevelopment()) {
      const submittedPayload = buildCurrentPayload();
      console.error('Notebook update failed', {
        noteId: item?.id,
        status: error?.status,
        code: error?.code,
        errors: error?.errors,
        responseText: error?.responseText,
        payload: describeUpdatePayload(submittedPayload)
      });
    }

    return { retryable: isRetryableSaveError(error) && !isConflictBlocked() };
  }

  function isDevelopment() {
    return document.documentElement.dataset.environment === 'Development' || location.hostname === 'localhost';
  }

  async function disposeCurrentItem() {
    if (!item || !autosave) return;
    if (isConflictBlocked()) {
      preserveUnsavedDraft();
      autosave.cancel();
    } else {
      await autosave.flush();
    }
    autosave.stop();
    autosave = null;
  }

  function setBackgroundInert(inert) {
    if (shell) shell.inert = inert;
    if (modal) modal.inert = false;
    document.body.classList.toggle('notebook-modal-open', inert);
  }

  function trapFocus(event) {
    if (event.key !== 'Tab' || modal.hidden) return;
    const focusable = [...modal.querySelectorAll(focusableSelector)].filter((element) => element.offsetParent !== null);
    if (!focusable.length) {
      event.preventDefault();
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

  function preserveUnsavedDraft() {
    if (!item?.id || !modal) return;
    sessionStorage.setItem(`notebook-draft:${item.id}`, JSON.stringify({
      itemId: item.id,
      type: item.type,
      title: modal.querySelector('[data-modal-title]').value,
      body: modal.querySelector('[data-modal-body]').value,
      checklistRows: item.type === 'Checklist' ? checklist.getRows() : [],
      sourceVersion: draftSourceVersion || item.version,
      savedAtUtc: new Date().toISOString()
    }));
  }

  function clearStoredDraft(itemId) {
    if (itemId) sessionStorage.removeItem(`notebook-draft:${itemId}`);
  }

  function readStoredDraft(itemId) {
    const key = `notebook-draft:${itemId}`;
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    try {
      return JSON.parse(raw);
    } catch {
      sessionStorage.removeItem(key);
      return null;
    }
  }

  function restoreStoredDraftIfNeeded() {
    const storedDraft = readStoredDraft(item.id);
    if (!storedDraft) return;

    const differs = storedDraft.title !== item.title
      || storedDraft.body !== item.body
      || JSON.stringify(storedDraft.checklistRows || []) !== JSON.stringify(item.checklistRows || []);
    if (!differs) {
      clearStoredDraft(item.id);
      return;
    }

    const stale = shouldTreatDraftAsConflict(storedDraft, item);
    const prompt = stale
      ? 'A newer saved version exists. Restore your local changes for review?'
      : 'Restore your unsaved local draft for this note?';
    if (!window.confirm(prompt)) return;

    modal.querySelector('[data-modal-title]').value = storedDraft.title || '';
    modal.querySelector('[data-modal-body]').value = storedDraft.body || '';
    if (item.type === 'Checklist') checklist.setRows(storedDraft.checklistRows || []);
    draftSourceVersion = storedDraft.sourceVersion || item.version;
    markChanged('title');
    markChanged('body');
    if (item.type === 'Checklist') markChanged('checklist');

    if (stale) {
      activateConflict({
        type: ConflictType.StaleDraft,
        pendingServerItem: item,
        localDraft: storedDraft,
        message: 'A newer saved version exists.'
      });
    } else {
      scheduleAutosave();
    }
  }

  function signInAgain() {
    preserveUnsavedDraft();
    const returnUrl = window.location.pathname + window.location.search + window.location.hash;
    window.location.assign('/Identity/Account/Login?ReturnUrl=' + encodeURIComponent(returnUrl));
  }

  async function copyUnsavedContent() {
    await copyLocalChanges();
    setSaveStatus('Unsaved note text copied. Sign in again before saving.', currentSaveError?.kind || 'session-expired');
  }

  async function copyLocalChanges() {
    const text = serialiseNotebookContent({
      ...buildCurrentPayload(),
      type: item?.type
    });
    await navigator.clipboard.writeText(text);
    const copyButton = modal?.querySelector('[data-notebook-copy-local]');
    if (copyButton) {
      const original = copyButton.textContent;
      copyButton.textContent = 'Copied';
      window.setTimeout(() => { copyButton.textContent = original; }, 1500);
    }
  }

  async function retrySave() {
    if (isConflictBlocked()) return;
    const button = modal.querySelector('[data-notebook-retry]');
    button.disabled = true;
    try {
      clearValidationBlock();
      autosave.schedule(buildCurrentPayload());
      await autosave.flush();
    } finally {
      button.disabled = false;
    }
  }

  function closeEditor({ fromHistory = false } = {}) {
    const closedId = item?.id;
    autosave?.stop();
    autosave = null;
    modal.hidden = true;
    setBackgroundInert(false);
    item = null;
    currentSaveError = null;
    clearConflictState();
    clearValidationBlock();
    if (!fromHistory) {
      if (openedByPushState) history.back();
      else history.replaceState(history.state, '', buildNoteUrl(null));
    }
    (board.findCard(closedId) || trigger)?.focus?.();
  }

  async function discardChangesAndClose() {
    if (!window.confirm('Discard unsaved changes and close this note?')) return;
    const itemId = item?.id;
    autosave?.cancel?.();
    clearStoredDraft(itemId);
    resetDirtyState();
    closeEditor();
  }

  async function useMyChanges() {
    if (!conflictState.active || !item) return;
    if (!window.confirm('Save your current changes over the newer saved version?')) return;

    const button = modal.querySelector('[data-notebook-use-local]');
    button.disabled = true;
    try {
      // Always refresh the concurrency token immediately before the deliberate overwrite.
      const latest = await NotebookApi.getItem(item.id);
      item.version = latest.version;
      draftSourceVersion = latest.version;
      currentSaveError = null;
      clearConflictState();
      clearValidationBlock();
      preserveUnsavedDraft();
      autosave.schedule(buildCurrentPayload());
      await autosave.flush();
    } catch (error) {
      if (conflictState.active) {
        conflictState.message = error?.message || 'The latest saved version could not be loaded.';
        renderConflictState();
      } else {
        handleEditorError(error);
      }
    } finally {
      button.disabled = false;
    }
  }

  async function reloadLatest() {
    if (!item) return;
    if (hasDirtyChanges() && !window.confirm('Discard your unsaved changes and load the latest saved version?')) return;

    const button = modal.querySelector('[data-notebook-reload-latest]');
    button.disabled = true;
    try {
      const latest = await NotebookApi.getItem(item.id);
      applyAuthoritativeItem(latest);
      clearStoredDraft(item.id);
      clearConflictState();
      currentSaveError = null;
      setSaveStatus('Saved', SaveState.Saved);
    } catch (error) {
      if (conflictState.active) {
        conflictState.message = error?.message || 'Unable to load the latest saved version.';
        renderConflictState();
      } else {
        setSaveStatus(error?.message || 'Unable to reload the note.', SaveState.Error);
      }
    } finally {
      button.disabled = false;
    }
  }

  async function pinItem() {
    if (!item || isConflictBlocked()) return;
    const button = modal.querySelector('[data-modal-pin]');
    button.disabled = true;
    try {
      await autosave?.flush();
      const response = await NotebookApi.setPinned(item.id, !item.isPinned, item.version);
      item = requireMutationItem(response, 'The pin response did not contain the updated note.');
      draftSourceVersion = item.version;
      renderPin();
      await reconcileMutation({
        response,
        board,
        view,
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts: options.applyCounts,
        preservePosition: false,
        prepend: true,
        showGlobalError: options.showGlobalError,
        reconcileFailureMessage: `The note was ${item.isPinned ? 'pinned' : 'unpinned'}, but the board could not refresh. Reload the page.`
      });
      setSaveStatus('Saved', SaveState.Saved);
    } catch (error) {
      handleEditorError(error);
    } finally {
      button.disabled = conflictState.active;
    }
  }

  async function open(id, openOptions = {}) {
    if (!modal) build();
    if (item && item.id !== id) await disposeCurrentItem();
    trigger = document.activeElement;
    item = await NotebookApi.getItem(id);
    configureAutosave();
    renderMode();
    restoreStoredDraftIfNeeded();
    modal.hidden = false;
    setBackgroundInert(true);
    modal.querySelector('[data-modal-title]').focus();
    if (openOptions.pushHistory !== false) {
      openedByPushState = true;
      history.pushState({ ...(history.state || {}), notebookModal: true, notebookNoteId: id }, '', buildNoteUrl(id));
    } else {
      openedByPushState = false;
    }
  }

  async function requestClose({ fromHistory = false } = {}) {
    if (!item || !modal || modal.hidden) return;
    const closeButton = modal.querySelector('[data-close]:not(.notebook-modal__backdrop)');
    closeButton.disabled = true;
    try {
      if (isConflictBlocked()) {
        preserveUnsavedDraft();
        autosave?.cancel?.();
        closeEditor({ fromHistory });
        return;
      }

      await autosave?.flush();
      closeEditor({ fromHistory });
    } catch (error) {
      handleEditorError(error);
    } finally {
      closeButton.disabled = false;
    }
  }

  function syncExternalUpdate(updated) {
    if (!item || item.id !== updated.id) return;
    if (hasDirtyChanges() || autosave?.hasPending?.()) {
      activateConflict({
        type: ConflictType.ExternalUpdate,
        pendingServerItem: updated,
        message: 'This note changed elsewhere.'
      });
      return;
    }

    applyAuthoritativeItem(updated);
    clearStoredDraft(updated.id);
    setSaveStatus('', SaveState.Idle);
  }

  return {
    open,
    requestClose,
    isOpen: () => Boolean(item && modal && !modal.hidden),
    syncExternalUpdate
  };
}

// SECTION: Notebook content update payload validation and diagnostics
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function assertValidVersion(version) {
  if (typeof version !== 'string' || !guidPattern.test(version)) {
    throw new NotebookApiError('The note version is invalid. Reload the note and try again.', {
      status: 0,
      code: 'notebook_invalid_local_version'
    });
  }
}

export function buildUpdatePayload({ title, body, type = 'Note', checklistRows = [] }) {
  const payload = {
    title: String(title ?? '').trim(),
    body: String(body ?? '').trim()
  };

  if (type === 'Checklist') payload.checklistRows = Array.isArray(checklistRows) ? checklistRows : [];
  return payload;
}

export function structuredCloneSafe(value) {
  if (typeof structuredClone === 'function') return structuredClone(value);
  return JSON.parse(JSON.stringify(value));
}

export function validationFingerprint(payload) {
  return JSON.stringify({
    titleLength: typeof payload?.title === 'string' ? payload.title.length : null,
    bodyLength: typeof payload?.body === 'string' ? payload.body.length : null,
    type: payload?.type,
    priority: payload?.priority,
    reminderAtUtc: payload?.reminderAtUtc,
    labelsIsArray: Array.isArray(payload?.labels),
    checklistRowsIsArray: Array.isArray(payload?.checklistRows)
  });
}

export function describeUpdatePayload(payload) {
  return {
    titleType: typeof payload?.title,
    titleLength: typeof payload?.title === 'string' ? payload.title.length : null,
    bodyType: typeof payload?.body,
    bodyLength: typeof payload?.body === 'string' ? payload.body.length : null,
    typeValue: payload?.type,
    typeValueType: typeof payload?.type,
    priorityValue: payload?.priority,
    priorityValueType: typeof payload?.priority,
    reminderAtUtc: payload?.reminderAtUtc,
    labelsIsArray: Array.isArray(payload?.labels),
    labelsCount: Array.isArray(payload?.labels) ? payload.labels.length : null,
    checklistRowsIsArray: Array.isArray(payload?.checklistRows),
    checklistRowCount: Array.isArray(payload?.checklistRows) ? payload.checklistRows.length : null
  };
}

export function renderValidationErrors(hostOrErrors, maybeErrors) {
  const host = Array.isArray(hostOrErrors) ? document.querySelector('[data-notebook-validation-summary]') : hostOrErrors;
  const validationErrors = Array.isArray(hostOrErrors) ? hostOrErrors : maybeErrors;

  if (!host || !Array.isArray(validationErrors) || validationErrors.length === 0) {
    if (host) {
      host.hidden = true;
      host.replaceChildren();
    }
    return;
  }

  const list = document.createElement('ul');
  validationErrors.forEach((error) => {
    const errorItem = document.createElement('li');
    errorItem.textContent = error.message;
    list.appendChild(errorItem);
  });

  host.replaceChildren(list);
  host.hidden = false;
}

// SECTION: Notebook save error classification for authentication-aware UI
export function classifyNotebookSaveError(error) {
  if (error instanceof NotebookApiError) {
    if (error.status === 401) return { kind: 'session-expired', message: 'Your session has expired. Sign in again to save this note.', actions: ['sign-in', 'copy', 'discard'] };
    if (error.status === 403) return { kind: 'forbidden', message: 'You are not authorised to edit this note.', actions: ['copy', 'discard'] };
    if (error.status === 415) return { kind: 'client-version', message: 'The editor is using an outdated application file. Reload the page and try again.', actions: ['reload', 'discard'] };
    if (error.status === 409) return { kind: 'conflict', message: 'This note was changed elsewhere.' };
    if (error.status === 400) return { kind: 'validation', message: getFirstValidationMessage(error), validationErrors: getValidationMessages(error), retryable: false };
    if (error.status >= 500) return { kind: 'server', message: error.message || 'The note could not be saved because of a server error.' };
  }
  return { kind: 'network', message: error?.message || 'The notebook service could not be reached.' };
}

// SECTION: Notebook autosave retry policy
export function isRetryableSaveError(error) {
  if (error?.code === 'notebook_network_error') return true;
  return [500, 502, 503, 504].includes(error?.status);
}
