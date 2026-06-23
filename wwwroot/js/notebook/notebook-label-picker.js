import { NotebookApi } from './notebook-api.js';

const state = {
  labels: [],
  initialised: false
};

export function normaliseLabelName(value) {
  return String(value || '').trim().replace(/^#+/, '').trim();
}

export function normaliseLabels(values) {
  const seen = new Set();
  return (Array.isArray(values) ? values : []).map(normaliseLabelName).filter((name) => {
    if (!name) return false;
    const key = name.toLocaleLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function normaliseCatalogue(labels = []) {
  return (Array.isArray(labels) ? labels : []).map((label) => ({
    id: Number(label?.id || 0),
    name: normaliseLabelName(label?.name),
    count: Number(label?.count || 0)
  })).filter((label) => label.id > 0 && label.name)
    .sort((a, b) => a.name.localeCompare(b.name));
}

function cataloguesEqual(current, next) {
  if (current.length !== next.length) return false;
  return current.every((label, index) => {
    const candidate = next[index];
    return label.id === candidate.id &&
      label.name === candidate.name &&
      label.count === candidate.count;
  });
}

function cloneCatalogue() {
  return state.labels.map((label) => ({ ...label }));
}

function dispatchCatalogueChanged(documentRef, labels) {
  if (!documentRef?.dispatchEvent) return;
  const EventCtor = documentRef.defaultView?.CustomEvent ?? globalThis.CustomEvent;
  if (!EventCtor) return;
  documentRef.dispatchEvent(new EventCtor('notebook:labels-changed', {
    detail: { labels: labels.map((label) => ({ ...label })) }
  }));
}

export function setNotebookLabelCatalog(labels = [], documentRef = document, options = {}) {
  const next = normaliseCatalogue(labels);
  const changed = !cataloguesEqual(state.labels, next);
  state.labels = next;
  state.initialised = options.markInitialised !== false;

  if (changed && options.notify !== false) {
    dispatchCatalogueChanged(documentRef, state.labels);
  }

  return cloneCatalogue();
}

export function hydrateNotebookLabelCatalog(documentRef = document) {
  if (state.initialised) return cloneCatalogue();

  const script = documentRef?.querySelector?.('#notebook-label-catalog');
  let labels = [];
  if (script) {
    try {
      labels = JSON.parse(script.textContent || '[]');
    } catch {
      labels = [];
    }
  }

  return setNotebookLabelCatalog(labels, documentRef, {
    notify: false,
    markInitialised: true
  });
}

export function getNotebookLabelCatalog() {
  return cloneCatalogue();
}

export async function refreshNotebookLabelCatalog(documentRef = document) {
  const labels = await NotebookApi.getLabels();
  return setNotebookLabelCatalog(labels || [], documentRef, {
    notify: true,
    markInitialised: true
  });
}

export function resetNotebookLabelCatalogForTests() {
  state.labels = [];
  state.initialised = false;
}

export function initNotebookLabelPicker(root, options = {}) {
  if (!root) return null;
  const documentRef = root.ownerDocument || document;
  const toggle = root.querySelector('[data-label-picker-toggle]');
  const popover = root.querySelector('[data-label-picker-popover]');
  const close = root.querySelector('[data-label-picker-close]');
  const selectedRoot = root.querySelector('[data-label-picker-selected]');
  const input = root.querySelector('[data-label-picker-input]');
  const suggestions = root.querySelector('[data-label-picker-suggestions]');
  const create = root.querySelector('[data-label-picker-create]');
  const floatingHost = root.closest('[data-notebook-card-label-host]');
  let selected = normaliseLabels(options.value || []);
  let busy = false;
  let onChange = options.onChange;
  let destroyed = false;

  function setBusy(value) {
    if (destroyed) return;
    busy = Boolean(value);
    if (toggle) toggle.disabled = busy;
    if (input) input.disabled = busy;
    suggestions?.querySelectorAll('button').forEach((button) => { button.disabled = busy; });
    if (create) create.disabled = busy;
    root.classList.toggle('is-busy', busy);
  }

  function renderSelected() {
    if (destroyed || !selectedRoot) return;
    selectedRoot.innerHTML = '';
    selected.forEach((name) => {
      const chip = documentRef.createElement('button');
      chip.type = 'button';
      chip.className = 'notebook-label-chip';
      chip.dataset.removeLabel = name;
      chip.setAttribute('aria-label', `Remove label ${name}`);
      chip.innerHTML = `<span>${escapeHtml(name)}</span><i class="bi bi-x"></i>`;
      selectedRoot.appendChild(chip);
    });
    selectedRoot.hidden = selected.length === 0;
  }

  function renderSuggestions() {
    if (destroyed || !input || !suggestions || !create) return;
    const queryText = normaliseLabelName(input.value);
    const query = queryText.toLocaleLowerCase();
    const selectedKeys = new Set(selected.map((x) => x.toLocaleLowerCase()));
    const catalogue = getNotebookLabelCatalog();
    const matches = catalogue.filter((label) => !query || label.name.toLocaleLowerCase().includes(query));

    suggestions.innerHTML = '';
    matches.slice(0, 50).forEach((label) => {
      const checked = selectedKeys.has(label.name.toLocaleLowerCase());
      const button = documentRef.createElement('button');
      button.type = 'button';
      button.className = 'notebook-label-picker__suggestion';
      button.dataset.toggleLabel = label.name;
      button.setAttribute('role', 'option');
      button.setAttribute('aria-selected', String(checked));
      button.innerHTML = `<i class="bi ${checked ? 'bi-check-square' : 'bi-square'}" aria-hidden="true"></i><span>${escapeHtml(label.name)}</span><small>${label.count}</small>`;
      suggestions.appendChild(button);
    });

    const exact = catalogue.some((label) => label.name.toLocaleLowerCase() === query);
    create.hidden = !queryText || exact;
    create.textContent = queryText ? `Create “${queryText}”` : '';
  }

  async function commit(next) {
    if (busy || destroyed) return;
    const previous = selected;
    selected = normaliseLabels(next);
    renderSelected();
    renderSuggestions();
    try {
      setBusy(true);
      await onChange?.([...selected], [...previous]);
    } catch (error) {
      selected = previous;
      renderSelected();
      renderSuggestions();
      throw error;
    } finally {
      setBusy(false);
    }
  }

  function positionFloating(anchor) {
    if (!floatingHost || !anchor) return;
    const view = documentRef.defaultView || window;
    const rect = anchor.getBoundingClientRect();
    const width = Math.min(320, view.innerWidth - 24);
    const left = Math.min(Math.max(12, rect.left), view.innerWidth - width - 12);
    const top = Math.min(rect.bottom + 8, view.innerHeight - 360);
    floatingHost.style.left = `${left}px`;
    floatingHost.style.top = `${Math.max(12, top)}px`;
    floatingHost.style.width = `${width}px`;
  }

  function open(anchor = null) {
    if (busy || destroyed || !popover) return;
    if (floatingHost) {
      floatingHost.hidden = false;
      positionFloating(anchor);
    }
    popover.hidden = false;
    toggle?.setAttribute('aria-expanded', 'true');
    renderSelected();
    renderSuggestions();
    queueMicrotask(() => input?.focus());
  }

  function closePicker() {
    if (destroyed || !popover) return;
    popover.hidden = true;
    toggle?.setAttribute('aria-expanded', 'false');
    if (input) input.value = '';
    renderSuggestions();
    if (floatingHost) floatingHost.hidden = true;
  }

  const handleToggleClick = () => popover?.hidden ? open(toggle) : closePicker();
  const handleInput = () => renderSuggestions();
  const handleInputKeydown = (event) => {
    if (event.key === 'Escape') { event.preventDefault(); closePicker(); }
    if (event.key === 'Enter') {
      event.preventDefault();
      const first = suggestions?.querySelector('[data-toggle-label]');
      if (first) first.click();
      else if (create && !create.hidden) create.click();
    }
  };
  const handleSuggestionClick = (event) => {
    const button = event.target.closest('[data-toggle-label]');
    if (!button) return;
    const name = button.dataset.toggleLabel;
    const exists = selected.some((value) => value.toLocaleLowerCase() === name.toLocaleLowerCase());
    const next = exists
      ? selected.filter((value) => value.toLocaleLowerCase() !== name.toLocaleLowerCase())
      : [...selected, name];
    commit(next).catch(() => {});
  };
  const handleSelectedClick = (event) => {
    const button = event.target.closest('[data-remove-label]');
    if (!button) return;
    const key = button.dataset.removeLabel.toLocaleLowerCase();
    commit(selected.filter((value) => value.toLocaleLowerCase() !== key)).catch(() => {});
  };
  const handleCreateClick = async () => {
    const name = normaliseLabelName(input?.value);
    if (!name || busy || destroyed) return;
    try {
      setBusy(true);
      const result = await NotebookApi.createLabel(name);
      setNotebookLabelCatalog(result?.labels || [], documentRef);
      const canonical = result?.label?.name || name;
      if (input) input.value = '';
      setBusy(false);
      await commit([...selected, canonical]);
    } catch (error) {
      setBusy(false);
      options.onError?.(error);
    }
  };
  const handleDocumentClick = (event) => {
    if (destroyed || !popover || popover.hidden) return;
    if (root.contains(event.target) || floatingHost?.contains(event.target)) return;
    closePicker();
  };
  const handleCatalogChanged = () => renderSuggestions();

  toggle?.addEventListener('click', handleToggleClick);
  close?.addEventListener('click', closePicker);
  input?.addEventListener('input', handleInput);
  input?.addEventListener('keydown', handleInputKeydown);
  suggestions?.addEventListener('click', handleSuggestionClick);
  selectedRoot?.addEventListener('click', handleSelectedClick);
  create?.addEventListener('click', handleCreateClick);
  documentRef.addEventListener('click', handleDocumentClick);
  documentRef.addEventListener('notebook:labels-changed', handleCatalogChanged);

  renderSelected();
  renderSuggestions();

  return {
    getValue: () => [...selected],
    setValue: (value) => { selected = normaliseLabels(value); renderSelected(); renderSuggestions(); },
    setBusy,
    setOnChange: (handler) => { onChange = handler; },
    configure: ({ value, onChange: nextOnChange } = {}) => {
      if (value !== undefined) selected = normaliseLabels(value);
      if (nextOnChange !== undefined) onChange = nextOnChange;
      renderSelected();
      renderSuggestions();
    },
    open,
    close: closePicker,
    refresh: () => refreshNotebookLabelCatalog(documentRef),
    destroy: () => {
      if (destroyed) return;
      destroyed = true;
      toggle?.removeEventListener('click', handleToggleClick);
      close?.removeEventListener('click', closePicker);
      input?.removeEventListener('input', handleInput);
      input?.removeEventListener('keydown', handleInputKeydown);
      suggestions?.removeEventListener('click', handleSuggestionClick);
      selectedRoot?.removeEventListener('click', handleSelectedClick);
      create?.removeEventListener('click', handleCreateClick);
      documentRef.removeEventListener('click', handleDocumentClick);
      documentRef.removeEventListener('notebook:labels-changed', handleCatalogChanged);
      if (floatingHost) floatingHost.hidden = true;
    }
  };
}

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, (character) => ({ '&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;' }[character]));
}
