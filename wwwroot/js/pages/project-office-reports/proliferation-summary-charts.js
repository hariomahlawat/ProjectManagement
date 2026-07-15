"use strict";

document.addEventListener("DOMContentLoaded", () => {
    const numberFormatter = new Intl.NumberFormat();
    let yearlyChart = null;
    let categoryChart = null;

    function parseData(host, attribute) {
        if (!host) return [];
        try {
            const parsed = JSON.parse(host.dataset[attribute] || "[]");
            return Array.isArray(parsed) ? parsed : [];
        } catch {
            return [];
        }
    }

    function showChartMessage(id, message) {
        const element = document.getElementById(id);
        if (!element) return;
        element.textContent = message;
        element.classList.remove("d-none");
    }

    function initProjectFinder() {
        const input = document.getElementById("pf-project-search");
        const hidden = document.getElementById("pf-project-search-id");
        const suggestions = document.getElementById("pf-project-search-suggestions");
        const clear = document.getElementById("pf-project-search-clear");
        const open = document.getElementById("pf-project-search-open");
        const status = document.getElementById("pf-project-search-status");
        if (!input || !suggestions || !window.ProliferationProjectPicker) return;

        const picker = new window.ProliferationProjectPicker({
            input,
            hiddenInput: hidden,
            suggestions,
            clearButton: clear,
            statusElement: status,
            onSelected: () => { open.disabled = false; },
            onCleared: () => { open.disabled = true; }
        });

        open?.addEventListener("click", () => {
            if (!picker.requireSelection()) return;
            const id = Number(hidden?.value || picker.selected?.id);
            if (Number.isInteger(id) && id > 0) {
                window.location.assign(`/ProjectOfficeReports/Proliferation/Project/${id}`);
            }
        });

        input.addEventListener("keydown", event => {
            if (event.key === "Enter" && picker.selected) {
                event.preventDefault();
                open?.click();
            }
        });
    }

    function initProjectTableDisclosure() {
        const button = document.getElementById("pf-project-show-all");
        const count = document.getElementById("pf-project-count");
        const rows = [...document.querySelectorAll("[data-project-row]")];
        if (!button || rows.length <= 15) return;

        let expanded = false;
        button.addEventListener("click", () => {
            expanded = !expanded;
            rows.forEach(row => {
                const rank = Number(row.dataset.projectRank || 0);
                row.classList.toggle("pf-project-row--collapsed", !expanded && rank > 15);
            });
            button.textContent = expanded ? "Show top 15 only" : "Show all projects";
            count.textContent = expanded
                ? `Showing all ${numberFormatter.format(rows.length)} projects`
                : `Showing 15 of ${numberFormatter.format(rows.length)} projects`;
        });
    }

    function initYearChart() {
        const host = document.getElementById("proliferation-yearly-chart");
        const canvas = document.getElementById("proliferation-yearly-chart-canvas");
        const rows = parseData(host, "yearly");
        if (!canvas || rows.length === 0) {
            showChartMessage("proliferation-yearly-chart-message", "No year-wise data is available.");
            return;
        }
        if (typeof Chart === "undefined") {
            showChartMessage("proliferation-yearly-chart-message", "The chart could not be loaded. Use the year-wise export for the complete data.");
            return;
        }

        let range = 10;
        const render = () => {
            const visible = range === "all" ? rows : rows.slice(-range);
            yearlyChart?.destroy();
            yearlyChart = new Chart(canvas, {
                type: "bar",
                data: {
                    labels: visible.map(x => String(x.year)),
                    datasets: [
                        {
                            label: "SDD",
                            data: visible.map(x => x.totals?.sdd ?? 0),
                            backgroundColor: "rgba(59, 130, 246, 0.82)",
                            borderRadius: 4,
                            maxBarThickness: 42,
                            stack: "total"
                        },
                        {
                            label: "515 ABW",
                            data: visible.map(x => x.totals?.abw515 ?? 0),
                            backgroundColor: "rgba(14, 165, 233, 0.66)",
                            borderRadius: 4,
                            maxBarThickness: 42,
                            stack: "total"
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        legend: { position: "top" },
                        tooltip: {
                            callbacks: {
                                footer: items => `Total: ${numberFormatter.format(items.reduce((sum, item) => sum + Number(item.parsed.y || 0), 0))}`
                            }
                        }
                    },
                    scales: {
                        x: { stacked: true, grid: { display: false } },
                        y: {
                            stacked: true,
                            beginAtZero: true,
                            ticks: { precision: 0, callback: value => numberFormatter.format(value) },
                            grid: { color: "rgba(148, 163, 184, 0.2)" }
                        }
                    }
                }
            });
        };

        document.querySelectorAll("[data-trend-range]").forEach(button => {
            const isActive = button.classList.contains("active");
            button.setAttribute("aria-pressed", isActive ? "true" : "false");
            button.addEventListener("click", () => {
                range = button.dataset.trendRange === "all" ? "all" : Number(button.dataset.trendRange || 10);
                document.querySelectorAll("[data-trend-range]").forEach(item => {
                    const active = item === button;
                    item.classList.toggle("active", active);
                    item.setAttribute("aria-pressed", active ? "true" : "false");
                });
                render();
            });
        });

        render();
    }

    function initCategoryChartWhenOpened() {
        const details = document.getElementById("proliferation-technical-analysis");
        const host = document.getElementById("proliferation-techcat-chart");
        const canvas = document.getElementById("proliferation-techcat-chart-canvas");
        if (!details || !host || !canvas) return;

        const render = () => {
            if (categoryChart) {
                categoryChart.resize();
                return;
            }
            const rows = parseData(host, "categories");
            if (rows.length === 0) {
                showChartMessage("proliferation-techcat-chart-message", "No technical-category data is available.");
                return;
            }
            if (typeof Chart === "undefined") {
                showChartMessage("proliferation-techcat-chart-message", "The category chart could not be loaded.");
                return;
            }

            categoryChart = new Chart(canvas, {
                type: "bar",
                data: {
                    labels: rows.map(x => x.name),
                    datasets: [{
                        label: "Total proliferation",
                        data: rows.map(x => x.total ?? 0),
                        backgroundColor: "rgba(59, 130, 246, 0.78)",
                        borderRadius: 5,
                        maxBarThickness: 32
                    }]
                },
                options: {
                    indexAxis: "y",
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: {
                            beginAtZero: true,
                            ticks: { precision: 0, callback: value => numberFormatter.format(value) },
                            grid: { color: "rgba(148, 163, 184, 0.2)" }
                        },
                        y: { grid: { display: false } }
                    }
                }
            });
        };

        details.addEventListener("toggle", () => {
            if (details.open) window.requestAnimationFrame(render);
        });
        if (details.open) window.requestAnimationFrame(render);
    }

    initProjectFinder();
    initProjectTableDisclosure();
    initYearChart();
    initCategoryChartWhenOpened();
});
