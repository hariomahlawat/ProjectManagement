let activeRequest = null;

const elements = () => {
  const dialog = document.querySelector('[data-pbd-confirm-dialog]');
  if (!(dialog instanceof HTMLDialogElement)) return null;

  return {
    dialog,
    title: dialog.querySelector('[data-pbd-confirm-title]'),
    message: dialog.querySelector('[data-pbd-confirm-message]'),
    confirm: dialog.querySelector('[data-pbd-confirm-action]'),
    cancel: dialog.querySelector('[data-pbd-confirm-cancel]')
  };
};

const finish = (result) => {
  if (!activeRequest) return;
  const request = activeRequest;
  activeRequest = null;

  if (request.dialog.open) request.dialog.close();
  request.resolve(result);
  window.requestAnimationFrame(() => {
    if (request.returnFocus instanceof HTMLElement && request.returnFocus.isConnected) {
      request.returnFocus.focus({ preventScroll: true });
    }
  });
};

const ensureBound = (ui) => {
  if (ui.dialog.dataset.pbdConfirmBound === 'true') return;
  ui.dialog.dataset.pbdConfirmBound = 'true';

  ui.confirm?.addEventListener('click', () => finish(true));
  ui.cancel?.addEventListener('click', () => finish(false));

  ui.dialog.addEventListener('cancel', (event) => {
    event.preventDefault();
    finish(false);
  });

  // Clicking the modal backdrop deliberately does nothing. Destructive actions
  // must always be made through the explicitly labelled confirmation button.
  ui.dialog.addEventListener('click', (event) => {
    if (event.target === ui.dialog) event.preventDefault();
  });

  ui.dialog.addEventListener('close', () => {
    if (activeRequest?.dialog === ui.dialog) finish(false);
  });
};

/**
 * Displays the reusable PRISM confirmation dialog.
 * The safe/cancel action receives initial focus and Enter never defaults to the
 * destructive action unless the user has explicitly focused that button.
 */
export const prismConfirm = ({
  title,
  message,
  confirmText = 'Confirm',
  cancelText = 'Cancel',
  tone = 'danger',
  returnFocus = document.activeElement
} = {}) => {
  const ui = elements();
  if (!ui) {
    console.error('PRISM confirmation dialog is not available.');
    return Promise.resolve(false);
  }

  ensureBound(ui);
  if (activeRequest) finish(false);

  if (ui.title) ui.title.textContent = title || 'Confirm action';
  if (ui.message) ui.message.textContent = message || 'Continue with this action?';
  if (ui.confirm) {
    ui.confirm.textContent = confirmText;
    ui.confirm.className = tone === 'danger'
      ? 'btn btn-danger'
      : 'btn btn-primary';
  }
  if (ui.cancel) ui.cancel.textContent = cancelText;
  ui.dialog.dataset.tone = tone;

  return new Promise((resolve) => {
    activeRequest = { resolve, returnFocus, dialog: ui.dialog };
    ui.dialog.showModal();
    window.requestAnimationFrame(() => ui.cancel?.focus({ preventScroll: true }));
  });
};
