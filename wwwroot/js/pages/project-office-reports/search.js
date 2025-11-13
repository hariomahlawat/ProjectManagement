// Global Search UI wiring (clear button behaves like Google)
(function () {
    function wire(form) {
        if (!form) return;
        const input = form.querySelector('input[name="q"]');
        const clearEl = form.querySelector('[data-clear]');
        if (!input || !clearEl) return;

        // show/hide clear button
        const update = () => {
            const has = !!(input.value && input.value.trim().length);
            clearEl.style.visibility = has ? 'visible' : 'hidden';
        };

        // click: force clear (do not submit)
        clearEl.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            input.value = '';
            input.focus();
            update();
        });

        // keyboard: Esc clears
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                input.value = '';
                update();
                e.preventDefault();
            }
        });

        input.addEventListener('input', update);
        update();
    }

    document.addEventListener('DOMContentLoaded', () => {
        wire(document.querySelector('form.pm-gs-search'));     // hero
        wire(document.querySelector('form.pm-gs-top__form'));  // results top bar
    });
})();
