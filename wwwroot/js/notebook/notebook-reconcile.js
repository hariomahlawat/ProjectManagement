import { NotebookApiError } from './notebook-api.js';

// SECTION: Notebook mutation response validation
export function requireMutationItem(response, message = 'The notebook response did not contain an updated item.') {
  if (!response?.item) {
    throw new NotebookApiError(message, { code: 'notebook_invalid_mutation_response' });
  }
  return response.item;
}

// SECTION: Local stale-card concurrency repair
export function updateCardConcurrencyState(card, item) {
  if (!card || !item) return;
  card.dataset.version = item.version;
  card.dataset.isPinned = String(item.isPinned).toLowerCase();
  card.dataset.status = item.status;
}

// SECTION: Shared post-mutation board reconciliation
export async function reconcileMutation({
  response,
  board,
  view = 'home',
  getCardHtml,
  applyCounts,
  preservePosition = true,
  prepend = false,
  showGlobalError,
  existingCard = null,
  renderFailureMessage = 'The note was updated, but its card could not be rendered. Reload the page.',
  reconcileFailureMessage = 'The note was updated, but the board could not refresh. Reload the page.'
}) {
  const item = requireMutationItem(response);
  applyCounts?.(response.counts);
  updateCardConcurrencyState(existingCard || board?.findCard?.(item.id), item);

  let html = response.cardHtml;
  if (!html) {
    try {
      html = await getCardHtml(item.id, view);
    } catch (error) {
      showGlobalError?.(renderFailureMessage);
      return { item, reconciled: false, code: 'notebook_card_render_failed' };
    }
  }

  try {
    board.upsertCard(item.id, html, item.isPinned, { preservePosition, prepend });
    return { item, reconciled: true };
  } catch (error) {
    showGlobalError?.(reconcileFailureMessage);
    updateCardConcurrencyState(existingCard || board?.findCard?.(item.id), item);
    return { item, reconciled: false, code: 'notebook_board_reconcile_failed' };
  }
}
