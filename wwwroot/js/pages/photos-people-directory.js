(() => {
    'use strict';

    const workspace = document.querySelector('[data-people-directory]');
    if (!workspace) return;

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
