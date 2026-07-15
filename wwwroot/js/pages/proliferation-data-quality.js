/* global bootstrap */
(() => {
  const root = document.querySelector('[data-page="proliferation-manage"]');
  const card = document.querySelector('#pf-data-quality-card');
  if (!root || !card) return;

  const el = {
    body: document.querySelector('#pf-quality-body'),
    type: document.querySelector('#pf-quality-type'),
    search: document.querySelector('#pf-quality-search'),
    clear: document.querySelector('#pf-quality-clear'),
    refresh: document.querySelector('#pf-quality-refresh'),
    range: document.querySelector('#pf-quality-range'),
    prev: document.querySelector('#pf-quality-prev'),
    next: document.querySelector('#pf-quality-next'),
    total: document.querySelector('#pf-quality-total'),
    invalidDate: document.querySelector('#pf-quality-invalid-date'),
    missingUnit: document.querySelector('#pf-quality-missing-unit'),
    invalidQuantity: document.querySelector('#pf-quality-invalid-quantity'),
    duplicates: document.querySelector('#pf-quality-duplicates'),
    modal: document.querySelector('#pf-quality-correct-modal'),
    modalContext: document.querySelector('#pf-quality-correct-context'),
    currentValue: document.querySelector('#pf-quality-current-value'),
    yearWrap: document.querySelector('#pf-quality-year-wrap'),
    dateWrap: document.querySelector('#pf-quality-date-wrap'),
    unitWrap: document.querySelector('#pf-quality-unit-wrap'),
    quantityWrap: document.querySelector('#pf-quality-quantity-wrap'),
    year: document.querySelector('#pf-quality-correct-year'),
    date: document.querySelector('#pf-quality-correct-date'),
    unit: document.querySelector('#pf-quality-correct-unit'),
    quantity: document.querySelector('#pf-quality-correct-quantity'),
    reason: document.querySelector('#pf-quality-correct-reason'),
    error: document.querySelector('#pf-quality-correct-error'),
    save: document.querySelector('#pf-quality-correct-save'),
    projectContext: document.querySelector('#pf-quality-project-context'),
    clearProject: document.querySelector('#pf-quality-clear-project')
  };

  const state = {
    page: 1,
    pageSize: 25,
    total: 0,
    issueType: '',
    search: '',
    projectId: Number(new URL(window.location.href).searchParams.get('projectId')) || null,
    items: [],
    selected: null,
    loaded: false,
    sequence: 0,
    controller: null,
    timer: null
  };

  const labels = {
    invalid_year: 'Invalid annual year',
    invalid_date: 'Invalid detailed date',
    missing_unit: 'Missing receiving unit',
    invalid_quantity: 'Invalid quantity',
    possible_duplicate: 'Possible duplicate'
  };

  const getCsrfToken = () => {
    const token = document.querySelector('meta[name="csrf-token"]')?.content?.trim();
    if (!token) throw new Error('Security token is unavailable. Refresh the page and try again.');
    return token;
  };

  const toast = (message, variant = 'success') => {
    const host = document.querySelector('#toastHost');
    if (!host || !window.bootstrap?.Toast) return;
    const node = document.createElement('div');
    node.className = `toast align-items-center text-bg-${variant} border-0`;
    node.role = 'status';
    const flex = document.createElement('div');
    flex.className = 'd-flex';
    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = message;
    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'btn-close btn-close-white me-2 m-auto';
    close.dataset.bsDismiss = 'toast';
    close.ariaLabel = 'Close';
    flex.append(body, close);
    node.append(flex);
    host.append(node);
    const instance = window.bootstrap.Toast.getOrCreateInstance(node, { delay: 5000 });
    node.addEventListener('hidden.bs.toast', () => node.remove(), { once: true });
    instance.show();
  };

  const formatNumber = (value) => new Intl.NumberFormat().format(Number(value) || 0);
  const formatDate = (value) => {
    const parts = String(value || '').split('-');
    if (parts.length !== 3) return value || '—';
    const date = new Date(Date.UTC(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2])));
    return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: '2-digit' }).format(date);
  };

  const create = (tag, text = '', className = '') => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== '') node.textContent = text;
    return node;
  };

  const recordSummary = (item) => {
    if (item.recordKind === 'yearly') return `${item.sourceLabel} · Year ${item.year ?? '—'} · Annual ${formatNumber(item.quantity)}`;
    return `${item.sourceLabel} · ${formatDate(item.proliferationDate)} · ${item.unitName || 'No unit'} · Qty ${formatNumber(item.quantity)}`;
  };

  const render = (data) => {
    state.items = Array.isArray(data.items) ? data.items : [];
    state.total = Number(data.total) || 0;
    state.page = Number(data.page) || 1;
    state.pageSize = Number(data.pageSize) || 25;

    if (el.total) el.total.textContent = String(state.total);
    if (el.invalidDate) el.invalidDate.textContent = String(Number(data.invalidDateOrYearCount) || 0);
    if (el.missingUnit) el.missingUnit.textContent = String(Number(data.missingUnitCount) || 0);
    if (el.invalidQuantity) el.invalidQuantity.textContent = String(Number(data.invalidQuantityCount) || 0);
    if (el.duplicates) el.duplicates.textContent = String(Number(data.possibleDuplicateCount) || 0);

    el.body?.replaceChildren();
    if (state.items.length === 0) {
      const row = document.createElement('tr');
      const cell = create('td', 'No data-quality issues match the current filters.', 'text-muted p-4');
      cell.colSpan = 5;
      row.append(cell);
      el.body?.append(row);
    } else {
      state.items.forEach((item) => {
        const row = document.createElement('tr');

        const projectCell = document.createElement('td');
        const project = create('div', item.projectName || 'Unknown project', 'fw-semibold');
        const code = create('div', item.projectCode || '', 'small text-muted');
        projectCell.append(project);
        if (item.projectCode) projectCell.append(code);

        const issueCell = document.createElement('td');
        const issueLine = document.createElement('div');
        const badge = create('span', labels[item.issueType] || item.issueType, `badge ${item.severity === 'high' ? 'text-bg-danger' : 'text-bg-warning'}`);
        issueLine.append(badge);
        const description = create('div', item.description || '', 'small mt-1');
        issueCell.append(issueLine, description);

        const recordCell = document.createElement('td');
        recordCell.append(create('div', recordSummary(item), 'small'));
        if ((Number(item.relatedRecordCount) || 0) > 1) {
          recordCell.append(create('div', `${item.relatedRecordCount} related records`, 'small text-muted'));
        }

        const statusCell = document.createElement('td');
        statusCell.append(create('span', item.approvalStatus || 'Unknown', 'badge text-bg-light'));

        const actionCell = document.createElement('td');
        actionCell.className = 'text-end';
        if (item.canCorrect) {
          const button = create('button', 'Correct', 'btn btn-sm btn-primary');
          button.type = 'button';
          button.dataset.correctIssue = item.issueKey;
          actionCell.append(button);
        } else {
          const link = create('a', 'Review records', 'btn btn-sm btn-outline-secondary');
          const url = new URL(window.location.href);
          url.searchParams.set('workspace', 'records');
          url.searchParams.set('projectId', String(item.projectId));
          url.searchParams.set('source', String(item.source));
          if (item.year) url.searchParams.set('year', String(item.year));
          url.searchParams.set('kind', item.recordKind);
          link.href = url.toString();
          actionCell.append(link);
        }

        row.append(projectCell, issueCell, recordCell, statusCell, actionCell);
        el.body?.append(row);
      });
    }

    const start = state.total === 0 ? 0 : ((state.page - 1) * state.pageSize) + 1;
    const end = Math.min(state.page * state.pageSize, state.total);
    if (el.range) el.range.textContent = state.total ? `Showing ${start}-${end} of ${state.total} issues` : 'No open issues';
    const pages = Math.max(1, Math.ceil(state.total / state.pageSize));
    if (el.prev) el.prev.disabled = state.page <= 1;
    if (el.next) el.next.disabled = state.page >= pages;

    document.querySelectorAll('[data-quality-filter]').forEach((button) => {
      button.classList.toggle('active', (button.dataset.qualityFilter || '') === state.issueType);
    });
  };

  const load = async () => {
    const sequence = ++state.sequence;
    state.controller?.abort();
    state.controller = new AbortController();
    if (el.body) el.body.innerHTML = '<tr><td colspan="5" class="text-muted p-4"><span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>Loading data-quality issues…</td></tr>';

    const params = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
    if (state.projectId) params.set('projectId', String(state.projectId));
    if (state.issueType) params.set('issueType', state.issueType);
    if (state.search) params.set('search', state.search);

    try {
      const response = await fetch(`/api/proliferation/data-quality?${params}`, {
        headers: { Accept: 'application/json' },
        signal: state.controller.signal
      });
      if (sequence !== state.sequence) return;
      if (!response.ok) throw new Error('Unable to load data-quality issues.');
      render(await response.json());
      el.projectContext?.classList.toggle('d-none', !state.projectId);
      state.loaded = true;
      window.dispatchEvent(new CustomEvent('proliferation:dataqualitychanged'));
    } catch (error) {
      if (error.name === 'AbortError') return;
      if (el.body) {
        el.body.replaceChildren();
        const row = document.createElement('tr');
        const cell = create('td', error.message || 'Unable to load data-quality issues.', 'text-danger p-4');
        cell.colSpan = 5;
        row.append(cell);
        el.body.append(row);
      }
    }
  };

  const configureModal = (item) => {
    state.selected = item;
    if (!item || !el.modal) return;
    if (el.modalContext) el.modalContext.textContent = `${item.projectName} · ${item.sourceLabel}`;
    if (el.currentValue) el.currentValue.textContent = recordSummary(item);
    [el.yearWrap, el.dateWrap, el.unitWrap, el.quantityWrap].forEach((node) => node?.classList.add('d-none'));
    if (el.year) el.year.value = item.year || '';
    if (el.date) el.date.value = item.proliferationDate || '';
    if (el.unit) el.unit.value = item.unitName || '';
    if (el.quantity) el.quantity.value = item.quantity ?? '';
    if (el.reason) el.reason.value = '';
    if (el.error) {
      el.error.textContent = '';
      el.error.classList.add('d-none');
    }

    if (item.recordKind === 'yearly') {
      el.yearWrap?.classList.remove('d-none');
      el.quantityWrap?.classList.remove('d-none');
      if (el.quantity) el.quantity.min = '0';
    } else {
      el.dateWrap?.classList.remove('d-none');
      el.unitWrap?.classList.remove('d-none');
      el.quantityWrap?.classList.remove('d-none');
      if (el.quantity) el.quantity.min = '1';
    }

    window.bootstrap?.Modal?.getOrCreateInstance(el.modal)?.show();
  };

  const saveCorrection = async () => {
    const item = state.selected;
    if (!item) return;
    const reason = el.reason?.value?.trim() || '';
    if (!reason) {
      if (el.error) {
        el.error.textContent = 'Enter a reason for correction.';
        el.error.classList.remove('d-none');
      }
      el.reason?.focus();
      return;
    }

    const maximum = new Date().getUTCFullYear() + 1;
    if (item.recordKind === 'yearly') {
      const year = Number(el.year?.value || 0);
      const quantityText = el.quantity?.value ?? '';
      const quantity = Number(quantityText);
      if (!Number.isInteger(year) || year < 2000 || year > maximum) {
        if (el.error) {
          el.error.textContent = `Enter a year between 2000 and ${maximum}.`;
          el.error.classList.remove('d-none');
        }
        el.year?.focus();
        return;
      }
      if (quantityText === '' || !Number.isInteger(quantity) || quantity < 0) {
        if (el.error) {
          el.error.textContent = 'Enter an annual quantity of zero or more.';
          el.error.classList.remove('d-none');
        }
        el.quantity?.focus();
        return;
      }
    } else {
      const quantityText = el.quantity?.value ?? '';
      const quantity = Number(quantityText);
      if (!el.date?.value) {
        if (el.error) {
          el.error.textContent = 'Select a valid correction date.';
          el.error.classList.remove('d-none');
        }
        el.date?.focus();
        return;
      }
      if (!el.unit?.value?.trim()) {
        if (el.error) {
          el.error.textContent = 'Enter the receiving unit.';
          el.error.classList.remove('d-none');
        }
        el.unit?.focus();
        return;
      }
      if (quantityText === '' || !Number.isInteger(quantity) || quantity <= 0) {
        if (el.error) {
          el.error.textContent = 'Enter a detailed quantity greater than zero.';
          el.error.classList.remove('d-none');
        }
        el.quantity?.focus();
        return;
      }
    }

    const payload = {
      recordKind: item.recordKind,
      rowVersion: item.rowVersion,
      correctedYear: item.recordKind === 'yearly' ? Number(el.year?.value || 0) : null,
      correctedDateUtc: item.recordKind === 'granular' && el.date?.value ? `${el.date.value}T00:00:00Z` : null,
      correctedUnitName: item.recordKind === 'granular' ? el.unit?.value?.trim() || null : null,
      correctedQuantity: Number(el.quantity?.value),
      reason
    };

    el.save.disabled = true;
    try {
      const response = await fetch(`/api/proliferation/data-quality/${encodeURIComponent(item.recordKind)}/${encodeURIComponent(item.recordId)}/correct`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getCsrfToken() },
        credentials: 'same-origin',
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        let message = 'Unable to correct the record.';
        try {
          const body = await response.json();
          message = body.message || body.detail || body.title || message;
        } catch {
          message = (await response.text()) || message;
        }
        throw new Error(message);
      }
      window.bootstrap?.Modal?.getOrCreateInstance(el.modal)?.hide();
      toast('Record corrected and audit entry created.', 'success');
      state.selected = null;
      await load();
      window.dispatchEvent(new CustomEvent('proliferation:recordchanged'));
    } catch (error) {
      if (el.error) {
        el.error.textContent = error.message || 'Unable to correct the record.';
        el.error.classList.remove('d-none');
      }
    } finally {
      el.save.disabled = false;
    }
  };

  el.body?.addEventListener('click', (event) => {
    const button = event.target.closest('[data-correct-issue]');
    if (!button) return;
    configureModal(state.items.find((item) => item.issueKey === button.dataset.correctIssue));
  });
  el.save?.addEventListener('click', saveCorrection);
  el.refresh?.addEventListener('click', load);
  el.clearProject?.addEventListener('click', () => {
    state.projectId = null;
    state.page = 1;
    const url = new URL(window.location.href);
    url.searchParams.delete('projectId');
    window.history.replaceState({}, '', url);
    el.projectContext?.classList.add('d-none');
    load();
  });
  el.clear?.addEventListener('click', () => {
    state.issueType = '';
    state.search = '';
    state.page = 1;
    if (el.type) el.type.value = '';
    if (el.search) el.search.value = '';
    load();
  });
  el.type?.addEventListener('change', () => {
    state.issueType = el.type.value || '';
    state.page = 1;
    load();
  });
  el.search?.addEventListener('input', () => {
    clearTimeout(state.timer);
    state.timer = setTimeout(() => {
      state.search = el.search.value.trim();
      state.page = 1;
      load();
    }, 250);
  });
  el.prev?.addEventListener('click', () => {
    if (state.page <= 1) return;
    state.page -= 1;
    load();
  });
  el.next?.addEventListener('click', () => {
    const pages = Math.max(1, Math.ceil(state.total / state.pageSize));
    if (state.page >= pages) return;
    state.page += 1;
    load();
  });
  document.querySelectorAll('[data-quality-filter]').forEach((button) => {
    button.addEventListener('click', () => {
      state.issueType = button.dataset.qualityFilter || '';
      state.page = 1;
      if (el.type) el.type.value = state.issueType;
      load();
    });
  });

  window.addEventListener('proliferation:workspacechange', (event) => {
    if (event.detail?.workspace === 'data-quality') load();
  });
  window.addEventListener('pagehide', () => state.controller?.abort(), { once: true });

  if (root.dataset.activeWorkspace === 'data-quality') {
    load();
  }
})();
