// wwwroot/js/tasks-page.js
// Handles: inline-actions visibility, drag reordering, and done checkbox auto-submit
// Requires: Bootstrap (for CSS only), Anti-forgery token in forms

(function () {
  function qs(sel, root) { return (root || document).querySelector(sel); }
  function qsa(sel, root) { return Array.from((root || document).querySelectorAll(sel)); }

  function isEditable(el) {
    if (!el) return false;
    const tag = el.tagName;
    return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
  }

  function initTaskToast() {
    const root = document.getElementById('taskToast');
    if (!root) return null;

    const messageEl = qs('[data-toast-role="message"]', root);
    const undoForm = qs('form[data-toast-role="undo-form"]', root);
    const undoInput = qs('[data-toast-role="undo-id"]', root);
    const undoBtn = qs('[data-toast-action="undo"]', root);
    const dismissBtn = qs('[data-toast-action="dismiss"]', root);

    let hideTimer = null;

    function clearTimer() {
      if (hideTimer) {
        clearTimeout(hideTimer);
        hideTimer = null;
      }
    }

    function setUndoId(id) {
      if (undoInput) {
        undoInput.value = id || '';
      }
    }

    function finalizeHide() {
      root.hidden = true;
    }

    function hide(options = {}) {
      const { immediate = false } = options;
      const wasVisible = root.classList.contains('is-visible');
      clearTimer();
      root.classList.remove('is-visible');
      root.setAttribute('aria-hidden', 'true');

      const reduceMotion = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

      if (immediate || !wasVisible || reduceMotion) {
        finalizeHide();
        return;
      }

      const onTransitionEnd = (event) => {
        if (event.target !== root || event.propertyName !== 'opacity') {
          return;
        }
        root.removeEventListener('transitionend', onTransitionEnd);
        if (root.classList.contains('is-visible')) {
          return;
        }
        finalizeHide();
      };

      root.addEventListener('transitionend', onTransitionEnd);
    }

    function show({ message, undoId, autoDismiss = true } = {}) {
      if (messageEl && typeof message === 'string') {
        messageEl.textContent = message;
      }
      setUndoId(undoId);
      clearTimer();
      root.hidden = false;
      requestAnimationFrame(() => {
        root.classList.add('is-visible');
        root.setAttribute('aria-hidden', 'false');
      });
      if (autoDismiss) {
        hideTimer = window.setTimeout(() => hide(), 6000);
      }
    }

    if (undoBtn) {
      undoBtn.addEventListener('click', (event) => {
        event.preventDefault();
        if (!undoForm || !undoInput || !undoInput.value) {
          return;
        }
        hide();
        undoForm.requestSubmit();
      });
    }

    if (dismissBtn) {
      dismissBtn.addEventListener('click', (event) => {
        event.preventDefault();
        hide();
      });
    }

    if (undoForm) {
      undoForm.addEventListener('submit', () => hide({ immediate: true }));
    }

    root.addEventListener('pointerenter', clearTimer);
    root.addEventListener('pointerleave', () => {
      if (!root.classList.contains('is-visible')) return;
      clearTimer();
      hideTimer = window.setTimeout(() => hide(), 3000);
    });

    root.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        hide();
      }
    });

    const autoMessage = root.dataset.toastAutoMessage;
    const autoId = root.dataset.toastAutoId;
    const autoVariant = root.dataset.toastAutoVariant;
    if (autoMessage) {
      const autoDismiss = autoVariant === 'undo' ? false : true;
      show({ message: autoMessage, undoId: autoId || '', autoDismiss });
    }

    return {
      show,
      hide,
      setUndo: setUndoId
    };
  }

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

    const FAILURE_THRESHOLD = 3;
    const failureState = new WeakMap();

    function getFailureState(list) {
      let state = failureState.get(list);
      if (!state) {
        state = { failures: 0, notified: false, snapshot: null };
        failureState.set(list, state);
      }
      return state;
    }

    function captureSnapshot(list, state) {
      const rows = qsa('li[draggable="true"][data-id]', list);
      state.snapshot = rows.map(node => ({ node, nextSibling: node.nextSibling }));
    }

    function revertSnapshot(list, state) {
      if (!state.snapshot) return;
      for (let i = state.snapshot.length - 1; i >= 0; i -= 1) {
        const { node, nextSibling } = state.snapshot[i];
        list.insertBefore(node, nextSibling || null);
      }
    }

    lists.forEach(list => {
      list.addEventListener('dragstart', e => {
        const li = e.target.closest('.todo-row[draggable="true"]');
        if (!li) return;
        dragEl = li;
        e.dataTransfer.effectAllowed = 'move';
        li.classList.add('opacity-50');
        const state = getFailureState(list);
        captureSnapshot(list, state);
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
        const state = getFailureState(list);
        const fd = new FormData();
        ids.forEach(id => fd.append('ids', id));
        const token = qs('input[name="__RequestVerificationToken"]')?.value;
        if (token) fd.append('__RequestVerificationToken', token);
        try {
          const response = await fetch('?handler=Reorder', { method: 'POST', body: fd, credentials: 'same-origin' });
          if (!response.ok) {
            throw new Error('Failed to reorder');
          }
          state.failures = 0;
          state.notified = false;
          state.snapshot = null;
        } catch (_) {
          state.failures = (state.failures || 0) + 1;
          revertSnapshot(list, state);
          if (state.failures >= FAILURE_THRESHOLD && !state.notified) {
            window.alert('We could not save the new order. Please try again later.');
            state.notified = true;
          }
        }
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

  // ---- 4) Keyboard navigation for task rows ----
  function initKeyboardNav() {
    const listContainer = document.getElementById('taskListContainer');
    if (!listContainer) return;

    const quickAddInput = qs('form input[name="NewTitle"]');
    const quickAddForm = quickAddInput ? quickAddInput.form : null;

    let currentRow = null;

    function getRows() {
      return qsa('.todo-row');
    }

    function ensureRowMetadata(row, isActive) {
      if (!row) return;
      if (!row.hasAttribute('tabindex')) {
        row.setAttribute('tabindex', '-1');
      }
      row.setAttribute('aria-selected', isActive ? 'true' : 'false');
      if (isActive) {
        row.setAttribute('tabindex', '0');
      } else {
        row.setAttribute('tabindex', '-1');
      }
    }

    function prepareRows() {
      const rows = getRows();
      if (currentRow && !rows.includes(currentRow)) {
        currentRow = null;
      }
      rows.forEach(row => ensureRowMetadata(row, row === currentRow));
      return rows;
    }

    function focusRow(row, { scroll = true } = {}) {
      if (!row) return;
      const rows = prepareRows();
      if (!rows.includes(row)) return;
      if (currentRow && currentRow !== row) {
        ensureRowMetadata(currentRow, false);
      }
      currentRow = row;
      ensureRowMetadata(row, true);
      try {
        row.focus({ preventScroll: true });
      } catch (_) {
        row.focus();
      }
      if (scroll && typeof row.scrollIntoView === 'function') {
        row.scrollIntoView({ block: 'nearest' });
      }
    }

    function getActiveRow(fallbackToFirst = true) {
      const rows = prepareRows();
      if (currentRow && rows.includes(currentRow)) {
        return currentRow;
      }
      if (!fallbackToFirst || rows.length === 0) {
        return null;
      }
      return rows[0];
    }

    if ('MutationObserver' in window) {
      const observer = new MutationObserver(() => {
        const rows = prepareRows();
        if (!currentRow && rows.length > 0) {
          ensureRowMetadata(rows[0], false);
        }
      });
      observer.observe(listContainer, { childList: true, subtree: true });
    }

    prepareRows();

    document.addEventListener('keydown', (e) => {
      if (e.defaultPrevented) return;
      if (e.altKey || e.ctrlKey || e.metaKey) return;

      const activeEl = document.activeElement;
      if (isEditable(activeEl)) {
        if (quickAddInput && activeEl === quickAddInput && e.key === 'Enter' && !e.shiftKey) {
          e.preventDefault();
          if (quickAddForm) {
            quickAddForm.requestSubmit();
          }
        }
        return;
      }

      const rows = prepareRows();
      if (rows.length === 0) return;

      if (e.key === 'j' || e.key === 'k') {
        const active = getActiveRow();
        if (!active) return;
        if (!currentRow) {
          focusRow(active);
          e.preventDefault();
          return;
        }
        const idx = rows.indexOf(active);
        if (idx === -1) return;
        let nextIdx = idx;
        if (e.key === 'j' && idx < rows.length - 1) {
          nextIdx = idx + 1;
        } else if (e.key === 'k' && idx > 0) {
          nextIdx = idx - 1;
        }
        if (nextIdx !== idx) {
          focusRow(rows[nextIdx]);
          e.preventDefault();
        }
        return;
      }

      if (e.key === 'x') {
        const row = getActiveRow();
        if (!row) return;
        const checkbox = qs('.task-select', row);
        if (!checkbox) return;
        checkbox.checked = !checkbox.checked;
        const changeEvent = new Event('change', { bubbles: true });
        checkbox.dispatchEvent(changeEvent);
        focusRow(row, { scroll: false });
        e.preventDefault();
        return;
      }

      if (e.key === 'd') {
        const row = getActiveRow();
        if (!row) return;
        const doneCheckbox = qs('.js-done-checkbox', row);
        if (!doneCheckbox) return;
        doneCheckbox.checked = !doneCheckbox.checked;
        const changeEvent = new Event('change', { bubbles: true });
        doneCheckbox.dispatchEvent(changeEvent);
        focusRow(row, { scroll: false });
        e.preventDefault();
      }
    });
  }

  function initCompactToggle() {
    const toggle = qs('.tasks-compact-toggle');
    const container = document.getElementById('taskListContainer');
    if (!toggle || !container) return;

    const STORAGE_KEY = 'pm.tasks.compact';
    const labelEl = qs('[data-role="label"]', toggle);
    const labelCompact = toggle.dataset.labelCompact || 'Compact view';
    const labelComfy = toggle.dataset.labelComfy || 'Comfortable view';
    let currentState = null;

    function saveState(value) {
      try {
        window.localStorage.setItem(STORAGE_KEY, value ? 'true' : 'false');
      } catch (_) {
        /* no-op */
      }
    }

    function readStoredState() {
      try {
        return window.localStorage.getItem(STORAGE_KEY);
      } catch (_) {
        return null;
      }
    }

    function updateToggleLabel(isCompact) {
      const label = isCompact ? labelComfy : labelCompact;
      if (labelEl) {
        labelEl.textContent = label;
      }
      toggle.setAttribute('title', label);
      toggle.setAttribute('aria-label', label);
    }

    function applyState(isCompact, { persist = true } = {}) {
      const shouldAnnounce = currentState !== null && currentState !== isCompact;
      currentState = isCompact;
      container.classList.toggle('compact', isCompact);
      toggle.dataset.compact = isCompact ? 'true' : 'false';
      toggle.setAttribute('aria-pressed', isCompact ? 'true' : 'false');
      toggle.classList.toggle('is-active', isCompact);
      updateToggleLabel(isCompact);
      if (!isCompact) {
        qsa('.todo-row.task-row--meta-visible', container).forEach(row => row.classList.remove('task-row--meta-visible'));
      }
      if (shouldAnnounce) {
        container.dispatchEvent(new CustomEvent('tasks:compact-change', { detail: { compact: isCompact } }));
      }
      if (persist) {
        saveState(isCompact);
      }
    }

    const stored = readStoredState();
    const initial = stored === 'true' || (stored === null && toggle.dataset.compact === 'true');
    applyState(initial, { persist: false });

    toggle.addEventListener('click', (event) => {
      event.preventDefault();
      const current = toggle.dataset.compact === 'true';
      applyState(!current);
    });

    return { applyState };
  }

  function initFilterOffcanvas() {
    const bar = qs('[data-filter-bar]');
    const desktopSlot = qs('[data-filter-desktop]');
    const mobileSlot = qs('[data-filter-mobile]');
    const form = qs('[data-filter-form]');
    const offcanvasEl = document.getElementById('tasksFilterOffcanvas');
    if (!bar || !desktopSlot || !mobileSlot || !form) return;

    const media = typeof window.matchMedia === 'function'
      ? window.matchMedia('(max-width: 767.98px)')
      : null;
    let currentMode = null;

    function moveForm(target) {
      if (!target) return;
      if (target.contains(form)) return;
      target.appendChild(form);
    }

    function hideOffcanvas() {
      if (!offcanvasEl) return;
      if (typeof bootstrap !== 'undefined' && typeof bootstrap.Offcanvas !== 'undefined') {
        const instance = bootstrap.Offcanvas.getInstance(offcanvasEl);
        if (instance) {
          instance.hide();
        }
      }
    }

    function setMobileState(active) {
      if (active) {
        bar.setAttribute('data-mobile-active', 'true');
      } else {
        bar.removeAttribute('data-mobile-active');
      }
    }

    function applyLayout() {
      const shouldMobile = media ? media.matches : false;
      if (currentMode === shouldMobile) return;
      currentMode = shouldMobile;
      if (shouldMobile) {
        moveForm(mobileSlot);
        setMobileState(true);
      } else {
        moveForm(desktopSlot);
        setMobileState(false);
        hideOffcanvas();
      }
    }

    applyLayout();

    if (media) {
      const handler = () => applyLayout();
      if (typeof media.addEventListener === 'function') {
        media.addEventListener('change', handler);
      } else if (typeof media.addListener === 'function') {
        media.addListener(handler);
      }
    }
  }

  function initTouchMetaReveal() {
    const container = document.getElementById('taskListContainer');
    if (!container) return;

    const coarse = typeof window.matchMedia === 'function'
      ? window.matchMedia('(hover: none)')
      : null;
    const supportsTouch = coarse ? coarse.matches : ('ontouchstart' in window || navigator.maxTouchPoints > 0);
    if (!supportsTouch) return;

    const observed = new WeakSet();

    function shouldEnable() {
      if (container.classList.contains('compact')) return true;
      if (typeof window.matchMedia === 'function') {
        return window.matchMedia('(max-width: 767.98px)').matches;
      }
      return false;
    }

    function closeOthers(except) {
      qsa('.todo-row.task-row--meta-visible', container).forEach(row => {
        if (row !== except) {
          row.classList.remove('task-row--meta-visible');
        }
      });
    }

    function setupRow(row) {
      if (!row || observed.has(row)) return;
      const meta = qs('[data-task-meta]', row);
      if (!meta) return;
      observed.add(row);

      let pointerId = null;
      let startX = 0;
      let startY = 0;
      let didSwipe = false;

      row.addEventListener('pointerdown', (event) => {
        if (event.pointerType !== 'touch') return;
        if (!shouldEnable()) return;
        if (event.target.closest('[data-task-meta]')) return;
        if (isEditable(event.target)) return;
        pointerId = event.pointerId;
        startX = event.clientX;
        startY = event.clientY;
        didSwipe = false;
      });

      row.addEventListener('pointermove', (event) => {
        if (event.pointerType !== 'touch') return;
        if (pointerId === null || event.pointerId !== pointerId) return;
        if (!shouldEnable()) return;
        const dx = event.clientX - startX;
        const dy = event.clientY - startY;
        if (Math.abs(dy) > 40) {
          return;
        }
        if (dx < -28 && Math.abs(dx) > Math.abs(dy)) {
          if (!row.classList.contains('task-row--meta-visible')) {
            closeOthers(row);
          }
          row.classList.add('task-row--meta-visible');
          didSwipe = true;
        } else if (dx > 24 && Math.abs(dx) > Math.abs(dy)) {
          row.classList.remove('task-row--meta-visible');
          didSwipe = true;
        }
      });

      row.addEventListener('pointerup', (event) => {
        if (pointerId === null || event.pointerId !== pointerId) return;
        pointerId = null;
        if (!shouldEnable()) return;
        if (didSwipe) {
          didSwipe = false;
          return;
        }
        if (event.pointerType === 'touch' && !event.target.closest('[data-task-meta]')) {
          const isVisible = row.classList.toggle('task-row--meta-visible');
          if (isVisible) {
            closeOthers(row);
          }
        }
      });

      row.addEventListener('pointercancel', (event) => {
        if (pointerId !== null && event.pointerId === pointerId) {
          pointerId = null;
          didSwipe = false;
        }
      });
    }

    function refreshRows() {
      qsa('.todo-row[data-touch-meta]', container).forEach(setupRow);
    }

    refreshRows();

    if ('MutationObserver' in window) {
      const observer = new MutationObserver(() => refreshRows());
      observer.observe(container, { childList: true, subtree: true });
    }

    container.addEventListener('tasks:compact-change', (event) => {
      if (!event.detail || event.detail.compact) {
        return;
      }
      closeOthers(null);
    });

    document.addEventListener('pointerdown', (event) => {
      if (event.pointerType !== 'touch') return;
      const row = event.target.closest('.todo-row');
      if (row && container.contains(row)) {
        return;
      }
      closeOthers(null);
    });
  }

  // ---- 0) Auto-submit done/undo checkboxes ----
  function initDoneAutosubmit(toastApi) {
    const nativeSubmit = HTMLFormElement.prototype.submit;

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

    if (typeof window.fetch === 'function') {
      document.addEventListener('submit', (e) => {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (!form.classList.contains('js-toggle-done-form')) return;

        e.preventDefault();

        if (form.dataset.submitting === 'true') {
          return;
        }
        form.dataset.submitting = 'true';

        const cb = qs('.js-done-checkbox', form);
        if (!cb) {
          delete form.dataset.submitting;
          nativeSubmit.call(form);
          return;
        }

        const doneChecked = cb.checked;
        const formData = new FormData(form);
        const undoIdValue = formData.get('id');
        const undoId = typeof undoIdValue === 'string' ? undoIdValue : '';
        const message = form.dataset.successMessage || 'Task marked done.';
        const target = form.action || window.location.href;
        const method = (form.getAttribute('method') || 'post').toUpperCase();

        (async () => {
          try {
            const response = await fetch(target, {
              method,
              body: formData,
              credentials: 'same-origin'
            });
            if (!response.ok) {
              throw new Error('Request failed');
            }

            if (toastApi && typeof toastApi.setUndo === 'function') {
              toastApi.setUndo(doneChecked ? undoId : '');
            }

            if (doneChecked && toastApi && typeof toastApi.show === 'function') {
              toastApi.show({ message, undoId, autoDismiss: false });
            } else if (!doneChecked && toastApi && typeof toastApi.hide === 'function') {
              toastApi.hide();
            }
          } catch (_) {
            nativeSubmit.call(form);
          } finally {
            delete form.dataset.submitting;
          }
        })();
      }, true);
    }
  }

  // Kick everything off
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      const toast = initTaskToast();
      initRowActionReveal();
      initDoneAutosubmit(toast);
      initDragReorder();
      initBulkSelection();
      initKeyboardNav();
      initFilterOffcanvas();
      initTouchMetaReveal();
      initCompactToggle();
    });
  } else {
    const toast = initTaskToast();
    initRowActionReveal();
    initDoneAutosubmit(toast);
    initDragReorder();
    initBulkSelection();
    initKeyboardNav();
    initFilterOffcanvas();
    initTouchMetaReveal();
    initCompactToggle();
  }
})();
