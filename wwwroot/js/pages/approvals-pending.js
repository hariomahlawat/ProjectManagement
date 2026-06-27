const form = document.querySelector('[data-decision-filter-form]');

if (form) {
  let searchTimer = null;
  const search = form.querySelector('input[type="search"]');

  form.querySelectorAll('[data-auto-submit]').forEach((control) => {
    control.addEventListener('change', () => {
      const pageField = form.querySelector('[name="PageNumber"]');
      if (pageField) pageField.value = '1';
      form.requestSubmit();
    });
  });

  if (search) {
    search.addEventListener('input', () => {
      window.clearTimeout(searchTimer);
      searchTimer = window.setTimeout(() => {
        if (search.value.trim().length === 0 || search.value.trim().length >= 2) {
          form.requestSubmit();
        }
      }, 450);
    });
  }
}
