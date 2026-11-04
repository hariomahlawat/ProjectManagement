// ToT Summary: "ToT completed per year" bar chart.
// The look, colours and value labels come from pm-chart-defaults.js (the one PRISM chart
// style, token-driven so Dark works and a theme switch redraws the chart).
import {
    applyPmChartDefaults,
    barScales,
    chartTokens,
    watchTheme
} from "../../charts/pm-chart-defaults.js";

let chart = null;
let rows = [];

function showMessage(text) {
    const message = document.getElementById("tot-yearly-chart-message");
    const host = document.getElementById("tot-yearly-chart-host");
    const download = document.getElementById("tot-chart-download");
    if (message) {
        message.textContent = text;
        message.hidden = false;
    }
    if (host) host.hidden = true;
    if (download) download.hidden = true;
}

function render() {
    const canvas = document.getElementById("tot-yearly-chart");
    if (!canvas) return;

    if (chart) {
        chart.destroy();
        chart = null;
    }

    if (rows.length === 0) {
        showMessage("No ToT completions to show yet.");
        return;
    }

    chart = new Chart(canvas, {
        type: "bar",
        data: {
            labels: rows.map(row => String(row.year)),
            datasets: [{
                label: "ToT completed",
                data: rows.map(row => row.count),
                backgroundColor: chartTokens().primary
            }]
        },
        options: {
            interaction: { mode: "index", intersect: false },
            scales: barScales()
        }
    });
}

// The canvas is transparent; paint the card colour behind it so the PNG reads in both themes.
function download() {
    if (!chart) return;
    const source = chart.canvas;
    const copy = document.createElement("canvas");
    copy.width = source.width;
    copy.height = source.height;
    const ctx = copy.getContext("2d");
    const background = chartTokens().card;
    if (background) {
        ctx.fillStyle = background;
        ctx.fillRect(0, 0, copy.width, copy.height);
    }
    ctx.drawImage(source, 0, 0);

    const link = document.createElement("a");
    link.href = copy.toDataURL("image/png");
    link.download = "tot-completed-per-year.png";
    document.body.appendChild(link);
    link.click();
    link.remove();
}

async function init() {
    if (!document.getElementById("tot-yearly-chart")) return;

    if (typeof Chart === "undefined") {
        showMessage("The chart could not be loaded.");
        return;
    }

    applyPmChartDefaults(Chart);
    watchTheme(Chart, render);
    document.getElementById("tot-chart-download")?.addEventListener("click", download);

    try {
        const response = await fetch(`${window.location.pathname}?handler=Yearly`, {
            headers: { Accept: "application/json" },
            credentials: "same-origin"
        });
        if (!response.ok) throw new Error(String(response.status));
        const data = await response.json();
        rows = Array.isArray(data) ? data : [];
    } catch {
        showMessage("The chart data could not be loaded.");
        return;
    }

    render();
}

document.addEventListener("DOMContentLoaded", init);
