"use strict";

document.addEventListener("DOMContentLoaded", () => {
    const numberFormatter = new Intl.NumberFormat();

    function parseData(host, key) {
        if (!host?.dataset?.[key]) return [];
        try {
            const value = JSON.parse(host.dataset[key]);
            return Array.isArray(value) ? value : [];
        } catch (error) {
            console.error(`Unable to read proliferation ${key} data.`, error);
            return [];
        }
    }

    function wireProjectSearch() {
        const input = document.getElementById("pf-project-search");
        const clear = document.getElementById("pf-project-search-clear");
        const rows = Array.from(document.querySelectorAll("[data-project-row]"));
        const empty = document.getElementById("pf-project-no-results");
        const count = document.getElementById("pf-project-count");
        if (!input || rows.length === 0) return;

        const apply = () => {
            const query = input.value.trim().toLocaleLowerCase();
            let visible = 0;
            rows.forEach(row => {
                const match = !query || (row.dataset.search || "").includes(query);
                row.classList.toggle("d-none", !match);
                if (match) visible += 1;
            });

            clear?.classList.toggle("d-none", !query);
            empty?.classList.toggle("d-none", visible !== 0);
            if (count) {
                count.textContent = query
                    ? `${numberFormatter.format(visible)} matching ${visible === 1 ? "project" : "projects"}`
                    : `Showing ${numberFormatter.format(rows.length)} projects`;
            }
        };

        input.addEventListener("input", apply);
        clear?.addEventListener("click", () => {
            input.value = "";
            apply();
            input.focus();
        });
    }

    function initYearChart() {
        const host = document.getElementById("proliferation-yearly-chart");
        const canvas = document.getElementById("proliferation-yearly-chart-canvas");
        const rows = parseData(host, "yearly")
            .slice()
            .sort((a, b) => Number(a.year) - Number(b.year));

        if (!canvas || rows.length === 0 || typeof Chart === "undefined") return;

        let range = 10;
        const buildRows = () => range === "all" ? rows : rows.slice(-Number(range));

        const chart = new Chart(canvas, {
            type: "bar",
            data: { labels: [], datasets: [] },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: {
                        position: "bottom",
                        labels: { usePointStyle: true, boxWidth: 8, padding: 18 }
                    },
                    tooltip: {
                        callbacks: {
                            footer(items) {
                                return `Total: ${numberFormatter.format(items.reduce((sum, item) => sum + Number(item.raw || 0), 0))}`;
                            }
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

        const render = () => {
            const selected = buildRows();
            chart.data.labels = selected.map(x => x.year);
            chart.data.datasets = [
                {
                    label: "SDD",
                    data: selected.map(x => x.totals?.sdd ?? 0),
                    backgroundColor: "rgba(59, 130, 246, 0.82)",
                    borderRadius: 5,
                    maxBarThickness: 44
                },
                {
                    label: "515 ABW",
                    data: selected.map(x => x.totals?.abw515 ?? 0),
                    backgroundColor: "rgba(14, 165, 233, 0.72)",
                    borderRadius: 5,
                    maxBarThickness: 44
                }
            ];
            chart.update();
        };

        document.querySelectorAll("[data-trend-range]").forEach(button => {
            button.addEventListener("click", () => {
                range = button.dataset.trendRange === "all" ? "all" : Number(button.dataset.trendRange || 10);
                document.querySelectorAll("[data-trend-range]").forEach(item => item.classList.toggle("active", item === button));
                render();
            });
        });

        render();
    }

    function initCategoryChart() {
        const host = document.getElementById("proliferation-techcat-chart");
        const canvas = document.getElementById("proliferation-techcat-chart-canvas");
        const rows = parseData(host, "categories");
        if (!canvas || rows.length === 0 || typeof Chart === "undefined") return;

        new Chart(canvas, {
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
    }

    wireProjectSearch();
    initYearChart();
    initCategoryChart();
});
