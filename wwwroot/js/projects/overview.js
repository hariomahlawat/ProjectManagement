(function () {
    if (typeof bootstrap === 'undefined') {
        return;
    }

    const procurement = document.getElementById('offcanvasProcurement');
    if (procurement) {
        procurement.addEventListener('shown.bs.offcanvas', function () {
            const firstField = procurement.querySelector('input,select,textarea');
            if (firstField) {
                firstField.focus();
            }
        });

        const marker = document.getElementById('open-procurement');
        if (marker && marker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(procurement);
            instance.show();
        }
    }

    const assignRoles = document.getElementById('offcanvasAssignRoles');
    if (assignRoles) {
        assignRoles.addEventListener('shown.bs.offcanvas', function () {
            const firstField = assignRoles.querySelector('select, input, textarea');
            if (firstField) {
                firstField.focus();
            }
        });

        const assignMarker = document.getElementById('open-assign-roles');
        if (assignMarker && assignMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(assignRoles);
            instance.show();
        }
    }

    const planEdit = document.getElementById('offcanvasPlanEdit');
    if (planEdit) {
        planEdit.addEventListener('shown.bs.offcanvas', function () {
            const firstDate = planEdit.querySelector('input[type="date"]');
            if (firstDate) {
                firstDate.focus();
            }
        });

        const planMarker = document.getElementById('open-plan-edit');
        if (planMarker && planMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(planEdit);
            instance.show();
        }
    }

    const planReview = document.getElementById('offcanvasPlanReview');
    if (planReview) {
        planReview.addEventListener('shown.bs.offcanvas', function () {
            const firstAction = planReview.querySelector('button, input, select, textarea');
            if (firstAction) {
                firstAction.focus();
            }
        });

        const reviewMarker = document.getElementById('open-plan-review');
        if (reviewMarker && reviewMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(planReview);
            instance.show();
        }
    }
})();
