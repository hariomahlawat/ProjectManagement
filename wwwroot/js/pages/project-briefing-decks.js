const root = document.querySelector('[data-pbd-root]');

if (root) {
  const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  const deckElement = root.querySelector('[data-deck-id]');
  const deckId = Number(deckElement?.dataset.deckId || 0);
  const storagePrefix = `projectBriefingDeck:${deckId}:`;

  class RequestError extends Error {
    constructor(message, status, payload) {
      super(message);
      this.name = 'RequestError';
      this.status = status;
      this.payload = payload;
    }
  }

  const requestJson = async (url, options = {}) => {
    const response = await fetch(url, {
      credentials: 'same-origin',
      ...options,
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': token,
        'X-Requested-With': 'XMLHttpRequest',
        ...(options.headers || {})
      }
    });

    const contentType = response.headers.get('content-type') || '';
    const payload = contentType.includes('application/json') ? await response.json() : null;
    if (!response.ok) {
      throw new RequestError(payload?.message || payload?.title || `Request failed (${response.status}).`, response.status, payload);
    }
    return payload;
  };

  const setState = (element, message, state = '') => {
    if (!element) return;
    element.textContent = message || '';
    element.classList.remove('is-saving', 'is-saved', 'is-error', 'is-success');
    if (state) element.classList.add(`is-${state}`);
  };

  const rowVersionInputs = [...root.querySelectorAll('input[name="RowVersion"], input[name="rowVersion"]')];
  const currentRowVersion = () => rowVersionInputs[0]?.value || '';
  const updateRowVersion = (value) => {
    if (!value) return;
    rowVersionInputs.forEach((input) => { input.value = value; });
  };

  const normalize = (value) => String(value ?? '').trim().toLocaleLowerCase();
  const formatDate = (value) => {
    if (!value) return '';
    const match = String(value).match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (!match) return String(value);
    const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
    return new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }).format(date);
  };

  const settings = root.querySelector('[data-pbd-open-settings="true"]');
  if (settings instanceof HTMLDetailsElement) settings.open = true;

  // Preserve the user's working context for this deck.
  const restoreScroll = () => {
    const saved = Number(sessionStorage.getItem(`${storagePrefix}scroll`) || 0);
    if (saved > 0) window.requestAnimationFrame(() => window.scrollTo({ top: saved, behavior: 'instant' }));
  };
  window.addEventListener('pagehide', () => sessionStorage.setItem(`${storagePrefix}scroll`, String(window.scrollY)));
  restoreScroll();

  // Selection method tabs.
  const tabs = [...root.querySelectorAll('[data-pbd-selector-tab]')];
  const panels = [...root.querySelectorAll('[data-pbd-selector-panel]')];
  const activatePanel = (name, focus = false) => {
    tabs.forEach((tab) => {
      const active = tab.dataset.pbdSelectorTab === name;
      tab.classList.toggle('is-active', active);
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
      if (active && focus) tab.focus();
    });
    panels.forEach((panel) => {
      const active = panel.dataset.pbdSelectorPanel === name;
      panel.hidden = !active;
      panel.classList.toggle('is-active', active);
    });
    sessionStorage.setItem(`${storagePrefix}activeTab`, name);
  };

  const savedTab = sessionStorage.getItem(`${storagePrefix}activeTab`);
  if (savedTab && tabs.some((tab) => tab.dataset.pbdSelectorTab === savedTab)) activatePanel(savedTab);

  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => activatePanel(tab.dataset.pbdSelectorTab || 'quick'));
    tab.addEventListener('keydown', (event) => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      let target = index;
      if (event.key === 'ArrowRight') target = (index + 1) % tabs.length;
      if (event.key === 'ArrowLeft') target = (index - 1 + tabs.length) % tabs.length;
      if (event.key === 'Home') target = 0;
      if (event.key === 'End') target = tabs.length - 1;
      activatePanel(tabs[target].dataset.pbdSelectorTab || 'quick', true);
    });
  });

  const metric = (name) => root.querySelector(`[data-pbd-metric="${name}"]`);
  const selectedTotal = root.querySelector('[data-pbd-selected-total]');
  const slideTotal = root.querySelector('[data-pbd-slide-total]');
  const slideBreakdown = root.querySelector('[data-pbd-slide-breakdown]');
  const readinessGaps = root.querySelector('[data-pbd-readiness-gaps]');
  const generateButton = root.querySelector('[data-pbd-generate]');
  const activeSavedCard = root.querySelector('.pbd-saved-card.is-active small');

  const includesDetailedSlides = () => {
    const value = root.querySelector('input[name="PresentationMode"]:checked')?.value;
    return value === 'DetailedProjects' || value === 'Combined';
  };
  const includesCostRd = () => ['CostRdOnly', 'Both'].includes(root.querySelector('input[name="CostMode"]:checked')?.value || '');
  const includesProliferation = () => ['ProliferationOnly', 'Both'].includes(root.querySelector('input[name="CostMode"]:checked')?.value || '');

  const updateReadinessSummary = (deck) => {
    const readiness = deck?.readiness || {};
    const estimate = deck?.slideEstimate || {};
    const total = Number(readiness.projectCount || 0);
    if (metric('projects')) metric('projects').textContent = String(total);
    if (metric('status')) metric('status').textContent = `${readiness.externalStatusAvailableCount || 0}/${total}`;
    if (metric('cost-rd')) metric('cost-rd').textContent = `${readiness.costRdAvailableCount || 0}/${total}`;
    if (metric('proliferation')) metric('proliferation').textContent = `${readiness.proliferationCostAvailableCount || 0}/${total}`;
    if (metric('photo')) metric('photo').textContent = `${readiness.coverPhotoAvailableCount || 0}/${total}`;
    if (selectedTotal) selectedTotal.textContent = String(total);
    if (generateButton) generateButton.disabled = total === 0;

    if (slideTotal) slideTotal.textContent = `${estimate.totalSlides || 0} ${(estimate.totalSlides || 0) === 1 ? 'slide' : 'slides'}`;
    if (slideBreakdown) {
      const continuationSlides = Number(estimate.capabilityContinuationSlides || 0);
      slideBreakdown.textContent = `Cover and portfolio ${estimate.coverAndPortfolioSlides || 0} · Summary ${estimate.summarySlides || 0} · Tables ${estimate.executiveTableSlides || 0} · Project slides ${estimate.detailedProjectSlides || 0}${continuationSlides > 0 ? ` · Capability continuations ${continuationSlides}` : ''}`;
    }

    if (activeSavedCard) {
      const suffix = total === 1 ? 'project' : 'projects';
      activeSavedCard.textContent = `${total} ${suffix} · updated just now`;
    }

    if (!readinessGaps) return;
    readinessGaps.replaceChildren();
    const addPill = (icon, text, className = '') => {
      const span = document.createElement('span');
      if (className) span.className = className;
      const i = document.createElement('i');
      i.className = `bi ${icon}`;
      i.setAttribute('aria-hidden', 'true');
      span.append(i, document.createTextNode(` ${text}`));
      readinessGaps.append(span);
    };

    if (total === 0) {
      addPill('bi-info-circle', 'Add projects to generate a deck', 'is-neutral');
      return;
    }

    const gaps = [];
    const missingStatus = total - Number(readiness.externalStatusAvailableCount || 0);
    const missingCost = total - Number(readiness.costRdAvailableCount || 0);
    const missingProliferation = total - Number(readiness.proliferationCostAvailableCount || 0);
    const missingPhoto = total - Number(readiness.coverPhotoAvailableCount || 0);
    const missingDescription = total - Number(readiness.descriptionAvailableCount || 0);
    if (missingStatus > 0) gaps.push(['bi-chat-left-text', `${missingStatus} without external status`]);
    if (includesCostRd() && missingCost > 0) gaps.push(['bi-currency-rupee', `${missingCost} without Cost (R&D)`]);
    if (includesProliferation() && missingProliferation > 0) gaps.push(['bi-boxes', `${missingProliferation} without proliferation cost`]);
    if (includesDetailedSlides() && missingPhoto > 0) gaps.push(['bi-image', `${missingPhoto} without PowerPoint-ready photo`]);
    if (includesDetailedSlides() && missingDescription > 0) gaps.push(['bi-card-text', `${missingDescription} without capability overview`]);
    if (gaps.length === 0) addPill('bi-check-circle', 'Selected content is ready', 'is-ready');
    else gaps.forEach(([icon, text]) => addPill(icon, text));
  };

  // Selected-project table management.
  const sortableBody = root.querySelector('[data-pbd-sortable]');
  const selectedTableWrap = root.querySelector('[data-pbd-selected-table-wrap]');
  const emptyProjects = root.querySelector('[data-pbd-empty-projects]');
  const noFilterResults = root.querySelector('[data-pbd-no-filter-results]');
  const selectedSearch = root.querySelector('[data-pbd-selected-search]');
  const selectedStage = root.querySelector('[data-pbd-selected-stage]');
  const selectedReadiness = root.querySelector('[data-pbd-selected-readiness]');
  const visibleCount = root.querySelector('[data-pbd-visible-count]');
  const selectVisible = root.querySelector('[data-pbd-select-visible]');
  const bulkTop = root.querySelector('[data-pbd-bulk-top]');
  const bulkBottom = root.querySelector('[data-pbd-bulk-bottom]');
  const bulkRemove = root.querySelector('[data-pbd-bulk-remove]');
  const filterReorderNote = root.querySelector('[data-pbd-filter-reorder-note]');
  const sortStatus = root.querySelector('[data-pbd-sort-status]');
  let sortable = null;

  if (selectedSearch) selectedSearch.value = sessionStorage.getItem(`${storagePrefix}selectedSearch`) || '';
  if (selectedStage) selectedStage.value = sessionStorage.getItem(`${storagePrefix}selectedStage`) || '';
  if (selectedReadiness) selectedReadiness.value = sessionStorage.getItem(`${storagePrefix}selectedReadiness`) || '';

  const currentRows = () => [...(sortableBody?.querySelectorAll('tr[data-project-id]') || [])];
  const selectedRowIds = () => currentRows()
    .filter((row) => row.querySelector('[data-pbd-row-select]')?.checked)
    .map((row) => Number(row.dataset.projectId));

  const refreshBulkActions = () => {
    const count = selectedRowIds().length;
    [bulkTop, bulkBottom, bulkRemove].forEach((button) => { if (button) button.disabled = count === 0; });
  };

  const applySelectedFilters = () => {
    const term = normalize(selectedSearch?.value);
    const stage = selectedStage?.value || '';
    const readiness = selectedReadiness?.value || '';
    let shown = 0;
    currentRows().forEach((row) => {
      const matchesText = !term || normalize(row.dataset.searchText).includes(term);
      const matchesStage = !stage || row.dataset.stage === stage;
      const matchesReadiness = !readiness || row.dataset[readiness.replace(/-([a-z])/g, (_, c) => c.toUpperCase())] === 'true';
      const visible = matchesText && matchesStage && matchesReadiness;
      row.hidden = !visible;
      if (visible) shown += 1;
    });

    const filtered = Boolean(term || stage || readiness);
    if (visibleCount) visibleCount.textContent = `${shown} shown`;
    if (noFilterResults) noFilterResults.hidden = shown > 0 || currentRows().length === 0;
    if (filterReorderNote) filterReorderNote.hidden = !filtered;
    if (sortable) sortable.option('disabled', filtered);
    currentRows().forEach((row) => {
      const handle = row.querySelector('.pbd-drag');
      if (handle) handle.disabled = filtered;
    });
    if (selectVisible) {
      selectVisible.checked = false;
      selectVisible.indeterminate = false;
    }
    refreshBulkActions();
  };

  [selectedSearch, selectedStage, selectedReadiness].forEach((control) => control?.addEventListener('input', () => {
    if (selectedSearch) sessionStorage.setItem(`${storagePrefix}selectedSearch`, selectedSearch.value);
    if (selectedStage) sessionStorage.setItem(`${storagePrefix}selectedStage`, selectedStage.value);
    if (selectedReadiness) sessionStorage.setItem(`${storagePrefix}selectedReadiness`, selectedReadiness.value);
    applySelectedFilters();
  }));

  selectVisible?.addEventListener('change', () => {
    currentRows().filter((row) => !row.hidden).forEach((row) => {
      const checkbox = row.querySelector('[data-pbd-row-select]');
      if (checkbox) checkbox.checked = selectVisible.checked;
    });
    refreshBulkActions();
  });

  const saveProjectOrder = async () => {
    if (!sortableBody || deckId <= 0) return;
    const projectIds = currentRows().map((row) => Number(row.dataset.projectId)).filter(Number.isInteger);
    setState(sortStatus, 'Saving slide order…', 'saving');
    try {
      const payload = await requestJson(root.dataset.reorderUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, projectIds, rowVersion: currentRowVersion() })
      });
      updateRowVersion(payload?.rowVersion);
      setState(sortStatus, 'Slide order saved.', 'saved');
    } catch (error) {
      setState(sortStatus, error.message || 'Slide order could not be saved.', 'error');
    }
  };

  const initialiseSortable = () => {
    if (!sortableBody || !window.Sortable || deckId <= 0) return;
    sortable?.destroy();
    sortable = window.Sortable.create(sortableBody, {
      animation: 130,
      handle: '.pbd-drag',
      ghostClass: 'pbd-sort-ghost',
      chosenClass: 'pbd-sort-chosen',
      onStart: () => setState(sortStatus, 'Reordering…', 'saving'),
      onEnd: saveProjectOrder
    });
    applySelectedFilters();
  };

  const createReadinessIcon = (icon, ready, title) => {
    const span = document.createElement('span');
    span.className = ready ? 'is-ready' : 'is-missing';
    span.title = title;
    const i = document.createElement('i');
    i.className = `bi ${icon}`;
    span.append(i);
    return span;
  };

  const buildProjectRow = (project) => {
    const hasDescription = project.briefDescription && project.briefDescription !== 'Brief description not recorded.';
    const row = document.createElement('tr');
    row.dataset.projectId = String(project.projectId);
    row.dataset.searchText = [project.projectName, project.lifecycleDisplay, project.presentStage, project.projectCategory, project.technicalCategory, project.externalStatus].filter(Boolean).join(' ');
    row.dataset.stage = project.presentStage || '';
    row.dataset.missingStatus = String(!project.externalStatus);
    row.dataset.missingCostRd = String(!project.costRd?.isAvailable);
    row.dataset.missingProliferation = String(!project.proliferationCost?.isAvailable);
    row.dataset.missingPhoto = String(!project.hasCoverPhoto);
    row.dataset.missingDescription = String(!hasDescription);

    const selectCell = document.createElement('td');
    selectCell.className = 'pbd-select-column';
    const selector = document.createElement('input');
    selector.type = 'checkbox';
    selector.dataset.pbdRowSelect = '';
    selector.setAttribute('aria-label', `Select ${project.projectName}`);
    selectCell.append(selector);

    const dragCell = document.createElement('td');
    dragCell.className = 'pbd-drag-column';
    const drag = document.createElement('button');
    drag.type = 'button';
    drag.className = 'pbd-drag';
    drag.title = 'Drag or use the up/down arrow keys to reorder';
    drag.setAttribute('aria-label', `Reorder ${project.projectName}. Use the up or down arrow key.`);
    drag.setAttribute('aria-keyshortcuts', 'ArrowUp ArrowDown');
    drag.innerHTML = '<i class="bi bi-grip-vertical" aria-hidden="true"></i>';
    dragCell.append(drag);

    const projectCell = document.createElement('td');
    projectCell.className = 'pbd-project-name';
    const link = document.createElement('a');
    link.href = project.openUrl || '#';
    link.target = '_blank';
    link.rel = 'noopener';
    link.title = project.projectName;
    link.textContent = project.projectName;
    const meta = document.createElement('small');
    meta.textContent = `${project.lifecycleDisplay} · ${project.projectCategory || 'Not categorised'} · ${project.technicalCategory || 'No technical category'}`;
    projectCell.append(link, meta);

    const stageCell = document.createElement('td');
    const stagePill = document.createElement('span');
    stagePill.className = 'pbd-stage';
    stagePill.textContent = project.presentStage || 'Not recorded';
    stageCell.append(stagePill);

    const costCell = document.createElement('td');
    costCell.className = 'pbd-cost';
    const costValue = document.createElement('strong');
    costValue.textContent = project.costRd?.displayValue || 'Not recorded';
    costCell.append(costValue);
    if (project.costRd?.basisDisplay) {
      const basis = document.createElement('small');
      basis.textContent = project.costRd.basisDisplay;
      costCell.append(basis);
    }

    const proliferationCell = document.createElement('td');
    proliferationCell.className = 'pbd-cost';
    const proliferationValue = document.createElement('strong');
    proliferationValue.textContent = project.proliferationCost?.displayValue || 'Not recorded';
    proliferationCell.append(proliferationValue);

    const statusCell = document.createElement('td');
    statusCell.className = 'pbd-status';
    if (project.externalStatus) {
      const status = document.createElement('span');
      status.title = project.externalStatus;
      status.textContent = project.externalStatus;
      statusCell.append(status);
      if (project.externalStatusDate) {
        const date = document.createElement('small');
        date.textContent = formatDate(project.externalStatusDate);
        statusCell.append(date);
      }
    } else {
      const missing = document.createElement('span');
      missing.className = 'pbd-missing';
      missing.textContent = 'No external status recorded';
      statusCell.append(missing);
    }

    const readinessCell = document.createElement('td');
    const readiness = document.createElement('div');
    readiness.className = 'pbd-readiness-icons';
    readiness.setAttribute('aria-label', 'Project deck readiness');
    readiness.append(
      createReadinessIcon('bi-image', project.hasCoverPhoto, project.hasCoverPhoto ? 'PowerPoint-ready cover photograph available' : (project.coverPhotoReadinessReason || 'No PowerPoint-ready cover photograph')),
      createReadinessIcon('bi-chat-left-text', Boolean(project.externalStatus), project.externalStatus ? 'External status available' : 'External status missing'),
      createReadinessIcon('bi-currency-rupee', Boolean(project.costRd?.isAvailable), project.costRd?.isAvailable ? `Cost (R&D) available from ${project.costRd.basisDisplay}` : 'Cost (R&D) not recorded'),
      createReadinessIcon('bi-card-text', Boolean(hasDescription), hasDescription ? 'Capability overview available' : 'Capability overview not recorded')
    );
    readinessCell.append(readiness);

    const actionCell = document.createElement('td');
    actionCell.className = 'pbd-row-actions';
    const edit = document.createElement('button');
    edit.type = 'button';
    edit.className = 'btn btn-sm btn-link';
    edit.dataset.pbdEditDescription = '';
    edit.dataset.projectId = String(project.projectId);
    edit.dataset.projectName = project.projectName;
    edit.dataset.description = project.briefDescriptionOverride || '';
    edit.title = 'Edit briefing description';
    edit.setAttribute('aria-label', `Edit briefing description for ${project.projectName}`);
    edit.innerHTML = '<i class="bi bi-pencil-square"></i>';

    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'btn btn-sm btn-link text-danger';
    remove.dataset.pbdRemoveProject = '';
    remove.dataset.projectId = String(project.projectId);
    remove.dataset.projectName = project.projectName;
    remove.title = 'Remove from deck';
    remove.setAttribute('aria-label', `Remove ${project.projectName} from deck`);
    remove.innerHTML = '<i class="bi bi-x-lg"></i>';
    actionCell.append(edit, remove);

    row.append(selectCell, dragCell, projectCell, stageCell, costCell, proliferationCell, statusCell, readinessCell, actionCell);
    return row;
  };

  const populateStageFilter = (projects) => {
    if (!selectedStage) return;
    const selected = selectedStage.value;
    selectedStage.replaceChildren(new Option('All stages', ''));
    [...new Set(projects.map((project) => project.presentStage).filter(Boolean))]
      .sort((a, b) => a.localeCompare(b))
      .forEach((stage) => selectedStage.append(new Option(stage, stage)));
    selectedStage.value = [...selectedStage.options].some((option) => option.value === selected) ? selected : '';
  };

  const renderSelectedProjects = (projects) => {
    if (!sortableBody) return;
    sortableBody.replaceChildren(...projects.map(buildProjectRow));
    const hasProjects = projects.length > 0;
    if (selectedTableWrap) selectedTableWrap.hidden = !hasProjects;
    if (emptyProjects) emptyProjects.hidden = hasProjects;
    populateStageFilter(projects);
    initialiseSortable();
    refreshBulkActions();
  };

  const applyEditorState = (deck) => {
    if (!deck) return;
    updateRowVersion(deck.rowVersion);
    updateReadinessSummary(deck);
    renderSelectedProjects(deck.projects || []);
  };

  const updateMembership = async (addProjectIds = [], removeProjectIds = [], statusElement = sortStatus) => {
    if (deckId <= 0 || (addProjectIds.length === 0 && removeProjectIds.length === 0)) return null;
    setState(statusElement, 'Saving deck membership…', 'saving');
    try {
      const payload = await requestJson(root.dataset.membershipUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, addProjectIds, removeProjectIds, rowVersion: currentRowVersion() })
      });
      applyEditorState(payload?.deck);
      if (searchRows.length > 0) {
        const added = new Set(addProjectIds.map(Number));
        const removed = new Set(removeProjectIds.map(Number));
        searchRows = searchRows.map((project) => ({
          ...project,
          isSelected: added.has(project.projectId)
            ? true
            : removed.has(project.projectId)
              ? false
              : project.isSelected
        }));
        renderSearchResults(searchRows);
      }
      const changes = [];
      if (payload?.addedCount) changes.push(`${payload.addedCount} added`);
      if (payload?.removedCount) changes.push(`${payload.removedCount} removed`);
      setState(statusElement, changes.length ? `Deck updated — ${changes.join(', ')}.` : 'No membership changes were required.', 'saved');
      return payload;
    } catch (error) {
      setState(statusElement, error.message || 'Deck membership could not be updated.', 'error');
      throw error;
    }
  };

  root.addEventListener('change', (event) => {
    if (event.target.matches('[data-pbd-row-select]')) refreshBulkActions();
  });

  root.addEventListener('submit', async (event) => {
    const form = event.target.closest('[data-pbd-remove-project-form]');
    if (!form) return;
    event.preventDefault();
    const projectId = Number(form.querySelector('input[name="projectId"]')?.value || 0);
    if (!projectId) return;
    const button = form.querySelector('button[type="submit"]');
    button?.setAttribute('disabled', 'disabled');
    try { await updateMembership([], [projectId]); }
    finally { button?.removeAttribute('disabled'); }
  });

  root.addEventListener('click', async (event) => {
    const remove = event.target.closest('[data-pbd-remove-project]');
    if (remove) {
      const projectId = Number(remove.dataset.projectId || 0);
      if (!projectId) return;
      remove.disabled = true;
      try { await updateMembership([], [projectId]); }
      finally { remove.disabled = false; }
      return;
    }
  });

  bulkRemove?.addEventListener('click', async () => {
    const ids = selectedRowIds();
    if (ids.length === 0) return;
    if (!window.confirm(`Remove ${ids.length} selected project${ids.length === 1 ? '' : 's'} from this deck?`)) return;
    await updateMembership([], ids);
  });

  const moveSelected = async (toTop) => {
    const selected = new Set(selectedRowIds());
    if (selected.size === 0 || !sortableBody) return;
    const rows = currentRows();
    const moving = rows.filter((row) => selected.has(Number(row.dataset.projectId)));
    const remaining = rows.filter((row) => !selected.has(Number(row.dataset.projectId)));
    const ordered = toTop ? [...moving, ...remaining] : [...remaining, ...moving];
    ordered.forEach((row) => sortableBody.append(row));
    await saveProjectOrder();
    applySelectedFilters();
  };
  bulkTop?.addEventListener('click', () => moveSelected(true));
  bulkBottom?.addEventListener('click', () => moveSelected(false));

  sortableBody?.addEventListener('keydown', async (event) => {
    const handle = event.target.closest('.pbd-drag');
    if (!handle || !['ArrowUp', 'ArrowDown'].includes(event.key) || handle.disabled) return;
    const row = handle.closest('tr[data-project-id]');
    const target = event.key === 'ArrowUp' ? row?.previousElementSibling : row?.nextElementSibling;
    if (!(row instanceof HTMLTableRowElement) || !(target instanceof HTMLTableRowElement)) return;
    event.preventDefault();
    if (event.key === 'ArrowUp') sortableBody.insertBefore(row, target);
    else sortableBody.insertBefore(target, row);
    handle.focus();
    await saveProjectOrder();
  });

  initialiseSortable();

  // Manage individual membership across all projects.
  const individualForm = root.querySelector('[data-pbd-individual-form]');
  const searchInput = root.querySelector('[data-pbd-project-search]');
  const searchResults = root.querySelector('[data-pbd-search-results]');
  const searchStatus = root.querySelector('[data-pbd-search-status]');
  const membershipFilter = root.querySelector('[data-pbd-membership-filter]');
  const membershipSummary = root.querySelector('[data-pbd-selected-count]');
  const applyMembershipButton = root.querySelector('[data-pbd-apply-membership]');
  const resultBaseline = new Map();
  const resultDesired = new Map();
  let searchRows = [];
  let searchTimer = 0;
  let searchAbortController = null;

  const pendingMembershipChanges = () => {
    const add = [];
    const remove = [];
    resultDesired.forEach((desired, projectId) => {
      const baseline = resultBaseline.get(projectId);
      if (desired && !baseline) add.push(projectId);
      if (!desired && baseline) remove.push(projectId);
    });
    return { add, remove };
  };

  const updateMembershipSummary = () => {
    const { add, remove } = pendingMembershipChanges();
    const parts = [];
    if (add.length) parts.push(`${add.length} to add`);
    if (remove.length) parts.push(`${remove.length} to remove`);
    if (membershipSummary) membershipSummary.textContent = parts.length ? parts.join(' · ') : 'No pending changes';
    if (applyMembershipButton) applyMembershipButton.disabled = add.length === 0 && remove.length === 0;
  };

  const applyMembershipResultFilter = () => {
    const filter = membershipFilter?.value || 'all';
    let shown = 0;
    searchResults?.querySelectorAll('[data-project-result]').forEach((node) => {
      const id = Number(node.dataset.projectResult);
      const selected = resultDesired.get(id) === true;
      const visible = filter === 'all' || (filter === 'selected' && selected) || (filter === 'unselected' && !selected);
      node.hidden = !visible;
      if (visible) shown += 1;
    });
    setState(searchStatus, searchRows.length ? `${shown} of ${searchRows.length} matching projects shown.` : 'No projects match this search.');
  };

  const renderSearchResults = (rows) => {
    searchRows = rows;
    resultBaseline.clear();
    resultDesired.clear();
    searchResults?.replaceChildren();
    if (!rows.length) {
      setState(searchStatus, 'No projects match this search.');
      updateMembershipSummary();
      return;
    }

    rows.forEach((project) => {
      resultBaseline.set(project.projectId, Boolean(project.isSelected));
      resultDesired.set(project.projectId, Boolean(project.isSelected));
      const label = document.createElement('label');
      label.className = 'pbd-search-result';
      label.dataset.projectResult = String(project.projectId);

      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.checked = Boolean(project.isSelected);
      checkbox.addEventListener('change', () => {
        resultDesired.set(project.projectId, checkbox.checked);
        label.classList.toggle('is-selected', checkbox.checked);
        badge.textContent = checkbox.checked ? 'IN DECK' : 'NOT IN DECK';
        badge.classList.toggle('is-in-deck', checkbox.checked);
        updateMembershipSummary();
        applyMembershipResultFilter();
      });

      const body = document.createElement('span');
      const heading = document.createElement('span');
      heading.className = 'pbd-search-result__heading';
      const name = document.createElement('strong');
      name.textContent = project.projectName;
      const badge = document.createElement('em');
      badge.className = project.isSelected ? 'pbd-membership-badge is-in-deck' : 'pbd-membership-badge';
      badge.textContent = project.isSelected ? 'IN DECK' : 'NOT IN DECK';
      heading.append(name, badge);
      const meta = document.createElement('small');
      meta.textContent = [project.lifecycle, project.presentStage, project.projectCategory, project.technicalCategory, project.projectOfficer].filter(Boolean).join(' · ');
      const ref = document.createElement('small');
      ref.textContent = project.caseFileNumber ? `Ref: ${project.caseFileNumber}` : 'No case-file reference';
      body.append(heading, meta, ref);
      label.append(checkbox, body);
      label.classList.toggle('is-selected', checkbox.checked);
      searchResults?.append(label);
    });
    updateMembershipSummary();
    applyMembershipResultFilter();
  };

  const searchProjects = async () => {
    const query = searchInput?.value.trim() || '';
    if (query.length < 2) {
      searchAbortController?.abort();
      searchResults?.replaceChildren();
      searchRows = [];
      setState(searchStatus, 'Enter at least two characters.');
      updateMembershipSummary();
      return;
    }

    searchAbortController?.abort();
    searchAbortController = new AbortController();
    setState(searchStatus, 'Searching…');
    try {
      const url = new URL(root.dataset.searchUrl, window.location.origin);
      url.searchParams.set('deckId', String(deckId));
      url.searchParams.set('query', query);
      const response = await fetch(url, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: searchAbortController.signal
      });
      const payload = await response.json().catch(() => null);
      if (!response.ok) throw new Error(payload?.message || `Search failed (${response.status}).`);
      renderSearchResults(payload || []);
    } catch (error) {
      if (error.name === 'AbortError') return;
      setState(searchStatus, error.message || 'Project search could not be completed.', 'error');
    }
  };

  searchInput?.addEventListener('input', () => {
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(searchProjects, 260);
  });
  membershipFilter?.addEventListener('change', applyMembershipResultFilter);

  individualForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const { add, remove } = pendingMembershipChanges();
    if (add.length === 0 && remove.length === 0) return;
    applyMembershipButton?.setAttribute('disabled', 'disabled');
    try {
      await updateMembership(add, remove, searchStatus);
    } finally {
      updateMembershipSummary();
    }
  });

  // Briefing-specific project description editor (delegated for dynamically refreshed rows).
  const descriptionModalElement = document.getElementById('pbd-description-modal');
  const descriptionModal = descriptionModalElement && window.bootstrap
    ? window.bootstrap.Modal.getOrCreateInstance(descriptionModalElement)
    : null;
  const descriptionForm = descriptionModalElement?.querySelector('[data-pbd-description-form]');
  const descriptionProjectId = descriptionModalElement?.querySelector('[data-pbd-description-project-id]');
  const descriptionValue = descriptionModalElement?.querySelector('[data-pbd-description-value]');
  const descriptionTitle = descriptionModalElement?.querySelector('[data-pbd-description-title]');
  const descriptionStatus = descriptionModalElement?.querySelector('[data-pbd-description-status]');

  root.addEventListener('click', (event) => {
    const button = event.target.closest('[data-pbd-edit-description]');
    if (!button) return;
    if (descriptionProjectId) descriptionProjectId.value = button.dataset.projectId || '';
    if (descriptionValue) descriptionValue.value = button.dataset.description || '';
    if (descriptionTitle) descriptionTitle.textContent = button.dataset.projectName || 'Brief description';
    setState(descriptionStatus, '');
    descriptionModal?.show();
    window.setTimeout(() => descriptionValue?.focus(), 180);
  });

  descriptionForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const projectId = Number(descriptionProjectId?.value || 0);
    if (!projectId || deckId <= 0) return;
    const submit = descriptionForm.querySelector('button[type="submit"]');
    submit?.setAttribute('disabled', 'disabled');
    setState(descriptionStatus, 'Saving…');
    try {
      const payload = await requestJson(root.dataset.descriptionUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, projectId, value: descriptionValue?.value || null, rowVersion: currentRowVersion() })
      });
      updateRowVersion(payload?.rowVersion);
      const editorButton = root.querySelector(`[data-pbd-edit-description][data-project-id="${projectId}"]`);
      if (editorButton) editorButton.dataset.description = descriptionValue?.value || '';
      setState(descriptionStatus, 'Briefing description saved.', 'success');
      window.setTimeout(() => descriptionModal?.hide(), 450);
    } catch (error) {
      setState(descriptionStatus, error.message || 'Description could not be saved.', 'error');
    } finally {
      submit?.removeAttribute('disabled');
    }
  });

  // Generate and download the PowerPoint without leaving the builder.
  const generateForm = root.querySelector('[data-pbd-generate-form]');
  const generateLabel = root.querySelector('[data-pbd-generate-label]');
  const generateProgress = root.querySelector('[data-pbd-generate-progress]');
  const generateStatus = root.querySelector('[data-pbd-generate-status]');
  const extractFileName = (header) => {
    if (!header) return 'Project_Briefing_Deck.pptx';
    const encoded = header.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
    if (encoded) return decodeURIComponent(encoded);
    return header.match(/filename="?([^";]+)"?/i)?.[1] || 'Project_Briefing_Deck.pptx';
  };

  generateForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    if (!generateButton || generateButton.disabled) return;
    generateButton.disabled = true;
    generateLabel?.classList.add('d-none');
    generateProgress?.classList.remove('d-none');
    setState(generateStatus, 'Building editable PowerPoint slides from current project data…');
    try {
      const response = await fetch(generateForm.action, {
        method: 'POST',
        credentials: 'same-origin',
        body: new FormData(generateForm),
        headers: {
          Accept: 'application/vnd.openxmlformats-officedocument.presentationml.presentation, application/problem+json, application/json',
          'X-CSRF-TOKEN': token,
          'X-Requested-With': 'XMLHttpRequest'
        }
      });
      const contentType = response.headers.get('content-type') || '';
      if (!response.ok || contentType.includes('json')) {
        const payload = contentType.includes('json') ? await response.json() : null;
        throw new Error(payload?.message || payload?.title || `PowerPoint generation failed (${response.status}).`);
      }
      const blob = await response.blob();
      const downloadUrl = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = downloadUrl;
      anchor.download = extractFileName(response.headers.get('content-disposition'));
      document.body.append(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(downloadUrl), 1500);
      const slideCount = response.headers.get('X-Project-Briefing-Slides');
      setState(generateStatus, slideCount ? `PowerPoint generated successfully — ${slideCount} slides.` : 'PowerPoint generated successfully.', 'success');
    } catch (error) {
      setState(generateStatus, error.message || 'The PowerPoint deck could not be generated.', 'error');
    } finally {
      generateButton.disabled = false;
      generateLabel?.classList.remove('d-none');
      generateProgress?.classList.add('d-none');
    }
  });

  document.getElementById('pbd-new-deck-modal')?.addEventListener('shown.bs.modal', () => {
    document.querySelector('[data-pbd-new-name]')?.focus();
  });

  applySelectedFilters();
}
