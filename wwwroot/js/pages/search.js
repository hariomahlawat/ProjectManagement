(() => {
  const suggestionEndpoint = '/Common/Search?handler=Suggestions';
  const resultPage = '/Common/Search';

  function makeSuggestionController(input) {
    const form = input.closest('form');
    const panel = form?.querySelector('[data-search-suggestions]');
    if (!(form instanceof HTMLFormElement) || !(panel instanceof HTMLElement)) return null;

    const clear = form.querySelector('[data-clear]');
    let timer = 0;
    let request = null;
    let activeIndex = -1;
    let items = [];

    const setClearState = () => {
      if (clear instanceof HTMLElement) {
        clear.classList.toggle('is-visible', input.value.length > 0);
      }
    };

    const close = () => {
      panel.hidden = true;
      panel.replaceChildren();
      items = [];
      activeIndex = -1;
      input.setAttribute('aria-expanded', 'false');
      input.removeAttribute('aria-activedescendant');
    };

    const setActive = (index) => {
      if (items.length === 0) return;
      activeIndex = Math.max(0, Math.min(index, items.length - 1));
      items.forEach((item, itemIndex) => item.classList.toggle('is-active', itemIndex === activeIndex));
      const active = items[activeIndex];
      if (active) {
        input.setAttribute('aria-activedescendant', active.id);
        active.scrollIntoView({ block: 'nearest' });
      }
    };

    const navigateToSuggestion = (item) => {
      const url = item?.dataset?.url;
      if (url) window.location.assign(url);
    };

    const render = (suggestions, query) => {
      panel.replaceChildren();
      items = [];
      activeIndex = -1;

      suggestions.forEach((suggestion, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.id = `${input.id || 'prism-search'}-suggestion-${index}`;
        button.className = 'pm-gs-suggestion';
        button.setAttribute('role', 'option');
        button.dataset.url = suggestion.url || '';

        const text = document.createElement('span');
        const title = document.createElement('span');
        title.className = 'pm-gs-suggestion__title';
        title.textContent = suggestion.title || 'Untitled result';
        text.append(title);

        if (suggestion.subtitle || suggestion.identifier) {
          const subtitle = document.createElement('span');
          subtitle.className = 'pm-gs-suggestion__subtitle';
          subtitle.textContent = suggestion.identifier || suggestion.subtitle;
          text.append(subtitle);
        }

        const source = document.createElement('span');
        source.className = 'pm-gs-suggestion__source';
        source.textContent = suggestion.sourceModule || suggestion.category || 'PRISM';
        button.append(text, source);
        button.addEventListener('mousedown', (event) => event.preventDefault());
        button.addEventListener('click', () => navigateToSuggestion(button));
        panel.append(button);
        items.push(button);
      });

      const all = document.createElement('button');
      all.type = 'button';
      all.className = 'pm-gs-suggestion-all';
      all.innerHTML = '<span></span><i class="bi bi-arrow-right" aria-hidden="true"></i>';
      all.firstElementChild.textContent = `See all results for “${query}”`;
      all.addEventListener('mousedown', (event) => event.preventDefault());
      all.addEventListener('click', () => {
        window.location.assign(`${resultPage}?q=${encodeURIComponent(query)}`);
      });
      panel.append(all);

      panel.hidden = suggestions.length === 0 && query.length < 2;
      input.setAttribute('aria-expanded', panel.hidden ? 'false' : 'true');
      panel.setAttribute('role', 'listbox');
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
        const response = await fetch(`${suggestionEndpoint}&q=${encodeURIComponent(query)}`, {
          method: 'GET',
          headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
          signal: request.signal,
          credentials: 'same-origin'
        });
        if (!response.ok) throw new Error(`Search suggestions failed with HTTP ${response.status}.`);
        const suggestions = await response.json();
        if (input.value.trim() !== query) return;
        render(Array.isArray(suggestions) ? suggestions : [], query);
      } catch (error) {
        if (error?.name !== 'AbortError') {
          console.warn('[PRISM] Search suggestions are temporarily unavailable.', error);
        }
        close();
      }
    };

    input.addEventListener('input', () => {
      setClearState();
      window.clearTimeout(timer);
      timer = window.setTimeout(load, 180);
    });

    input.addEventListener('keydown', (event) => {
      if (event.key === 'ArrowDown' && items.length > 0) {
        event.preventDefault();
        setActive(activeIndex < 0 ? 0 : activeIndex + 1);
      } else if (event.key === 'ArrowUp' && items.length > 0) {
        event.preventDefault();
        setActive(activeIndex <= 0 ? items.length - 1 : activeIndex - 1);
      } else if (event.key === 'Enter' && activeIndex >= 0) {
        event.preventDefault();
        navigateToSuggestion(items[activeIndex]);
      } else if (event.key === 'Escape') {
        close();
      }
    });

    input.addEventListener('focus', () => {
      if (input.value.trim().length >= 2 && panel.childElementCount > 0) {
        panel.hidden = false;
        input.setAttribute('aria-expanded', 'true');
      }
    });

    clear?.addEventListener('click', () => {
      input.value = '';
      setClearState();
      close();
      input.focus();
    });

    document.addEventListener('pointerdown', (event) => {
      if (!form.contains(event.target)) close();
    });

    setClearState();
    return { close };
  }

  function initSuggestions() {
    document.querySelectorAll('[data-search-suggest]').forEach((node) => {
      if (node instanceof HTMLInputElement) makeSuggestionController(node);
    });
  }

  function initClickTelemetry() {
    const tokenForm = document.querySelector('[data-search-click-token]');
    const token = tokenForm?.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const query = new URLSearchParams(window.location.search).get('q') || '';
    if (!token || !query) return;

    document.querySelectorAll('[data-search-result]').forEach((link) => {
      if (!(link instanceof HTMLAnchorElement)) return;
      link.addEventListener('click', () => {
        const form = new FormData();
        form.append('__RequestVerificationToken', token);
        form.append('query', query);
        form.append('entityType', link.dataset.searchEntityType || '');
        form.append('entityKey', link.dataset.searchEntityKey || '');
        form.append('rank', link.dataset.searchRank || '0');
        form.append('sourceModule', link.dataset.searchSource || '');

        const endpoint = `${window.location.pathname}?handler=Click`;
        if (navigator.sendBeacon) {
          navigator.sendBeacon(endpoint, form);
        } else {
          fetch(endpoint, { method: 'POST', body: form, credentials: 'same-origin', keepalive: true }).catch(() => {});
        }
      }, { passive: true });
    });
  }


  function initShortcut() {
    document.addEventListener('keydown', (event) => {
      if (!(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== 'k') return;
      const input = document.querySelector('[data-search-suggest]');
      if (!(input instanceof HTMLInputElement)) return;
      event.preventDefault();
      input.focus({ preventScroll: true });
      input.select();
    });
  }

  function initFilters() {
    document.querySelectorAll('.pm-gs-filter').forEach((details) => {
      if (!(details instanceof HTMLDetailsElement)) return;
      document.addEventListener('pointerdown', (event) => {
        if (details.open && !details.contains(event.target)) details.open = false;
      });
    });
  }

  function boot() {
    initSuggestions();
    initShortcut();
    initClickTelemetry();
    initFilters();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
})();
