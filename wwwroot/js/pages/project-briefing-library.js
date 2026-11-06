// Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
const askConfirm = options => (window.PrismConfirm?.show
    ? window.PrismConfirm.show(options)
    : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));

// Briefing deck library: narrows and orders the deck table in place. Search matches deck names,
// descriptions and the names of the projects in each deck; "Mine" keeps decks the person owns
// or last edited. Nothing is fetched: every row is already on the page.
const library = document.querySelector('[data-pbd-library]');

if (library) {
  const body = library.querySelector('[data-pbd-library-rows]');
  const rows = [...library.querySelectorAll('[data-pbd-library-row]')];
  const search = library.querySelector('[data-pbd-library-search]');
  const sort = library.querySelector('[data-pbd-library-sort]');
  const scopeButtons = [...library.querySelectorAll('[data-pbd-library-scope]')];
  const count = library.querySelector('[data-pbd-library-count]');
  const empty = library.querySelector('[data-pbd-library-empty]');
  const emptyText = library.querySelector('[data-pbd-library-empty-text]');
  const resetButton = library.querySelector('[data-pbd-library-reset]');
  const title = library.querySelector('[data-pbd-library-title]');
  // Search, filter and sort are kept for this browser tab, so coming back finds the same view.
  const viewKey = 'prism.briefingDecks.libraryView';
  let scope = 'all';

  const comparers = {
    edited: (a, b) => Number(b.dataset.edited) - Number(a.dataset.edited),
    generated: (a, b) => Number(b.dataset.generated) - Number(a.dataset.generated)
      || Number(b.dataset.edited) - Number(a.dataset.edited),
    name: (a, b) => a.dataset.name.localeCompare(b.dataset.name, undefined, { sensitivity: 'base', numeric: true })
  };

  const apply = () => {
    const terms = (search?.value || '').trim().toLowerCase().split(/\s+/).filter(Boolean);
    let shown = 0;
    rows.forEach((row) => {
      const visible = (scope === 'all' || row.dataset.mine === 'true')
        && terms.every((term) => row.dataset.search.includes(term));
      row.hidden = !visible;
      if (visible) shown += 1;
      // When the search found the deck through its projects, say which ones.
      const match = row.querySelector('[data-pbd-library-match]');
      if (!match) return;
      const own = `${row.dataset.name || ''} ${row.querySelector('.pbd-library__description')?.textContent || ''}`.toLowerCase();
      const projects = visible && terms.length && !terms.every((term) => own.includes(term))
        ? (row.dataset.projects || '').split('\n').filter((name) => name && terms.some((term) => name.toLowerCase().includes(term)))
        : [];
      match.hidden = projects.length === 0;
      match.textContent = projects.length
        ? `Matches: ${projects.slice(0, 2).join(', ')}${projects.length > 2 ? ` and ${projects.length - 2} more` : ''}`
        : '';
    });
    if (count) count.textContent = String(shown);
    if (title) title.textContent = scope === 'mine' ? 'My decks' : 'All decks';
    if (empty) empty.hidden = shown > 0;
    // Say why the list is empty, and offer the way out.
    if (emptyText && shown === 0) {
      emptyText.textContent = terms.length
        ? `No ${scope === 'mine' ? 'deck of yours' : 'deck'} matches “${search.value.trim()}”.`
        : 'You have not created or edited a deck yet. "Mine" shows decks you own or last edited.';
    }
    if (resetButton) resetButton.textContent = terms.length && scope === 'all' ? 'Clear search' : 'Show all decks';
    remember();
  };

  const remember = () => {
    try {
      window.sessionStorage.setItem(viewKey, JSON.stringify({ scope, query: search?.value || '', sort: sort?.value || 'edited' }));
    } catch { /* not remembered */ }
  };

  const setScope = (next) => {
    scope = next;
    scopeButtons.forEach((other) => {
      const active = other.dataset.pbdLibraryScope === scope;
      other.classList.toggle('is-active', active);
      other.setAttribute('aria-pressed', String(active));
    });
  };

  const order = () => {
    const compare = comparers[sort?.value] || comparers.edited;
    [...rows].sort(compare).forEach((row) => body.append(row));
  };

  // Delete asks in the Studio's own confirmation dialog. The dialog script loads only when it is
  // needed; if it cannot load, the browser's confirm still guards the delete.
  library.addEventListener('submit', async (event) => {
    const form = event.target.closest('form[data-pbd-library-delete]');
    if (!form || form.dataset.pbdConfirmed === 'true') return;
    event.preventDefault();
    const message = form.dataset.confirm;
    let confirmed;
    try {
      const { prismConfirm } = await import('./project-briefing-confirm.js');
      confirmed = await prismConfirm({
        title: `Delete “${form.dataset.deckName || 'this deck'}”?`,
        message: form.dataset.confirmDetail || message,
        confirmText: 'Delete deck',
        cancelText: 'Keep deck',
        tone: 'danger',
        returnFocus: form.closest('.dropdown')?.querySelector('[data-bs-toggle]')
      });
    } catch {
      confirmed = await askConfirm({ tone: 'danger', title: message, confirmText: 'Delete deck', cancelText: 'Keep deck' });
    }
    if (!confirmed) return;
    form.dataset.pbdConfirmed = 'true';
    form.requestSubmit();
  });

  search?.addEventListener('input', apply);
  sort?.addEventListener('change', () => { order(); remember(); });
  scopeButtons.forEach((button) => {
    button.addEventListener('click', () => {
      setScope(button.dataset.pbdLibraryScope);
      apply();
    });
  });
  resetButton?.addEventListener('click', () => {
    if (search) search.value = '';
    setScope('all');
    apply();
    search?.focus();
  });

  // Back to the view this tab last had.
  try {
    const saved = JSON.parse(window.sessionStorage.getItem(viewKey) || 'null');
    if (saved) {
      if (saved.scope === 'mine') setScope('mine');
      if (search && typeof saved.query === 'string') search.value = saved.query;
      if (sort && ['edited', 'generated', 'name'].includes(saved.sort)) sort.value = saved.sort;
      order();
      apply();
    }
  } catch { /* start fresh */ }
}
