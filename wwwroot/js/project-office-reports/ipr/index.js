(() => {
  'use strict';

  const normalizeText = value => (value || '')
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();

  const focusFirstFormError = container => {
    if (!container) return;

    const projectError = container.querySelector('[data-valmsg-for="Input.ProjectId"].field-validation-error');
    if (projectError && projectError.textContent.trim()) {
      const projectSearch = container.querySelector('[data-ipr-project-search]');
      projectSearch?.scrollIntoView?.({ block: 'center' });
      projectSearch?.focus?.({ preventScroll: true });
      return;
    }

    const fieldMessage = Array.from(container.querySelectorAll('[data-valmsg-for].field-validation-error'))
      .find(message => message.textContent.trim());
    if (fieldMessage) {
      const fieldName = fieldMessage.getAttribute('data-valmsg-for');
      if (fieldName) {
        const escapedName = typeof CSS !== 'undefined' && typeof CSS.escape === 'function'
          ? CSS.escape(fieldName)
          : fieldName.replace(/([\.\[\]])/g, '\\$1');
        const field = container.querySelector(`[name="${escapedName}"]`);
        field?.scrollIntoView?.({ block: 'center' });
        field?.focus?.({ preventScroll: true });
        if (field) return;
      }
    }

    const invalid = container.querySelector('.input-validation-error, [aria-invalid="true"]');
    if (invalid) {
      invalid.scrollIntoView?.({ block: 'center' });
      invalid.focus?.({ preventScroll: true });
      return;
    }

    const summary = container.querySelector('[data-ipr-form-summary]');
    if (summary && summary.textContent.trim()) {
      summary.scrollIntoView?.({ block: 'start' });
      return;
    }

    const recordForm = container.matches?.('[data-ipr-record-form]')
      ? container
      : container.querySelector('[data-ipr-record-form]');
    const htmlInvalid = recordForm?.querySelector(':invalid');
    if (htmlInvalid) {
      htmlInvalid.scrollIntoView?.({ block: 'center' });
      htmlInvalid.focus?.({ preventScroll: true });
    }
  };

  const initialiseOffcanvas = () => {
    const offcanvasElement = document.getElementById('iprRecordOffcanvas');
    if (!offcanvasElement || typeof bootstrap === 'undefined' || !bootstrap.Offcanvas) return;

    const mode = (offcanvasElement.getAttribute('data-ipr-mode') || '').toLowerCase();
    const hasForm = (offcanvasElement.getAttribute('data-ipr-has-form') || '').toLowerCase() === 'true';
    const shouldShowOffcanvas = mode === 'create' || mode === 'edit';
    const supportsUrlApi = typeof URL === 'function' && URL.prototype && 'searchParams' in URL.prototype;

    const parseSearchParams = () => {
      const search = window.location.search ? window.location.search.substring(1) : '';
      if (!search) return {};

      return search.split('&').reduce((accumulator, part) => {
        if (!part) return accumulator;
        const [rawKey, rawValue = ''] = part.split('=');
        const key = decodeURIComponent(rawKey.replace(/\+/g, ' '));
        const value = decodeURIComponent(rawValue.replace(/\+/g, ' '));
        accumulator[key] = value;
        return accumulator;
      }, {});
    };

    const getQueryParam = key => {
      if (supportsUrlApi) {
        return new URL(window.location.href).searchParams.get(key);
      }

      const params = parseSearchParams();
      return Object.prototype.hasOwnProperty.call(params, key) ? params[key] : null;
    };

    const buildUrlWithoutModeAndId = () => {
      if (supportsUrlApi) {
        const url = new URL(window.location.href);
        url.searchParams.delete('mode');
        url.searchParams.delete('id');
        return url.toString();
      }

      const location = window.location;
      const origin = location.origin || `${location.protocol}//${location.host}`;
      const base = `${origin}${location.pathname}`;
      const params = parseSearchParams();
      delete params.mode;
      delete params.id;

      const query = Object.keys(params)
        .map(paramKey => `${encodeURIComponent(paramKey)}=${encodeURIComponent(params[paramKey])}`)
        .join('&');
      const hash = location.hash || '';
      return query ? `${base}?${query}${hash}` : `${base}${hash}`;
    };

    const updateTriggerStates = (nextMode, nextId) => {
      document.querySelectorAll('[data-ipr-offcanvas-trigger]').forEach(button => {
        const triggerMode = (button.getAttribute('data-ipr-offcanvas-trigger') || '').toLowerCase();
        const triggerId = button.getAttribute('data-ipr-record-id') || '';
        const expanded =
          (triggerMode === 'create' && nextMode === 'create') ||
          (triggerMode === 'edit' && nextMode === 'edit' && triggerId === nextId && nextId !== '');
        button.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      });
    };

    const instance = bootstrap.Offcanvas.getOrCreateInstance(offcanvasElement);
    const currentId = (getQueryParam('id') || '').toString();

    offcanvasElement.addEventListener('shown.bs.offcanvas', () => {
      if (hasForm) {
        focusFirstFormError(offcanvasElement);
      }
    });

    if (shouldShowOffcanvas) {
      instance.show();
      updateTriggerStates(mode, currentId);
    } else {
      updateTriggerStates('', '');
    }

    offcanvasElement.addEventListener('hidden.bs.offcanvas', () => {
      const nextUrl = buildUrlWithoutModeAndId();
      if (typeof history !== 'undefined' && typeof history.replaceState === 'function') {
        history.replaceState({}, document.title, nextUrl);
      } else {
        window.location.assign(nextUrl);
      }
      updateTriggerStates('', '');
    });

    if (!hasForm) return;

    ['iprCreateForm', 'iprEditForm'].forEach(formId => {
      const form = document.getElementById(formId);
      if (!form) return;

      form.addEventListener('invalid', () => {
        window.setTimeout(() => focusFirstFormError(form), 0);
      }, true);

      form.addEventListener('submit', () => {
        if (!form.checkValidity()) return;
        const button = form.querySelector('[data-ipr-submit-button]');
        if (!button || button.disabled) return;

        const label = button.querySelector('[data-ipr-submit-label]');
        const submittingText = button.getAttribute('data-submitting-text') || 'Saving…';
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        if (label) {
          label.innerHTML = `<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>${submittingText}`;
        }
      });
    });
  };

  class IprProjectPicker {
    constructor(root) {
      this.root = root;
      this.input = root.querySelector('[data-ipr-project-search]');
      this.valueInput = root.querySelector('[data-ipr-project-value]');
      this.panel = root.querySelector('[data-ipr-project-options]');
      this.clearButton = root.querySelector('[data-ipr-project-clear]');
      this.empty = root.querySelector('[data-ipr-project-empty]');
      this.selection = root.querySelector('[data-ipr-project-selection]');
      this.selectionText = root.querySelector('[data-ipr-project-selection-text]');
      this.status = root.querySelector('[data-ipr-project-status]');
      this.validationMessage = root.closest('.ipr-form-field')?.querySelector('[data-valmsg-for="Input.ProjectId"]');
      this.options = Array.from(root.querySelectorAll('[data-ipr-project-option]'));
      this.visibleOptions = [];
      this.activeIndex = -1;
      this.selectedId = this.valueInput?.value || '';
      this.committedId = this.selectedId;
      this.committedText = this.input?.value || '';
      this.committedSecondary = this.selectionText?.textContent || '';
      this.isOpen = false;

      if (!this.input || !this.valueInput || !this.panel) return;

      this.options.forEach((option, index) => {
        if (!option.id) option.id = `${this.panel.id}-option-${index}`;
        option.setAttribute('aria-selected', option.dataset.projectId === this.selectedId ? 'true' : 'false');
        option.addEventListener('click', () => this.select(option));
      });

      this.input.addEventListener('focus', () => this.openAndFilter(true));
      this.input.addEventListener('click', () => this.openAndFilter(true));
      this.input.addEventListener('input', () => {
        if (this.input.value !== this.committedText) {
          this.valueInput.value = '';
          this.selectedId = '';
          this.setSelectionMeta('');
          this.options.forEach(option => option.setAttribute('aria-selected', 'false'));
          this.updateClearButton();
        }
        this.clearValidationFeedback();
        this.setValidity('');
        this.openAndFilter();
      });
      this.input.addEventListener('keydown', event => this.onKeyDown(event));
      this.input.addEventListener('blur', () => {
        window.setTimeout(() => {
          if (!this.root.contains(document.activeElement)) this.commitOrRejectTypedText({ focus: false });
        }, 0);
      });
      this.clearButton?.addEventListener('click', () => this.clear());
      document.addEventListener('pointerdown', event => {
        if (!this.root.contains(event.target)) {
          this.commitOrRejectTypedText({ focus: false });
          this.close();
        }
      });

      this.updateClearButton();
    }

    openAndFilter(showAll = false) {
      const query = showAll && this.input.value === this.committedText
        ? ''
        : normalizeText(this.input.value);
      const ranked = this.options
        .map(option => {
          const haystack = normalizeText(option.dataset.projectSearch || option.dataset.projectLabel || '');
          const label = normalizeText(option.dataset.projectLabel || '');
          let rank = 4;
          if (!query) rank = option.dataset.projectId === this.committedId ? 0 : 3;
          else if (label === query) rank = 0;
          else if (label.startsWith(query) || haystack.startsWith(query)) rank = 1;
          else if (haystack.split(' ').some(word => word.startsWith(query))) rank = 2;
          else if (haystack.includes(query)) rank = 3;
          else rank = Number.POSITIVE_INFINITY;
          return { option, rank, label };
        })
        .filter(item => Number.isFinite(item.rank))
        .sort((left, right) => left.rank - right.rank || left.label.localeCompare(right.label));

      const visibleLimit = 12;
      const visibleSet = new Set(ranked.slice(0, visibleLimit).map(item => item.option));
      this.options.forEach(option => option.classList.toggle('d-none', !visibleSet.has(option)));
      this.visibleOptions = ranked.slice(0, visibleLimit).map(item => item.option);
      this.empty?.classList.toggle('d-none', ranked.length > 0);
      this.setStatus(ranked.length === 0
        ? 'No matching project found.'
        : ranked.length > visibleLimit
          ? `${ranked.length} matching projects. Showing the first ${visibleLimit}.`
          : `${ranked.length} matching project${ranked.length === 1 ? '' : 's'}.`);

      this.activeIndex = this.visibleOptions.length > 0 ? 0 : -1;
      this.applyActiveOption();
      this.panel.classList.remove('d-none');
      this.input.setAttribute('aria-expanded', 'true');
      this.isOpen = true;
    }

    onKeyDown(event) {
      if (event.key === 'Escape') {
        if (this.isOpen) {
          event.preventDefault();
          this.restoreCommittedSelection();
          this.setValidity('');
          this.close();
        }
        return;
      }

      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        if (!this.isOpen) this.openAndFilter();
        if (this.visibleOptions.length === 0) return;
        const direction = event.key === 'ArrowDown' ? 1 : -1;
        this.activeIndex = (this.activeIndex + direction + this.visibleOptions.length) % this.visibleOptions.length;
        this.applyActiveOption();
        return;
      }

      if (event.key === 'Enter') {
        if (!this.isOpen) {
          event.preventDefault();
          this.openAndFilter();
          return;
        }
        const option = this.visibleOptions[this.activeIndex];
        if (option) {
          event.preventDefault();
          this.select(option);
        }
        return;
      }

      if (event.key === 'Tab' && this.isOpen) {
        this.commitOrRejectTypedText({ focus: false });
        this.close();
      }
    }

    applyActiveOption() {
      this.visibleOptions.forEach((option, index) => {
        const active = index === this.activeIndex;
        option.classList.toggle('is-active', active);
        if (active) {
          this.input.setAttribute('aria-activedescendant', option.id);
          option.scrollIntoView?.({ block: 'nearest' });
        }
      });
      if (this.activeIndex < 0) this.input.removeAttribute('aria-activedescendant');
    }

    select(option, { focus = true } = {}) {
      const id = option.dataset.projectId || '';
      const label = option.dataset.projectLabel || '';
      const secondary = option.dataset.projectSecondary || '';

      this.valueInput.value = id;
      this.selectedId = id;
      this.committedId = id;
      this.committedText = label;
      this.committedSecondary = secondary;
      this.input.value = label;
      this.clearValidationFeedback();
      this.setValidity('');
      this.options.forEach(item => item.setAttribute('aria-selected', item === option ? 'true' : 'false'));
      this.setSelectionMeta(secondary);
      this.updateClearButton();
      this.valueInput.dispatchEvent(new Event('change', { bubbles: true }));
      this.close();
      if (focus) this.input.focus();
    }

    clear() {
      this.valueInput.value = '';
      this.selectedId = '';
      this.committedId = '';
      this.committedText = '';
      this.committedSecondary = '';
      this.input.value = '';
      this.clearValidationFeedback();
      this.setValidity('');
      this.options.forEach(option => option.setAttribute('aria-selected', 'false'));
      this.setSelectionMeta('');
      this.updateClearButton();
      this.valueInput.dispatchEvent(new Event('change', { bubbles: true }));
      this.input.focus();
      this.openAndFilter(true);
    }

    commitOrRejectTypedText({ focus = false } = {}) {
      const typed = this.input.value.trim();
      if (!typed || typed === this.committedText) {
        this.setValidity('');
        if (!typed) this.clearWithoutOpening();
        else this.restoreCommittedSelection();
        return;
      }

      const exact = this.options.find(option =>
        normalizeText(option.dataset.projectLabel || '') === normalizeText(typed));
      if (exact) {
        this.select(exact, { focus });
        return;
      }

      this.setValidity('Select a project from the search results, or clear the field to leave it unassigned.');
    }

    clearWithoutOpening() {
      const changed = Boolean(this.committedId);
      this.valueInput.value = '';
      this.selectedId = '';
      this.committedId = '';
      this.committedText = '';
      this.committedSecondary = '';
      this.options.forEach(option => option.setAttribute('aria-selected', 'false'));
      this.setSelectionMeta('');
      this.updateClearButton();
      if (changed) this.valueInput.dispatchEvent(new Event('change', { bubbles: true }));
    }

    restoreCommittedSelection() {
      this.valueInput.value = this.committedId;
      this.selectedId = this.committedId;
      this.input.value = this.committedText;
      this.options.forEach(option =>
        option.setAttribute('aria-selected', option.dataset.projectId === this.committedId ? 'true' : 'false'));
      this.setSelectionMeta(this.committedSecondary);
      this.updateClearButton();
    }


    clearValidationFeedback() {
      this.input.classList.remove('is-invalid');
      if (!this.validationMessage) return;
      this.validationMessage.textContent = '';
      this.validationMessage.classList.remove('field-validation-error');
      this.validationMessage.classList.add('field-validation-valid');
    }

    setValidity(message) {
      this.input.setCustomValidity(message);
      this.input.setAttribute('aria-invalid', message ? 'true' : 'false');
    }

    setSelectionMeta(text) {
      if (this.selectionText) this.selectionText.textContent = text;
      this.selection?.classList.toggle('d-none', !text);
    }

    updateClearButton() {
      this.clearButton?.classList.toggle('d-none', !this.input.value);
    }

    setStatus(message) {
      if (this.status) this.status.textContent = message;
    }

    close() {
      this.panel.classList.add('d-none');
      this.input.setAttribute('aria-expanded', 'false');
      this.input.removeAttribute('aria-activedescendant');
      this.isOpen = false;
      this.activeIndex = -1;
      this.options.forEach(option => option.classList.remove('is-active'));
    }
  }

  const initialiseProjectPickers = () => {
    document.querySelectorAll('[data-ipr-project-picker]').forEach(root => new IprProjectPicker(root));
  };

  const initialiseFilterModal = () => {
    const modal = document.getElementById('iprFiltersModal');
    const trigger = document.querySelector('[data-ipr-filter-trigger]');
    if (!modal || !trigger || typeof bootstrap === 'undefined' || !bootstrap.Modal) return;

    modal.addEventListener('show.bs.modal', () => trigger.setAttribute('aria-expanded', 'true'));
    modal.addEventListener('shown.bs.modal', () => modal.querySelector('[data-ipr-filter-initial-focus]')?.focus());
    modal.addEventListener('hidden.bs.modal', () => {
      trigger.setAttribute('aria-expanded', 'false');
      trigger.focus();
    });
  };

  const initialiseConfirmations = () => {
    document.querySelectorAll('form[data-ipr-confirm]').forEach(form => {
      form.addEventListener('submit', event => {
        const message = form.getAttribute('data-ipr-confirm') || 'Are you sure?';
        if (!window.confirm(message)) {
          event.preventDefault();
          event.stopImmediatePropagation();
        }
      });
    });
  };

  const initialiseToasts = () => {
    const container = document.getElementById('iprToastContainer');
    if (!container || typeof bootstrap === 'undefined' || !bootstrap.Toast) return;
    container.querySelectorAll('.toast').forEach(element => bootstrap.Toast.getOrCreateInstance(element).show());
  };

  const initialiseLoadingState = () => {
    const skeleton = document.querySelector('[data-ipr-loading-skeleton]');
    const contentTargets = document.querySelectorAll('[data-ipr-table-content]');
    if (!skeleton || contentTargets.length === 0) return;

    const showSkeleton = () => {
      skeleton.classList.remove('d-none');
      contentTargets.forEach(element => element.classList.add('d-none'));
    };
    skeleton.classList.add('d-none');
    contentTargets.forEach(element => element.classList.remove('d-none'));
    document.querySelectorAll('form[data-ipr-loading-form]').forEach(form => form.addEventListener('submit', showSkeleton));
    document.querySelectorAll('[data-ipr-loading-link]').forEach(link => link.addEventListener('click', showSkeleton));
  };

  const initialiseAutoSubmitFilters = () => {
    const form = document.querySelector('.ipr-filter-bar[data-ipr-loading-form]');
    if (!form) return;
    form.querySelectorAll('[data-ipr-auto-submit]').forEach(control => {
      control.addEventListener('change', () => form.requestSubmit());
    });
  };

  const initialiseGrantedDate = () => {
    document.querySelectorAll('[data-ipr-record-form]').forEach(form => {
      const status = form.querySelector('[data-ipr-status-select]');
      const field = form.querySelector('[data-ipr-granted-date-field]');
      const input = form.querySelector('[data-ipr-granted-date]');
      if (!status || !field || !input) return;

      const sync = clearWhenHidden => {
        const granted = status.value.toLowerCase() === 'granted';
        field.classList.toggle('is-hidden', !granted);
        field.setAttribute('aria-hidden', granted ? 'false' : 'true');
        input.disabled = !granted;
        input.required = granted;
        if (!granted && clearWhenHidden) input.value = '';
      };

      sync(false);
      status.addEventListener('change', () => sync(true));
    });
  };

  const initialiseAttachmentUpload = () => {
    const form = document.getElementById('iprAttachmentForm');
    if (!form) return;

    form.addEventListener('submit', () => {
      const button = form.querySelector('button[type="submit"]');
      if (!button || !form.checkValidity()) return;
      button.disabled = true;
      button.setAttribute('aria-busy', 'true');
      button.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Uploading…';
    });
  };

  initialiseProjectPickers();
  initialiseOffcanvas();
  initialiseFilterModal();
  initialiseConfirmations();
  initialiseToasts();
  initialiseLoadingState();
  initialiseAutoSubmitFilters();
  initialiseGrantedDate();
  initialiseAttachmentUpload();
})();
