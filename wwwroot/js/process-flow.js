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
    checklistPrefetch: null,
    checklistManageMode: false,
    sortable: null,
    walking: false,
    vertical: false,
    rowHeight: 50,
    searchMatches: [],
    searchActive: -1,
    modalReturnFocus: null,
    lastPointerInGuide: false,
    authenticationRecoveryStarted: false
  };

  // Map geometry. The SVG uses a 1000 x 300 viewBox stretched to the map box, and
  // stations are placed in percentages of the same box, so CSS can change the map height.
  const MAP_HEIGHT = 300;
  const MAIN_Y = 120;
  const LANE_SPACING = 56;
  // PRISM's sticky header is 52px + 46px (navigation-shell.css); the route strip sits under it.
  const SHELL_HEADER_HEIGHT = 98;
  const STRIP_HEIGHT = 66;
  const REVEAL_SESSION_KEY = 'prism.process.revealed';
  // Wide screens (1600px and up) turn the route into a vertical line in a fixed left column,
  // with the stage guide beside it. Geometry is in pixels of a 340px-wide map.
  const wideQuery = window.matchMedia('(min-width: 1600px)');
  const VERTICAL_WIDTH = 340;
  const VERTICAL_MAIN_X = 160;
  const VERTICAL_LANE_SPACING = 44;

  const map = root.querySelector('[data-route-map]');
  const board = root.querySelector('[data-route-board]');
  const svg = root.querySelector('[data-route-svg]');
  const placeholder = root.querySelector('[data-route-placeholder]');
  const routeSummary = root.querySelector('[data-route-summary]');
  const announcer = root.querySelector('[data-route-announcer]');
  const stageSearch = root.querySelector('[data-stage-search]');
  const searchResults = root.querySelector('[data-search-results]');
  const strip = root.querySelector('[data-route-strip]');
  const stripDots = root.querySelector('[data-route-strip-dots]');
  const walkPanel = root.querySelector('[data-walk-panel]');
  const routeList = root.querySelector('[data-route-list]');
  const routeListPanel = root.querySelector('[data-route-list-panel]');
  const scroller = root.querySelector('[data-route-scroll]');
  const checklistList = root.querySelector('[data-checklist-list]');
  const checklistCount = root.querySelector('[data-checklist-count]');
  const stagePicker = root.querySelector('[data-stage-picker]');
  const checklistCopy = root.querySelector('[data-checklist-copy]');
  const checklistManage = root.querySelector('[data-checklist-manage]');
  const checklistAdd = root.querySelector('[data-checklist-add]');
  const manageLabel = root.querySelector('[data-manage-label]');
  const purposeEdit = root.querySelector('[data-purpose-edit]');
  const stageGuide = root.querySelector('[data-stage-guide]');
  const stageTitle = root.querySelector('[data-stage-title]');

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
  // Route map, strip, picker and phone list
  // ---------------------------------------------------------------------------
  // Horizontal: x is a share of a 1000-unit width and y is in a 300-unit height.
  // Vertical: x and y are pixels of the VERTICAL_WIDTH-wide map; each column becomes a row.
  function stationX(node) {
    // A detour sits on the same side lane as the second parallel branch; it has its own row.
    if (state.vertical) return VERTICAL_MAIN_X + (clamp(node.lane, -1, 1) * VERTICAL_LANE_SPACING);
    return ((node.column + 0.5) / state.route.columns) * 1000;
  }

  function stationY(node) {
    if (state.vertical) return (node.column + 0.5) * state.rowHeight;
    return MAIN_Y + (node.lane * LANE_SPACING);
  }

  function pathFor(source, target) {
    const x1 = stationX(source);
    const y1 = stationY(source);
    const x2 = stationX(target);
    const y2 = stationY(target);
    if (state.vertical) {
      if (x1 === x2) return `M ${x1} ${y1} L ${x2} ${y2}`;
      const midY = (y1 + y2) / 2;
      return `M ${x1} ${y1} C ${x1} ${midY}, ${x2} ${midY}, ${x2} ${y2}`;
    }
    if (y1 === y2) return `M ${x1} ${y1} L ${x2} ${y2}`;
    const mid = (x1 + x2) / 2;
    return `M ${x1} ${y1} C ${mid} ${y1}, ${mid} ${y2}, ${x2} ${y2}`;
  }

  // Fit the vertical route, with its legend, into the window from where the board starts on
  // first load, within readable limits; taller routes scroll inside the column.
  function boardRestTop() {
    return Math.max(board.getBoundingClientRect().top + window.scrollY, SHELL_HEADER_HEIGHT + 16);
  }

  function verticalRowHeight() {
    // 146px covers the column's padding, the legend with its key hint, and the bottom margin.
    const available = window.innerHeight - boardRestTop() - 146;
    return clamp(Math.floor(available / state.route.columns), 34, 58);
  }

  function svgElement(name, attributes) {
    const element = document.createElementNS('http://www.w3.org/2000/svg', name);
    Object.entries(attributes).forEach(([key, value]) => element.setAttribute(key, value));
    return element;
  }

  function parallelGroups() {
    const seen = new Set();
    return state.route.nodes
      .filter(node => node.parallelWith.length)
      .map(node => [node, ...node.parallelWith.map(code => state.route.byCode.get(code))])
      .filter(group => {
        const key = group.map(node => node.code).sort().join('|');
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
      });
  }

  function joinCodes(nodes) {
    const codes = nodes.map(node => node.code);
    return codes.length <= 1 ? codes.join('') : `${codes.slice(0, -1).join(', ')} and ${codes[codes.length - 1]}`;
  }

  function renderRoute() {
    const { route } = state;
    state.vertical = wideQuery.matches;
    map.dataset.orientation = state.vertical ? 'vertical' : 'horizontal';
    root.classList.toggle('is-route-vertical', state.vertical);
    if (state.vertical) {
      state.rowHeight = verticalRowHeight();
      const height = state.rowHeight * route.columns;
      map.style.height = `${height}px`;
      svg.setAttribute('viewBox', `0 0 ${VERTICAL_WIDTH} ${height}`);
      // The board never runs past the bottom of the window; a longer route scrolls inside it.
      board.style.maxHeight = `calc(100vh - ${Math.round(boardRestTop()) + 16}px)`;
    } else {
      map.style.height = '';
      board.style.maxHeight = '';
      svg.setAttribute('viewBox', `0 0 1000 ${MAP_HEIGHT}`);
    }
    // Very short rows drop the stage codes so names stay readable.
    map.classList.toggle('is-compact', state.vertical && state.rowHeight < 40);
    svg.innerHTML = '';
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
      if (state.vertical && node.lane < 0) button.classList.add('is-label-left');
      if (state.vertical && node.lane > 0) button.classList.add('is-label-side');
      button.dataset.stageCode = node.code;
      if (state.vertical) {
        button.style.left = `${stationX(node)}px`;
        button.style.top = `${stationY(node)}px`;
      } else {
        button.style.left = `${(stationX(node) / 10).toFixed(4)}%`;
        button.style.top = `${((stationY(node) / MAP_HEIGHT) * 100).toFixed(4)}%`;
      }
      button.style.setProperty('--station-delay', `${60 + (index * 20)}ms`);
      button.tabIndex = -1;
      button.setAttribute('aria-label', `Stage ${node.displayIndex} of ${route.nodes.length}: ${node.name}${node.optional ? ', only when required' : ''}`);
      button.innerHTML = `
        <span class="route-station__dot" aria-hidden="true">
          <span class="route-station__index">${pad(node.displayIndex)}</span>
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
    playRevealOnce();

    const conditional = route.nodes.filter(node => node.optional);
    const parts = [`${route.nodes.length} stages`];
    parallelGroups().forEach(group => parts.push(`${joinCodes(group)} run together`));
    if (conditional.length) parts.push(`${joinCodes(conditional)} only when required`);
    routeSummary.textContent = parts.join(' · ');

    stagePicker.innerHTML = state.order
      .map(node => `<option value="${escapeHtml(node.code)}">${pad(node.displayIndex)} · ${escapeHtml(node.name)}${node.optional ? ' (only when required)' : ''}</option>`)
      .join('');

    routeList.innerHTML = state.order.map(node => `
      <li>
        <button type="button" class="route-list__item${node.optional ? ' is-conditional' : ''}" data-goto-stage="${escapeHtml(node.code)}">
          <span class="route-list__index">${pad(node.displayIndex)}</span>
          <span class="route-list__name">${escapeHtml(node.name)}
            ${node.parallelWith.length ? `<small>Runs alongside ${escapeHtml(node.parallelWith.join(', '))}</small>` : ''}
            ${node.optional ? '<small>Only when required</small>' : ''}
          </span>
          <span class="route-list__code">${escapeHtml(node.code)}</span>
        </button>
      </li>`).join('');

    stripDots.innerHTML = state.order.map(node => `
      <li>
        <button type="button" class="route-strip__dot${node.optional ? ' is-conditional' : ''}" data-goto-stage="${escapeHtml(node.code)}"
                tabindex="-1" title="${pad(node.displayIndex)} · ${escapeHtml(node.name)}" aria-label="${escapeHtml(node.name)}">
          <span aria-hidden="true">${escapeHtml(node.code)}</span>
        </button>
      </li>`).join('');

    updateScrollFades();
  }

  // The reveal plays once per browser session and is then switched off, so a resize or
  // re-layout can never replay it and leave the map half drawn.
  function playRevealOnce() {
    let seen = false;
    try { seen = window.sessionStorage.getItem(REVEAL_SESSION_KEY) === '1'; } catch { /* storage is optional */ }
    if (seen || prefersReducedMotion()) {
      map.classList.add('is-ready', 'is-revealed');
      return;
    }
    map.classList.add('is-ready');
    const revealMs = 600 + 60 + (state.route.nodes.length * 20) + 350;
    window.setTimeout(() => map.classList.add('is-revealed'), revealMs);
    try { window.sessionStorage.setItem(REVEAL_SESSION_KEY, '1'); } catch { /* storage is optional */ }
  }

  function updateRouteState() {
    const active = state.selectedCode;
    map.querySelectorAll('.route-station').forEach(button => {
      const stationState = Route.stationState(state.route, button.dataset.stageCode, active);
      button.classList.toggle('is-active', stationState === 'active');
      button.classList.toggle('is-adjacent', stationState === 'adjacent');
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
      path.classList.toggle('is-connected', linkState === 'connected');
      path.classList.toggle('is-lit', linkState === 'lit');
    });

    stagePicker.value = active;
    root.querySelectorAll('[data-route-list] [data-goto-stage], [data-route-strip-dots] [data-goto-stage]').forEach(button => {
      const current = button.dataset.gotoStage === active;
      button.classList.toggle('is-active', current);
      if (current) button.setAttribute('aria-current', 'step');
      else button.removeAttribute('aria-current');
    });
    const node = state.route.byCode.get(active);
    setText('[data-route-strip-current]', node ? `${pad(node.displayIndex)} · ${node.name}` : '');
  }

  function scrollStationIntoView(code) {
    const button = map.querySelector(`.route-station[data-stage-code="${code}"]`);
    if (!button) return;
    const behavior = prefersReducedMotion() ? 'auto' : 'smooth';
    if (state.vertical) {
      // Scroll only the route column; the page itself must not move.
      if (scroller.scrollHeight <= scroller.clientHeight) return;
      const top = button.offsetTop - (scroller.clientHeight / 2);
      scroller.scrollTo({ top: Math.max(0, top), behavior });
      return;
    }
    if (scroller.scrollWidth <= scroller.clientWidth) return;
    const target = button.offsetLeft - (scroller.clientWidth / 2);
    scroller.scrollTo({ left: Math.max(0, target), behavior });
  }

  function updateScrollFades() {
    const scrollableY = state.vertical && scroller.scrollHeight > scroller.clientHeight + 1;
    scroller.classList.toggle('is-scrollable-y', scrollableY);
    scroller.classList.toggle('is-at-top', scroller.scrollTop <= 2);
    scroller.classList.toggle('is-at-bottom', scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2);
    const scrollable = !state.vertical && scroller.scrollWidth > scroller.clientWidth + 1;
    scroller.classList.toggle('is-scrollable', scrollable);
    scroller.classList.toggle('is-at-start', scroller.scrollLeft <= 2);
    scroller.classList.toggle('is-at-end', scroller.scrollLeft + scroller.clientWidth >= scroller.scrollWidth - 2);
  }

  function setStripVisible(requested) {
    // The vertical route never scrolls away, so it needs no strip.
    const visible = requested && !state.vertical;
    // While showing, the strip is pinned under the PRISM header at the page's width.
    if (visible) {
      const rect = root.getBoundingClientRect();
      strip.style.setProperty('--strip-left', `${rect.left}px`);
      strip.style.setProperty('--strip-width', `${rect.width}px`);
    }
    root.classList.toggle('has-strip', visible);
    strip.setAttribute('aria-hidden', visible ? 'false' : 'true');
    strip.querySelectorAll('button').forEach(button => { button.tabIndex = visible ? 0 : -1; });
  }

  // When a selection is made away from the guide (on the map, in search, in the pager),
  // bring the stage title into view if it is not already readable.
  // The strip is always allowed for: it appears as soon as the map scrolls away, so the title
  // must land below the header and the strip even when the strip is not showing yet.
  function revealGuide() {
    const rect = stageTitle.getBoundingClientRect();
    const topLimit = SHELL_HEADER_HEIGHT + (state.vertical ? 0 : STRIP_HEIGHT) + 16;
    if (rect.top >= topLimit && rect.bottom <= window.innerHeight - 160) return;
    const anchor = (state.walking ? walkPanel : stageTitle).getBoundingClientRect().top + window.scrollY;
    window.scrollTo({ top: Math.max(0, anchor - topLimit), behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
  }

  // ---------------------------------------------------------------------------
  // Selection and stage guide
  // ---------------------------------------------------------------------------
  // history: 'push' adds a Back-button entry, 'replace' rewrites the current one, 'none' leaves it.
  async function selectStage(code, { history: historyMode = 'push', focusStation = false, reveal = false } = {}) {
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

    const hash = `#stage-${node.code.toLowerCase()}`;
    if (location.hash.toLowerCase() !== hash && historyMode !== 'none') {
      if (historyMode === 'push') history.pushState({ stage: node.code }, '', hash);
      else history.replaceState({ stage: node.code }, '', hash);
    }

    if (changed) {
      announcer.textContent = `Stage ${node.displayIndex} of ${state.order.length}: ${node.name}`;
      if (!prefersReducedMotion()) {
        stageGuide.classList.remove('is-entering');
        void stageGuide.offsetWidth;
        stageGuide.classList.add('is-entering');
      }
    }
    if (reveal) revealGuide();
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
    setText('[data-stage-total]', `/${state.order.length}`);
    setText('[data-stage-title]', node.name);
    root.querySelector('[data-stage-conditional]').hidden = !node.optional;
    const parallel = root.querySelector('[data-stage-parallel]');
    parallel.hidden = !node.parallelWith.length;
    parallel.innerHTML = node.parallelWith.length
      ? `<i class="bi bi-diagram-2" aria-hidden="true"></i> Runs alongside ${node.parallelWith.map(escapeHtml).join(', ')}`
      : '';
    setText('[data-stage-story]', Route.describe(route, node.code));
    renderNeighbours(node);
    renderPager();
    renderWalkPanel(node);

    purposeEdit.hidden = !state.canEditPurpose;
    checklistManage.hidden = true;
    checklistAdd.hidden = true;
    checklistCopy.hidden = true;
    setText('[data-stage-purpose]', 'Loading stage purpose…');
    setText('[data-purpose-updated]', '');
    renderChecklistLoading();
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
      <div class="stage-neighbours__group">
        <span class="stage-neighbours__label">Before</span>
        ${before.length ? before.map(item => neighbourButton(item, item.optional ? 'Only if it was required' : '')).join('') : '<span class="stage-neighbours__edge">Start of the route</span>'}
      </div>
      <div class="stage-neighbours__group">
        <span class="stage-neighbours__label">Next</span>
        ${after.length ? after.map(item => neighbourButton(item, item.optional ? 'Only when required' : '')).join('') : '<span class="stage-neighbours__edge">End of the route</span>'}
      </div>`;
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
  // Walkthrough: an inline panel at the top of the stage guide, so it never covers content.
  // ---------------------------------------------------------------------------
  function routeSummaryText() {
    const conditional = state.route.nodes.filter(node => node.optional);
    const parts = [`That is the whole route: ${state.order.length} stages from ${state.order[0].name} to ${state.order[state.order.length - 1].name}.`];
    parallelGroups().forEach(group => parts.push(`${listNames(group)} run at the same time.`));
    if (conditional.length) parts.push(`${listNames(conditional)} ${conditional.length === 1 ? 'applies' : 'apply'} only when required.`);
    return parts.join(' ');
  }

  function listNames(nodes) {
    const names = nodes.map(node => node.name);
    return names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
  }

  function renderWalkPanel(node) {
    walkPanel.hidden = !state.walking;
    if (!state.walking) return;
    const last = state.activeIndex === state.order.length - 1;
    const next = state.order[state.activeIndex + 1];
    setText('[data-walk-step]', `Walkthrough · Step ${node.displayIndex} of ${state.order.length}`);
    setText('[data-walk-story]', last ? `${Route.describe(state.route, node.code)} ${routeSummaryText()}` : Route.describe(state.route, node.code));
    walkPanel.querySelector('[data-walk-restart]').hidden = state.activeIndex === 0;
    walkPanel.querySelector('[data-walk-back]').hidden = state.activeIndex === 0;
    walkPanel.querySelector('[data-walk-next]').innerHTML = last
      ? '<i class="bi bi-check2" aria-hidden="true"></i> Finish walkthrough'
      : `Next: ${escapeHtml(next.name)} <i class="bi bi-arrow-right" aria-hidden="true"></i>`;
  }

  // Starts from the stage you are on; "From stage 1" in the panel restarts at the beginning.
  function setWalkthrough(on) {
    state.walking = on;
    root.classList.toggle('is-walking', on);
    setText('[data-walk-label]', on ? 'End walkthrough' : 'Walk me through it');
    const node = state.route.byCode.get(state.selectedCode);
    renderWalkPanel(node);
    if (on) {
      announcer.textContent = `Walkthrough started at stage ${node.displayIndex}: ${node.name}`;
      revealGuide();
      walkPanel.querySelector('[data-walk-next]').focus({ preventScroll: true });
    } else {
      announcer.textContent = 'Walkthrough ended';
    }
  }

  async function walkNext() {
    if (state.activeIndex >= state.order.length - 1) {
      setWalkthrough(false);
      toggleFocus();
      return;
    }
    await selectStage(state.order[state.activeIndex + 1].code, { reveal: true });
    walkPanel.querySelector('[data-walk-next]').focus({ preventScroll: true });
  }

  function toggleFocus() {
    root.querySelector('.route-walk-button')?.focus({ preventScroll: true });
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
      console.error(error);
      if (state.selectedCode === normalizedCode && !error?.authenticationHandled) renderGuidanceError(error);
    }
  }

  // Fills the cache for every stage so search can look inside purposes and checklists.
  function prefetchChecklists() {
    if (state.checklistPrefetch || !state.route) return state.checklistPrefetch;
    const queue = state.order.map(node => node.code).filter(code => !state.checklistCache.has(code));
    const worker = async () => {
      while (queue.length) {
        const code = queue.shift();
        try {
          const checklist = normalizeChecklist(await sendJson(checklistUrl(code)));
          // An edit made while the prefetch was in flight wins over the older copy.
          if (!state.checklistCache.has(code)) state.checklistCache.set(code, checklist);
        } catch {
          // Search falls back to names and codes for stages that could not be read.
        }
      }
    };
    state.checklistPrefetch = Promise.all([worker(), worker(), worker(), worker()]);
    return state.checklistPrefetch;
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
    checklistList.classList.remove('is-empty', 'is-managing');
    checklistList.setAttribute('aria-busy', 'true');
    checklistList.innerHTML = `
      <li class="stage-checklist__state">
        <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
        <span>Loading checklist…</span>
      </li>`;
  }

  function renderGuidanceError(error) {
    destroySortable();
    state.currentChecklist = null;
    const forbidden = error?.status === 403;
    setText('[data-stage-purpose]', forbidden
      ? 'You do not have access to this stage guidance.'
      : 'The guidance for this stage could not be loaded.');
    setText('[data-purpose-updated]', '');
    purposeEdit.hidden = true;
    checklistList.classList.add('is-empty');
    checklistList.classList.remove('is-managing');
    checklistList.setAttribute('aria-busy', 'false');
    checklistList.innerHTML = `
      <li class="stage-checklist__state stage-checklist__state--error" role="alert">
        <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
        <span>${forbidden ? 'Ask an administrator if you need it.' : 'Check your connection and try again.'}</span>
        ${forbidden ? '' : '<button type="button" class="stage-action" data-action="retry-checklist">Try again</button>'}
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
    const managing = state.canEditChecklist && state.checklistManageMode && !empty;
    checklistCount.textContent = empty ? '' : `· ${items.length} ${items.length === 1 ? 'item' : 'items'}`;
    checklistList.setAttribute('aria-busy', 'false');
    checklistList.classList.toggle('is-empty', empty);
    checklistList.classList.toggle('is-managing', managing);
    checklistAdd.hidden = !managing;
    checklistManage.hidden = !state.canEditChecklist || empty;
    checklistCopy.hidden = empty || managing;
    manageLabel.textContent = managing ? 'Done' : 'Manage';
    checklistManage.classList.toggle('is-active', managing);
    checklistManage.setAttribute('aria-pressed', managing ? 'true' : 'false');

    if (empty) {
      checklistList.innerHTML = `
        <li class="stage-checklist__state stage-checklist__state--empty">
          <span class="stage-checklist__empty-icon" aria-hidden="true"><i class="bi bi-list-check"></i></span>
          ${state.canEditChecklist
            ? `<strong>This stage has no checklist yet</strong>
               <span>Readers see only the purpose. Add the checks a project officer should confirm before moving on.</span>
               <button type="button" class="btn btn-primary btn-sm stage-add" data-action="add-item">
                 <i class="bi bi-plus-lg" aria-hidden="true"></i> Add first item
               </button>`
            : `<strong>No checklist has been published for this stage yet</strong>
               <span>Use the purpose above as the current guidance. For more detail, ask the process owner to add a checklist.</span>`}
        </li>`;
      return;
    }

    const last = items.length - 1;
    checklistList.innerHTML = items.map((item, index) => `
      <li class="stage-checklist__item"
          data-checklist-item-id="${item.id}"
          data-item-row-version="${escapeHtml(item.rowVersion)}">
        ${managing ? '<span class="stage-checklist__drag" aria-hidden="true" title="Drag to reorder"><i class="bi bi-grip-vertical"></i></span>' : ''}
        <span class="stage-checklist__number">${pad(index + 1)}</span>
        <div class="stage-checklist__text">${renderChecklistText(item.text)}</div>
        ${managing ? `
        <span class="stage-checklist__item-actions">
          <button type="button" data-action="move-item-up" aria-label="Move item ${index + 1} up"${index === 0 ? ' disabled' : ''}><i class="bi bi-arrow-up" aria-hidden="true"></i></button>
          <button type="button" data-action="move-item-down" aria-label="Move item ${index + 1} down"${index === last ? ' disabled' : ''}><i class="bi bi-arrow-down" aria-hidden="true"></i></button>
          <button type="button" data-action="edit-item" aria-label="Edit checklist item ${index + 1}"><i class="bi bi-pencil" aria-hidden="true"></i></button>
          <button type="button" data-action="delete-item" aria-label="Delete checklist item ${index + 1}"><i class="bi bi-trash3" aria-hidden="true"></i></button>
        </span>` : ''}
      </li>`).join('');

    if (managing && window.Sortable) {
      state.sortable = window.Sortable.create(checklistList, {
        animation: 180,
        handle: '.stage-checklist__drag',
        ghostClass: 'is-dragging',
        chosenClass: 'is-chosen',
        onEnd: () => persistChecklistOrder()
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

  async function persistChecklistOrder({ focusItemId = null, focusAction = null } = {}) {
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
      if (focusItemId) {
        const position = items.findIndex(item => item.itemId === focusItemId) + 1;
        announcer.textContent = `Item moved to position ${position} of ${items.length}.`;
        const row = checklistList.querySelector(`[data-checklist-item-id="${focusItemId}"]`);
        const opposite = focusAction === 'move-item-up' ? 'move-item-down' : 'move-item-up';
        const target = row?.querySelector(`[data-action="${focusAction}"]:not(:disabled)`)
          || row?.querySelector(`[data-action="${opposite}"]:not(:disabled)`)
          || row?.querySelector('[data-action="edit-item"]');
        target?.focus();
      } else {
        showToast('Checklist order updated.', 'success');
      }
    } catch (error) {
      await handleMutationError(error, 'Unable to reorder checklist items.');
    }
  }

  function moveChecklistItem(button, delta) {
    const row = button.closest('[data-checklist-item-id]');
    if (!row) return;
    const sibling = delta < 0 ? row.previousElementSibling : row.nextElementSibling;
    if (!sibling) return;
    if (delta < 0) sibling.before(row);
    else sibling.after(row);
    persistChecklistOrder({
      focusItemId: Number(row.dataset.checklistItemId),
      focusAction: delta < 0 ? 'move-item-up' : 'move-item-down'
    });
  }

  function cacheChecklist(checklist) {
    state.checklistCache.set(checklist.stageCode, checklist);
    if (state.selectedCode === checklist.stageCode) applyChecklist(checklist);
  }

  function toggleChecklistManageMode() {
    if (!state.canEditChecklist || !state.currentChecklist) return;
    state.checklistManageMode = !state.checklistManageMode;
    renderChecklist(state.currentChecklist);
    checklistManage.focus();
  }

  function currentStageName() {
    return state.route?.byCode.get(state.selectedCode)?.name || '';
  }

  function setModalStage(modalElement, prefix) {
    const eyebrow = modalElement?.querySelector('[data-modal-stage]');
    if (eyebrow) eyebrow.textContent = `${prefix} · ${currentStageName()}`.toUpperCase();
  }

  function rememberModalOpener() {
    state.modalReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }

  // Bootstrap returns focus to <body> when the opener has been re-rendered; send it somewhere useful.
  function restoreFocusAfterModal() {
    const target = state.modalReturnFocus;
    state.modalReturnFocus = null;
    if (target?.isConnected && !target.closest('[hidden]')) {
      target.focus();
      return;
    }
    const fallback = [checklistAdd, checklistManage, purposeEdit, stageTitle].find(element => element && !element.hidden);
    fallback?.focus();
  }

  function openPurposeModal() {
    if (!state.canEditPurpose || !state.currentChecklist || !purposeModal || !purposeText) return;
    rememberModalOpener();
    setModalStage(purposeModalElement, 'Stage purpose');
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
    if (purpose === (state.currentChecklist.purpose || '')) {
      purposeModal.hide();
      return;
    }
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
    rememberModalOpener();
    const editing = mode === 'edit' && item;
    setModalStage(itemModalElement, 'Processing checklist');
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
    rememberModalOpener();
    setModalStage(deleteModalElement, 'Processing checklist');
    deleteForm.querySelector('[data-delete-preview]').textContent = item.text;
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
  // Search: names, codes, purposes and checklist text. Focus stays in the input and the
  // highlighted option is announced through aria-activedescendant.
  // ---------------------------------------------------------------------------
  function snippetAround(text, term) {
    const index = text.toLowerCase().indexOf(term);
    if (index < 0) return '';
    // Start and end on word boundaries so the snippet never begins mid-word.
    let start = Math.max(0, index - 20);
    while (start > 0 && /\S/.test(text[start - 1])) start -= 1;
    let end = Math.min(text.length, index + term.length + 40);
    while (end < text.length && /\S/.test(text[end])) end += 1;
    const before = (start > 0 ? '…' : '') + text.slice(start, index);
    const after = text.slice(index + term.length, end) + (end < text.length ? '…' : '');
    return `${escapeHtml(before)}<mark>${escapeHtml(text.slice(index, index + term.length))}</mark>${escapeHtml(after)}`;
  }

  function searchMatches(term) {
    if (!term) return [];
    const matches = [];
    state.order.forEach(node => {
      if (node.searchText.includes(term)) {
        matches.push({ node, rank: node.code.toLowerCase() === term ? 0 : 1, where: '' });
        return;
      }
      const checklist = state.checklistCache.get(node.code);
      if (!checklist) return;
      if (checklist.purpose.toLowerCase().includes(term)) {
        matches.push({ node, rank: 2, where: `Purpose: ${snippetAround(checklist.purpose, term)}` });
        return;
      }
      const item = checklist.items.find(candidate => candidate.text.toLowerCase().includes(term));
      if (item) matches.push({ node, rank: 3, where: `Checklist: ${snippetAround(item.text.replace(/\s+/g, ' '), term)}` });
    });
    return matches.sort((a, b) => a.rank - b.rank || a.node.displayIndex - b.node.displayIndex).slice(0, 8);
  }

  function applySearch() {
    const term = stageSearch.value.trim().toLowerCase();
    state.searchMatches = searchMatches(term);
    state.searchActive = state.searchMatches.length ? 0 : -1;
    const open = Boolean(term);
    searchResults.hidden = !open;
    stageSearch.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (!open) {
      searchResults.innerHTML = '';
      stageSearch.removeAttribute('aria-activedescendant');
      return;
    }
    const pending = state.checklistPrefetch && state.checklistCache.size < state.order.length;
    searchResults.innerHTML = state.searchMatches.length
      ? state.searchMatches.map((match, index) => `
          <div role="option" id="processSearchOption${index}" class="route-search__option" data-goto-stage="${escapeHtml(match.node.code)}" aria-selected="false">
            <span class="route-search__index">${pad(match.node.displayIndex)}</span>
            <span class="route-search__name">${escapeHtml(match.node.name)}${match.where ? `<small>${match.where}</small>` : ''}</span>
            <span class="route-search__code">${escapeHtml(match.node.code)}</span>
          </div>`).join('')
      : `<div class="route-search__empty">${pending ? 'Searching the guidance…' : 'No stage, purpose or checklist item mentions that.'}</div>`;
    highlightSearchOption();
  }

  function highlightSearchOption() {
    const options = [...searchResults.querySelectorAll('[role="option"]')];
    options.forEach((option, index) => {
      const active = index === state.searchActive;
      option.classList.toggle('is-active', active);
      option.setAttribute('aria-selected', active ? 'true' : 'false');
      if (active) option.scrollIntoView({ block: 'nearest' });
    });
    if (state.searchActive >= 0) stageSearch.setAttribute('aria-activedescendant', `processSearchOption${state.searchActive}`);
    else stageSearch.removeAttribute('aria-activedescendant');
  }

  function closeSearch() {
    stageSearch.value = '';
    applySearch();
  }

  function pickSearchMatch(code) {
    closeSearch();
    selectStage(code, { reveal: true });
    stageTitle.focus({ preventScroll: true });
  }

  function initialCodeFromHash() {
    const match = location.hash.match(/^#stage-(.+)$/i);
    return match ? match[1].toUpperCase() : null;
  }

  async function loadFlow() {
    placeholder.hidden = false;
    placeholder.innerHTML = `
      <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
      <span>Loading the procurement route…</span>`;
    map.setAttribute('aria-busy', 'true');
    try {
      const dto = await sendJson(flowUrl());
      state.route = Route.buildRoute(Route.normalizeFlow(dto, state.version));
      state.order = [...state.route.nodes].sort((a, b) => a.displayIndex - b.displayIndex);
      if (!state.order.length) throw new Error('The workflow has no stages.');
      stageGuide.hidden = false;
      root.classList.remove('has-route-error');
      renderRoute();
      const initialCode = initialCodeFromHash();
      const initial = initialCode && state.route.byCode.has(initialCode) ? initialCode : state.order[0].code;
      await selectStage(initial, { history: initialCode ? 'replace' : 'none' });
    } catch (error) {
      console.error(error);
      if (error?.authenticationHandled) return;
      map.setAttribute('aria-busy', 'false');
      stageGuide.hidden = true;
      root.classList.add('has-route-error');
      routeSummary.textContent = 'Route unavailable';
      placeholder.innerHTML = `
        <div class="route-map__error" role="alert">
          <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
          <strong>The procurement route could not be loaded.</strong>
          <span>Check your connection and try again. If it keeps failing, contact the system administrator.</span>
          <button type="button" class="btn btn-primary btn-sm" data-action="retry-flow">Try again</button>
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

  // Same day-month-year form the server uses for "Guidance updated", e.g. 28 Oct 2026.
  function formatDate(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }).format(date);
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
    element.setAttribute('role', variant === 'danger' ? 'alert' : 'status');
    element.innerHTML = '<div class="d-flex"><div class="toast-body"></div><button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button></div>';
    element.querySelector('.toast-body').textContent = message;
    container.appendChild(element);
    const toast = bootstrap.Toast.getOrCreateInstance(element, { delay });
    element.addEventListener('hidden.bs.toast', () => element.remove());
    toast.show();
  }

  // ---------------------------------------------------------------------------
  // Events
  // ---------------------------------------------------------------------------
  root.addEventListener('click', event => {
    const station = event.target.closest('.route-station[data-stage-code]');
    if (station) {
      selectStage(station.dataset.stageCode, { reveal: true });
      return;
    }

    const gotoButton = event.target.closest('[data-goto-stage]');
    if (gotoButton) {
      if (gotoButton.closest('[data-search-results]')) {
        pickSearchMatch(gotoButton.dataset.gotoStage);
        return;
      }
      if (gotoButton.closest('[data-route-list]')) routeListPanel.open = false;
      selectStage(gotoButton.dataset.gotoStage, { reveal: true });
      return;
    }

    const actionButton = event.target.closest('[data-action]');
    if (!actionButton) return;
    const action = actionButton.dataset.action;
    if (action === 'previous-stage') goRelative(-1, { reveal: true });
    else if (action === 'next-stage') goRelative(1, { reveal: true });
    else if (action === 'toggle-walkthrough') setWalkthrough(!state.walking);
    else if (action === 'walk-next') walkNext();
    else if (action === 'walk-restart') selectStage(state.order[0].code, { reveal: true });
    else if (action === 'edit-purpose') openPurposeModal();
    else if (action === 'toggle-checklist-edit') toggleChecklistManageMode();
    else if (action === 'add-item') openItemModal('create');
    else if (action === 'copy-checklist') copyChecklist();
    else if (action === 'move-item-up') moveChecklistItem(actionButton, -1);
    else if (action === 'move-item-down') moveChecklistItem(actionButton, 1);
    else if (action === 'edit-item') {
      const item = findChecklistItem(actionButton);
      if (item) openItemModal('edit', item);
    } else if (action === 'delete-item') {
      const item = findChecklistItem(actionButton);
      if (item) openDeleteModal(item);
    } else if (action === 'retry-checklist' && state.selectedCode) {
      state.checklistCache.delete(state.selectedCode);
      renderChecklistLoading();
      loadChecklist(state.selectedCode, { force: true });
    } else if (action === 'retry-flow') {
      loadFlow();
    }
  });

  stagePicker.addEventListener('change', () => selectStage(stagePicker.value, { reveal: true }));
  scroller.addEventListener('scroll', updateScrollFades, { passive: true });
  // Re-lay the route when the window crosses the wide breakpoint or changes height.
  let relayoutFrame = 0;
  function relayoutRoute() {
    if (!state.route) return;
    window.cancelAnimationFrame(relayoutFrame);
    relayoutFrame = window.requestAnimationFrame(() => {
      const wantVertical = wideQuery.matches;
      if (wantVertical === state.vertical && (!wantVertical || verticalRowHeight() === state.rowHeight)) {
        updateScrollFades();
        return;
      }
      const stationHadFocus = Boolean(document.activeElement?.closest?.('.route-station'));
      renderRoute();
      updateRouteState();
      if (state.vertical) setStripVisible(false);
      scrollStationIntoView(state.selectedCode);
      updateScrollFades();
      if (stationHadFocus) map.querySelector('.route-station.is-active')?.focus({ preventScroll: true });
    });
  }
  window.addEventListener('resize', relayoutRoute);

  map.addEventListener('keydown', event => {
    if (!event.target.closest('.route-station')) return;
    const keys = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };
    if (event.key in keys) {
      event.preventDefault();
      goRelative(keys[event.key], { focusStation: true, history: 'replace' });
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault();
      const target = event.key === 'Home' ? state.order[0] : state.order[state.order.length - 1];
      selectStage(target.code, { focusStation: true, history: 'replace' });
    }
  });

  stageSearch.addEventListener('focus', () => {
    prefetchChecklists()?.then(() => { if (stageSearch.value.trim()) applySearch(); });
  });
  stageSearch.addEventListener('input', applySearch);
  stageSearch.addEventListener('keydown', event => {
    const count = state.searchMatches.length;
    if (event.key === 'ArrowDown' && count) {
      event.preventDefault();
      state.searchActive = (state.searchActive + 1) % count;
      highlightSearchOption();
    } else if (event.key === 'ArrowUp' && count) {
      event.preventDefault();
      state.searchActive = (state.searchActive - 1 + count) % count;
      highlightSearchOption();
    } else if (event.key === 'Enter') {
      const match = state.searchMatches[state.searchActive];
      if (match) {
        event.preventDefault();
        pickSearchMatch(match.node.code);
      }
    } else if (event.key === 'Escape') {
      closeSearch();
    }
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
  [purposeModalElement, itemModalElement, deleteModalElement].forEach(modal => {
    modal?.addEventListener('hidden.bs.modal', restoreFocusAfterModal);
  });

  // Arrow keys move between stages from the map and the route strip, and from the page
  // itself, but not while the reader is working in the stage guide or a form.
  document.addEventListener('pointerdown', event => {
    state.lastPointerInGuide = Boolean(event.target.closest?.('.stage-guide, .process-header'));
  });
  document.addEventListener('keydown', event => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.target?.closest?.('input, textarea, select, [contenteditable="true"], .modal')) return;
    // "/" stays with PRISM's global search; the stage finder is the field in the page header.
    if (event.key === 'Escape' && state.walking) {
      setWalkthrough(false);
      toggleFocus();
      return;
    }
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
    const focusOnPage = event.target === document.body || event.target === document.documentElement;
    const focusInRoute = Boolean(event.target?.closest?.('[data-route-board], [data-route-strip]')) && !event.target.closest('.route-station');
    if ((focusOnPage && !state.lastPointerInGuide) || focusInRoute) {
      event.preventDefault();
      goRelative(event.key === 'ArrowRight' ? 1 : -1, { history: 'replace' });
    }
  });

  // Back and Forward move between the stages you opened.
  window.addEventListener('popstate', () => {
    const code = initialCodeFromHash() || state.order[0]?.code;
    if (code && state.route?.byCode.has(code) && code !== state.selectedCode) selectStage(code, { history: 'none' });
  });
  window.addEventListener('hashchange', () => {
    const code = initialCodeFromHash();
    if (code && state.route?.byCode.has(code) && code !== state.selectedCode) selectStage(code, { history: 'none' });
  });

  if (typeof IntersectionObserver === 'function') {
    // Show the strip once almost none of the map is left on screen (not only when it has
    // gone entirely), so a selection on a short laptop screen never leaves the reader without a route.
    const thresholds = Array.from({ length: 21 }, (_, index) => index / 20);
    new IntersectionObserver(([entry]) => {
      const visible = entry.intersectionRect.height;
      setStripVisible(entry.boundingClientRect.top < SHELL_HEADER_HEIGHT && visible < 72);
    }, { rootMargin: `-${SHELL_HEADER_HEIGHT}px 0px 0px 0px`, threshold: thresholds }).observe(board);
  }

  loadFlow();
}
