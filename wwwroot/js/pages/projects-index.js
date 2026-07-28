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

    root.querySelectorAll('[data-project-card-cover-image]').forEach(image => {
        const host = image.closest('[data-project-card-cover]');
        if (!host) return;

        const showFallback = () => {
            image.hidden = true;
            image.removeAttribute('srcset');
            host.classList.add('project-card__visual--icon');
        };

        image.addEventListener('error', showFallback, { once: true });

        // The image can fail before this deferred page script attaches its
        // listener. naturalWidth is the reliable post-load failure check.
        if (image.complete && image.naturalWidth === 0) {
            showFallback();
        }
    });
})();

// Enhancement block: full-row keyboard/mouse navigation. Sorting is handled
// server-side so that card view, table view and every page share one order.
(() => {
    'use strict';

    const rows = document.querySelectorAll('[data-project-row]');
    if (!rows.length) return;

    rows.forEach(row => {
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
