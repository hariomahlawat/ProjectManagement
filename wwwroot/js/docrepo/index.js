(function () {
    const form = document.querySelector('form[data-docrepo-filter]');
    if (!form) {
        return;
    }

    const inputs = Array.from(form.querySelectorAll('input, select'));
    inputs.forEach((input) => {
        input.addEventListener('change', () => {
            form.requestSubmit();
        });
    });
})();
