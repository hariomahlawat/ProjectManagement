// wwwroot/js/proliferation-summary-charts.js
document.addEventListener("DOMContentLoaded", function () {
    // helper to wire download buttons
    function wireDownloadButtons() {
        const btns = document.querySelectorAll('[data-action="download-png"][data-target]');
        btns.forEach(btn => {
            btn.addEventListener("click", function () {
                const targetId = btn.dataset.target;
                if (!targetId) return;
                const canvas = document.getElementById(targetId);
                if (!canvas) return;
                const link = document.createElement("a");
                link.href = canvas.toDataURL("image/png");
                link.download = targetId + ".png";
                link.click();
            });
        });
    }

    // chart 1: yearly trend (already present earlier)
    (function initYearlyChart() {
        const host = document.getElementById("proliferation-yearly-chart");
        if (!host) return;

        const raw = host.dataset.yearly;
        if (!raw) return;

        let rows;
        try {
            rows = JSON.parse(raw);
        } catch (e) {
            console.error("Proliferation summary yearly: bad JSON", e);
            return;
        }

        const labels = rows.map(r => r.year);
        const totals = rows.map(r => r.totals?.total ?? 0);
        const sdd = rows.map(r => r.totals?.sdd ?? 0);
        const abw515 = rows.map(r => r.totals?.abw515 ?? 0);

        const canvas = document.getElementById("proliferation-yearly-chart-canvas");
        if (!canvas) return;

        new Chart(canvas, {
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
    })();

    // chart 2: by technical category (new)
    (function initTechCategoryChart() {
        const host = document.getElementById("proliferation-techcat-chart");
        if (!host) return;

        const raw = host.dataset.categories;
        if (!raw) return;

        let rows;
        try {
            rows = JSON.parse(raw);
        } catch (e) {
            console.error("Proliferation technical categories: bad JSON", e);
            return;
        }

        if (!Array.isArray(rows) || rows.length === 0) {
            return;
        }

        const labels = rows.map(r => r.name);
        const counts = rows.map(r => r.total ?? 0);
        const canvas = document.getElementById("proliferation-techcat-chart-canvas");
        if (!canvas) return;

        // simple plugin to draw value at bar end
        const valueLabelPlugin = {
            id: "valueLabelPlugin",
            afterDatasetsDraw(chart, args, opts) {
                const { ctx } = chart;
                ctx.save();
                const meta = chart.getDatasetMeta(0);
                meta.data.forEach((bar, index) => {
                    const value = counts[index];
                    ctx.font = "12px system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif";
                    ctx.fillStyle = "#0f172a";
                    ctx.textBaseline = "middle";
                    const x = bar.x + 6;
                    const y = bar.y;
                    ctx.fillText(String(value), x, y);
                });
                ctx.restore();
            }
        };

        new Chart(canvas, {
            type: "bar",
            data: {
                labels: labels,
                datasets: [
                    {
                        label: "Proliferations",
                        data: counts,
                        backgroundColor: "rgba(59, 130, 246, 0.85)",
                        borderRadius: 6,
                        maxBarThickness: 32
                    }
                ]
            },
            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) {
                                return `Proliferations: ${ctx.parsed.x}`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        beginAtZero: true,
                        ticks: { precision: 0 },
                        grid: { color: "rgba(148, 163, 184, 0.25)" }
                    },
                    y: {
                        grid: { display: false }
                    }
                }
            },
            plugins: [valueLabelPlugin]
        });
    })();

    wireDownloadButtons();
});
