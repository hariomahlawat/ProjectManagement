(() => {
    'use strict';

    function reveal(image) {
        if (!(image instanceof HTMLImageElement)) {
            return;
        }

        const picture = image.closest('picture');
        const host = image.closest('[data-project-cover-host]');
        const fallback = host?.querySelector('[data-project-cover-fallback]');

        if (!host || !fallback) {
            return;
        }

        picture?.remove();
        if (!picture) {
            image.remove();
        }

        host.classList.remove('project-photo-cover-frame--media');
        host.classList.add('project-photo-cover-frame--empty', 'is-unavailable');
        fallback.classList.remove('d-none');
    }

    function wire(image) {
        if (!(image instanceof HTMLImageElement) || image.dataset.coverFallbackWired === '1') {
            return;
        }

        image.dataset.coverFallbackWired = '1';
        image.addEventListener('error', () => reveal(image), { once: true });

        if (image.complete && image.naturalWidth === 0) {
            reveal(image);
        }
    }

    document.addEventListener('error', (event) => {
        const image = event.target;

        if (image instanceof HTMLImageElement && image.matches('[data-project-cover-image]')) {
            reveal(image);
        }
    }, true);

    document.querySelectorAll('[data-project-cover-image]').forEach(wire);
})();
