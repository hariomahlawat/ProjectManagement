(async function () {
    const boardEl = document.getElementById('ffc-board');

    if (!boardEl) {
        return;
    }

    const filterState = window.FfcFilterState ? window.FfcFilterState.load() : { showCompleted: true, showPlanned: true };
    const completedCheckbox = document.getElementById('ffcFilterCompleted');
    const plannedCheckbox = document.getElementById('ffcFilterPlanned');

    const dataUrl = boardEl.dataset.dataUrl || '/ProjectOfficeReports/FFC/Map?handler=Data';
    let boardData = [];

    function persistFilterState() {
        if (window.FfcFilterState) {
            window.FfcFilterState.save(filterState);
        }
    }

    async function loadData() {
        const response = await fetch(dataUrl, { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error('Failed to load data');
        }

        /** @type {{name:string, iso3:string, installed:number, delivered:number, planned:number, total:number}[]} */
        const rows = await response.json();
        return rows;
    }

    function esc(value) {
        return String(value ?? '').replace(/[&<>"']/g, (match) => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        })[match]);
    }

    function countryDisplay(row) {
        const iso = (row?.iso3 || '').toUpperCase();
        const name = row?.name;
        return (name && String(name).trim().length > 0) ? name : iso;
    }

    function applyDeliveryFilter(row) {
        const showCompleted = filterState.showCompleted !== false;
        const showPlanned = filterState.showPlanned !== false;
        const installed = showCompleted ? (row.installed || 0) : 0;
        const delivered = showCompleted ? (row.delivered || 0) : 0;
        const planned = showPlanned ? (row.planned || 0) : 0;
        const total = installed + delivered + planned;

        return {
            ...row,
            installed,
            delivered,
            planned,
            total
        };
    }

    function tileHtml(row) {
        const iso = (row.iso3 || '').toUpperCase();
        const name = countryDisplay(row);
        return `
            <div class="ffc-card">
                <div class="ffc-card__hdr">
                    <div>
                        <div class="ffc-card__country">${esc(name)}</div>
                        <div class="text-muted small">ISO-3 <span class="ffc-card__iso">${esc(iso)}</span></div>
                    </div>
                    <div class="ffc-chip">Total units ${(row.total || 0)}</div>
                </div>
                <div class="ffc-rows">
                    <div class="ffc-row"><span>Installed units</span><strong>${row.installed || 0}</strong></div>
                    <div class="ffc-row"><span>Delivered (not installed)</span><strong>${row.delivered || 0}</strong></div>
                    <div class="ffc-row"><span>Planned units</span><strong>${row.planned || 0}</strong></div>
                </div>
            </div>`;
    }


    function sortRows(rows) {
        const clone = Array.isArray(rows) ? [...rows] : [];
        clone.sort((a, b) => (b.total || 0) - (a.total || 0)
            || (a.name || '').localeCompare(b.name || '')
            || (a.iso3 || '').localeCompare(b.iso3 || ''));
        return clone;
    }

    function render(rows) {
        const filtered = rows.map(applyDeliveryFilter);
        const sorted = sortRows(filtered);
        boardEl.innerHTML = sorted.map(tileHtml).join('');
    }

    function syncFilterControls() {
        if (completedCheckbox) {
            completedCheckbox.checked = filterState.showCompleted !== false;
            completedCheckbox.addEventListener('change', () => {
                filterState.showCompleted = completedCheckbox.checked;
                persistFilterState();
                render(boardData);
            });
        }

        if (plannedCheckbox) {
            plannedCheckbox.checked = filterState.showPlanned !== false;
            plannedCheckbox.addEventListener('change', () => {
                filterState.showPlanned = plannedCheckbox.checked;
                persistFilterState();
                render(boardData);
            });
        }
    }

    try {
        boardData = await loadData();
        syncFilterControls();
        render(boardData);
    } catch (error) {
        console.error('Board failed', error);
        boardEl.innerHTML = '<div class="alert alert-danger">Failed to load board.</div>';
    }
})();
