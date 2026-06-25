(() => {
  'use strict';
  const forms = document.querySelectorAll('.approval-decision-form');
  forms.forEach(form => {
    form.addEventListener('submit', event => {
      const submitter = event.submitter;
      if (!submitter) return;
      const decision = submitter.value;
      if (decision === 'Approve') {
        const message = submitter.dataset.confirmMessage || 'Approve this request? The change will be applied immediately.';
        if (!window.confirm(message)) { event.preventDefault(); return; }
      }
      if (form.classList.contains('is-submitting')) { event.preventDefault(); return; }
      form.classList.add('is-submitting');
      form.querySelectorAll('button[type="submit"]').forEach(button => button.disabled = true);
      submitter.disabled = false;
      submitter.innerHTML = '<span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>Processing';
    });
  });
})();
