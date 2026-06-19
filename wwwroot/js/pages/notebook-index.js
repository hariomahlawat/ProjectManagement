// SECTION: Notebook page bootstrap under CSP-safe external script
import { initNotebookApp } from '../notebook/notebook-app.js';

// SECTION: Legacy textarea autosize and board view preference
function initLegacyNotebookEnhancements() {
  document.querySelectorAll('[data-autoresize]').forEach((textarea) => {
    const resize = () => { textarea.style.height = 'auto'; textarea.style.height = `${textarea.scrollHeight}px`; };
    textarea.addEventListener('input', resize); resize();
  });
  document.querySelectorAll('[data-submit-on-change]').forEach((input) => input.addEventListener('change', () => input.form?.submit()));
  const root = document.querySelector('.notebook-shell');
  const saved = localStorage.getItem('notebook-board-view') || 'grid';
  root?.setAttribute('data-board-view', saved);
  document.querySelectorAll('[data-notebook-view]').forEach((button) => button.addEventListener('click', () => { localStorage.setItem('notebook-board-view', button.dataset.notebookView); root?.setAttribute('data-board-view', button.dataset.notebookView); }));
}

document.addEventListener('DOMContentLoaded', () => { initLegacyNotebookEnhancements(); initNotebookApp(); });
