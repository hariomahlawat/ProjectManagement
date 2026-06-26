(function () {
  const DIRECT_DATE_REQUIRED_STATUSES = new Set(['InProgress']);
  const REQUEST_DATE_REQUIRED_STATUSES = new Set(['InProgress', 'Completed']);

  const STAGE_FLASH_KEY = 'prism.stage.flash';

  function escapeSelector(value) {
    if (typeof value !== 'string') {
      return '';
    }

    if (window.CSS && typeof window.CSS.escape === 'function') {
      return window.CSS.escape(value);
    }

    return value.replace(/([\0-\x1f\x7f-\x9f\s!"#$%&'()*+,./:;<=>?@\[\\\]\^`{|}~])/g, '\\$1');
  }

  function statusLabel(status) {
    switch (status) {
      case 'Completed':
        return 'Completed';
      case 'InProgress':
        return 'In progress';
      case 'Blocked':
        return 'Blocked';
      case 'Skipped':
        return 'Skipped';
      case 'NotStarted':
        return 'Not started';
      default:
        return status || '';
    }
  }

  function formatDate(isoDate) {
    if (!isoDate) {
      return '—';
    }

    const [year, month, day] = isoDate.split('-').map((part) => parseInt(part, 10));
    if (!year || !month || !day) {
      return '—';
    }

    const date = new Date(Date.UTC(year, month - 1, day));
    return date.toLocaleDateString(undefined, {
      day: '2-digit',
      month: 'short',
      year: 'numeric'
    });
  }

  function calculateDurationDays(startIso, endIso) {
    if (!startIso || !endIso) {
      return null;
    }

    const start = new Date(`${startIso}T00:00:00Z`);
    const end = new Date(`${endIso}T00:00:00Z`);
    const diff = end.getTime() - start.getTime();
    if (Number.isNaN(diff) || diff < 0) {
      return null;
    }

    return Math.floor(diff / 86400000) + 1;
  }

  function postJson(path, body, token) {
    if (!path) {
      throw new Error('Path is required');
    }

    const headers = {
      'Content-Type': 'application/json'
    };

    if (token) {
      headers.RequestVerificationToken = token;
    }

    let requestUrl = path;
    if (typeof window !== 'undefined' && window.location) {
      try {
        const url = new URL(path, window.location.origin);
        if (url.host === window.location.host) {
          url.protocol = window.location.protocol;
        }
        requestUrl = url.toString();
      } catch (error) {
        console.warn('Failed to normalise request URL. Using raw path.', error);
      }
    }

    return fetch(requestUrl, {
      method: 'POST',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'same-origin'
    });
  }

  function badgeClass(status) {
    switch ((status || '').toLowerCase()) {
      case 'completed':
        return 'pm-pill pm-pill-success';
      case 'inprogress':
        return 'pm-pill pm-pill-primary';
      case 'blocked':
        return 'pm-pill pm-pill-warning';
      case 'skipped':
        return 'pm-pill pm-pill-muted';
      default:
        return 'pm-pill pm-pill-muted';
    }
  }

  function ensureToastContainer() {
    let container = document.getElementById('directApplyToastContainer');
    if (!container) {
      container = document.createElement('div');
      container.id = 'directApplyToastContainer';
      container.className = 'toast-container position-fixed top-0 end-0 p-3';
      container.setAttribute('aria-live', 'polite');
      container.setAttribute('aria-atomic', 'true');
      document.body.appendChild(container);
    }

    return container;
  }

  function showToast(message, variant) {
    const container = ensureToastContainer();
    const toastEl = document.createElement('div');
    toastEl.className = `toast align-items-center text-bg-${variant} border-0`;
    toastEl.role = 'status';

    const wrapper = document.createElement('div');
    wrapper.className = 'd-flex';

    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = message;

    const dismiss = document.createElement('button');
    dismiss.type = 'button';
    dismiss.className = 'btn-close btn-close-white me-2 m-auto';
    dismiss.setAttribute('data-bs-dismiss', 'toast');
    dismiss.setAttribute('aria-label', 'Close');

    wrapper.appendChild(body);
    wrapper.appendChild(dismiss);
    toastEl.appendChild(wrapper);
    container.appendChild(toastEl);
    const toast = bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 4000 });
    toastEl.addEventListener('hidden.bs.toast', () => {
      toast.dispose();
      toastEl.remove();
    });
    toast.show();
  }

  function queueStageRefresh(message, variant = 'success') {
    try {
      window.sessionStorage.setItem(STAGE_FLASH_KEY, JSON.stringify({ message, variant }));
    } catch (error) {
      console.warn('Unable to persist stage update message.', error);
    }

    window.location.reload();
  }

  function showQueuedStageMessage() {
    let stored = null;
    try {
      stored = window.sessionStorage.getItem(STAGE_FLASH_KEY);
      if (stored) {
        window.sessionStorage.removeItem(STAGE_FLASH_KEY);
      }
    } catch (error) {
      console.warn('Unable to restore stage update message.', error);
    }

    if (!stored) return;

    try {
      const payload = JSON.parse(stored);
      if (payload?.message) {
        showToast(payload.message, payload.variant || 'success');
      }
    } catch (error) {
      console.warn('Invalid stage update message.', error);
    }
  }

  function todayIso() {
    const today = new Date();
    const month = `${today.getMonth() + 1}`.padStart(2, '0');
    const day = `${today.getDate()}`.padStart(2, '0');
    return `${today.getFullYear()}-${month}-${day}`;
  }

  function computeNeedsStart(status, actualStart) {
    const normalizedStatus = typeof status === 'string' ? status.trim().toLowerCase() : '';
    return normalizedStatus === 'inprogress' && !actualStart;
  }

  function computeNeedsFinish(status, completedOn) {
    const normalizedStatus = typeof status === 'string' ? status.trim().toLowerCase() : '';
    return normalizedStatus === 'completed' && !completedOn;
  }

  function computeIncompleteState(status, actualStart, completedOn, requiresBackfill) {
    const needsStart = computeNeedsStart(status, actualStart);
    const needsFinish = computeNeedsFinish(status, completedOn);
    return Boolean(requiresBackfill || needsStart || needsFinish);
  }

  function describeVariance(name, value) {
    const magnitude = Math.abs(value);
    if (value === 0) {
      return {
        text: `${name} on time`,
        title: `${name} on time`,
        cssClass: 'pm-chip pm-chip-on-time'
      };
    }

    const sign = value > 0 ? '+' : '−';
    const direction = value > 0 ? 'late' : 'early';
    const plural = magnitude === 1 ? '' : 's';
    return {
      text: `${name} ${sign}${magnitude}d`,
      title: `${name} ${magnitude} day${plural} ${direction}`,
      cssClass: value > 0 ? 'pm-chip pm-chip-late' : 'pm-chip pm-chip-early'
    };
  }

  function updateStageRow(stageCode, payloadStatus, updated) {
    if (!stageCode) return;
    const row = document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`);
    if (!row) return;

    const updatedStatus = (updated && updated.status) ? updated.status : payloadStatus;
    const statusText = updatedStatus || payloadStatus;
    const actualStartIso = updated?.actualStart ?? null;
    const completedIso = updated?.completedOn ?? null;
    const requiresBackfill = Boolean(updated?.requiresBackfill);
    const startVariance = typeof updated?.startVarianceDays === 'number' ? updated.startVarianceDays : null;
    const finishVariance = typeof updated?.finishVarianceDays === 'number' ? updated.finishVarianceDays : null;

    const normalizedStatus = typeof statusText === 'string' ? statusText.trim().toLowerCase() : '';
    row.classList.remove('is-complete', 'is-active');
    if (normalizedStatus === 'completed') {
      row.classList.add('is-complete');
    } else if (normalizedStatus === 'inprogress') {
      row.classList.add('is-active');
    }

    if (requiresBackfill) {
      row.setAttribute('data-requires-backfill', 'true');
    } else {
      row.removeAttribute('data-requires-backfill');
    }

    const badge = row.querySelector('[data-stage-status-pill]');
    if (badge) {
      badge.textContent = statusLabel(statusText);
      badge.className = badgeClass(statusText || '');
      badge.setAttribute('aria-label', `Stage status: ${statusLabel(statusText)}`);
    }

    const actualSpan = row.querySelector('[data-stage-actual-start]');
    if (actualSpan) {
      actualSpan.textContent = formatDate(actualStartIso);
    }

    const completedSpan = row.querySelector('[data-stage-completed]');
    if (completedSpan) {
      completedSpan.textContent = formatDate(completedIso);
    }

    const durationSpan = row.querySelector('[data-stage-duration]');
    if (durationSpan) {
      const days = calculateDurationDays(actualStartIso, completedIso);
      if (days) {
        durationSpan.textContent = `(${days} d)`;
        durationSpan.classList.remove('d-none');
      } else {
        durationSpan.textContent = '';
        durationSpan.classList.add('d-none');
      }
    }

    const triggers = row.querySelectorAll('[data-direct-apply]');
    triggers.forEach((trigger) => {
      const triggerStatus = trigger.getAttribute('data-status');
      if (triggerStatus === 'Completed') {
        if (completedIso) {
          trigger.setAttribute('data-default-date', completedIso);
        } else {
          trigger.removeAttribute('data-default-date');
        }
      } else if (triggerStatus === 'InProgress') {
        if (actualStartIso) {
          trigger.setAttribute('data-default-date', actualStartIso);
        } else {
          trigger.removeAttribute('data-default-date');
        }
      }
    });

    const needsStart = computeNeedsStart(statusText, actualStartIso);
    const needsFinish = computeNeedsFinish(statusText, completedIso);

    const incompleteBadge = row.querySelector('[data-stage-incomplete]');
    if (incompleteBadge) {
      const showIncomplete = computeIncompleteState(updatedStatus, actualStartIso, completedIso, requiresBackfill);
      incompleteBadge.classList.toggle('d-none', !showIncomplete);
    }

    const dateHint = row.querySelector('[data-stage-date-hint]');
    if (dateHint) {
      if (needsStart && needsFinish) {
        dateHint.textContent = 'Actual start and finish missing';
        dateHint.classList.remove('d-none');
      } else if (needsStart) {
        dateHint.textContent = 'Actual start missing';
        dateHint.classList.remove('d-none');
      } else if (needsFinish) {
        dateHint.textContent = 'Actual finish missing';
        dateHint.classList.remove('d-none');
      } else {
        dateHint.textContent = '';
        dateHint.classList.add('d-none');
      }
    }

    const backfillPill = row.querySelector('[data-stage-backfill-pill]');
    if (backfillPill) {
      backfillPill.classList.toggle('d-none', !requiresBackfill);
    }

    const pendingFlag = row.querySelector('[data-stage-pending]');
    if (pendingFlag && pendingFlag.classList.contains('pm-flag')) {
      if (updated?.pendingStatus) {
        const textParts = ['Awaiting HoD approval'];
        if (updated.pendingDate) {
          textParts.push(`· ${formatDate(updated.pendingDate)}`);
        }
        pendingFlag.textContent = textParts.join(' ');
        pendingFlag.classList.remove('d-none');
      } else if (!updated?.pendingStatus && !pendingFlag.textContent.trim()) {
        pendingFlag.classList.add('d-none');
      }
    }

    const startChip = row.querySelector('[data-stage-start-variance]');
    if (startChip) {
      if (startVariance === null || startVariance === undefined || Number.isNaN(startVariance)) {
        startChip.classList.add('d-none');
        startChip.textContent = '';
      } else {
        const details = describeVariance('Start', startVariance);
        startChip.textContent = details.text;
        startChip.className = details.cssClass;
        startChip.setAttribute('title', details.title);
        startChip.setAttribute('aria-label', details.title);
        startChip.classList.remove('d-none');
      }
    }

    const finishChip = row.querySelector('[data-stage-finish-variance]');
    if (finishChip) {
      if (finishVariance === null || finishVariance === undefined || Number.isNaN(finishVariance)) {
        finishChip.classList.add('d-none');
        finishChip.textContent = '';
      } else {
        const details = describeVariance('Finish', finishVariance);
        finishChip.textContent = details.text;
        finishChip.className = details.cssClass;
        finishChip.setAttribute('title', details.title);
        finishChip.setAttribute('aria-label', details.title);
        finishChip.classList.remove('d-none');
      }
    }

    const hasAnyBackfill = document.querySelector('[data-stage-backfill-pill]:not(.d-none)') !== null;
    document.dispatchEvent(new CustomEvent('pm:backfill-state-changed', {
      detail: { hasBackfill: hasAnyBackfill }
    }));
  }

  function updatePendingBadge(stageCode, status, dateIso) {
    if (!stageCode) return;
    const row = document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`);
    if (!row) return;

    const container = row.querySelector('[data-stage-pending]');
    if (!container) return;

    if (!status) {
      container.textContent = '';
      container.classList.add('d-none');
      return;
    }

    const parts = ['Awaiting HoD approval'];
    if (dateIso) {
      const formatted = formatDate(dateIso);
      if (formatted && formatted !== '—') {
        parts.push(`· ${formatted}`);
      }
    }

    container.textContent = parts.join(' ');
    container.classList.remove('d-none');
  }

  function disableRequestButton(stageCode) {
    if (!stageCode) return;
    const row = document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`);
    if (!row) return;

    const buttons = row.querySelectorAll('[data-stage-request-button]');
    buttons.forEach((button) => {
      button.disabled = true;
      button.classList.add('disabled');
    });
  }

  function enableRequestButton(stageCode) {
    if (!stageCode) return;
    const row = document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`);
    if (!row) return;

    const buttons = row.querySelectorAll('[data-stage-request-button]');
    buttons.forEach((button) => {
      button.disabled = false;
      button.classList.remove('disabled');
    });
  }

  function updateStageRequestsContainer(listElement) {
    const card = document.querySelector('[data-stage-requests-card]');
    if (!card) {
      return;
    }

    const list = listElement || card.querySelector('[data-stage-decision-list]');
    const emptyState = card.querySelector('[data-stage-requests-empty]');
    const hasItems = Boolean(list && list.querySelector('[data-stage-request-item]'));

    if (hasItems) {
      if (emptyState) {
        emptyState.classList.add('d-none');
      }
      return;
    }

    if (emptyState) {
      emptyState.classList.remove('d-none');
    }

    card.remove();
  }

  function renderErrors(container, messages) {
    if (!container) return;
    const hasErrors = Array.isArray(messages) && messages.length > 0;
    if (!hasErrors) {
      container.classList.add('d-none');
      container.textContent = '';
      return;
    }

    const list = document.createElement('ul');
    list.className = 'mb-0';
    messages.forEach((message) => {
      const item = document.createElement('li');
      item.textContent = message;
      list.appendChild(item);
    });
    container.replaceChildren(list);
    container.classList.remove('d-none');
  }

  function renderMissingPredecessors(container, predecessors) {
    if (!container) return;
    const hasItems = Array.isArray(predecessors) && predecessors.length > 0;
    if (!hasItems) {
      container.classList.add('d-none');
      container.replaceChildren();
      return;
    }

    const header = document.createElement('div');
    header.className = 'fw-semibold mb-1';
    header.textContent = 'Predecessors not yet completed:';

    const list = document.createElement('ul');
    list.className = 'mb-0 ps-3';

    predecessors.forEach((predecessor) => {
      const item = document.createElement('li');
      item.textContent = predecessor;
      list.appendChild(item);
    });

    container.replaceChildren(header, list);
    container.classList.remove('d-none');
  }

  function handleWarnings(warnings) {
    if (!Array.isArray(warnings)) return;
    warnings.forEach((warning) => {
      if (warning) {
        showToast(warning, 'warning');
      }
    });
  }

  function bootDirectApply() {
    const modalEl = document.getElementById('stageDirectApplyModal');
    if (!modalEl) {
      return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    const form = modalEl.querySelector('[data-direct-apply-form]');
    const stageLabel = modalEl.querySelector('[data-direct-apply-stage]');
    const statusLabelEl = modalEl.querySelector('[data-direct-apply-status]');
    const dateInput = modalEl.querySelector('input[name="date"]');
    const noteInput = modalEl.querySelector('textarea[name="note"]');
    const projectInput = modalEl.querySelector('input[name="projectId"]');
    const stageInput = modalEl.querySelector('input[name="stageCode"]');
    const statusInput = modalEl.querySelector('input[name="status"]');
    const tokenInput = modalEl.querySelector('input[name="__RequestVerificationToken"]');
    const errorContainer = modalEl.querySelector('[data-direct-apply-errors]');
    const dateHint = modalEl.querySelector('[data-direct-apply-date-hint]');
    const dateLabel = modalEl.querySelector('[data-direct-apply-date-label]');
    const missingPredecessorsContainer = modalEl.querySelector('[data-direct-apply-missing]');
    const forceGroup = modalEl.querySelector('[data-direct-apply-force-group]');
    const forceCheckbox = modalEl.querySelector('[data-direct-apply-force]');
    const forceHint = modalEl.querySelector('[data-direct-apply-force-hint]');
    const submitButton = modalEl.querySelector('[data-direct-apply-submit]');
    const noDateWarning = modalEl.querySelector('[data-direct-apply-no-date-warning]');
    const actorCanOverride = modalEl.getAttribute('data-authorised-override') === 'true';
    let activeStatus = '';

    function updateSubmitButton(status) {
      if (!submitButton) return;
      if (status === 'Completed') {
        submitButton.textContent = actorCanOverride && !dateInput?.value
          ? 'Complete and create backfill'
          : 'Complete stage';
        return;
      }
      submitButton.textContent = status === 'InProgress'
        ? 'Start stage'
        : status === 'Skipped'
          ? 'Skip stage'
          : status === 'Reopen'
            ? 'Reopen stage'
            : 'Apply change';
    }

    function updateNoDateWarning(status) {
      if (!noDateWarning || !dateInput) return;
      const shouldShow = actorCanOverride && status === 'Completed' && !dateInput.value;
      noDateWarning.classList.toggle('d-none', !shouldShow);
      updateSubmitButton(status);
    }

    function applyDateState(status, { resetValue = false } = {}) {
      if (!dateInput) return false;
      const requiresDate = DIRECT_DATE_REQUIRED_STATUSES.has(status)
        || (status === 'Completed' && !actorCanOverride);
      dateInput.required = requiresDate;

      if (dateLabel) {
        dateLabel.textContent = status === 'InProgress'
          ? 'Started on'
          : status === 'Completed'
            ? 'Completed on'
            : status === 'Reopen'
              ? 'Reopened on'
              : 'Effective date';
      }

      if (resetValue) {
        dateInput.value = requiresDate ? todayIso() : '';
      }
      if (dateHint) {
        dateHint.textContent = status === 'Completed' && actorCanOverride
          ? 'Optional for an authorised override; omitting it creates mandatory backfill.'
          : requiresDate
            ? 'Required'
            : 'Optional';
      }
      updateNoDateWarning(status);
      return requiresDate;
    }

    function setForceGroupVisible(visible) {
      if (!forceGroup) return;
      forceGroup.classList.toggle('d-none', !visible);
      if (!visible && forceCheckbox) {
        forceCheckbox.checked = false;
      }
    }

    function setForceHintHighlighted(highlighted) {
      if (!forceHint) return;
      forceHint.classList.toggle('text-danger', highlighted);
      forceHint.classList.toggle('fw-semibold', highlighted);
    }

    if (forceCheckbox) {
      forceCheckbox.addEventListener('change', () => {
        if (forceCheckbox.checked) {
          setForceHintHighlighted(false);
        }
      });
    }

    document.addEventListener('click', (event) => {
      const trigger = event.target.closest('[data-direct-apply]');
      if (!trigger) {
        return;
      }

      event.preventDefault();

      const projectId = trigger.getAttribute('data-project') || '';
      const stageCode = trigger.getAttribute('data-stage') || '';
      const status = trigger.getAttribute('data-status') || '';
      const stageName = trigger.getAttribute('data-stage-name') || '';
      const defaultDate = trigger.getAttribute('data-default-date') || '';
      activeStatus = status;

      if (projectInput) projectInput.value = projectId;
      if (stageInput) stageInput.value = stageCode;
      if (statusInput) statusInput.value = status;
      if (stageLabel) stageLabel.textContent = `${stageCode ? stageCode + ' — ' : ''}${stageName}`.trim();
      if (statusLabelEl) statusLabelEl.textContent = `Change to ${statusLabel(status)}`;
      if (errorContainer) {
        errorContainer.classList.add('d-none');
        errorContainer.textContent = '';
      }
      renderMissingPredecessors(missingPredecessorsContainer, []);
      if (noteInput) {
        noteInput.value = '';
      }
      setForceGroupVisible(false);
      if (forceCheckbox) {
        forceCheckbox.checked = false;
      }
      setForceHintHighlighted(false);

      const requiresDate = applyDateState(status, { resetValue: true });
      if (dateInput) {
        dateInput.value = defaultDate || (requiresDate ? todayIso() : '');
      }
      updateNoDateWarning(status);

      if (submitButton) {
        submitButton.disabled = false;
      }

      modal.show();
    });

    if (!form) {
      return;
    }

    if (dateInput) {
      dateInput.addEventListener('input', () => {
        updateNoDateWarning(activeStatus);
      });
    }

    const resetControls = () => {
      renderMissingPredecessors(missingPredecessorsContainer, []);
      setForceGroupVisible(false);
      setForceHintHighlighted(false);
      if (submitButton) submitButton.disabled = false;
    };


    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!projectInput || !stageInput || !statusInput || !tokenInput) {
        return;
      }

      if (submitButton) {
        submitButton.disabled = true;
      }

      if (errorContainer) {
        errorContainer.classList.add('d-none');
        errorContainer.textContent = '';
      }

      const payload = {
        projectId: Number.parseInt(projectInput.value, 10),
        stageCode: stageInput.value,
        status: statusInput.value,
        date: dateInput && dateInput.value ? dateInput.value : null,
        note: noteInput && noteInput.value ? noteInput.value.trim() : null,
        forceBackfillPredecessors: !!(forceCheckbox?.checked)
      };

      if (!payload.projectId) {
        payload.projectId = 0;
      }

      const stageCode = stageInput.value;
      const row = stageCode ? document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`) : null;
      if (row) {
        row.setAttribute('data-loading', 'true');
      }

      try {
        const response = await postJson('/Projects/Stages/ApplyChange', payload, tokenInput.value);

        if (response.status === 422) {
          const data = await response.json().catch(() => null);
          const messages = data?.details || ['Validation failed.'];
          renderErrors(errorContainer, messages);
          const missing = Array.isArray(data?.missingPredecessors) ? data.missingPredecessors : [];
          renderMissingPredecessors(missingPredecessorsContainer, missing);
          setForceGroupVisible(missing.length > 0);
          if (missing.length > 0) {
            const shouldHighlight = !forceCheckbox || !forceCheckbox.checked;
            setForceHintHighlighted(shouldHighlight);
            if (shouldHighlight && forceCheckbox) {
              forceCheckbox.focus();
            }
          } else {
            setForceHintHighlighted(false);
          }
          if (submitButton) submitButton.disabled = false;
          return;
        }

        if (response.status === 409) {
          const conflict = await response.json().catch(() => null);
          if (conflict?.error === 'stale') {
            showToast('Timeline changed since this page was loaded. Refreshing…', 'warning');
            setTimeout(() => {
              window.location.reload();
            }, 800);
          } else if (conflict?.error === 'plan-pending') {
            showToast('Blocked: plan version is awaiting HoD review.', 'warning');
          } else {
            showToast('Unable to update the stage right now.', 'danger');
          }
          resetControls();
          return;
        }

        if (response.status === 403) {
          showToast('You are not authorised to update project stages.', 'danger');
          resetControls();
          return;
        }

        if (response.status === 404) {
          showToast('Project or stage not found (stale UI).', 'warning');
          resetControls();
          return;
        }

        if (!response.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          resetControls();
          return;
        }

        const data = await response.json();
        if (!data?.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          resetControls();
          return;
        }

        modal.hide();

        const warnings = Array.isArray(data.warnings)
          ? data.warnings.filter((warning) => typeof warning === 'string' && warning.trim())
          : [];
        const authorisedOverride = warnings.some((warning) =>
          warning.toLowerCase().includes('authorised override'));
        const backfilledCount = Number(data.backfilled?.count || 0);
        const backfilledStages = Array.isArray(data.backfilled?.stages)
          ? data.backfilled.stages.filter(Boolean)
          : [];

        let message = 'Stage updated.';
        let variant = 'success';

        if (authorisedOverride) {
          message = 'Stage completed through an authorised override. Mandatory completion-date backfill has been created.';
          variant = 'warning';
        } else if (backfilledCount > 0) {
          const stageList = backfilledStages.length > 0 ? `: ${backfilledStages.join(', ')}` : '';
          message = `Stage updated. ${backfilledCount} predecessor stage${backfilledCount === 1 ? '' : 's'} completed with mandatory backfill${stageList}.`;
          variant = 'info';
        } else if (warnings.length > 0) {
          message = `Stage updated. ${warnings.join(' ')}`;
          variant = 'warning';
        }

        queueStageRefresh(message, variant);
      } catch (error) {
        console.error(error);
        showToast('Unable to update the stage right now.', 'danger');
        renderMissingPredecessors(missingPredecessorsContainer, []);
        setForceHintHighlighted(false);
      } finally {
        if (submitButton) submitButton.disabled = false;
        if (row) {
          row.removeAttribute('data-loading');
        }
      }
    });
  }


  function bootStageRequest() {
    const modalEl = document.getElementById('stageRequestModal');
    if (!modalEl) {
      return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    const form = modalEl.querySelector('[data-stage-request-form]');
    const projectInput = modalEl.querySelector('input[name="projectId"]');
    const tokenInput = modalEl.querySelector('input[name="__RequestVerificationToken"]');
    const rowsContainer = modalEl.querySelector('[data-stage-request-rows]');
    const rowTemplate = modalEl.querySelector('[data-stage-request-row-template]');
    const addButton = modalEl.querySelector('[data-stage-request-add]');
    const errorContainer = modalEl.querySelector('[data-stage-request-errors]');
    const conflictContainer = modalEl.querySelector('[data-stage-request-conflict]');
    const summaryList = modalEl.querySelector('[data-stage-request-summary]');
    const submitButton = modalEl.querySelector('[data-stage-request-submit]');
    const titleElement = modalEl.querySelector('[data-stage-update-title]');
    const subtitleElement = modalEl.querySelector('[data-stage-update-subtitle]');

    const optionsByCode = new Map();
    document.querySelectorAll('[data-stage-request-button]').forEach((button) => {
      const code = button.getAttribute('data-stage') || '';
      if (!code || optionsByCode.has(code)) return;
      optionsByCode.set(code, {
        code,
        name: button.getAttribute('data-stage-name') || code,
        currentStatus: button.getAttribute('data-current-status') || 'NotStarted',
        pendingStatus: button.getAttribute('data-pending-status') || '',
        pendingDate: button.getAttribute('data-pending-date') || '',
        pendingNote: button.getAttribute('data-pending-note') || ''
      });
    });
    const stageOptions = Array.from(optionsByCode.values());

    function transitionOptions(currentStatus) {
      switch (currentStatus) {
        case 'NotStarted':
          return [
            { value: 'InProgress', label: 'Start stage' },
            { value: 'Blocked', label: 'Mark blocked' },
            { value: 'Skipped', label: 'Skip stage' }
          ];
        case 'InProgress':
          return [
            { value: 'Completed', label: 'Complete stage' },
            { value: 'Blocked', label: 'Mark blocked' },
            { value: 'Skipped', label: 'Skip stage' }
          ];
        case 'Blocked':
          return [
            { value: 'InProgress', label: 'Resume stage' },
            { value: 'Skipped', label: 'Skip stage' }
          ];
        case 'Completed':
        case 'Skipped':
          return [{ value: 'InProgress', label: 'Reopen stage' }];
        default:
          return [{ value: 'InProgress', label: 'Start stage' }];
      }
    }

    function transitionLabel(currentStatus, targetStatus) {
      if (targetStatus === 'Completed') return 'Complete';
      if (targetStatus === 'Blocked') return 'Mark blocked';
      if (targetStatus === 'Skipped') return 'Skip';
      if (targetStatus === 'InProgress') {
        if (currentStatus === 'NotStarted') return 'Start';
        if (currentStatus === 'Blocked') return 'Resume';
        return 'Reopen';
      }
      return statusLabel(targetStatus) || 'Update';
    }

    function dateConfiguration(status) {
      switch (status) {
        case 'InProgress':
          return { label: 'Started on', required: true, hint: 'Required' };
        case 'Completed':
          return { label: 'Completed on', required: true, hint: 'Required' };
        case 'Blocked':
          return { label: 'Blocked on', required: false, hint: 'Optional' };
        case 'Skipped':
          return { label: 'Effective date', required: false, hint: 'Optional' };
        default:
          return { label: 'Effective date', required: false, hint: 'Optional' };
      }
    }

    function requiresNote(currentStatus, targetStatus, rowCount) {
      return rowCount > 1
        || targetStatus === 'Blocked'
        || targetStatus === 'Skipped'
        || (targetStatus === 'InProgress' && ['Blocked', 'Completed', 'Skipped'].includes(currentStatus));
    }

    function stageOptionFor(code) {
      return optionsByCode.get(code) || null;
    }

    function configureTargetSelect(row, currentStatus, preferredTarget) {
      const select = row.querySelector('[data-stage-request-target]');
      if (!select) return '';
      const allowed = transitionOptions(currentStatus);
      select.innerHTML = '';
      allowed.forEach((target) => {
        const option = document.createElement('option');
        option.value = target.value;
        option.textContent = target.label;
        select.appendChild(option);
      });
      const targetExists = allowed.some((target) => target.value === preferredTarget);
      select.value = targetExists ? preferredTarget : (allowed[0]?.value || '');
      return select.value;
    }

    function setDateState(row, status, { resetValue = false, preferredValue = '' } = {}) {
      const dateInput = row.querySelector('[data-stage-request-date]');
      const label = row.querySelector('[data-stage-request-date-label]');
      const hint = row.querySelector('[data-stage-request-date-hint]');
      const config = dateConfiguration(status);
      if (label) label.textContent = config.label;
      if (hint) hint.textContent = config.hint;
      if (dateInput) {
        dateInput.required = config.required;
        if (preferredValue) {
          dateInput.value = preferredValue;
        } else if (resetValue) {
          dateInput.value = config.required ? todayIso() : '';
        }
      }
    }

    function refreshNoteState() {
      if (!rowsContainer) return;
      const rows = Array.from(rowsContainer.querySelectorAll('[data-stage-request-row]'));
      rows.forEach((row) => {
        const stageSelect = row.querySelector('[data-stage-request-stage]');
        const targetSelect = row.querySelector('[data-stage-request-target]');
        const noteInput = row.querySelector('[data-stage-request-note]');
        const qualifier = row.querySelector('[data-stage-request-note-qualifier]');
        const hint = row.querySelector('[data-stage-request-note-hint]');
        const currentStatus = row.dataset.currentStatus || stageOptionFor(stageSelect?.value || '')?.currentStatus || 'NotStarted';
        const required = requiresNote(currentStatus, targetSelect?.value || '', rows.length);
        if (noteInput) noteInput.required = required;
        if (qualifier) qualifier.textContent = required ? '(required)' : '(optional)';
        if (hint) {
          hint.textContent = required
            ? 'Explain this exceptional or coordinated update for the HoD.'
            : 'Add context for the HoD where useful.';
        }
      });
    }

    function setRemoveButtonsState() {
      if (!rowsContainer) return;
      const rows = Array.from(rowsContainer.querySelectorAll('[data-stage-request-row]'));
      rows.forEach((row) => {
        const removeButton = row.querySelector('[data-stage-request-remove]');
        if (removeButton) {
          removeButton.disabled = rows.length === 1 || row.dataset.locked === 'true';
          removeButton.classList.toggle('d-none', rows.length === 1 || row.dataset.locked === 'true');
        }
      });
      refreshNoteState();
    }

    function populateStageSelect(select, selectedCode) {
      if (!select) return;
      select.innerHTML = '';
      stageOptions.forEach((opt) => {
        const option = document.createElement('option');
        option.value = opt.code;
        option.textContent = `${opt.code} — ${opt.name}`;
        option.selected = selectedCode === opt.code;
        select.appendChild(option);
      });
    }

    function updateRowFromStage(row, preferredTarget = '', preferredDate = '', preferredNote = '') {
      const stageSelect = row.querySelector('[data-stage-request-stage]');
      const stageCode = stageSelect?.value || '';
      const stage = stageOptionFor(stageCode);
      const currentStatus = stage?.currentStatus || 'NotStarted';
      row.dataset.currentStatus = currentStatus;

      const rowTitle = row.querySelector('[data-stage-request-row-title]');
      const currentState = row.querySelector('[data-stage-request-current-state]');
      if (rowTitle) rowTitle.textContent = stage?.name || 'Stage';
      if (currentState) currentState.textContent = `Current official status: ${statusLabel(currentStatus)}`;

      const selectedTarget = configureTargetSelect(row, currentStatus, preferredTarget);
      setDateState(row, selectedTarget, { resetValue: !preferredDate, preferredValue: preferredDate });

      const noteInput = row.querySelector('[data-stage-request-note]');
      if (noteInput && preferredNote) noteInput.value = preferredNote;
      refreshNoteState();
      renderSummary();
    }

    function renderSummary() {
      if (!summaryList || !rowsContainer) return;
      summaryList.innerHTML = '';
      const rows = Array.from(rowsContainer.querySelectorAll('[data-stage-request-row]'));
      if (rows.length === 0) {
        const empty = document.createElement('li');
        empty.textContent = 'No stages selected yet.';
        summaryList.appendChild(empty);
        return;
      }

      rows.forEach((row) => {
        const stageSelect = row.querySelector('[data-stage-request-stage]');
        const targetSelect = row.querySelector('[data-stage-request-target]');
        const dateInput = row.querySelector('[data-stage-request-date]');
        if (!stageSelect?.value || !targetSelect?.value) return;
        const stage = stageOptionFor(stageSelect.value);
        const action = transitionLabel(row.dataset.currentStatus || stage?.currentStatus || 'NotStarted', targetSelect.value);
        const config = dateConfiguration(targetSelect.value);
        const dateText = dateInput?.value ? formatDate(dateInput.value) : null;
        const item = document.createElement('li');
        item.textContent = `${stage?.name || stageSelect.value} — ${action}${dateText ? ` · ${config.label} ${dateText}` : ''}`;
        summaryList.appendChild(item);
      });
    }

    function createRow({ stageCode = '', targetStatus = '', requestedDate = '', note = '', locked = false } = {}) {
      if (!rowsContainer || !rowTemplate) return null;
      const fragment = rowTemplate.content.cloneNode(true);
      const row = fragment.querySelector('[data-stage-request-row]');
      if (!row) return null;
      row.dataset.locked = locked ? 'true' : 'false';

      const stageSelect = row.querySelector('[data-stage-request-stage]');
      const selectedCodes = rowsContainer
        ? new Set(Array.from(rowsContainer.querySelectorAll('[data-stage-request-stage]')).map((select) => select.value).filter(Boolean))
        : new Set();
      const defaultStageCode = stageCode || stageOptions.find((option) => !selectedCodes.has(option.code))?.code || stageOptions[0]?.code || '';
      populateStageSelect(stageSelect, defaultStageCode);
      if (stageSelect && locked) {
        stageSelect.disabled = true;
        stageSelect.setAttribute('aria-readonly', 'true');
      }

      rowsContainer.appendChild(fragment);
      updateRowFromStage(row, targetStatus, requestedDate, note);

      const removeButton = row.querySelector('[data-stage-request-remove]');
      removeButton?.addEventListener('click', () => {
        row.remove();
        setRemoveButtonsState();
        renderSummary();
      });

      stageSelect?.addEventListener('change', () => updateRowFromStage(row));

      const targetSelect = row.querySelector('[data-stage-request-target]');
      targetSelect?.addEventListener('change', () => {
        setDateState(row, targetSelect.value, { resetValue: true });
        refreshNoteState();
        renderSummary();
      });

      row.querySelector('[data-stage-request-date]')?.addEventListener('input', renderSummary);
      row.querySelector('[data-stage-request-note]')?.addEventListener('input', renderSummary);

      setRemoveButtonsState();
      renderSummary();
      return row;
    }

    document.addEventListener('click', (event) => {
      const trigger = event.target.closest('[data-stage-request]');
      if (!trigger || trigger.disabled) return;
      event.preventDefault();

      const projectId = trigger.getAttribute('data-project') || '';
      const stageCode = trigger.getAttribute('data-stage') || '';
      const stageName = trigger.getAttribute('data-stage-name') || stageCode || 'stage';
      const pendingStatus = trigger.getAttribute('data-pending-status') || '';
      const pendingDate = trigger.getAttribute('data-pending-date') || '';
      const pendingNote = trigger.getAttribute('data-pending-note') || '';
      const isEditingPending = Boolean(pendingStatus);

      if (projectInput) projectInput.value = projectId;
      if (rowsContainer) {
        rowsContainer.innerHTML = '';
        createRow({
          stageCode,
          targetStatus: pendingStatus,
          requestedDate: pendingDate,
          note: pendingNote,
          locked: true
        });
      }

      if (titleElement) titleElement.textContent = isEditingPending ? `Edit ${stageName} stage update` : `Update ${stageName} stage`;
      if (subtitleElement) {
        subtitleElement.textContent = isEditingPending
          ? 'Revise the update currently awaiting HoD approval.'
          : 'Submit the proposed status and date for HoD approval.';
      }
      conflictContainer?.classList.toggle('d-none', !isEditingPending);
      if (errorContainer) {
        errorContainer.classList.add('d-none');
        errorContainer.textContent = '';
      }
      if (submitButton) {
        submitButton.disabled = false;
        submitButton.textContent = isEditingPending ? 'Update submission' : 'Submit update';
      }
      modal.show();
    });

    addButton?.addEventListener('click', () => createRow());

    if (!form) return;

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!projectInput || !rowsContainer || !tokenInput) return;

      refreshNoteState();
      const invalidControl = form.querySelector(':invalid');
      if (invalidControl) {
        invalidControl.reportValidity();
        return;
      }

      if (submitButton) submitButton.disabled = true;
      if (errorContainer) {
        errorContainer.classList.add('d-none');
        errorContainer.textContent = '';
      }

      const stagesPayload = [];
      rowsContainer.querySelectorAll('[data-stage-request-row]').forEach((row) => {
        const stageSelect = row.querySelector('[data-stage-request-stage]');
        const targetSelect = row.querySelector('[data-stage-request-target]');
        const dateInput = row.querySelector('[data-stage-request-date]');
        const noteInput = row.querySelector('[data-stage-request-note]');
        if (!stageSelect?.value || !targetSelect?.value) return;
        stagesPayload.push({
          stageCode: stageSelect.value,
          requestedStatus: targetSelect.value,
          requestedDate: dateInput?.value || null,
          note: noteInput?.value?.trim() || null
        });
      });

      if (stagesPayload.length === 0) {
        renderErrors(errorContainer, ['Select at least one stage to update.']);
        if (submitButton) submitButton.disabled = false;
        return;
      }

      const stageCodes = stagesPayload.map((stage) => stage.stageCode.toLowerCase());
      const duplicateStage = stageCodes.find((code, index) => stageCodes.indexOf(code) !== index);
      if (duplicateStage) {
        renderErrors(errorContainer, ['Each stage can be included only once in an update.']);
        if (submitButton) submitButton.disabled = false;
        return;
      }

      const payload = {
        projectId: Number.parseInt(projectInput.value, 10) || 0,
        stages: stagesPayload
      };

      try {
        const response = await postJson('/Projects/Stages/RequestChange', payload, tokenInput.value);
        if (response.status === 403) {
          renderErrors(errorContainer, ['Only the Project Officer assigned to this project can submit stage updates.']);
          if (submitButton) submitButton.disabled = false;
          return;
        }
        if (response.status === 422) {
          const data = await response.json().catch(() => null);
          const details = Array.isArray(data?.details) ? data.details : [];
          const messages = details.length > 0
            ? details.flatMap((item) => {
              const stageCode = item?.stageCode || 'Stage';
              const errs = Array.isArray(item?.errors) && item.errors.length > 0 ? item.errors : ['Validation failed.'];
              const missing = Array.isArray(item?.missingPredecessors) ? item.missingPredecessors : [];
              const combined = errs.map((err) => `${stageCode}: ${err}`);
              if (missing.length > 0) combined.push(`${stageCode}: Complete required predecessor stages first (${missing.join(', ')})`);
              return combined;
            })
            : ['The stage update could not be submitted.'];
          if (Array.isArray(data?.errors)) messages.push(...data.errors);
          renderErrors(errorContainer, messages);
          if (submitButton) submitButton.disabled = false;
          return;
        }
        if (!response.ok) {
          renderErrors(errorContainer, ['Unable to submit the stage update right now.']);
          if (submitButton) submitButton.disabled = false;
          return;
        }
        const data = await response.json().catch(() => null);
        if (!data?.ok) {
          renderErrors(errorContainer, ['Unable to submit the stage update right now.']);
          if (submitButton) submitButton.disabled = false;
          return;
        }

        modal.hide();
        queueStageRefresh('Stage update submitted. It is now visible and awaiting HoD approval.', 'success');
      } catch (error) {
        console.error(error);
        renderErrors(errorContainer, ['Unable to submit the stage update right now.']);
        if (submitButton) submitButton.disabled = false;
      }
    });
  }
  function bootInlineStageDecisions() {
    document.addEventListener('click', async (event) => {
      const button = event.target.closest('[data-stage-decision-inline]');
      if (!button) {
        return;
      }

      event.preventDefault();

      if (button.disabled) {
        return;
      }

      const decisionRaw = button.getAttribute('data-decision') || '';
      const decision = decisionRaw.trim();
      if (!decision) {
        return;
      }

      const requestId = Number.parseInt(button.getAttribute('data-request-id') || '', 10);
      if (!requestId) {
        return;
      }

      const dropdownMenu = button.closest('.dropdown-menu');
      if (dropdownMenu) {
        const toggle = dropdownMenu.previousElementSibling;
        if (toggle) {
          const dropdown = bootstrap.Dropdown.getOrCreateInstance(toggle);
          dropdown.hide();
        }
      }

      const stageCode = button.getAttribute('data-stage') || '';
      const stageLabel = button.getAttribute('data-stage-name') || stageCode || 'stage';
      const tokenInput = document.querySelector('[data-stage-decision-token]');
      const token = tokenInput ? tokenInput.value : '';

      if (!token) {
        showToast('Unable to update the stage right now.', 'danger');
        return;
      }

      button.disabled = true;
      const row = button.closest('[data-stage-row]');
      if (row) {
        row.setAttribute('data-loading', 'true');
      }

      try {
        const response = await postJson('/Projects/Stages/DecideChange', {
          requestId,
          decision,
          decisionNote: null
        }, token || '');

        if (response.status === 403) {
          showToast('You are not allowed to decide this request.', 'danger');
          return;
        }

        if (response.status === 404 || response.status === 409) {
          const message = response.status === 404
            ? 'Request not found.'
            : 'This request has already been decided.';
          showToast(message, 'warning');
          if (stageCode) {
            updatePendingBadge(stageCode, null, null);
            enableRequestButton(stageCode);
          }
          const item = document.querySelector(`[data-stage-request-item][data-request-id="${requestId}"]`);
          if (item) {
            item.remove();
          }
          const list = document.querySelector('[data-stage-decision-list]');
          updateStageRequestsContainer(list);
          return;
        }

        if (response.status === 422) {
          showToast('Unable to update the stage right now.', 'danger');
          return;
        }

        if (!response.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          return;
        }

        const data = await response.json().catch(() => null);
        if (!data?.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          return;
        }

        const updated = data.updated || null;
        if (stageCode) {
          updateStageRow(stageCode, updated?.status || null, updated);
          updatePendingBadge(stageCode, null, null);
          enableRequestButton(stageCode);
        }

        handleWarnings(data.warnings);

        const decisionLower = decision.toLowerCase();
        if (decisionLower === 'approve') {
          showToast(`Approved change for ${stageLabel}.`, 'success');
        } else {
          showToast(`Rejected change for ${stageLabel}.`, 'info');
        }

        const item = document.querySelector(`[data-stage-request-item][data-request-id="${requestId}"]`);
        if (item) {
          item.remove();
        }
        const list = document.querySelector('[data-stage-decision-list]');
        updateStageRequestsContainer(list);
      } catch (error) {
        console.error(error);
        showToast('Unable to update the stage right now.', 'danger');
      } finally {
        button.disabled = false;
        if (row) {
          row.removeAttribute('data-loading');
        }
      }
    });
  }

  function bootStageDecisions() {
    const list = document.querySelector('[data-stage-decision-list]');
    if (!list) {
      return;
    }

    const tokenInput = document.querySelector('[data-stage-decision-token]');
    const token = tokenInput ? tokenInput.value : '';
    list.addEventListener('click', async (event) => {
      const button = event.target.closest('[data-stage-decision]');
      if (!button) {
        return;
      }

      event.preventDefault();

      if (button.disabled) {
        return;
      }

      const decisionRaw = button.getAttribute('data-stage-decision') || '';
      const decision = decisionRaw.trim();
      if (!decision) {
        return;
      }

      const item = button.closest('[data-stage-request-item]');
      if (!item) {
        return;
      }

      const requestId = Number.parseInt(item.getAttribute('data-request-id') || '', 10);
      if (!requestId) {
        return;
      }

      const stageCode = item.getAttribute('data-stage-code') || '';
      const stageLabel = item.getAttribute('data-stage-label') || stageCode || 'stage';
      const noteInput = item.querySelector('[data-stage-decision-note]');
      const note = noteInput && noteInput.value ? noteInput.value.trim() : null;
      const errorContainer = item.querySelector('[data-stage-decision-error]');
      const buttons = item.querySelectorAll('[data-stage-decision]');

      const enableControls = () => {
        buttons.forEach((btn) => {
          btn.disabled = false;
        });
        if (noteInput) {
          noteInput.disabled = false;
        }
      };

      buttons.forEach((btn) => {
        btn.disabled = true;
      });
      if (noteInput) {
        noteInput.disabled = true;
      }

      if (errorContainer) {
        errorContainer.classList.add('d-none');
        errorContainer.textContent = '';
      }

      let removed = false;

      try {
        console.debug('[DecideChange] requestId:', requestId, 'decision:', decision);
        const response = await postJson('/Projects/Stages/DecideChange', {
          requestId,
          decision,
          decisionNote: note
        }, token || '');

        if (response.status === 403) {
          showToast('You are not allowed to decide this request.', 'danger');
          enableControls();
          return;
        }

        if (response.status === 404 || response.status === 409) {
          const message = response.status === 404
            ? 'Request not found.'
            : 'This request has already been decided.';
          showToast(message, 'warning');
          item.remove();
          removed = true;
          if (stageCode) {
            updatePendingBadge(stageCode, null, null);
            enableRequestButton(stageCode);
          }
          updateStageRequestsContainer(list);
          return;
        }

        if (response.status === 422) {
          const data422 = await response.json().catch(() => null);
          const message = data422?.error || 'Validation failed.';
          if (errorContainer) {
            errorContainer.textContent = message;
            errorContainer.classList.remove('d-none');
          }
          enableControls();
          return;
        }

        if (!response.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          enableControls();
          return;
        }

        const data = await response.json().catch(() => null);
        if (!data?.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          enableControls();
          return;
        }

        const updated = data.updated || null;
        if (stageCode) {
          updateStageRow(stageCode, updated?.status || null, updated);
          updatePendingBadge(stageCode, null, null);
          enableRequestButton(stageCode);
        }

        handleWarnings(data.warnings);

        const decisionLower = decision.toLowerCase();
        if (decisionLower === 'approve') {
          showToast(`Approved change for ${stageLabel}.`, 'success');
        } else {
          showToast(`Rejected change for ${stageLabel}.`, 'info');
        }

        item.remove();
        removed = true;
        updateStageRequestsContainer(list);
      } catch (error) {
        console.error(error);
        showToast('Unable to update the stage right now.', 'danger');
      } finally {
        if (!removed) {
          enableControls();
        }
      }
    });
  }

  function boot() {
    if (typeof bootstrap === 'undefined' || !bootstrap.Modal || !bootstrap.Toast) {
      return;
    }

    showQueuedStageMessage();
    bootDirectApply();
    bootStageRequest();
    bootInlineStageDecisions();
    bootStageDecisions();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
})();
