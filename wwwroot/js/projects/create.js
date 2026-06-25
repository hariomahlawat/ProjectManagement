(function () {
  'use strict';

  const form = document.querySelector('[data-project-create-form]');
  if (!form) return;

  const isOngoing = document.getElementById('IsOngoing');
  const ongoingFields = document.getElementById('OngoingFields');
  const ongoingCard = document.querySelector('[data-status-card="ongoing"]');
  const isLegacy = document.getElementById('IsLegacy');
  const legacyFields = document.getElementById('LegacyFields');
  const legacyCard = document.querySelector('[data-status-card="legacy"]');
  const statusMessage = document.querySelector('[data-status-message]');
  const categorySelect = document.querySelector('[name="Input.CategoryId"]');
  const subCategoryField = document.getElementById('SubCategoryField');
  const subCategorySelect = document.getElementById('SubCategoryId');
  const nameInput = document.getElementById('Input_Name');
  const nameWarning = document.getElementById('project-name-duplicate');

  function setExpandedState(card, fields, active) {
    fields?.classList.toggle('visually-hidden', !active);
    card?.classList.toggle('is-active', active);
  }

  function showStatusMessage(text) {
    if (!statusMessage) return;
    statusMessage.textContent = text;
    statusMessage.hidden = false;
    window.clearTimeout(showStatusMessage.timer);
    showStatusMessage.timer = window.setTimeout(() => {
      statusMessage.hidden = true;
      statusMessage.textContent = '';
    }, 4000);
  }

  function toggleOngoing({ userInitiated = false } = {}) {
    const active = Boolean(isOngoing?.checked);
    if (active && isLegacy?.checked) {
      isLegacy.checked = false;
      setExpandedState(legacyCard, legacyFields, false);
      if (userInitiated) {
        showStatusMessage('Legacy record was turned off because a project cannot be both ongoing and legacy.');
      }
    }
    setExpandedState(ongoingCard, ongoingFields, active);
  }

  function toggleLegacy({ userInitiated = false } = {}) {
    const active = Boolean(isLegacy?.checked);
    if (active && isOngoing?.checked) {
      isOngoing.checked = false;
      setExpandedState(ongoingCard, ongoingFields, false);
      if (userInitiated) {
        showStatusMessage('Already in progress was turned off because a legacy project is completed.');
      }
    }
    setExpandedState(legacyCard, legacyFields, active);
  }

  async function loadSubCategories(categoryId, selectedValue) {
    if (!subCategorySelect || !subCategoryField) return;

    subCategorySelect.innerHTML = '<option value="">— Select subcategory —</option>';
    subCategorySelect.disabled = true;
    subCategoryField.hidden = true;

    if (!categoryId) return;

    try {
      const response = await fetch(`/api/categories/children?parentId=${encodeURIComponent(categoryId)}`, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json' }
      });

      if (!response.ok) return;

      const items = await response.json();
      if (!Array.isArray(items) || items.length === 0) return;

      const fragment = document.createDocumentFragment();
      fragment.appendChild(new Option('— Select subcategory —', ''));

      for (const item of items) {
        fragment.appendChild(new Option(String(item.name), String(item.id)));
      }

      subCategorySelect.innerHTML = '';
      subCategorySelect.appendChild(fragment);
      subCategorySelect.disabled = false;
      subCategoryField.hidden = false;

      if (selectedValue) subCategorySelect.value = String(selectedValue);
    } catch {
      // Category remains valid even if child lookup is temporarily unavailable.
    }
  }

  function createCombobox(select) {
    if (!select || select.dataset.comboboxReady === 'true') return;

    const options = Array.from(select.options).map((option) => ({
      value: option.value,
      text: (option.textContent || '').trim()
    }));

    const wrapper = document.createElement('div');
    wrapper.className = 'project-combobox';

    const inputWrap = document.createElement('div');
    inputWrap.className = 'project-combobox__input-wrap';

    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'form-control project-control project-combobox__input';
    input.placeholder = select.dataset.comboboxPlaceholder || 'Search or select';
    input.autocomplete = 'off';
    input.setAttribute('role', 'combobox');
    input.setAttribute('aria-autocomplete', 'list');
    input.setAttribute('aria-expanded', 'false');

    const listId = `${select.id || select.name.replace(/\W/g, '_')}_listbox`;
    input.setAttribute('aria-controls', listId);

    const clear = document.createElement('button');
    clear.type = 'button';
    clear.className = 'project-combobox__clear';
    clear.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
    clear.setAttribute('aria-label', 'Clear selection');

    const toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'project-combobox__toggle';
    toggle.innerHTML = '<i class="bi bi-chevron-down" aria-hidden="true"></i>';
    toggle.setAttribute('aria-label', 'Show options');

    const list = document.createElement('ul');
    list.id = listId;
    list.className = 'project-combobox__list';
    list.setAttribute('role', 'listbox');
    list.hidden = true;

    select.hidden = true;
    select.setAttribute('aria-hidden', 'true');
    select.tabIndex = -1;

    select.parentNode.insertBefore(wrapper, select);
    wrapper.appendChild(inputWrap);
    inputWrap.appendChild(input);
    inputWrap.appendChild(clear);
    inputWrap.appendChild(toggle);
    wrapper.appendChild(list);
    wrapper.appendChild(select);
    select.dataset.comboboxReady = 'true';

    let filtered = options;
    let activeIndex = -1;

    function selectedOption() {
      return options.find((option) => option.value === select.value) || options[0];
    }

    function syncInput() {
      const selected = selectedOption();
      input.value = selected && selected.value ? selected.text : '';
      clear.hidden = !select.value;
    }

    function closeList() {
      list.hidden = true;
      input.setAttribute('aria-expanded', 'false');
      activeIndex = -1;
    }

    function setActive(index) {
      const nodes = Array.from(list.querySelectorAll('[role="option"]'));
      nodes.forEach((node, nodeIndex) => node.classList.toggle('is-active', nodeIndex === index));
      activeIndex = index;
      nodes[index]?.scrollIntoView({ block: 'nearest' });
    }

    function selectValue(value) {
      select.value = value;
      select.dispatchEvent(new Event('change', { bubbles: true }));
      syncInput();
      closeList();
    }

    function render(query = '') {
      const needle = query.trim().toLocaleLowerCase();
      filtered = options.filter((option) => {
        if (!option.value) return needle.length === 0;
        return option.text.toLocaleLowerCase().includes(needle);
      });

      list.innerHTML = '';
      if (filtered.length === 0) {
        const empty = document.createElement('li');
        empty.className = 'project-combobox__empty';
        empty.textContent = 'No matching options';
        list.appendChild(empty);
      } else {
        filtered.forEach((option) => {
          const item = document.createElement('li');
          item.className = 'project-combobox__option';
          item.setAttribute('role', 'option');
          item.setAttribute('aria-selected', String(option.value === select.value));
          item.dataset.value = option.value;
          item.textContent = option.text;
          item.addEventListener('pointerdown', (event) => {
            event.preventDefault();
            selectValue(option.value);
          });
          list.appendChild(item);
        });
      }

      list.hidden = false;
      input.setAttribute('aria-expanded', 'true');
      activeIndex = -1;
    }

    input.addEventListener('focus', () => {
      input.select();
      render(input.value === selectedOption()?.text ? '' : input.value);
    });

    input.addEventListener('input', () => render(input.value));

    input.addEventListener('keydown', (event) => {
      const optionNodes = Array.from(list.querySelectorAll('[role="option"]'));
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        if (list.hidden) render(input.value);
        setActive(Math.min(activeIndex + 1, optionNodes.length - 1));
      } else if (event.key === 'ArrowUp') {
        event.preventDefault();
        setActive(Math.max(activeIndex - 1, 0));
      } else if (event.key === 'Enter' && activeIndex >= 0) {
        event.preventDefault();
        const value = optionNodes[activeIndex]?.dataset.value;
        if (value !== undefined) selectValue(value);
      } else if (event.key === 'Escape') {
        closeList();
        syncInput();
      } else if (event.key === 'Tab') {
        closeList();
        syncInput();
      }
    });

    toggle.addEventListener('click', () => {
      if (list.hidden) {
        input.focus();
        render('');
      } else {
        closeList();
      }
    });

    clear.addEventListener('click', () => {
      selectValue('');
      input.focus();
    });

    document.addEventListener('pointerdown', (event) => {
      if (!wrapper.contains(event.target)) {
        closeList();
        syncInput();
      }
    }, true);

    syncInput();
  }

  function setupDuplicateNameCheck() {
    if (!nameInput || !nameWarning) return;
    const endpoint = form.dataset.nameCheckUrl;
    if (!endpoint) return;

    let timer;
    let controller;

    async function checkName() {
      const name = nameInput.value.trim();
      nameWarning.hidden = true;
      nameWarning.textContent = '';
      if (name.length < 3) return;

      controller?.abort();
      controller = new AbortController();

      try {
        const url = new URL(endpoint, window.location.origin);
        url.searchParams.set('name', name);
        const response = await fetch(url, {
          credentials: 'same-origin',
          headers: { Accept: 'application/json' },
          signal: controller.signal
        });
        if (!response.ok) return;
        const payload = await response.json();
        if (!payload || !Array.isArray(payload.matches) || payload.matches.length === 0) return;

        const names = payload.matches.map((match) => match.name).filter(Boolean).slice(0, 3);
        if (names.length === 0) return;
        nameWarning.textContent = `Similar project${names.length > 1 ? 's' : ''} already exist: ${names.join(', ')}. Verify before creating another record.`;
        nameWarning.hidden = false;
      } catch (error) {
        if (error?.name !== 'AbortError') {
          nameWarning.hidden = true;
        }
      }
    }

    nameInput.addEventListener('input', () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(checkName, 450);
    });

    if (nameInput.value.trim().length >= 3) checkName();
  }

  isOngoing?.addEventListener('change', () => toggleOngoing({ userInitiated: true }));
  isLegacy?.addEventListener('change', () => toggleLegacy({ userInitiated: true }));
  toggleOngoing();
  toggleLegacy();

  const preselectedSub = subCategorySelect?.dataset.selected || '';
  if (categorySelect?.value) loadSubCategories(categorySelect.value, preselectedSub);
  categorySelect?.addEventListener('change', (event) => loadSubCategories(event.target.value, ''));

  document.querySelectorAll('.js-create-combobox').forEach(createCombobox);
  setupDuplicateNameCheck();
})();
