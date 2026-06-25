const root = document.querySelector('[data-process-flow-root]');

if (root) {
  const state = {
    version: (root.dataset.processVersion || '').trim(),
    canEdit: root.dataset.canEdit === 'true',
    flow: null,
    stageByCode: new Map(),
    incoming: new Map(),
    outgoing: new Map(),
    selectedCode: null,
    checklistCache: new Map(),
    currentChecklist: null,
    checklistRequest: null,
    sortable: null,
    searchTerm: ''
  };

  const PHASES = [
    { id: 'initiation', name: 'Initiation', codes: ['FS', 'SOW'] },
    { id: 'approval', name: 'Approval', codes: ['IPA', 'AON'] },
    { id: 'tendering', name: 'Tendering & evaluation', codes: ['BID', 'TEC', 'BM', 'COB', 'PNC'] },
    { id: 'contracting', name: 'Contracting', codes: ['EAS', 'SO'] },
    { id: 'delivery', name: 'Development & assurance', codes: ['DEVP', 'ATP'] },
    { id: 'closure', name: 'Closure & exploitation', codes: ['PAYMENT', 'TOT'] }
  ];

  const STAGE_PURPOSES = {
    FS: 'Establish operational need, feasibility, broad scope, stakeholders and indicative resources.',
    SOW: 'Define and vet the technical scope, deliverables, standards, acceptance criteria and responsibilities.',
    IPA: 'Obtain in-principle approval to progress the proposal for detailed processing and costing.',
    AON: 'Secure formal acceptance of necessity or sanction for procurement and associated expenditure.',
    BID: 'Publish the approved tender package and manage bidder communication, clarifications and submissions.',
    TEC: 'Evaluate technical compliance, capability, demonstrations and mandatory documentation.',
    BM: 'Establish an independent and defensible benchmark for assessing price reasonableness.',
    COB: 'Open commercial bids of technically qualified firms and establish the commercial position.',
    PNC: 'Conduct price negotiations where authorised and record the basis for the negotiated outcome.',
    EAS: 'Obtain expenditure approval or financial sanction based on the evaluated commercial proposal.',
    SO: 'Issue the supply order or contract with approved terms, milestones and obligations.',
    DEVP: 'Execute development, integration, reviews and milestone monitoring against the contracted scope.',
    ATP: 'Verify the delivered system against approved acceptance test procedures and contractual criteria.',
    PAYMENT: 'Process payment against accepted deliverables, contractual milestones and supporting documents.',
    TOT: 'Complete transfer of technology, knowledge, documentation and sustainment arrangements where applicable.'
  };

  const canvas = root.querySelector('[data-flow-canvas]');
  const workspace = root.querySelector('[data-process-workspace]');
  const phaseStrip = root.querySelector('[data-phase-strip]');
  const stageJump = root.querySelector('[data-stage-jump]');
  const searchInput = root.querySelector('[data-stage-search]');
  const searchClear = root.querySelector('[data-search-clear]');
  const stageCount = root.querySelector('[data-stage-count]');
  const desktopInspector = root.querySelector('[data-stage-inspector]');
  const emptyInspector = root.querySelector('[data-stage-empty]');
  const contentInspector = root.querySelector('[data-stage-content]');
  const mobileStageButton = root.querySelector('[data-action="open-stage-panel"]');
  const mobileStageTitle = root.querySelector('[data-mobile-stage-title]');
  const offcanvasEl = document.getElementById('checklistOffcanvas');
  const offcanvas = offcanvasEl ? bootstrap.Offcanvas.getOrCreateInstance(offcanvasEl) : null;
  const itemModalEl = document.getElementById('checklistItemModal');
  const deleteModalEl = document.getElementById('checklistDeleteModal');
  const itemModal = itemModalEl ? bootstrap.Modal.getOrCreateInstance(itemModalEl) : null;
  const deleteModal = deleteModalEl ? bootstrap.Modal.getOrCreateInstance(deleteModalEl) : null;
  const itemForm = itemModalEl?.querySelector('[data-checklist-form]');
  const deleteForm = deleteModalEl?.querySelector('[data-checklist-delete-form]');
  const itemText = itemForm?.querySelector('textarea[name="text"]');
  const characterCount = itemForm?.querySelector('[data-character-count]');
  const checklistLists = Array.from(document.querySelectorAll('[data-checklist-list]'));
  const primaryChecklist = document.querySelector('[data-checklist-primary]');
  const actionGroups = Array.from(document.querySelectorAll('[data-checklist-actions]'));

  class HttpError extends Error {
    constructor(response, data) {
      super(data?.message || data?.title || `Request failed (${response.status})`);
      this.status = response.status;
      this.data = data;
    }
  }

  async function sendJson(url, { method = 'GET', body } = {}) {
    const headers = { Accept: 'application/json' };
    let payload;
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    const response = await fetch(url, { method, headers, body: payload, credentials: 'same-origin' });
    const type = response.headers.get('content-type') || '';
    const data = type.includes('application/json') ? await response.json().catch(() => null) : null;
    if (!response.ok) throw new HttpError(response, data);
    return data;
  }

  function flowUrl() {
    return `/api/processes/${encodeURIComponent(state.version)}/flow`;
  }

  function checklistUrl(code, suffix = '') {
    return `/api/processes/${encodeURIComponent(state.version)}/stages/${encodeURIComponent(code)}/checklist${suffix}`;
  }

  function normalizeFlow(dto) {
    const nodes = (Array.isArray(dto?.nodes) ? dto.nodes : [])
      .map((node) => ({
        code: String(node.code || node.id || '').toUpperCase(),
        name: String(node.name || node.label || node.code || ''),
        sequence: Number.parseInt(node.sequence, 10) || 0,
        optional: node.optional === true || node.optional === 'true',
        parallelGroup: node.parallelGroup || null
      }))
      .filter((node) => node.code)
      .sort((a, b) => a.sequence - b.sequence || a.code.localeCompare(b.code));

    nodes.forEach((node, index) => {
      node.displayIndex = index + 1;
      node.phase = resolvePhase(node.code);
      node.searchText = `${node.code} ${node.name} ${node.phase.name}`.toLowerCase();
    });

    const edges = (Array.isArray(dto?.edges) ? dto.edges : [])
      .map((edge) => ({
        source: String(edge.source || edge.from || '').toUpperCase(),
        target: String(edge.target || edge.to || '').toUpperCase()
      }))
      .filter((edge) => edge.source && edge.target);

    return { version: dto?.version || state.version, nodes, edges };
  }

  function resolvePhase(code) {
    return PHASES.find((phase) => phase.codes.includes(code)) || PHASES[PHASES.length - 1];
  }

  function buildGraph(flow) {
    state.incoming.clear();
    state.outgoing.clear();
    flow.nodes.forEach((node) => {
      state.incoming.set(node.code, []);
      state.outgoing.set(node.code, []);
    });
    flow.edges.forEach((edge) => {
      state.incoming.get(edge.target)?.push(edge.source);
      state.outgoing.get(edge.source)?.push(edge.target);
    });
  }

  function normalizeChecklist(dto) {
    return {
      id: dto?.id,
      version: dto?.version,
      stageCode: dto?.stageCode,
      rowVersion: dto?.rowVersion,
      items: (Array.isArray(dto?.items) ? dto.items : [])
        .map((item) => ({
          id: Number(item.id),
          text: String(item.text || ''),
          sequence: Number.parseInt(item.sequence, 10) || 0,
          rowVersion: item.rowVersion || ''
        }))
        .sort((a, b) => a.sequence - b.sequence || a.id - b.id)
    };
  }

  function phaseNodes(phase) {
    return state.flow.nodes.filter((node) => node.phase.id === phase.id);
  }

  function detectParallelSet(nodes) {
    if (nodes.length < 2) return [];
    const groups = [];
    const used = new Set();
    for (const node of nodes) {
      if (used.has(node.code)) continue;
      const incoming = state.incoming.get(node.code) || [];
      const outgoing = state.outgoing.get(node.code) || [];
      const siblings = nodes.filter((candidate) => {
        if (candidate.code === node.code || used.has(candidate.code)) return false;
        const ci = state.incoming.get(candidate.code) || [];
        const co = state.outgoing.get(candidate.code) || [];
        return incoming.length === 1 && outgoing.length === 1 &&
          ci.length === 1 && co.length === 1 &&
          ci[0] === incoming[0] && co[0] === outgoing[0];
      });
      if (siblings.length) {
        const set = [node, ...siblings].sort((a, b) => a.sequence - b.sequence);
        set.forEach((item) => used.add(item.code));
        groups.push(set);
      }
    }
    return groups;
  }

  function renderPhaseStrip() {
    phaseStrip.innerHTML = '';
    PHASES.forEach((phase, index) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'phase-chip';
      button.dataset.phaseTarget = phase.id;
      button.innerHTML = `<span class="phase-chip__number">${index + 1}</span><span>${phase.name}</span>`;
      phaseStrip.appendChild(button);
    });
  }

  function renderMap() {
    canvas.innerHTML = '';
    canvas.setAttribute('aria-busy', 'false');
    const grid = document.createElement('div');
    grid.className = 'process-phase-grid';

    PHASES.forEach((phase, index) => {
      const card = document.createElement('section');
      card.className = 'process-phase-card';
      card.id = `process-phase-${phase.id}`;
      card.dataset.phaseIndex = String(index + 1);
      card.dataset.phaseId = phase.id;

      const nodes = phaseNodes(phase);
      const header = document.createElement('header');
      header.className = 'process-phase-card__header';
      header.innerHTML = `<div><span class="process-phase-index">${index + 1}</span><h3>${escapeHtml(phase.name)}</h3></div><small>${nodes.length} stage${nodes.length === 1 ? '' : 's'}</small>`;
      card.appendChild(header);

      const stack = document.createElement('div');
      stack.className = 'process-stage-stack';
      const parallelSets = detectParallelSet(nodes);
      const parallelCodes = new Set(parallelSets.flat().map((node) => node.code));

      nodes.forEach((node) => {
        if (parallelCodes.has(node.code)) {
          const group = parallelSets.find((set) => set[0].code === node.code);
          if (!group) return;
          const branch = document.createElement('div');
          branch.className = 'process-branch';
          group.forEach((branchNode) => branch.appendChild(createStageButton(branchNode)));
          stack.appendChild(branch);
          return;
        }
        const step = document.createElement('div');
        step.className = 'process-step';
        step.appendChild(createStageButton(node));
        stack.appendChild(step);
      });

      card.appendChild(stack);
      grid.appendChild(card);
    });

    canvas.appendChild(grid);
    applySearch();
    applySelection();
  }

  function createStageButton(node) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `process-stage${node.optional ? ' process-stage--optional' : ''}`;
    button.dataset.stageCode = node.code;
    button.dataset.stageName = node.name;
    button.dataset.phaseId = node.phase.id;
    button.setAttribute('aria-label', `${node.displayIndex}. ${node.name}${node.optional ? ', optional stage' : ''}`);
    button.innerHTML = `
      <span class="process-stage__number">${node.displayIndex}</span>
      <span class="process-stage__copy"><strong>${escapeHtml(node.name)}</strong><small>${escapeHtml(node.code)}</small></span>
      ${node.optional ? '<span class="process-stage__optional">Optional</span>' : '<i class="bi bi-chevron-right" aria-hidden="true"></i>'}`;
    return button;
  }

  function populateJump() {
    stageJump.innerHTML = '<option value="">Jump to stage…</option>';
    state.flow.nodes.forEach((node) => {
      const option = document.createElement('option');
      option.value = node.code;
      option.textContent = `${node.displayIndex}. ${node.name}`;
      stageJump.appendChild(option);
    });
  }

  function updateElements(selector, value) {
    document.querySelectorAll(selector).forEach((element) => { element.textContent = value; });
  }

  function showInspector(stage) {
    if (!stage) {
      emptyInspector.hidden = false;
      contentInspector.hidden = true;
      mobileStageButton.hidden = true;
      toggleEditActions(false);
      return;
    }

    emptyInspector.hidden = true;
    contentInspector.hidden = false;
    mobileStageButton.hidden = false;
    mobileStageTitle.textContent = `${stage.displayIndex}. ${stage.name}`;
    const previous = (state.incoming.get(stage.code) || []).map(stageName).join(' / ') || 'Start of process';
    const next = (state.outgoing.get(stage.code) || []).map(stageName).join(' / ') || 'Process complete';

    updateElements('[data-stage-title]', `${stage.displayIndex}. ${stage.name}`);
    updateElements('[data-stage-phase]', stage.phase.name);
    updateElements('[data-stage-code-label]', stage.code);
    updateElements('[data-stage-requirement]', stage.optional ? 'Optional stage' : '');
    updateElements('[data-stage-purpose]', STAGE_PURPOSES[stage.code] || 'Review the approved checks, dependencies and required outputs for this stage.');
    updateElements('[data-stage-previous]', previous);
    updateElements('[data-stage-next]', next);
    updateElements('[data-stage-position]', `Stage ${stage.displayIndex} of ${state.flow.nodes.length}`);
    document.querySelectorAll('[data-stage-requirement]').forEach((el) => {
      el.classList.toggle('is-optional', stage.optional);
      el.hidden = !stage.optional;
    });
    toggleEditActions(state.canEdit);

    document.querySelectorAll('[data-action="previous-stage"]').forEach((button) => {
      button.disabled = stage.displayIndex === 1;
      button.setAttribute('aria-disabled', button.disabled ? 'true' : 'false');
    });
    document.querySelectorAll('[data-action="next-stage"]').forEach((button) => {
      button.disabled = stage.displayIndex === state.flow.nodes.length;
      button.setAttribute('aria-disabled', button.disabled ? 'true' : 'false');
    });
  }

  function stageName(code) {
    return state.stageByCode.get(code)?.name || code;
  }

  function toggleEditActions(show) {
    actionGroups.forEach((group) => {
      group.hidden = !show;
      group.disabled = !show;
    });
  }

  function applySelection() {
    root.querySelectorAll('[data-stage-code]').forEach((button) => {
      const code = button.dataset.stageCode;
      button.classList.toggle('is-selected', code === state.selectedCode);
      button.setAttribute('aria-pressed', code === state.selectedCode ? 'true' : 'false');
    });
    root.querySelectorAll('.phase-chip').forEach((chip) => {
      const stage = state.stageByCode.get(state.selectedCode);
      chip.classList.toggle('is-active', Boolean(stage && chip.dataset.phaseTarget === stage.phase.id));
    });
  }

  async function selectStage(code, { openMobile = false, updateHash = true } = {}) {
    const stage = state.stageByCode.get(code);
    if (!stage) return;
    state.selectedCode = stage.code;
    stageJump.value = stage.code;
    showInspector(stage);
    applySelection();
    const selectedButton = root.querySelector(`[data-stage-code="${CSS.escape(stage.code)}"]`);
    if (selectedButton && !isElementMostlyVisible(selectedButton)) {
      selectedButton.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });
    }
    await loadChecklist(stage.code);
    if (updateHash) history.replaceState(null, '', `#stage-${stage.code.toLowerCase()}`);
    if (openMobile && window.innerWidth < 1200) offcanvas?.show();
  }

  async function loadChecklist(code, { force = false } = {}) {
    const token = Symbol(code);
    state.checklistRequest = token;
    state.currentChecklist = null;
    renderChecklist(null, { loading: true });

    if (!force && state.checklistCache.has(code)) {
      state.currentChecklist = state.checklistCache.get(code);
      renderChecklist(state.currentChecklist);
      return;
    }

    try {
      const dto = await sendJson(checklistUrl(code));
      if (state.checklistRequest !== token) return;
      const checklist = normalizeChecklist(dto);
      state.currentChecklist = checklist;
      state.checklistCache.set(code, checklist);
      renderChecklist(checklist);
    } catch (error) {
      if (state.checklistRequest !== token) return;
      renderChecklist(null, { error: 'Unable to load the checklist for this stage.' });
      handleError(error, 'Unable to load checklist.');
    }
  }

  function renderChecklist(checklist, options = {}) {
    const checklistCount = checklist?.items?.length || 0;
    document.querySelectorAll('[data-checklist-count]').forEach((element) => {
      element.textContent = String(checklistCount);
      element.setAttribute('aria-label', `${checklistCount} checklist item${checklistCount === 1 ? '' : 's'}`);
    });
    checklistLists.forEach((list) => {
      list.innerHTML = '';
      list.setAttribute('aria-busy', String(options.loading === true));
      if (options.loading) {
        list.innerHTML = '<li class="checklist-empty"><span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>Loading stage guidance…</li>';
        return;
      }
      if (options.error) {
        const item = document.createElement('li');
        item.className = 'checklist-empty text-danger';
        item.textContent = options.error;
        list.appendChild(item);
        return;
      }
      if (!checklist || checklist.items.length === 0) {
        const item = document.createElement('li');
        item.className = 'checklist-empty';
        item.textContent = state.selectedCode ? 'No checklist items are defined for this stage.' : 'Select a stage to view its processing checklist.';
        list.appendChild(item);
        return;
      }
      checklist.items.forEach((item, index) => list.appendChild(createChecklistItem(item, index + 1)));
    });
    setupSortable();
  }

  function createChecklistItem(item, index) {
    const li = document.createElement('li');
    li.className = `checklist-item${state.canEdit ? '' : ' read-only'}`;
    li.dataset.itemId = String(item.id);
    li.dataset.rowVersion = item.rowVersion || '';
    li.dataset.sequence = String(item.sequence);

    if (state.canEdit) {
      const handle = document.createElement('button');
      handle.type = 'button';
      handle.className = 'btn btn-link p-0 checklist-handle';
      handle.dataset.handle = 'true';
      handle.setAttribute('aria-label', 'Drag to reorder checklist item');
      handle.innerHTML = '<i class="bi bi-grip-vertical" aria-hidden="true"></i>';
      li.appendChild(handle);
    } else {
      const number = document.createElement('span');
      number.className = 'checklist-item__index';
      number.textContent = String(index);
      li.appendChild(number);
    }

    const body = document.createElement('div');
    body.className = 'item-body';
    const paragraph = document.createElement('p');
    paragraph.textContent = item.text;
    body.appendChild(paragraph);
    li.appendChild(body);

    if (state.canEdit) {
      const actions = document.createElement('div');
      actions.className = 'item-actions';
      actions.innerHTML = `
        <button type="button" class="btn btn-light" data-action="edit-item" aria-label="Edit checklist item" title="Edit"><i class="bi bi-pencil" aria-hidden="true"></i></button>
        <button type="button" class="btn btn-light text-danger" data-action="delete-item" aria-label="Delete checklist item" title="Delete"><i class="bi bi-trash" aria-hidden="true"></i></button>`;
      li.appendChild(actions);
    }
    return li;
  }

  function setupSortable() {
    state.sortable?.destroy?.();
    state.sortable = null;
    if (!state.canEdit || !primaryChecklist || !state.currentChecklist || state.currentChecklist.items.length < 2 || !window.Sortable) return;
    state.sortable = window.Sortable.create(primaryChecklist, {
      animation: 160,
      draggable: '.checklist-item',
      handle: '[data-handle]',
      ghostClass: 'opacity-50',
      onEnd: (event) => { if (event.oldIndex !== event.newIndex) submitReorder(); }
    });
  }

  async function submitReorder() {
    if (!state.selectedCode || !state.currentChecklist) return;
    const items = Array.from(primaryChecklist.querySelectorAll('.checklist-item')).map((li, index) => ({
      itemId: Number.parseInt(li.dataset.itemId, 10),
      rowVersion: li.dataset.rowVersion,
      sequence: index + 1
    }));
    try {
      const dto = await sendJson(`${checklistUrl(state.selectedCode)}/reorder`, {
        method: 'POST',
        body: { templateRowVersion: state.currentChecklist.rowVersion, items }
      });
      const checklist = normalizeChecklist(dto);
      cacheChecklist(checklist);
      showToast('Checklist order updated.', 'success');
    } catch (error) {
      handleError(error, 'Unable to reorder checklist.');
      await loadChecklist(state.selectedCode, { force: true });
    }
  }

  function cacheChecklist(checklist) {
    state.currentChecklist = checklist;
    state.checklistCache.set(state.selectedCode, checklist);
    renderChecklist(checklist);
  }

  function findItemFromAction(button) {
    const li = button.closest('.checklist-item');
    if (!li || !state.currentChecklist) return null;
    return state.currentChecklist.items.find((item) => item.id === Number.parseInt(li.dataset.itemId, 10)) || null;
  }

  function openItemModal(mode, item = null) {
    if (!itemForm || !itemModal) return;
    itemForm.dataset.mode = mode;
    itemForm.querySelector('input[name="itemId"]').value = item?.id || '';
    itemForm.querySelector('input[name="itemRowVersion"]').value = item?.rowVersion || '';
    itemText.value = item?.text || '';
    itemForm.querySelector('[data-submit-label]').textContent = mode === 'edit' ? 'Save changes' : 'Add item';
    updateCharacterCount();
    itemModalEl.addEventListener('shown.bs.modal', () => itemText.focus(), { once: true });
    itemModal.show();
  }

  function openDeleteModal(item) {
    if (!deleteForm || !deleteModal) return;
    deleteForm.querySelector('input[name="itemId"]').value = item.id;
    deleteForm.querySelector('input[name="itemRowVersion"]').value = item.rowVersion || '';
    deleteModal.show();
  }

  async function submitItemForm(event) {
    event.preventDefault();
    if (!state.selectedCode || !state.currentChecklist) return;
    const text = itemText.value.trim();
    if (!text) return showToast('Checklist item text cannot be empty.', 'warning');
    const mode = itemForm.dataset.mode === 'edit' ? 'edit' : 'create';
    const itemId = Number.parseInt(itemForm.querySelector('input[name="itemId"]').value || '0', 10);
    const itemRowVersion = itemForm.querySelector('input[name="itemRowVersion"]').value;
    setFormBusy(itemForm, true);
    try {
      const url = mode === 'edit' ? `${checklistUrl(state.selectedCode)}/${itemId}` : checklistUrl(state.selectedCode);
      const body = mode === 'edit'
        ? { text, templateRowVersion: state.currentChecklist.rowVersion, itemRowVersion }
        : { text, templateRowVersion: state.currentChecklist.rowVersion };
      const dto = await sendJson(url, { method: mode === 'edit' ? 'PUT' : 'POST', body });
      cacheChecklist(normalizeChecklist(dto));
      itemModal.hide();
      showToast(mode === 'edit' ? 'Checklist item updated.' : 'Checklist item added.', 'success');
    } catch (error) {
      handleError(error, mode === 'edit' ? 'Unable to update checklist item.' : 'Unable to add checklist item.');
    } finally {
      setFormBusy(itemForm, false);
    }
  }

  async function submitDeleteForm(event) {
    event.preventDefault();
    if (!state.selectedCode || !state.currentChecklist) return;
    const itemId = Number.parseInt(deleteForm.querySelector('input[name="itemId"]').value || '0', 10);
    const itemRowVersion = deleteForm.querySelector('input[name="itemRowVersion"]').value;
    setFormBusy(deleteForm, true);
    try {
      const dto = await sendJson(`${checklistUrl(state.selectedCode)}/${itemId}`, {
        method: 'DELETE',
        body: { templateRowVersion: state.currentChecklist.rowVersion, itemRowVersion }
      });
      cacheChecklist(normalizeChecklist(dto));
      deleteModal.hide();
      showToast('Checklist item removed.', 'success');
    } catch (error) {
      handleError(error, 'Unable to delete checklist item.');
    } finally {
      setFormBusy(deleteForm, false);
    }
  }

  function setFormBusy(form, busy) {
    const button = form.querySelector('button[type="submit"]');
    const spinner = form.querySelector('.spinner-border');
    if (button) button.disabled = busy;
    if (spinner) spinner.hidden = !busy;
  }

  function applySearch() {
    const term = state.searchTerm.trim().toLowerCase();
    let firstMatch = null;
    root.querySelectorAll('.process-stage').forEach((button) => {
      const stage = state.stageByCode.get(button.dataset.stageCode);
      const matches = !term || stage?.searchText.includes(term);
      button.classList.toggle('is-filtered-out', Boolean(term && !matches));
      button.classList.toggle('is-search-match', Boolean(term && matches));
      if (term && matches && !firstMatch) firstMatch = button;
    });
    searchClear.hidden = !term;
    return firstMatch;
  }

  function goRelative(delta) {
    const current = state.stageByCode.get(state.selectedCode);
    if (!current) return;
    const next = state.flow.nodes[current.displayIndex - 1 + delta];
    if (next) selectStage(next.code);
  }

  function scrollToPhase(id) {
    document.getElementById(`process-phase-${id}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  function isElementMostlyVisible(element) {
    const rect = element.getBoundingClientRect();
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
    return rect.top >= 120 && rect.bottom <= viewportHeight - 40;
  }

  function showOverview() {
    state.selectedCode = null;
    history.replaceState(null, '', location.pathname + location.search);
    stageJump.value = '';
    showInspector(null);
    renderChecklist(null);
    applySelection();
    canvas.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  async function toggleFullscreen() {
    if (!document.fullscreenElement) await workspace.requestFullscreen?.();
    else await document.exitFullscreen?.();
  }

  function updateCharacterCount() {
    if (characterCount && itemText) characterCount.textContent = `${itemText.value.length} / 512`;
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[char]));
  }

  function showToast(message, variant = 'primary') {
    let container = document.getElementById('processToastContainer');
    if (!container) {
      container = document.createElement('div');
      container.id = 'processToastContainer';
      container.className = 'toast-container position-fixed top-0 end-0 p-3';
      container.style.zIndex = '2000';
      document.body.appendChild(container);
    }
    const element = document.createElement('div');
    element.className = `toast align-items-center text-bg-${variant} border-0`;
    element.innerHTML = `<div class="d-flex"><div class="toast-body"></div><button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button></div>`;
    element.querySelector('.toast-body').textContent = message;
    container.appendChild(element);
    const toast = bootstrap.Toast.getOrCreateInstance(element, { delay: 4500 });
    element.addEventListener('hidden.bs.toast', () => element.remove());
    toast.show();
  }

  function handleError(error, fallback) {
    console.error(error);
    if (error?.status === 409) showToast('This checklist changed in another session. Reloading the latest version.', 'warning');
    else if (error?.status === 403) showToast('You are not authorised to change this checklist.', 'danger');
    else showToast(error?.message || fallback, 'danger');
  }

  function initialStageFromHash() {
    const match = location.hash.match(/^#stage-(.+)$/i);
    return match ? match[1].toUpperCase() : null;
  }

  async function loadFlow() {
    try {
      const dto = await sendJson(flowUrl());
      state.flow = normalizeFlow(dto);
      state.stageByCode = new Map(state.flow.nodes.map((node) => [node.code, node]));
      buildGraph(state.flow);
      if (stageCount) stageCount.textContent = String(state.flow.nodes.length);
      renderPhaseStrip();
      populateJump();
      renderMap();
      const initial = initialStageFromHash();
      if (initial && state.stageByCode.has(initial)) await selectStage(initial, { updateHash: false });
      else showInspector(null);
    } catch (error) {
      console.error(error);
      canvas.setAttribute('aria-busy', 'false');
      canvas.innerHTML = '<div class="checklist-empty text-danger m-3">Unable to load the procurement workflow. Please refresh the page.</div>';
    }
  }

  root.addEventListener('click', (event) => {
    const stageButton = event.target.closest('.process-stage');
    if (stageButton) {
      selectStage(stageButton.dataset.stageCode, { openMobile: window.innerWidth < 1200 });
      return;
    }
    const phaseButton = event.target.closest('[data-phase-target]');
    if (phaseButton) {
      scrollToPhase(phaseButton.dataset.phaseTarget);
      return;
    }
    const actionButton = event.target.closest('[data-action]');
    if (!actionButton) return;
    const action = actionButton.dataset.action;
    if (action === 'show-overview') showOverview();
    else if (action === 'toggle-fullscreen') toggleFullscreen();
    else if (action === 'print-process') window.print();
    else if (action === 'open-stage-panel' && state.selectedCode) offcanvas?.show();
    else if (action === 'previous-stage') goRelative(-1);
    else if (action === 'next-stage') goRelative(1);
    else if (action === 'add-item') openItemModal('create');
    else if (action === 'edit-item') {
      const item = findItemFromAction(actionButton);
      if (item) openItemModal('edit', item);
    } else if (action === 'delete-item') {
      const item = findItemFromAction(actionButton);
      if (item) openDeleteModal(item);
    }
  });

  searchInput.addEventListener('input', () => {
    state.searchTerm = searchInput.value;
    applySearch();
  });
  searchInput.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      const match = applySearch();
      if (match) selectStage(match.dataset.stageCode, { openMobile: window.innerWidth < 1200 });
    }
  });
  searchClear.addEventListener('click', () => {
    searchInput.value = '';
    state.searchTerm = '';
    applySearch();
    searchInput.focus();
  });
  stageJump.addEventListener('change', () => {
    if (stageJump.value) selectStage(stageJump.value, { openMobile: window.innerWidth < 1200 });
  });
  itemForm?.addEventListener('submit', submitItemForm);
  deleteForm?.addEventListener('submit', submitDeleteForm);
  itemText?.addEventListener('input', updateCharacterCount);
  document.addEventListener('keydown', (event) => {
    if (event.target?.matches?.('input, textarea, select') || event.target?.isContentEditable) return;
    if (event.key === 'ArrowLeft' && state.selectedCode) { event.preventDefault(); goRelative(-1); }
    if (event.key === 'ArrowRight' && state.selectedCode) { event.preventDefault(); goRelative(1); }
    if (event.key === 'Escape' && document.fullscreenElement) document.exitFullscreen?.();
  });

  renderChecklist(null);
  loadFlow();
}
