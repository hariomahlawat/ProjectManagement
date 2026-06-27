(() => {
  'use strict';

  const form = document.querySelector('[data-approval-form]');
  const dialog = document.getElementById('approval-confirm-dialog');
  if (!form) return;

  let approvedSubmitter = null;
  let confirmed = false;

  const setSubmitting = (submitter) => {
    if (form.classList.contains('is-submitting')) return false;
    form.classList.add('is-submitting');
    form.querySelectorAll('button[type="submit"]').forEach((button) => {
      button.disabled = true;
    });
    if (submitter) {
      submitter.disabled = false;
      submitter.innerHTML = '<span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>Processing';
    }
    return true;
  };

  form.addEventListener('submit', (event) => {
    const submitter = event.submitter;
    if (!submitter) return;

    const decision = submitter.dataset.decision;
    if (decision === 'approve' && !confirmed && dialog?.showModal) {
      event.preventDefault();
      approvedSubmitter = submitter;
      const title = dialog.querySelector('#approval-confirm-title');
      const message = dialog.querySelector('[data-confirm-message]');
      if (title) title.textContent = form.dataset.confirmTitle || 'Approve request';
      if (message) message.textContent = form.dataset.confirmMessage || 'Approve this request? The change will be applied immediately.';
      dialog.showModal();
      return;
    }

    if (!setSubmitting(submitter)) {
      event.preventDefault();
    }
  });

  dialog?.querySelector('[data-confirm-cancel]')?.addEventListener('click', () => {
    approvedSubmitter = null;
    dialog.close();
  });

  dialog?.querySelector('[data-confirm-approve]')?.addEventListener('click', () => {
    if (!approvedSubmitter) return;
    confirmed = true;
    dialog.close();
    form.requestSubmit(approvedSubmitter);
  });

  dialog?.addEventListener('cancel', () => {
    approvedSubmitter = null;
  });
})();
