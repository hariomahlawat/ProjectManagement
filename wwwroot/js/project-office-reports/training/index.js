import { initAsyncMultiselect } from '../../widgets/async-multiselect.js';

function ensureBootstrapModal() {
  if (typeof window === 'undefined') {
    return null;
  }

  const { bootstrap } = window;
  return bootstrap && typeof bootstrap.Modal === 'function' ? bootstrap : null;
}

function showModalElement(modalEl) {
  if (!modalEl) {
    return;
  }

  const bootstrap = ensureBootstrapModal();
  if (bootstrap) {
    bootstrap.Modal.getOrCreateInstance(modalEl).show();
    return;
  }

  modalEl.classList.add('show');
  modalEl.style.display = 'block';
  modalEl.removeAttribute('aria-hidden');
  modalEl.setAttribute('aria-modal', 'true');
}

function hideModalElement(modalEl) {
  if (!modalEl) {
    return;
  }

  const bootstrap = ensureBootstrapModal();
  if (bootstrap) {
    const instance = bootstrap.Modal.getOrCreateInstance(modalEl);
    instance.hide();
    return;
  }

  modalEl.classList.remove('show');
  modalEl.style.removeProperty('display');
  modalEl.setAttribute('aria-hidden', 'true');
  modalEl.removeAttribute('aria-modal');
}

function updateButtonToBusyState(button) {
  const busyLabel = button.getAttribute('data-training-busy-label');
  if (!busyLabel) {
    return;
  }

  button.dataset.trainingOriginalHtml = button.innerHTML;

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

    const submitter = event.submitter || form.querySelector('[type="submit"]');
    if (!submitter || submitter.disabled) {
      return;
    }

    submitter.disabled = true;
    updateButtonToBusyState(submitter);

    const modalEl = form.closest('.modal');
    if (modalEl) {
      hideModalElement(modalEl);
    }
  });
}

function initAutoShowExportModal() {
  const modals = document.querySelectorAll('[data-training-export-auto-show="true"]');
  modals.forEach(showModalElement);
}

function initTrainingExportForms() {
  const forms = document.querySelectorAll('.training-export-form');
  forms.forEach(disableFormOnSubmit);
}

function init() {
  initAsyncMultiselect();
  initAutoShowExportModal();
  initTrainingExportForms();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
  init();
}
