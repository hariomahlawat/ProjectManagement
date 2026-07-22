const root = document.querySelector('[data-pbd-root]');

if (root) {
  const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  const deckElement = root.querySelector('[data-deck-id]');
  const deckId = Number(deckElement?.dataset.deckId || 0);

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
    const payload = contentType.includes('application/json')
      ? await response.json()
      : null;

    if (!response.ok) {
      throw new Error(payload?.message || payload?.title || `Request failed (${response.status}).`);
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

  // Open settings automatically for an empty collection without relying on a rendered boolean attribute.
  const settings = root.querySelector('[data-pbd-open-settings="true"]');
  if (settings instanceof HTMLDetailsElement) settings.open = true;

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
  };

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

  // Individual project search and multi-selection.
  const searchInput = root.querySelector('[data-pbd-project-search]');
  const searchResults = root.querySelector('[data-pbd-search-results]');
  const searchStatus = root.querySelector('[data-pbd-search-status]');
  const selectedCount = root.querySelector('[data-pbd-selected-count]');
  const selectedInputs = root.querySelector('[data-pbd-selected-inputs]');
  const addSelectedButton = root.querySelector('[data-pbd-add-individual]');
  const selectedProjects = new Map();
  let searchTimer = 0;
  let searchAbortController = null;

  const escapeText = (value) => String(value ?? '');

  const renderSelectedInputs = () => {
    if (selectedInputs) {
      selectedInputs.replaceChildren(...[...selectedProjects.keys()].map((projectId) => {
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'ProjectIds';
        input.value = String(projectId);
        return input;
      }));
    }
    const count = selectedProjects.size;
    if (selectedCount) selectedCount.textContent = `${count} project${count === 1 ? '' : 's'} selected`;
    if (addSelectedButton) addSelectedButton.disabled = count === 0;
  };

  const toggleProject = (project, checked) => {
    if (checked) selectedProjects.set(project.projectId, project);
    else selectedProjects.delete(project.projectId);
    renderSelectedInputs();
    searchResults?.querySelectorAll('[data-project-result]').forEach((node) => {
      const id = Number(node.dataset.projectResult);
      const active = selectedProjects.has(id);
      node.classList.toggle('is-selected', active);
      const checkbox = node.querySelector('input[type="checkbox"]');
      if (checkbox) checkbox.checked = active;
    });
  };

  const renderSearchResults = (rows) => {
    if (!searchResults) return;
    searchResults.replaceChildren();
    if (!rows.length) {
      setState(searchStatus, 'No projects match this search.');
      return;
    }

    rows.forEach((project) => {
      const label = document.createElement('label');
      label.className = 'pbd-search-result';
      label.dataset.projectResult = String(project.projectId);

      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.checked = selectedProjects.has(project.projectId);
      checkbox.addEventListener('change', () => toggleProject(project, checkbox.checked));

      const body = document.createElement('span');
      const name = document.createElement('strong');
      name.textContent = escapeText(project.projectName);
      const meta = document.createElement('small');
      const parts = [project.lifecycle, project.presentStage, project.projectCategory, project.technicalCategory, project.projectOfficer]
        .filter(Boolean);
      meta.textContent = parts.join(' · ');
      const ref = document.createElement('small');
      ref.textContent = project.caseFileNumber ? `Ref: ${project.caseFileNumber}` : 'No case-file reference';
      body.append(name, meta, ref);
      label.append(checkbox, body);
      label.classList.toggle('is-selected', checkbox.checked);
      searchResults.append(label);
    });
    setState(searchStatus, `${rows.length} matching project${rows.length === 1 ? '' : 's'} shown.`);
  };

  const searchProjects = async () => {
    const query = searchInput?.value.trim() || '';
    if (query.length < 2) {
      searchAbortController?.abort();
      searchResults?.replaceChildren();
      setState(searchStatus, 'Enter at least two characters.');
      return;
    }

    searchAbortController?.abort();
    searchAbortController = new AbortController();
    setState(searchStatus, 'Searching…');
    try {
      const url = new URL(root.dataset.searchUrl, window.location.origin);
      url.searchParams.set('query', query);
      const response = await fetch(url, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: searchAbortController.signal
      });
      if (!response.ok) throw new Error(`Search failed (${response.status}).`);
      renderSearchResults(await response.json());
    } catch (error) {
      if (error.name === 'AbortError') return;
      setState(searchStatus, error.message || 'Project search could not be completed.', 'error');
    }
  };

  searchInput?.addEventListener('input', () => {
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(searchProjects, 260);
  });

  // Persist drag-and-drop and keyboard-accessible slide order.
  const sortableBody = root.querySelector('[data-pbd-sortable]');
  const sortStatus = root.querySelector('[data-pbd-sort-status]');
  const saveProjectOrder = async () => {
    if (!sortableBody || deckId <= 0) return;
    const projectIds = [...sortableBody.querySelectorAll('[data-project-id]')]
      .map((row) => Number(row.dataset.projectId))
      .filter(Number.isInteger);
    setState(sortStatus, 'Saving slide order…', 'saving');
    try {
      const payload = await requestJson(root.dataset.reorderUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, projectIds, rowVersion: currentRowVersion() })
      });
      updateRowVersion(payload?.rowVersion);
      setState(sortStatus, 'Slide order saved.', 'saved');
    } catch (error) {
      setState(sortStatus, `${error.message} Reload to restore the saved order.`, 'error');
    }
  };

  if (sortableBody && window.Sortable && deckId > 0) {
    window.Sortable.create(sortableBody, {
      animation: 130,
      handle: '.pbd-drag',
      ghostClass: 'pbd-sort-ghost',
      chosenClass: 'pbd-sort-chosen',
      onStart: () => setState(sortStatus, 'Reordering…', 'saving'),
      onEnd: saveProjectOrder
    });
  }

  sortableBody?.querySelectorAll('.pbd-drag').forEach((handle) => {
    handle.addEventListener('keydown', async (event) => {
      if (!['ArrowUp', 'ArrowDown'].includes(event.key)) return;
      const row = handle.closest('[data-project-id]');
      if (!row) return;
      const target = event.key === 'ArrowUp' ? row.previousElementSibling : row.nextElementSibling;
      if (!(target instanceof HTMLTableRowElement)) return;
      event.preventDefault();
      if (event.key === 'ArrowUp') sortableBody.insertBefore(row, target);
      else sortableBody.insertBefore(target, row);
      handle.focus();
      await saveProjectOrder();
    });
  });

  // Briefing-specific project description editor.
  const descriptionModalElement = document.getElementById('pbd-description-modal');
  const descriptionModal = descriptionModalElement && window.bootstrap
    ? window.bootstrap.Modal.getOrCreateInstance(descriptionModalElement)
    : null;
  const descriptionForm = descriptionModalElement?.querySelector('[data-pbd-description-form]');
  const descriptionProjectId = descriptionModalElement?.querySelector('[data-pbd-description-project-id]');
  const descriptionValue = descriptionModalElement?.querySelector('[data-pbd-description-value]');
  const descriptionTitle = descriptionModalElement?.querySelector('[data-pbd-description-title]');
  const descriptionStatus = descriptionModalElement?.querySelector('[data-pbd-description-status]');

  root.querySelectorAll('[data-pbd-edit-description]').forEach((button) => {
    button.addEventListener('click', () => {
      if (descriptionProjectId) descriptionProjectId.value = button.dataset.projectId || '';
      if (descriptionValue) descriptionValue.value = button.dataset.description || '';
      if (descriptionTitle) descriptionTitle.textContent = button.dataset.projectName || 'Brief description';
      setState(descriptionStatus, '');
      descriptionModal?.show();
      window.setTimeout(() => descriptionValue?.focus(), 180);
    });
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
        body: JSON.stringify({
          deckId,
          projectId,
          value: descriptionValue?.value || null,
          rowVersion: currentRowVersion()
        })
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
  const generateButton = root.querySelector('[data-pbd-generate]');
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
      setState(generateStatus, slideCount
        ? `PowerPoint generated successfully — ${slideCount} slides.`
        : 'PowerPoint generated successfully.', 'success');
    } catch (error) {
      setState(generateStatus, error.message || 'The PowerPoint deck could not be generated.', 'error');
    } finally {
      generateButton.disabled = false;
      generateLabel?.classList.remove('d-none');
      generateProgress?.classList.add('d-none');
    }
  });

  // Focus the first field when creating a deck.
  document.getElementById('pbd-new-deck-modal')?.addEventListener('shown.bs.modal', () => {
    document.querySelector('[data-pbd-new-name]')?.focus();
  });
}
