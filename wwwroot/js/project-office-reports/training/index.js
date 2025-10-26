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

function initAutoShowExportModal() {
  const modals = document.querySelectorAll('[data-training-export-auto-show="true"]');
  modals.forEach(showModalElement);
}

function init() {
  initAsyncMultiselect();
  initAutoShowExportModal();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
  init();
}
