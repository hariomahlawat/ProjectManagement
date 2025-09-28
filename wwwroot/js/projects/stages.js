(function () {
  const DIRECT_DATE_REQUIRED_STATUSES = new Set(['InProgress']);
  const REQUEST_DATE_REQUIRED_STATUSES = new Set(['InProgress', 'Completed']);

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

  function badgeClass(status) {
    switch (status) {
      case 'Completed':
        return 'badge bg-success';
      case 'InProgress':
        return 'badge bg-primary';
      default:
        return 'badge bg-secondary';
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

  function todayIso() {
    const today = new Date();
    const month = `${today.getMonth() + 1}`.padStart(2, '0');
    const day = `${today.getDate()}`.padStart(2, '0');
    return `${today.getFullYear()}-${month}-${day}`;
  }

  function updateStageRow(stageCode, payloadStatus, updated) {
    if (!stageCode) return;
    const row = document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`);
    if (!row) return;

    const updatedStatus = (updated && updated.status) ? updated.status : payloadStatus;
    const statusText = updatedStatus || payloadStatus;
    const badge = row.querySelector('[data-stage-status]');
    if (badge) {
      badge.textContent = statusLabel(statusText);
      badge.className = badgeClass(statusText || '');
    }

    const actualSpan = row.querySelector('[data-stage-actual-start]');
    if (actualSpan) {
      actualSpan.textContent = formatDate(updated?.actualStart ?? null);
    }

    const completedSpan = row.querySelector('[data-stage-completed]');
    if (completedSpan) {
      completedSpan.textContent = formatDate(updated?.completedOn ?? null);
    }

    const durationSpan = row.querySelector('[data-stage-duration]');
    if (durationSpan) {
      const days = calculateDurationDays(updated?.actualStart ?? null, updated?.completedOn ?? null);
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
        if (updated?.completedOn) {
          trigger.setAttribute('data-default-date', updated.completedOn);
        } else {
          trigger.removeAttribute('data-default-date');
        }
      } else if (triggerStatus === 'InProgress') {
        if (updated?.actualStart) {
          trigger.setAttribute('data-default-date', updated.actualStart);
        } else {
          trigger.removeAttribute('data-default-date');
        }
      }
    });
  }

  function updatePendingBadge(stageCode, status, dateIso) {
    if (!stageCode) return;
    const row = document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`);
    if (!row) return;

    const container = row.querySelector('[data-stage-pending]');
    if (!container) return;

    if (!status) {
      container.textContent = '';
      return;
    }

    const badge = document.createElement('span');
    badge.className = 'badge bg-warning-subtle text-warning border border-warning-subtle';
    badge.textContent = `Pending: ${statusLabel(status)}`;

    if (dateIso) {
      const formatted = formatDate(dateIso);
      if (formatted && formatted !== '—') {
        badge.textContent += ` · ${formatted}`;
      }
    }

    container.replaceChildren(badge);
  }

  function disableRequestButton(stageCode) {
    if (!stageCode) return;
    const row = document.querySelector(`[data-stage-row="${escapeSelector(stageCode)}"]`);
    if (!row) return;

    const button = row.querySelector('[data-stage-request-button]');
    if (button) {
      button.disabled = true;
      button.classList.add('disabled');
    }
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
    const missingPredecessorsContainer = modalEl.querySelector('[data-direct-apply-missing]');
    const forceCheckbox = modalEl.querySelector('#forceBackfillPredecessors')
      || modalEl.querySelector('[data-direct-apply-force]');
    const forceHint = modalEl.querySelector('[data-direct-apply-force-hint]');
    const submitButton = modalEl.querySelector('[data-direct-apply-submit]');

    function setDateRequired(required) {
      if (!dateInput) return;
      dateInput.required = required;
      if (dateHint) {
        dateHint.textContent = required ? 'Required.' : 'Optional.';
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
      if (forceCheckbox) {
        forceCheckbox.checked = false;
      }
      setForceHintHighlighted(false);

      const requiresDate = DIRECT_DATE_REQUIRED_STATUSES.has(status);
      setDateRequired(requiresDate);
      if (dateInput) {
        dateInput.value = defaultDate || (requiresDate ? todayIso() : '');
      }

      if (submitButton) {
        submitButton.disabled = false;
      }

      modal.show();
    });

    if (!form) {
      return;
    }

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
        forceBackfillPredecessors: !!document.querySelector('#forceBackfillPredecessors')?.checked
      };

      if (!payload.projectId) {
        payload.projectId = 0;
      }

      try {
        const response = await fetch('/Projects/Stages/ApplyChange', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': tokenInput.value
          },
          body: JSON.stringify(payload)
        });

        if (response.status === 422) {
          const data = await response.json().catch(() => null);
          const messages = data?.details || ['Validation failed.'];
          renderErrors(errorContainer, messages);
          const missing = Array.isArray(data?.missingPredecessors) ? data.missingPredecessors : [];
          renderMissingPredecessors(missingPredecessorsContainer, missing);
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
          showToast('Pending request was superseded by this change.', 'warning');
          modal.hide();
          renderMissingPredecessors(missingPredecessorsContainer, []);
          setForceHintHighlighted(false);
          if (submitButton) submitButton.disabled = false;
          return;
        }

        if (!response.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          renderMissingPredecessors(missingPredecessorsContainer, []);
          setForceHintHighlighted(false);
          if (submitButton) submitButton.disabled = false;
          return;
        }

        const data = await response.json();
        if (!data?.ok) {
          showToast('Unable to update the stage right now.', 'danger');
          renderMissingPredecessors(missingPredecessorsContainer, []);
          setForceHintHighlighted(false);
          if (submitButton) submitButton.disabled = false;
          return;
        }

        updateStageRow(stageInput.value, statusInput.value, data.updated);
        modal.hide();
        showToast('Stage updated.', 'success');
        handleWarnings(data.warnings);
        if (data.backfilled?.count > 0) {
          const stages = Array.isArray(data.backfilled.stages) && data.backfilled.stages.length > 0
            ? ` (${data.backfilled.stages.join(', ')})`
            : '';
          showToast(`Backfilled ${data.backfilled.count} predecessor stage(s)${stages}.`, 'info');
        }
        renderMissingPredecessors(missingPredecessorsContainer, []);
        setForceHintHighlighted(false);
        const modalForceCheckbox = document.querySelector('#forceBackfillPredecessors');
        if (modalForceCheckbox) {
          modalForceCheckbox.checked = false;
        }
        if (forceCheckbox && forceCheckbox !== modalForceCheckbox) {
          forceCheckbox.checked = false;
        }
      } catch (error) {
        console.error(error);
        showToast('Unable to update the stage right now.', 'danger');
        renderMissingPredecessors(missingPredecessorsContainer, []);
        setForceHintHighlighted(false);
      } finally {
        if (submitButton) submitButton.disabled = false;
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
    const stageLabel = modalEl.querySelector('[data-stage-request-stage]');
    const statusLabelEl = modalEl.querySelector('[data-stage-request-status]');
    const projectInput = modalEl.querySelector('input[name="projectId"]');
    const stageInput = modalEl.querySelector('input[name="stageCode"]');
    const targetSelect = modalEl.querySelector('[data-stage-request-target]');
    const dateInput = modalEl.querySelector('[data-stage-request-date]');
    const dateHint = modalEl.querySelector('[data-stage-request-date-hint]');
    const noteInput = modalEl.querySelector('[data-stage-request-note]');
    const tokenInput = modalEl.querySelector('input[name="__RequestVerificationToken"]');
    const errorContainer = modalEl.querySelector('[data-stage-request-errors]');
    const conflictContainer = modalEl.querySelector('[data-stage-request-conflict]');
    const missingContainer = modalEl.querySelector('[data-stage-request-missing]');
    const submitButton = modalEl.querySelector('[data-stage-request-submit]');
    let activeStageCurrentStatus = '';

    function setDateRequired(required) {
      if (!dateInput) return;
      dateInput.required = required;
      if (dateHint) {
        dateHint.textContent = required ? 'Required.' : 'Optional.';
      }
    }

    function updateStatusSummary() {
      if (!statusLabelEl) return;
      const targetStatus = targetSelect ? targetSelect.value : '';
      const targetLabelRaw = statusLabel(targetStatus);
      const targetLabel = targetLabelRaw || (targetStatus ? targetStatus : 'Select a status');
      const currentLabel = statusLabel(activeStageCurrentStatus);
      if (currentLabel) {
        statusLabelEl.textContent = `Request change to ${targetLabel} (current: ${currentLabel})`;
      } else {
        statusLabelEl.textContent = `Request change to ${targetLabel}`;
      }
    }

    document.addEventListener('click', (event) => {
      const trigger = event.target.closest('[data-stage-request]');
      if (!trigger) {
        return;
      }

      if (trigger.disabled) {
        return;
      }

      event.preventDefault();

      const projectId = trigger.getAttribute('data-project') || '';
      const stageCode = trigger.getAttribute('data-stage') || '';
      const stageName = trigger.getAttribute('data-stage-name') || '';
      const currentStatus = trigger.getAttribute('data-current-status') || '';
      activeStageCurrentStatus = currentStatus;

      if (projectInput) projectInput.value = projectId;
      if (stageInput) stageInput.value = stageCode;
      if (stageLabel) stageLabel.textContent = `${stageCode ? stageCode + ' — ' : ''}${stageName}`.trim();

      if (targetSelect) {
        targetSelect.value = currentStatus;
        if (currentStatus && targetSelect.value !== currentStatus) {
          targetSelect.value = 'NotStarted';
        }
        if (!currentStatus && targetSelect.value === '') {
          targetSelect.value = 'NotStarted';
        }
      }

      const selectedStatus = targetSelect ? targetSelect.value : '';
      const requiresDate = REQUEST_DATE_REQUIRED_STATUSES.has(selectedStatus);
      setDateRequired(requiresDate);
      if (dateInput) {
        dateInput.value = requiresDate ? todayIso() : '';
      }

      updateStatusSummary();

      if (errorContainer) {
        errorContainer.classList.add('d-none');
        errorContainer.textContent = '';
      }
      if (conflictContainer) {
        conflictContainer.classList.add('d-none');
      }
      renderMissingPredecessors(missingContainer, []);
      if (noteInput) {
        noteInput.value = '';
      }

      if (submitButton) {
        submitButton.disabled = false;
      }

      modal.show();
    });

    if (targetSelect) {
      targetSelect.addEventListener('change', () => {
        const status = targetSelect.value;
        const requiresDate = REQUEST_DATE_REQUIRED_STATUSES.has(status);
        setDateRequired(requiresDate);
        if (requiresDate && dateInput && !dateInput.value) {
          dateInput.value = todayIso();
        }
        updateStatusSummary();
      });
    }

    if (!form) {
      return;
    }

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!projectInput || !stageInput || !targetSelect || !tokenInput) {
        return;
      }

      if (submitButton) {
        submitButton.disabled = true;
      }

      if (errorContainer) {
        errorContainer.classList.add('d-none');
        errorContainer.textContent = '';
      }
      if (conflictContainer) {
        conflictContainer.classList.add('d-none');
      }
      renderMissingPredecessors(missingContainer, []);

      const payload = {
        projectId: Number.parseInt(projectInput.value, 10),
        stageCode: stageInput.value,
        requestedStatus: targetSelect.value,
        requestedDate: dateInput && dateInput.value ? dateInput.value : null,
        note: noteInput && noteInput.value ? noteInput.value.trim() : null
      };

      if (!payload.projectId) {
        payload.projectId = 0;
      }

      try {
        const response = await fetch('/Projects/Stages/RequestChange', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': tokenInput.value
          },
          body: JSON.stringify(payload)
        });

        if (response.status === 422) {
          const data = await response.json().catch(() => null);
          const messages = data?.details || ['Validation failed.'];
          renderErrors(errorContainer, messages);
          const missing = Array.isArray(data?.missingPredecessors) ? data.missingPredecessors : [];
          renderMissingPredecessors(missingContainer, missing);
          if (submitButton) submitButton.disabled = false;
          return;
        }

        if (response.status === 409) {
          if (conflictContainer) {
            conflictContainer.classList.remove('d-none');
          }
          if (submitButton) submitButton.disabled = false;
          return;
        }

        if (!response.ok) {
          renderErrors(errorContainer, ['Unable to submit the request right now.']);
          if (submitButton) submitButton.disabled = false;
          return;
        }

        const data = await response.json().catch(() => null);
        if (!data?.ok) {
          renderErrors(errorContainer, ['Unable to submit the request right now.']);
          if (submitButton) submitButton.disabled = false;
          return;
        }

        modal.hide();
        showToast('Request submitted.', 'success');
        updatePendingBadge(stageInput.value, payload.requestedStatus, payload.requestedDate);
        disableRequestButton(stageInput.value);
      } catch (error) {
        console.error(error);
        renderErrors(errorContainer, ['Unable to submit the request right now.']);
      } finally {
        if (submitButton) submitButton.disabled = false;
      }
    });
  }

  function boot() {
    if (typeof bootstrap === 'undefined' || !bootstrap.Modal || !bootstrap.Toast) {
      return;
    }

    bootDirectApply();
    bootStageRequest();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
})();
