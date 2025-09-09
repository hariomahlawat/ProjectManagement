// wwwroot/js/todo.js

(function () {
  // Ensure Bootstrap is available
  if (!window.bootstrap) return;

  // ---------- Dropdowns: render menus in <body> & flip safely ----------
  function initDropdowns() {
    document.querySelectorAll('.todo-kebab[data-bs-toggle="dropdown"]').forEach(btn => {
      // Mount menus to body so scrollable cards don't clip them
      new bootstrap.Dropdown(btn, {
        container: document.body,
        popperConfig: {
          strategy: 'fixed',
          modifiers: [
            { name: 'preventOverflow', options: { boundary: 'viewport' } },
            { name: 'flip', options: { fallbackPlacements: ['left','top','bottom'] } },
            { name: 'offset', options: { offset: [0, 6] } },
          ]
        }
      });

      // If there's not enough room to the right, open to the left (dropstart)
      btn.addEventListener('show.bs.dropdown', () => {
        const dd = btn.closest('.dropdown');
        if (!dd) return;
        const r = btn.getBoundingClientRect();
        const roomRight = (window.innerWidth - r.right) > 320; // ~menu width
        // Only toggle for widgets tight to the right; OK on My Tasks too
        dd.classList.toggle('dropstart', !roomRight);
      });
    });
  }

  // ---------- Shared confirm modal ----------
  const modalEl = document.getElementById('appConfirmModal');
  const confirmTextEl = document.getElementById('appConfirmText');
  const confirmYesBtn = document.getElementById('appConfirmYes');
  const modal = modalEl ? new bootstrap.Modal(modalEl) : null;

  let pendingAction = null; // () => void

  function askConfirm(message, onYes) {
    if (!modal) {
      // Fallback if modal is missing; still CSP-safe
      if (window.confirm(message)) onYes?.();
      return;
    }
    confirmTextEl.textContent = message || 'Are you sure?';
    pendingAction = onYes;
    modal.show();
  }

  if (confirmYesBtn && modalEl) {
    confirmYesBtn.addEventListener('click', () => {
      try { pendingAction && pendingAction(); } finally {
        pendingAction = null;
        modal.hide();
      }
    });
    modalEl.addEventListener('hidden.bs.modal', () => pendingAction = null);
  }

  // ---------- Event delegation for checkboxes & delete ----------
  document.addEventListener('click', (e) => {
    // A) Complete task checkbox with confirmation
    const cb = e.target.closest('.js-done-checkbox');
    if (cb) {
      e.preventDefault(); // don't toggle immediately
      askConfirm('Mark this task as completed?', () => {
        // apply intended toggle then submit reliably
        cb.checked = !cb.checked;
        if (cb.form && cb.form.requestSubmit) cb.form.requestSubmit();
        else if (cb.form) cb.form.submit();
      });
      return;
    }

    // B) Delete button (within a form). We use the button class, not inline onsubmit.
    const delBtn = e.target.closest('.js-confirm-delete');
    if (delBtn && delBtn.form) {
      e.preventDefault();
      const msg = delBtn.getAttribute('data-confirm') || 'Delete this item?';
      askConfirm(msg, () => {
        if (delBtn.form.requestSubmit) delBtn.form.requestSubmit(delBtn);
        else delBtn.form.submit();
      });
      return;
    }
  });

  
  // ---------- Done checkbox: autosubmit + instant visual feedback ----------
  function initDoneAutosubmit() {
    document.addEventListener('change', (e) => {
      const cb = e.target.closest('.js-done-checkbox');
      if (!cb) return;
      // Visual: toggle .done on the closest .todo-row (used in widget), and data-status on li[data-id] (used in Tasks page)
      const row = cb.closest('li.todo-row, li[data-id]');
      if (row) {
        if (cb.checked) {
          row.classList.add('done');
          row.setAttribute('data-status','done');
        } else {
          row.classList.remove('done');
          row.removeAttribute('data-status');
        }
      }
      // Submit the form to persist
      const form = cb.closest('form');
      if (form) form.requestSubmit();
    }, { passive: true });
  }

  // ---------- Kick things off ----------
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function(){ initDropdowns(); initDoneAutosubmit(); initDoneAutosubmit(); });
  } else {
    initDropdowns(); initDoneAutosubmit();
  }
})();

