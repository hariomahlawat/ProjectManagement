/**
 * Enables the application-wide Ctrl/Cmd + K shortcut for PRISM search.
 * The handler is intentionally scoped to the shared shell search input and
 * does not submit automatically; users remain in control of the query.
 */
export function initGlobalSearchShortcut() {
  const form = document.querySelector('[data-global-search]');
  const input = form?.querySelector('input[name="q"]');

  if (!(input instanceof HTMLInputElement)) {
    return;
  }

  document.addEventListener('keydown', (event) => {
    const key = event.key.toLowerCase();
    const isSearchShortcut = (event.ctrlKey || event.metaKey) && key === 'k';

    if (isSearchShortcut) {
      event.preventDefault();
      input.focus({ preventScroll: true });
      input.select();
      return;
    }

    if (event.key === 'Escape' && document.activeElement === input) {
      input.blur();
    }
  });
}
