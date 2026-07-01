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

        row.addEventListener('keydown', event => {
            if (event.target.closest(interactiveSelector) || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
                return;
            }

            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                navigate();
            }
        });
    });

    document.querySelectorAll('.js-auto-submit').forEach(control => {
        control.addEventListener('change', () => {
            control.form?.requestSubmit();
        });
    });

    const filterForm = document.querySelector('.pi-filter-form');
    const officerFilter = document.getElementById('projectOfficerFilter');
    const assignmentFilter = document.getElementById('assignmentFilter');

    if (filterForm && officerFilter && assignmentFilter) {
        officerFilter.addEventListener('change', () => {
            if (officerFilter.value) {
                assignmentFilter.value = 'all';
            }
        }, { capture: true });

        assignmentFilter.addEventListener('change', () => {
            if (assignmentFilter.value !== 'all') {
                officerFilter.value = '';
            }
        }, { capture: true });
    }

    document.querySelectorAll('.js-clear-search').forEach(button => {
        button.addEventListener('click', () => {
            const form = button.closest('form');
            const searchInput = form?.querySelector('input[name="Query"]');

            if (!form || !searchInput) {
                return;
            }

            searchInput.value = '';
            form.requestSubmit();
        });
    });
})();
