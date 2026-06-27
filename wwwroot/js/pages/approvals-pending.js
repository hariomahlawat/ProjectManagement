const INITIALISED_ATTRIBUTE = 'decisionFilterInitialised';
const SEARCH_DELAY_MS = 450;
const MIN_SEARCH_LENGTH = 2;

/**
 * Initialises the pending-approvals filter controls.
 *
 * The function is deliberately idempotent because shared page bootstrapping can
 * run after partial-page updates as well as on the initial document load.
 *
 * @param {ParentNode} root DOM scope to inspect. Defaults to the full document.
 */
export function initPendingApprovalsRows(root = document) {
  if (!root || typeof root.querySelector !== 'function') {
    return;
  }

  const form = root.querySelector('[data-decision-filter-form]');
  if (!(form instanceof HTMLFormElement)) {
    return;
  }

  if (form.dataset[INITIALISED_ATTRIBUTE] === 'true') {
    return;
  }

  form.dataset[INITIALISED_ATTRIBUTE] = 'true';

  let searchTimer = null;
  const search = form.querySelector('input[type="search"]');

  const resetPageNumber = () => {
    const pageField = form.querySelector('[name="PageNumber"]');
    if (pageField instanceof HTMLInputElement) {
      pageField.value = '1';
    }
  };

  const submitFilters = () => {
    resetPageNumber();
    form.requestSubmit();
  };

  form.querySelectorAll('[data-auto-submit]').forEach((control) => {
    control.addEventListener('change', submitFilters);
  });

  if (search instanceof HTMLInputElement) {
    search.addEventListener('input', () => {
      window.clearTimeout(searchTimer);
      searchTimer = window.setTimeout(() => {
        const query = search.value.trim();
        if (query.length === 0 || query.length >= MIN_SEARCH_LENGTH) {
          submitFilters();
        }
      }, SEARCH_DELAY_MS);
    });
  }
}

export default initPendingApprovalsRows;
