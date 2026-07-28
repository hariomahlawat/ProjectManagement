(() => {
  'use strict';

  const root = document.querySelector('.cpw');
  if (!root) return;

  const viewStorageKey = 'completedProjectsWorkspaceViewV2';
  const returnStateStorageKey = 'completedProjectsReturnStateV1';
  const validViews = new Set(['register', 'overview', 'quality']);

  const stickyChrome = [
    document.querySelector('.pm-topbar'),
    document.querySelector('.pm-module-subnav-wrap')
  ].filter(Boolean);

  const syncStickyOffset = () => {
    const offset = stickyChrome.reduce((total, element) => {
      if (!(element instanceof HTMLElement) || element.getClientRects().length === 0) return total;
      return total + element.getBoundingClientRect().height;
    }, 0);

    root.style.setProperty('--cpw-sticky-offset', `${Math.ceil(offset || 98)}px`);
  };

  syncStickyOffset();
  window.addEventListener('resize', syncStickyOffset, { passive: true });

  if ('ResizeObserver' in window) {
    const chromeObserver = new ResizeObserver(syncStickyOffset);
    stickyChrome.forEach((element) => chromeObserver.observe(element));
  }

  const readStoredView = () => {
    try {
      const stored = sessionStorage.getItem(viewStorageKey);
      return validViews.has(stored) ? stored : null;
    } catch {
      return null;
    }
  };

  const storeView = (view) => {
    try {
      sessionStorage.setItem(viewStorageKey, view);
    } catch {
      // Session storage is an enhancement only; the register remains the default.
    }
  };

  const filterToggle = root.querySelector('[data-filter-toggle]');
  const filterPanel = root.querySelector('[data-filter-panel]');
  filterToggle?.addEventListener('click', () => {
    const open = filterPanel?.classList.toggle('is-open') ?? false;
    filterToggle.setAttribute('aria-expanded', String(open));
  });

  const tabs = [...root.querySelectorAll('[data-view]')];
  const panels = [...root.querySelectorAll('[data-view-panel]')];
  const workspaceViewInput = root.querySelector('[data-workspace-view-input]');

  const syncViewState = (view) => {
    if (workspaceViewInput) workspaceViewInput.value = view;

    try {
      const url = new URL(window.location.href);
      if (view === 'register') {
        url.searchParams.delete('WorkspaceView');
      } else {
        url.searchParams.set('WorkspaceView', view);
      }

      window.history.replaceState(window.history.state, '', url);
    } catch {
      // URL synchronisation is progressive enhancement only.
    }
  };

  const setView = (requestedView, focusTab = false) => {
    const view = validViews.has(requestedView) ? requestedView : 'register';

    tabs.forEach((tab) => {
      const active = tab.dataset.view === view;
      tab.classList.toggle('is-active', active);
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
      if (active && focusTab) tab.focus();
    });

    panels.forEach((panel) => {
      const active = panel.dataset.viewPanel === view;
      panel.classList.toggle('is-active', active);
      panel.hidden = !active;
    });

    storeView(view);
    syncViewState(view);
  };

  tabs.forEach((tab) => {
    tab.addEventListener('click', () => setView(tab.dataset.view));
    tab.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
      event.preventDefault();
      const currentIndex = tabs.indexOf(tab);
      const direction = event.key === 'ArrowRight' ? 1 : -1;
      const nextIndex = (currentIndex + direction + tabs.length) % tabs.length;
      setView(tabs[nextIndex].dataset.view, true);
    });
  });

  root.querySelectorAll('[data-open-view]').forEach((control) => {
    control.addEventListener('click', () => setView(control.dataset.openView, true));
  });

  setView((validViews.has(root.dataset.requestedView) ? root.dataset.requestedView : null) || readStoredView() || root.dataset.defaultView || 'register');

  const drawer = root.querySelector('[data-drawer]');
  const drawerBody = root.querySelector('[data-drawer-body]');
  const drawerSuccess = root.querySelector('[data-drawer-success]');
  const backdrop = root.querySelector('[data-drawer-backdrop]');
  let returnFocusTarget = null;
  let drawerSuccessTimer = null;

  const getFocusableElements = () => drawer
    ? [...drawer.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
        .filter((element) => !element.hidden && element.getAttribute('aria-hidden') !== 'true')
    : [];

  const hideDrawerSuccess = () => {
    if (drawerSuccessTimer) {
      window.clearTimeout(drawerSuccessTimer);
      drawerSuccessTimer = null;
    }

    if (drawerSuccess) {
      drawerSuccess.hidden = true;
      const message = drawerSuccess.querySelector('span');
      if (message) message.textContent = '';
    }
  };

  const showDrawerSuccess = (message) => {
    if (!drawerSuccess || !message) return;

    const target = drawerSuccess.querySelector('span');
    if (target) target.textContent = message;
    drawerSuccess.hidden = false;

    if (drawerSuccessTimer) window.clearTimeout(drawerSuccessTimer);
    drawerSuccessTimer = window.setTimeout(hideDrawerSuccess, 5000);
  };

  const closeDrawer = () => {
    if (!drawer?.classList.contains('is-open')) return;

    hideDrawerSuccess();
    drawer.classList.remove('is-open');
    drawer.setAttribute('aria-hidden', 'true');
    if (backdrop) backdrop.hidden = true;
    document.body.classList.remove('cpw-drawer-open');

    const target = returnFocusTarget;
    returnFocusTarget = null;
    if (target instanceof HTMLElement && document.contains(target)) target.focus();
  };

  const openDrawer = (id, opener, focusClose = true, successMessage = '') => {
    const template = document.getElementById(`cpw-project-${id}`);
    if (!template || !drawer || !drawerBody) return;

    hideDrawerSuccess();
    returnFocusTarget = opener instanceof HTMLElement ? opener : document.activeElement;
    drawerBody.replaceChildren(template.content.cloneNode(true));
    drawer.classList.add('is-open');
    drawer.setAttribute('aria-hidden', 'false');
    if (backdrop) backdrop.hidden = false;
    document.body.classList.add('cpw-drawer-open');

    if (successMessage) showDrawerSuccess(successMessage);

    if (focusClose) {
      requestAnimationFrame(() => drawer.querySelector('[data-close-drawer]')?.focus());
    }
  };

  root.addEventListener('click', (event) => {
    const opener = event.target.closest('[data-open-project]');
    if (opener) {
      event.preventDefault();
      openDrawer(opener.dataset.openProject, opener);
      return;
    }

    const editLink = event.target.closest('[data-edit-project]');
    if (editLink instanceof HTMLAnchorElement) {
      event.preventDefault();

      const currentUrl = `${window.location.pathname}${window.location.search}${window.location.hash}`;
      const state = {
        returnUrl: currentUrl,
        scrollY: window.scrollY,
        projectId: editLink.dataset.projectId,
        view: tabs.find((tab) => tab.classList.contains('is-active'))?.dataset.view || 'register'
      };

      try {
        sessionStorage.setItem(returnStateStorageKey, JSON.stringify(state));
      } catch {
        // Context restoration remains optional when session storage is unavailable.
      }

      const target = new URL(editLink.href, window.location.origin);
      target.searchParams.set('returnUrl', currentUrl);
      window.location.assign(target.toString());
      return;
    }

    if (event.target.closest('[data-close-drawer]')) closeDrawer();
  });

  backdrop?.addEventListener('click', closeDrawer);
  document.addEventListener('keydown', (event) => {
    if (!drawer?.classList.contains('is-open')) return;

    if (event.key === 'Escape') {
      event.preventDefault();
      closeDrawer();
      return;
    }

    if (event.key !== 'Tab') return;
    const focusable = getFocusableElements();
    if (!focusable.length) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  const restoreReturnState = () => {
    let state = null;

    try {
      const raw = sessionStorage.getItem(returnStateStorageKey);
      state = raw ? JSON.parse(raw) : null;
    } catch {
      state = null;
    }

    if (!state || typeof state !== 'object') return;

    const currentUrl = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    if (state.returnUrl !== currentUrl) return;

    try {
      sessionStorage.removeItem(returnStateStorageKey);
    } catch {
      // No action required.
    }

    if (validViews.has(state.view)) setView(state.view);

    requestAnimationFrame(() => {
      window.scrollTo({ top: Number(state.scrollY) || 0, behavior: 'auto' });
      requestAnimationFrame(() => {
        const opener = root.querySelector(`[data-open-project="${CSS.escape(String(state.projectId || ''))}"]`);
        openDrawer(state.projectId, opener, false, root.dataset.successMessage || '');
      });
    });
  };

  restoreReturnState();
})();
