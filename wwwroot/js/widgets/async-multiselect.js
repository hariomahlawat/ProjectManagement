const initialized = new WeakSet();

export function initAsyncMultiselect(root = document) {
  const scope = root ?? document;
  const widgets = scope.querySelectorAll('.js-async-multiselect');

  widgets.forEach((widget) => {
    if (initialized.has(widget)) {
      return;
    }

    if (setupMultiselect(widget)) {
      initialized.add(widget);
    }
  });
}

function setupMultiselect(container) {
  if (!(container instanceof HTMLElement)) {
    return false;
  }

  const source = container.dataset.source;
  const fieldName = container.dataset.name;

  if (!source || !fieldName) {
    return false;
  }

  const searchPlaceholder = container.dataset.searchPlaceholder ?? 'Search…';
  const noResultsText = container.dataset.noResults ?? 'No matches found';
  const removeLabel = container.dataset.removeLabel ?? 'Remove';
  const loadingText = container.dataset.loadingText ?? 'Searching…';
  // Section: Status messages
  const unauthorizedText = container.dataset.unauthorizedText ?? 'Not authorised to search projects.';
  const errorText = container.dataset.errorText ?? 'Unable to load results. Try again.';
  const pageSize = normalizePageSize(container.dataset.pageSize);
  const maxSelections = normalizeMaxSelections(container.dataset.maxSelection);
  const inputId = container.dataset.inputId;

  const initialSelections = readInitialSelections(container, fieldName);

  const hiddenContainer = document.createElement('div');
  hiddenContainer.className = 'async-multiselect__hidden';

  const chipsList = document.createElement('div');
  chipsList.className = 'async-multiselect__chips';

  const inputWrapper = document.createElement('div');
  inputWrapper.className = 'async-multiselect__input';

  const searchInput = document.createElement('input');
  searchInput.type = 'search';
  searchInput.className = 'form-control form-control-sm async-multiselect__search-input';
  searchInput.placeholder = searchPlaceholder;
  searchInput.autocomplete = 'off';
  if (inputId) {
    searchInput.id = inputId;
  }
  searchInput.setAttribute('aria-autocomplete', 'list');
  searchInput.setAttribute('role', 'combobox');
  searchInput.setAttribute('aria-expanded', 'false');

  const resultsList = document.createElement('div');
  resultsList.className = 'async-multiselect__results';
  resultsList.setAttribute('role', 'listbox');

  const resultsId = generateId('async-multiselect-results');
  resultsList.id = resultsId;
  searchInput.setAttribute('aria-controls', resultsId);

  const status = document.createElement('div');
  status.className = 'async-multiselect__status';
  status.setAttribute('role', 'status');
  status.setAttribute('aria-live', 'polite');

  container.innerHTML = '';
  container.appendChild(hiddenContainer);
  container.appendChild(chipsList);
  inputWrapper.appendChild(searchInput);
  inputWrapper.appendChild(resultsList);
  container.appendChild(inputWrapper);
  container.appendChild(status);

  const selections = new Map();
  let optionButtons = [];
  let activeIndex = -1;
  let debounceHandle = null;
  let currentController = null;
  let lastQuery = null;
  let observer;

  function announce(message) {
    if (!message) {
      return;
    }
    status.textContent = message;
    window.setTimeout(() => {
      if (status.textContent === message) {
        status.textContent = '';
      }
    }, 1000);
  }

  function addSelection(value, label, announceChange = true) {
    const key = String(value ?? '').trim();
    if (!key || selections.has(key)) {
      return;
    }

    if (maxSelections > 0 && selections.size >= maxSelections) {
      if (maxSelections === 1) {
        clearSelections(false);
      } else {
        announce(`Only ${maxSelections} selections allowed`);
        return;
      }
    }

    const resolvedLabel = label ? String(label).trim() : key;

    const chip = document.createElement('span');
    chip.className = 'async-multiselect__chip';
    chip.dataset.value = key;

    const chipLabel = document.createElement('span');
    chipLabel.className = 'async-multiselect__chip-label';
    chipLabel.textContent = resolvedLabel;

    const removeButton = document.createElement('button');
    removeButton.type = 'button';
    removeButton.className = 'async-multiselect__remove';
    removeButton.setAttribute('aria-label', `${removeLabel} ${resolvedLabel}`.trim());
    removeButton.textContent = '×';
    removeButton.addEventListener('click', () => {
      removeSelection(key, true);
    });

    chip.appendChild(chipLabel);
    chip.appendChild(removeButton);
    chipsList.appendChild(chip);

    const hiddenInput = document.createElement('input');
    hiddenInput.type = 'hidden';
    hiddenInput.name = fieldName;
    hiddenInput.value = key;
    hiddenInput.dataset.label = resolvedLabel;
    hiddenContainer.appendChild(hiddenInput);

    selections.set(key, { chip, hiddenInput, label: resolvedLabel });
    updateOptionStates();

    if (announceChange) {
      announce(`${resolvedLabel} added`);
    }
  }

  function removeSelection(value, shouldFocus = false, announceChange = true) {
    const key = String(value ?? '').trim();
    if (!selections.has(key)) {
      return;
    }

    const selection = selections.get(key);
    selection?.chip?.remove();
    selection?.hiddenInput?.remove();
    selections.delete(key);
    updateOptionStates();

    if (announceChange) {
      announce(`${selection?.label ?? key} removed`);
    }

    if (shouldFocus) {
      searchInput.focus();
    }
  }

  function clearSelections(announceChange = true) {
    const keys = Array.from(selections.keys());
    keys.forEach((key) => {
      removeSelection(key, false, announceChange);
    });
  }

  function removeLastSelection() {
    const chip = chipsList.lastElementChild;
    if (!(chip instanceof HTMLElement)) {
      return;
    }
    const key = chip.dataset.value;
    if (!key) {
      return;
    }
    removeSelection(key, true);
  }

  function updateOptionStates() {
    optionButtons.forEach((button) => {
      const value = button.dataset.value ?? '';
      if (!value) {
        return;
      }
      const isSelected = selections.has(value);
      button.disabled = isSelected;
      button.classList.toggle('async-multiselect__option--selected', isSelected);
      if (isSelected && button.classList.contains('async-multiselect__option--active')) {
        moveActive(1);
      }
    });
  }

  function showResults() {
    resultsList.classList.add('async-multiselect__results--visible');
    searchInput.setAttribute('aria-expanded', 'true');
  }

  function closeResults() {
    resultsList.classList.remove('async-multiselect__results--visible');
    searchInput.setAttribute('aria-expanded', 'false');
    optionButtons = [];
    activeIndex = -1;
    resultsList.innerHTML = '';
  }

  function showMessage(message) {
    resultsList.innerHTML = '';
    const info = document.createElement('div');
    info.className = 'async-multiselect__message';
    info.textContent = message;
    resultsList.appendChild(info);
    optionButtons = [];
    activeIndex = -1;
    showResults();
  }

  function setActiveOption(index, scrollIntoView = true) {
    if (optionButtons.length === 0) {
      activeIndex = -1;
      return;
    }

    if (activeIndex >= 0 && optionButtons[activeIndex]) {
      optionButtons[activeIndex].classList.remove('async-multiselect__option--active');
    }

    const clamped = Math.max(0, Math.min(index, optionButtons.length - 1));
    activeIndex = clamped;
    const activeOption = optionButtons[activeIndex];
    if (activeOption) {
      activeOption.classList.add('async-multiselect__option--active');
      if (scrollIntoView) {
        activeOption.scrollIntoView({ block: 'nearest' });
      }
    }
  }

  function setFirstAvailableOption() {
    for (let i = 0; i < optionButtons.length; i++) {
      if (!optionButtons[i].disabled) {
        setActiveOption(i, false);
        return;
      }
    }
    activeIndex = optionButtons.length > 0 ? 0 : -1;
    if (activeIndex >= 0) {
      setActiveOption(activeIndex, false);
    }
  }

  function moveActive(delta) {
    if (optionButtons.length === 0) {
      activeIndex = -1;
      return;
    }

    let index = activeIndex;
    for (let i = 0; i < optionButtons.length; i++) {
      index = (index + delta + optionButtons.length) % optionButtons.length;
      if (!optionButtons[index].disabled) {
        setActiveOption(index);
        return;
      }
    }
    setActiveOption(index);
  }

  function renderResults(items) {
    resultsList.innerHTML = '';
    optionButtons = [];
    activeIndex = -1;

    const fragment = document.createDocumentFragment();
    items.forEach((item) => {
      const value = item?.value ? String(item.value) : '';
      const label = item?.label ? String(item.label) : '';
      if (!value || !label) {
        return;
      }

      const option = document.createElement('button');
      option.type = 'button';
      option.className = 'async-multiselect__option';
      option.textContent = label;
      option.dataset.value = value;
      option.dataset.label = label;
      option.setAttribute('role', 'option');
      option.disabled = selections.has(value);
      if (option.disabled) {
        option.classList.add('async-multiselect__option--selected');
      }

      option.addEventListener('click', () => {
        if (option.disabled) {
          return;
        }
        addSelection(value, label);
        closeResults();
        searchInput.value = '';
        searchInput.focus();
      });

      fragment.appendChild(option);
      optionButtons.push(option);
    });

    if (optionButtons.length === 0) {
      showMessage(noResultsText);
      return;
    }

    resultsList.appendChild(fragment);
    showResults();
    setFirstAvailableOption();
  }

  function abortCurrentRequest() {
    if (currentController) {
      currentController.abort();
    }
  }

  async function performSearch(term) {
    const query = term.trim();
    if (query === lastQuery && resultsList.classList.contains('async-multiselect__results--visible')) {
      return;
    }
    abortCurrentRequest();

    const controller = new AbortController();
    currentController = controller;

    showMessage(loadingText);

    const params = new URLSearchParams();
    if (query.length > 0) {
      params.set('q', query);
    }
    params.set('page', '1');
    params.set('pageSize', String(pageSize));

    let response;
    try {
      response = await fetch(`${source}?${params.toString()}`, {
        credentials: 'same-origin',
        signal: controller.signal
      });
    } catch (err) {
      if (controller.signal.aborted) {
        return;
      }
      showMessage(errorText);
      return;
    }

    if (!response.ok) {
      if (response.status === 401 || response.status === 403) {
        console.warn(`Async multiselect unauthorized request (${response.status}) for ${source}`);
        showMessage(unauthorizedText);
      } else {
        console.warn(`Async multiselect request failed (${response.status}) for ${source}`);
        showMessage(errorText);
      }
      return;
    }

    let payload;
    try {
      payload = await response.json();
    } catch (err) {
      showMessage(errorText);
      return;
    }

    if (!payload || !Array.isArray(payload.items)) {
      showMessage(errorText);
      return;
    }

    const items = payload.items
      .map((item) => ({
        value: item && Object.prototype.hasOwnProperty.call(item, 'id') ? String(item.id) : '',
        label: item && Object.prototype.hasOwnProperty.call(item, 'name') ? String(item.name) : ''
      }))
      .filter((item) => item.value && item.label);

    if (items.length === 0) {
      showMessage(noResultsText);
      return;
    }

    renderResults(items);
    lastQuery = query;
  }

  function handleInput() {
    const value = searchInput.value ?? '';
    lastQuery = null;
    abortCurrentRequest();
    if (debounceHandle) {
      window.clearTimeout(debounceHandle);
    }
    debounceHandle = window.setTimeout(() => {
      performSearch(value);
    }, 200);
  }

  function handleKeyDown(event) {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!resultsList.classList.contains('async-multiselect__results--visible')) {
          performSearch(searchInput.value ?? '');
        } else {
          moveActive(1);
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (resultsList.classList.contains('async-multiselect__results--visible')) {
          moveActive(-1);
        }
        break;
      case 'Enter':
        if (resultsList.classList.contains('async-multiselect__results--visible') && activeIndex >= 0) {
          const activeOption = optionButtons[activeIndex];
          if (activeOption && !activeOption.disabled) {
            event.preventDefault();
            activeOption.click();
          }
        }
        break;
      case 'Escape':
        if (resultsList.classList.contains('async-multiselect__results--visible')) {
          event.preventDefault();
          closeResults();
        }
        break;
      case 'Backspace':
        if (!searchInput.value) {
          removeLastSelection();
        }
        break;
      default:
        break;
    }
  }

  function handleFocus() {
    performSearch(searchInput.value ?? '');
  }

  function handleDocumentClick(event) {
    if (!(event.target instanceof Element)) {
      return;
    }
    if (!container.contains(event.target)) {
      closeResults();
    }
  }

  function cleanup() {
    if (observer) {
      observer.disconnect();
      observer = undefined;
    }
    document.removeEventListener('click', handleDocumentClick);
    window.removeEventListener('beforeunload', cleanup);
    abortCurrentRequest();
  }

  searchInput.addEventListener('input', handleInput);
  searchInput.addEventListener('keydown', handleKeyDown);
  searchInput.addEventListener('focus', handleFocus);
  document.addEventListener('click', handleDocumentClick);

  // Section: Focus input on container clicks
  container.addEventListener('click', (event) => {
    if (!(event.target instanceof Element)) {
      return;
    }
    if (!container.contains(event.target)) {
      return;
    }
    if (!searchInput.contains(event.target)) {
      searchInput.focus();
    }
  });

  inputWrapper.addEventListener('keydown', (event) => {
    if (event.key === 'Tab' && event.shiftKey && chipsList.childElementCount > 0) {
      const firstChip = chipsList.firstElementChild;
      if (firstChip instanceof HTMLElement) {
        firstChip.focus?.();
      }
    }
  });

  observer = new MutationObserver(() => {
    if (!document.body.contains(container)) {
      cleanup();
    }
  });
  observer.observe(document.body, { childList: true, subtree: true });
  window.addEventListener('beforeunload', cleanup);

  initialSelections.forEach((item) => {
    addSelection(item.value, item.label, false);
  });

  return true;
}

function readInitialSelections(container, fieldName) {
  const selector = buildHiddenInputSelector(fieldName);
  const inputs = Array.from(container.querySelectorAll(selector));
  return inputs
    .map((input) => ({
      value: input.value,
      label: input.dataset.label ?? input.value
    }))
    .filter((item) => item.value);
}

function buildHiddenInputSelector(fieldName) {
  if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
    return `input[type="hidden"][name="${CSS.escape(fieldName)}"]`;
  }
  return `input[type="hidden"][name="${fieldName.replace(/"/g, '\\"')}"]`;
}

function normalizePageSize(value) {
  const parsed = Number.parseInt(value ?? '', 10);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return 20;
  }
  return Math.min(parsed, 50);
}

function normalizeMaxSelections(value) {
  const parsed = Number.parseInt(value ?? '', 10);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return 0;
  }
  return parsed;
}

function generateId(prefix) {
  const random = Math.random().toString(36).slice(2, 10);
  return `${prefix}-${random}`;
}
