// SECTION: Notebook board DOM updates
export function createNotebookBoard(root = document) {
  const findCard = (id) => root.querySelector(`[data-note-id="${CSS.escape(id)}"]`);
  const boardFor = (pinned) => root.querySelector(pinned ? '[data-board-section="pinned"] [data-notebook-board]' : '[data-board-section="others"] [data-notebook-board]') || root.querySelector('[data-notebook-board]');
  const replaceCard = (id, html) => { const card = findCard(id); if (card) card.outerHTML = html; };
  const insertCard = (html, pinned = false) => { const board = boardFor(pinned); if (board) board.insertAdjacentHTML('afterbegin', html); };
  const removeCard = (id) => findCard(id)?.remove();
  return { findCard, replaceCard, insertCard, removeCard };
}
