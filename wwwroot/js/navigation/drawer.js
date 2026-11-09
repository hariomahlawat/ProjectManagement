// ---------- Utility selectors ----------
const FOCUSABLE_SELECTORS = [
  'a[href]',
  'area[href]',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'button:not([disabled])',
  // A <details> summary is natively focusable; collapsed drawer sections are reached through it.
  'details > summary:first-of-type',
  'iframe',
  'object',
  'embed',
  '[contenteditable]',
  '[tabindex]:not([tabindex="-1"])'
].join(', ');

// ---------- Utility helpers ----------
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


function syncBodyScrollLock() {
  const hasOpenDrawer = Boolean(document.querySelector('[data-drawer].is-open'));
  document.body.classList.toggle('pm-drawer-open', hasOpenDrawer);
}

function getFocusableElements(container) {
  return Array.from(container.querySelectorAll(FOCUSABLE_SELECTORS)).filter(
    (element) => {
      if (!(element instanceof HTMLElement)) {
        return false;
      }

      if (element.hasAttribute('disabled')) {
        return false;
      }

      if (element.getAttribute('aria-hidden') === 'true') {
        return false;
      }

      const hiddenAncestor = element.closest('[aria-hidden="true"]');
      if (hiddenAncestor && hiddenAncestor !== element) {
        return false;
      }

      const closedDetails = element.closest('details:not([open])');
      if (closedDetails && !element.closest('summary')) {
        return false;
      }

      return true;
    }
  );
}

// ---------- Remembered section (per browser; a convenience, never required) ----------
const SECTION_STORAGE_KEY = 'prism.navDrawer.section';

function readRememberedSection() {
  try {
    return window.localStorage.getItem(SECTION_STORAGE_KEY);
  } catch {
    return null;
  }
}

function rememberSection(key) {
  try {
    window.localStorage.setItem(SECTION_STORAGE_KEY, key);
  } catch {
    // Storage blocked (private window, policy): the drawer works without it.
  }
}

// ---------- "Find a page" filter ----------
function normaliseQuery(value) {
  return (value || '').toLocaleLowerCase().split(/\s+/).filter(Boolean);
}

function createFilter(panel) {
  const body = panel.querySelector('.pm-drawer__body');
  if (!(body instanceof HTMLElement)) {
    return null;
  }

  const wrapper = document.createElement('div');
  wrapper.className = 'pm-drawer__filter';
  wrapper.setAttribute('role', 'search');

  const label = document.createElement('label');
  label.className = 'visually-hidden';
  label.htmlFor = 'pmDrawerFilter';
  label.textContent = 'Find a page in the menu';

  const icon = document.createElement('i');
  icon.className = 'bi bi-search pm-drawer__filter-icon';
  icon.setAttribute('aria-hidden', 'true');

  const input = document.createElement('input');
  input.type = 'search';
  input.id = 'pmDrawerFilter';
  input.className = 'pm-drawer__filter-input';
  input.placeholder = 'Find a page…';
  input.autocomplete = 'off';
  input.spellcheck = false;
  input.setAttribute('enterkeyhint', 'go');
  input.setAttribute('aria-keyshortcuts', '/');
  input.setAttribute('aria-describedby', 'pmDrawerFilterStatus');

  const status = document.createElement('p');
  status.id = 'pmDrawerFilterStatus';
  status.className = 'visually-hidden';
  status.setAttribute('aria-live', 'polite');

  const empty = document.createElement('p');
  empty.className = 'pm-drawer__filter-empty';
  empty.hidden = true;

  wrapper.append(label, icon, input, status);
  body.parentElement.insertBefore(wrapper, body);
  body.insertBefore(empty, body.firstChild);

  return { input, status, empty };
}

function closeOtherSections(drawer, openGroup) {
  drawer.querySelectorAll('details[data-drawer-section][open]').forEach((other) => {
    if (other !== openGroup) {
      other.open = false;
    }
  });
}

