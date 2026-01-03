(function (window, document) {
    'use strict';

    // SECTION: DOM references
    var table = document.querySelector('[data-ffc-detailed-table]');
    if (!table) {
        return;
    }

    var tbody = table.tBodies && table.tBodies.length > 0 ? table.tBodies[0] : null;
    if (!tbody) {
        return;
    }

    var completedCheckbox = document.getElementById('ffcFilterCompleted');
    var plannedCheckbox = document.getElementById('ffcFilterPlanned');

    // SECTION: Filter state
    var filterState = window.FfcFilterState ? window.FfcFilterState.load() : { showCompleted: true, showPlanned: true };

    function persistFilterState() {
        if (window.FfcFilterState) {
            window.FfcFilterState.save(filterState);
        }
    }

    // SECTION: Status helpers
    function resolveBucket(statusText) {
        var value = (statusText || '').toLowerCase();
        if (value.indexOf('planned') !== -1) {
            return 'planned';
        }
        if (value.indexOf('delivered') !== -1 || value.indexOf('installed') !== -1) {
            return 'completed';
        }
        return '';
    }

    function shouldShowRow(bucket) {
        if (!bucket) {
            return true;
        }

        if (bucket === 'planned') {
            return filterState.showPlanned !== false;
        }

        return filterState.showCompleted !== false;
    }

    // SECTION: Filter application
    function applyFilters() {
        var rows = Array.from(tbody.rows);
        var activeGroupRow = null;
        var groupHasVisibleRows = false;

        rows.forEach(function (row) {
            if (row.classList.contains('ffc-dtable__group-row')) {
                if (activeGroupRow) {
                    activeGroupRow.classList.toggle('d-none', !groupHasVisibleRows);
                }
                activeGroupRow = row;
                groupHasVisibleRows = false;
                return;
            }

            var status = row.getAttribute('data-ffc-status') || '';
            var bucket = resolveBucket(status);
            var show = shouldShowRow(bucket);

            row.classList.toggle('d-none', !show);
            if (show) {
                groupHasVisibleRows = true;
            }
        });

        if (activeGroupRow) {
            activeGroupRow.classList.toggle('d-none', !groupHasVisibleRows);
        }
    }

    // SECTION: Drill-down link handling
    function disableDrillDownLinks() {
        var links = table.querySelectorAll('a.ffc-dtable__link');
        links.forEach(function (link) {
            link.removeAttribute('href');
            link.setAttribute('aria-disabled', 'true');
            link.setAttribute('tabindex', '-1');
            link.classList.add('text-muted', 'text-decoration-none');
        });
    }

    // SECTION: UI wiring
    if (completedCheckbox) {
        completedCheckbox.checked = filterState.showCompleted !== false;
        completedCheckbox.addEventListener('change', function () {
            filterState.showCompleted = completedCheckbox.checked;
            persistFilterState();
            applyFilters();
        });
    }

    if (plannedCheckbox) {
        plannedCheckbox.checked = filterState.showPlanned !== false;
        plannedCheckbox.addEventListener('change', function () {
            filterState.showPlanned = plannedCheckbox.checked;
            persistFilterState();
            applyFilters();
        });
    }

    disableDrillDownLinks();
    applyFilters();
})(window, document);
