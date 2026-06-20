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

// SECTION: Reconciliation diagnostics
function logReconciliationFailure(item, stage, error) {
  console.error('Notebook card reconciliation failed', { itemId: item?.id, stage, error });
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
  command = 'unknown',
  renderFailureMessage = 'The note was updated, but its card could not be rendered. Reload the page.',
  reconcileFailureMessage = 'The note was updated, but the board could not refresh. Reload the page.'
}) {
  const item = requireMutationItem(response);
  applyCounts?.(response.counts);
  updateCardConcurrencyState(existingCard || board?.findCard?.(item.id), item);

  // SECTION: Server-rendered card acquisition
  let html = response.cardHtml;
  if (!html) {
    console.warn('Notebook mutation response did not contain card HTML.', { itemId: item.id, command });
    try {
      html = await getCardHtml(item.id, view);
    } catch (error) {
      logReconciliationFailure(item, 'server-card-rendering', error);
      showGlobalError?.(renderFailureMessage);
      return { item, reconciled: false, code: 'notebook_card_render_failed' };
    }
  }

  if (typeof html !== 'string' || !html.trim()) {
    const error = new Error('Notebook card response was empty.');
    logReconciliationFailure(item, 'empty-card-response', error);
    showGlobalError?.(renderFailureMessage);
    return { item, reconciled: false, code: 'notebook_empty_card_response' };
  }

  // SECTION: Board reconciliation
  try {
    board.upsertCard(item.id, html, item.isPinned, { preservePosition, prepend });
    return { item, reconciled: true };
  } catch (error) {
    const stage = String(error?.message || '').includes('board')
      ? 'target-board'
      : String(error?.message || '').includes('card response') || String(error?.message || '').includes('card HTML')
        ? 'invalid-card-html'
        : 'card-replacement';
    logReconciliationFailure(item, stage, error);
    showGlobalError?.(stage === 'invalid-card-html' ? renderFailureMessage : reconcileFailureMessage);
    updateCardConcurrencyState(existingCard || board?.findCard?.(item.id), item);
    return { item, reconciled: false, code: stage === 'invalid-card-html' ? 'notebook_invalid_card_html' : 'notebook_board_reconcile_failed' };
  }
}
