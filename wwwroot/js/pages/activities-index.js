(function () {
  const moduleRoot = document.querySelector('[data-module="activities-index"]');
  if (!moduleRoot) {
    return;
  }

  const filterOffcanvas = document.getElementById('activitiesFilterOffcanvas');
  const filterForm = filterOffcanvas
    ? filterOffcanvas.querySelector('form')
    : moduleRoot.querySelector('.activities-filter, .activities-filter-form');
  if (filterForm) {
    const autoSubmitInputs = filterForm.querySelectorAll('[data-activities-autosubmit]');
    autoSubmitInputs.forEach((input) => {
      input.addEventListener('change', () => {
        const pageInput = filterForm.querySelector('input[name="Page"]');
        if (pageInput) {
          pageInput.value = '1';
        }

        if (typeof filterForm.requestSubmit === 'function') {
          filterForm.requestSubmit();
        } else {
          filterForm.submit();
        }
      });
    });
  }

  moduleRoot.addEventListener('submit', (event) => {
    const form = event.target.closest('form');
    if (!form) {
      return;
    }

    if (form.matches('[data-activities-delete-form]')) {
      const trigger = event.submitter || form.querySelector('[data-confirm]');
      const confirmationMessage = trigger ? trigger.getAttribute('data-confirm') : form.getAttribute('data-confirm');
      if (confirmationMessage && !window.confirm(confirmationMessage)) {
        event.preventDefault();
      }
    }
  });

  const exportForm = moduleRoot.querySelector('[data-activities-export-form]');
  if (exportForm) {
    exportForm.addEventListener('submit', () => {
      const button = exportForm.querySelector('button');
      if (button) {
        button.dataset.originalText = button.innerHTML;
        button.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Exporting…';
        button.setAttribute('aria-busy', 'true');
      }
    });
  }
})();
