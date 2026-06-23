// SECTION: Notebook board drag-and-drop and keyboard reordering
function directCards(board) {
  return [...board.querySelectorAll(':scope > [data-note-id]')];
}

function serialiseBoard(board) {
  return directCards(board).map((card) => ({
    id: card.dataset.noteId,
    version: card.dataset.version
  }));
}

function restoreOrder(board, ids) {
  const map = new Map(directCards(board).map((card) => [card.dataset.noteId, card]));
  ids.forEach((id) => {
    const card = map.get(id);
    if (card) board.append(card);
  });
}

function findInsertionTarget(board, x, y, draggingCard) {
  const candidates = directCards(board).filter((card) => card !== draggingCard);
  let closest = null;
  let closestDistance = Number.POSITIVE_INFINITY;
  for (const card of candidates) {
    const rect = card.getBoundingClientRect();
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2;
    const distance = Math.hypot(x - centerX, y - centerY);
    if (distance < closestDistance) {
      closest = card;
      closestDistance = distance;
    }
  }
  if (!closest) return { target: null, after: false };
  const rect = closest.getBoundingClientRect();
  const after = y > rect.top + rect.height / 2 || (Math.abs(y - (rect.top + rect.height / 2)) < rect.height * 0.25 && x > rect.left + rect.width / 2);
  return { target: closest, after };
}

