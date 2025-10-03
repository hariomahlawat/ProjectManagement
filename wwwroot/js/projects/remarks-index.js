(function () {
    const namespace = window.ProjectRemarks || {};
    const createPanel = typeof namespace.createRemarksPanel === 'function'
        ? namespace.createRemarksPanel
        : null;
    const showToast = typeof namespace.showToast === 'function'
        ? namespace.showToast
        : undefined;

    const remarksElement = document.querySelector('[data-remarks-panel]');
    if (!remarksElement || !createPanel) {
        return;
    }

    const panel = createPanel(remarksElement, showToast);

    remarksElement.addEventListener('remarks:page-changed', (event) => {
        const detail = event?.detail || {};
        const pageNumber = Number.parseInt(detail.page, 10);
        if (!Number.isFinite(pageNumber)) {
            return;
        }

        if (typeof URL === 'function') {
            const url = new URL(window.location.href);
            if (pageNumber > 1) {
                url.searchParams.set('page', String(pageNumber));
            } else {
                url.searchParams.delete('page');
            }

            if (typeof window.history?.replaceState === 'function') {
                window.history.replaceState({}, '', url.toString());
            }
        }

        if (!detail.initialLoad) {
            if (typeof window.scrollTo === 'function') {
                try {
                    window.scrollTo({ top: 0, behavior: 'smooth' });
                } catch (error) {
                    window.scrollTo(0, 0);
                }
            }
        }
    });

    panel.ensureLoaded();
})();
