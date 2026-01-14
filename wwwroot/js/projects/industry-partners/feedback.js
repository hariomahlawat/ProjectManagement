// Section: Toast helpers
function ensureToastHost() {
  let host = document.getElementById('industryPartnerToastHost');
  if (host) {
    return host;
  }

  host = document.createElement('div');
  host.id = 'industryPartnerToastHost';
  host.className = 'toast-container position-fixed top-0 end-0 p-3';
  document.body.appendChild(host);
  return host;
}

function showToast(message, variant = 'success') {
  if (!message || !window.bootstrap?.Toast) {
    return;
  }

  const host = ensureToastHost();
  const toastEl = document.createElement('div');
  toastEl.className = `toast align-items-center text-bg-${variant} border-0`;
  toastEl.setAttribute('role', 'status');
  toastEl.setAttribute('aria-live', 'polite');
  toastEl.setAttribute('aria-atomic', 'true');

  toastEl.innerHTML = `
    <div class="d-flex">
      <div class="toast-body">${message}</div>
      <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
    </div>
  `;

  host.appendChild(toastEl);
  const toast = window.bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 4000 });
  toastEl.addEventListener('hidden.bs.toast', () => {
    toast.dispose();
    toastEl.remove();
  });
  toast.show();
}

// Section: Industry partner feedback initializer
export function initIndustryPartnerFeedback() {
  document.body.addEventListener('industry-partner-archived', () => {
    showToast('Industry partner archived.');
  });

  document.body.addEventListener('industry-partner-reactivated', () => {
    showToast('Industry partner reactivated.');
  });

  document.body.addEventListener('industry-partner-overview-saved', () => {
    showToast('Overview updated.');
  });
}
