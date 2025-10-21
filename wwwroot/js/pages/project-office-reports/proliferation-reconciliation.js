(function () {
  const modeMap = {
    auto: 'Auto',
    yearly: 'UseYearly',
    granular: 'UseGranular',
    combined: 'UseYearlyAndGranular'
  };

  function ensureBootstrap() {
    return typeof window !== 'undefined' && window.bootstrap && typeof window.bootstrap.Toast === 'function'
      ? window.bootstrap
      : null;
  }

  function ensureToastHost() {
    let host = document.querySelector('[data-pro-rec-toast-host]');
    if (!host) {
      host = document.createElement('div');
      host.className = 'toast-container position-fixed top-0 end-0 p-3';
      host.setAttribute('aria-live', 'polite');
      host.setAttribute('aria-atomic', 'true');
      host.dataset.proRecToastHost = 'true';
      document.body.appendChild(host);
    }
    return host;
  }

  function createToastElement(message, variant) {
    const toast = document.createElement('div');
    toast.className = `toast align-items-center text-bg-${variant} border-0`; 
    toast.setAttribute('role', 'status');
    toast.setAttribute('aria-live', 'polite');
    toast.setAttribute('aria-atomic', 'true');

    const wrapper = document.createElement('div');
    wrapper.className = 'd-flex';

    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = message;

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'btn-close btn-close-white me-2 m-auto';
    close.setAttribute('data-bs-dismiss', 'toast');
    close.setAttribute('aria-label', 'Dismiss');

    wrapper.append(body, close);
    toast.append(wrapper);
    return toast;
  }

  function showToast(message, variant = 'primary') {
    if (!message) {
      return;
    }

    const bootstrap = ensureBootstrap();
    if (!bootstrap) {
      console.warn('Bootstrap is required to show toasts.');
      return;
    }

    const host = ensureToastHost();
    const toastEl = createToastElement(message, variant);
    host.appendChild(toastEl);

    const toast = bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 5000 });
    toastEl.addEventListener('hidden.bs.toast', () => {
      toast.dispose();
      toastEl.remove();
    }, { once: true });

    toast.show();
  }

  function getAntiforgeryToken() {
    const tokenInput = document.querySelector('[data-pro-rec-antiforgery] input[name="__RequestVerificationToken"]');
    return tokenInput ? tokenInput.value : null;
  }

  function parseDatasetNumber(value) {
    if (value === undefined || value === null || value === '') {
      return null;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  function setButtonBusy(button, busy) {
    if (!button) {
      return;
    }

    if (busy) {
      button.dataset.originalContent = button.innerHTML;
      button.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>';
      button.disabled = true;
    } else {
      if (button.dataset.originalContent) {
        button.innerHTML = button.dataset.originalContent;
        delete button.dataset.originalContent;
      }
      button.disabled = false;
    }
  }

  async function sendPreferenceRequest(button) {
    const action = button.dataset.proRecAction;
    const mode = modeMap[action];
    if (!mode) {
      return;
    }

    const projectId = parseDatasetNumber(button.dataset.projectId);
    const year = parseDatasetNumber(button.dataset.year);
    const source = button.dataset.source;
    const rowVersion = button.dataset.rowVersion || null;

    if (!projectId || !source) {
      showToast('Missing project details for this row.', 'danger');
      return;
    }

    const payload = {
      projectId,
      source,
      year,
      mode,
      rowVersion
    };

    const headers = { 'Content-Type': 'application/json' };
    const token = getAntiforgeryToken();
    if (token) {
      headers.RequestVerificationToken = token;
    }

    setButtonBusy(button, true);

    try {
      const response = await fetch('/api/proliferation/year-preference', {
        method: 'POST',
        headers,
        body: JSON.stringify(payload)
      });

      if (response.ok) {
        const data = await response.json();
        let message;
        switch (data.outcome) {
          case 'Created':
            message = 'Preference saved.';
            break;
          case 'Updated':
            message = 'Preference updated.';
            break;
          case 'Cleared':
            message = 'Preference reset to automatic mode.';
            break;
          case 'NoChange':
            message = 'Preference already set to automatic.';
            break;
          default:
            message = 'Preference updated.';
            break;
        }

        showToast(message, 'success');
        window.setTimeout(() => window.location.reload(), 600);
        return;
      }

      let detail = 'Unable to update preference.';
      try {
        const problem = await response.json();
        if (problem && typeof problem.detail === 'string' && problem.detail.trim() !== '') {
          detail = problem.detail;
        }
      } catch (err) {
        // Ignore parsing errors and use fallback message.
      }

      if (response.status === 409) {
        showToast(detail, 'warning');
      } else if (response.status === 400) {
        showToast(detail, 'warning');
      } else if (response.status === 403) {
        showToast('You are not authorised to change proliferation preferences.', 'danger');
      } else {
        showToast('A server error prevented the preference change.', 'danger');
      }
    } catch (error) {
      console.error('Failed to update preference', error);
      showToast('A network error prevented the request from completing.', 'danger');
    } finally {
      setButtonBusy(button, false);
    }
  }

  function handleActionClick(event) {
    const target = event.target.closest('[data-pro-rec-action]');
    if (!target || target.disabled) {
      return;
    }

    event.preventDefault();
    sendPreferenceRequest(target);
  }

  function initActionButtons() {
    document.addEventListener('click', handleActionClick);
  }

  document.addEventListener('DOMContentLoaded', () => {
    initActionButtons();

    const container = document.querySelector('[data-pro-rec-toast-container]');
    if (container) {
      const messages = container.querySelectorAll('[data-pro-rec-toast]');
      messages.forEach(messageEl => {
        const message = messageEl.textContent?.trim();
        const variant = messageEl.getAttribute('data-variant') || 'primary';
        if (message) {
          showToast(message, variant);
        }
      });
    }
  });
})();
