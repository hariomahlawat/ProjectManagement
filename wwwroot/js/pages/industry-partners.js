(function () {
  const root = document.querySelector('[data-industry-partners-root]');
  if (!root) {
    return;
  }

  // SECTION: Shared helpers
  function debounce(fn, wait) {
    let timer;
    return function () {
      const args = arguments;
      clearTimeout(timer);
      timer = setTimeout(() => fn.apply(null, args), wait);
    };
  }

  function escapeHtml(value) {
    return String(value || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  // SECTION: Inline field autosave
  function initInlineAutosave() {
    const token = document.querySelector('#industryPartnerToken input[name="__RequestVerificationToken"]')?.value;
    const statusEl = root.querySelector('[data-save-status]');
    const fields = root.querySelectorAll('[data-inline-field]');

    if (!fields.length) {
      return;
    }

    function setStatus(message, isError) {
      if (!statusEl) {
        return;
      }

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
            RequestVerificationToken: token || ''
          },
          body: body.toString()
        });

        if (!response.ok) {
          const payload = await response.json();
          setStatus(Object.values(payload.errors || {}).flat().join(' '), true);
          return;
        }

        setStatus('Saved', false);
        setTimeout(() => setStatus('', false), 1400);
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
  }

  // SECTION: Linked project typeahead and validation
  function initProjectTypeahead() {
    const linkForm = root.querySelector('[data-link-project-form]');
    if (!linkForm) {
      return;
    }

    const searchInput = linkForm.querySelector('[data-project-search]');
    const projectIdInput = linkForm.querySelector('[data-project-id]');
    const resultsContainer = linkForm.querySelector('[data-project-results]');
    const errorMessage = linkForm.querySelector('[data-project-error]');
    const submitButton = linkForm.querySelector('[data-link-project-submit]');
    const existingLinksRoot = root.querySelector('[data-existing-links]');

    let lastSelectedLabel = '';
    let activeSearchController = null;
    let latestSearchRequestId = 0;
    let latestItems = [];
    let latestSuggestionIds = new Set();
    let activeIndex = -1;

    function setSubmitEnabled() {
      if (!submitButton || !projectIdInput) {
        return;
      }
      submitButton.disabled = !projectIdInput.value;
    }

    function clearSelection() {
      if (!projectIdInput) {
        return;
      }
      projectIdInput.value = '';
      lastSelectedLabel = '';
      setSubmitEnabled();
    }

    function showError(message) {
      if (!errorMessage) {
        return;
      }

      if (!message) {
        errorMessage.classList.add('d-none');
        errorMessage.textContent = '';
        return;
      }

      errorMessage.textContent = message;
      errorMessage.classList.remove('d-none');
    }

    function hideResults() {
      if (!resultsContainer) {
        return;
      }
      resultsContainer.classList.add('d-none');
      resultsContainer.innerHTML = '';
      activeIndex = -1;
      latestItems = [];
      searchInput?.setAttribute('aria-expanded', 'false');
    }

    function highlightLabel(text, query) {
      if (!query) {
        return escapeHtml(text);
      }

      const lowerLabel = text.toLowerCase();
      const lowerQuery = query.toLowerCase();
      const matchIndex = lowerLabel.indexOf(lowerQuery);
      if (matchIndex < 0) {
        return escapeHtml(text);
      }

      const before = text.slice(0, matchIndex);
      const match = text.slice(matchIndex, matchIndex + query.length);
      const after = text.slice(matchIndex + query.length);
      return `${escapeHtml(before)}<strong>${escapeHtml(match)}</strong>${escapeHtml(after)}`;
    }

    function setActiveItem(index) {
      const buttons = Array.from(resultsContainer?.querySelectorAll('.ip-typeahead-item[data-item-index]') || []);
      buttons.forEach((buttonEl) => {
        const isActive = Number(buttonEl.getAttribute('data-item-index')) === index;
        buttonEl.classList.toggle('is-active', isActive);
      });
      activeIndex = index;
    }

    function renderResults(items, query) {
      if (!resultsContainer) {
        return;
      }

      latestItems = items;
      latestSuggestionIds = new Set(items.map((item) => String(item.id)));
      activeIndex = -1;
      resultsContainer.innerHTML = '';

      if (!items.length) {
        const emptyState = document.createElement('div');
        emptyState.className = 'ip-typeahead-item';
        emptyState.textContent = 'No eligible projects found (only Development stage or Completed projects can be linked)';
        emptyState.setAttribute('aria-disabled', 'true');
        resultsContainer.appendChild(emptyState);
        resultsContainer.classList.remove('d-none');
        searchInput?.setAttribute('aria-expanded', 'true');
        return;
      }

      items.forEach((item, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'ip-typeahead-item';
        button.setAttribute('role', 'option');
        button.setAttribute('data-item-index', String(index));
        button.innerHTML = highlightLabel(item.name, query);
        button.addEventListener('click', () => selectProject(item));
        button.addEventListener('mouseenter', () => setActiveItem(index));
        resultsContainer.appendChild(button);
      });

      resultsContainer.classList.remove('d-none');
      searchInput?.setAttribute('aria-expanded', 'true');
    }

    function renderLoading() {
      if (!resultsContainer) {
        return;
      }
      resultsContainer.innerHTML = '<div class="ip-typeahead-item" aria-disabled="true">Searching...</div>';
      resultsContainer.classList.remove('d-none');
      searchInput?.setAttribute('aria-expanded', 'true');
    }


    // SECTION: Toast feedback
    function showToast(message, variant) {
      if (typeof bootstrap === 'undefined' || !bootstrap.Toast) {
        showError(message);
        return;
      }

      let toastHost = document.getElementById('industryPartnersToastHost');
      if (!toastHost) {
        toastHost = document.createElement('div');
        toastHost.id = 'industryPartnersToastHost';
        toastHost.className = 'toast-container position-fixed top-0 end-0 p-3';
        document.body.appendChild(toastHost);
      }

      const toastWrapper = document.createElement('div');
      toastWrapper.className = `toast align-items-center text-bg-${variant || 'warning'} border-0`;
      toastWrapper.setAttribute('role', 'status');
      toastWrapper.setAttribute('aria-live', 'polite');
      toastWrapper.setAttribute('aria-atomic', 'true');

      toastWrapper.innerHTML = `
        <div class="d-flex">
          <div class="toast-body">${escapeHtml(message)}</div>
          <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
        </div>`;

      toastHost.appendChild(toastWrapper);
      const toastInstance = bootstrap.Toast.getOrCreateInstance(toastWrapper, { autohide: true, delay: 5000 });
      toastWrapper.addEventListener('hidden.bs.toast', () => {
        toastInstance.dispose();
        toastWrapper.remove();
      }, { once: true });
      toastInstance.show();
    }

    function selectProject(item) {
      if (!searchInput || !projectIdInput) {
        return;
      }

      searchInput.value = item.name;
      projectIdInput.value = String(item.id);
      lastSelectedLabel = item.name;
      hideResults();
      showError('');
      setSubmitEnabled();
    }

    function isDuplicateProject(projectId) {
      if (!existingLinksRoot) {
        return false;
      }

      return !!existingLinksRoot.querySelector(`[data-project-id="${projectId}"]`);
    }

    const searchProjects = debounce(async () => {
      if (!searchInput) {
        return;
      }

      const query = searchInput.value.trim();
      if (query.length < 2) {
        if (activeSearchController) {
          activeSearchController.abort();
          activeSearchController = null;
        }
        hideResults();
        return;
      }

      latestSearchRequestId += 1;
      const requestId = latestSearchRequestId;

      if (activeSearchController) {
        activeSearchController.abort();
      }

      activeSearchController = new AbortController();
      renderLoading();

      try {
        const response = await fetch(`/api/industry-partners/projects?q=${encodeURIComponent(query)}&take=20`, {
          headers: { Accept: 'application/json' },
          signal: activeSearchController.signal
        });

        if (requestId !== latestSearchRequestId || searchInput.value.trim() !== query) {
          return;
        }

        if (!response.ok) {
          hideResults();
          showError('Unable to fetch projects right now.');
          return;
        }

        const payload = await response.json();
        renderResults(payload.items || [], query);
      } catch (err) {
        if (err && err.name === 'AbortError') {
          return;
        }

        hideResults();
        showError('Unable to fetch projects right now.');
      } finally {
        if (requestId === latestSearchRequestId) {
          activeSearchController = null;
        }
      }
    }, 250);

    searchInput?.addEventListener('input', () => {
      if (searchInput.value.trim() !== lastSelectedLabel) {
        clearSelection();
      }

      showError('');
      searchProjects();
    });

    searchInput?.addEventListener('keydown', (event) => {
      const resultsVisible = resultsContainer && !resultsContainer.classList.contains('d-none');

      if (event.key === 'ArrowDown' && resultsVisible && latestItems.length) {
        event.preventDefault();
        const nextIndex = activeIndex < latestItems.length - 1 ? activeIndex + 1 : 0;
        setActiveItem(nextIndex);
        return;
      }

      if (event.key === 'ArrowUp' && resultsVisible && latestItems.length) {
        event.preventDefault();
        const nextIndex = activeIndex > 0 ? activeIndex - 1 : latestItems.length - 1;
        setActiveItem(nextIndex);
        return;
      }

      if (event.key === 'Enter' && resultsVisible && activeIndex >= 0 && latestItems[activeIndex]) {
        event.preventDefault();
        selectProject(latestItems[activeIndex]);
        return;
      }

      if (event.key === 'Escape') {
        hideResults();
      }
    });

    linkForm.addEventListener('submit', (event) => {
      const selectedProjectId = projectIdInput?.value;

      if (!selectedProjectId) {
        event.preventDefault();
        showError('Select a project from the list.');
        setSubmitEnabled();
        return;
      }

      if (isDuplicateProject(selectedProjectId)) {
        event.preventDefault();
        showError('This project is already linked to the selected partner.');
        return;
      }

      if (!latestSuggestionIds.has(String(selectedProjectId))) {
        event.preventDefault();
        const eligibilityMessage = 'Only projects in Development stage or Completed can be linked.';
        showError(eligibilityMessage);
        showToast(eligibilityMessage, 'warning');
        clearSelection();
        return;
      }

      showError('');
    });

    document.addEventListener('click', (event) => {
      if (!linkForm.contains(event.target)) {
        hideResults();
      }
    });

    setSubmitEnabled();
  }

  // SECTION: Contact forms validation
  function initContactValidation() {
    const addContactForm = root.querySelector('[data-add-contact-form]');
    if (!addContactForm) {
      return;
    }

    const phoneInput = addContactForm.querySelector('[data-contact-phone]');
    const emailInput = addContactForm.querySelector('[data-contact-email]');
    const errorEl = addContactForm.querySelector('[data-contact-error]');

    function toggleError(show) {
      if (!errorEl) {
        return;
      }

      errorEl.classList.toggle('d-none', !show);
      phoneInput?.classList.toggle('is-invalid', show && !phoneInput.value.trim());
      emailInput?.classList.toggle('is-invalid', show && !emailInput.value.trim());
    }

    function sanitizePhone() {
      if (!phoneInput) {
        return;
      }

      const digitsOnly = (phoneInput.value || '').replace(/[^\d+]/g, '');
      phoneInput.value = digitsOnly;
    }

    phoneInput?.addEventListener('input', () => {
      if (phoneInput.value.trim() || emailInput?.value.trim()) {
        toggleError(false);
      }
    });

    emailInput?.addEventListener('input', () => {
      if (phoneInput?.value.trim() || emailInput.value.trim()) {
        toggleError(false);
      }
    });

    addContactForm.addEventListener('submit', (event) => {
      sanitizePhone();

      const hasPhone = !!phoneInput?.value.trim();
      const hasEmail = !!emailInput?.value.trim();

      if (!hasPhone && !hasEmail) {
        event.preventDefault();
        toggleError(true);
        return;
      }

      toggleError(false);
    });
  }

  // SECTION: Delete confirmations
  function initDeleteConfirmations() {
    const deleteTrigger = root.querySelector('[data-delete-partner-trigger]');
    const deleteMessage = root.querySelector('[data-delete-partner-message]');
    const deleteSubmit = root.querySelector('[data-delete-partner-submit]');

    if (deleteTrigger && deleteMessage && deleteSubmit) {
      deleteTrigger.addEventListener('click', () => {
        const linkedCount = Number(deleteTrigger.getAttribute('data-linked-project-count') || '0');
        const partnerName = deleteTrigger.getAttribute('data-partner-name') || 'this partner';

        if (linkedCount > 0) {
          deleteMessage.textContent = `${partnerName} cannot be deleted while linked projects exist. Unlink ${linkedCount} project(s) before deleting.`;
          deleteSubmit.disabled = true;
          return;
        }

        deleteMessage.textContent = 'This will remove the partner and all contacts and attachments. This action cannot be undone.';
        deleteSubmit.disabled = false;
      });
    }

    const contactDeleteForms = root.querySelectorAll('[data-contact-delete-form]');
    contactDeleteForms.forEach((form) => {
      const button = form.querySelector('button[data-confirm]');
      if (!button || button.getAttribute('data-confirm') !== 'true') {
        return;
      }

      form.addEventListener('submit', (event) => {
        const confirmed = window.confirm('Delete this contact? This action cannot be undone.');
        if (!confirmed) {
          event.preventDefault();
        }
      });
    });
  }

  // SECTION: Attachment upload state handling
  function initAttachmentUpload() {
    const uploadForm = root.querySelector('[data-attachment-upload-form]');
    if (!uploadForm) {
      return;
    }

    const fileInput = uploadForm.querySelector('[data-attachment-file]');
    const fileName = uploadForm.querySelector('[data-attachment-file-name]');
    const submitButton = uploadForm.querySelector('[data-attachment-upload-submit]');

    function syncUploadState() {
      const hasFile = !!fileInput?.files?.length;
      submitButton.disabled = !hasFile;
      fileName.textContent = hasFile ? fileInput.files[0].name : 'No file selected';
    }

    fileInput?.addEventListener('change', syncUploadState);

    uploadForm.addEventListener('submit', () => {
      submitButton.disabled = true;
      submitButton.textContent = 'Uploading...';
    });

    syncUploadState();
  }

  initInlineAutosave();
  initProjectTypeahead();
  initContactValidation();
  initDeleteConfirmations();
  initAttachmentUpload();
})();
