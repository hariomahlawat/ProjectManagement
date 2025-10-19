(function () {
  const api = {
    overview: '/api/proliferation/overview',
    createYearly: '/api/proliferation/yearly',
    createGranular: '/api/proliferation/granular',
    importYearly: '/api/proliferation/import/yearly',
    importGranular: '/api/proliferation/import/granular',
    exportCsv: '/api/proliferation/export',
    setPreference: '/api/proliferation/year-preference'
  };

  const state = {
    filters: {
      years: [],
      dateFrom: null,
      dateTo: null,
      projectCategoryId: null,
      technicalCategoryId: null,
      source: '',
      search: '',
      page: 1,
      pageSize: 25
    }
  };

  document.addEventListener('DOMContentLoaded', () => {
    initializeFilters();
    attachActions();
    loadOverview();
  });

  function initializeFilters() {
    const yearSelect = document.getElementById('filter-year');
    if (yearSelect && yearSelect.options.length === 0) {
      const currentYear = new Date().getFullYear();
      for (let i = 0; i < 6; i += 1) {
        const option = document.createElement('option');
        option.value = (currentYear - i).toString();
        option.textContent = (currentYear - i).toString();
        yearSelect.append(option);
      }
    }

    const dateRangeInput = document.getElementById('filter-daterange');
    if (dateRangeInput) {
      dateRangeInput.placeholder = 'YYYY-MM-DD to YYYY-MM-DD';
    }

    const sourceSelect = document.getElementById('filter-source');
    if (sourceSelect) {
      sourceSelect.addEventListener('change', () => {
        state.filters.source = sourceSelect.value || '';
      });
    }
  }

  function attachActions() {
    const applyButton = document.getElementById('btn-apply');
    if (applyButton) {
      applyButton.addEventListener('click', () => {
        readFiltersFromInputs();
        loadOverview();
      });
    }

    const resetButton = document.getElementById('btn-reset');
    if (resetButton) {
      resetButton.addEventListener('click', () => {
        resetFilters();
        loadOverview();
      });
    }

    const addYearlyButton = document.getElementById('btn-add-yearly');
    if (addYearlyButton) {
      addYearlyButton.addEventListener('click', () => openYearlyModal());
    }

    const addGranularButton = document.getElementById('btn-add-granular');
    if (addGranularButton) {
      addGranularButton.addEventListener('click', () => openGranularModal());
    }

    const importYearlyButton = document.getElementById('btn-import-yearly');
    if (importYearlyButton) {
      importYearlyButton.addEventListener('click', () => triggerImport(api.importYearly));
    }

    const importGranularButton = document.getElementById('btn-import-granular');
    if (importGranularButton) {
      importGranularButton.addEventListener('click', () => triggerImport(api.importGranular));
    }

    const exportButton = document.getElementById('btn-export');
    if (exportButton) {
      exportButton.addEventListener('click', () => {
        readFiltersFromInputs();
        const url = buildUrl(api.exportCsv, buildQuery());
        window.open(url, '_blank');
      });
    }

    const reconcileButton = document.getElementById('btn-reconcile');
    if (reconcileButton) {
      reconcileButton.addEventListener('click', () => openPreferenceDrawer());
    }
  }

  function readFiltersFromInputs() {
    const yearSelect = document.getElementById('filter-year');
    const dateRangeInput = document.getElementById('filter-daterange');
    const projectCategorySelect = document.getElementById('filter-project-category');
    const technicalCategorySelect = document.getElementById('filter-technical-category');
    const sourceSelect = document.getElementById('filter-source');
    const searchInput = document.getElementById('filter-search');

    state.filters.years = [];
    if (yearSelect) {
      const values = Array.from(yearSelect.selectedOptions).map((o) => o.value).filter(Boolean);
      if (values.length > 0) {
        state.filters.years = values.map((v) => parseInt(v, 10)).filter((v) => !Number.isNaN(v));
      }
    }

    state.filters.dateFrom = null;
    state.filters.dateTo = null;
    if (dateRangeInput && dateRangeInput.value) {
      const parts = dateRangeInput.value.split('to').map((p) => p.trim()).filter(Boolean);
      if (parts.length === 2) {
        state.filters.dateFrom = parts[0];
        state.filters.dateTo = parts[1];
      }
    }

    state.filters.projectCategoryId = projectCategorySelect && projectCategorySelect.value ? parseInt(projectCategorySelect.value, 10) : null;
    state.filters.technicalCategoryId = technicalCategorySelect && technicalCategorySelect.value ? parseInt(technicalCategorySelect.value, 10) : null;
    state.filters.source = sourceSelect && sourceSelect.value ? sourceSelect.value : '';
    state.filters.search = searchInput && searchInput.value ? searchInput.value.trim() : '';
  }

  function resetFilters() {
    state.filters.years = [];
    state.filters.dateFrom = null;
    state.filters.dateTo = null;
    state.filters.projectCategoryId = null;
    state.filters.technicalCategoryId = null;
    state.filters.source = '';
    state.filters.search = '';

    const inputs = ['filter-year', 'filter-daterange', 'filter-project-category', 'filter-technical-category', 'filter-source', 'filter-search'];
    inputs.forEach((id) => {
      const element = document.getElementById(id);
      if (element && 'value' in element) {
        element.value = '';
      }
      if (element && element instanceof HTMLSelectElement) {
        Array.from(element.options).forEach((opt) => {
          opt.selected = false;
        });
      }
    });
  }

  function buildQuery() {
    const query = new URLSearchParams();

    state.filters.years.forEach((year) => query.append('years', year.toString()));

    if (state.filters.dateFrom) {
      query.set('dateFrom', state.filters.dateFrom);
    }

    if (state.filters.dateTo) {
      query.set('dateTo', state.filters.dateTo);
    }

    if (state.filters.projectCategoryId) {
      query.set('projectCategoryId', state.filters.projectCategoryId.toString());
    }

    if (state.filters.technicalCategoryId) {
      query.set('technicalCategoryId', state.filters.technicalCategoryId.toString());
    }

    if (state.filters.source) {
      query.set('source', state.filters.source);
    }

    if (state.filters.search) {
      query.set('search', state.filters.search);
    }

    query.set('page', state.filters.page.toString());
    query.set('pageSize', state.filters.pageSize.toString());
    return query;
  }

  async function loadOverview() {
    try {
      const url = buildUrl(api.overview, buildQuery());
      const response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
      if (!response.ok) {
        throw new Error('Failed to load overview');
      }

      const data = await response.json();
      const overview = normalizeOverviewResponse(data);
      renderKpis(overview.kpis);
      renderTable(overview.rows);
    } catch (error) {
      console.error(error);
      renderError();
    }
  }

  function normalizeOverviewResponse(payload) {
    const summary = payload?.summary ?? payload?.Summary ?? null;
    const grid = payload?.grid ?? payload?.Grid ?? null;
    const kpisSource = payload?.Kpis ?? payload?.kpis ?? null;

    const totals = summary ? summary.totals ?? summary.Totals ?? null : null;
    const lastYear = summary ? summary.lastTwelveMonths ?? summary.LastTwelveMonths ?? null : null;

    const selectNumber = (...candidates) => {
      for (const candidate of candidates) {
        if (typeof candidate === 'number' && Number.isFinite(candidate)) {
          return candidate;
        }
        if (typeof candidate === 'string' && candidate.trim() !== '') {
          const parsed = Number(candidate);
          if (!Number.isNaN(parsed)) {
            return parsed;
          }
        }
      }
      return 0;
    };

    const kpis = {
      totalCompletedProjects: selectNumber(
        kpisSource?.TotalCompletedProjects,
        kpisSource?.totalCompletedProjects,
        totals?.projects,
        totals?.Projects
      ),
      totalProliferationAllTime: selectNumber(
        kpisSource?.TotalProliferationAllTime,
        kpisSource?.totalProliferationAllTime,
        totals?.totalQuantity,
        totals?.TotalQuantity
      ),
      totalProliferationSdd: selectNumber(
        kpisSource?.TotalProliferationSdd,
        kpisSource?.totalProliferationSdd,
        totals?.sddQuantity,
        totals?.SddQuantity
      ),
      totalProliferationAbw515: selectNumber(
        kpisSource?.TotalProliferationAbw515,
        kpisSource?.totalProliferationAbw515,
        totals?.abw515Quantity,
        totals?.Abw515Quantity
      ),
      lastYearProjectsProliferated: selectNumber(
        kpisSource?.LastYearProjectsProliferated,
        kpisSource?.lastYearProjectsProliferated,
        lastYear?.projects,
        lastYear?.Projects
      ),
      lastYearTotalProliferation: selectNumber(
        kpisSource?.LastYearTotalProliferation,
        kpisSource?.lastYearTotalProliferation,
        lastYear?.totalQuantity,
        lastYear?.TotalQuantity
      ),
      lastYearSdd: selectNumber(
        kpisSource?.LastYearSdd,
        kpisSource?.lastYearSdd,
        lastYear?.sddQuantity,
        lastYear?.SddQuantity
      ),
      lastYearAbw515: selectNumber(
        kpisSource?.LastYearAbw515,
        kpisSource?.lastYearAbw515,
        lastYear?.abw515Quantity,
        lastYear?.Abw515Quantity
      )
    };

    let rows = payload?.Rows ?? payload?.rows ?? null;
    if (!Array.isArray(rows)) {
      if (rows && typeof rows === 'object') {
        const items = rows.items ?? rows.Items;
        rows = Array.isArray(items) ? items : [];
      } else if (grid) {
        if (Array.isArray(grid)) {
          rows = grid;
        } else if (typeof grid === 'object') {
          const items = grid.items ?? grid.Items;
          rows = Array.isArray(items) ? items : [];
        } else {
          rows = [];
        }
      } else {
        rows = [];
      }
    }

    return {
      kpis,
      rows: Array.isArray(rows) ? rows : [],
      totalCount: selectNumber(
        payload?.TotalCount,
        payload?.totalCount,
        grid?.totalCount,
        grid?.TotalCount
      )
    };
  }

  function renderKpis(kpis) {
    const metrics = kpis ?? {
      totalCompletedProjects: 0,
      totalProliferationAllTime: 0,
      totalProliferationSdd: 0,
      totalProliferationAbw515: 0,
      lastYearProjectsProliferated: 0,
      lastYearTotalProliferation: 0,
      lastYearSdd: 0,
      lastYearAbw515: 0
    };

    setKpi('kpi-total-completed', metrics.totalCompletedProjects, 'Projects with data');
    setKpi('kpi-total-prolif', metrics.totalProliferationAllTime, 'Total quantity');
    setKpi('kpi-total-sdd', metrics.totalProliferationSdd, 'SDD quantity');
    setKpi('kpi-total-abw', metrics.totalProliferationAbw515, '515 ABW quantity');

    setKpi('kpi-lastyear-projects', metrics.lastYearProjectsProliferated, 'Projects (last year)');
    setKpi('kpi-lastyear-total', metrics.lastYearTotalProliferation, 'Total (last year)');
    setKpi('kpi-lastyear-sdd', metrics.lastYearSdd, 'SDD (last year)');
    setKpi('kpi-lastyear-abw', metrics.lastYearAbw515, '515 ABW (last year)');
  }

  function setKpi(elementId, value, label) {
    const element = document.getElementById(elementId);
    if (!element) {
      return;
    }

    element.innerHTML = '';
    const valueElement = document.createElement('div');
    valueElement.className = 'kpi-value';
    valueElement.textContent = (value ?? 0).toString();

    const labelElement = document.createElement('div');
    labelElement.className = 'kpi-label';
    labelElement.textContent = label;

    element.append(valueElement, labelElement);
  }

  function resolveProjectDisplay(row) {
    const name = row.projectName ?? row.ProjectName ?? row.project ?? row.Project ?? '';
    const code = row.projectCode ?? row.ProjectCode ?? '';
    return code ? `${name} (${code})` : name;
  }

  function formatDate(value) {
    if (value == null || value === '') {
      return '—';
    }

    if (value instanceof Date && !Number.isNaN(value.getTime())) {
      return value.toISOString().slice(0, 10);
    }

    if (typeof value === 'string') {
      const trimmed = value.trim();
      if (trimmed === '') {
        return '—';
      }

      if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
        return trimmed;
      }

      const parsed = new Date(trimmed);
      if (!Number.isNaN(parsed.getTime())) {
        return parsed.toISOString().slice(0, 10);
      }

      return trimmed;
    }

    return value.toString();
  }

  function renderTable(rows) {
    const table = document.getElementById('results');
    if (!table) {
      return;
    }

    table.innerHTML = '';
    const thead = document.createElement('thead');
    const headerRow = document.createElement('tr');
    ['Year', 'Project', 'Source', 'Data type', 'Unit', 'Simulator', 'Date', 'Quantity', 'Status', 'Mode'].forEach((header) => {
      const th = document.createElement('th');
      th.textContent = header;
      headerRow.append(th);
    });
    thead.append(headerRow);

    const tbody = document.createElement('tbody');
    const items = Array.isArray(rows)
      ? rows
      : Array.isArray(rows?.items)
        ? rows.items
        : Array.isArray(rows?.Items)
          ? rows.Items
          : [];
    items.forEach((row) => {
      const tr = document.createElement('tr');
      const projectCell = resolveProjectDisplay(row);
      const source = row.source ?? row.Source ?? '';
      const dataType = row.dataType ?? row.DataType ?? '';
      const unitName = row.unitName ?? row.UnitName ?? '—';
      const simulatorName = row.simulatorName ?? row.SimulatorName ?? '—';
      const rawDate = row.proliferationDate ?? row.ProliferationDate ?? row.dateUtc ?? row.DateUtc ?? null;
      const proliferationDate = formatDate(rawDate);
      const quantity = row.quantity ?? row.Quantity ?? 0;
      const approvalStatus = row.approvalStatus ?? row.ApprovalStatus ?? '';
      const mode = row.mode ?? row.Mode ?? '—';
      const year = row.year ?? row.Year ?? '';

      const cells = [
        year,
        projectCell,
        source,
        dataType,
        unitName,
        simulatorName,
        proliferationDate,
        quantity,
        approvalStatus,
        mode
      ];

      cells.forEach((value) => {
        const td = document.createElement('td');
        td.textContent = value != null ? value.toString() : '';
        tr.append(td);
      });

      tbody.append(tr);
    });

    table.append(thead, tbody);
  }

  function renderError() {
    const table = document.getElementById('results');
    if (!table) {
      return;
    }

    table.innerHTML = '';
    const tbody = document.createElement('tbody');
    const row = document.createElement('tr');
    const cell = document.createElement('td');
    cell.colSpan = 10;
    cell.textContent = 'Unable to load proliferation overview. Please try again later.';
    row.append(cell);
    tbody.append(row);
    table.append(tbody);
  }

  function buildUrl(base, params) {
    const query = params.toString();
    return query ? `${base}?${query}` : base;
  }

  function openYearlyModal() {
    const container = ensureModalHost();
    container.innerHTML = '';

    const form = document.createElement('form');
    form.className = 'modal';
    form.innerHTML = `
      <h2>Add yearly entry</h2>
      <label>Project ID <input type="number" name="projectId" required /></label>
      <label>Source
        <select name="source">
          <option value="Sdd">SDD</option>
          <option value="Abw515">515 ABW</option>
        </select>
      </label>
      <label>Year <input type="number" name="year" required min="2000" /></label>
      <label>Total quantity <input type="number" name="totalQuantity" required min="0" /></label>
      <label>Remarks <textarea name="remarks" maxlength="500"></textarea></label>
      <div class="modal-actions">
        <button type="submit">Save</button>
        <button type="button" data-close>Cancel</button>
      </div>
    `;

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const formData = new FormData(form);
      const payload = {
        projectId: Number(formData.get('projectId')),
        source: formData.get('source'),
        year: Number(formData.get('year')),
        totalQuantity: Number(formData.get('totalQuantity')),
        remarks: formData.get('remarks') || null
      };

      try {
        const response = await fetch(api.createYearly, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
          body: JSON.stringify(payload)
        });

        if (!response.ok) {
          const error = await response.json().catch(() => ({}));
          throw new Error(error.message || 'Failed to create yearly entry');
        }

        closeModal();
        loadOverview();
      } catch (error) {
        alert(error.message);
      }
    });

    form.querySelector('[data-close]').addEventListener('click', closeModal);

    container.append(form);
    container.removeAttribute('aria-hidden');
  }

  function openGranularModal() {
    const container = ensureModalHost();
    container.innerHTML = '';

    const form = document.createElement('form');
    form.className = 'modal';
    form.innerHTML = `
      <h2>Add granular entry</h2>
      <label>Project ID <input type="number" name="projectId" required /></label>
      <label>Simulator name <input type="text" name="simulatorName" required maxlength="200" /></label>
      <label>Unit name <input type="text" name="unitName" required maxlength="200" /></label>
      <label>Proliferation date <input type="date" name="proliferationDate" required /></label>
      <label>Quantity <input type="number" name="quantity" required min="0" /></label>
      <label>Remarks <textarea name="remarks" maxlength="500"></textarea></label>
      <div class="modal-actions">
        <button type="submit">Save</button>
        <button type="button" data-close>Cancel</button>
      </div>
    `;

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const formData = new FormData(form);
      const payload = {
        projectId: Number(formData.get('projectId')),
        simulatorName: formData.get('simulatorName'),
        unitName: formData.get('unitName'),
        proliferationDate: formData.get('proliferationDate'),
        quantity: Number(formData.get('quantity')),
        remarks: formData.get('remarks') || null
      };

      try {
        const response = await fetch(api.createGranular, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
          body: JSON.stringify(payload)
        });

        if (!response.ok) {
          const error = await response.json().catch(() => ({}));
          throw new Error(error.message || 'Failed to create granular entry');
        }

        closeModal();
        loadOverview();
      } catch (error) {
        alert(error.message);
      }
    });

    form.querySelector('[data-close]').addEventListener('click', closeModal);

    container.append(form);
    container.removeAttribute('aria-hidden');
  }

  function openPreferenceDrawer() {
    const drawer = ensureDrawerHost();
    drawer.innerHTML = '';
    const form = document.createElement('form');
    form.className = 'drawer';
    form.innerHTML = `
      <h2>Set year preference</h2>
      <label>Project ID <input type="number" name="projectId" required /></label>
      <label>Source
        <select name="source">
          <option value="Sdd">SDD</option>
          <option value="Abw515">515 ABW</option>
        </select>
      </label>
      <label>Year <input type="number" name="year" required min="2000" /></label>
      <label>Mode
        <select name="mode">
          <option value="Auto">Auto</option>
          <option value="UseYearly">UseYearly</option>
          <option value="UseGranular">UseGranular</option>
        </select>
      </label>
      <div class="drawer-actions">
        <button type="submit">Save preference</button>
        <button type="button" data-close>Close</button>
      </div>
    `;

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const formData = new FormData(form);
      const payload = {
        projectId: Number(formData.get('projectId')),
        source: formData.get('source'),
        year: Number(formData.get('year')),
        mode: formData.get('mode')
      };

      try {
        const response = await fetch(api.setPreference, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
          body: JSON.stringify(payload)
        });

        if (!response.ok) {
          const error = await response.json().catch(() => ({}));
          throw new Error(error.message || 'Failed to update preference');
        }

        alert('Preference saved');
        closeDrawer();
        loadOverview();
      } catch (error) {
        alert(error.message);
      }
    });

    form.querySelector('[data-close]').addEventListener('click', closeDrawer);

    drawer.append(form);
    drawer.removeAttribute('aria-hidden');
  }

  function ensureModalHost() {
    const host = document.getElementById('modal-host');
    host.classList.add('modal-visible');
    return host;
  }

  function ensureDrawerHost() {
    const drawer = document.getElementById('drawer-host');
    drawer.classList.add('drawer-visible');
    return drawer;
  }

  function closeModal() {
    const host = document.getElementById('modal-host');
    if (host) {
      host.innerHTML = '';
      host.classList.remove('modal-visible');
      host.setAttribute('aria-hidden', 'true');
    }
  }

  function closeDrawer() {
    const drawer = document.getElementById('drawer-host');
    if (drawer) {
      drawer.innerHTML = '';
      drawer.classList.remove('drawer-visible');
      drawer.setAttribute('aria-hidden', 'true');
    }
  }

  function triggerImport(endpoint) {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.csv';
    input.addEventListener('change', async () => {
      if (!input.files || input.files.length === 0) {
        return;
      }

      const formData = new FormData();
      formData.append('file', input.files[0]);

      try {
        const response = await fetch(endpoint, {
          method: 'POST',
          body: formData
        });

        if (!response.ok) {
          const error = await response.json().catch(() => ({}));
          const messages = error.errors ? error.errors.map((e) => `Row ${e.rowNumber}: ${e.message}`).join('\n') : error.message;
          throw new Error(messages || 'Import failed');
        }

        alert('Import completed successfully.');
        loadOverview();
      } catch (error) {
        alert(error.message);
      }
    });

    input.click();
  }
})();
