const DESKTOP_MEDIA_QUERY = '(min-width: 992px)';

const FOCUSABLE_SELECTORS = [
  'a[href]',
  'area[href]',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'button:not([disabled])',
  'iframe',
  'object',
  'embed',
  '[contenteditable]',
  '[tabindex]:not([tabindex="-1"])'
].join(', ');

function normaliseId(id) {
  return typeof id === 'string' ? id.replace(/^#/, '') : '';
}

function findTogglesForDrawer(drawerId) {
  if (!drawerId) {
    return [];
  }

  const normalised = normaliseId(drawerId);
  const candidates = document.querySelectorAll('[data-drawer-toggle]');
  const result = [];

  candidates.forEach((candidate) => {
    if (!(candidate instanceof HTMLElement)) {
      return;
    }

    const target =
      candidate.getAttribute('data-drawer-target') ||
      candidate.getAttribute('aria-controls') ||
      candidate.getAttribute('data-target') ||
      candidate.getAttribute('href');

    if (!target) {
      return;
    }

    if (normaliseId(target) === normalised) {
      result.push(candidate);
    }
  });

  return result;
}

function getFocusableElements(container) {
  return Array.from(container.querySelectorAll(FOCUSABLE_SELECTORS)).filter(
    (element) =>
      element instanceof HTMLElement &&
      !element.hasAttribute('disabled') &&
      element.getAttribute('aria-hidden') !== 'true'
  );
}

function setupDrawer(drawer) {
  if (!(drawer instanceof HTMLElement)) {
    return;
  }

  if (drawer.dataset.drawerInitialized === 'true') {
    return;
  }

  drawer.dataset.drawerInitialized = 'true';

  const providedId = drawer.id || drawer.getAttribute('data-drawer') || '';
  const resolvedId = normaliseId(providedId);

  if (resolvedId && !drawer.id) {
    drawer.id = resolvedId;
  }

  const panel = drawer.querySelector('[data-drawer-panel]') || drawer;
  const overlay = drawer.querySelector('[data-drawer-overlay]');
  const closeButtons = Array.from(drawer.querySelectorAll('[data-drawer-close]'));
  const toggles = findTogglesForDrawer(resolvedId);
  const isStaticDrawer = drawer.hasAttribute('data-drawer-static');

  if (panel instanceof HTMLElement && !panel.hasAttribute('tabindex')) {
    panel.setAttribute('tabindex', '-1');
  }

  toggles.forEach((toggle) => {
    if (!toggle.hasAttribute('aria-controls') && resolvedId) {
      toggle.setAttribute('aria-controls', resolvedId);
    }

    toggle.setAttribute('aria-expanded', drawer.classList.contains('is-open') ? 'true' : 'false');
  });

  let restoreFocusTo = null;
  let isOpen = drawer.classList.contains('is-open');

  if (!drawer.hasAttribute('aria-hidden')) {
    drawer.setAttribute('aria-hidden', 'true');
  }

  if (!isOpen) {
    drawer.classList.remove('is-open');
  }

  const isToggleVisible = (toggle) => {
    if (!(toggle instanceof HTMLElement)) {
      return false;
    }

    if (typeof window !== 'undefined') {
      const style = window.getComputedStyle(toggle);

      if (style.display === 'none' || style.visibility === 'hidden') {
        return false;
      }
    }

    if (toggle.offsetParent !== null) {
      return true;
    }

    const rect = toggle.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };

  const desktopMediaQuery =
    typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia(DESKTOP_MEDIA_QUERY)
      : null;

  const hasVisibleToggle = () => toggles.some((toggle) => isToggleVisible(toggle));

  const shouldForceVisible = () => {
    if (!isStaticDrawer) {
      return false;
    }

    if (desktopMediaQuery && desktopMediaQuery.matches) {
      return true;
    }

    return !hasVisibleToggle();
  };

  const syncAriaHidden = () => {
    if (shouldForceVisible()) {
      drawer.setAttribute('aria-hidden', 'false');
    } else {
      drawer.setAttribute('aria-hidden', isOpen ? 'false' : 'true');
    }
  };

  const handleKeydown = (event) => {
    if (!isOpen) {
      return;
    }

    if (event.key === 'Escape' || event.key === 'Esc') {
      event.preventDefault();
      closeDrawer();
      return;
    }

    if (event.key !== 'Tab') {
      return;
    }

    const focusable = getFocusableElements(panel instanceof HTMLElement ? panel : drawer);

    if (focusable.length === 0) {
      event.preventDefault();
      if (panel instanceof HTMLElement) {
        panel.focus({ preventScroll: true });
      }
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;

    if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus({ preventScroll: true });
    } else if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus({ preventScroll: true });
    }
  };

  const focusFirstElement = () => {
    const focusable = getFocusableElements(panel instanceof HTMLElement ? panel : drawer);

    if (focusable.length > 0) {
      focusable[0].focus({ preventScroll: true });
      return;
    }

    if (panel instanceof HTMLElement) {
      panel.focus({ preventScroll: true });
    }
  };

  const openDrawer = () => {
    if (isOpen) {
      return;
    }

    restoreFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    drawer.classList.add('is-open');
    toggles.forEach((toggle) => toggle.setAttribute('aria-expanded', 'true'));
    isOpen = true;
    syncAriaHidden();
    document.addEventListener('keydown', handleKeydown);
    window.requestAnimationFrame(focusFirstElement);
  };

  const closeDrawer = () => {
    if (!isOpen) {
      return;
    }

    drawer.classList.remove('is-open');
    toggles.forEach((toggle) => toggle.setAttribute('aria-expanded', 'false'));
    isOpen = false;
    syncAriaHidden();
    document.removeEventListener('keydown', handleKeydown);

    if (restoreFocusTo && typeof restoreFocusTo.focus === 'function') {
      restoreFocusTo.focus({ preventScroll: true });
    }

    restoreFocusTo = null;
  };

  const toggleDrawer = (event) => {
    event.preventDefault();
    if (isOpen) {
      closeDrawer();
    } else {
      openDrawer();
    }
  };

  toggles.forEach((toggle) => {
    toggle.addEventListener('click', toggleDrawer);
  });

  syncAriaHidden();

  if (desktopMediaQuery) {
    const handleMediaChange = () => {
      syncAriaHidden();
    };

    if (typeof desktopMediaQuery.addEventListener === 'function') {
      desktopMediaQuery.addEventListener('change', handleMediaChange);
    } else if (typeof desktopMediaQuery.addListener === 'function') {
      desktopMediaQuery.addListener(handleMediaChange);
    }
  }

  closeButtons.forEach((closeButton) => {
    closeButton.addEventListener('click', (event) => {
      event.preventDefault();
      closeDrawer();
    });
  });

  if (overlay) {
    overlay.addEventListener('click', closeDrawer);
  }
};

export function initDrawer() {
  const drawers = document.querySelectorAll('[data-drawer]');
  drawers.forEach((drawer) => setupDrawer(drawer));
}

export default initDrawer;
