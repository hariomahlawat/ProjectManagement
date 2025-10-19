(function () {
  const endpoints = {
    overview: '/api/proliferation/overview',
    exportCsv: '/api/proliferation/export',
    createYearly: '/api/proliferation/yearly',
    createGranular: '/api/proliferation/granular',
    setPreference: '/api/proliferation/year-preference',
    importYearly: '/api/proliferation/import/yearly',
    importGranular: '/api/proliferation/import/granular'
  };

  const state = {
    filters: {
      years: [],
      from: null,
      to: null,
      projectCategoryId: null,
      technicalCategoryId: null,
      source: '',
      search: ''
    },
    page: 1,
    pageSize: 25,
    totalCount: 0
  };

  const elements = {};
  const bootstrapComponents = {};

  document.addEventListener('DOMContentLoaded', initialize);

  function initialize() {
    cacheElements();
    instantiateComponents();
    populateYearOptions();
    attachFilterHandlers();
    attachActionButtons();
    registerForms();
    renderSkeletons();
    loadOverview();
  }

  function cacheElements() {
    elements.kpiSection = document.getElementById('kpiSection');
    elements.analyticsSection = document.getElementById('analyticsSection');
    elements.tableBody = document.querySelector('#resultsTable tbody');
    elements.tableContainer = document.getElementById('tableContainer');
    elements.paginationSummary = document.getElementById('paginationSummary');
    elements.paginationControls = document.getElementById('paginationControls');
    elements.paginationContainer = document.getElementById('paginationContainer');
    elements.filterForm = document.getElementById('filterForm');
    elements.toastContainer = document.getElementById('toastContainer');
    elements.kpiLastUpdated = document.getElementById('kpi-last-updated');
    elements.importStatus = document.getElementById('importStatus');
    elements.importFileInput = document.getElementById('import-file');
    elements.importDropzone = document.querySelector('[data-dropzone]');
  }

  function instantiateComponents() {
    const granularOffcanvasEl = document.getElementById('granularOffcanvas');
    const yearlyOffcanvasEl = document.getElementById('yearlyOffcanvas');
    const reconciliationOffcanvasEl = document.getElementById('reconciliationOffcanvas');
    const importModalEl = document.getElementById('importModal');

    bootstrapComponents.granularOffcanvas = granularOffcanvasEl ? new bootstrap.Offcanvas(granularOffcanvasEl) : null;
    bootstrapComponents.yearlyOffcanvas = yearlyOffcanvasEl ? new bootstrap.Offcanvas(yearlyOffcanvasEl) : null;
    bootstrapComponents.reconciliationOffcanvas = reconciliationOffcanvasEl ? new bootstrap.Offcanvas(reconciliationOffcanvasEl) : null;
    bootstrapComponents.importModal = importModalEl ? new bootstrap.Modal(importModalEl) : null;
  }

  function populateYearOptions() {
    const select = document.getElementById('filter-years');
    if (!select) return;

    const currentYear = new Date().getFullYear();
    for (let i = 0; i < 6; i += 1) {
      const year = currentYear - i;
      const option = document.createElement('option');
      option.value = year.toString();
      option.textContent = year.toString();
      select.append(option);
    }
  }

  function attachFilterHandlers() {
    if (!elements.filterForm) return;

    elements.filterForm.addEventListener('submit', (event) => {
      event.preventDefault();
      readFiltersFromForm();
      state.page = 1;
      loadOverview();
    });

    const resetButton = elements.filterForm.querySelector('[data-action="reset-filters"]');
    if (resetButton) {
      resetButton.addEventListener('click', () => {
        resetFilters();
        loadOverview();
      });
    }
  }

  function attachActionButtons() {
    document.querySelectorAll('[data-action="open-granular"]').forEach((button) => {
      button.addEventListener('click', () => {
        resetForm(document.getElementById('granularForm'));
        bootstrapComponents.granularOffcanvas?.show();
      });
    });

    document.querySelectorAll('[data-action="open-yearly"]').forEach((button) => {
      button.addEventListener('click', () => {
        resetForm(document.getElementById('yearlyForm'));
        bootstrapComponents.yearlyOffcanvas?.show();
      });
    });

    document.querySelectorAll('[data-action="open-reconciliation"]').forEach((button) => {
      button.addEventListener('click', () => {
        document.querySelectorAll('#reconciliationOffcanvas form').forEach(resetForm);
        bootstrapComponents.reconciliationOffcanvas?.show();
      });
    });

    document.querySelectorAll('[data-action="open-import"]').forEach((button) => {
      button.addEventListener('click', () => {
        resetForm(document.getElementById('importForm'));
        setImportStatus('');
        elements.importDropzone?.classList.remove('drag-over');
        bootstrapComponents.importModal?.show();
      });
    });

    document.querySelectorAll('[data-action="export"]').forEach((button) => {
      button.addEventListener('click', () => {
        readFiltersFromForm();
        const url = `${endpoints.exportCsv}?${buildQueryString()}`;
        window.open(url, '_blank', 'noopener');
      });
    });

    elements.importDropzone?.addEventListener('click', () => elements.importFileInput?.click());
    elements.importDropzone?.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        elements.importFileInput?.click();
      }
    });

    elements.importDropzone?.addEventListener('dragover', (event) => {
      event.preventDefault();
      elements.importDropzone?.classList.add('drag-over');
    });

    elements.importDropzone?.addEventListener('dragleave', () => {
      elements.importDropzone?.classList.remove('drag-over');
    });

    elements.importDropzone?.addEventListener('drop', (event) => {
      event.preventDefault();
      elements.importDropzone?.classList.remove('drag-over');
      if (elements.importFileInput && event.dataTransfer?.files?.length) {
        elements.importFileInput.files = event.dataTransfer.files;
        setImportStatus(`${event.dataTransfer.files[0].name} ready to upload`);
      }
    });

    elements.importFileInput?.addEventListener('change', () => {
      if (elements.importFileInput?.files?.length) {
        setImportStatus(`${elements.importFileInput.files[0].name} ready to upload`);
      } else {
        setImportStatus('');
      }
    });
  }

  function registerForms() {
    setupJsonForm('yearlyForm', endpoints.createYearly, transformYearlyForm, {
      successMessage: 'Yearly total recorded',
      onSuccess: () => {
        bootstrapComponents.yearlyOffcanvas?.hide();
        loadOverview();
      }
    });

    setupJsonForm('granularForm', endpoints.createGranular, transformGranularForm, {
      successMessage: 'Granular record submitted',
      onSuccess: () => {
        bootstrapComponents.granularOffcanvas?.hide();
        loadOverview();
      }
    });

    document.querySelectorAll('#reconciliationOffcanvas form').forEach((form) => {
      setupJsonForm(form, endpoints.setPreference, transformPreferenceForm, {
        successMessage: null,
        onSuccess: () => {
          showToast('Preference saved', 'success');
          bootstrapComponents.reconciliationOffcanvas?.hide();
          loadOverview();
        }
      });
    });

    setupMultipartForm('importForm', getImportEndpoint, {
      onSuccess: (payload) => {
        const accepted = payload?.accepted ?? payload?.Accepted ?? 0;
        const rejected = payload?.rejected ?? payload?.Rejected ?? 0;
        const message = rejected > 0
          ? `${accepted} rows imported, ${rejected} rejected.`
          : `${accepted} rows imported successfully.`;
        showToast(message, rejected > 0 ? 'warning' : 'success');
        bootstrapComponents.importModal?.hide();
        setImportStatus('');
        loadOverview();
      }
    });
  }

  function renderSkeletons() {
    renderKpiSkeleton();
    renderAnalyticsSkeleton();
    renderTableSkeleton();
  }

  function renderKpiSkeleton() {
    if (!elements.kpiSection) return;
    elements.kpiSection.innerHTML = '';
    elements.kpiSection.setAttribute('aria-busy', 'true');
    for (let i = 0; i < 4; i += 1) {
      const column = document.createElement('div');
      column.className = 'col-12 col-sm-6 col-xl-3';
      const skeleton = document.createElement('div');
      skeleton.className = 'skeleton kpi';
      column.append(skeleton);
      elements.kpiSection.append(column);
    }
  }

  function renderAnalyticsSkeleton() {
    if (!elements.analyticsSection) return;
    elements.analyticsSection.innerHTML = '';
    elements.analyticsSection.setAttribute('aria-busy', 'true');
    for (let i = 0; i < 4; i += 1) {
      const column = document.createElement('div');
      column.className = 'col-12 col-sm-6 col-xl-3';
      const skeleton = document.createElement('div');
      skeleton.className = 'skeleton analytics';
      column.append(skeleton);
      elements.analyticsSection.append(column);
    }
  }

  function renderTableSkeleton() {
    if (!elements.tableBody || !elements.tableContainer) return;
    elements.tableContainer.setAttribute('aria-busy', 'true');
    elements.tableBody.innerHTML = '';
    for (let i = 0; i < 6; i += 1) {
      const row = document.createElement('tr');
      const cell = document.createElement('td');
      cell.colSpan = 10;
      const skeleton = document.createElement('div');
      skeleton.className = 'skeleton table-row';
      cell.append(skeleton);
      row.append(cell);
      elements.tableBody.append(row);
    }
  }

  async function loadOverview() {
    renderKpiSkeleton();
    renderAnalyticsSkeleton();
    renderTableSkeleton();

    try {
      const response = await fetch(`${endpoints.overview}?${buildQueryString()}`, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
      });

      if (!response.ok) {
        throw new Error('Failed to load proliferation overview');
      }

      const payload = await response.json();
      const data = normalizeOverviewResponse(payload);
      state.totalCount = data.totalCount ?? 0;
      renderKpis(data.kpis);
      renderAnalytics(data.kpis);
      renderTable(data.rows);
      renderPagination();
      updateLastUpdated();
    } catch (error) {
      console.error(error);
      showToast('Unable to load proliferation overview. Please try again later.', 'danger');
      renderErrorState();
    }
  }

  function renderKpis(kpis) {
    if (!elements.kpiSection) return;
    const metrics = kpis || {};
    const cards = [
      {
        title: 'Completed projects',
        value: metrics.totalCompletedProjects,
        meta: 'Projects with approved proliferation data'
      },
      {
        title: 'Total proliferation',
        value: metrics.totalProliferationAllTime,
        meta: 'All-time quantity across sources'
      },
      {
        title: 'SDD quantity',
        value: metrics.totalProliferationSdd,
        meta: 'Approved SDD proliferation'
      },
      {
        title: '515 ABW quantity',
        value: metrics.totalProliferationAbw515,
        meta: 'Approved 515 ABW proliferation'
      }
    ];

    elements.kpiSection.setAttribute('aria-busy', 'false');
    elements.kpiSection.innerHTML = cards.map((card) => `
      <div class="col-12 col-sm-6 col-xl-3">
        <article class="kpi-card" role="group" aria-label="${escapeHtml(card.title)}">
          <div class="kpi-card__title">${escapeHtml(card.title)}</div>
          <div class="kpi-card__value">${formatNumber(card.value)}</div>
          <div class="kpi-card__meta">${escapeHtml(card.meta)}</div>
        </article>
      </div>
    `).join('');
  }

  function renderAnalytics(kpis) {
    if (!elements.analyticsSection) return;
    const metrics = kpis || {};
    const cards = [
      {
        label: 'Projects proliferated',
        value: metrics.lastYearProjectsProliferated,
        trend: 'In the last 12 months'
      },
      {
        label: 'Total proliferation',
        value: metrics.lastYearTotalProliferation,
        trend: 'Last 12 months'
      },
      {
        label: 'SDD quantity',
        value: metrics.lastYearSdd,
        trend: 'Last 12 months'
      },
      {
        label: '515 ABW quantity',
        value: metrics.lastYearAbw515,
        trend: 'Last 12 months'
      }
    ];

    elements.analyticsSection.setAttribute('aria-busy', 'false');
    elements.analyticsSection.innerHTML = cards.map((card) => `
      <div class="col-12 col-sm-6 col-xl-3">
        <article class="analytics-card" role="group" aria-label="${escapeHtml(card.label)}">
          <div class="analytics-card__label">${escapeHtml(card.label)}</div>
          <div class="analytics-card__value">${formatNumber(card.value)}</div>
          <div class="analytics-card__trend text-muted">${escapeHtml(card.trend)}</div>
        </article>
      </div>
    `).join('');
  }

  function renderTable(rows) {
    if (!elements.tableBody || !elements.tableContainer) return;

    const items = Array.isArray(rows) ? rows : [];
    elements.tableContainer.setAttribute('aria-busy', 'false');

    if (items.length === 0) {
      elements.tableBody.innerHTML = `
        <tr>
          <td colspan="10" class="text-center py-5 text-muted">
            <i class="bi bi-clipboard-data mb-2" aria-hidden="true"></i>
            <div>No records match the selected filters.</div>
          </td>
        </tr>
      `;
      return;
    }

    elements.tableBody.innerHTML = items.map((row) => {
      const projectCode = row.projectCode ? ` <span class="text-muted">(${escapeHtml(row.projectCode)})</span>` : '';
      const dateDisplay = row.proliferationDate || row.dateUtc ? formatDate(row.proliferationDate || row.dateUtc) : '—';
      const mode = row.mode || '—';
      return `
        <tr>
          <td>${escapeHtml(row.year)}</td>
          <td>${escapeHtml(row.project || row.projectName || '')}${projectCode}</td>
          <td>${escapeHtml(row.source)}</td>
          <td>${escapeHtml(row.dataType)}</td>
          <td>${escapeHtml(row.unitName || '—')}</td>
          <td>${escapeHtml(row.simulatorName || '—')}</td>
          <td>${escapeHtml(dateDisplay)}</td>
          <td class="text-end">${formatNumber(row.quantity)}</td>
          <td>${escapeHtml(row.approvalStatus || '—')}</td>
          <td>${escapeHtml(mode)}</td>
        </tr>
      `;
    }).join('');
  }

  function renderPagination() {
    if (!elements.paginationControls || !elements.paginationSummary) return;

    const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
    if (state.page > totalPages) {
      state.page = totalPages;
    }

    elements.paginationSummary.textContent = state.totalCount === 0
      ? 'No results to display'
      : `Showing page ${state.page} of ${totalPages} (${state.totalCount} records)`;

    elements.paginationContainer.hidden = state.totalCount === 0;
    elements.paginationControls.innerHTML = '';

    if (state.totalCount === 0) {
      return;
    }

    const createItem = (label, page, disabled = false, active = false) => {
      const li = document.createElement('li');
      li.className = `page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}`;
      const link = document.createElement('button');
      link.type = 'button';
      link.className = 'page-link';
      link.textContent = label;
      link.disabled = disabled;
      if (!disabled) {
        link.addEventListener('click', () => {
          state.page = page;
          loadOverview();
        });
      }
      li.append(link);
      return li;
    };

    elements.paginationControls.append(createItem('Prev', Math.max(1, state.page - 1), state.page === 1));

    const windowSize = 5;
    const start = Math.max(1, state.page - Math.floor(windowSize / 2));
    const end = Math.min(totalPages, start + windowSize - 1);
    for (let page = start; page <= end; page += 1) {
      elements.paginationControls.append(createItem(page.toString(), page, false, page === state.page));
    }

    elements.paginationControls.append(createItem('Next', Math.min(totalPages, state.page + 1), state.page === totalPages));
  }

  function renderErrorState() {
    if (elements.kpiSection) {
      elements.kpiSection.innerHTML = '';
      elements.kpiSection.setAttribute('aria-busy', 'false');
    }

    if (elements.analyticsSection) {
      elements.analyticsSection.innerHTML = '';
      elements.analyticsSection.setAttribute('aria-busy', 'false');
    }

    if (elements.tableBody) {
      elements.tableBody.innerHTML = `
        <tr>
          <td colspan="10" class="text-center py-5 text-muted">
            Unable to load proliferation overview. Please try again later.
          </td>
        </tr>
      `;
    }
    if (elements.tableContainer) {
      elements.tableContainer.setAttribute('aria-busy', 'false');
    }
    if (elements.paginationContainer) {
      elements.paginationContainer.hidden = true;
    }
  }

  function readFiltersFromForm() {
    if (!elements.filterForm) return;
    const data = new FormData(elements.filterForm);

    const years = Array.from(elements.filterForm.querySelectorAll('[data-filter="years"] option:checked'))
      .map((option) => parseInt(option.value, 10))
      .filter((value) => Number.isInteger(value));

    state.filters.years = years;

    const fromInput = elements.filterForm.querySelector('[data-filter="from"]');
    const toInput = elements.filterForm.querySelector('[data-filter="to"]');
    const projectCategoryInput = elements.filterForm.querySelector('[data-filter="project-category"]');
    const technicalCategoryInput = elements.filterForm.querySelector('[data-filter="technical-category"]');
    const sourceInput = elements.filterForm.querySelector('[data-filter="source"]');
    const searchInput = elements.filterForm.querySelector('[data-filter="search"]');

    state.filters.from = (data.get('from') || fromInput?.value || '').toString();
    state.filters.to = (data.get('to') || toInput?.value || '').toString();

    const projectCategory = (data.get('projectCategory') || projectCategoryInput?.value || '').toString();
    const technicalCategory = (data.get('technicalCategory') || technicalCategoryInput?.value || '').toString();

    state.filters.projectCategoryId = projectCategory ? parseInt(projectCategory, 10) : null;
    state.filters.technicalCategoryId = technicalCategory ? parseInt(technicalCategory, 10) : null;
    state.filters.source = (data.get('source') || sourceInput?.value || '').toString();
    state.filters.search = (data.get('search') || searchInput?.value || '').toString().trim();
  }

  function resetFilters() {
    if (!elements.filterForm) return;
    elements.filterForm.reset();
    elements.filterForm.querySelectorAll('select[multiple] option').forEach((option) => {
      option.selected = false;
    });
    state.filters = {
      years: [],
      from: null,
      to: null,
      projectCategoryId: null,
      technicalCategoryId: null,
      source: '',
      search: ''
    };
    state.page = 1;
  }

  function buildQueryString() {
    const query = new URLSearchParams();

    state.filters.years.forEach((year) => {
      query.append('Years', year.toString());
    });

    if (state.filters.from) {
      query.set('FromDateUtc', toIsoDate(state.filters.from));
    }

    if (state.filters.to) {
      query.set('ToDateUtc', toIsoDate(state.filters.to));
    }

    if (Number.isInteger(state.filters.projectCategoryId)) {
      query.set('ProjectCategoryId', state.filters.projectCategoryId.toString());
    }

    if (Number.isInteger(state.filters.technicalCategoryId)) {
      query.set('TechnicalCategoryId', state.filters.technicalCategoryId.toString());
    }

    if (state.filters.source) {
      query.set('Source', state.filters.source);
    }

    if (state.filters.search) {
      query.set('Search', state.filters.search);
    }

    query.set('Page', state.page.toString());
    query.set('PageSize', state.pageSize.toString());

    return query.toString();
  }

  function setupJsonForm(formIdOrElement, endpoint, transform, options = {}) {
    const form = typeof formIdOrElement === 'string'
      ? document.getElementById(formIdOrElement)
      : formIdOrElement;

    if (!form) return;

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!form.checkValidity()) {
        form.classList.add('was-validated');
        return;
      }

      const submitButton = form.querySelector('[data-submit]');
      toggleButtonLoading(submitButton, true);

      try {
        const payload = transform(new FormData(form));
        const response = await fetch(endpoint, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest'
          },
          body: JSON.stringify(payload)
        });

        if (!response.ok) {
          const error = await response.json().catch(() => ({}));
          throw new Error(error?.error || error?.message || 'Request failed');
        }

        if (options.successMessage !== null) {
          const message = options.successMessage || 'Saved successfully';
          showToast(message, 'success');
        }
        options.onSuccess?.();
        resetForm(form);
      } catch (error) {
        console.error(error);
        showToast(error.message || 'Unable to save changes', 'danger');
      } finally {
        toggleButtonLoading(submitButton, false);
      }
    });
  }

  function setupMultipartForm(formId, endpointFactory, options = {}) {
    const form = document.getElementById(formId);
    if (!form) return;

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!form.checkValidity()) {
        form.classList.add('was-validated');
        return;
      }

      const submitButton = form.querySelector('[data-submit]');
      toggleButtonLoading(submitButton, true);

      try {
        const formData = new FormData(form);
        const endpoint = endpointFactory(formData);
        const response = await fetch(endpoint, {
          method: 'POST',
          body: formData
        });

        if (!response.ok) {
          const error = await response.json().catch(() => ({}));
          const detail = error?.errors
            ? error.errors.map((x) => `Row ${x.rowNumber}: ${x.message}`).join('\n')
            : error?.message;
          throw new Error(detail || 'Import failed');
        }

        const payload = await response.json().catch(() => ({}));
        options.onSuccess?.(payload);
        resetForm(form);
      } catch (error) {
        console.error(error);
        showToast(error.message || 'Import failed', 'danger');
        setImportStatus(error.message || 'Import failed');
      } finally {
        toggleButtonLoading(submitButton, false);
      }
    });
  }

  function transformYearlyForm(formData) {
    return {
      projectId: Number(formData.get('projectId')),
      source: formData.get('source'),
      year: Number(formData.get('year')),
      totalQuantity: Number(formData.get('totalQuantity')),
      remarks: formData.get('remarks') || null
    };
  }

  function transformGranularForm(formData) {
    const date = formData.get('proliferationDate');
    return {
      projectId: Number(formData.get('projectId')),
      simulatorName: formData.get('simulatorName'),
      unitName: formData.get('unitName'),
      proliferationDateUtc: date ? `${date}T00:00:00Z` : null,
      quantity: Number(formData.get('quantity')),
      remarks: formData.get('remarks') || null
    };
  }

  function transformPreferenceForm(formData) {
    return {
      projectId: Number(formData.get('projectId')),
      source: formData.get('source'),
      year: Number(formData.get('year')),
      mode: formData.get('mode')
    };
  }

  function getImportEndpoint(formData) {
    const type = formData.get('importType');
    return type === 'granular' ? endpoints.importGranular : endpoints.importYearly;
  }

  function toggleButtonLoading(button, loading) {
    if (!button) return;
    const spinner = button.querySelector('.spinner-border');
    if (spinner) {
      spinner.hidden = !loading;
    }
    button.disabled = loading;
  }

  function resetForm(form) {
    if (!form) return;
    form.reset();
    form.classList.remove('was-validated');
    form.querySelectorAll('.spinner-border').forEach((spinner) => {
      spinner.hidden = true;
    });
  }

  function setImportStatus(message) {
    if (elements.importStatus) {
      elements.importStatus.textContent = message;
    }
  }

  function showToast(message, variant = 'success') {
    if (!elements.toastContainer) return;
    const toastEl = document.createElement('div');
    toastEl.className = `toast align-items-center text-bg-${variant}`;
    toastEl.setAttribute('role', 'status');
    toastEl.setAttribute('aria-live', 'polite');
    toastEl.setAttribute('aria-atomic', 'true');
    toastEl.innerHTML = `
      <div class="d-flex">
        <div class="toast-body">${escapeHtml(message)}</div>
        <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
      </div>
    `;

    elements.toastContainer.append(toastEl);
    const toast = new bootstrap.Toast(toastEl, { delay: 4000, autohide: true });
    toast.show();
    toastEl.addEventListener('hidden.bs.toast', () => toastEl.remove());
  }

  function normalizeOverviewResponse(payload) {
    if (!payload || typeof payload !== 'object') {
      return { kpis: {}, rows: [], totalCount: 0 };
    }

    const kpis = payload.Kpis || payload.kpis || {};
    const rows = payload.Rows || payload.rows || [];
    const totalCount = payload.TotalCount ?? payload.totalCount ?? 0;

    return { kpis, rows, totalCount };
  }

  function formatNumber(value) {
    const number = typeof value === 'number' ? value : Number(value);
    if (Number.isNaN(number)) {
      return '0';
    }
    return new Intl.NumberFormat().format(number);
  }

  function formatDate(value) {
    if (!value) return '—';
    if (value instanceof Date) {
      return value.toISOString().slice(0, 10);
    }
    const date = new Date(value);
    if (!Number.isNaN(date.getTime())) {
      return date.toISOString().slice(0, 10);
    }
    return value;
  }

  function escapeHtml(value) {
    return (value ?? '').toString()
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function toIsoDate(value) {
    if (!value) return '';
    if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      return `${value}T00:00:00Z`;
    }
    const date = new Date(value);
    if (!Number.isNaN(date.getTime())) {
      return date.toISOString();
    }
    return value;
  }

  function updateLastUpdated() {
    if (!elements.kpiLastUpdated) return;
    elements.kpiLastUpdated.textContent = `Updated ${new Date().toLocaleString()}`;
  }
})();
