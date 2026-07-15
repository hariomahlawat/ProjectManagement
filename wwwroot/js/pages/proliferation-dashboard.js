"use strict";

(() => {
    const root = document.querySelector('[data-page="proliferation-records"]');
    if (!root) return;

    const api = {
        groups: "/api/proliferation/groups",
        projects: "/api/proliferation/projects",
        export: "/api/proliferation/export"
    };

    const formatter = new Intl.NumberFormat();
    const state = {
        page: 1,
        pageSize: 25,
        total: 0,
        projectId: null,
        controller: null,
        projectController: null
    };

    const el = {
        project: document.getElementById("pf-record-project"),
        projectId: document.getElementById("pf-record-project-id"),
        projectSuggestions: document.getElementById("pf-record-project-suggestions"),
        source: document.getElementById("pf-record-source"),
        year: document.getElementById("pf-record-year"),
        search: document.getElementById("pf-record-search"),
        reset: document.getElementById("pf-record-reset"),
        export: document.getElementById("pf-record-export"),
        summary: document.getElementById("pf-record-summary"),
        pageSize: document.getElementById("pf-record-page-size"),
        groups: document.getElementById("pf-record-groups"),
        loading: document.getElementById("pf-record-loading"),
        empty: document.getElementById("pf-record-empty"),
        error: document.getElementById("pf-record-error"),
        range: document.getElementById("pf-record-range"),
        prev: document.getElementById("pf-record-prev"),
        next: document.getElementById("pf-record-next")
    };

    const debounce = (fn, delay = 300) => {
        let timer;
        return (...args) => {
            clearTimeout(timer);
            timer = setTimeout(() => fn(...args), delay);
        };
    };

    function addText(parent, tag, text, className) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        node.textContent = text ?? "";
        parent.appendChild(node);
        return node;
    }

    function formatDate(value) {
        if (!value) return "—";
        const date = new Date(`${value}T00:00:00`);
        return Number.isNaN(date.getTime())
            ? value
            : date.toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" });
    }

    function queryParams() {
        const params = new URLSearchParams({
            page: String(state.page),
            pageSize: String(state.pageSize)
        });
        if (state.projectId) params.set("projectId", String(state.projectId));
        if (el.source.value) params.set("source", el.source.value);
        if (el.year.value) params.set("year", el.year.value);
        if (el.search.value.trim()) params.set("search", el.search.value.trim());
        return params;
    }

    async function readError(response) {
        const text = await response.text().catch(() => "");
        if (!text) return `Request failed (${response.status}).`;
        try {
            const json = JSON.parse(text);
            return json.message || json.detail || json.title || text;
        } catch {
            return text;
        }
    }

    function setLoading(isLoading) {
        el.loading.classList.toggle("d-none", !isLoading);
        el.groups.setAttribute("aria-busy", String(isLoading));
    }

    function renderCalculation(item, body) {
        const mode = String(item.effectiveMode || "");
        const combined = mode === "UseYearlyAndGranular";
        const annualOnly = mode === "UseYearly" || (mode === "Auto" && Number(item.detailedQuantity || 0) === 0);

        if (combined) {
            const calculation = document.createElement("div");
            calculation.className = "pf-record-calculation";
            const values = [
                ["Annual quantity", item.annualQuantity, "Quantity without individual date/unit details"],
                ["Detailed quantity", item.detailedQuantity, `${formatter.format(item.detailedEntryCount)} approved ${item.detailedEntryCount === 1 ? "entry" : "entries"}`],
                ["Reported total", item.reportedTotal, "Both quantities are counted"]
            ];

            values.forEach((value, index) => {
                const block = document.createElement("div");
                if (index === 2) block.classList.add("pf-record-calculation__result");
                addText(block, "span", value[0]);
                addText(block, "strong", formatter.format(value[1] || 0));
                addText(block, "small", value[2]);
                calculation.appendChild(block);
                if (index < 2) addText(calculation, "span", index === 0 ? "+" : "=", "pf-record-calculation__operator");
            });
            body.appendChild(calculation);
        } else {
            const selection = document.createElement("div");
            selection.className = "pf-calculation__selection";
            const counted = document.createElement("div");
            counted.className = "pf-calculation__selected";
            addText(counted, "span", annualOnly ? "Annual quantity" : "Detailed quantity");
            addText(counted, "strong", formatter.format(annualOnly ? item.annualQuantity : item.detailedQuantity));
            addText(counted, "small", "Counted in the reported total");
            selection.appendChild(counted);
            addText(selection, "span", "=", "pf-record-calculation__operator");

            const result = document.createElement("div");
            result.className = "pf-calculation__result";
            addText(result, "span", "Reported total");
            addText(result, "strong", formatter.format(item.reportedTotal || 0));
            addText(result, "small", item.calculationLabel);
            selection.appendChild(result);

            const reference = document.createElement("div");
            reference.className = "pf-calculation__reference";
            addText(reference, "span", annualOnly ? "Detailed quantity" : "Annual quantity");
            addText(reference, "strong", formatter.format(annualOnly ? item.detailedQuantity : item.annualQuantity));
            addText(reference, "small", "Available for reference; not added");
            selection.appendChild(reference);
            body.appendChild(selection);
        }

        if (item.hasCountingException) {
            const note = document.createElement("div");
            note.className = "pf-note pf-note--warning mt-3";
            const icon = document.createElement("i");
            icon.className = "bi bi-sliders";
            icon.setAttribute("aria-hidden", "true");
            note.appendChild(icon);
            addText(note, "span", "A specific counting rule is configured for this project, source and year.");
            body.appendChild(note);
        }
    }

    function renderEntries(item, body) {
        if (!Array.isArray(item.detailedEntries) || item.detailedEntries.length === 0) {
            const note = document.createElement("p");
            note.className = "text-muted small mt-3 mb-0";
            note.textContent = item.detailedQuantity > 0
                ? "Detailed entries are not included on this page. Open the project for the complete calculation."
                : "No approved detailed entries are recorded for this project-year.";
            body.appendChild(note);
            return;
        }

        const heading = document.createElement("div");
        heading.className = "pf-record-detail-heading";
        addText(heading, "h4", "Detailed entries");
        addText(heading, "span", `${formatter.format(item.detailedEntries.length)} shown`);
        body.appendChild(heading);

        const responsive = document.createElement("div");
        responsive.className = "table-responsive";
        const table = document.createElement("table");
        table.className = "table table-sm align-middle mb-0 pf-detail-table";
        table.innerHTML = "<thead><tr><th>Date</th><th>Receiving unit</th><th class=\"text-end\">Quantity</th><th>Remarks</th></tr></thead>";
        const tbody = document.createElement("tbody");

        item.detailedEntries.forEach(entry => {
            const row = document.createElement("tr");
            addText(row, "td", formatDate(entry.proliferationDate));
            addText(row, "td", entry.unitName || "—");
            addText(row, "td", formatter.format(entry.quantity || 0), "text-end fw-semibold");
            addText(row, "td", entry.remarks || "—", "text-muted");
            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        responsive.appendChild(table);
        body.appendChild(responsive);
    }

    function renderItem(item) {
        const details = document.createElement("details");
        details.className = "pf-record-group";

        const summary = document.createElement("summary");
        const identity = document.createElement("div");
        identity.className = "pf-record-group__identity";
        const title = document.createElement("a");
        title.href = `/ProjectOfficeReports/Proliferation/Project/${encodeURIComponent(item.projectId)}`;
        title.textContent = item.projectName;
        title.addEventListener("click", event => event.stopPropagation());
        identity.appendChild(title);
        const meta = [item.projectCode, item.sourceLabel, item.year].filter(Boolean).join(" · ");
        addText(identity, "small", meta);
        summary.appendChild(identity);

        const metrics = document.createElement("div");
        metrics.className = "pf-record-group__metrics";
        [["Annual", item.annualQuantity], ["Detailed", item.detailedQuantity]].forEach(([label, value]) => {
            const metric = document.createElement("span");
            addText(metric, "small", label);
            addText(metric, "strong", formatter.format(value || 0));
            metrics.appendChild(metric);
        });
        summary.appendChild(metrics);

        const total = document.createElement("div");
        total.className = "pf-record-group__total";
        addText(total, "small", "Reported total");
        addText(total, "strong", formatter.format(item.reportedTotal || 0));
        summary.appendChild(total);

        const chevron = document.createElement("i");
        chevron.className = "bi bi-chevron-down pf-record-group__chevron";
        chevron.setAttribute("aria-hidden", "true");
        summary.appendChild(chevron);
        details.appendChild(summary);

        const body = document.createElement("div");
        body.className = "pf-record-group__body";
        renderCalculation(item, body);
        renderEntries(item, body);

        const actions = document.createElement("div");
        actions.className = "pf-record-group__actions";
        const projectLink = document.createElement("a");
        projectLink.className = "btn btn-sm btn-outline-primary";
        projectLink.href = `/ProjectOfficeReports/Proliferation/Project/${encodeURIComponent(item.projectId)}`;
        projectLink.textContent = "Open project total";
        actions.appendChild(projectLink);
        if (root.dataset.canManageRecords === "true") {
            const manageLink = document.createElement("a");
            manageLink.className = "btn btn-sm btn-outline-secondary";
            manageLink.href = `/ProjectOfficeReports/Proliferation/Manage?projectId=${encodeURIComponent(item.projectId)}&source=${encodeURIComponent(item.source)}&year=${encodeURIComponent(item.year)}`;
            manageLink.textContent = "Manage records";
            actions.appendChild(manageLink);
        }
        body.appendChild(actions);
        details.appendChild(body);
        return details;
    }

    function render(data) {
        el.groups.replaceChildren();
        const items = Array.isArray(data.items) ? data.items : [];
        items.forEach(item => el.groups.appendChild(renderItem(item)));

        state.total = Number(data.total || 0);
        state.page = Number(data.page || state.page);
        if (state.projectId && !el.project.value && items.length > 0) {
            const selectedProject = items.find(item => Number(item.projectId) === Number(state.projectId));
            if (selectedProject) {
                el.project.value = selectedProject.projectCode
                    ? `${selectedProject.projectName} (${selectedProject.projectCode})`
                    : selectedProject.projectName;
            }
        }
        state.pageSize = Number(data.pageSize || state.pageSize);

        const start = state.total === 0 ? 0 : ((state.page - 1) * state.pageSize) + 1;
        const end = Math.min(state.page * state.pageSize, state.total);
        el.summary.textContent = `${formatter.format(state.total)} ${state.total === 1 ? "project-year calculation" : "project-year calculations"}`;
        el.range.textContent = state.total === 0
            ? "No records"
            : `Showing ${formatter.format(start)}–${formatter.format(end)} of ${formatter.format(state.total)}`;
        el.empty.classList.toggle("d-none", items.length !== 0);
        el.prev.disabled = state.page <= 1;
        el.next.disabled = end >= state.total;
    }

    async function load() {
        state.controller?.abort();
        state.controller = new AbortController();
        setLoading(true);
        el.error.classList.add("d-none");

        try {
            const response = await fetch(`${api.groups}?${queryParams()}`, {
                headers: { Accept: "application/json" },
                signal: state.controller.signal
            });
            if (!response.ok) throw new Error(await readError(response));
            render(await response.json());
        } catch (error) {
            if (error.name === "AbortError") return;
            el.groups.replaceChildren();
            el.empty.classList.add("d-none");
            el.error.textContent = error.message || "Unable to load proliferation records.";
            el.error.classList.remove("d-none");
            el.summary.textContent = "Records could not be loaded.";
        } finally {
            setLoading(false);
        }
    }

    function closeSuggestions() {
        el.projectSuggestions.classList.add("d-none");
        el.projectSuggestions.replaceChildren();
    }

    async function searchProjects() {
        const query = el.project.value.trim();
        if (query.length < 2) {
            closeSuggestions();
            return;
        }

        state.projectController?.abort();
        state.projectController = new AbortController();
        try {
            const response = await fetch(`${api.projects}?q=${encodeURIComponent(query)}`, {
                headers: { Accept: "application/json" },
                signal: state.projectController.signal
            });
            if (!response.ok) return;
            const projects = await response.json();
            el.projectSuggestions.replaceChildren();
            projects.forEach(project => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "pf-suggestion";
                button.setAttribute("role", "option");
                addText(button, "strong", project.name);
                if (project.code) addText(button, "small", project.code);
                button.addEventListener("click", () => {
                    state.projectId = project.id;
                    el.projectId.value = project.id;
                    el.project.value = project.display || (project.code ? `${project.name} (${project.code})` : project.name);
                    closeSuggestions();
                    state.page = 1;
                    load();
                });
                el.projectSuggestions.appendChild(button);
            });
            el.projectSuggestions.classList.toggle("d-none", projects.length === 0);
        } catch (error) {
            if (error.name !== "AbortError") closeSuggestions();
        }
    }

    function resetProjectSelection() {
        state.projectId = null;
        el.projectId.value = "";
    }

    const reload = debounce(() => {
        state.page = 1;
        load();
    });

    el.source.addEventListener("change", reload);
    el.year.addEventListener("input", reload);
    el.search.addEventListener("input", reload);
    el.pageSize.addEventListener("change", () => {
        state.pageSize = Number(el.pageSize.value || 25);
        state.page = 1;
        load();
    });
    el.project.addEventListener("input", () => {
        resetProjectSelection();
        searchProjects();
        if (!el.project.value.trim()) reload();
    });
    el.project.addEventListener("blur", () => setTimeout(closeSuggestions, 150));
    el.project.addEventListener("keydown", event => {
        if (event.key !== "Enter") return;
        const first = el.projectSuggestions.querySelector("button");
        if (first) {
            event.preventDefault();
            first.click();
        }
    });
    el.prev.addEventListener("click", () => {
        if (state.page > 1) {
            state.page -= 1;
            load();
            el.groups.scrollIntoView({ behavior: "smooth", block: "start" });
        }
    });
    el.next.addEventListener("click", () => {
        if (state.page * state.pageSize < state.total) {
            state.page += 1;
            load();
            el.groups.scrollIntoView({ behavior: "smooth", block: "start" });
        }
    });
    el.reset.addEventListener("click", () => {
        state.projectId = null;
        state.page = 1;
        el.project.value = "";
        el.projectId.value = "";
        el.source.value = "";
        el.year.value = "";
        el.search.value = "";
        closeSuggestions();
        load();
    });
    el.export.addEventListener("click", () => {
        const params = new URLSearchParams();
        if (state.projectId) params.set("projectId", String(state.projectId));
        if (el.source.value) params.set("source", el.source.value);
        if (el.year.value) params.append("years", el.year.value);
        if (el.search.value.trim()) params.set("search", el.search.value.trim());
        window.location.assign(`${api.export}?${params}`);
    });

    const initial = new URLSearchParams(window.location.search);
    const initialProjectId = Number(initial.get("projectId"));
    if (Number.isInteger(initialProjectId) && initialProjectId > 0) {
        state.projectId = initialProjectId;
        el.projectId.value = String(initialProjectId);
        fetch(`${api.projects}?q=`, { headers: { Accept: "application/json" } })
            .then(response => response.ok ? response.json() : [])
            .then(projects => {
                const match = projects.find(x => x.id === initialProjectId);
                if (match) el.project.value = match.display || match.name;
            })
            .catch(() => {});
    }
    if (initial.get("source")) el.source.value = initial.get("source");
    if (initial.get("year")) el.year.value = initial.get("year");

    load();
})();
