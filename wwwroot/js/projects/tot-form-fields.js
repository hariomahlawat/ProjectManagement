(() => {
    'use strict';

    function setGroup(group, enabled) {
        if (!group) return;
        group.classList.toggle('d-none', !enabled);
        group.querySelectorAll('input, select, textarea').forEach(control => {
            control.disabled = !enabled;
        });
    }

    function initialise(root) {
        const status = root.querySelector('[data-tot-shared-status]');
        if (!(status instanceof HTMLSelectElement)) return;

        const start = root.querySelector('[data-tot-shared-start]');
        const completion = root.querySelector('[data-tot-shared-completion]');
        const milestones = root.querySelector('[data-tot-shared-milestones]');
        const guidance = root.querySelector('[data-tot-shared-guidance]');
        const startHelp = root.querySelector('[data-tot-shared-start-help]');
        const fopm = root.querySelector('[data-tot-shared-fopm]');
        const fopmDate = root.querySelector('[data-tot-shared-fopm-date]');

        function update() {
            const value = status.value;
            const inProgress = value === 'InProgress';
            const completed = value === 'Completed';
            const datesApplicable = inProgress || completed;

            setGroup(start, datesApplicable);
            setGroup(completion, completed);
            setGroup(milestones, datesApplicable);

            if (guidance) {
                guidance.textContent = {
                    InProgress: 'Start date is required.',
                    Completed: 'Start date is optional; completion date is required.',
                    NotStarted: 'Start and completion dates are not applicable.',
                    NotRequired: 'ToT dates and milestones are not applicable.'
                }[value] || '';
            }

            if (startHelp) {
                startHelp.textContent = completed
                    ? 'Optional. Enter year, month and year, or exact date.'
                    : 'Required. Enter year, month and year, or exact date.';
            }

            const manufactured = datesApplicable &&
                fopm instanceof HTMLSelectElement &&
                fopm.value === 'true';
            setGroup(fopmDate, manufactured);
        }

        status.addEventListener('change', update);
        fopm?.addEventListener('change', update);
        update();
    }

    document.querySelectorAll('[data-tot-status-form]').forEach(initialise);
})();
