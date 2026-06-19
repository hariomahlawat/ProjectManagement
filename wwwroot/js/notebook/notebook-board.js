// SECTION: Notebook board DOM updates
export function createNotebookBoard(root = document) {
  const findCard = (id) => root.querySelector(`[data-note-id="${CSS.escape(id)}"]`);
  const getSection = (isPinned) => root.querySelector(`[data-notebook-section="${isPinned ? 'pinned' : 'others'}"]`);
  const getBoard = (isPinned) => root.querySelector(`[data-notebook-board="${isPinned ? 'pinned' : 'others'}"]`);
  const htmlToElement = (html) => { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild; };
  const refreshSectionVisibility = () => { ['pinned','others'].forEach((name) => { const section = root.querySelector(`[data-notebook-section="${name}"]`); const board = root.querySelector(`[data-notebook-board="${name}"]`); if (!section || !board) return; const count = board.querySelectorAll('[data-note-id]').length; if (name === 'pinned') section.hidden = count === 0; const countEl = root.querySelector(`[data-notebook-count="${name}"]`); if (countEl) countEl.textContent = String(count); }); };
  const refreshEmptyState = () => { const empty = root.querySelector('[data-notebook-empty-state]') || root.querySelector('[data-notebook-empty]'); if (!empty) return; const count = [...root.querySelectorAll('[data-notebook-board]')].reduce((total, board) => total + board.querySelectorAll(':scope > [data-note-id]').length, 0); empty.hidden = count > 0; };
  const upsertCard = (id, html, isPinned, options = {}) => { const current = findCard(id); const targetBoard = getBoard(isPinned); if (!targetBoard) throw new Error('Notebook target board was not found.'); const fragment = htmlToElement(html); current?.remove(); options.prepend === false ? targetBoard.append(fragment) : targetBoard.prepend(fragment); refreshSectionVisibility(); refreshEmptyState(); return fragment; };
  const replaceCard = (id, html) => { const current = findCard(id); if (!current) return null; const fragment = htmlToElement(html); current.replaceWith(fragment); refreshSectionVisibility(); refreshEmptyState(); return fragment; };
  const insertCard = (html, pinned = false) => { const fragment = htmlToElement(html); const board = getBoard(pinned); if (!board) throw new Error('Notebook target board was not found.'); board.prepend(fragment); refreshSectionVisibility(); refreshEmptyState(); return fragment; };
  const removeCard = (id) => { findCard(id)?.remove(); refreshSectionVisibility(); refreshEmptyState(); };
  return { findCard, getSection, getBoard, replaceCard, insertCard, upsertCard, removeCard, refreshSectionVisibility, refreshEmptyState };
}
