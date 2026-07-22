(() => {
  'use strict';
  const modalElement = document.getElementById('celebrationDeleteModal');
  const idInput = document.getElementById('celebration-delete-id');
  const nameTarget = modalElement?.querySelector('[data-admin-celebration-name]');
  if (!modalElement || !idInput || !window.bootstrap) return;
  const modal = window.bootstrap.Modal.getOrCreateInstance(modalElement);
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-admin-celebration-delete]');
    if (!button) return;
    idInput.value = button.dataset.id || '';
    if (nameTarget) nameTarget.textContent = button.dataset.name || 'this entry';
    modal.show();
  });
})();
