/**
 * Application-wide PRISM search palette.
 * Ctrl/Cmd+K focuses the shared shell search and lightweight suggestions are
 * fetched only after two characters. Full document/OCR ranking stays on the
 * Search results page and is never executed on every keystroke.
 */
export function initGlobalSearchShortcut() {
  const form = document.querySelector('[data-global-search]');
  const input = form?.querySelector('input[name="q"]');

  if (!(form instanceof HTMLFormElement) || !(input instanceof HTMLInputElement)) {
    return;
  }

  const endpoint = '/Common/Search?handler=Suggestions';
  const searchPage = '/Common/Search';
  const shortcut = form.querySelector('.pm-top-search__shortcut');
  const panel = document.createElement('div');
  panel.className = 'pm-global-search-panel';
  panel.hidden = true;
  panel.setAttribute('role', 'listbox');
  panel.setAttribute('aria-label', 'PRISM search suggestions');
  form.append(panel);

  input.setAttribute('aria-autocomplete', 'list');
  input.setAttribute('aria-expanded', 'false');

  let timer = 0;
  let request = null;
  let activeIndex = -1;
  let options = [];

  const close = () => {
    panel.hidden = true;
    panel.replaceChildren();
    options = [];
    activeIndex = -1;
    input.setAttribute('aria-expanded', 'false');
    input.removeAttribute('aria-activedescendant');
  };

  const navigate = (option) => {
    const url = option?.dataset?.url;
    if (url) window.location.assign(url);
  };

  const setActive = (index) => {
    if (options.length === 0) return;
    activeIndex = Math.max(0, Math.min(index, options.length - 1));
    options.forEach((option, optionIndex) => option.classList.toggle('is-active', optionIndex === activeIndex));
    const active = options[activeIndex];
    input.setAttribute('aria-activedescendant', active.id);
    active.scrollIntoView({ block: 'nearest' });
  };

  const makeOption = (suggestion, index) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.id = `pm-global-search-option-${index}`;
    button.className = 'pm-global-search-option';
    button.dataset.url = suggestion.url || '';
    button.setAttribute('role', 'option');

    const icon = document.createElement('span');
    icon.className = 'pm-global-search-option__icon';
    icon.innerHTML = '<i class="bi bi-search" aria-hidden="true"></i>';

    const text = document.createElement('span');
    text.className = 'pm-global-search-option__text';
    const title = document.createElement('strong');
    title.textContent = suggestion.title || 'Untitled result';
    const meta = document.createElement('small');
    meta.textContent = [suggestion.identifier || suggestion.subtitle, suggestion.sourceModule]
      .filter(Boolean)
      .join(' · ');
    text.append(title, meta);

    button.append(icon, text);
    button.addEventListener('mousedown', (event) => event.preventDefault());
    button.addEventListener('click', () => navigate(button));
    return button;
  };

  const render = (suggestions, query) => {
    panel.replaceChildren();
    options = suggestions.map(makeOption);
    options.forEach((option) => panel.append(option));

    const all = document.createElement('button');
    all.type = 'button';
    all.className = 'pm-global-search-all';
    const label = document.createElement('span');
    label.textContent = `See all results for “${query}”`;
    const arrow = document.createElement('i');
    arrow.className = 'bi bi-arrow-right';
    arrow.setAttribute('aria-hidden', 'true');
    all.append(label, arrow);
    all.addEventListener('mousedown', (event) => event.preventDefault());
    all.addEventListener('click', () => window.location.assign(`${searchPage}?q=${encodeURIComponent(query)}`));
    panel.append(all);

    panel.hidden = false;
    input.setAttribute('aria-expanded', 'true');
  };

  const load = async () => {
    const query = input.value.trim();
    if (query.length < 2) {
      close();
      return;
    }

    request?.abort();
    request = new AbortController();
    try {
      const response = await fetch(`${endpoint}&q=${encodeURIComponent(query)}`, {
        method: 'GET',
        headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: request.signal,
        credentials: 'same-origin'
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const payload = await response.json();
      if (query !== input.value.trim()) return;
      render(Array.isArray(payload) ? payload : [], query);
    } catch (error) {
      if (error?.name !== 'AbortError') {
        console.warn('[PRISM] Global search suggestions are temporarily unavailable.', error);
      }
      close();
    }
  };

  input.addEventListener('input', () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(load, 180);
  });

  input.addEventListener('keydown', (event) => {
    if (event.key === 'ArrowDown' && options.length > 0) {
      event.preventDefault();
      setActive(activeIndex < 0 ? 0 : activeIndex + 1);
    } else if (event.key === 'ArrowUp' && options.length > 0) {
      event.preventDefault();
      setActive(activeIndex <= 0 ? options.length - 1 : activeIndex - 1);
    } else if (event.key === 'Enter' && activeIndex >= 0) {
      event.preventDefault();
      navigate(options[activeIndex]);
    } else if (event.key === 'Escape') {
      close();
      input.blur();
    }
  });

  input.addEventListener('focus', () => {
    shortcut?.setAttribute('aria-hidden', 'true');
    if (input.value.trim().length >= 2 && panel.childElementCount > 0) {
      panel.hidden = false;
      input.setAttribute('aria-expanded', 'true');
    }
  });

  input.addEventListener('blur', () => shortcut?.setAttribute('aria-hidden', 'false'));

  document.addEventListener('pointerdown', (event) => {
    if (!form.contains(event.target)) close();
  });

  document.addEventListener('keydown', (event) => {
    const key = event.key.toLowerCase();
    const isSearchShortcut = (event.ctrlKey || event.metaKey) && key === 'k';
    if (!isSearchShortcut) return;

    event.preventDefault();
    input.focus({ preventScroll: true });
    input.select();
  });
}
