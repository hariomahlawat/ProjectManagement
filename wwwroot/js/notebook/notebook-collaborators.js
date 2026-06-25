import { NotebookApi } from './notebook-api.js';
import { reconcileMutation, requireMutationItem, updateCardConcurrencyState } from './notebook-reconcile.js';
import { confirmNotebookAction } from './notebook-confirm-dialog.js';

const escapeHtml = (value) => String(value || '').replace(/[&<>"']/g, (c) => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));

export function initNotebookCollaborators(root, options = {}) {
  const dialog = root?.querySelector('[data-notebook-collaborators-dialog]');
  if (!dialog) return null;
  const panel = dialog.querySelector('.notebook-collaborators-dialog__panel');
  const list = dialog.querySelector('[data-collaborator-list]');
  const empty = dialog.querySelector('[data-collaborators-empty]');
  const search = dialog.querySelector('[data-collaborator-search]');
  const searchWrap = dialog.querySelector('[data-collaborators-search-wrap]');
  const results = dialog.querySelector('[data-collaborator-search-results]');
  const spinner = dialog.querySelector('[data-collaborator-search-spinner]');
  const status = dialog.querySelector('[data-collaborators-status]');
  let activeCard = null;
  let currentItem = null;
  let timer = 0;

  const setStatus = (message = '') => { status.textContent = message; };
  const close = () => { dialog.hidden = true; document.body.classList.remove('notebook-dialog-open'); results.hidden = true; results.innerHTML = ''; search.value = ''; activeCard = null; currentItem = null; };
  const canManage = () => String(currentItem?.accessLevel || activeCard?.dataset?.accessLevel || '').toLowerCase() === 'owner';

  function render(rows) {
    list.innerHTML = '';
    const collaborators = Array.isArray(rows) ? rows : [];
    empty.hidden = collaborators.length > 1;
    collaborators.forEach((row) => {
      const item = document.createElement('div');
      item.className = 'notebook-collaborator-row';
      item.innerHTML = `
        <span class="notebook-collaborator-avatar">${escapeHtml(row.initials || initials(row.displayName))}</span>
        <span class="notebook-collaborator-row__identity"><strong>${escapeHtml(row.displayName)}</strong><small>${escapeHtml(row.email)}</small></span>
        <span class="notebook-collaborator-role">${row.isOwner ? 'Owner' : 'Can edit'}</span>
        ${!row.isOwner && canManage() ? `<button type="button" class="notebook-dialog-icon text-danger" data-remove-collaborator="${escapeHtml(row.userId)}" aria-label="Remove ${escapeHtml(row.displayName)}" title="Remove collaborator"><i class="bi bi-x-circle"></i></button>` : ''}`;
      list.appendChild(item);
    });
    searchWrap.hidden = !canManage();
  }

  async function refresh() {
    const rows = await NotebookApi.getCollaborators(currentItem.id);
    currentItem.collaborators = rows;
    render(rows);
  }

  async function open(card) {
    activeCard = card;
    dialog.hidden = false;
    document.body.classList.add('notebook-dialog-open');
    panel.focus?.();
    setStatus('Loading collaborators…');
    try {
      currentItem = await NotebookApi.getItem(card.dataset.noteId);
      await refresh();
      setStatus('');
      if (canManage()) search.focus();
    } catch (error) {
      setStatus(error?.message || 'Unable to load collaborators.');
      options.showError?.(error?.message || 'Unable to load collaborators.');
    }
  }

  async function reconcile(response) {
    const updated = requireMutationItem(response);
    updateCardConcurrencyState(activeCard, updated);
    await reconcileMutation({ response, board: options.board, view: options.view, getCardHtml: NotebookApi.getCardHtml, applyCounts: options.applyCounts, preservePosition: true, showGlobalError: options.showError, existingCard: activeCard });
    activeCard = document.querySelector(`[data-note-id="${updated.id}"]`) || activeCard;
    currentItem = updated;
    await refresh();
  }

  search.addEventListener('input', () => {
    clearTimeout(timer);
    const query = search.value.trim();
    results.innerHTML = '';
    results.hidden = true;
    if (query.length < 2 || !currentItem) return;
    timer = setTimeout(async () => {
      spinner.hidden = false;
      try {
        const rows = await NotebookApi.searchCollaborators(currentItem.id, query);
        results.innerHTML = '';
        rows.forEach((row) => {
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'notebook-collaborator-result';
          button.dataset.addCollaborator = row.userId;
          button.innerHTML = `<span class="notebook-collaborator-avatar">${escapeHtml(row.initials || initials(row.displayName))}</span><span><strong>${escapeHtml(row.displayName)}</strong><small>${escapeHtml(row.email)}</small></span><i class="bi bi-plus-circle"></i>`;
          results.appendChild(button);
        });
        if (!rows.length) results.innerHTML = '<p class="notebook-collaborator-result-empty">No matching active PRISM users.</p>';
        results.hidden = false;
      } catch (error) { options.showError?.(error?.message || 'User search failed.'); }
      finally { spinner.hidden = true; }
    }, 250);
  });

  dialog.addEventListener('click', async (event) => {
    if (event.target.closest('[data-collaborators-close]')) { close(); return; }
    const add = event.target.closest('[data-add-collaborator]');
    if (add && currentItem) {
      add.disabled = true;
      try { await reconcile(await NotebookApi.addCollaborator(currentItem.id, add.dataset.addCollaborator, currentItem.version)); search.value = ''; results.hidden = true; }
      catch (error) { options.showError?.(error?.message || 'Collaborator could not be added.'); }
      finally { add.disabled = false; }
      return;
    }
    const remove = event.target.closest('[data-remove-collaborator]');
    if (remove && currentItem) {
      const name = remove.closest('.notebook-collaborator-row')?.querySelector('strong')?.textContent?.trim() || 'This person';
      const confirmed = await confirmNotebookAction({ title: `Remove ${name}?`, message: `${name} will immediately lose access to this shared note.`, confirmText: 'Remove', tone: 'danger' });
      if (!confirmed) return;
      remove.disabled = true;
      try { await reconcile(await NotebookApi.removeCollaborator(currentItem.id, remove.dataset.removeCollaborator, currentItem.version)); }
      catch (error) { options.showError?.(error?.message || 'Collaborator could not be removed.'); }
      finally { remove.disabled = false; }
    }
  });

  document.addEventListener('keydown', (event) => { if (event.key === 'Escape' && !dialog.hidden) close(); });
  return { open, close };
}

function initials(value) {
  return String(value || '').trim().split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase()).join('');
}
