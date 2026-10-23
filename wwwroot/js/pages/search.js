import {
  createSearchCombobox,
  readRecentSearches,
  rememberSearch,
  resultsUrl,
  sameOriginUrl
} from '../navigation/search-combobox.js';

/**
 * PRISM Search page: search boxes, recent searches, result click telemetry, "Show more
 * results", and the Filters panel (lazy detailed facets, project finder, date presets).
 * The shared combobox and the Ctrl/⌘+K and "/" shortcuts live in navigation/search-combobox.js.
 */

const FILTER_NAMES = ['Source', 'Project', 'Status', 'FileType', 'Stage', 'DateFrom', 'DateTo'];
const numberFormat = new Intl.NumberFormat('en-US');

// -----------------------------------------------------------------------------
// Search boxes and recent searches
// -----------------------------------------------------------------------------

function initSearchBoxes(root) {
  root.querySelectorAll('input[data-search-combobox]').forEach((input) => {
    if (input instanceof HTMLInputElement) createSearchCombobox(input);
  });

  // Searches that arrive by link (hints, did-you-mean, related chips) count as recent too.
  const recordInput = root.querySelector('input[data-search-record-recent]');
  if (recordInput instanceof HTMLInputElement && recordInput.value.trim()) {
    rememberSearch(recordInput.value);
  }
}

function initRecentSearches(root) {
  root.querySelectorAll('[data-search-recent]').forEach((container) => {
    const list = container.querySelector('[data-search-recent-list]');
    if (!(list instanceof HTMLElement)) return;
    const current = new URLSearchParams(window.location.search).get('q')?.trim().toLowerCase() || '';
    const recent = readRecentSearches().filter((query) => query.toLowerCase() !== current);
    list.replaceChildren(...recent.map((query) => {
      const item = document.createElement('li');
      const link = document.createElement('a');
      link.href = resultsUrl(query);
      link.className = 'pm-gs-recent__link';
      const icon = document.createElement('i');
      icon.className = 'bi bi-clock-history';
      icon.setAttribute('aria-hidden', 'true');
      const label = document.createElement('span');
      label.textContent = query;
      link.append(icon, label);
      item.append(link);
      return item;
    }));
    container.hidden = recent.length === 0;
  });
}

// -----------------------------------------------------------------------------
// Click telemetry (left and middle click, including results added by "Show more")
// -----------------------------------------------------------------------------

function initClickTelemetry(root) {
  const token = root.querySelector('[data-search-click-token] input[name="__RequestVerificationToken"]')?.value;
  const query = new URLSearchParams(window.location.search).get('q') || '';
  if (!token || !query) return;

  const send = (link) => {
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
  };

  const onClick = (event) => {
    if (event.type === 'auxclick' && event.button !== 1) return;
    const link = event.target instanceof Element ? event.target.closest('a[data-search-result]') : null;
    if (link instanceof HTMLAnchorElement) send(link);
  };

  root.addEventListener('click', onClick);
  root.addEventListener('auxclick', onClick);
}

// -----------------------------------------------------------------------------
// Show more results (progressive enhancement of the "next page" link)
// -----------------------------------------------------------------------------

function setRange(range, first, last) {
  if (!(range instanceof HTMLElement)) return;
  const total = Number(range.dataset.total || 0);
  const strong = (text) => {
    const node = document.createElement('strong');
    node.textContent = text;
    return node;
  };
  range.replaceChildren(
    'Showing ',
    strong(`${numberFormat.format(first)}–${numberFormat.format(last)}`),
    ' of ',
    strong(numberFormat.format(total)),
    ' results');
}

