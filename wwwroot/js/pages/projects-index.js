(() => {
    'use strict';

    const root = document.querySelector('[data-projects-page]');
    if (!root) return;

    const form = root.querySelector('[data-project-filter-form]');
    const search = root.querySelector('[data-project-search]');
    const autoSubmitControls = root.querySelectorAll('[data-project-auto-submit]');
    const viewButtons = root.querySelectorAll('[data-project-view]');
    const resultViews = root.querySelectorAll('[data-project-results]');
    const storageKey = 'prism.projects.view';
    let searchTimer = 0;

    const submitForm = () => {
        if (!form) return;
        if (typeof form.requestSubmit === 'function') form.requestSubmit();
        else form.submit();
    };

    if (search) {
        search.addEventListener('input', () => {
            window.clearTimeout(searchTimer);
            searchTimer = window.setTimeout(submitForm, 450);
        });

        search.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                window.clearTimeout(searchTimer);
            }
        });
    }

    autoSubmitControls.forEach(control => {
        control.addEventListener('change', submitForm);
    });

    const setView = view => {
        const resolved = view === 'table' ? 'table' : 'cards';
        viewButtons.forEach(button => {
            const isActive = button.dataset.projectView === resolved;
            button.classList.toggle('is-active', isActive);
            button.setAttribute('aria-pressed', String(isActive));
        });
        resultViews.forEach(container => {
            const isActive = container.dataset.projectResults === resolved;
            container.classList.toggle('is-active', isActive);
            container.hidden = !isActive;
        });
        try { window.localStorage.setItem(storageKey, resolved); } catch { /* storage is optional */ }
    };

    viewButtons.forEach(button => {
        button.addEventListener('click', () => setView(button.dataset.projectView));
    });

    let preferredView = 'cards';
    try { preferredView = window.localStorage.getItem(storageKey) || 'cards'; } catch { /* storage is optional */ }
    setView(preferredView);

    const typeRadios = root.querySelectorAll('[data-project-type]');
    const unclassified = root.querySelector('[data-project-type-unclassified]');
    const clearUnclassified = root.querySelector('[data-clear-unclassified]');

    typeRadios.forEach(radio => {
        radio.addEventListener('change', () => {
            if (radio.checked && unclassified) unclassified.checked = false;
        });
    });

    if (unclassified) {
        unclassified.addEventListener('change', () => {
            if (!unclassified.checked) return;
            typeRadios.forEach(radio => { radio.checked = false; });
            if (clearUnclassified) clearUnclassified.checked = false;
        });
    }

    if (clearUnclassified) {
        clearUnclassified.addEventListener('change', () => {
            if (clearUnclassified.checked && unclassified) unclassified.checked = false;
        });
    }
})();

// Enhancement block: current-page sorting and full-row keyboard/mouse navigation.
(() => {
    'use strict';

    const table = document.querySelector('[data-project-sort-table]');
    if (!table) return;

    const tbody = table.tBodies[0];
    const rows = () => Array.from(tbody.querySelectorAll('[data-project-row]'));
    const sortButtons = table.querySelectorAll('[data-sort]');
    let currentKey = '';
    let currentDirection = 1;

    const columnIndexByKey = {
        project: 0,
        status: 1,
        officer: 3,
        category: 4,
        casefile: 5
    };

    const normalise = value => (value || '').trim().toLocaleLowerCase();

    const sortTable = key => {
        const columnIndex = columnIndexByKey[key];
        if (columnIndex === undefined) return;

        currentDirection = currentKey === key ? currentDirection * -1 : 1;
        currentKey = key;

        const sorted = rows().sort((a, b) => {
            const aCell = a.cells[columnIndex];
            const bCell = b.cells[columnIndex];
            const aValue = normalise(aCell?.dataset.sortValue || aCell?.textContent);
            const bValue = normalise(bCell?.dataset.sortValue || bCell?.textContent);
            return aValue.localeCompare(bValue, undefined, { numeric: true, sensitivity: 'base' }) * currentDirection;
        });

        sorted.forEach(row => tbody.appendChild(row));
        sortButtons.forEach(button => {
            const active = button.dataset.sort === key;
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-sort', active ? (currentDirection === 1 ? 'ascending' : 'descending') : 'none');
            const icon = button.querySelector('i');
            if (icon) icon.className = active
                ? `bi ${currentDirection === 1 ? 'bi-sort-alpha-down' : 'bi-sort-alpha-up'} `
                : 'bi bi-arrow-down-up';
        });
    };

    sortButtons.forEach(button => button.addEventListener('click', event => {
        event.stopPropagation();
        sortTable(button.dataset.sort);
    }));

    rows().forEach(row => {
        const open = () => {
            const href = row.dataset.href;
            if (href) window.location.assign(href);
        };

        row.addEventListener('click', event => {
            if (event.target.closest('a, button, input, select, textarea, label')) return;
            open();
        });

        row.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                open();
            }
        });
    });
})();
