// Proliferation Summary: project finder, ranked-table disclosure and the two analytics charts.
// The chart look, colours and value labels come from pm-chart-defaults.js (the one PRISM chart
// style, token-driven so Dark works and a theme switch redraws the charts).
import {
    applyPmChartDefaults,
    barScales,
    chartTokens,
    renderLegend,
    sizeChartHost,
    watchTheme
} from "../../charts/pm-chart-defaults.js";

document.addEventListener("DOMContentLoaded", () => {
    const numberFormatter = new Intl.NumberFormat();
    let yearlyChart = null;
    let categoryChart = null;
    let renderYearChart = null;

    if (typeof Chart !== "undefined") {
        applyPmChartDefaults(Chart);
        watchTheme(Chart, () => {
            renderYearChart?.();
            if (categoryChart) {
                categoryChart.destroy();
                categoryChart = null;
                const panel = document.querySelector('[data-analytics-panel="category"]');
                if (panel && !panel.hidden) renderCategoryChart();
            }
        });
    }

    const categoryNames = new Map([
        ["Firing Smls", "Firing Simulators"],
        ["Firing Sml", "Firing Simulators"],
        ["Driving Smls", "Driving Simulators"],
        ["Driving Sml", "Driving Simulators"],
        ["AR/ VR", "AR / VR"],
        ["AR/VR", "AR / VR"],
        ["AI", "Artificial Intelligence"]
    ]);

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
            getExtraParams: () => ({ includeLegacy: true }),
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
            button.textContent = expanded ? "Show top 15" : `View all ${numberFormatter.format(rows.length)} projects`;
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
            const tokens = chartTokens();
            // SDD and 515 ABW are two tints of the primary hue and are named in the legend
            // chips and tooltips, so the split never relies on colour alone.
            const series = [
                { label: "SDD", color: tokens.primary, values: visible.map(x => x.totals?.sdd ?? 0) },
                { label: "515 ABW", color: tokens.primarySoft, values: visible.map(x => x.totals?.abw515 ?? 0) }
            ];
            yearlyChart?.destroy();
            yearlyChart = new Chart(canvas, {
                type: "bar",
                data: {
                    labels: visible.map(x => String(x.year)),
                    datasets: series.map(item => ({
                        label: item.label,
                        data: item.values,
                        backgroundColor: item.color,
                        stack: "total"
                    }))
                },
                options: {
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        tooltip: {
                            callbacks: {
                                label: context => `${context.dataset.label}: ${numberFormatter.format(Number(context.parsed.y || 0))}`,
                                footer: items => `Total: ${numberFormatter.format(items.reduce((sum, item) => sum + Number(item.parsed.y || 0), 0))}`
                            }
                        }
                    },
                    scales: barScales({ stacked: true })
                }
            });
            renderLegend(
                document.querySelector('[data-legend="proliferation-yearly-chart"]'),
                yearlyChart,
                series.map((item, index) => ({ label: item.label, color: item.color, datasetIndex: index })));
        };
        renderYearChart = render;

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

    function renderCategoryChart() {
        const host = document.getElementById("proliferation-techcat-chart");
        const canvas = document.getElementById("proliferation-techcat-chart-canvas");
        if (!host || !canvas) return;

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

        sizeChartHost(host, { rows: rows.length, horizontal: true, min: 245, max: 720 });
        const total = rows.reduce((sum, row) => sum + Number(row.total || 0), 0);

        categoryChart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: rows.map(x => categoryNames.get(x.name) || x.name),
                datasets: [{
                    label: "Total proliferation",
                    data: rows.map(x => x.total ?? 0),
                    backgroundColor: chartTokens().primary
                }]
            },
            options: {
                indexAxis: "y",
                layout: { padding: { right: 52, top: 4, bottom: 0 } },
                interaction: { mode: "nearest", intersect: true },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: context => {
                                const value = Number(context.parsed.x || 0);
                                const percentage = total > 0 ? ` (${(value / total * 100).toFixed(1)}%)` : "";
                                return `${numberFormatter.format(value)}${percentage}`;
                            }
                        }
                    }
                },
                scales: barScales({ horizontal: true })
            }
        });
    }

    function initAnalyticsTabs() {
        const tabs = [...document.querySelectorAll("[data-analytics-tab]")];
        const panels = [...document.querySelectorAll("[data-analytics-panel]")];
        const trendControls = document.querySelector("[data-analytics-trend-controls]");
        const description = document.getElementById("pf-analytics-description");
        if (!tabs.length || !panels.length) return;

        const activate = view => {
            const target = view === "category" ? "category" : "trend";
            tabs.forEach(tab => {
                const active = tab.dataset.analyticsTab === target;
                tab.classList.toggle("active", active);
                tab.setAttribute("aria-selected", active ? "true" : "false");
                tab.tabIndex = active ? 0 : -1;
            });
            panels.forEach(panel => {
                const active = panel.dataset.analyticsPanel === target;
                panel.classList.toggle("d-none", !active);
                panel.hidden = !active;
            });
            trendControls?.classList.toggle("d-none", target !== "trend");
            if (description) {
                description.textContent = target === "trend"
                    ? "Year-wise reported proliferation with the SDD and 515 ABW split."
                    : "Reported proliferation grouped by technical category.";
            }
            window.requestAnimationFrame(() => {
                if (target === "category") renderCategoryChart();
                else yearlyChart?.resize();
            });
        };

        tabs.forEach(tab => {
            tab.addEventListener("click", () => activate(tab.dataset.analyticsTab));
            tab.addEventListener("keydown", event => {
                if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
                event.preventDefault();
                const index = tabs.indexOf(tab);
                let next = index;
                if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
                if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
                if (event.key === 'Home') next = 0;
                if (event.key === 'End') next = tabs.length - 1;
                tabs[next]?.focus();
                activate(tabs[next]?.dataset.analyticsTab);
            });
        });

        activate("trend");
    }

    initProjectFinder();
    initProjectTableDisclosure();
    initYearChart();
    initAnalyticsTabs();
});
