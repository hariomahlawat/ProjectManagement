// SECTION: Notebook board drag-and-drop and keyboard reordering
const BOARD_SELECTOR = '[data-notebook-board="pinned"], [data-notebook-board="others"]';
const OWNED_CARD_SELECTOR = ':scope > [data-note-id][data-reorderable="true"]';
const SYSTEM_CARD_SELECTOR = ':scope > [data-notebook-system-home-card][data-reorderable="true"]';
const CARD_SELECTOR = `${OWNED_CARD_SELECTOR}, ${SYSTEM_CARD_SELECTOR}`;
const ALL_CARD_SELECTOR = ':scope > [data-note-id], :scope > [data-notebook-system-home-card]';
const DRAG_THRESHOLD_PX = 6;
const TOUCH_LONG_PRESS_MS = 300;
const TOUCH_CANCEL_DISTANCE_PX = 8;
const INSERTION_HYSTERESIS_PX = 10;
const EDGE_SCROLL_ZONE_PX = 72;
const MAX_EDGE_SCROLL_PX = 18;
const FLIP_DURATION_MS = 150;

function directCards(board) {
  return [...board.querySelectorAll(CARD_SELECTOR)];
}

function allCards(board) {
  return [...board.querySelectorAll(ALL_CARD_SELECTOR)];
}

function ownedCards(board) {
  return [...board.querySelectorAll(OWNED_CARD_SELECTOR)];
}

function cardKey(card) {
  if (card?.dataset?.noteId) return `note:${card.dataset.noteId}`;
  if (card?.dataset?.notebookSystemHomeCard) return `system:${card.dataset.notebookSystemHomeCard}`;
  return '';
}

function serialiseBoard(board) {
  return ownedCards(board).map((card) => ({
    id: card.dataset.noteId,
    version: card.dataset.version
  }));
}

function restoreOrder(board, keys) {
  const map = new Map(allCards(board).map((card) => [cardKey(card), card]));
  keys.forEach((key) => {
    const card = map.get(key);
    if (card) board.append(card);
  });
}

function isInteractiveDragTarget(target) {
  if (!(target instanceof Element)) return true;

  // The keyboard handle itself is a deliberate drag surface.
  if (target.closest('[data-notebook-drag-handle]')) return false;

  // Card metadata and action rails always retain their own interaction semantics.
  if (target.closest('.notebook-card-actions, .notebook-card-tags, [data-no-card-drag]')) return true;

  // Most form/control descendants block card dragging. The one exception is the
  // system-note full-card open button, which is explicitly marked as a passive
  // surface: click opens it; click-and-move rearranges it.
  const interactive = target.closest('button, input, textarea, select, option, summary, details, [contenteditable="true"], [role="button"]');
  if (interactive && !interactive.matches('[data-card-passive-open]')) return true;

  // Normal note open links are passive surfaces too. Any other link is an action.
  const link = target.closest('a');
  return Boolean(link && !link.classList.contains('notebook-card__open-area'));
}

function captureRects(elements) {
  return new Map(elements.map((element) => [element, element.getBoundingClientRect()]));
}

function playFlip(elements, beforeRects, windowRef = window) {
  if (windowRef.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
  elements.forEach((element) => {
    const before = beforeRects.get(element);
    if (!before) return;
    const after = element.getBoundingClientRect();
    const dx = before.left - after.left;
    const dy = before.top - after.top;
    if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5) return;
    element.animate?.([
      { transform: `translate3d(${dx}px, ${dy}px, 0)` },
      { transform: 'translate3d(0, 0, 0)' }
    ], {
      duration: FLIP_DURATION_MS,
      easing: 'cubic-bezier(.2, 0, 0, 1)'
    });
  });
}

function groupVisualRows(cards) {
  const entries = cards.map((card) => ({ card, rect: card.getBoundingClientRect() }))
    .sort((a, b) => Math.abs(a.rect.top - b.rect.top) > 8 ? a.rect.top - b.rect.top : a.rect.left - b.rect.left);
  const rows = [];
  for (const entry of entries) {
    const row = rows.find((candidate) => Math.abs(candidate.top - entry.rect.top) <= 12);
    if (row) {
      row.items.push(entry);
      row.bottom = Math.max(row.bottom, entry.rect.bottom);
    } else {
      rows.push({ top: entry.rect.top, bottom: entry.rect.bottom, items: [entry] });
    }
  }
  rows.forEach((row) => row.items.sort((a, b) => a.rect.left - b.rect.left));
  rows.sort((a, b) => a.top - b.top);
  return rows;
}

