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

  const modalElement = document.getElementById('activitiesDeleteConfirmModal');
  const modalMessage = modalElement ? modalElement.querySelector('[data-delete-confirm-message]') : null;
  const modalConfirmButton = modalElement ? modalElement.querySelector('[data-delete-confirm-accept]') : null;
  const defaultMessage = modalMessage ? modalMessage.dataset.defaultMessage : null;
  const modalInstance = modalElement && window.bootstrap ? new window.bootstrap.Modal(modalElement) : null;

  let pendingForm = null;
  let pendingSubmitter = null;

  if (modalElement) {
    modalElement.addEventListener('hidden.bs.modal', () => {
      pendingForm = null;
      pendingSubmitter = null;
      if (modalMessage && defaultMessage) {
        modalMessage.textContent = defaultMessage;
      }
    });
  }

  if (modalConfirmButton && modalInstance) {
    modalConfirmButton.addEventListener('click', () => {
      if (!pendingForm) {
        modalInstance.hide();
        return;
      }

      const formToSubmit = pendingForm;
      const submitter = pendingSubmitter;

      pendingForm = null;
      pendingSubmitter = null;

      formToSubmit.dataset.activitiesDeleteConfirmed = 'true';
      modalInstance.hide();

      window.setTimeout(() => {
        if (typeof formToSubmit.requestSubmit === 'function') {
          if (submitter) {
            formToSubmit.requestSubmit(submitter);
          } else {
            formToSubmit.requestSubmit();
          }
        } else {
          formToSubmit.submit();
        }
      }, 150);
    });
  }

  moduleRoot.addEventListener('submit', (event) => {
    const form = event.target.closest('form');
    if (!form) {
      return;
    }

    if (form.matches('[data-activities-delete-form]')) {
      if (form.dataset.activitiesDeleteConfirmed === 'true') {
        delete form.dataset.activitiesDeleteConfirmed;
        return;
      }

      const trigger = event.submitter || form.querySelector('[data-confirm]');
      const confirmationMessage = trigger
        ? trigger.getAttribute('data-confirm')
        : form.getAttribute('data-confirm');

      if (modalInstance && modalConfirmButton) {
        event.preventDefault();
        pendingForm = form;
        pendingSubmitter = event.submitter || null;
        if (modalMessage) {
          modalMessage.textContent = confirmationMessage || defaultMessage || '';
        }
        modalInstance.show();
        return;
      }

      if (confirmationMessage && !window.confirm(confirmationMessage)) {
        event.preventDefault();
      }
    }
  });

  const exportForm = moduleRoot.querySelector('[data-activities-export-form]');
  if (exportForm) {
    const exportButton = exportForm.querySelector('button');
    if (exportButton) {
      const busyMarkup = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Exporting…';

      function restoreExportButton() {
        if (!exportButton.dataset.originalText) {
          return;
        }

        exportButton.innerHTML = exportButton.dataset.originalText;
        delete exportButton.dataset.originalText;
        exportButton.removeAttribute('aria-busy');
        exportButton.disabled = false;
      }

      exportForm.addEventListener('submit', () => {
        if (exportButton.hasAttribute('aria-busy')) {
          return;
        }

        if (!exportButton.dataset.originalText) {
          exportButton.dataset.originalText = exportButton.innerHTML;
        }

        exportButton.innerHTML = busyMarkup;
        exportButton.setAttribute('aria-busy', 'true');
        exportButton.disabled = true;

        let cleanedUp = false;
        let timeoutId;

        function cleanup() {
          if (cleanedUp) {
            return;
          }

          cleanedUp = true;
          window.clearTimeout(timeoutId);
          restoreExportButton();
          window.removeEventListener('focus', cleanup);
          document.removeEventListener('visibilitychange', handleVisibilityChange);
        }

        function handleVisibilityChange() {
          if (document.visibilityState === 'visible') {
            cleanup();
          }
        }

        window.addEventListener('focus', cleanup);
        document.addEventListener('visibilitychange', handleVisibilityChange);
        timeoutId = window.setTimeout(cleanup, 2500);
      });
    }
  }
})();
