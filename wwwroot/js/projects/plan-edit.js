// SECTION: Unsaved-changes guard and remembered tab (discovery C3.6, PO Timeline Increment 2d)
(function () {
  const offcanvas = document.getElementById('offcanvasPlanEdit');
  if (!offcanvas) {
    return;
  }

  const forms = Array.from(offcanvas.querySelectorAll('form[data-plan-edit-form]'));
  if (forms.length === 0) {
    return;
  }

  // A form is dirty from its first edit until it is submitted or (Durations) autosaved.
  const dirty = new Set();
  let leaving = false;
  const markDirty = (event) => {
    if (event.target instanceof HTMLInputElement ||
        event.target instanceof HTMLSelectElement ||
        event.target instanceof HTMLTextAreaElement) {
      dirty.add(event.currentTarget);
    }
  };

  // A refused post reopened the editor with the user's entries: they are not saved yet.
  if (offcanvas.querySelector('[data-plan-restored]')) {
    const activePane = offcanvas.querySelector('.tab-pane.active');
    forms.filter((form) => !activePane || activePane.contains(form)).forEach((form) => dirty.add(form));
  }

  forms.forEach((form) => {
    form.addEventListener('input', markDirty);
    form.addEventListener('change', markDirty);
    form.addEventListener('plan:autosaved', () => dirty.delete(form));
    form.addEventListener('submit', (event) => {
      if (event.defaultPrevented || leaving) {
        return;
      }
      // Each tab posts only its own form, so edits on the other tab would be dropped silently.
      const otherDirty = forms.some((other) => other !== form && dirty.has(other));
      if (otherDirty && !window.confirm('Your unsaved changes on the other tab will not be included. Continue?')) {
        event.preventDefault();
        event.stopImmediatePropagation();
        return;
      }
      leaving = true;
    });
  });

  offcanvas.addEventListener('hide.bs.offcanvas', (event) => {
    if (leaving || dirty.size === 0) {
      return;
    }
    if (!window.confirm('You have unsaved changes in the planner. Close it anyway?')) {
      event.preventDefault();
      return;
    }
    dirty.clear();
  });

  window.addEventListener('beforeunload', (event) => {
    if (leaving || dirty.size === 0) {
      return;
    }
    event.preventDefault();
    event.returnValue = '';
  });

  // Reopen on the tab the user last used for this project, unless a refused post
  // reopened the editor on the tab it came from. Storage is a convenience only.
  const projectId = forms[0].querySelector('input[name="Input.ProjectId"]')?.value;
  const storageKey = projectId ? `prism.planEditor.tab.${projectId}` : null;
  const tabs = Array.from(offcanvas.querySelectorAll('#plan-edit-tabs [data-bs-toggle="tab"]'));
  tabs.forEach((tab) => tab.addEventListener('shown.bs.tab', () => {
    try {
      if (storageKey) window.localStorage.setItem(storageKey, tab.id);
    } catch {
      // Storage may be unavailable (private window, blocked site data).
    }
  }));
  if (storageKey && !offcanvas.querySelector('[data-plan-submission-errors]')) {
    let stored = null;
    try {
      stored = window.localStorage.getItem(storageKey);
    } catch {
      stored = null;
    }
    const remembered = tabs.find((tab) => tab.id === stored);
    if (remembered && !remembered.classList.contains('active') && !remembered.disabled && window.bootstrap?.Tab) {
      window.bootstrap.Tab.getOrCreateInstance(remembered).show();
    }
  }
})();

