const trigger = document.querySelector('[data-cw-filter-trigger]');
const popover = document.querySelector('[data-cw-filter-popover]');
if (trigger && popover) {
    trigger.addEventListener('click', () => {
        const willOpen = popover.hidden;
        popover.hidden = !willOpen;
        trigger.setAttribute('aria-expanded', String(willOpen));
    });
    document.addEventListener('click', (event) => {
        if (!popover.hidden && !popover.contains(event.target) && !trigger.contains(event.target)) {
            popover.hidden = true;
            trigger.setAttribute('aria-expanded', 'false');
        }
    });
}

const canvas = document.getElementById('command-stage-chart');
if (canvas && window.Chart) {
    let rows = [];
    try { rows = JSON.parse(canvas.dataset.series || '[]'); } catch { rows = []; }
    const stageNames = [...new Map(rows.map(row => [row.stageCode, row.stageName])).entries()];
    const categories = [...new Set(rows.map(row => row.categoryName))];
    const palette = ['#3c68e8', '#ef7a00', '#52c653', '#8f4cf0', '#15a6a6', '#d94b68'];
    const datasets = categories.map((category, index) => ({
        label: category,
        data: stageNames.map(([code]) => rows.find(row => row.stageCode === code && row.categoryName === category)?.count || 0),
        backgroundColor: palette[index % palette.length],
        borderWidth: 0,
        borderRadius: 3,
        maxBarThickness: 52
    }));
    const chart = new Chart(canvas, {
        type: 'bar',
        data: { labels: stageNames.map(([code]) => code === 'UNASSIGNED' ? 'Unassigned' : code), datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            plugins: {
                legend: { position: 'top', labels: { usePointStyle: true, pointStyle: 'rectRounded', boxWidth: 10, boxHeight: 10, padding: 18 } },
                tooltip: { callbacks: { title: items => stageNames[items[0].dataIndex]?.[1] || items[0].label } }
            },
            scales: {
                x: { stacked: true, grid: { display: false }, ticks: { color: '#5f6e83' } },
                y: { stacked: true, beginAtZero: true, ticks: { precision: 0, color: '#5f6e83' }, grid: { color: 'rgba(103,119,143,.15)' } }
            }
        }
    });
    document.querySelector('[data-chart-download]')?.addEventListener('click', () => {
        const link = document.createElement('a');
        link.download = 'ongoing-projects-stage-distribution.png';
        link.href = chart.toBase64Image('image/png', 1);
        link.click();
    });
}
