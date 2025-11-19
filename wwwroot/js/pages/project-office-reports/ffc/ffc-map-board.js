(async function () {
    const boardEl = document.getElementById('ffc-board');
    const exportBtn = document.getElementById('ffc-board-export');
    const fullBtn = document.getElementById('ffc-board-fullscreen');
    const screenshotBtn = document.getElementById('ffc-board-screenshot');
    const boardPage = document.getElementById('ffc-board-page');
    const sortSelect = document.getElementById('ffc-board-sort');

    if (!boardEl) {
        return;
    }

    const dataUrl = boardEl.dataset.dataUrl || '/ProjectOfficeReports/FFC/Map?handler=Data';
    let boardData = [];
    let currentSort = sortSelect?.value || 'total';

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
        if (currentSort === 'country') {
            clone.sort((a, b) => (a.name || '').localeCompare(b.name || '')
                || (a.iso3 || '').localeCompare(b.iso3 || ''));
            return clone;
        }

        clone.sort((a, b) => (b.total || 0) - (a.total || 0)
            || (a.name || '').localeCompare(b.name || '')
            || (a.iso3 || '').localeCompare(b.iso3 || ''));
        return clone;
    }

    function render(rows) {
        const sorted = sortRows(rows);
        boardEl.innerHTML = sorted.map(tileHtml).join('');
    }

    function toggleFullWidth() {
        const container = document.querySelector('.container-xxl');
        if (!container) {
            return;
        }

        container.classList.toggle('ffc-board-full');
        const isFull = container.classList.contains('ffc-board-full');
        if (fullBtn) {
            fullBtn.textContent = isFull ? 'Normal width' : 'Full width';
            fullBtn.setAttribute('aria-pressed', isFull ? 'true' : 'false');
        }
    }

    function toggleScreenshotMode() {
        if (!boardPage || !screenshotBtn) {
            return;
        }

        const isActive = boardPage.classList.toggle('ffc-board--screenshot');
        screenshotBtn.setAttribute('aria-pressed', isActive ? 'true' : 'false');
        screenshotBtn.textContent = isActive ? 'Exit screenshot mode' : 'Screenshot mode';
        screenshotBtn.classList.toggle('btn-primary', isActive);
        screenshotBtn.classList.toggle('btn-outline-secondary', !isActive);
    }

    async function exportPng() {
        if (!window.html2canvas) {
            console.error('html2canvas is unavailable');
            return;
        }

        const canvas = await window.html2canvas(boardEl, {
            useCORS: false,
            backgroundColor: '#ffffff',
            scale: 2,
        });
        const dataUrl = canvas.toDataURL('image/png');
        const link = document.createElement('a');
        link.href = dataUrl;
        link.download = 'ffc-country-board.png';
        link.click();
    }

    try {
        boardData = await loadData();
        render(boardData);

        fullBtn?.addEventListener('click', toggleFullWidth);
        exportBtn?.addEventListener('click', exportPng);
        screenshotBtn?.addEventListener('click', toggleScreenshotMode);
        sortSelect?.addEventListener('change', (event) => {
            currentSort = event.target?.value || 'total';
            render(boardData);
        });
    } catch (error) {
        console.error('Board failed', error);
        boardEl.innerHTML = '<div class="alert alert-danger">Failed to load board.</div>';
    }
})();
