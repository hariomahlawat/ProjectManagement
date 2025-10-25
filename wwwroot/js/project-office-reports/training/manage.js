(function () {
    const scheduleInputs = document.querySelectorAll('input[name="Input.ScheduleMode"]');
    const dateRangeSection = document.querySelector('[data-training-schedule="date-range"]');
    const monthYearSection = document.querySelector('[data-training-schedule="month-year"]');

    function updateVisibility() {
        const selected = document.querySelector('input[name="Input.ScheduleMode"]:checked');
        const mode = selected ? selected.value : null;
        const isDateRange = mode === 'DateRange' || mode === '0';

        if (dateRangeSection) {
            dateRangeSection.classList.toggle('d-none', !isDateRange);
        }

        if (monthYearSection) {
            monthYearSection.classList.toggle('d-none', isDateRange);
        }
    }

    scheduleInputs.forEach((input) => {
        input.addEventListener('change', updateVisibility);
    });

    updateVisibility();
})();
