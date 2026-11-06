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
    });
    if (count) count.textContent = String(shown);
    if (empty) empty.hidden = shown > 0;
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
  sort?.addEventListener('change', order);
  scopeButtons.forEach((button) => {
    button.addEventListener('click', () => {
      scope = button.dataset.pbdLibraryScope;
      scopeButtons.forEach((other) => {
        const active = other === button;
        other.classList.toggle('is-active', active);
        other.setAttribute('aria-pressed', String(active));
      });
      apply();
    });
  });
}