export function initNotebookDragOrder(shell, boardController, options = {}) {
  if (!shell || shell.dataset.view !== 'home') return null;
  const api = options.api;
  if (!api?.reorderItems) throw new Error('Notebook reorder API is unavailable.');

  const showError = options.showError || (() => {});
  const showToast = options.showToast || (() => {});
  const liveRegion = shell.querySelector('[data-notebook-reorder-live]');
  const toggle = shell.querySelector('[data-notebook-rearrange-toggle]');
  const done = shell.querySelector('[data-notebook-rearrange-done]');
  let rearrangeMode = false;
  let armedCard = null;
  let draggedCard = null;
  let draggedBoard = null;
  let snapshot = [];
  let keyboardState = null;
  let activeSave = Promise.resolve();
  let pendingSave = null;
  let touchTimer = null;
  let touchState = null;

  const announce = (message) => {
    if (liveRegion) liveRegion.textContent = message;
  };

  const isEnabled = () => shell.dataset.boardView === 'grid' && (rearrangeMode || !window.matchMedia?.('(pointer: coarse)').matches);

  const refreshCards = () => {
    shell.querySelectorAll('[data-notebook-board="pinned"], [data-notebook-board="others"]').forEach((board) => {
      board.dataset.reorderEnabled = String(isEnabled());
      directCards(board).forEach((card) => {
        card.draggable = isEnabled();
        card.querySelector('[data-notebook-drag-handle]')?.toggleAttribute('hidden', !isEnabled());
      });
    });
  };

  const persist = (board, originalIds) => {
    const section = board.dataset.notebookBoard;
    const items = serialiseBoard(board);
    pendingSave = { board, section, items, originalIds };
    activeSave = activeSave.then(async () => {
      const job = pendingSave;
      pendingSave = null;
      if (!job) return;
      try {
        await api.reorderItems(job.section, job.items);
        showToast({ message: 'Note order saved.', tone: 'success', duration: 1800 });
      } catch (error) {
        restoreOrder(job.board, job.originalIds);
        boardController.refreshSectionVisibility();
        showError(error?.message || 'Could not save note order. Previous order restored.');
      }
      if (pendingSave) return persist(pendingSave.board, pendingSave.originalIds);
    });
  };

  const beginKeyboard = (handle, card) => {
    const board = card.parentElement;
    if (!isEnabled() || !board?.matches('[data-notebook-board="pinned"], [data-notebook-board="others"]')) return;
    keyboardState = {
      handle,
      card,
      board,
      originalIds: directCards(board).map((entry) => entry.dataset.noteId)
    };
    card.classList.add('is-keyboard-dragging');
    handle.setAttribute('aria-grabbed', 'true');
    const position = directCards(board).indexOf(card) + 1;
    announce(`Picked up ${card.querySelector('.notebook-card-title')?.textContent || 'note'}, position ${position} of ${directCards(board).length}.`);
  };

  const finishKeyboard = (save) => {
    if (!keyboardState) return;
    const { handle, card, board, originalIds } = keyboardState;
    card.classList.remove('is-keyboard-dragging');
    handle.setAttribute('aria-grabbed', 'false');
    if (save) {
      persist(board, originalIds);
      announce('Note dropped.');
    } else {
      restoreOrder(board, originalIds);
      announce('Rearrangement cancelled.');
    }
    keyboardState = null;
  };

  shell.addEventListener('pointerdown', (event) => {
    const handle = event.target.closest('[data-notebook-drag-handle]');
    if (!handle || !isEnabled()) return;
    armedCard = handle.closest('[data-note-id]');
    if (event.pointerType === 'mouse') return;
    const card = armedCard;
    const board = card?.parentElement;
    if (!card || !board?.matches('[data-notebook-board="pinned"], [data-notebook-board="others"]')) return;
    const startX = event.clientX;
    const startY = event.clientY;
    touchTimer = window.setTimeout(() => {
      touchState = {
        pointerId: event.pointerId,
        card,
        board,
        originalIds: directCards(board).map((entry) => entry.dataset.noteId)
      };
      card.classList.add('is-dragging');
      handle.setPointerCapture?.(event.pointerId);
      announce(`Picked up ${card.querySelector('.notebook-card-title')?.textContent || 'note'}.`);
    }, 300);
    const cancelLongPress = (moveEvent) => {
      if (Math.hypot(moveEvent.clientX - startX, moveEvent.clientY - startY) > 8 && !touchState) {
        window.clearTimeout(touchTimer);
        touchTimer = null;
      }
    };
    handle.addEventListener('pointermove', cancelLongPress, { once: true });
  });

  shell.addEventListener('pointermove', (event) => {
    if (!touchState || event.pointerId !== touchState.pointerId) return;
    event.preventDefault();
    const targetElement = document.elementFromPoint(event.clientX, event.clientY);
    const targetBoard = targetElement?.closest?.('[data-notebook-board]');
    if (targetBoard !== touchState.board) return;
    const { target, after } = findInsertionTarget(targetBoard, event.clientX, event.clientY, touchState.card);
    if (!target) targetBoard.append(touchState.card);
    else if (after) target.after(touchState.card);
    else target.before(touchState.card);
  }, { passive: false });

  const finishTouch = (event, save) => {
    if (touchTimer) window.clearTimeout(touchTimer);
    touchTimer = null;
    if (!touchState || event.pointerId !== touchState.pointerId) return;
    const { card, board, originalIds } = touchState;
    card.classList.remove('is-dragging');
    if (save) persist(board, originalIds); else restoreOrder(board, originalIds);
    touchState = null;
  };

  shell.addEventListener('pointerup', (event) => finishTouch(event, true));
  shell.addEventListener('pointercancel', (event) => finishTouch(event, false));

  shell.addEventListener('dragstart', (event) => {
    const card = event.target.closest('[data-note-id]');
    if (!card || card !== armedCard || !isEnabled()) {
      event.preventDefault();
      return;
    }
    const parent = card.parentElement;
    if (!parent?.matches('[data-notebook-board="pinned"], [data-notebook-board="others"]')) {
      event.preventDefault();
      return;
    }
    draggedCard = card;
    draggedBoard = parent;
    snapshot = directCards(parent).map((entry) => entry.dataset.noteId);
    card.classList.add('is-dragging');
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', card.dataset.noteId);
  });

  shell.addEventListener('dragover', (event) => {
    if (!draggedCard || !draggedBoard) return;
    const board = event.target.closest('[data-notebook-board]');
    if (board !== draggedBoard) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    const { target, after } = findInsertionTarget(board, event.clientX, event.clientY, draggedCard);
    if (!target) board.append(draggedCard);
    else if (after) target.after(draggedCard);
    else target.before(draggedCard);
  });

  shell.addEventListener('drop', (event) => {
    if (!draggedCard || event.target.closest('[data-notebook-board]') !== draggedBoard) return;
    event.preventDefault();
    persist(draggedBoard, snapshot);
  });

  shell.addEventListener('dragend', () => {
    draggedCard?.classList.remove('is-dragging');
    draggedCard = null;
    draggedBoard = null;
    snapshot = [];
    armedCard = null;
  });

  shell.addEventListener('keydown', (event) => {
    const handle = event.target.closest('[data-notebook-drag-handle]');
    if (!handle) return;
    const card = handle.closest('[data-note-id]');
    if (!keyboardState && (event.key === ' ' || event.key === 'Enter')) {
      event.preventDefault();
      beginKeyboard(handle, card);
      return;
    }
    if (!keyboardState || keyboardState.handle !== handle) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      finishKeyboard(false);
      return;
    }
    if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault();
      finishKeyboard(true);
      return;
    }
    if (!['ArrowLeft', 'ArrowUp', 'ArrowRight', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    const cards = directCards(keyboardState.board);
    const index = cards.indexOf(card);
    const delta = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
    const nextIndex = Math.max(0, Math.min(cards.length - 1, index + delta));
    if (nextIndex === index) return;
    const target = cards[nextIndex];
    if (delta < 0) target.before(card); else target.after(card);
    announce(`Moved to position ${directCards(keyboardState.board).indexOf(card) + 1} of ${cards.length}.`);
  });

  toggle?.addEventListener('click', () => {
    rearrangeMode = true;
    shell.classList.add('is-rearranging');
    toggle.hidden = true;
    if (done) done.hidden = false;
    refreshCards();
  });

  done?.addEventListener('click', () => {
    rearrangeMode = false;
    shell.classList.remove('is-rearranging');
    done.hidden = true;
    if (toggle) toggle.hidden = false;
    refreshCards();
  });

  document.addEventListener('notebook:board-view-changed', refreshCards);
  const observer = new MutationObserver(refreshCards);
  shell.querySelectorAll('[data-notebook-board="pinned"], [data-notebook-board="others"]').forEach((board) => observer.observe(board, { childList: true }));
  refreshCards();

  return {
    refresh: refreshCards,
    destroy() {
      observer.disconnect();
      document.removeEventListener('notebook:board-view-changed', refreshCards);
    }
  };
}

export const notebookDragOrderTestHelpers = { directCards, serialiseBoard, restoreOrder, findInsertionTarget };
