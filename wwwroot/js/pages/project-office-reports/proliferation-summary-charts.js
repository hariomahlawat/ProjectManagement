"use strict";

// wwwroot/js/pages/project-office-reports/proliferation-summary-charts.js
document.addEventListener("DOMContentLoaded", function () {
    const DEVICE_PIXEL_RATIO = window.devicePixelRatio || 1;

    function getParsedDataset(host, key, logLabel) {
        if (!host) {
            return null;
        }

        const raw = host.dataset[key];
        if (!raw) {
            return null;
        }

        try {
            return JSON.parse(raw);
        } catch (error) {
            console.error(`${logLabel}: bad JSON`, error);
            return null;
        }
    }

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
                link.download = `${targetId}.png`;
                link.click();
            });
        });
    }

    (function initYearlyChart() {
        const host = document.getElementById("proliferation-yearly-chart");
        const rows = getParsedDataset(host, "yearly", "Proliferation summary yearly");
        if (!rows || rows.length === 0) {
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
                labels,
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
                devicePixelRatio: DEVICE_PIXEL_RATIO,
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

    (function initTechCategoryChart() {
        const host = document.getElementById("proliferation-techcat-chart");
        const rows = getParsedDataset(host, "categories", "Proliferation technical categories");
        if (!rows || rows.length === 0) {
            return;
        }

        const labels = rows.map(r => r.name);
        const counts = rows.map(r => r.total ?? 0);
        const canvas = document.getElementById("proliferation-techcat-chart-canvas");
        if (!canvas) return;

        const valueLabelPlugin = {
            id: "valueLabelPlugin",
            afterDatasetsDraw(chart) {
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
                labels,
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
                devicePixelRatio: DEVICE_PIXEL_RATIO,
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
