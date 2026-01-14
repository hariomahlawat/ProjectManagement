// Section: Create partner modal helpers
function shouldOpenCreatePartnerModal() {
  return document.querySelector('[data-create-partner-modal="open"]');
}

function showCreatePartnerModal() {
  const modalElement = document.getElementById('createPartnerModal');
  if (!modalElement || !window.bootstrap?.Modal) {
    return;
  }

  const modal = window.bootstrap.Modal.getOrCreateInstance(modalElement);
  modal.show();
}

// Section: Create partner modal initializer
export function initCreatePartnerModal() {
  if (!shouldOpenCreatePartnerModal()) {
    return;
  }

  showCreatePartnerModal();
}
