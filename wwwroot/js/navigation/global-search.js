import { createSearchCombobox, initSearchShortcuts } from './search-combobox.js';

/**
 * Application-wide PRISM search: the top-bar search box and the Ctrl/⌘+K and "/"
 * shortcuts. Suggestions are lightweight and fetched only after two characters; full
 * document/OCR ranking stays on the Search results page.
 */
export function initGlobalSearchShortcut() {
  const input = document.querySelector('[data-global-search] input[data-search-combobox]');
  if (input instanceof HTMLInputElement) {
    createSearchCombobox(input, { blurOnEscape: true });
  }

  initSearchShortcuts(document);
}
