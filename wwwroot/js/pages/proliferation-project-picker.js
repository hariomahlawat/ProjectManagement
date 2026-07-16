(() => {
  'use strict';

  const pageRoot = document.querySelector('[data-page="proliferation-manage"]');
  if (!pageRoot) return;

  const RECENT_STORAGE_KEY = 'prism.proliferation.recent-projects.v1';
  const MAX_RECENT = 5;
  const MAX_EMPTY_RESULTS = 12;
  const MAX_SEARCH_RESULTS = 30;
  const MIN_OPTIONS_TO_ENHANCE = 10;
  const instances = new WeakMap();
  let instanceSequence = 0;

  function normalise(value) {
    return String(value ?? '')
      .normalize('NFKD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLocaleLowerCase()
      .replace(/[^a-z0-9]+/g, ' ')
      .trim();
  }

  function unique(values) {
    return [...new Set(values.filter(Boolean))];
  }

  function readRecentIds() {
    try {
      const parsed = JSON.parse(window.localStorage.getItem(RECENT_STORAGE_KEY) || '[]');
      return Array.isArray(parsed)
        ? unique(parsed.map((value) => String(value ?? '').trim())).slice(0, MAX_RECENT)
        : [];
    } catch {
      return [];
    }
  }

  function writeRecentId(value) {
    const id = String(value ?? '').trim();
    if (!id) return;

    try {
      const next = [id, ...readRecentIds().filter((item) => item !== id)].slice(0, MAX_RECENT);
      window.localStorage.setItem(RECENT_STORAGE_KEY, JSON.stringify(next));
    } catch {
      // Local storage is an optional convenience. The picker remains fully functional without it.
    }
  }

  function parseOption(option, index) {
    const label = String(option.textContent ?? '').replace(/\s+/g, ' ').trim();
    const explicitCode = String(option.dataset.projectCode ?? '').trim();
    const explicitCategory = String(option.dataset.projectCategory ?? '').trim();
    const trailingCodeMatch = !explicitCode ? label.match(/\(([^()]*)\)\s*$/) : null;
    const derivedCode = trailingCodeMatch?.[1]?.trim() || '';
    const code = explicitCode || derivedCode;
    const primary = trailingCodeMatch && derivedCode
      ? label.slice(0, trailingCodeMatch.index).trim()
      : label;
    const secondaryParts = unique([code, explicitCategory]);
    const secondary = secondaryParts.join(' · ');

    return {
      index,
      value: String(option.value ?? ''),
      label,
      primary: primary || label,
      secondary,
      disabled: option.disabled,
      searchText: normalise([
        label,
        primary,
        code,
        explicitCategory,
        option.dataset.searchTerms
      ].filter(Boolean).join(' '))
    };
  }

  function getEmptyOption(select) {
    return Array.from(select.options).find((option) => String(option.value ?? '') === '') ?? null;
  }

  function shouldEnhance(select) {
    if (!(select instanceof HTMLSelectElement)) return false;
    if (select.multiple || select.size > 1 || select.dataset.projectPicker === 'off') return false;
    if (instances.has(select) || select.dataset.projectPickerEnhanced === 'true') return false;

    const identity = `${select.id} ${select.name} ${select.dataset.projectPicker}`.toLocaleLowerCase();
    const explicitlyEnabled = select.hasAttribute('data-project-picker');
    const looksLikeProjectSelect = identity.includes('project');
    const selectableCount = Array.from(select.options).filter((option) => option.value).length;

    return explicitlyEnabled || (looksLikeProjectSelect && selectableCount >= MIN_OPTIONS_TO_ENHANCE);
  }

  class ProjectPicker {
    constructor(select) {
      this.select = select;
      this.items = [];
      this.filteredItems = [];
      this.activeIndex = -1;
      this.isOpen = false;
      this.selectedValue = String(select.value ?? '');
      this.selectedLabel = '';
      this.lastObservedValue = this.selectedValue;
      this.instanceId = ++instanceSequence;
      this.listboxId = `pf-project-picker-listbox-${this.instanceId}`;
      this.statusId = `pf-project-picker-status-${this.instanceId}`;
      this.optionIdPrefix = `pf-project-picker-option-${this.instanceId}`;
      this.valueDescriptor = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value');
      this.originalFocus = select.focus.bind(select);
      this.originalScrollIntoView = select.scrollIntoView.bind(select);

      this.buildMarkup();
      this.rebuildItems();
      this.patchNativeSelect();
      this.bindEvents();
      this.observeSelect();
      this.syncFromSelect({ force: true });
      this.syncVisualState();

      select.dataset.projectPickerEnhanced = 'true';
      instances.set(select, this);
    }

    buildMarkup() {
      const emptyOption = getEmptyOption(this.select);
      const emptyLabel = String(emptyOption?.textContent ?? '').replace(/\s+/g, ' ').trim();
      const configuredPlaceholder = String(this.select.dataset.projectPickerPlaceholder ?? '').trim();
      const placeholder = configuredPlaceholder
        || (emptyLabel && !/^select project$/i.test(emptyLabel) ? `${emptyLabel} — type to search` : '')
        || 'Search project name or case file number';

      this.root = document.createElement('div');
      this.root.className = 'pf-project-picker';
      this.root.dataset.projectPickerRoot = 'true';

      this.control = document.createElement('div');
      this.control.className = 'pf-project-picker__control';

      this.searchIcon = document.createElement('i');
      this.searchIcon.className = 'bi bi-search pf-project-picker__search-icon';
      this.searchIcon.setAttribute('aria-hidden', 'true');

      this.input = document.createElement('input');
      this.input.type = 'text';
      this.input.className = 'form-control pf-project-picker__input';
      this.input.placeholder = placeholder;
      this.input.autocomplete = 'off';
      this.input.spellcheck = false;
      this.input.setAttribute('role', 'combobox');
      this.input.setAttribute('aria-autocomplete', 'list');
      this.input.setAttribute('aria-haspopup', 'listbox');
      this.input.setAttribute('aria-expanded', 'false');
      this.input.setAttribute('aria-controls', this.listboxId);
      this.input.setAttribute('aria-describedby', unique([
        this.select.getAttribute('aria-describedby'),
        this.statusId
      ]).join(' '));
      if (this.select.required) this.input.setAttribute('aria-required', 'true');

      this.clearButton = document.createElement('button');
      this.clearButton.type = 'button';
      this.clearButton.className = 'btn btn-link pf-project-picker__clear';
      this.clearButton.setAttribute('aria-label', 'Clear selected project');
      this.clearButton.hidden = true;
      const clearIcon = document.createElement('i');
      clearIcon.className = 'bi bi-x-lg';
      clearIcon.setAttribute('aria-hidden', 'true');
      this.clearButton.append(clearIcon);

      this.toggleButton = document.createElement('button');
      this.toggleButton.type = 'button';
      this.toggleButton.className = 'btn btn-link pf-project-picker__toggle';
      this.toggleButton.setAttribute('aria-label', 'Show project options');
      this.toggleButton.setAttribute('aria-expanded', 'false');
      this.toggleButton.setAttribute('aria-controls', this.listboxId);
      const toggleIcon = document.createElement('i');
      toggleIcon.className = 'bi bi-chevron-down';
      toggleIcon.setAttribute('aria-hidden', 'true');
      this.toggleButton.append(toggleIcon);

      this.popup = document.createElement('div');
      this.popup.className = 'pf-project-picker__popup';
      this.popup.hidden = true;

      this.status = document.createElement('div');
      this.status.id = this.statusId;
      this.status.className = 'pf-project-picker__status';
      this.status.setAttribute('aria-live', 'polite');
      this.status.setAttribute('aria-atomic', 'true');

      this.listbox = document.createElement('div');
      this.listbox.id = this.listboxId;
      this.listbox.className = 'pf-project-picker__listbox';
      this.listbox.setAttribute('role', 'listbox');
      this.listbox.setAttribute('aria-label', 'Project search results');

      this.popup.append(this.status, this.listbox);
      this.control.append(this.searchIcon, this.input, this.clearButton, this.toggleButton);
      this.root.append(this.control, this.popup);

      this.select.before(this.root);
      this.select.classList.add('pf-project-picker__native');
      this.select.setAttribute('tabindex', '-1');
      this.select.setAttribute('aria-hidden', 'true');
    }

    rebuildItems() {
      this.items = Array.from(this.select.options)
        .map(parseOption)
        .filter((item) => item.value);
      this.selectedValue = String(this.select.value ?? '');
      this.selectedLabel = this.items.find((item) => item.value === this.selectedValue)?.label || '';
      if (this.isOpen) this.renderResults(this.getSearchQuery());
    }

    patchNativeSelect() {
      if (this.valueDescriptor?.get && this.valueDescriptor?.set) {
        try {
          Object.defineProperty(this.select, 'value', {
            configurable: true,
            enumerable: true,
            get: () => this.valueDescriptor.get.call(this.select),
            set: (value) => {
              this.valueDescriptor.set.call(this.select, value);
              queueMicrotask(() => this.syncFromSelect());
            }
          });
        } catch {
          // Some engines may not permit an instance-level value descriptor. Polling below covers that case.
        }
      }

      try {
        this.select.focus = () => this.input.focus();
        this.select.scrollIntoView = (options) => this.input.scrollIntoView(options);
      } catch {
        // Focus redirection is a progressive enhancement only.
      }
    }

    bindEvents() {
      this.input.addEventListener('focus', () => {
        this.open();
        if (this.selectedLabel && this.input.value === this.selectedLabel) {
          this.input.select();
        }
      });

      this.input.addEventListener('click', () => this.open());

      this.input.addEventListener('input', () => {
        this.open();
        this.renderResults(this.getSearchQuery());
      });

      this.input.addEventListener('keydown', (event) => this.handleKeydown(event));

      this.input.addEventListener('blur', () => {
        window.setTimeout(() => {
          if (!this.root.contains(document.activeElement)) {
            this.close({ restoreSelection: true });
          }
        }, 0);
      });

      this.toggleButton.addEventListener('click', () => {
        if (this.isOpen) {
          this.close({ restoreSelection: true });
        } else {
          this.input.focus();
          this.open();
        }
      });

      this.clearButton.addEventListener('click', () => {
        this.setValue('', { dispatchChange: true, remember: false });
        this.input.focus();
        this.open();
      });

      this.select.addEventListener('change', () => this.syncFromSelect({ force: true }));
      this.select.form?.addEventListener('reset', () => window.setTimeout(() => this.syncFromSelect({ force: true }), 0));

      document.addEventListener('pointerdown', (event) => {
        if (this.isOpen && !this.root.contains(event.target)) {
          this.close({ restoreSelection: true });
        }
      });

      this.valuePoller = window.setInterval(() => {
        if (document.visibilityState !== 'visible') return;
        const current = String(this.select.value ?? '');
        if (current !== this.lastObservedValue) this.syncFromSelect({ force: true });
      }, 350);
    }

    observeSelect() {
      this.selectObserver = new MutationObserver((mutations) => {
        const optionsChanged = mutations.some((mutation) => mutation.type === 'childList');
        if (optionsChanged) this.rebuildItems();
        this.syncVisualState();
        this.syncFromSelect();
      });

      this.selectObserver.observe(this.select, {
        attributes: true,
        attributeFilter: ['class', 'disabled', 'required', 'aria-describedby', 'aria-invalid'],
        childList: true,
        subtree: true
      });
    }

    getSearchQuery() {
      const entered = String(this.input.value ?? '');
      return entered === this.selectedLabel ? '' : entered;
    }

    rankItems(query) {
      const needle = normalise(query);
      const recentIds = readRecentIds();
      const recentRank = new Map(recentIds.map((value, index) => [value, index]));

      if (!needle) {
        const recent = recentIds
          .map((value) => this.items.find((item) => item.value === value))
          .filter(Boolean);
        const recentValues = new Set(recent.map((item) => item.value));
        const alphabetical = this.items
          .filter((item) => !recentValues.has(item.value))
          .sort((a, b) => a.label.localeCompare(b.label, undefined, { sensitivity: 'base', numeric: true }))
          .slice(0, MAX_EMPTY_RESULTS);

        return {
          sections: [
            ...(recent.length ? [{ label: 'Recently used', items: recent }] : []),
            { label: recent.length ? 'All projects' : '', items: alphabetical }
          ],
          flat: [...recent, ...alphabetical],
          total: this.items.length,
          isSearch: false
        };
      }

      const tokens = needle.split(' ').filter(Boolean);
      const ranked = this.items
        .map((item) => {
          const haystack = item.searchText;
          if (!tokens.every((token) => haystack.includes(token))) return null;

          let score = 100;
          if (haystack === needle) score = 0;
          else if (haystack.startsWith(needle)) score = 8;
          else if (haystack.split(' ').some((word) => word.startsWith(needle))) score = 18;
          else if (haystack.includes(needle)) score = 28;
          else score = 40 + tokens.reduce((sum, token) => sum + haystack.indexOf(token), 0);

          if (recentRank.has(item.value)) score -= Math.max(1, 5 - recentRank.get(item.value));
          if (item.value === this.selectedValue) score -= 2;

          return { item, score };
        })
        .filter(Boolean)
        .sort((a, b) => a.score - b.score
          || a.item.label.localeCompare(b.item.label, undefined, { sensitivity: 'base', numeric: true }));

      const matches = ranked.map((entry) => entry.item);
      return {
        sections: [{ label: '', items: matches.slice(0, MAX_SEARCH_RESULTS) }],
        flat: matches.slice(0, MAX_SEARCH_RESULTS),
        total: matches.length,
        isSearch: true
      };
    }

    renderResults(query) {
      const result = this.rankItems(query);
      this.filteredItems = result.flat;
      this.activeIndex = this.filteredItems.length ? 0 : -1;
      this.listbox.replaceChildren();

      if (!this.filteredItems.length) {
        const empty = document.createElement('div');
        empty.className = 'pf-project-picker__empty';
        empty.innerHTML = '<i class="bi bi-search" aria-hidden="true"></i><strong>No matching project</strong><span>Check the spelling or use a shorter project name or case file number.</span>';
        this.listbox.append(empty);
        this.status.textContent = 'No matching projects.';
        this.updateActiveDescendant();
        return;
      }

      let renderedIndex = 0;
      result.sections.forEach((section) => {
        if (!section.items.length) return;

        if (section.label) {
          const heading = document.createElement('div');
          heading.className = 'pf-project-picker__section-label';
          heading.textContent = section.label;
          this.listbox.append(heading);
        }

        section.items.forEach((item) => {
          const option = document.createElement('div');
          option.id = `${this.optionIdPrefix}-${renderedIndex}`;
          option.className = 'pf-project-picker__option';
          option.setAttribute('role', 'option');
          option.setAttribute('aria-selected', item.value === this.selectedValue ? 'true' : 'false');
          option.dataset.value = item.value;
          option.dataset.resultIndex = String(renderedIndex);
          if (item.disabled) {
            option.classList.add('is-disabled');
            option.setAttribute('aria-disabled', 'true');
          }

          const copy = document.createElement('span');
          copy.className = 'pf-project-picker__option-copy';
          const primary = document.createElement('strong');
          primary.textContent = item.primary;
          copy.append(primary);
          if (item.secondary) {
            const secondary = document.createElement('small');
            secondary.textContent = item.secondary;
            copy.append(secondary);
          }

          const marker = document.createElement('i');
          marker.className = item.value === this.selectedValue
            ? 'bi bi-check2 pf-project-picker__option-marker'
            : 'bi bi-arrow-return-left pf-project-picker__option-marker';
          marker.setAttribute('aria-hidden', 'true');

          option.append(copy, marker);
          option.addEventListener('pointermove', () => {
            if (!item.disabled) this.setActiveIndex(Number(option.dataset.resultIndex));
          });
          option.addEventListener('mousedown', (event) => event.preventDefault());
          option.addEventListener('click', () => {
            if (!item.disabled) this.chooseItem(item);
          });

          this.listbox.append(option);
          renderedIndex += 1;
        });
      });

      if (result.isSearch) {
        const shown = Math.min(result.total, MAX_SEARCH_RESULTS);
        this.status.textContent = result.total === 1
          ? '1 matching project.'
          : `${result.total} matching projects${result.total > shown ? `; showing the first ${shown}` : ''}.`;
      } else {
        this.status.textContent = `${result.total} projects available. Type to search by name or case file number.`;
      }

      this.setActiveIndex(this.activeIndex);
    }

    handleKeydown(event) {
      switch (event.key) {
        case 'ArrowDown':
          event.preventDefault();
          this.open();
          this.moveActive(1);
          break;
        case 'ArrowUp':
          event.preventDefault();
          this.open();
          this.moveActive(-1);
          break;
        case 'Home':
          if (this.isOpen && this.filteredItems.length) {
            event.preventDefault();
            this.setActiveIndex(0);
          }
          break;
        case 'End':
          if (this.isOpen && this.filteredItems.length) {
            event.preventDefault();
            this.setActiveIndex(this.filteredItems.length - 1);
          }
          break;
        case 'Enter':
          if (this.isOpen && this.activeIndex >= 0) {
            event.preventDefault();
            const item = this.filteredItems[this.activeIndex];
            if (item && !item.disabled) this.chooseItem(item);
          }
          break;
        case 'Escape':
          if (this.isOpen) {
            event.preventDefault();
            this.close({ restoreSelection: true });
            this.input.select();
          }
          break;
        case 'Tab':
          this.close({ restoreSelection: true });
          break;
        default:
          break;
      }
    }

    moveActive(delta) {
      if (!this.filteredItems.length) return;
      let next = this.activeIndex;
      for (let attempts = 0; attempts < this.filteredItems.length; attempts += 1) {
        next = (next + delta + this.filteredItems.length) % this.filteredItems.length;
        if (!this.filteredItems[next]?.disabled) {
          this.setActiveIndex(next);
          return;
        }
      }
    }

    setActiveIndex(index) {
      if (!Number.isInteger(index) || index < 0 || index >= this.filteredItems.length) {
        this.activeIndex = -1;
      } else {
        this.activeIndex = index;
      }

      this.listbox.querySelectorAll('[role="option"]').forEach((option) => {
        const isActive = Number(option.dataset.resultIndex) === this.activeIndex;
        option.classList.toggle('is-active', isActive);
        if (isActive) option.scrollIntoView({ block: 'nearest' });
      });
      this.updateActiveDescendant();
    }

    updateActiveDescendant() {
      if (this.activeIndex >= 0) {
        this.input.setAttribute('aria-activedescendant', `${this.optionIdPrefix}-${this.activeIndex}`);
      } else {
        this.input.removeAttribute('aria-activedescendant');
      }
    }

    open() {
      if (this.select.disabled) return;
      if (!this.isOpen) {
        this.isOpen = true;
        this.root.classList.add('is-open');
        this.popup.hidden = false;
        this.input.setAttribute('aria-expanded', 'true');
        this.toggleButton.setAttribute('aria-expanded', 'true');
        this.toggleButton.setAttribute('aria-label', 'Hide project options');
      }
      this.renderResults(this.getSearchQuery());
    }

    close({ restoreSelection = false } = {}) {
      if (restoreSelection) this.input.value = this.selectedLabel;
      this.isOpen = false;
      this.root.classList.remove('is-open');
      this.popup.hidden = true;
      this.input.setAttribute('aria-expanded', 'false');
      this.toggleButton.setAttribute('aria-expanded', 'false');
      this.toggleButton.setAttribute('aria-label', 'Show project options');
      this.input.removeAttribute('aria-activedescendant');
      this.activeIndex = -1;
    }

    chooseItem(item) {
      this.setValue(item.value, { dispatchChange: true, remember: true });
      this.close();
      this.input.focus();
      this.input.select();
    }

    setValue(value, { dispatchChange = false, remember = false } = {}) {
      const nextValue = String(value ?? '');
      const previous = String(this.select.value ?? '');
      this.select.value = nextValue;
      this.syncFromSelect({ force: true });

      if (remember && nextValue) writeRecentId(nextValue);
      if (dispatchChange && previous !== nextValue) {
        this.select.dispatchEvent(new Event('change', { bubbles: true }));
      }
    }

    syncFromSelect({ force = false } = {}) {
      const value = String(this.select.value ?? '');
      if (!force && value === this.lastObservedValue && value === this.selectedValue) {
        this.syncVisualState();
        return;
      }

      this.lastObservedValue = value;
      this.selectedValue = value;
      this.selectedLabel = this.items.find((item) => item.value === value)?.label || '';
      if (!this.isOpen || !this.input.matches(':focus')) {
        this.input.value = this.selectedLabel;
      }
      this.clearButton.hidden = !value || this.select.disabled;
      this.syncVisualState();
      if (this.isOpen) this.renderResults(this.getSearchQuery());
    }

    syncVisualState() {
      const isInvalid = this.select.classList.contains('is-invalid')
        || this.select.getAttribute('aria-invalid') === 'true';
      const describedBy = unique([
        this.select.getAttribute('aria-describedby'),
        this.statusId
      ]).join(' ');

      this.root.classList.toggle('is-disabled', this.select.disabled);
      this.input.disabled = this.select.disabled;
      this.toggleButton.disabled = this.select.disabled;
      this.clearButton.disabled = this.select.disabled;
      this.clearButton.hidden = !this.select.value || this.select.disabled;
      this.input.classList.toggle('is-invalid', isInvalid);
      this.input.setAttribute('aria-invalid', isInvalid ? 'true' : 'false');
      this.input.setAttribute('aria-describedby', describedBy);
      this.input.toggleAttribute('aria-required', this.select.required);
      if (this.select.disabled && this.isOpen) this.close({ restoreSelection: true });
    }
  }

  function enhanceSelect(select) {
    if (!shouldEnhance(select)) return null;
    try {
      return new ProjectPicker(select);
    } catch (error) {
      console.error('Project picker initialisation failed.', error);
      return null;
    }
  }

  function enhanceWithin(root) {
    if (!(root instanceof Element || root instanceof Document)) return;
    if (root instanceof HTMLSelectElement) enhanceSelect(root);
    root.querySelectorAll?.('select').forEach(enhanceSelect);
  }

  enhanceWithin(pageRoot);

  const pageObserver = new MutationObserver((mutations) => {
    mutations.forEach((mutation) => {
      mutation.addedNodes.forEach((node) => {
        if (node instanceof Element) enhanceWithin(node);
      });
    });
  });
  pageObserver.observe(pageRoot, { childList: true, subtree: true });

  window.PRISMProjectPicker = Object.freeze({
    enhance: enhanceSelect,
    enhanceWithin,
    getInstance: (select) => instances.get(select) ?? null
  });
})();
