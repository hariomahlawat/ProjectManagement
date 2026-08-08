// SECTION: Notebook shared DOM helpers
export const qs = (root, selector) => root ? root.querySelector(selector) : null;
export const qsa = (root, selector) => Array.from(root ? root.querySelectorAll(selector) : []);
export const closestAction = (event) => event.target.closest('[data-action]');

// SECTION: Passive card-opening contract
const CARD_OPEN_INTERACTIVE_SELECTOR = [
  'a',
  'button',
  'input',
  'textarea',
  'select',
  'summary',
  'details',
  '[role="button"]',
  '[contenteditable="true"]',
  '[data-notebook-drag-handle]',
  '.notebook-card-actions'
].join(', ');

/**
 * Returns the note card that should open for a passive content click.
 * Interactive descendants keep their own action, while ordinary card content
 * (including checklist item text) follows the same open-card behaviour as notes.
 */
export function getPassiveNotebookCardOpenTarget(target, shell) {
  if (!target?.closest || !shell?.contains) return null;

  const card = target.closest('[data-note-id]');
  if (!card || !shell.contains(card)) return null;
  if (shell.classList.contains('is-rearranging') || shell.classList.contains('is-pointer-dragging')) return null;
  if (target.closest(CARD_OPEN_INTERACTIVE_SELECTOR)) return null;
  if (!card.querySelector('[data-action="open-note"]')) return null;

  return card;
}