(function () {
  const offcanvas = document.getElementById('offcanvasPlanEdit');
  if (!offcanvas) {
    return;
  }

  const forms = offcanvas.querySelectorAll('form[data-plan-edit-form]');
  const durationsForm = Array.from(forms).find((form) =>
    form.querySelector('input[name="Input.Mode"]')?.value === 'Durations');
  const projectIdInput = durationsForm?.querySelector('input[name="Input.ProjectId"]');
  if (!durationsForm || !projectIdInput) {
    return;
  }

  const savedAt = document.createElement('div');
  savedAt.className = 'small text-muted mt-2';
  savedAt.id = 'planSavedAt';
  savedAt.setAttribute('role', 'status');
  savedAt.setAttribute('aria-live', 'polite');
  (durationsForm.querySelector('.border-top') ?? durationsForm).appendChild(savedAt);

  const DEBOUNCE_MS = 1500;
  let timer = null;
  let activeSave = null;
  let editVersion = 0;
  let submitting = false;
  let resumingForm = null;
  let paused = false;

  const cancelQueuedSave = () => {
    window.clearTimeout(timer);
    timer = null;
  };

  const saveDraft = () => {
    timer = null;
    if (submitting || paused) {
      return;
    }
    // Never overlap working-input writes. A later edit will save the latest form.
    if (activeSave) {
      timer = window.setTimeout(saveDraft, DEBOUNCE_MS);
      return;
    }

    const version = editVersion;
    const formData = new FormData(durationsForm);
    formData.set('Input.Action', 'Autosave');
    const draftUrl = new URL(durationsForm.action, window.location.origin);
    draftUrl.searchParams.set('id', projectIdInput.value);
    savedAt.textContent = 'Saving…';

    activeSave = (async () => {
      try {
        const response = await fetch(draftUrl.toString(), {
          method: 'POST',
          headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'Accept': 'application/json'
          },
          body: formData
        });
        if (response.redirected || !response.headers.get('Content-Type')?.includes('application/json')) {
          throw new Error('The server did not confirm the save. Reload and try again.');
        }
        const data = await response.json();
        if (!response.ok || data?.ok !== true || !data.savedAt) {
          throw new Error(data?.message || 'Validation failed. Check the entered values.');
        }
        // The server snapshots the saved proposal under the project gate.
        // Refresh the token even when the user has made subsequent local edits:
        // the next autosave must compare against the last COMMITTED proposal.
        if (typeof data.editToken === 'string') {
          // Exact dates in the other tab were NOT refreshed with the Duration
          // calculation. Keep that tab's old fingerprint so its stale payload
          // cannot silently undo newer saved Duration proposal values.
          const token = durationsForm.querySelector('input[name="Input.EditToken"]');
          if (token) token.value = data.editToken;
        }
        if (version === editVersion && !submitting && !paused) {
          savedAt.textContent = `Saved at ${new Date(data.savedAt).toLocaleTimeString()}`;
          durationsForm.dispatchEvent(new CustomEvent('plan:autosaved'));
        }
      } catch (error) {
        if (!submitting && !paused) {
          savedAt.textContent = `Could not save. ${error.message || 'Please try again.'}`;
        }
      }
    })().finally(() => { activeSave = null; });
  };

  durationsForm.addEventListener('input', (event) => {
    const target = event.target;
    if (!(target instanceof HTMLInputElement) &&
        !(target instanceof HTMLSelectElement) &&
        !(target instanceof HTMLTextAreaElement)) {
      return;
    }
    editVersion += 1;
    if (submitting || paused) {
      return;
    }
    savedAt.textContent = 'Unsaved changes';
    cancelQueuedSave();
    timer = window.setTimeout(saveDraft, DEBOUNCE_MS);
  });

  // Keep native submitter and constraint validation semantics for every explicit
  // action. The server validates the submitted values, not an older saved draft.
  forms.forEach((form) => form.addEventListener('submit', async (event) => {
    if (resumingForm === form) {
      resumingForm = null;
      return;
    }
    if (submitting) {
      event.preventDefault();
      return;
    }
    submitting = true;
    cancelQueuedSave();
    if (!activeSave) {
      return;
    }

    event.preventDefault();
    const submitter = event.submitter;
    await activeSave;
    if (paused || !form.reportValidity()) {
      submitting = false;
      return;
    }
    resumingForm = form;
    form.requestSubmit(submitter ?? undefined);
  }));

  const pauseAutosave = () => {
    paused = true;
    cancelQueuedSave();
  };
  // The unsaved-changes guard may keep the planner open; only a real close pauses autosave.
  offcanvas.addEventListener('hide.bs.offcanvas', (event) => {
    if (!event.defaultPrevented) pauseAutosave();
  });
  offcanvas.addEventListener('show.bs.offcanvas', () => { paused = false; });
  window.addEventListener('pagehide', pauseAutosave);
})();
