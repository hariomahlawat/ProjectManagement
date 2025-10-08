document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('select[data-auto-submit]').forEach((selectElement) => {
        selectElement.addEventListener('change', () => {
            const form = selectElement.form;
            if (form) {
                form.submit();
            }
        });
    });
});
