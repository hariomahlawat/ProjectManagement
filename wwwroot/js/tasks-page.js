// wwwroot/js/tasks-page.js
// Handles: inline-actions visibility, drag reordering, and done checkbox auto-submit
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

  // ---- 3) Bulk selection toolbar ----
  function initBulkSelection() {
    const toolbar = qs('#bulkSelectionToolbar');
    if (!toolbar) return;

    const countEl = qs('[data-bulk-role="count"]', toolbar);
    const selectAll = qs('#bulkSelectAll', toolbar);
    const clearBtn = qs('[data-bulk-action="clear"]', toolbar);
    const forms = qsa('form.bulk-action-form', toolbar);
    const listContainer = document.getElementById('taskListContainer');

    const getCheckboxes = () => qsa('.task-select');

    function syncForms(ids) {
      forms.forEach(form => {
        const holder = qs('.bulk-selected-inputs', form);
        if (!holder) return;
        holder.innerHTML = '';
        ids.forEach(id => {
          const input = document.createElement('input');
          input.type = 'hidden';
          input.name = 'ids';
          input.value = id;
          holder.appendChild(input);
        });
      });
    }

    function updateToolbar() {
      const boxes = getCheckboxes();
      const selectedBoxes = boxes.filter(cb => cb.checked);
      const ids = selectedBoxes.map(cb => cb.value);
      const count = ids.length;

      if (countEl) {
        const label = count === 1 ? '1 task selected' : `${count} tasks selected`;
        countEl.textContent = count > 0 ? label : '0 tasks selected';
      }

      if (count > 0) {
        toolbar.classList.remove('d-none');
      } else {
        toolbar.classList.add('d-none');
      }

      if (selectAll) {
        if (boxes.length === 0) {
          selectAll.checked = false;
          selectAll.indeterminate = false;
          selectAll.disabled = true;
        } else {
          selectAll.disabled = false;
          const allChecked = count === boxes.length;
          selectAll.checked = allChecked;
          selectAll.indeterminate = count > 0 && !allChecked;
        }
      }

      syncForms(ids);
    }

    document.addEventListener('change', (e) => {
      const cb = e.target.closest('.task-select');
      if (!cb) return;
      updateToolbar();
    }, { passive: true });

    if (selectAll) {
      selectAll.addEventListener('change', () => {
        const boxes = getCheckboxes();
        boxes.forEach(cb => {
          cb.checked = !!selectAll.checked;
        });
        updateToolbar();
      });
    }

    if (clearBtn) {
      clearBtn.addEventListener('click', (e) => {
        e.preventDefault();
        const boxes = getCheckboxes();
        boxes.forEach(cb => { cb.checked = false; });
        updateToolbar();
      });
    }

    forms.forEach(form => {
      form.addEventListener('submit', (e) => {
        const boxes = getCheckboxes();
        const selected = boxes.filter(cb => cb.checked).map(cb => cb.value);
        if (selected.length === 0) {
          e.preventDefault();
          return;
        }
        syncForms(selected);
      });
    });

    if (listContainer && 'MutationObserver' in window) {
      const observer = new MutationObserver(() => updateToolbar());
      observer.observe(listContainer, { childList: true, subtree: true });
    }

    updateToolbar();
  }

  // ---- 0) Auto-submit done/undo checkboxes ----
  function initDoneAutosubmit() {
    function markVisualDone(cb) {
      const row = cb.closest('li[data-id]');
      if (!row) return;
      if (cb.checked) {
        row.setAttribute('data-status', 'done');
        // If this is a non-completed tab (rows are draggable), give an instant vanish hint.
        if (row.getAttribute('draggable') === 'true') {
          row.classList.add('vanish');
        }
      } else {
        row.removeAttribute('data-status');
        row.classList.remove('vanish');
      }
    }
    document.addEventListener('change', (e) => {
      const cb = e.target.closest('.js-done-checkbox');
      if (cb) markVisualDone(cb);
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
      initBulkSelection();
    });
  } else {
    initRowActionReveal();
    initDoneAutosubmit();
    initDragReorder();
    initBulkSelection();
  }
})();
