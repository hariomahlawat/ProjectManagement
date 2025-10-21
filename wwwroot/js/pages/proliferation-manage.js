/* global bootstrap */
(() => {
  const Modal = window.bootstrap?.Modal ?? null;
  const Collapse = window.bootstrap?.Collapse ?? null;

  const listCard = document.querySelector('#pf-list-card');
  const overridesCard = document.querySelector('#pf-overrides-card');
  const editorCard = document.querySelector('#pf-editor');
  if (!listCard || !editorCard) {
    return;
  }

  const storageKey = 'proliferation-manage-filters';
  const overridesStorageKey = 'proliferation-manage-preference-overrides';
  const api = {
    list: '/api/proliferation/list',
    overrides: '/api/proliferation/preferences/overrides',
    yearly: (id) => `/api/proliferation/yearly/${id}`,
    granular: (id) => `/api/proliferation/granular/${id}`,
    saveYearly: (id) => (id ? `/api/proliferation/yearly/${id}` : '/api/proliferation/yearly'),
    saveGranular: (id) => (id ? `/api/proliferation/granular/${id}` : '/api/proliferation/granular'),
    deleteYearly: (id, rowVersion) => `/api/proliferation/yearly/${id}?rowVersion=${encodeURIComponent(rowVersion)}`,
    deleteGranular: (id, rowVersion) => `/api/proliferation/granular/${id}?rowVersion=${encodeURIComponent(rowVersion)}`,
    setPreference: '/api/proliferation/year-preference'
  };

  const listEl = document.querySelector('#pf-list');
  const pagerEl = document.querySelector('#pf-pager');
  const countEl = document.querySelector('#pf-count');

  const filters = {
    projectId: '',
    source: '',
    year: '',
    kind: ''
  };

  const pager = {
    page: 1,
    pageSize: Number(listCard.dataset.pageSize) || 25,
    total: 0
  };

  const defaults = {
    year: Number(editorCard.dataset.currentYear) || new Date().getUTCFullYear(),
    granularSource: '1'
  };

  const editor = {
    form: document.querySelector('#pf-form'),
    id: document.querySelector('#pf-id'),
    kind: document.querySelector('#pf-kind'),
    rowVersion: document.querySelector('#pf-row-version'),
    project: document.querySelector('#pf-project'),
    source: document.querySelector('#pf-source'),
    year: document.querySelector('#pf-year'),
    date: document.querySelector('#pf-date'),
    unit: document.querySelector('#pf-unit'),
    qty: document.querySelector('#pf-qty'),
    remarks: document.querySelector('#pf-remarks'),
    btnSave: document.querySelector('#pf-save'),
    btnReset: document.querySelector('#pf-reset'),
    btnDelete: document.querySelector('#pf-delete')
  };

  const filterInputs = {
    project: document.querySelector('#pf-project-filter'),
    source: document.querySelector('#pf-source-filter'),
    year: document.querySelector('#pf-year-filter'),
    kind: document.querySelector('#pf-kind-filter'),
    refresh: document.querySelector('#pf-refresh')
  };

  const toastHost = document.querySelector('#toastHost');

  const overridesElements = {
    card: overridesCard,
    collapse: overridesCard ? overridesCard.querySelector('#pf-overrides-collapse') : null,
    toggle: overridesCard ? overridesCard.querySelector('#pf-overrides-collapse-toggle') : null,
    tableBody: overridesCard ? overridesCard.querySelector('#pf-overrides-body') : null,
    summary: overridesCard ? overridesCard.querySelector('#pf-overrides-summary') : null,
    reset: overridesCard ? overridesCard.querySelector('#pf-overrides-reset') : null,
    project: overridesCard ? overridesCard.querySelector('#pf-overrides-project') : null,
    source: overridesCard ? overridesCard.querySelector('#pf-overrides-source') : null,
    year: overridesCard ? overridesCard.querySelector('#pf-overrides-year') : null,
    search: overridesCard ? overridesCard.querySelector('#pf-overrides-search') : null,
    refresh: overridesCard ? overridesCard.querySelector('#pf-overrides-refresh') : null,
    export: overridesCard ? overridesCard.querySelector('#pf-overrides-export') : null
  };
  const overridesOverviewUrl = overridesCard?.dataset?.overviewUrl ?? '';
  const overridesExportUrl = overridesCard?.dataset?.exportUrl ?? '';
  const overridesState = {
    filters: {
      projectId: '',
      source: '',
      year: '',
      search: ''
    },
    collapsed: false
  };
  const overridesRows = new Map();
  let overridesSearchTimer = null;

  const deleteModalElements = (() => {
    const element = document.querySelector('#pf-delete-modal');
    if (!element) return null;
    return {
      element,
      project: element.querySelector('[data-confirm-project]'),
      date: element.querySelector('[data-confirm-date]'),
      type: element.querySelector('[data-confirm-type]'),
      confirm: element.querySelector('[data-confirm-accept]'),
      cancel: element.querySelector('[data-confirm-cancel]')
    };
  })();

  let deleteModalTrigger = null;

  function toast(message, variant = 'success') {
    if (!message || !toastHost) return;
    const wrapper = document.createElement('div');
    wrapper.className = `toast align-items-center text-bg-${variant} border-0`;
    wrapper.role = 'status';
    wrapper.innerHTML = `
      <div class="d-flex">
        <div class="toast-body">${message}</div>
        <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
      </div>`;
    toastHost.append(wrapper);
    const instance = bootstrap?.Toast?.getOrCreateInstance(wrapper, { delay: 3500 }) ?? null;
    wrapper.addEventListener('hidden.bs.toast', () => wrapper.remove(), { once: true });
    instance?.show();
  }

  function loadFiltersFromStorage() {
    try {
      const raw = sessionStorage.getItem(storageKey);
      if (!raw) return;
      const saved = JSON.parse(raw);
      if (typeof saved === 'object' && saved) {
        filters.projectId = saved.projectId ?? '';
        filters.source = saved.source ?? '';
        filters.year = saved.year ?? '';
        filters.kind = saved.kind ?? '';
        if (Number.isFinite(saved.pageSize) && saved.pageSize > 0) {
          pager.pageSize = saved.pageSize;
        }
      }
    } catch (err) {
      console.warn('Unable to read saved filters', err); // eslint-disable-line no-console
    }
  }

  function saveFilters() {
    try {
      const payload = {
        projectId: filters.projectId,
        source: filters.source,
        year: filters.year,
        kind: filters.kind,
        pageSize: pager.pageSize
      };
      sessionStorage.setItem(storageKey, JSON.stringify(payload));
    } catch (err) {
      console.warn('Unable to persist filters', err); // eslint-disable-line no-console
    }
  }

  function loadOverrideStateFromStorage() {
    if (!overridesCard) return;
    try {
      const raw = sessionStorage.getItem(overridesStorageKey);
      if (!raw) return;
      const saved = JSON.parse(raw);
      if (typeof saved === 'object' && saved) {
        overridesState.filters.projectId = saved.projectId ?? '';
        overridesState.filters.source = saved.source ?? '';
        overridesState.filters.year = saved.year ?? '';
        overridesState.filters.search = saved.search ?? '';
        overridesState.collapsed = Boolean(saved.collapsed);
      }
    } catch (error) {
      console.warn('Unable to read preference override filters', error); // eslint-disable-line no-console
    }
  }

  function saveOverrideState() {
    if (!overridesCard) return;
    try {
      const payload = {
        projectId: overridesState.filters.projectId,
        source: overridesState.filters.source,
        year: overridesState.filters.year,
        search: overridesState.filters.search,
        collapsed: overridesState.collapsed
      };
      sessionStorage.setItem(overridesStorageKey, JSON.stringify(payload));
    } catch (error) {
      console.warn('Unable to persist preference override filters', error); // eslint-disable-line no-console
    }
  }

  function applyOverrideFiltersToInputs() {
    if (!overridesCard) return;
    if (overridesElements.project) {
      overridesElements.project.value = hasOption(overridesElements.project, overridesState.filters.projectId)
        ? overridesState.filters.projectId
        : '';
    }
    if (overridesElements.source) {
      overridesElements.source.value = hasOption(overridesElements.source, overridesState.filters.source)
        ? overridesState.filters.source
        : '';
    }
    if (overridesElements.year) {
      overridesElements.year.value = overridesState.filters.year ?? '';
    }
    if (overridesElements.search) {
      overridesElements.search.value = overridesState.filters.search ?? '';
    }
  }

  function overridesFiltersActive() {
    const { projectId, source, year, search } = overridesState.filters;
    return Boolean(projectId || source || year || search);
  }

  function updateOverridesResetVisibility() {
    if (!overridesElements.reset) return;
    overridesElements.reset.classList.toggle('d-none', !overridesFiltersActive());
  }

  function updateOverridesExportAvailability(enabled) {
    if (!overridesElements.export) return;
    const shouldDisable = !enabled;
    overridesElements.export.disabled = shouldDisable;
    overridesElements.export.setAttribute('aria-disabled', shouldDisable ? 'true' : 'false');
    overridesElements.export.classList.toggle('disabled', shouldDisable);
  }

  function setOverridesCollapsed(collapsed, persist = true) {
    if (!overridesElements.collapse) return;
    const instance = Collapse?.getOrCreateInstance(overridesElements.collapse, { toggle: false }) ?? null;
    if (instance) {
      if (collapsed) {
        instance.hide();
      } else {
        instance.show();
      }
    } else {
      overridesElements.collapse.classList.toggle('show', !collapsed);
    }

    const expandedLabel = overridesElements.toggle?.querySelector('[data-expanded-text]') ?? null;
    const collapsedLabel = overridesElements.toggle?.querySelector('[data-collapsed-text]') ?? null;
    if (expandedLabel && collapsedLabel) {
      expandedLabel.classList.toggle('d-none', collapsed);
      collapsedLabel.classList.toggle('d-none', !collapsed);
    }
    overridesElements.toggle?.setAttribute('aria-expanded', collapsed ? 'false' : 'true');

    if (persist) {
      overridesState.collapsed = collapsed;
      saveOverrideState();
    }
  }

  function formatDateTime(value) {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return String(value);
    }
    return date.toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  function applyFiltersToInputs() {
    if (filterInputs.project) {
      filterInputs.project.value = hasOption(filterInputs.project, filters.projectId) ? filters.projectId : '';
    }
    if (filterInputs.source) {
      filterInputs.source.value = hasOption(filterInputs.source, filters.source) ? filters.source : '';
    }
    if (filterInputs.kind) {
      filterInputs.kind.value = hasOption(filterInputs.kind, filters.kind) ? filters.kind : '';
    }
    if (filterInputs.year) {
      filterInputs.year.value = filters.year ?? '';
    }
  }

  function hasOption(select, value) {
    if (!select || value === undefined || value === null) return false;
    return Array.from(select.options).some((opt) => opt.value === String(value));
  }

  function updateFilter(key, value) {
    filters[key] = value ?? '';
    pager.page = 1;
    saveFilters();
    fetchList();
  }

  function formatNumber(value) {
    if (!Number.isFinite(value)) return '0';
    return Number(value).toLocaleString();
  }

  function formatDate(value) {
    if (!value) return '';
    const text = String(value);
    if (text.includes('T')) {
      return text.split('T')[0];
    }
    if (text.includes(' ')) {
      return text.split(' ')[0];
    }
    return text;
  }

  function updateOverridesFilter(key, rawValue) {
    if (!overridesCard) return;
    const value = rawValue ?? '';
    overridesState.filters[key] = value;
    saveOverrideState();
    updateOverridesResetVisibility();
    fetchOverrides();
  }

  function resetOverridesFilters() {
    if (!overridesCard) return;
    overridesState.filters.projectId = '';
    overridesState.filters.source = '';
    overridesState.filters.year = '';
    overridesState.filters.search = '';
    saveOverrideState();
    applyOverrideFiltersToInputs();
    updateOverridesResetVisibility();
    fetchOverrides();
  }

  function normalizeOverrideRow(row) {
    if (!row || typeof row !== 'object') return null;
    const sourceValue = Number(row.sourceValue ?? row.source);
    const effectiveTotalRaw = Number(row.effectiveTotal ?? row.EffectiveTotal);
    const modeRaw = row.mode ?? row.Mode;
    const effectiveModeRaw = row.effectiveMode ?? row.EffectiveMode;
    return {
      id: String(row.id ?? ''),
      projectId: row.projectId ?? '',
      projectName: row.projectName ?? 'Unknown project',
      projectCode: row.projectCode ?? '',
      source: row.source,
      sourceValue: Number.isFinite(sourceValue) ? sourceValue : null,
      sourceLabel: row.sourceLabel ?? String(row.source ?? ''),
      year: row.year ?? '',
      mode: typeof modeRaw === 'string' ? modeRaw : String(modeRaw ?? ''),
      modeLabel: row.modeLabel ?? String(modeRaw ?? ''),
      effectiveMode: typeof effectiveModeRaw === 'string' ? effectiveModeRaw : String(effectiveModeRaw ?? ''),
      effectiveModeLabel: row.effectiveModeLabel ?? String(effectiveModeRaw ?? ''),
      setByUserId: row.setByUserId ?? '',
      setByDisplayName: row.setByDisplayName ?? row.setByUserId ?? '',
      setOnUtc: row.setOnUtc ?? '',
      hasYearly: Boolean(row.hasYearly ?? row.hasApprovedYearly),
      hasGranular: Boolean(row.hasGranular ?? row.hasApprovedGranular),
      effectiveTotal: Number.isFinite(effectiveTotalRaw) ? effectiveTotalRaw : null
    };
  }

  function findLatestOverrideRow() {
    let latest = null;
    overridesRows.forEach((row) => {
      if (!row?.setOnUtc) return;
      const timestamp = new Date(row.setOnUtc).getTime();
      if (!Number.isFinite(timestamp)) return;
      if (!latest || timestamp > latest.timestamp) {
        latest = { timestamp, row };
      }
    });
    return latest?.row ?? null;
  }

  function updateOverridesSummary() {
    if (!overridesElements.summary) return;
    const count = overridesRows.size;
    if (count === 0) {
      overridesElements.summary.textContent = 'No overrides configured.';
      return;
    }

    let summary = count === 1 ? '1 override loaded.' : `${count} overrides loaded.`;
    const latestRow = findLatestOverrideRow();
    if (latestRow) {
      const when = formatDateTime(latestRow.setOnUtc);
      const actor = latestRow.setByDisplayName || latestRow.setByUserId || '';
      let detail = '';
      if (when && actor) {
        detail = `Last updated ${when} by ${actor}`;
      } else if (when) {
        detail = `Last updated ${when}`;
      } else if (actor) {
        detail = `Last updated by ${actor}`;
      }
      if (detail) {
        summary = `${summary} ${detail}.`;
      }
    }

    overridesElements.summary.textContent = summary;
  }

  function renderOverrides(rows) {
    if (!overridesElements.tableBody) return;
    overridesRows.clear();

    if (!rows || rows.length === 0) {
      overridesElements.tableBody.innerHTML = '<tr><td colspan="6" class="text-muted">No overrides found.</td></tr>';
      if (overridesElements.summary) {
        overridesElements.summary.textContent = 'No overrides configured.';
      }
      overridesElements.tableBody.innerHTML = '<tr><td colspan="8" class="text-muted">No overrides found.</td></tr>';
      updateOverridesSummary();
      updateOverridesExportAvailability(false);
      return;
    }

    const normalizedRows = rows
      .map((raw) => {
        const row = normalizeOverrideRow(raw);
        if (!row || !row.id) return null;
        overridesRows.set(row.id, row);
        return row;
      })
      .filter(Boolean);

    normalizedRows.sort((a, b) => {
      const coverageScore = (value) => {
        if (value.hasYearly && value.hasGranular) return 0;
        if (value.hasGranular) return 1;
        if (value.hasYearly) return 2;
        return 3;
      };
      const coverageDiff = coverageScore(a) - coverageScore(b);
      if (coverageDiff !== 0) return coverageDiff;
      const modeDiff = String(a.modeLabel).localeCompare(String(b.modeLabel), undefined, { sensitivity: 'base' });
      if (modeDiff !== 0) return modeDiff;
      const dateA = a.setOnUtc ? new Date(a.setOnUtc).getTime() : 0;
      const dateB = b.setOnUtc ? new Date(b.setOnUtc).getTime() : 0;
      return dateB - dateA;
    });

    const stats = {
      both: 0,
      granularOnly: 0,
      yearlyOnly: 0,
      none: 0,
      autoFallback: 0
    };

    const markup = normalizedRows.map((row) => {
      const projectCode = row.projectCode ? `<div class="small text-muted">${row.projectCode}</div>` : '';
      const scope = `<div class="small text-muted">${row.sourceLabel} · ${row.year}</div>`;
      const updated = formatDateTime(row.setOnUtc);
      const coverageBadges = [];
      if (row.hasYearly && row.hasGranular) {
        stats.both += 1;
        coverageBadges.push('<span class="badge text-bg-success">Yearly + Granular present</span>');
      } else if (row.hasGranular) {
        stats.granularOnly += 1;
        coverageBadges.push('<span class="badge text-bg-primary">Granular present</span>');
        coverageBadges.push('<span class="badge text-bg-light text-wrap">No yearly total</span>');
      } else if (row.hasYearly) {
        stats.yearlyOnly += 1;
        coverageBadges.push('<span class="badge text-bg-primary">Yearly present</span>');
        coverageBadges.push('<span class="badge text-bg-light text-wrap">No granular data</span>');
      } else {
        stats.none += 1;
        coverageBadges.push('<span class="badge text-bg-warning text-dark">No approved data</span>');
      }

      if (row.mode === 'Auto' && !row.hasGranular) {
        stats.autoFallback += 1;
        coverageBadges.push('<span class="badge text-bg-warning text-dark">Auto fallback</span>');
      }

      const effectiveTotal = Number.isFinite(row.effectiveTotal) ? row.effectiveTotal : null;
      const effectiveTotalDisplay = effectiveTotal === null ? '—' : effectiveTotal.toLocaleString();
      const setById = row.setByUserId && row.setByUserId !== row.setByDisplayName
        ? `<div class="small text-muted">ID: ${row.setByUserId}</div>`
        : '';
      const actions = `
        <div class="btn-group btn-group-sm" role="group">
          <button type="button" class="btn btn-outline-secondary" data-action="focus-list" data-id="${row.id}">List</button>
          <button type="button" class="btn btn-outline-secondary" data-action="prefill" data-id="${row.id}">Editor</button>
          <button type="button" class="btn btn-outline-secondary" data-action="overview" data-id="${row.id}">Overview</button>
          <button type="button" class="btn btn-outline-danger" data-action="clear" data-id="${row.id}">Clear</button>
        </div>`;

      return `
        <tr>
          <td>
            <div class="fw-semibold">${row.projectName}</div>
            ${projectCode}
          </td>
          <td>${scope}</td>
          <td>
            <div class="d-flex flex-wrap gap-1">${coverageBadges.join('')}</div>
          </td>
          <td>
            <div class="fw-semibold">${row.modeLabel}</div>
            <div class="small text-muted">Effective: ${row.effectiveModeLabel}</div>
            <div class="small">Total in play: <span class="fw-semibold">${effectiveTotalDisplay}</span></div>
          </td>
          <td>
            <div class="fw-semibold">${row.setByDisplayName}</div>
            <div class="small text-muted">${updated}</div>
          </td>
          <td>${row.sourceLabel}</td>
          <td>${row.year}</td>
          <td>${row.modeLabel}</td>
          <td>${row.effectiveModeLabel}</td>
          <td>
            <div class="fw-semibold">${row.setByDisplayName}</div>
            ${setById}
          </td>
          <td>${updated}</td>
          <td class="text-end">${actions}</td>
        </tr>`;
    }).join('');

    overridesElements.tableBody.innerHTML = markup || '<tr><td colspan="6" class="text-muted">No overrides found.</td></tr>';
    if (overridesElements.summary) {
      const count = overridesRows.size;
      const pieces = [];
      if (stats.both) pieces.push(`${stats.both} with both data types`);
      if (stats.granularOnly) pieces.push(`${stats.granularOnly} granular-only`);
      if (stats.yearlyOnly) pieces.push(`${stats.yearlyOnly} yearly-only`);
      if (stats.none) pieces.push(`${stats.none} without approved data`);
      if (stats.autoFallback) pieces.push(`${stats.autoFallback} auto fallback`);
      const detail = pieces.length ? ` (${pieces.join(', ')})` : '';
      overridesElements.summary.textContent = count === 1
        ? `1 override loaded${detail}.`
        : `${count} overrides loaded${detail}.`;
    }
    overridesElements.tableBody.innerHTML = markup || '<tr><td colspan="8" class="text-muted">No overrides found.</td></tr>';
    updateOverridesSummary();
    updateOverridesExportAvailability(overridesRows.size > 0);
  }

  async function fetchOverrides() {
    if (!overridesCard || !overridesElements.tableBody) return;
    const params = new URLSearchParams();
    if (overridesState.filters.projectId) params.set('projectId', overridesState.filters.projectId);
    if (overridesState.filters.source) params.set('source', overridesState.filters.source);
    if (overridesState.filters.year) params.set('year', overridesState.filters.year);
    if (overridesState.filters.search) params.set('search', overridesState.filters.search);

    overridesElements.tableBody.innerHTML = '<tr><td colspan="6" class="text-muted">Loading…</td></tr>';
    if (overridesElements.summary) {
      overridesElements.summary.textContent = '';
    }
    updateOverridesExportAvailability(false);

    try {
      const response = await fetch(`${api.overrides}?${params.toString()}`, { headers: { Accept: 'application/json' } });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || 'Unable to load overrides');
      }
      const data = await response.json();
      const rows = Array.isArray(data) ? data : [];
      renderOverrides(rows);
    } catch (error) {
      overridesElements.tableBody.innerHTML = '<tr><td colspan="6" class="text-danger">Failed to load overrides.</td></tr>';
      if (overridesElements.summary) {
        overridesElements.summary.textContent = '';
      }
      updateOverridesExportAvailability(false);
      toast(error.message || 'Unable to load overrides', 'danger');
    }
  }

  function exportOverrides() {
    if (!overridesExportUrl) {
      toast('Export is unavailable right now. Refresh and try again.', 'warning');
      return;
    }

    try {
      const url = new URL(overridesExportUrl, window.location.origin);
      if (overridesState.filters.projectId) url.searchParams.set('projectId', overridesState.filters.projectId);
      if (overridesState.filters.source) url.searchParams.set('source', overridesState.filters.source);
      if (overridesState.filters.year) url.searchParams.set('year', overridesState.filters.year);
      if (overridesState.filters.search) url.searchParams.set('search', overridesState.filters.search);

      const link = document.createElement('a');
      link.href = url.toString();
      link.target = '_blank';
      link.rel = 'noopener';
      link.setAttribute('download', '');
      document.body.append(link);
      link.click();
      link.remove();
    } catch (error) {
      toast('Unable to start export. Please try again.', 'danger');
    }
  }

  function openOverview(row, anchor = '') {
    if (!row || !overridesOverviewUrl) return;
    try {
      const url = new URL(overridesOverviewUrl, window.location.origin);
      if (row.projectId) url.searchParams.set('projectId', row.projectId);
      if (Number.isFinite(row.sourceValue) && row.sourceValue !== null) {
        url.searchParams.set('source', String(row.sourceValue));
      }
      if (row.year) url.searchParams.set('year', row.year);
      if (anchor) {
        url.hash = anchor.startsWith('#') ? anchor : `#${anchor}`;
      }
      window.open(url.toString(), '_blank', 'noopener');
    } catch (error) {
      toast('Unable to open overview.', 'danger');
    }
  }

  function ensureFilterOption(select, value, label) {
    if (!select || value === undefined || value === null) return;
    const stringValue = String(value);
    if (hasOption(select, stringValue)) return;
    const option = document.createElement('option');
    option.value = stringValue;
    option.textContent = label ?? stringValue;
    select.append(option);
  }

  function focusListFromOverride(row) {
    if (!row) return;
    const projectValue = row.projectId ? String(row.projectId) : '';
    const sourceValue = row.sourceValue !== null && row.sourceValue !== undefined
      ? String(row.sourceValue)
      : '';
    const yearValue = row.year ? String(row.year) : '';

    if (filterInputs.project && projectValue) {
      ensureFilterOption(filterInputs.project, projectValue, row.projectName);
      filterInputs.project.value = projectValue;
    }
    if (filterInputs.source && sourceValue) {
      filterInputs.source.value = sourceValue;
    }
    if (filterInputs.year) {
      filterInputs.year.value = yearValue;
    }

    filters.projectId = projectValue;
    filters.source = sourceValue;
    filters.year = yearValue;
    filters.kind = '';
    pager.page = 1;
    applyFiltersToInputs();
    saveFilters();
    fetchList();
    listCard?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    toast('List filters updated for this override.', 'info');
  }

  function prefillEditorFromOverride(row) {
    if (!row) return;
    resetEditor('yearly');
    if (editor.project && hasOption(editor.project, row.projectId)) {
      editor.project.value = String(row.projectId);
    }
    if (editor.source && row.sourceValue !== null && row.sourceValue !== undefined) {
      const sourceValue = String(row.sourceValue);
      if (hasOption(editor.source, sourceValue)) {
        editor.source.value = sourceValue;
      }
    }
    if (editor.year) {
      editor.year.value = String(row.year ?? defaults.year);
    }
    setTab('yearly');
    editor.project?.focus();
    editorCard?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    toast('Editor prefilled from override.', 'info');
  }

  async function clearOverride(row) {
    if (!row) return;
    const confirmed = window.confirm('Clear this preference override and return to defaults?');
    if (!confirmed) return;
    try {
      const payload = {
        projectId: row.projectId,
        source: row.sourceValue ?? row.source,
        year: row.year,
        mode: 'UseYearlyAndGranular'
      };
      const response = await fetch(api.setPreference, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        const text = await response.text();
        if (response.status === 403) {
          openOverview(row, '#preferences');
          throw new Error(text || 'You do not have permission to change preferences. Try updating them from the overview.');
        }
        throw new Error(text || 'Unable to clear override');
      }
      toast('Preference override cleared.', 'success');
      fetchOverrides();
    } catch (error) {
      toast(error.message || 'Unable to clear override', 'danger');
    }
  }

  function confirmDeletion(details = {}) {
    const fallbackType = details.type === 'yearly' ? 'yearly total' : 'record';
    const fallbackProject = details.project ? ` for ${details.project}` : '';
    const fallbackDate = details.dateOrYear ? ` (${details.dateOrYear})` : '';
    const fallbackMessage = `Are you sure you want to delete this ${fallbackType}${fallbackProject}${fallbackDate}?`;

    if (!deleteModalElements?.element || !deleteModalElements?.confirm || !Modal) {
      return Promise.resolve(window.confirm(fallbackMessage));
    }

    const instance = Modal.getOrCreateInstance(deleteModalElements.element, {
      backdrop: 'static',
      keyboard: true,
      focus: true
    });

    deleteModalTrigger = null;
    const trigger = details.trigger;
    if (trigger instanceof HTMLElement) {
      deleteModalTrigger = trigger;
    } else if (document.activeElement instanceof HTMLElement) {
      deleteModalTrigger = document.activeElement;
    }

    if (deleteModalElements.type) {
      const typeLabel = details.type === 'yearly' ? 'yearly total' : 'record';
      deleteModalElements.type.textContent = typeLabel;
    }
    if (deleteModalElements.project) {
      deleteModalElements.project.textContent = details.project || 'this project';
    }
    if (deleteModalElements.date) {
      deleteModalElements.date.textContent = details.dateOrYear || 'the selected period';
    }

    if (deleteModalElements.cancel) {
      deleteModalElements.cancel.disabled = false;
    }
    deleteModalElements.confirm.disabled = false;

    return new Promise(resolve => {
      let settled = false;

      const cleanup = () => {
        deleteModalElements.confirm.removeEventListener('click', handleConfirm);
        deleteModalElements.element.removeEventListener('hidden.bs.modal', handleHidden);
        deleteModalElements.element.removeEventListener('shown.bs.modal', handleShown);
      };

      const handleHidden = () => {
        cleanup();
        if (deleteModalElements.cancel) {
          deleteModalElements.cancel.disabled = false;
        }
        deleteModalElements.confirm.disabled = false;
        if (!settled) {
          settled = true;
          resolve(false);
        }
        if (deleteModalTrigger && typeof deleteModalTrigger.focus === 'function') {
          deleteModalTrigger.focus();
        }
        deleteModalTrigger = null;
      };

      const handleConfirm = () => {
        if (settled) {
          return;
        }
        settled = true;
        deleteModalElements.confirm.disabled = true;
        if (deleteModalElements.cancel) {
          deleteModalElements.cancel.disabled = true;
        }
        resolve(true);
        instance.hide();
      };

      const handleShown = () => {
        deleteModalElements.confirm.focus();
      };

      deleteModalElements.confirm.addEventListener('click', handleConfirm);
      deleteModalElements.element.addEventListener('hidden.bs.modal', handleHidden, { once: true });
      deleteModalElements.element.addEventListener('shown.bs.modal', handleShown, { once: true });

      instance.show();
    });
  }

  async function fetchList() {
    if (!listEl) return;
    const params = new URLSearchParams();
    if (filters.projectId) params.set('projectId', filters.projectId);
    if (filters.source) params.set('source', filters.source);
    if (filters.year) params.set('year', filters.year);
    if (filters.kind) params.set('kind', filters.kind);
    params.set('page', String(pager.page));
    params.set('pageSize', String(pager.pageSize));

    listEl.innerHTML = '<div class="list-group-item text-muted" data-placeholder>Loading…</div>';
    countEl.textContent = '';

    try {
      const response = await fetch(`${api.list}?${params.toString()}`, { headers: { Accept: 'application/json' } });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || 'Unable to load list');
      }
      const data = await response.json();
      pager.page = Number(data.page) || pager.page;
      pager.pageSize = Number(data.pageSize) || pager.pageSize;
      pager.total = Number(data.total) || 0;
      const items = Array.isArray(data.items) ? data.items : [];
      renderList(items);
      renderPager();
      renderCount();
    } catch (error) {
      pager.total = 0;
      renderList([]);
      renderPager();
      renderCount();
      toast(error.message || 'Unable to load list', 'danger');
    }
  }

  function initOverrides() {
    if (!overridesCard) return;
    loadOverrideStateFromStorage();
    applyOverrideFiltersToInputs();
    updateOverridesResetVisibility();
    setOverridesCollapsed(overridesState.collapsed, false);

    if (overridesElements.collapse && Collapse) {
      overridesElements.collapse.addEventListener('shown.bs.collapse', () => {
        overridesState.collapsed = false;
        saveOverrideState();
      });
      overridesElements.collapse.addEventListener('hidden.bs.collapse', () => {
        overridesState.collapsed = true;
        saveOverrideState();
      });
    }

    overridesElements.toggle?.addEventListener('click', () => {
      setOverridesCollapsed(!overridesState.collapsed);
    });

    overridesElements.project?.addEventListener('change', (event) => {
      updateOverridesFilter('projectId', event.target.value);
    });
    overridesElements.source?.addEventListener('change', (event) => {
      updateOverridesFilter('source', event.target.value);
    });
    overridesElements.year?.addEventListener('change', (event) => {
      const raw = (event.target.value || '').trim();
      if (raw && !/^[0-9]{4}$/.test(raw)) {
        toast('Year must be a four digit number.', 'warning');
        event.target.value = '';
        updateOverridesFilter('year', '');
        return;
      }
      updateOverridesFilter('year', raw);
    });

    overridesElements.search?.addEventListener('input', (event) => {
      const raw = (event.target.value || '').trim();
      if (overridesSearchTimer) {
        clearTimeout(overridesSearchTimer);
      }
      overridesSearchTimer = setTimeout(() => {
        updateOverridesFilter('search', raw);
      }, 250);
    });

    overridesElements.refresh?.addEventListener('click', () => {
      fetchOverrides();
    });

    overridesElements.reset?.addEventListener('click', () => {
      resetOverridesFilters();
    });

    overridesElements.export?.addEventListener('click', () => {
      exportOverrides();
    });

    overridesElements.tableBody?.addEventListener('click', (event) => {
      const button = event.target.closest('button[data-action][data-id]');
      if (!button) return;
      const id = button.getAttribute('data-id');
      const action = button.getAttribute('data-action');
      if (!id || !action) return;
      const row = overridesRows.get(id);
      if (!row) {
        toast('Override details not found. Refresh and try again.', 'warning');
        return;
      }
      if (action === 'overview') {
        openOverview(row, '#overview');
      } else if (action === 'prefill') {
        prefillEditorFromOverride(row);
      } else if (action === 'focus-list') {
        focusListFromOverride(row);
      } else if (action === 'clear') {
        clearOverride(row);
      }
    });

    fetchOverrides();
  }

  function renderList(items) {
    if (!listEl) return;
    if (!items || items.length === 0) {
      listEl.innerHTML = '<div class="list-group-item text-muted" data-placeholder>No records found.</div>';
      return;
    }

    const rows = items.map((item) => {
      const kind = (item.kind || '').toString().toLowerCase() === 'yearly' ? 'yearly' : 'granular';
      const project = item.projectName ?? 'Unknown project';
      const sourceLabel = item.sourceLabel ?? '';
      const quantity = formatNumber(item.quantity ?? item.totalQuantity);
      const dateText = item.proliferationDateUtc ? formatDate(item.proliferationDateUtc) : (item.year ?? '');
      const subtitleParts = [sourceLabel, dateText].filter(Boolean);
      const approval = item.approvalStatus ? `Status: ${item.approvalStatus}` : '';
      const subtitle = [subtitleParts.join(' · '), approval].filter(Boolean).join(' • ');
      return `
        <button class="list-group-item list-group-item-action" data-id="${item.id}" data-kind="${kind}" type="button">
          <div class="d-flex justify-content-between align-items-start gap-3">
            <div>
              <div class="fw-semibold">${project}</div>
              <div class="small text-muted">${subtitle}</div>
            </div>
            <div class="text-end">
              <div class="fw-semibold">${quantity}</div>
            </div>
          </div>
        </button>`;
    });

    listEl.innerHTML = rows.join('');
  }

  function renderCount() {
    if (!countEl) return;
    if (!pager.total) {
      countEl.textContent = 'No records to display.';
      return;
    }
    const start = (pager.page - 1) * pager.pageSize + 1;
    const end = Math.min(pager.page * pager.pageSize, pager.total);
    countEl.textContent = `Showing ${start}-${end} of ${pager.total}`;
  }

  function renderPager() {
    if (!pagerEl) return;
    pagerEl.innerHTML = '';
    const totalPages = Math.max(1, Math.ceil(pager.total / pager.pageSize));
    if (totalPages <= 1) return;

    const addPage = (label, page, disabled = false, active = false) => {
      const li = document.createElement('li');
      li.className = 'page-item';
      if (disabled) li.classList.add('disabled');
      if (active) li.classList.add('active');
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'page-link';
      button.textContent = label;
      button.dataset.page = String(page);
      li.append(button);
      pagerEl.append(li);
    };

    const addEllipsis = () => {
      const li = document.createElement('li');
      li.className = 'page-item disabled';
      const span = document.createElement('span');
      span.className = 'page-link';
      span.textContent = '…';
      li.append(span);
      pagerEl.append(li);
    };

    addPage('‹', pager.page - 1, pager.page <= 1, false);

    const maxButtons = 5;
    let start = Math.max(1, pager.page - 2);
    let end = Math.min(totalPages, start + maxButtons - 1);
    if (end - start < maxButtons - 1) {
      start = Math.max(1, end - maxButtons + 1);
    }

    if (start > 1) {
      addPage('1', 1, false, pager.page === 1);
      if (start > 2) addEllipsis();
    }

    for (let p = start; p <= end; p += 1) {
      addPage(String(p), p, false, pager.page === p);
    }

    if (end < totalPages) {
      if (end < totalPages - 1) addEllipsis();
      addPage(String(totalPages), totalPages, false, pager.page === totalPages);
    }

    addPage('›', pager.page + 1, pager.page >= totalPages, false);
  }

  pagerEl?.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-page]');
    if (!button) return;
    const targetPage = Number(button.dataset.page);
    if (!Number.isFinite(targetPage) || targetPage === pager.page || targetPage < 1) return;
    const totalPages = Math.max(1, Math.ceil(pager.total / pager.pageSize));
    if (targetPage > totalPages) return;
    pager.page = targetPage;
    fetchList();
  });

  listEl?.addEventListener('click', (event) => {
    const button = event.target.closest('[data-id][data-kind]');
    if (!button) return;
    const id = button.getAttribute('data-id');
    const kind = button.getAttribute('data-kind');
    if (!id || !kind) return;
    loadIntoEditor(kind, id).catch((err) => {
      toast(err.message || 'Unable to load entry', 'danger');
    });
  });

  function enforceSourceForKind(kind) {
    const options = Array.from(editor.source?.options ?? []);
    if (!options.length) return;
    if (kind === 'granular') {
      options.forEach((opt) => {
        if (!opt.value) return;
        const isSdd = Number(opt.value) === Number(defaults.granularSource);
        opt.disabled = !isSdd;
      });
      const selected = options.find((opt) => !opt.disabled) ?? options.find((opt) => opt.value);
      if (selected) {
        editor.source.value = selected.value;
      }
    } else {
      options.forEach((opt) => {
        opt.disabled = false;
      });
    }
  }

  function setTab(kind, options = {}) {
    const target = kind === 'yearly' ? 'yearly' : 'granular';
    const updateHash = options.updateHash !== false;
    document.querySelectorAll('#pf-editor-tabs .nav-link').forEach((btn) => {
      const active = btn.dataset.target === target;
      btn.classList.toggle('active', active);
      btn.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    editor.kind.value = target;
    const isGranular = target === 'granular';
    document.querySelectorAll('.granular-only').forEach((el) => {
      el.classList.toggle('d-none', !isGranular);
    });
    if (editor.date) editor.date.required = isGranular;
    if (editor.unit) editor.unit.required = isGranular;
    if (editor.qty) editor.qty.min = isGranular ? 1 : 0;
    enforceSourceForKind(target);
    if (updateHash) {
      const newHash = `#${target}`;
      if (window.location.hash !== newHash) {
        window.history.replaceState(null, '', newHash);
      }
    }
  }

  document.querySelector('#tab-granular')?.addEventListener('click', () => setTab('granular'));
  document.querySelector('#tab-yearly')?.addEventListener('click', () => setTab('yearly'));

  async function loadIntoEditor(kind, id) {
    const endpoint = kind === 'yearly' ? api.yearly(id) : api.granular(id);
    const response = await fetch(endpoint, { headers: { Accept: 'application/json' } });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || 'Failed to load entry');
    }
    const detail = await response.json();
    editor.id.value = detail.id ?? '';
    editor.rowVersion.value = detail.rowVersion ?? '';
    editor.project.value = String(detail.projectId ?? '');
    editor.source.value = String(detail.source ?? '');
    editor.year.value = String(detail.year ?? defaults.year);
    if (kind === 'granular') {
      const iso = formatDate(detail.proliferationDateUtc ?? detail.proliferationDate);
      editor.date.value = iso;
      if (iso && iso.length >= 4) {
        editor.year.value = iso.slice(0, 4);
      }
      editor.unit.value = detail.unitName ?? '';
      editor.qty.value = detail.quantity ?? '';
      setTab('granular');
    } else {
      editor.date.value = '';
      editor.unit.value = '';
      editor.qty.value = detail.totalQuantity ?? '';
      setTab('yearly');
    }
    editor.remarks.value = detail.remarks ?? '';
    editor.btnDelete.disabled = false;
  }

  editor.form?.addEventListener('submit', async (event) => {
    event.preventDefault();
    if (!editor.btnSave) return;
    editor.btnSave.disabled = true;
    try {
      const kind = editor.kind.value === 'yearly' ? 'yearly' : 'granular';
      const id = editor.id.value || null;
      const payload = buildPayload(kind);
      if (!payload) {
        throw new Error('Please fill in the required fields.');
      }
      const url = kind === 'yearly' ? api.saveYearly(id) : api.saveGranular(id);
      const method = id ? 'PUT' : 'POST';
      const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || 'Save failed');
      }
      toast('Entry saved successfully.', 'success');
      await fetchList();
      resetEditor(kind === 'yearly' ? 'yearly' : 'granular');
    } catch (error) {
      toast(error.message || 'Unable to save entry', 'danger');
    } finally {
      editor.btnSave.disabled = false;
    }
  });

  function buildPayload(kind) {
    const projectId = Number(editor.project?.value ?? '');
    const source = Number(editor.source?.value ?? '');
    const year = Number(editor.year?.value ?? '');
    const remarks = editor.remarks?.value?.trim() || null;

    if (!Number.isFinite(projectId) || projectId <= 0) return null;
    if (!Number.isFinite(source) || source <= 0) return null;
    if (!Number.isFinite(year) || year < 2000) return null;

    if (kind === 'yearly') {
      const quantity = Number(editor.qty?.value ?? '');
      if (!Number.isFinite(quantity) || quantity < 0) return null;
      const payload = {
        projectId,
        source,
        year,
        totalQuantity: quantity,
        remarks
      };
      if (editor.rowVersion.value) {
        payload.rowVersion = editor.rowVersion.value;
      }
      return payload;
    }

    const dateValue = editor.date?.value ?? '';
    if (!dateValue) return null;
    const unit = editor.unit?.value?.trim();
    const quantity = Number(editor.qty?.value ?? '');
    if (!unit) return null;
    if (!Number.isFinite(quantity) || quantity <= 0) return null;

    const payload = {
      projectId,
      source,
      unitName: unit,
      proliferationDateUtc: `${dateValue}T00:00:00Z`,
      quantity,
      remarks
    };
    if (editor.rowVersion.value) {
      payload.rowVersion = editor.rowVersion.value;
    }
    return payload;
  }

  editor.btnReset?.addEventListener('click', () => {
    const currentKind = editor.kind.value;
    resetEditor(currentKind);
  });

  editor.btnDelete?.addEventListener('click', async () => {
    if (editor.btnDelete.disabled) return;
    const id = editor.id.value;
    const kind = editor.kind.value === 'yearly' ? 'yearly' : 'granular';
    if (!id) return;
    const rowVersion = editor.rowVersion.value;
    if (!rowVersion) {
      toast('The record is out of date. Reload the entry before deleting.', 'warning');
      return;
    }
    const projectText = editor.project?.selectedOptions?.[0]?.text?.trim() || '';
    const dateOrYear = kind === 'yearly'
      ? (editor.year?.value?.trim() || '')
      : formatDate(editor.date?.value || '');
    const confirmed = await confirmDeletion({
      project: projectText,
      type: kind,
      dateOrYear,
      trigger: editor.btnDelete
    });
    if (!confirmed) return;
    editor.btnDelete.disabled = true;
    try {
      const url = kind === 'yearly' ? api.deleteYearly(id, rowVersion) : api.deleteGranular(id, rowVersion);
      const response = await fetch(url, { method: 'DELETE' });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || 'Delete failed');
      }
      toast('Entry deleted.', 'warning');
      await fetchList();
      resetEditor(kind);
    } catch (error) {
      editor.btnDelete.disabled = false;
      toast(error.message || 'Unable to delete entry', 'danger');
    }
  });

  function resetEditor(preferredKind = 'granular') {
    editor.form?.reset();
    editor.id.value = '';
    editor.rowVersion.value = '';
    editor.qty.value = '';
    editor.remarks.value = '';
    if (editor.year) editor.year.value = String(defaults.year);
    if (editor.date) editor.date.value = '';
    if (editor.unit) editor.unit.value = '';
    editor.btnDelete && (editor.btnDelete.disabled = true);
    setTab(preferredKind, { updateHash: false });
  }

  function handleHashChange() {
    const hash = window.location.hash.replace('#', '').toLowerCase();
    if (hash === 'yearly') {
      setTab('yearly', { updateHash: false });
    } else {
      setTab('granular', { updateHash: false });
    }
  }

  window.addEventListener('hashchange', handleHashChange);

  function initFilters() {
    loadFiltersFromStorage();
    applyFiltersToInputs();
    ['project', 'source', 'kind'].forEach((key) => {
      const input = filterInputs[key];
      if (!input) return;
      input.addEventListener('change', (event) => {
        updateFilter(key === 'project' ? 'projectId' : key, event.target.value);
      });
    });
    filterInputs.year?.addEventListener('change', (event) => {
      const value = (event.target.value || '').trim();
      if (value && !/^[0-9]{4}$/.test(value)) {
        toast('Year must be a four digit number.', 'warning');
        event.target.value = '';
        updateFilter('year', '');
        return;
      }
      updateFilter('year', value);
    });
    filterInputs.refresh?.addEventListener('click', () => {
      pager.page = 1;
      fetchList();
    });
  }

  function init() {
    initFilters();
    initOverrides();
    handleHashChange();
    fetchList();
  }

  init();
})();
