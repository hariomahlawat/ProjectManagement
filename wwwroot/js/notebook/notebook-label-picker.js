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

export function setNotebookLabelCatalog(labels = []) {
  state.labels = (Array.isArray(labels) ? labels : []).map((label) => ({
    id: Number(label?.id || 0), name: normaliseLabelName(label?.name), count: Number(label?.count || 0)
  })).filter((label) => label.name).sort((a, b) => a.name.localeCompare(b.name));
}

export function getNotebookLabelCatalog(documentRef = document) {
  const script = documentRef.querySelector('#notebook-label-catalog');
  if (script && state.labels.length === 0) {
    try { setNotebookLabelCatalog(JSON.parse(script.textContent || '[]')); } catch { setNotebookLabelCatalog([]); }
  }
  return state.labels.map((label) => ({ ...label }));
}

export function initNotebookLabelPicker(root, options = {}) {
  const toggle = root.querySelector('[data-label-picker-toggle]');
  const popover = root.querySelector('[data-label-picker-popover]');
  const close = root.querySelector('[data-label-picker-close]');
  const selectedRoot = root.querySelector('[data-label-picker-selected]');
  const input = root.querySelector('[data-label-picker-input]');
  const suggestions = root.querySelector('[data-label-picker-suggestions]');
  const create = root.querySelector('[data-label-picker-create]');
  let selected = normaliseLabels(options.value || []);
  let busy = false;

  function renderSelected() {
    selectedRoot.innerHTML = '';
    selected.forEach((name) => {
      const chip = document.createElement('button');
      chip.type = 'button'; chip.className = 'notebook-label-chip'; chip.dataset.removeLabel = name;
      chip.innerHTML = `<span>${escapeHtml(name)}</span><i class="bi bi-x"></i>`;
      selectedRoot.appendChild(chip);
    });
  }

  function renderSuggestions() {
    const query = normaliseLabelName(input.value).toLocaleLowerCase();
    const selectedKeys = new Set(selected.map((x) => x.toLocaleLowerCase()));
    const matches = getNotebookLabelCatalog().filter((label) => !selectedKeys.has(label.name.toLocaleLowerCase()) && (!query || label.name.toLocaleLowerCase().includes(query)));
    suggestions.innerHTML = '';
    matches.slice(0, 12).forEach((label) => {
      const button = document.createElement('button');
      button.type = 'button'; button.className = 'notebook-label-picker__suggestion'; button.dataset.selectLabel = label.name;
      button.innerHTML = `<i class="bi bi-tag"></i><span>${escapeHtml(label.name)}</span><small>${label.count}</small>`;
      suggestions.appendChild(button);
    });
    const exact = getNotebookLabelCatalog().some((label) => label.name.toLocaleLowerCase() === query);
    create.hidden = !query || exact || selectedKeys.has(query);
    create.textContent = query ? `Create “${normaliseLabelName(input.value)}”` : '';
  }

  async function commit(next) {
    if (busy) return;
    const previous = selected;
    selected = normaliseLabels(next);
    renderSelected(); renderSuggestions();
    try { await options.onChange?.([...selected], [...previous]); }
    catch (error) { selected = previous; renderSelected(); renderSuggestions(); throw error; }
  }

  function open() { if (busy) return; popover.hidden = false; toggle.setAttribute('aria-expanded', 'true'); renderSuggestions(); queueMicrotask(() => input.focus()); }
  function closePicker() { popover.hidden = true; toggle.setAttribute('aria-expanded', 'false'); input.value = ''; renderSuggestions(); }

  toggle.addEventListener('click', () => popover.hidden ? open() : closePicker());
  close.addEventListener('click', closePicker);
  input.addEventListener('input', renderSuggestions);
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') { event.preventDefault(); closePicker(); }
    if (event.key === 'Enter') { event.preventDefault(); const first = suggestions.querySelector('[data-select-label]'); if (first) first.click(); else if (!create.hidden) create.click(); }
  });
  suggestions.addEventListener('click', (event) => { const button = event.target.closest('[data-select-label]'); if (button) commit([...selected, button.dataset.selectLabel]).catch(() => {}); });
  selectedRoot.addEventListener('click', (event) => { const button = event.target.closest('[data-remove-label]'); if (button) commit(selected.filter((x) => x !== button.dataset.removeLabel)).catch(() => {}); });
  create.addEventListener('click', () => { const name = normaliseLabelName(input.value); if (!name) return; setNotebookLabelCatalog([...getNotebookLabelCatalog(), { id: 0, name, count: 0 }]); input.value = ''; commit([...selected, name]).catch(() => {}); });
  document.addEventListener('click', (event) => { if (!popover.hidden && !root.contains(event.target)) closePicker(); });

  renderSelected(); renderSuggestions();
  return {
    getValue: () => [...selected],
    setValue: (value) => { selected = normaliseLabels(value); renderSelected(); renderSuggestions(); },
    setBusy: (value) => { busy = Boolean(value); toggle.disabled = busy; input.disabled = busy; },
    close: closePicker
  };
}

function escapeHtml(value) { return String(value).replace(/[&<>'"]/g, (c) => ({ '&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;' }[c])); }
