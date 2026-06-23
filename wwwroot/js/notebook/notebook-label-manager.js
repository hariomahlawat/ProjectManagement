import { NotebookApi } from './notebook-api.js';
import { getNotebookLabelCatalog, normaliseLabelName, refreshNotebookLabelCatalog, setNotebookLabelCatalog } from './notebook-label-picker.js';

export function initNotebookLabelManager(root, options = {}) {
  if (!root) return null;
  const feedback = root.querySelector('[data-label-manager-feedback]');
  const list = root.querySelector('[data-label-manager-list]');
  const empty = root.querySelector('[data-label-manager-empty]');
  const loading = root.querySelector('[data-label-manager-loading]');
  const createInput = root.querySelector('[data-label-manager-create-input]');
  const createButton = root.querySelector('[data-label-manager-create]');
  let busy = false;

  const setFeedback = (text = '', error = false) => {
    feedback.textContent = text;
    feedback.hidden = !text;
    feedback.classList.toggle('is-error', error);
  };

  function render(labels = getNotebookLabelCatalog()) {
    list.innerHTML = '';
    empty.hidden = labels.length !== 0;
    labels.forEach((label) => {
      const row = document.createElement('div');
      row.className = 'notebook-label-manager__row';
      row.dataset.labelId = String(label.id);
      row.dataset.originalName = label.name;
      row.innerHTML = `
        <i class="bi bi-tag" aria-hidden="true"></i>
        <input value="${escapeHtml(label.name)}" maxlength="60" aria-label="Label name ${escapeHtml(label.name)}" />
        <span title="${label.count} labelled items">${label.count}</span>
        <button type="button" data-label-rename aria-label="Save ${escapeHtml(label.name)}"><i class="bi bi-check-lg"></i></button>
        <button type="button" data-label-delete aria-label="Delete ${escapeHtml(label.name)}"><i class="bi bi-trash"></i></button>`;
      list.appendChild(row);
    });
  }

  async function load() {
    loading.hidden = false;
    empty.hidden = true;
    try {
      const labels = await refreshNotebookLabelCatalog();
      render(labels);
    } catch (error) {
      setFeedback(error.message || 'Unable to load labels.', true);
    } finally {
      loading.hidden = true;
    }
  }

  async function createLabel() {
    const name = normaliseLabelName(createInput.value);
    if (!name || busy) {
      if (!name) { setFeedback('Enter a label name.', true); createInput.focus(); }
      return;
    }
    try {
      busy = true;
      createButton.disabled = true;
      setFeedback('Creating…');
      const result = await NotebookApi.createLabel(name);
      setNotebookLabelCatalog(result.labels || []);
      render(result.labels || []);
      createInput.value = '';
      setFeedback('Label created.');
      options.onCatalogChange?.(result.labels || []);
      createInput.focus();
    } catch (error) {
      setFeedback(error.message || 'Unable to create the label.', true);
    } finally {
      busy = false;
      createButton.disabled = false;
    }
  }

  const open = async () => {
    root.hidden = false;
    document.body.classList.add('notebook-modal-open');
    setFeedback('');
    await load();
    queueMicrotask(() => createInput.focus());
  };

  const close = () => {
    root.hidden = true;
    document.body.classList.remove('notebook-modal-open');
    setFeedback('');
  };

  createButton.addEventListener('click', createLabel);
  createInput.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') { event.preventDefault(); createLabel(); }
    if (event.key === 'Escape' && createInput.value) { createInput.value = ''; }
  });

  root.addEventListener('click', async (event) => {
    if (event.target.closest('[data-label-manager-close]')) { close(); return; }
    const row = event.target.closest('[data-label-id]');
    if (!row || busy) return;
    const id = Number(row.dataset.labelId);
    const input = row.querySelector('input');

    if (event.target.closest('[data-label-rename]')) {
      const name = normaliseLabelName(input.value);
      if (!name) { setFeedback('Enter a label name.', true); input.focus(); return; }
      try {
        busy = true;
        setFeedback('Saving…');
        const result = await NotebookApi.renameLabel(id, name);
        setNotebookLabelCatalog(result.labels || []);
        render(result.labels || []);
        setFeedback('Label updated.');
        options.onCatalogChange?.(result.labels || []);
      } catch (error) {
        input.value = row.dataset.originalName;
        setFeedback(error.message || 'Unable to rename the label.', true);
      } finally { busy = false; }
    }

    if (event.target.closest('[data-label-delete]')) {
      if (!confirm(`Delete label “${input.value}” from all notes? Notes will not be deleted.`)) return;
      try {
        busy = true;
        setFeedback('Deleting…');
        const result = await NotebookApi.deleteLabel(id);
        setNotebookLabelCatalog(result.labels || []);
        render(result.labels || []);
        setFeedback('Label deleted.');
        options.onCatalogChange?.(result.labels || []);
        const currentTag = new URL(location.href).searchParams.get('tag');
        if (currentTag && currentTag.toLocaleLowerCase() === row.dataset.originalName.toLocaleLowerCase()) {
          history.replaceState({}, '', '/Notebook?view=labels');
        }
      } catch (error) {
        setFeedback(error.message || 'Unable to delete the label.', true);
      } finally { busy = false; }
    }
  });

  document.addEventListener('keydown', (event) => { if (event.key === 'Escape' && !root.hidden) close(); });
  render(getNotebookLabelCatalog());
  return { open, close, reload: load, render };
}

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, (character) => ({ '&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;' }[character]));
}
