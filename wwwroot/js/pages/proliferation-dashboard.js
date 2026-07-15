"use strict";

(() => {
    const root = document.querySelector('[data-page="proliferation-records"]');
    if (!root) return;

    const api = {
        groups: "/api/proliferation/groups",
        projects: "/api/proliferation/projects",
        entries: projectId => `/api/proliferation/groups/${encodeURIComponent(projectId)}/entries`,
        export: "/api/proliferation/export"
    };

    const formatter = new Intl.NumberFormat();
    const state = {
        page: 1,
        pageSize: 25,
        total: 0,
        projectId: null,
        requestController: null,
        requestSequence: 0,
        picker: null
    };

    const el = {
        project: document.getElementById("pf-record-project"),
        projectId: document.getElementById("pf-record-project-id"),
        projectSuggestions: document.getElementById("pf-record-project-suggestions"),
        projectStatus: document.getElementById("pf-record-project-status"),
        projectClear: document.getElementById("pf-record-project-clear"),
        source: document.getElementById("pf-record-source"),
        year: document.getElementById("pf-record-year"),
        yearStatus: document.getElementById("pf-record-year-status"),
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
            window.clearTimeout(timer);
            timer = window.setTimeout(() => fn(...args), delay);
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

    function validateYear() {
        const raw = el.year.value.trim();
        if (!raw) {
            el.year.classList.remove("is-invalid");
            el.yearStatus.textContent = "";
            return true;
        }
        if (!/^\d{4}$/.test(raw)) {
            el.year.classList.add("is-invalid");
            el.yearStatus.textContent = "Enter a four-digit year, or leave blank for all years.";
            return false;
        }
        const year = Number(raw);
        if (year < 2000 || year > 3000) {
            el.year.classList.add("is-invalid");
            el.yearStatus.textContent = "Year must be between 2000 and 3000.";
            return false;
        }
        el.year.classList.remove("is-invalid");
        el.yearStatus.textContent = "";
        return true;
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

    function setLoading(isLoading) {
        el.loading.classList.toggle("d-none", !isLoading);
        el.groups.setAttribute("aria-busy", String(isLoading));
        el.prev.disabled = isLoading || state.page <= 1;
        const end = Math.min(state.page * state.pageSize, state.total);
        el.next.disabled = isLoading || end >= state.total;
        el.export.disabled = isLoading;
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

    function renderEntriesTable(entries, host) {
        host.replaceChildren();
        if (!Array.isArray(entries) || entries.length === 0) {
            addText(host, "p", "No approved detailed entries are recorded for this project-year.", "text-muted small mb-0");
            return;
        }

        const heading = document.createElement("div");
        heading.className = "pf-record-detail-heading";
        addText(heading, "h4", "Detailed entries");
        addText(heading, "span", `${formatter.format(entries.length)} shown`);
        host.appendChild(heading);

        const responsive = document.createElement("div");
        responsive.className = "table-responsive";
        const table = document.createElement("table");
        table.className = "table table-sm align-middle mb-0 pf-detail-table";
        table.innerHTML = '<thead><tr><th>Date</th><th>Receiving unit</th><th class="text-end">Quantity</th><th>Remarks</th></tr></thead>';
        const tbody = document.createElement("tbody");

        entries.forEach(entry => {
            const row = document.createElement("tr");
            addText(row, "td", formatDate(entry.proliferationDate));
            addText(row, "td", entry.unitName || "—");
            addText(row, "td", formatter.format(entry.quantity || 0), "text-end fw-semibold");
            addText(row, "td", entry.remarks || "—", "text-muted");
            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        responsive.appendChild(table);
        host.appendChild(responsive);
    }

    async function loadEntries(item, host) {
        if (host.dataset.loaded === "true" || host.dataset.loading === "true") return;
        if (Number(item.detailedEntryCount || 0) === 0) {
            host.dataset.loaded = "true";
            renderEntriesTable([], host);
            return;
        }

        host.dataset.loading = "true";
        host.replaceChildren();
        const loading = document.createElement("div");
        loading.className = "d-flex align-items-center gap-2 text-muted small py-2";
        loading.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span>Loading detailed entries…</span>';
        host.appendChild(loading);

        try {
            const params = new URLSearchParams({ source: String(item.source), year: String(item.year) });
            const response = await fetch(`${api.entries(item.projectId)}?${params}`, {
                headers: { Accept: "application/json" },
                credentials: "same-origin"
            });
            if (!response.ok) throw new Error(await readError(response));
            const rows = await response.json();
            host.dataset.loaded = "true";
            renderEntriesTable(rows, host);
        } catch (error) {
            host.replaceChildren();
            const alert = addText(host, "div", error.message || "Unable to load detailed entries.", "alert alert-danger py-2 mb-0");
            alert.setAttribute("role", "alert");
            const retry = document.createElement("button");
            retry.type = "button";
            retry.className = "btn btn-sm btn-link px-0 ms-2";
            retry.textContent = "Try again";
            retry.addEventListener("click", () => {
                delete host.dataset.loading;
                loadEntries(item, host);
            });
            alert.appendChild(retry);
        } finally {
            delete host.dataset.loading;
        }
    }

    function renderItem(item) {
        const details = document.createElement("details");
        const year = Number(item.year);
        const invalidYear = !Number.isInteger(year) || year < 2000 || year > 3000;
        details.className = `pf-record-group${invalidYear ? " pf-record-group--invalid" : ""}`;

        const summary = document.createElement("summary");
        const identity = document.createElement("div");
        identity.className = "pf-record-group__identity";
        const title = document.createElement("a");
        title.href = `/ProjectOfficeReports/Proliferation/Project/${encodeURIComponent(item.projectId)}`;
        title.textContent = item.projectName;
        title.addEventListener("click", event => event.stopPropagation());
        identity.appendChild(title);
        const yearLabel = invalidYear ? `Invalid year: ${item.year}` : item.year;
        const meta = [item.projectCode, item.sourceLabel, yearLabel].filter(Boolean).join(" · ");
        addText(identity, "small", meta, invalidYear ? "text-warning-emphasis fw-semibold" : "");
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

        if (invalidYear) {
            const warning = document.createElement("div");
            warning.className = "alert alert-warning py-2 mt-3 mb-0";
            warning.setAttribute("role", "alert");
            warning.textContent = "This record has an invalid historical year. Its quantity remains included in the reported total until the underlying record is corrected.";
            body.appendChild(warning);
        }

        const entriesHost = document.createElement("div");
        entriesHost.className = "pf-record-entries mt-3";
        body.appendChild(entriesHost);

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
        details.addEventListener("toggle", () => {
            if (!details.open) return;
            if (invalidYear) {
                entriesHost.replaceChildren();
                addText(entriesHost, "p", "Correct the historical date before opening its detailed entries.", "text-muted small mb-0");
                return;
            }
            loadEntries(item, entriesHost);
        });
        return details;
    }

    function render(data) {
        el.groups.replaceChildren();
        const items = Array.isArray(data.items) ? data.items : [];
        items.forEach(item => el.groups.appendChild(renderItem(item)));

        state.total = Number(data.total || 0);
        state.page = Number(data.page || state.page);
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
        if (!validateYear()) return;
        state.requestController?.abort();
        state.requestController = new AbortController();
        const sequence = ++state.requestSequence;
        setLoading(true);
        el.error.classList.add("d-none");

        try {
            const response = await fetch(`${api.groups}?${queryParams()}`, {
                headers: { Accept: "application/json" },
                signal: state.requestController.signal,
                credentials: "same-origin"
            });
            if (!response.ok) throw new Error(await readError(response));
            const data = await response.json();
            if (sequence !== state.requestSequence) return;
            render(data);
        } catch (error) {
            if (error.name === "AbortError" || sequence !== state.requestSequence) return;
            el.groups.replaceChildren();
            el.empty.classList.add("d-none");
            el.error.textContent = error.message || "Unable to load proliferation records.";
            el.error.classList.remove("d-none");
            el.summary.textContent = "Records could not be loaded.";
        } finally {
            if (sequence === state.requestSequence) setLoading(false);
        }
    }

    const reload = debounce(() => {
        state.page = 1;
        load();
    });

    function initializeProjectPicker() {
        if (!window.ProliferationProjectPicker) return;
        state.picker = new window.ProliferationProjectPicker({
            input: el.project,
            hiddenInput: el.projectId,
            suggestions: el.projectSuggestions,
            statusElement: el.projectStatus,
            clearButton: el.projectClear,
            onSelected: project => {
                state.projectId = Number(project.id);
                state.page = 1;
                load();
            },
            onCleared: () => {
                state.projectId = null;
                state.page = 1;
                load();
            }
        });
    }

    el.source.addEventListener("change", reload);
    el.year.addEventListener("input", debounce(() => {
        if (validateYear()) reload();
    }, 350));
    el.search.addEventListener("input", reload);
    el.pageSize.addEventListener("change", () => {
        state.pageSize = Number(el.pageSize.value || 25);
        state.page = 1;
        load();
    });
    el.prev.addEventListener("click", () => {
        if (state.page <= 1) return;
        state.page -= 1;
        load();
        document.getElementById("record-results-heading")?.scrollIntoView({ behavior: "smooth", block: "start" });
    });
    el.next.addEventListener("click", () => {
        if (state.page * state.pageSize >= state.total) return;
        state.page += 1;
        load();
        document.getElementById("record-results-heading")?.scrollIntoView({ behavior: "smooth", block: "start" });
    });
    el.reset.addEventListener("click", () => {
        state.page = 1;
        state.projectId = null;
        state.picker?.clearSelection({ preserveText: false, notify: false });
        el.source.value = "";
        el.year.value = "";
        el.search.value = "";
        validateYear();
        load();
        el.project.focus();
    });
    el.export.addEventListener("click", () => {
        if (!validateYear()) return;
        const params = new URLSearchParams();
        if (state.projectId) params.set("projectId", String(state.projectId));
        if (el.source.value) params.set("source", el.source.value);
        if (el.year.value) params.append("years", el.year.value);
        if (el.search.value.trim()) params.set("search", el.search.value.trim());
        window.location.assign(`${api.export}?${params}`);
    });

    initializeProjectPicker();

    const initial = new URLSearchParams(window.location.search);
    const initialProjectId = Number(initial.get("projectId"));
    if (Number.isInteger(initialProjectId) && initialProjectId > 0) {
        state.projectId = initialProjectId;
        el.projectId.value = String(initialProjectId);
        state.picker?.initializeById(initialProjectId, { notify: false }).then(project => {
            if (project) return;
            state.projectId = null;
            el.projectId.value = "";
            el.projectStatus.textContent = "The linked project is no longer available. Showing all projects.";
            load();
        });
    }
    if (initial.get("source")) el.source.value = initial.get("source");
    if (initial.get("year")) el.year.value = initial.get("year");

    load();
})();
