function ensureBootstrapModal() {
  if (typeof window === 'undefined') {
    return null;
  }

  const bootstrap = window.bootstrap;
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

function initFilterModalAccessibility() {
  const modalEl = document.getElementById('proliferation-filter-modal');
  if (!modalEl) {
    return;
  }

  const trigger = document.querySelector('[data-bs-target="#proliferation-filter-modal"]');
  if (trigger) {
    modalEl.addEventListener('show.bs.modal', () => {
      trigger.setAttribute('aria-expanded', 'true');
    });

    modalEl.addEventListener('hidden.bs.modal', () => {
      trigger.setAttribute('aria-expanded', 'false');
      if (typeof trigger.focus === 'function') {
        trigger.focus();
      }
    });
  }

  modalEl.addEventListener('shown.bs.modal', () => {
    const searchInput = document.getElementById('proliferation-search');
    if (searchInput) {
      searchInput.focus();
      if (typeof searchInput.select === 'function') {
        searchInput.select();
      }
    }
  });
}

function initAutoShowModals() {
  const modals = document.querySelectorAll('[data-proliferation-auto-show="true"]');
  modals.forEach(showModalElement);
}

function init() {
  initFilterModalAccessibility();
  initAutoShowModals();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
  init();
}
