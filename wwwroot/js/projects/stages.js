(function () {
  const DATE_REQUIRED_STATUSES = new Set(['InProgress']);

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

  function boot() {
    const modalEl = document.getElementById('stageDirectApplyModal');
    if (!modalEl) {
      return;
    }

    if (typeof bootstrap === 'undefined' || !bootstrap.Modal || !bootstrap.Toast) {
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
    const forceCheckbox = modalEl.querySelector('[data-direct-apply-force]');
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

      const requiresDate = DATE_REQUIRED_STATUSES.has(status);
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
        forceBackfillPredecessors: forceCheckbox ? Boolean(forceCheckbox.checked) : false
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
        renderMissingPredecessors(missingPredecessorsContainer, []);
        setForceHintHighlighted(false);
        if (forceCheckbox) {
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

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
})();