function setupDrawer(drawer) {
  if (!(drawer instanceof HTMLElement)) {
    return;
  }

  if (drawer.dataset.drawerInitialized === 'true') {
    return;
  }

  // The drawer trigger lives in the sticky top bar. Move the fixed drawer shell
  // to <body> so ancestor backdrop-filter, transform, containment, or overflow
  // can never change its viewport positioning or clip its contents.
  if (drawer.parentElement !== document.body) {
    document.body.appendChild(drawer);
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
  const groups = Array.from(drawer.querySelectorAll('[data-drawer-group]'));
  const toggles = findTogglesForDrawer(resolvedId);

  if (panel instanceof HTMLElement && !panel.hasAttribute('tabindex')) {
    panel.setAttribute('tabindex', '-1');
  }

  toggles.forEach((toggle) => {
    if (!toggle.hasAttribute('aria-controls') && resolvedId) {
      toggle.setAttribute('aria-controls', resolvedId);
    }

    toggle.setAttribute('aria-expanded', drawer.classList.contains('is-open') ? 'true' : 'false');
  });

  // ---------- Drawer group toggles ----------
  groups.forEach((group) => {
    if (!(group instanceof HTMLElement) || group.dataset.drawerGroupInitialized === 'true') {
      return;
    }

    const summary = group.querySelector('[data-drawer-group-summary]');
    const groupPanel = group.querySelector('[data-drawer-group-panel]');
    const links = group.querySelectorAll('[data-drawer-group-link]');

    links.forEach((link) => {
      link.addEventListener('click', (event) => {
        event.stopPropagation();
      });
    });

    const sync = () => {
      const expanded = group instanceof HTMLDetailsElement ? group.open : summary?.getAttribute('aria-expanded') === 'true';

      if (summary instanceof HTMLElement) {
        summary.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      }

      if (groupPanel instanceof HTMLElement) {
        groupPanel.setAttribute('aria-hidden', expanded ? 'false' : 'true');
      }
    };

    if (group instanceof HTMLDetailsElement) {
      group.addEventListener('toggle', () => {
        sync();
        // Sections are an accordion: opening one closes the others, which keeps the
        // drawer short enough to scan (docs/ux/navigation-drawer.md).
        // While the filter shows every matching section, the accordion stands aside.
        if (group.open && group.hasAttribute('data-drawer-section') && drawer.dataset.drawerFiltering !== 'true') {
          closeOtherSections(drawer, group);
          rememberSection(group.getAttribute('data-drawer-section'));
        }
      });
      sync();
    } else if (summary instanceof HTMLElement && groupPanel instanceof HTMLElement) {
      summary.addEventListener('click', (event) => {
        event.preventDefault();
        const expanded = summary.getAttribute('aria-expanded') === 'true';
        summary.setAttribute('aria-expanded', expanded ? 'false' : 'true');
        groupPanel.setAttribute('aria-hidden', expanded ? 'true' : 'false');
      });
      sync();
    }

    group.dataset.drawerGroupInitialized = 'true';
  });

  // ---------- Remembered section ----------
  // When no section holds the current page, reopen the one the user last opened.
  const sectionGroups = groups.filter((group) => group instanceof HTMLDetailsElement && group.hasAttribute('data-drawer-section'));
  if (sectionGroups.length > 0 && !sectionGroups.some((group) => group.open)) {
    const remembered = readRememberedSection();
    const match = sectionGroups.find((group) => group.getAttribute('data-drawer-section') === remembered);
    if (match) {
      match.open = true;
    }
  }

  // ---------- Filter ----------
  const filter = sectionGroups.length > 0 && panel instanceof HTMLElement ? createFilter(panel) : null;
  let openBeforeFilter = null;

  const visibleLinks = () =>
    Array.from(panel.querySelectorAll('a.pm-drawer__link')).filter((link) => {
      const item = link.closest('[data-drawer-item]');
      if (item && item.hidden) {
        return false;
      }
      const section = link.closest('[data-drawer-section]');
      if (section && section.hidden) {
        return false;
      }
      const closed = link.closest('details:not([open])');
      return !closed;
    });

  const applyFilter = () => {
    if (!filter) {
      return;
    }

    const tokens = normaliseQuery(filter.input.value);
    const sections = Array.from(panel.querySelectorAll('[data-drawer-section]'));

    if (tokens.length === 0) {
      sections.forEach((section) => {
        section.hidden = false;
        section.querySelectorAll('[data-drawer-item]').forEach((item) => {
          item.hidden = false;
        });
      });
      if (openBeforeFilter) {
        sectionGroups.forEach((group) => {
          group.open = openBeforeFilter.has(group);
        });
      }
      openBeforeFilter = null;
      delete drawer.dataset.drawerFiltering;
      filter.empty.hidden = true;
      filter.status.textContent = '';
      return;
    }

    if (!openBeforeFilter) {
      openBeforeFilter = new Set(sectionGroups.filter((group) => group.open));
    }
    drawer.dataset.drawerFiltering = 'true';

    let matches = 0;
    sections.forEach((section) => {
      const title = section.querySelector('.pm-drawer__section-title');
      const sectionText = title ? title.textContent : '';
      let sectionMatches = 0;

      section.querySelectorAll('[data-drawer-item]').forEach((item) => {
        const text = `${item.textContent} ${sectionText}`.toLocaleLowerCase();
        const isMatch = tokens.every((token) => text.includes(token));
        item.hidden = !isMatch;
        if (isMatch) {
          sectionMatches += 1;
        }
      });

      section.hidden = sectionMatches === 0;
      if (sectionMatches > 0 && section instanceof HTMLDetailsElement) {
        section.open = true;
      }
      matches += sectionMatches;
    });

    filter.empty.hidden = matches > 0;
    filter.empty.textContent = matches > 0 ? '' : `No pages match “${filter.input.value.trim()}”.`;
    filter.status.textContent = matches === 0
      ? 'No pages match.'
      : `${matches} ${matches === 1 ? 'page matches' : 'pages match'}.`;
  };

  const clearFilter = () => {
    if (filter && filter.input.value) {
      filter.input.value = '';
      applyFilter();
    }
  };

  if (filter) {
    filter.input.addEventListener('input', applyFilter);
    filter.input.addEventListener('keydown', (event) => {
      if (event.key === 'ArrowDown') {
        const [first] = visibleLinks();
        if (first) {
          event.preventDefault();
          first.focus();
        }
      } else if (event.key === 'Enter') {
        const [first] = visibleLinks();
        if (first && filter.input.value.trim()) {
          event.preventDefault();
          first.click();
        }
      }
    });
  }

  let restoreFocusTo = null;
  let isOpen = drawer.classList.contains('is-open');

  if (!drawer.hasAttribute('aria-hidden')) {
    drawer.setAttribute('aria-hidden', 'true');
  }

  if (!isOpen) {
    drawer.classList.remove('is-open');
  }

  const syncAriaHidden = () => {
    drawer.setAttribute('aria-hidden', isOpen ? 'false' : 'true');
  };

  const handleKeydown = (event) => {
    if (!isOpen) {
      return;
    }

    if (event.key === 'Escape' || event.key === 'Esc') {
      event.preventDefault();
      // First Escape clears the filter; the next one closes the drawer.
      if (filter && event.target === filter.input && filter.input.value) {
        clearFilter();
        return;
      }
      closeDrawer();
      return;
    }

    // Ctrl/Cmd+K is the global search: close the dialog so the search box it focuses
    // is not left behind the overlay.
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && String(event.key).toLowerCase() === 'k') {
      closeDrawer();
      return;
    }

    const active = document.activeElement;
    const typing = active instanceof HTMLElement
      && (active.matches('input, textarea, select') || active.isContentEditable);

    if (filter && event.key === '/' && !typing && !event.ctrlKey && !event.metaKey && !event.altKey) {
      event.preventDefault();
      filter.input.focus();
      return;
    }

    // Use the key's own target: the filter's ArrowDown has already moved focus to a link
    // by the time this document listener sees the same event.
    const keyTarget = event.target;
    if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && keyTarget instanceof HTMLElement && keyTarget.matches('a.pm-drawer__link')) {
      const links = visibleLinks();
      const index = links.indexOf(keyTarget);
      if (index >= 0) {
        event.preventDefault();
        if (event.key === 'ArrowDown' && index < links.length - 1) {
          links[index + 1].focus();
        } else if (event.key === 'ArrowUp' && index > 0) {
          links[index - 1].focus();
        } else if (event.key === 'ArrowUp' && filter) {
          filter.input.focus();
        }
      }
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

    // Start where the user is: the current page's link, when it is visible.
    const current = focusable.find((element) => element.getAttribute('aria-current') === 'page');
    if (current) {
      current.focus({ preventScroll: true });
      if (typeof current.scrollIntoView === 'function') {
        current.scrollIntoView({ block: 'nearest' });
      }
      return;
    }

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
    // Capture phase: while the dialog is open its keys ("/", Escape, Tab) come before
    // page-wide shortcuts such as the global search's "/" (search-combobox.js).
    document.addEventListener('keydown', handleKeydown, true);
    syncBodyScrollLock();
    window.requestAnimationFrame(focusFirstElement);
  };

  const closeDrawer = () => {
    if (!isOpen) {
      return;
    }

    drawer.classList.remove('is-open');
    toggles.forEach((toggle) => toggle.setAttribute('aria-expanded', 'false'));
    isOpen = false;
    clearFilter();
    syncAriaHidden();
    document.removeEventListener('keydown', handleKeydown, true);
    syncBodyScrollLock();

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
  syncBodyScrollLock();

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
