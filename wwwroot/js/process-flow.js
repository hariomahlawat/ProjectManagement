const root = document.querySelector('[data-process-flow-root]');

if (root) {
  const state = {
    version: (root.dataset.processVersion || '').trim(),
    canEditChecklist: root.dataset.canEditChecklist === 'true',
    canEditPurpose: root.dataset.canEditPurpose === 'true',
    flow: null,
    nodes: [],
    edges: [],
    stageByCode: new Map(),
    incoming: new Map(),
    outgoing: new Map(),
    activeIndex: 0,
    selectedCode: null,
    currentChecklist: null,
    checklistCache: new Map(),
    checklistAbortController: null,
    checklistManageMode: false,
    sortable: null,
    mode: 'journey',
    worldWidth: 2800,
    worldHeight: 900,
    worldScale: 1,
    worldX: 0,
    worldY: 0,
    mapScale: 0.62,
    mapX: 0,
    mapY: 0,
    pointer: null,
    wheelAccumulator: 0,
    wheelLockedUntil: 0,
    authenticationRecoveryStarted: false
  };

  const experience = root.querySelector('[data-process-experience]');
  const scene = root.querySelector('[data-process-scene]');
  const worldViewport = root.querySelector('[data-world-viewport]');
  const world = root.querySelector('[data-process-world]');
  const svg = root.querySelector('[data-process-svg]');
  const nodeLayer = root.querySelector('[data-process-nodes]');
  const placeholder = root.querySelector('[data-flow-placeholder]');
  const stageJump = root.querySelector('[data-stage-jump]');
  const stageSearch = root.querySelector('[data-stage-search]');
  const searchClear = root.querySelector('[data-search-clear]');
  const searchResults = root.querySelector('[data-search-results]');
  const mapTools = root.querySelector('[data-map-tools]');
  const mapZoom = root.querySelector('[data-map-zoom]');
  const wheelCue = root.querySelector('[data-wheel-cue]');
  const progressTrack = root.querySelector('[data-progress-track]');
  const progressCurrent = root.querySelector('[data-progress-current]');
  const progressTotal = root.querySelector('[data-progress-total]');
  const sceneStageLabel = root.querySelector('[data-scene-stage-label]');
  const checklistList = root.querySelector('[data-checklist-list]');
  const checklistCount = root.querySelector('[data-checklist-count]');
  const checklistManage = root.querySelector('[data-checklist-manage]');
  const checklistAdd = root.querySelector('[data-checklist-add]');
  const manageLabel = root.querySelector('[data-manage-label]');
  const purposeEdit = root.querySelector('[data-purpose-edit]');

  const purposeModalElement = document.getElementById('stagePurposeModal');
  const itemModalElement = document.getElementById('checklistItemModal');
  const deleteModalElement = document.getElementById('checklistDeleteModal');
  const purposeModal = purposeModalElement ? bootstrap.Modal.getOrCreateInstance(purposeModalElement) : null;
  const itemModal = itemModalElement ? bootstrap.Modal.getOrCreateInstance(itemModalElement) : null;
  const deleteModal = deleteModalElement ? bootstrap.Modal.getOrCreateInstance(deleteModalElement) : null;
  const purposeForm = purposeModalElement?.querySelector('[data-purpose-form]');
  const purposeText = purposeForm?.querySelector('textarea[name="purpose"]');
  const purposeCharacterCount = purposeForm?.querySelector('[data-purpose-character-count]');
  const itemForm = itemModalElement?.querySelector('[data-checklist-form]');
  const itemText = itemForm?.querySelector('textarea[name="text"]');
  const itemCharacterCount = itemForm?.querySelector('[data-character-count]');
  const deleteForm = deleteModalElement?.querySelector('[data-checklist-delete-form]');

  class HttpError extends Error {
    constructor(response, data) {
      super(data?.message || data?.title || `Request failed (${response.status})`);
      this.status = response.status;
      this.data = data;
    }
  }

  async function sendJson(url, { method = 'GET', body, signal } = {}) {
    const normalizedMethod = String(method || 'GET').toUpperCase();
    const headers = {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    };

    if (!['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(normalizedMethod)) {
      const token = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content')?.trim();
      if (!token) throw new Error('Security token is unavailable. Refresh the page and try again.');
      headers['X-CSRF-TOKEN'] = token;
    }

    let payload;
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }

    const response = await fetch(url, {
      method: normalizedMethod,
      headers,
      body: payload,
      credentials: 'same-origin',
      cache: 'no-store',
      signal
    });

    const redirectedToLogin = response.redirected && /\/Account\/Login/i.test(response.url);
    if (response.status === 401 || redirectedToLogin) {
      recoverAuthentication();
      const error = new HttpError(response, { message: 'Your sign-in session has expired.' });
      error.authenticationHandled = true;
      throw error;
    }

    const contentType = response.headers.get('content-type') || '';
    const data = contentType.includes('application/json')
      ? await response.json().catch(() => null)
      : null;

    if (!response.ok) throw new HttpError(response, data);
    return data;
  }

  function recoverAuthentication() {
    if (state.authenticationRecoveryStarted) return;
    state.authenticationRecoveryStarted = true;
    showToast('Your sign-in session has expired. Reconnecting…', 'warning', 1800);
    window.setTimeout(() => window.location.reload(), 900);
  }

  function flowUrl() {
    return `/api/processes/${encodeURIComponent(state.version)}/flow`;
  }

  function checklistUrl(code, suffix = '') {
    return `/api/processes/${encodeURIComponent(state.version)}/stages/${encodeURIComponent(code)}/checklist${suffix}`;
  }

  function normalizeFlow(dto) {
    const nodes = (Array.isArray(dto?.nodes) ? dto.nodes : [])
      .map((node, index) => ({
        code: String(node.code || '').trim().toUpperCase(),
        name: String(node.name || node.code || '').trim(),
        sequence: Number(node.sequence) || ((index + 1) * 10),
        optional: Boolean(node.optional),
        parallelGroup: node.parallelGroup || null,
        dependsOn: Array.isArray(node.dependsOn)
          ? node.dependsOn.map(code => String(code || '').trim().toUpperCase()).filter(Boolean)
          : []
      }))
      .filter(node => node.code)
      .sort((a, b) => a.sequence - b.sequence || a.code.localeCompare(b.code));

    nodes.forEach((node, index) => {
      node.displayIndex = index + 1;
      node.searchText = `${node.code} ${node.name}`.toLowerCase();
    });

    const edges = (Array.isArray(dto?.edges) ? dto.edges : [])
      .map(edge => ({
        source: String(edge.source || '').trim().toUpperCase(),
        target: String(edge.target || '').trim().toUpperCase()
      }))
      .filter(edge => edge.source && edge.target);

    return { version: dto?.version || state.version, nodes, edges };
  }

  function normalizeChecklist(dto) {
    return {
      id: Number(dto?.id) || 0,
      version: dto?.version || state.version,
      stageCode: String(dto?.stageCode || '').toUpperCase(),
      purpose: String(dto?.purpose || '').trim(),
      purposeUpdatedByUserId: dto?.purposeUpdatedByUserId || null,
      purposeUpdatedOn: dto?.purposeUpdatedOn || null,
      updatedByUserId: dto?.updatedByUserId || null,
      updatedOn: dto?.updatedOn || null,
      rowVersion: toBase64(dto?.rowVersion),
      items: (Array.isArray(dto?.items) ? dto.items : [])
        .map(item => ({
          id: Number(item.id),
          text: String(item.text || '').trim(),
          sequence: Number(item.sequence) || 0,
          rowVersion: toBase64(item.rowVersion),
          updatedByUserId: item.updatedByUserId || null,
          updatedOn: item.updatedOn || null
        }))
        .sort((a, b) => a.sequence - b.sequence || a.id - b.id)
    };
  }

  function toBase64(value) {
    if (!value) return '';
    if (typeof value === 'string') return value;
    if (Array.isArray(value)) {
      let binary = '';
      value.forEach(byte => { binary += String.fromCharCode(byte); });
      return btoa(binary);
    }
    return '';
  }

  function buildGraph() {
    state.stageByCode = new Map(state.nodes.map(node => [node.code, node]));
    state.incoming = new Map(state.nodes.map(node => [node.code, []]));
    state.outgoing = new Map(state.nodes.map(node => [node.code, []]));

    state.edges.forEach(edge => {
      if (!state.stageByCode.has(edge.source) || !state.stageByCode.has(edge.target)) return;
      state.outgoing.get(edge.source).push(edge.target);
      state.incoming.get(edge.target).push(edge.source);
    });
  }

  function calculateLayout() {
    const depths = new Map();
    const unresolved = new Set(state.nodes.map(node => node.code));
    let guard = state.nodes.length * 4;

    while (unresolved.size && guard-- > 0) {
      let progressed = false;
      [...unresolved].forEach(code => {
        const prerequisites = state.incoming.get(code) || [];
        if (prerequisites.every(dep => depths.has(dep))) {
          const depth = prerequisites.length
            ? Math.max(...prerequisites.map(dep => depths.get(dep))) + 1
            : 0;
          depths.set(code, depth);
          unresolved.delete(code);
          progressed = true;
        }
      });
      if (!progressed) break;
    }

    unresolved.forEach(code => {
      const node = state.stageByCode.get(code);
      depths.set(code, Math.max(0, Math.floor((node?.displayIndex || 1) - 1)));
    });

    const maxDepth = Math.max(0, ...depths.values());
    const xGap = 205;
    const startX = 190;
    const centerY = 450;
    const groups = new Map();

    state.nodes.forEach(node => {
      node.depth = depths.get(node.code) || 0;
      if (!groups.has(node.depth)) groups.set(node.depth, []);
      groups.get(node.depth).push(node);
    });

    groups.forEach(nodesAtDepth => {
      nodesAtDepth.sort((a, b) => a.sequence - b.sequence || a.code.localeCompare(b.code));
      const count = nodesAtDepth.length;
      nodesAtDepth.forEach((node, index) => {
        node.x = startX + (node.depth * xGap);
        if (count === 1) node.y = centerY;
        else if (count === 2) node.y = index === 0 ? 290 : 610;
        else {
          const spread = 520;
          node.y = centerY - (spread / 2) + ((spread / Math.max(1, count - 1)) * index);
        }
      });
    });

    state.worldWidth = startX + (maxDepth * xGap) + 230;
    state.worldHeight = 900;
    world.style.width = `${state.worldWidth}px`;
    world.style.height = `${state.worldHeight}px`;
    svg.setAttribute('viewBox', `0 0 ${state.worldWidth} ${state.worldHeight}`);
    svg.setAttribute('width', String(state.worldWidth));
    svg.setAttribute('height', String(state.worldHeight));
  }

  function pathForEdge(source, target) {
    const startX = source.x + 78;
    const endX = target.x - 78;
    const startY = source.y;
    const endY = target.y;
    const distance = Math.max(70, endX - startX);
    const curve = Math.min(140, Math.max(60, distance * 0.44));
    return `M ${startX} ${startY} C ${startX + curve} ${startY}, ${endX - curve} ${endY}, ${endX} ${endY}`;
  }

  function renderWorld() {
    nodeLayer.innerHTML = '';
    svg.innerHTML = `
      <defs>
        <linearGradient id="processPathGradient" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="#2d4e78" />
          <stop offset="100%" stop-color="#5f86bb" />
        </linearGradient>
        <linearGradient id="processActiveGradient" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="#4aa6ff" />
          <stop offset="100%" stop-color="#7bdcff" />
        </linearGradient>
        <filter id="processGlow" x="-50%" y="-50%" width="200%" height="200%">
          <feGaussianBlur stdDeviation="6" result="blur" />
          <feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
        </filter>
        <marker id="processArrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" fill="#7896bd"></path>
        </marker>
        <marker id="processArrowActive" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" fill="#7bdcff"></path>
        </marker>
      </defs>
      <g class="process-path-grid" aria-hidden="true">
        <line x1="0" y1="450" x2="${state.worldWidth}" y2="450"></line>
      </g>`;

    state.edges.forEach((edge, index) => {
      const source = state.stageByCode.get(edge.source);
      const target = state.stageByCode.get(edge.target);
      if (!source || !target) return;
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      path.setAttribute('d', pathForEdge(source, target));
      path.setAttribute('data-edge-source', source.code);
      path.setAttribute('data-edge-target', target.code);
      path.setAttribute('marker-end', 'url(#processArrow)');
      path.classList.add('process-connection');
      if (target.optional || (source.optional && target.code !== source.code)) path.classList.add('is-conditional');
      path.style.setProperty('--edge-delay', `${index * 28}ms`);
      svg.appendChild(path);
    });

    state.nodes.forEach(node => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'process-node';
      button.dataset.stageCode = node.code;
      button.style.left = `${node.x}px`;
      button.style.top = `${node.y}px`;
      button.setAttribute('aria-label', `Stage ${node.displayIndex}: ${node.name}`);
      button.setAttribute('aria-pressed', 'false');
      if (node.optional) button.classList.add('is-conditional');
      button.innerHTML = `
        <span class="process-node__halo" aria-hidden="true"></span>
        <span class="process-node__index">${String(node.displayIndex).padStart(2, '0')}</span>
        <span class="process-node__body">
          <strong>${escapeHtml(node.name)}</strong>
          <small>${escapeHtml(node.code)}${node.optional ? ' · Conditional' : ''}</small>
        </span>`;
      nodeLayer.appendChild(button);
    });

    renderProgressTrack();
    placeholder.hidden = true;
    world.setAttribute('aria-busy', 'false');
  }

  function renderProgressTrack() {
    progressTrack.innerHTML = '';
    state.nodes.forEach(node => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'process-progress__dot';
      button.dataset.progressCode = node.code;
      button.title = `${node.displayIndex}. ${node.name}`;
      button.setAttribute('aria-label', `Open ${node.name}`);
      progressTrack.appendChild(button);
    });
    progressTotal.textContent = String(state.nodes.length).padStart(2, '0');
  }

  function populateJump() {
    stageJump.innerHTML = '<option value="">Jump to stage…</option>';
    state.nodes.forEach(node => {
      const option = document.createElement('option');
      option.value = node.code;
      option.textContent = `${String(node.displayIndex).padStart(2, '0')} · ${node.name}`;
      stageJump.appendChild(option);
    });
  }

  function setMode(mode, { preserveCamera = false } = {}) {
    const nextMode = mode === 'map' ? 'map' : 'journey';
    state.mode = nextMode;
    experience.dataset.mode = nextMode;
    root.querySelectorAll('[data-mode-button]').forEach(button => {
      const active = button.dataset.modeButton === nextMode;
      button.classList.toggle('is-active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
    mapTools.hidden = nextMode !== 'map';
    wheelCue.hidden = nextMode !== 'journey';
    scene.classList.toggle('is-map-mode', nextMode === 'map');
    scene.classList.toggle('is-journey-mode', nextMode === 'journey');

    if (nextMode === 'map') {
      if (!preserveCamera) resetMapCamera();
      applyWorldTransform(false);
    } else {
      focusActiveStage(false);
    }
  }

  function viewportSize() {
    const rect = worldViewport.getBoundingClientRect();
    return { width: Math.max(1, rect.width), height: Math.max(1, rect.height) };
  }

  function focusActiveStage(animate = true) {
    const node = state.nodes[state.activeIndex];
    if (!node) return;
    const viewport = viewportSize();
    const scale = clamp(viewport.width / 1120, 0.82, 1.08);
    const visualCenterX = viewport.width * (viewport.width > 1000 ? 0.47 : 0.5);
    const visualCenterY = viewport.height * 0.51;
    const depthPeers = state.nodes.filter(candidate => candidate.depth === node.depth);
    const focusY = depthPeers.length > 1
      ? depthPeers.reduce((sum, candidate) => sum + candidate.y, 0) / depthPeers.length
      : node.y;
    state.worldScale = scale;
    state.worldX = visualCenterX - (node.x * scale);
    state.worldY = visualCenterY - (focusY * scale);
    applyWorldTransform(animate);
  }

  function resetMapCamera() {
    const viewport = viewportSize();
    const fitScale = Math.min(
      (viewport.width - 70) / state.worldWidth,
      (viewport.height - 80) / state.worldHeight
    );
    state.mapScale = clamp(fitScale, 0.46, 0.72);
    state.mapX = (viewport.width - (state.worldWidth * state.mapScale)) / 2;
    state.mapY = (viewport.height - (state.worldHeight * state.mapScale)) / 2;
    updateMapZoom();
  }

  function applyWorldTransform(animate = true) {
    const x = state.mode === 'map' ? state.mapX : state.worldX;
    const y = state.mode === 'map' ? state.mapY : state.worldY;
    const scale = state.mode === 'map' ? state.mapScale : state.worldScale;
    world.classList.toggle('is-moving', animate && !prefersReducedMotion());
    world.style.transform = `translate3d(${x}px, ${y}px, 0) scale(${scale})`;
    if (animate) window.setTimeout(() => world.classList.remove('is-moving'), 900);
  }

  function updateMapZoom() {
    if (mapZoom) mapZoom.textContent = `${Math.round(state.mapScale * 100)}%`;
  }

  function zoomMap(delta) {
    if (state.mode !== 'map') return;
    const viewport = viewportSize();
    const oldScale = state.mapScale;
    const newScale = clamp(oldScale + delta, 0.42, 1.15);
    if (Math.abs(newScale - oldScale) < 0.001) return;
    const centerWorldX = (viewport.width / 2 - state.mapX) / oldScale;
    const centerWorldY = (viewport.height / 2 - state.mapY) / oldScale;
    state.mapScale = newScale;
    state.mapX = viewport.width / 2 - centerWorldX * newScale;
    state.mapY = viewport.height / 2 - centerWorldY * newScale;
    updateMapZoom();
    applyWorldTransform(true);
  }

  async function selectStage(code, { updateHash = true, animate = true } = {}) {
    const node = state.stageByCode.get(String(code || '').toUpperCase());
    if (!node) return;
    state.activeIndex = node.displayIndex - 1;
    state.selectedCode = node.code;
    state.checklistManageMode = false;
    updateSelection();
    renderStageHeader(node);
    if (state.mode === 'journey') focusActiveStage(animate);
    stageJump.value = node.code;
    if (updateHash) history.replaceState(null, '', `#stage-${node.code.toLowerCase()}`);
    await loadChecklist(node.code);
  }

  function updateSelection() {
    const active = state.nodes[state.activeIndex];
    const activeCode = active?.code;
    const activeDepth = active?.depth ?? -1;

    root.querySelectorAll('.process-node[data-stage-code]').forEach(button => {
      const node = state.stageByCode.get(button.dataset.stageCode);
      const selected = node?.code === activeCode;
      const past = node && (node.depth < activeDepth || (node.depth === activeDepth && node.displayIndex < active.displayIndex));
      button.classList.toggle('is-active', selected);
      button.classList.toggle('is-past', Boolean(past));
      button.setAttribute('aria-pressed', selected ? 'true' : 'false');
    });

    root.querySelectorAll('.process-connection').forEach(path => {
      const source = path.dataset.edgeSource;
      const target = path.dataset.edgeTarget;
      const selected = target === activeCode || source === activeCode;
      const targetNode = state.stageByCode.get(target);
      const traversed = targetNode && targetNode.depth <= activeDepth;
      path.classList.toggle('is-active', selected);
      path.classList.toggle('is-traversed', Boolean(traversed));
      path.setAttribute('marker-end', selected ? 'url(#processArrowActive)' : 'url(#processArrow)');
    });

    root.querySelectorAll('[data-progress-code]').forEach(dot => {
      const node = state.stageByCode.get(dot.dataset.progressCode);
      dot.classList.toggle('is-active', node?.code === activeCode);
      dot.classList.toggle('is-past', Boolean(node && node.displayIndex < active.displayIndex));
    });

    progressCurrent.textContent = String(active?.displayIndex || 0).padStart(2, '0');
    sceneStageLabel.textContent = active ? `${String(active.displayIndex).padStart(2, '0')} · ${active.name}` : 'Process overview';

    root.querySelectorAll('[data-action="previous-stage"]').forEach(button => {
      button.disabled = state.activeIndex <= 0;
    });
    root.querySelectorAll('[data-action="next-stage"]').forEach(button => {
      button.disabled = state.activeIndex >= state.nodes.length - 1;
    });
  }

  function renderStageHeader(node) {
    setText('[data-stage-number]', String(node.displayIndex).padStart(2, '0'));
    setText('[data-stage-code-label]', node.code);
    setText('[data-stage-title]', node.name);
    root.querySelectorAll('[data-stage-conditional]').forEach(element => {
      element.hidden = !node.optional;
    });
    purposeEdit.hidden = !state.canEditPurpose;
    checklistManage.hidden = !state.canEditChecklist;
    checklistAdd.hidden = true;
    manageLabel.textContent = 'Manage';
    setText('[data-stage-purpose]', 'Loading stage purpose…');
    setText('[data-purpose-updated]', '');
    renderChecklistLoading();
  }

  async function loadChecklist(code, { force = false } = {}) {
    const normalizedCode = String(code || '').toUpperCase();
    if (!normalizedCode) return;

    if (!force && state.checklistCache.has(normalizedCode)) {
      applyChecklist(state.checklistCache.get(normalizedCode));
      return;
    }

    state.checklistAbortController?.abort();
    const controller = new AbortController();
    state.checklistAbortController = controller;
    checklistList.setAttribute('aria-busy', 'true');

    try {
      const dto = await sendJson(checklistUrl(normalizedCode), { signal: controller.signal });
      if (controller.signal.aborted || state.selectedCode !== normalizedCode) return;
      const checklist = normalizeChecklist(dto);
      state.checklistCache.set(normalizedCode, checklist);
      applyChecklist(checklist);
    } catch (error) {
      if (error?.name === 'AbortError') return;
      handleError(error, 'Unable to load stage guidance.');
      if (state.selectedCode === normalizedCode) renderChecklistError();
    }
  }

  function applyChecklist(checklist) {
    state.currentChecklist = checklist;
    renderPurpose(checklist);
    renderChecklist(checklist);
  }

  function renderPurpose(checklist) {
    setText('[data-stage-purpose]', checklist?.purpose || 'Purpose not recorded.');
    const updated = checklist?.purposeUpdatedOn ? formatDateTime(checklist.purposeUpdatedOn) : '';
    setText('[data-purpose-updated]', updated ? `Updated ${updated}` : '');
  }

  function renderChecklistLoading() {
    destroySortable();
    checklistCount.textContent = '0';
    checklistList.setAttribute('aria-busy', 'true');
    checklistList.innerHTML = `
      <li class="stage-checklist__state">
        <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
        <span>Loading checklist…</span>
      </li>`;
  }

  function renderChecklistError() {
    destroySortable();
    checklistList.setAttribute('aria-busy', 'false');
    checklistList.innerHTML = `
      <li class="stage-checklist__state stage-checklist__state--error">
        <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
        <span>Unable to load this checklist.</span>
        <button type="button" data-action="retry-checklist">Retry</button>
      </li>`;
  }

  function renderChecklist(checklist) {
    destroySortable();
    const items = checklist?.items || [];
    checklistCount.textContent = String(items.length);
    checklistList.setAttribute('aria-busy', 'false');
    checklistAdd.hidden = !(state.canEditChecklist && state.checklistManageMode);
    manageLabel.textContent = state.checklistManageMode ? 'Done' : 'Manage';
    checklistManage.classList.toggle('is-active', state.checklistManageMode);

    if (!items.length) {
      checklistList.innerHTML = `
        <li class="stage-checklist__state">
          <i class="bi bi-list-check" aria-hidden="true"></i>
          <span>No checklist items are defined for this stage.</span>
        </li>`;
      return;
    }

    checklistList.innerHTML = items.map((item, index) => `
      <li class="stage-checklist__item${state.checklistManageMode ? ' is-managing' : ''}"
          data-checklist-item-id="${item.id}"
          data-item-row-version="${escapeHtml(item.rowVersion)}">
        <span class="stage-checklist__drag" aria-hidden="true"><i class="bi bi-grip-vertical"></i></span>
        <span class="stage-checklist__number">${String(index + 1).padStart(2, '0')}</span>
        <span class="stage-checklist__text">${escapeHtml(item.text)}</span>
        <span class="stage-checklist__item-actions">
          <button type="button" data-action="edit-item" aria-label="Edit checklist item"><i class="bi bi-pencil"></i></button>
          <button type="button" data-action="delete-item" aria-label="Delete checklist item"><i class="bi bi-trash3"></i></button>
        </span>
      </li>`).join('');

    if (state.canEditChecklist && state.checklistManageMode && window.Sortable) {
      state.sortable = window.Sortable.create(checklistList, {
        animation: 180,
        handle: '.stage-checklist__drag',
        ghostClass: 'is-dragging',
        chosenClass: 'is-chosen',
        onEnd: persistChecklistOrder
      });
    }
  }

  function destroySortable() {
    state.sortable?.destroy();
    state.sortable = null;
  }

  async function persistChecklistOrder() {
    if (!state.currentChecklist || !state.selectedCode) return;
    const items = [...checklistList.querySelectorAll('[data-checklist-item-id]')].map((element, index) => ({
      itemId: Number(element.dataset.checklistItemId),
      sequence: index + 1,
      rowVersion: element.dataset.itemRowVersion || ''
    }));

    try {
      const dto = await sendJson(`${checklistUrl(state.selectedCode)}/reorder`, {
        method: 'POST',
        body: {
          templateRowVersion: state.currentChecklist.rowVersion,
          items
        }
      });
      cacheChecklist(normalizeChecklist(dto));
      showToast('Checklist order updated.', 'success');
    } catch (error) {
      await handleMutationError(error, 'Unable to reorder checklist items.');
    }
  }

  function cacheChecklist(checklist) {
    state.checklistCache.set(checklist.stageCode, checklist);
    if (state.selectedCode === checklist.stageCode) applyChecklist(checklist);
  }

  function toggleChecklistManageMode() {
    if (!state.canEditChecklist || !state.currentChecklist) return;
    state.checklistManageMode = !state.checklistManageMode;
    renderChecklist(state.currentChecklist);
  }

  function openPurposeModal() {
    if (!state.canEditPurpose || !state.currentChecklist || !purposeModal || !purposeText) return;
    purposeText.value = state.currentChecklist.purpose || '';
    updatePurposeCharacterCount();
    purposeModalElement.addEventListener('shown.bs.modal', () => purposeText.focus(), { once: true });
    purposeModal.show();
  }

  async function submitPurpose(event) {
    event.preventDefault();
    if (!state.currentChecklist || !state.selectedCode || !purposeText) return;
    const purpose = purposeText.value.trim();
    if (!purpose) return showToast('Stage purpose cannot be empty.', 'warning');
    setFormBusy(purposeForm, true);
    try {
      const dto = await sendJson(`${checklistUrl(state.selectedCode)}/purpose`, {
        method: 'PUT',
        body: {
          purpose,
          templateRowVersion: state.currentChecklist.rowVersion
        }
      });
      cacheChecklist(normalizeChecklist(dto));
      purposeModal.hide();
      showToast('Stage purpose updated.', 'success');
    } catch (error) {
      await handleMutationError(error, 'Unable to update stage purpose.');
    } finally {
      setFormBusy(purposeForm, false);
    }
  }

  function openItemModal(mode, item = null) {
    if (!state.canEditChecklist || !state.currentChecklist || !itemModal || !itemForm || !itemText) return;
    const editing = mode === 'edit' && item;
    itemForm.dataset.mode = editing ? 'edit' : 'create';
    itemForm.querySelector('input[name="itemId"]').value = editing ? item.id : '';
    itemForm.querySelector('input[name="itemRowVersion"]').value = editing ? item.rowVersion : '';
    itemText.value = editing ? item.text : '';
    itemForm.querySelector('[data-submit-label]').textContent = editing ? 'Save item' : 'Add item';
    document.getElementById('checklistItemModalLabel').textContent = editing ? 'Edit checklist item' : 'Add checklist item';
    updateItemCharacterCount();
    itemModalElement.addEventListener('shown.bs.modal', () => itemText.focus(), { once: true });
    itemModal.show();
  }

  function openDeleteModal(item) {
    if (!state.canEditChecklist || !deleteModal || !deleteForm) return;
    deleteForm.querySelector('input[name="itemId"]').value = item.id;
    deleteForm.querySelector('input[name="itemRowVersion"]').value = item.rowVersion || '';
    deleteModal.show();
  }

  async function submitItem(event) {
    event.preventDefault();
    if (!state.currentChecklist || !state.selectedCode || !itemText) return;
    const text = itemText.value.trim();
    if (!text) return showToast('Checklist item text cannot be empty.', 'warning');
    const editing = itemForm.dataset.mode === 'edit';
    const itemId = Number(itemForm.querySelector('input[name="itemId"]').value || 0);
    const itemRowVersion = itemForm.querySelector('input[name="itemRowVersion"]').value || '';
    setFormBusy(itemForm, true);
    try {
      const dto = await sendJson(editing ? `${checklistUrl(state.selectedCode)}/${itemId}` : checklistUrl(state.selectedCode), {
        method: editing ? 'PUT' : 'POST',
        body: editing
          ? { text, templateRowVersion: state.currentChecklist.rowVersion, itemRowVersion }
          : { text, templateRowVersion: state.currentChecklist.rowVersion }
      });
      cacheChecklist(normalizeChecklist(dto));
      itemModal.hide();
      showToast(editing ? 'Checklist item updated.' : 'Checklist item added.', 'success');
    } catch (error) {
      await handleMutationError(error, editing ? 'Unable to update checklist item.' : 'Unable to add checklist item.');
    } finally {
      setFormBusy(itemForm, false);
    }
  }

  async function submitDelete(event) {
    event.preventDefault();
    if (!state.currentChecklist || !state.selectedCode) return;
    const itemId = Number(deleteForm.querySelector('input[name="itemId"]').value || 0);
    const itemRowVersion = deleteForm.querySelector('input[name="itemRowVersion"]').value || '';
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
      await handleMutationError(error, 'Unable to delete checklist item.');
    } finally {
      setFormBusy(deleteForm, false);
    }
  }

  async function handleMutationError(error, fallback) {
    if (error?.authenticationHandled || error?.status === 401) return;
    if (error?.status === 409) {
      showToast('This stage guidance changed in another session. Loading the latest version.', 'warning');
      state.checklistCache.delete(state.selectedCode);
      await loadChecklist(state.selectedCode, { force: true });
      return;
    }
    if (error?.status === 403) {
      showToast('You are not authorised to make this change.', 'danger');
      return;
    }
    showToast(error?.message || fallback, 'danger');
  }

  function findChecklistItem(button) {
    const element = button.closest('[data-checklist-item-id]');
    const id = Number(element?.dataset.checklistItemId || 0);
    return state.currentChecklist?.items.find(item => item.id === id) || null;
  }

  function goRelative(delta) {
    const nextIndex = clamp(state.activeIndex + delta, 0, state.nodes.length - 1);
    if (nextIndex === state.activeIndex) return;
    selectStage(state.nodes[nextIndex].code);
  }

  function applySearch() {
    const term = stageSearch.value.trim().toLowerCase();
    searchClear.hidden = !term;
    if (!term) {
      searchResults.hidden = true;
      searchResults.innerHTML = '';
      return;
    }

    const matches = state.nodes.filter(node => node.searchText.includes(term)).slice(0, 8);
    searchResults.innerHTML = matches.length
      ? matches.map(node => `
          <button type="button" data-search-stage="${node.code}">
            <span>${String(node.displayIndex).padStart(2, '0')}</span>
            <strong>${escapeHtml(node.name)}</strong>
            <small>${escapeHtml(node.code)}</small>
          </button>`).join('')
      : '<div class="process-search-results__empty">No matching stage</div>';
    searchResults.hidden = false;
  }

  function initialCodeFromHash() {
    const match = location.hash.match(/^#stage-(.+)$/i);
    return match ? match[1].toUpperCase() : null;
  }

  async function loadFlow() {
    try {
      const dto = await sendJson(flowUrl());
      const normalized = normalizeFlow(dto);
      state.flow = normalized;
      state.nodes = normalized.nodes;
      state.edges = normalized.edges;
      buildGraph();
      calculateLayout();
      renderWorld();
      populateJump();
      setText('[data-stage-count]', String(state.nodes.length));
      const initialCode = initialCodeFromHash();
      const initial = initialCode && state.stageByCode.has(initialCode) ? initialCode : state.nodes[0]?.code;
      if (initial) await selectStage(initial, { updateHash: Boolean(initialCode), animate: false });
      setMode('journey');
    } catch (error) {
      console.error(error);
      placeholder.innerHTML = `
        <div class="process-loading__error">
          <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
          <strong>Unable to load the procurement workflow.</strong>
          <span>Refresh the page or contact the system administrator.</span>
        </div>`;
    }
  }

  function handleSceneWheel(event) {
    if (state.mode !== 'journey' || !state.nodes.length) return;
    if (event.target.closest('.stage-guide, .modal, input, textarea, select')) return;
    const now = performance.now();
    if (now < state.wheelLockedUntil) {
      event.preventDefault();
      return;
    }

    state.wheelAccumulator += event.deltaY;
    if (Math.abs(state.wheelAccumulator) < 45) return;
    const direction = state.wheelAccumulator > 0 ? 1 : -1;
    state.wheelAccumulator = 0;
    const atBoundary = (direction < 0 && state.activeIndex === 0)
      || (direction > 0 && state.activeIndex === state.nodes.length - 1);
    if (atBoundary) return;
    event.preventDefault();
    state.wheelLockedUntil = now + 560;
    goRelative(direction);
  }

  function beginMapDrag(event) {
    if (state.mode !== 'map' || event.button !== 0) return;
    state.pointer = {
      id: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      mapX: state.mapX,
      mapY: state.mapY
    };
    worldViewport.setPointerCapture?.(event.pointerId);
    scene.classList.add('is-panning');
  }

  function moveMapDrag(event) {
    if (!state.pointer || state.pointer.id !== event.pointerId || state.mode !== 'map') return;
    state.mapX = state.pointer.mapX + (event.clientX - state.pointer.startX);
    state.mapY = state.pointer.mapY + (event.clientY - state.pointer.startY);
    applyWorldTransform(false);
  }

  function endMapDrag(event) {
    if (!state.pointer || state.pointer.id !== event.pointerId) return;
    state.pointer = null;
    scene.classList.remove('is-panning');
    worldViewport.releasePointerCapture?.(event.pointerId);
  }

  function setFormBusy(form, busy) {
    if (!form) return;
    const submit = form.querySelector('button[type="submit"]');
    const spinner = form.querySelector('.spinner-border');
    if (submit) submit.disabled = busy;
    if (spinner) spinner.hidden = !busy;
  }

  function updatePurposeCharacterCount() {
    if (purposeCharacterCount && purposeText) purposeCharacterCount.textContent = `${purposeText.value.length} / 600`;
  }

  function updateItemCharacterCount() {
    if (itemCharacterCount && itemText) itemCharacterCount.textContent = `${itemText.value.length} / 512`;
  }

  function setText(selector, value) {
    root.querySelectorAll(selector).forEach(element => { element.textContent = value ?? ''; });
  }

  function formatDateTime(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat(undefined, { day: '2-digit', month: 'short', year: 'numeric' }).format(date);
  }

  function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
  }

  function prefersReducedMotion() {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>'"]/g, character => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
    }[character]));
  }

  function showToast(message, variant = 'primary', delay = 4300) {
    let container = document.getElementById('processToastContainer');
    if (!container) {
      container = document.createElement('div');
      container.id = 'processToastContainer';
      container.className = 'toast-container position-fixed top-0 end-0 p-3';
      container.style.zIndex = '2100';
      document.body.appendChild(container);
    }
    const element = document.createElement('div');
    element.className = `toast align-items-center text-bg-${variant} border-0`;
    element.innerHTML = '<div class="d-flex"><div class="toast-body"></div><button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button></div>';
    element.querySelector('.toast-body').textContent = message;
    container.appendChild(element);
    const toast = bootstrap.Toast.getOrCreateInstance(element, { delay });
    element.addEventListener('hidden.bs.toast', () => element.remove());
    toast.show();
  }

  function handleError(error, fallback) {
    console.error(error);
    if (error?.authenticationHandled || error?.status === 401) return;
    if (error?.status === 403) showToast('You are not authorised to view this stage guidance.', 'danger');
    else showToast(error?.message || fallback, 'danger');
  }

  root.addEventListener('click', event => {
    const nodeButton = event.target.closest('[data-stage-code]');
    if (nodeButton?.classList.contains('process-node')) {
      selectStage(nodeButton.dataset.stageCode);
      return;
    }

    const progressButton = event.target.closest('[data-progress-code]');
    if (progressButton) {
      selectStage(progressButton.dataset.progressCode);
      return;
    }

    const searchButton = event.target.closest('[data-search-stage]');
    if (searchButton) {
      selectStage(searchButton.dataset.searchStage);
      stageSearch.value = '';
      applySearch();
      return;
    }

    const modeButton = event.target.closest('[data-mode-button]');
    if (modeButton) {
      setMode(modeButton.dataset.modeButton);
      return;
    }

    const actionButton = event.target.closest('[data-action]');
    if (!actionButton) return;
    const action = actionButton.dataset.action;
    if (action === 'begin-journey') {
      setMode('journey');
      selectStage(state.nodes[0]?.code, { animate: false });
      experience.scrollIntoView({ behavior: prefersReducedMotion() ? 'auto' : 'smooth', block: 'start' });
    } else if (action === 'show-map') {
      setMode('map');
      experience.scrollIntoView({ behavior: prefersReducedMotion() ? 'auto' : 'smooth', block: 'start' });
    } else if (action === 'previous-stage') goRelative(-1);
    else if (action === 'next-stage') goRelative(1);
    else if (action === 'toggle-fullscreen') {
      if (!document.fullscreenElement) experience.requestFullscreen?.();
      else document.exitFullscreen?.();
    } else if (action === 'print-process') window.print();
    else if (action === 'zoom-in') zoomMap(0.1);
    else if (action === 'zoom-out') zoomMap(-0.1);
    else if (action === 'reset-map') { resetMapCamera(); applyWorldTransform(true); }
    else if (action === 'edit-purpose') openPurposeModal();
    else if (action === 'toggle-checklist-edit') toggleChecklistManageMode();
    else if (action === 'add-item') openItemModal('create');
    else if (action === 'edit-item') {
      const item = findChecklistItem(actionButton);
      if (item) openItemModal('edit', item);
    } else if (action === 'delete-item') {
      const item = findChecklistItem(actionButton);
      if (item) openDeleteModal(item);
    } else if (action === 'retry-checklist' && state.selectedCode) {
      state.checklistCache.delete(state.selectedCode);
      loadChecklist(state.selectedCode, { force: true });
    }
  });

  stageSearch.addEventListener('input', applySearch);
  stageSearch.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      const match = state.nodes.find(node => node.searchText.includes(stageSearch.value.trim().toLowerCase()));
      if (match) selectStage(match.code);
    }
    if (event.key === 'Escape') {
      stageSearch.value = '';
      applySearch();
    }
  });
  searchClear.addEventListener('click', () => {
    stageSearch.value = '';
    applySearch();
    stageSearch.focus();
  });
  document.addEventListener('click', event => {
    if (!event.target.closest('.process-search-wrap')) searchResults.hidden = true;
  });
  stageJump.addEventListener('change', () => {
    if (stageJump.value) selectStage(stageJump.value);
  });

  scene.addEventListener('wheel', handleSceneWheel, { passive: false });
  worldViewport.addEventListener('pointerdown', beginMapDrag);
  worldViewport.addEventListener('pointermove', moveMapDrag);
  worldViewport.addEventListener('pointerup', endMapDrag);
  worldViewport.addEventListener('pointercancel', endMapDrag);

  purposeForm?.addEventListener('submit', submitPurpose);
  purposeText?.addEventListener('input', updatePurposeCharacterCount);
  itemForm?.addEventListener('submit', submitItem);
  itemText?.addEventListener('input', updateItemCharacterCount);
  deleteForm?.addEventListener('submit', submitDelete);

  document.addEventListener('keydown', event => {
    if (event.target?.matches?.('input, textarea, select, button') || event.target?.isContentEditable) return;
    if (event.key === 'ArrowLeft') { event.preventDefault(); goRelative(-1); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); goRelative(1); }
    else if (event.key === 'Escape' && document.fullscreenElement) document.exitFullscreen?.();
    else if (event.key.toLowerCase() === 'm') setMode(state.mode === 'map' ? 'journey' : 'map');
  });

  window.addEventListener('resize', () => {
    if (!state.nodes.length) return;
    if (state.mode === 'map') resetMapCamera();
    else focusActiveStage(false);
    applyWorldTransform(false);
  });

  document.addEventListener('fullscreenchange', () => {
    experience.classList.toggle('is-fullscreen', document.fullscreenElement === experience);
    window.setTimeout(() => {
      if (state.mode === 'map') resetMapCamera();
      else focusActiveStage(false);
      applyWorldTransform(false);
    }, 80);
  });

  loadFlow();
}
