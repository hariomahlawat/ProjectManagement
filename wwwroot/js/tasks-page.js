// wwwroot/js/tasks-page.js
// Handles: inline-actions visibility, drag reordering, and bulk actions
// Requires: Bootstrap (for CSS only), Anti-forgery token in forms

(function () {
  function qs(sel, root) { return (root || document).querySelector(sel); }
  function qsa(sel, root) { return Array.from((root || document).querySelectorAll(sel)); }

  // ---- 1) Show row actions whenever a row's edit form changes ----
  function initRowActionReveal() {
    qsa('li[data-id]').forEach(row => {
      const form = qs('form[method="post"][id^="f-"]', row);
      const actions = qs('.row-actions', row);
      if (!form || !actions) return;
      const show = () => actions.classList.remove('d-none');
      const hide = () => actions.classList.add('d-none');
      hide();
      form.addEventListener('input', show, { passive: true });
      form.addEventListener('reset', () => setTimeout(hide, 0));
      form.addEventListener('keydown', e => {
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
          e.preventDefault();
          form.requestSubmit();
        }
      });
    });
  }

  // ---- 2) Drag reorder for open items ----
  function initDragReorder() {
    const lists = qsa('.todo-list');
    if (lists.length === 0) return;
    let dragEl = null;

    lists.forEach(list => {
      list.addEventListener('dragstart', e => {
        const li = e.target.closest('.todo-row[draggable="true"]');
        if (!li) return;
        dragEl = li;
        e.dataTransfer.effectAllowed = 'move';
        li.classList.add('opacity-50');
      });

      list.addEventListener('dragover', e => {
        if (!dragEl) return;
        e.preventDefault();
        const li = e.target.closest('.todo-row[draggable="true"]');
        if (!li || li === dragEl) return;
        const rect = li.getBoundingClientRect();
        const before = (e.clientY - rect.top) < rect.height / 2;
        li.parentNode.insertBefore(dragEl, before ? li : li.nextSibling);
      });

      list.addEventListener('dragend', async () => {
        if (dragEl) dragEl.classList.remove('opacity-50');
        dragEl = null;
        // Only include draggable rows (i.e., open items)
        const ids = qsa('li[draggable="true"][data-id]', list).map(r => r.dataset.id);
        if (ids.length === 0) return;
        const fd = new FormData();
        ids.forEach(id => fd.append('ids', id));
        const token = qs('input[name="__RequestVerificationToken"]')?.value;
        if (token) fd.append('__RequestVerificationToken', token);
        try {
          await fetch('?handler=Reorder', { method: 'POST', body: fd, credentials: 'same-origin' });
        } catch (_) { /* swallow network errors silently */ }
      });
    });
  }

  // ---- 3) Bulk select mode & actions (no inline onclick) ----
  function initBulkActions() {
    const selectBtn = qs('#btnSelectMode');
    const bulkForm = qs('#bulkForm');
    if (!selectBtn || !bulkForm) return;

    let selecting = false;
    selectBtn.addEventListener('click', () => {
      selecting = !selecting;
      selectBtn.classList.toggle('active', selecting);
      bulkForm.classList.toggle('d-none', !selecting);
      qsa('.task-select').forEach(cb => cb.classList.toggle('d-none', !selecting));
    });

    // Attach to any button with data-bulk-action
    qsa('[data-bulk-action]').forEach(btn => {
      btn.addEventListener('click', () => {
        const action = btn.getAttribute('data-bulk-action');
        const ids = qsa('.task-select:checked').map(cb => cb.value);
        if (ids.length === 0) {
          alert('Select at least one task.');
          return;
        }
        qs('#bulkAction').value = action;
        qs('#bulkIds').value = ids.join(',');
        bulkForm.requestSubmit();
      });
    });
  }

  
  // ---- 0) Auto-submit done/undo checkboxes ----
  function initDoneAutosubmit() {
    document.addEventListener('change', (e) => {
      const cb = e.target.closest('.js-done-checkbox');
      if (!cb) return;
      const form = cb.closest('form');
      if (form) form.requestSubmit();
    }, { passive: true });
  }

  // Kick everything off
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      initRowActionReveal();
      initDoneAutosubmit();
      initDragReorder();
      initBulkActions();
    });
  } else {
    initRowActionReveal();
      initDoneAutosubmit();
    initDragReorder();
    initBulkActions();
  }
})();
