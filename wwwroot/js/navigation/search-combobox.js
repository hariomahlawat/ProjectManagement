/**
 * PRISM search box: one accessible combobox shared by the top-bar search and the
 * Search page (WAI-ARIA combobox with a listbox popup and aria-activedescendant).
 *
 * - Suggestions are fetched after two characters; full ranking stays on the results page.
 * - With an empty box it offers the user's recent searches (kept in this browser only).
 * - Escape closes the popup and never clears what was typed.
 * - Every row is built with textContent; nothing from the server is parsed as HTML.
 */

export const SUGGESTION_ENDPOINT = '/Common/Search?handler=Suggestions';
export const RESULTS_PAGE = '/Common/Search';
export const RECENT_KEY = 'prism.search.recent';
export const RECENT_LIMIT = 6;
const MIN_QUERY = 2;

let comboboxCount = 0;

const CATEGORY_ICONS = {
  projects: 'bi-kanban',
  documents: 'bi-file-earmark-text',
  trackers: 'bi-clipboard-data',
  records: 'bi-clipboard-data',
  organisation: 'bi-building',
  'my work': 'bi-person-check',
  media: 'bi-images'
};

// -----------------------------------------------------------------------------
// Recent searches (per browser; storage can be blocked, so every access is guarded)
// -----------------------------------------------------------------------------

function storageOf(win) {
  try {
    return win?.localStorage ?? null;
  } catch {
    return null;
  }
}

export function readRecentSearches(win = globalThis.window) {
  try {
    const raw = storageOf(win)?.getItem(RECENT_KEY);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed)
      ? parsed.filter((value) => typeof value === 'string' && value.trim()).slice(0, RECENT_LIMIT)
      : [];
  } catch {
    return [];
  }
}

export function rememberSearch(query, win = globalThis.window) {
  const value = String(query ?? '').trim().slice(0, 200);
  if (!value) return;
  try {
    const key = value.toLowerCase();
    const next = [value, ...readRecentSearches(win).filter((item) => item.toLowerCase() !== key)]
      .slice(0, RECENT_LIMIT);
    storageOf(win)?.setItem(RECENT_KEY, JSON.stringify(next));
  } catch {
    // Storage full or blocked: recent searches are a convenience only.
  }
}

export function clearRecentSearches(win = globalThis.window) {
  try {
    storageOf(win)?.removeItem(RECENT_KEY);
  } catch {
    // Ignore blocked storage.
  }
}

// -----------------------------------------------------------------------------
// Small helpers
// -----------------------------------------------------------------------------

/** Returns the URL when it stays on this origin, otherwise null. */
export function sameOriginUrl(url, location = globalThis.window?.location) {
  if (!url || !location) return null;
  try {
    const target = new URL(url, location.href);
    return target.origin === location.origin ? target.href : null;
  } catch {
    return null;
  }
}

export function resultsUrl(query) {
  return `${RESULTS_PAGE}?q=${encodeURIComponent(query)}`;
}

export function iconFor(suggestion) {
  const key = String(suggestion?.category || '').trim().toLowerCase();
  return CATEGORY_ICONS[key] || 'bi-search';
}

export function isApplePlatform(nav = globalThis.navigator) {
  const platform = nav?.userAgentData?.platform || nav?.platform || '';
  return /mac|iphone|ipad|ipod/i.test(platform);
}

export function shortcutLabel(nav = globalThis.navigator) {
  return isApplePlatform(nav) ? '⌘K' : 'Ctrl K';
}

/**
 * Appends text to the node with the first occurrence of the query wrapped in <mark>,
 * preferring a match at the start of a word.
 */
