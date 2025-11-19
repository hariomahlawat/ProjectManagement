(async function () {
    const boardEl = document.getElementById('ffc-board');

    if (!boardEl) {
        return;
    }

    const dataUrl = boardEl.dataset.dataUrl || '/ProjectOfficeReports/FFC/Map?handler=Data';
    let boardData = [];

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
        const sorted = sortRows(rows);
        boardEl.innerHTML = sorted.map(tileHtml).join('');
    }

    try {
        boardData = await loadData();
        render(boardData);
    } catch (error) {
        console.error('Board failed', error);
        boardEl.innerHTML = '<div class="alert alert-danger">Failed to load board.</div>';
    }
})();
