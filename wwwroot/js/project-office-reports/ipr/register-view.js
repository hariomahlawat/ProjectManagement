(() => {
  'use strict';

  // IPR register view: instant filters, grouping and the record panel.
  // Rows carry their status facts from the server (data-counted / data-closed come from
  // IprStatusPolicy), so this file only matches rows against the chosen filters. The URL
  // keeps the same parameter names the server binds, so links, reloads and Export agree.

  const root = document.querySelector('[data-ipr-register]');
  if (!root) return;

  const rowsBody = root.querySelector('[data-ipr-rows]');
  const rows = Array.from(root.querySelectorAll('[data-ipr-row]'));
  const panel = root.querySelector('[data-ipr-panel]');
  const scrim = root.querySelector('[data-ipr-panel-scrim]');
  const exportLink = document.querySelector('[data-ipr-export]');
  const CLOSED = ['Rejected', 'Withdrawn'];
  const GROUP_LABELS = { project: 'Project', status: 'Status', year: 'Filing year' };

  let records = [];
  try {
    records = JSON.parse(document.getElementById('iprRecordData')?.textContent || '[]');
  } catch {
    records = [];
  }
  const recordsById = new Map(records.map(record => [String(record.Id), record]));

  const initial = (() => {
    try { return JSON.parse(root.dataset.iprState || '{}'); } catch { return {}; }
  })();

  const state = {
    query: initial.query || '',
    types: Array.isArray(initial.types) ? initial.types.slice() : [],
    statuses: Array.isArray(initial.statuses) ? initial.statuses.slice() : [],
    year: initial.year || '',
    dateBasis: initial.dateBasis || 'Filed',
    linkage: initial.linkage || 'All',
    evidence: initial.evidence || 'All',
    projectId: initial.projectId || '',
    group: initial.group || '',
    selectedId: initial.selectedRecordId || ''
  };

  const rowFacts = rows.map(row => ({
    row,
    id: row.dataset.id,
    type: row.dataset.type,
    status: row.dataset.status,
    counted: row.dataset.counted === 'true',
    closed: row.dataset.closed === 'true',
    files: Number.parseInt(row.dataset.files || '0', 10) || 0,
    projectId: row.dataset.projectId || '',
    project: row.dataset.project || '',
    filedYear: row.dataset.filedYear || '',
    grantedYear: row.dataset.grantedYear || '',
    // Title, filing number and project, one per line; matched separately like the server.
    searchFields: (row.dataset.search || '').split('\n').map(field => field.toLowerCase())
  }));

  // ---- Filtering ---------------------------------------------------------------------------

  const statusScope = statuses => {
    if (statuses.length === 0) return 'counted';
    if (statuses.length === 1 && (statuses[0] === 'Filed' || statuses[0] === 'Granted')) return statuses[0];
    if (statuses.every(status => CLOSED.includes(status))) return 'closed';
    return '';
  };

  const statusesForScope = scope => ({
    counted: [],
    Filed: ['Filed'],
    Granted: ['Granted'],
    closed: CLOSED.slice()
  })[scope] || [];

  const matches = (fact, filters) => {
    if (filters.statuses.length === 0) {
      if (!fact.counted) return false;
    } else if (!filters.statuses.includes(fact.status)) {
      return false;
    }
    if (filters.types.length === 1 && fact.type !== filters.types[0]) return false;
    if (filters.projectId) {
      if (fact.projectId !== filters.projectId) return false;
    } else if (filters.linkage === 'Unassigned' && fact.projectId) {
      return false;
    } else if (filters.linkage === 'Linked' && !fact.projectId) {
      return false;
    }
    if (filters.evidence === 'Missing' && fact.files > 0) return false;
    if (filters.evidence === 'Available' && fact.files === 0) return false;
    if (filters.year) {
      const year = filters.dateBasis === 'Protected' ? fact.grantedYear : fact.filedYear;
      if (year !== filters.year) return false;
    }
    if (filters.query) {
      // Same rule as IprQueryFilter: trimmed, lower-cased, contained in any single field.
      const term = filters.query.trim().toLowerCase();
      if (term && !fact.searchFields.some(field => field.includes(term))) return false;
    }
    return true;
  };

  const countWith = patch => {
    const filters = Object.assign({}, state, patch);
    return rowFacts.reduce((total, fact) => total + (matches(fact, filters) ? 1 : 0), 0);
  };

  // ---- Grouping ----------------------------------------------------------------------------

  // A group is keyed by what identifies it and shown by its label. Projects are keyed by id,
  // because two projects may share a name; unlinked records sort first.
  const groupOf = fact => {
    if (state.group === 'project') {
      return fact.projectId
        ? { key: `project:${fact.projectId}`, label: fact.project, rank: 1 }
        : { key: 'project:none', label: 'Not linked to a project', rank: 0 };
    }
    if (state.group === 'status') {
      const label = fact.status === 'Filed' ? 'Pending' : fact.status === 'Granted' ? 'Granted' : 'Closed';
      return { key: label, label, rank: ['Pending', 'Granted', 'Closed'].indexOf(label) };
    }
    const year = fact.filedYear || 'No filing date';
    return { key: year, label: year, rank: 0 };
  };

  const groupOrder = (left, right) => {
    if (left.rank !== right.rank) return left.rank - right.rank;
    if (state.group === 'year') return right.label.localeCompare(left.label);
    return left.label.localeCompare(right.label) || left.key.localeCompare(right.key);
  };

  const render = () => {
    const visible = rowFacts.filter(fact => matches(fact, state));
    const visibleSet = new Set(visible);

    rowsBody.querySelectorAll('[data-ipr-group-row]').forEach(element => element.remove());

    if (state.group) {
      const groups = new Map();
      visible.forEach(fact => {
        const group = groupOf(fact);
        if (!groups.has(group.key)) groups.set(group.key, Object.assign(group, { members: [] }));
        groups.get(group.key).members.push(fact);
      });
      Array.from(groups.values()).sort(groupOrder).forEach(group => {
        const members = group.members;
        const header = document.createElement('tr');
        header.className = 'ipr-table__group';
        header.dataset.iprGroupRow = '';
        const cell = document.createElement('th');
        cell.colSpan = 7;
        cell.scope = 'rowgroup';
        const label = document.createElement('span');
        label.textContent = group.label;
        const count = document.createElement('small');
        count.textContent = `${members.length} ${members.length === 1 ? 'filing' : 'filings'}`;
        cell.append(label, count);
        header.append(cell);
        rowsBody.append(header);
        members.forEach(fact => rowsBody.append(fact.row));
      });
      rowFacts.filter(fact => !visibleSet.has(fact)).forEach(fact => rowsBody.append(fact.row));
    } else {
      rowFacts.forEach(fact => rowsBody.append(fact.row));
    }

    rowFacts.forEach(fact => {
      fact.row.hidden = !visibleSet.has(fact);
      fact.row.classList.toggle('is-selected', fact.id === state.selectedId);
    });

    renderControls(visible.length);
    syncUrl();
  };

  // ---- Controls ----------------------------------------------------------------------------

  const queryInput = root.querySelector('[data-ipr-filter-query]');
  const typeButtons = Array.from(root.querySelectorAll('[data-ipr-filter-type]'));
  const statusButtons = Array.from(root.querySelectorAll('[data-ipr-filter-status]'));
  const flagButtons = Array.from(root.querySelectorAll('[data-ipr-filter-flag]'));
  const yearSelect = root.querySelector('[data-ipr-filter-year]');
  const yearLabel = root.querySelector('[data-ipr-year-label]');
  const groupSelect = root.querySelector('[data-ipr-group]');
  const resultCount = root.querySelector('[data-ipr-result-count]');
  const clearButtons = Array.from(root.querySelectorAll('[data-ipr-clear]'));
  const empty = root.querySelector('[data-ipr-empty]');
  const projectChip = root.querySelector('[data-ipr-project-chip]');

  const setPressed = (button, on) => {
    button.classList.toggle('is-on', on);
    button.setAttribute('aria-pressed', on ? 'true' : 'false');
  };

  const setCount = (button, value) => {
    const target = button.querySelector('[data-ipr-count]');
    if (target) target.textContent = String(value);
  };

  const hasFilters = () => Boolean(
    state.query || state.types.length || state.statuses.length || state.year ||
    state.linkage !== 'All' || state.evidence !== 'All' || state.projectId);

  const renderControls = visibleCount => {
    const typeValue = state.types.length === 1 ? state.types[0] : 'all';
    typeButtons.forEach(button => {
      const value = button.dataset.iprFilterType;
      setPressed(button, value === typeValue);
      if (value !== 'all') setCount(button, countWith({ types: [value] }));
    });

    const scope = statusScope(state.statuses);
    statusButtons.forEach(button => {
      const value = button.dataset.iprFilterStatus;
      setPressed(button, value === scope);
      setCount(button, countWith({ statuses: statusesForScope(value) }));
    });

    flagButtons.forEach(button => {
      const flag = button.dataset.iprFilterFlag;
      const on = flag === 'missing' ? state.evidence === 'Missing' : state.linkage === 'Unassigned';
      setPressed(button, on);
      setCount(button, countWith(flag === 'missing'
        ? { evidence: 'Missing' }
        : { linkage: 'Unassigned', projectId: '' }));
      button.disabled = flag === 'unassigned' && Boolean(state.projectId);
    });

    if (yearSelect) {
      if (state.year && !Array.from(yearSelect.options).some(option => option.value === state.year)) {
        yearSelect.add(new Option(state.year, state.year));
      }
      yearSelect.value = state.year;
    }
    if (yearLabel) yearLabel.textContent = state.dateBasis === 'Protected' ? 'Granted in' : 'Filed in';
    if (groupSelect) groupSelect.value = state.group;
    if (queryInput && queryInput.value !== state.query) queryInput.value = state.query;

    if (projectChip) {
      const project = rowFacts.find(fact => fact.projectId && fact.projectId === state.projectId);
      projectChip.classList.toggle('d-none', !state.projectId);
      const label = projectChip.querySelector('[data-ipr-project-chip-label]');
      if (label) label.textContent = state.projectId ? `Project: ${project?.project || state.projectId}` : '';
    }

    if (resultCount) resultCount.textContent = `${visibleCount} ${visibleCount === 1 ? 'record' : 'records'}`;
    clearButtons.forEach(button => button.classList.toggle('d-none', !hasFilters()));

    if (empty) {
      const none = visibleCount === 0;
      empty.classList.toggle('d-none', !none);
      const hint = empty.querySelector('[data-ipr-empty-hint]');
      const title = empty.querySelector('[data-ipr-empty-title]');
      if (none && rows.length > 0) {
        if (title) title.textContent = 'No records match';
        if (hint) {
          hint.textContent = statusScope(state.statuses) === 'closed'
            ? 'No filing has been marked rejected or withdrawn.'
            : 'Try another search or remove a filter.';
        }
      }
    }
  };

  const filterParams = () => {
    const params = new URLSearchParams();
    if (state.query) params.set('query', state.query);
    state.types.forEach(type => params.append('types', type));
    state.statuses.forEach(status => params.append('statuses', status));
    if (state.year) params.set('year', state.year);
    if (state.year && state.dateBasis === 'Protected') params.set('dateBasis', 'Protected');
    if (state.projectId) params.set('projectId', state.projectId);
    else if (state.linkage !== 'All') params.set('linkage', state.linkage);
    if (state.evidence !== 'All') params.set('evidence', state.evidence);
    return params;
  };

  const syncUrl = () => {
    const params = filterParams();
    const url = new URL(window.location.href);
    const next = new URLSearchParams();
    next.set('tab', 'records');
    params.forEach((value, key) => next.append(key, value));
    if (state.group) next.set('group', state.group);
    if (state.selectedId) next.set('selectedRecordId', state.selectedId);
    ['mode', 'id'].forEach(key => {
      const value = url.searchParams.get(key);
      if (value) next.set(key, value);
    });
    url.search = next.toString();
    history.replaceState({}, document.title, url.toString());

    if (exportLink) {
      const exportUrl = new URL(exportLink.href, window.location.href);
      const exportParams = new URLSearchParams();
      exportParams.set('handler', 'Export');
      params.forEach((value, key) => exportParams.append(key, value));
      exportUrl.search = exportParams.toString();
      exportLink.href = exportUrl.toString();
    }
  };

  let queryTimer = 0;
  queryInput?.addEventListener('input', () => {
    window.clearTimeout(queryTimer);
    queryTimer = window.setTimeout(() => {
      state.query = queryInput.value.trim();
      render();
    }, 120);
  });

  typeButtons.forEach(button => button.addEventListener('click', () => {
    const value = button.dataset.iprFilterType;
    state.types = value === 'all' ? [] : [value];
    render();
  }));

  statusButtons.forEach(button => button.addEventListener('click', () => {
    state.statuses = statusesForScope(button.dataset.iprFilterStatus);
    render();
  }));

  flagButtons.forEach(button => button.addEventListener('click', () => {
    if (button.dataset.iprFilterFlag === 'missing') {
      state.evidence = state.evidence === 'Missing' ? 'All' : 'Missing';
    } else {
      state.linkage = state.linkage === 'Unassigned' ? 'All' : 'Unassigned';
    }
    render();
  }));

  yearSelect?.addEventListener('change', () => {
    state.year = yearSelect.value;
    if (!state.year) state.dateBasis = 'Filed';
    render();
  });

  groupSelect?.addEventListener('change', () => {
    state.group = groupSelect.value;
    render();
  });

  root.querySelector('[data-ipr-project-chip-clear]')?.addEventListener('click', () => {
    state.projectId = '';
    render();
  });

  clearButtons.forEach(button => button.addEventListener('click', () => {
    Object.assign(state, { query: '', types: [], statuses: [], year: '', dateBasis: 'Filed', linkage: 'All', evidence: 'All', projectId: '' });
    render();
    queryInput?.focus();
  }));

  // Summary cards filter in place on this view; their href still works without script.
  document.querySelectorAll('[data-ipr-scope]').forEach(card => card.addEventListener('click', event => {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return;
    event.preventDefault();
    state.statuses = statusesForScope(card.dataset.iprScope);
    render();
    root.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }));

  // ---- Record panel ------------------------------------------------------------------------

  const set = (selector, value) => {
    const element = panel?.querySelector(selector);
    if (element) element.textContent = value ?? '';
  };

  // Same "dd MMM yyyy" shape as the server-rendered table (Intl's en-GB writes "Sept").
  const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const formatDate = value => {
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value || '');
    if (!match) return '—';
    return `${match[3]} ${MONTHS[Number(match[2]) - 1]} ${match[1]}`;
  };

  const editHref = (id, hash) => {
    const url = new URL(window.location.href);
    url.searchParams.set('selectedRecordId', id);
    url.searchParams.set('mode', 'edit');
    url.searchParams.set('id', id);
    url.hash = hash || '';
    return url.toString();
  };

  let lastFocus = null;

  const openPanel = (id, { focus = true } = {}) => {
    const record = recordsById.get(String(id));
    const fact = rowFacts.find(item => item.id === String(id));
    if (!panel || !record || !fact) return;

    const isCopyright = fact.type === 'Copyright';
    const status = record.Status || '';
    const statusModifier = fact.status === 'Granted' ? 'granted' : fact.closed ? 'closed' : 'filed';

    panel.querySelector('[data-ipr-panel-dot]').className = `ipr-type-dot ipr-type-dot--${fact.type.toLowerCase()}`;
    set('[data-ipr-panel-eyebrow]', `${record.IprType} · ${record.ApplicationNumber}`);
    set('[data-ipr-panel-title]', record.Title);
    const badge = panel.querySelector('[data-ipr-panel-status]');
    badge.className = `ipr-status-badge ipr-status-badge--${statusModifier}`;
    badge.textContent = status;

    set('[data-ipr-panel-number]', record.ApplicationNumber);
    set('[data-ipr-panel-type]', record.IprType);
    set('[data-ipr-panel-filed]', formatDate(record.FiledOn));
    set('[data-ipr-panel-granted-label]', isCopyright ? 'Registered on' : 'Granted on');
    set('[data-ipr-panel-granted]', formatDate(record.GrantedOn));
    set('[data-ipr-panel-by]', record.FiledBy || 'Not recorded');

    const projectCell = panel.querySelector('[data-ipr-panel-project]');
    projectCell.replaceChildren();
    if (record.ProjectId) {
      const link = document.createElement('a');
      const base = root.dataset.iprProjectBaseUrl || '/Projects/Overview';
      link.href = `${base}${base.includes('?') ? '&' : '?'}id=${encodeURIComponent(record.ProjectId)}`;
      link.textContent = record.ProjectName;
      projectCell.append(link);
    } else {
      projectCell.textContent = 'Not linked';
      projectCell.classList.add('is-muted');
    }

    set('[data-ipr-panel-progress-filed]', formatDate(record.FiledOn));
    const outcome = panel.querySelector('[data-ipr-panel-progress-outcome]');
    outcome.classList.toggle('is-done', fact.status === 'Granted');
    outcome.classList.toggle('is-closed', fact.closed);
    if (fact.status === 'Granted') {
      set('[data-ipr-panel-outcome-title]', isCopyright ? 'Registered' : 'Granted');
      set('[data-ipr-panel-outcome-text]', formatDate(record.GrantedOn));
    } else if (fact.closed) {
      set('[data-ipr-panel-outcome-title]', fact.status);
      set('[data-ipr-panel-outcome-text]', 'Kept as history; not counted as filed.');
    } else {
      set('[data-ipr-panel-outcome-title]', 'Awaiting outcome');
      set('[data-ipr-panel-outcome-text]', record.PendingAge ? `Pending for ${record.PendingAge}` : 'Pending');
    }

    const files = panel.querySelector('[data-ipr-panel-files]');
    files.replaceChildren();
    const attachments = record.Attachments || [];
    if (attachments.length === 0) {
      const missing = document.createElement('p');
      missing.className = 'ipr-record-panel__missing';
      missing.textContent = 'No filing receipt or grant certificate uploaded yet.';
      files.append(missing);
    } else {
      const downloadBase = root.dataset.iprDownloadUrl || `${window.location.pathname.replace(/\/$/, '')}/Download`;
      attachments.forEach(attachment => {
        const link = document.createElement('a');
        link.className = 'ipr-record-panel__file';
        link.href = `${downloadBase}${downloadBase.includes('?') ? '&' : '?'}iprRecordId=${encodeURIComponent(record.Id)}&attachmentId=${encodeURIComponent(attachment.Id)}`;
        link.target = '_blank';
        link.rel = 'noopener';
        const icon = document.createElement('i');
        icon.className = 'bi bi-file-earmark-pdf';
        icon.setAttribute('aria-hidden', 'true');
        const text = document.createElement('span');
        const name = document.createElement('strong');
        name.textContent = attachment.FileName || 'Attachment';
        const meta = document.createElement('small');
        meta.textContent = [attachment.FileSize, attachment.UploadedBy, attachment.UploadedAt].filter(Boolean).join(' · ');
        text.append(name, meta);
        link.append(icon, text);
        files.append(link);
      });
    }

    const notes = panel.querySelector('[data-ipr-panel-notes]');
    notes.textContent = record.ExternalRemark || 'No notes yet.';
    notes.classList.toggle('is-muted', !record.ExternalRemark);

    const edit = panel.querySelector('[data-ipr-panel-edit]');
    const grant = panel.querySelector('[data-ipr-panel-grant]');
    const evidence = panel.querySelector('[data-ipr-panel-evidence]');
    if (edit) edit.href = editHref(record.Id);
    if (grant) {
      grant.href = editHref(record.Id);
      grant.classList.toggle('d-none', fact.status !== 'Filed');
    }
    if (evidence) evidence.href = editHref(record.Id, 'iprEvidenceHeading');

    state.selectedId = String(record.Id);
    rowFacts.forEach(item => item.row.classList.toggle('is-selected', item.id === state.selectedId));
    if (panel.hidden) lastFocus = document.activeElement;
    panel.hidden = false;
    if (scrim) scrim.hidden = false;
    document.body.classList.add('ipr-panel-open');
    syncUrl();
    if (focus) panel.focus({ preventScroll: true });
  };

  const closePanel = () => {
    if (!panel || panel.hidden) return;
    panel.hidden = true;
    if (scrim) scrim.hidden = true;
    document.body.classList.remove('ipr-panel-open');
    state.selectedId = '';
    rowFacts.forEach(item => item.row.classList.remove('is-selected'));
    syncUrl();
    if (lastFocus && typeof lastFocus.focus === 'function') lastFocus.focus({ preventScroll: true });
  };

  rowFacts.forEach(fact => {
    fact.row.addEventListener('click', event => {
      if (event.target.closest('a, input, select, textarea')) return;
      openPanel(fact.id);
    });
  });

  panel?.querySelector('[data-ipr-panel-close]')?.addEventListener('click', closePanel);
  scrim?.addEventListener('click', closePanel);
  document.addEventListener('keydown', event => {
    if (!panel || panel.hidden) return;
    if (event.key === 'Tab') window.iprTrapFocus?.(panel, event);
    if (event.key === 'Escape' && !document.querySelector('.offcanvas.show, .modal.show')) {
      closePanel();
    }
  });

  render();
  const editingOpen = new URL(window.location.href).searchParams.get('mode');
  if (state.selectedId && recordsById.has(state.selectedId) && !editingOpen) {
    openPanel(state.selectedId, { focus: false });
  }
})();
