(() => {
  const confirmModalId = 'iprManageConfirmModal';
  const fallbackMessage = 'Delete this record? This action cannot be undone.';

  function ensureBootstrap() {
    return typeof window !== 'undefined' && window.bootstrap ? window.bootstrap : null;
  }

  function ensureBootstrapModal() {
    const bootstrap = ensureBootstrap();
    return bootstrap && typeof bootstrap.Modal === 'function' ? bootstrap : null;
  }

  function ensureConfirmModalElement() {
    let modal = document.getElementById(confirmModalId);
    if (modal) {
      return modal;
    }

    modal = document.createElement('div');
    modal.id = confirmModalId;
    modal.className = 'modal fade ipr-confirm-modal';
    modal.tabIndex = -1;
    modal.setAttribute('aria-hidden', 'true');
    modal.innerHTML = `
      <div class="modal-dialog modal-dialog-centered">
        <div class="modal-content border-0 shadow-lg">
          <button type="button" class="btn-close ipr-confirm-modal__close" data-bs-dismiss="modal" aria-label="Cancel"></button>
          <div class="modal-body p-4">
            <div class="d-flex align-items-start gap-3">
              <div class="ipr-confirm-modal__icon" aria-hidden="true">!</div>
              <div class="flex-grow-1">
                <h5 class="ipr-confirm-modal__title mb-2">Delete IPR record</h5>
                <p class="ipr-confirm-modal__message mb-1" data-ipr-confirm-message>You're about to permanently delete <span class="ipr-confirm-modal__subject" data-ipr-confirm-subject></span>.</p>
                <p class="ipr-confirm-modal__subtitle mb-0">This action cannot be undone.</p>
              </div>
            </div>
          </div>
          <div class="modal-footer border-0 pt-0">
            <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal" data-ipr-confirm-cancel>Cancel</button>
            <button type="button" class="btn btn-danger" data-ipr-confirm-accept>Delete</button>
          </div>
        </div>
      </div>
    `;

    document.body.appendChild(modal);
    return modal;
  }

  let isConfirmModalActive = false;

  function showConfirmModal(displayText, message) {
    const bootstrap = ensureBootstrapModal();
    if (!bootstrap) {
      return Promise.resolve(window.confirm(message || fallbackMessage));
    }

    const modalEl = ensureConfirmModalElement();
    if (isConfirmModalActive) {
      return Promise.resolve(false);
    }

    const instance = bootstrap.Modal.getOrCreateInstance(modalEl, {
      backdrop: 'static',
      keyboard: true,
      focus: true
    });

    const subjectEl = modalEl.querySelector('[data-ipr-confirm-subject]');
    if (subjectEl) {
      const subject = displayText && displayText.trim().length > 0 ? displayText : 'this record';
      subjectEl.textContent = subject;
    }

    const confirmButton = modalEl.querySelector('[data-ipr-confirm-accept]');
    const cancelButton = modalEl.querySelector('[data-ipr-confirm-cancel]');

    isConfirmModalActive = true;

    return new Promise(resolve => {
      let handled = false;

      const cleanup = () => {
        confirmButton?.removeEventListener('click', handleConfirm);
        cancelButton?.removeEventListener('click', handleCancel);
        modalEl.removeEventListener('hidden.bs.modal', handleHidden);
        isConfirmModalActive = false;
      };

      const handleConfirm = () => {
        handled = true;
        cleanup();
        resolve(true);
        instance.hide();
      };

      const handleCancel = () => {
        handled = true;
        cleanup();
        resolve(false);
        instance.hide();
      };

      const handleHidden = () => {
        if (!handled) {
          resolve(false);
        }
        cleanup();
      };

      confirmButton?.addEventListener('click', handleConfirm);
      cancelButton?.addEventListener('click', handleCancel);
      modalEl.addEventListener('hidden.bs.modal', handleHidden, { once: true });

      instance.show();

      window.setTimeout(() => {
        confirmButton?.focus();
      }, 120);
    });
  }

  document.addEventListener('click', event => {
    const button = event.target.closest('[data-manage-ipr-delete]');
    if (!button) {
      return;
    }

    const form = button.closest('form');
    if (!form) {
      return;
    }

    event.preventDefault();
    event.stopImmediatePropagation();

    const title = button.getAttribute('data-record-title');
    const display = title && title.trim().length > 0 ? `"${title.trim()}"` : 'this record';
    const message = `Delete ${display}? This action cannot be undone.`;

    showConfirmModal(display, message).then(confirmed => {
      if (!confirmed) {
        return;
      }

      if (typeof form.requestSubmit === 'function') {
        form.requestSubmit(button);
        return;
      }

      form.submit();
    });
  });

  ['iprCreateForm', 'iprEditForm'].forEach(formId => {
    const form = document.getElementById(formId);
    if (!form) {
      return;
    }

    form.addEventListener(
      'invalid',
      () => {
        window.setTimeout(() => {
          const firstInvalid = form.querySelector(':invalid');
          if (firstInvalid && typeof firstInvalid.scrollIntoView === 'function') {
            firstInvalid.scrollIntoView({ block: 'center' });
          }
        }, 0);
      },
      true
    );
  });
})();
