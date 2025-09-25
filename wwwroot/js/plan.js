(() => {
  const form = document.getElementById('plan-draft-form');
  if (!form) {
    return;
  }

  const metadataSource = document.getElementById('plan-calculator-data');
  if (!metadataSource) {
    return;
  }

  let metadata;
  try {
    metadata = JSON.parse(metadataSource.value ?? metadataSource.textContent ?? '{}');
  } catch (err) {
    console.error('Failed to parse plan metadata', err);
    return;
  }

  const stages = Array.isArray(metadata.stages) ? metadata.stages.slice().sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)) : [];
  const dependencies = Array.isArray(metadata.dependencies) ? metadata.dependencies : [];

  if (stages.length === 0) {
    return;
  }

  const stageIndex = new Map();
  stages.forEach((stage) => {
    if (stage && typeof stage.code === 'string') {
      stageIndex.set(stage.code, stage);
    }
  });

  const dependencyMap = new Map();
  dependencies.forEach((dep) => {
    if (!dep || typeof dep.from !== 'string' || typeof dep.on !== 'string') {
      return;
    }

    const key = dep.from;
    if (!dependencyMap.has(key)) {
      dependencyMap.set(key, []);
    }

    dependencyMap.get(key).push(dep.on);
  });

  const allowEdit = form.dataset.allowEdit === 'true';
  const anchorStageInput = form.querySelector('[name="Input.AnchorStageCode"]');
  const anchorDateInput = form.querySelector('[name="Input.AnchorDate"]');
  const transitionRuleInput = form.querySelector('[name="Input.TransitionRule"]');
  const skipWeekendsInput = form.querySelector('[name="Input.SkipWeekends"]');
  const pncApplicableInput = form.querySelector('[name="Input.PncApplicable"]');
  const durationInputs = Array.from(form.querySelectorAll('input[name$=".DurationDays"]'));
  const manualStartInputs = Array.from(form.querySelectorAll('input[data-manual-start]'));
  const manualDueInputs = Array.from(form.querySelectorAll('input[data-manual-due]'));
  const unlockManualToggle = form.querySelector('[name="Input.UnlockManualDates"]');

  const startSpans = new Map();
  const dueSpans = new Map();
  form.querySelectorAll('.plan-start[data-stage-code]').forEach((el) => {
    startSpans.set(el.dataset.stageCode, el);
  });
  form.querySelectorAll('.plan-due[data-stage-code]').forEach((el) => {
    dueSpans.set(el.dataset.stageCode, el);
  });

  const formatDate = (date) => {
    if (!(date instanceof Date)) {
      return '–';
    }

    return new Intl.DateTimeFormat('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric'
    }).format(date);
  };

  const parseDate = (value) => {
    if (!value) {
      return null;
    }

    const parts = value.split('-');
    if (parts.length !== 3) {
      return null;
    }

    const year = Number.parseInt(parts[0], 10);
    const month = Number.parseInt(parts[1], 10);
    const day = Number.parseInt(parts[2], 10);

    if (Number.isNaN(year) || Number.isNaN(month) || Number.isNaN(day)) {
      return null;
    }

    return new Date(Date.UTC(year, month - 1, day));
  };

  const addDays = (date, days) => {
    const result = new Date(date.getTime());
    result.setUTCDate(result.getUTCDate() + days);
    return result;
  };

  const isWeekend = (date) => {
    const day = date.getUTCDay();
    return day === 0 || day === 6;
  };

  const nextWorkday = (date) => {
    let current = new Date(date.getTime());
    while (isWeekend(current)) {
      current = addDays(current, 1);
    }
    return current;
  };

  const normalizeDate = (date, skipWeekends) => {
    if (!skipWeekends || !(date instanceof Date)) {
      return date;
    }
    return nextWorkday(date);
  };

  const calculateDue = (start, duration, skipWeekends) => {
    if (duration <= 0) {
      return new Date(start.getTime());
    }

    let due = new Date(start.getTime());
    let remaining = duration - 1;

    while (remaining > 0) {
      due = addDays(due, 1);
      if (skipWeekends) {
        while (isWeekend(due)) {
          due = addDays(due, 1);
        }
      }
      remaining -= 1;
    }

    return normalizeDate(due, skipWeekends);
  };

  const calculateEarliestStart = (stageCode, results, included, skipWeekends, transitionRule) => {
    const deps = dependencyMap.get(stageCode);
    if (!deps || deps.length === 0) {
      return null;
    }

    let earliest = null;

    deps.forEach((dependencyCode) => {
      if (!included.has(dependencyCode)) {
        return;
      }

      const dependency = results.get(dependencyCode);
      if (!dependency) {
        return;
      }

      let candidate = dependency.due;
      if (transitionRule === 'NextWorkingDay') {
        candidate = addDays(candidate, 1);
      }
      candidate = normalizeDate(candidate, skipWeekends);

      if (!earliest || candidate.getTime() > earliest.getTime()) {
        earliest = candidate;
      }
    });

    return earliest;
  };

  const calculateSchedule = (options) => {
    const { anchorStageCode, anchorDate, skipWeekends, transitionRule, pncApplicable, durations, manualOverrides } = options;
    const anchorStage = stageIndex.get(anchorStageCode);

    if (!anchorStage || !(anchorDate instanceof Date)) {
      return new Map();
    }

    const includedStages = new Set(stages.map((s) => s.code));
    if (!pncApplicable) {
      includedStages.delete('PNC');
    }

    const results = new Map();
    const anchorSequence = anchorStage.sequence ?? 0;

    stages.forEach((stage) => {
      if ((stage.sequence ?? 0) < anchorSequence) {
        return;
      }

      if (!includedStages.has(stage.code)) {
        return;
      }

      const duration = Math.max(0, durations.get(stage.code) ?? 0);
      const manualOverride = manualOverrides.get(stage.code);
      let start;

      if (stage.code === anchorStageCode) {
        start = normalizeDate(manualOverride?.start ?? anchorDate, skipWeekends);
      } else {
        const earliest = calculateEarliestStart(stage.code, results, includedStages, skipWeekends, transitionRule);
        let candidate = earliest ? new Date(earliest.getTime()) : normalizeDate(anchorDate, skipWeekends);

        if (manualOverride?.start instanceof Date) {
          const manualStart = normalizeDate(manualOverride.start, skipWeekends);
          if (!candidate || manualStart.getTime() > candidate.getTime()) {
            candidate = manualStart;
          }
        }

        start = candidate;
      }

      if (!(start instanceof Date)) {
        return;
      }

      let due = duration <= 0 ? new Date(start.getTime()) : calculateDue(start, duration, skipWeekends);
      if (manualOverride?.due instanceof Date) {
        let manualDue = normalizeDate(manualOverride.due, skipWeekends);
        if (manualDue.getTime() < start.getTime()) {
          manualDue = new Date(start.getTime());
        }
        due = manualDue;
      }

      results.set(stage.code, { start, due });
    });

    return results;
  };

  const readOptions = () => {
    const anchorStageCode = anchorStageInput?.value?.trim();
    const anchorDateValue = anchorDateInput?.value ?? '';
    const anchorDate = parseDate(anchorDateValue);
    const transitionRule = transitionRuleInput?.value === 'SameDay' ? 'SameDay' : 'NextWorkingDay';
    const skipWeekends = !!(skipWeekendsInput && skipWeekendsInput.checked);
    const pncApplicable = !!(pncApplicableInput ? pncApplicableInput.checked : true);

    if (!anchorStageCode || !(anchorDate instanceof Date)) {
      return null;
    }

    const durations = new Map();
    durationInputs.forEach((input) => {
      const stageCode = input.name.split('.')[1]?.split(']')[0];
      const code = input.closest('tr')?.dataset.stageCode || stageCode;
      if (!code) {
        return;
      }

      const value = Number.parseInt(input.value, 10);
      durations.set(code, Number.isNaN(value) ? 0 : value);
    });

    const manualOverrides = new Map();
    manualStartInputs.forEach((input) => {
      const code = input.dataset.manualStart;
      if (!code) {
        return;
      }

      const startDate = parseDate(input.value);
      if (startDate instanceof Date) {
        const existing = manualOverrides.get(code) ?? {};
        existing.start = startDate;
        manualOverrides.set(code, existing);
      }
    });

    manualDueInputs.forEach((input) => {
      const code = input.dataset.manualDue;
      if (!code) {
        return;
      }

      const dueDate = parseDate(input.value);
      if (dueDate instanceof Date) {
        const existing = manualOverrides.get(code) ?? {};
        existing.due = dueDate;
        manualOverrides.set(code, existing);
      }
    });

    return {
      anchorStageCode,
      anchorDate,
      skipWeekends,
      transitionRule,
      pncApplicable,
      durations,
      manualOverrides
    };
  };

  const updateManualInputState = () => {
    const unlocked = allowEdit && unlockManualToggle && unlockManualToggle.checked;
    manualStartInputs.forEach((input) => {
      if (unlocked) {
        input.removeAttribute('readonly');
      } else {
        input.setAttribute('readonly', 'readonly');
      }
    });

    manualDueInputs.forEach((input) => {
      if (unlocked) {
        input.removeAttribute('readonly');
      } else {
        input.setAttribute('readonly', 'readonly');
      }
    });
  };

  const clearOutputs = () => {
    startSpans.forEach((el) => {
      el.textContent = '–';
    });
    dueSpans.forEach((el) => {
      el.textContent = '–';
    });
  };

  const recalculate = () => {
    const options = readOptions();
    if (!options) {
      clearOutputs();
      return;
    }

    const results = calculateSchedule(options);

    stages.forEach((stage) => {
      const schedule = results.get(stage.code);
      const startEl = startSpans.get(stage.code);
      const dueEl = dueSpans.get(stage.code);

      if (startEl) {
        startEl.textContent = schedule ? formatDate(schedule.start) : '–';
      }

      if (dueEl) {
        dueEl.textContent = schedule ? formatDate(schedule.due) : '–';
      }
    });
  };

  const relevantInputs = [
    anchorStageInput,
    anchorDateInput,
    transitionRuleInput,
    skipWeekendsInput,
    pncApplicableInput,
    unlockManualToggle,
    ...durationInputs,
    ...manualStartInputs,
    ...manualDueInputs
  ].filter(Boolean);

  relevantInputs.forEach((input) => {
    input.addEventListener('change', recalculate);
    input.addEventListener('input', recalculate);
  });

  if (unlockManualToggle) {
    unlockManualToggle.addEventListener('change', updateManualInputState);
  }

  updateManualInputState();
  recalculate();
})();