function initLoadMore(root) {
  root.addEventListener('click', async (event) => {
    const link = event.target instanceof Element ? event.target.closest('a[data-search-load-more]') : null;
    if (!(link instanceof HTMLAnchorElement) || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
    const url = sameOriginUrl(link.href);
    const results = root.querySelector('[data-search-results]');
    if (!url || !(results instanceof HTMLElement) || link.getAttribute('aria-disabled') === 'true') return;

    event.preventDefault();
    const label = link.textContent;
    link.setAttribute('aria-disabled', 'true');
    link.textContent = 'Loading more results…';
    results.setAttribute('aria-busy', 'true');

    try {
      const response = await fetch(url, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const page = new DOMParser().parseFromString(await response.text(), 'text/html');
      const hits = Array.from(page.querySelectorAll('[data-search-results] [data-search-hit]'));
      if (hits.length === 0) throw new Error('No further results in the response.');

      const imported = hits.map((hit) => document.importNode(hit, true));
      results.append(...imported);

      const nav = root.querySelector('[data-search-pagination]');
      const nextLink = page.querySelector('[data-search-pagination] a[data-search-load-more]');
      if (nextLink instanceof HTMLAnchorElement && nav instanceof HTMLElement) {
        link.href = nextLink.getAttribute('href') || link.href;
        link.textContent = label;
        link.removeAttribute('aria-disabled');
      } else {
        nav?.remove();
      }

      const ranks = Array.from(results.querySelectorAll('a[data-search-result]'))
        .map((anchor) => Number(anchor.dataset.searchRank || 0))
        .filter((rank) => rank > 0);
      if (ranks.length > 0) setRange(root.querySelector('[data-search-range]'), Math.min(...ranks), Math.max(...ranks));

      // Move focus to the first new result so keyboard users continue where the list grew.
      imported[0]?.querySelector('a[data-search-result]')?.focus();
    } catch (error) {
      console.warn('[PRISM] Could not load more results in place; opening the next page.', error);
      window.location.assign(url);
    } finally {
      results.removeAttribute('aria-busy');
    }
  });
}

// -----------------------------------------------------------------------------
// Filters: project finder
// -----------------------------------------------------------------------------

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

      facets.forEach((facet, index) => {
        const label = (facet.dataset.projectLabel || facet.textContent || '').toLocaleLowerCase();
        const matches = term.length === 0 || label.includes(term);
        const visible = term.length > 0
          ? matches
          : (expanded || index < initialLimit || isSelected(facet));
        facet.classList.toggle('is-collapsed', !visible);
      });

      if (more instanceof HTMLButtonElement) {
        const canExpand = term.length === 0 && facets.length > initialLimit;
        more.hidden = !canExpand;
        more.textContent = expanded ? 'Show fewer' : `Show all ${facets.length} projects`;
        more.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      }
    };

    if (search instanceof HTMLInputElement) {
      search.setAttribute('aria-label', `Find a project among ${facets.length}`);
      search.addEventListener('input', applyVisibility);
      search.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && search.value) {
          event.preventDefault();
          event.stopPropagation();
          search.value = '';
          applyVisibility();
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
      facet.querySelector('input[type="checkbox"]')?.addEventListener('change', applyVisibility);
    });

    applyVisibility();
  });
}

// -----------------------------------------------------------------------------
// Filters: lazily loaded detailed facets
// -----------------------------------------------------------------------------

function humanizeToken(value) {
  return String(value || '').replace(/([a-z0-9])([A-Z])/g, '$1 $2');
}

function appliedValues(name) {
  return new Set(new URLSearchParams(window.location.search).getAll(name).map((value) => value.toLocaleLowerCase()));
}