function calculateInsertionIndex(board, x, y) {
  const cards = directCards(board);
  if (cards.length === 0) return 0;
  const rows = groupVisualRows(cards);
  let selectedRow = rows[rows.length - 1];
  for (let index = 0; index < rows.length; index += 1) {
    const row = rows[index];
    const previous = rows[index - 1];
    const next = rows[index + 1];
    const upper = previous ? (previous.bottom + row.top) / 2 : Number.NEGATIVE_INFINITY;
    const lower = next ? (row.bottom + next.top) / 2 : Number.POSITIVE_INFINITY;
    if (y >= upper && y < lower) {
      selectedRow = row;
      break;
    }
  }

  const rowStart = rows.slice(0, rows.indexOf(selectedRow)).reduce((sum, row) => sum + row.items.length, 0);
  for (let index = 0; index < selectedRow.items.length; index += 1) {
    const { rect } = selectedRow.items[index];
    if (x < rect.left + rect.width / 2) return rowStart + index;
  }
  return rowStart + selectedRow.items.length;
}

function findInsertionTarget(board, x, y) {
  const cards = directCards(board);
  const index = calculateInsertionIndex(board, x, y);
  return { target: cards[index] || null, after: false, index };
}

function movePlaceholder(board, placeholder, desiredIndex, lastMove, pointer) {
  const cards = directCards(board);
  const currentChildren = [...board.children].filter((child) => child === placeholder || child.matches?.(CARD_SELECTOR));
  const currentIndex = currentChildren.indexOf(placeholder);
  const normalizedIndex = Math.max(0, Math.min(cards.length, desiredIndex));
  if (normalizedIndex === currentIndex) return false;

  if (lastMove && Math.abs(normalizedIndex - currentIndex) === 1) {
    const boundaryCard = normalizedIndex > currentIndex ? cards[Math.min(normalizedIndex, cards.length - 1)] : cards[Math.max(0, normalizedIndex)];
    const rect = boundaryCard?.getBoundingClientRect();
    if (rect) {
      const boundary = rect.left + rect.width / 2;
      if (normalizedIndex > currentIndex && pointer.x < boundary + INSERTION_HYSTERESIS_PX) return false;
      if (normalizedIndex < currentIndex && pointer.x > boundary - INSERTION_HYSTERESIS_PX) return false;
    }
  }

  const animatedCards = directCards(board);
  const before = captureRects(animatedCards);
  const target = cards[normalizedIndex] || null;
  if (target) board.insertBefore(placeholder, target); else board.append(placeholder);
  board.dispatchEvent(new CustomEvent('notebook:masonry-refresh', { bubbles: true }));
  playFlip(animatedCards, before);
  return true;
}

function createPlaceholder(card) {
  const rect = card.getBoundingClientRect();
  const placeholder = document.createElement('div');
  placeholder.className = 'notebook-card-placeholder';
  placeholder.style.width = `${rect.width}px`;
  placeholder.style.height = `${rect.height}px`;
  placeholder.setAttribute('aria-hidden', 'true');
  return placeholder;
}

function createPreview(card, rect) {
  const preview = card.cloneNode(true);
  preview.removeAttribute('data-note-id');
  preview.removeAttribute('data-notebook-card-id');
  preview.removeAttribute('data-notebook-system-home-card');
  preview.classList.remove('has-open-menu', 'has-open-popover');
  preview.classList.add('is-drag-preview');
  preview.setAttribute('aria-hidden', 'true');
  preview.querySelectorAll('[id]').forEach((node) => node.removeAttribute('id'));
  preview.querySelectorAll('details[open]').forEach((node) => node.removeAttribute('open'));
  preview.querySelectorAll('.notebook-card-actions, [data-notebook-drag-handle]').forEach((node) => node.remove());
  Object.assign(preview.style, {
    position: 'fixed',
    left: '0',
    top: '0',
    width: `${rect.width}px`,
    height: `${rect.height}px`,
    margin: '0',
    zIndex: '2147483000',
    pointerEvents: 'none'
  });
  document.body.append(preview);
  return preview;
}

