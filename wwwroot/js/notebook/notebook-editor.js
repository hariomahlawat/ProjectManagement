import { NotebookApi, NotebookApiError } from './notebook-api.js';
import { createAutosave } from './notebook-autosave.js';
import { createChecklistEditor } from './notebook-checklist-editor.js';
import { reconcileMutation, requireMutationItem } from './notebook-reconcile.js';
import { getFirstValidationMessage, getValidationMessages } from './notebook-errors.js';

// SECTION: Notebook modal editor lifecycle and coordinated mutations
export function initNotebookEditor(board, view, options = {}) {
  let modal, item, autosave, checklist, trigger, openedByPushState = false, currentSaveError = null, pendingExternalUpdate = null;
  let blockedByValidation = false;
  let lastValidationFingerprint = null;
  let lastSubmittedChecklistRows = [];
  const shell = options.shell || document.querySelector('.notebook-shell');
  const dirtyState = { title: false, body: false, checklist: false };
  const buildNoteUrl = (id) => { const url = new URL(location.href); id ? url.searchParams.set('note', id) : url.searchParams.delete('note'); return url; };
  const focusableSelector = 'a[href],button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])';
  function setStatus(text, state = 'idle') { const el = modal?.querySelector('[data-notebook-save-state]'); if (el) { el.textContent = text || ''; el.dataset.state = state; } const retry = modal?.querySelector('[data-notebook-retry]'); const reload = modal?.querySelector('[data-notebook-reload-latest]'); const discard = modal?.querySelector('[data-modal-discard]'); const signIn = modal?.querySelector('[data-notebook-sign-in]'); const copy = modal?.querySelector('[data-notebook-copy-unsaved]'); if (retry) retry.hidden = !['network','server','error'].includes(state); if (reload) { reload.hidden = !['conflict','client-version'].includes(state); reload.textContent = state === 'client-version' ? 'Reload application' : 'Reload latest'; } if (discard) discard.hidden = !['network','server','error','conflict','client-version','session-expired','forbidden'].includes(state); if (signIn) signIn.hidden = state !== 'session-expired'; if (copy) copy.hidden = !['session-expired','forbidden','network','server','error'].includes(state); }
  function buildCurrentPayload() {
    return buildUpdatePayload({
      title: modal.querySelector('[data-modal-title]').value,
      body: modal.querySelector('[data-modal-body]').value,
      type: item.type,
      checklistRows: item.type === 'Checklist' ? checklist.getRows() : []
    });
  }
  function scheduleAutosave() { const nextPayload = buildCurrentPayload(); const nextFingerprint = validationFingerprint(nextPayload); if (blockedByValidation && nextFingerprint === lastValidationFingerprint) return; if (blockedByValidation) clearValidationBlock(); autosave?.schedule(nextPayload); }
  function clearValidationBlock() { blockedByValidation = false; lastValidationFingerprint = null; renderValidationErrors([]); }
  function configureAutosave() { autosave?.stop(); autosave = createAutosave({ save: saveEditorPayload, onSaving: () => setStatus('Saving…','saving'), onPersisted: applyPersistedResponse, onSaveError: handleEditorError, onReconcileError: handleReconcileError }); }
  function renderPin() { const pin = modal.querySelector('[data-modal-pin]'); pin?.classList.toggle('is-active', !!item?.isPinned); if (pin) pin.setAttribute('aria-label', item?.isPinned ? 'Unpin note' : 'Pin note'); }
  function renderMode() { modal.querySelector('[data-modal-title]').value = item.title || ''; modal.querySelector('[data-modal-body]').value = item.body || ''; checklist.setRows(item.checklistRows || []); modal.querySelector('[data-modal-checklist]').hidden = item.type !== 'Checklist'; dirtyState.title = dirtyState.body = dirtyState.checklist = false; pendingExternalUpdate = null; clearValidationBlock(); renderPin(); }
  function build() { modal = document.createElement('div'); modal.className = 'notebook-modal'; modal.hidden = true; modal.setAttribute('role','dialog'); modal.setAttribute('aria-modal','true'); modal.setAttribute('aria-labelledby','notebook-modal-title'); modal.innerHTML = '<div class="notebook-modal__backdrop" data-close></div><section class="notebook-modal__dialog"><header><input id="notebook-modal-title" data-modal-title class="notebook-modal__title" maxlength="220"><button type="button" class="notebook-action-icon" data-modal-pin aria-label="Pin note"><i class="bi bi-pin-angle"></i></button></header><textarea data-modal-body class="notebook-modal__body" maxlength="20000" placeholder="Take a note…"></textarea><div class="notebook-editor__validation" data-notebook-validation-summary role="alert" hidden></div><div data-modal-checklist class="notebook-checklist-editor" hidden></div><div class="notebook-save-feedback"><span class="notebook-modal__save-state" data-notebook-save-state aria-live="polite"></span><button type="button" data-notebook-retry hidden>Retry</button><button type="button" data-notebook-reload-latest hidden>Reload latest</button><button type="button" data-notebook-sign-in hidden>Sign in again</button><button type="button" data-notebook-copy-unsaved hidden>Copy note text</button><button type="button" data-modal-discard hidden>Discard changes</button></div><footer><button type="button" class="btn btn-sm btn-link" data-close aria-label="Close note editor">Close</button></footer></section>'; document.body.appendChild(modal); checklist = createChecklistEditor(modal.querySelector('[data-modal-checklist]'), { onChange: () => { dirtyState.checklist = true; scheduleAutosave(); } }); modal.addEventListener('click', (e) => { if (e.target.matches('[data-close]')) requestClose(); }); modal.addEventListener('keydown', trapFocus); modal.querySelector('[data-modal-title]').addEventListener('input', () => { dirtyState.title = true; scheduleAutosave(); }); modal.querySelector('[data-modal-body]').addEventListener('input', () => { dirtyState.body = true; scheduleAutosave(); }); modal.querySelector('[data-modal-pin]').addEventListener('click', pinItem); modal.querySelector('[data-notebook-retry]')?.addEventListener('click', retrySave); modal.querySelector('[data-notebook-reload-latest]')?.addEventListener('click', reloadLatest); modal.querySelector('[data-notebook-sign-in]')?.addEventListener('click', signInAgain); modal.querySelector('[data-notebook-copy-unsaved]')?.addEventListener('click', copyUnsavedContent); modal.querySelector('[data-modal-discard]')?.addEventListener('click', discardChangesAndClose); }
  async function saveEditorPayload(data) { const requestPayload = { ...data, version: item.version }; assertValidVersion(requestPayload.version); lastSubmittedChecklistRows = item.type === 'Checklist' ? structuredClone(requestPayload.checklistRows || []) : []; return item.type === 'Checklist' ? NotebookApi.updateChecklist(item.id, requestPayload) : NotebookApi.updateContent(item.id, requestPayload); }
  async function applyPersistedResponse(response) { item = requireMutationItem(response); if (item.type === 'Checklist' && Array.isArray(item.checklistRows)) checklist.reconcileRows(item.checklistRows, lastSubmittedChecklistRows); dirtyState.title = dirtyState.body = dirtyState.checklist = false; clearValidationBlock(); clearStoredDraft(item?.id); setStatus('Saved','saved'); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts: options.applyCounts, preservePosition: true, showGlobalError: options.showGlobalError, renderFailureMessage: 'The note was saved, but its card could not refresh. Reload the page.', reconcileFailureMessage: 'The note was saved, but the board could not refresh. Reload the page.' }); }
  function handleReconcileError() { options.showGlobalError?.('The note was saved, but the board could not refresh. Reload the page.'); setStatus('Saved','saved'); }
  function classifySaveError(error) { return classifyNotebookSaveError(error); }
  function handleEditorError(error) {
    currentSaveError = classifySaveError(error);
    setStatus(currentSaveError.message, currentSaveError.kind);
    if (currentSaveError.kind === 'validation') {
      setStatus(currentSaveError.message, 'validation');
      renderValidationErrors(currentSaveError.validationErrors);
      const submittedPayload = buildCurrentPayload();
      blockedByValidation = true;
      lastValidationFingerprint = validationFingerprint(submittedPayload);
      autosave?.cancel?.();
    }
    if (isDevelopment()) {
      const submittedPayload = buildCurrentPayload();
      console.error('Notebook update failed', { noteId: item?.id, status: error?.status, code: error?.code, errors: error?.errors, responseText: error?.responseText, payload: describeUpdatePayload(submittedPayload) });
    }
    return { retryable: isRetryableSaveError(error) };
  }
  function isDevelopment() { return document.documentElement.dataset.environment === 'Development' || location.hostname === 'localhost'; }
  async function disposeCurrentItem() { if (!item || !autosave) return; await autosave.flush(); autosave.stop(); autosave = null; }
  function setBackgroundInert(inert) { if (shell) shell.inert = inert; if (modal) modal.inert = false; document.body.classList.toggle('notebook-modal-open', inert); }
  function trapFocus(event) { if (event.key !== 'Tab' || modal.hidden) return; const focusable = [...modal.querySelectorAll(focusableSelector)].filter(el => el.offsetParent !== null); if (!focusable.length) { event.preventDefault(); return; } const first = focusable[0], last = focusable[focusable.length - 1]; if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); } else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); } }
  function preserveUnsavedDraft() { if (!item?.id) return; sessionStorage.setItem(`notebook-draft:${item.id}`, JSON.stringify({ itemId: item.id, type: item.type, title: modal.querySelector('[data-modal-title]').value, body: modal.querySelector('[data-modal-body]').value, checklistRows: item.type === 'Checklist' ? checklist.getRows() : [], sourceVersion: item.version, savedAtUtc: new Date().toISOString() })); } function clearStoredDraft(itemId) { if (itemId) sessionStorage.removeItem(`notebook-draft:${itemId}`); } function readStoredDraft(itemId) { const key = `notebook-draft:${itemId}`; const raw = sessionStorage.getItem(key); if (!raw) return null; try { return JSON.parse(raw); } catch { sessionStorage.removeItem(key); return null; } } function restoreStoredDraftIfNeeded() { const storedDraft = readStoredDraft(item.id); if (storedDraft && (storedDraft.title !== item.title || storedDraft.body !== item.body || JSON.stringify(storedDraft.checklistRows || []) !== JSON.stringify(item.checklistRows || [])) && window.confirm('Restore your unsaved local draft for this note?')) { modal.querySelector('[data-modal-title]').value = storedDraft.title || ''; modal.querySelector('[data-modal-body]').value = storedDraft.body || ''; if (item.type === 'Checklist') checklist.setRows(storedDraft.checklistRows || []); scheduleAutosave(); } }
  function signInAgain() { preserveUnsavedDraft(); const returnUrl = window.location.pathname + window.location.search + window.location.hash; window.location.assign('/Identity/Account/Login?ReturnUrl=' + encodeURIComponent(returnUrl)); }
  async function copyUnsavedContent() { const title = modal.querySelector('[data-modal-title]').value.trim(); const body = modal.querySelector('[data-modal-body]').value.trim(); const text = [title, body].filter(Boolean).join('\n\n'); await navigator.clipboard.writeText(text); setStatus('Unsaved note text copied. Sign in again before saving.', currentSaveError?.kind || 'session-expired'); }
  async function retrySave() { const button = modal.querySelector('[data-notebook-retry]'); button.disabled = true; try { clearValidationBlock(); autosave.schedule(buildCurrentPayload()); await autosave.flush(); } finally { button.disabled = false; } }
  async function discardChangesAndClose() { if (!window.confirm('Discard unsaved changes and close this note?')) return; const closedId = item?.id; autosave?.cancel?.(); autosave?.stop(); autosave = null; currentSaveError = null; clearValidationBlock(); item = null; modal.hidden = true; setBackgroundInert(false); history.replaceState(history.state, '', buildNoteUrl(null)); (board.findCard(closedId) || trigger)?.focus?.(); }
  async function reloadLatest() { if (currentSaveError?.kind === 'client-version') { window.location.reload(); return; } if (!window.confirm('Reload the latest saved version? Unsaved changes in this editor will be discarded.')) return; const button = modal.querySelector('[data-notebook-reload-latest]'); button.disabled = true; try { item = await NotebookApi.getItem(item.id); renderMode(); clearValidationBlock(); setStatus('', 'idle'); } catch (error) { setStatus(error.message || 'Unable to reload the note.', 'error'); } finally { button.disabled = false; } }
  async function pinItem() { if (!item) return; const button = modal.querySelector('[data-modal-pin]'); button.disabled = true; try { await autosave?.flush(); const response = await NotebookApi.setPinned(item.id, !item.isPinned, item.version); item = requireMutationItem(response, 'The pin response did not contain the updated note.'); renderPin(); await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts: options.applyCounts, preservePosition: false, prepend: true, showGlobalError: options.showGlobalError, reconcileFailureMessage: `The note was ${item.isPinned ? 'pinned' : 'unpinned'}, but the board could not refresh. Reload the page.` }); setStatus('Saved','saved'); } catch (error) { handleEditorError(error); } finally { button.disabled = false; } }
  async function open(id, options = {}) { if (!modal) build(); if (item && item.id !== id) await disposeCurrentItem(); trigger = document.activeElement; item = await NotebookApi.getItem(id); configureAutosave(); renderMode(); restoreStoredDraftIfNeeded(); modal.hidden = false; setBackgroundInert(true); modal.querySelector('[data-modal-title]').focus(); if (options.pushHistory !== false) { openedByPushState = true; history.pushState({ ...(history.state || {}), notebookModal: true, notebookNoteId: id }, '', buildNoteUrl(id)); } else { openedByPushState = false; } }
  async function requestClose({ fromHistory = false } = {}) { if (!item || !modal || modal.hidden) return; const closeButton = modal.querySelector('[data-close]:not(.notebook-modal__backdrop)'); closeButton.disabled = true; try { await autosave?.flush(); autosave?.stop(); autosave = null; modal.hidden = true; setBackgroundInert(false); const closedId = item.id; item = null; if (!fromHistory) { if (openedByPushState) history.back(); else history.replaceState(history.state, '', buildNoteUrl(null)); } (board.findCard(closedId) || trigger)?.focus?.(); } catch (error) { handleEditorError(error); } finally { closeButton.disabled = false; } }
  function syncExternalUpdate(updated) { if (!item || item.id !== updated.id) return; if (dirtyState.title || dirtyState.body || dirtyState.checklist) { pendingExternalUpdate = updated; setStatus('This checklist changed elsewhere.', 'conflict'); return; } item = updated; if (updated.type === 'Checklist') checklist.reconcileRows(updated.checklistRows || []); modal.querySelector('[data-modal-title]').value = updated.title || ''; modal.querySelector('[data-modal-body]').value = updated.body || ''; renderPin(); }
  return { open, requestClose, isOpen: () => !!item && !!modal && !modal.hidden, syncExternalUpdate };
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
    const item = document.createElement('li');
    item.textContent = error.message;
    list.appendChild(item);
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
