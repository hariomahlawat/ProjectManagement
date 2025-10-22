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

function initFilterDrawerAccessibility() {
  const drawerEl = document.getElementById('tot-filter-drawer');
  if (!drawerEl) {
    return;
  }

  const trigger = document.querySelector('[data-bs-target="#tot-filter-drawer"]');
  if (trigger) {
    drawerEl.addEventListener('show.bs.offcanvas', () => {
      trigger.setAttribute('aria-expanded', 'true');
    });

    drawerEl.addEventListener('hidden.bs.offcanvas', () => {
      trigger.setAttribute('aria-expanded', 'false');
      if (typeof trigger.focus === 'function') {
        trigger.focus();
      }
    });
  }

  drawerEl.addEventListener('shown.bs.offcanvas', () => {
    const searchInput = document.getElementById('tot-search-term');
    if (searchInput) {
      searchInput.focus();
      if (typeof searchInput.select === 'function') {
        searchInput.select();
      }
    }
  });
}

function initAutoShowModals() {
  const modals = document.querySelectorAll('[data-tot-auto-show="true"]');
  modals.forEach(showModalElement);
}

function init() {
  initFilterDrawerAccessibility();
  initAutoShowModals();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
  init();
}
