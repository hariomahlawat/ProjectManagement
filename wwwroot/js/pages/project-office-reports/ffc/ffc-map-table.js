(async function () {
    const table = document.getElementById('ffc-table');
    if (!table) {
        return;
    }

    const tbody = table.querySelector('tbody');
    async function load() {
        const response = await fetch('/ProjectOfficeReports/FFC/MapTable?handler=Data', { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error('Failed to load data');
        }

        /** @type {{name:string, iso3:string, installed:number, delivered:number, planned:number, total:number}[]} */
        const rows = await response.json();
        rows.sort((a, b) => (b.total || 0) - (a.total || 0)
            || (a.name || '').localeCompare(b.name || '')
            || (a.iso3 || '').localeCompare(b.iso3 || ''));
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

    function render(rows) {
        tbody.innerHTML = rows.map((r) => `
            <tr>
                <td>
                    ${esc(r.name || '')}
                    <small class="text-muted ms-1"><code>${esc((r.iso3 || '').toUpperCase())}</code></small>
                </td>
                <td class="text-end">${r.installed || 0}</td>
                <td class="text-end">${r.delivered || 0}</td>
                <td class="text-end">${r.planned || 0}</td>
                <td class="text-end fw-semibold">${r.total || 0}</td>
            </tr>`).join('');
    }

    const data = await load();
    render(data);

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
