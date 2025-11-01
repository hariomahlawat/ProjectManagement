(async function () {
    const table = document.getElementById('ffc-table');
    if (!table) {
        return;
    }

    const tbody = table.querySelector('tbody');
    const exportBtn = document.getElementById('ffc-table-export');

    async function load() {
        const response = await fetch('/ProjectOfficeReports/FFC/Map?handler=Data', { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error('Failed to load data');
        }

        /** @type {{iso3:string, installed:number, delivered:number, planned:number, total:number}[]} */
        const rows = await response.json();
        rows.sort((a, b) => (b.total || 0) - (a.total || 0) || (a.iso3 || '').localeCompare(b.iso3 || ''));
        return rows;
    }

    function render(rows) {
        tbody.innerHTML = rows.map((r) => `
            <tr>
                <td><code>${(r.iso3 || '').toUpperCase()}</code></td>
                <td class="text-end">${r.installed || 0}</td>
                <td class="text-end">${r.delivered || 0}</td>
                <td class="text-end">${r.planned || 0}</td>
                <td class="text-end fw-semibold">${r.total || 0}</td>
            </tr>`).join('');
    }

    function exportCsv(rows) {
        const header = ['ISO3', 'Installed', 'Delivered (not installed)', 'Planned', 'Total'];
        const lines = [header.join(',')].concat(rows.map((r) => [
            (r.iso3 || '').toUpperCase(),
            r.installed || 0,
            r.delivered || 0,
            r.planned || 0,
            r.total || 0,
        ].join(',')));

        const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'ffc-country-table.csv';
        link.click();
        URL.revokeObjectURL(url);
    }

    const data = await load();
    render(data);
    exportBtn?.addEventListener('click', () => exportCsv(data));

    table.querySelectorAll('th[data-k]').forEach((th) => {
        th.style.cursor = 'pointer';
        let asc = false;
        th.addEventListener('click', () => {
            const key = th.dataset.k;
            asc = !asc;
            data.sort((a, b) => {
                const va = a[key] ?? '';
                const vb = b[key] ?? '';
                if (typeof va === 'number' && typeof vb === 'number') {
                    return asc ? va - vb : vb - va;
                }

                return asc
                    ? String(va).localeCompare(String(vb))
                    : String(vb).localeCompare(String(va));
            });
            render(data);
        });
    });
})();
