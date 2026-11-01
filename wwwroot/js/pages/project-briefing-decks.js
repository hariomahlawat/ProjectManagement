import { prismConfirm } from './project-briefing-confirm.js';

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

  // Dedicated settings drawer with canonical dirty-state tracking.
  const settingsLauncher = root.querySelector('[data-pbd-settings-open]');
  const settingsDrawer = root.querySelector('[data-pbd-settings-drawer]');
  const settingsBackdrop = root.querySelector('[data-pbd-settings-backdrop]');
  const settingsForm = root.querySelector('[data-pbd-settings-form]');
  const settingsSave = root.querySelector('[data-pbd-settings-save]');
  const settingsDirtyBadge = root.querySelector('[data-pbd-settings-dirty]');
  const settingsStatus = root.querySelector('[data-pbd-settings-status]');
  let settingsInitialState = '';
  let settingsDirty = false;
  let settingsReturnFocus = null;
  const settingsCollapsibleSections = settingsDrawer
    ? [...settingsDrawer.querySelectorAll('[data-pbd-settings-collapsible]')]
    : [];
  let restoringSettingsSections = false;
  const currentSettingsLayout = () => root.querySelector('input[name="Layout"]:checked')?.value || 'StandardBriefing';
  const settingsSectionStorageKey = () => `${storagePrefix}settingsSections:${currentSettingsLayout()}`;
  const defaultOpenSettingsSections = () => new Set(['content']);

  const persistSettingsSectionState = () => {
    if (restoringSettingsSections) return;
    const openSections = settingsCollapsibleSections
      .filter((section) => !section.hidden && section.open)
      .map((section) => section.dataset.pbdSettingsSection)
      .filter(Boolean);
    sessionStorage.setItem(settingsSectionStorageKey(), JSON.stringify(openSections));
  };

  const restoreSettingsSectionState = () => {
    if (settingsCollapsibleSections.length === 0) return;
    let openSections = defaultOpenSettingsSections();
    const saved = sessionStorage.getItem(settingsSectionStorageKey());
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        if (Array.isArray(parsed)) openSections = new Set(parsed.map(String));
      } catch {
        sessionStorage.removeItem(settingsSectionStorageKey());
      }
    }
    restoringSettingsSections = true;
    settingsCollapsibleSections.forEach((section) => {
      const key = section.dataset.pbdSettingsSection || '';
      section.open = !section.hidden && openSections.has(key);
    });
    restoringSettingsSections = false;
  };

  settingsCollapsibleSections.forEach((section) => section.addEventListener('toggle', persistSettingsSectionState));

  const updateRowList = root.querySelector('[data-pbd-update-row-list]');
  const updateRowOrderInput = root.querySelector('[data-pbd-update-row-order]');
  const updateRowValidation = root.querySelector('[data-pbd-update-rows-validation]');
  const updateRowSelectAll = root.querySelector('[data-pbd-update-rows-select-all]');
  const updateRowReset = root.querySelector('[data-pbd-update-rows-reset]');
  const updateRowInitialOrder = updateRowOrderInput?.value || '';
  const updateRowRecommendedOrder = updateRowOrderInput?.dataset.recommendedOrder || updateRowInitialOrder;
  let settingsValid = true;
  let updateRowsValid = true;
  let draggedUpdateRow = null;

  const applySettingsValidity = () => {
    settingsValid = updateRowsValid;
    if (settingsSave) settingsSave.disabled = !settingsDirty || !settingsValid;
  };

  const updateRowElements = () => updateRowList
    ? [...updateRowList.querySelectorAll('[data-pbd-update-row]')]
    : [];
  const selectedUpdateSheetRowKeys = () => new Set(updateRowElements()
    .filter((row) => row.querySelector('input[name="UpdateSheetRows"]')?.checked)
    .map((row) => row.dataset.rowKey || '')
    .filter(Boolean));
  const updateSheetRowIsSelected = (key) => selectedUpdateSheetRowKeys().has(key);

  const syncUpdateRowOrder = () => {
    const rows = updateRowElements();
    if (updateRowOrderInput) {
      updateRowOrderInput.value = rows.map((row) => row.dataset.rowKey || '').filter(Boolean).join(',');
    }
    rows.forEach((row, index) => {
      row.classList.toggle('is-selected', Boolean(row.querySelector('input[name="UpdateSheetRows"]')?.checked));
      const up = row.querySelector('[data-pbd-update-row-up]');
      const down = row.querySelector('[data-pbd-update-row-down]');
      if (up instanceof HTMLButtonElement) up.disabled = index === 0;
      if (down instanceof HTMLButtonElement) down.disabled = index === rows.length - 1;
      row.setAttribute('aria-posinset', String(index + 1));
      row.setAttribute('aria-setsize', String(rows.length));
    });
  };

  const restoreUpdateRowOrder = (orderValue) => {
    if (!updateRowList) return;
    const keys = String(orderValue || '').split(',').map((value) => value.trim()).filter(Boolean);
    const byKey = new Map(updateRowElements().map((row) => [row.dataset.rowKey || '', row]));
    keys.forEach((key) => {
      const row = byKey.get(key);
      if (row) updateRowList.append(row);
    });
    byKey.forEach((row, key) => {
      if (!keys.includes(key)) updateRowList.append(row);
    });
    syncUpdateRowOrder();
  };

  const validateUpdateSheetRows = ({ focus = false } = {}) => {
    const updateLayout = currentSettingsLayout() === 'ProjectUpdateSheet';
    const valid = !updateLayout || selectedUpdateSheetRowKeys().size > 0;
    updateRowsValid = valid;
    updateRowValidation?.toggleAttribute('hidden', valid);
    updateRowList?.classList.toggle('has-validation-error', !valid);
    updateRowList?.setAttribute('aria-invalid', String(!valid));
    applySettingsValidity();
    if (!valid && focus) {
      updateRowElements()[0]?.querySelector('input[name="UpdateSheetRows"]')?.focus();
    }
    return valid;
  };

  const moveUpdateRow = (row, direction) => {
    if (!updateRowList || !row) return;
    const sibling = direction < 0 ? row.previousElementSibling : row.nextElementSibling;
    if (!sibling) return;
    if (direction < 0) updateRowList.insertBefore(row, sibling);
    else updateRowList.insertBefore(sibling, row);
    syncUpdateRowOrder();
    refreshSettingsDirtyState();
  };

  updateRowElements().forEach((row) => {
    row.draggable = true;
    row.querySelector('[data-pbd-update-row-up]')?.addEventListener('click', () => moveUpdateRow(row, -1));
    row.querySelector('[data-pbd-update-row-down]')?.addEventListener('click', () => moveUpdateRow(row, 1));
    row.querySelector('input[name="UpdateSheetRows"]')?.addEventListener('change', () => {
      syncUpdateRowOrder();
      validateUpdateSheetRows();
    });
    row.addEventListener('dragstart', (event) => {
      draggedUpdateRow = row;
      row.classList.add('is-dragging');
      event.dataTransfer?.setData('text/plain', row.dataset.rowKey || '');
      if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
    });
    row.addEventListener('dragend', () => {
      draggedUpdateRow = null;
      row.classList.remove('is-dragging');
      updateRowElements().forEach((candidate) => candidate.classList.remove('is-drag-over'));
      syncUpdateRowOrder();
      refreshSettingsDirtyState();
    });
    row.addEventListener('dragover', (event) => {
      if (!draggedUpdateRow || draggedUpdateRow === row || !updateRowList) return;
      event.preventDefault();
      const rect = row.getBoundingClientRect();
      const after = event.clientY > rect.top + (rect.height / 2);
      updateRowList.insertBefore(draggedUpdateRow, after ? row.nextElementSibling : row);
      row.classList.add('is-drag-over');
    });
    row.addEventListener('dragleave', () => row.classList.remove('is-drag-over'));
  });

  updateRowSelectAll?.addEventListener('click', () => {
    updateRowElements().forEach((row) => {
      const checkbox = row.querySelector('input[name="UpdateSheetRows"]');
      if (checkbox instanceof HTMLInputElement) checkbox.checked = true;
    });
    syncUpdateRowOrder();
    validateUpdateSheetRows();
    validateInstitutionalProfile();
    refreshSettingsDirtyState();
  });

  updateRowReset?.addEventListener('click', () => {
    restoreUpdateRowOrder(updateRowRecommendedOrder);
    const recommendedKeys = new Set(String(updateRowRecommendedOrder || '')
      .split(',')
      .map((value) => value.trim())
      .filter(Boolean));
    updateRowElements().forEach((row) => {
      const checkbox = row.querySelector('input[name="UpdateSheetRows"]');
      if (checkbox instanceof HTMLInputElement) {
        checkbox.checked = recommendedKeys.has(row.dataset.rowKey || '');
      }
    });
    const hideEmpty = root.querySelector('input[name="HideEmptyUpdateSheetValues"]');
    if (hideEmpty instanceof HTMLInputElement) hideEmpty.checked = false;
    syncUpdateRowOrder();
    validateUpdateSheetRows();
    refreshSettingsDirtyState();
  });

  syncUpdateRowOrder();

  // Dedicated editor for optional, template-driven additional slides.
  const profileDrawer = root.querySelector('[data-pbd-profile-drawer]');
  const profileBackdrop = root.querySelector('[data-pbd-profile-backdrop]');
  const profileForm = root.querySelector('[data-pbd-profile-form]');
  const profileSave = root.querySelector('[data-pbd-profile-save]');
  const profileStatus = root.querySelector('[data-pbd-profile-status]');
  let profileInitialState = '';
  let profileDirty = false;
  let profileValid = true;
  let profileReturnFocus = null;

  const serializeForm = (form) => form instanceof HTMLFormElement
    ? [...new FormData(form).entries()]
      .filter(([name]) => name !== 'RowVersion')
      .map(([name, value]) => `${name}=${String(value).trim()}`)
      .sort()
      .join('&')
    : '';

  const setProfileDirty = (dirty) => {
    profileDirty = dirty;
    if (profileSave) profileSave.disabled = !dirty || !profileValid;
    if (profileStatus) {
      profileStatus.textContent = dirty ? 'Unsaved profile changes' : 'No unsaved profile changes';
      profileStatus.classList.toggle('is-dirty', dirty);
      profileStatus.classList.remove('is-saving');
    }
  };

  const refreshProfileDirtyState = () => setProfileDirty(serializeForm(profileForm) !== profileInitialState);

  // Optional SDD institutional-profile slide.
  const institutionalProfileEnable = root.querySelector('[data-pbd-institutional-profile-enable]');
  const institutionalProfileSettings = root.querySelector('[data-pbd-institutional-profile-settings]');
  const institutionalHistoryEnable = root.querySelector('[data-pbd-institutional-history-enable]');
  const institutionalHistoryFields = root.querySelector('[data-pbd-institutional-history-fields]');
  const institutionalFooterEnable = root.querySelector('[data-pbd-institutional-footer-enable]');
  const institutionalFooterFields = root.querySelector('[data-pbd-institutional-footer-fields]');
  const institutionalFooterValidation = root.querySelector('[data-pbd-institutional-footer-validation]');
  const institutionalModuleList = root.querySelector('[data-pbd-institutional-module-list]');
  const institutionalModuleOrderInput = root.querySelector('[data-pbd-institutional-module-order]');
  const institutionalProfileValidation = root.querySelector('[data-pbd-institutional-profile-validation]');
  const institutionalLayoutSummary = root.querySelector('[data-pbd-institutional-layout-summary]');
  const institutionalHistoryEditor = root.querySelector('[data-pbd-institutional-history-editor]');
  const institutionalHistoryList = root.querySelector('[data-pbd-institutional-history-list]');
  const institutionalHistoryValue = root.querySelector('[data-pbd-institutional-history-value]');
  const institutionalHistoryValidation = root.querySelector('[data-pbd-institutional-history-validation]');
  const institutionalPartnershipEditor = root.querySelector('[data-pbd-institutional-partnership-editor]');
  const institutionalPartnershipList = root.querySelector('[data-pbd-institutional-partnership-list]');
  const institutionalPartnershipValue = root.querySelector('[data-pbd-institutional-partnership-value]');
  const institutionalPartnershipValidation = root.querySelector('[data-pbd-institutional-partnership-validation]');
  const institutionalModuleInitialOrder = institutionalModuleOrderInput?.value || '';
  const institutionalHistoryInitialValue = institutionalHistoryValue?.value || '';
  const institutionalPartnershipInitialValue = institutionalPartnershipValue?.value || '';
  let draggedInstitutionalModule = null;

  const createInstitutionalActionButton = (attribute, label, icon, extraClass = '') => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `btn btn-sm btn-light ${extraClass}`.trim();
    button.setAttribute(attribute, '');
    button.setAttribute('aria-label', label);
    button.innerHTML = `<i class="bi ${icon}" aria-hidden="true"></i>`;
    return button;
  };

  const createInstitutionalHistoryItem = (year = '', text = '') => {
    const row = document.createElement('div');
    row.className = 'pbd-institutional-list-editor__item';
    row.setAttribute('data-pbd-institutional-history-item', '');

    const handle = document.createElement('span');
    handle.className = 'pbd-institutional-list-editor__handle';
    handle.setAttribute('aria-hidden', 'true');
    handle.innerHTML = '<i class="bi bi-grip-vertical"></i>';

    const yearInput = document.createElement('input');
    yearInput.className = 'form-control pbd-institutional-list-editor__year';
    yearInput.type = 'number';
    yearInput.min = '1900';
    yearInput.max = '2200';
    yearInput.value = String(year || '');
    yearInput.setAttribute('aria-label', 'Milestone year');
    yearInput.setAttribute('data-pbd-institutional-history-year', '');

    const textInput = document.createElement('input');
    textInput.className = 'form-control';
    textInput.maxLength = 100;
    textInput.value = String(text || '');
    textInput.setAttribute('aria-label', 'Milestone description');
    textInput.setAttribute('data-pbd-institutional-history-text', '');

    const actions = document.createElement('span');
    actions.className = 'pbd-institutional-list-editor__actions';
    actions.append(
      createInstitutionalActionButton('data-pbd-list-up', 'Move milestone up', 'bi-arrow-up'),
      createInstitutionalActionButton('data-pbd-list-down', 'Move milestone down', 'bi-arrow-down'),
      createInstitutionalActionButton('data-pbd-list-remove', 'Remove milestone', 'bi-trash', 'text-danger')
    );

    row.append(handle, yearInput, textInput, actions);
    return row;
  };

  const createInstitutionalPartnershipItem = (text = '') => {
    const row = document.createElement('div');
    row.className = 'pbd-institutional-list-editor__item pbd-institutional-list-editor__item--partner';
    row.setAttribute('data-pbd-institutional-partnership-item', '');

    const handle = document.createElement('span');
    handle.className = 'pbd-institutional-list-editor__handle';
    handle.setAttribute('aria-hidden', 'true');
    handle.innerHTML = '<i class="bi bi-grip-vertical"></i>';

    const textInput = document.createElement('input');
    textInput.className = 'form-control';
    textInput.maxLength = 80;
    textInput.value = String(text || '');
    textInput.setAttribute('aria-label', 'MoU or partner name');
    textInput.setAttribute('data-pbd-institutional-partnership-text', '');

    const actions = document.createElement('span');
    actions.className = 'pbd-institutional-list-editor__actions';
    actions.append(
      createInstitutionalActionButton('data-pbd-list-up', 'Move partner up', 'bi-arrow-up'),
      createInstitutionalActionButton('data-pbd-list-down', 'Move partner down', 'bi-arrow-down'),
      createInstitutionalActionButton('data-pbd-list-remove', 'Remove partner', 'bi-trash', 'text-danger')
    );

    row.append(handle, textInput, actions);
    return row;
  };

  const updateInstitutionalListButtons = (list, itemSelector) => {
    if (!list) return;
    const rows = [...list.querySelectorAll(itemSelector)];
    rows.forEach((row, index) => {
      const up = row.querySelector('[data-pbd-list-up]');
      const down = row.querySelector('[data-pbd-list-down]');
      if (up instanceof HTMLButtonElement) up.disabled = index === 0;
      if (down instanceof HTMLButtonElement) down.disabled = index === rows.length - 1;
    });
  };

  const institutionalHistoryCount = () => institutionalHistoryList
    ? [...institutionalHistoryList.querySelectorAll('[data-pbd-institutional-history-item]')]
      .filter((row) => {
        const year = Number(row.querySelector('[data-pbd-institutional-history-year]')?.value || 0);
        const text = row.querySelector('[data-pbd-institutional-history-text]')?.value?.trim() || '';
        return year >= 1900 && year <= 2200 && Boolean(text);
      }).length
    : 0;

  const institutionalPartnershipCount = () => institutionalPartnershipList
    ? [...institutionalPartnershipList.querySelectorAll('[data-pbd-institutional-partnership-item]')]
      .filter((row) => Boolean(row.querySelector('[data-pbd-institutional-partnership-text]')?.value?.trim()))
      .length
    : 0;

  const syncInstitutionalHistoryEditor = () => {
    if (!institutionalHistoryList || !(institutionalHistoryValue instanceof HTMLTextAreaElement)) return;
    const lines = [...institutionalHistoryList.querySelectorAll('[data-pbd-institutional-history-item]')]
      .map((row) => {
        const year = row.querySelector('[data-pbd-institutional-history-year]')?.value?.trim() || '';
        const text = row.querySelector('[data-pbd-institutional-history-text]')?.value?.trim() || '';
        return year && text ? `${year} | ${text}` : '';
      })
      .filter(Boolean);
    institutionalHistoryValue.value = lines.join('\n');
    updateInstitutionalListButtons(institutionalHistoryList, '[data-pbd-institutional-history-item]');
  };

  const syncInstitutionalPartnershipEditor = () => {
    if (!institutionalPartnershipList || !(institutionalPartnershipValue instanceof HTMLTextAreaElement)) return;
    const lines = [...institutionalPartnershipList.querySelectorAll('[data-pbd-institutional-partnership-item]')]
      .map((row) => row.querySelector('[data-pbd-institutional-partnership-text]')?.value?.trim() || '')
      .filter(Boolean);
    institutionalPartnershipValue.value = lines.join('\n');
    updateInstitutionalListButtons(institutionalPartnershipList, '[data-pbd-institutional-partnership-item]');
  };

  const restoreInstitutionalHistoryEditor = (value) => {
    if (!institutionalHistoryList) return;
    institutionalHistoryList.replaceChildren();
    String(value || '').split(/\r?\n/).map((line) => line.trim()).filter(Boolean).forEach((line) => {
      const separator = line.indexOf('|');
      if (separator <= 0) return;
      institutionalHistoryList.append(createInstitutionalHistoryItem(
        line.slice(0, separator).trim(),
        line.slice(separator + 1).trim()));
    });
    syncInstitutionalHistoryEditor();
  };

  const restoreInstitutionalPartnershipEditor = (value) => {
    if (!institutionalPartnershipList) return;
    institutionalPartnershipList.replaceChildren();
    String(value || '').split(/\r?\n/).map((line) => line.trim()).filter(Boolean).forEach((line) => {
      institutionalPartnershipList.append(createInstitutionalPartnershipItem(line));
    });
    syncInstitutionalPartnershipEditor();
  };

  const moveInstitutionalListItem = (row, direction) => {
    const list = row?.parentElement;
    if (!list) return;
    const sibling = direction < 0 ? row.previousElementSibling : row.nextElementSibling;
    if (!sibling) return;
    if (direction < 0) list.insertBefore(row, sibling);
    else list.insertBefore(sibling, row);
  };

  const institutionalModuleElements = () => institutionalModuleList
    ? [...institutionalModuleList.querySelectorAll('[data-pbd-institutional-module]')]
    : [];

  const selectedInstitutionalModuleCount = () => institutionalModuleElements()
    .filter((row) => row.querySelector('input[name="InstitutionalModules"]')?.checked)
    .length;

  const syncInstitutionalModuleOrder = () => {
    const rows = institutionalModuleElements();
    if (institutionalModuleOrderInput) {
      institutionalModuleOrderInput.value = rows
        .map((row) => row.dataset.moduleKey || '')
        .filter(Boolean)
        .join(',');
    }
    rows.forEach((row, index) => {
      row.classList.toggle('is-selected', Boolean(row.querySelector('input[name="InstitutionalModules"]')?.checked));
      const up = row.querySelector('[data-pbd-institutional-module-up]');
      const down = row.querySelector('[data-pbd-institutional-module-down]');
      if (up instanceof HTMLButtonElement) up.disabled = index === 0;
      if (down instanceof HTMLButtonElement) down.disabled = index === rows.length - 1;
      row.setAttribute('aria-posinset', String(index + 1));
      row.setAttribute('aria-setsize', String(rows.length));
    });
  };

  const restoreInstitutionalModuleOrder = (orderValue) => {
    if (!institutionalModuleList) return;
    const keys = String(orderValue || '').split(',').map((value) => value.trim()).filter(Boolean);
    const byKey = new Map(institutionalModuleElements().map((row) => [row.dataset.moduleKey || '', row]));
    keys.forEach((key) => {
      const row = byKey.get(key);
      if (row) institutionalModuleList.append(row);
    });
    byKey.forEach((row, key) => {
      if (!keys.includes(key)) institutionalModuleList.append(row);
    });
    syncInstitutionalModuleOrder();
  };

  const selectedInstitutionalModuleKeys = () => institutionalModuleElements()
    .filter((row) => row.querySelector('input[name="InstitutionalModules"]')?.checked)
    .map((row) => row.dataset.moduleKey || '')
    .filter(Boolean);

  const syncInstitutionalLayoutSummary = () => {
    if (!institutionalLayoutSummary) return;
    const keys = selectedInstitutionalModuleKeys();
    const parts = [];
    if (institutionalHistoryEnable?.checked) parts.push('original alternating history timeline');
    if (keys.length > 0) parts.push(`${keys.length} institutional card${keys.length === 1 ? '' : 's'} in the selected order`);
    if (institutionalFooterEnable?.checked) parts.push('custom footer strip');
    institutionalLayoutSummary.textContent = parts.length > 0
      ? `SDD profile composition: ${parts.join(' · ')}. ERP figures remain read-only and no selected module is omitted.`
      : 'Select the timeline or at least one output module.';
  };

  const validateInstitutionalProfile = ({ focus = false } = {}) => {
    const enabled = Boolean(institutionalProfileEnable?.checked);
    const historySelected = Boolean(institutionalHistoryEnable?.checked);
    const historyValid = !historySelected || institutionalHistoryCount() > 0;
    const selectedKeys = selectedInstitutionalModuleKeys();
    const hasContent = historySelected || selectedKeys.length > 0;
    const partnershipSelected = selectedKeys.includes('Partnerships');
    const partnershipValid = !partnershipSelected || institutionalPartnershipCount() > 0;
    const footerSelected = Boolean(institutionalFooterEnable?.checked);
    const footerText = root.querySelector('input[name="InstitutionalFooterStripText"]')?.value?.trim() || '';
    const footerValue = root.querySelector('input[name="InstitutionalFooterStripEmphasisValue"]')?.value?.trim() || '';
    const footerValid = !footerSelected || Boolean(footerText || footerValue);
    const valid = !enabled || (hasContent && historyValid && partnershipValid && footerValid);
    profileValid = valid;
    institutionalProfileValidation?.toggleAttribute('hidden', !enabled || hasContent);
    institutionalHistoryValidation?.toggleAttribute('hidden', !enabled || historyValid);
    institutionalPartnershipValidation?.toggleAttribute('hidden', !enabled || partnershipValid);
    institutionalFooterValidation?.toggleAttribute('hidden', !enabled || footerValid);
    institutionalModuleList?.classList.toggle('has-validation-error', enabled && !hasContent);
    institutionalModuleList?.setAttribute('aria-invalid', String(enabled && !hasContent));
    institutionalHistoryEditor?.classList.toggle('has-validation-error', enabled && !historyValid);
    institutionalPartnershipEditor?.classList.toggle('has-validation-error', enabled && !partnershipValid);
    syncInstitutionalLayoutSummary();
    if (profileSave) profileSave.disabled = !profileDirty || !profileValid;
    if (!valid && focus) {
      if (!historyValid) institutionalHistoryEditor?.querySelector('input')?.focus();
      else if (!partnershipValid) institutionalPartnershipEditor?.querySelector('input')?.focus();
      else if (!footerValid) root.querySelector('input[name="InstitutionalFooterStripText"]')?.focus();
      else institutionalModuleElements()[0]?.querySelector('input[name="InstitutionalModules"]')?.focus();
    }
    return valid;
  };

  const syncInstitutionalProfileSettings = () => {
    // Inclusion is controlled exclusively from the Additional Slides workspace.
    // Keep the focused editor available so a disabled slide can be configured before it is enabled.
    if (institutionalProfileSettings) institutionalProfileSettings.hidden = false;
    if (institutionalHistoryFields) institutionalHistoryFields.hidden = !institutionalHistoryEnable?.checked;
    if (institutionalFooterFields) institutionalFooterFields.hidden = !institutionalFooterEnable?.checked;
    validateInstitutionalProfile();
  };

  const moveInstitutionalModule = (row, direction) => {
    if (!institutionalModuleList || !row) return;
    const sibling = direction < 0 ? row.previousElementSibling : row.nextElementSibling;
    if (!sibling) return;
    if (direction < 0) institutionalModuleList.insertBefore(row, sibling);
    else institutionalModuleList.insertBefore(sibling, row);
    syncInstitutionalModuleOrder();
    refreshProfileDirtyState();
  };

  institutionalModuleElements().forEach((row) => {
    row.draggable = true;
    row.querySelector('[data-pbd-institutional-module-up]')?.addEventListener('click', () => moveInstitutionalModule(row, -1));
    row.querySelector('[data-pbd-institutional-module-down]')?.addEventListener('click', () => moveInstitutionalModule(row, 1));
    row.querySelector('input[name="InstitutionalModules"]')?.addEventListener('change', () => {
      syncInstitutionalModuleOrder();
      validateInstitutionalProfile();
    });
    row.addEventListener('dragstart', (event) => {
      draggedInstitutionalModule = row;
      row.classList.add('is-dragging');
      event.dataTransfer?.setData('text/plain', row.dataset.moduleKey || '');
      if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
    });
    row.addEventListener('dragend', () => {
      draggedInstitutionalModule = null;
      row.classList.remove('is-dragging');
      institutionalModuleElements().forEach((candidate) => candidate.classList.remove('is-drag-over'));
      syncInstitutionalModuleOrder();
      refreshProfileDirtyState();
    });
    row.addEventListener('dragover', (event) => {
      if (!draggedInstitutionalModule || draggedInstitutionalModule === row || !institutionalModuleList) return;
      event.preventDefault();
      const rect = row.getBoundingClientRect();
      const after = event.clientY > rect.top + (rect.height / 2);
      institutionalModuleList.insertBefore(draggedInstitutionalModule, after ? row.nextElementSibling : row);
      row.classList.add('is-drag-over');
    });
    row.addEventListener('dragleave', () => row.classList.remove('is-drag-over'));
  });

  institutionalHistoryList?.addEventListener('input', () => {
    syncInstitutionalHistoryEditor();
    validateInstitutionalProfile();
    refreshProfileDirtyState();
  });
  institutionalHistoryList?.addEventListener('click', (event) => {
    const button = event.target.closest('button');
    const row = event.target.closest('[data-pbd-institutional-history-item]');
    if (!(button instanceof HTMLButtonElement) || !row) return;
    if (button.hasAttribute('data-pbd-list-remove')) row.remove();
    else if (button.hasAttribute('data-pbd-list-up')) moveInstitutionalListItem(row, -1);
    else if (button.hasAttribute('data-pbd-list-down')) moveInstitutionalListItem(row, 1);
    else return;
    syncInstitutionalHistoryEditor();
    validateInstitutionalProfile();
    refreshProfileDirtyState();
  });
  root.querySelector('[data-pbd-institutional-history-add]')?.addEventListener('click', () => {
    if (!institutionalHistoryList || institutionalHistoryList.children.length >= 8) return;
    const row = createInstitutionalHistoryItem();
    institutionalHistoryList.append(row);
    syncInstitutionalHistoryEditor();
    validateInstitutionalProfile();
    refreshProfileDirtyState();
    row.querySelector('[data-pbd-institutional-history-year]')?.focus();
  });

  institutionalPartnershipList?.addEventListener('input', () => {
    syncInstitutionalPartnershipEditor();
    validateInstitutionalProfile();
    refreshProfileDirtyState();
  });
  institutionalPartnershipList?.addEventListener('click', (event) => {
    const button = event.target.closest('button');
    const row = event.target.closest('[data-pbd-institutional-partnership-item]');
    if (!(button instanceof HTMLButtonElement) || !row) return;
    if (button.hasAttribute('data-pbd-list-remove')) row.remove();
    else if (button.hasAttribute('data-pbd-list-up')) moveInstitutionalListItem(row, -1);
    else if (button.hasAttribute('data-pbd-list-down')) moveInstitutionalListItem(row, 1);
    else return;
    syncInstitutionalPartnershipEditor();
    validateInstitutionalProfile();
    refreshProfileDirtyState();
  });
  root.querySelector('[data-pbd-institutional-partnership-add]')?.addEventListener('click', () => {
    if (!institutionalPartnershipList || institutionalPartnershipList.children.length >= 8) return;
    const row = createInstitutionalPartnershipItem();
    institutionalPartnershipList.append(row);
    syncInstitutionalPartnershipEditor();
    validateInstitutionalProfile();
    refreshProfileDirtyState();
    row.querySelector('[data-pbd-institutional-partnership-text]')?.focus();
  });

  institutionalProfileEnable?.addEventListener('change', syncInstitutionalProfileSettings);
  institutionalHistoryEnable?.addEventListener('change', syncInstitutionalProfileSettings);
  institutionalFooterEnable?.addEventListener('change', syncInstitutionalProfileSettings);
  syncInstitutionalHistoryEditor();
  syncInstitutionalPartnershipEditor();
  syncInstitutionalModuleOrder();
  syncInstitutionalProfileSettings();
  profileInitialState = serializeForm(profileForm);
  setProfileDirty(false);

  const focusableProfileElements = () => profileDrawer
    ? [...profileDrawer.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])')]
      .filter((element) => !element.hidden && element.getClientRects().length > 0)
    : [];

  const openProfileDrawer = (launcher) => {
    if (!profileDrawer || !profileBackdrop) return;
    if (settingsDirty) {
      openSettingsDrawer();
      if (settingsStatus) {
        settingsStatus.textContent = 'Save or discard deck settings before editing the SDD Institutional Profile.';
        settingsStatus.classList.add('is-dirty');
      }
      return;
    }
    const launchedFromSettings = Boolean(launcher && settingsDrawer?.contains(launcher));
    if (settingsDrawer?.classList.contains('is-open')) closeSettingsDrawer({ discard: false, force: true });
    profileReturnFocus = launchedFromSettings ? settingsLauncher : (launcher || document.activeElement);
    profileDrawer.classList.add('is-open');
    profileDrawer.setAttribute('aria-hidden', 'false');
    root.querySelectorAll('[data-pbd-profile-open]').forEach((button) => button.setAttribute('aria-expanded', 'true'));
    profileBackdrop.hidden = false;
    document.body.classList.add('pbd-profile-drawer-open');
    window.requestAnimationFrame(() => focusableProfileElements()[0]?.focus());
  };

  const confirmProfileDiscard = async (message = 'Your changes to the SDD Institutional Profile have not been saved.') => {
    if (!profileDirty) return true;
    return prismConfirm({
      title: 'Discard unsaved changes?',
      message,
      confirmText: 'Discard changes',
      cancelText: 'Keep editing',
      tone: 'danger',
      returnFocus: document.activeElement
    });
  };

  const closeProfileDrawer = async ({ discard = true, force = false } = {}) => {
    if (!profileDrawer || !profileBackdrop) return false;
    if (profileDirty && !force && !(await confirmProfileDiscard())) return false;
    if (discard && profileDirty && profileForm instanceof HTMLFormElement) {
      profileForm.reset();
      restoreInstitutionalModuleOrder(institutionalModuleInitialOrder);
      restoreInstitutionalHistoryEditor(institutionalHistoryInitialValue);
      restoreInstitutionalPartnershipEditor(institutionalPartnershipInitialValue);
      syncInstitutionalProfileSettings();
      setProfileDirty(false);
    }
    profileDrawer.classList.remove('is-open');
    profileDrawer.setAttribute('aria-hidden', 'true');
    root.querySelectorAll('[data-pbd-profile-open]').forEach((button) => button.setAttribute('aria-expanded', 'false'));
    profileBackdrop.hidden = true;
    document.body.classList.remove('pbd-profile-drawer-open');
    if (profileReturnFocus instanceof HTMLElement) profileReturnFocus.focus();
    return true;
  };

  root.querySelectorAll('[data-pbd-profile-open]').forEach((button) =>
    button.addEventListener('click', () => openProfileDrawer(button)));
  root.querySelectorAll('[data-pbd-profile-close]').forEach((button) =>
    button.addEventListener('click', async () => { await closeProfileDrawer(); }));
  profileBackdrop?.addEventListener('click', async () => { await closeProfileDrawer(); });
  profileDrawer?.addEventListener('keydown', async (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      await closeProfileDrawer();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = focusableProfileElements();
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });
  profileForm?.addEventListener('input', refreshProfileDirtyState);
  profileForm?.addEventListener('change', refreshProfileDirtyState);
  profileForm?.addEventListener('submit', (event) => {
    syncInstitutionalHistoryEditor();
    syncInstitutionalPartnershipEditor();
    syncInstitutionalModuleOrder();
    if (!validateInstitutionalProfile({ focus: true })) {
      event.preventDefault();
      if (profileStatus) {
        profileStatus.textContent = 'Complete the highlighted profile configuration before saving.';
        profileStatus.classList.add('is-dirty');
      }
      return;
    }
    if (!profileDirty) {
      event.preventDefault();
      return;
    }
    if (profileSave) profileSave.disabled = true;
    if (profileStatus) {
      profileStatus.textContent = 'Saving profile…';
      profileStatus.classList.remove('is-dirty');
      profileStatus.classList.add('is-saving');
    }
    profileDirty = false;
  });

  if (profileDrawer?.dataset.pbdProfileReopen === 'true') {
    window.setTimeout(() => openProfileDrawer(), 0);
  }

  root.querySelectorAll('[data-pbd-additional-slide-toggle]').forEach((toggle) => {
    toggle.addEventListener('change', () => {
      const form = toggle.closest('[data-pbd-additional-slide-toggle-form]');
      const enabledInput = form?.querySelector('[data-pbd-additional-slide-enabled]');
      if (enabledInput instanceof HTMLInputElement) enabledInput.value = String(toggle.checked);
      if (form instanceof HTMLFormElement) form.submit();
    });
  });

  const stageSummaryToggle = settingsForm?.querySelector('[data-pbd-stage-summary-toggle]');
  const stageTableOption = settingsForm?.querySelector('[data-pbd-stage-table-option]');
  const stageTableToggle = settingsForm?.querySelector('[data-pbd-stage-table-toggle]');

  const syncStageSummaryOptions = () => {
    const enabled = !(stageSummaryToggle instanceof HTMLInputElement) || stageSummaryToggle.checked;
    if (stageTableToggle instanceof HTMLInputElement) stageTableToggle.disabled = !enabled;
    if (stageTableOption instanceof HTMLElement) {
      stageTableOption.classList.toggle('is-disabled', !enabled);
      stageTableOption.setAttribute('aria-disabled', String(!enabled));
    }
  };

  const serializeSettings = () => serializeForm(settingsForm);

  const setSettingsDirty = (dirty) => {
    settingsDirty = dirty;
    settingsDirtyBadge?.toggleAttribute('hidden', !dirty);
    if (settingsSave) settingsSave.disabled = !dirty || !settingsValid;
    if (settingsStatus) {
      settingsStatus.textContent = dirty ? 'Unsaved settings' : 'No unsaved changes';
      settingsStatus.classList.toggle('is-dirty', dirty);
      settingsStatus.classList.remove('is-saving');
    }
  };

  const refreshSettingsDirtyState = () => setSettingsDirty(serializeSettings() !== settingsInitialState);

  const focusableSettingsElements = () => settingsDrawer
    ? [...settingsDrawer.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])')]
        .filter((element) => !element.hidden && element.getClientRects().length > 0)
    : [];

  const openSettingsDrawer = () => {
    if (!settingsDrawer || !settingsBackdrop || !settingsLauncher) return;
    settingsReturnFocus = document.activeElement;
    settingsDrawer.classList.add('is-open');
    settingsDrawer.setAttribute('aria-hidden', 'false');
    settingsBackdrop.hidden = false;
    settingsLauncher.setAttribute('aria-expanded', 'true');
    document.body.classList.add('pbd-settings-drawer-open');
    restoreSettingsSectionState();
    window.requestAnimationFrame(() => focusableSettingsElements()[0]?.focus());
  };

  const confirmSettingsDiscard = async (message = 'Your deck-setting changes have not been saved.') => {
    if (!settingsDirty) return true;
    return prismConfirm({
      title: 'Discard unsaved changes?',
      message,
      confirmText: 'Discard changes',
      cancelText: 'Keep editing',
      tone: 'danger',
      returnFocus: document.activeElement
    });
  };

  const closeSettingsDrawer = async ({ discard = true, force = false } = {}) => {
    if (!settingsDrawer || !settingsBackdrop || !settingsLauncher) return false;
    if (settingsDirty && !force && !(await confirmSettingsDiscard())) return false;
    if (discard && settingsDirty && settingsForm instanceof HTMLFormElement) {
      settingsForm.reset();
      restoreUpdateRowOrder(updateRowInitialOrder);
      syncStageSummaryOptions();
      syncTemplateSettings();
      validateUpdateSheetRows();
      setSettingsDirty(false);
    }
    settingsDrawer.classList.remove('is-open');
    settingsDrawer.setAttribute('aria-hidden', 'true');
    settingsBackdrop.hidden = true;
    settingsLauncher.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('pbd-settings-drawer-open');
    if (settingsReturnFocus instanceof HTMLElement) settingsReturnFocus.focus();
    return true;
  };

  settingsLauncher?.addEventListener('click', openSettingsDrawer);
  root.querySelectorAll('[data-pbd-settings-close]').forEach((button) => button.addEventListener('click', async () => { await closeSettingsDrawer(); }));
  settingsBackdrop?.addEventListener('click', async () => { await closeSettingsDrawer(); });
  settingsDrawer?.addEventListener('keydown', async (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      await closeSettingsDrawer();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = focusableSettingsElements();
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });
  settingsForm?.addEventListener('input', refreshSettingsDirtyState);
  settingsForm?.addEventListener('change', refreshSettingsDirtyState);
  settingsForm?.addEventListener('submit', (event) => {
    if (!validateUpdateSheetRows({ focus: true })) {
      event.preventDefault();
      if (settingsStatus) {
        settingsStatus.textContent = 'Select at least one Project Update Sheet information row.';
        settingsStatus.classList.add('is-dirty');
      }
      return;
    }
    if (!settingsDirty) {
      event.preventDefault();
      return;
    }
    if (settingsSave) settingsSave.disabled = true;
    if (settingsStatus) {
      settingsStatus.textContent = 'Saving settings…';
      settingsStatus.classList.remove('is-dirty');
      settingsStatus.classList.add('is-saving');
    }
    settingsDirty = false;
  });
  window.addEventListener('beforeunload', (event) => {
    if (!settingsDirty && !profileDirty) return;
    event.preventDefault();
    event.returnValue = '';
  });

  const confirmSettingsNavigation = async () => {
    if (!settingsDirty) return true;
    const confirmed = await confirmSettingsDiscard('Your unsaved deck settings will be lost if you continue.');
    if (confirmed) setSettingsDirty(false);
    return confirmed;
  };

  const confirmProfileNavigation = async () => {
    if (!profileDirty) return true;
    const confirmed = await confirmProfileDiscard('Your unsaved SDD Institutional Profile changes will be lost if you continue.');
    if (confirmed) setProfileDirty(false);
    return confirmed;
  };

  document.addEventListener('click', async (event) => {
    if ((!settingsDirty && !profileDirty) || event.defaultPrevented || event.button !== 0
        || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const link = event.target.closest('a[href]');
    if (!(link instanceof HTMLAnchorElement)
        || settingsDrawer?.contains(link)
        || profileDrawer?.contains(link)) return;
    const href = link.getAttribute('href') || '';
    if (!href || href.startsWith('#') || href.startsWith('javascript:')
        || link.target === '_blank' || link.hasAttribute('download')) return;

    event.preventDefault();
    if (settingsDirty && !(await confirmSettingsNavigation())) return;
    if (profileDirty && !(await confirmProfileNavigation())) return;
    window.location.assign(link.href);
  }, true);

  document.addEventListener('submit', async (event) => {
    if (event.defaultPrevented) return;
    const submittedForm = event.target;
    if (!(submittedForm instanceof HTMLFormElement)
        || submittedForm === settingsForm
        || submittedForm === profileForm
        || submittedForm.matches('[data-pbd-generate-form]')) return;
    if (!settingsDirty && !profileDirty) return;

    event.preventDefault();
    if (settingsDirty && !(await confirmSettingsNavigation())) return;
    if (profileDirty && !(await confirmProfileNavigation())) return;
    HTMLFormElement.prototype.submit.call(submittedForm);
  }, true);

  // Preserve the user's working context for this deck.
  const restoreScroll = () => {
    const saved = Number(sessionStorage.getItem(`${storagePrefix}scroll`) || 0);
    if (saved > 0) window.requestAnimationFrame(() => window.scrollTo({ top: saved, behavior: 'auto' }));
  };
  window.addEventListener('pagehide', () => sessionStorage.setItem(`${storagePrefix}scroll`, String(window.scrollY)));
  restoreScroll();

  // The shared-deck list folds away at every width so the Studio keeps its three panes; it
  // stays open only when the person opened it in this session.
  const deckWorkspace = root.querySelector('.pbd-workspace');
  const savedDecksPanel = root.querySelector('[data-pbd-saved-decks]');
  const savedDecksToggle = root.querySelector('[data-pbd-decks-toggle]');
  const savedDecksMedia = window.matchMedia('(min-width: 0px)');
  const savedDecksStorageKey = `${storagePrefix}savedDecksOpen`;

  const setSavedDecksOpen = (open, persist = true) => {
    if (!deckWorkspace || !savedDecksPanel || !savedDecksToggle) return;
    const effectiveOpen = savedDecksMedia.matches ? open : true;
    deckWorkspace.classList.toggle('is-decks-collapsed', !effectiveOpen);
    savedDecksToggle.setAttribute('aria-expanded', String(effectiveOpen));
    const activeDeckName = savedDecksToggle.dataset.pbdActiveDeckName || 'current deck';
    savedDecksToggle.title = effectiveOpen ? 'Hide shared decks' : `Show shared decks — ${activeDeckName}`;
    savedDecksToggle.setAttribute('aria-label', effectiveOpen
      ? `Hide shared decks. Current deck: ${activeDeckName}`
      : `Show shared decks. Current deck: ${activeDeckName}`);
    const label = savedDecksToggle.querySelector('span');
    if (label) label.textContent = effectiveOpen ? 'Hide decks' : 'Shared decks';
    if (persist && savedDecksMedia.matches) {
      sessionStorage.setItem(savedDecksStorageKey, effectiveOpen ? 'true' : 'false');
    }
  };

  const syncSavedDecksForViewport = () => {
    if (!savedDecksMedia.matches) {
      setSavedDecksOpen(true, false);
      return;
    }
    const saved = sessionStorage.getItem(savedDecksStorageKey);
    setSavedDecksOpen(saved === 'true', false);
  };

  savedDecksToggle?.addEventListener('click', () => {
    const currentlyOpen = savedDecksToggle.getAttribute('aria-expanded') === 'true';
    setSavedDecksOpen(!currentlyOpen);
  });
  savedDecksMedia.addEventListener?.('change', syncSavedDecksForViewport);
  syncSavedDecksForViewport();

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


  // Add projects: a dialog over the Studio. Every selection method saves in place.
  const addProjectsModalElement = document.getElementById('pbd-add-projects-modal');
  const addProjectsModal = addProjectsModalElement && window.bootstrap
    ? window.bootstrap.Modal.getOrCreateInstance(addProjectsModalElement)
    : null;
  const addProjectsStatus = addProjectsModalElement?.querySelector('[data-pbd-add-projects-status]');
  root.querySelectorAll('[data-pbd-open-selector]').forEach((button) => button.addEventListener('click', () => {
    setState(addProjectsStatus, '');
    addProjectsModal?.show();
  }));
  addProjectsModalElement?.addEventListener('shown.bs.modal', () => {
    addProjectsModalElement.querySelector('[data-pbd-selector-tab].is-active')?.focus();
  });

  const generateButton = root.querySelector('[data-pbd-generate]');
  const activeSavedCard = root.querySelector('.pbd-saved-card.is-active small');
  const membershipStatus = root.querySelector('[data-pbd-membership-status]');

  const selectedLayout = currentSettingsLayout;
  const isUpdateSheet = () => selectedLayout() === 'ProjectUpdateSheet';
  const includesDetailedSlides = () => {
    if (isUpdateSheet()) return true;
    const value = root.querySelector('input[name="PresentationMode"]:checked')?.value;
    return value === 'DetailedProjects' || value === 'Combined';
  };
  const narrativeMode = () => isUpdateSheet() ? 'ProjectBrief' : (root.querySelector('input[name="NarrativeMode"]:checked')?.value || 'CapabilityOverview');
  const includesProjectBrief = () => isUpdateSheet() || ['ProjectBrief', 'Both'].includes(narrativeMode());

  const syncTemplateSettings = () => {
    const updateSheet = isUpdateSheet();
    root.querySelectorAll('[data-pbd-standard-section]').forEach((element) => { element.hidden = updateSheet; });
    root.querySelectorAll('[data-pbd-standard-settings]').forEach((element) => { element.hidden = updateSheet; });
    root.querySelectorAll('[data-pbd-update-settings]').forEach((element) => { element.hidden = !updateSheet; });

    const detailedSettings = root.querySelector('[data-pbd-detailed-settings]');
    if (detailedSettings) detailedSettings.hidden = updateSheet || !includesDetailedSlides();
    const projectBriefLayoutSettings = root.querySelector('[data-pbd-project-brief-layout-settings]');
    if (projectBriefLayoutSettings) {
      projectBriefLayoutSettings.hidden = updateSheet
        || !includesDetailedSlides()
        || !includesProjectBrief();
    }
    root.querySelector('[data-pbd-presentation-design]')?.classList.toggle('is-update-sheet', updateSheet);
    root.querySelectorAll('[data-pbd-theme-preview]').forEach((preview) => {
      preview.classList.toggle('is-update-sheet', updateSheet);
    });
    restoreSettingsSectionState();
    validateUpdateSheetRows();
  };
  root.querySelectorAll('[data-pbd-layout-choice], input[name="PresentationMode"], input[name="CostMode"], input[name="NarrativeMode"]')
    .forEach((choice) => choice.addEventListener('change', syncTemplateSettings));
  stageSummaryToggle?.addEventListener('change', () => {
    syncStageSummaryOptions();
    refreshSettingsDirtyState();
  });
  syncStageSummaryOptions();
  syncTemplateSettings();
  settingsInitialState = serializeSettings();
  setSettingsDirty(false);
  if (root.querySelector('[data-pbd-open-settings="true"]')) {
    window.setTimeout(openSettingsDrawer, 0);
  }

  // The Studio listens for this and rebuilds after any in-place change.
  const announceDeckChange = (detail = {}) => {
    document.dispatchEvent(new CustomEvent('pbd:deck-changed', { detail }));
  };

  const leftOutNotice = root.querySelector('[data-pbd-left-out-notice]');
  const leftOutText = root.querySelector('[data-pbd-left-out-text]');
  const updateLeftOutNotice = (projects) => {
    const count = projects.filter((project) => project.isDeletedOrArchived).length;
    if (leftOutNotice) leftOutNotice.hidden = count === 0;
    if (leftOutText && count > 0) {
      leftOutText.textContent = `${count} ${count === 1 ? 'project in this deck is' : 'projects in this deck are'} deleted or archived and will be left out of the PowerPoint. Remove ${count === 1 ? 'it' : 'them'} from the deck, or restore the project to include it again.`;
    }
  };

  const applyEditorState = (deck) => {
    if (!deck) return;
    const projects = deck.projects || [];
    const presented = projects.filter((project) => !project.isDeletedOrArchived).length;
    updateRowVersion(deck.rowVersion);
    updateLeftOutNotice(projects);
    if (generateButton) generateButton.disabled = presented === 0;
    if (activeSavedCard) {
      activeSavedCard.textContent = `${projects.length} ${projects.length === 1 ? 'project' : 'projects'} · updated just now`;
    }
    announceDeckChange({ hasProjects: presented > 0, deck });
  };

  const updateMembership = async (addProjectIds = [], removeProjectIds = [], statusElement = membershipStatus) => {
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
      setState(statusElement, changes.length ? `Deck updated: ${changes.join(', ')}.` : 'No membership changes were required.', 'saved');
      return payload;
    } catch (error) {
      setState(statusElement, error.message || 'Deck membership could not be updated.', 'error');
      throw error;
    }
  };

  // Remove from deck (the Studio inspector's button); the project record is not changed.
  root.addEventListener('click', async (event) => {
    const remove = event.target.closest('[data-pbd-remove-project]');
    if (!remove) return;
    const projectId = Number(remove.dataset.projectId || 0);
    if (!projectId) return;
    const confirmed = await prismConfirm({
      title: 'Remove this project from the deck?',
      message: `${remove.dataset.projectName || 'The project'} and its slides leave this deck. The project record is not changed, and you can add it back at any time.`,
      confirmText: 'Remove project',
      cancelText: 'Keep project',
      tone: 'danger',
      returnFocus: remove
    });
    if (!confirmed) return;
    remove.disabled = true;
    try { await updateMembership([], [projectId]); }
    catch { /* the status line already explains the failure */ }
    finally { remove.disabled = false; }
  });

  // Quick selections and category selections: posted in place, the deck refreshed from the reply.
  root.addEventListener('submit', async (event) => {
    const form = event.target.closest('[data-pbd-add-selection-form]');
    if (!form) return;
    event.preventDefault();
    const button = event.submitter instanceof HTMLButtonElement ? event.submitter : form.querySelector('button[type="submit"]');
    button?.setAttribute('disabled', 'disabled');
    setState(addProjectsStatus, 'Adding projects…', 'saving');
    try {
      const body = new FormData(form);
      body.set('RowVersion', currentRowVersion());
      const response = await fetch(form.action, {
        method: 'POST',
        credentials: 'same-origin',
        body,
        headers: { Accept: 'application/json', 'X-CSRF-TOKEN': token, 'X-Requested-With': 'XMLHttpRequest' }
      });
      const payload = await response.json().catch(() => null);
      if (!response.ok) throw new Error(payload?.message || `Projects could not be added (${response.status}).`);
      applyEditorState(payload?.deck);
      setState(addProjectsStatus, payload?.message || 'Deck updated.', 'saved');
      setState(membershipStatus, payload?.message || 'Deck updated.', 'saved');
    } catch (error) {
      setState(addProjectsStatus, error.message || 'Projects could not be added.', 'error');
    } finally {
      button?.removeAttribute('disabled');
    }
  });

  // Reorder: the Studio outline asks for a new project order (within present stages).
  document.addEventListener('pbd:reorder-projects', async (event) => {
    const projectIds = (event.detail?.projectIds || []).map(Number).filter(Number.isInteger);
    if (deckId <= 0 || projectIds.length === 0) return;
    setState(membershipStatus, 'Saving slide order…', 'saving');
    try {
      const payload = await requestJson(root.dataset.reorderUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, projectIds, rowVersion: currentRowVersion() })
      });
      updateRowVersion(payload?.rowVersion);
      setState(membershipStatus, 'Slide order saved.', 'saved');
    } catch (error) {
      setState(membershipStatus, error.message || 'Slide order could not be saved.', 'error');
    }
    // Rebuild either way: the slides follow the saved order, or return to it after a failure.
    announceDeckChange();
  });

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
    if (descriptionTitle) descriptionTitle.textContent = button.dataset.projectName || 'Capability overview';
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
      // The table row and the Studio inspector both carry an editor button for the project.
      root.querySelectorAll(`[data-pbd-edit-description][data-project-id="${projectId}"]`).forEach((editorButton) => {
        editorButton.dataset.description = descriptionValue?.value || '';
      });
      setState(descriptionStatus, 'Deck-specific capability overview saved.', 'success');
      announceDeckChange(payload?.deck ? { deck: payload.deck } : {});
      window.setTimeout(() => descriptionModal?.hide(), 450);
    } catch (error) {
      setState(descriptionStatus, error.message || 'Capability overview could not be saved.', 'error');
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
    if (settingsDirty) {
      openSettingsDrawer();
      if (settingsStatus) {
        settingsStatus.textContent = 'Save or discard settings before generating the PowerPoint.';
        settingsStatus.classList.add('is-dirty');
      }
      return;
    }
    if (profileDirty) {
      openProfileDrawer();
      if (profileStatus) {
        profileStatus.textContent = 'Save or discard profile changes before generating the PowerPoint.';
        profileStatus.classList.add('is-dirty');
      }
      return;
    }
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

}
