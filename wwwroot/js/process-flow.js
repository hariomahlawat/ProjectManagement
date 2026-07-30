const root = document.querySelector('[data-process-flow-root]');

if (root) {
  const state = {
    version: (root.dataset.processVersion || '').trim(),
    canEditChecklist: root.dataset.canEditChecklist === 'true',
    canEditPurpose: root.dataset.canEditPurpose === 'true',
    flow: null,
    nodes: [],
    edges: [],
    visualEdges: [],
    optionalDetours: [],
    stageByCode: new Map(),
    incoming: new Map(),
    outgoing: new Map(),
    structuralIncoming: new Map(),
    structuralOutgoing: new Map(),
    branchClusters: [],
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
    wheelAccumulator: 0,
    wheelLockedUntil: 0,
    transitionTimer: null,
    guideTimer: null,
    authenticationRecoveryStarted: false,
    theme: 'light'
  };

  const workspace = root.querySelector('[data-process-workspace]');
  const experience = root.querySelector('[data-process-experience]');
  const introduction = root.querySelector('[data-process-introduction]');
  const stageSearchDialog = root.querySelector('[data-stage-search-dialog]');
  const scene = root.querySelector('[data-process-scene]');
  const worldViewport = root.querySelector('[data-world-viewport]');
  const world = root.querySelector('[data-process-world]');
  const svg = root.querySelector('[data-process-svg]');
  const nodeLayer = root.querySelector('[data-process-nodes]');
  const placeholder = root.querySelector('[data-flow-placeholder]');
  const stageSearch = root.querySelector('[data-stage-search]');
  const searchClear = root.querySelector('[data-search-clear]');
  const searchResults = root.querySelector('[data-search-results]');
  const themeToggle = root.querySelector('[data-theme-toggle]');
  const themeIcon = root.querySelector('[data-theme-icon]');
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
  const stageGuide = root.querySelector('[data-stage-guide]');
  const fullscreenExit = root.querySelector('[data-fullscreen-exit]');

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
  function closeIntroduction() {
    if (!introduction?.open) return;
    if (typeof introduction.close === 'function') introduction.close();
    else introduction.removeAttribute('open');
  }

  function showIntroduction() {
    if (!introduction || introduction.open) return;
    if (typeof introduction.showModal === 'function') introduction.showModal();
    else introduction.setAttribute('open', '');
    window.requestAnimationFrame(() => {
      introduction.querySelector('[data-action="begin-journey"]')?.focus();
    });
  }


  function closeStageSearch() {
    if (!stageSearchDialog?.open) return;
    if (typeof stageSearchDialog.close === 'function') stageSearchDialog.close();
    else stageSearchDialog.removeAttribute('open');
  }

  function showStageSearch() {
    if (!stageSearchDialog || stageSearchDialog.open) return;
    if (typeof stageSearchDialog.showModal === 'function') stageSearchDialog.showModal();
    else stageSearchDialog.setAttribute('open', '');
    window.requestAnimationFrame(() => {
      stageSearch.value = '';
      applySearch({ forceOpen: true });
      stageSearch.focus();
    });
  }

  function applyTheme(theme, { persist = true } = {}) {
    const normalized = theme === 'dark' ? 'dark' : 'light';
    state.theme = normalized;
    root.dataset.processTheme = normalized;
    const dark = normalized === 'dark';
    if (themeToggle) {
      themeToggle.setAttribute('aria-pressed', dark ? 'true' : 'false');
      themeToggle.setAttribute('aria-label', dark ? 'Use light theme' : 'Use dark theme');
      themeToggle.title = dark ? 'Use light theme' : 'Use dark theme';
    }
    if (themeIcon) {
      themeIcon.className = dark ? 'bi bi-sun' : 'bi bi-moon-stars';
    }
    if (persist) {
      try { localStorage.setItem('prism.process.theme', normalized); } catch { /* storage is optional */ }
    }
  }

  function loadPreferredTheme() {
    let preferred = 'light';
    try {
      const stored = localStorage.getItem('prism.process.theme');
      if (stored === 'dark' || stored === 'light') preferred = stored;
    } catch { /* storage is optional */ }
    applyTheme(preferred, { persist: false });
  }

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

    state.branchClusters = [];
    state.nodes.filter(node => !node.optional).forEach(source => {
      const branches = (state.outgoing.get(source.code) || [])
        .filter(code => !state.stageByCode.get(code)?.optional);
      if (branches.length < 2) return;

      const downstreamSets = branches.map(code => new Set(
        (state.outgoing.get(code) || []).filter(next => !state.stageByCode.get(next)?.optional)));
      const convergenceCandidates = [...(downstreamSets[0] || [])]
        .filter(code => downstreamSets.every(set => set.has(code)))
        .map(code => state.stageByCode.get(code))
        .filter(Boolean)
        .sort((a, b) => a.displayIndex - b.displayIndex);

      const convergence = convergenceCandidates[0];
      if (!convergence) return;

      state.branchClusters.push({
        source: source.code,
        branches: [...branches],
        convergence: convergence.code,
        codes: new Set([source.code, ...branches, convergence.code])
      });
    });

    buildPresentationTopology();
  }

  function buildPresentationTopology() {
    const optionalCodes = new Set(state.nodes.filter(node => node.optional).map(node => node.code));
    const structuralEdges = state.edges
      .filter(edge => !optionalCodes.has(edge.source) && !optionalCodes.has(edge.target))
      .map(edge => ({ ...edge, kind: 'structural', conditional: false, synthetic: false }));

    state.optionalDetours = state.nodes
      .filter(node => node.optional)
      .map(node => {
        const sourceCode = (state.incoming.get(node.code) || [])
          .map(code => state.stageByCode.get(code))
          .filter(candidate => candidate && !candidate.optional)
          .sort((a, b) => b.displayIndex - a.displayIndex)[0]?.code || null;
        const explicitSuccessor = (state.outgoing.get(node.code) || [])
          .map(code => state.stageByCode.get(code))
          .filter(candidate => candidate && !candidate.optional)
          .sort((a, b) => a.displayIndex - b.displayIndex)[0]?.code || null;
        const inferredSuccessor = state.nodes.find(candidate =>
          !candidate.optional && candidate.displayIndex > node.displayIndex)?.code || null;
        return {
          code: node.code,
          source: sourceCode,
          successor: explicitSuccessor || inferredSuccessor
        };
      })
      .filter(detour => detour.source);

    const visualEdges = [...structuralEdges];
    state.optionalDetours.forEach(detour => {
      if (detour.successor && !visualEdges.some(edge => edge.source === detour.source && edge.target === detour.successor)) {
        visualEdges.push({
          source: detour.source,
          target: detour.successor,
          kind: 'bypass',
          conditional: false,
          synthetic: true
        });
      }

      visualEdges.push({
        source: detour.source,
        target: detour.code,
        kind: 'conditional-entry',
        conditional: true,
        synthetic: true
      });

      // A conditional stage in the middle of the workflow rejoins the main route.
      // A terminal conditional stage (currently ToT) is a single optional continuation
      // from the last mandatory stage and therefore has no synthetic return path.
      if (detour.successor) {
        visualEdges.push({
          source: detour.code,
          target: detour.successor,
          kind: 'conditional-return',
          conditional: true,
          synthetic: true
        });
      }
    });

    state.visualEdges = visualEdges;
    state.structuralIncoming = new Map(state.nodes.filter(node => !node.optional).map(node => [node.code, []]));
    state.structuralOutgoing = new Map(state.nodes.filter(node => !node.optional).map(node => [node.code, []]));
    structuralEdges.forEach(edge => {
      state.structuralOutgoing.get(edge.source)?.push(edge.target);
      state.structuralIncoming.get(edge.target)?.push(edge.source);
    });
  }

  function branchClusterFor(code) {
    return state.branchClusters.find(cluster => cluster.codes.has(code)) || null;
  }

  function entityForCode(code) {
    return state.stageByCode.get(code);
  }

  function calculateLayout() {
    const mandatoryNodes = state.nodes.filter(node => !node.optional);
    const depths = new Map();
    const unresolved = new Set(mandatoryNodes.map(node => node.code));
    let guard = mandatoryNodes.length * 4;

    while (unresolved.size && guard-- > 0) {
      let progressed = false;
      [...unresolved].forEach(code => {
        const prerequisites = state.structuralIncoming.get(code) || [];
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
    const xGap = 190;
    const startX = 175;
    const centerY = 450;
    const groups = new Map();

    mandatoryNodes.forEach(node => {
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
        else if (count === 2) node.y = index === 0 ? 215 : 685;
        else {
          const spread = 560;
          node.y = centerY - (spread / 2) + ((spread / Math.max(1, count - 1)) * index);
        }
      });
    });

    state.optionalDetours.forEach((detour, index) => {
      const node = state.stageByCode.get(detour.code);
      const source = state.stageByCode.get(detour.source);
      const successor = detour.successor ? state.stageByCode.get(detour.successor) : null;
      if (!node || !source) return;
      node.depth = source.depth + .5;
      node.x = successor ? (source.x + successor.x) / 2 : source.x + (xGap * 1.25);
      node.y = centerY + 215 + (index * 18);
    });

    const maxEntityX = Math.max(...state.nodes.map(node => Number(node.x) || 0));
    state.worldWidth = maxEntityX + 190;
    state.worldHeight = 900;
    world.style.width = `${state.worldWidth}px`;
    world.style.height = `${state.worldHeight}px`;
    svg.setAttribute('viewBox', `0 0 ${state.worldWidth} ${state.worldHeight}`);
    svg.setAttribute('width', String(state.worldWidth));
    svg.setAttribute('height', String(state.worldHeight));
  }

  function pathForEdge(source, target, edge) {
    const sourceHalf = 82;
    const targetHalf = 82;
    const startX = source.x + sourceHalf;
    const endX = target.x - targetHalf;
    const startY = source.y;
    const endY = target.y;
    const distance = Math.max(48, endX - startX);
    const verticalDistance = Math.abs(endY - startY);
    const curve = Math.min(130, Math.max(45, distance * .42));

    if (edge?.kind === 'conditional-entry' || edge?.kind === 'conditional-return') {
      const direction = endY > startY ? 1 : -1;
      const verticalControl = Math.min(120, Math.max(64, verticalDistance * .55));
      return `M ${startX} ${startY} C ${startX + curve * .58} ${startY}, ${endX - curve * .35} ${endY - direction * verticalControl}, ${endX} ${endY}`;
    }

    return `M ${startX} ${startY} C ${startX + curve} ${startY}, ${endX - curve} ${endY}, ${endX} ${endY}`;
  }

  function renderWorld() {
    nodeLayer.innerHTML = '';
    svg.innerHTML = `
      <defs>
        <linearGradient id="processPathGradient" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="var(--process-path-start)" />
          <stop offset="100%" stop-color="var(--process-path-end)" />
        </linearGradient>
        <linearGradient id="processActiveGradient" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="var(--process-active-start)" />
          <stop offset="100%" stop-color="var(--process-active-end)" />
        </linearGradient>
        <linearGradient id="processConditionalGradient" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="var(--process-conditional-start)" />
          <stop offset="100%" stop-color="var(--process-conditional-end)" />
        </linearGradient>
        <filter id="processGlow" x="-50%" y="-50%" width="200%" height="200%">
          <feGaussianBlur stdDeviation="4.5" result="blur" />
          <feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
        </filter>
        <marker id="processArrow" viewBox="0 0 10 10" refX="8.5" refY="5" markerWidth="4.2" markerHeight="4.2" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--process-arrow)"></path>
        </marker>
        <marker id="processArrowActive" viewBox="0 0 10 10" refX="8.5" refY="5" markerWidth="4.8" markerHeight="4.8" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--process-arrow-active)"></path>
        </marker>
        <marker id="processArrowConditional" viewBox="0 0 10 10" refX="8.5" refY="5" markerWidth="4.5" markerHeight="4.5" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--process-arrow-conditional)"></path>
        </marker>
      </defs>
      <g class="process-path-grid" aria-hidden="true">
        <line x1="0" y1="450" x2="${state.worldWidth}" y2="450"></line>
      </g>`;

    state.visualEdges.forEach((edge, index) => {
      const source = entityForCode(edge.source);
      const target = entityForCode(edge.target);
      if (!source || !target) return;
      const d = pathForEdge(source, target, edge);
      const edgeId = `${edge.source}-${edge.target}-${edge.kind || 'structural'}`;

      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      path.setAttribute('d', d);
      path.setAttribute('data-edge-id', edgeId);
      path.setAttribute('data-edge-source', edge.source);
      path.setAttribute('data-edge-target', edge.target);
      path.setAttribute('data-edge-kind', edge.kind || 'structural');
      path.setAttribute('marker-end', edge.conditional ? 'url(#processArrowConditional)' : 'url(#processArrow)');
      path.classList.add('process-connection');
      if (edge.conditional) path.classList.add('is-conditional');
      if (edge.kind === 'bypass' || edge.kind === 'terminal-main') path.classList.add('is-bypass');
      if ((state.structuralOutgoing.get(edge.source) || []).length > 1) path.classList.add('is-branch');
      path.style.setProperty('--edge-delay', `${index * 24}ms`);
      svg.appendChild(path);

      const signal = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      signal.setAttribute('d', d);
      signal.setAttribute('data-signal-id', edgeId);
      signal.setAttribute('data-edge-source', edge.source);
      signal.setAttribute('data-edge-target', edge.target);
      signal.setAttribute('data-edge-kind', edge.kind || 'structural');
      signal.classList.add('process-route-signal');
      if (edge.conditional) signal.classList.add('is-conditional');
      svg.appendChild(signal);
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
          <small><span class="process-node__code">${escapeHtml(node.code)}</span>${node.optional ? '<span class="process-node__conditional-copy"> · Conditional</span>' : ''}</small>
        </span>`;
      nodeLayer.appendChild(button);
    });

    renderProgressTrack();
    placeholder.hidden = true;
    world.setAttribute('aria-busy', 'false');
  }

  function renderProgressTrack() {
    progressTrack.innerHTML = '';
    const branchPositions = new Map();
    state.branchClusters.forEach(cluster => {
      cluster.branches.forEach((code, index) => branchPositions.set(code, index));
    });

    state.nodes.forEach(node => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'process-progress__dot';
      button.dataset.progressCode = node.code;
      button.title = `${node.displayIndex}. ${node.name}`;
      button.setAttribute('aria-label', `Open ${node.name}`);
      if (node.optional) button.classList.add('is-conditional');
      if (branchPositions.has(node.code)) {
        button.classList.add('is-parallel', branchPositions.get(node.code) === 0 ? 'is-parallel-up' : 'is-parallel-down');
      }
      button.innerHTML = `<span class="process-progress__code">${escapeHtml(node.code)}</span>`;
      progressTrack.appendChild(button);
    });
    progressTotal.textContent = String(state.nodes.length).padStart(2, '0');
  }

  function setMode() {
    state.mode = 'journey';
    experience.dataset.mode = 'journey';
    if (wheelCue) wheelCue.hidden = false;
    scene.classList.remove('is-map-mode', 'is-panning');
    scene.classList.add('is-journey-mode');
    updateSelection();
    focusActiveStage(false);
  }

  function viewportSize() {
    const rect = worldViewport.getBoundingClientRect();
    return { width: Math.max(1, rect.width), height: Math.max(1, rect.height) };
  }

  function expandJourneyContextForWideViewport(tiers) {
    if (viewportSize().width < 1480) return tiers;

    const visibleCodes = [...tiers.entries()]
      .filter(([, tier]) => tier !== 'hidden')
      .map(([code]) => code);

    visibleCodes.forEach(code => {
      [...(state.structuralIncoming.get(code) || []), ...(state.structuralOutgoing.get(code) || [])]
        .forEach(candidate => {
          if (tiers.get(candidate) === 'hidden') tiers.set(candidate, 'context');
        });
    });

    return tiers;
  }

  function journeyTiersFor(active) {
    const tiers = new Map(state.nodes.map(node => [node.code, 'hidden']));
    if (!active) return tiers;
    tiers.set(active.code, 'active');

    const cluster = branchClusterFor(active.code);
    if (cluster) {
      if (active.code === cluster.source) {
        cluster.branches.forEach(code => tiers.set(code, 'near'));
        tiers.set(cluster.convergence, 'context');
        (state.structuralIncoming.get(cluster.source) || []).forEach(code => tiers.set(code, 'context'));
      } else if (cluster.branches.includes(active.code)) {
        tiers.set(cluster.source, 'near');
        cluster.branches.filter(code => code !== active.code).forEach(code => tiers.set(code, 'near'));
        tiers.set(cluster.convergence, 'near');
      } else if (active.code === cluster.convergence) {
        cluster.branches.forEach(code => tiers.set(code, 'near'));
        tiers.set(cluster.source, 'context');
        (state.structuralOutgoing.get(cluster.convergence) || []).forEach(code => tiers.set(code, 'near'));
        state.optionalDetours
          .filter(detour => detour.source === cluster.convergence)
          .forEach(detour => tiers.set(detour.code, 'context'));
      }
      return expandJourneyContextForWideViewport(tiers);
    }

    const activeDetour = state.optionalDetours.find(detour => detour.code === active.code);
    if (activeDetour) {
      tiers.set(activeDetour.source, 'near');
      if (activeDetour.successor) tiers.set(activeDetour.successor, 'near');
      return expandJourneyContextForWideViewport(tiers);
    }

    const predecessors = state.structuralIncoming.get(active.code) || [];
    const successors = state.structuralOutgoing.get(active.code) || [];
    predecessors.forEach(code => tiers.set(code, 'near'));
    successors.forEach(code => tiers.set(code, 'near'));

    predecessors.forEach(code => {
      const predecessorCluster = branchClusterFor(code);
      if (predecessorCluster?.convergence === code) return;
      (state.structuralIncoming.get(code) || []).forEach(next => {
        if (tiers.get(next) === 'hidden') tiers.set(next, 'context');
      });
    });
    successors.forEach(code => {
      const successorCluster = branchClusterFor(code);
      if (successorCluster?.source === code) return;
      (state.structuralOutgoing.get(code) || []).forEach(next => {
        if (tiers.get(next) === 'hidden') tiers.set(next, 'context');
      });
    });

    state.optionalDetours
      .filter(detour => detour.source === active.code)
      .forEach(detour => tiers.set(detour.code, 'context'));

    return expandJourneyContextForWideViewport(tiers);
  }


  function focusActiveStage(animate = true) {
    const node = state.nodes[state.activeIndex];
    if (!node) return;

    const viewport = viewportSize();
    const tiers = journeyTiersFor(node);
    const visible = state.nodes.filter(candidate => tiers.get(candidate.code) !== 'hidden');

    const minX = Math.min(...visible.map(candidate => candidate.x)) - 125;
    const maxX = Math.max(...visible.map(candidate => candidate.x)) + 125;
    const minY = Math.min(...visible.map(candidate => candidate.y)) - 120;
    const maxY = Math.max(...visible.map(candidate => candidate.y)) + 120;
    const rangeX = Math.max(380, maxX - minX);
    const rangeY = Math.max(320, maxY - minY);
    let scale = Math.min(
      (viewport.width * .84) / rangeX,
      (viewport.height * .76) / rangeY
    );
    scale = clamp(scale, .78, 1.22);

    const boundsCenterX = (minX + maxX) / 2;
    const boundsCenterY = (minY + maxY) / 2;
    const focusX = (boundsCenterX * .44) + (node.x * .56);
    const focusY = (boundsCenterY * .52) + (node.y * .48);
    const visualCenterX = viewport.width * .5;
    const visualCenterY = viewport.height * .50;
    state.worldScale = scale;
    state.worldX = visualCenterX - (focusX * scale);
    state.worldY = visualCenterY - (focusY * scale);
    applyWorldTransform(animate);
  }

  function applyWorldTransform(animate = true) {
    world.classList.toggle('is-moving', animate && !prefersReducedMotion());
    world.style.transform = `translate3d(${state.worldX}px, ${state.worldY}px, 0) scale(${state.worldScale})`;
    if (animate) window.setTimeout(() => world.classList.remove('is-moving'), 780);
  }

  function nodeScaleForTier(tier) {
    if (tier === 'active') return 1.38;
    if (tier === 'near') return 1;
    if (tier === 'context') return .76;
    return .52;
  }
  async function selectStage(code, { updateHash = true, animate = true } = {}) {
    const node = state.stageByCode.get(String(code || '').toUpperCase());
    if (!node) return;

    const previousIndex = state.activeIndex;
    const nextIndex = node.displayIndex - 1;
    const changed = state.selectedCode !== node.code;
    state.activeIndex = nextIndex;
    state.selectedCode = node.code;
    state.checklistManageMode = false;

    if (changed && animate && !prefersReducedMotion()) {
      window.clearTimeout(state.transitionTimer);
      window.clearTimeout(state.guideTimer);
      scene.classList.remove('is-moving-forward', 'is-moving-backward');
      scene.classList.add(nextIndex >= previousIndex ? 'is-moving-forward' : 'is-moving-backward', 'is-stage-transitioning');
      stageGuide?.classList.add('is-updating');
      state.transitionTimer = window.setTimeout(() => {
        scene.classList.remove('is-stage-transitioning', 'is-moving-forward', 'is-moving-backward');
      }, 780);
      state.guideTimer = window.setTimeout(() => stageGuide?.classList.remove('is-updating'), 210);
    } else {
      stageGuide?.classList.remove('is-updating');
    }

    updateSelection();
    renderStageHeader(node);
    if (state.mode === 'journey') focusActiveStage(animate);
    if (updateHash) history.replaceState(null, '', `#stage-${node.code.toLowerCase()}`);
    await loadChecklist(node.code);
  }

  function updateSelection() {
    const active = state.nodes[state.activeIndex];
    if (!active) return;

    const activeCode = active.code;
    const activeDepth = active.depth ?? -1;
    const tiers = journeyTiersFor(active);
    scene.dataset.activeCode = activeCode.toLowerCase();

    root.querySelectorAll('.process-node[data-stage-code]').forEach(button => {
      const node = state.stageByCode.get(button.dataset.stageCode);
      const selected = node?.code === activeCode;
      const past = node && (node.depth < activeDepth || (node.depth === activeDepth && node.displayIndex < active.displayIndex));
      const tier = tiers.get(node?.code) || 'hidden';

      button.classList.toggle('is-active', selected);
      button.classList.toggle('is-past', Boolean(past));
      button.classList.toggle('is-near', tier === 'near');
      button.classList.toggle('is-context', tier === 'context');
      button.classList.toggle('is-distant', tier === 'hidden');
      button.dataset.journeyTier = tier;
      button.setAttribute('aria-pressed', selected ? 'true' : 'false');
      button.setAttribute('aria-hidden', state.mode === 'journey' && tier === 'hidden' ? 'true' : 'false');
      button.tabIndex = state.mode === 'journey' && tier === 'hidden' ? -1 : 0;

      button.style.setProperty('--node-scale', String(nodeScaleForTier(tier)));
      button.style.setProperty('--node-opacity', tier === 'active' ? '1' : tier === 'near' ? '.9' : tier === 'context' ? '.46' : '0');
      button.style.setProperty('--node-blur', tier === 'context' ? '.45px' : tier === 'hidden' ? '6px' : '0px');
    });

    const activeDetour = state.optionalDetours.find(detour => detour.code === activeCode);
    root.querySelectorAll('.process-connection').forEach(path => {
      const source = path.dataset.edgeSource;
      const target = path.dataset.edgeTarget;
      const kind = path.dataset.edgeKind;
      const sourceTier = tiers.get(source) || 'hidden';
      const targetTier = tiers.get(target) || 'hidden';
      const storyVisible = sourceTier !== 'hidden' && targetTier !== 'hidden';
      const conditional = path.classList.contains('is-conditional');
      const connected = source === activeCode || target === activeCode;
      const detourActive = Boolean(activeDetour && conditional && (source === activeCode || target === activeCode));
      const bypassActive = !activeDetour && !conditional && connected;
      const activePath = detourActive || bypassActive;
      const targetNode = state.stageByCode.get(target);
      const traversed = !conditional && targetNode && targetNode.displayIndex <= active.displayIndex;

      path.classList.toggle('is-active', activePath);
      path.classList.toggle('is-traversed', Boolean(traversed));
      path.classList.toggle('is-story-hidden', !storyVisible);
      path.classList.toggle('is-story-context', storyVisible && !activePath);
      path.setAttribute('marker-end', conditional
        ? 'url(#processArrowConditional)'
        : activePath ? 'url(#processArrowActive)' : 'url(#processArrow)');
    });

    root.querySelectorAll('.process-route-signal').forEach(path => {
      const source = path.dataset.edgeSource;
      const target = path.dataset.edgeTarget;
      const conditional = path.classList.contains('is-conditional');
      const sourceTier = tiers.get(source) || 'hidden';
      const targetTier = tiers.get(target) || 'hidden';
      const storyVisible = sourceTier !== 'hidden' && targetTier !== 'hidden';
      const connected = source === activeCode || target === activeCode;
      const activeSignal = Boolean(storyVisible && connected && (conditional ? activeDetour : !activeDetour));
      path.classList.toggle('is-active', activeSignal);
      path.classList.toggle('is-story-hidden', !storyVisible);
    });

    root.querySelectorAll('[data-progress-code]').forEach(dot => {
      const node = state.stageByCode.get(dot.dataset.progressCode);
      dot.classList.toggle('is-active', node?.code === activeCode);
      dot.classList.toggle('is-past', Boolean(node && node.displayIndex < active.displayIndex));
      dot.setAttribute('aria-current', node?.code === activeCode ? 'step' : 'false');
    });

    progressCurrent.textContent = String(active.displayIndex).padStart(2, '0');
    sceneStageLabel.textContent = `${String(active.displayIndex).padStart(2, '0')} · ${active.name}`;

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
    stageGuide?.classList.remove('has-empty-checklist');
    checklistList.classList.remove('is-empty');
    checklistCount.textContent = '0';
    checklistManage.hidden = !state.canEditChecklist;
    checklistList.setAttribute('aria-busy', 'true');
    checklistList.innerHTML = `
      <li class="stage-checklist__state">
        <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
        <span>Loading checklist…</span>
      </li>`;
  }

  function renderChecklistError() {
    destroySortable();
    stageGuide?.classList.remove('has-empty-checklist');
    checklistList.classList.remove('is-empty');
    checklistList.setAttribute('aria-busy', 'false');
    checklistList.innerHTML = `
      <li class="stage-checklist__state stage-checklist__state--error">
        <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
        <span>Unable to load this checklist.</span>
        <button type="button" data-action="retry-checklist">Retry</button>
      </li>`;
  }

  function renderChecklistInline(value) {
    const escaped = escapeHtml(value);
    return escaped.replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>');
  }

  function splitInlineNumberedList(value) {
    const source = String(value || '').trim();
    if (!source || /[\r\n]/.test(source)) return null;

    const markers = [...source.matchAll(/(?:^|\s)(\d{1,2})[.)]\s+/g)];
    if (markers.length < 2) return null;

    const firstMarker = markers[0];
    const heading = source.slice(0, firstMarker.index).trim().replace(/[:\-–—]\s*$/, '');
    const items = markers.map((match, index) => {
      const start = match.index + match[0].length;
      const end = index + 1 < markers.length ? markers[index + 1].index : source.length;
      return source.slice(start, end).trim();
    }).filter(Boolean);

    return items.length >= 2 ? { heading, items } : null;
  }

  function renderChecklistText(value) {
    const source = String(value || '').replace(/\r\n?/g, '\n').trim();
    if (!source) return '';

    const inlineList = splitInlineNumberedList(source);
    if (inlineList) {
      return `
        <div class="stage-checklist__content">
          ${inlineList.heading ? `<div class="stage-checklist__title">${renderChecklistInline(inlineList.heading)}</div>` : ''}
          <ol class="stage-checklist__sublist">
            ${inlineList.items.map(item => `<li>${renderChecklistInline(item)}</li>`).join('')}
          </ol>
        </div>`;
    }

    const lines = source.split('\n');
    const hasList = lines.some(line => /^\s*(?:\d{1,2}[.)]|[-*•])\s+/.test(line));
    const blocks = [];
    let listType = null;
    let listItems = [];

    const closeList = () => {
      if (!listType || !listItems.length) return;
      const tag = listType === 'numbered' ? 'ol' : 'ul';
      blocks.push(`<${tag} class="stage-checklist__sublist stage-checklist__sublist--${listType}">${listItems.map(item => `<li>${renderChecklistInline(item)}</li>`).join('')}</${tag}>`);
      listType = null;
      listItems = [];
    };

    lines.forEach((rawLine, index) => {
      const line = rawLine.trim();
      if (!line) {
        closeList();
        return;
      }

      const numbered = line.match(/^\d{1,2}[.)]\s+(.+)$/);
      const bulleted = line.match(/^[-*•]\s+(.+)$/);
      if (numbered || bulleted) {
        const nextType = numbered ? 'numbered' : 'bulleted';
        if (listType && listType !== nextType) closeList();
        listType = nextType;
        listItems.push((numbered || bulleted)[1].trim());
        return;
      }

      closeList();
      const isHeading = (index === 0 && hasList) || /:$/.test(line);
      blocks.push(isHeading
        ? `<div class="stage-checklist__title">${renderChecklistInline(line.replace(/:$/, ''))}</div>`
        : `<p>${renderChecklistInline(line)}</p>`);
    });
    closeList();

    return `<div class="stage-checklist__content">${blocks.join('')}</div>`;
  }

  function renderChecklist(checklist) {
    destroySortable();
    const items = checklist?.items || [];
    const empty = items.length === 0;
    checklistCount.textContent = String(items.length);
    checklistList.setAttribute('aria-busy', 'false');
    checklistList.classList.toggle('is-empty', empty);
    stageGuide?.classList.toggle('has-empty-checklist', empty);
    checklistAdd.hidden = !(state.canEditChecklist && state.checklistManageMode && !empty);
    checklistManage.hidden = !state.canEditChecklist || empty;
    manageLabel.textContent = state.checklistManageMode ? 'Done' : 'Manage';
    checklistManage.classList.toggle('is-active', state.checklistManageMode);

    if (empty) {
      checklistList.innerHTML = `
        <li class="stage-checklist__state stage-checklist__state--empty">
          <span class="stage-checklist__state-icon"><i class="bi bi-list-check" aria-hidden="true"></i></span>
          <strong>No reference checks recorded</strong>
          <span>This stage currently has no processing checklist.</span>
          ${state.canEditChecklist ? `
            <button type="button" class="stage-checklist__first-action" data-action="add-item">
              <i class="bi bi-plus-lg" aria-hidden="true"></i>
              Add first checklist item
            </button>` : ''}
        </li>`;
      return;
    }

    checklistList.innerHTML = items.map((item, index) => `
      <li class="stage-checklist__item${state.checklistManageMode ? ' is-managing' : ''}"
          data-checklist-item-id="${item.id}"
          data-item-row-version="${escapeHtml(item.rowVersion)}">
        <span class="stage-checklist__drag" aria-hidden="true"><i class="bi bi-grip-vertical"></i></span>
        <span class="stage-checklist__number">${String(index + 1).padStart(2, '0')}</span>
        <span class="stage-checklist__text">${renderChecklistText(item.text)}</span>
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

  function applySearch({ forceOpen = false } = {}) {
    const term = stageSearch.value.trim().toLowerCase();
    searchClear.hidden = !term;
    const shouldOpen = forceOpen || Boolean(term) || document.activeElement === stageSearch;
    if (!shouldOpen) {
      searchResults.hidden = true;
      searchResults.innerHTML = '';
      return;
    }

    const matches = (term
      ? state.nodes.filter(node => node.searchText.includes(term))
      : state.nodes).slice(0, 15);
    searchResults.innerHTML = matches.length
      ? matches.map(node => `
          <button type="button" data-search-stage="${node.code}"${node.code === state.selectedCode ? ' class="is-current"' : ''}>
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
      setText('[data-stage-count]', String(state.nodes.length));
      const initialCode = initialCodeFromHash();
      const initial = initialCode && state.stageByCode.has(initialCode) ? initialCode : state.nodes[0]?.code;
      if (initial) await selectStage(initial, { updateHash: Boolean(initialCode), animate: false });
      setMode();
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
      closeStageSearch();
      scene.focus({ preventScroll: true });
      return;
    }


    const actionButton = event.target.closest('[data-action]');
    if (!actionButton) return;
    const action = actionButton.dataset.action;
    if (action === 'begin-journey') {
      closeIntroduction();
      setMode();
      selectStage(state.nodes[0]?.code, { animate: false });
      scene.focus({ preventScroll: true });
    } else if (action === 'begin-fullscreen') {
      closeIntroduction();
      setMode();
      selectStage(state.nodes[0]?.code, { animate: false });
      experience.requestFullscreen?.();
    } else if (action === 'show-introduction') {
      showIntroduction();
    } else if (action === 'close-introduction') {
      closeIntroduction();
    } else if (action === 'open-stage-search') {
      showStageSearch();
    } else if (action === 'close-stage-search') {
      closeStageSearch();
    } else if (action === 'toggle-theme') {
      applyTheme(state.theme === 'dark' ? 'light' : 'dark');
    } else if (action === 'previous-stage') goRelative(-1);
    else if (action === 'next-stage') goRelative(1);
    else if (action === 'toggle-fullscreen') {
      if (!document.fullscreenElement) experience.requestFullscreen?.();
      else document.exitFullscreen?.();
    } else if (action === 'print-process') window.print();
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

  stageSearch.addEventListener('input', () => applySearch({ forceOpen: true }));
  stageSearch.addEventListener('focus', () => applySearch({ forceOpen: true }));
  stageSearch.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      const match = state.nodes.find(node => node.searchText.includes(stageSearch.value.trim().toLowerCase()));
      if (match) {
        selectStage(match.code);
        closeStageSearch();
        scene.focus({ preventScroll: true });
      }
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
    if (stageSearchDialog?.open && !event.target.closest('.process-search-wrap')) searchResults.hidden = true;
  });
  stageSearchDialog?.addEventListener('click', event => {
    if (event.target === stageSearchDialog) closeStageSearch();
  });
  introduction?.addEventListener('click', event => {
    if (event.target === introduction) closeIntroduction();
  });
  scene.addEventListener('wheel', handleSceneWheel, { passive: false });

  purposeForm?.addEventListener('submit', submitPurpose);
  purposeText?.addEventListener('input', updatePurposeCharacterCount);
  itemForm?.addEventListener('submit', submitItem);
  itemText?.addEventListener('input', updateItemCharacterCount);
  deleteForm?.addEventListener('submit', submitDelete);

  document.addEventListener('keydown', event => {
    if (event.target?.matches?.('input, textarea, select, button') || event.target?.isContentEditable) return;
    if (event.key === '/') { event.preventDefault(); showStageSearch(); }
    else if (event.key === 'ArrowLeft') { event.preventDefault(); goRelative(-1); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); goRelative(1); }
    else if (event.key === 'Escape' && document.fullscreenElement) document.exitFullscreen?.();
  });

  function syncWorkspaceHeight() {
    if (!workspace || document.fullscreenElement === experience) return;
    const top = workspace.getBoundingClientRect().top;
    const footerReserve = 10;
    const available = Math.max(560, window.innerHeight - top - footerReserve);
    workspace.style.setProperty('--process-available-height', `${available}px`);
  }

  let cameraRefreshFrame = 0;

  function refreshCameraForAvailableSpace() {
    cameraRefreshFrame = 0;
    if (!state.nodes.length) return;
    focusActiveStage(false);
    applyWorldTransform(false);
  }

  function scheduleCameraRefresh() {
    syncWorkspaceHeight();
    if (cameraRefreshFrame) return;
    cameraRefreshFrame = window.requestAnimationFrame(refreshCameraForAvailableSpace);
  }

  window.addEventListener('resize', scheduleCameraRefresh);

  const workspaceResizeObserver = typeof ResizeObserver === 'function' && workspace
    ? new ResizeObserver(scheduleCameraRefresh)
    : null;
  workspaceResizeObserver?.observe(workspace);

  document.addEventListener('fullscreenchange', () => {
    const active = document.fullscreenElement === experience;
    experience.classList.toggle('is-fullscreen', active);
    if (fullscreenExit) fullscreenExit.hidden = !active;
    if (!active) syncWorkspaceHeight();
    window.setTimeout(() => {
      focusActiveStage(false);
      applyWorldTransform(false);
    }, 80);
  });

  loadPreferredTheme();
  syncWorkspaceHeight();
  loadFlow();
}
