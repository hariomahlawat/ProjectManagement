const toastContainerId = 'visitsToastContainer';

function ensureBootstrap() {
  return typeof window !== 'undefined' && window.bootstrap && typeof window.bootstrap.Toast === 'function'
    ? window.bootstrap
    : null;
}

function ensureToastHost() {
  let host = document.getElementById(toastContainerId);
  if (host) {
    return host;
  }

  host = document.createElement('div');
  host.id = toastContainerId;
  host.className = 'toast-container position-fixed top-0 end-0 p-3';
  host.setAttribute('aria-live', 'polite');
  host.setAttribute('aria-atomic', 'true');
  document.body.appendChild(host);
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

  const dismiss = document.createElement('button');
  dismiss.type = 'button';
  dismiss.className = 'btn-close btn-close-white me-2 m-auto';
  dismiss.setAttribute('data-bs-dismiss', 'toast');
  dismiss.setAttribute('aria-label', 'Dismiss notification');

  wrapper.append(body, dismiss);
  toast.append(wrapper);
  return toast;
}

function showToast(message, variant) {
  const bootstrap = ensureBootstrap();
  if (!bootstrap || !message) {
    return;
  }

  const host = ensureToastHost();
  const toastEl = createToastElement(message, variant);
  host.appendChild(toastEl);

  const toastInstance = bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 5000 });
  toastEl.addEventListener('hidden.bs.toast', () => {
    toastInstance.dispose();
    toastEl.remove();
  }, { once: true });

  toastInstance.show();
}

function initToasts() {
  const containers = document.querySelectorAll('[data-visits-toast-container]');
  containers.forEach(container => {
    const items = container.querySelectorAll('[data-visits-toast]');
    items.forEach(item => {
      const message = item.textContent?.trim();
      if (!message) {
        return;
      }

      const variant = item.getAttribute('data-variant') || 'primary';
      showToast(message, variant);
    });
  });
}

function updateButtonToBusyState(button) {
  const busyLabel = button.getAttribute('data-visits-busy-label');
  if (!busyLabel) {
    return;
  }

  button.dataset.visitsOriginalHtml = button.innerHTML;
  const spinner = document.createElement('span');
  spinner.className = 'spinner-border spinner-border-sm me-2';
  spinner.setAttribute('role', 'status');
  spinner.setAttribute('aria-hidden', 'true');

  const label = document.createElement('span');
  label.textContent = busyLabel;

  button.innerHTML = '';
  button.append(spinner, label);
}

function disableFormOnSubmit(form) {
  form.addEventListener('submit', event => {
    if (event.defaultPrevented) {
      return;
    }

    const submitter = form.querySelector('[type="submit"]');
    if (!submitter || submitter.disabled) {
      return;
    }

    submitter.disabled = true;
    updateButtonToBusyState(submitter);
  });
}

function initDisableOnSubmit() {
  const forms = document.querySelectorAll('form[data-visits-disable-on-submit]');
  forms.forEach(disableFormOnSubmit);
}

function attachConfirmDialog(form) {
  const message = form.getAttribute('data-confirm');
  if (!message) {
    return;
  }

  form.addEventListener('submit', event => {
    if (!window.confirm(message)) {
      event.preventDefault();
    }
  });
}

function initConfirmations() {
  const forms = document.querySelectorAll('form[data-confirm]');
  forms.forEach(attachConfirmDialog);
}

function init() {
  initToasts();
  initConfirmations();
  initDisableOnSubmit();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
  init();
}
