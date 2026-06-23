(() => {
    'use strict';

    if (typeof Chart === 'undefined') return;

    const rootStyles = getComputedStyle(document.documentElement);
    const blue = rootStyles.getPropertyValue('--bs-primary').trim() || '#3f6ee8';
    const green = '#2e9d68';
    const grid = 'rgba(148, 163, 184, .18)';
    const text = rootStyles.getPropertyValue('--bs-secondary-color').trim() || '#64748b';
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

    const readJson = (canvas, attr) => {
        try { return JSON.parse(canvas.dataset[attr] || '[]'); }
        catch { return []; }
    };

    const commonOptions = {
        responsive: true,
        maintainAspectRatio: false,
        animation: reduced ? false : { duration: 450 },
        plugins: {
            legend: { position: 'top', align: 'start', labels: { boxWidth: 11, boxHeight: 11, color: text, usePointStyle: true } },
            tooltip: { mode: 'index', intersect: false }
        },
        scales: {
            x: { grid: { display: false }, ticks: { color: text } },
            y: { beginAtZero: true, ticks: { precision: 0, color: text }, grid: { color: grid } }
        }
    };

    const eventCanvas = document.getElementById('iprEventTrend');
    if (eventCanvas) {
        const rows = readJson(eventCanvas, 'yearly');
        new Chart(eventCanvas, {
            type: 'bar',
            data: {
                labels: rows.map(x => x.Year ?? x.year),
                datasets: [
                    { label: 'Filed', data: rows.map(x => x.Filed ?? x.filed ?? 0), backgroundColor: blue, borderRadius: 6, maxBarThickness: 42 },
                    { label: 'Granted', data: rows.map(x => x.Granted ?? x.granted ?? 0), backgroundColor: green, borderRadius: 6, maxBarThickness: 42 }
                ]
            },
            options: commonOptions
        });
    }

    const typeCanvas = document.getElementById('iprTypeTrend');
    if (typeCanvas) {
        const rows = readJson(typeCanvas, 'types');
        new Chart(typeCanvas, {
            type: 'bar',
            data: {
                labels: rows.map(x => x.Type ?? x.type),
                datasets: [
                    { label: 'Filed', data: rows.map(x => x.Filed ?? x.filed ?? 0), backgroundColor: blue, borderRadius: 6, maxBarThickness: 34 },
                    { label: 'Granted', data: rows.map(x => x.Granted ?? x.granted ?? 0), backgroundColor: green, borderRadius: 6, maxBarThickness: 34 }
                ]
            },
            options: { ...commonOptions, indexAxis: 'y' }
        });
    }
})();
