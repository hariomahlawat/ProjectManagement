(function () {
  const root = document.querySelector('[data-industry-partners-root]');
  if (!root) return;

  // SECTION: Shared helpers
  function debounce(fn, wait) {
    let timer;
    return function () {
      const args = arguments;
      clearTimeout(timer);
      timer = setTimeout(() => fn.apply(null, args), wait);
    };
  }

  // SECTION: Inline field autosave
  const token = document.querySelector('#industryPartnerToken input[name="__RequestVerificationToken"]')?.value;
  const statusEl = root.querySelector('[data-save-status]');
  const fields = root.querySelectorAll('[data-inline-field]');

  function setStatus(message, isError) {
    if (!statusEl) return;
    statusEl.textContent = message;
    statusEl.classList.toggle('text-danger', !!isError);
    statusEl.classList.toggle('text-success', !isError);
  }

  const saveField = debounce(async (fieldEl) => {
    const partnerId = fieldEl.getAttribute('data-partner-id');
    const field = fieldEl.getAttribute('data-inline-field');
    const value = fieldEl.value;

    const body = new URLSearchParams();
    body.set('id', partnerId);
    body.set('field', field);
    body.set('value', value);

    try {
      const response = await fetch('?handler=UpdateField', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
          'RequestVerificationToken': token || ''
        },
        body: body.toString()
      });

      if (!response.ok) {
        const payload = await response.json();
        setStatus(Object.values(payload.errors || {}).flat().join(' '), true);
        return;
      }

      setStatus('Saved', false);
      setTimeout(() => setStatus('', false), 1500);
    } catch (err) {
      setStatus('Save failed. Please retry.', true);
    }
  }, 400);

  fields.forEach((field) => {
    field.addEventListener('input', function () {
      setStatus('Saving...', false);
      saveField(field);
    });
  });

  // SECTION: Linked project typeahead
  const linkForm = root.querySelector('[data-link-project-form]');
  if (!linkForm) return;

  const searchInput = linkForm.querySelector('[data-project-search]');
  const projectIdInput = linkForm.querySelector('[data-project-id]');
  const resultsContainer = linkForm.querySelector('[data-project-results]');
  const errorMessage = linkForm.querySelector('[data-project-error]');
  const submitButton = linkForm.querySelector('[data-link-project-submit]');
  let lastSelectedLabel = '';
  let activeSearchController = null;
  let latestSearchRequestId = 0;

  function setSubmitEnabled() {
    if (!submitButton || !projectIdInput) return;
    submitButton.disabled = !projectIdInput.value;
  }

  function clearSelection() {
    if (!projectIdInput) return;
    projectIdInput.value = '';
    lastSelectedLabel = '';
    setSubmitEnabled();
  }

  function hideResults() {
    if (!resultsContainer) return;
    resultsContainer.classList.add('d-none');
    resultsContainer.innerHTML = '';
  }

  function showError(show) {
    if (!errorMessage) return;
    errorMessage.classList.toggle('d-none', !show);
  }

  function selectProject(item) {
    if (!searchInput || !projectIdInput) return;
    searchInput.value = item.name;
    projectIdInput.value = String(item.id);
    lastSelectedLabel = item.name;
    hideResults();
    showError(false);
    setSubmitEnabled();
  }

  function renderResults(items) {
    if (!resultsContainer) return;
    resultsContainer.innerHTML = '';

    if (!items || !items.length) {
      hideResults();
      return;
    }

    items.forEach((item) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'list-group-item list-group-item-action';
      button.textContent = item.name;
      button.addEventListener('click', () => selectProject(item));
      resultsContainer.appendChild(button);
    });

    resultsContainer.classList.remove('d-none');
  }

  const searchProjects = debounce(async () => {
    if (!searchInput) return;
    const query = searchInput.value.trim();

    if (query.length < 2) {
      // SECTION: Cancel pending typeahead request when query becomes too short
      if (activeSearchController) {
        activeSearchController.abort();
        activeSearchController = null;
      }
      hideResults();
      return;
    }

    // SECTION: Guard against out-of-order typeahead responses
    latestSearchRequestId += 1;
    const requestId = latestSearchRequestId;

    if (activeSearchController) {
      activeSearchController.abort();
    }
    activeSearchController = new AbortController();

    try {
      const response = await fetch(`/api/industry-partners/projects?q=${encodeURIComponent(query)}&take=20`, {
        headers: { 'Accept': 'application/json' },
        signal: activeSearchController.signal
      });

      if (requestId !== latestSearchRequestId || searchInput.value.trim() !== query) {
        return;
      }

      if (!response.ok) {
        hideResults();
        return;
      }

      const payload = await response.json();
      renderResults(payload.items || []);
    } catch (err) {
      if (err && err.name === 'AbortError') {
        return;
      }
      hideResults();
    } finally {
      if (requestId === latestSearchRequestId) {
        activeSearchController = null;
      }
    }
  }, 250);

  if (searchInput) {
    searchInput.addEventListener('input', () => {
      if (searchInput.value.trim() !== lastSelectedLabel) {
        clearSelection();
      }
      showError(false);
      searchProjects();
    });

    searchInput.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        hideResults();
      }
    });
  }

  linkForm.addEventListener('submit', (event) => {
    if (!projectIdInput || !projectIdInput.value) {
      event.preventDefault();
      showError(true);
      setSubmitEnabled();
      return;
    }

    showError(false);
  });

  document.addEventListener('click', (event) => {
    if (!linkForm.contains(event.target)) {
      hideResults();
    }
  });

  setSubmitEnabled();
})();
