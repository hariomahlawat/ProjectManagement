// wwwroot/js/todo.js

(function () {
  // Ensure Bootstrap is available
  if (!window.bootstrap) return;

  const TODO_TOGGLE_SELECTOR = '.todo-list [data-bs-toggle="dropdown"]';
  const processedToggles = new WeakSet();
  const portalRegistry = new WeakMap();
  const FLIP_FALLBACKS = ['bottom-end', 'top-end', 'top-start', 'bottom-start'];
  const DEFAULT_PLACEMENT = 'bottom-end';
  const ALT_PLACEMENT = 'top-end';

  function onNextFrame(callback) {
    if (typeof window.requestAnimationFrame === 'function') {
      return window.requestAnimationFrame(callback);
    }
    return window.setTimeout(callback, 16);
  }

  function measureMenuHeight(menu) {
    if (!(menu instanceof HTMLElement)) return 0;
    const rect = menu.getBoundingClientRect();
    if (rect.height > 0) return rect.height;

    const { display, visibility } = menu.style;
    menu.style.visibility = 'hidden';
    menu.style.display = 'block';
    const measuredRect = menu.getBoundingClientRect();
    menu.style.display = display;
    menu.style.visibility = visibility;
    return measuredRect.height;
  }

  function schedulePopperPlacement(instance, placement, menu) {
    if (!instance) return;

    const desiredPlacement = placement || DEFAULT_PLACEMENT;
    const shouldHideMenu = menu instanceof HTMLElement && desiredPlacement === ALT_PLACEMENT;
    const previousVisibility = shouldHideMenu ? menu.style.visibility : null;

    if (shouldHideMenu) {
      menu.style.visibility = 'hidden';
    }

    const applyPlacement = (attempt = 0) => {
      const popper = instance._popper;
      if (!popper) {
        if (attempt >= 5) {
          if (shouldHideMenu) {
            menu.style.visibility = previousVisibility || '';
          }
          return;
        }
        onNextFrame(() => applyPlacement(attempt + 1));
        return;
      }

      popper.setOptions((options) => ({
        ...options,
        placement: desiredPlacement
      }));
      popper.update();

      if (shouldHideMenu) {
        menu.style.visibility = previousVisibility || '';
      }
    };

    onNextFrame(() => applyPlacement());
  }

  function ensureDropdownInstance(toggle) {
    const popperConfig = {
      strategy: 'fixed',
      modifiers: [
        { name: 'flip', options: { fallbackPlacements: FLIP_FALLBACKS } },
        { name: 'preventOverflow', options: { boundary: 'viewport' } },
        { name: 'offset', options: { offset: [0, 6] } }
      ]
    };

    const instance = bootstrap.Dropdown.getOrCreateInstance(toggle, { popperConfig });
    if (instance && instance._config) {
      instance._config.popperConfig = popperConfig;
    }
    return instance;
  }

  function getPortalData(toggle) {
    let data = portalRegistry.get(toggle);
    if (data) {
      return data;
    }

    const dropdown = toggle.closest('.dropdown');
    if (!dropdown) return null;

    if (!dropdown.closest('.todo-list')) return null;

    const menu = dropdown.querySelector('.dropdown-menu');
    if (!menu) return null;

    data = {
      menu,
      row: dropdown.closest('.todo-row'),
      originalParent: menu.parentNode,
      originalNextSibling: menu.nextSibling,
      portalActive: false,
      customPlacement: null
    };

    portalRegistry.set(toggle, data);
    return data;
  }

  function setupTodoDropdown(toggle) {
    if (!(toggle instanceof HTMLElement)) return;
    if (processedToggles.has(toggle)) return;

    const data = getPortalData(toggle);
    if (!data) return;

    ensureDropdownInstance(toggle);
    processedToggles.add(toggle);
  }

  function scanTodoDropdowns(root) {
    const elements = [];
    if (!root || root === document) {
      elements.push(...document.querySelectorAll(TODO_TOGGLE_SELECTOR));
    } else if (root instanceof Element || root instanceof DocumentFragment) {
      if (root instanceof Element && root.matches(TODO_TOGGLE_SELECTOR)) {
        elements.push(root);
      }
      elements.push(...root.querySelectorAll(TODO_TOGGLE_SELECTOR));
    }

    elements.forEach(setupTodoDropdown);
  }

  function activatePortal(toggle, data) {
    if (!data || data.portalActive) return;

    // refresh the anchor position in case the DOM changed while detached
    data.originalParent = data.menu.parentNode;
    data.originalNextSibling = data.menu.nextSibling;

    if (data.menu.parentNode !== document.body) {
      document.body.appendChild(data.menu);
    }

    data.portalActive = true;
    data.row?.classList.add('todo-row--dropdown-open');
  }

  function restorePortal(data) {
    if (!data || !data.portalActive) return;

    data.portalActive = false;
    data.row?.classList.remove('todo-row--dropdown-open');

    const { menu, originalParent, originalNextSibling } = data;
    if (!(originalParent instanceof Element) || !originalParent.isConnected) {
      menu.remove();
      return;
    }

    if (originalNextSibling && originalNextSibling.parentNode === originalParent) {
      originalParent.insertBefore(menu, originalNextSibling);
    } else {
      originalParent.appendChild(menu);
    }
  }

  function handleDropdownShow(event) {
    const toggle = event.target;
    if (!(toggle instanceof HTMLElement)) return;

    document.querySelectorAll('[data-bs-toggle="dropdown"]').forEach((btn) => {
      if (btn === toggle) return;
      const instance = bootstrap.Dropdown.getInstance(btn);
      if (instance) instance.hide();
    });

    if (!toggle.closest('.todo-list')) return;

    setupTodoDropdown(toggle);
    const data = getPortalData(toggle);
    if (!data) return;

    activatePortal(toggle, data);

    const instance = bootstrap.Dropdown.getInstance(toggle);
    if (!instance) return;

    const { menu } = data;
    if (!(menu instanceof HTMLElement)) return;

    let placement = DEFAULT_PLACEMENT;
    const container = toggle.closest('.todo-list') || toggle.closest('.todo-widget');
    const menuHeight = measureMenuHeight(menu);

    if (container instanceof HTMLElement && menuHeight > 0) {
      const toggleRect = toggle.getBoundingClientRect();
      const containerRect = container.getBoundingClientRect();
      const spaceAbove = Math.max(0, toggleRect.top - containerRect.top);
      const spaceBelow = Math.max(0, containerRect.bottom - toggleRect.bottom);

      if (spaceBelow < menuHeight && spaceAbove >= menuHeight) {
        placement = ALT_PLACEMENT;
      }
    }

    data.customPlacement = placement === ALT_PLACEMENT ? placement : null;
    schedulePopperPlacement(instance, placement, menu);
  }

  function handleDropdownHidden(event) {
    const toggle = event.target;
    if (!(toggle instanceof HTMLElement)) return;

    const data = portalRegistry.get(toggle);
    if (!data) return;

    const instance = bootstrap.Dropdown.getInstance(toggle);
    if (instance && instance._popper) {
      instance._popper.setOptions((options) => ({
        ...options,
        placement: DEFAULT_PLACEMENT
      }));
      instance._popper.update();
    }

    data.customPlacement = null;

    restorePortal(data);
  }

  document.addEventListener('show.bs.dropdown', handleDropdownShow);
  document.addEventListener('hidden.bs.dropdown', handleDropdownHidden);

  let dropdownObserver;

  function startDropdownObserver() {
    if (!('MutationObserver' in window)) return;
    if (dropdownObserver || !document.body) return;

    dropdownObserver = new MutationObserver((records) => {
      for (const record of records) {
        for (const node of record.addedNodes) {
          if (node instanceof HTMLElement) {
            scanTodoDropdowns(node);
          }
        }
      }
    });

    dropdownObserver.observe(document.body, { childList: true, subtree: true });
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

  function initAll() {
    scanTodoDropdowns(document);
    startDropdownObserver();
    initDoneAutosubmit();
  }

  // Rescan for dropdowns after HTMX swaps insert todo rows dynamically
  document.addEventListener('htmx:afterSwap', (event) => {
    const target = event.detail && event.detail.target;
    if (target instanceof HTMLElement) {
      scanTodoDropdowns(target);
    }
  });

  // ---------- Kick things off ----------
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => initAll());
  } else {
    initAll();
  }
})();

