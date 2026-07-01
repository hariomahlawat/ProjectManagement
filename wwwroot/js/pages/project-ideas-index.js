// SECTION: Project Ideas board interactions
(() => {
    const interactiveSelector = 'a, button, input, select, textarea, label, form, [role="button"]';

    document.querySelectorAll('.js-idea-row[data-href]').forEach(row => {
        const navigate = () => {
            const href = row.getAttribute('data-href');
            if (href) {
                window.location.assign(href);
            }
        };

        row.addEventListener('click', event => {
            if (event.target.closest(interactiveSelector)) {
                return;
            }

            if (window.getSelection()?.toString()) {
                return;
            }

            navigate();
        });
    });

    document.querySelectorAll('.js-auto-submit').forEach(control => {
        control.addEventListener('change', () => {
            control.form?.requestSubmit();
        });
    });
})();