export function appendHighlighted(node, text, query) {
  const doc = node.ownerDocument;
  const value = String(text ?? '');
  const needle = String(query ?? '').trim();
  const lower = value.toLowerCase();
  const lowerNeedle = needle.toLowerCase();
  let index = -1;
  if (needle && lower.length === value.length && lowerNeedle.length === needle.length) {
    const wordStart = new RegExp(`(^|[^\\p{L}\\p{N}])${lowerNeedle.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`, 'u').exec(lower);
    index = wordStart ? wordStart.index + wordStart[1].length : lower.indexOf(lowerNeedle);
  }

  if (index < 0) {
    node.append(doc.createTextNode(value));
    return node;
  }

  const mark = doc.createElement('mark');
  mark.textContent = value.slice(index, index + needle.length);
  node.append(doc.createTextNode(value.slice(0, index)), mark, doc.createTextNode(value.slice(index + needle.length)));
  return node;
}

function isVisible(element) {
  return typeof element?.getClientRects === 'function' && element.getClientRects().length > 0;
}

function isEditable(target) {
  if (typeof target?.closest !== 'function') return false;
  return target.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"]') !== null;
}

// -----------------------------------------------------------------------------
// Combobox
// -----------------------------------------------------------------------------

/**
 * Turns a search input inside a form into a combobox.
 *
 * @param {HTMLInputElement} input
 * @param {{ fetchImpl?: typeof fetch, debounceMs?: number, blurOnEscape?: boolean, navigate?: (url: string) => void }} [options]
 */
