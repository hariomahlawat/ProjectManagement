// wwwroot/js/todo.js

(function () {
  // Ensure Bootstrap is available
  if (!window.bootstrap) return;

  // ---------- Dropdowns: render menus in <body> & flip safely ----------
  function initDropdowns() {
    document.querySelectorAll('[data-bs-toggle="dropdown"]').forEach(btn => {
      const parent = btn.parentElement;
      if (!parent) return;

      const menu = parent.querySelector(':scope > .dropdown-menu') || parent.querySelector('.dropdown-menu');
      if (!menu) return;

      const originalParent = menu.parentNode;
      const originalNextSibling = menu.nextSibling;
      const todoRow = btn.closest('.todo-row');

      bootstrap.Dropdown.getOrCreateInstance(btn, {
        popperConfig: {
          strategy: 'fixed',
          modifiers: [
            { name: 'flip', options: { fallbackPlacements: ['bottom-end','top-end','top-start','bottom-start'] } },
            { name: 'preventOverflow', options: { boundary: 'viewport' } },
            { name: 'offset', options: { offset: [0, 6] } }
          ]
        }
      });

      btn.addEventListener('show.bs.dropdown', () => {
        if (todoRow) {
          todoRow.classList.add('todo-row--dropdown-open');
        }
        if (menu.parentNode !== document.body) {
          document.body.appendChild(menu);
        }
      });

      btn.addEventListener('hidden.bs.dropdown', () => {
        if (todoRow) {
          todoRow.classList.remove('todo-row--dropdown-open');
        }
        if (!originalParent) return;

        if (originalNextSibling && originalNextSibling.parentNode === originalParent) {
          originalParent.insertBefore(menu, originalNextSibling);
        } else {
          originalParent.appendChild(menu);
        }
      });
    });
  }

  // Close any other open dropdown when a new one opens
  document.addEventListener('show.bs.dropdown', (e) => {
    document.querySelectorAll('[data-bs-toggle="dropdown"]').forEach(btn => {
      if (btn !== e.target) {
        const inst = bootstrap.Dropdown.getInstance(btn);
        if (inst) inst.hide();
      }
    });
  });

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
    document.addEventListener('DOMContentLoaded', () => {
      initDropdowns();
      initDoneAutosubmit();
    });
  } else {
    initDropdowns();
    initDoneAutosubmit();
  }
})();

