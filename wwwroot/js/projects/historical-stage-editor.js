(() => {
    'use strict';

    const completedOutcome = '1';
    const ceasedOutcome = '3';

    function setFieldState(field, enabled) {
        if (!(field instanceof HTMLInputElement)) {
            return;
        }

        field.disabled = !enabled;
        if (!enabled) {
            field.value = '';
        }
    }

    function updateRow(row) {
        const outcome = row.querySelector('[data-historical-stage-outcome]');
        const start = row.querySelector('[data-historical-stage-start]');
        const completed = row.querySelector('[data-historical-stage-completed]');
        if (!(outcome instanceof HTMLSelectElement)) {
            return;
        }

        setFieldState(
            start,
            outcome.value === completedOutcome || outcome.value === ceasedOutcome);
        setFieldState(completed, outcome.value === completedOutcome);
    }

    document.querySelectorAll('[data-historical-stage-form]').forEach(form => {
        const rows = Array.from(form.querySelectorAll('[data-historical-stage-row]'));
        rows.forEach(row => {
            const outcome = row.querySelector('[data-historical-stage-outcome]');
            outcome?.addEventListener('change', () => updateRow(row));
            updateRow(row);
        });

        form.addEventListener('submit', () => {
            const submit = form.querySelector('[data-historical-stage-submit]');
            if (submit instanceof HTMLButtonElement) {
                submit.disabled = true;
                submit.textContent = 'Saving…';
            }
        });
    });
})();
