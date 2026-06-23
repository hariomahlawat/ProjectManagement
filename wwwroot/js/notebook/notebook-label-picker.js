import { NotebookApi } from './notebook-api.js';

const state = { labels: [] };

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

export function setNotebookLabelCatalog(labels = [], documentRef = document) {
  state.labels = (Array.isArray(labels) ? labels : []).map((label) => ({
    id: Number(label?.id || 0),
    name: normaliseLabelName(label?.name),
    count: Number(label?.count || 0)
  })).filter((label) => label.id > 0 && label.name)
    .sort((a, b) => a.name.localeCompare(b.name));

  documentRef.dispatchEvent(new CustomEvent('notebook:labels-changed', {
    detail: { labels: state.labels.map((label) => ({ ...label })) }
  }));
}

export function getNotebookLabelCatalog(documentRef = document) {
  const script = documentRef.querySelector('#notebook-label-catalog');
  if (script && state.labels.length === 0) {
    try { setNotebookLabelCatalog(JSON.parse(script.textContent || '[]'), documentRef); }
    catch { state.labels = []; }
  }
  return state.labels.map((label) => ({ ...label }));
}

export async function refreshNotebookLabelCatalog(documentRef = document) {
  const labels = await NotebookApi.getLabels();
  setNotebookLabelCatalog(labels || [], documentRef);
  return getNotebookLabelCatalog(documentRef);
}

export function initNotebookLabelPicker(root, options = {}) {
  if (!root) return null;
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

  function setBusy(value) {
    busy = Boolean(value);
    if (toggle) toggle.disabled = busy;
    input.disabled = busy;
    suggestions.querySelectorAll('button').forEach((button) => { button.disabled = busy; });
    create.disabled = busy;
    root.classList.toggle('is-busy', busy);
  }

  function renderSelected() {
    selectedRoot.innerHTML = '';
    selected.forEach((name) => {
      const chip = document.createElement('button');
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
    const queryText = normaliseLabelName(input.value);
    const query = queryText.toLocaleLowerCase();
    const selectedKeys = new Set(selected.map((x) => x.toLocaleLowerCase()));
    const matches = getNotebookLabelCatalog().filter((label) => !query || label.name.toLocaleLowerCase().includes(query));

    suggestions.innerHTML = '';
    matches.slice(0, 50).forEach((label) => {
      const checked = selectedKeys.has(label.name.toLocaleLowerCase());
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'notebook-label-picker__suggestion';
      button.dataset.toggleLabel = label.name;
      button.setAttribute('role', 'option');
      button.setAttribute('aria-selected', String(checked));
      button.innerHTML = `<i class="bi ${checked ? 'bi-check-square' : 'bi-square'}" aria-hidden="true"></i><span>${escapeHtml(label.name)}</span><small>${label.count}</small>`;
      suggestions.appendChild(button);
    });

    const exact = getNotebookLabelCatalog().some((label) => label.name.toLocaleLowerCase() === query);
    create.hidden = !queryText || exact;
    create.textContent = queryText ? `Create “${queryText}”` : '';
  }

  async function commit(next) {
    if (busy) return;
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
    const rect = anchor.getBoundingClientRect();
    const width = Math.min(320, window.innerWidth - 24);
    const left = Math.min(Math.max(12, rect.left), window.innerWidth - width - 12);
    const top = Math.min(rect.bottom + 8, window.innerHeight - 360);
    floatingHost.style.left = `${left}px`;
    floatingHost.style.top = `${Math.max(12, top)}px`;
    floatingHost.style.width = `${width}px`;
  }

  function open(anchor = null) {
    if (busy) return;
    if (floatingHost) {
      floatingHost.hidden = false;
      positionFloating(anchor);
    }
    popover.hidden = false;
    toggle?.setAttribute('aria-expanded', 'true');
    renderSelected();
    renderSuggestions();
    queueMicrotask(() => input.focus());
  }

  function closePicker() {
    popover.hidden = true;
    toggle?.setAttribute('aria-expanded', 'false');
    input.value = '';
    renderSuggestions();
    if (floatingHost) floatingHost.hidden = true;
  }

  toggle?.addEventListener('click', () => popover.hidden ? open(toggle) : closePicker());
  close.addEventListener('click', closePicker);
  input.addEventListener('input', renderSuggestions);
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') { event.preventDefault(); closePicker(); }
    if (event.key === 'Enter') {
      event.preventDefault();
      const first = suggestions.querySelector('[data-toggle-label]');
      if (first) first.click();
      else if (!create.hidden) create.click();
    }
  });

  suggestions.addEventListener('click', (event) => {
    const button = event.target.closest('[data-toggle-label]');
    if (!button) return;
    const name = button.dataset.toggleLabel;
    const exists = selected.some((value) => value.toLocaleLowerCase() === name.toLocaleLowerCase());
    const next = exists
      ? selected.filter((value) => value.toLocaleLowerCase() !== name.toLocaleLowerCase())
      : [...selected, name];
    commit(next).catch(() => {});
  });

  selectedRoot.addEventListener('click', (event) => {
    const button = event.target.closest('[data-remove-label]');
    if (!button) return;
    const key = button.dataset.removeLabel.toLocaleLowerCase();
    commit(selected.filter((value) => value.toLocaleLowerCase() !== key)).catch(() => {});
  });

  create.addEventListener('click', async () => {
    const name = normaliseLabelName(input.value);
    if (!name || busy) return;
    try {
      setBusy(true);
      const result = await NotebookApi.createLabel(name);
      setNotebookLabelCatalog(result?.labels || []);
      const canonical = result?.label?.name || name;
      input.value = '';
      setBusy(false);
      await commit([...selected, canonical]);
    } catch (error) {
      setBusy(false);
      options.onError?.(error);
    }
  });

  document.addEventListener('click', (event) => {
    if (popover.hidden) return;
    if (root.contains(event.target) || floatingHost?.contains(event.target)) return;
    closePicker();
  });

  document.addEventListener('notebook:labels-changed', renderSuggestions);
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
    refresh: () => refreshNotebookLabelCatalog()
  };
}

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, (character) => ({ '&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;' }[character]));
}
