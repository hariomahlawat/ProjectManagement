// SECTION: DOM-order-safe adaptive masonry for Notebook cards
const BOARD_SELECTOR = '[data-notebook-board]';
const ITEM_SELECTOR = ':scope > [data-note-id], :scope > .notebook-card-placeholder';
const DEFAULT_ROW_HEIGHT = 8;
const DEFAULT_GAP = 12;

function directItems(board) {
  return [...board.querySelectorAll(ITEM_SELECTOR)];
}

function numericStyle(value, fallback) {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

export function calculateMasonrySpan(height, rowHeight = DEFAULT_ROW_HEIGHT, gap = DEFAULT_GAP) {
  if (!Number.isFinite(height) || height <= 0) return 1;
  return Math.max(1, Math.ceil((height + gap) / (rowHeight + gap)));
}

export function layoutMasonryBoard(board, shell = board?.closest?.('.notebook-shell')) {
  if (!board) return;
  const isGrid = shell?.dataset?.boardView !== 'list';
  const useMasonry = isGrid && board.dataset.layout === 'masonry';
  const items = directItems(board);

  if (!useMasonry) {
    items.forEach((item) => item.style.removeProperty('grid-row-end'));
    board.classList.remove('is-masonry-ready');
    return;
  }

  const style = getComputedStyle(board);
  const rowHeight = numericStyle(style.gridAutoRows, DEFAULT_ROW_HEIGHT);
  const rowGap = numericStyle(style.rowGap, DEFAULT_GAP);

  items.forEach((item) => {
    // Temporarily remove the old span so intrinsic content height is measured.
    item.style.removeProperty('grid-row-end');
    const height = item.getBoundingClientRect().height;
    item.style.gridRowEnd = `span ${calculateMasonrySpan(height, rowHeight, rowGap)}`;
  });
  board.classList.add('is-masonry-ready');
}

export function initNotebookMasonryGrid(shell, options = {}) {
  if (!shell) return null;
  const windowRef = options.windowRef || window;
  const boards = () => [...shell.querySelectorAll(BOARD_SELECTOR)];
  let frame = 0;
  let cardObserver = null;

  const run = () => {
    frame = 0;
    boards().forEach((board) => layoutMasonryBoard(board, shell));
  };

  const schedule = () => {
    if (frame) return;
    frame = windowRef.requestAnimationFrame(run);
  };

  const observeCards = () => {
    if (!cardObserver || shell.dataset.boardView === 'list') return;
    cardObserver.disconnect();
    boards().forEach((board) => directItems(board).forEach((item) => cardObserver.observe(item)));
  };

  if ('ResizeObserver' in windowRef) {
    cardObserver = new windowRef.ResizeObserver(schedule);
  }

  const boardObserver = new MutationObserver(() => {
    observeCards();
    schedule();
  });
  boards().forEach((board) => boardObserver.observe(board, { childList: true, subtree: true }));

  const onImageLoad = (event) => {
    if (event.target instanceof HTMLImageElement && event.target.closest(BOARD_SELECTOR)) schedule();
  };
  const onBoardView = () => {
    observeCards();
    schedule();
  };
  const onExplicitRefresh = () => schedule();

  shell.addEventListener('load', onImageLoad, true);
  shell.addEventListener('notebook:masonry-refresh', onExplicitRefresh);
  document.addEventListener('notebook:board-view-changed', onBoardView);
  windowRef.addEventListener('resize', schedule, { passive: true });

  observeCards();
  schedule();

  return {
    refresh: schedule,
    destroy() {
      if (frame) windowRef.cancelAnimationFrame(frame);
      cardObserver?.disconnect();
      boardObserver.disconnect();
      shell.removeEventListener('load', onImageLoad, true);
      shell.removeEventListener('notebook:masonry-refresh', onExplicitRefresh);
      document.removeEventListener('notebook:board-view-changed', onBoardView);
      windowRef.removeEventListener('resize', schedule);
    }
  };
}

export const notebookMasonryTestHelpers = { directItems, numericStyle };
