import { NotebookBoardTargetError, NotebookCardHtmlError } from './notebook-errors.js';

// SECTION: Notebook board DOM updates
export function createNotebookBoard(root = document) {
  // SECTION: Board lookup helpers
  const findCard = (id) => root.querySelector(`[data-note-id="${CSS.escape(id)}"]`);
  const getSection = (isPinned) => root.querySelector(`[data-notebook-section="${isPinned ? 'pinned' : 'others'}"]`);
  const getBoard = (isPinned) => {
    // SECTION: Prefer split home boards, then fall back to single-board views
    const namedBoard = root.querySelector(`[data-notebook-board="${isPinned ? 'pinned' : 'others'}"]`);
    return namedBoard || root.querySelector('[data-notebook-board]:not([data-notebook-board="pinned"]):not([data-notebook-board="others"])');
  };

  // SECTION: Safe server-rendered card parsing
  function htmlToCardElement(html, expectedId) {
    if (typeof html !== 'string' || !html.trim()) {
      throw new NotebookCardHtmlError('Notebook card HTML was empty.');
    }

    const template = document.createElement('template');
    template.innerHTML = html.trim();
    const elements = template.content.children;

    if (elements.length !== 1) {
      throw new NotebookCardHtmlError('Notebook card response must contain exactly one root element.');
    }

    const card = elements[0];
    if (!card.matches('[data-note-id]')) {
      throw new NotebookCardHtmlError('Notebook card response did not contain a note card.');
    }

    if (expectedId !== undefined && expectedId !== null && card.dataset.noteId !== String(expectedId)) {
      throw new NotebookCardHtmlError('Notebook card response did not match the requested note.');
    }

    return card;
  }

  // SECTION: Board state refresh helpers
  const refreshSectionVisibility = () => { ['pinned','others'].forEach((name) => { const section = root.querySelector(`[data-notebook-section="${name}"]`); const board = root.querySelector(`[data-notebook-board="${name}"]`); if (!section || !board) return; const count = board.querySelectorAll('[data-note-id]').length; if (name === 'pinned') section.hidden = count === 0; const countEl = root.querySelector(`[data-notebook-count="${name}"]`); if (countEl) countEl.textContent = String(count); }); };
  const refreshEmptyState = () => { const empty = root.querySelector('[data-notebook-empty-state="current"]') || root.querySelector('[data-notebook-empty-state]') || root.querySelector('[data-notebook-empty]'); if (!empty) return; const count = [...root.querySelectorAll('[data-notebook-board]')].reduce((total, board) => total + board.querySelectorAll(':scope > [data-note-id]').length, 0); empty.hidden = count > 0; };

  // SECTION: Card mutation helpers
  const upsertCard = (id, html, isPinned, options = {}) => { const current = findCard(id); const targetBoard = getBoard(isPinned); if (!targetBoard) throw new NotebookBoardTargetError(`Notebook board "${isPinned ? 'pinned' : 'others'}" was not found.`); const fragment = htmlToCardElement(html, id); const sameBoard = current && current.parentElement === targetBoard; const preservePosition = options.preservePosition !== false; if (sameBoard && preservePosition) { current.replaceWith(fragment); } else { current?.remove(); options.prepend === false ? targetBoard.append(fragment) : targetBoard.prepend(fragment); } refreshSectionVisibility(); refreshEmptyState(); return fragment; };
  const replaceCard = (id, html) => { const current = findCard(id); if (!current) return null; const fragment = htmlToCardElement(html, id); current.replaceWith(fragment); refreshSectionVisibility(); refreshEmptyState(); return fragment; };
  const insertCard = (html, pinned = false) => { const fragment = htmlToCardElement(html); const board = getBoard(pinned); if (!board) throw new NotebookBoardTargetError(`Notebook board "${pinned ? 'pinned' : 'others'}" was not found.`); board.prepend(fragment); refreshSectionVisibility(); refreshEmptyState(); return fragment; };
  const removeCard = (id) => { findCard(id)?.remove(); refreshSectionVisibility(); refreshEmptyState(); };
  return { findCard, getSection, getBoard, replaceCard, insertCard, upsertCard, removeCard, refreshSectionVisibility, refreshEmptyState, htmlToCardElement };
}
