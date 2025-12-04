(() => {
  const filterModalElement = document.getElementById('iprFiltersModal');
  const filterTrigger = document.querySelector('[data-ipr-filter-trigger]');
  if (!filterModalElement || !filterTrigger || typeof bootstrap === 'undefined' || !bootstrap.Modal) {
    return;
  }

  filterModalElement.addEventListener('show.bs.modal', () => {
    filterTrigger.setAttribute('aria-expanded', 'true');
  });

  filterModalElement.addEventListener('shown.bs.modal', () => {
    const initialFocus = filterModalElement.querySelector('[data-ipr-filter-initial-focus]');
    if (initialFocus) {
      initialFocus.focus();
    }
  });

  filterModalElement.addEventListener('hidden.bs.modal', () => {
    filterTrigger.setAttribute('aria-expanded', 'false');
    filterTrigger.focus();
  });
})();

(() => {
  const confirmForms = document.querySelectorAll('form[data-ipr-confirm]');
  if (confirmForms.length === 0) {
    return;
  }

  confirmForms.forEach(form => {
    form.addEventListener('submit', event => {
      const message = form.getAttribute('data-ipr-confirm') || 'Are you sure?';

      if (!window.confirm(message)) {
        event.preventDefault();
        event.stopImmediatePropagation();
      }
    });
  });
})();

(() => {
  const toastContainer = document.getElementById('iprToastContainer');
  if (!toastContainer || typeof bootstrap === 'undefined' || !bootstrap.Toast) {
    return;
  }

  const toasts = toastContainer.querySelectorAll('.toast');
  toasts.forEach(toastElement => {
    const instance = bootstrap.Toast.getOrCreateInstance(toastElement);
    instance.show();
  });
})();

(() => {
  const skeleton = document.querySelector('[data-ipr-loading-skeleton]');
  const contentTargets = document.querySelectorAll('[data-ipr-table-content]');
  if (!skeleton || contentTargets.length === 0) {
    return;
  }

  const showSkeleton = () => {
    skeleton.classList.remove('d-none');
    contentTargets.forEach(element => {
      element.classList.add('d-none');
    });
  };

  const hideSkeleton = () => {
    skeleton.classList.add('d-none');
    contentTargets.forEach(element => {
      element.classList.remove('d-none');
    });
  };

  hideSkeleton();

  document.querySelectorAll('form[data-ipr-loading-form]').forEach(form => {
    form.addEventListener('submit', () => {
      showSkeleton();
    });
  });

  document.querySelectorAll('[data-ipr-loading-link]').forEach(link => {
    link.addEventListener('click', () => {
      showSkeleton();
    });
  });
})();