function createFacetSection(title, inputName, facets, { project = false, humanize = false, open = false } = {}) {
  if (!Array.isArray(facets) || facets.length === 0) return null;

  const details = document.createElement('details');
  details.className = 'pm-gs-filter__section';
  details.dataset.filterSection = '';
  const selected = appliedValues(inputName);
  details.open = open || selected.size > 0;

  const summary = document.createElement('summary');
  summary.append(document.createTextNode(`${title} `));
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
    count.textContent = numberFormat.format(Number(facet?.count ?? 0));
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

function renderDetailedFacets(container, payload) {
  container.replaceChildren(...[
    createFacetSection('Source', 'Source', payload.sources, { open: true }),
    createFacetSection('Project', 'Project', payload.projects, { project: true }),
    createFacetSection('Status', 'Status', payload.statuses, { humanize: true }),
    createFacetSection('File type', 'FileType', payload.fileTypes),
    createFacetSection('Stage', 'Stage', payload.stages, { humanize: true })
  ].filter(Boolean));
}

function renderFacetSkeleton(container) {
  const skeleton = document.createElement('div');
  skeleton.className = 'pm-gs-filter__skeleton';
  skeleton.setAttribute('aria-hidden', 'true');
  for (let index = 0; index < 5; index += 1) {
    const bar = document.createElement('span');
    bar.className = 'pm-gs-filter__skeleton-bar';
    skeleton.append(bar);
  }
  const label = document.createElement('span');
  label.className = 'visually-hidden';
  label.textContent = 'Loading filters…';
  container.replaceChildren(skeleton, label);
}

function renderFacetError(container, retry) {
  const message = document.createElement('div');
  message.className = 'pm-gs-filter__error';
  message.setAttribute('role', 'alert');
  const text = document.createElement('span');
  text.textContent = 'Filters could not be loaded.';
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'btn btn-sm btn-outline-secondary';
  button.textContent = 'Retry';
  button.addEventListener('click', () => {
    retry();
  });
  message.append(text, button);
  container.replaceChildren(message);
}

function initLazyFacets(details, onLoaded) {
  const dynamic = details.querySelector('[data-search-dynamic-facets]');
  if (!(dynamic instanceof HTMLElement)) return;
  let controller = null;

  const load = async () => {
    if (details.dataset.facetsLoaded === 'true') return;
    controller?.abort();
    controller = new AbortController();
    const signal = controller.signal;
    renderFacetSkeleton(dynamic);
    dynamic.setAttribute('aria-busy', 'true');

    try {
      // Razor Pages facet handler: handler=Facets. Preserve the authorised query/filter state.
      const url = new URL(window.location.href);
      url.searchParams.set('handler', 'Facets');
      url.searchParams.delete('Cursor');
      const response = await fetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        credentials: 'same-origin',
        signal
      });
      if (!response.ok) throw new Error(`Search facets failed with HTTP ${response.status}.`);
      const payload = await response.json();
      if (!payload?.ok || !payload?.detailedLoaded) throw new Error('Search facets are unavailable.');

      renderDetailedFacets(dynamic, payload);
      details.dataset.facetsLoaded = 'true';
      initProjectFacets(dynamic);
      onLoaded();
    } catch (error) {
      if (signal.aborted) return;
      console.warn('[PRISM] Search facets are temporarily unavailable.', error);
      renderFacetError(dynamic, () => {
        void load().then(() => {
          dynamic.querySelector('input, summary, button')?.focus();
        });
      });
    } finally {
      if (controller?.signal === signal) dynamic.removeAttribute('aria-busy');
    }
  };

  details.addEventListener('toggle', () => {
    if (details.open) void load();
    else if (details.dataset.facetsLoaded !== 'true') controller?.abort();
  });
  if (details.open) void load();
}

// -----------------------------------------------------------------------------
// Filters: panel behaviour, counts, pending changes and date presets
// -----------------------------------------------------------------------------

function selectionKey(entries) {
  return entries
    .filter(([name, value]) => FILTER_NAMES.includes(name) && String(value).trim())
    .map(([name, value]) => `${name}=${String(value).trim().toLocaleLowerCase()}`)
    .sort()
    .join('&');
}

function updateFilterSelectionCounts(details, form) {
  form.querySelectorAll('[data-filter-section]').forEach((section) => {
    const count = section.querySelectorAll('input[type="checkbox"]:checked').length
      + Array.from(section.querySelectorAll('input[type="date"]')).filter((input) => input.value).length;
    const badge = section.querySelector('[data-filter-selected-count]');
    if (badge instanceof HTMLElement) {
      badge.textContent = String(count);
      badge.hidden = count === 0;
    }
  });

  const count = form.querySelectorAll('input[type="checkbox"]:checked').length
    + Array.from(form.querySelectorAll('input[type="date"]')).filter((input) => input.value).length;
  const badge = details.querySelector('[data-filter-active-count]');
  if (badge instanceof HTMLElement) {
    badge.textContent = String(count);
    badge.hidden = count === 0;
  }
  const label = details.querySelector('[data-filter-active-label]');
  if (label instanceof HTMLElement) label.textContent = count === 0 ? '' : `${count} selected`;
}

