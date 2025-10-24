(() => {
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-manage-ipr-delete]');
    if (!button) {
      return;
    }

    const title = button.getAttribute('data-record-title');
    const display = title && title.trim().length > 0 ? `"${title.trim()}"` : 'this record';
    if (!window.confirm(`Delete ${display}? This action cannot be undone.`)) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
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
