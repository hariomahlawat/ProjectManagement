(function () {
  const offcanvas = document.getElementById('offcanvasActualDates');
  if (!offcanvas) {
    return;
  }

  const form = offcanvas.querySelector('[data-actuals-form]');
  if (!form) {
    return;
  }

  const saveButton = form.querySelector('[data-actuals-save]');
  const rows = Array.from(form.querySelectorAll('[data-actuals-row]'));
  const todayValue = form.dataset.today ?? '';
  const today = todayValue ? new Date(todayValue) : null;

  const initialValues = new Map();
  rows.forEach((row) => {
    const code = row.dataset.stageCode ?? '';
    const startInput = row.querySelector('[data-field="start"]');
    const completedInput = row.querySelector('[data-field="completed"]');
    initialValues.set(code, {
      start: startInput instanceof HTMLInputElement ? startInput.value : '',
      completed: completedInput instanceof HTMLInputElement ? completedInput.value : ''
    });
  });

  const setRowError = (row, message) => {
    const errorEl = row.querySelector('[data-row-error]');
    if (!(errorEl instanceof HTMLElement)) {
      return;
    }

    if (!message) {
      errorEl.classList.add('d-none');
      errorEl.textContent = '';
    } else {
      errorEl.classList.remove('d-none');
      errorEl.textContent = message;
    }
  };

  const parseDate = (value) => {
    if (!value) {
      return null;
    }

    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  };

  const validateRow = (row) => {
    const startInput = row.querySelector('[data-field="start"]');
    const completedInput = row.querySelector('[data-field="completed"]');
    const status = (row.dataset.stageStatus ?? '').toLowerCase();

    const startValue = startInput instanceof HTMLInputElement ? startInput.value : '';
    const completedValue = completedInput instanceof HTMLInputElement ? completedInput.value : '';

    const startDate = parseDate(startValue);
    const completedDate = parseDate(completedValue);

    let error = '';

    if (today) {
      if (startDate && startDate > today) {
        error = 'Start date cannot be in the future.';
      }

      if (!error && completedDate && completedDate > today) {
        error = 'Completion date cannot be in the future.';
      }
    }

    if (!error && startDate && completedDate && startDate > completedDate) {
      error = 'Completion must be on or after the start date.';
    }

    if (!error) {
      switch (status) {
        case 'notstarted':
          if (startValue || completedValue) {
            error = 'This stage has not started yet. Update the status first.';
          }
          break;
        case 'skipped':
          if (startValue || completedValue) {
            error = 'Skipped stages cannot record actual dates.';
          }
          break;
        case 'inprogress':
        case 'blocked':
          if (completedValue && !startValue) {
            error = 'Add a start date before recording completion.';
          }
          break;
        case 'completed':
          if (!startValue || !completedValue) {
            error = 'Completed stages need both start and completion dates.';
          }
          break;
        default:
          break;
      }
    }

    setRowError(row, error);
    return !error;
  };

  const hasChanges = () => rows.some((row) => {
    const code = row.dataset.stageCode ?? '';
    const original = initialValues.get(code) ?? { start: '', completed: '' };
    const startInput = row.querySelector('[data-field="start"]');
    const completedInput = row.querySelector('[data-field="completed"]');
    const startValue = startInput instanceof HTMLInputElement ? startInput.value : '';
    const completedValue = completedInput instanceof HTMLInputElement ? completedInput.value : '';

    return startValue !== original.start || completedValue !== original.completed;
  });

  const refreshState = () => {
    const allValid = rows.every(validateRow);
    if (saveButton instanceof HTMLButtonElement) {
      saveButton.disabled = !allValid || !hasChanges();
    }
  };

  form.addEventListener('input', (event) => {
    if (!(event.target instanceof HTMLInputElement)) {
      return;
    }

    const row = event.target.closest('[data-actuals-row]');
    if (!row) {
      return;
    }

    validateRow(row);
    refreshState();
  });

  form.addEventListener('submit', (event) => {
    const allValid = rows.every(validateRow);
    if (!allValid) {
      event.preventDefault();
      const firstError = form.querySelector('[data-row-error]:not(.d-none)');
      if (firstError instanceof HTMLElement) {
        firstError.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }
      return;
    }

    if (saveButton instanceof HTMLButtonElement) {
      saveButton.disabled = true;
      saveButton.setAttribute('data-loading', 'true');
    }
  });

  refreshState();
})();
