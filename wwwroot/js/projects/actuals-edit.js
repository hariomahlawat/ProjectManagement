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

  const formatLocalDate = (value) => {
    if (!value) return '—';
    const [year, month, day] = value.split('-').map(Number);
    if (!year || !month || !day) return value;
    return new Date(Date.UTC(year, month - 1, day)).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  };

  // SECTION: Chronology graph
  // Mirrors the server (StageDateSuggestionResolver.ResolveBranchBoundaries and
  // ActualTimelineValidationService.FindViolations) on the workflow's dependency graph:
  // skipped predecessors are transparent, every completed predecessor bounds its
  // successor, and only violations absent from the official record block a submission.
  const normalizeCode = (code) => (code ?? '').trim().toUpperCase();
  const rowByCode = new Map(rows.map((row) => [normalizeCode(row.dataset.stageCode), row]));
  const unrenderedStages = new Map();
  try {
    JSON.parse(form.dataset.actualsUnrenderedStages || '[]').forEach((stage) => {
      unrenderedStages.set(normalizeCode(stage.code), stage);
    });
  } catch {
    // A malformed projection only loses boundaries; the server still validates.
  }

  const inputOf = (row, field) => {
    const input = row.querySelector(`[data-field="${field}"]`);
    return input instanceof HTMLInputElement ? input : null;
  };
  const rowStatus = (row) => (row.dataset.stageStatus ?? '').toLowerCase();
  const rowName = (row) => row.dataset.stageName ?? row.dataset.stageCode ?? 'the effective predecessor';

  // Official values come from the markup; a proposal form is prefilled with proposed ones.
  const officialValue = (row, field) => {
    const attribute = field === 'start' ? 'data-official-start' : 'data-official-completed';
    if (row.hasAttribute(attribute)) {
      return row.getAttribute(attribute) ?? '';
    }
    return (initialValues.get(row.dataset.stageCode ?? '') ?? { start: '', completed: '' })[field];
  };
  // The server keeps the recorded completion when the field is posted blank.
  const factValue = (row, field, mode) => {
    if (mode === 'official') {
      return officialValue(row, field);
    }
    const value = inputOf(row, field)?.value ?? '';
    return field === 'completed' && !value ? officialValue(row, 'completed') : value;
  };

  const predecessorCodes = (code) => {
    const row = rowByCode.get(code);
    if (!row) {
      return (unrenderedStages.get(code)?.predecessors ?? []).map(normalizeCode).filter(Boolean);
    }
    if (row.hasAttribute('data-predecessors')) {
      return (row.dataset.predecessors ?? '').split(',').map(normalizeCode).filter(Boolean);
    }
    // Hosts that do not project the workflow graph fall back to display order.
    const index = rows.indexOf(row);
    return index > 0 ? [normalizeCode(rows[index - 1].dataset.stageCode)] : [];
  };

  const stageFacts = (code, mode) => {
    const row = rowByCode.get(code);
    if (row) {
      return { status: rowStatus(row), completed: factValue(row, 'completed', mode), name: rowName(row) };
    }
    const stage = unrenderedStages.get(code);
    return stage
      ? { status: (stage.status ?? '').toLowerCase(), completed: stage.completed ?? '', name: stage.name ?? stage.code }
      : null;
  };

  const branchBounds = (row, mode) => {
    const bounds = new Map();
    const collect = (code, path) => {
      if (path.has(code)) {
        return;
      }
      const facts = stageFacts(code, mode);
      if (!facts) {
        return;
      }

      path.add(code);
      if (facts.status === 'skipped') {
        predecessorCodes(code).forEach((ancestor) => collect(ancestor, path));
      } else if (facts.status === 'completed' && facts.completed && !bounds.has(code)) {
        bounds.set(code, { code, value: facts.completed, source: facts.name });
      }
      path.delete(code);
    };

    const self = normalizeCode(row.dataset.stageCode);
    predecessorCodes(self).forEach((code) => collect(code, new Set([self])));
    return Array.from(bounds.values());
  };

  // The server's controlling bound: latest completion, then stage code. ISO dates compare lexically.
  const controllingBound = (bounds) => bounds
    .slice()
    .sort((a, b) => (a.value === b.value ? a.code.localeCompare(b.code) : (a.value < b.value ? 1 : -1)))[0]
    ?? { code: '', value: '', source: '' };

  const resolveChronologyBoundary = (row) => controllingBound(branchBounds(row, 'final'));

  const participatesInChronology = (row) =>
    row.dataset.chronologyExcluded !== 'true'
    && ['inprogress', 'completed', 'blocked'].includes(rowStatus(row));

  // An explicit start is the chronology fact; a completed stage without one is bounded by its completion.
  const chronologyViolations = (row, mode) => {
    if (!participatesInChronology(row)) {
      return [];
    }
    const start = factValue(row, 'start', mode);
    const date = start || (rowStatus(row) === 'completed' ? factValue(row, 'completed', mode) : '');
    if (!date) {
      return [];
    }
    return branchBounds(row, mode)
      .filter((bound) => date < bound.value)
      .map((bound) => ({ ...bound, key: `${Boolean(start)}|${date}|${bound.code}|${bound.value}` }));
  };

  // Violations already in the official record are historical warnings on the server, not errors.
  const officialViolationKeys = new Map(rows.map((row) =>
    [row, new Set(chronologyViolations(row, 'official').map((violation) => violation.key))]));
  const newChronologyViolations = (row) => chronologyViolations(row, 'final')
    .filter((violation) => !officialViolationKeys.get(row).has(violation.key));

  const isRowChanged = (row) => {
    const original = initialValues.get(row.dataset.stageCode ?? '') ?? { start: '', completed: '' };
    return (inputOf(row, 'start')?.value ?? '') !== original.start
      || (inputOf(row, 'completed')?.value ?? '') !== original.completed;
  };

  const refreshChronologyBoundaries = () => {
    rows.forEach((row) => {
      const boundary = resolveChronologyBoundary(row);
      // No native `min` where the server tolerates the dates (an excluded row, or an
      // untouched row whose only violations are historical); it would block the post.
      const tolerated = !participatesInChronology(row)
        || (!isRowChanged(row)
          && chronologyViolations(row, 'final').length > 0
          && newChronologyViolations(row).length === 0);
      const min = tolerated ? '' : boundary.value;

      const startInput = inputOf(row, 'start');
      const completedInput = inputOf(row, 'completed');
      if (startInput) {
        startInput.min = min;
      }
      if (completedInput) {
        completedInput.min = min;
      }
    });
  };

  const validateRow = (row) => {
    const code = row.dataset.stageCode ?? '';
    const original = initialValues.get(code) ?? { start: '', completed: '' };
    const startInput = row.querySelector('[data-field="start"]');
    const completedInput = row.querySelector('[data-field="completed"]');
    const status = (row.dataset.stageStatus ?? '').toLowerCase();
    const boundary = resolveChronologyBoundary(row);
    const earliestStartValue = row.dataset.chronologyExcluded === 'true' ? '' : boundary.value;
    const startSource = boundary.source || row.dataset.startSource || 'the effective predecessor';

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
      // Only chronology applies to an untouched row (C7.2): an edit elsewhere can
      // still introduce a violation here, which the server would reject.
      const conflict = controllingBound(newChronologyViolations(row));
      setRowError(row, conflict.code
        ? `Actual dates cannot precede ${conflict.source} completion (${formatLocalDate(conflict.value)}). Same-day commencement is permitted. Adjust this stage's dates or ${conflict.source}'s completion.`
        : '');
      return !conflict.code;
    }

    const startDate = parseDate(startValue);
    const completedDate = parseDate(completedValue);
    const earliestStartDate = parseDate(earliestStartValue);

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

    const chronologyDate = startDate || completedDate;
    if (!error && chronologyDate && earliestStartDate && chronologyDate < earliestStartDate) {
      error = `Actual dates cannot precede ${startSource} completion (${formatLocalDate(earliestStartValue)}). Same-day commencement is permitted.`;
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

  const hasChanges = () => resubmitRequired || noteChanged() || rows.some((row) => {
    const code = row.dataset.stageCode ?? '';
    const original = initialValues.get(code) ?? { start: '', completed: '' };
    const startInput = row.querySelector('[data-field="start"]');
    const completedInput = row.querySelector('[data-field="completed"]');
    const startValue = startInput instanceof HTMLInputElement ? startInput.value : '';
    const completedValue = completedInput instanceof HTMLInputElement ? completedInput.value : '';

    return startValue !== original.start || completedValue !== original.completed;
  });

  const refreshState = () => {
    refreshChronologyBoundaries();
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
    // Validate every row (no short-circuit) so each row's message stays current.
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
