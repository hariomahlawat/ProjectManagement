const root = document.querySelector('[data-process-flow-root]');
const OPTIONAL_STAGE_CODE = 'PNC';
const elk = typeof globalThis !== 'undefined' && typeof globalThis.ELK === 'function'
  ? new globalThis.ELK()
  : null;

if (root) {
  const initialVersion = (root.dataset.processVersion || '').trim();
  const canEdit = root.dataset.canEdit === 'true';
  const flowCanvas = root.querySelector('[data-flow-canvas]');
  const stageTitleEls = Array.from(document.querySelectorAll('[data-stage-title]'));
  const checklistLists = Array.from(document.querySelectorAll('[data-checklist-list]'));
  const primaryChecklist = document.querySelector('[data-checklist-primary]');
  const actionGroups = Array.from(document.querySelectorAll('[data-checklist-actions]'));
  const itemModalEl = document.getElementById('checklistItemModal');
  const deleteModalEl = document.getElementById('checklistDeleteModal');
  const itemForm = itemModalEl ? itemModalEl.querySelector('[data-checklist-form]') : null;
  const deleteForm = deleteModalEl ? deleteModalEl.querySelector('[data-checklist-delete-form]') : null;
  const itemModal = itemModalEl ? new bootstrap.Modal(itemModalEl) : null;
  const deleteModal = deleteModalEl ? new bootstrap.Modal(deleteModalEl) : null;
  const checklistOffcanvasEl = document.getElementById('checklistOffcanvas');
  const checklistOffcanvas = checklistOffcanvasEl ? bootstrap.Offcanvas.getOrCreateInstance(checklistOffcanvasEl) : null;
  const flowPlaceholderTemplate = (() => {
    if (!flowCanvas) {
      return null;
    }

    const placeholder = flowCanvas.querySelector('[data-flow-placeholder]');
    return placeholder ? placeholder.outerHTML : null;
  })();

  const state = {
    version: initialVersion,
    canEdit,
    diagram: null,
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

  let sortableLoader;
  async function ensureSortable() {
    if (!sortableLoader) {
      sortableLoader = Promise.resolve().then(() => {
        if (!window.Sortable) {
          throw new Error('SortableJS failed to load.');
        }

        return window.Sortable;
      });
    }

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

  function readVersionFromRoot() {
    return (root.dataset.processVersion || '').trim();
  }

  function buildFlowUrl(versionOverride) {
    const normalizedVersion = typeof versionOverride === 'string' && versionOverride.trim().length > 0
      ? versionOverride.trim()
      : state.version;

    if (!normalizedVersion) {
      return null;
    }

    const encodedVersion = encodeURIComponent(normalizedVersion);
    return `/api/processes/${encodedVersion}/flow`;
  }

  function buildChecklistUrl(stageCode, suffix = '') {
    const encodedVersion = encodeURIComponent(state.version);
    const encodedStage = encodeURIComponent(stageCode);
    return `/api/processes/${encodedVersion}/stages/${encodedStage}/checklist${suffix}`;
  }

  function createFlowPlaceholderElement() {
    const placeholder = document.createElement('div');
    placeholder.className = 'text-center text-muted py-5';
    placeholder.setAttribute('data-flow-placeholder', '');

    const spinner = document.createElement('div');
    spinner.className = 'spinner-border text-primary mb-3';
    spinner.setAttribute('role', 'status');
    spinner.setAttribute('aria-hidden', 'true');
    placeholder.appendChild(spinner);

    const message = document.createElement('p');
    message.className = 'mb-0';
    message.textContent = 'Loading flow…';
    placeholder.appendChild(message);

    return placeholder;
  }

  function showFlowLoading() {
    if (!flowCanvas) {
      return;
    }

    flowCanvas.setAttribute('aria-busy', 'true');
    flowCanvas.dataset.ready = 'false';

    if (flowCanvas.firstElementChild && flowCanvas.firstElementChild.hasAttribute('data-flow-placeholder')) {
      return;
    }

    if (flowPlaceholderTemplate) {
      flowCanvas.innerHTML = flowPlaceholderTemplate;
      return;
    }

    flowCanvas.innerHTML = '';
    flowCanvas.appendChild(createFlowPlaceholderElement());
  }

  function clearFlowCanvas() {
    if (!flowCanvas) {
      return;
    }

    flowCanvas.innerHTML = '';
    flowCanvas.setAttribute('aria-busy', 'false');
    flowCanvas.dataset.ready = 'false';
  }

  function resetStateForVersionChange() {
    state.stageByCode.clear();
    state.diagram = null;
    state.selectedStage = null;
    state.currentChecklist = null;
    state.checklistCache.clear();
    state.checklistPromise = null;

    if (state.sortable && typeof state.sortable.destroy === 'function') {
      state.sortable.destroy();
    }

    state.sortable = null;
  }

  const versionObserver = new MutationObserver((mutations) => {
    for (const mutation of mutations) {
      if (mutation.type === 'attributes' && mutation.attributeName === 'data-process-version') {
        const updatedVersion = readVersionFromRoot();
        handleVersionChange(updatedVersion);
      }
    }
  });

  function handleVersionChange(newVersion) {
    const normalized = (newVersion || '').trim();
    if (normalized === state.version) {
      return;
    }

    state.version = normalized;
    resetStateForVersionChange();
    toggleActionGroups(false);
    setStageDetails(null);

    if (!normalized) {
      clearFlowCanvas();
      renderChecklist(null, { errorMessage: 'Select a process version to load.' });
      return;
    }

    renderChecklist(null, { loading: true });
    showFlowLoading();
    loadFlow();
  }

  function normaliseFlow(dto) {
    const nodes = Array.isArray(dto?.nodes)
      ? dto.nodes.map((node) => ({
        code: node.code || node.id,
        name: node.name || node.label || node.code,
        sequence: Number.parseInt(node.sequence, 10) || 0,
        displayIndex: 0,
        optional: node.optional === true || node.optional === 'true',
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

    nodes.forEach((node, index) => {
      node.displayIndex = index + 1;
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

  const SVG_NS = 'http://www.w3.org/2000/svg';
  const NODE_SIZES = {
    terminator: { width: 220, height: 72 },
    process: { width: 240, height: 88 },
    decision: { width: 132, height: 132 }
  };

  function createSvgElement(name, attributes = {}) {
    const el = document.createElementNS(SVG_NS, name);
    Object.entries(attributes).forEach(([key, value]) => {
      if (value === null || value === undefined) {
        return;
      }
      el.setAttribute(key, String(value));
    });
    return el;
  }

  function ensureDefs(svg) {
    let defs = svg.querySelector('defs#pm-flow-defs');
    if (defs) {
      defs.innerHTML = '';
    } else {
      defs = createSvgElement('defs', { id: 'pm-flow-defs' });
      svg.prepend(defs);
    }

    const marker = createSvgElement('marker', {
      id: 'pm-flow-arrow',
      orient: 'auto',
      markerWidth: 10,
      markerHeight: 10,
      refX: 8,
      refY: 3
    });
    const markerPath = createSvgElement('path', {
      d: 'M0,0 L8,3 L0,6 Z',
      fill: 'currentColor'
    });
    marker.appendChild(markerPath);
    defs.appendChild(marker);
  }

  function wrapLabelLines(text, maxChars = 20) {
    if (!text) {
      return [''];
    }

    const words = String(text).trim().split(/\s+/);
    const lines = [];
    let current = '';

    words.forEach((word) => {
      const candidate = current ? `${current} ${word}` : word;
      if (candidate.length <= maxChars || !current) {
        current = candidate;
      } else {
        lines.push(current);
        current = word;
      }
    });

    if (current) {
      lines.push(current);
    }

    return lines;
  }

  function createLabelElement(text, layout, options = {}) {
    const lines = wrapLabelLines(text, options.maxChars || 20);
    const lineHeight = options.lineHeight || 20;
    const textEl = createSvgElement('text', {
      class: 'flow-node__label',
      x: layout.width / 2,
      y: layout.height / 2 - ((lines.length - 1) * lineHeight) / 2,
      'text-anchor': 'middle',
      'dominant-baseline': 'middle'
    });

    lines.forEach((line, index) => {
      const tspan = createSvgElement('tspan', {
        x: layout.width / 2,
        dy: index === 0 ? 0 : lineHeight
      });
      tspan.textContent = line;
      textEl.appendChild(tspan);
    });

    return textEl;
  }

  function createNodeGroup(node, layout, modifier) {
    const group = createSvgElement('g', {
      class: `flow-node flow-node--${modifier}${node.optional ? ' is-optional' : ''}`,
      transform: `translate(${layout.x - layout.width / 2} ${layout.y - layout.height / 2})`
    });
    group.dataset.stageCode = node.code;
    group.dataset.stageLabel = node.label;
    group.setAttribute('tabindex', '0');
    group.setAttribute('role', 'button');
    group.setAttribute('aria-label', node.label);
    return group;
  }

  function drawTerminator(node, layout) {
    const group = createNodeGroup(node, layout, 'terminator');
    const rect = createSvgElement('rect', {
      x: 0,
      y: 0,
      width: layout.width,
      height: layout.height,
      rx: layout.height / 2,
      class: 'flow-node__body'
    });
    group.appendChild(rect);
    group.appendChild(createLabelElement(node.label, layout));
    return group;
  }

  function drawProcess(node, layout) {
    const group = createNodeGroup(node, layout, 'process');
    const rect = createSvgElement('rect', {
      x: 0,
      y: 0,
      width: layout.width,
      height: layout.height,
      rx: 18,
      class: 'flow-node__body'
    });
    group.appendChild(rect);
    group.appendChild(createLabelElement(node.label, layout));
    return group;
  }

  function drawDecision(node, layout) {
    const group = createNodeGroup(node, layout, 'decision');
    const polygon = createSvgElement('polygon', {
      points: `${layout.width / 2},0 ${layout.width},${layout.height / 2} ${layout.width / 2},${layout.height} 0,${layout.height / 2}`,
      class: 'flow-node__body'
    });
    group.appendChild(polygon);
    group.appendChild(createLabelElement(node.label, layout));
    return group;
  }

  function drawConnector(pathData, options = {}) {
    return createSvgElement('path', {
      class: `flow-connector${options.dashed ? ' flow-connector--dashed' : ''}`,
      d: pathData,
      'marker-end': 'url(#pm-flow-arrow)'
    });
  }

  function buildGraph(flow) {
    const incoming = new Map();
    const outgoing = new Map();

    flow.nodes.forEach((node) => {
      incoming.set(node.code, []);
      outgoing.set(node.code, []);
    });

    flow.edges.forEach((edge) => {
      const fromList = outgoing.get(edge.source);
      const toList = incoming.get(edge.target);
      if (fromList && !fromList.includes(edge.target)) {
        fromList.push(edge.target);
      }
      if (toList && !toList.includes(edge.source)) {
        toList.push(edge.source);
      }
    });

    flow.nodes.forEach((node) => {
      const incomingList = incoming.get(node.code);
      const outgoingList = outgoing.get(node.code);
      if (incomingList) {
        incoming.set(node.code, incomingList.sort((a, b) => a.localeCompare(b)));
      }
      if (outgoingList) {
        outgoing.set(node.code, outgoingList.sort((a, b) => a.localeCompare(b)));
      }
    });

    return { incoming, outgoing };
  }

  async function computeDiagramLayout(flow) {
    const graph = buildGraph(flow);
    if (!elk) {
      throw new Error('Process layout engine unavailable.');
    }

    const nodeMeta = new Map();
    const children = flow.nodes.map((node) => {
      const incomingCount = graph.incoming.get(node.code)?.length || 0;
      const outgoingCount = graph.outgoing.get(node.code)?.length || 0;
      let shape = 'process';
      if (incomingCount === 0 || outgoingCount === 0) {
        shape = 'terminator';
      } else if (outgoingCount > 1) {
        shape = 'decision';
      }

      const size = NODE_SIZES[shape] || NODE_SIZES.process;
      nodeMeta.set(node.code, { node, shape, size });
      return {
        id: node.code,
        width: size.width,
        height: size.height
      };
    });

    const edges = flow.edges.map((edge, index) => ({
      id: edge.id || `edge-${index}`,
      sources: [edge.source],
      targets: [edge.target]
    }));

    const layout = await elk.layout({
      id: 'root',
      layoutOptions: {
        'elk.algorithm': 'layered',
        'elk.direction': 'DOWN',
        'elk.layered.spacing.nodeNodeBetweenLayers': '80',
        'elk.spacing.nodeNode': '36',
        'elk.layered.nodePlacement.bk.fixedAlignment': 'BALANCED',
        'elk.edgeRouting': 'ORTHOGONAL',
        'elk.layered.feedbackEdges': 'true',
        'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
        'elk.padding': '[48,32,48,32]'
      },
      children,
      edges
    });

    const nodeLayouts = new Map();
    let maxX = 0;
    let maxY = 0;
    (layout.children || []).forEach((child) => {
      const meta = nodeMeta.get(child.id);
      if (!meta) {
        return;
      }
      const centerX = child.x + child.width / 2;
      const centerY = child.y + child.height / 2;
      maxX = Math.max(maxX, child.x + child.width);
      maxY = Math.max(maxY, child.y + child.height);
      nodeLayouts.set(child.id, {
        x: centerX,
        y: centerY,
        width: child.width,
        height: child.height,
        shape: meta.shape,
        label: `${meta.node.displayIndex}. ${meta.node.name}`,
        code: meta.node.code,
        optional: meta.node.optional
      });
    });

    const edgePaths = new Map();
    (layout.edges || []).forEach((edge) => {
      if (!edge.sections || edge.sections.length === 0) {
        return;
      }
      const d = edge.sections
        .map((section) => {
          const commands = [`M ${section.startPoint.x} ${section.startPoint.y}`];
          (section.bendPoints || []).forEach((bend) => {
            commands.push(`L ${bend.x} ${bend.y}`);
          });
          commands.push(`L ${section.endPoint.x} ${section.endPoint.y}`);
          return commands.join(' ');
        })
        .join(' ');
      edgePaths.set(edge.id, d);
    });

    const width = Math.max(layout.width || 0, maxX + 32);
    const height = Math.max(layout.height || 0, maxY + 32);

    return {
      nodes: nodeLayouts,
      width,
      height,
      edgePaths,
      incoming: graph.incoming,
      outgoing: graph.outgoing
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
      toggleActionGroups(false);
      return;
    }

    const title = `${stage.displayIndex}. ${stage.name}`;
    updateElements(stageTitleEls, title);
    toggleActionGroups(canEdit);
  }

  function updateElements(elements, text) {
    elements.forEach((el) => {
      el.textContent = text;
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
    if (!flowCanvas) {
      return;
    }

    const requestedVersion = state.version;
    if (!requestedVersion) {
      clearFlowCanvas();
      return;
    }

    showFlowLoading();

    try {
      const flowUrl = buildFlowUrl(requestedVersion);
      if (!flowUrl) {
        clearFlowCanvas();
        return;
      }

      const data = await sendJson(flowUrl);
      if (state.version !== requestedVersion) {
        return;
      }

      const flow = normaliseFlow(data);
      state.stageByCode.clear();
      flow.nodes.forEach((node) => {
        const stage = { ...node };
        const normalizedCode =
          typeof stage.code === 'string' ? stage.code.toUpperCase() : '';
        stage.optional = normalizedCode === OPTIONAL_STAGE_CODE;
        node.optional = stage.optional;
        state.stageByCode.set(stage.code, stage);
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
      if (flowCanvas) {
        const placeholder = flowCanvas.querySelector('[data-flow-placeholder]');
        if (placeholder) {
          placeholder.innerHTML = '<p class="text-danger mb-0">Unable to load process flow.</p>';
        }
        flowCanvas.setAttribute('aria-busy', 'false');
        flowCanvas.dataset.ready = 'false';
      }

      if (state.version === requestedVersion) {
        setStageDetails(null);
        renderChecklist(null, { errorMessage: 'Unable to load checklist until the flow is available.' });
        showToast('Unable to load process flow.', 'danger');
      }
    }
  }

  async function renderFlow(flow) {
    if (!flowCanvas) {
      return;
    }

    const layout = await computeDiagramLayout(flow);
    if (!layout) {
      return;
    }
    const svg = createSvgElement('svg', {
      class: 'process-flow-diagram',
      viewBox: `0 0 ${layout.width} ${layout.height}`,
      width: '100%',
      height: '100%',
      focusable: 'false',
      'aria-label': 'Process flow diagram'
    });
    ensureDefs(svg);

    const connectorsLayer = createSvgElement('g', { class: 'flow-connectors' });
    const nodesLayer = createSvgElement('g', { class: 'flow-nodes' });
    svg.appendChild(connectorsLayer);
    svg.appendChild(nodesLayer);

    flowCanvas.innerHTML = '';
    flowCanvas.appendChild(svg);

    const nodeElements = new Map();
    const edgeElements = new Map();
    const edgeByKey = new Map();

    flow.nodes.forEach((node) => {
      const layoutInfo = layout.nodes.get(node.code);
      if (!layoutInfo) {
        return;
      }

      const descriptor = {
        ...node,
        label: layoutInfo.label
      };

      let group;
      if (layoutInfo.shape === 'terminator') {
        group = drawTerminator(descriptor, layoutInfo);
      } else if (layoutInfo.shape === 'decision') {
        group = drawDecision(descriptor, layoutInfo);
      } else {
        group = drawProcess(descriptor, layoutInfo);
      }

      group.addEventListener('click', () => {
        selectStage(node.code);
      });

      group.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          selectStage(node.code);
        }
      });

      nodesLayer.appendChild(group);
      nodeElements.set(node.code, { element: group, layout: layoutInfo });
    });

    flow.edges.forEach((edge) => {
      const source = nodeElements.get(edge.source);
      const target = nodeElements.get(edge.target);
      if (!source || !target) {
        return;
      }

      const pathData = layout.edgePaths?.get(edge.id);
      if (!pathData) {
        return;
      }

      const dashed = Boolean(state.stageByCode.get(edge.target)?.optional);
      const connector = drawConnector(pathData, { dashed });
      connector.dataset.edgeId = edge.id;
      connector.dataset.source = edge.source;
      connector.dataset.target = edge.target;

      connectorsLayer.appendChild(connector);

      const edgeEntry = { element: connector, source: edge.source, target: edge.target };
      edgeElements.set(edge.id, edgeEntry);
      edgeByKey.set(`${edge.source}|${edge.target}`, edgeEntry);
    });

    const placeholder = flowCanvas.querySelector('[data-flow-placeholder]');
    if (placeholder) {
      placeholder.remove();
    }
    flowCanvas.setAttribute('aria-busy', 'false');
    flowCanvas.dataset.ready = 'true';

    state.diagram = {
      svg,
      nodes: nodeElements,
      edges: edgeElements,
      edgeByKey,
      incoming: layout.incoming,
      outgoing: layout.outgoing
    };

    if (state.selectedStage) {
      highlightStageOnGraph(state.selectedStage);
    }
  }

  function highlightStageOnGraph(stageCode) {
    if (!state.diagram) {
      return;
    }

    const diagram = state.diagram;

    diagram.nodes.forEach(({ element }) => {
      element.classList.remove('is-selected', 'is-predecessor', 'is-successor');
    });
    diagram.edges.forEach(({ element }) => {
      element.classList.remove('is-selected-edge', 'is-predecessor-edge', 'is-successor-edge');
    });

    const current = diagram.nodes.get(stageCode);
    if (!current) {
      return;
    }

    current.element.classList.add('is-selected');
    if (current.element.parentNode) {
      current.element.parentNode.appendChild(current.element);
    }

    const incomingCodes = diagram.incoming.get(stageCode) || [];
    incomingCodes.forEach((code) => {
      const nodeEntry = diagram.nodes.get(code);
      if (nodeEntry) {
        nodeEntry.element.classList.add('is-predecessor');
      }
      const edgeEntry = diagram.edgeByKey?.get(`${code}|${stageCode}`);
      if (edgeEntry) {
        edgeEntry.element.classList.add('is-predecessor-edge', 'is-selected-edge');
      }
    });

    const outgoingCodes = diagram.outgoing.get(stageCode) || [];
    outgoingCodes.forEach((code) => {
      const nodeEntry = diagram.nodes.get(code);
      if (nodeEntry) {
        nodeEntry.element.classList.add('is-successor');
      }
      const edgeEntry = diagram.edgeByKey?.get(`${stageCode}|${code}`);
      if (edgeEntry) {
        edgeEntry.element.classList.add('is-successor-edge', 'is-selected-edge');
      }
    });

    diagram.edges.forEach((edge) => {
      if (edge.source === stageCode || edge.target === stageCode) {
        edge.element.classList.add('is-selected-edge');
      }
    });
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
    if (checklistOffcanvas && checklistOffcanvasEl && !checklistOffcanvasEl.classList.contains('show')) {
      checklistOffcanvas.show();
    }
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

    if (itemModalEl) {
      itemModalEl.addEventListener('shown.bs.modal', () => {
        const focusTarget = itemForm.querySelector('textarea[name="text"]');
        if (focusTarget) {
          focusTarget.focus();
        }
      }, { once: true });
    }

    itemModal.show();
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
    showFlowLoading();
    root.addEventListener('click', handleActionClick);
    if (itemForm) {
      itemForm.addEventListener('submit', handleItemFormSubmit);
    }
    if (deleteForm) {
      deleteForm.addEventListener('submit', handleDeleteFormSubmit);
    }
    versionObserver.observe(root, { attributes: true, attributeFilter: ['data-process-version'] });

    const currentDatasetVersion = readVersionFromRoot();
    if (currentDatasetVersion !== state.version) {
      handleVersionChange(currentDatasetVersion);
    } else {
      loadFlow();
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
}
