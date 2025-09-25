(() => {
  const form = document.getElementById('plan-draft-form');
  if (!form) {
    return;
  }

  const rows = form.querySelectorAll('tbody tr');
  rows.forEach((row) => {
    const startInput = row.querySelector('input[name$=".PlannedStart"]');
    const dueInput = row.querySelector('input[name$=".PlannedDue"]');

    if (!startInput || !dueInput || startInput.disabled || dueInput.disabled) {
      return;
    }

    const syncMin = () => {
      if (startInput.value) {
        dueInput.setAttribute('min', startInput.value);
      } else {
        dueInput.removeAttribute('min');
      }
    };

    const validate = () => {
      if (startInput.value && dueInput.value && dueInput.value < startInput.value) {
        dueInput.setCustomValidity('Planned due must be on or after the planned start date.');
      } else {
        dueInput.setCustomValidity('');
      }
    };

    const handleStartChange = () => {
      syncMin();
      validate();
    };

    const handleDueChange = () => {
      validate();
    };

    startInput.addEventListener('input', handleStartChange);
    startInput.addEventListener('change', handleStartChange);
    dueInput.addEventListener('input', handleDueChange);
    dueInput.addEventListener('change', handleDueChange);

    syncMin();
    validate();
  });
})();
