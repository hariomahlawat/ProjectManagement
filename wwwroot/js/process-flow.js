const root = document.querySelector('[data-process-flow-root]');

if (root) {
  const version = (root.dataset.processVersion || '').trim();
  const canEdit = root.dataset.canEdit === 'true';
  const flowCanvas = root.querySelector('[data-flow-canvas]');
  const stageTitleEls = Array.from(document.querySelectorAll('[data-stage-title]'));
  const stageSubtitleEls = Array.from(document.querySelectorAll('[data-stage-subtitle]'));
  const stageCodeEls = Array.from(document.querySelectorAll('[data-stage-code]'));
  const stageParallelEls = Array.from(document.querySelectorAll('[data-stage-parallel]'));
  const stageDependenciesEls = Array.from(document.querySelectorAll('[data-stage-dependencies]'));
  const optionalBadgeEls = Array.from(document.querySelectorAll('[data-stage-optional]'));
  const checklistLists = Array.from(document.querySelectorAll('[data-checklist-list]'));
  const primaryChecklist = root.querySelector('[data-checklist-primary]');
  const actionGroups = Array.from(document.querySelectorAll('[data-checklist-actions]'));
  const itemModalEl = document.getElementById('checklistItemModal');
  const deleteModalEl = document.getElementById('checklistDeleteModal');
  const itemForm = itemModalEl ? itemModalEl.querySelector('[data-checklist-form]') : null;
  const deleteForm = deleteModalEl ? deleteModalEl.querySelector('[data-checklist-delete-form]') : null;
  const itemModal = itemModalEl ? new bootstrap.Modal(itemModalEl) : null;
  const deleteModal = deleteModalEl ? new bootstrap.Modal(deleteModalEl) : null;

  const state = {
    version,
    canEdit,
    flow: null,
    cytoscape: null,
    stageByCode: new Map(),
    selectedStage: null,
    checklistCache: new Map(),
    currentChecklist: null,
    checklistPromise: null,
    sortable: null
  };

  class HttpError extends Error {
    constructor(response, data) {
      super((data && (data.message || data.title)) || `Request failed (${response.status})`);
      this.name = 'HttpError';
      this.response = response;
      this.status = response.status;
      this.data = data || null;
    }
  }

  let cytoscapeLoader;
  async function ensureCytoscape() {
    if (window.cytoscape) {
      return window.cytoscape;
    }

    if (!cytoscapeLoader) {
      cytoscapeLoader = (async () => {
        const [{ default: cytoscape }, dagreModule, { default: cytoscapeDagre }, { default: cytoscapePanzoom }] = await Promise.all([
          import('https://cdn.jsdelivr.net/npm/cytoscape@3.30.1/+esm'),
          import('https://cdn.jsdelivr.net/npm/dagre@0.8.5/+esm'),
          import('https://cdn.jsdelivr.net/npm/cytoscape-dagre@2.5.0/+esm'),
          import('https://cdn.jsdelivr.net/npm/cytoscape-panzoom@2.5.3/+esm')
        ]);
        const dagre = dagreModule && (dagreModule.default || dagreModule);
        if (typeof window !== 'undefined') {
          window.dagre = dagre;
        }
        cytoscape.use(cytoscapeDagre);
        cytoscape.use(cytoscapePanzoom);
        return cytoscape;
      })();
    }

    return cytoscapeLoader;
  }

  let sortableLoader;
  async function ensureSortable() {
    if (sortableLoader) {
      return sortableLoader;
    }

    sortableLoader = import('https://cdn.jsdelivr.net/npm/sortablejs@1.15.2/modular/sortable.esm.js').then((module) => module.Sortable || module.default);
    return sortableLoader;
  }

  function ensureToastContainer() {
    let container = document.getElementById('processToastContainer');
    if (!container) {
      container = document.createElement('div');
      container.id = 'processToastContainer';
      container.className = 'toast-container position-fixed top-0 end-0 p-3';
      container.setAttribute('aria-live', 'polite');
      container.setAttribute('aria-atomic', 'true');
      document.body.appendChild(container);
    }

    return container;
  }

  function showToast(message, variant = 'primary', options = {}) {
    const container = ensureToastContainer();
    const toastEl = document.createElement('div');
    toastEl.className = `toast align-items-center text-bg-${variant} border-0`;
    toastEl.role = 'status';
    toastEl.setAttribute('aria-live', 'polite');
    toastEl.setAttribute('aria-atomic', 'true');

    const wrapper = document.createElement('div');
    wrapper.className = 'd-flex align-items-center gap-3';

    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = message;
    wrapper.appendChild(body);

    if (options && typeof options.onAction === 'function' && options.actionLabel) {
      const actionBtn = document.createElement('button');
      actionBtn.type = 'button';
      actionBtn.className = 'btn btn-sm btn-light ms-auto';
      actionBtn.textContent = options.actionLabel;
      actionBtn.addEventListener('click', () => {
        options.onAction();
      }, { once: true });
      wrapper.appendChild(actionBtn);
    }

    const dismiss = document.createElement('button');
    dismiss.type = 'button';
    dismiss.className = 'btn-close btn-close-white me-2 m-auto';
    dismiss.setAttribute('data-bs-dismiss', 'toast');
    dismiss.setAttribute('aria-label', 'Close');
    wrapper.appendChild(dismiss);

    toastEl.appendChild(wrapper);
    container.appendChild(toastEl);

    const toast = bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 5000 });
    toastEl.addEventListener('hidden.bs.toast', () => {
      toast.dispose();
      toastEl.remove();
    });
    toast.show();
  }

  async function sendJson(url, { method = 'GET', body } = {}) {
    const headers = { Accept: 'application/json' };
    let payload;
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }

    const response = await fetch(url, {
      method,
      headers,
      body: payload,
      credentials: 'same-origin'
    });

    const contentType = response.headers.get('content-type') || '';
    const isJson = contentType.includes('application/json');
    const data = isJson ? await response.json().catch(() => null) : null;

    if (!response.ok) {
      throw new HttpError(response, data);
    }

    return data;
  }

  function buildChecklistUrl(stageCode, suffix = '') {
    const encodedVersion = encodeURIComponent(state.version);
    const encodedStage = encodeURIComponent(stageCode);
    return `/api/processes/${encodedVersion}/stages/${encodedStage}/checklist${suffix}`;
  }

  function normaliseFlow(dto) {
    const nodes = Array.isArray(dto?.nodes)
      ? dto.nodes.map((node) => ({
        code: node.code || node.id,
        name: node.name || node.label || node.code,
        sequence: Number.parseInt(node.sequence, 10) || 0,
        optional: Boolean(node.optional),
        parallelGroup: node.parallelGroup || null,
        dependsOn: Array.isArray(node.dependsOn) ? node.dependsOn.map((d) => String(d)) : []
      }))
      : [];

    nodes.sort((a, b) => {
      if (a.sequence === b.sequence) {
        return a.code.localeCompare(b.code);
      }
      return a.sequence - b.sequence;
    });

    const edges = Array.isArray(dto?.edges)
      ? dto.edges.map((edge, index) => ({
        source: edge.source || edge.from || edge.u || '',
        target: edge.target || edge.to || edge.v || '',
        id: edge.id || `${edge.source || edge.from || edge.u || 'edge'}-${edge.target || edge.to || edge.v || index}`
      })).filter((edge) => edge.source && edge.target)
      : [];

    return {
      version: dto?.version || state.version,
      nodes,
      edges
    };
  }

  function normaliseChecklist(dto) {
    if (!dto) {
      return null;
    }

    const items = Array.isArray(dto.items)
      ? dto.items.map((item) => ({
        id: item.id,
        text: item.text || '',
        sequence: Number.parseInt(item.sequence, 10) || 0,
        rowVersion: item.rowVersion,
        updatedOn: item.updatedOn ? new Date(item.updatedOn) : null,
        updatedBy: item.updatedByUserId || null
      })).sort((a, b) => {
        if (a.sequence === b.sequence) {
          return a.id - b.id;
        }
        return a.sequence - b.sequence;
      })
      : [];

    return {
      id: dto.id,
      version: dto.version,
      stageCode: dto.stageCode,
      rowVersion: dto.rowVersion,
      items
    };
  }

  function setStageDetails(stage) {
    if (!stage) {
      updateElements(stageTitleEls, 'Select a stage');
      updateElements(stageSubtitleEls, 'Choose a stage on the diagram to see its checklist.');
      updateElements(stageCodeEls, '—');
      updateElements(stageParallelEls, '—');
      updateDependencies([]);
      optionalBadgeEls.forEach((el) => { el.hidden = true; });
      toggleActionGroups(false);
      return;
    }

    const title = `${stage.sequence}. ${stage.name}`;
    updateElements(stageTitleEls, title);
    updateElements(stageSubtitleEls, 'Review the required activities before progressing to the next milestone.');
    updateElements(stageCodeEls, stage.code);
    updateElements(stageParallelEls, stage.parallelGroup || '—');
    updateDependencies(stage.dependsOn || []);
    optionalBadgeEls.forEach((el) => { el.hidden = !stage.optional; });
    toggleActionGroups(canEdit);
  }

  function updateElements(elements, text) {
    elements.forEach((el) => {
      el.textContent = text;
    });
  }

  function updateDependencies(dependsOnCodes) {
    const codes = Array.isArray(dependsOnCodes) ? dependsOnCodes : [];
    const fragmentFactory = (code) => {
      const badge = document.createElement('span');
      badge.className = 'badge rounded-pill bg-light border text-secondary me-2 mb-2';
      const stage = state.stageByCode.get(code);
      badge.textContent = stage ? `${code} · ${stage.name}` : code;
      return badge;
    };

    stageDependenciesEls.forEach((container) => {
      container.innerHTML = '';
      if (codes.length === 0) {
        const empty = document.createElement('span');
        empty.className = 'text-muted';
        empty.textContent = 'None';
        container.appendChild(empty);
        return;
      }

      codes.forEach((code) => container.appendChild(fragmentFactory(code)));
    });
  }

  function toggleActionGroups(visible) {
    actionGroups.forEach((group) => {
      group.hidden = !visible;
      const buttons = Array.from(group.querySelectorAll('button'));
      buttons.forEach((btn) => {
        btn.disabled = !visible || !state.selectedStage;
      });
    });
  }

  function formatUpdated(item) {
    if (!item.updatedOn) {
      return null;
    }

    const formatter = new Intl.DateTimeFormat(undefined, {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit'
    });

    const dateText = formatter.format(item.updatedOn);
    if (item.updatedBy) {
      return `Updated ${dateText} · ${item.updatedBy}`;
    }

    return `Updated ${dateText}`;
  }

  function renderChecklist(checklist, options = {}) {
    const isLoading = options.loading === true;
    const errorMessage = options.errorMessage || null;

    checklistLists.forEach((list) => {
      list.setAttribute('aria-busy', String(isLoading));
      list.innerHTML = '';

      if (isLoading) {
        const loading = document.createElement('li');
        loading.className = 'checklist-empty';
        loading.textContent = 'Loading…';
        list.appendChild(loading);
        return;
      }

      if (errorMessage) {
        const error = document.createElement('li');
        error.className = 'checklist-empty text-danger';
        error.textContent = errorMessage;
        list.appendChild(error);
        return;
      }

      if (!checklist || checklist.items.length === 0) {
        const empty = document.createElement('li');
        empty.className = 'checklist-empty';
        empty.textContent = state.selectedStage ? 'No checklist items defined for this stage yet.' : 'Select a stage to see its checklist.';
        list.appendChild(empty);
        return;
      }

      checklist.items.forEach((item) => {
        const li = document.createElement('li');
        li.className = 'checklist-item';
        if (!state.canEdit) {
          li.classList.add('read-only');
        }
        li.dataset.itemId = String(item.id);
        li.dataset.rowVersion = item.rowVersion ? String(item.rowVersion) : '';
        li.dataset.sequence = String(item.sequence);

        if (state.canEdit) {
          const handle = document.createElement('button');
          handle.type = 'button';
          handle.className = 'btn btn-link p-0 checklist-handle';
          handle.setAttribute('data-handle', 'true');
          handle.innerHTML = '<i class="bi bi-grip-vertical" aria-hidden="true"></i><span class="visually-hidden">Reorder item</span>';
          li.appendChild(handle);
        }

        const body = document.createElement('div');
        body.className = 'item-body';
        const paragraph = document.createElement('p');
        paragraph.className = 'mb-1';
        paragraph.textContent = item.text;
        body.appendChild(paragraph);

        const metaText = formatUpdated(item);
        if (metaText) {
          const meta = document.createElement('div');
          meta.className = 'small text-muted';
          meta.textContent = metaText;
          body.appendChild(meta);
        }

        li.appendChild(body);

        if (state.canEdit) {
          const actions = document.createElement('div');
          actions.className = 'item-actions btn-group btn-group-sm';

          const editBtn = document.createElement('button');
          editBtn.type = 'button';
          editBtn.className = 'btn btn-outline-secondary';
          editBtn.dataset.action = 'edit-item';
          editBtn.textContent = 'Edit';

          const deleteBtn = document.createElement('button');
          deleteBtn.type = 'button';
          deleteBtn.className = 'btn btn-outline-danger';
          deleteBtn.dataset.action = 'delete-item';
          deleteBtn.textContent = 'Delete';

          actions.appendChild(editBtn);
          actions.appendChild(deleteBtn);
          li.appendChild(actions);
        }

        list.appendChild(li);
      });
    });

    setupSortable();
  }

  async function setupSortable() {
    if (!state.canEdit || !primaryChecklist) {
      return;
    }

    if (state.sortable) {
      state.sortable.destroy();
      state.sortable = null;
    }

    if (!state.currentChecklist || state.currentChecklist.items.length < 2) {
      return;
    }

    const SortableCtor = await ensureSortable();
    state.sortable = SortableCtor.create(primaryChecklist, {
      animation: 150,
      draggable: '.checklist-item',
      handle: '[data-handle]',
      onEnd: (evt) => {
        if (evt.oldIndex === evt.newIndex) {
          return;
        }
        submitReorder();
      }
    });
  }

  async function submitReorder() {
    if (!state.selectedStage || !state.currentChecklist) {
      return;
    }

    const order = Array.from(primaryChecklist.querySelectorAll('.checklist-item')).map((li, index) => ({
      itemId: Number.parseInt(li.dataset.itemId || '0', 10),
      rowVersion: li.dataset.rowVersion,
      sequence: index + 1
    }));

    const payload = {
      templateRowVersion: state.currentChecklist.rowVersion,
      items: order
    };

    try {
      const data = await sendJson(`${buildChecklistUrl(state.selectedStage)}/reorder`, { method: 'POST', body: payload });
      const checklist = normaliseChecklist(data);
      state.currentChecklist = checklist;
      state.checklistCache.set(state.selectedStage, checklist);
      renderChecklist(checklist);
      showToast('Checklist order updated.', 'success');
    } catch (error) {
      handleChecklistError(error, 'Unable to reorder checklist items.');
    }
  }

  function handleChecklistError(error, fallbackMessage) {
    if (error instanceof HttpError) {
      if (error.status === 409) {
        const message = (error.data && (error.data.message || error.message)) || 'This checklist changed. Reload to continue.';
        showToast(message, 'warning', {
          actionLabel: 'Reload',
          onAction: () => reloadChecklist(true)
        });
        reloadChecklist(true);
        return;
      }

      if (error.status === 404) {
        showToast('The requested item no longer exists. Reloading…', 'warning');
        reloadChecklist(true);
        return;
      }

      showToast(error.message || fallbackMessage, error.status >= 500 ? 'danger' : 'warning');
      return;
    }

    console.error(error);
    showToast(fallbackMessage, 'danger');
  }

  async function loadFlow() {
    if (!version || !flowCanvas) {
      return;
    }

    try {
      const data = await sendJson(`/api/processes/${encodeURIComponent(version)}/flow`);
      const flow = normaliseFlow(data);
      state.flow = flow;
      flow.nodes.forEach((node) => {
        state.stageByCode.set(node.code, node);
      });
      await renderFlow(flow);
      if (flow.nodes.length > 0) {
        selectStage(flow.nodes[0].code);
      } else {
        setStageDetails(null);
        renderChecklist(null);
      }
    } catch (error) {
      console.error(error);
      const placeholder = flowCanvas.querySelector('[data-flow-placeholder]');
      if (placeholder) {
        placeholder.innerHTML = '<p class="text-danger mb-0">Unable to load process flow.</p>';
      }
      setStageDetails(null);
      renderChecklist(null, { errorMessage: 'Unable to load checklist until the flow is available.' });
      showToast('Unable to load process flow.', 'danger');
    }
  }

  async function renderFlow(flow) {
    const cytoscape = await ensureCytoscape();
    const elements = [];

    flow.nodes.forEach((node) => {
      elements.push({
        data: {
          id: node.code,
          label: `${node.sequence}. ${node.name}`,
          code: node.code,
          sequence: node.sequence,
          optional: node.optional,
          parallelGroup: node.parallelGroup || ''
        },
        classes: node.optional ? 'is-optional' : ''
      });
    });

    flow.edges.forEach((edge) => {
      elements.push({
        data: {
          id: edge.id,
          source: edge.source,
          target: edge.target
        }
      });
    });

    if (state.cytoscape) {
      state.cytoscape.destroy();
    }

    const cy = cytoscape({
      container: flowCanvas,
      elements,
      style: [
        {
          selector: 'node',
          style: {
            'background-color': '#0d6efd',
            'border-color': '#0b5ed7',
            'border-width': 2,
            'color': '#0b162b',
            'font-size': 12,
            'font-weight': '600',
            'text-valign': 'center',
            'text-halign': 'center',
            'text-wrap': 'wrap',
            'text-max-width': 120,
            'padding': '12px',
            'shape': 'round-rectangle',
            'background-opacity': 0.08,
            'border-opacity': 0.8
          }
        },
        {
          selector: 'node.is-optional',
          style: {
            'border-style': 'dashed',
            'border-color': '#f59f00'
          }
        },
        {
          selector: 'node.predecessor',
          style: {
            'background-color': '#198754',
            'background-opacity': 0.15,
            'border-color': '#198754',
            'border-opacity': 0.7
          }
        },
        {
          selector: 'node.successor',
          style: {
            'background-color': '#6f42c1',
            'background-opacity': 0.15,
            'border-color': '#6f42c1',
            'border-opacity': 0.7
          }
        },
        {
          selector: 'node.is-selected',
          style: {
            'background-color': '#0d6efd',
            'background-opacity': 0.25,
            'border-color': '#0d6efd',
            'border-width': 3,
            'color': '#052c65'
          }
        },
        {
          selector: 'edge',
          style: {
            'curve-style': 'bezier',
            'target-arrow-shape': 'triangle',
            'target-arrow-color': '#6c757d',
            'line-color': '#adb5bd',
            'width': 2,
            'arrow-scale': 1
          }
        },
        {
          selector: 'edge.successor-edge',
          style: {
            'line-color': '#0d6efd',
            'target-arrow-color': '#0d6efd',
            'width': 3
          }
        },
        {
          selector: 'edge.predecessor-edge',
          style: {
            'line-color': '#198754',
            'target-arrow-color': '#198754',
            'width': 3
          }
        },
        {
          selector: 'edge.is-selected-edge',
          style: {
            'line-color': '#0d6efd',
            'target-arrow-color': '#0d6efd',
            'width': 3
          }
        }
      ],
      layout: {
        name: 'dagre',
        rankDir: 'LR',
        nodeSep: 50,
        rankSep: 100,
        edgeSep: 20
      }
    });

    state.cytoscape = cy;

    cy.once('layoutstop', () => {
      cy.fit(undefined, 40);
      cy.resize();
    });

    cy.on('tap', 'node', (evt) => {
      const node = evt.target;
      if (node && node.id()) {
        selectStage(node.id());
      }
    });

    cy.panzoom({
      zoomFactor: 0.05,
      minZoom: 0.3,
      maxZoom: 2,
      fitPadding: 40
    });

    const placeholder = flowCanvas.querySelector('[data-flow-placeholder]');
    if (placeholder) {
      placeholder.remove();
    }
    flowCanvas.setAttribute('aria-busy', 'false');
    flowCanvas.dataset.ready = 'true';

    window.addEventListener('resize', () => {
      if (!state.cytoscape) {
        return;
      }
      state.cytoscape.resize();
    });
  }

  function highlightStageOnGraph(stageCode) {
    if (!state.cytoscape) {
      return;
    }

    const cy = state.cytoscape;
    cy.nodes().removeClass('is-selected predecessor successor');
    cy.edges().removeClass('is-selected-edge predecessor-edge successor-edge');

    const node = cy.getElementById(stageCode);
    if (!node || node.empty()) {
      return;
    }

    node.addClass('is-selected');
    node.incomers('node').addClass('predecessor');
    node.incomers('edge').addClass('predecessor-edge');
    node.outgoers('node').addClass('successor');
    node.outgoers('edge').addClass('successor-edge');
    node.connectedEdges().addClass('is-selected-edge');

    try {
      cy.animate({ center: { eles: node }, duration: 250 });
    } catch (error) {
      cy.center(node);
    }
  }

  async function selectStage(stageCode) {
    const stage = state.stageByCode.get(stageCode);
    state.selectedStage = stage ? stage.code : null;
    setStageDetails(stage || null);
    toggleActionGroups(canEdit && Boolean(stage));
    if (!stage) {
      renderChecklist(null);
      return;
    }

    highlightStageOnGraph(stage.code);
    loadChecklist(stage.code);
  }

  function loadChecklist(stageCode, options = {}) {
    const { force = false } = options;
    const requestToken = Symbol(stageCode);
    state.checklistPromise = requestToken;
    if (force) {
      state.checklistCache.delete(stageCode);
    }
    state.currentChecklist = null;
    renderChecklist(state.currentChecklist, { loading: true });

    const cached = !force ? state.checklistCache.get(stageCode) : null;
    if (cached) {
      state.currentChecklist = cached;
      renderChecklist(cached);
      state.checklistPromise = null;
      return;
    }

    sendJson(buildChecklistUrl(stageCode))
      .then((data) => {
        if (state.checklistPromise !== requestToken) {
          return;
        }
        const checklist = normaliseChecklist(data);
        state.currentChecklist = checklist;
        state.checklistCache.set(stageCode, checklist);
        renderChecklist(checklist);
      })
      .catch((error) => {
        if (state.checklistPromise !== requestToken) {
          return;
        }
        console.error(error);
        renderChecklist(null, { errorMessage: 'Unable to load checklist for this stage.' });
        handleChecklistError(error, 'Unable to load checklist.');
      })
      .finally(() => {
        if (state.checklistPromise === requestToken) {
          state.checklistPromise = null;
        }
      });
  }

  function reloadChecklist(force = false) {
    if (!state.selectedStage) {
      return;
    }
    loadChecklist(state.selectedStage, { force });
  }

  function handleActionClick(event) {
    const button = event.target.closest('[data-action]');
    if (!button) {
      return;
    }

    const action = button.dataset.action;
    if (!action) {
      return;
    }

    if (!state.selectedStage) {
      showToast('Select a stage first.', 'warning');
      return;
    }

    const itemEl = button.closest('.checklist-item');
    let item;
    if (itemEl && state.currentChecklist) {
      const itemId = Number.parseInt(itemEl.dataset.itemId || '0', 10);
      item = state.currentChecklist.items.find((it) => it.id === itemId) || null;
    }

    switch (action) {
      case 'add-item':
        openItemModal('create');
        break;
      case 'edit-item':
        if (item) {
          openItemModal('edit', item);
        }
        break;
      case 'delete-item':
        if (item) {
          openDeleteModal(item);
        }
        break;
      default:
        break;
    }
  }

  function openItemModal(mode, item = null) {
    if (!itemModal || !itemForm) {
      return;
    }

    itemForm.dataset.mode = mode;
    const textArea = itemForm.querySelector('textarea[name="text"]');
    const itemIdInput = itemForm.querySelector('input[name="itemId"]');
    const itemRowVersionInput = itemForm.querySelector('input[name="itemRowVersion"]');
    const submitLabel = itemForm.querySelector('[data-submit-label]');

    if (mode === 'edit' && item) {
      textArea.value = item.text;
      itemIdInput.value = item.id;
      itemRowVersionInput.value = item.rowVersion || '';
      submitLabel.textContent = 'Save changes';
    } else {
      textArea.value = '';
      itemIdInput.value = '';
      itemRowVersionInput.value = '';
      submitLabel.textContent = 'Add item';
    }

    const spinner = itemForm.querySelector('.spinner-border');
    if (spinner) {
      spinner.hidden = true;
    }

    itemModal.show();
    setTimeout(() => {
      textArea.focus();
    }, 150);
  }

  function openDeleteModal(item) {
    if (!deleteModal || !deleteForm) {
      return;
    }

    const itemIdInput = deleteForm.querySelector('input[name="itemId"]');
    const itemRowVersionInput = deleteForm.querySelector('input[name="itemRowVersion"]');
    const spinner = deleteForm.querySelector('.spinner-border');
    const submitLabel = deleteForm.querySelector('[data-submit-label]');

    itemIdInput.value = item.id;
    itemRowVersionInput.value = item.rowVersion || '';
    submitLabel.textContent = 'Delete item';
    if (spinner) {
      spinner.hidden = true;
    }

    deleteModal.show();
  }

  async function handleItemFormSubmit(event) {
    event.preventDefault();
    if (!state.selectedStage || !state.currentChecklist || !itemForm) {
      showToast('Select a stage first.', 'warning');
      return;
    }

    const formData = new FormData(itemForm);
    const mode = itemForm.dataset.mode === 'edit' ? 'edit' : 'create';
    const text = (formData.get('text') || '').toString().trim();
    const itemId = Number.parseInt((formData.get('itemId') || '0').toString(), 10);
    const itemRowVersion = formData.get('itemRowVersion');

    if (!text) {
      showToast('Checklist item text cannot be empty.', 'warning');
      return;
    }

    const submitButton = itemForm.querySelector('button[type="submit"]');
    const spinner = itemForm.querySelector('.spinner-border');
    if (submitButton) {
      submitButton.disabled = true;
    }
    if (spinner) {
      spinner.hidden = false;
    }

    try {
      if (mode === 'edit') {
        await updateChecklistItem(itemId, text, itemRowVersion);
      } else {
        await createChecklistItem(text);
      }
      if (itemModal) {
        itemModal.hide();
      }
      itemForm.reset();
    } catch (error) {
      handleChecklistError(error, mode === 'edit' ? 'Unable to update checklist item.' : 'Unable to add checklist item.');
    } finally {
      if (submitButton) {
        submitButton.disabled = false;
      }
      if (spinner) {
        spinner.hidden = true;
      }
    }
  }

  async function handleDeleteFormSubmit(event) {
    event.preventDefault();
    if (!state.selectedStage || !state.currentChecklist || !deleteForm) {
      showToast('Select a stage first.', 'warning');
      return;
    }

    const formData = new FormData(deleteForm);
    const itemId = Number.parseInt((formData.get('itemId') || '0').toString(), 10);
    const itemRowVersion = formData.get('itemRowVersion');

    const submitButton = deleteForm.querySelector('button[type="submit"]');
    const spinner = deleteForm.querySelector('.spinner-border');
    if (submitButton) {
      submitButton.disabled = true;
    }
    if (spinner) {
      spinner.hidden = false;
    }

    try {
      await deleteChecklistItem(itemId, itemRowVersion);
      if (deleteModal) {
        deleteModal.hide();
      }
    } catch (error) {
      handleChecklistError(error, 'Unable to delete checklist item.');
    } finally {
      if (submitButton) {
        submitButton.disabled = false;
      }
      if (spinner) {
        spinner.hidden = true;
      }
    }
  }

  async function createChecklistItem(text) {
    const payload = {
      text,
      templateRowVersion: state.currentChecklist?.rowVersion
    };

    const data = await sendJson(buildChecklistUrl(state.selectedStage), { method: 'POST', body: payload });
    const checklist = normaliseChecklist(data);
    state.currentChecklist = checklist;
    state.checklistCache.set(state.selectedStage, checklist);
    renderChecklist(checklist);
    showToast('Checklist item added.', 'success');
  }

  async function updateChecklistItem(itemId, text, itemRowVersion) {
    const payload = {
      text,
      templateRowVersion: state.currentChecklist?.rowVersion,
      itemRowVersion
    };

    const data = await sendJson(`${buildChecklistUrl(state.selectedStage)}/${itemId}`, { method: 'PUT', body: payload });
    const checklist = normaliseChecklist(data);
    state.currentChecklist = checklist;
    state.checklistCache.set(state.selectedStage, checklist);
    renderChecklist(checklist);
    showToast('Checklist item updated.', 'success');
  }

  async function deleteChecklistItem(itemId, itemRowVersion) {
    const payload = {
      templateRowVersion: state.currentChecklist?.rowVersion,
      itemRowVersion
    };

    const data = await sendJson(`${buildChecklistUrl(state.selectedStage)}/${itemId}`, { method: 'DELETE', body: payload });
    const checklist = normaliseChecklist(data);
    state.currentChecklist = checklist;
    state.checklistCache.set(state.selectedStage, checklist);
    renderChecklist(checklist);
    showToast('Checklist item deleted.', 'success');
  }

  function boot() {
    setStageDetails(null);
    renderChecklist(null);
    root.addEventListener('click', handleActionClick);
    if (itemForm) {
      itemForm.addEventListener('submit', handleItemFormSubmit);
    }
    if (deleteForm) {
      deleteForm.addEventListener('submit', handleDeleteFormSubmit);
    }
    loadFlow();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
}