export function createSearchCombobox(input, options = {}) {
  const form = input?.form;
  if (!form || input.dataset.comboboxReady === 'true') return null;
  input.dataset.comboboxReady = 'true';

  const doc = input.ownerDocument;
  const win = doc.defaultView;
  const fetchImpl = options.fetchImpl || win.fetch.bind(win);
  const debounceMs = options.debounceMs ?? 180;
  const navigate = options.navigate || ((url) => win.location.assign(url));
  const baseId = input.id || `pm-search-${++comboboxCount}`;
  const clear = form.querySelector('[data-clear]');

  const popup = doc.createElement('div');
  popup.className = 'pm-search-popup';
  popup.hidden = true;

  const status = doc.createElement('div');
  status.className = 'pm-search-popup__status';
  status.hidden = true;
  status.setAttribute('aria-hidden', 'true');

  const listbox = doc.createElement('div');
  listbox.id = `${baseId}-listbox`;
  listbox.className = 'pm-search-popup__list';
  listbox.setAttribute('role', 'listbox');
  listbox.setAttribute('aria-label', 'Search suggestions');

  const live = doc.createElement('div');
  live.className = 'visually-hidden';
  live.setAttribute('role', 'status');
  live.setAttribute('aria-live', 'polite');
  live.setAttribute('aria-atomic', 'true');

  popup.append(status, listbox);
  form.append(popup, live);

  input.setAttribute('role', 'combobox');
  input.setAttribute('aria-autocomplete', 'list');
  input.setAttribute('aria-expanded', 'false');
  input.setAttribute('aria-controls', listbox.id);
  input.setAttribute('aria-haspopup', 'listbox');

  let optionNodes = [];
  let activeIndex = -1;
  let timer = 0;
  let request = null;
  let lastQuery = null;
  let announceTimer = 0;
  const cache = new Map();

  const announce = (message) => {
    win.clearTimeout(announceTimer);
    // A short delay lets screen readers finish echoing the typed character first.
    announceTimer = win.setTimeout(() => { live.textContent = message; }, 400);
  };

  const setClearState = () => {
    if (clear) clear.hidden = input.value.length === 0;
  };

  const setActive = (index) => {
    if (optionNodes.length === 0) {
      activeIndex = -1;
      input.removeAttribute('aria-activedescendant');
      return;
    }
    activeIndex = (index + optionNodes.length) % optionNodes.length;
    optionNodes.forEach((option, optionIndex) => {
      const active = optionIndex === activeIndex;
      option.classList.toggle('is-active', active);
      option.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    const active = optionNodes[activeIndex];
    input.setAttribute('aria-activedescendant', active.id);
    active.scrollIntoView?.({ block: 'nearest' });
  };

  const open = () => {
    popup.hidden = false;
    input.setAttribute('aria-expanded', 'true');
  };

  const close = () => {
    win.clearTimeout(timer);
    request?.abort();
    request = null;
    popup.hidden = true;
    input.setAttribute('aria-expanded', 'false');
    input.removeAttribute('aria-activedescendant');
    activeIndex = -1;
    optionNodes.forEach((option) => {
      option.classList.remove('is-active');
      option.setAttribute('aria-selected', 'false');
    });
  };

  const setStatus = (kind, text) => {
    status.hidden = !text;
    status.className = `pm-search-popup__status${kind ? ` pm-search-popup__status--${kind}` : ''}`;
    status.replaceChildren();
    if (!text) return;
    if (kind === 'loading') {
      const spinner = doc.createElement('span');
      spinner.className = 'spinner-border spinner-border-sm';
      spinner.setAttribute('aria-hidden', 'true');
      status.append(spinner);
    } else {
      const icon = doc.createElement('i');
      icon.className = `bi ${kind === 'error' ? 'bi-exclamation-circle' : 'bi-info-circle'}`;
      icon.setAttribute('aria-hidden', 'true');
      status.append(icon);
    }
    const label = doc.createElement('span');
    label.textContent = text;
    status.append(label);
  };

  const makeOption = ({ icon, title, meta, query, url, action, className }) => {
    const option = doc.createElement('div');
    option.id = `${baseId}-option-${optionNodes.length}`;
    option.className = `pm-search-option${className ? ` ${className}` : ''}`;
    option.setAttribute('role', 'option');
    option.setAttribute('aria-selected', 'false');
    option.tabIndex = -1;
    if (url) option.dataset.url = url;
    if (query !== undefined) option.dataset.query = query;
    if (action) option.dataset.action = action;

    const iconWrap = doc.createElement('span');
    iconWrap.className = 'pm-search-option__icon';
    iconWrap.setAttribute('aria-hidden', 'true');
    const glyph = doc.createElement('i');
    glyph.className = `bi ${icon}`;
    iconWrap.append(glyph);

    const text = doc.createElement('span');
    text.className = 'pm-search-option__text';
    text.append(title);
    if (meta) {
      const metaNode = doc.createElement('span');
      metaNode.className = 'pm-search-option__meta';
      metaNode.textContent = meta;
      text.append(metaNode);
    }

    option.append(iconWrap, text);
    option.addEventListener('pointerdown', (event) => event.preventDefault());
    option.addEventListener('click', () => activate(option));
    optionNodes.push(option);
    return option;
  };

  const titleNode = (text, query) => {
    const node = doc.createElement('span');
    node.className = 'pm-search-option__title';
    return appendHighlighted(node, text, query);
  };

  const plainTitle = (text) => {
    const node = doc.createElement('span');
    node.className = 'pm-search-option__title';
    node.textContent = text;
    return node;
  };

  const seeAllOption = (query) => makeOption({
    icon: 'bi-arrow-return-left',
    title: plainTitle(`See all results for “${query}”`),
    query,
    className: 'pm-search-option--all'
  });

  const reset = () => {
    optionNodes = [];
    activeIndex = -1;
    listbox.replaceChildren();
    input.removeAttribute('aria-activedescendant');
  };

  const renderRecent = () => {
    reset();
    setStatus(null, '');
    const recent = readRecentSearches(win);
    if (recent.length === 0) {
      close();
      return false;
    }

    const group = doc.createElement('div');
    group.setAttribute('role', 'group');
    group.setAttribute('aria-labelledby', `${baseId}-recent-label`);
    const heading = doc.createElement('div');
    heading.id = `${baseId}-recent-label`;
    heading.className = 'pm-search-popup__heading';
    heading.setAttribute('role', 'presentation');
    heading.textContent = 'Recent searches';
    group.append(heading);
    recent.forEach((query) => group.append(makeOption({ icon: 'bi-clock-history', title: plainTitle(query), query })));
    group.append(makeOption({
      icon: 'bi-x-circle',
      title: plainTitle('Clear recent searches'),
      action: 'clear-recent',
      className: 'pm-search-option--quiet'
    }));
    listbox.append(group);
    lastQuery = '';
    open();
    announce(`${recent.length} recent search${recent.length === 1 ? '' : 'es'}. Use the up and down arrows to choose one.`);
    return true;
  };

  const renderLoading = (query) => {
    setStatus('loading', 'Looking for matches…');
    // Keep the previous matches on screen while typing; only the "See all" row follows the text.
    const seeAll = optionNodes.find((option) => option.classList.contains('pm-search-option--all'));
    if (seeAll && !popup.hidden) {
      seeAll.dataset.query = query;
      seeAll.querySelector('.pm-search-option__title').textContent = `See all results for “${query}”`;
      return;
    }
    reset();
    listbox.append(seeAllOption(query));
    open();
  };

  const renderSuggestions = (suggestions, query) => {
    reset();
    suggestions.forEach((suggestion) => {
      const url = sameOriginUrl(suggestion?.url, win.location);
      if (!url) return;
      const meta = [suggestion.identifier || suggestion.subtitle, suggestion.sourceModule || suggestion.category]
        .filter(Boolean)
        .join(' · ');
      listbox.append(makeOption({
        icon: iconFor(suggestion),
        title: titleNode(suggestion.title || 'Untitled result', query),
        meta,
        url
      }));
    });
    const count = optionNodes.length;
    setStatus(count === 0 ? 'empty' : null, count === 0 ? 'No quick matches. Press Enter to search everything.' : '');
    listbox.append(seeAllOption(query));
    lastQuery = query;
    open();
    announce(count === 0
      ? 'No quick matches. Press Enter to search everything.'
      : `${count} suggestion${count === 1 ? '' : 's'}. Use the up and down arrows to review.`);
  };

  const renderError = (query) => {
    reset();
    setStatus('error', 'Suggestions are unavailable. Press Enter to search.');
    listbox.append(seeAllOption(query));
    lastQuery = query;
    open();
    announce('Suggestions are unavailable. Press Enter to search.');
  };

  const load = async (query) => {
    if (cache.has(query)) {
      renderSuggestions(cache.get(query), query);
      return;
    }

    request?.abort();
    const controller = new win.AbortController();
    request = controller;
    try {
      const response = await fetchImpl(`${SUGGESTION_ENDPOINT}&q=${encodeURIComponent(query)}`, {
        method: 'GET',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: controller.signal,
        credentials: 'same-origin'
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const payload = await response.json();
      if (request !== controller || input.value.trim() !== query) return;
      const suggestions = Array.isArray(payload) ? payload : [];
      cache.set(query, suggestions);
      if (cache.size > 20) cache.delete(cache.keys().next().value);
      renderSuggestions(suggestions, query);
    } catch (error) {
      if (error?.name === 'AbortError' || request !== controller) return;
      console.warn('[PRISM] Search suggestions are temporarily unavailable.', error);
      if (input.value.trim() === query && doc.activeElement === input) renderError(query);
    } finally {
      if (request === controller) request = null;
    }
  };

  const refresh = () => {
    const query = input.value.trim();
    win.clearTimeout(timer);
    if (query.length === 0) {
      if (doc.activeElement === input) renderRecent(); else close();
      return;
    }
    if (query.length < MIN_QUERY) {
      close();
      return;
    }
    if (!cache.has(query)) renderLoading(query);
    timer = win.setTimeout(() => { void load(query); }, cache.has(query) ? 0 : debounceMs);
  };

  const search = (query) => {
    const value = String(query ?? '').trim();
    if (!value) return;
    rememberSearch(value, win);
    close();
    navigate(resultsUrl(value));
  };

  function activate(option) {
    if (!option) return;
    if (option.dataset.action === 'clear-recent') {
      clearRecentSearches(win);
      close();
      announce('Recent searches cleared.');
      input.focus();
      return;
    }
    if (option.dataset.url) {
      const url = sameOriginUrl(option.dataset.url, win.location);
      if (!url) return;
      rememberSearch(input.value, win);
      close();
      navigate(url);
      return;
    }
    if (option.dataset.query !== undefined) {
      input.value = option.dataset.query;
      setClearState();
      search(option.dataset.query);
    }
  }

  input.addEventListener('input', () => {
    setClearState();
    refresh();
  });

  input.addEventListener('keydown', (event) => {
    if (event.isComposing) return;
    const isOpen = !popup.hidden;
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp': {
        event.preventDefault();
        if (!isOpen) {
          refresh();
          return;
        }
        if (optionNodes.length === 0) return;
        const down = event.key === 'ArrowDown';
        setActive(activeIndex < 0 ? (down ? 0 : optionNodes.length - 1) : activeIndex + (down ? 1 : -1));
        break;
      }
      case 'Enter':
        if (isOpen && activeIndex >= 0) {
          event.preventDefault();
          activate(optionNodes[activeIndex]);
        }
        break;
      case 'Escape':
        // type="search" clears its value on Escape by default; never lose what was typed.
        event.preventDefault();
        if (isOpen) {
          close();
        } else if (options.blurOnEscape) {
          input.blur();
        }
        break;
      case 'Tab':
        close();
        break;
      default:
        break;
    }
  });

  input.addEventListener('focus', () => {
    const query = input.value.trim();
    if (query.length === 0) {
      renderRecent();
    } else if (query.length >= MIN_QUERY && query === lastQuery && optionNodes.length > 0) {
      open();
    }
  });

  form.addEventListener('submit', (event) => {
    const query = input.value.trim();
    if (!query) {
      // An empty search only reloads the landing page; keep the user where they are.
      event.preventDefault();
      input.focus();
      return;
    }
    rememberSearch(query, win);
    close();
  });

  clear?.addEventListener('click', () => {
    input.value = '';
    setClearState();
    input.focus();
    renderRecent();
  });

  doc.addEventListener('pointerdown', (event) => {
    if (!form.contains(event.target)) close();
  });

  setClearState();
  return { input, listbox, popup, close, refresh };
}

// -----------------------------------------------------------------------------
// Keyboard shortcuts: Ctrl/⌘+K anywhere, "/" outside text fields
// -----------------------------------------------------------------------------

export function initSearchShortcuts(doc = globalThis.document, { navigate } = {}) {
  const win = doc.defaultView;
  if (doc.documentElement.dataset.searchShortcuts === 'true') return;
  doc.documentElement.dataset.searchShortcuts = 'true';
  const go = navigate || ((url) => win.location.assign(url));
  const label = shortcutLabel(win.navigator);

  doc.querySelectorAll('[data-search-shortcut-hint]').forEach((hint) => { hint.textContent = label; });
  doc.querySelectorAll('input[data-search-combobox]').forEach((input) => {
    input.setAttribute('aria-keyshortcuts', isApplePlatform(win.navigator) ? 'Meta+K /' : 'Control+K /');
  });

  const visibleInput = () => Array.from(doc.querySelectorAll('input[data-search-combobox]')).find(isVisible) || null;

  doc.addEventListener('keydown', (event) => {
    if (event.defaultPrevented || event.isComposing) return;
    const key = String(event.key || '').toLowerCase();
    const isPalette = (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && key === 'k';
    const isSlash = key === '/' && !event.ctrlKey && !event.metaKey && !event.altKey && !isEditable(event.target);
    if (!isPalette && !isSlash) return;

    const input = visibleInput();
    if (input) {
      event.preventDefault();
      input.focus({ preventScroll: false });
      input.select();
    } else if (isPalette) {
      // The top-bar box is hidden on narrow screens; the Search page has its own box.
      event.preventDefault();
      go(RESULTS_PAGE);
    }
  });
}
