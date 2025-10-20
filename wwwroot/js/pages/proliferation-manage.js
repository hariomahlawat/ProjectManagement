/* global bootstrap */
(() => {
  const listCard = document.querySelector('[data-page-size][data-default-year]');
  const editorCard = document.querySelector('#pf-editor');
  if (!listCard || !editorCard) {
    return;
  }

  const storageKey = 'proliferation-manage-filters';
  const api = {
    list: '/api/proliferation/list',
    yearly: (id) => `/api/proliferation/yearly/${id}`,
    granular: (id) => `/api/proliferation/granular/${id}`,
    saveYearly: (id) => (id ? `/api/proliferation/yearly/${id}` : '/api/proliferation/yearly'),
    saveGranular: (id) => (id ? `/api/proliferation/granular/${id}` : '/api/proliferation/granular'),
    deleteYearly: (id, rowVersion) => `/api/proliferation/yearly/${id}?rowVersion=${encodeURIComponent(rowVersion)}`,
    deleteGranular: (id, rowVersion) => `/api/proliferation/granular/${id}?rowVersion=${encodeURIComponent(rowVersion)}`
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
    simulator: document.querySelector('#pf-simulator'),
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
    if (editor.simulator) editor.simulator.required = isGranular;
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
      editor.simulator.value = detail.simulatorName ?? '';
      editor.unit.value = detail.unitName ?? '';
      editor.qty.value = detail.quantity ?? '';
      setTab('granular');
    } else {
      editor.date.value = '';
      editor.simulator.value = '';
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
    const simulator = editor.simulator?.value?.trim();
    const unit = editor.unit?.value?.trim();
    const quantity = Number(editor.qty?.value ?? '');
    if (!simulator || !unit) return null;
    if (!Number.isFinite(quantity) || quantity <= 0) return null;

    const payload = {
      projectId,
      source,
      simulatorName: simulator,
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
    const confirmed = window.confirm('Are you sure you want to delete this entry?');
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
    if (editor.simulator) editor.simulator.value = '';
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
    handleHashChange();
    fetchList();
  }

  init();
})();
