const toastContainerId = 'socialMediaToastContainer';

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
  const toastEl = createToastElement(message, variant || 'primary');
  host.appendChild(toastEl);

  const toastInstance = bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 5000 });
  toastEl.addEventListener('hidden.bs.toast', () => {
    toastInstance.dispose();
    toastEl.remove();
  }, { once: true });

  toastInstance.show();
}

function initToasts() {
  const containers = document.querySelectorAll('[data-social-toast-container]');
  containers.forEach(container => {
    const items = container.querySelectorAll('[data-social-toast]');
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
  const busyLabel = button.getAttribute('data-social-busy-label');
  if (!busyLabel) {
    return;
  }

  button.dataset.socialOriginalHtml = button.innerHTML;
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

    if (typeof form.checkValidity === 'function' && !form.checkValidity()) {
      event.preventDefault();
      form.reportValidity();
      return;
    }

    const submitter = event.submitter || form.querySelector('[type="submit"]');
    if (!submitter || submitter.disabled) {
      return;
    }

    submitter.disabled = true;
    updateButtonToBusyState(submitter);
  });
}

function initDisableOnSubmit() {
  const forms = document.querySelectorAll('form[data-social-disable-on-submit]');
  forms.forEach(disableFormOnSubmit);
}

function ensureBootstrapModal() {
  const bootstrap = ensureBootstrap();
  return bootstrap && typeof bootstrap.Modal === 'function' ? bootstrap : null;
}

function showModalElement(modalEl) {
  if (!modalEl) {
    return;
  }

  const bootstrap = ensureBootstrapModal();
  if (bootstrap) {
    bootstrap.Modal.getOrCreateInstance(modalEl).show();
    return;
  }

  modalEl.classList.add('show');
  modalEl.style.display = 'block';
  modalEl.removeAttribute('aria-hidden');
  modalEl.setAttribute('aria-modal', 'true');
}

function initAutoShowModals() {
  const modals = document.querySelectorAll('[data-social-auto-show="true"]');
  modals.forEach(showModalElement);
}

// ----------------------------------------------------
// PHOTO UPLOAD GUARDRAILS
// ----------------------------------------------------

// SECTION: Formatting helpers
function formatMb(bytes) {
  return (bytes / (1024 * 1024)).toFixed(1);
}

// SECTION: Photo upload interactions
function initPhotoUploadInputs() {
  const inputs = document.querySelectorAll('[data-photo-upload-input]');
  inputs.forEach(input => {
    const summaryId = input.getAttribute('aria-describedby')
      ?.split(' ')
      .map(id => id.trim())
      .find(id => id.endsWith('photo-summary'));
    const summaryEl = summaryId ? document.getElementById(summaryId) : null;
    const maxFiles = Number(input.getAttribute('data-max-files') || '0');
    const maxBytes = Number(input.getAttribute('data-max-bytes') || '0');

    input.addEventListener('change', () => {
      const files = Array.from(input.files || []);
      const previewId = input.getAttribute('data-photo-preview-target');
      const previewEl = previewId ? document.getElementById(previewId) : null;

      if (summaryEl) {
        const totalBytes = files.reduce((sum, file) => sum + (file.size || 0), 0);
        summaryEl.textContent = files.length === 0
          ? ''
          : `${files.length} photograph${files.length === 1 ? '' : 's'} selected · ${formatMb(totalBytes)} MB`;
      }

      if (maxFiles > 0 && files.length > maxFiles) {
        input.setCustomValidity(`Select no more than ${maxFiles} photographs.`);
        showToast(`You can add up to ${maxFiles} photographs at a time.`, 'danger');
      } else {
        input.setCustomValidity('');
      }

      if (maxBytes > 0) {
        const oversized = files.find(file => (file.size || 0) > maxBytes);
        if (oversized) {
          input.setCustomValidity(`\"${oversized.name}\" exceeds the permitted size.`);
          showToast(`\"${oversized.name}\" exceeds the per-photo limit.`, 'danger');
        }
      }

      renderPhotoPreviews(previewEl, files);
    });
  });
}



function renderPhotoPreviews(container, files) {
  if (!container) return;
  container.innerHTML = '';

  if (!files.length) {
    container.hidden = true;
    return;
  }

  container.hidden = false;
  files.slice(0, 12).forEach(file => {
    const item = document.createElement('div');
    item.className = 'social-upload-preview__item';

    const image = document.createElement('img');
    image.alt = '';
    image.src = URL.createObjectURL(file);
    image.addEventListener('load', () => URL.revokeObjectURL(image.src), { once: true });

    const name = document.createElement('span');
    name.className = 'social-upload-preview__name';
    name.textContent = file.name;

    item.append(image, name);
    container.appendChild(item);
  });
}

function initSocialFilterBar() {
  const form = document.querySelector('[data-social-filter-form]');
  if (!form) return;

  form.querySelectorAll('[data-social-auto-submit]').forEach(control => {
    control.addEventListener('change', () => form.requestSubmit());
  });

  const search = form.querySelector('input[name="Q"]');
  if (search) {
    let timer;
    search.addEventListener('input', () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => form.requestSubmit(), 450);
    });
  }
}

function initSocialRows() {
  document.querySelectorAll('[data-social-row-url]').forEach(row => {
    row.addEventListener('click', event => {
      if (event.target.closest('a, button, input, select, form, .dropdown-menu')) return;
      const url = row.getAttribute('data-social-row-url');
      if (url) window.location.assign(url);
    });
  });
}

function initPhotoFallbacks() {
  document.querySelectorAll('[data-social-photo]').forEach(image => {
    image.addEventListener('error', () => {
      image.hidden = true;
      const fallback = image.parentElement?.querySelector('[data-social-photo-fallback]');
      if (fallback) fallback.hidden = false;
    }, { once: true });
  });
}

function initSocialDropzones() {
  document.querySelectorAll('.social-dropzone').forEach(zone => {
    ['dragenter', 'dragover'].forEach(name => zone.addEventListener(name, event => {
      event.preventDefault();
      zone.classList.add('is-dragover');
    }));
    ['dragleave', 'drop'].forEach(name => zone.addEventListener(name, () => zone.classList.remove('is-dragover')));
  });
}

function init() {
  initToasts();
  initDisableOnSubmit();
  initAutoShowModals();
  initPhotoUploadInputs();
  initSocialFilterBar();
  initSocialRows();
  initPhotoFallbacks();
  initSocialDropzones();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
  init();
}
