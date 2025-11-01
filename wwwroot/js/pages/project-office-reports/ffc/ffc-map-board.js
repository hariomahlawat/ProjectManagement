(async function () {
    const boardEl = document.getElementById('ffc-board');
    const exportBtn = document.getElementById('ffc-board-export');
    const fullBtn = document.getElementById('ffc-board-fullscreen');
    const screenshotBtn = document.getElementById('ffc-board-screenshot');
    const boardPage = document.getElementById('ffc-board-page');

    if (!boardEl) {
        return;
    }

    async function loadData() {
        const url = '/ProjectOfficeReports/FFC/Map?handler=Data';
        const response = await fetch(url, { credentials: 'same-origin' });
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
                    <div class="ffc-chip">Total ${(row.total || 0)}</div>
                </div>
                <div class="ffc-rows">
                    <div class="ffc-row"><span>Installed</span><strong>${row.installed || 0}</strong></div>
                    <div class="ffc-row"><span>Delivered (not installed)</span><strong>${row.delivered || 0}</strong></div>
                    <div class="ffc-row"><span>Planned</span><strong>${row.planned || 0}</strong></div>
                </div>
            </div>`;
    }

    function render(rows) {
        rows.sort((a, b) => (b.total || 0) - (a.total || 0)
            || (a.name || '').localeCompare(b.name || '')
            || (a.iso3 || '').localeCompare(b.iso3 || ''));
        boardEl.innerHTML = rows.map(tileHtml).join('');
    }

    function toggleFullWidth() {
        const container = document.querySelector('.container-xxl');
        if (!container) {
            return;
        }

        container.classList.toggle('ffc-board-full');
        const isFull = container.classList.contains('ffc-board-full');
        fullBtn.textContent = isFull ? 'Normal width' : 'Full width';
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
        const data = await loadData();
        render(data);

        fullBtn?.addEventListener('click', toggleFullWidth);
        exportBtn?.addEventListener('click', exportPng);
        screenshotBtn?.addEventListener('click', toggleScreenshotMode);
    } catch (error) {
        console.error('Board failed', error);
        boardEl.innerHTML = '<div class="alert alert-danger">Failed to load board.</div>';
    }
})();
