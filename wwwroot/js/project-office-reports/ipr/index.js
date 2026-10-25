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
          : fieldName.replace(/([.\[\]])/g, '\\$1');
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

    const form = container.matches?.('[data-ipr-record-form]')
      ? container
      : container.querySelector('[data-ipr-record-form]');
    const htmlInvalid = form?.querySelector(':invalid');
    htmlInvalid?.scrollIntoView?.({ block: 'center' });
    htmlInvalid?.focus?.({ preventScroll: true });
  };

  const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

  // Keeps Tab and Shift+Tab inside an open panel (it is a modal dialog over the register).
  // A Bootstrap modal opened from the panel (the patent/copyright guidance) manages its own focus.
  const trapFocus = (container, event) => {
    if (event.key !== 'Tab' || document.querySelector('.modal.show')) return;
    const items = Array.from(container.querySelectorAll(FOCUSABLE))
      .filter(item => !item.closest('[hidden], .d-none') && window.getComputedStyle(item).display !== 'none');
    if (items.length === 0) return;
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;
    if (!container.contains(active)) {
      event.preventDefault();
      (event.shiftKey ? last : first).focus();
    } else if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  };
  window.iprTrapFocus = trapFocus;

  // The record editor opens in the record panel's place (server-rendered for mode=create/edit).
  const initialiseEditor = () => {
    const element = document.getElementById('iprRecordEditor');
    if (!element) return;

    const closeUrl = element.dataset.iprCloseUrl || window.location.pathname;
    const form = element.querySelector('[data-ipr-record-form]');
    let dirty = false;
    let submitting = false;
    let leaving = false;

    document.body.classList.add('ipr-panel-open');
    if (form?.querySelector('.field-validation-error, .input-validation-error, [data-ipr-form-summary] li')) {
      focusFirstFormError(element);
    } else {
      element.querySelector('input:not([type="hidden"]), select, textarea')?.focus?.({ preventScroll: true });
    }

    form?.addEventListener('input', () => { dirty = true; });
    form?.addEventListener('change', () => { dirty = true; });

    const close = () => {
      if (dirty && !submitting && !window.confirm('Discard your changes to this record?')) return;
      leaving = true;
      window.location.assign(closeUrl);
    };

    document.addEventListener('keydown', event => {
      trapFocus(element, event);
      if (event.key !== 'Escape' || document.querySelector('.modal.show, .offcanvas.show')) return;
      if (event.target.closest?.('[data-ipr-project-picker]')) return;
      event.preventDefault();
      close();
    });

    document.querySelectorAll('[data-ipr-editor-close]').forEach(link => {
      link.addEventListener('click', event => {
        if (dirty && !submitting && !window.confirm('Discard your changes to this record?')) {
          event.preventDefault();
          return;
        }
        leaving = true;
      });
    });

    // Any other way out (site navigation, back, closing the tab) asks the browser's own question.
    window.addEventListener('beforeunload', event => {
      if (!dirty || submitting || leaving) return;
      event.preventDefault();
      event.returnValue = '';
    });

    if (!form) return;

    form.addEventListener('invalid', () => {
      window.setTimeout(() => focusFirstFormError(form), 0);
    }, true);

    form.addEventListener('submit', () => {
      if (!form.checkValidity()) return;
      const button = form.querySelector('[data-ipr-submit-button]');
      if (!button || button.disabled) return;

      submitting = true;
      const label = button.querySelector('[data-ipr-submit-label]');
      const evidence = form.querySelector('[data-ipr-evidence-input]');
      const hasFile = Boolean(evidence?.files?.length);
      const submittingText = hasFile ? 'Saving and uploading…' : (button.getAttribute('data-submitting-text') || 'Saving…');
      button.disabled = true;
      button.setAttribute('aria-busy', 'true');
      if (label) {
        label.innerHTML = `<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>${submittingText}`;
      }
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
          if (!this.root.contains(document.activeElement)) {
            this.commitOrRejectTypedText({ focus: false });
            this.close();
          }
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
          else if (label.startsWith(query)) rank = 1;
          else if (haystack.includes(query)) rank = 2;
          return { option, rank };
        })
        .filter(item => item.rank < 4)
        .sort((a, b) => a.rank - b.rank || (a.option.dataset.projectLabel || '').localeCompare(b.option.dataset.projectLabel || ''));

      const visible = new Set(ranked.map(item => item.option));
      this.options.forEach(option => option.classList.toggle('d-none', !visible.has(option)));
      this.visibleOptions = ranked.map(item => item.option);
      this.empty?.classList.toggle('d-none', this.visibleOptions.length > 0);
      this.activeIndex = this.visibleOptions.length > 0 ? 0 : -1;
      this.applyActiveOption();
      this.panel.classList.remove('d-none');
      this.input.setAttribute('aria-expanded', 'true');
      this.isOpen = true;
      this.setStatus(`${this.visibleOptions.length} project option${this.visibleOptions.length === 1 ? '' : 's'} available.`);
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
      const allOption = this.options.find(option => !option.dataset.projectId);
      if (allOption) {
        this.select(allOption);
        return;
      }

      this.valueInput.value = '';
      this.selectedId = '';
      this.committedId = '';
      this.committedText = '';
      this.committedSecondary = '';
      this.input.value = '';
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

      const exact = this.options.find(option => normalizeText(option.dataset.projectLabel || '') === normalizeText(typed));
      if (exact) {
        this.select(exact, { focus });
        return;
      }

      this.setValidity('Select a project from the search results, or clear the field.');
    }

    clearWithoutOpening() {
      const changed = Boolean(this.committedId);
      this.valueInput.value = '';
      this.selectedId = '';
      this.committedId = '';
      this.committedText = '';
      this.committedSecondary = '';
      this.options.forEach(option => option.setAttribute('aria-selected', option.dataset.projectId ? 'false' : 'true'));
      this.setSelectionMeta('');
      this.updateClearButton();
      if (changed) this.valueInput.dispatchEvent(new Event('change', { bubbles: true }));
    }

    restoreCommittedSelection() {
      this.valueInput.value = this.committedId;
      this.selectedId = this.committedId;
      this.input.value = this.committedText;
      this.options.forEach(option => option.setAttribute('aria-selected', option.dataset.projectId === this.committedId ? 'true' : 'false'));
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
      this.clearButton?.classList.toggle('d-none', !this.input.value && !this.valueInput.value);
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

  const initialiseDensity = () => {
    const root = document.querySelector('[data-ipr-density-root]');
    const buttons = Array.from(document.querySelectorAll('[data-ipr-density]'));
    if (!root || buttons.length === 0) return;

    const apply = density => {
      const resolved = density === 'comfortable' ? 'comfortable' : 'compact';
      root.classList.toggle('density-comfortable', resolved === 'comfortable');
      buttons.forEach(button => {
        const active = button.dataset.iprDensity === resolved;
        button.classList.toggle('is-active', active);
        button.setAttribute('aria-pressed', active ? 'true' : 'false');
      });
      try { localStorage.setItem('ipr:table-density', resolved); } catch { /* optional */ }
    };

    let saved = 'compact';
    try { saved = localStorage.getItem('ipr:table-density') || 'compact'; } catch { /* optional */ }
    apply(saved);
    buttons.forEach(button => button.addEventListener('click', () => apply(button.dataset.iprDensity)));
  };

  const initialiseConfirmations = () => {
    document.querySelectorAll('form[data-ipr-confirm]').forEach(form => {
      form.addEventListener('submit', event => {
        const message = form.dataset.iprConfirm || 'Are you sure?';
        if (!window.confirm(message)) {
          event.preventDefault();
          event.stopImmediatePropagation();
        }
      });
    });
  };

  const initialiseFilterDrawer = () => {
    const drawer = document.getElementById('iprFilterOffcanvas');
    const form = drawer?.querySelector('[data-ipr-filter-form]');
    if (!drawer || !form) return;

    const dateBasis = form.querySelector('[data-ipr-date-basis]');
    const year = form.querySelector('[data-ipr-date-year]');
    const help = form.querySelector('[data-ipr-date-basis-help]');
    const project = form.querySelector('[data-ipr-project-value]');
    const linkage = form.querySelector('[data-ipr-linkage-filter]');

    const syncDateYears = () => {
      if (!dateBasis || !year) return;
      const protectedBasis = normalizeText(dateBasis.value) === 'protected';
      let selectedAvailable = !year.value;

      Array.from(year.options).forEach(option => {
        if (!option.value) {
          option.hidden = false;
          option.disabled = false;
          return;
        }

        const available = protectedBasis
          ? option.dataset.hasProtected === 'true'
          : option.dataset.hasFiled === 'true';
        option.hidden = !available;
        option.disabled = !available;
        if (option.selected && available) selectedAvailable = true;
      });

      if (!selectedAvailable) year.value = '';
      if (help) {
        help.textContent = protectedBasis
          ? 'Uses the patent grant or copyright registration date; pending records are excluded.'
          : 'Uses the original filing date.';
      }
    };

    const syncLinkage = () => {
      if (!project || !linkage) return;
      const projectSelected = Boolean(project.value);
      if (projectSelected) linkage.value = 'All';
      linkage.disabled = projectSelected;
      linkage.closest('.ipr-filter-field')?.classList.toggle('is-disabled', projectSelected);
    };

    dateBasis?.addEventListener('change', syncDateYears);
    project?.addEventListener('change', syncLinkage);
    form.addEventListener('submit', () => {
      const page = form.querySelector('input[name="pageNumber"]');
      if (page) page.value = '1';
      if (linkage?.disabled) linkage.disabled = false;
    });

    drawer.addEventListener('shown.bs.offcanvas', () => {
      drawer.querySelector('select, input:not([type="hidden"])')?.focus?.();
    });

    syncDateYears();
    syncLinkage();
  };

  const initialiseTypeGuidance = () => {
    const guidanceByType = {
      patent: {
        guidance: 'Use for a new technical product, process, mechanism or technical working arrangement.',
        position: 'Granted means the patent grant has been issued.',
        pending: 'Patent pending',
        protected: 'Patent granted',
        date: 'Grant date'
      },
      copyright: {
        guidance: 'Use for original software, documents, drawings, 3D assets, audiovisual or training content.',
        position: 'Registered means the copyright registration has been issued.',
        pending: 'Registration pending',
        protected: 'Copyright registered',
        date: 'Registration date'
      },
      default: {
        guidance: 'Patent protects a technical invention; copyright protects original software, documents and creative material.',
        position: 'Select an IPR category to use the correct lifecycle terminology.',
        pending: 'Pending',
        protected: 'Granted',
        date: 'Grant / registration date'
      }
    };

    document.querySelectorAll('[data-ipr-record-form]').forEach(form => {
      const typeSelect = form.querySelector('[data-ipr-type-select]');
      const statusSelect = form.querySelector('[data-ipr-status-select]');
      const guidance = form.querySelector('[data-ipr-type-guidance] span');
      const positionGuidance = form.querySelector('[data-ipr-position-guidance]');
      const dateLabel = form.querySelector('[data-ipr-protected-date-label]');
      if (!typeSelect) return;

      const sync = () => {
        const key = normalizeText(typeSelect.value);
        const content = guidanceByType[key] || guidanceByType.default;
        if (guidance) guidance.textContent = content.guidance;
        if (positionGuidance) positionGuidance.textContent = content.position;
        if (dateLabel) dateLabel.textContent = content.date;

        if (statusSelect) {
          Array.from(statusSelect.options).forEach(option => {
            const value = normalizeText(option.value);
            if (value === 'filed') option.textContent = content.pending;
            if (value === 'granted') option.textContent = content.protected;
          });
        }
      };

      sync();
      typeSelect.addEventListener('change', sync);
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

  initialiseProjectPickers();
  initialiseEditor();
  initialiseDensity();
  initialiseConfirmations();
  initialiseFilterDrawer();
  initialiseTypeGuidance();
  initialiseGrantedDate();
})();
