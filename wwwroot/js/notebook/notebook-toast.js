let region = null;
let nextId = 0;

export function initNotebookToastRegion(root = document.querySelector('[data-notebook-toast-region]')) {
  region = root || null;
  return { show: showNotebookToast, clear: clearNotebookToasts };
}

export function showNotebookToast({ message, tone = 'neutral', actionText = '', onAction = null, duration = 3500 } = {}) {
  if (!region || !message) return null;
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
