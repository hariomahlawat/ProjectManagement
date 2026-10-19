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
          forms.forEach((form) => {
            const token = form.querySelector('input[name="Input.EditToken"]');
            if (token) token.value = data.editToken;
          });
        }
        if (version === editVersion && !submitting && !paused) {
          savedAt.textContent = `Saved at ${new Date(data.savedAt).toLocaleTimeString()}`;
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
  offcanvas.addEventListener('hide.bs.offcanvas', pauseAutosave);
  offcanvas.addEventListener('show.bs.offcanvas', () => { paused = false; });
  window.addEventListener('pagehide', pauseAutosave);
})();
