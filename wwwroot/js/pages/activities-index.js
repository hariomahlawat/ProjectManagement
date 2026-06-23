(function () {
  const moduleRoot = document.querySelector('[data-module="activities-index"]');
  if (!moduleRoot) return;

  moduleRoot.querySelectorAll('[data-activity-preview-image]').forEach((image) => {
    const markBroken = () => image.closest('.activity-card__preview')?.classList.add('is-broken');
    image.addEventListener('error', markBroken, { once: true });
    if (image.complete && image.naturalWidth === 0) markBroken();
  });

  const filterForm = document.getElementById('activitiesFilterForm');
  if (filterForm) {
    const submitFromFirstPage = () => {
      const pageInput = filterForm.querySelector('input[name="Page"]');
      if (pageInput) pageInput.value = '1';
      if (typeof filterForm.requestSubmit === 'function') filterForm.requestSubmit();
      else filterForm.submit();
    };

    const searchInput = filterForm.querySelector('input[name="Search"]');
    if (searchInput) {
      searchInput.addEventListener('keydown', (event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          submitFromFirstPage();
        }
      });
    }

    filterForm.querySelectorAll('[data-activities-autosubmit]').forEach((input) => {
      input.addEventListener('change', submitFromFirstPage);
    });

    document.addEventListener('click', (event) => {
      const openDetails = filterForm.querySelector('.activities-more-filters[open]');
      if (openDetails && !openDetails.contains(event.target)) {
        openDetails.removeAttribute('open');
      }
    });
  }

  const modalElement = document.getElementById('activitiesDeleteConfirmModal');
  const modalMessage = modalElement?.querySelector('[data-delete-confirm-message]');
  const modalConfirmButton = modalElement?.querySelector('[data-delete-confirm-accept]');
  const defaultMessage = modalMessage?.dataset.defaultMessage || '';
  const modalInstance = modalElement && window.bootstrap ? new window.bootstrap.Modal(modalElement) : null;
  let pendingForm = null;
  let pendingSubmitter = null;

  if (modalElement) {
    modalElement.addEventListener('hidden.bs.modal', () => {
      pendingForm = null;
      pendingSubmitter = null;
      if (modalMessage) modalMessage.textContent = defaultMessage;
    });
  }

  if (modalConfirmButton && modalInstance) {
    modalConfirmButton.addEventListener('click', () => {
      if (!pendingForm) return modalInstance.hide();
      const form = pendingForm;
      const submitter = pendingSubmitter;
      pendingForm = null;
      pendingSubmitter = null;
      form.dataset.activitiesDeleteConfirmed = 'true';
      modalInstance.hide();
      if (submitter?.click) submitter.click();
      else if (typeof form.requestSubmit === 'function') form.requestSubmit();
      else form.submit();
    });
  }

  moduleRoot.addEventListener('submit', (event) => {
    const form = event.target.closest('form');
    if (!form?.matches('[data-activities-delete-form]')) return;
    if (form.dataset.activitiesDeleteConfirmed === 'true') {
      delete form.dataset.activitiesDeleteConfirmed;
      return;
    }

    const trigger = event.submitter || form.querySelector('[data-confirm]');
    const message = trigger?.getAttribute('data-confirm') || form.getAttribute('data-confirm') || defaultMessage;
    if (modalInstance && modalConfirmButton) {
      event.preventDefault();
      pendingForm = form;
      pendingSubmitter = event.submitter || null;
      if (modalMessage) modalMessage.textContent = message;
      modalInstance.show();
    } else if (message && !window.confirm(message)) {
      event.preventDefault();
    }
  });

  const exportForm = moduleRoot.querySelector('[data-activities-export-form]');
  const exportButton = exportForm?.querySelector('button');
  if (exportForm && exportButton) {
    exportForm.addEventListener('submit', () => {
      if (exportButton.getAttribute('aria-busy') === 'true') return;
      const original = exportButton.innerHTML;
      exportButton.setAttribute('aria-busy', 'true');
      exportButton.disabled = true;
      exportButton.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Exporting…';
      window.setTimeout(() => {
        exportButton.innerHTML = original;
        exportButton.removeAttribute('aria-busy');
        exportButton.disabled = false;
      }, 3000);
    });
  }
})();
