import { NotebookApi } from './notebook-api.js';
import { reconcileMutation, requireMutationItem, updateCardConcurrencyState } from './notebook-reconcile.js';
import { confirmNotebookAction } from './notebook-confirm-dialog.js';

const escapeHtml = (value) => String(value || '').replace(/[&<>"']/g, (character) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
const normaliseRole = (role) => String(role).toLowerCase() === 'viewer' || Number(role) === 1 ? 'Viewer' : 'Editor';
const roleLabel = (role) => normaliseRole(role) === 'Viewer' ? 'View only' : 'Can edit';

export function initNotebookCollaborators(root, options = {}) {
  const dialog = root?.querySelector('[data-notebook-collaborators-dialog]');
  if (!dialog) return null;

  const panel = dialog.querySelector('.notebook-collaborators-dialog__panel');
  const list = dialog.querySelector('[data-collaborator-list]');
  const empty = dialog.querySelector('[data-collaborators-empty]');
  const management = dialog.querySelector('[data-collaborators-management]');
  const intro = dialog.querySelector('[data-collaborators-intro]');
  const search = dialog.querySelector('[data-collaborator-search]');
  const searchWrap = dialog.querySelector('[data-collaborators-search-wrap]');
  const results = dialog.querySelector('[data-collaborator-search-results]');
  const spinner = dialog.querySelector('[data-collaborator-search-spinner]');
  const status = dialog.querySelector('[data-collaborators-status]');
  const sharePanel = dialog.querySelector('[data-collaborator-share-panel]');
  const shareAvatar = dialog.querySelector('[data-share-avatar]');
  const shareName = dialog.querySelector('[data-share-name]');
  const shareEmail = dialog.querySelector('[data-share-email]');
  const shareRole = dialog.querySelector('[data-share-role]');
  const shareConfirm = dialog.querySelector('[data-share-confirm]');

  let activeCard = null;
  let currentItem = null;
  let selectedUser = null;
  let searchTimer = 0;
  let searchController = null;
  let busy = false;

  const setStatus = (message = '') => { status.textContent = message; };
  const canManage = () => Boolean(currentItem?.canManageCollaborators ?? String(currentItem?.accessLevel || activeCard?.dataset?.accessLevel || '').toLowerCase() === 'owner');

  function clearSearchResults() {
    results.hidden = true;
    results.replaceChildren();
  }

  function clearSelection({ preserveSearch = false } = {}) {
    selectedUser = null;
    sharePanel.hidden = true;
    shareConfirm.disabled = false;
    shareRole.value = 'Viewer';
    if (!preserveSearch) search.value = '';
  }

  function close() {
    clearTimeout(searchTimer);
    searchController?.abort();
    dialog.hidden = true;
    document.body.classList.remove('notebook-dialog-open');
    clearSearchResults();
    clearSelection();
    setStatus('');
    activeCard = null;
    currentItem = null;
    busy = false;
  }

  function render(rows) {
    list.replaceChildren();
    const collaborators = Array.isArray(rows) ? rows : [];
    const nonOwners = collaborators.filter((row) => !row.isOwner);
    empty.hidden = nonOwners.length > 0;

    collaborators.forEach((row) => {
      const item = document.createElement('div');
      item.className = 'notebook-collaborator-row';
      const role = normaliseRole(row.role);
      const permission = row.isOwner
        ? '<span class="notebook-collaborator-role">Owner</span>'
        : canManage()
          ? `<label class="notebook-collaborator-role-control"><span class="visually-hidden">Permission for ${escapeHtml(row.displayName)}</span><select data-collaborator-role="${escapeHtml(row.userId)}" data-current-role="${role}" aria-label="Permission for ${escapeHtml(row.displayName)}"><option value="Viewer" ${role === 'Viewer' ? 'selected' : ''}>View only</option><option value="Editor" ${role === 'Editor' ? 'selected' : ''}>Can edit</option></select></label>`
          : `<span class="notebook-collaborator-role">${roleLabel(role)}</span>`;

      item.innerHTML = `
        <span class="notebook-collaborator-avatar">${escapeHtml(row.initials || initials(row.displayName))}</span>
        <span class="notebook-collaborator-row__identity"><strong>${escapeHtml(row.displayName)}</strong><small>${escapeHtml(row.email)}</small></span>
        ${permission}
        ${!row.isOwner && canManage() ? `<button type="button" class="notebook-dialog-icon text-danger" data-remove-collaborator="${escapeHtml(row.userId)}" aria-label="Remove ${escapeHtml(row.displayName)}" title="Remove collaborator"><i class="bi bi-x-circle"></i></button>` : ''}`;
      list.appendChild(item);
    });

    management.hidden = !canManage();
    searchWrap.hidden = !canManage();
    intro.textContent = canManage()
      ? 'Share this note and control whether each person can edit or only view it.'
      : 'People who currently have access to this note.';
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
    clearSelection();
    clearSearchResults();
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
    await reconcileMutation({
      response,
      board: options.board,
      view: options.view,
      getCardHtml: NotebookApi.getCardHtml,
      applyCounts: options.applyCounts,
      preservePosition: true,
      showGlobalError: options.showError,
      existingCard: activeCard
    });
    activeCard = document.querySelector(`[data-note-id="${updated.id}"]`) || activeCard;
    currentItem = updated;
    options.onItemUpdated?.(updated);
    await refresh();
  }

  function selectSearchResult(row) {
    selectedUser = row;
    shareAvatar.textContent = row.initials || initials(row.displayName);
    shareName.textContent = row.displayName || 'PRISM user';
    shareEmail.textContent = row.email || '';
    shareRole.value = 'Viewer';
    sharePanel.hidden = false;
    clearSearchResults();
    shareRole.focus();
  }

  search.addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchController?.abort();
    clearSelection({ preserveSearch: true });
    clearSearchResults();
    const query = search.value.trim();
    if (query.length < 2 || !currentItem || !canManage()) return;

    searchTimer = setTimeout(async () => {
      searchController = new AbortController();
      spinner.hidden = false;
      try {
        const rows = await NotebookApi.searchCollaborators(currentItem.id, query, { signal: searchController.signal });
        results.replaceChildren();
        rows.forEach((row) => {
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'notebook-collaborator-result';
          button.dataset.selectCollaborator = row.userId;
          button.innerHTML = `<span class="notebook-collaborator-avatar">${escapeHtml(row.initials || initials(row.displayName))}</span><span><strong>${escapeHtml(row.displayName)}</strong><small>${escapeHtml(row.email)}</small></span><i class="bi bi-chevron-right"></i>`;
          button.addEventListener('click', () => selectSearchResult(row));
          results.appendChild(button);
        });
        if (!rows.length) results.innerHTML = '<p class="notebook-collaborator-result-empty">No matching active PRISM users.</p>';
        results.hidden = false;
      } catch (error) {
        if (error?.code !== 'notebook_request_aborted') options.showError?.(error?.message || 'User search failed.');
      } finally {
        spinner.hidden = true;
        searchController = null;
      }
    }, 250);
  });

  shareConfirm.addEventListener('click', async () => {
    if (!selectedUser || !currentItem || busy) return;
    busy = true;
    shareConfirm.disabled = true;
    setStatus(`Sharing with ${selectedUser.displayName}…`);
    try {
      await reconcile(await NotebookApi.addCollaborator(currentItem.id, selectedUser.userId, shareRole.value, currentItem.version));
      setStatus(`${selectedUser.displayName} now has ${roleLabel(shareRole.value).toLowerCase()} access.`);
      clearSelection();
      search.focus();
    } catch (error) {
      setStatus('');
      options.showError?.(error?.message || 'Collaborator could not be added.');
    } finally {
      busy = false;
      shareConfirm.disabled = false;
    }
  });

  dialog.querySelector('[data-share-cancel]')?.addEventListener('click', () => {
    clearSelection();
    clearSearchResults();
    search.focus();
  });

  list.addEventListener('change', async (event) => {
    const select = event.target.closest('[data-collaborator-role]');
    if (!select || !currentItem || busy) return;
    const previousRole = select.dataset.currentRole || 'Viewer';
    if (select.value === previousRole) return;

    busy = true;
    select.disabled = true;
    setStatus('Changing permission…');
    try {
      await reconcile(await NotebookApi.updateCollaboratorRole(currentItem.id, select.dataset.collaboratorRole, select.value, currentItem.version));
      setStatus(`Permission changed to ${roleLabel(select.value)}.`);
    } catch (error) {
      select.value = previousRole;
      setStatus('');
      options.showError?.(error?.message || 'Permission could not be changed.');
    } finally {
      busy = false;
      select.disabled = false;
    }
  });

  list.addEventListener('click', async (event) => {
    const remove = event.target.closest('[data-remove-collaborator]');
    if (!remove || !currentItem || busy) return;
    const name = remove.closest('.notebook-collaborator-row')?.querySelector('strong')?.textContent?.trim() || 'This person';
    const confirmed = await confirmNotebookAction({
      title: `Remove ${name}?`,
      message: `${name} will immediately lose access to this shared note.`,
      confirmText: 'Remove',
      tone: 'danger'
    });
    if (!confirmed) return;

    busy = true;
    remove.disabled = true;
    setStatus(`Removing ${name}…`);
    try {
      await reconcile(await NotebookApi.removeCollaborator(currentItem.id, remove.dataset.removeCollaborator, currentItem.version));
      setStatus(`${name} no longer has access.`);
    } catch (error) {
      setStatus('');
      options.showError?.(error?.message || 'Collaborator could not be removed.');
    } finally {
      busy = false;
      remove.disabled = false;
    }
  });

  dialog.addEventListener('click', (event) => {
    if (event.target.closest('[data-collaborators-close]')) close();
  });
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape' && !dialog.hidden) close(); });

  return { open, close };
}

function initials(value) {
  return String(value || '').trim().split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase()).join('');
}
