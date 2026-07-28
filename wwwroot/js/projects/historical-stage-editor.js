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
        const offcanvas = form.closest('#offcanvasHistoricalStages');

        const clearTargetedRow = () => {
            rows.forEach(row => row.classList.remove('is-targeted'));
        };

        const focusStageRow = stageCode => {
            clearTargetedRow();
            if (!stageCode) {
                return;
            }

            const row = rows.find(candidate =>
                (candidate.dataset.stageCode ?? '').toLowerCase() === stageCode.toLowerCase());
            if (!(row instanceof HTMLElement)) {
                return;
            }

            row.classList.add('is-targeted');
            row.scrollIntoView({ behavior: 'smooth', block: 'center' });

            const start = row.querySelector('[data-historical-stage-start]');
            if (start instanceof HTMLInputElement && !start.disabled) {
                window.setTimeout(() => start.focus({ preventScroll: true }), 180);
            }
        };

        if (offcanvas instanceof HTMLElement) {
            offcanvas.addEventListener('shown.bs.offcanvas', event => {
                const trigger = event.relatedTarget;
                const stageCode = trigger instanceof Element
                    ? trigger.getAttribute('data-historical-focus-stage') ?? ''
                    : '';
                focusStageRow(stageCode);
            });
            offcanvas.addEventListener('hidden.bs.offcanvas', clearTargetedRow);
        }

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
