// SECTION: Notebook page bootstrap under CSP-safe external script
import { initNotebookApp } from '../notebook/notebook-app.js';

// SECTION: Legacy textarea autosize and board view preference
function initLegacyNotebookEnhancements() {
  // SECTION: Textarea sizing helpers
  document.querySelectorAll('[data-autoresize]').forEach((textarea) => {
    const resize = () => { textarea.style.height = 'auto'; textarea.style.height = `${textarea.scrollHeight}px`; };
    textarea.addEventListener('input', resize); resize();
  });

  // SECTION: Drawer type-specific editor fields
  const typeSelect = document.querySelector('[data-notebook-type-select]');
  const fieldGroups = Array.from(document.querySelectorAll('[data-notebook-type-fields]'));
  const normalize = (value) => (value || '').toString().trim().toLowerCase();
  const selectedTypeName = () => normalize(typeSelect?.options[typeSelect.selectedIndex]?.text || typeSelect?.value);
  const setGroupEnabled = (group, isEnabled) => {
    group.hidden = !isEnabled;
    group.querySelectorAll('input, select, textarea, button').forEach((control) => { control.disabled = !isEnabled; });
  };
  const updateFields = () => {
    const selected = selectedTypeName();
    fieldGroups.forEach((group) => {
      const allowedTypes = (group.dataset.notebookTypeFields || '').split(',').map(normalize);
      setGroupEnabled(group, allowedTypes.includes(selected));
    });
  };
  if (typeSelect && fieldGroups.length) {
    typeSelect.addEventListener('change', updateFields);
    updateFields();
  }

  // SECTION: Lightweight server-rendered form helpers
  document.querySelectorAll('[data-submit-on-change]').forEach((input) => input.addEventListener('change', () => input.form?.submit()));
  const root = document.querySelector('.notebook-shell');
  const saved = localStorage.getItem('notebook-board-view') || 'grid';
  root?.setAttribute('data-board-view', saved);
  document.querySelectorAll('[data-notebook-view]').forEach((button) => button.addEventListener('click', () => { localStorage.setItem('notebook-board-view', button.dataset.notebookView); root?.setAttribute('data-board-view', button.dataset.notebookView); }));
}

document.addEventListener('DOMContentLoaded', () => { initLegacyNotebookEnhancements(); initNotebookApp(); });
