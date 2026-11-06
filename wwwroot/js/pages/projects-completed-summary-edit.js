(() => {
  'use strict';

  // Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
  const askConfirm = options => (window.PrismConfirm?.show
      ? window.PrismConfirm.show(options)
      : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));


  const form = document.querySelector('[data-completed-project-edit-form]');
  if (!(form instanceof HTMLFormElement)) return;

  let dirty = false;
  let submitting = false;

  const markDirty = () => { dirty = true; };
  form.addEventListener('input', markDirty);
  form.addEventListener('change', markDirty);

  const autoGrow = (element) => {
    if (!(element instanceof HTMLTextAreaElement)) return;
    element.style.height = 'auto';
    element.style.height = `${Math.max(element.scrollHeight, 94)}px`;
  };

  form.querySelectorAll('[data-autogrow]').forEach((element) => {
    autoGrow(element);
    element.addEventListener('input', () => autoGrow(element));
  });

  const reasonContainer = form.querySelector('[data-not-available-reason]');
  const reasonInput = form.querySelector('[data-reason-input]');
  const reasonClearNote = form.querySelector('[data-reason-clear-note]');

  const syncReasonField = () => {
    const selected = form.querySelector('input[name="Input.AvailableForProliferation"]:checked');
    const isNotAvailable = selected instanceof HTMLInputElement && selected.value === 'false';

    reasonContainer?.classList.toggle('is-hidden', !isNotAvailable);

    if (reasonInput instanceof HTMLTextAreaElement) {
      reasonInput.required = isNotAvailable;
      reasonInput.disabled = !isNotAvailable;
    }

    if (reasonClearNote instanceof HTMLElement) {
      const hasReason = reasonInput instanceof HTMLTextAreaElement && reasonInput.value.trim().length > 0;
      reasonClearNote.hidden = isNotAvailable || !hasReason;
    }
  };

  form.querySelectorAll('input[name="Input.AvailableForProliferation"]').forEach((radio) => {
    radio.addEventListener('change', syncReasonField);
  });
  syncReasonField();

  const newLppPanel = form.querySelector('[data-new-lpp-panel]');
  if (newLppPanel instanceof HTMLDetailsElement && newLppPanel.dataset.openOnLoad === 'true') {
    newLppPanel.open = true;
  }

  form.querySelector('[data-cancel-new-lpp]')?.addEventListener('click', () => {
    form.querySelectorAll('[data-new-lpp-input]').forEach((field) => {
      if (field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement) {
        field.value = '';
      } else if (field instanceof HTMLSelectElement) {
        field.selectedIndex = 0;
      }
    });

    if (newLppPanel instanceof HTMLDetailsElement) newLppPanel.open = false;
    dirty = true;
  });

  const confirmNavigation = (event) => {
    if (!dirty || submitting) return;
    const link = event.currentTarget;
    event.preventDefault();
    askConfirm({
      tone: 'warning',
      title: 'Discard the unsaved changes?',
      message: 'Your edits to this summary have not been saved.',
      confirmText: 'Discard changes',
      cancelText: 'Keep editing'
    }).then((accepted) => {
      if (!accepted) return;
      dirty = false;
      if (link instanceof HTMLAnchorElement && link.href) window.location.assign(link.href);
    });
  };

  document.querySelectorAll('[data-cancel-edit]').forEach((link) => {
    link.addEventListener('click', confirmNavigation);
  });

  window.addEventListener('beforeunload', (event) => {
    if (!dirty || submitting) return;
    event.preventDefault();
    event.returnValue = '';
  });

  form.addEventListener('submit', (event) => {
    if (event.defaultPrevented || !form.checkValidity()) return;

    submitting = true;
    const saveButton = form.querySelector('[data-save-button]');
    const saveLabel = form.querySelector('[data-save-label]');
    const savingLabel = form.querySelector('[data-saving-label]');

    if (saveButton instanceof HTMLButtonElement) saveButton.disabled = true;
    if (saveLabel instanceof HTMLElement) saveLabel.hidden = true;
    if (savingLabel instanceof HTMLElement) savingLabel.hidden = false;
  });
})();
