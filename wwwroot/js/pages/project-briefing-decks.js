import { prismConfirm } from './project-briefing-confirm.js';

const root = document.querySelector('[data-pbd-root]');

if (root) {

  // Session storage only remembers conveniences (open sections, tab, scroll). It can be blocked
  // or full; that must never stop the builder from starting.
  const storage = {
    get: (key) => { try { return window.sessionStorage.getItem(key); } catch { return null; } },
    set: (key, value) => { try { window.sessionStorage.setItem(key, value); } catch { /* not remembered */ } },
    remove: (key) => { try { window.sessionStorage.removeItem(key); } catch { /* not remembered */ } }
  };
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

  const statusFadeTimers = new WeakMap();
  const setState = (element, message, state = '') => {
    if (!element) return;
    window.clearTimeout(statusFadeTimers.get(element));
    element.textContent = message || '';
    element.classList.remove('is-saving', 'is-saved', 'is-error', 'is-success');
    if (state) element.classList.add(`is-${state}`);
    // A confirmation under the outline ("49 projects added …") clears itself; errors stay.
    if (state === 'saved' && element.matches('[data-pbd-membership-status]')) {
      statusFadeTimers.set(element, window.setTimeout(() => {
        if (element.classList.contains('is-saved')) setState(element, '');
      }, 6000));
    }
  };

  const rowVersionInputs = [...root.querySelectorAll('input[name="RowVersion"], input[name="rowVersion"]')];
  const currentRowVersion = () => rowVersionInputs[0]?.value || '';
  const updateRowVersion = (value) => {
    if (!value) return;
    rowVersionInputs.forEach((input) => { input.value = value; });
  };

  // Every in-place change to the deck sends its row version, so they run one at a time: each
  // starts after the previous one has finished and picked up the version the server returned.
  let deckMutationTail = Promise.resolve();
  // "Saved · shared" by the deck title says what the last change did: saving, saved, or not saved.
  const savedChip = root.querySelector('[data-pbd-saved-chip]');
  const savedChipText = savedChip?.querySelector('[data-pbd-saved-chip-text]');
  let savesRunning = 0;
  const showSaveState = (state) => {
    if (!savedChip) return;
    savedChip.classList.toggle('is-saving', state === 'saving');
    savedChip.classList.toggle('is-error', state === 'error');
    if (savedChipText) {
      savedChipText.textContent = state === 'saving' ? 'Saving…' : state === 'error' ? 'Not saved' : 'Saved · shared';
    }
  };
  const runDeckMutation = (mutate) => {
    savesRunning += 1;
    showSaveState('saving');
    const run = deckMutationTail.then(() => mutate());
    deckMutationTail = run.catch(() => {});
    run.then(
      () => { savesRunning -= 1; if (!savesRunning) showSaveState('saved'); },
      () => { savesRunning -= 1; showSaveState('error'); });
    return run;
  };

  // How the deck works, said once: dismissed for good on this browser.
  const modelNote = root.querySelector('[data-pbd-model-note]');
  const modelNoteKey = 'prism.briefingDecks.modelNoteSeen';
  if (modelNote) {
    let seen = false;
    try { seen = window.localStorage.getItem(modelNoteKey) === '1'; } catch { /* storage refused: show it */ }
    modelNote.hidden = seen;
    modelNote.querySelector('[data-pbd-model-note-dismiss]')?.addEventListener('click', () => {
      try { window.localStorage.setItem(modelNoteKey, '1'); } catch { /* shown again next time */ }
      modelNote.remove();
    });
  }

  // Deck settings: the form in the Studio's Deck tab, saved in place as it changes.
  const settingsForm = root.querySelector('[data-pbd-settings-form]');
  const settingsStatus = root.querySelector('[data-pbd-settings-status]');
  const settingsCollapsibleSections = settingsForm
    ? [...settingsForm.querySelectorAll('[data-pbd-settings-collapsible]')]
    : [];
  let restoringSettingsSections = false;
  // The layout whose remembered open step is showing; other changes leave the open step alone.
  let restoredSettingsLayout = null;
  const currentSettingsLayout = () => root.querySelector('input[name="Layout"]:checked')?.value || 'StandardBriefing';
  const settingsSectionStorageKey = () => `${storagePrefix}settingsSections:${currentSettingsLayout()}`;
  // The steps open closed, so the summaries read as an overview of the deck.
  const defaultOpenSettingsSections = () => new Set();

  const persistSettingsSectionState = () => {
    if (restoringSettingsSections) return;
    const openSections = settingsCollapsibleSections
      .filter((section) => !section.hidden && section.open)
      .map((section) => section.dataset.pbdSettingsSection)
      .filter(Boolean);
    storage.set(settingsSectionStorageKey(), JSON.stringify(openSections));
  };

  const restoreSettingsSectionState = () => {
    if (settingsCollapsibleSections.length === 0) return;
    let openSections = defaultOpenSettingsSections();
    const saved = storage.get(settingsSectionStorageKey());
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        if (Array.isArray(parsed)) openSections = new Set(parsed.map(String));
      } catch {
        storage.remove(settingsSectionStorageKey());
      }
    }
    restoringSettingsSections = true;
    settingsCollapsibleSections.forEach((section) => {
      const key = section.dataset.pbdSettingsSection || '';
      section.open = !section.hidden && openSections.has(key);
    });
    restoringSettingsSections = false;
  };

  // One step open at a time: opening a step closes the others.
  settingsCollapsibleSections.forEach((section) => section.addEventListener('toggle', () => {
    if (!restoringSettingsSections && section.open) {
      settingsCollapsibleSections.forEach((other) => {
        if (other !== section && other.open) other.open = false;
      });
    }
    persistSettingsSectionState();
  }));

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
    // The profile editor saves with a page reload; let a pending settings save finish first.
    if (settingsPending()) saveSettingsNow();
    profileReturnFocus = launcher || document.activeElement;
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
  // Escape also works when focus is outside the drawer (the drawer's own handler covers inside).
  document.addEventListener('keydown', async (event) => {
    if (event.key !== 'Escape' || event.defaultPrevented) return;
    if (!profileDrawer?.classList.contains('is-open') || profileDrawer.contains(event.target)) return;
    if (document.querySelector('dialog[open]')) return;
    event.preventDefault();
    await closeProfileDrawer();
  });
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
  profileForm?.addEventListener('submit', async (event) => {
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
    // Both saves change the deck's row version, so the profile posts only after a pending
    // settings save has finished (it then carries the new row version).
    if (settingsPending()) {
      event.preventDefault();
      if (!(await saveSettingsNow())) {
        if (profileSave) profileSave.disabled = false;
        if (profileStatus) {
          profileStatus.textContent = 'The deck settings could not be saved, so the profile was not saved either.';
          profileStatus.classList.remove('is-saving');
          profileStatus.classList.add('is-dirty');
        }
        return;
      }
      profileDirty = false;
      HTMLFormElement.prototype.submit.call(profileForm);
      return;
    }
    profileDirty = false;
  });

  if (profileDrawer?.dataset.pbdProfileReopen === 'true') {
    window.setTimeout(() => openProfileDrawer(), 0);
  }

  root.querySelectorAll('[data-pbd-additional-slide-toggle]').forEach((toggle) => {
    toggle.addEventListener('change', () => {
      // The switch sits in the Extra slides step; its form is outside the settings form.
      const form = toggle.form || toggle.closest('[data-pbd-additional-slide-toggle-form]');
      const enabledInput = form?.querySelector('[data-pbd-additional-slide-enabled]');
      if (enabledInput instanceof HTMLInputElement) enabledInput.value = String(toggle.checked);
      // requestSubmit, so the page's submit guard waits for a pending settings save first.
      if (form instanceof HTMLFormElement) form.requestSubmit();
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
  // Settings changed since the last save, latest last.
  const changedSettings = new Set();
  // The names whose values differ between two serialized settings states.
  const movedSettings = (before, after) => {
    const read = (state) => {
      const values = new Map();
      String(state || '').split('&').filter(Boolean).forEach((pair) => {
        const name = pair.slice(0, Math.max(0, pair.indexOf('=')));
        values.set(name, [...(values.get(name) || []), pair.slice(name.length + 1)]);
      });
      return values;
    };
    const a = read(before);
    const b = read(after);
    return [...new Set([...a.keys(), ...b.keys()])]
      .filter((name) => (a.get(name) || []).join('') !== (b.get(name) || []).join(''));
  };

  // Deck settings live in the Studio's Deck tab and save themselves: a change is posted in place
  // a moment after the last edit, saves never overlap, and the Studio rebuilds from the saved deck.
  const conflictBanner = root.querySelector('[data-pbd-conflict]');
  const conflictText = root.querySelector('[data-pbd-conflict-text]');
  let settingsSavedState = '';
  let settingsTimer = 0;
  let settingsDrain = null;
  let settingsQueued = false;
  let settingsDiscarded = false;

  // Unsaved means the form differs from what the server last accepted, so a failed save (invalid,
  // offline, conflict) still counts until a later save succeeds or the person discards it.
  const settingsPending = () => !settingsDiscarded && (Boolean(settingsTimer) || Boolean(settingsDrain)
    || (settingsForm instanceof HTMLFormElement && serializeSettings() !== settingsSavedState));

  const showConflict = (message) => {
    if (conflictText && message) conflictText.textContent = `${message} Your last change was not saved.`;
    if (conflictBanner) conflictBanner.hidden = false;
  };
  root.querySelector('[data-pbd-conflict-reload]')?.addEventListener('click', () => {
    settingsDiscarded = true;
    window.location.reload();
  });

  // Each step's one-line summary, read from the form as it stands.
  const settingsValue = (name) => settingsForm?.querySelector(`input[name="${name}"]:checked`)?.value || '';
  const settingsChecked = (name) => Boolean(settingsForm?.querySelector(`input[type="checkbox"][name="${name}"]`)?.checked);
  const settingsValueLabel = (name) => {
    const input = settingsForm?.querySelector(`input[name="${name}"]:checked`);
    return input?.dataset.label || input?.closest('label')?.querySelector('strong, span')?.textContent?.trim() || '';
  };
  // A segmented choice explains only the option that is selected.
  const refreshSegmentHelp = () => {
    settingsForm?.querySelectorAll('[data-pbd-seg-help]').forEach((help) => {
      const name = help.dataset.pbdSegHelp;
      help.textContent = settingsForm.querySelector(`input[name="${name}"]:checked`)?.dataset.help || '';
    });
  };
  const sentence = (parts) => {
    const text = parts.filter(Boolean).join(', ');
    return text ? text.charAt(0).toUpperCase() + text.slice(1) : 'None';
  };
  // The institution slides switched on, by name, for the Extra slides summary.
  const institutionSlides = () => [...root.querySelectorAll('[data-pbd-additional-slide-card]')]
    .filter((card) => card.querySelector('[data-pbd-additional-slide-toggle]')?.checked)
    .map((card) => card.dataset.slideName || '');
  const stepSummaries = () => {
    const sheet = isUpdateSheet();
    const mode = settingsValue('PresentationMode');
    const narrative = settingsValue('NarrativeMode');
    const detailed = mode === 'DetailedProjects' || mode === 'Combined';
    const stage = settingsChecked('ShowPresentStage');
    const status = settingsChecked('ShowPresentStatus');
    const keyPoints = settingsChecked('ShowKeyPoints');
    const project = [stage && status ? 'Stage and status' : stage ? 'Stage' : status ? 'Status remark' : 'No stage or status'];
    if (keyPoints) {
      project.push(narrative === 'Both'
        ? `key points on the ${settingsValue('KeyPointSlide') === 'CapabilitySlide' ? 'capability' : 'brief'} slide`
        : 'key points');
    } else if (narrative !== 'CapabilityOverview') {
      project.push(`${settingsValueLabel('ProjectBriefLayout').toLowerCase()} brief layout`);
    }
    const rows = settingsForm?.querySelectorAll('input[name="UpdateSheetRows"]:checked').length || 0;
    const marking = (settingsForm?.querySelector('input[name="HandlingMarking"]')?.value || '').trim();
    const insignia = { None: 'no insignia', CoverAndSummary: 'insignia on cover and summary', AllSlides: 'insignia on all slides' }[settingsValue('BrandingScope')] || '';
    return {
      slides: sheet
        ? 'Update sheets'
        : [{ ExecutiveTable: 'Status table', DetailedProjects: 'Project slides', Combined: 'Table and project slides' }[mode],
          detailed ? { CapabilityOverview: 'capability overview', ProjectBrief: 'project brief', Both: 'capability and brief' }[narrative] : '']
          .filter(Boolean).join(' · '),
      project: project.join(' · '),
      sheet: `${rows} ${rows === 1 ? 'row' : 'rows'}${settingsChecked('HideEmptyUpdateSheetValues') ? ' · empty rows hidden' : ''}`,
      cost: { CostRdOnly: 'Cost (R&D)', ProliferationOnly: 'Proliferation cost', Both: 'Both costs', None: 'No cost' }[settingsValue('CostMode')] || '',
      extra: sheet
        ? sentence([settingsChecked('IncludeCoverSlide') && 'cover', settingsChecked('IncludePortfolioSummarySlide') && 'portfolio summary', ...institutionSlides()])
        : sentence([
          settingsChecked('IncludeStageSummary') && (settingsChecked('IncludeStageDistributionTable') ? 'stage summary with table' : 'stage summary'),
          settingsChecked('IncludeProjectCategorySummary') && 'category summary',
          settingsChecked('IncludeTechnicalCategorySummary') && 'technical summary',
          ...institutionSlides()]),
      look: [settingsValueLabel('PresentationTheme'), insignia, settingsValueLabel('ClosingSlideType')].filter(Boolean).join(' · '),
      handling: marking ? `Marked ${marking}` : 'No marking'
    };
  };
  const refreshSettingsSummary = () => {
    if (!settingsForm) return;
    refreshSegmentHelp();
    const summaries = stepSummaries();
    let number = 0;
    settingsForm.querySelectorAll('[data-pbd-step]').forEach((step) => {
      const summary = step.querySelector('[data-pbd-step-summary]');
      if (summary) summary.textContent = summaries[step.dataset.pbdStep] || '';
      const stepNumber = step.querySelector('[data-pbd-step-number]');
      if (!step.hidden) number += 1;
      if (stepNumber) stepNumber.textContent = String(number);
    });
  };

  // The deck's identity and its template line follow every save.
  const presetStatus = root.querySelector('[data-pbd-preset-status]');
  const templateCurrent = root.querySelector('[data-pbd-template-current]');
  const templateCustomised = root.querySelector('[data-pbd-template-customised]');
  const templateToggle = root.querySelector('[data-pbd-template-toggle]');
  const templateList = root.querySelector('[data-pbd-template-list]');
  const presetTitles = Object.fromEntries([...root.querySelectorAll('[data-pbd-preset]')]
    .map((button) => [button.dataset.pbdPreset, button.querySelector('strong')?.textContent?.trim() || '']));
  const presetSummaries = Object.fromEntries([...root.querySelectorAll('[data-pbd-preset]')]
    .map((button) => [button.dataset.pbdPreset, button.querySelector('small')?.textContent?.trim() || '']));
  const setTemplateListOpen = (open) => {
    if (templateList) templateList.hidden = !open;
    templateToggle?.setAttribute('aria-expanded', String(open));
  };
  templateToggle?.addEventListener('click', () => setTemplateListOpen(Boolean(templateList?.hidden)));
  templateList?.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    setTemplateListOpen(false);
    templateToggle?.focus();
  });
  const applySettingsDeck = (deck) => {
    if (!deck) return;
    const name = root.querySelector('[data-pbd-deck-name]');
    if (name) name.textContent = deck.name || '';
    const description = root.querySelector('[data-pbd-deck-description]');
    if (description) {
      description.textContent = deck.description || '';
      description.hidden = !deck.description;
    }
    root.querySelectorAll('[data-pbd-preset]').forEach((button) => {
      const current = button.dataset.pbdPreset === deck.preset;
      button.classList.toggle('is-current', current);
      button.setAttribute('aria-pressed', String(current));
    });
    const known = Boolean(deck.preset && presetTitles[deck.preset]);
    if (templateCurrent && known) templateCurrent.textContent = presetTitles[deck.preset];
    if (templateCustomised) templateCustomised.hidden = !(known && deck.presetAdjusted);
    const progressTemplate = root.querySelector('[data-pbd-progress-template]');
    if (progressTemplate && known) {
      progressTemplate.textContent = `${presetTitles[deck.preset]}${deck.presetAdjusted ? ', customised' : ''}`;
    }
    if (presetStatus) {
      presetStatus.textContent = !known
        ? 'Choose a template to apply its settings; you can still change any of them.'
        : deck.presetAdjusted
          ? 'Started from this template, then customised.'
          : presetSummaries[deck.preset];
    }
  };

  const saveSettingsOnce = async () => {
    if (!validateUpdateSheetRows({ focus: false })) {
      setState(settingsStatus, 'Select at least one Project Update Sheet row; nothing was saved.', 'error');
      return false;
    }
    if (!(settingsForm.querySelector('[data-pbd-settings-name]')?.value || '').trim()) {
      setState(settingsStatus, 'The deck needs a name; nothing was saved.', 'error');
      return false;
    }
    const state = serializeSettings();
    if (state === settingsSavedState) {
      setState(settingsStatus, 'All changes saved.', 'saved');
      return true;
    }

    setState(settingsStatus, 'Saving…', 'saving');
    try {
      const { payload } = await runDeckMutation(async () => {
        const body = new FormData(settingsForm);
        body.set('RowVersion', currentRowVersion());
        const reply = await fetch(settingsForm.action, {
          method: 'POST',
          credentials: 'same-origin',
          body,
          headers: { Accept: 'application/json', 'X-CSRF-TOKEN': token, 'X-Requested-With': 'XMLHttpRequest' }
        });
        const replyPayload = await reply.json().catch(() => null);
        // A refused save fails inside the queue, so "Saved · shared" never reports it as saved.
        if (reply.status === 409) {
          showConflict(replyPayload?.message);
          throw new Error('Not saved: someone else changed this deck.');
        }
        if (!reply.ok) throw new Error(replyPayload?.message || `The settings could not be saved (${reply.status}).`);
        updateRowVersion(replyPayload?.deck?.rowVersion);
        return { response: reply, payload: replyPayload };
      });
      // Every setting whose saved value moved, including ones set by script (row order, Select
      // all), the latest edit last so the preview follows it.
      const moved = movedSettings(settingsSavedState, state);
      const changed = [
        ...moved.filter((name) => !changedSettings.has(name)),
        ...[...changedSettings].filter((name) => moved.includes(name))
      ];
      settingsSavedState = state;
      changedSettings.clear();
      applyEditorState(payload?.deck);
      // The Studio shows a slide the change affects once it has rebuilt the slides.
      if (changed.length) {
        root.querySelector('[data-pbd-preset-undo]')?.remove();
        document.dispatchEvent(new CustomEvent('pbd:settings-saved', { detail: { changed } }));
      }
      applySettingsDeck(payload?.deck);
      refreshSettingsSummary();
      setState(settingsStatus, 'All changes saved.', 'saved');
      return true;
    } catch (error) {
      setState(settingsStatus, error.message || 'The settings could not be saved.', 'error');
      return false;
    }
  };

  // One shared save: a call while a save is running joins it, and it resolves only once every
  // change queued meanwhile has been saved too (true) or a save has failed (false).
  const saveSettingsNow = () => {
    window.clearTimeout(settingsTimer);
    settingsTimer = 0;
    if (!(settingsForm instanceof HTMLFormElement)) return Promise.resolve(true);
    if (settingsDrain) {
      settingsQueued = true;
      return settingsDrain;
    }
    settingsDrain = (async () => {
      let saved;
      do {
        settingsQueued = false;
        saved = await saveSettingsOnce();
      } while (settingsQueued);
      return saved && serializeSettings() === settingsSavedState;
    })().finally(() => {
      settingsDrain = null;
    });
    return settingsDrain;
  };

  // Navigation and full-page posts go ahead only once the settings are saved, or when the
  // person chooses to leave them unsaved. Forms that ask their own question first (remove an
  // additional slide) call it through the root after that question.
  // Speaker notes the Studio holds but has not saved (a save that failed or is still running).
  let notesDiscarded = false;
  const notesUnsaved = () => !notesDiscarded
    && Boolean(document.querySelector('[data-pbd-studio][data-unsaved-notes="true"]'));
  const settleNotesBeforeLeaving = async () => {
    // A notes save started when the box lost focus finishes first.
    await deckMutationTail;
    if (!notesUnsaved()) return true;
    const discard = await prismConfirm({
      title: 'Leave without saving the speaker notes?',
      message: 'Speaker notes for a project could not be saved and will be lost if you continue.',
      confirmText: 'Leave anyway',
      cancelText: 'Stay here',
      tone: 'danger',
      returnFocus: document.activeElement
    });
    if (discard) notesDiscarded = true;
    return discard;
  };

  const settleSettingsBeforeLeaving = async () => {
    if (settingsPending() && !(await saveSettingsNow())) {
      const discard = await prismConfirm({
        title: 'Leave without saving the settings?',
        message: 'Your latest deck settings change could not be saved and will be lost if you continue.',
        confirmText: 'Leave anyway',
        cancelText: 'Stay here',
        tone: 'danger',
        returnFocus: document.activeElement
      });
      if (!discard) return false;
      settingsDiscarded = true;
    }
    return settleNotesBeforeLeaving();
  };
  root.pbdSettleSettings = settleSettingsBeforeLeaving;

  // Kept under its earlier name: the row, profile-module and stage-summary editors call it.
  const refreshSettingsDirtyState = (delay = 700) => {
    window.clearTimeout(settingsTimer);
    settingsTimer = window.setTimeout(() => {
      settingsTimer = 0;
      saveSettingsNow();
    }, delay);
  };

  settingsForm?.addEventListener('change', (event) => {
    // Remember what changed, so the preview can show a slide it affects once saved.
    const name = event.target instanceof HTMLInputElement ? event.target.name : '';
    if (name && event.target.form === settingsForm) {
      changedSettings.delete(name);
      changedSettings.add(name);
    }
    refreshSettingsSummary();
    refreshSettingsDirtyState(400);
  });
  settingsForm?.addEventListener('input', (event) => {
    if (event.target instanceof HTMLInputElement && event.target.type === 'text') refreshSettingsDirtyState(1200);
  });
  settingsForm?.addEventListener('submit', (event) => {
    event.preventDefault();
    saveSettingsNow();
  });
  window.addEventListener('beforeunload', (event) => {
    if (!settingsPending() && !profileDirty && !notesUnsaved()) return;
    event.preventDefault();
    event.returnValue = '';
  });

  // Presets: applied on the server from one definition, then the page reloads to show every
  // setting (the Studio returns to the same slide and tab).
  root.querySelectorAll('[data-pbd-preset]').forEach((button) => button.addEventListener('click', async () => {
    // A customised deck loses those choices; Undo is offered afterwards, but ask first.
    if (templateCustomised && !templateCustomised.hidden) {
      const title = presetTitles[button.dataset.pbdPreset] || 'this template';
      const confirmed = await prismConfirm({
        title: `Apply ${title}?`,
        message: `This deck's slide, cost and look settings change to the ${title} template, replacing your customised choices. Projects, notes and slide text stay. You can undo straight after.`,
        confirmText: 'Apply template',
        cancelText: 'Keep my settings',
        tone: 'primary',
        returnFocus: button
      });
      if (!confirmed) return;
    }
    if (settingsPending() && !(await saveSettingsNow())) return;
    root.querySelectorAll('[data-pbd-preset]').forEach((other) => { other.disabled = true; });
    setState(settingsStatus, 'Applying the template…', 'saving');
    try {
      await runDeckMutation(() => requestJson(root.dataset.presetUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, preset: button.dataset.pbdPreset, rowVersion: currentRowVersion() })
      }));
      window.location.reload();
    } catch (error) {
      if (error instanceof RequestError && error.status === 409) showConflict(error.message);
      setState(settingsStatus, error.message || 'The template could not be applied.', 'error');
      root.querySelectorAll('[data-pbd-preset]').forEach((other) => { other.disabled = false; });
    }
  }));

  // Rename and describe the deck where its name is shown.
  const titleView = root.querySelector('[data-pbd-deck-title-view]');
  const renameForm = root.querySelector('[data-pbd-rename-form]');
  const renameName = root.querySelector('[data-pbd-rename-name]');
  const renameDescription = root.querySelector('[data-pbd-rename-description]');
  const settingsName = settingsForm?.querySelector('[data-pbd-settings-name]');
  const settingsDescription = settingsForm?.querySelector('[data-pbd-settings-description]');
  const deckDescriptionText = root.querySelector('[data-pbd-deck-description]');
  const setRenaming = (renaming) => {
    if (renameForm) renameForm.hidden = !renaming;
    if (titleView) titleView.hidden = renaming;
    if (deckDescriptionText) deckDescriptionText.hidden = renaming || !(settingsDescription?.value || '').trim();
  };
  root.querySelector('[data-pbd-rename]')?.addEventListener('click', () => {
    if (renameName) renameName.value = settingsName?.value || '';
    if (renameDescription) renameDescription.value = settingsDescription?.value || '';
    setRenaming(true);
    renameName?.focus();
    renameName?.select();
  });
  root.querySelector('[data-pbd-rename-cancel]')?.addEventListener('click', () => {
    setRenaming(false);
    root.querySelector('[data-pbd-rename]')?.focus();
  });
  renameForm?.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    setRenaming(false);
    root.querySelector('[data-pbd-rename]')?.focus();
  });
  renameForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const name = (renameName?.value || '').trim();
    if (!name) {
      renameName?.focus();
      return;
    }
    const previousName = settingsName?.value || '';
    const previousDescription = settingsDescription?.value || '';
    if (settingsName) settingsName.value = name;
    if (settingsDescription) settingsDescription.value = (renameDescription?.value || '').trim();
    setRenaming(false);
    if (await saveSettingsNow()) {
      root.querySelector('[data-pbd-rename]')?.focus();
      return;
    }
    // A refused name (already taken, say) must not ride along with every later settings save:
    // restore the saved name and reopen the editor with what was typed.
    if (settingsName) settingsName.value = previousName;
    if (settingsDescription) settingsDescription.value = previousDescription;
    setRenaming(true);
    renameName?.focus();
  });

  const confirmProfileNavigation = async () => {
    if (!profileDirty) return true;
    const confirmed = await confirmProfileDiscard('Your unsaved SDD Institutional Profile changes will be lost if you continue.');
    if (confirmed) setProfileDirty(false);
    return confirmed;
  };

  document.addEventListener('click', async (event) => {
    if ((!settingsPending() && !profileDirty && !notesUnsaved()) || event.defaultPrevented || event.button !== 0
        || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const link = event.target.closest('a[href]');
    if (!(link instanceof HTMLAnchorElement) || profileDrawer?.contains(link)) return;
    const href = link.getAttribute('href') || '';
    if (!href || href.startsWith('#') || href.startsWith('javascript:')
        || link.target === '_blank' || link.target.startsWith('prism-project-') || link.hasAttribute('download')) return;

    event.preventDefault();
    if (!(await settleSettingsBeforeLeaving())) return;
    if (profileDirty && !(await confirmProfileNavigation())) return;
    window.location.assign(link.href);
  }, true);

  // Full-page posts (additional slides, duplicate, delete) wait for a pending settings save.
  document.addEventListener('submit', async (event) => {
    if (event.defaultPrevented) return;
    const submittedForm = event.target;
    if (!(submittedForm instanceof HTMLFormElement)
        || submittedForm === settingsForm
        || submittedForm === profileForm
        || submittedForm === renameForm
        || submittedForm.matches('[data-pbd-generate-form], [data-pbd-add-selection-form], [data-pbd-individual-form], [data-pbd-remove-additional-slide-form]')) return;
    if (!settingsPending() && !profileDirty && !notesUnsaved()) return;

    event.preventDefault();
    if (!(await settleSettingsBeforeLeaving())) return;
    if (profileDirty && !(await confirmProfileNavigation())) return;
    HTMLFormElement.prototype.submit.call(submittedForm);
  }, true);

  // Preserve the user's working context for this deck.
  const restoreScroll = () => {
    const saved = Number(storage.get(`${storagePrefix}scroll`) || 0);
    if (saved > 0) window.requestAnimationFrame(() => window.scrollTo({ top: saved, behavior: 'auto' }));
  };
  window.addEventListener('pagehide', () => storage.set(`${storagePrefix}scroll`, String(window.scrollY)));
  restoreScroll();

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
    storage.set(`${storagePrefix}activeTab`, name);
  };

  const savedTab = storage.get(`${storagePrefix}activeTab`);
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


  root.querySelectorAll('[data-pbd-auto-dismiss]').forEach((alert) => {
    window.setTimeout(() => {
      alert.classList.add('is-leaving');
      window.setTimeout(() => alert.remove(), 300);
    }, 6000);
  });

  // Add projects: a dialog over the Studio. Every selection method saves in place.
  const addProjectsModalElement = document.getElementById('pbd-add-projects-modal');
  const addProjectsModal = addProjectsModalElement && window.bootstrap
    ? window.bootstrap.Modal.getOrCreateInstance(addProjectsModalElement)
    : null;
  const addProjectsStatus = addProjectsModalElement?.querySelector('[data-pbd-add-projects-status]');
  // Undo after a quick or category add: removes only the projects that add brought in.
  const offerAddUndo = (added, button) => {
    if (!addProjectsStatus) return;
    const undo = document.createElement('button');
    undo.type = 'button';
    undo.className = 'btn btn-link btn-sm pbd-add-projects__undo';
    undo.textContent = 'Undo';
    undo.setAttribute('aria-label', `Undo: remove the ${added.length === 1 ? 'project' : `${added.length} projects`} just added`);
    undo.addEventListener('click', async () => {
      undo.disabled = true;
      try {
        await updateMembership([], added, addProjectsStatus);
        const message = `Removed the ${added.length === 1 ? 'project' : `${added.length} projects`} just added.`;
        setState(addProjectsStatus, message, 'saved');
        setState(membershipStatus, message, 'saved');
        if (button?.classList.contains('is-added')) {
          button.classList.remove('is-added');
          button.textContent = 'Add';
        }
      } catch {
        undo.disabled = false;
      }
    });
    addProjectsStatus.append(' ', undo);
  };
  const quickAddButtons = () => [...(addProjectsModalElement?.querySelectorAll('.pbd-quick-card button[type="submit"]') || [])];
  const openAddProjects = () => {
    setState(addProjectsStatus, '');
    quickAddButtons().forEach((button) => {
      button.classList.remove('is-added');
      button.textContent = 'Add';
    });
    addProjectsModal?.show();
  };
  // Focus can leave the dialog (a button disabled while saving drops it to the page); Escape
  // still closes it.
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape' || event.defaultPrevented) return;
    if (!addProjectsModalElement?.classList.contains('show')) return;
    if (addProjectsModalElement.contains(event.target instanceof Node ? event.target : null)) return;
    if (document.querySelector('dialog[open]')) return;
    addProjectsModal?.hide();
  });
  root.addEventListener('click', (event) => {
    if (event.target instanceof Element && event.target.closest('[data-pbd-open-selector]')) openAddProjects();
  });
  // From the library, Generate opens the deck here and runs this Generate: progress, saving
  // pending changes and the gap question all apply. It starts only once the Studio says it has
  // set the Gaps count (pbd:studio-ready), however the two modules load (Codex review on #2950);
  // the flag is dropped so a reload does not repeat it.
  const startUrl = new URL(window.location.href);
  if (startUrl.searchParams.get('generate') === '1') {
    startUrl.searchParams.delete('generate');
    window.history.replaceState(window.history.state, '', startUrl);
    const generateFromLibrary = () => {
      const form = root.querySelector('[data-pbd-generate-form]');
      const button = form?.querySelector('[data-pbd-generate]');
      if (form instanceof HTMLFormElement && button && !button.disabled) {
        button.focus();
        form.requestSubmit();
      }
    };
    if (root.querySelector('[data-pbd-studio][data-pbd-studio-ready="true"]')) generateFromLibrary();
    else document.addEventListener('pbd:studio-ready', generateFromLibrary, { once: true });
  }

  // A new deck opens on Add projects, its next step. Drop the flag so a reload does not reopen it.
  if (root.querySelector('[data-pbd-studio][data-pbd-start="projects"]')) {
    openAddProjects();
    const url = new URL(window.location.href);
    url.searchParams.delete('start');
    window.history.replaceState(window.history.state, '', url);
  }
  addProjectsModalElement?.addEventListener('shown.bs.modal', () => {
    addProjectsModalElement.querySelector('[data-pbd-selector-tab].is-active')?.focus();
  });

  const generateButton = root.querySelector('[data-pbd-generate]');
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
    // Steps and choices that only apply to slides for each project.
    const projectSlidesStep = root.querySelector('[data-pbd-project-slides-step]');
    if (projectSlidesStep) projectSlidesStep.hidden = updateSheet || !includesDetailedSlides();
    const projectContent = root.querySelector('[data-pbd-project-content-settings]');
    if (projectContent) projectContent.hidden = updateSheet || !includesDetailedSlides();
    const projectBriefLayoutSettings = root.querySelector('[data-pbd-project-brief-layout-settings]');
    if (projectBriefLayoutSettings) {
      projectBriefLayoutSettings.hidden = updateSheet
        || !includesDetailedSlides()
        || !includesProjectBrief()
        // The key points layout draws its own brief slide.
        || Boolean(root.querySelector('[data-pbd-key-points-toggle]')?.checked);
    }
    // The slide choice matters only when the deck draws both project slides.
    const keyPointSlide = root.querySelector('[data-pbd-key-point-slide]');
    if (keyPointSlide) {
      keyPointSlide.hidden = !root.querySelector('[data-pbd-key-points-toggle]')?.checked
        || narrativeMode() !== 'Both';
    }
    root.querySelector('[data-pbd-presentation-design]')?.classList.toggle('is-update-sheet', updateSheet);
    root.querySelectorAll('[data-pbd-theme-preview]').forEach((preview) => {
      preview.classList.toggle('is-update-sheet', updateSheet);
    });
    if (restoredSettingsLayout !== currentSettingsLayout()) {
      restoredSettingsLayout = currentSettingsLayout();
      restoreSettingsSectionState();
    }
    validateUpdateSheetRows();
    refreshSettingsSummary();
  };
  root.querySelectorAll('[data-pbd-layout-choice], input[name="PresentationMode"], input[name="CostMode"], input[name="NarrativeMode"], [data-pbd-key-points-toggle]')
    .forEach((choice) => choice.addEventListener('change', syncTemplateSettings));
  stageSummaryToggle?.addEventListener('change', () => {
    syncStageSummaryOptions();
    refreshSettingsDirtyState();
  });
  syncStageSummaryOptions();
  syncTemplateSettings();
  settingsSavedState = serializeSettings();
  refreshSettingsSummary();

  // The Studio listens for this and rebuilds after any in-place change.
  const announceDeckChange = (detail = {}) => {
    root.querySelector('[data-pbd-preset-undo]')?.remove();
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

  // The deck's current members, kept for actions on projects that have no slides.
  let latestProjects = [];
  try {
    latestProjects = JSON.parse(root.querySelector('[data-pbd-studio-data]')?.textContent || 'null')?.projects || [];
  } catch {
    latestProjects = [];
  }

  const applyEditorState = (deck) => {
    if (!deck) return;
    const projects = deck.projects || [];
    latestProjects = projects;
    reconcileSearchMembership(projects);
    const presented = projects.filter((project) => project.isPresented).length;
    updateRowVersion(deck.rowVersion);
    updateLeftOutNotice(projects);
    if (generateButton) generateButton.disabled = presented === 0;
    announceDeckChange({ hasProjects: presented > 0, deck });
  };

  const updateMembership = async (addProjectIds = [], removeProjectIds = [], statusElement = membershipStatus) => {
    if (deckId <= 0 || (addProjectIds.length === 0 && removeProjectIds.length === 0)) return null;
    setState(statusElement, 'Saving deck membership…', 'saving');
    try {
      const payload = await runDeckMutation(async () => {
        const reply = await requestJson(root.dataset.membershipUrl, {
          method: 'POST',
          body: JSON.stringify({ deckId, addProjectIds, removeProjectIds, rowVersion: currentRowVersion() })
        });
        updateRowVersion(reply?.deck?.rowVersion);
        return reply;
      });
      applyEditorState(payload?.deck);
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

  // Deleted or archived members have no slides; the notice above the slide removes them.
  root.querySelector('[data-pbd-remove-left-out]')?.addEventListener('click', async (event) => {
    const button = event.currentTarget;
    const leftOut = latestProjects.filter((project) => project.isDeletedOrArchived);
    if (leftOut.length === 0) return;
    const names = leftOut.map((project) => project.projectName).join(', ');
    const confirmed = await prismConfirm({
      title: leftOut.length === 1 ? 'Remove the left-out project?' : `Remove ${leftOut.length} left-out projects?`,
      message: `${names} ${leftOut.length === 1 ? 'is' : 'are'} deleted or archived and already left out of the PowerPoint. Removing ${leftOut.length === 1 ? 'it' : 'them'} only tidies this deck; the project records are not changed.`,
      confirmText: leftOut.length === 1 ? 'Remove project' : 'Remove projects',
      cancelText: 'Keep',
      tone: 'danger',
      returnFocus: button
    });
    if (!confirmed) return;
    button.disabled = true;
    try { await updateMembership([], leftOut.map((project) => project.projectId)); }
    catch { /* the status line already explains the failure */ }
    finally { button.disabled = false; }
  });

  // Quick selections and category selections: posted in place, the deck refreshed from the reply.
  root.addEventListener('submit', async (event) => {
    const form = event.target.closest('[data-pbd-add-selection-form]');
    if (!form) return;
    event.preventDefault();
    const button = event.submitter instanceof HTMLButtonElement ? event.submitter : form.querySelector('button[type="submit"]');
    button?.setAttribute('disabled', 'disabled');
    // Taken when this save starts in the queue, after any earlier save's reply is applied, so a
    // second add made before the first finished undoes only its own projects.
    let membersBefore = new Set();
    setState(addProjectsStatus, 'Adding projects…', 'saving');
    try {
      const payload = await runDeckMutation(async () => {
        membersBefore = new Set(latestProjects.map((project) => Number(project.projectId)));
        const body = new FormData(form);
        body.set('RowVersion', currentRowVersion());
        const response = await fetch(form.action, {
          method: 'POST',
          credentials: 'same-origin',
          body,
          headers: { Accept: 'application/json', 'X-CSRF-TOKEN': token, 'X-Requested-With': 'XMLHttpRequest' }
        });
        const reply = await response.json().catch(() => null);
        if (!response.ok) throw new Error(reply?.message || `Projects could not be added (${response.status}).`);
        updateRowVersion(reply?.deck?.rowVersion);
        return reply;
      });
      applyEditorState(payload?.deck);
      setState(addProjectsStatus, payload?.message || 'Deck updated.', 'saved');
      setState(membershipStatus, payload?.message || 'Deck updated.', 'saved');
      // A selection can add dozens of projects in one click; offer to take exactly those back out.
      const added = latestProjects
        .map((project) => Number(project.projectId))
        .filter((projectId) => !membersBefore.has(projectId));
      if (added.length) offerAddUndo(added, button);
      if (button && form.classList.contains('pbd-quick-card')) {
        button.classList.add('is-added');
        button.replaceChildren(Object.assign(document.createElement('i'), { className: 'bi bi-check-lg' }), document.createTextNode(' Added'));
        button.firstChild.setAttribute('aria-hidden', 'true');
      }
    } catch (error) {
      setState(addProjectsStatus, error.message || 'Projects could not be added.', 'error');
    } finally {
      button?.removeAttribute('disabled');
      // Disabling the button dropped focus to the page; put it back so the keyboard carries on.
      // (Bootstrap's focus trap may have caught it on the dialog itself.)
      if (button && [document.body, addProjectsModalElement].includes(document.activeElement)) button.focus();
    }
  });

  // How a project appears (own slides, table row only, left out): asked for by the Studio inspector.
  document.addEventListener('pbd:set-project-presentation', async (event) => {
    const projectId = Number(event.detail?.projectId);
    const presentation = event.detail?.presentation;
    if (deckId <= 0 || !Number.isInteger(projectId) || !presentation) return;
    setState(membershipStatus, 'Saving how the project appears…', 'saving');
    try {
      const payload = await runDeckMutation(async () => {
        const reply = await requestJson(root.dataset.itemPresentationUrl, {
          method: 'POST',
          body: JSON.stringify({ deckId, projectId, presentation, rowVersion: currentRowVersion() })
        });
        updateRowVersion(reply?.rowVersion);
        return reply;
      });
      applyEditorState(payload?.deck);
      setState(membershipStatus, 'Saved.', 'saved');
    } catch (error) {
      if (error instanceof RequestError && error.status === 409) showConflict(error.message);
      setState(membershipStatus, error.message || 'The change could not be saved.', 'error');
      // The Studio puts back the saved choice, then rebuilds from the server.
      document.dispatchEvent(new CustomEvent('pbd:project-presentation-failed', {
        detail: { projectId, previous: event.detail?.previous }
      }));
      announceDeckChange({});
    }
  });

  // Speaker notes for a project: asked for by the Studio inspector.
  document.addEventListener('pbd:set-project-notes', async (event) => {
    const projectId = Number(event.detail?.projectId);
    if (deckId <= 0 || !Number.isInteger(projectId)) return;
    const notes = event.detail?.notes ?? '';
    setState(membershipStatus, 'Saving the speaker notes…', 'saving');
    try {
      const payload = await runDeckMutation(async () => {
        const reply = await requestJson(root.dataset.speakerNotesUrl, {
          method: 'POST',
          body: JSON.stringify({ deckId, projectId, notes, rowVersion: currentRowVersion() })
        });
        updateRowVersion(reply?.rowVersion);
        return reply;
      });
      applyEditorState(payload?.deck);
      setState(membershipStatus, 'Saved.', 'saved');
    } catch (error) {
      if (error instanceof RequestError && error.status === 409) showConflict(error.message);
      setState(membershipStatus, error.message || 'The speaker notes could not be saved.', 'error');
      document.dispatchEvent(new CustomEvent('pbd:project-notes-failed', {
        detail: { projectId, message: error.message }
      }));
    }
  });

  // The photograph a deck shows for a project: asked for by the Studio inspector.
  document.addEventListener('pbd:set-project-photo', async (event) => {
    const projectId = Number(event.detail?.projectId);
    const photoId = event.detail?.photoId ?? null;
    if (deckId <= 0 || !Number.isInteger(projectId)) return;
    setState(membershipStatus, 'Saving the photograph…', 'saving');
    try {
      const payload = await runDeckMutation(async () => {
        const reply = await requestJson(root.dataset.itemPhotoUrl, {
          method: 'POST',
          body: JSON.stringify({ deckId, projectId, photoId, rowVersion: currentRowVersion() })
        });
        updateRowVersion(reply?.rowVersion);
        return reply;
      });
      applyEditorState(payload?.deck);
      setState(membershipStatus, 'Saved.', 'saved');
    } catch (error) {
      if (error instanceof RequestError && error.status === 409) showConflict(error.message);
      setState(membershipStatus, error.message || 'The photograph could not be saved.', 'error');
      document.dispatchEvent(new CustomEvent('pbd:project-photo-failed', {
        detail: { projectId, message: error.message }
      }));
    }
  });

  // Reorder: the Studio outline asks for a new project order (within present stages).
  document.addEventListener('pbd:reorder-projects', async (event) => {
    const projectIds = (event.detail?.projectIds || []).map(Number).filter(Number.isInteger);
    if (deckId <= 0 || projectIds.length === 0) return;
    setState(membershipStatus, 'Saving slide order…', 'saving');
    try {
      await runDeckMutation(async () => {
        const payload = await requestJson(root.dataset.reorderUrl, {
          method: 'POST',
          body: JSON.stringify({ deckId, projectIds, rowVersion: currentRowVersion() })
        });
        updateRowVersion(payload?.rowVersion);
      });
      setState(membershipStatus, 'Slide order saved.', 'saved');
    } catch (error) {
      if (error instanceof RequestError && error.status === 409) showConflict(error.message);
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
    if (showingDeckRows) {
      setState(searchStatus, `This deck has ${searchRows.length} ${searchRows.length === 1 ? 'project' : 'projects'}. Untick to remove, or search to add more.`);
      return;
    }
    setState(searchStatus, searchRows.length ? `${shown} of ${searchRows.length} matching projects shown.` : 'No projects match this search.');
  };

  // After any membership change (search, quick add, categories, removals), mark search results
  // from the deck's actual members while keeping ticks the person has not applied yet.
  const reconcileSearchMembership = (projects) => {
    // No search typed and no deck list showing yet (the deck was empty): show the members now.
    const blankQuery = (searchInput?.value.trim() || '').length < 2;
    if (blankQuery && !showingDeckRows) {
      if (projects.length) {
        showingDeckRows = true;
        renderSearchResults(deckRowsFrom(projects));
      }
      return;
    }
    if (searchRows.length === 0) return;
    const members = new Set(projects.map((project) => Number(project.projectId)));
    const pending = new Map();
    resultDesired.forEach((desired, projectId) => {
      if (desired !== resultBaseline.get(projectId)) pending.set(projectId, desired);
    });
    searchRows = showingDeckRows
      ? deckRowsFrom(projects)
      : searchRows.map((project) => ({ ...project, isSelected: members.has(Number(project.projectId)) }));
    renderSearchResults(searchRows);
    pending.forEach((desired, projectId) => {
      if (desired === members.has(projectId)) return;
      const checkbox = searchResults?.querySelector(`[data-project-result="${projectId}"] input[type="checkbox"]`);
      if (!(checkbox instanceof HTMLInputElement) || checkbox.checked === desired) return;
      checkbox.checked = desired;
      checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    });
  };

  // Saved membership and the pending tick, told apart: only saved members read "In deck".
  const showMembershipBadge = (badge, saved, desired) => {
    const [text, state] = saved
      ? (desired ? ['In deck', 'is-in-deck'] : ['Will be removed', 'is-pending-remove'])
      : (desired ? ['Will be added', 'is-pending-add'] : ['', '']);
    badge.textContent = text;
    badge.hidden = !text;
    badge.className = `pbd-membership-badge ${state}`.trim();
  };

  // With no search typed, the list is the deck's own projects (untick to remove).
  let showingDeckRows = false;
  const deckRowsFrom = (projects) => projects.map((project) => ({
    projectId: Number(project.projectId),
    projectName: project.projectName,
    isSelected: true,
    fromDeck: true,
    lifecycle: project.lifecycleDisplay,
    presentStage: project.presentStage,
    projectCategory: project.projectCategory,
    technicalCategory: project.technicalCategory
  }));

  const renderSearchResults = (rows) => {
    searchRows = rows;
    resultBaseline.clear();
    resultDesired.clear();
    searchResults?.replaceChildren();
    if (!rows.length) {
      // The deck list emptied (every project removed): back to the prompt, ready to refill.
      if (showingDeckRows) {
        showingDeckRows = false;
        setState(searchStatus, 'Type at least two letters of a project name to find it.');
        updateMembershipSummary();
        return;
      }
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
        showMembershipBadge(badge, Boolean(project.isSelected), checkbox.checked);
        updateMembershipSummary();
        applyMembershipResultFilter();
      });

      const body = document.createElement('span');
      const heading = document.createElement('span');
      heading.className = 'pbd-search-result__heading';
      const name = document.createElement('strong');
      name.textContent = project.projectName;
      const badge = document.createElement('em');
      showMembershipBadge(badge, Boolean(project.isSelected), Boolean(project.isSelected));
      heading.append(name, badge);
      const meta = document.createElement('small');
      // "Completed · Completed" said once.
      meta.textContent = [...new Set([project.lifecycle, project.presentStage, project.projectCategory, project.technicalCategory, project.projectOfficer].filter(Boolean))].join(' · ');
      body.append(heading, meta);
      if (!project.fromDeck) {
        const ref = document.createElement('small');
        ref.textContent = project.caseFileNumber ? `Ref: ${project.caseFileNumber}` : 'No case-file reference';
        body.append(ref);
      }
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
      if (latestProjects.length) {
        showingDeckRows = true;
        renderSearchResults(deckRowsFrom(latestProjects));
        return;
      }
      showingDeckRows = false;
      searchResults?.replaceChildren();
      searchRows = [];
      setState(searchStatus, 'Type at least two letters of a project name to find it.');
      updateMembershipSummary();
      return;
    }
    showingDeckRows = false;

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
  // Open on the deck's own projects.
  if (individualForm && deckId > 0) searchProjects();

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
      const payload = await runDeckMutation(async () => {
        const reply = await requestJson(root.dataset.descriptionUrl, {
          method: 'POST',
          body: JSON.stringify({ deckId, projectId, value: descriptionValue?.value || null, rowVersion: currentRowVersion() })
        });
        updateRowVersion(reply?.rowVersion);
        return reply;
      });
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

  // Before the deck is shown or handed over with gaps, say so once (per deck, per session):
  // the slides would carry placeholders such as "Not recorded".
  const gapsAcknowledgedKey = `${storagePrefix}gapsAcknowledged`;
  const openGapKinds = () => {
    const badge = root.querySelector('[data-pbd-checks-badge]');
    return badge && !badge.hidden ? Number(badge.textContent) || 0 : 0;
  };
  const confirmGaps = async (action, returnFocus) => {
    const kinds = openGapKinds();
    if (!kinds) return true;
    try { if (window.sessionStorage.getItem(gapsAcknowledgedKey) === '1') return true; } catch { /* ask */ }
    const choice = await prismConfirm({
      title: `${action} with gaps?`,
      message: `${kinds} ${kinds === 1 ? 'kind' : 'kinds'} of gap ${kinds === 1 ? 'is' : 'are'} still open. Those slides show placeholders such as "Not recorded". The Gaps tab lists them.`,
      confirmText: `${action} anyway`,
      cancelText: 'Review gaps',
      tone: 'primary',
      returnFocus
    });
    if (choice) {
      try { window.sessionStorage.setItem(gapsAcknowledgedKey, '1'); } catch { /* asked again next time */ }
      return true;
    }
    root.querySelector('[data-pbd-inspector-tab="checks"]')?.click();
    return false;
  };
  // Present: the Studio opens the viewer; this runs first and lets the click through once agreed.
  const presentButton = root.querySelector('[data-pbd-preview-open]');
  let presentAgreed = false;
  presentButton?.addEventListener('click', async (event) => {
    if (presentAgreed) { presentAgreed = false; return; }
    if (!openGapKinds()) return;
    try { if (window.sessionStorage.getItem(gapsAcknowledgedKey) === '1') return; } catch { /* ask */ }
    event.stopImmediatePropagation();
    if (await confirmGaps('Present', presentButton)) {
      presentAgreed = true;
      presentButton.click();
    }
  }, { capture: true });

  generateForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    // Generate from saved settings: finish a pending save first.
    if (settingsPending() && !(await saveSettingsNow())) {
      setState(generateStatus, 'Save the deck settings before generating the PowerPoint.', 'error');
      return;
    }
    // In-place changes still saving (speaker notes saved on leaving the box, a photo, how a
    // project appears) finish first, so the PowerPoint reads them.
    await deckMutationTail;
    if (document.querySelector('[data-pbd-studio][data-unsaved-notes="true"]')) {
      setState(generateStatus, 'Save the speaker notes before generating the PowerPoint.', 'error');
      return;
    }
    // Asked only now: each save above applies the deck's new gaps to the Gaps count, so the
    // warning describes the deck actually being downloaded (Codex review on #2938).
    if (!(await confirmGaps('Generate', generateButton))) return;
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
