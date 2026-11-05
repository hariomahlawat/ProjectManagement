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
// Theme switches can arrive before the data does; nothing is drawn until it has loaded.
let loaded = false;

// Shows either the chart (message null) or a message in its place, never both.
function setState(messageText) {
    const message = document.getElementById("tot-yearly-chart-message");
    const host = document.getElementById("tot-yearly-chart-host");
    const download = document.getElementById("tot-chart-download");
    const showChart = messageText === null;
    if (message) {
        message.textContent = showChart ? "" : messageText;
        message.hidden = showChart;
    }
    if (host) host.hidden = !showChart;
    if (download) download.hidden = !showChart;
}

function showMessage(text) {
    setState(text);
}

function render() {
    const canvas = document.getElementById("tot-yearly-chart");
    if (!canvas || !loaded) return;

    if (chart) {
        chart.destroy();
        chart = null;
    }

    if (rows.length === 0) {
        showMessage("No ToT completions to show yet.");
        return;
    }

    setState(null);
    chart = new Chart(canvas, {
        type: "bar",
        data: {
            labels: rows.map(row => String(row.year)),
            datasets: [{
                label: "ToT completed",
                data: rows.map(row => row.count),
                backgroundColor: chartTokens().primary,
                // Many years have one or two completions; fully rounded short bars read as dots.
                // Square at the axis, lightly rounded on top, so a bar of 1 still reads as a bar.
                borderSkipped: "start",
                borderRadius: { topLeft: 3, topRight: 3, bottomLeft: 0, bottomRight: 0 }
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

    loaded = true;
    render();
}

document.addEventListener("DOMContentLoaded", init);
