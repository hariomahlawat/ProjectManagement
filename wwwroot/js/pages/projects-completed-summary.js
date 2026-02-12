/* ---------- SECTION: Completed summary filters auto-submit ---------- */
(function () {
  const form = document.getElementById('csFiltersForm');
  if (!form) {
    return;
  }

  /* ---------- SECTION: Auto-submit eligible controls ---------- */
  const elements = form.querySelectorAll('select[data-auto-submit="change"]');
  if (!elements.length) {
    return;
  }

  let isSubmitting = false;

  /* ---------- SECTION: Safe submit helper ---------- */
  const submitForm = () => {
    if (isSubmitting) {
      return;
    }
    isSubmitting = true;

    // Prefer requestSubmit so HTML5 constraints and default submit behaviour apply consistently.
    if (form.requestSubmit) {
      form.requestSubmit();
      return;
    }

    form.submit();
  };

  /* ---------- SECTION: Event bindings ---------- */
  elements.forEach((el) => {
    el.addEventListener('change', () => {
      submitForm();
    });
  });
})();
