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

  function initProjectFacets(root = document) {
    root.querySelectorAll('[data-project-facet-list]').forEach((container) => {
      if (!(container instanceof HTMLElement) || container.dataset.projectFacetInit === 'true') return;
      container.dataset.projectFacetInit = 'true';

      const search = container.querySelector('[data-project-facet-search]');
      const more = container.querySelector('[data-project-facet-more]');
      const facets = Array.from(container.querySelectorAll('[data-project-facet]'))
        .filter((node) => node instanceof HTMLElement);
      const initialLimit = 8;
      let expanded = false;

      const isSelected = (facet) => {
        const checkbox = facet.querySelector('input[type="checkbox"]');
        return checkbox instanceof HTMLInputElement && checkbox.checked;
      };

      const applyVisibility = () => {
        const term = search instanceof HTMLInputElement ? search.value.trim().toLocaleLowerCase() : '';
        let matchingCount = 0;

        facets.forEach((facet, index) => {
          const label = (facet.dataset.projectLabel || facet.textContent || '').toLocaleLowerCase();
          const matches = term.length === 0 || label.includes(term);
          if (matches) matchingCount += 1;

          const visible = term.length > 0
            ? matches
            : (expanded || index < initialLimit || isSelected(facet));
          facet.classList.toggle('is-collapsed', !visible);
        });

        if (more instanceof HTMLButtonElement) {
          const canExpand = term.length === 0 && facets.length > initialLimit;
          more.hidden = !canExpand;
          more.textContent = expanded ? 'Show less' : `Show all (${facets.length})`;
          more.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        }

        if (search instanceof HTMLInputElement) {
          search.setAttribute('aria-label', `Filter ${facets.length} project facets`);
          search.dataset.matchCount = String(matchingCount);
        }
      };

      if (search instanceof HTMLInputElement) {
        search.addEventListener('input', applyVisibility);
        search.addEventListener('keydown', (event) => {
          if (event.key === 'Escape' && search.value) {
            search.value = '';
            applyVisibility();
            event.stopPropagation();
          }
        });
      }

      if (more instanceof HTMLButtonElement) {
        more.addEventListener('click', () => {
          expanded = !expanded;
          applyVisibility();
        });
      }

      facets.forEach((facet) => {
        const checkbox = facet.querySelector('input[type="checkbox"]');
        checkbox?.addEventListener('change', applyVisibility);
      });

      applyVisibility();
    });
  }

  function humanizeToken(value) {
    return String(value || '').replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  }

  function selectedValues(name) {
    return new Set(new URLSearchParams(window.location.search).getAll(name).map((value) => value.toLocaleLowerCase()));
  }

  function createFacetSection(title, inputName, facets, { project = false, humanize = false, open = false } = {}) {
    if (!Array.isArray(facets) || facets.length === 0) return null;

    const details = document.createElement('details');
    details.className = 'pm-gs-filter__section';
    details.dataset.filterSection = '';
    const selected = selectedValues(inputName);
    details.open = open || selected.size > 0;

    const summary = document.createElement('summary');
    summary.append(document.createTextNode(title));
    const selectedCount = document.createElement('span');
    selectedCount.className = 'pm-gs-filter__selection-count';
    selectedCount.dataset.filterSelectedCount = '';
    selectedCount.textContent = String(selected.size);
    selectedCount.hidden = selected.size === 0;
    summary.append(selectedCount);
    details.append(summary);

    const body = document.createElement('div');
    body.className = 'pm-gs-filter__section-body';
    if (project) body.dataset.projectFacetList = '';

    if (project && facets.length > 8) {
      const search = document.createElement('input');
      search.className = 'pm-gs-filter__facet-search';
      search.type = 'search';
      search.placeholder = 'Find project…';
      search.autocomplete = 'off';
      search.dataset.projectFacetSearch = '';
      body.append(search);
    }

    facets.forEach((facet, index) => {
      const value = String(facet?.value ?? '');
      if (!value) return;
      const labelText = String(facet?.label || value);
      const id = `lazy-${inputName.toLocaleLowerCase()}-${index}`;

      const label = document.createElement('label');
      label.className = 'pm-gs-filter__option';
      label.htmlFor = id;
      if (project) {
        label.dataset.projectFacet = '';
        label.dataset.projectLabel = labelText.toLocaleLowerCase();
      }

      const input = document.createElement('input');
      input.id = id;
      input.type = 'checkbox';
      input.name = inputName;
      input.value = value;
      input.checked = selected.has(value.toLocaleLowerCase());

      const text = document.createElement('span');
      text.textContent = humanize ? humanizeToken(labelText) : labelText;
      const count = document.createElement('small');
      count.textContent = String(facet?.count ?? 0);
      label.append(input, text, count);
      body.append(label);
    });

    if (project && facets.length > 8) {
      const more = document.createElement('button');
      more.type = 'button';
      more.className = 'pm-gs-filter__show-more';
      more.dataset.projectFacetMore = '';
      more.textContent = 'Show more';
      body.append(more);
    }

    details.append(body);
    return details;
  }

  function updateFilterSelectionCounts(form) {
    if (!(form instanceof HTMLFormElement)) return;

    form.querySelectorAll('[data-filter-section]').forEach((section) => {
      if (!(section instanceof HTMLDetailsElement)) return;
      const count = section.querySelectorAll('input[type="checkbox"]:checked').length
        + Array.from(section.querySelectorAll('input[type="date"]'))
          .filter((input) => input instanceof HTMLInputElement && input.value).length;
      const badge = section.querySelector('[data-filter-selected-count]');
      if (badge instanceof HTMLElement) {
        badge.textContent = String(count);
        badge.hidden = count === 0;
      }
    });

    const count = form.querySelectorAll('input[type="checkbox"]:checked').length
      + Array.from(form.querySelectorAll('input[type="date"]'))
        .filter((input) => input instanceof HTMLInputElement && input.value).length;
    const owner = form.closest('[data-search-filter]');
    const badge = owner?.querySelector('[data-filter-active-count]');
    if (badge instanceof HTMLElement) {
      badge.textContent = String(count);
      badge.hidden = count === 0;
    }
  }

  function initFilterSelectionCounts(root = document) {
    root.querySelectorAll('[data-search-filter-form]').forEach((form) => {
      if (!(form instanceof HTMLFormElement) || form.dataset.selectionCountInit === 'true') return;
      form.dataset.selectionCountInit = 'true';
      form.addEventListener('change', () => updateFilterSelectionCounts(form));
      form.addEventListener('input', (event) => {
        if (event.target instanceof HTMLInputElement && event.target.type === 'date') updateFilterSelectionCounts(form);
      });
      updateFilterSelectionCounts(form);
    });
  }

  function renderDetailedFacets(container, payload) {
    if (!(container instanceof HTMLElement)) return;
    container.replaceChildren();
    const sections = [
      createFacetSection('Source', 'Source', payload.sources, { open: true }),
      createFacetSection('Project', 'Project', payload.projects, { project: true }),
      createFacetSection('Status', 'Status', payload.statuses, { humanize: true }),
      createFacetSection('File type', 'FileType', payload.fileTypes),
      createFacetSection('Stage', 'Stage', payload.stages, { humanize: true })
    ].filter(Boolean);
    sections.forEach((section) => container.append(section));
  }

  function initLazyFacets() {
    document.querySelectorAll('[data-search-filter]').forEach((details) => {
      if (!(details instanceof HTMLDetailsElement)) return;
      const dynamic = details.querySelector('[data-search-dynamic-facets]');
      const form = details.querySelector('[data-search-filter-form]');
      if (!(dynamic instanceof HTMLElement) || !(form instanceof HTMLFormElement)) return;

      const load = async () => {
        if (details.dataset.facetsLoaded === 'true' || details.dataset.facetsLoading === 'true') return;
        details.dataset.facetsLoading = 'true';
        dynamic.classList.add('is-loading');
        dynamic.setAttribute('aria-busy', 'true');

        const loading = document.createElement('div');
        loading.className = 'pm-gs-filter__loading';
        loading.textContent = 'Loading filters…';
        dynamic.replaceChildren(loading);

        try {
          // Razor Pages facet handler: handler=Facets. Preserve the authorised query/filter state.
          const url = new URL(window.location.href);
          url.searchParams.set('handler', 'Facets');
          url.searchParams.delete('Cursor');
          const response = await fetch(url, {
            method: 'GET',
            headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
          });
          if (!response.ok) throw new Error(`Search facets failed with HTTP ${response.status}.`);
          const payload = await response.json();
          if (!payload?.ok || !payload?.detailedLoaded) throw new Error('Search facets are unavailable.');

          renderDetailedFacets(dynamic, payload);
          details.dataset.facetsLoaded = 'true';
          initProjectFacets(dynamic);
          updateFilterSelectionCounts(form);
        } catch (error) {
          const message = document.createElement('div');
          message.className = 'pm-gs-filter__loading pm-gs-filter__loading--error';
          message.textContent = 'Filters are temporarily unavailable.';
          dynamic.replaceChildren(message);
          console.warn('[PRISM] Search facets are temporarily unavailable.', error);
        } finally {
          details.dataset.facetsLoading = 'false';
          dynamic.classList.remove('is-loading');
          dynamic.removeAttribute('aria-busy');
        }
      };

      details.addEventListener('toggle', () => {
        if (details.open) void load();
      });
      if (details.open) void load();
    });
  }

  function initFilters() {
    document.querySelectorAll('[data-search-filter]').forEach((details) => {
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
    initProjectFacets();
    initFilterSelectionCounts();
    initLazyFacets();
    initFilters();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
})();
