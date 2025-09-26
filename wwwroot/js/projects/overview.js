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

    const marker = document.getElementById('open-procurement');
    if (marker && marker.dataset.open === '1' && typeof bootstrap !== 'undefined') {
        const instance = bootstrap.Offcanvas.getOrCreateInstance(offcanvas);
        instance.show();
    }
})();
