/* global bootstrap, ProliferationProjectPicker */
(() => {
  const Modal = window.bootstrap?.Modal ?? null;
  const Collapse = window.bootstrap?.Collapse ?? null;

  const pageRoot = document.querySelector('[data-page="proliferation-manage"]');
  const canApproveRecords = pageRoot?.dataset?.canApprove === 'true';
  const listCard = document.querySelector('#pf-list-card');
  const overridesCard = document.querySelector('#pf-overrides-card');
  const editorCard = document.querySelector('#pf-editor');
  const manageLayout = document.querySelector('#pf-manage-layout');
  const commandElements = {
    title: document.querySelector('[data-command-title]'),
    scope: document.querySelector('[data-command-scope]'),
    updated: document.querySelector('[data-command-updated-value]'),
    updatedWrap: document.querySelector('[data-command-updated-wrap]'),
    typeBadge: document.querySelector('#pf-editor-type-badge'),
    saveLabel: document.querySelector('[data-save-label]')
  };
  const decisionButtons = {
    approve: document.querySelector('#pf-approve'),
    reject: document.querySelector('#pf-reject'),
    busy: false
  };
  const approvalElements = {
    container: document.querySelector('[data-approval-container]'),
    badge: document.querySelector('[data-approval-badge]'),
    status: document.querySelector('[data-approval-status]'),
    submitted: document.querySelector('[data-approval-submitted]'),
    updated: document.querySelector('[data-approval-updated]'),
    decided: document.querySelector('[data-approval-decided]')
  };
  const currentRecord = {
    id: '',
    kind: '',
    status: '',
    rowVersion: '',
    sourceLabel: '',
    projectId: '',
    source: '',
    year: '',
    quantity: ''
  };
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
    setPreference: '/api/proliferation/year-preference',
    decideYearly: (id) => `/api/proliferation/yearly/${id}/decision`,
    decideGranular: (id) => `/api/proliferation/granular/${id}/decision`,
    groups: '/api/proliferation/groups',
    unitSuggestions: '/api/proliferation/reports/unit-suggestions',
    projects: '/api/proliferation/projects'
  };

  const listEl = document.querySelector('#pf-list');
  const pagerEl = document.querySelector('#pf-pager');
  const countEl = document.querySelector('#pf-count');

  // SECTION: Filter state
  const filters = {
    projectId: '',
    source: '',
    year: '',
    kind: '',
    status: '',
    search: ''
  };

  let activeWorkspace = pageRoot?.dataset?.initialWorkspace || 'records';
  let storedRecordStatus = '';

  function isApprovalWorkspace() {
    return activeWorkspace === 'approvals';
  }

  function getCsrfToken() {
    const token = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content')?.trim();
    if (!token) throw new Error('Security token is unavailable. Refresh the page and try again.');
    return token;
  }

  function sanitizeId(value) {
    const text = String(value ?? '').trim();
    return /^[0-9]+$/.test(text) ? text : '';
  }

  function sanitizeKind(value) {
    const text = String(value ?? '').trim().toLowerCase();
    return text === 'yearly' || text === 'granular' ? text : '';
  }

  function sanitizeYear(value) {
    const text = String(value ?? '').trim();
    return /^[0-9]{4}$/.test(text) ? text : '';
  }

  function escapeHtml(value) {
    return String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  const pager = {
    page: 1,
    pageSize: Number(listCard.dataset.pageSize) || 25,
    total: 0
  };

  const bootDefaults = (() => {
    const listProject = sanitizeId(listCard?.dataset?.bootProjectId);
    const listSource = sanitizeId(listCard?.dataset?.bootSource);
    const listYear = sanitizeYear(listCard?.dataset?.bootYear);
    const listKind = sanitizeKind(listCard?.dataset?.bootKind);

    const editorProject = sanitizeId(editorCard?.dataset?.bootProjectId);
    const editorSource = sanitizeId(editorCard?.dataset?.bootSource);
    const editorYear = sanitizeYear(editorCard?.dataset?.bootYear);
    const editorKind = sanitizeKind(editorCard?.dataset?.bootKind);

    const overridesProject = sanitizeId(overridesCard?.dataset?.bootProjectId);
    const overridesSource = sanitizeId(overridesCard?.dataset?.bootSource);
    const overridesYear = sanitizeYear(overridesCard?.dataset?.bootYear);
    const overridesKind = sanitizeKind(overridesCard?.dataset?.bootKind);

    return {
      filters: {
        projectId: listProject || editorProject || '',
        source: listSource || editorSource || '',
        year: listYear || editorYear || '',
        kind: sanitizeKind(listKind || editorKind || overridesKind)
      },
      editor: {
        projectId: editorProject || listProject || '',
        source: editorSource || listSource || '',
        year: editorYear || listYear || '',
        kind: sanitizeKind(editorKind || listKind)
      },
      overrides: {
        projectId: overridesProject || listProject || editorProject || '',
        source: overridesSource || listSource || editorSource || '',
        year: overridesYear || listYear || editorYear || '',
        kind: sanitizeKind(overridesKind || listKind || editorKind)
      }
    };
  })();

  const deepLinkContext = (() => {
    const context = bootDefaults.filters || {};
    const projectId = context.projectId || '';
    const source = context.source || '';
    const year = context.year || '';
    const kind = context.kind || '';
    if (!projectId && !source && !year && !kind) {
      return null;
    }
    return { projectId, source, year, kind };
  })();

  let bootFocusPending = false;
  let bootEventEmitted = false;

  const defaults = {
    year: Number(editorCard.dataset.currentYear) || new Date().getUTCFullYear(),
    granularSource: '1',
    boot: bootDefaults
  };

  if (bootDefaults.editor?.year) {
    const parsedYear = Number(bootDefaults.editor.year);
    if (Number.isFinite(parsedYear)) {
      defaults.year = parsedYear;
    }
  }

  const editor = {
    form: document.querySelector('#pf-form'),
    id: document.querySelector('#pf-id'),
    kind: document.querySelector('#pf-kind'),
    rowVersion: document.querySelector('#pf-row-version'),
    project: document.querySelector('#pf-project'),
    projectSearch: document.querySelector('#pf-project-search'),
    source: document.querySelector('#pf-source'),
    year: document.querySelector('#pf-year'),
    date: document.querySelector('#pf-date'),
    unit: document.querySelector('#pf-unit'),
    qty: document.querySelector('#pf-qty'),
    remarks: document.querySelector('#pf-remarks'),
    btnSave: document.querySelector('#pf-save'),
    btnReset: document.querySelector('#pf-reset'),
    btnAddAnother: document.querySelector('#pf-add-another'),
    btnDelete: document.querySelector('#pf-delete'),
    impact: document.querySelector('#pf-editor-impact'),
    impactCurrent: document.querySelector('[data-impact-current]'),
    impactNext: document.querySelector('[data-impact-next]'),
    impactNextLabel: document.querySelector('[data-impact-next-label]'),
    impactNote: document.querySelector('[data-impact-note]'),
    unitSuggestions: document.querySelector('#pf-unit-suggestions'),
    duplicateWarning: document.querySelector('#pf-duplicate-warning'),
    requiredSummary: document.querySelector('#pf-required-summary')
  };

  if (editor.date) {
    const maxDate = new Date();
    maxDate.setUTCDate(maxDate.getUTCDate() + 30);
    editor.date.max = maxDate.toISOString().slice(0, 10);
  }

  let editorBaseline = '';

  function serializeEditorState() {
    return JSON.stringify({
      kind: editor.kind?.value || 'granular',
      projectId: editor.project?.value || '',
      source: editor.source?.value || '',
      year: editor.year?.value || '',
      date: editor.date?.value || '',
      unit: editor.unit?.value?.trim() || '',
      quantity: editor.qty?.value || '',
      remarks: editor.remarks?.value?.trim() || ''
    });
  }

  function markEditorClean() {
    editorBaseline = serializeEditorState();
  }

  function isEditorDirty() {
    return Boolean(editorBaseline) && serializeEditorState() !== editorBaseline;
  }

  function confirmDiscardChanges() {
    if (!isEditorDirty()) {
      return true;
    }

    return window.confirm('You have unsaved changes. Continue without saving them?');
  }

  function beginNewEntry(kind, options = {}) {
    const target = kind === 'yearly' ? 'yearly' : 'granular';
    if (!confirmDiscardChanges()) {
      return false;
    }

    const preserveContext = options.preserveContext === true;
    const context = preserveContext
      ? {
          projectId: editor.project?.value || '',
          project: editorProjectPicker?.getSelected() || null,
          source: editor.source?.value || '',
          year: editor.year?.value || ''
        }
      : null;

    resetEditor(target);

    if (context) {
      if (context.project) {
        editorProjectPicker?.setSelection(context.project, { notify: false, dispatch: false });
      } else if (context.projectId) {
        editorProjectPicker?.initializeById(context.projectId, { notify: false, dispatch: false });
      }
      if (editor.source && context.source && hasOption(editor.source, context.source)) {
        editor.source.value = context.source;
      }
      if (editor.year && context.year) {
        editor.year.value = context.year;
      }
      updateSaveButtonState();
      markEditorClean();
    }

    return true;
  }

  const fieldErrors = {
    project: document.querySelector('[data-field-error="project"]'),
    source: document.querySelector('[data-field-error="source"]'),
    year: document.querySelector('[data-field-error="year"]'),
    date: document.querySelector('[data-field-error="date"]'),
    unit: document.querySelector('[data-field-error="unit"]'),
    qty: document.querySelector('[data-field-error="qty"]')
  };

  const fieldStates = {
    project: { input: editor.projectSearch || editor.project, error: fieldErrors.project, touched: false },
    source: { input: editor.source, error: fieldErrors.source, touched: false },
    year: { input: editor.year, error: fieldErrors.year, touched: false },
    date: { input: editor.date, error: fieldErrors.date, touched: false },
    unit: { input: editor.unit, error: fieldErrors.unit, touched: false },
    qty: { input: editor.qty, error: fieldErrors.qty, touched: false }
  };

  const saveButtonState = {
    defaultContent: editor.btnSave ? editor.btnSave.innerHTML : 'Save',
    busy: false,
    successTimer: null
  };

  function getFieldState(name) {
    return fieldStates[name] ?? null;
  }

  function getActiveFieldNames() {
    const base = ['project', 'source', 'qty'];
    if (editor.kind?.value === 'yearly') {
      return [...base, 'year'];
    }
    return [...base, 'date', 'unit'];
  }

  function getFieldError(name) {
    const isYearly = editor.kind?.value === 'yearly';
    const isGranular = !isYearly;
    switch (name) {
      case 'project': {
        const value = Number(editor.project?.value ?? '');
        if (!Number.isFinite(value) || value <= 0) {
          return 'Select a project.';
        }
        return '';
      }
      case 'source': {
        const value = Number(editor.source?.value ?? '');
        if (!Number.isFinite(value) || value <= 0) {
          return 'Select a source.';
        }
        return '';
      }
      case 'year': {
        const raw = (editor.year?.value ?? '').toString().trim();
        if (!raw) {
          return 'Enter a year.';
        }
        if (!/^[0-9]{4}$/.test(raw)) {
          return 'Enter a four-digit year.';
        }
        const value = Number(raw);
        if (value < 2000 || value > 3000) {
          return 'Year must be between 2000 and 3000.';
        }
        return '';
      }
      case 'date': {
        if (!isGranular) {
          return '';
        }
        const value = (editor.date?.value ?? '').toString().trim();
        if (!value) {
          return 'Select a proliferation date.';
        }
        const selected = new Date(`${value}T00:00:00Z`);
        const minimum = new Date('2000-01-01T00:00:00Z');
        const maximum = new Date();
        maximum.setUTCDate(maximum.getUTCDate() + 30);
        if (Number.isNaN(selected.getTime()) || selected < minimum) {
          return 'Date cannot be earlier than 01 Jan 2000.';
        }
        if (selected > maximum) {
          return 'Date cannot be more than 30 days in the future.';
        }
        return '';
      }
      case 'unit': {
        if (!isGranular) {
          return '';
        }
        const value = editor.unit?.value?.trim() ?? '';
        if (!value) {
          return 'Enter a unit name.';
        }
        return '';
      }
      case 'qty': {
        const raw = (editor.qty?.value ?? '').toString().trim();
        if (!raw) {
          return 'Enter a quantity.';
        }
        const value = Number(raw);
        if (!Number.isFinite(value)) {
          return 'Enter a valid quantity.';
        }
        if (isGranular && value <= 0) {
          return 'Quantity must be greater than zero.';
        }
        if (isYearly && value < 0) {
          return 'Quantity cannot be negative.';
        }
        return '';
      }
      default:
        return '';
    }
  }

  function applyFieldError(name, error, display = false) {
    const state = getFieldState(name);
    if (!state) return;
    const shouldShow = Boolean(error) && display;
    const { error: errorEl, input } = state;
    if (errorEl) {
      if (shouldShow) {
        errorEl.textContent = error;
        errorEl.classList.remove('d-none');
      } else {
        errorEl.textContent = '';
        errorEl.classList.add('d-none');
      }
    }
    if (input) {
      if (shouldShow) {
        input.setAttribute('aria-invalid', 'true');
      } else {
        input.removeAttribute('aria-invalid');
      }
    }
  }

  function validateField(name, options = {}) {
    const display = options.display === true;
    const error = getFieldError(name);
    applyFieldError(name, error, display);
    return !error;
  }

  function clearValidationState() {
    Object.keys(fieldStates).forEach((name) => {
      const state = getFieldState(name);
      if (!state) return;
      state.touched = false;
      applyFieldError(name, '', false);
    });
  }

  function updateSaveButtonState() {
    if (!editor.btnSave) return;
    if (saveButtonState.busy) {
      editor.btnSave.disabled = true;
      return;
    }
    const activeNames = getActiveFieldNames();
    const activeSet = new Set(activeNames);
    Object.keys(fieldStates).forEach((name) => {
      if (!activeSet.has(name)) {
        const state = getFieldState(name);
        if (!state) return;
        state.touched = false;
        applyFieldError(name, '', false);
      }
    });
    let formValid = true;
    const incomplete = [];
    const fieldLabels = {
      project: 'project',
      source: 'source',
      year: 'year',
      date: 'proliferation date',
      unit: 'receiving unit',
      qty: 'quantity'
    };
    activeNames.forEach((name) => {
      const state = getFieldState(name);
      if (!state) return;
      const display = Boolean(state.touched);
      const valid = validateField(name, { display });
      if (!valid) {
        formValid = false;
        incomplete.push(fieldLabels[name] || name);
      }
    });
    if (editor.requiredSummary) {
      editor.requiredSummary.textContent = incomplete.length
        ? `Complete: ${incomplete.join(', ')}.`
        : 'All required fields are complete.';
      editor.requiredSummary.classList.toggle('text-success', incomplete.length === 0);
      editor.requiredSummary.classList.toggle('text-muted', incomplete.length > 0);
    }
    editor.btnSave.disabled = !formValid;
    editor.btnSave.setAttribute('aria-disabled', editor.btnSave.disabled ? 'true' : 'false');
  }

  function validateForm(options = {}) {
    const { focus = false } = options;
    const fields = getActiveFieldNames();
    let firstInvalid = null;
    fields.forEach((name) => {
      const state = getFieldState(name);
      if (!state) return;
      state.touched = true;
      const valid = validateField(name, { display: true });
      if (!valid && !firstInvalid) {
        firstInvalid = state.input;
      }
    });
    updateSaveButtonState();
    if (focus && firstInvalid && typeof firstInvalid.focus === 'function') {
      try {
        firstInvalid.focus({ preventScroll: true });
      } catch (err) {
        firstInvalid.focus();
      }
    }
    return !firstInvalid;
  }

  function attachValidationHandlers(name) {
    const state = getFieldState(name);
    if (!state?.input) return;
    const events = state.input.tagName === 'SELECT' ? ['change', 'blur'] : ['input', 'change', 'blur'];
    events.forEach((eventName) => {
      state.input.addEventListener(eventName, () => {
        state.touched = true;
        validateField(name, { display: true });
        updateSaveButtonState();
      });
    });
  }

  function setSaveButtonState(state) {
    if (!editor.btnSave) return;
    window.clearTimeout(saveButtonState.successTimer);
    if (state === 'loading') {
      saveButtonState.busy = true;
      editor.btnSave.disabled = true;
      editor.btnSave.setAttribute('aria-disabled', 'true');
      editor.btnSave.setAttribute('aria-busy', 'true');
      editor.btnSave.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span><span>Saving…</span>';
      return;
    }
    if (state === 'success') {
      saveButtonState.busy = false;
      editor.btnSave.disabled = true;
      editor.btnSave.setAttribute('aria-disabled', 'true');
      editor.btnSave.setAttribute('aria-busy', 'false');
      editor.btnSave.innerHTML = '<span class="me-1" aria-hidden="true">✓</span><span>Saved</span>';
      saveButtonState.successTimer = window.setTimeout(() => {
        setSaveButtonState('idle');
        updateSaveButtonState();
      }, 1500);
      return;
    }
    saveButtonState.busy = false;
    editor.btnSave.setAttribute('aria-busy', 'false');
    editor.btnSave.innerHTML = saveButtonState.defaultContent || 'Save';
    updateSaveButtonState();
  }

  ['project', 'source', 'year', 'date', 'unit', 'qty'].forEach((name) => {
    attachValidationHandlers(name);
    const input = getFieldState(name)?.input;
    if (input) {
      input.addEventListener(input.tagName === 'SELECT' ? 'change' : 'input', scheduleEditorImpact);
      input.addEventListener(input.tagName === 'SELECT' ? 'change' : 'input', scheduleDuplicateCheck);
    }
  });

  editor.unit?.addEventListener('focus', refreshUnitSuggestions);
  editor.unit?.addEventListener('input', () => {
    window.clearTimeout(unitSuggestionTimer);
    unitSuggestionTimer = window.setTimeout(refreshUnitSuggestions, 250);
  });

  clearValidationState();
  setSaveButtonState('idle');

  const filterInputs = {
    project: document.querySelector('#pf-filter-project'),
    projectSearch: document.querySelector('#pf-filter-project-search'),
    source: document.querySelector('#pf-filter-source'),
    year: document.querySelector('#pf-filter-year'),
    kind: document.querySelector('#pf-filter-type'),
    status: document.querySelector('#pf-filter-status'),
    search: document.querySelector('#pf-filter-search'),
    reset: document.querySelector('#pf-filter-reset'),
    chips: document.querySelector('#pf-filter-chips')
  };
  let filterSearchTimer = null;

  const toastHost = document.querySelector('#toastHost');

  const overridesElements = {
    card: overridesCard,
    collapse: overridesCard ? overridesCard.querySelector('#pf-overrides-collapse') : null,
    toggle: overridesCard ? overridesCard.querySelector('#pf-overrides-collapse-toggle') : null,
    tableBody: overridesCard ? overridesCard.querySelector('#pf-overrides-body') : null,
    summary: overridesCard ? overridesCard.querySelector('#pf-overrides-summary') : null,
    footer: overridesCard ? overridesCard.querySelector('#pf-overrides-summary')?.closest('.card-footer') : null,
    reset: overridesCard ? overridesCard.querySelector('#pf-overrides-reset') : null,
    project: overridesCard ? overridesCard.querySelector('#pf-overrides-project') : null,
    projectSearch: overridesCard ? overridesCard.querySelector('#pf-overrides-project-search') : null,
    source: overridesCard ? overridesCard.querySelector('#pf-overrides-source') : null,
    year: overridesCard ? overridesCard.querySelector('#pf-overrides-year') : null,
    search: overridesCard ? overridesCard.querySelector('#pf-overrides-search') : null,
    refresh: overridesCard ? overridesCard.querySelector('#pf-overrides-refresh') : null,
    export: document.querySelector('#pf-overrides-export'),
    ruleEditor: overridesCard ? overridesCard.querySelector('#pf-rule-editor') : null,
    ruleProject: overridesCard ? overridesCard.querySelector('#pf-rule-project') : null,
    ruleProjectSearch: overridesCard ? overridesCard.querySelector('#pf-rule-project-search') : null,
    ruleSource: overridesCard ? overridesCard.querySelector('#pf-rule-source') : null,
    ruleYear: overridesCard ? overridesCard.querySelector('#pf-rule-year') : null,
    ruleMode: overridesCard ? overridesCard.querySelector('#pf-rule-mode') : null,
    ruleSave: overridesCard ? overridesCard.querySelector('#pf-rule-save') : null,
    ruleGuidance: overridesCard ? overridesCard.querySelector('#pf-rule-guidance') : null,
    ruleDefaultBadge: overridesCard ? overridesCard.querySelector('#pf-rule-default-badge') : null,
    ruleReason: overridesCard ? overridesCard.querySelector('#pf-rule-reason') : null,
    ruleReasonWrap: overridesCard ? overridesCard.querySelector('#pf-rule-reason-wrap') : null,
    ruleImpact: overridesCard ? overridesCard.querySelector('#pf-rule-impact') : null
  };
  let editorProjectPicker = null;
  let filterProjectPicker = null;
  let overridesProjectPicker = null;
  let ruleProjectPicker = null;

  function buildProjectPicker(input, valueInput, options = {}) {
    const suggestions = options.suggestions || input?.closest('[data-project-search-picker]')?.querySelector('[role="listbox"]');
    if (!input || !valueInput || !suggestions || typeof window.ProliferationProjectPicker !== 'function') return null;
    return new window.ProliferationProjectPicker({
      input,
      hiddenInput: valueInput,
      suggestions,
      clearButton: input.closest('[data-project-search-picker]')?.querySelector('.pf-project-picker__clear'),
      statusElement: input.closest('[data-project-search-picker]')?.querySelector('[role="status"]'),
      endpoint: api.projects,
      minimumLength: 0,
      maxVisible: 8,
      onSelected: options.onSelected,
      onCleared: options.onCleared
    });
  }

  function initProjectPickers() {
    editorProjectPicker = buildProjectPicker(editor.projectSearch, editor.project, {
      onSelected: () => {
        fieldStates.project.touched = true;
        validateField('project', { display: true });
        updateSaveButtonState();
        scheduleEditorImpact();
        scheduleDuplicateCheck();
        editor.source?.focus();
      },
      onCleared: () => {
        fieldStates.project.touched = true;
        validateField('project', { display: true });
        updateSaveButtonState();
        scheduleEditorImpact();
        scheduleDuplicateCheck();
      }
    });

    filterProjectPicker = buildProjectPicker(filterInputs.projectSearch, filterInputs.project, {
      onSelected: project => updateFilter('projectId', String(project.id)),
      onCleared: () => updateFilter('projectId', '')
    });

    overridesProjectPicker = buildProjectPicker(overridesElements.projectSearch, overridesElements.project, {
      onSelected: project => updateOverridesFilter('projectId', String(project.id)),
      onCleared: () => updateOverridesFilter('projectId', '')
    });

    ruleProjectPicker = buildProjectPicker(overridesElements.ruleProjectSearch, overridesElements.ruleProject, {
      onSelected: () => {
        refreshRuleImpact();
        updateRuleActionState();
      },
      onCleared: () => {
        refreshRuleImpact();
        updateRuleActionState();
      }
    });
  }

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
  let listController = null;
  let listSequence = 0;
  let overridesController = null;
  let rulePreviewController = null;
  let editorImpactController = null;
  let editorImpactTimer = null;
  let unitSuggestionController = null;
  let unitSuggestionTimer = null;
  let duplicateCheckController = null;
  let duplicateCheckTimer = null;

  const deleteModalElements = (() => {
    const element = document.querySelector('#pf-delete-modal');
    if (!element) return null;
    return {
      element,
      project: element.querySelector('[data-confirm-project]'),
      date: element.querySelector('[data-confirm-date]'),
      source: element.querySelector('[data-confirm-source]'),
      quantity: element.querySelector('[data-confirm-quantity]'),
      status: element.querySelector('[data-confirm-status]'),
      type: element.querySelector('[data-confirm-type]'),
      confirm: element.querySelector('[data-confirm-accept]'),
      cancel: element.querySelector('[data-confirm-cancel]')
    };
  })();

  let deleteModalTrigger = null;
  const rejectModalElements = (() => {
    const element = document.querySelector('#pf-reject-modal');
    if (!element) return null;
    return {
      element,
      reason: element.querySelector('#pf-reject-reason'),
      error: element.querySelector('#pf-reject-reason-error'),
      confirm: element.querySelector('#pf-reject-confirm')
    };
  })();

  async function readErrorResponse(response, fallback) {
    const text = await response.text().catch(() => '');
    if (!text) return fallback;
    try {
      const payload = JSON.parse(text);
      if (payload?.message) return String(payload.message);
      if (payload?.detail) return String(payload.detail);
      if (payload?.title && payload.title !== 'One or more validation errors occurred.') return String(payload.title);
      if (payload?.errors && typeof payload.errors === 'object') {
        const messages = Object.values(payload.errors)
          .flatMap(value => Array.isArray(value) ? value : [value])
          .map(value => String(value || '').trim())
          .filter(Boolean);
        if (messages.length) return messages.join(' ');
      }
    } catch {
      // Plain-text error response.
    }
    return text || fallback;
  }

  function toast(message, variant = 'success') {
    if (!message || !toastHost) return;
    const wrapper = document.createElement('div');
    wrapper.className = `toast align-items-center text-bg-${variant} border-0`;
    wrapper.role = 'status';

    const layout = document.createElement('div');
    layout.className = 'd-flex';
    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = String(message);
    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'btn-close btn-close-white me-2 m-auto';
    close.setAttribute('data-bs-dismiss', 'toast');
    close.setAttribute('aria-label', 'Close');
    layout.append(body, close);
    wrapper.append(layout);

    toastHost.append(wrapper);
    const instance = bootstrap?.Toast?.getOrCreateInstance(wrapper, { delay: 3500 }) ?? null;
    wrapper.addEventListener('hidden.bs.toast', () => wrapper.remove(), { once: true });
    instance?.show();
  }

  function emitDeepLinkEvent(action, detail = {}) {
    if (!deepLinkContext || !action) return;
    try {
      const payload = { action, ...deepLinkContext, ...detail };
      window.dispatchEvent(new CustomEvent('proliferation:manage:deeplink', { detail: payload }));
    } catch (error) {
      // Ignore analytics issues to avoid blocking UX
    }
  }

  function applyBootFilterDefaults() {
    const context = bootDefaults.filters || {};
    const projectId = context.projectId || '';
    const source = context.source || '';
    const year = context.year || '';
    const kind = context.kind || '';
    if (!projectId && !source && !year && !kind) {
      return;
    }
    filters.projectId = projectId;
    filters.source = source;
    filters.year = year;
    filters.kind = kind;
    filters.search = '';
    pager.page = 1;
    bootFocusPending = true;
    if (!bootEventEmitted) {
      emitDeepLinkEvent('boot');
      bootEventEmitted = true;
    }
    saveFilters();
  }

  function applyBootEditorDefaults() {
    const context = bootDefaults.editor || {};
    if (editor.project && context.projectId && hasOption(editor.project, context.projectId)) {
      editor.project.value = context.projectId;
      editorProjectPicker?.initializeById(context.projectId, { notify: false, dispatch: false }).then(() => markEditorClean());
    }
    if (editor.source && context.source && hasOption(editor.source, context.source)) {
      editor.source.value = context.source;
    }
    if (editor.year && context.year) {
      editor.year.value = context.year;
    }
  }

  function applyBootOverridesDefaults() {
    if (!overridesCard) return;
    const context = bootDefaults.overrides || {};
    const projectId = context.projectId || '';
    const source = context.source || '';
    const year = context.year || '';
    if (!projectId && !source && !year) {
      return;
    }
    overridesState.filters.projectId = projectId;
    overridesState.filters.source = source;
    overridesState.filters.year = year;
    overridesState.filters.search = '';
    overridesState.collapsed = false;
    saveOverrideState();
  }

  function applyBootFocusIfNeeded() {
    if (!bootFocusPending || !listEl) return;
    const targetKind = bootDefaults.filters?.kind || '';
    const selector = targetKind ? `[data-kind="${targetKind}"]` : '[data-id]';
    const button = listEl.querySelector(selector) || listEl.querySelector('[data-id]');
    if (!button) {
      return;
    }
    bootFocusPending = false;
    window.requestAnimationFrame(() => {
      if (typeof button.focus === 'function') {
        button.focus();
      }
    });
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
        filters.status = saved.status ?? '';
        filters.search = saved.search ?? '';
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
        status: filters.status,
        search: filters.search,
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
      const projectId = hasOption(overridesElements.project, overridesState.filters.projectId)
        ? overridesState.filters.projectId
        : '';
      overridesElements.project.value = projectId;
      if (projectId) overridesProjectPicker?.initializeById(projectId, { notify: false, dispatch: false });
      else overridesProjectPicker?.clear({ notify: false, dispatch: false });
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

  function updateOverridesFooterVisibility() {
    if (!overridesElements.footer) return;
    const hasSummary = Boolean(overridesElements.summary?.textContent?.trim());
    const hasReset = Boolean(overridesElements.reset && !overridesElements.reset.classList.contains('d-none'));
    overridesElements.footer.classList.toggle('d-none', !hasSummary && !hasReset);
  }

  function updateOverridesResetVisibility() {
    if (overridesElements.reset) {
      overridesElements.reset.classList.toggle('d-none', !overridesFiltersActive());
    }
    updateOverridesFooterVisibility();
  }

  function updateOverridesExportAvailability(enabled) {
    if (!overridesElements.export) return;
    const shouldDisable = !enabled;
    overridesElements.export.disabled = shouldDisable;
    overridesElements.export.setAttribute('aria-disabled', shouldDisable ? 'true' : 'false');
    overridesElements.export.classList.toggle('disabled', shouldDisable);
  }

  function setOverridesCollapsed(collapsed, persist = true) {
    if (overridesElements.collapse) {
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
    }

    manageLayout?.classList.toggle('pf-manage-layout--rail-collapsed', Boolean(collapsed));

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

  function getScopeLabel(kind) {
    return kind === 'yearly' ? 'Annual quantity' : 'Detailed entry';
  }

  function setCommandTitle(text) {
    if (commandElements.title) commandElements.title.textContent = text || 'Current record';
  }

  function setCommandScope(kind) {
    if (!commandElements.scope) return;
    const normalized = kind === 'yearly' ? 'yearly' : 'granular';
    commandElements.scope.textContent = getScopeLabel(normalized);
    commandElements.scope.dataset.scope = normalized;
  }

  function setCommandUpdated(value) {
    if (!commandElements.updated) return;
    const text = value ? formatDateTime(value) : '';
    commandElements.updated.textContent = text || '—';
    commandElements.updatedWrap?.classList.toggle('d-none', !text);
    if (value) {
      commandElements.updated.setAttribute('data-timestamp', value);
    } else {
      commandElements.updated.removeAttribute('data-timestamp');
    }
  }

  function getStatusBadgeConfig(status) {
    switch (status) {
      case 'approved':
        return { text: 'Approved', className: 'text-bg-success' };
      case 'rejected':
        return { text: 'Rejected', className: 'text-bg-danger' };
      case 'pending':
        return { text: 'Pending', className: 'text-bg-warning text-dark' };
      default:
        return { text: 'Awaiting selection', className: 'text-bg-secondary' };
    }
  }

  function resetApprovalUi() {
    approvalElements.container?.classList.add('d-none');
    if (approvalElements.badge) {
      const { text, className } = getStatusBadgeConfig('');
      approvalElements.badge.textContent = text;
      approvalElements.badge.className = `badge ${className}`;
    }
    if (approvalElements.status) {
      approvalElements.status.textContent = 'Select a record to review approval status.';
    }
    if (approvalElements.submitted) {
      approvalElements.submitted.textContent = 'Submitted: —';
    }
    if (approvalElements.updated) {
      approvalElements.updated.textContent = 'Last updated: —';
    }
    if (approvalElements.decided) {
      approvalElements.decided.textContent = 'Decision: —';
      approvalElements.decided.hidden = true;
    }
  }

  function updateApprovalUi(detail) {
    if (!approvalElements.container) return;
    if (!detail) {
      resetApprovalUi();
      return;
    }

    approvalElements.container.classList.remove('d-none');
    const statusRaw = (detail.approvalStatus ?? detail.ApprovalStatus ?? '').toString().toLowerCase();
    const { text, className } = getStatusBadgeConfig(statusRaw || 'pending');
    if (approvalElements.badge) {
      approvalElements.badge.textContent = text;
      approvalElements.badge.className = `badge ${className}`;
    }

    if (approvalElements.status) {
      if (statusRaw === 'approved') {
        approvalElements.status.textContent = 'Approved';
      } else if (statusRaw === 'rejected') {
        approvalElements.status.textContent = 'Rejected';
      } else {
        approvalElements.status.textContent = 'Pending approval';
      }
    }

    const createdOn = detail.createdOnUtc ?? detail.CreatedOnUtc;
    if (approvalElements.submitted) {
      approvalElements.submitted.textContent = createdOn
        ? `Submitted: ${formatDateTime(createdOn)}`
        : 'Submitted: —';
    }

    const lastUpdated = detail.lastUpdatedOnUtc ?? detail.LastUpdatedOnUtc;
    if (approvalElements.updated) {
      approvalElements.updated.textContent = lastUpdated
        ? `Last updated: ${formatDateTime(lastUpdated)}`
        : 'Last updated: —';
    }

    if (approvalElements.decided) {
      const decidedOn = detail.approvedOnUtc ?? detail.ApprovedOnUtc;
      if (decidedOn) {
        const label = statusRaw === 'approved' ? 'Approved on' : 'Decided on';
        approvalElements.decided.textContent = `${label}: ${formatDateTime(decidedOn)}`;
        approvalElements.decided.hidden = false;
      } else if (statusRaw === 'rejected') {
        approvalElements.decided.textContent = 'Decision: Rejected';
        approvalElements.decided.hidden = false;
      } else {
        approvalElements.decided.textContent = 'Decision: —';
        approvalElements.decided.hidden = true;
      }
    }
  }

  function getSaveActionText() {
    const status = (currentRecord.status || '').toLowerCase();
    if (!currentRecord.id) return 'Save';
    if (status === 'approved') return canApproveRecords ? 'Save correction' : 'Submit amendment';
    if (status === 'rejected') return 'Resubmit';
    return 'Save changes';
  }

  function updateContextualActions() {
    const hasRecord = Boolean(currentRecord.id);
    const status = (currentRecord.status || '').toLowerCase();
    const pending = hasRecord && status === 'pending';

    if (decisionButtons.approve) {
      const visible = canApproveRecords && pending;
      decisionButtons.approve.classList.toggle('d-none', !visible);
      decisionButtons.approve.disabled = !visible || decisionButtons.busy;
      decisionButtons.approve.setAttribute('aria-disabled', decisionButtons.approve.disabled ? 'true' : 'false');
    }
    if (decisionButtons.reject) {
      const visible = canApproveRecords && pending;
      decisionButtons.reject.classList.toggle('d-none', !visible);
      decisionButtons.reject.disabled = !visible || decisionButtons.busy;
      decisionButtons.reject.setAttribute('aria-disabled', decisionButtons.reject.disabled ? 'true' : 'false');
    }
    if (editor.btnDelete) {
      const visible = !isApprovalWorkspace() && hasRecord && (status !== 'approved' || canApproveRecords);
      editor.btnDelete.classList.toggle('d-none', !visible);
      editor.btnDelete.disabled = !visible;
    }
    if (editor.btnSave) {
      const visible = !isApprovalWorkspace();
      editor.btnSave.classList.toggle('d-none', !visible);
      if (!visible) editor.btnSave.disabled = true;
    }

    const label = getSaveActionText();
    saveButtonState.defaultContent = `<i class="bi bi-check2" aria-hidden="true"></i> <span data-save-label>${label}</span>`;
    if (!saveButtonState.busy && editor.btnSave) {
      editor.btnSave.innerHTML = saveButtonState.defaultContent;
    }
  }

  function updateDecisionButtons() {
    updateContextualActions();
  }

  function setDecisionBusy(busy) {
    decisionButtons.busy = Boolean(busy);
    updateDecisionButtons();
  }

  function applyFiltersToInputs() {
    if (filterInputs.project) {
      const projectId = hasOption(filterInputs.project, filters.projectId) ? filters.projectId : '';
      filterInputs.project.value = projectId;
      if (projectId) filterProjectPicker?.initializeById(projectId, { notify: false, dispatch: false });
      else filterProjectPicker?.clear({ notify: false, dispatch: false });
    }
    if (filterInputs.source) {
      filterInputs.source.value = hasOption(filterInputs.source, filters.source) ? filters.source : '';
    }
    if (filterInputs.kind) {
      filterInputs.kind.value = hasOption(filterInputs.kind, filters.kind) ? filters.kind : '';
    }
    if (filterInputs.status) {
      filterInputs.status.value = hasOption(filterInputs.status, filters.status) ? filters.status : '';
    }
    if (filterInputs.year) {
      filterInputs.year.value = filters.year ?? '';
    }
    if (filterInputs.search) {
      filterInputs.search.value = filters.search ?? '';
    }
  }

  function hasOption(select, value) {
    if (!select || value === undefined || value === null) return false;
    return Array.from(select.options).some((opt) => opt.value === String(value));
  }

  function getOptionLabel(select, value) {
    if (!select || !value) return '';
    const option = Array.from(select.options).find((opt) => opt.value === String(value));
    return option ? option.textContent.trim() : '';
  }

  function normalizeSourceToken(value) {
    return String(value ?? '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]/g, '');
  }

  function resolveSourceSelectValue(detail) {
    if (!editor.source || !detail) return '';
    const rawCandidates = [
      detail.sourceValue,
      detail.SourceValue,
      detail.source,
      detail.Source,
      detail.sourceLabel,
      detail.SourceLabel
    ].filter((value) => value !== undefined && value !== null && String(value).trim() !== '');

    for (const candidate of rawCandidates) {
      if (hasOption(editor.source, candidate)) return String(candidate);
    }

    const aliases = new Map([
      ['1', '1'],
      ['sdd', '1'],
      ['simulatordevelopmentdivision', '1'],
      ['2', '2'],
      ['abw515', '2'],
      ['515abw', '2'],
      ['armybaseworkshop515', '2']
    ]);

    for (const candidate of rawCandidates) {
      const token = normalizeSourceToken(candidate);
      const aliased = aliases.get(token);
      if (aliased && hasOption(editor.source, aliased)) return aliased;

      const matchingOption = Array.from(editor.source.options).find((option) =>
        normalizeSourceToken(option.textContent) === token);
      if (matchingOption) return matchingOption.value;
    }

    return '';
  }

  function renderFilterChips() {
    const host = filterInputs.chips;
    if (!host) return;
    host.innerHTML = '';
    const chips = [];
    if (filters.projectId) {
      const label = getOptionLabel(filterInputs.project, filters.projectId) || `Project ${filters.projectId}`;
      chips.push({ key: 'project', label: 'Project', value: label });
    }
    if (filters.source) {
      const label = getOptionLabel(filterInputs.source, filters.source) || filters.source;
      chips.push({ key: 'source', label: 'Source', value: label });
    }
    if (filters.kind) {
      const label = getOptionLabel(filterInputs.kind, filters.kind) || (filters.kind === 'granular' ? 'Detailed entry' : 'Annual quantity');
      chips.push({ key: 'kind', label: 'Type', value: label });
    }
    if (filters.status) {
      const label =
        getOptionLabel(filterInputs.status, filters.status) ||
        (filters.status.charAt(0).toUpperCase() + filters.status.slice(1));
      chips.push({ key: 'status', label: 'Status', value: label });
    }
    if (filters.year) {
      chips.push({ key: 'year', label: 'Year', value: filters.year });
    }
    if (filters.search) {
      chips.push({ key: 'search', label: 'Search', value: `"${filters.search}"` });
    }

    for (const chip of chips) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'btn btn-sm btn-outline-secondary d-inline-flex align-items-center gap-2';
      button.dataset.filterKey = chip.key;
      const labelSpan = document.createElement('span');
      labelSpan.textContent = `${chip.label}: ${chip.value}`;
      const closeSpan = document.createElement('span');
      closeSpan.setAttribute('aria-hidden', 'true');
      closeSpan.textContent = '×';
      const srSpan = document.createElement('span');
      srSpan.className = 'visually-hidden';
      srSpan.textContent = `Remove ${chip.label.toLowerCase()} filter`;
      button.append(labelSpan, closeSpan, srSpan);
      host.append(button);
    }
  }

  function updateFilter(key, value) {
    const normalized = value ?? '';
    if (filters[key] === normalized) return;
    filters[key] = normalized;
    pager.page = 1;
    saveFilters();
    renderFilterChips();
    fetchList();
  }

  function resetFilters() {
    filters.projectId = '';
    filters.source = '';
    filters.year = '';
    filters.kind = '';
    filters.status = isApprovalWorkspace() ? 'pending' : '';
    filters.search = '';
    pager.page = 1;
    applyFiltersToInputs();
    if (filterSearchTimer) {
      clearTimeout(filterSearchTimer);
      filterSearchTimer = null;
    }
    saveFilters();
    renderFilterChips();
    fetchList();
  }

  function clearFilter(key) {
    switch (key) {
      case 'project':
        if (filterInputs.project) filterInputs.project.value = '';
        filterProjectPicker?.clear({ notify: false, dispatch: false });
        updateFilter('projectId', '');
        break;
      case 'source':
        if (filterInputs.source) filterInputs.source.value = '';
        updateFilter('source', '');
        break;
      case 'kind':
        if (filterInputs.kind) filterInputs.kind.value = '';
        updateFilter('kind', '');
        break;
      case 'status':
        if (isApprovalWorkspace()) {
          if (filterInputs.status) filterInputs.status.value = 'pending';
          updateFilter('status', 'pending');
        } else {
          if (filterInputs.status) filterInputs.status.value = '';
          updateFilter('status', '');
        }
        break;
      case 'year':
        if (filterInputs.year) filterInputs.year.value = '';
        updateFilter('year', '');
        break;
      case 'search':
        if (filterInputs.search) filterInputs.search.value = '';
        if (filterSearchTimer) {
          clearTimeout(filterSearchTimer);
          filterSearchTimer = null;
        }
        updateFilter('search', '');
        break;
      default:
        break;
    }
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

  function updateOverridesSummary(stats = null) {
    if (!overridesElements.summary) return;
    const count = overridesRows.size;
    if (count === 0) {
      overridesElements.summary.textContent = '';
      updateOverridesFooterVisibility();
      return;
    }

    const detailParts = [];
    if (stats) {
      const breakdown = [];
      if (stats.both) breakdown.push(`${stats.both} with both data types`);
      if (stats.granularOnly) breakdown.push(`${stats.granularOnly} detailed-only`);
      if (stats.yearlyOnly) breakdown.push(`${stats.yearlyOnly} annual-only`);
      if (stats.none) breakdown.push(`${stats.none} without approved data`);
      if (stats.autoFallback) breakdown.push(`${stats.autoFallback} auto fallback`);
      if (breakdown.length) {
        detailParts.push(`Breakdown: ${breakdown.join(', ')}.`);
      }
    }

    const latestRow = findLatestOverrideRow();
    if (latestRow) {
      const when = formatDateTime(latestRow.setOnUtc);
      const actor = latestRow.setByDisplayName || latestRow.setByUserId || '';
      if (when && actor) {
        detailParts.push(`Last updated ${when} by ${actor}.`);
      } else if (when) {
        detailParts.push(`Last updated ${when}.`);
      } else if (actor) {
        detailParts.push(`Last updated by ${actor}.`);
      }
    }

    let summary = count === 1 ? '1 counting exception loaded.' : `${count} counting exceptions loaded.`;
    if (detailParts.length) {
      summary = `${summary} ${detailParts.join(' ')}`;
    }
    overridesElements.summary.textContent = summary;
    updateOverridesFooterVisibility();
  }

  function renderOverrides(rows) {
    if (!overridesElements.tableBody) return;
    overridesRows.clear();

    if (!rows || rows.length === 0) {
      overridesElements.tableBody.innerHTML = '<tr><td colspan="6" class="text-muted">No counting exceptions configured.</td></tr>';
      updateOverridesSummary();
      updateOverridesExportAvailability(false);
      updateRuleActionState();
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
      const projectCode = row.projectCode ? `<div class="small text-muted">${escapeHtml(row.projectCode)}</div>` : '';
      const scope = `<div class="small text-muted">${escapeHtml(row.sourceLabel)} · ${escapeHtml(row.year)}</div>`;
      const updated = formatDateTime(row.setOnUtc);
      const coverageBadges = [];
      if (row.hasYearly && row.hasGranular) {
        stats.both += 1;
        coverageBadges.push('<span class="badge text-bg-success">Annual + detailed records present</span>');
      } else if (row.hasGranular) {
        stats.granularOnly += 1;
        coverageBadges.push('<span class="badge text-bg-primary">Detailed records present</span>');
        coverageBadges.push('<span class="badge text-bg-light text-wrap">No annual quantity</span>');
      } else if (row.hasYearly) {
        stats.yearlyOnly += 1;
        coverageBadges.push('<span class="badge text-bg-primary">Annual quantity present</span>');
        coverageBadges.push('<span class="badge text-bg-light text-wrap">No detailed entries</span>');
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
      const actions = `
        <div class="btn-group btn-group-sm" role="group">
          <button type="button" class="btn btn-outline-secondary" data-action="focus-list" data-id="${escapeHtml(row.id)}">Records</button>
          <button type="button" class="btn btn-outline-primary" data-action="edit-rule" data-id="${escapeHtml(row.id)}">Edit rule</button>
          <button type="button" class="btn btn-outline-secondary" data-action="project" data-id="${escapeHtml(row.id)}">Project total</button>
          <button type="button" class="btn btn-outline-danger" data-action="clear" data-id="${escapeHtml(row.id)}">Use default</button>
        </div>`;

      return `
        <tr>
          <td>
            <div class="fw-semibold">${escapeHtml(row.projectName)}</div>
            ${projectCode}
          </td>
          <td>${scope}</td>
          <td>
            <div class="d-flex flex-wrap gap-1">${coverageBadges.join('')}</div>
          </td>
          <td>
            <div class="fw-semibold">${escapeHtml(row.modeLabel)}</div>
            <div class="small text-muted">Effective: ${escapeHtml(row.effectiveModeLabel)}</div>
            <div class="small">Total in play: <span class="fw-semibold">${escapeHtml(effectiveTotalDisplay)}</span></div>
          </td>
          <td>
            <div class="fw-semibold">${escapeHtml(row.setByDisplayName)}</div>
            <div class="small text-muted">${escapeHtml(updated)}</div>
          </td>
          <td class="text-end">${actions}</td>
        </tr>`;
    }).join('');

    overridesElements.tableBody.innerHTML = markup || '<tr><td colspan="6" class="text-muted">No counting exceptions configured.</td></tr>';
    updateOverridesSummary(stats);
    updateOverridesExportAvailability(overridesRows.size > 0);
    updateRuleActionState();
  }

  async function fetchOverrides() {
    if (!overridesCard || !overridesElements.tableBody) return;
    overridesController?.abort();
    overridesController = new AbortController();
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
      const response = await fetch(`${api.overrides}?${params.toString()}`, { headers: { Accept: 'application/json' }, signal: overridesController.signal });
      if (!response.ok) {
        throw new Error(await readErrorResponse(response, 'Unable to load counting exceptions'));
      }
      const data = await response.json();
      const rows = Array.isArray(data) ? data : [];
      renderOverrides(rows);
    } catch (error) {
      if (error.name === 'AbortError') return;
      overridesElements.tableBody.innerHTML = '<tr><td colspan="6" class="text-danger">Failed to load counting exceptions.</td></tr>';
      if (overridesElements.summary) {
        overridesElements.summary.textContent = '';
      }
      updateOverridesExportAvailability(false);
      toast(error.message || 'Unable to load counting exceptions', 'danger');
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
    filters.search = '';
    pager.page = 1;
    applyFiltersToInputs();
    if (filterSearchTimer) {
      clearTimeout(filterSearchTimer);
      filterSearchTimer = null;
    }
    saveFilters();
    renderFilterChips();
    fetchList();
    listCard?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    toast('List filters updated for this counting exception.', 'info');
  }

  function getSourceDefaultMode(sourceValue) {
    return Number(sourceValue) === 2 ? 'UseYearly' : 'UseYearlyAndGranular';
  }

  function calculateRuleTotal(mode, annual, detailed) {
    if (mode === 'UseYearly') return annual;
    if (mode === 'UseGranular') return detailed;
    if (mode === 'Auto') return detailed > 0 ? detailed : annual;
    return annual + detailed;
  }

  function getEditorTargetYear() {
    if (editor.kind?.value === 'yearly') {
      return Number(editor.year?.value || 0);
    }
    const date = editor.date?.value || '';
    return /^\d{4}-/.test(date) ? Number(date.slice(0, 4)) : 0;
  }

  function hideEditorImpact() {
    editor.impact?.classList.add('d-none');
  }

  async function refreshEditorImpact() {
    const projectId = Number(editor.project?.value || 0);
    const source = Number(editor.source?.value || 0);
    const year = getEditorTargetYear();
    const quantity = Number(editor.qty?.value || 0);
    const kind = editor.kind?.value === 'yearly' ? 'yearly' : 'granular';
    if (!editor.impact || projectId <= 0 || ![1, 2].includes(source) || year < 2000 || !Number.isFinite(quantity) || quantity < (kind === 'granular' ? 1 : 0)) {
      hideEditorImpact();
      return;
    }

    editorImpactController?.abort();
    editorImpactController = new AbortController();
    try {
      const params = new URLSearchParams({ projectId: String(projectId), source: String(source), year: String(year), page: '1', pageSize: '10' });
      const response = await fetch(`${api.groups}?${params}`, { headers: { Accept: 'application/json' }, signal: editorImpactController.signal });
      if (!response.ok) throw new Error('Unable to load calculation preview');
      const data = await response.json();
      const row = Array.isArray(data.items) ? data.items.find((x) => Number(x.projectId) === projectId && Number(x.source) === source && Number(x.year) === year) : null;
      let annual = Number(row?.annualQuantity || 0);
      let detailed = Number(row?.detailedQuantity || 0);
      const current = Number(row?.reportedTotal || 0);
      const sameCombination = currentRecord.id &&
        Number(currentRecord.projectId) === projectId &&
        Number(currentRecord.source) === source &&
        Number(currentRecord.year) === year;
      if (sameCombination && (currentRecord.status || '').toLowerCase() === 'approved') {
        const previousQuantity = Number(currentRecord.quantity || 0);
        if (currentRecord.kind === 'yearly') annual = Math.max(0, annual - previousQuantity);
        else detailed = Math.max(0, detailed - previousQuantity);
      }
      if (kind === 'yearly') annual += quantity;
      else detailed += quantity;
      const mode = row?.effectiveMode || getSourceDefaultMode(source);
      const afterApproval = calculateRuleTotal(mode, annual, detailed);
      if (editor.impactCurrent) editor.impactCurrent.textContent = formatNumber(current);
      if (editor.impactNext) editor.impactNext.textContent = formatNumber(afterApproval);
      if (editor.impactNextLabel) editor.impactNextLabel.textContent = canApproveRecords ? 'After save' : 'After approval';
      if (editor.impactNote) {
        const hasBothRecordTypes = Number(row?.annualQuantity || 0) > 0 && Number(row?.detailedQuantity || 0) > 0;
        const modeLabel = row?.effectiveModeLabel || ({
          UseYearly: 'Annual quantity only',
          UseGranular: 'Detailed entries only',
          UseYearlyAndGranular: 'Annual quantity + detailed entries',
          Auto: 'Detailed entries where available; otherwise annual quantity'
        }[mode] || mode);
        const quantityIsCounted = kind === 'yearly'
          ? mode === 'UseYearly' || mode === 'UseYearlyAndGranular' || (mode === 'Auto' && detailed <= 0)
          : mode === 'UseGranular' || mode === 'UseYearlyAndGranular' || mode === 'Auto';
        const approvalTiming = canApproveRecords
          ? 'The saved record will be approved immediately.'
          : 'The reported total changes only after approval.';
        editor.impactNote.textContent = hasBothRecordTypes
          ? `Annual and detailed records already exist for ${year}. Counting rule: ${modeLabel}. This quantity ${quantityIsCounted ? 'will' : 'will not'} affect the reported total. ${approvalTiming}`
          : `${modeLabel}. This quantity ${quantityIsCounted ? 'will' : 'will not'} affect the reported total. ${approvalTiming}`;
      }
      editor.impact.classList.remove('d-none');
    } catch (error) {
      if (error.name === 'AbortError') return;
      hideEditorImpact();
    }
  }

  function scheduleEditorImpact() {
    window.clearTimeout(editorImpactTimer);
    editorImpactTimer = window.setTimeout(refreshEditorImpact, 250);
  }

  function hideDuplicateWarning() {
    editor.duplicateWarning?.classList.add('d-none');
  }

  function normaliseComparableText(value) {
    return String(value ?? '').trim().replace(/\s+/g, ' ').toLocaleLowerCase();
  }

  function scheduleDuplicateCheck() {
    window.clearTimeout(duplicateCheckTimer);
    hideDuplicateWarning();
    if (editor.kind?.value !== 'granular') return;
    duplicateCheckTimer = window.setTimeout(checkForDuplicateEntry, 260);
  }

  async function checkForDuplicateEntry() {
    const projectId = Number(editor.project?.value || 0);
    const source = Number(editor.source?.value || 0);
    const date = editor.date?.value || '';
    const unit = normaliseComparableText(editor.unit?.value);
    const quantity = Number(editor.qty?.value || 0);
    if (projectId <= 0 || source <= 0 || !date || !unit || quantity <= 0) return;

    duplicateCheckController?.abort();
    duplicateCheckController = new AbortController();
    const params = new URLSearchParams({
      projectId: String(projectId),
      source: String(source),
      year: date.slice(0, 4),
      kind: 'granular',
      approvalStatus: 'approved',
      page: '1',
      pageSize: '100',
      search: editor.unit?.value?.trim() || ''
    });

    try {
      const response = await fetch(`${api.list}?${params}`, {
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
        signal: duplicateCheckController.signal
      });
      if (!response.ok) return;
      const payload = await response.json();
      const duplicate = (Array.isArray(payload?.items) ? payload.items : []).some(item => {
        if (String(item.id || '') === String(editor.id?.value || '')) return false;
        return formatDate(item.proliferationDateUtc) === date
          && normaliseComparableText(item.unitName) === unit
          && Number(item.quantity) === quantity
          && String(item.approvalStatus || '').toLowerCase() === 'approved';
      });
      editor.duplicateWarning?.classList.toggle('d-none', !duplicate);
    } catch (error) {
      if (error?.name !== 'AbortError') hideDuplicateWarning();
    }
  }

  async function refreshUnitSuggestions() {
    if (!editor.unitSuggestions || editor.kind?.value !== 'granular') return;
    const query = editor.unit?.value?.trim() || '';
    unitSuggestionController?.abort();
    unitSuggestionController = new AbortController();
    try {
      const params = new URLSearchParams({ take: '20' });
      if (query) params.set('q', query);
      const response = await fetch(`${api.unitSuggestions}?${params}`, { headers: { Accept: 'application/json' }, signal: unitSuggestionController.signal });
      if (!response.ok) return;
      const rows = await response.json();
      editor.unitSuggestions.replaceChildren();
      (Array.isArray(rows) ? rows : []).forEach((value) => {
        const option = document.createElement('option');
        option.value = value;
        editor.unitSuggestions.append(option);
      });
    } catch (error) {
      if (error.name !== 'AbortError') editor.unitSuggestions?.replaceChildren();
    }
  }

  async function refreshRuleImpact() {
    const projectId = Number(overridesElements.ruleProject?.value || 0);
    const source = Number(overridesElements.ruleSource?.value || 0);
    const year = Number(overridesElements.ruleYear?.value || 0);
    const host = overridesElements.ruleImpact;
    if (!host || !Number.isInteger(projectId) || projectId <= 0 || ![1, 2].includes(source) || year < 2000) {
      host?.classList.add('d-none');
      return;
    }
    rulePreviewController?.abort();
    rulePreviewController = new AbortController();
    host.classList.remove('d-none');
    host.textContent = 'Loading calculation impact…';
    try {
      const qs = new URLSearchParams({ projectId: String(projectId), source: String(source), year: String(year), page: '1', pageSize: '1' });
      const response = await fetch(`${api.groups}?${qs}`, { headers: { Accept: 'application/json' }, signal: rulePreviewController.signal });
      if (!response.ok) throw new Error('Unable to load calculation');
      const data = await response.json();
      const row = Array.isArray(data.items) ? data.items[0] : null;
      const annual = Number(row?.annualQuantity || 0);
      const detailed = Number(row?.detailedQuantity || 0);
      const current = Number(row?.reportedTotal || 0);
      const selected = overridesElements.ruleMode?.value || 'default';
      const mode = selected === 'default' ? getSourceDefaultMode(source) : selected;
      const next = calculateRuleTotal(mode, annual, detailed);
      host.replaceChildren();
      const title = document.createElement('strong');
      title.textContent = 'Calculation impact';
      const text = document.createElement('span');
      text.textContent = `Annual ${formatNumber(annual)} · detailed ${formatNumber(detailed)} · current reported total ${formatNumber(current)} · after change ${formatNumber(next)}`;
      host.append(title, text);
    } catch (error) {
      if (error.name === 'AbortError') return;
      host.textContent = 'Calculation impact could not be loaded.';
    }
  }

  function findRuleScopeOverride() {
    const projectId = Number(overridesElements.ruleProject?.value || 0);
    const source = Number(overridesElements.ruleSource?.value || 0);
    const year = Number(overridesElements.ruleYear?.value || 0);
    if (!Number.isInteger(projectId) || projectId <= 0 || ![1, 2].includes(source) || year < 2000) {
      return null;
    }
    return Array.from(overridesRows.values()).find((row) =>
      Number(row.projectId) === projectId &&
      Number(row.sourceValue ?? row.source) === source &&
      Number(row.year) === year) || null;
  }

  function updateRuleActionState() {
    const button = overridesElements.ruleSave;
    if (!button) return;
    const projectId = Number(overridesElements.ruleProject?.value || 0);
    const source = Number(overridesElements.ruleSource?.value || 0);
    const year = Number(overridesElements.ruleYear?.value || 0);
    const selectedMode = overridesElements.ruleMode?.value || 'default';
    const reason = overridesElements.ruleReason?.value?.trim() || '';
    const validScope = Number.isInteger(projectId) && projectId > 0 && [1, 2].includes(source) && Number.isInteger(year) && year >= 2000 && year <= 3000;
    const existingOverride = validScope ? findRuleScopeOverride() : null;
    const restoringDefault = selectedMode === 'default';
    const visible = validScope && (!restoringDefault || Boolean(existingOverride));
    const enabled = visible && (restoringDefault || Boolean(reason));

    button.classList.toggle('d-none', !visible);
    button.disabled = !enabled;
    button.setAttribute('aria-disabled', enabled ? 'false' : 'true');
    button.textContent = restoringDefault ? 'Restore source default' : 'Save exception';
  }

  function updateRuleGuidance() {
    if (!overridesElements.ruleSource) return;
    const isAbw = Number(overridesElements.ruleSource.value) === 2;
    if (overridesElements.ruleDefaultBadge) {
      overridesElements.ruleDefaultBadge.textContent = isAbw
        ? '515 ABW default: annual quantity'
        : 'SDD default: annual + detailed';
    }
    const selectedMode = overridesElements.ruleMode?.value || 'default';
    if (overridesElements.ruleGuidance) {
      if (selectedMode === 'default') {
        overridesElements.ruleGuidance.textContent = isAbw
          ? '515 ABW normally counts the approved annual quantity. Detailed entries remain available for reference.'
          : 'SDD normally adds the annual quantity and approved detailed entries.';
      } else {
        overridesElements.ruleGuidance.textContent = 'This creates a deliberate exception for the selected project, source and year.';
      }
    }
    const isException = selectedMode !== 'default';
    overridesElements.ruleReasonWrap?.classList.toggle('d-none', !isException);
    if (overridesElements.ruleReason) {
      overridesElements.ruleReason.required = isException;
      overridesElements.ruleReason.classList.remove('is-invalid');
      if (!isException) overridesElements.ruleReason.value = '';
    }
    updateRuleActionState();
    refreshRuleImpact();
  }

  function editRuleFromOverride(row) {
    if (!row || !overridesElements.ruleEditor) return;
    if (overridesElements.ruleProject) {
      ensureFilterOption(overridesElements.ruleProject, row.projectId, row.projectName);
      overridesElements.ruleProject.value = String(row.projectId ?? '');
      ruleProjectPicker?.initializeById(row.projectId, { notify: false, dispatch: false });
    }
    if (overridesElements.ruleSource && row.sourceValue !== null && row.sourceValue !== undefined) {
      overridesElements.ruleSource.value = String(row.sourceValue);
    }
    if (overridesElements.ruleYear) {
      overridesElements.ruleYear.value = String(row.year ?? defaults.year);
    }
    if (overridesElements.ruleMode) {
      const allowed = ['UseYearlyAndGranular', 'UseYearly', 'UseGranular', 'Auto'];
      overridesElements.ruleMode.value = allowed.includes(row.mode) ? row.mode : 'default';
    }
    if (overridesElements.ruleReason) overridesElements.ruleReason.value = '';
    updateRuleGuidance();
    overridesElements.ruleEditor.scrollIntoView({ behavior: 'smooth', block: 'center' });
    overridesElements.ruleMode?.focus();
  }

  function openProjectTotal(row) {
    const projectId = Number(row?.projectId);
    if (!Number.isInteger(projectId) || projectId <= 0) return;
    window.open(`/ProjectOfficeReports/Proliferation/Project/${encodeURIComponent(projectId)}`, '_blank', 'noopener');
  }

  async function saveCountingRule() {
    const projectId = Number(overridesElements.ruleProject?.value || 0);
    const source = Number(overridesElements.ruleSource?.value || 0);
    const year = Number(overridesElements.ruleYear?.value || 0);
    const selected = overridesElements.ruleMode?.value || 'default';
    const reason = overridesElements.ruleReason?.value?.trim() || '';

    if (!Number.isInteger(projectId) || projectId <= 0) {
      toast('Select a project for the counting rule.', 'warning');
      (overridesElements.ruleProjectSearch || overridesElements.ruleProject)?.focus();
      return;
    }
    if (![1, 2].includes(source)) {
      toast('Select a valid source.', 'warning');
      overridesElements.ruleSource?.focus();
      return;
    }
    if (!Number.isInteger(year) || year < 2000 || year > 3000) {
      toast('Enter a valid four digit year.', 'warning');
      overridesElements.ruleYear?.focus();
      return;
    }

    if (selected !== 'default' && !reason) {
      toast('Enter a reason for the counting exception.', 'warning');
      overridesElements.ruleReason?.classList.add('is-invalid');
      overridesElements.ruleReason?.focus();
      return;
    }

    const mode = selected === 'default' ? getSourceDefaultMode(source) : selected;
    const allowed = ['UseYearlyAndGranular', 'UseYearly', 'UseGranular', 'Auto'];
    if (!allowed.includes(mode)) {
      toast('Select a valid counting rule.', 'warning');
      overridesElements.ruleMode?.focus();
      return;
    }

    const button = overridesElements.ruleSave;
    const original = button?.innerHTML || 'Save counting rule';
    if (button) {
      button.disabled = true;
      button.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Saving…';
    }

    try {
      const response = await fetch(api.setPreference, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getCsrfToken() },
        body: JSON.stringify({ projectId, source, year, mode, reason: reason || null })
      });
      if (!response.ok) {
        throw new Error(await readErrorResponse(response, 'Unable to save the counting rule.'));
      }

      toast(selected === 'default' ? 'Source default restored.' : 'Counting rule saved.', 'success');
      if (overridesElements.ruleReason) overridesElements.ruleReason.value = '';
      await fetchOverrides();
      await refreshRuleImpact();
    } catch (error) {
      toast(error.message || 'Unable to save the counting rule.', 'danger');
    } finally {
      if (button) {
        button.innerHTML = original;
        updateRuleActionState();
      }
    }
  }

  async function clearOverride(row) {
    if (!row) return;
    const confirmed = window.confirm('Return this project-year to its source default counting rule?');
    if (!confirmed) return;
    try {
      const payload = {
        projectId: row.projectId,
        source: row.sourceValue ?? row.source,
        year: row.year,
        mode: Number(row.sourceValue ?? row.source) === 2 ? 'UseYearly' : 'UseYearlyAndGranular',
        reason: 'Restored source default'
      };
      const response = await fetch(api.setPreference, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getCsrfToken() },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        const message = await readErrorResponse(response, 'Unable to restore the source default');
        if (response.status === 403) {
          openOverview(row, '#preferences');
          throw new Error(message || 'You do not have permission to change this counting rule.');
        }
        throw new Error(message);
      }
      toast('Counting exception cleared.', 'success');
      emitDeepLinkEvent('override-cleared', {
        targetProjectId: row.projectId ? String(row.projectId) : '',
        targetSource: row.sourceValue !== null && row.sourceValue !== undefined
          ? String(row.sourceValue)
          : String(row.source ?? ''),
        targetYear: row.year ? String(row.year) : ''
      });
      fetchOverrides();
    } catch (error) {
      toast(error.message || 'Unable to restore the source default', 'danger');
    }
  }

  function confirmDeletion(details = {}) {
    const fallbackType = details.type === 'yearly' ? 'annual quantity' : 'record';
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
      const typeLabel = details.type === 'yearly' ? 'annual quantity' : 'record';
      deleteModalElements.type.textContent = typeLabel;
    }
    if (deleteModalElements.project) {
      deleteModalElements.project.textContent = details.project || 'this project';
    }
    if (deleteModalElements.date) deleteModalElements.date.textContent = details.dateOrYear || 'the selected period';
    if (deleteModalElements.source) deleteModalElements.source.textContent = details.source || '—';
    if (deleteModalElements.quantity) deleteModalElements.quantity.textContent = details.quantity || '—';
    if (deleteModalElements.status) deleteModalElements.status.textContent = details.status || '—';

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
    const sequence = ++listSequence;
    listController?.abort();
    listController = new AbortController();
    const params = new URLSearchParams();
    if (filters.projectId) params.set('projectId', filters.projectId);
    if (filters.source) params.set('source', filters.source);
    if (filters.year) params.set('year', filters.year);
    if (filters.kind) params.set('kind', filters.kind);
    if (filters.status) params.set('approvalStatus', filters.status);
    if (filters.search) params.set('search', filters.search);
    params.set('page', String(pager.page));
    params.set('pageSize', String(pager.pageSize));

    listEl.innerHTML = '<div class="list-group-item text-muted" data-placeholder>Loading…</div>';
    countEl.textContent = '';

    try {
      const response = await fetch(`${api.list}?${params.toString()}`, { headers: { Accept: 'application/json' }, signal: listController.signal });
      if (sequence !== listSequence) return;
      if (!response.ok) {
        throw new Error(await readErrorResponse(response, 'Unable to load list'));
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
      if (error.name === 'AbortError') return;
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
    applyBootOverridesDefaults();
    applyOverrideFiltersToInputs();
    updateOverridesResetVisibility();
    setOverridesCollapsed(overridesState.collapsed, false);

    if (overridesElements.collapse && Collapse) {
      overridesElements.collapse.addEventListener('shown.bs.collapse', () => {
        overridesState.collapsed = false;
        manageLayout?.classList.remove('pf-manage-layout--rail-collapsed');
        saveOverrideState();
      });
      overridesElements.collapse.addEventListener('hidden.bs.collapse', () => {
        overridesState.collapsed = true;
        manageLayout?.classList.add('pf-manage-layout--rail-collapsed');
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

    if (overridesElements.ruleProject && bootDefaults.overrides?.projectId) {
      overridesElements.ruleProject.value = bootDefaults.overrides.projectId;
      ruleProjectPicker?.initializeById(bootDefaults.overrides.projectId, { notify: false, dispatch: false });
    }
    if (overridesElements.ruleSource) {
      overridesElements.ruleSource.value = bootDefaults.overrides?.source || '1';
    }
    if (overridesElements.ruleYear) {
      overridesElements.ruleYear.value = bootDefaults.overrides?.year || String(defaults.year);
    }
    updateRuleGuidance();
    overridesElements.ruleProject?.addEventListener('change', () => {
      refreshRuleImpact();
      updateRuleActionState();
    });
    overridesElements.ruleSource?.addEventListener('change', updateRuleGuidance);
    overridesElements.ruleYear?.addEventListener('change', () => {
      refreshRuleImpact();
      updateRuleActionState();
    });
    overridesElements.ruleMode?.addEventListener('change', updateRuleGuidance);
    overridesElements.ruleReason?.addEventListener('input', () => {
      overridesElements.ruleReason.classList.remove('is-invalid');
      updateRuleActionState();
    });
    overridesElements.ruleSave?.addEventListener('click', saveCountingRule);

    overridesElements.tableBody?.addEventListener('click', (event) => {
      const button = event.target.closest('button[data-action][data-id]');
      if (!button) return;
      const id = button.getAttribute('data-id');
      const action = button.getAttribute('data-action');
      if (!id || !action) return;
      const row = overridesRows.get(id);
      if (!row) {
        toast('Counting exception details not found. Refresh and try again.', 'warning');
        return;
      }
      if (action === 'project') {
        openProjectTotal(row);
      } else if (action === 'edit-rule') {
        editRuleFromOverride(row);
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
      const emptyMessage = isApprovalWorkspace()
        ? 'No proliferation records are awaiting approval.'
        : 'No records match the current filters.';
      listEl.innerHTML = `<div class="list-group-item text-muted" data-placeholder>${escapeHtml(emptyMessage)}</div>`;
      applyBootFocusIfNeeded();
      return;
    }

    const rows = items.map((item) => {
      const kind = (item.kind || '').toString().toLowerCase() === 'yearly' ? 'yearly' : 'granular';
      const project = item.projectName ?? 'Unknown project';
      const sourceLabel = item.sourceLabel ?? '';
      const quantity = formatNumber(item.quantity ?? item.totalQuantity);
      const dateText = item.proliferationDateUtc ? formatDate(item.proliferationDateUtc) : (item.year ?? '');
      const unitName = kind === 'granular' ? (item.unitName ?? '').trim() : '';
      const subtitleParts = [sourceLabel, dateText, unitName].filter(Boolean);
      const approval = item.approvalStatus ? `Status: ${item.approvalStatus}` : '';
      const subtitle = [subtitleParts.join(' · '), approval].filter(Boolean).join(' • ');
      const updatedValue = item.lastUpdatedOnUtc ?? item.LastUpdatedOnUtc ?? '';
      const updatedAttr = updatedValue ? ` data-updated="${updatedValue}"` : '';
      const isActive = currentRecord.id && currentRecord.id === String(item.id ?? '') && currentRecord.kind === kind;
      const activeClass = isActive ? ' active' : '';
      const currentAttr = isActive ? ' aria-current="true"' : '';
      return `
        <button class="list-group-item list-group-item-action${activeClass}" data-id="${item.id}" data-kind="${kind}"${updatedAttr}${currentAttr} type="button">
          <div class="d-flex justify-content-between align-items-start gap-3">
            <div>
              <div class="fw-semibold">${escapeHtml(project)}</div>
              <div class="small text-muted">${escapeHtml(subtitle)}</div>
            </div>
            <div class="text-end">
              <div class="fw-semibold">${escapeHtml(quantity)}</div>
            </div>
          </div>
        </button>`;
    });

    listEl.innerHTML = rows.join('');
    applyBootFocusIfNeeded();
  }

  function renderCount() {
    if (!countEl) return;
    const footer = countEl.closest('.card-footer');
    if (!pager.total) {
      countEl.textContent = '';
      footer?.classList.add('d-none');
      return;
    }
    footer?.classList.remove('d-none');
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
    if (!id || !kind || !confirmDiscardChanges()) return;

    const metadata = {
      updated: button.dataset.updated || ''
    };

    loadIntoEditor(kind, id, metadata)
      .then(() => {
        if (listEl) {
          listEl.querySelectorAll('.list-group-item.active').forEach((item) => {
            item.classList.remove('active');
            item.removeAttribute('aria-current');
          });
        }
        button.classList.add('active');
        button.setAttribute('aria-current', 'true');
      })
      .catch((err) => {
        toast(err.message || 'Unable to load entry', 'danger');
      });
  });

  function enforceSourceForKind() {
    Array.from(editor.source?.options ?? []).forEach((option) => {
      option.disabled = false;
    });
  }

  function setTab(kind, options = {}) {
    const target = kind === 'yearly' ? 'yearly' : 'granular';
    const updateHash = options.updateHash !== false;
    editor.kind.value = target;
    const isGranular = target === 'granular';
    document.querySelectorAll('.granular-only').forEach((el) => {
      el.classList.toggle('d-none', !isGranular);
    });
    document.querySelectorAll('.yearly-only').forEach((el) => {
      el.classList.toggle('d-none', isGranular);
    });
    if (editor.year) {
      editor.year.readOnly = false;
      editor.year.removeAttribute('aria-readonly');
      editor.year.required = !isGranular;
    }
    if (editor.date) editor.date.required = isGranular;
    if (editor.unit) editor.unit.required = isGranular;
    if (editor.qty) editor.qty.min = isGranular ? 1 : 0;
    enforceSourceForKind(target);
    setCommandScope(target);
    if (commandElements.typeBadge) {
      commandElements.typeBadge.textContent = isGranular ? 'Detailed entry' : 'Annual quantity';
    }

    const guidance = document.querySelector('[data-guidance-text]');
    const quantityLabel = document.querySelector('[data-quantity-label]');
    const quantityHelp = document.querySelector('[data-quantity-help]');
    if (guidance) {
      guidance.textContent = isGranular
        ? 'Use a detailed entry when the proliferation date, receiving unit and quantity are known.'
        : 'Use an annual quantity for records that do not have individual date and unit details. Enter only the quantity not already represented by detailed entries.';
    }
    if (quantityLabel) quantityLabel.textContent = isGranular ? 'Quantity' : 'Annual quantity';
    if (quantityHelp) {
      quantityHelp.textContent = isGranular
        ? 'Enter the quantity received by the selected unit on this date.'
        : 'Enter only the aggregate quantity for which detailed entries are unavailable.';
    }
    if (updateHash) {
      const newHash = `#${target}`;
      if (window.location.hash !== newHash) {
        window.history.replaceState(null, '', newHash);
      }
    }
    updateSaveButtonState();
    updateContextualActions();
    scheduleEditorImpact();
  }


  editor.date?.addEventListener('change', () => {
    const value = editor.date.value || '';
    if (value.length >= 4 && editor.kind?.value === 'granular') {
      editor.year.value = value.slice(0, 4);
      validateField('year', { display: true });
      updateSaveButtonState();
      scheduleEditorImpact();
    }
  });

  document.querySelector('#tab-granular')?.addEventListener('click', () => {
    if (editor.kind?.value !== 'granular') {
      beginNewEntry('granular', { preserveContext: true });
    }
  });
  document.querySelector('#tab-yearly')?.addEventListener('click', () => {
    if (editor.kind?.value !== 'yearly') {
      beginNewEntry('yearly', { preserveContext: true });
    }
  });

  document.querySelectorAll('[data-new-proliferation]').forEach((button) => {
    button.addEventListener('click', () => {
      const kind = button.dataset.newProliferation === 'yearly' ? 'yearly' : 'granular';
      if (!beginNewEntry(kind, { preserveContext: true })) return;
      document.querySelector('#pf-detail-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      (editor.projectSearch || editor.project)?.focus();
    });
  });

  async function loadIntoEditor(kind, id, metadata = {}) {
    const endpoint = kind === 'yearly' ? api.yearly(id) : api.granular(id);
    const response = await fetch(endpoint, { headers: { Accept: 'application/json' } });
    if (!response.ok) {
      throw new Error(await readErrorResponse(response, 'Failed to load entry'));
    }
    const detail = await response.json();
    editor.id.value = detail.id ?? '';
    editor.rowVersion.value = detail.rowVersion ?? '';
    editor.project.value = String(detail.projectId ?? '');
    await editorProjectPicker?.initializeById(detail.projectId, { notify: false, dispatch: false });
    const resolvedSource = resolveSourceSelectValue(detail);
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
    if (editor.source) {
      editor.source.value = resolvedSource;
    }
    editor.remarks.value = detail.remarks ?? '';
    editor.btnDelete.disabled = false;
    const statusValue = (detail.approvalStatus ?? detail.ApprovalStatus ?? '').toString().toLowerCase();
    currentRecord.id = String(detail.id ?? '');
    currentRecord.kind = kind;
    currentRecord.status = statusValue;
    currentRecord.rowVersion = detail.rowVersion ?? '';
    currentRecord.sourceLabel = getOptionLabel(editor.source, editor.source.value) || '';
    currentRecord.projectId = String(detail.projectId ?? '');
    currentRecord.source = resolvedSource;
    currentRecord.year = String(kind === 'yearly' ? (detail.year ?? '') : (editor.year?.value || ''));
    currentRecord.quantity = kind === 'yearly' ? String(detail.totalQuantity ?? '') : String(detail.quantity ?? '');
    const projectLabel = editorProjectPicker?.getSelected()?.display || getOptionLabel(editor.project, editor.project.value) || 'Selected project';
    setCommandTitle(projectLabel);
    const updatedValue = detail.lastUpdatedOnUtc ?? detail.LastUpdatedOnUtc ?? (typeof metadata === 'object' && metadata ? metadata.updated || '' : '');
    setCommandUpdated(updatedValue);
    updateApprovalUi(detail);
    updateDecisionButtons();
    clearValidationState();
    setSaveButtonState('idle');
    updateSaveButtonState();
    markEditorClean();
    scheduleEditorImpact();
    scheduleDuplicateCheck();
  }

  editor.form?.addEventListener('submit', async (event) => {
    event.preventDefault();
    if (!editor.btnSave) return;
    const isValid = validateForm({ focus: true });
    if (!isValid) {
      return;
    }
    setSaveButtonState('loading');
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
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getCsrfToken() },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        throw new Error(await readErrorResponse(response, 'Save failed'));
      }
      const result = await response.json().catch(() => ({}));
      const savedId = id || result.id;
      await fetchList();
      if (savedId) {
        await loadIntoEditor(kind, savedId);
        const normalizedStatus = String(currentRecord.status || '').toLowerCase();
        const message = normalizedStatus === 'approved'
          ? 'Entry saved and approved.'
          : normalizedStatus === 'pending'
            ? 'Entry saved and submitted for approval.'
            : 'Entry saved successfully.';
        toast(message, 'success');
        if (!id) editor.btnAddAnother?.classList.remove('d-none');
      } else {
        resetEditor(kind);
        toast('Entry saved successfully.', 'success');
      }
      setSaveButtonState('success');
      window.dispatchEvent(new CustomEvent('proliferation:recordchanged'));
    } catch (error) {
      setSaveButtonState('idle');
      toast(error.message || 'Unable to save entry', 'danger');
    }
  });

  async function decideRecord(approve, reason = null) {
    if (!canApproveRecords || !currentRecord.id) {
      return false;
    }
    if (!confirmDiscardChanges()) {
      return false;
    }

    const kind = currentRecord.kind === 'yearly' ? 'yearly' : 'granular';
    const id = currentRecord.id;
    const rowVersion = editor.rowVersion.value || currentRecord.rowVersion;
    if (!rowVersion) {
      toast('Refresh the record before taking action.', 'warning');
      return false;
    }

    const endpoint = kind === 'yearly' ? api.decideYearly(id) : api.decideGranular(id);
    const payload = { approve: Boolean(approve), rowVersion, reason };

    setDecisionBusy(true);
    try {
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getCsrfToken() },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        throw new Error(await readErrorResponse(response, 'Unable to update approval status'));
      }
      toast(approve ? 'Entry approved.' : 'Entry rejected.', approve ? 'success' : 'warning');
      await fetchList();
      await loadIntoEditor(kind, id);
      window.dispatchEvent(new CustomEvent('proliferation:recordchanged'));
      return true;
    } catch (error) {
      toast(error.message || 'Unable to update approval status', 'danger');
      return false;
    } finally {
      setDecisionBusy(false);
    }
  }

  function buildPayload(kind) {
    const projectId = Number(editor.project?.value ?? '');
    const source = Number(editor.source?.value ?? '');
    const year = Number(editor.year?.value ?? '');
    const remarks = editor.remarks?.value?.trim() || null;

    if (!Number.isFinite(projectId) || projectId <= 0) return null;
    if (!Number.isFinite(source) || source <= 0) return null;

    if (kind === 'yearly') {
      if (!Number.isFinite(year) || year < 2000) return null;
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
    if (!confirmDiscardChanges()) return;
    const currentKind = editor.kind.value;
    resetEditor(currentKind);
  });

  editor.btnAddAnother?.addEventListener('click', () => {
    beginNewEntry(editor.kind?.value || 'granular', { preserveContext: true });
    (editor.kind?.value === 'yearly' ? editor.qty : editor.date)?.focus();
  });

  decisionButtons.approve?.addEventListener('click', () => {
    decideRecord(true).catch((error) => {
      toast(error.message || 'Unable to update approval status', 'danger');
    });
  });

  decisionButtons.reject?.addEventListener('click', () => {
    if (!rejectModalElements || !Modal) {
      const reason = window.prompt('Reason for rejection');
      if (!reason?.trim()) return;
      decideRecord(false, reason.trim());
      return;
    }
    rejectModalElements.reason.value = '';
    rejectModalElements.error.classList.add('d-none');
    Modal.getOrCreateInstance(rejectModalElements.element).show();
  });

  rejectModalElements?.confirm?.addEventListener('click', async () => {
    const reason = rejectModalElements.reason?.value?.trim() || '';
    if (!reason) {
      rejectModalElements.error?.classList.remove('d-none');
      rejectModalElements.reason?.focus();
      return;
    }
    rejectModalElements.confirm.disabled = true;
    try {
      const succeeded = await decideRecord(false, reason);
      if (succeeded) Modal?.getOrCreateInstance(rejectModalElements.element)?.hide();
    } finally {
      rejectModalElements.confirm.disabled = false;
    }
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
    const projectText = editorProjectPicker?.getSelected()?.display || editor.project?.selectedOptions?.[0]?.text?.trim() || '';
    const dateOrYear = kind === 'yearly'
      ? (editor.year?.value?.trim() || '')
      : formatDate(editor.date?.value || '');
    const confirmed = await confirmDeletion({
      project: projectText,
      type: kind,
      dateOrYear,
      source: currentRecord.sourceLabel,
      quantity: editor.qty?.value || currentRecord.quantity,
      status: currentRecord.status || 'Unknown',
      trigger: editor.btnDelete
    });
    if (!confirmed) return;
    editor.btnDelete.disabled = true;
    try {
      const url = kind === 'yearly' ? api.deleteYearly(id, rowVersion) : api.deleteGranular(id, rowVersion);
      const response = await fetch(url, { method: 'DELETE', headers: { 'X-CSRF-TOKEN': getCsrfToken() }, credentials: 'same-origin' });
      if (!response.ok) {
        throw new Error(await readErrorResponse(response, 'Delete failed'));
      }
      toast('Entry deleted.', 'warning');
      await fetchList();
      resetEditor(kind);
      window.dispatchEvent(new CustomEvent('proliferation:recordchanged'));
    } catch (error) {
      editor.btnDelete.disabled = false;
      toast(error.message || 'Unable to delete entry', 'danger');
    }
  });

  function resetEditor(preferredKind = 'granular') {
    editor.form?.reset();
    editorProjectPicker?.clear({ notify: false, dispatch: false });
    editor.id.value = '';
    editor.rowVersion.value = '';
    editor.qty.value = '';
    editor.remarks.value = '';
    if (editor.year) editor.year.value = String(defaults.year);
    if (editor.date) editor.date.value = '';
    if (editor.unit) editor.unit.value = '';
    editor.btnDelete && (editor.btnDelete.disabled = true);
    editor.btnAddAnother?.classList.add('d-none');
    clearValidationState();
    setTab(preferredKind, { updateHash: false });
    setCommandUpdated('');
    currentRecord.id = '';
    currentRecord.kind = preferredKind === 'yearly' ? 'yearly' : 'granular';
    currentRecord.status = '';
    currentRecord.rowVersion = '';
    currentRecord.sourceLabel = '';
    currentRecord.projectId = '';
    currentRecord.source = '';
    currentRecord.year = '';
    currentRecord.quantity = '';
    setCommandTitle(preferredKind === 'yearly' ? 'New annual quantity' : 'New detailed entry');
    resetApprovalUi();
    updateDecisionButtons();
    if (listEl) {
      listEl.querySelectorAll('.list-group-item.active').forEach((item) => {
        item.classList.remove('active');
        item.removeAttribute('aria-current');
      });
    }
    setSaveButtonState('idle');
    hideEditorImpact();
    hideDuplicateWarning();
    updateContextualActions();
    markEditorClean();
  }

  function getHashKind() {
    const hash = window.location.hash.replace('#', '').toLowerCase();
    if (hash === 'yearly' || hash === 'granular') {
      return hash;
    }
    return '';
  }

  function handleHashChange() {
    const kind = getHashKind();
    if (!kind || kind === editor.kind?.value) return;
    if (!beginNewEntry(kind, { preserveContext: true })) {
      window.history.replaceState(null, '', `#${editor.kind?.value || 'granular'}`);
    }
  }

  function setEditorReviewMode(enabled) {
    editor.form?.querySelectorAll('input:not([type="hidden"]), select, textarea').forEach((control) => {
      control.disabled = Boolean(enabled);
    });
    editorProjectPicker?.setDisabled(Boolean(enabled));
    editorCard?.classList.toggle('pf-manage-card--review', Boolean(enabled));
  }

  function updateListWorkspaceCopy() {
    const heading = document.querySelector('#pf-list-heading');
    const subtitle = document.querySelector('#pf-list-subtitle');
    const guidance = document.querySelector('#pf-list-guidance');
    if (isApprovalWorkspace()) {
      if (heading) heading.textContent = 'Pending records';
      if (subtitle) subtitle.textContent = 'Select a submitted record to review and decide.';
      if (guidance) guidance.textContent = 'Choose a pending record below. Approval uses the last saved values.';
    } else {
      if (heading) heading.textContent = 'Existing records';
      if (subtitle) subtitle.textContent = 'Select a record to review or edit it.';
      if (guidance) guidance.textContent = 'Choose a record below. Use the buttons above to start a new entry.';
    }
  }

  function applyWorkspace(workspace) {
    const next = workspace || 'records';
    if (next === activeWorkspace && ((next === 'approvals') === (filters.status === 'pending'))) {
      updateListWorkspaceCopy();
      return;
    }

    if (next === 'approvals') {
      if (activeWorkspace !== 'approvals') storedRecordStatus = filters.status || '';
      activeWorkspace = 'approvals';
      filters.status = 'pending';
      if (filterInputs.status) {
        filterInputs.status.value = 'pending';
        filterInputs.status.disabled = true;
      }
      setEditorReviewMode(true);
      if (!currentRecord.id || currentRecord.status.toLowerCase() !== 'pending') {
        resetEditor(currentRecord.kind || 'granular');
        setCommandTitle('Select a pending record');
        setCommandScope('Review');
        if (approvalElements.status) approvalElements.status.textContent = 'Select a pending record to review.';
      }
      pager.page = 1;
      updateListWorkspaceCopy();
      updateContextualActions();
      renderFilterChips();
      saveFilters();
      fetchList();
      return;
    }

    const wasApproval = activeWorkspace === 'approvals';
    activeWorkspace = next;
    setEditorReviewMode(false);
    if (wasApproval) {
      filters.status = storedRecordStatus || '';
      if (filterInputs.status) {
        filterInputs.status.disabled = false;
        filterInputs.status.value = hasOption(filterInputs.status, filters.status) ? filters.status : '';
      }
      pager.page = 1;
      renderFilterChips();
      saveFilters();
      fetchList();
    } else if (filterInputs.status) {
      filterInputs.status.disabled = false;
    }
    updateListWorkspaceCopy();
    updateContextualActions();
    updateSaveButtonState();
  }

  pageRoot?.querySelectorAll('[data-workspace]').forEach((tab) => {
    tab.addEventListener('click', (event) => {
      const requested = tab.dataset.workspace || 'records';
      if (requested === activeWorkspace || requested !== 'approvals' || !isEditorDirty()) return;
      if (!window.confirm('You have unsaved changes. Open the approval queue and discard them?')) {
        event.preventDefault();
        event.stopImmediatePropagation();
      }
    }, true);
  });

  window.addEventListener('proliferation:workspacechange', (event) => {
    applyWorkspace(event.detail?.workspace || 'records');
  });

  window.addEventListener('hashchange', handleHashChange);
  window.addEventListener('pagehide', () => {
    listController?.abort();
    overridesController?.abort();
    rulePreviewController?.abort();
    editorImpactController?.abort();
    unitSuggestionController?.abort();
    duplicateCheckController?.abort();
    window.clearTimeout(duplicateCheckTimer);
    editorProjectPicker?.destroy();
    filterProjectPicker?.destroy();
    overridesProjectPicker?.destroy();
    ruleProjectPicker?.destroy();
  }, { once: true });

  window.addEventListener('beforeunload', (event) => {
    if (!isEditorDirty()) return;
    event.preventDefault();
    event.returnValue = '';
  });

  function initFilters() {
    loadFiltersFromStorage();
    applyBootFilterDefaults();
    applyFiltersToInputs();
    renderFilterChips();

    const mappings = new Map([
      ['project', 'projectId'],
      ['source', 'source'],
      ['kind', 'kind'],
      ['status', 'status']
    ]);
    mappings.forEach((filterKey, inputKey) => {
      const input = filterInputs[inputKey];
      if (!input) return;
      input.addEventListener('change', (event) => {
        updateFilter(filterKey, event.target.value);
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

    if (filterInputs.search) {
      filterInputs.search.addEventListener('input', (event) => {
        const raw = (event.target.value || '').trim();
        if (filterSearchTimer) {
          clearTimeout(filterSearchTimer);
        }
        filterSearchTimer = setTimeout(() => {
          updateFilter('search', raw);
          filterSearchTimer = null;
        }, 250);
      });
    }

    filterInputs.reset?.addEventListener('click', () => {
      resetFilters();
    });

    filterInputs.chips?.addEventListener('click', (event) => {
      const button = event.target.closest('button[data-filter-key]');
      if (!button) return;
      const { filterKey } = button.dataset;
      if (!filterKey) return;
      clearFilter(filterKey);
    });
  }

  function init() {
    initProjectPickers();
    initFilters();
    applyBootEditorDefaults();
    setCommandUpdated('');
    initOverrides();

    const hashKind = getHashKind();
    const bootKind = bootDefaults.editor?.kind || bootDefaults.filters?.kind || '';
    const initialKind = hashKind || bootKind || 'granular';

    setTab(initialKind, { updateHash: false });
    resetApprovalUi();
    updateContextualActions();
    setSaveButtonState('idle');
    markEditorClean();
    fetchList();
  }

  init();
})();