function isoDate(date) {
  const pad = (value) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function datePreset(kind, today = new Date()) {
  const end = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const start = new Date(end);
  if (kind === '30d') start.setDate(start.getDate() - 29);
  else if (kind === '12m') start.setFullYear(start.getFullYear() - 1, start.getMonth(), start.getDate() + 1);
  else if (kind === 'ytd') start.setMonth(0, 1);
  else return null;
  return { from: isoDate(start), to: isoDate(end) };
}

function initFilterPanel(details) {
  const form = details.querySelector('[data-search-filter-form]');
  const summary = details.querySelector('summary');
  const pending = details.querySelector('[data-filter-pending]');
  if (!(form instanceof HTMLFormElement) || !(summary instanceof HTMLElement)) return;

  const applied = () => selectionKey(Array.from(new URLSearchParams(window.location.search).entries()));
  const current = () => selectionKey(Array.from(new FormData(form).entries()));
  const isDirty = () => current() !== applied();
  const narrow = window.matchMedia('(max-width: 767.98px)');

  const refresh = () => {
    updateFilterSelectionCounts(details, form);
    if (pending instanceof HTMLElement) {
      pending.textContent = isDirty() ? 'Changes not applied yet' : '';
    }
  };

  // Put every control back to the filters the results were built with.
  const revert = () => {
    const params = new URLSearchParams(window.location.search);
    form.querySelectorAll('input[type="checkbox"]').forEach((input) => {
      const values = params.getAll(input.name).map((value) => value.toLocaleLowerCase());
      input.checked = values.includes(input.value.toLocaleLowerCase());
    });
    form.querySelectorAll('input[type="date"]').forEach((input) => {
      input.value = params.get(input.name) || '';
    });
    refresh();
  };

  const close = ({ discard = false, returnFocus = false } = {}) => {
    if (discard) revert();
    details.open = false;
    if (returnFocus) summary.focus();
  };

  // Clicking away keeps unapplied choices on screen rather than silently dropping them.
  const dismissSoftly = () => {
    if (isDirty()) {
      pending?.classList.remove('is-nudged');
      void pending?.offsetWidth;
      pending?.classList.add('is-nudged');
      form.querySelector('button[type="submit"]')?.focus({ preventScroll: true });
      return;
    }
    close();
  };

  summary.addEventListener('click', (event) => {
    if (details.open && isDirty()) {
      event.preventDefault();
      dismissSoftly();
    }
  });

  form.addEventListener('change', refresh);
  form.addEventListener('input', (event) => {
    if (event.target instanceof HTMLInputElement && event.target.type === 'date') refresh();
  });

  details.addEventListener('toggle', () => {
    document.documentElement.classList.toggle('pm-gs-filter-sheet-open', details.open && narrow.matches);
    if (details.open) {
      refresh();
      if (narrow.matches) {
        form.focus({ preventScroll: true });
      } else {
        // Lets the panel cap its height to the space below the button.
        form.style.setProperty('--pm-gs-panel-top', `${Math.round(summary.getBoundingClientRect().bottom)}px`);
      }
    }
  });

  // On the document: focus may have left the panel after a click elsewhere.
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && details.open && !event.defaultPrevented) {
      event.preventDefault();
      close({ discard: true, returnFocus: true });
    }
  });

  details.querySelectorAll('[data-filter-dismiss]').forEach((node) => {
    node.addEventListener('click', () => {
      if (node.classList.contains('pm-gs-filter__backdrop')) dismissSoftly();
      else close({ discard: true, returnFocus: true });
    });
  });

  document.addEventListener('pointerdown', (event) => {
    if (details.open && !details.contains(event.target)) dismissSoftly();
  });

  form.querySelectorAll('[data-date-preset]').forEach((button) => {
    button.addEventListener('click', () => {
      const range = datePreset(button.getAttribute('data-date-preset'));
      if (!range) return;
      const from = form.querySelector('input[name="DateFrom"]');
      const to = form.querySelector('input[name="DateTo"]');
      if (from instanceof HTMLInputElement) from.value = range.from;
      if (to instanceof HTMLInputElement) to.value = range.to;
      refresh();
    });
  });

  form.tabIndex = -1;
  initLazyFacets(details, refresh);
  initProjectFacets(form);
  refresh();
}

function initFilters(root) {
  root.querySelectorAll('[data-search-filter]').forEach((details) => {
    if (details instanceof HTMLDetailsElement) initFilterPanel(details);
  });
}

// -----------------------------------------------------------------------------
// Boot
// -----------------------------------------------------------------------------

function boot() {
  const root = document.querySelector('[data-search-page]');
  if (!(root instanceof HTMLElement)) return;
  initSearchBoxes(root);
  initRecentSearches(root);
  initClickTelemetry(root);
  initLoadMore(root);
  initFilters(root);
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
