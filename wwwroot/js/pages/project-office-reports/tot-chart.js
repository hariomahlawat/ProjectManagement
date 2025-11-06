// wwwroot/js/pages/project-office-reports/tot-chart.js

let totChartInstance = null;

async function loadTotYearlyChart() {
    const canvas = document.getElementById("tot-yearly-chart");
    if (!canvas) {
        return;
    }

    const resp = await fetch(window.location.pathname + "?handler=Yearly", {
        headers: {
            "Accept": "application/json"
        }
    });

    if (!resp.ok) {
        return;
    }

    const data = await resp.json();
    const ctx = canvas.getContext("2d");

    const commonLayout = {
        padding: {
            left: 12,
            right: 12,
            bottom: 32
        }
    };

    if (totChartInstance) {
        totChartInstance.destroy();
        totChartInstance = null;
    }

    if (!data || data.length === 0) {
        totChartInstance = new Chart(ctx, {
            type: "bar",
            data: {
                labels: ["No data"],
                datasets: [{
                    label: "ToT completed",
                    data: [0]
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                layout: commonLayout,
                plugins: {
                    legend: { display: false },
                    tooltip: { enabled: false }
                },
                scales: {
                    x: { display: false, grid: { display: false } },
                    y: { display: false, grid: { display: false } }
                }
            },
            plugins: [{
                id: "empty-message",
                afterDraw(chart) {
                    const area = chart.chartArea;
                    const ctx = chart.ctx;
                    ctx.save();
                    ctx.textAlign = "center";
                    ctx.textBaseline = "middle";
                    ctx.fillStyle = "#6b7280";
                    ctx.font = "13px system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif";
                    const x = (area.left + area.right) / 2;
                    const y = (area.top + area.bottom) / 2;
                    ctx.fillText("No ToT completions to display", x, y);
                    ctx.restore();
                }
            }]
        });

        attachDownloadHandler();
        return;
    }

    const labels = data.map(x => x.year);
    const values = data.map(x => x.count);

    totChartInstance = new Chart(ctx, {
        type: "bar",
        data: {
            labels: labels,
            datasets: [{
                label: "ToT completed",
                data: values,
                backgroundColor: "rgba(59, 130, 246, 0.35)",
                borderColor: "rgba(59, 130, 246, 1)",
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            layout: commonLayout,
            plugins: {
                legend: { display: false },
                tooltip: {
                    mode: "index",
                    intersect: false
                },
                title: { display: false }
            },
            scales: {
                x: {
                    grid: { display: false },
                    ticks: {
                        maxRotation: 0,
                        autoSkip: true
                    }
                },
                y: {
                    beginAtZero: true,
                    ticks: { stepSize: 1 }
                }
            }
        }
    });

    attachDownloadHandler();
}

function attachDownloadHandler() {
    const btn = document.getElementById("tot-chart-download");
    if (!btn) {
        return;
    }
    btn.onclick = function () {
        if (!totChartInstance) {
            return;
        }
        const url = totChartInstance.toBase64Image();
        const link = document.createElement("a");
        link.href = url;
        link.download = "tot-completed-per-year.png";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };
}

document.addEventListener("DOMContentLoaded", loadTotYearlyChart);
