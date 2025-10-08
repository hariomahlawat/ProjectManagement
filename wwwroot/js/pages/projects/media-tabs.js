function initMediaTabs() {
    const tabButtons = document.querySelectorAll('[data-media-tab-button]');
    if (!tabButtons || tabButtons.length === 0) {
        return;
    }

    const filterForm = document.querySelector('[data-media-filter-form]');
    if (!filterForm) {
        return;
    }

    const tabInput = filterForm.querySelector('[data-media-tab-input]');
    if (!tabInput) {
        return;
    }

    tabButtons.forEach((button) => {
        button.addEventListener('shown.bs.tab', (event) => {
            const target = event.target;
            if (!target || typeof target.getAttribute !== 'function') {
                return;
            }

            const key = target.getAttribute('data-media-tab');
            if (key) {
                tabInput.value = key;
            }
        });
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initMediaTabs);
} else {
    initMediaTabs();
}
