const toastContainerId = 'visitsToastContainer';

function ensureBootstrap() {
  return typeof window !== 'undefined' && window.bootstrap && typeof window.bootstrap.Toast === 'function'
    ? window.bootstrap
    : null;
}

function ensureToastHost() {
  let host = document.getElementById(toastContainerId);
  if (host) {
    return host;
  }

  host = document.createElement('div');
  host.id = toastContainerId;
  host.className = 'toast-container position-fixed top-0 end-0 p-3';
  host.setAttribute('aria-live', 'polite');
  host.setAttribute('aria-atomic', 'true');
  document.body.appendChild(host);
  return host;
}

function createToastElement(message, variant) {
  const toast = document.createElement('div');
  toast.className = `toast align-items-center text-bg-${variant} border-0`;
  toast.setAttribute('role', 'status');
  toast.setAttribute('aria-live', 'polite');
  toast.setAttribute('aria-atomic', 'true');

  const wrapper = document.createElement('div');
  wrapper.className = 'd-flex';

  const body = document.createElement('div');
  body.className = 'toast-body';
  body.textContent = message;

  const dismiss = document.createElement('button');
  dismiss.type = 'button';
  dismiss.className = 'btn-close btn-close-white me-2 m-auto';
  dismiss.setAttribute('data-bs-dismiss', 'toast');
  dismiss.setAttribute('aria-label', 'Dismiss notification');

  wrapper.append(body, dismiss);
  toast.append(wrapper);
  return toast;
}

function showToast(message, variant) {
  const bootstrap = ensureBootstrap();
  if (!bootstrap || !message) {
    return;
  }

  const host = ensureToastHost();
  const toastEl = createToastElement(message, variant);
  host.appendChild(toastEl);

  const toastInstance = bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 5000 });
  toastEl.addEventListener('hidden.bs.toast', () => {
    toastInstance.dispose();
    toastEl.remove();
  }, { once: true });

  toastInstance.show();
}

function initToasts() {
  const containers = document.querySelectorAll('[data-visits-toast-container]');
  containers.forEach(container => {
    const items = container.querySelectorAll('[data-visits-toast]');
    items.forEach(item => {
      const message = item.textContent?.trim();
      if (!message) {
        return;
      }

      const variant = item.getAttribute('data-variant') || 'primary';
      showToast(message, variant);
    });
  });
}

function updateButtonToBusyState(button) {
  const busyLabel = button.getAttribute('data-visits-busy-label');
  if (!busyLabel) {
    return;
  }

  button.dataset.visitsOriginalHtml = button.innerHTML;
  const spinner = document.createElement('span');
  spinner.className = 'spinner-border spinner-border-sm me-2';
  spinner.setAttribute('role', 'status');
  spinner.setAttribute('aria-hidden', 'true');

  const label = document.createElement('span');
  label.textContent = busyLabel;

  button.innerHTML = '';
  button.append(spinner, label);
}

function disableFormOnSubmit(form) {
  form.addEventListener('submit', event => {
    if (event.defaultPrevented) {
      return;
    }

    const submitter = form.querySelector('[type="submit"]');
    if (!submitter || submitter.disabled) {
      return;
    }

    submitter.disabled = true;
    updateButtonToBusyState(submitter);
  });
}

function initDisableOnSubmit() {
  const forms = document.querySelectorAll('form[data-visits-disable-on-submit]');
  forms.forEach(disableFormOnSubmit);
}

const confirmModalId = 'visitsConfirmModal';

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
  modal.className = 'modal fade visits-confirm-modal';
  modal.tabIndex = -1;
  modal.setAttribute('aria-hidden', 'true');
  modal.innerHTML = `
    <div class="modal-dialog modal-dialog-centered">
      <div class="modal-content border-0 shadow-lg">
        <button type="button" class="btn-close visits-confirm-modal__close" data-bs-dismiss="modal" aria-label="Cancel"></button>
        <div class="modal-body p-4">
          <div class="d-flex align-items-start gap-3">
            <div class="visits-confirm-modal__icon" aria-hidden="true">!</div>
            <div class="flex-grow-1">
              <h5 class="visits-confirm-modal__title mb-1">Review before deleting</h5>
              <p class="visits-confirm-modal__message mb-3" data-visits-confirm-message></p>
              <p class="visits-confirm-modal__subtitle mb-0">This action can't be undone.</p>
            </div>
          </div>
        </div>
        <div class="modal-footer border-0 pt-0">
          <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal" data-visits-confirm-cancel>Keep record</button>
          <button type="button" class="btn btn-danger" data-visits-confirm-accept>Delete</button>
        </div>
      </div>
    </div>
  `;

  document.body.appendChild(modal);
  return modal;
}

function showConfirmModal(message) {
  const bootstrap = ensureBootstrapModal();
  if (!bootstrap) {
    return Promise.resolve(window.confirm(message));
  }

  const modalEl = ensureConfirmModalElement();
  const instance = bootstrap.Modal.getOrCreateInstance(modalEl, {
    backdrop: 'static',
    keyboard: true,
    focus: true
  });

  const messageEl = modalEl.querySelector('[data-visits-confirm-message]');
  if (messageEl) {
    messageEl.textContent = message;
  }

  const confirmButton = modalEl.querySelector('[data-visits-confirm-accept]');
  const cancelButton = modalEl.querySelector('[data-visits-confirm-cancel]');

  return new Promise(resolve => {
    let handled = false;

    const cleanup = () => {
      confirmButton?.removeEventListener('click', handleConfirm);
      cancelButton?.removeEventListener('click', handleCancel);
      modalEl.removeEventListener('hidden.bs.modal', handleHidden);
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
  });
}

function attachConfirmDialog(form) {
  const message = form.getAttribute('data-confirm');
  if (!message) {
    return;
  }

  form.addEventListener('submit', event => {
    if (form.dataset.visitsConfirmBypassed === 'true') {
      delete form.dataset.visitsConfirmBypassed;
      return;
    }

    event.preventDefault();

    const submitter = event.submitter;

    showConfirmModal(message).then(confirmed => {
      if (!confirmed) {
        return;
      }

      form.dataset.visitsConfirmBypassed = 'true';

      if (typeof form.requestSubmit === 'function') {
        form.requestSubmit(submitter);
        return;
      }

      if (submitter && typeof submitter.click === 'function') {
        submitter.click();
        return;
      }

      form.submit();
    });
  });
}

function initConfirmations() {
  const forms = document.querySelectorAll('form[data-confirm]');
  forms.forEach(attachConfirmDialog);
}

function init() {
  initToasts();
  initConfirmations();
  initDisableOnSubmit();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
  init();
}
