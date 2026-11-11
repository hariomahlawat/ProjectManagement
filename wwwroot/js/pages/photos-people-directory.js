(() => {
    'use strict';

    const workspace = document.querySelector('[data-people-directory]');
    if (!workspace) return;

    // The slim bar sticks just below the application's top bar, whatever its height.
    const topbar = document.querySelector('.pm-topbar');
    if (topbar && workspace.querySelector('[data-photos-bar]')) {
        const syncBarOffset = () => workspace.style.setProperty('--photos-bar-top', `${topbar.offsetHeight}px`);
        syncBarOffset();
        if ('ResizeObserver' in window) new ResizeObserver(syncBarOffset).observe(topbar);
    }

    const searchForm = workspace.querySelector('[data-people-search-form]');
    const searchInput = workspace.querySelector('[data-people-search-input]');
    if (searchForm && searchInput) {
        let searchTimer;
        searchInput.addEventListener('input', () => {
            window.clearTimeout(searchTimer);
            searchTimer = window.setTimeout(() => searchForm.requestSubmit(), 450);
        });
    }

    const toggle = workspace.querySelector('[data-people-select-toggle]');
    const cancel = workspace.querySelector('[data-people-selection-cancel]');
    const bar = workspace.querySelector('[data-people-selection-bar]');
    const countLabel = workspace.querySelector('[data-people-selection-count]');
    const submit = workspace.querySelector('[data-people-selection-submit]');
    const submitLabel = workspace.querySelector('[data-people-selection-submit-label]');
    const matchValue = workspace.querySelector('[data-people-match-value]');
    const matchButtons = [...workspace.querySelectorAll('[data-people-match]')];
    const cards = [...workspace.querySelectorAll('[data-person-card]')];
    const selections = [...workspace.querySelectorAll('[data-person-selection]')];
    const persistedSelections = [...workspace.querySelectorAll('[data-person-persisted-selection]')];

    if (!toggle || !bar || selections.length === 0) return;

    let selectionMode = false;
    const maximumSelection = 10;

    const selected = () => selections.filter(input => input.checked);
    const selectedCount = () => selected().length + persistedSelections.length;

    // SECTION: Keep the selection across search, sort and paging
    // Ticked people live only in this page's checkboxes. Searching, sorting or changing
    // page reloads the directory, so write the current selection into those forms and
    // links; the server renders it back (off-page selections as persisted inputs).
    const navigationForms = [searchForm, workspace.querySelector('.people-sort')].filter(Boolean);
    const pageLinks = [...workspace.querySelectorAll('.people-pagination a[href]')];

    function selectedIds() {
        return [...selected(), ...persistedSelections].map(input => input.value);
    }

    function syncSelectionIntoNavigation() {
        const ids = selectedIds();
        const match = ids.length > 1 ? (matchValue?.value === 'any' ? 'any' : 'all') : null;

        navigationForms.forEach(form => {
            form.querySelectorAll('input[type="hidden"][name="SelectedIds"], input[type="hidden"][name="PeopleMatch"]')
                .forEach(input => input.remove());
            ids.forEach(id => {
                const hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = 'SelectedIds';
                hidden.value = id;
                form.appendChild(hidden);
            });
            if (match) {
                const hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = 'PeopleMatch';
                hidden.value = match;
                form.appendChild(hidden);
            }
        });

        pageLinks.forEach(link => {
            const url = new URL(link.getAttribute('href'), window.location.href);
            [...url.searchParams.keys()]
                .filter(key => key === 'SelectedIds' || /^SelectedIds\[\d+\]$/.test(key) || key === 'PeopleMatch')
                .forEach(key => url.searchParams.delete(key));
            ids.forEach(id => url.searchParams.append('SelectedIds', id));
            if (match) url.searchParams.set('PeopleMatch', match);
            link.setAttribute('href', `${url.pathname}${url.search}${url.hash}`);
        });
    }

    function setCardState(input) {
        const card = input.closest('[data-person-card]');
        card?.classList.toggle('is-selected', input.checked);
        card?.querySelector('[data-person-selection-control]')
            ?.setAttribute('aria-pressed', String(input.checked));
    }

    function update() {
        const chosen = selected();
        const totalChosen = chosen.length + persistedSelections.length;
        selections.forEach(setCardState);

        countLabel.textContent = `${totalChosen} selected`;
        submit.disabled = totalChosen === 0;
        submitLabel.textContent = totalChosen > 1
            ? (matchValue.value === 'all' ? 'View photos together' : 'View selected photos')
            : 'View photos';

        selections.forEach(input => {
            input.disabled = !input.checked && totalChosen >= maximumSelection;
        });

        if (totalChosen < 2 && matchValue.value !== 'all') {
            matchValue.value = 'all';
            matchButtons.forEach(button => {
                button.classList.toggle('is-active', button.dataset.peopleMatch === 'all');
            });
        }

        matchButtons.forEach(button => {
            button.disabled = totalChosen < 2;
        });

        syncSelectionIntoNavigation();
    }

    function enterSelectionMode() {
        selectionMode = true;
        workspace.classList.add('is-selecting');
        toggle.setAttribute('aria-pressed', 'true');
        toggle.classList.add('is-active');
        bar.hidden = false;
        update();
    }

    function exitSelectionMode() {
        selectionMode = false;
        workspace.classList.remove('is-selecting');
        toggle.setAttribute('aria-pressed', 'false');
        toggle.classList.remove('is-active');
        selections.forEach(input => {
            input.checked = false;
            input.disabled = false;
            setCardState(input);
        });
        persistedSelections.splice(0).forEach(input => input.remove());
        matchValue.value = 'all';
        matchButtons.forEach(button => {
            button.classList.toggle('is-active', button.dataset.peopleMatch === 'all');
            button.disabled = false;
        });
        bar.hidden = true;
        update();
    }

    function toggleSelection(input) {
        if (!selectionMode) enterSelectionMode();
        if (!input.checked && selectedCount() >= maximumSelection) return;
        input.checked = !input.checked;
        update();
    }

    toggle.addEventListener('click', () => {
        if (selectionMode) exitSelectionMode();
        else enterSelectionMode();
    });
    cancel?.addEventListener('click', exitSelectionMode);

    cards.forEach(card => {
        const input = card.querySelector('[data-person-selection]');
        const control = card.querySelector('[data-person-selection-control]');
        const galleryLink = card.querySelector('[data-person-gallery-link]');
        if (!input) return;

        input.addEventListener('change', update);
        control?.addEventListener('click', event => {
            event.preventDefault();
            event.stopPropagation();
            toggleSelection(input);
        });
        galleryLink?.addEventListener('click', event => {
            if (!selectionMode) return;
            event.preventDefault();
            toggleSelection(input);
        });
    });

    matchButtons.forEach(button => {
        button.addEventListener('click', () => {
            if (selectedCount() < 2) return;
            matchValue.value = button.dataset.peopleMatch === 'any' ? 'any' : 'all';
            matchButtons.forEach(candidate => candidate.classList.toggle('is-active', candidate === button));
            update();
        });
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && selectionMode) exitSelectionMode();
    });

    if (selectedCount() > 0) enterSelectionMode();
    else update();
})();
