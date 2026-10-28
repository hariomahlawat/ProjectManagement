const root = document.querySelector('[data-process-flow-root]');
const Route = window.PrismProcessRoute;

if (root && Route) {
  const state = {
    version: (root.dataset.processVersion || '').trim(),
    canEditChecklist: root.dataset.canEditChecklist === 'true',
    canEditPurpose: root.dataset.canEditPurpose === 'true',
    route: null,
    order: [],
    activeIndex: 0,
    selectedCode: null,
    currentChecklist: null,
    checklistCache: new Map(),
    checklistAbortController: null,
    checklistManageMode: false,
    ticks: new Map(),
    sortable: null,
    walking: false,
    authenticationRecoveryStarted: false
  };

  // Map geometry. The SVG uses a 1000-unit wide viewBox stretched to the map width,
  // so x is a share of the width and y is in pixels of the fixed-height map.
  const MAP_HEIGHT = 364;
  const MAIN_Y = 158;
  const LANE_SPACING = 64;

  const map = root.querySelector('[data-route-map]');
  const svg = root.querySelector('[data-route-svg]');
  const placeholder = root.querySelector('[data-route-placeholder]');
  const routeSummary = root.querySelector('[data-route-summary]');
  const announcer = root.querySelector('[data-route-announcer]');
  const stageSearch = root.querySelector('[data-stage-search]');
  const searchResults = root.querySelector('[data-search-results]');
  const walkbar = root.querySelector('[data-walkbar]');
  const checklistList = root.querySelector('[data-checklist-list]');
  const checklistCount = root.querySelector('[data-checklist-count]');
  const checklistNote = root.querySelector('[data-checklist-note]');
  const checklistCopy = root.querySelector('[data-checklist-copy]');
  const checklistManage = root.querySelector('[data-checklist-manage]');
  const checklistAdd = root.querySelector('[data-checklist-add]');
  const manageLabel = root.querySelector('[data-manage-label]');
  const purposeEdit = root.querySelector('[data-purpose-edit]');
  const stageGuide = root.querySelector('[data-stage-guide]');

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

  // ---------------------------------------------------------------------------
  // HTTP
  // ---------------------------------------------------------------------------
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

  function normalizeChecklist(dto) {
    return {
      id: Number(dto?.id) || 0,
      version: dto?.version || state.version,
      stageCode: String(dto?.stageCode || '').toUpperCase(),
      purpose: String(dto?.purpose || '').trim(),
      purposeUpdatedOn: dto?.purposeUpdatedOn || null,
      updatedOn: dto?.updatedOn || null,
      rowVersion: toBase64(dto?.rowVersion),
      items: (Array.isArray(dto?.items) ? dto.items : [])
        .map(item => ({
          id: Number(item.id),
          text: String(item.text || '').trim(),
          sequence: Number(item.sequence) || 0,
          rowVersion: toBase64(item.rowVersion)
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

  // ---------------------------------------------------------------------------
  // Route map
  // ---------------------------------------------------------------------------
  function stationX(node) {
    return ((node.column + 0.5) / state.route.columns) * 1000;
  }

  function stationY(node) {
    return MAIN_Y + (node.lane * LANE_SPACING);
  }

  function pathFor(source, target) {
    const x1 = stationX(source);
    const y1 = stationY(source);
    const x2 = stationX(target);
    const y2 = stationY(target);
    if (y1 === y2) return `M ${x1} ${y1} L ${x2} ${y2}`;
    const mid = (x1 + x2) / 2;
    return `M ${x1} ${y1} C ${mid} ${y1}, ${mid} ${y2}, ${x2} ${y2}`;
  }

  function svgElement(name, attributes) {
    const element = document.createElementNS('http://www.w3.org/2000/svg', name);
    Object.entries(attributes).forEach(([key, value]) => element.setAttribute(key, value));
    return element;
  }

  function renderRoute() {
    const { route } = state;
    svg.setAttribute('viewBox', `0 0 1000 ${MAP_HEIGHT}`);
    svg.innerHTML = `
      <defs>
        <linearGradient id="routeDoneGradient" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="1000" y2="0">
          <stop offset="0" style="stop-color: var(--route-done-start)" />
          <stop offset="1" style="stop-color: var(--route-done-end)" />
        </linearGradient>
      </defs>`;

    const glowLayer = svgElement('g', { class: 'route-map__glow' });
    const lineLayer = svgElement('g', { class: 'route-map__track' });
    svg.append(glowLayer, lineLayer);

    route.links.forEach(link => {
      const d = pathFor(route.byCode.get(link.source), route.byCode.get(link.target));
      const data = { 'data-link-source': link.source, 'data-link-target': link.target, 'data-link-kind': link.kind };
      const kindClass = link.conditional ? ' is-conditional' : '';
      glowLayer.appendChild(svgElement('path', { d, class: `route-glow${kindClass}`, ...data }));
      lineLayer.appendChild(svgElement('path', { d, class: `route-line${kindClass}`, ...data }));
    });

    map.querySelectorAll('.route-station').forEach(element => element.remove());
    route.nodes.forEach((node, index) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'route-station';
      if (node.optional) button.classList.add('is-conditional');
      if (node.lane < 0) button.classList.add('is-label-above');
      button.dataset.stageCode = node.code;
      button.style.left = `${(stationX(node) / 10).toFixed(4)}%`;
      button.style.top = `${stationY(node)}px`;
      button.style.setProperty('--station-delay', `${120 + (index * 45)}ms`);
      button.tabIndex = -1;
      button.setAttribute('aria-label', `Stage ${node.displayIndex} of ${route.nodes.length}: ${node.name}${node.optional ? ', only when required' : ''}`);
      button.innerHTML = `
        <span class="route-station__dot" aria-hidden="true">
          <span class="route-station__index">${pad(node.displayIndex)}</span>
          <i class="bi bi-check-lg route-station__check"></i>
        </span>
        <span class="route-station__label" aria-hidden="true">
          <span class="route-station__code">${escapeHtml(node.code)}</span>
          <span class="route-station__name">${escapeHtml(node.name)}</span>
        </span>`;
      map.appendChild(button);
    });

    map.style.setProperty('--route-columns', String(route.columns));
    placeholder.hidden = true;
    map.setAttribute('aria-busy', 'false');
    map.classList.add('is-ready');

    const conditional = route.nodes.filter(node => node.optional).length;
    const parallelGroups = new Set(route.nodes
      .filter(node => node.parallelWith.length)
      .map(node => [node.code, ...node.parallelWith].sort().join('|'))).size;
    const parts = [`${route.nodes.length} stages`];
    if (parallelGroups) parts.push(`${parallelGroups} parallel ${parallelGroups === 1 ? 'pair' : 'groups'}`);
    if (conditional) parts.push(`${conditional} only when required`);
    routeSummary.textContent = parts.join(' · ');
  }

  function updateRouteState() {
    const active = state.selectedCode;
    map.querySelectorAll('.route-station').forEach(button => {
      const stationState = Route.stationState(state.route, button.dataset.stageCode, active);
      button.classList.toggle('is-active', stationState === 'active');
      button.classList.toggle('is-done', stationState === 'done');
      button.tabIndex = stationState === 'active' ? 0 : -1;
      if (stationState === 'active') button.setAttribute('aria-current', 'step');
      else button.removeAttribute('aria-current');
    });

    svg.querySelectorAll('[data-link-source]').forEach(path => {
      const link = {
        source: path.dataset.linkSource,
        target: path.dataset.linkTarget,
        kind: path.dataset.linkKind,
        conditional: path.dataset.linkKind.startsWith('conditional')
      };
      const linkState = Route.linkState(state.route, link, active);
      path.classList.toggle('is-done', linkState === 'done');
      path.classList.toggle('is-lit', linkState === 'lit');
    });
  }

  function scrollStationIntoView(code) {
    const button = map.querySelector(`.route-station[data-stage-code="${code}"]`);
    const scroller = root.querySelector('[data-route-scroll]');
    if (!button || !scroller || scroller.scrollWidth <= scroller.clientWidth) return;
    const target = button.offsetLeft - (scroller.clientWidth / 2);
    scroller.scrollTo({ left: Math.max(0, target), behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
  }

  // ---------------------------------------------------------------------------
  // Selection and stage guide
  // ---------------------------------------------------------------------------
  async function selectStage(code, { updateHash = true, focusStation = false } = {}) {
    const node = state.route?.byCode.get(String(code || '').toUpperCase());
    if (!node) return;

    const changed = state.selectedCode !== node.code;
    state.activeIndex = state.order.indexOf(node);
    state.selectedCode = node.code;
    if (changed) state.checklistManageMode = false;

    updateRouteState();
    renderStageHeader(node);
    if (focusStation) map.querySelector(`.route-station[data-stage-code="${node.code}"]`)?.focus({ preventScroll: true });
    scrollStationIntoView(node.code);
    if (updateHash) history.replaceState(null, '', `#stage-${node.code.toLowerCase()}`);
    if (changed) {
      announcer.textContent = `Stage ${node.displayIndex} of ${state.order.length}: ${node.name}`;
      if (!prefersReducedMotion()) {
        stageGuide.classList.remove('is-entering');
        void stageGuide.offsetWidth;
        stageGuide.classList.add('is-entering');
      }
    }
    await loadChecklist(node.code);
  }

  function goRelative(delta, options = {}) {
    const next = state.order[clamp(state.activeIndex + delta, 0, state.order.length - 1)];
    if (next && next.code !== state.selectedCode) selectStage(next.code, options);
  }

  function renderStageHeader(node) {
    const { route } = state;
    setText('[data-stage-number]', pad(node.displayIndex));
    setText('[data-stage-code-label]', node.code);
    setText('[data-stage-title]', node.name);
    setText('[data-stage-position]', `Stage ${node.displayIndex} of ${state.order.length}`);
    root.querySelector('[data-stage-conditional]').hidden = !node.optional;
    const parallel = root.querySelector('[data-stage-parallel]');
    parallel.hidden = !node.parallelWith.length;
    parallel.innerHTML = node.parallelWith.length
      ? `<i class="bi bi-diagram-2" aria-hidden="true"></i> Runs alongside ${node.parallelWith.map(escapeHtml).join(', ')}`
      : '';
    setText('[data-stage-story]', Route.describe(route, node.code));
    renderNeighbours(node);
    renderPager();

    purposeEdit.hidden = !state.canEditPurpose;
    checklistManage.hidden = true;
    checklistAdd.hidden = true;
    checklistCopy.hidden = true;
    setText('[data-stage-purpose]', 'Loading stage purpose…');
    setText('[data-purpose-updated]', '');
    renderChecklistLoading();

    if (state.walking) {
      setText('[data-walk-step]', `Step ${node.displayIndex} of ${state.order.length}`);
      setText('[data-walk-name]', node.name);
      const next = state.order[state.activeIndex + 1];
      const nextButton = walkbar.querySelector('[data-walk-next]');
      nextButton.disabled = !next;
      nextButton.innerHTML = next
        ? `Next: ${escapeHtml(next.name)} <i class="bi bi-arrow-right" aria-hidden="true"></i>`
        : 'End of route';
      walkbar.querySelector('[data-action="previous-stage"]').disabled = state.activeIndex === 0;
    }
  }

  function neighbourButton(node, note = '') {
    return `
      <button type="button" class="stage-neighbour" data-goto-stage="${escapeHtml(node.code)}">
        <span class="stage-neighbour__code">${escapeHtml(node.code)}</span>
        <span class="stage-neighbour__name">${escapeHtml(node.name)}${note ? `<small>${escapeHtml(note)}</small>` : ''}</span>
      </button>`;
  }

  function renderNeighbours(node) {
    const { route } = state;
    const before = route.predecessors(node.code);
    const after = route.successors(node.code);
    const host = root.querySelector('[data-stage-neighbours]');
    host.innerHTML = `
      <span class="stage-neighbours__label">Comes after</span>
      ${before.length ? before.map(item => neighbourButton(item, item.optional ? 'Only if it was required' : '')).join('') : '<span class="stage-neighbours__edge">Start of the route</span>'}
      <span class="stage-neighbours__label">You are here</span>
      <div class="stage-neighbour stage-neighbour--current">
        <span class="stage-neighbour__code">${escapeHtml(node.code)}</span>
        <span class="stage-neighbour__name">${escapeHtml(node.name)}</span>
      </div>
      <span class="stage-neighbours__label">Leads to</span>
      ${after.length ? after.map(item => neighbourButton(item, item.optional ? 'Only when required' : '')).join('') : '<span class="stage-neighbours__edge">End of the route</span>'}`;
  }

  function renderPager() {
    const previous = state.order[state.activeIndex - 1];
    const next = state.order[state.activeIndex + 1];
    const previousButton = root.querySelector('[data-pager-previous]');
    const nextButton = root.querySelector('[data-pager-next]');
    previousButton.hidden = !previous;
    nextButton.hidden = !next;
    setText('[data-pager-previous-name]', previous ? `${pad(previous.displayIndex)} · ${previous.name}` : '');
    setText('[data-pager-next-name]', next ? `${pad(next.displayIndex)} · ${next.name}` : '');
  }

  // ---------------------------------------------------------------------------
  // Checklist
  // ---------------------------------------------------------------------------
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
    setText('[data-stage-purpose]', checklist?.purpose || 'The purpose of this stage has not been recorded yet.');
    const updated = checklist?.purposeUpdatedOn ? formatDate(checklist.purposeUpdatedOn) : '';
    setText('[data-purpose-updated]', updated ? `Purpose updated ${updated}` : '');
  }

  function renderChecklistLoading() {
    destroySortable();
    checklistCount.textContent = '';
    checklistNote.hidden = true;
    checklistList.classList.remove('is-empty', 'is-managing');
    checklistList.setAttribute('aria-busy', 'true');
    checklistList.innerHTML = `
      <li class="stage-checklist__state">
        <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
        <span>Loading checklist…</span>
      </li>`;
  }

  function renderChecklistError() {
    destroySortable();
    checklistList.classList.remove('is-empty', 'is-managing');
    checklistList.setAttribute('aria-busy', 'false');
    checklistList.innerHTML = `
      <li class="stage-checklist__state stage-checklist__state--error">
        <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
        <span>This checklist could not be loaded. Check your connection and try again.</span>
        <button type="button" class="stage-action" data-action="retry-checklist">Retry</button>
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

  function ticksFor(code) {
    if (!state.ticks.has(code)) state.ticks.set(code, new Set());
    return state.ticks.get(code);
  }

  function renderChecklist(checklist) {
    destroySortable();
    const items = checklist?.items || [];
    const empty = items.length === 0;
    const managing = state.canEditChecklist && state.checklistManageMode && !empty;
    checklistCount.textContent = empty ? '' : `· ${items.length} ${items.length === 1 ? 'item' : 'items'}`;
    checklistList.setAttribute('aria-busy', 'false');
    checklistList.classList.toggle('is-empty', empty);
    checklistList.classList.toggle('is-managing', managing);
    checklistAdd.hidden = !managing;
    checklistManage.hidden = !state.canEditChecklist || empty;
    checklistCopy.hidden = empty || managing;
    checklistNote.hidden = empty || managing;
    manageLabel.textContent = managing ? 'Done' : 'Manage';
    checklistManage.classList.toggle('is-active', managing);

    if (empty) {
      checklistList.innerHTML = `
        <li class="stage-checklist__state stage-checklist__state--empty">
          <span class="stage-checklist__empty-icon" aria-hidden="true"><i class="bi bi-list-check"></i></span>
          ${state.canEditChecklist
            ? `<strong>This stage has no checklist yet</strong>
               <span>Readers see only the purpose. Add the checks a project officer should confirm before moving on.</span>
               <button type="button" class="stage-action stage-action--primary" data-action="add-item">
                 <i class="bi bi-plus-lg" aria-hidden="true"></i> Add first item
               </button>`
            : `<strong>No checklist has been published for this stage yet</strong>
               <span>Use the purpose above as the current guidance. For more detail, ask the process owner to add a checklist.</span>`}
        </li>`;
      return;
    }

    const ticks = ticksFor(checklist.stageCode);
    checklistList.innerHTML = items.map((item, index) => {
      const ticked = !managing && ticks.has(item.id);
      return `
      <li class="stage-checklist__item${ticked ? ' is-ticked' : ''}"
          data-checklist-item-id="${item.id}"
          data-item-row-version="${escapeHtml(item.rowVersion)}">
        ${managing
          ? '<span class="stage-checklist__drag" aria-hidden="true"><i class="bi bi-grip-vertical"></i></span>'
          : `<input type="checkbox" class="stage-checklist__tick" id="processTick${item.id}" data-tick-item="${item.id}"${ticked ? ' checked' : ''} />`}
        <label class="stage-checklist__number"${managing ? '' : ` for="processTick${item.id}"`}>${pad(index + 1)}</label>
        <div class="stage-checklist__text">${renderChecklistText(item.text)}</div>
        ${managing ? `
        <span class="stage-checklist__item-actions">
          <button type="button" data-action="edit-item" aria-label="Edit checklist item ${index + 1}"><i class="bi bi-pencil" aria-hidden="true"></i></button>
          <button type="button" data-action="delete-item" aria-label="Delete checklist item ${index + 1}"><i class="bi bi-trash3" aria-hidden="true"></i></button>
        </span>` : ''}
      </li>`;
    }).join('');

    if (managing && window.Sortable) {
      state.sortable = window.Sortable.create(checklistList, {
        animation: 180,
        handle: '.stage-checklist__drag',
        ghostClass: 'is-dragging',
        chosenClass: 'is-chosen',
        onEnd: persistChecklistOrder
      });
    }
  }

  function checklistAsText(checklist) {
    const node = state.route.byCode.get(checklist.stageCode);
    const lines = [`${pad(node.displayIndex)} ${node.code} · ${node.name}`];
    if (checklist.purpose) lines.push('', checklist.purpose);
    lines.push('', 'Processing checklist');
    checklist.items.forEach((item, index) => lines.push(`${index + 1}. ${item.text.replace(/\n+/g, '\n   ')}`));
    return lines.join('\n');
  }

  async function copyChecklist() {
    if (!state.currentChecklist?.items.length) return;
    const text = checklistAsText(state.currentChecklist);
    try {
      await navigator.clipboard.writeText(text);
      showToast('Checklist copied.', 'success', 2200);
    } catch {
      // Clipboard access can be refused on plain-HTTP LAN origins; fall back to a selection copy.
      const area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      const copied = document.execCommand?.('copy');
      area.remove();
      showToast(copied ? 'Checklist copied.' : 'Copy is not available in this browser. Select the text and copy it instead.', copied ? 'success' : 'warning', 2600);
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
      if (!editing) state.checklistManageMode = true;
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

  // ---------------------------------------------------------------------------
  // Search and walkthrough
  // ---------------------------------------------------------------------------
  function searchMatches() {
    const term = stageSearch.value.trim().toLowerCase();
    if (!term) return [];
    return state.order.filter(node => node.searchText.includes(term)).slice(0, 8);
  }

  function applySearch() {
    const term = stageSearch.value.trim();
    const matches = searchMatches();
    const open = Boolean(term);
    searchResults.hidden = !open;
    stageSearch.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (!open) {
      searchResults.innerHTML = '';
      return;
    }
    searchResults.innerHTML = matches.length
      ? matches.map((node, index) => `
          <button type="button" role="option" data-goto-stage="${escapeHtml(node.code)}"${index === 0 ? ' class="is-first" aria-selected="true"' : ' aria-selected="false"'}>
            <span class="route-search__index">${pad(node.displayIndex)}</span>
            <span class="route-search__name">${escapeHtml(node.name)}</span>
            <span class="route-search__code">${escapeHtml(node.code)}</span>
          </button>`).join('')
      : '<div class="route-search__empty">No stage matches that name or code.</div>';
  }

  function closeSearch() {
    stageSearch.value = '';
    applySearch();
  }

  function setWalkthrough(on) {
    state.walking = on;
    walkbar.hidden = !on;
    root.classList.toggle('is-walking', on);
    const toggle = root.querySelector('.route-walk-button');
    toggle.setAttribute('aria-pressed', on ? 'true' : 'false');
    setText('[data-walk-label]', on ? 'End walkthrough' : 'Walk me through it');
    if (on) {
      selectStage(state.order[0].code, { focusStation: true });
    }
  }

  function initialCodeFromHash() {
    const match = location.hash.match(/^#stage-(.+)$/i);
    return match ? match[1].toUpperCase() : null;
  }

  async function loadFlow() {
    try {
      const dto = await sendJson(flowUrl());
      state.route = Route.buildRoute(Route.normalizeFlow(dto, state.version));
      state.order = [...state.route.nodes].sort((a, b) => a.displayIndex - b.displayIndex);
      if (!state.order.length) throw new Error('The workflow has no stages.');
      renderRoute();
      const initialCode = initialCodeFromHash();
      const initial = initialCode && state.route.byCode.has(initialCode) ? initialCode : state.order[0].code;
      await selectStage(initial, { updateHash: Boolean(initialCode) });
    } catch (error) {
      console.error(error);
      map.setAttribute('aria-busy', 'false');
      placeholder.innerHTML = `
        <div class="route-map__error">
          <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
          <strong>The procurement route could not be loaded.</strong>
          <span>Refresh the page. If it still fails, contact the system administrator.</span>
        </div>`;
    }
  }

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------
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

  function pad(value) {
    return String(value).padStart(2, '0');
  }

  function formatDate(value) {
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

  // ---------------------------------------------------------------------------
  // Events
  // ---------------------------------------------------------------------------
  root.addEventListener('click', event => {
    const station = event.target.closest('.route-station[data-stage-code]');
    if (station) {
      selectStage(station.dataset.stageCode);
      return;
    }

    const gotoButton = event.target.closest('[data-goto-stage]');
    if (gotoButton) {
      const fromSearch = Boolean(gotoButton.closest('[data-search-results]'));
      selectStage(gotoButton.dataset.gotoStage, { focusStation: fromSearch });
      if (fromSearch) closeSearch();
      return;
    }

    const actionButton = event.target.closest('[data-action]');
    if (!actionButton) return;
    const action = actionButton.dataset.action;
    if (action === 'previous-stage') goRelative(-1);
    else if (action === 'next-stage') goRelative(1);
    else if (action === 'toggle-walkthrough') setWalkthrough(!state.walking);
    else if (action === 'edit-purpose') openPurposeModal();
    else if (action === 'toggle-checklist-edit') toggleChecklistManageMode();
    else if (action === 'add-item') openItemModal('create');
    else if (action === 'copy-checklist') copyChecklist();
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

  checklistList.addEventListener('change', event => {
    const tick = event.target.closest('[data-tick-item]');
    if (!tick || !state.currentChecklist) return;
    const ticks = ticksFor(state.currentChecklist.stageCode);
    const id = Number(tick.dataset.tickItem);
    if (tick.checked) ticks.add(id);
    else ticks.delete(id);
    tick.closest('.stage-checklist__item')?.classList.toggle('is-ticked', tick.checked);
  });

  map.addEventListener('keydown', event => {
    if (!event.target.closest('.route-station')) return;
    const keys = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };
    if (event.key in keys) {
      event.preventDefault();
      goRelative(keys[event.key], { focusStation: true });
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault();
      const target = event.key === 'Home' ? state.order[0] : state.order[state.order.length - 1];
      selectStage(target.code, { focusStation: true });
    }
  });

  stageSearch.addEventListener('input', applySearch);
  stageSearch.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      const match = searchMatches()[0];
      if (match) {
        event.preventDefault();
        closeSearch();
        selectStage(match.code, { focusStation: true });
      }
    } else if (event.key === 'Escape') {
      closeSearch();
    } else if (event.key === 'ArrowDown') {
      const first = searchResults.querySelector('button');
      if (first) {
        event.preventDefault();
        first.focus();
      }
    }
  });
  searchResults.addEventListener('keydown', event => {
    const buttons = [...searchResults.querySelectorAll('button')];
    const index = buttons.indexOf(document.activeElement);
    if (event.key === 'ArrowDown' && index < buttons.length - 1) { event.preventDefault(); buttons[index + 1].focus(); }
    else if (event.key === 'ArrowUp') { event.preventDefault(); (index > 0 ? buttons[index - 1] : stageSearch).focus(); }
    else if (event.key === 'Escape') { closeSearch(); stageSearch.focus(); }
  });
  document.addEventListener('click', event => {
    if (!event.target.closest('[data-route-search]')) {
      searchResults.hidden = true;
      stageSearch.setAttribute('aria-expanded', 'false');
    }
  });

  purposeForm?.addEventListener('submit', submitPurpose);
  purposeText?.addEventListener('input', updatePurposeCharacterCount);
  itemForm?.addEventListener('submit', submitItem);
  itemText?.addEventListener('input', updateItemCharacterCount);
  deleteForm?.addEventListener('submit', submitDelete);

  document.addEventListener('keydown', event => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.target?.closest?.('input, textarea, select, [contenteditable="true"], .modal')) return;
    if (event.key === '/') {
      event.preventDefault();
      stageSearch.focus();
    } else if (event.key === 'Escape' && state.walking) {
      setWalkthrough(false);
    } else if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight') && !event.target?.closest?.('.route-station, button')) {
      event.preventDefault();
      goRelative(event.key === 'ArrowRight' ? 1 : -1);
    }
  });

  loadFlow();
}
