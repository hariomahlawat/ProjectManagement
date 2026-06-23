const state = {
  root: null,
  active: null,
  previousFocus: null,
  keydownHandler: null
};

const DEFAULTS = Object.freeze({
  title: 'Confirm action',
  message: '',
  detail: '',
  confirmText: 'Confirm',
  cancelText: 'Cancel',
  tone: 'primary',
  allowBackdropClose: true
});

export function initNotebookConfirmDialog(root = document.querySelector('[data-notebook-confirm]')) {
  if (!root) return null;
  if (state.root === root) return createController();
  disposeNotebookConfirmDialog();
  state.root = root;
  root.querySelectorAll('[data-confirm-cancel]').forEach((button) => {
    button.addEventListener('click', (event) => {
      if (event.currentTarget.classList.contains('notebook-confirm__backdrop') && state.active?.options.allowBackdropClose === false) return;
      resolveActive(false);
    });
  });
  root.querySelector('[data-confirm-accept]')?.addEventListener('click', () => resolveActive(true));
  state.keydownHandler = handleKeydown;
  document.addEventListener('keydown', state.keydownHandler);
  return createController();
}

export function confirmNotebookAction(options = {}) {
  const root = state.root ?? document.querySelector('[data-notebook-confirm]');
  if (!root) return Promise.resolve(false);
  if (!state.root) initNotebookConfirmDialog(root);
  if (state.active) resolveActive(false);

  const merged = { ...DEFAULTS, ...options };
  state.previousFocus = document.activeElement;
  applyOptions(merged);
  root.hidden = false;
  document.body.classList.add('notebook-confirm-open');

  return new Promise((resolve) => {
    state.active = { resolve, options: merged, settled: false };
    queueMicrotask(() => root.querySelector('[data-confirm-accept]')?.focus());
  });
}

export function disposeNotebookConfirmDialog() {
  if (state.active) resolveActive(false);
  if (state.keydownHandler) document.removeEventListener('keydown', state.keydownHandler);
  state.root = null;
  state.keydownHandler = null;
  state.previousFocus = null;
}

function createController() {
  return {
    confirm: confirmNotebookAction,
    close: () => resolveActive(false),
    destroy: disposeNotebookConfirmDialog
  };
}

function applyOptions(options) {
  const root = state.root;
  root.dataset.tone = ['danger', 'warning', 'primary'].includes(options.tone) ? options.tone : 'primary';
  root.querySelector('[data-confirm-title]').textContent = options.title;
  root.querySelector('[data-confirm-message]').textContent = options.message;
  const detail = root.querySelector('[data-confirm-detail]');
  detail.textContent = options.detail || '';
  detail.hidden = !options.detail;
  root.querySelector('[data-confirm-accept]').textContent = options.confirmText;
  root.querySelectorAll('[data-confirm-cancel]').forEach((button) => {
    if (!button.classList.contains('notebook-confirm__backdrop') && !button.classList.contains('notebook-confirm__close')) button.textContent = options.cancelText;
  });
}

function resolveActive(value) {
  const active = state.active;
  if (!active || active.settled) return;
  active.settled = true;
  state.active = null;
  if (state.root) state.root.hidden = true;
  document.body.classList.remove('notebook-confirm-open');
  const previous = state.previousFocus;
  state.previousFocus = null;
  active.resolve(Boolean(value));
  queueMicrotask(() => previous?.isConnected && previous.focus?.());
}

function handleKeydown(event) {
  if (!state.active || state.root?.hidden) return;
  if (event.key === 'Escape') {
    event.preventDefault();
    resolveActive(false);
    return;
  }
  if (event.key !== 'Tab') return;
  const focusable = [...state.root.querySelectorAll('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
    .filter((element) => !element.hidden && element.offsetParent !== null);
  if (!focusable.length) return;
  const first = focusable[0];
  const last = focusable.at(-1);
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
}
