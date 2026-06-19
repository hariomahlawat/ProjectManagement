import { NotebookApi } from './notebook-api.js';
import { createAutosave } from './notebook-autosave.js';

// SECTION: Modal editor component
export function initNotebookEditor(board, view) {
  let item = null, modal = null, autosave = null;
  const build = () => {
    modal = document.createElement('div'); modal.className = 'notebook-modal'; modal.hidden = true;
    modal.innerHTML = '<div class="notebook-modal__backdrop" data-close></div><section class="notebook-modal__dialog" role="dialog" aria-modal="true" aria-labelledby="notebook-modal-title"><header><input id="notebook-modal-title" class="notebook-modal__title" maxlength="200"><button type="button" class="notebook-action-icon" data-pin aria-label="Pin note"><i class="bi bi-pin-angle"></i></button></header><textarea class="notebook-modal__body" placeholder="Take a note…"></textarea><div class="notebook-modal__status" aria-live="polite"></div><footer><button type="button" class="btn btn-sm btn-link" data-close>Close</button></footer></section>';
    document.body.appendChild(modal);
    modal.addEventListener('click', e => { if (e.target.matches('[data-close]')) close(); });
    modal.querySelector('.notebook-modal__title').addEventListener('input', () => autosave?.schedule());
    modal.querySelector('.notebook-modal__body').addEventListener('input', () => autosave?.schedule());
  };
  const save = async () => { if (!item) return; const status = modal.querySelector('.notebook-modal__status'); status.textContent = 'Saving…'; const updated = await NotebookApi.updateItem(item.id, { ...item, title: modal.querySelector('.notebook-modal__title').value, body: modal.querySelector('.notebook-modal__body').value, labels: item.labels?.map(l => l.name) || [], version: item.version }); item = updated; status.textContent = 'Saved'; const html = await NotebookApi.getCardHtml(item.id, view); board.replaceCard(item.id, html); };
  const open = async (id, push = true) => { if (!modal) build(); item = await NotebookApi.getItem(id); modal.querySelector('.notebook-modal__title').value = item.title || ''; modal.querySelector('.notebook-modal__body').value = item.body || ''; autosave = createAutosave(save); modal.hidden = false; modal.querySelector('.notebook-modal__title').focus(); if (push) history.pushState({ notebookNoteId: id }, '', `/Notebook?note=${id}`); };
  const close = async () => { if (autosave?.isPending()) await autosave.flush(); if (modal) modal.hidden = true; item = null; };
  return { open, close };
}
