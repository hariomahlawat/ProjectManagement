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
      if (saved.scope === 'mine' && scopeButtons.length) setScope('mine');
      if (search && typeof saved.query === 'string') search.value = saved.query;
      if (sort && ['edited', 'generated', 'name'].includes(saved.sort)) sort.value = saved.sort;
      order();
      apply();
    }
  } catch { /* start fresh */ }
}

// Generate from the library: leave a one-time marker for the Studio, which then generates. A link
// with ?generate=1 opened any other way only opens the deck (review 2 fixes, Low 9).
document.addEventListener('click', (event) => {
  const link = event.target instanceof Element ? event.target.closest('a[href*="generate=1"]') : null;
  if (!link) return;
  // Only a plain click that opens the deck in this tab: a link opened in another tab or window
  // would leave the marker here, for a later plain Open to consume (Codex review on #3389).
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey
      || (link.target && link.target !== '_self')) return;
  const match = /BriefingDecks\/(\d+)/.exec(link.getAttribute('href') || '');
  if (!match) return;
  try { window.sessionStorage.setItem(`prism.briefingDecks.generate.${match[1]}`, '1'); } catch { /* opens without generating */ }
});

// Download builds the PowerPoint on the server and returns a file, so the page does not change.
// It runs as a request: the button stays busy until the file has arrived (or failed), and a
// second click cannot build it twice (review 4 fixes Low 7, review 5 fixes L2). Anything that is
// not a PowerPoint (an error message, or the sign-in page after it expired) is shown by opening
// the page the server answered with.
const fileNameOf = (header) => {
  if (!header) return 'Project_Briefing_Deck.pptx';
  const encoded = header.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  if (encoded) {
    try { return decodeURIComponent(encoded); } catch { /* the plain name below */ }
  }
  return header.match(/filename="?([^";]+)"?/i)?.[1] || 'Project_Briefing_Deck.pptx';
};

document.addEventListener('submit', async (event) => {
  const form = event.target instanceof HTMLFormElement ? event.target : null;
  if (!form || !/handler=Download/i.test(form.getAttribute('action') || '')) return;
  event.preventDefault();
  const button = form.querySelector('button[type="submit"]');
  if (button instanceof HTMLButtonElement) {
    if (button.dataset.pbdDownloading === 'true') return;
    button.dataset.pbdDownloading = 'true';
    button.setAttribute('aria-disabled', 'true');
  }
  const making = window.PrismToast?.show({ tone: 'info', text: 'Making the PowerPoint…', sticky: true });
  try {
    const response = await fetch(form.action, { method: 'POST', credentials: 'same-origin', body: new FormData(form) });
    const type = response.headers.get('content-type') || '';
    if (!response.ok || response.redirected || !type.includes('presentationml')) {
      // The server's own message (or the sign-in page) is on the page it answered with.
      window.location.assign(response.url || window.location.href);
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileNameOf(response.headers.get('content-disposition'));
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    making?.dismiss?.();
    window.PrismToast?.show({ tone: 'success', text: 'PowerPoint downloaded.' });
  } catch {
    making?.dismiss?.();
    window.PrismToast?.show({ tone: 'danger', text: 'The PowerPoint could not be downloaded. Check the connection and try again.' });
  } finally {
    if (button instanceof HTMLButtonElement) {
      delete button.dataset.pbdDownloading;
      button.removeAttribute('aria-disabled');
    }
  }
});
