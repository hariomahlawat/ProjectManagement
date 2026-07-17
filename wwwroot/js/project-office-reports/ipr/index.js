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

  const initialiseOffcanvas = () => {
    const element = document.getElementById('iprRecordOffcanvas');
    if (!element || typeof bootstrap === 'undefined' || !bootstrap.Offcanvas) return;

    const mode = (element.dataset.iprMode || '').toLowerCase();
    const hasForm = (element.dataset.iprHasForm || '').toLowerCase() === 'true';
    const shouldShow = mode === 'create' || mode === 'edit';
    const instance = bootstrap.Offcanvas.getOrCreateInstance(element);

    element.addEventListener('shown.bs.offcanvas', () => {
      if (hasForm) focusFirstFormError(element);
    });

    if (shouldShow) instance.show();

    element.addEventListener('hidden.bs.offcanvas', () => {
      const url = new URL(window.location.href);
      url.searchParams.delete('mode');
      url.searchParams.delete('id');
      history.replaceState({}, document.title, url.toString());
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

  const formatDate = value => {
    if (!value) return '—';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '—';
    return new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }).format(date);
  };

  const initialiseRecordInspector = () => {
    const inspector = document.querySelector('[data-ipr-record-inspector]');
    const dataElement = document.getElementById('iprRecordData');
    if (!inspector || !dataElement) return;

    let records = [];
    try {
      records = JSON.parse(dataElement.textContent || '[]');
    } catch {
      records = [];
    }
    if (!Array.isArray(records) || records.length === 0) return;

    const byId = new Map(records.map(record => [String(record.Id ?? record.id), record]));
    const rows = Array.from(document.querySelectorAll('[data-ipr-record-row]'));
    const projectBaseUrl = inspector.dataset.iprProjectBaseUrl || '/Projects/Overview';
    const storageKey = `ipr:selected:${window.location.pathname}:${new URL(window.location.href).searchParams.get('page') || '1'}`;

    const setText = (selector, value) => {
      const element = inspector.querySelector(selector);
      if (element) element.textContent = value ?? '';
    };

    const setBadge = (selector, value, baseClass, modifier) => {
      const element = inspector.querySelector(selector);
      if (!element) return;
      element.className = `${baseClass} ${baseClass}--${modifier}`;
      element.textContent = value;
    };

    const replaceProject = record => {
      const current = inspector.querySelector('[data-ipr-inspector-project]');
      if (!current) return;
      const projectId = record.ProjectId ?? record.projectId;
      const projectName = record.ProjectName ?? record.projectName ?? 'Unassigned project';
      const replacement = document.createElement(projectId ? 'a' : 'span');
      replacement.dataset.iprInspectorProject = '';
      replacement.textContent = projectName;
      if (projectId) {
        const separator = projectBaseUrl.includes('?') ? '&' : '?';
        replacement.href = `${projectBaseUrl}${separator}id=${encodeURIComponent(projectId)}`;
      } else {
        replacement.className = 'ipr-inspector-unassigned';
      }
      current.replaceWith(replacement);
    };

    const renderAttachments = record => {
      const container = inspector.querySelector('[data-ipr-inspector-attachments]');
      if (!container) return;
      container.replaceChildren();
      const attachments = record.Attachments ?? record.attachments ?? [];
      setText('[data-ipr-inspector-file-count]', String(record.AttachmentCount ?? record.attachmentCount ?? attachments.length));

      if (!attachments.length) {
        const empty = document.createElement('div');
        empty.className = 'ipr-inspector-empty-file';
        empty.innerHTML = '<i class="bi bi-paperclip" aria-hidden="true"></i><span>No attachment available</span>';
        container.append(empty);
        return;
      }

      const basePath = window.location.pathname.replace(/\/$/, '');
      attachments.forEach(attachment => {
        const link = document.createElement('a');
        const recordId = record.Id ?? record.id;
        const attachmentId = attachment.Id ?? attachment.id;
        link.href = `${basePath}/Download?iprRecordId=${encodeURIComponent(recordId)}&attachmentId=${encodeURIComponent(attachmentId)}`;
        link.target = '_blank';
        link.rel = 'noopener';

        const icon = document.createElement('i');
        icon.className = 'bi bi-file-earmark-pdf';
        icon.setAttribute('aria-hidden', 'true');
        const content = document.createElement('span');
        const name = document.createElement('strong');
        name.textContent = attachment.FileName ?? attachment.fileName ?? 'Attachment';
        const meta = document.createElement('small');
        meta.textContent = `${attachment.FileSize ?? attachment.fileSize ?? ''} · ${attachment.UploadedAt ?? attachment.uploadedAt ?? ''}`;
        content.append(name, meta);
        link.append(icon, content);
        container.append(link);
      });
    };

    const selectRecord = id => {
      const record = byId.get(String(id));
      if (!record) return;

      rows.forEach(row => {
        const selected = row.dataset.recordId === String(id);
        row.classList.toggle('is-selected', selected);
        row.setAttribute('aria-selected', selected ? 'true' : 'false');
        row.tabIndex = selected ? 0 : -1;
      });
      setText('[data-ipr-inspector-title]', record.Title ?? record.title ?? 'Untitled record');
      setText('[data-ipr-inspector-filing]', record.ApplicationNumber ?? record.applicationNumber ?? '—');
      setText('[data-ipr-inspector-filed]', formatDate(record.FiledOn ?? record.filedOn));
      setText('[data-ipr-inspector-filedby]', record.FiledBy ?? record.filedBy ?? 'Not recorded');
      setText('[data-ipr-inspector-granted]', formatDate(record.GrantedOn ?? record.grantedOn));
      setText('[data-ipr-inspector-notes]', record.ExternalRemark ?? record.externalRemark ?? 'No notes recorded.');

      const type = record.IprType ?? record.iprType ?? 'Patent';
      const status = record.Status ?? record.status ?? 'Awaiting grant';
      setBadge('[data-ipr-inspector-type]', type, 'ipr-type-badge', normalizeText(type));
      setBadge('[data-ipr-inspector-status]', status, 'ipr-status-badge', status === 'Granted' ? 'granted' : 'filed');
      replaceProject(record);
      renderAttachments(record);

      const selectedRow = rows.find(row => row.dataset.recordId === String(id));
      const editLink = inspector.querySelector('[data-ipr-inspector-edit]');
      const rowEdit = selectedRow?.querySelector('.ipr-row-edit-link');
      if (editLink && rowEdit) editLink.href = rowEdit.href;

      try { sessionStorage.setItem(storageKey, String(id)); } catch { /* storage is optional */ }
    };

    document.querySelectorAll('[data-ipr-select-record]').forEach(button => {
      button.addEventListener('click', () => selectRecord(button.dataset.recordId));
    });

    rows.forEach(row => {
      row.addEventListener('click', event => {
        if (event.target.closest('a, button, input, select, textarea')) return;
        selectRecord(row.dataset.recordId);
      });
      row.addEventListener('keydown', event => {
        if (event.target.closest('a, button, input, select, textarea')) return;
        const currentIndex = rows.indexOf(row);
        let nextIndex = currentIndex;

        if (event.key === 'ArrowDown') nextIndex = Math.min(rows.length - 1, currentIndex + 1);
        else if (event.key === 'ArrowUp') nextIndex = Math.max(0, currentIndex - 1);
        else if (event.key === 'Home') nextIndex = 0;
        else if (event.key === 'End') nextIndex = rows.length - 1;
        else if (event.key !== 'Enter' && event.key !== ' ') return;

        event.preventDefault();
        const targetRow = rows[nextIndex];
        selectRecord(targetRow.dataset.recordId);
        targetRow.focus({ preventScroll: true });
        targetRow.scrollIntoView?.({ block: 'nearest' });
      });
    });

    let initialId = rows[0]?.dataset.recordId;
    try {
      const saved = sessionStorage.getItem(storageKey);
      if (saved && byId.has(saved)) initialId = saved;
    } catch { /* storage is optional */ }
    if (initialId) selectRecord(initialId);
  };

  const initialiseInspectorResize = () => {
    const workbench = document.querySelector('.ipr-record-workbench');
    const handle = document.querySelector('[data-ipr-inspector-resize]');
    if (!workbench || !handle) return;

    const storageKey = 'ipr:inspector-width';
    const cssDefaultWidth = Number.parseInt(getComputedStyle(workbench).getPropertyValue('--ipr-inspector-width'), 10);
    const defaultWidth = Number.isFinite(cssDefaultWidth) ? cssDefaultWidth : 400;
    const getLimits = () => {
      const available = workbench.getBoundingClientRect().width;
      return {
        min: 340,
        max: Math.max(340, Math.min(520, Math.round(available * 0.42)))
      };
    };
    const clampWidth = width => {
      const limits = getLimits();
      return Math.min(limits.max, Math.max(limits.min, Math.round(width)));
    };
    const applyWidth = (width, persist = false) => {
      const resolved = clampWidth(width);
      workbench.style.setProperty('--ipr-inspector-width', `${resolved}px`);
      handle.setAttribute('aria-valuemin', String(getLimits().min));
      handle.setAttribute('aria-valuemax', String(getLimits().max));
      handle.setAttribute('aria-valuenow', String(resolved));
      if (persist) {
        try { localStorage.setItem(storageKey, String(resolved)); } catch { /* optional */ }
      }
      return resolved;
    };

    let initialWidth = defaultWidth;
    try {
      const saved = Number.parseInt(localStorage.getItem(storageKey) || '', 10);
      if (Number.isFinite(saved)) initialWidth = saved;
    } catch { /* optional */ }
    applyWidth(initialWidth);

    let dragging = false;
    const onPointerMove = event => {
      if (!dragging) return;
      const rect = workbench.getBoundingClientRect();
      applyWidth(rect.right - event.clientX);
    };
    const stopDragging = () => {
      if (!dragging) return;
      dragging = false;
      handle.classList.remove('is-dragging');
      document.body.style.removeProperty('cursor');
      document.body.style.removeProperty('user-select');
      const current = Number.parseInt(getComputedStyle(workbench).getPropertyValue('--ipr-inspector-width'), 10);
      if (Number.isFinite(current)) applyWidth(current, true);
      window.removeEventListener('pointermove', onPointerMove);
      window.removeEventListener('pointerup', stopDragging);
      window.removeEventListener('pointercancel', stopDragging);
    };

    handle.addEventListener('pointerdown', event => {
      if (window.matchMedia('(max-width: 1050px)').matches) return;
      event.preventDefault();
      dragging = true;
      handle.classList.add('is-dragging');
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
      handle.setPointerCapture?.(event.pointerId);
      window.addEventListener('pointermove', onPointerMove);
      window.addEventListener('pointerup', stopDragging);
      window.addEventListener('pointercancel', stopDragging);
    });

    handle.addEventListener('keydown', event => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight' && event.key !== 'Home') return;
      event.preventDefault();
      const current = Number.parseInt(getComputedStyle(workbench).getPropertyValue('--ipr-inspector-width'), 10) || defaultWidth;
      if (event.key === 'Home') {
        applyWidth(defaultWidth, true);
        return;
      }
      const delta = event.key === 'ArrowLeft' ? 20 : -20;
      applyWidth(current + delta, true);
    });

    handle.addEventListener('dblclick', () => applyWidth(defaultWidth, true));
    window.addEventListener('resize', () => {
      const current = Number.parseInt(getComputedStyle(workbench).getPropertyValue('--ipr-inspector-width'), 10) || defaultWidth;
      applyWidth(current);
    });
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

  const initialisePageSize = () => {
    const select = document.querySelector('[data-ipr-page-size]');
    if (!select) return;
    select.addEventListener('change', () => {
      const url = new URL(window.location.href);
      url.searchParams.set('pageSize', select.value);
      url.searchParams.set('page', '1');
      url.searchParams.delete('mode');
      url.searchParams.delete('id');
      window.location.assign(url.toString());
    });
  };

  const initialiseProjectGroups = () => {
    const container = document.querySelector('[data-ipr-project-groups]');
    const groups = Array.from(document.querySelectorAll('[data-ipr-project-group]'));
    if (!container || groups.length === 0) return;

    const search = document.querySelector('[data-ipr-project-group-search]');
    const status = document.querySelector('[data-ipr-project-group-status]');
    const sort = document.querySelector('[data-ipr-project-group-sort]');
    const expand = document.querySelector('[data-ipr-expand-projects]');
    const empty = document.querySelector('[data-ipr-project-groups-empty]');

    groups.forEach(group => {
      group.querySelector('.ipr-project-group__name[href]')?.addEventListener('click', event => event.stopPropagation());
    });

    const compare = (left, right, mode) => {
      const leftName = normalizeText(left.dataset.projectName || '');
      const rightName = normalizeText(right.dataset.projectName || '');
      const leftUnassigned = left.dataset.unassigned === 'true' ? 1 : 0;
      const rightUnassigned = right.dataset.unassigned === 'true' ? 1 : 0;
      const leftAwaiting = Number.parseInt(left.dataset.awaiting === 'true' ? '1' : '0', 10);
      const rightAwaiting = Number.parseInt(right.dataset.awaiting === 'true' ? '1' : '0', 10);
      const leftTotal = Number.parseInt(left.dataset.total || '0', 10);
      const rightTotal = Number.parseInt(right.dataset.total || '0', 10);
      const leftLatest = Date.parse(left.dataset.latest || '') || 0;
      const rightLatest = Date.parse(right.dataset.latest || '') || 0;

      if (mode === 'name') return leftName.localeCompare(rightName);
      if (mode === 'total') return rightTotal - leftTotal || leftName.localeCompare(rightName);
      if (mode === 'latest') return rightLatest - leftLatest || leftName.localeCompare(rightName);
      return rightUnassigned - leftUnassigned || rightAwaiting - leftAwaiting || leftName.localeCompare(rightName);
    };

    const sortGroups = () => {
      const mode = sort?.value || 'attention';
      [...groups]
        .sort((left, right) => compare(left, right, mode))
        .forEach(group => container.insertBefore(group, empty || null));
    };

    const updateExpansionButton = () => {
      if (!expand) return;
      const visibleGroups = groups.filter(group => !group.classList.contains('d-none'));
      const anyOpen = visibleGroups.some(group => group.open);
      expand.innerHTML = anyOpen
        ? '<i class="bi bi-arrows-collapse" aria-hidden="true"></i> Collapse all'
        : '<i class="bi bi-chevron-double-down" aria-hidden="true"></i> Expand awaiting';
    };

    const filter = () => {
      const query = normalizeText(search?.value || '');
      const position = status?.value || 'all';
      let visible = 0;

      groups.forEach(group => {
        const matchesText = !query || normalizeText(group.dataset.search || '').includes(query);
        const matchesPosition = position === 'all' ||
          (position === 'awaiting' && group.dataset.awaiting === 'true') ||
          (position === 'granted' && group.dataset.granted === 'true') ||
          (position === 'unassigned' && group.dataset.unassigned === 'true');
        const show = matchesText && matchesPosition;
        group.classList.toggle('d-none', !show);
        if (show) {
          visible += 1;
          if (query) group.open = true;
        }
      });

      empty?.classList.toggle('d-none', visible > 0);
      updateExpansionButton();
    };

    groups.forEach(group => group.addEventListener('toggle', updateExpansionButton));
    search?.addEventListener('input', filter);
    status?.addEventListener('change', filter);
    sort?.addEventListener('change', sortGroups);
    expand?.addEventListener('click', () => {
      const visibleGroups = groups.filter(group => !group.classList.contains('d-none'));
      const anyOpen = visibleGroups.some(group => group.open);
      if (anyOpen) {
        visibleGroups.forEach(group => { group.open = false; });
        updateExpansionButton();
        return;
      }

      const targets = visibleGroups.filter(group => group.dataset.awaiting === 'true' || group.dataset.unassigned === 'true');
      (targets.length > 0 ? targets : visibleGroups).forEach(group => { group.open = true; });
      updateExpansionButton();
    });

    sortGroups();
    filter();
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

  const initialiseToasts = () => {
    const container = document.getElementById('iprToastContainer');
    if (!container || typeof bootstrap === 'undefined' || !bootstrap.Toast) return;
    container.querySelectorAll('.toast').forEach(element => bootstrap.Toast.getOrCreateInstance(element).show());
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
  initialiseRecordInspector();
  initialiseInspectorResize();
  initialiseDensity();
  initialisePageSize();
  initialiseProjectGroups();
  initialiseConfirmations();
  initialiseToasts();
  initialiseAutoSubmitFilters();
  initialiseGrantedDate();
  initialiseAttachmentUpload();
})();
