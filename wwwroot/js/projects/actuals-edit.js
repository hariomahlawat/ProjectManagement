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


  const clearTargetedRow = () => {
    rows.forEach((row) => row.classList.remove('is-targeted'));
  };

  const focusStageRow = (stageCode) => {
    clearTargetedRow();
    if (!stageCode) {
      return;
    }

    const row = rows.find((candidate) =>
      (candidate.dataset.stageCode ?? '').toLowerCase() === stageCode.toLowerCase());
    if (!(row instanceof HTMLElement)) {
      return;
    }

    row.classList.add('is-targeted');
    row.scrollIntoView({ behavior: 'smooth', block: 'center' });

    const startInput = row.querySelector('[data-field="start"]');
    if (startInput instanceof HTMLInputElement && !startInput.disabled) {
      window.setTimeout(() => startInput.focus({ preventScroll: true }), 180);
    }
  };

  offcanvas.addEventListener('shown.bs.offcanvas', (event) => {
    const trigger = event.relatedTarget;
    const stageCode = trigger instanceof Element
      ? trigger.getAttribute('data-actuals-focus-stage') ?? ''
      : '';
    focusStageRow(stageCode);
  });

  offcanvas.addEventListener('hidden.bs.offcanvas', clearTargetedRow);

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

  // Cross-stage chronology (workflow graph, skipped ancestry, convergence, historical
  // violations, pending-proposal baselines) is decided only by the server's shared
  // Actual validator. A rejected post reopens this editor with the entered values and
  // the server's stage-specific messages, so no native `min` is set here (C7.2).

  const validateRow = (row) => {
    const code = row.dataset.stageCode ?? '';
    const original = initialValues.get(code) ?? { start: '', completed: '' };
    const startInput = row.querySelector('[data-field="start"]');
    const completedInput = row.querySelector('[data-field="completed"]');
    const status = (row.dataset.stageStatus ?? '').toLowerCase();

    const startValue = startInput instanceof HTMLInputElement ? startInput.value : '';
    const completedValue = completedInput instanceof HTMLInputElement ? completedInput.value : '';
    const isChanged = startValue !== original.start || completedValue !== original.completed;
    // Clearing is relative to the official start. A proposal form is prefilled with
    // proposed values, so a pending clear (blank start over a recorded one) keeps
    // its explicit intent on resubmission.
    const officialStart = row.dataset.officialStart ?? original.start;
    const clearStartInput = row.querySelector('[data-clear-start]');
    if (clearStartInput instanceof HTMLInputElement) {
      clearStartInput.value = officialStart && !startValue ? 'true' : 'false';
    }

    if (!isChanged) {
      setRowError(row, '');
      return true;
    }

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
          error = 'This stage has not started yet. Update the status first.';
          break;
        case 'skipped':
          error = 'Skipped stages cannot record actual dates.';
          break;
        case 'inprogress':
          if (!startValue) {
            error = 'Actual start is required while the stage is InProgress; transition the stage before clearing it.';
          } else if (completedValue) {
            error = 'Use the stage-transition workflow to complete this stage; completion cannot be recorded here while InProgress.';
          }
          break;
        case 'completed':
          break;
        default:
          error = 'Actual dates can only be edited when the stage is InProgress or Completed.';
          break;
      }
    }

    setRowError(row, error);
    return !error;
  };

  // A proposal's note is content too: revising only the reason is a resubmission.
  const noteInput = form.querySelector('[data-actuals-note]');
  const initialNote = noteInput instanceof HTMLTextAreaElement ? noteInput.value : '';
  const noteChanged = () => noteInput instanceof HTMLTextAreaElement && noteInput.value.trim() !== initialNote.trim();

  // A pending proposal whose official baseline changed must be resubmittable as-is: the
  // server records it again against the new official dates (it cannot be approved otherwise).
  const resubmitRequired = form.dataset.actualsResubmit === 'true';

  // Values restored after the server refused a post are the user's own entries, not the
  // recorded dates, so they can be submitted again once corrected (or as they are).
  const restoredSubmission = form.dataset.actualsRestored === 'true';

  const hasChanges = () => resubmitRequired || restoredSubmission || noteChanged() || rows.some((row) => {
    const code = row.dataset.stageCode ?? '';
    const original = initialValues.get(code) ?? { start: '', completed: '' };
    const startInput = row.querySelector('[data-field="start"]');
    const completedInput = row.querySelector('[data-field="completed"]');
    const startValue = startInput instanceof HTMLInputElement ? startInput.value : '';
    const completedValue = completedInput instanceof HTMLInputElement ? completedInput.value : '';

    return startValue !== original.start || completedValue !== original.completed;
  });

  const refreshState = () => {
    // Validate every row (no short-circuit) so each row's message stays current.
    const allValid = rows.map(validateRow).every(Boolean);
    if (saveButton instanceof HTMLButtonElement) {
      saveButton.disabled = !allValid || !hasChanges();
    }
  };

  form.addEventListener('input', (event) => {
    if (event.target === noteInput) {
      refreshState();
      return;
    }

    if (!(event.target instanceof HTMLInputElement)) {
      return;
    }

    const row = event.target.closest('[data-actuals-row]');
    if (!row) {
      return;
    }

    refreshState();
  });

  form.addEventListener('submit', (event) => {
    const allValid = rows.map(validateRow).every(Boolean);
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

  // A withdrawal posts once; a second click would only report "already withdrawn".
  const withdrawForm = document.getElementById('actuals-withdraw-form');
  if (withdrawForm instanceof HTMLFormElement) {
    withdrawForm.addEventListener('submit', (event) => {
      if (event.defaultPrevented) {
        return;
      }

      form.querySelectorAll('[data-actuals-withdraw]').forEach((button) => {
        if (button instanceof HTMLButtonElement) {
          button.disabled = true;
        }
      });
    });
  }

  refreshState();
})();
