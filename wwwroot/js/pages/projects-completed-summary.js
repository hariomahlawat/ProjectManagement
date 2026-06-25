(function () {
  'use strict';

  const form = document.getElementById('csFiltersForm');
  if (!form) return;

  let submitting = false;
  const submitForm = () => {
    if (submitting) return;
    submitting = true;
    if (typeof form.requestSubmit === 'function') form.requestSubmit();
    else form.submit();
  };

  form.querySelectorAll('[data-auto-submit="change"]').forEach((element) => {
    element.addEventListener('change', submitForm);
  });

  form.querySelectorAll('[data-submit-on-enter]').forEach((element) => {
    element.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        submitForm();
      }
    });
    element.addEventListener('change', submitForm);
  });

  const searchInput = form.querySelector('[data-search-input]');
  const searchField = searchInput?.closest('.cs-search-field');
  const clearSearch = form.querySelector('[data-clear-search]');
  let searchTimer;

  const syncSearchState = () => {
    searchField?.classList.toggle('has-value', Boolean(searchInput?.value.trim()));
  };

  if (searchInput) {
    syncSearchState();
    searchInput.addEventListener('input', () => {
      syncSearchState();
      window.clearTimeout(searchTimer);
      searchTimer = window.setTimeout(submitForm, 500);
    });
    searchInput.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        window.clearTimeout(searchTimer);
        submitForm();
      }
    });
  }

  clearSearch?.addEventListener('click', () => {
    if (!searchInput) return;
    searchInput.value = '';
    syncSearchState();
    submitForm();
  });

  const toggle = document.getElementById('csFilterToggle');
  const advanced = document.getElementById('csAdvancedFilters');
  toggle?.addEventListener('click', () => {
    if (!advanced) return;
    const isOpen = !advanced.hasAttribute('hidden');
    if (isOpen) advanced.setAttribute('hidden', '');
    else advanced.removeAttribute('hidden');
    toggle.setAttribute('aria-expanded', String(!isOpen));
  });

  const modal = document.getElementById('csRemarksModal');
  const modalBody = document.getElementById('csRemarksBody');
  let lastTrigger = null;

  const closeModal = () => {
    if (!modal) return;
    modal.setAttribute('hidden', '');
    document.body.classList.remove('cs-modal-open');
    lastTrigger?.focus();
  };

  document.querySelectorAll('[data-remarks]').forEach((button) => {
    button.addEventListener('click', () => {
      if (!modal || !modalBody) return;
      lastTrigger = button;
      modalBody.textContent = button.getAttribute('data-remarks') || '';
      modal.removeAttribute('hidden');
      document.body.classList.add('cs-modal-open');
      modal.querySelector('[data-close-remarks]')?.focus();
    });
  });

  modal?.querySelectorAll('[data-close-remarks]').forEach((element) => {
    element.addEventListener('click', closeModal);
  });

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && modal && !modal.hasAttribute('hidden')) closeModal();
  });
})();
