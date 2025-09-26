(function () {
    const offcanvas = document.getElementById('offcanvasProcurement');
    if (!offcanvas) {
        return;
    }

    offcanvas.addEventListener('shown.bs.offcanvas', function () {
        const firstField = offcanvas.querySelector('input,select,textarea');
        if (firstField) {
            firstField.focus();
        }
    });
})();
