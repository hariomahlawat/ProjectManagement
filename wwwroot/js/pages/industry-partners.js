(() => {
  'use strict';

  const root = document.querySelector('[data-industry-partners-root]');
  if (!root) return;

  const debounce = (fn, wait) => {
    let timer;
    return (...args) => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => fn(...args), wait);
    };
  };

  const escapeSelector = (value) => window.CSS?.escape ? window.CSS.escape(String(value)) : String(value).replace(/[^a-zA-Z0-9_-]/g, '\\$&');

  const escapeHtml = (value) => String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

  function closeContactEditors(exceptId = null) {
    root.querySelectorAll('[data-contact-edit]').forEach((editor) => {
      const id = editor.dataset.contactEdit;
      if (exceptId !== null && String(id) === String(exceptId)) return;
      editor.classList.add('d-none');
      root.querySelector(`[data-contact-read="${escapeSelector(id)}"]`)?.classList.remove('d-none');
    });
  }

  function syncContactToolbar() {
    const panel = root.querySelector('[data-collapsible-panel="contact-add-panel"]');
    const contactsTab = panel?.closest('[data-tab-panel="contacts"]');
    const addFormOpen = Boolean(panel && !panel.classList.contains('d-none') && contactsTab?.classList.contains('is-active'));
    const editorOpen = Boolean(root.querySelector('[data-contact-edit]:not(.d-none)'));

    root.querySelectorAll('[data-contact-toolbar-action]').forEach((button) => {
      button.classList.toggle('d-none', addFormOpen || editorOpen);
      button.disabled = addFormOpen || editorOpen;
    });
  }

  const setPanelVisible = (panelName, visible) => {
    const panel = root.querySelector(`[data-collapsible-panel="${escapeSelector(panelName)}"]`);
    if (!panel) return;

    panel.classList.toggle('d-none', !visible);
    root.querySelectorAll(`[data-toggle-panel="${escapeSelector(panelName)}"]`).forEach((button) => {
      button.setAttribute('aria-expanded', visible ? 'true' : 'false');
    });

    if (panelName === 'project-add-panel') {
      root.querySelectorAll('[data-project-toolbar-action]').forEach((button) => {
        button.classList.toggle('d-none', visible);
      });
    }

    if (panelName === 'contact-add-panel') {
      if (visible) closeContactEditors();
      syncContactToolbar();
    }

    if (visible) {
      const focusTarget = panel.querySelector('input:not([type="hidden"]), select, textarea, button');
      window.setTimeout(() => focusTarget?.focus(), 80);
    }
  };

  function initDrawer() {
    const drawerElement = root.querySelector('[data-partner-drawer]');
    if (!drawerElement || typeof bootstrap === 'undefined' || !bootstrap.Offcanvas) return;

    const drawer = bootstrap.Offcanvas.getOrCreateInstance(drawerElement, {
      backdrop: true,
      keyboard: true,
      scroll: false
    });

    if (drawerElement.dataset.openDrawer === 'true') {
      drawer.show();
    }

    drawerElement.addEventListener('hidden.bs.offcanvas', () => {
      const closeUrl = drawerElement.dataset.closeUrl;
      if (closeUrl) {
        window.history.replaceState({}, '', closeUrl);
      }
    });
  }

  function activateTab(tabName, updateUrl = true) {
    const safeTab = ['overview', 'contacts', 'projects', 'files'].includes(tabName) ? tabName : 'overview';

    root.querySelectorAll('[data-tab-target]').forEach((button) => {
      const active = button.dataset.tabTarget === safeTab;
      button.classList.toggle('is-active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });

    root.querySelectorAll('[data-tab-panel]').forEach((panel) => {
      panel.classList.toggle('is-active', panel.dataset.tabPanel === safeTab);
    });

    if (safeTab !== 'projects') {
      setPanelVisible('project-add-panel', false);
    }
    if (safeTab !== 'contacts') {
      setPanelVisible('contact-add-panel', false);
      closeContactEditors();
    }
    syncContactToolbar();

    if (updateUrl) {
      const url = new URL(window.location.href);
      url.searchParams.set('tab', safeTab);
      if (safeTab !== 'overview') url.searchParams.delete('edit');
      window.history.replaceState({}, '', url);
    }
  }

  function initTabsAndPanels() {
    root.querySelectorAll('[data-tab-target]').forEach((button) => {
      button.addEventListener('click', () => activateTab(button.dataset.tabTarget || 'overview'));
    });

    root.querySelectorAll('[data-open-tab]').forEach((button) => {
      button.addEventListener('click', () => {
        const tab = button.dataset.openTab || 'overview';
        activateTab(tab);
        if (button.dataset.showPanel) {
          setPanelVisible(button.dataset.showPanel, true);
        }
      });
    });

    root.querySelectorAll('[data-toggle-panel]').forEach((button) => {
      button.addEventListener('click', () => {
        const name = button.dataset.togglePanel;
        const panel = name ? root.querySelector(`[data-collapsible-panel="${escapeSelector(name)}"]`) : null;
        if (name && panel) setPanelVisible(name, panel.classList.contains('d-none'));
      });
    });

    root.querySelectorAll('[data-hide-panel]').forEach((button) => {
      button.addEventListener('click', () => {
        if (button.dataset.hidePanel) setPanelVisible(button.dataset.hidePanel, false);
      });
    });
  }

  function initInlineEditors() {
    root.querySelectorAll('[data-edit-contact]').forEach((button) => {
      button.addEventListener('click', () => {
        const id = button.dataset.editContact;
        setPanelVisible('contact-add-panel', false);
        closeContactEditors(id);
        root.querySelector(`[data-contact-read="${escapeSelector(id)}"]`)?.classList.add('d-none');
        const editor = root.querySelector(`[data-contact-edit="${escapeSelector(id)}"]`);
        editor?.classList.remove('d-none');
        syncContactToolbar();
        editor?.querySelector('input:not([type="hidden"])')?.focus();
      });
    });

    root.querySelectorAll('[data-cancel-contact]').forEach((button) => {
      button.addEventListener('click', () => {
        const id = button.dataset.cancelContact;
        root.querySelector(`[data-contact-read="${escapeSelector(id)}"]`)?.classList.remove('d-none');
        root.querySelector(`[data-contact-edit="${escapeSelector(id)}"]`)?.classList.add('d-none');
        syncContactToolbar();
      });
    });

    document.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') return;
      closeContactEditors();
      setPanelVisible('contact-add-panel', false);
      syncContactToolbar();
    });

    syncContactToolbar();
  }

  function initContactValidation() {
    root.querySelectorAll('[data-contact-form]').forEach((form) => {
      const phone = form.querySelector('[data-contact-phone]');
      const email = form.querySelector('[data-contact-email]');
      const error = form.querySelector('[data-contact-error]');

      const validate = () => {
        const hasPhoneOrEmail = Boolean(phone?.value.trim() || email?.value.trim());
        const optional = form.dataset.contactOptional === 'true';
        const contactName = form.querySelector('[name="contactName"]');
        const hasAnyContactValue = Boolean(contactName?.value.trim() || phone?.value.trim() || email?.value.trim());
        const valid = hasPhoneOrEmail || (optional && !hasAnyContactValue);
        error?.classList.toggle('d-none', valid);
        phone?.classList.toggle('is-invalid', !valid);
        email?.classList.toggle('is-invalid', !valid);
        return valid;
      };

      phone?.addEventListener('input', () => {
        if (phone.value.trim() || email?.value.trim()) validate();
      });
      email?.addEventListener('input', () => {
        if (phone?.value.trim() || email.value.trim()) validate();
      });
      form.addEventListener('submit', (event) => {
        if (!validate()) {
          event.preventDefault();
          (phone || email)?.focus();
        }
      });
    });
  }

  function initConfirmationsAndSubmissionState() {
    root.querySelectorAll('[data-confirm-form]').forEach((form) => {
      form.addEventListener('submit', (event) => {
        const message = form.dataset.confirmForm || 'Continue with this action?';
        if (!window.confirm(message)) event.preventDefault();
      });
    });

    root.querySelectorAll('form').forEach((form) => {
      form.addEventListener('submit', (event) => {
        if (event.defaultPrevented) return;
        const submit = form.querySelector('[data-submit-once]');
        if (!submit || submit.disabled) return;
        submit.disabled = true;
        submit.dataset.originalText = submit.textContent || '';
        submit.textContent = 'Saving…';
      });
    });
  }

  function initDirectorySearch() {
    const form = root.querySelector('[data-directory-search-form]');
    const input = root.querySelector('[data-directory-search]');
    if (!form || !input) return;

    let initialValue = input.value;
    const submitSearch = debounce(() => {
      if (input.value === initialValue) return;
      form.requestSubmit();
    }, 450);

    input.addEventListener('input', submitSearch);
  }

  function initDuplicateSuggestions() {
    const input = root.querySelector('[data-duplicate-name-input]');
    const container = root.querySelector('[data-duplicate-suggestions]');
    if (!input || !container) return;

    let controller;
    const load = debounce(async () => {
      const name = input.value.trim();
      if (name.length < 3) {
        container.classList.add('d-none');
        container.innerHTML = '';
        return;
      }

      controller?.abort();
      controller = new AbortController();

      try {
        const response = await fetch(`?handler=DuplicateSuggestions&name=${encodeURIComponent(name)}`, {
          headers: { Accept: 'application/json' },
          signal: controller.signal
        });
        if (!response.ok) return;

        const payload = await response.json();
        const items = payload.items || [];
        if (!items.length) {
          container.classList.add('d-none');
          container.innerHTML = '';
          return;
        }

        const links = items.map((item) => {
          const url = new URL(window.location.href);
          url.searchParams.set('id', String(item.id));
          url.searchParams.set('tab', 'overview');
          url.searchParams.delete('edit');
          const details = [item.location || 'Location not added', `${item.contactCount} contacts`, `${item.projectCount} projects`].join(' · ');
          return `
            <a class="ip-duplicate-item" href="${escapeHtml(url.toString())}">
              <span><span>${escapeHtml(item.name)}</span><small>${escapeHtml(details)}</small></span>
              <i class="bi bi-arrow-right" aria-hidden="true"></i>
            </a>`;
        }).join('');

        container.innerHTML = `<strong>Possible existing organisations</strong>${links}`;
        container.classList.remove('d-none');
      } catch (error) {
        if (error?.name !== 'AbortError') {
          container.classList.add('d-none');
        }
      }
    }, 280);

    input.addEventListener('input', load);
  }

  function initProjectPicker() {
    const form = root.querySelector('[data-project-association-form]');
    if (!form) return;

    const input = form.querySelector('[data-project-search]');
    const idInput = form.querySelector('[data-project-id]');
    const results = form.querySelector('[data-project-results]');
    const error = form.querySelector('[data-project-error]');
    const submit = form.querySelector('[data-project-link-submit]');

    if (!input || !results || !idInput) return;

    let controller;
    let items = [];
    let activeIndex = -1;
    let selectedLabel = '';

    const setSubmitState = () => {
      if (submit) submit.disabled = !idInput.value;
    };

    const hideResults = () => {
      results.classList.add('d-none');
      results.innerHTML = '';
      input.setAttribute('aria-expanded', 'false');
      items = [];
      activeIndex = -1;
    };

    const render = (data) => {
      items = data;
      activeIndex = -1;
      if (!data.length) {
        results.innerHTML = '<div class="ip-typeahead-item" aria-disabled="true"><span class="ip-typeahead-name">No projects found</span><span class="ip-typeahead-meta">Completed and archived projects are included in the search.</span></div>';
      } else {
        results.innerHTML = data.map((item, index) => `
          <button type="button" class="ip-typeahead-item" data-project-option="${index}" role="option">
            <span class="ip-typeahead-name">${escapeHtml(item.name)}</span>
            <span class="ip-typeahead-meta">${escapeHtml([item.caseFileNumber, item.statusLabel].filter(Boolean).join(' · '))}</span>
          </button>`).join('');
      }
      results.classList.remove('d-none');
      input.setAttribute('aria-expanded', 'true');
    };

    const select = (item) => {
      idInput.value = String(item.id);
      input.value = item.name;
      selectedLabel = item.name;
      error?.classList.add('d-none');
      hideResults();
      setSubmitState();
    };

    results.addEventListener('click', (event) => {
      const option = event.target.closest('[data-project-option]');
      if (!option) return;
      const item = items[Number(option.dataset.projectOption)];
      if (item) select(item);
    });

    const search = debounce(async () => {
      const query = input.value.trim();
      if (query.length < 2) {
        hideResults();
        return;
      }

      controller?.abort();
      controller = new AbortController();
      results.innerHTML = '<div class="ip-typeahead-item" aria-disabled="true"><span class="ip-typeahead-name">Searching…</span></div>';
      results.classList.remove('d-none');

      try {
        const response = await fetch(`/api/industry-partners/projects?q=${encodeURIComponent(query)}&take=25`, {
          headers: { Accept: 'application/json' },
          signal: controller.signal
        });
        if (!response.ok) throw new Error('Project search failed.');
        const payload = await response.json();
        render(payload.items || []);
      } catch (searchError) {
        if (searchError?.name === 'AbortError') return;
        error.textContent = 'Unable to search projects right now.';
        error.classList.remove('d-none');
        hideResults();
      }
    }, 240);

    input.addEventListener('input', () => {
      if (input.value.trim() !== selectedLabel) {
        idInput.value = '';
        selectedLabel = '';
        setSubmitState();
      }
      error?.classList.add('d-none');
      search();
    });

    input.addEventListener('keydown', (event) => {
      if (results.classList.contains('d-none') || !items.length) {
        if (event.key === 'Escape') hideResults();
        return;
      }

      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        activeIndex = event.key === 'ArrowDown'
          ? (activeIndex + 1) % items.length
          : (activeIndex <= 0 ? items.length - 1 : activeIndex - 1);
        results.querySelectorAll('[data-project-option]').forEach((option, index) => {
          option.classList.toggle('is-active', index === activeIndex);
        });
      } else if (event.key === 'Enter' && activeIndex >= 0) {
        event.preventDefault();
        select(items[activeIndex]);
      } else if (event.key === 'Escape') {
        hideResults();
      }
    });

    form.addEventListener('submit', (event) => {
      if (!idInput.value) {
        event.preventDefault();
        error.textContent = 'Select a project from the search results.';
        error.classList.remove('d-none');
        input.focus();
        return;
      }

      if (submit) {
        submit.disabled = true;
        submit.textContent = 'Linking…';
      }
    });

    document.addEventListener('click', (event) => {
      if (!form.contains(event.target)) hideResults();
    });

    setSubmitState();
  }

  function initFileUpload() {
    const form = root.querySelector('[data-attachment-upload-form]');
    if (!form) return;

    const input = form.querySelector('[data-attachment-file]');
    const label = form.querySelector('[data-attachment-file-name]');
    const submit = form.querySelector('[data-attachment-upload-submit]');
    const drop = form.querySelector('[data-file-drop]');
    if (!input || !label || !submit) return;

    const sync = () => {
      const file = input.files?.[0];
      label.textContent = file ? file.name : 'Choose a brochure, catalogue or company document';
      submit.disabled = !file;
    };

    input.addEventListener('change', sync);
    drop?.addEventListener('dragover', (event) => {
      event.preventDefault();
      drop.classList.add('is-dragover');
    });
    drop?.addEventListener('dragleave', () => drop.classList.remove('is-dragover'));
    drop?.addEventListener('drop', (event) => {
      event.preventDefault();
      drop.classList.remove('is-dragover');
      if (!event.dataTransfer?.files?.length) return;
      const transfer = new DataTransfer();
      transfer.items.add(event.dataTransfer.files[0]);
      input.files = transfer.files;
      sync();
    });
    form.addEventListener('submit', (event) => {
      if (event.defaultPrevented) return;
      submit.disabled = true;
      submit.textContent = 'Uploading…';
    });

    sync();
  }

  function initModalFocus() {
    const modal = document.getElementById('addOrganisationModal');
    modal?.addEventListener('shown.bs.modal', () => {
      modal.querySelector('[data-duplicate-name-input]')?.focus();
    });
  }

  initDrawer();
  initTabsAndPanels();
  initInlineEditors();
  initContactValidation();
  initConfirmationsAndSubmissionState();
  initDirectorySearch();
  initDuplicateSuggestions();
  initProjectPicker();
  initFileUpload();
  initModalFocus();
})();
