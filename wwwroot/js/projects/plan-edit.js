(function () {
  const offcanvas = document.getElementById('offcanvasPlanEdit');
  if (!offcanvas) {
    return;
  }

  const forms = offcanvas.querySelectorAll('form[action$="/Projects/Timeline/EditPlan"]');
  if (!forms.length) {
    return;
  }

  const durationsForm = Array.from(forms).find((form) => {
    const modeInput = form.querySelector('input[name="Input.Mode"]');
    return modeInput && modeInput.value === 'Durations';
  });

  if (!durationsForm) {
    return;
  }

  const projectIdInput = durationsForm.querySelector('input[name="Input.ProjectId"]');
  const footer = durationsForm.querySelector('.border-top');
  const savedAt = document.createElement('div');
  savedAt.className = 'small text-muted mt-2';
  savedAt.id = 'planSavedAt';
  (footer ?? durationsForm).appendChild(savedAt);

  const alertHost = document.createElement('div');
  alertHost.className = 'mt-2';
  durationsForm.prepend(alertHost);

  let timer = null;
  const DEBOUNCE_MS = 1500;

  const queueSave = () => {
    if (!projectIdInput) {
      return;
    }

    if (timer) {
      window.clearTimeout(timer);
    }

    timer = window.setTimeout(saveDraft, DEBOUNCE_MS);
  };

  const saveDraft = async () => {
    timer = null;

    try {
      const formData = new FormData(durationsForm);
      formData.set('Input.Action', 'SaveDraft');

      const draftUrl = new URL(durationsForm.action, window.location.origin);
      draftUrl.searchParams.set('id', projectIdInput.value);

      const response = await fetch(draftUrl.toString(), {
        method: 'POST',
        headers: {
          'X-Requested-With': 'XMLHttpRequest'
        },
        body: formData
      });

      if (response.ok) {
        const now = new Date();
        savedAt.textContent = `Saved at ${now.toLocaleTimeString()}`;
      }
    } catch (error) {
      // Ignore autosave errors silently.
    }
  };

  durationsForm.addEventListener('input', (event) => {
    const target = event.target;
    if (!(target instanceof HTMLInputElement) &&
        !(target instanceof HTMLSelectElement) &&
        !(target instanceof HTMLTextAreaElement)) {
      return;
    }

    queueSave();
  });

  const submitButton = durationsForm.querySelector('button[type="submit"][name="Input.Action"][value="Submit"]');

  submitButton?.addEventListener('click', async (event) => {
    event.preventDefault();

    if (!projectIdInput) {
      durationsForm.submit();
      return;
    }

    alertHost.innerHTML = '';

    try {
      const validateUrl = new URL('/Projects/Timeline/EditPlan/Validate', window.location.origin);
      validateUrl.searchParams.set('id', projectIdInput.value);

      const response = await fetch(validateUrl.toString(), {
        method: 'GET',
        headers: {
          'X-Requested-With': 'XMLHttpRequest'
        }
      });

      if (response.ok) {
        const data = await response.json();
        if (data && Array.isArray(data.errors) && data.errors.length > 0) {
          const items = data.errors.map((message) => `<li>${message}</li>`).join('');
          alertHost.innerHTML = `<div class="alert alert-warning" role="alert"><strong>Fix these issues before submitting:</strong><ul class="mt-2 mb-0">${items}</ul></div>`;
          return;
        }
      }
    } catch (error) {
      // If validation fails, proceed with normal submission.
    }

    durationsForm.submit();
  });
})();
