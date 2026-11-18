// Add projects (docs/ux/briefing-add-projects.md): filters narrow one list of every project
// (values inside a filter are alternatives, filters combine), ticks are kept when the filters
// change so sets mix, and Add saves the ticked projects through the page's AddSelection form
// (Undo offered by that form's handler). A project already in the deck has Remove instead of a
// tick. Kept in its own module so its behaviour can be tested (cold review 2, M5).
//
// The page gives it what it shares with the rest of the Studio:
//   root, deckId, checkReply(response, options), setState(element, message, state),
//   statusElement (the dialog's status line), updateMembership(add, remove, statusElement),
//   confirmRemoval(options) -> Promise<boolean> (the PrismConfirm dialog), modalElement, getMembers() -> the deck's projects now.
export function initAddProjects(deps) {
  const { root, deckId, checkReply, setState, statusElement, updateMembership, confirmRemoval, modalElement, getMembers } = deps;
  const filterRoot = root.querySelector('[data-pbd-filter]');
  if (!filterRoot) return null;
  const doc = root.ownerDocument;
  const view = doc.defaultView;
  const filterQuery = filterRoot.querySelector('[data-pbd-filter-query]');
  const filterChips = filterRoot.querySelector('[data-pbd-filter-chips]');
  const filterPanel = filterRoot.querySelector('[data-pbd-filter-panel]');
  const filterCount = filterRoot.querySelector('[data-pbd-filter-count]');
  const filterTickAll = filterRoot.querySelector('[data-pbd-filter-tick-all]');
  const filterListRetry = filterRoot.querySelector('[data-pbd-filter-list-retry]');
  const filterResults = filterRoot.querySelector('[data-pbd-filter-results]');
  const filterAddForm = filterRoot.querySelector('[data-pbd-filter-add-form]');
  const filterTickedLabel = root.querySelector('[data-pbd-filter-ticked]');
  const filterRoom = root.querySelector('[data-pbd-filter-room]');
  const filterClear = root.querySelector('[data-pbd-filter-clear]');
  const filterAdd = root.querySelector('[data-pbd-filter-add]');
  // A deck holds at most this many projects (ProjectBriefingDeckService.MaximumProjectsPerDeck).
  const maximumProjects = Number(root.dataset.maxProjects) || 120;

  const members = () => new Set((getMembers?.() || []).map((project) => Number(project.projectId)));

  // The choices and their counts come from the server the first time the dialog opens, not with
  // the page. "loading", "loaded" or "failed"; a failed load can be tried again from the panel.
  let filterOptions = {};
  let optionsState = 'idle';
  let optionsLoad = null;
  let optionsError = '';
  // With refresh, choices already loaded are fetched again quietly (each opening, cold review 4),
  // so their counts follow the list; what is shown stays until the new ones arrive.
  const loadFilterOptions = ({ refresh = false } = {}) => {
    if (deckId <= 0 || optionsState === 'loading' || optionsState === 'refreshing') return optionsLoad;
    if (optionsState === 'loaded' && !refresh) return optionsLoad;
    const quiet = optionsState === 'loaded';
    optionsState = quiet ? 'refreshing' : 'loading';
    if (!quiet) renderFilterPanel();
    const url = new URL(root.dataset.filterOptionsUrl, view.location.origin);
    url.searchParams.set('deckId', String(deckId));
    optionsLoad = view.fetch(url, {
      credentials: 'same-origin',
      headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
    })
      .then(async (response) => {
        checkReply(response, { read: true });
        const payload = await response.json().catch(() => null);
        // The server's own message (deck not found, no access) when it gives one (cold review 4).
        if (!response.ok) throw new Error(payload?.message || 'The choices could not be loaded.');
        filterOptions = payload || {};
        optionsState = 'loaded';
      })
      .catch((error) => {
        // checkReply's message (signed out, no access) says more than a generic failure
        // (cold review 3). A failed quiet refresh keeps the choices already shown.
        if (quiet) { optionsState = 'loaded'; return; }
        optionsError = error?.message || '';
        optionsState = 'failed';
      })
      .finally(() => {
        // Whoever was on the open filter's chip while its choices loaded is taken into them
        // (cold review 4); focus inside the panel is not pulled away by a quiet refresh.
        const active = doc.activeElement;
        const onOpenChip = Boolean(openFilter) && active?.dataset?.pbdFilterOpen === openFilter;
        const inPanel = filterPanel.contains(active);
        const inChips = filterChips.contains(active);
        if (!(quiet && inChips)) renderFilterChips();
        if (!(quiet && inPanel)) renderFilterPanel();
        if (onOpenChip) {
          (filterPanel.querySelector('input, select, button')
            || filterChips.querySelector(`[data-pbd-filter-open="${openFilter}"]`))?.focus();
        }
      });
    return optionsLoad;
  };
  let showAllYears = false;

  // The completed-between lists offer the last ten financial years and the earliest (cold review
  // 2): thirty-five years back to FY 1992-93 made a long list for a rare choice.
  // "Show all years" lists every one (cold review 4), as does a range already set to an older year.
  const completionYears = () => {
    const years = filterOptions.completionFinancialYears || [];
    if (years.length <= 11 || showAllYears) return years;
    const shown = new Set(years.slice(0, 10).map((year) => String(year.value)).concat(String(years[years.length - 1].value)));
    if ([filterState.completedFrom, filterState.completedTo].some((value) => value && !shown.has(String(value)))) return years;
    const earliest = years[years.length - 1];
    return [...years.slice(0, 10), { value: earliest.value, label: `${earliest.label} (earliest)` }];
  };

  const FILTERS = [
    { key: 'arpp', label: 'ARPP', options: () => filterOptions.arppYears || [] },
    { key: 'technicalCategory', label: 'Technical category', options: () => filterOptions.technicalCategories || [] },
    { key: 'projectCategory', label: 'Project category', options: () => filterOptions.projectCategories || [] },
    {
      key: 'status',
      label: 'Status',
      options: () => [
        { value: 'Active', label: 'Ongoing', count: filterOptions.ongoingCount },
        { value: 'Completed', label: 'Completed', count: filterOptions.completedCount }
      ]
    },
    { key: 'completed', label: 'Completed between', range: true },
    { key: 'proliferation', label: 'Available for proliferation', toggle: true }
  ];
  const filterState = {
    arpp: new Set(), technicalCategory: new Set(), projectCategory: new Set(), status: new Set(),
    completedFrom: '', completedTo: '', proliferation: false
  };
  const ticked = new Map(); // projectId -> row, kept across filter changes
  let showTicked = false; // the list shows the ticked projects instead of the filter's matches
  let adding = false; // an Add is queued or saving: a second click waits for it
  let pendingAdd = new Map(); // the rows an Add sent, so Undo can tick them again
  let filterRows = [];
  let openFilter = '';
  let filterTimer = 0;
  let filterAbort = null;
  let listRequest = 0;

  const optionLabel = (filter, value) => filter.options().find((option) => String(option.value) === String(value))?.label || String(value);
  const yearLabel = (year) => (filterOptions.completionFinancialYears || []).find((option) => String(option.value) === String(year))?.label || year;
  const filterSummary = (filter) => {
    if (filter.toggle) return filterState.proliferation ? filter.label : '';
    if (filter.range) {
      const { completedFrom: from, completedTo: to } = filterState;
      if (from && to) return from === to ? `Completed in ${yearLabel(from)}` : `Completed ${yearLabel(from)} to ${yearLabel(to)}`;
      if (from) return `Completed from ${yearLabel(from)}`;
      if (to) return `Completed up to ${yearLabel(to)}`;
      return '';
    }
    const values = [...filterState[filter.key]];
    if (!values.length) return '';
    const names = values.map((value) => optionLabel(filter, value));
    return `${filter.label}: ${names.length > 2 ? `${names.slice(0, 2).join(', ')} +${names.length - 2}` : names.join(', ')}`;
  };

  const renderFilterChips = () => {
    filterChips.replaceChildren();
    FILTERS.forEach((filter) => {
      const summary = filterSummary(filter);
      const chip = doc.createElement('span');
      chip.className = `pbd-filter-chip${summary ? ' is-set' : ''}${openFilter === filter.key ? ' is-open' : ''}`;
      const button = doc.createElement('button');
      button.type = 'button';
      button.className = 'pbd-filter-chip__open';
      button.dataset.pbdFilterOpen = filter.key;
      // While the ticked projects are reviewed the filters wait (cold review 2): changing one
      // would quietly leave the review.
      button.disabled = showTicked;
      if (filter.toggle) {
        button.setAttribute('aria-pressed', String(filterState.proliferation));
        const count = filterOptions.proliferationCount;
        button.textContent = Number.isFinite(count) ? `${filter.label} · ${count}` : filter.label;
      } else {
        button.setAttribute('aria-expanded', String(openFilter === filter.key));
        button.setAttribute('aria-controls', 'pbd-filter-panel');
        button.textContent = summary || `+ ${filter.label}`;
      }
      chip.append(button);
      if (summary && !filter.toggle && !showTicked) {
        const clear = doc.createElement('button');
        clear.type = 'button';
        clear.className = 'pbd-filter-chip__clear';
        clear.dataset.pbdFilterClear = filter.key;
        clear.setAttribute('aria-label', `Clear ${filter.label}`);
        clear.innerHTML = '<i class="bi bi-x" aria-hidden="true"></i>';
        chip.append(clear);
      }
      filterChips.append(chip);
    });
    if (filterQuery) filterQuery.disabled = showTicked;
    const anySet = FILTERS.some((filter) => filterSummary(filter)) || Boolean(filterQuery?.value.trim());
    if (anySet && !showTicked) {
      const reset = doc.createElement('button');
      reset.type = 'button';
      reset.className = 'btn btn-link btn-sm pbd-filter__reset';
      reset.dataset.pbdFilterReset = 'true';
      reset.textContent = 'Clear filters';
      filterChips.append(reset);
    }
  };

  const panelNote = (text) => {
    const note = doc.createElement('small');
    note.className = 'pbd-filter__note';
    note.textContent = text;
    return note;
  };
  // Closes the open filter; useful on a phone, where an open panel leaves little room for the list.
  // Named "Close", not "Done", so it is not mistaken for the dialog's Done.
  const panelDone = () => {
    const done = doc.createElement('button');
    done.type = 'button';
    done.className = 'btn btn-sm btn-outline-secondary pbd-filter__done';
    done.dataset.pbdFilterDone = 'true';
    done.textContent = 'Close';
    const filter = FILTERS.find((candidate) => candidate.key === openFilter);
    if (filter) done.setAttribute('aria-label', `Close the ${filter.label} choices`);
    return done;
  };

  const renderFilterPanel = () => {
    const filter = FILTERS.find((candidate) => candidate.key === openFilter);
    filterPanel.replaceChildren();
    filterPanel.hidden = !filter;
    if (!filter) return;
    filterPanel.id = 'pbd-filter-panel';
    filterPanel.setAttribute('aria-label', filter.label);
    if (!['loaded', 'refreshing'].includes(optionsState) && !filter.toggle && filter.key !== 'status') {
      if (optionsState === 'failed') {
        filterPanel.append(panelNote(optionsError || 'The choices could not be loaded.'));
        const retry = doc.createElement('button');
        retry.type = 'button';
        retry.className = 'btn btn-sm btn-outline-primary';
        retry.dataset.pbdFilterRetry = 'true';
        retry.textContent = 'Try again';
        filterPanel.append(retry);
      } else {
        filterPanel.append(panelNote('Loading the choices…'));
      }
      return;
    }
    if (filter.range) {
      const years = completionYears();
      const select = (name, label) => {
        const wrap = doc.createElement('label');
        wrap.className = 'pbd-filter__range';
        const text = doc.createElement('span');
        text.textContent = label;
        const element = doc.createElement('select');
        element.className = 'form-select form-select-sm';
        element.dataset.pbdFilterRange = name;
        element.append(new view.Option('Any', ''));
        years.forEach((year) => element.append(new view.Option(year.label, String(year.value))));
        element.value = filterState[name];
        wrap.append(text, element);
        return wrap;
      };
      filterPanel.append(select('completedFrom', 'From'), select('completedTo', 'To'));
      const allYears = filterOptions.completionFinancialYears || [];
      if (years.length < allYears.length) {
        const more = doc.createElement('button');
        more.type = 'button';
        more.className = 'btn btn-link btn-sm';
        more.dataset.pbdFilterAllYears = 'true';
        more.textContent = `Show all ${allYears.length} years`;
        filterPanel.append(more);
      }
      filterPanel.append(panelDone());
      const undated = filterOptions.completedWithoutDateCount;
      // A project recorded with a year but no date or month is counted from April of that year
      // (cold review 4): the note says so.
      filterPanel.append(panelNote(`Financial years, April to March. A completion year with no date counts from April of that year.${undated
        ? ` ${undated} completed ${undated === 1 ? 'project has' : 'projects have'} no completion date or year and ${undated === 1 ? 'is' : 'are'} not matched.`
        : ''}`));
      return;
    }
    const options = filter.options();
    if (!options.length) {
      filterPanel.append(panelNote(filter.key === 'arpp' ? 'No ARPP has been published yet.' : 'Nothing to choose from yet.'));
      return;
    }
    const list = doc.createElement('div');
    list.className = 'pbd-chips';
    // A category and its sub-categories sit together, the sub-categories after it; the server
    // lists them in that order with their depth.
    let group = list;
    options.forEach((option) => {
      if (Number.isFinite(option.depth) && option.depth === 0) {
        group = doc.createElement('span');
        group.className = 'pbd-filter__group';
        list.append(group);
      }
      const label = doc.createElement('label');
      label.className = `pbd-chip${option.depth > 0 ? ' is-child' : ''}`;
      const input = doc.createElement('input');
      input.type = 'checkbox';
      input.value = String(option.value);
      input.dataset.pbdFilterValue = filter.key;
      input.checked = filterState[filter.key].has(String(option.value));
      const text = doc.createElement('span');
      text.textContent = Number.isFinite(option.count) ? `${option.label} · ${option.count}` : option.label;
      label.append(input, text);
      group.append(label);
      if (option.depth > 0) group.classList.add('has-children');
    });
    filterPanel.append(list, panelDone());
    filterPanel.append(panelNote(filter.key === 'projectCategory' || filter.key === 'technicalCategory'
      ? 'A parent category includes its sub-categories. Counts are of all projects.'
      : 'Counts are of all projects.'));
  };

  // How many more projects the deck can take, and whether the ticks fit (cold review 2, High).
  const room = () => Math.max(0, maximumProjects - members().size);
  if (filterRoom && !filterRoom.id) filterRoom.id = 'pbd-filter-room';
  let roomAnnouncer = root.querySelector('[data-pbd-filter-announce]');
  if (!roomAnnouncer && filterRoom) {
    roomAnnouncer = doc.createElement('span');
    roomAnnouncer.className = 'visually-hidden';
    roomAnnouncer.dataset.pbdFilterAnnounce = 'true';
    roomAnnouncer.setAttribute('aria-live', 'polite');
    filterRoom.after(roomAnnouncer);
  }
  // When the ticks fill the deck's room, the projects not ticked cannot be ticked (cold review 3,
  // M2): finding out only on Add was too late. Ticked ones can always be unticked.
  const syncTickLimits = () => {
    const full = ticked.size >= room();
    filterResults.querySelectorAll('input[data-pbd-filter-tick]').forEach((checkbox) => {
      const held = full && !checkbox.checked;
      checkbox.disabled = held;
      // A held-back tick is described by the room line, so it is not just "dimmed" (cold review 4).
      const describedBy = [checkbox.dataset.metaId, held && filterRoom?.id].filter(Boolean).join(' ');
      if (describedBy) checkbox.setAttribute('aria-describedby', describedBy);
      else checkbox.removeAttribute('aria-describedby');
    });
  };
  let roomWasOver = false;
  const updateTickedFooter = () => {
    const count = ticked.size;
    if (count === 0) showTicked = false;
    if (filterTickedLabel) {
      // The label says what a click does; no pressed state on top of a changing label (cold review 3).
      filterTickedLabel.textContent = count === 0
        ? 'Nothing ticked'
        : showTicked ? `${count} ticked · Back to the list` : `${count} ticked · Review`;
      filterTickedLabel.disabled = count === 0;
    }
    if (filterRoom) {
      const left = room();
      const over = count > left;
      // Read out only when the ticks go over the room or come back within it, not on every tick
      // (cold review 3), through a region that is always live (cold review 4: switching a region
      // live in the same moment as its text changes is often not read).
      if (over !== roomWasOver && roomAnnouncer) {
        roomAnnouncer.textContent = over
          ? `More projects are ticked than the deck has room for: untick ${count - left}.`
          : 'The ticked projects fit in the deck again.';
      }
      roomWasOver = over;
      filterRoom.textContent = left === 0
        ? `This deck is full: it holds ${maximumProjects} projects. Remove some to add others.`
        : over
          ? `The deck has room for ${left} more: untick ${count - left}.`
          : count === left
            ? `The ticks fill the deck's room (${left} more).`
            : `Room for ${left} more in this deck.`;
      filterRoom.classList.toggle('is-over', over || left === 0);
    }
    syncTickLimits();
    if (filterClear) filterClear.hidden = count === 0;
    if (filterAdd) filterAdd.textContent = count ? `Add ${count} ${count === 1 ? 'project' : 'projects'}` : 'Add projects';
  };

  // Tick all ticks only as many as fit (cold review 3, M2).
  const tickAllLabel = (notInDeck, allTicked) => {
    if (allTicked) return `Untick these ${notInDeck.length}`;
    const untickedCount = notInDeck.filter((row) => !ticked.has(row.projectId)).length;
    const fits = Math.max(0, room() - ticked.size);
    return untickedCount <= fits
      ? `Tick all ${notInDeck.length} not in the deck`
      : `Tick the first ${fits} that fit`;
  };
  const updateTickAll = (notInDeck) => {
    if (!filterTickAll) return;
    const allTicked = notInDeck.length > 0 && notInDeck.every((row) => ticked.has(row.projectId));
    const fits = room() - ticked.size;
    filterTickAll.hidden = notInDeck.length === 0 || showTicked || (!allTicked && fits <= 0);
    filterTickAll.dataset.mode = allTicked ? 'untick' : 'tick';
    filterTickAll.textContent = tickAllLabel(notInDeck, allTicked);
  };

  const renderFilterRows = () => {
    filterResults.replaceChildren();
    // The ticked view lists every ticked project, also those the current filters hide. With
    // nothing left ticked (all unticked, or all added) it returns to the list, and the filters
    // it held back are offered again (Codex review).
    const wasReviewing = showTicked;
    if (ticked.size === 0) showTicked = false;
    if (wasReviewing && !showTicked) renderFilterChips();
    const rows = showTicked ? [...ticked.values()] : filterRows;
    const notInDeck = rows.filter((row) => !row.isSelected);
    const inDeck = rows.length - notInDeck.length;
    if (filterCount) {
      // Ongoing projects have no completion year, so "Ongoing" with "Completed between" can match
      // nothing: say why (cold review 3).
      const contradictory = filterState.status.size === 1 && filterState.status.has('Active')
        && Boolean(filterState.completedFrom || filterState.completedTo);
      filterCount.textContent = showTicked
        ? `The ${rows.length} ticked ${rows.length === 1 ? 'project' : 'projects'}, whatever the filters. Untick any you do not want; go back to the list to change the filters.`
        : rows.length
          ? `${rows.length} ${rows.length === 1 ? 'project matches' : 'projects match'}${inDeck ? ` · ${inDeck} already in the deck` : ''}`
          : contradictory
            ? 'No projects match: ongoing projects have no completion year. Clear Status or Completed between.'
            : 'No projects match these filters.';
    }
    if (filterListRetry) filterListRetry.hidden = true;
    updateTickAll(notInDeck);
    rows.forEach((row) => {
      const item = doc.createElement(row.isSelected ? 'div' : 'label');
      item.className = `pbd-search-result pbd-filter__row${row.isSelected ? ' is-in-deck' : ''}${ticked.has(row.projectId) ? ' is-selected' : ''}`;
      item.dataset.projectResult = String(row.projectId);
      if (row.isSelected) {
        const mark = doc.createElement('i');
        mark.className = 'bi bi-check2-circle pbd-filter__in-deck-mark';
        mark.setAttribute('aria-hidden', 'true');
        item.append(mark);
      } else {
        const checkbox = doc.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.checked = ticked.has(row.projectId);
        checkbox.dataset.pbdFilterTick = String(row.projectId);
        // Named by the project's name, its details read as a description, not all as the name
        // (cold review 4).
        checkbox.setAttribute('aria-labelledby', `pbd-filter-name-${row.projectId}`);
        checkbox.dataset.metaId = `pbd-filter-meta-${row.projectId}`;
        checkbox.setAttribute('aria-describedby', checkbox.dataset.metaId);
        item.append(checkbox);
      }
      const body = doc.createElement('span');
      const heading = doc.createElement('span');
      heading.className = 'pbd-search-result__heading';
      const name = doc.createElement('strong');
      name.id = `pbd-filter-name-${row.projectId}`;
      name.textContent = row.projectName;
      heading.append(name);
      if (row.isSelected) {
        const badge = doc.createElement('em');
        badge.className = 'pbd-membership-badge is-in-deck';
        badge.textContent = 'In deck';
        heading.append(badge);
      }
      const meta = doc.createElement('small');
      meta.id = `pbd-filter-meta-${row.projectId}`;
      meta.textContent = [...new Set([row.lifecycle, row.presentStage, row.technicalCategory, row.projectCategory, row.projectOfficer].filter(Boolean))].join(' · ');
      body.append(heading, meta);
      item.append(body);
      if (row.isSelected) {
        const remove = doc.createElement('button');
        remove.type = 'button';
        remove.className = 'btn btn-link btn-sm pbd-filter__remove';
        remove.dataset.pbdFilterRemove = String(row.projectId);
        remove.textContent = 'Remove';
        remove.setAttribute('aria-label', `Remove ${row.projectName} from the deck`);
        item.append(remove);
      }
      filterResults.append(item);
    });
    updateTickedFooter();
  };

  const loadFilterRows = async () => {
    if (deckId <= 0) return;
    filterAbort?.abort();
    filterAbort = new view.AbortController();
    const request = ++listRequest;
    const url = new URL(root.dataset.filterUrl, view.location.origin);
    url.searchParams.set('deckId', String(deckId));
    const query = filterQuery?.value.trim() || '';
    lastQuery = query;
    if (query) url.searchParams.set('query', query);
    ['arpp', 'technicalCategory', 'projectCategory', 'status'].forEach((key) => {
      filterState[key].forEach((value) => url.searchParams.append(key, value));
    });
    if (filterState.completedFrom) url.searchParams.set('completedFrom', filterState.completedFrom);
    if (filterState.completedTo) url.searchParams.set('completedTo', filterState.completedTo);
    if (filterState.proliferation) url.searchParams.set('proliferation', 'true');
    // "Finding projects…" only when the answer is slow, so it is not read out on every keystroke
    // (cold review 3).
    const finding = view.setTimeout(() => {
      if (request === listRequest && filterCount) filterCount.textContent = 'Finding projects…';
    }, 400);
    try {
      const response = await view.fetch(url, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: filterAbort.signal
      });
      checkReply(response, { read: true });
      const payload = await response.json().catch(() => null);
      if (!response.ok) throw new Error(payload?.message || `Projects could not be listed (${response.status}).`);
      if (request !== listRequest) return;
      // "In deck" comes from the deck as the page knows it now, not from when the server
      // answered: a reply that arrives after an Add must not offer those projects again
      // (cold review 2, M3).
      const inDeck = members();
      filterRows = (payload?.rows || []).map((row) => {
        const projectId = Number(row.projectId);
        return { ...row, projectId, isSelected: inDeck.has(projectId) };
      });
      renderFilterRows();
    } catch (error) {
      if (error.name === 'AbortError' || request !== listRequest) return;
      // No stale rows to tick under an error, and a way to try again (cold review 3, M3).
      filterRows = [];
      renderFilterRows();
      if (filterCount) filterCount.textContent = error.message || 'Projects could not be listed.';
      if (filterListRetry) filterListRetry.hidden = false;
    } finally {
      view.clearTimeout(finding);
    }
  };
  const reloadFilterRows = (delay = 0) => {
    view.clearTimeout(filterTimer);
    filterTimer = view.setTimeout(loadFilterRows, delay);
  };

  // After any change to the deck, mark the rows from its actual members; a ticked project that
  // is now in the deck is no longer ticked.
  const refreshMembership = (projects) => {
    const inDeck = new Set(projects.map((project) => Number(project.projectId)));
    inDeck.forEach((projectId) => ticked.delete(projectId));
    filterRows = filterRows.map((row) => ({ ...row, isSelected: inDeck.has(row.projectId) }));
    renderFilterRows();
  };

  const closePanel = (focusChip = true) => {
    const key = openFilter;
    openFilter = '';
    renderFilterChips();
    renderFilterPanel();
    if (focusChip) filterChips.querySelector(`[data-pbd-filter-open="${key}"]`)?.focus();
  };

  // Spaces or a retyped letter that leave the search as it was send nothing (cold review 4).
  let lastQuery = '';
  filterQuery?.addEventListener('input', () => {
    renderFilterChips();
    if (filterQuery.value.trim() === lastQuery) return;
    reloadFilterRows(300);
  });
  filterListRetry?.addEventListener('click', () => {
    filterListRetry.hidden = true;
    if (filterCount) filterCount.textContent = 'Finding projects…';
    filterQuery?.focus();
    loadFilterRows();
  });
  filterChips.addEventListener('click', (event) => {
    if (!(event.target instanceof view.Element)) return;
    if (event.target.closest('[data-pbd-filter-reset]')) {
      ['arpp', 'technicalCategory', 'projectCategory', 'status'].forEach((key) => filterState[key].clear());
      filterState.completedFrom = '';
      filterState.completedTo = '';
      filterState.proliferation = false;
      if (filterQuery) filterQuery.value = '';
      openFilter = '';
      renderFilterChips();
      renderFilterPanel();
      reloadFilterRows();
      filterQuery?.focus();
      return;
    }
    const clear = event.target.closest('[data-pbd-filter-clear]');
    if (clear) {
      const key = clear.dataset.pbdFilterClear;
      if (key === 'completed') { filterState.completedFrom = ''; filterState.completedTo = ''; }
      else filterState[key]?.clear?.();
      renderFilterChips();
      renderFilterPanel();
      reloadFilterRows();
      filterChips.querySelector(`[data-pbd-filter-open="${key}"]`)?.focus();
      return;
    }
    const open = event.target.closest('[data-pbd-filter-open]');
    if (!open || open.disabled) return;
    const key = open.dataset.pbdFilterOpen;
    if (key === 'proliferation') {
      filterState.proliferation = !filterState.proliferation;
      renderFilterChips();
      reloadFilterRows();
      filterChips.querySelector('[data-pbd-filter-open="proliferation"]')?.focus();
      return;
    }
    openFilter = openFilter === key ? '' : key;
    if (openFilter && optionsState === 'failed') loadFilterOptions();
    renderFilterChips();
    renderFilterPanel();
    // While its choices load the panel has nothing to focus: the chip keeps it (cold review 4),
    // and the choices take it when they arrive.
    if (openFilter) (filterPanel.querySelector('input, select, button') || filterChips.querySelector(`[data-pbd-filter-open="${key}"]`))?.focus();
    else filterChips.querySelector(`[data-pbd-filter-open="${key}"]`)?.focus();
  });
  filterPanel.addEventListener('click', (event) => {
    if (!(event.target instanceof view.Element)) return;
    if (event.target.closest('[data-pbd-filter-retry]')) { loadFilterOptions(); return; }
    if (event.target.closest('[data-pbd-filter-all-years]')) {
      showAllYears = true;
      renderFilterPanel();
      filterPanel.querySelector('[data-pbd-filter-range="completedFrom"]')?.focus();
      return;
    }
    if (event.target.closest('[data-pbd-filter-done]')) closePanel();
  });
  filterPanel.addEventListener('change', (event) => {
    const target = event.target;
    if (target instanceof view.HTMLInputElement && target.dataset.pbdFilterValue) {
      const values = filterState[target.dataset.pbdFilterValue];
      if (target.checked) values.add(target.value);
      else values.delete(target.value);
    } else if (target instanceof view.HTMLSelectElement && target.dataset.pbdFilterRange) {
      filterState[target.dataset.pbdFilterRange] = target.value;
      // A range chosen the wrong way round is put the right way round where it shows (cold
      // review 2), not only on the server.
      const { completedFrom: from, completedTo: to } = filterState;
      if (from && to && Number(from) > Number(to)) {
        filterState.completedFrom = to;
        filterState.completedTo = from;
        filterPanel.querySelector('[data-pbd-filter-range="completedFrom"]').value = to;
        filterPanel.querySelector('[data-pbd-filter-range="completedTo"]').value = from;
      }
    } else {
      return;
    }
    renderFilterChips();
    reloadFilterRows(120);
  });
  // Escape closes the open filter and returns to its chip, from the panel or from a chip, not the
  // dialog.
  const escapeClosesPanel = (event) => {
    if (event.key !== 'Escape' || !openFilter) return;
    event.preventDefault();
    event.stopPropagation();
    closePanel();
  };
  filterChips.addEventListener('keydown', escapeClosesPanel);
  filterPanel.addEventListener('keydown', escapeClosesPanel);

  filterResults.addEventListener('change', (event) => {
    const target = event.target;
    if (!(target instanceof view.HTMLInputElement) || !target.dataset.pbdFilterTick) return;
    const projectId = Number(target.dataset.pbdFilterTick);
    const row = filterRows.find((candidate) => candidate.projectId === projectId) || ticked.get(projectId);
    if (target.checked && row) ticked.set(projectId, row);
    else ticked.delete(projectId);
    if (showTicked) {
      // Unticked in the ticked view: it leaves the view, and focus moves to its neighbour.
      const item = target.closest('.pbd-filter__row');
      const next = item?.nextElementSibling?.querySelector('input') || item?.previousElementSibling?.querySelector('input');
      renderFilterRows();
      renderFilterChips();
      // With nothing left ticked the review ends and its button is disabled: the search takes
      // focus (cold review 4).
      (next
        ? filterResults.querySelector(`[data-pbd-filter-tick="${next.dataset.pbdFilterTick}"]`)
        : ticked.size ? filterTickedLabel : filterQuery)?.focus();
      return;
    }
    target.closest('.pbd-filter__row')?.classList.toggle('is-selected', target.checked);
    // Only the counts change: the list is not rebuilt, so focus stays on the tick.
    updateTickAll(filterRows.filter((candidate) => !candidate.isSelected));
    updateTickedFooter();
  });
  filterTickAll?.addEventListener('click', () => {
    const untick = filterTickAll.dataset.mode === 'untick';
    let fits = room() - ticked.size;
    filterRows.filter((row) => !row.isSelected).forEach((row) => {
      if (untick) ticked.delete(row.projectId);
      else if (!ticked.has(row.projectId) && fits > 0) { ticked.set(row.projectId, row); fits -= 1; }
    });
    renderFilterRows();
    // "Tick the first N that fit" is hidden once they are ticked: focus goes to the count of
    // ticked projects instead of nowhere (cold review 4).
    (filterTickAll.hidden ? (ticked.size ? filterTickedLabel : filterQuery) : filterTickAll)?.focus();
  });
  filterClear?.addEventListener('click', () => {
    ticked.clear();
    showTicked = false;
    renderFilterRows();
    renderFilterChips();
    filterQuery?.focus();
  });
  filterTickedLabel?.addEventListener('click', () => {
    if (ticked.size === 0) return;
    showTicked = !showTicked;
    openFilter = '';
    renderFilterRows();
    renderFilterChips();
    renderFilterPanel();
    filterTickedLabel.focus();
  });
  filterAdd?.addEventListener('click', () => {
    if (!filterAddForm || adding) return;
    if (ticked.size === 0) {
      setState(statusElement, 'Tick the projects to add first.', 'error');
      return;
    }
    // A deck holds at most 120 projects: say so before sending, with how many to untick.
    const left = room();
    if (ticked.size > left) {
      // The line under it already says how many fit and how many to untick.
      setState(statusElement, left === 0
        ? `Not added: this deck already holds ${maximumProjects} projects, the most a deck can hold.`
        : 'Not added: more projects are ticked than this deck has room for.', 'error');
      return;
    }
    // One Add at a time: a second click before this one is saved would send the same projects
    // again and report that nothing was added.
    adding = true;
    filterAdd.disabled = true;
    filterAdd.setAttribute('aria-busy', 'true');
    pendingAdd = new Map(ticked);
    filterAddForm.querySelectorAll('input[name="ProjectIds"]').forEach((input) => input.remove());
    ticked.forEach((_row, projectId) => {
      const input = doc.createElement('input');
      input.type = 'hidden';
      input.name = 'ProjectIds';
      input.value = String(projectId);
      filterAddForm.append(input);
    });
    filterAddForm.requestSubmit();
  });
  filterAddForm?.addEventListener('pbd:add-selection-settled', (event) => {
    adding = false;
    const payload = event.detail?.payload;
    // Projects the server could not add (archived, deleted or cancelled since the list was
    // loaded) are unticked and named, not left ticked to fail again (cold review 2, M4), also
    // when none could be added (cold review 3, M1).
    const skipped = (payload?.skippedProjectIds || []).map(Number);
    if (skipped.length) {
      const names = skipped.map((projectId) => pendingAdd.get(projectId)?.projectName).filter(Boolean);
      skipped.forEach((projectId) => ticked.delete(projectId));
      const note = doc.createElement('span');
      note.className = 'pbd-filter__skipped';
      // When none could be added the server's message already says why: only the names follow.
      // Otherwise the note comes before Undo, not after it (cold review 4).
      note.textContent = event.detail?.ok
        ? ` ${skipped.length === 1 ? 'One project was' : `${skipped.length} projects were`} not added: ${names.join(', ') || 'no longer available'} (archived, deleted or cancelled).`
        : names.length ? ` (${names.join(', ')})` : '';
      const undo = statusElement?.querySelector('button');
      if (undo) undo.before(note, ' ');
      else statusElement?.append(note);
      // They are no longer offered either.
      filterRows = filterRows.filter((row) => !skipped.includes(row.projectId));
      renderFilterRows();
    }
    if (!event.detail?.ok) pendingAdd = new Map();
    if (filterAdd) {
      filterAdd.disabled = false;
      filterAdd.removeAttribute('aria-busy');
      // The button was disabled while saving, which drops focus: put it back.
      if ([doc.body, modalElement].includes(doc.activeElement)) filterAdd.focus();
    }
    updateTickedFooter();
  });
  // Undo after an Add ticks those projects again, so the selection need not be rebuilt
  // (cold review 2).
  doc.addEventListener('pbd:add-undone', (event) => {
    const undone = (event.detail?.projectIds || []).map(Number);
    undone.forEach((projectId) => {
      const row = pendingAdd.get(projectId);
      if (row) ticked.set(projectId, { ...row, isSelected: false });
    });
    renderFilterRows();
  });
  filterResults.addEventListener('click', async (event) => {
    const button = event.target instanceof view.Element ? event.target.closest('[data-pbd-filter-remove]') : null;
    if (!button) return;
    const projectId = Number(button.dataset.pbdFilterRemove);
    const name = filterRows.find((row) => row.projectId === projectId)?.projectName
      || (getMembers?.() || []).find((project) => Number(project.projectId) === projectId)?.projectName
      || 'this project';
    // Removing deletes the project's place in this deck with its notes, photo choice and
    // position, and adding it back starts afresh: so it asks, rather than offering an Undo that
    // could not bring those back.
    const confirmed = await confirmRemoval({
      title: `Remove ${name}?`,
      message: 'It leaves this deck with its speaker notes, chosen photo and place in the order. Adding it again starts it afresh; the project itself is not changed.',
      confirmText: 'Remove project',
      cancelText: 'Keep',
      tone: 'danger',
      returnFocus: button
    });
    if (!confirmed) return;
    button.disabled = true;
    try {
      await updateMembership([], [projectId], statusElement);
      setState(statusElement, `Removed ${name}.`, 'saved');
      // The row is rebuilt as "not in the deck"; keep the keyboard on it.
      filterResults.querySelector(`[data-pbd-filter-tick="${projectId}"]`)?.focus();
    } catch {
      button.disabled = false;
    }
  });

  renderFilterChips();
  updateTickedFooter();
  // The choices load when the dialog first opens. The list loads on every opening (cold review 3,
  // M4), so projects created, archived or re-categorised since are shown as they are now. Ticks stay.
  const openDialog = () => {
    loadFilterOptions({ refresh: true });
    reloadFilterRows();
  };
  modalElement?.addEventListener('show.bs.modal', openDialog);

  return { refreshMembership, open: openDialog, reload: loadFilterRows };
}
