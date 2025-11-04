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

    // expected shape from C#:
    // [{ year: 2025, totals: { total: 13, sdd: 3, abw515: 10 } }, ...]
    const labels = rows.map(r => r.year);
    const totals = rows.map(r => r.totals?.total ?? 0);
    const sdd = rows.map(r => r.totals?.sdd ?? 0);
    const abw515 = rows.map(r => r.totals?.abw515 ?? 0);

    const ctx = document.getElementById("proliferation-yearly-chart-canvas");
    if (!ctx) return;

    // “wow” bit: twin bars (SDD, 515) + line on top for total
    // fits nicely with your current light UI
    new Chart(ctx, {
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
                        // nicer labels: “10 (SDD)” etc
                        label: function (ctx) {
                            const label = ctx.dataset.label || "";
                            const value = ctx.parsed.y ?? ctx.raw ?? 0;
                            return `${label}: ${value}`;
                        }
                    }
                },
                title: {
                    display: false
                }
            },
            scales: {
                x: {
                    grid: {
                        display: false
                    }
                },
                y: {
                    beginAtZero: true,
                    grid: {
                        color: "rgba(148, 163, 184, 0.25)"
                    },
                    ticks: {
                        precision: 0
                    }
                }
            }
        }
    });
});
