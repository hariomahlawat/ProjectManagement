let region = null;
let nextId = 0;

export function initNotebookToastRegion(root = document.querySelector('[data-notebook-toast-region]')) {
  region = root || null;
  return { show: showNotebookToast, clear: clearNotebookToasts };
}

const MAX_VISIBLE_TOASTS = 3;
const prismToasts = []; // { tone, message, handle } for the Notebook's messages in the shared toast

export function showNotebookToast({ message, tone = 'neutral', actionText = '', onAction = null, duration = 3500 } = {}) {
  // The shared glass toast (docs/ux/toasts.md) when it is on the page; the Notebook's own below.
  // The Notebook's rules still apply there: the same error is shown once, and at most three of
  // its messages stay, so repeated failures never bury a later Undo.
  if (typeof window !== 'undefined' && window.PrismToast && message) {
    const live = prismToasts.filter((entry) => entry.handle.element.isConnected && !entry.handle.element.classList.contains('is-leaving'));
    prismToasts.length = 0;
    prismToasts.push(...live);
    if (tone === 'error') {
      const duplicate = prismToasts.find((entry) => entry.tone === tone && entry.message === message);
      if (duplicate) return { id: duplicate.handle.element.id || '', close: duplicate.handle.dismiss };
    }
    while (prismToasts.length >= MAX_VISIBLE_TOASTS) prismToasts.shift().handle.dismiss();
    const prismTone = { success: 'success', error: 'danger', danger: 'danger', warning: 'warning' }[tone] || 'info';
    const handle = window.PrismToast.show({
      tone: prismTone,
      text: message,
      sticky: !(duration > 0),
      life: duration > 0 ? duration : undefined,
      action: actionText && typeof onAction === 'function' ? { label: actionText, run: () => onAction() } : undefined
    });
    prismToasts.push({ tone, message, handle });
    return { id: handle.element.id || '', close: handle.dismiss };
  }
  if (!region || !message) return null;
  // The same error raised twice (for example by a retried action) is shown once.
  const duplicate = [...region.querySelectorAll('.notebook-toast')]
    .find((existing) => existing.dataset.tone === tone && existing.firstElementChild?.textContent === message);
  if (duplicate && tone === 'error') return { id: duplicate.id, close: () => removeToast(duplicate) };
  // Older messages give way so the stack never covers the board.
  const visible = [...region.querySelectorAll('.notebook-toast')];
  visible.slice(0, Math.max(0, visible.length - (MAX_VISIBLE_TOASTS - 1))).forEach(removeToast);
  const toast = document.createElement('div');
  const id = `notebook-toast-${++nextId}`;
  toast.id = id;
  toast.className = 'notebook-toast';
  toast.dataset.tone = tone;
  toast.setAttribute('role', tone === 'error' ? 'alert' : 'status');
  const text = document.createElement('span');
  text.textContent = message;
  toast.appendChild(text);
  if (actionText && typeof onAction === 'function') {
    const action = document.createElement('button');
    action.type = 'button';
    action.textContent = actionText;
    action.addEventListener('click', async () => {
      action.disabled = true;
      try { await onAction(); removeToast(toast); }
      catch { action.disabled = false; }
    });
    toast.appendChild(action);
  }
  const close = document.createElement('button');
  close.type = 'button';
  close.className = 'notebook-toast__close';
  close.setAttribute('aria-label', 'Dismiss notification');
  close.textContent = '×';
  close.addEventListener('click', () => removeToast(toast));
  toast.appendChild(close);
  region.appendChild(toast);
  const timer = duration > 0 ? window.setTimeout(() => removeToast(toast), duration) : null;
  toast._notebookTimer = timer;
  return { id, close: () => removeToast(toast) };
}

export function clearNotebookToasts() {
  region?.querySelectorAll('.notebook-toast').forEach(removeToast);
}

function removeToast(toast) {
  if (!toast?.isConnected) return;
  if (toast._notebookTimer) window.clearTimeout(toast._notebookTimer);
  toast.remove();
}