export function initNotebookDragOrder(shell, boardController, options = {}) {
  if (!shell || shell.dataset.view !== 'home') return null;
  const api = options.api;
  if (!api?.reorderItems || !api?.setSystemItemPlacement) throw new Error('Notebook reorder API is unavailable.');

  const showError = options.showError || (() => {});
  const liveRegion = shell.querySelector('[data-notebook-reorder-live]');
  let pointerState = null;
  let dragState = null;
  let keyboardState = null;
  let activeSave = Promise.resolve();
  let pendingSave = null;
  let framePending = false;
  let suppressClickUntil = 0;

  const announce = (message) => {
    if (liveRegion) liveRegion.textContent = message;
  };

  // Rearrangement is a native board interaction in grid view. There is no separate
  // mode: mouse movement starts a drag; touch uses a deliberate long-press.
  const isEnabled = () => shell.dataset.boardView === 'grid';

  const refreshCards = () => {
    shell.querySelectorAll(BOARD_SELECTOR).forEach((board) => {
      board.dataset.reorderEnabled = String(isEnabled());
      directCards(board).forEach((card) => {
        card.draggable = false;
        card.querySelectorAll('a, img').forEach((node) => { node.draggable = false; });
        card.querySelector('[data-notebook-drag-handle]')?.toggleAttribute('hidden', !isEnabled());
      });
    });
  };

  const persist = (board, originalKeys) => {
    const section = board.dataset.notebookBoard;
    const items = serialiseBoard(board);
    pendingSave = { board, section, items, originalKeys };
    activeSave = activeSave.then(async () => {
      const job = pendingSave;
      pendingSave = null;
      if (!job) return;
      try {
        // Normal Notebook items keep their authoritative sparse order contract. The
        // system note stores only its visual slot among those owned cards.
        await api.reorderItems(job.section, job.items);
        const mixedCards = directCards(job.board);
        const systemCard = mixedCards.find((card) => card.dataset.notebookSystemHomeCard);
        if (systemCard) {
          const position = mixedCards.indexOf(systemCard);
          const key = systemCard.dataset.notebookSystemHomeCard;
          const response = await api.setSystemItemPlacement(key, job.section === 'pinned', position);
          const preference = response?.preference;
          if (preference) {
            systemCard.dataset.systemIsPinned = String(Boolean(preference.isPinned));
            systemCard.dataset.systemHomePosition = String(Number(preference.homePosition || 0));
            systemCard.dataset.systemPreferenceVersion = preference.version || '';
          }
        }
      } catch (error) {
        restoreOrder(job.board, job.originalKeys);
        boardController.refreshSectionVisibility();
        showError(error?.message || 'Could not save note order. Previous order restored.');
      }
      if (pendingSave) return persist(pendingSave.board, pendingSave.originalKeys);
    });
  };

  const updatePreview = () => {
    if (!dragState) return;
    const { preview, pointer, offsetX, offsetY } = dragState;
    preview.style.transform = `translate3d(${pointer.x - offsetX}px, ${pointer.y - offsetY}px, 0) rotate(.35deg) scale(1.018)`;
  };

  const autoScroll = () => {
    if (!dragState) return;
    const y = dragState.pointer.y;
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
    let delta = 0;
    if (y < EDGE_SCROLL_ZONE_PX) delta = -MAX_EDGE_SCROLL_PX * (1 - y / EDGE_SCROLL_ZONE_PX);
    else if (y > viewportHeight - EDGE_SCROLL_ZONE_PX) delta = MAX_EDGE_SCROLL_PX * (1 - (viewportHeight - y) / EDGE_SCROLL_ZONE_PX);
    if (Math.abs(delta) > 0.5) window.scrollBy(0, delta);
  };

  const processPointerFrame = () => {
    framePending = false;
    if (!dragState) return;
    updatePreview();
    autoScroll();
    const targetElement = document.elementFromPoint(dragState.pointer.x, dragState.pointer.y);
    const targetBoard = targetElement?.closest?.('[data-notebook-board]');
    if (targetBoard !== dragState.board) return;
    const desiredIndex = calculateInsertionIndex(targetBoard, dragState.pointer.x, dragState.pointer.y);
    const moved = movePlaceholder(targetBoard, dragState.placeholder, desiredIndex, dragState.lastMove, dragState.pointer);
    if (moved) dragState.lastMove = { index: desiredIndex, x: dragState.pointer.x, y: dragState.pointer.y };
  };

  const scheduleFrame = () => {
    if (framePending) return;
    framePending = true;
    requestAnimationFrame(processPointerFrame);
  };

  const beginPointerDrag = (state) => {
    const { card, board, clientX, clientY, pointerId } = state;

    // Floating menus/palettes are owned by the normal card UI. Close them while the
    // card is still attached to the Notebook before creating the drag preview.
    shell.dispatchEvent(new CustomEvent('notebook:drag-start', { bubbles: true, detail: { key: cardKey(card) } }));

    const rect = card.getBoundingClientRect();
    const placeholder = createPlaceholder(card);
    board.replaceChild(placeholder, card);
    const preview = createPreview(card, rect);
    card.classList.add('is-drag-source');
    shell.classList.add('is-pointer-dragging');
    shell.setPointerCapture?.(pointerId);
    dragState = {
      card,
      board,
      placeholder,
      preview,
      pointerId,
      pointer: { x: clientX, y: clientY },
      offsetX: clientX - rect.left,
      offsetY: clientY - rect.top,
      originalKeys: state.originalKeys,
      lastMove: null
    };
    document.body.classList.add('notebook-is-dragging');
    announce(`Picked up ${card.querySelector('.notebook-card-title')?.textContent || 'note'}.`);
    updatePreview();
  };

  const cancelPointerArm = () => {
    if (pointerState?.timer) window.clearTimeout(pointerState.timer);
    pointerState = null;
  };

  const finishPointerDrag = (save) => {
    if (!dragState) {
      cancelPointerArm();
      return;
    }
    const { card, board, placeholder, preview, originalKeys, pointerId } = dragState;
    preview.remove();
    placeholder.replaceWith(card);
    board.dispatchEvent(new CustomEvent('notebook:masonry-refresh', { bubbles: true }));
    card.classList.remove('is-drag-source');
    shell.classList.remove('is-pointer-dragging');
    document.body.classList.remove('notebook-is-dragging');
    shell.releasePointerCapture?.(pointerId);
    if (save) {
      persist(board, originalKeys);
      suppressClickUntil = performance.now() + 300;
      announce(`Dropped at position ${directCards(board).indexOf(card) + 1} of ${directCards(board).length}.`);
    } else {
      restoreOrder(board, originalKeys);
      board.dispatchEvent(new CustomEvent('notebook:masonry-refresh', { bubbles: true }));
      announce('Rearrangement cancelled.');
    }
    dragState = null;
    pointerState = null;
    shell.dispatchEvent(new CustomEvent('notebook:drag-end', { bubbles: true, detail: { saved: Boolean(save) } }));
    boardController.refreshSectionVisibility();
  };

  const beginKeyboard = (handle, card) => {
    const board = card.parentElement;
    if (!isEnabled() || !board?.matches(BOARD_SELECTOR)) return;
    keyboardState = { handle, card, board, originalKeys: allCards(board).map(cardKey) };
    card.classList.add('is-keyboard-dragging');
    handle.setAttribute('aria-grabbed', 'true');
    announce(`Picked up ${card.querySelector('.notebook-card-title')?.textContent || 'note'}, position ${directCards(board).indexOf(card) + 1} of ${directCards(board).length}.`);
  };

  const finishKeyboard = (save) => {
    if (!keyboardState) return;
    const { handle, card, board, originalKeys } = keyboardState;
    card.classList.remove('is-keyboard-dragging');
    handle.setAttribute('aria-grabbed', 'false');
    if (save) {
      persist(board, originalKeys);
      announce('Note dropped.');
    } else {
      restoreOrder(board, originalKeys);
      announce('Rearrangement cancelled.');
    }
    keyboardState = null;
  };

  const onPointerDown = (event) => {
    if (!isEnabled() || event.button !== 0 || pointerState || dragState) return;
    const card = event.target.closest('[data-note-id][data-reorderable="true"], [data-notebook-system-home-card][data-reorderable="true"]');
    const board = card?.parentElement;
    if (!card || !board?.matches(BOARD_SELECTOR) || isInteractiveDragTarget(event.target)) return;

    const state = {
      card,
      board,
      pointerId: event.pointerId,
      pointerType: event.pointerType,
      startX: event.clientX,
      startY: event.clientY,
      clientX: event.clientX,
      clientY: event.clientY,
      originalKeys: allCards(board).map(cardKey),
      timer: null
    };
    pointerState = state;

    if (event.pointerType !== 'mouse') {
      state.timer = window.setTimeout(() => {
        if (pointerState === state) beginPointerDrag(state);
      }, TOUCH_LONG_PRESS_MS);
    }
  };

  const onPointerMove = (event) => {
    if (dragState && event.pointerId === dragState.pointerId) {
      event.preventDefault();
      dragState.pointer = { x: event.clientX, y: event.clientY };
      scheduleFrame();
      return;
    }
    if (!pointerState || event.pointerId !== pointerState.pointerId) return;
    pointerState.clientX = event.clientX;
    pointerState.clientY = event.clientY;
    const distance = Math.hypot(event.clientX - pointerState.startX, event.clientY - pointerState.startY);
    if (pointerState.pointerType === 'mouse' && distance >= DRAG_THRESHOLD_PX) {
      event.preventDefault();
      beginPointerDrag(pointerState);
      return;
    }
    if (pointerState.pointerType !== 'mouse' && distance > TOUCH_CANCEL_DISTANCE_PX && !dragState) cancelPointerArm();
  };

  const onPointerUp = (event) => {
    if (dragState && event.pointerId === dragState.pointerId) finishPointerDrag(true);
    else if (pointerState && event.pointerId === pointerState.pointerId) cancelPointerArm();
  };

  const onPointerCancel = (event) => {
    if (dragState && event.pointerId === dragState.pointerId) finishPointerDrag(false);
    else if (pointerState && event.pointerId === pointerState.pointerId) cancelPointerArm();
  };

  const onClickCapture = (event) => {
    if (performance.now() >= suppressClickUntil || !event.target.closest('[data-note-id], [data-notebook-system-home-card]')) return;
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  const onKeyDown = (event) => {
    const handle = event.target.closest('[data-notebook-drag-handle]');
    if (!handle) return;
    const card = handle.closest('[data-note-id][data-reorderable="true"], [data-notebook-system-home-card][data-reorderable="true"]');
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
    const before = captureRects(cards);
    const target = cards[nextIndex];
    if (delta < 0) target.before(card); else target.after(card);
    keyboardState.board.dispatchEvent(new CustomEvent('notebook:masonry-refresh', { bubbles: true }));
    playFlip(cards, before);
    announce(`Moved to position ${directCards(keyboardState.board).indexOf(card) + 1} of ${cards.length}.`);
  };

  shell.addEventListener('pointerdown', onPointerDown);
  shell.addEventListener('pointermove', onPointerMove, { passive: false });
  shell.addEventListener('pointerup', onPointerUp);
  shell.addEventListener('pointercancel', onPointerCancel);
  shell.addEventListener('click', onClickCapture, true);
  shell.addEventListener('keydown', onKeyDown);
  document.addEventListener('notebook:board-view-changed', refreshCards);

  const observer = new MutationObserver(refreshCards);
  shell.querySelectorAll(BOARD_SELECTOR).forEach((board) => observer.observe(board, { childList: true }));
  refreshCards();

  return {
    refresh: refreshCards,
    destroy() {
      finishPointerDrag(false);
      observer.disconnect();
      shell.removeEventListener('pointerdown', onPointerDown);
      shell.removeEventListener('pointermove', onPointerMove);
      shell.removeEventListener('pointerup', onPointerUp);
      shell.removeEventListener('pointercancel', onPointerCancel);
      shell.removeEventListener('click', onClickCapture, true);
      shell.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('notebook:board-view-changed', refreshCards);
    }
  };
}

export const notebookDragOrderTestHelpers = {
  directCards,
  ownedCards,
  cardKey,
  serialiseBoard,
  restoreOrder,
  findInsertionTarget,
  calculateInsertionIndex,
  isInteractiveDragTarget,
  groupVisualRows
};
