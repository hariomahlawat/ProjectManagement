// wwwroot/js/proliferation-summary-charts.js
document.addEventListener("DOMContentLoaded", function () {
    const host = document.getElementById("proliferation-yearly-chart");
    if (!host) return;

    const raw = host.dataset.yearly;
    if (!raw) return;

    let rows;
    try {
        rows = JSON.parse(raw);
    } catch (e) {
        console.error("Proliferation summary: bad JSON", e);
        return;
    }

    const labels = rows.map(r => r.year);
    const totals = rows.map(r => r.totals?.total ?? 0);
    const sdd = rows.map(r => r.totals?.sdd ?? 0);
    const abw515 = rows.map(r => r.totals?.abw515 ?? 0);

    const canvas = document.getElementById("proliferation-yearly-chart-canvas");
    if (!canvas) return;

    const chart = new Chart(canvas, {
        type: "bar",
        data: {
            labels: labels,
            datasets: [
                {
                    type: "bar",
                    label: "SDD",
                    data: sdd,
                    backgroundColor: "rgba(59, 130, 246, 0.75)",
                    borderRadius: 8,
                    order: 2,
                    maxBarThickness: 34
                },
                {
                    type: "bar",
                    label: "515 ABW",
                    data: abw515,
                    backgroundColor: "rgba(14, 165, 233, 0.75)",
                    borderRadius: 8,
                    order: 2,
                    maxBarThickness: 34
                },
                {
                    type: "line",
                    label: "Total",
                    data: totals,
                    borderColor: "rgba(15, 23, 42, 0.9)",
                    borderWidth: 2,
                    tension: 0.35,
                    pointRadius: 4,
                    pointBackgroundColor: "rgba(15, 23, 42, 1)",
                    order: 1,
                    yAxisID: "y"
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            // force high-res render so it doesn’t look pixelated
            devicePixelRatio: window.devicePixelRatio || 1,
            interaction: {
                mode: "index",
                intersect: false
            },
            plugins: {
                legend: {
                    position: "top",
                    labels: {
                        usePointStyle: true
                    }
                },
                tooltip: {
                    callbacks: {
                        label: function (ctx) {
                            const label = ctx.dataset.label || "";
                            const value = ctx.parsed.y ?? ctx.raw ?? 0;
                            return `${label}: ${value}`;
                        }
                    }
                }
            },
            scales: {
                x: {
                    grid: { display: false }
                },
                y: {
                    beginAtZero: true,
                    grid: { color: "rgba(148, 163, 184, 0.25)" },
                    ticks: { precision: 0 }
                }
            }
        }
    });

    // download PNG
    const dlBtn = document.querySelector('.chart-actions [data-action="download-png"]');
    if (dlBtn) {
        dlBtn.addEventListener("click", function () {
            const link = document.createElement("a");
            link.href = canvas.toDataURL("image/png");
            link.download = "proliferation-yearly.png";
            link.click();
        });
    }
});
