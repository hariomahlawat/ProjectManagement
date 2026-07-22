(() => {
    "use strict";

    const root = document.querySelector('[data-page="proliferation-analysis"]');
    if (!root) return;

    const endpoints = {
        analysis: root.dataset.analysisEndpoint,
        export: root.dataset.exportEndpoint,
        projects: root.dataset.projectsEndpoint,
        lookups: root.dataset.lookupsEndpoint,
        projectPageBase: root.dataset.projectPageBase
    };

    const minimumYear = Number(root.dataset.minimumYear || 2000);
    const maximumYear = Number(root.dataset.maximumYear || new Date().getUTCFullYear() + 1);
    const currentYear = Number(root.dataset.currentYear || new Date().getUTCFullYear());
    const token = root.querySelector('#pf-analysis-antiforgery input[name="__RequestVerificationToken"]')?.value || "";

    const elements = {
        scopeRadios: Array.from(root.querySelectorAll('input[name="analysisScope"]')),
        scopeChoices: Array.from(root.querySelectorAll('.pf-analysis-choice')),
        periodMode: root.querySelector('#pf-analysis-period-mode'),
        year: root.querySelector('#pf-analysis-year'),
        fromYear: root.querySelector('#pf-analysis-from-year'),
        toYear: root.querySelector('#pf-analysis-to-year'),
        fromDate: root.querySelector('#pf-analysis-from-date'),
        toDate: root.querySelector('#pf-analysis-to-date'),
        singleYearWrap: root.querySelector('#pf-analysis-single-year-wrap'),
        fromYearWrap: root.querySelector('#pf-analysis-from-year-wrap'),
        toYearWrap: root.querySelector('#pf-analysis-to-year-wrap'),
        fromDateWrap: root.querySelector('#pf-analysis-from-date-wrap'),
        toDateWrap: root.querySelector('#pf-analysis-to-date-wrap'),
        dateNote: root.querySelector('#pf-analysis-date-note'),
        scopeOptions: root.querySelector('#pf-analysis-scope-options'),
        categoryWrap: root.querySelector('#pf-analysis-category-wrap'),
        category: root.querySelector('#pf-analysis-category'),
        projectsWrap: root.querySelector('#pf-analysis-projects-wrap'),
        projectSearch: root.querySelector('#pf-analysis-project-search'),
        projectSuggestions: root.querySelector('#pf-analysis-project-suggestions'),
        projectChips: root.querySelector('#pf-analysis-project-chips'),
        projectStatus: root.querySelector('#pf-analysis-project-status'),
        source: root.querySelector('#pf-analysis-source'),
        run: root.querySelector('#pf-analysis-run'),
        clear: root.querySelector('#pf-analysis-clear'),
        export: root.querySelector('#pf-analysis-export'),
        status: root.querySelector('#pf-analysis-status'),
        results: root.querySelector('#pf-analysis-results'),
        edit: root.querySelector('#pf-analysis-edit'),
        resultContext: root.querySelector('#pf-analysis-result-context'),
        total: root.querySelector('#pf-analysis-total'),
        basis: root.querySelector('#pf-analysis-basis'),
        sdd: root.querySelector('#pf-analysis-sdd'),
        abw: root.querySelector('#pf-analysis-abw'),
        projectCount: root.querySelector('#pf-analysis-project-count'),
        unitCount: root.querySelector('#pf-analysis-unit-count'),
        unitCountNote: root.querySelector('#pf-analysis-unit-count-note'),
        coverage: root.querySelector('#pf-analysis-coverage'),
        projectBody: root.querySelector('#pf-analysis-project-body'),
        loadUnits: root.querySelector('#pf-analysis-load-units'),
        unitPlaceholder: root.querySelector('#pf-analysis-unit-placeholder'),
        unitTableWrap: root.querySelector('#pf-analysis-unit-table-wrap'),
        unitBody: root.querySelector('#pf-analysis-unit-body')
    };

    const state = {
        allProjects: [],
        selectedProjects: [],
        suggestionResults: [],
        activeSuggestionIndex: -1,
        projectSearchTimer: 0,
        latestRequest: null,
        latestResult: null,
        unitsLoaded: false,
        requestController: null
    };

    const numberFormatter = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 0 });
    const dateFormatter = new Intl.DateTimeFormat("en-GB", {
        day: "2-digit",
        month: "short",
        year: "numeric"
    });

    function setHidden(element, hidden) {
        element?.classList.toggle("d-none", hidden);
    }

    function setStatus(message, isError = false) {
        if (!elements.status) return;
        elements.status.textContent = message || "";
        elements.status.classList.toggle("is-error", isError);
    }

    function setBusy(isBusy, label = "Generating…") {
        if (!elements.run) return;
        elements.run.disabled = isBusy;
        elements.run.innerHTML = isBusy
            ? `<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> ${escapeHtml(label)}`
            : '<i class="bi bi-play-fill" aria-hidden="true"></i> Generate report';
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function formatNumber(value) {
        const number = Number(value || 0);
        return numberFormatter.format(Number.isFinite(number) ? number : 0);
    }

    function parseIsoDate(value) {
        if (!value) return null;
        const parts = String(value).split("-").map(Number);
        if (parts.length !== 3 || parts.some(part => !Number.isFinite(part))) return null;
        return new Date(Date.UTC(parts[0], parts[1] - 1, parts[2]));
    }

    function formatDate(value) {
        const date = parseIsoDate(value);
        return date ? dateFormatter.format(date) : "—";
    }

    function fillYearSelect(select, selectedYear) {
        if (!select) return;
        select.innerHTML = "";
        for (let year = maximumYear; year >= minimumYear; year -= 1) {
            const option = document.createElement("option");
            option.value = String(year);
            option.textContent = String(year);
            if (year === selectedYear) option.selected = true;
            select.append(option);
        }
    }

    function initialisePeriods() {
        fillYearSelect(elements.year, currentYear);
        fillYearSelect(elements.fromYear, Math.max(minimumYear, currentYear - 4));
        fillYearSelect(elements.toYear, currentYear);

        const minimumDate = `${minimumYear}-01-01`;
        const maximumDate = `${maximumYear}-12-31`;
        for (const input of [elements.fromDate, elements.toDate]) {
            if (!input) continue;
            input.min = minimumDate;
            input.max = maximumDate;
        }
    }

    function selectedScope() {
        return Number(elements.scopeRadios.find(radio => radio.checked)?.value || 1);
    }

    function updateScopeUi() {
        const scope = selectedScope();
        elements.scopeChoices.forEach(choice => {
            const radio = choice.querySelector('input[type="radio"]');
            choice.classList.toggle("is-selected", Boolean(radio?.checked));
        });

        setHidden(elements.scopeOptions, scope === 1);
        setHidden(elements.categoryWrap, scope !== 2);
        setHidden(elements.projectsWrap, scope !== 3);
        invalidateResult();
    }

    function updatePeriodUi() {
        const mode = Number(elements.periodMode?.value || 1);
        setHidden(elements.singleYearWrap, mode !== 2);
        setHidden(elements.fromYearWrap, mode !== 3);
        setHidden(elements.toYearWrap, mode !== 3);
        setHidden(elements.fromDateWrap, mode !== 4);
        setHidden(elements.toDateWrap, mode !== 4);
        setHidden(elements.dateNote, mode !== 4);
        invalidateResult();
    }

    function invalidateResult() {
        if (!state.latestResult) return;
        elements.export.disabled = true;
        setStatus("Report options changed. Generate the report again to update the results.");
    }

    function antiforgeryHeaders() {
        if (!token) return {};
        return {
            "X-CSRF-TOKEN": token,
            "RequestVerificationToken": token
        };
    }

    function ensureAntiforgeryToken() {
        if (token) return;
        throw new Error("The secure report session could not be initialised. Refresh the page and try again.");
    }

    async function fetchJson(url, options = {}) {
        const hasBody = options.body !== undefined && options.body !== null;
        if (hasBody) ensureAntiforgeryToken();

        const response = await fetch(url, {
            credentials: "same-origin",
            ...options,
            headers: {
                Accept: "application/json",
                ...(hasBody ? { "Content-Type": "application/json" } : {}),
                ...(hasBody ? antiforgeryHeaders() : {}),
                ...(options.headers || {})
            }
        });

        if (!response.ok) {
            throw await createSafeError(response);
        }

        if (response.status === 204) return null;

        const contentType = response.headers.get("content-type") || "";
        if (!contentType.includes("json")) {
            throw new Error("The server returned an unexpected response. Refresh the page and try again.");
        }

        return response.json();
    }

    async function createSafeError(response) {
        let payload = null;
        try {
            const contentType = response.headers.get("content-type") || "";
            if (contentType.includes("json")) payload = await response.json();
        } catch {
            payload = null;
        }

        const traceId = payload?.traceId || payload?.extensions?.traceId || null;
        const candidate = payload?.detail || payload?.message || payload?.title || "";
        let message;

        if (candidate && String(candidate).length <= 500) {
            message = String(candidate);
        } else if (response.status === 400) {
            message = "The report request could not be validated. Refresh the page and try again.";
        } else if (response.status === 401) {
            message = "Your session has expired. Sign in again and retry the report.";
        } else if (response.status === 403) {
            message = "You are not authorised to generate this report.";
        } else if (response.status === 404 || response.status === 405) {
            message = "The reporting service is not available. Confirm that the latest report files have been deployed.";
        } else if (response.status >= 500) {
            message = "The report could not be generated because of a server error.";
        } else {
            message = `The report request failed (${response.status}).`;
        }

        if (traceId) message += ` Reference: ${traceId}`;

        const error = new Error(message);
        error.status = response.status;
        error.traceId = traceId;
        return error;
    }

    async function loadLookups() {
        try {
            const payload = await fetchJson(endpoints.lookups);
            const categories = Array.isArray(payload?.technicalCategories)
                ? payload.technicalCategories
                : [];
            for (const category of categories) {
                const option = document.createElement("option");
                option.value = String(category.id);
                option.textContent = category.name;
                elements.category.append(option);
            }
        } catch {
            const option = document.createElement("option");
            option.value = "";
            option.textContent = "Technical categories unavailable";
            elements.category.replaceChildren(option);
            elements.category.disabled = true;
        }
    }

    async function loadProjects() {
        try {
            const payload = await fetchJson(`${endpoints.projects}?take=500`);
            state.allProjects = Array.isArray(payload?.items) ? payload.items : [];
            elements.projectStatus.textContent = state.allProjects.length
                ? `${state.allProjects.length} completed simulators available.`
                : "No eligible simulators are available.";
        } catch {
            state.allProjects = [];
            elements.projectStatus.textContent = "Simulator search is temporarily unavailable.";
            elements.projectStatus.classList.add("text-danger");
        }
    }

    function projectSearchText(project) {
        return [
            project?.name,
            project?.acronym,
            project?.code,
            project?.technicalCategory,
            project?.projectCategory
        ].filter(Boolean).join(" ").toLocaleLowerCase();
    }

    function projectSecondary(project) {
        return [project?.code, project?.technicalCategory || project?.projectCategory]
            .filter(Boolean)
            .join(" · ");
    }

    function showProjectSuggestions() {
        const query = String(elements.projectSearch?.value || "").trim().toLocaleLowerCase();
        const selectedIds = new Set(state.selectedProjects.map(project => Number(project.id)));
        const candidates = state.allProjects
            .filter(project => !selectedIds.has(Number(project.id)))
            .filter(project => !query || projectSearchText(project).includes(query))
            .slice(0, 20);

        state.suggestionResults = candidates;
        state.activeSuggestionIndex = candidates.length ? 0 : -1;
        renderProjectSuggestions();
    }

    function renderProjectSuggestions() {
        if (!elements.projectSuggestions || !elements.projectSearch) return;
        elements.projectSuggestions.innerHTML = "";

        if (!state.suggestionResults.length) {
            const empty = document.createElement("div");
            empty.className = "pf-analysis-project-empty";
            empty.textContent = state.allProjects.length
                ? "No matching simulator."
                : "Simulator search is unavailable.";
            elements.projectSuggestions.append(empty);
        } else {
            state.suggestionResults.forEach((project, index) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = `pf-analysis-project-option${index === state.activeSuggestionIndex ? " is-active" : ""}`;
                button.setAttribute("role", "option");
                button.setAttribute("aria-selected", index === state.activeSuggestionIndex ? "true" : "false");
                button.innerHTML = `
                    <strong>${escapeHtml(project.name)}</strong>
                    <small>${escapeHtml(projectSecondary(project) || "Completed simulator")}</small>`;
                button.addEventListener("pointerdown", event => event.preventDefault());
                button.addEventListener("click", () => selectProject(project));
                elements.projectSuggestions.append(button);
            });
        }

        setHidden(elements.projectSuggestions, false);
        elements.projectSearch.setAttribute("aria-expanded", "true");
    }

    function closeProjectSuggestions() {
        setHidden(elements.projectSuggestions, true);
        elements.projectSearch?.setAttribute("aria-expanded", "false");
        state.activeSuggestionIndex = -1;
    }

    function moveProjectSuggestion(direction) {
        if (!state.suggestionResults.length) return;
        state.activeSuggestionIndex = (state.activeSuggestionIndex + direction + state.suggestionResults.length)
            % state.suggestionResults.length;
        renderProjectSuggestions();
        elements.projectSuggestions
            ?.querySelector('.pf-analysis-project-option.is-active')
            ?.scrollIntoView({ block: "nearest" });
    }

    function selectProject(project) {
        if (!project || state.selectedProjects.some(item => Number(item.id) === Number(project.id))) return;
        state.selectedProjects.push(project);
        state.selectedProjects.sort((a, b) => String(a.name).localeCompare(String(b.name), undefined, { sensitivity: "base" }));
        elements.projectSearch.value = "";
        renderProjectChips();
        closeProjectSuggestions();
        elements.projectSearch.focus();
        invalidateResult();
    }

    function removeProject(projectId) {
        state.selectedProjects = state.selectedProjects.filter(project => Number(project.id) !== Number(projectId));
        renderProjectChips();
        invalidateResult();
    }

    function renderProjectChips() {
        if (!elements.projectChips) return;
        elements.projectChips.innerHTML = "";
        for (const project of state.selectedProjects) {
            const chip = document.createElement("span");
            chip.className = "pf-analysis-project-chip";
            chip.innerHTML = `
                <span title="${escapeHtml(project.name)}">${escapeHtml(project.name)}</span>
                <button type="button" aria-label="Remove ${escapeHtml(project.name)}">
                    <i class="bi bi-x" aria-hidden="true"></i>
                </button>`;
            chip.querySelector("button")?.addEventListener("click", () => removeProject(project.id));
            elements.projectChips.append(chip);
        }

        elements.projectStatus.textContent = state.selectedProjects.length
            ? `${state.selectedProjects.length} ${state.selectedProjects.length === 1 ? "simulator" : "simulators"} selected.`
            : "Select one or more simulators.";
        elements.projectStatus.classList.remove("text-danger");
    }

    function buildRequest(includeUnitBreakdown = false) {
        const scope = selectedScope();
        const periodMode = Number(elements.periodMode?.value || 1);
        const request = {
            scope,
            periodMode,
            technicalCategoryId: scope === 2 ? numberOrNull(elements.category?.value) : null,
            projectIds: scope === 3 ? state.selectedProjects.map(project => Number(project.id)) : [],
            year: periodMode === 2 ? numberOrNull(elements.year?.value) : null,
            fromYear: periodMode === 3 ? numberOrNull(elements.fromYear?.value) : null,
            toYear: periodMode === 3 ? numberOrNull(elements.toYear?.value) : null,
            fromDate: periodMode === 4 ? elements.fromDate?.value || null : null,
            toDate: periodMode === 4 ? elements.toDate?.value || null : null,
            source: numberOrNull(elements.source?.value),
            includeUnitBreakdown
        };

        validateClientRequest(request);
        return request;
    }

    function numberOrNull(value) {
        if (value === null || value === undefined || String(value).trim() === "") return null;
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    }

    function validateClientRequest(request) {
        if (request.scope === 2 && !request.technicalCategoryId) {
            throw new Error("Select a technical category.");
        }

        if (request.scope === 3 && request.projectIds.length === 0) {
            throw new Error("Select at least one simulator.");
        }

        if (request.periodMode === 3 && request.fromYear > request.toYear) {
            throw new Error("The first year must be before or equal to the last year.");
        }

        if (request.periodMode === 4) {
            if (!request.fromDate || !request.toDate) {
                throw new Error("Select both the start and end date.");
            }
            if (request.fromDate > request.toDate) {
                throw new Error("The start date must be before or equal to the end date.");
            }
        }
    }

    async function runReport(options = {}) {
        const includeUnitBreakdown = Boolean(options.includeUnitBreakdown);
        let request;
        try {
            request = buildRequest(includeUnitBreakdown);
        } catch (error) {
            setStatus(error.message, true);
            return null;
        }

        state.requestController?.abort();
        state.requestController = new AbortController();
        setBusy(true, includeUnitBreakdown ? "Loading units…" : "Generating…");
        setStatus(includeUnitBreakdown ? "Loading unit-wise details…" : "Calculating authoritative totals…");

        try {
            const result = await fetchJson(endpoints.analysis, {
                method: "POST",
                body: JSON.stringify(request),
                signal: state.requestController.signal
            });

            state.latestRequest = { ...request, includeUnitBreakdown: false };
            state.latestResult = result;
            state.unitsLoaded = includeUnitBreakdown;
            renderResult(result, includeUnitBreakdown);
            elements.export.disabled = false;
            setStatus("Report generated.");
            setHidden(elements.results, false);

            if (!options.keepPosition) {
                elements.results.scrollIntoView({ behavior: "smooth", block: "start" });
            }
            return result;
        } catch (error) {
            if (error?.name === "AbortError") return null;
            setStatus(error?.message || "The report could not be generated.", true);
            return null;
        } finally {
            setBusy(false);
        }
    }

    function renderResult(result, includeUnits) {
        const summary = result?.summary || {};
        elements.resultContext.textContent = `${result.scopeLabel || "Report"} · ${result.periodLabel || "All time"} · ${result.sourceLabel || "All sources"}`;
        elements.total.textContent = formatNumber(summary.totalProliferation);
        elements.sdd.textContent = formatNumber(summary.sddTotal);
        elements.abw.textContent = formatNumber(summary.abw515Total);
        elements.projectCount.textContent = formatNumber(summary.projectCount);

        const unitDataLoaded = Boolean(summary.unitDataLoaded || includeUnits);
        const hasUnitBreakdown = Boolean(summary.hasUnitBreakdown);
        if (unitDataLoaded) {
            elements.unitCount.textContent = formatNumber(summary.receivingUnitCount);
            if (elements.unitCountNote) {
                elements.unitCountNote.textContent = hasUnitBreakdown
                    ? "From approved detailed entries"
                    : "No unit-level entries";
            }
        } else {
            elements.unitCount.textContent = "—";
            if (elements.unitCountNote) {
                elements.unitCountNote.textContent = hasUnitBreakdown
                    ? "Load unit-wise breakdown"
                    : "Not available in annual records";
            }
        }

        elements.basis.textContent = result.calculationBasis || "";
        elements.coverage.innerHTML = `<i class="bi bi-info-circle" aria-hidden="true"></i><span>${escapeHtml(result.coverageMessage || "")}</span>`;

        renderProjectRows(Array.isArray(result.projects) ? result.projects : []);

        if (unitDataLoaded) {
            renderUnitRows(Array.isArray(result.units) ? result.units : []);
        } else {
            resetUnitSection(hasUnitBreakdown);
        }
    }

    function renderProjectRows(rows) {
        elements.projectBody.innerHTML = "";
        if (!rows.length) {
            elements.projectBody.innerHTML = '<tr><td colspan="5" class="pf-analysis-empty">No approved proliferation was recorded for the selected scope and period.</td></tr>';
            return;
        }

        const fragment = document.createDocumentFragment();
        for (const row of rows) {
            const tr = document.createElement("tr");
            const projectUrl = `${endpoints.projectPageBase}${encodeURIComponent(row.projectId)}`;
            tr.innerHTML = `
                <td>
                    <a class="pf-analysis-project-link" href="${projectUrl}">${escapeHtml(row.projectName)}</a>
                    ${row.projectCode ? `<span class="pf-analysis-project-code">${escapeHtml(row.projectCode)}</span>` : ""}
                </td>
                <td>${escapeHtml(row.technicalCategory || "Not categorised")}</td>
                <td class="text-end pf-analysis-quantity">${formatNumber(row.sddQuantity)}</td>
                <td class="text-end pf-analysis-quantity">${formatNumber(row.abw515Quantity)}</td>
                <td class="text-end pf-analysis-quantity fw-semibold">${formatNumber(row.totalQuantity)}</td>`;
            fragment.append(tr);
        }
        elements.projectBody.append(fragment);
    }

    function resetUnitSection(hasUnitData) {
        setHidden(elements.unitTableWrap, true);
        setHidden(elements.unitPlaceholder, false);
        elements.unitBody.innerHTML = "";
        elements.unitPlaceholder.textContent = hasUnitData
            ? "Unit-wise data is available. Load it only when required."
            : "No approved detailed entries with receiving-unit names are available for this report.";
        elements.loadUnits.disabled = !hasUnitData;
        elements.loadUnits.innerHTML = "Show unit-wise breakdown";
    }

    function renderUnitRows(rows) {
        elements.unitBody.innerHTML = "";
        setHidden(elements.unitPlaceholder, true);
        setHidden(elements.unitTableWrap, false);
        elements.loadUnits.disabled = false;
        elements.loadUnits.innerHTML = "Refresh unit-wise breakdown";

        if (!rows.length) {
            elements.unitBody.innerHTML = '<tr><td colspan="5" class="pf-analysis-empty">No approved detailed entries with receiving-unit names were found.</td></tr>';
            return;
        }

        const fragment = document.createDocumentFragment();
        for (const row of rows) {
            const first = formatDate(row.firstDate);
            const last = formatDate(row.lastDate);
            const period = first === last ? first : `${first} – ${last}`;
            const tr = document.createElement("tr");
            tr.innerHTML = `
                <td class="fw-semibold">${escapeHtml(row.unitName)}</td>
                <td>
                    ${escapeHtml(row.projectName)}
                    ${row.projectCode ? `<span class="pf-analysis-project-code">${escapeHtml(row.projectCode)}</span>` : ""}
                </td>
                <td>${escapeHtml(row.sourceLabel)}</td>
                <td>${escapeHtml(period)}${Number(row.entryCount) > 1 ? `<span class="pf-analysis-project-code">${formatNumber(row.entryCount)} entries</span>` : ""}</td>
                <td class="text-end pf-analysis-quantity fw-semibold">${formatNumber(row.quantity)}</td>`;
            fragment.append(tr);
        }
        elements.unitBody.append(fragment);
    }

    async function exportReport() {
        let request;
        try {
            request = buildRequest(true);
        } catch (error) {
            setStatus(error.message, true);
            return;
        }

        try {
            ensureAntiforgeryToken();
        } catch (error) {
            setStatus(error.message, true);
            return;
        }

        elements.export.disabled = true;
        const originalHtml = elements.export.innerHTML;
        elements.export.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Exporting…';
        setStatus("Preparing Excel workbook…");

        try {
            const response = await fetch(endpoints.export, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Content-Type": "application/json",
                    ...antiforgeryHeaders()
                },
                body: JSON.stringify(request)
            });

            if (!response.ok) throw await createSafeError(response);

            const blob = await response.blob();
            const fileName = getDownloadFileName(response.headers.get("content-disposition"))
                || "proliferation-analysis.xlsx";
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement("a");
            anchor.href = url;
            anchor.download = fileName;
            document.body.append(anchor);
            anchor.click();
            anchor.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 1000);
            setStatus("Excel report exported.");
        } catch (error) {
            setStatus(error?.message || "The Excel report could not be exported.", true);
        } finally {
            elements.export.innerHTML = originalHtml;
            elements.export.disabled = !state.latestResult;
        }
    }

    function getDownloadFileName(contentDisposition) {
        if (!contentDisposition) return null;
        const utfMatch = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
        if (utfMatch) return decodeURIComponent(utfMatch[1].replace(/["']/g, ""));
        const simpleMatch = contentDisposition.match(/filename="?([^";]+)"?/i);
        return simpleMatch ? simpleMatch[1] : null;
    }

    function clearReport() {
        elements.scopeRadios.forEach(radio => { radio.checked = radio.value === "1"; });
        elements.periodMode.value = "1";
        elements.year.value = String(currentYear);
        elements.fromYear.value = String(Math.max(minimumYear, currentYear - 4));
        elements.toYear.value = String(currentYear);
        elements.fromDate.value = "";
        elements.toDate.value = "";
        elements.category.value = "";
        elements.source.value = "";
        elements.projectSearch.value = "";
        state.selectedProjects = [];
        state.latestRequest = null;
        state.latestResult = null;
        state.unitsLoaded = false;
        renderProjectChips();
        updateScopeUi();
        updatePeriodUi();
        setHidden(elements.results, true);
        elements.export.disabled = true;
        setStatus("");
        closeProjectSuggestions();
    }

    function bindEvents() {
        elements.scopeRadios.forEach(radio => radio.addEventListener("change", updateScopeUi));
        elements.periodMode?.addEventListener("change", updatePeriodUi);
        elements.category?.addEventListener("change", invalidateResult);
        elements.source?.addEventListener("change", invalidateResult);
        for (const control of [elements.year, elements.fromYear, elements.toYear, elements.fromDate, elements.toDate]) {
            control?.addEventListener("change", invalidateResult);
        }

        elements.projectSearch?.addEventListener("input", () => {
            window.clearTimeout(state.projectSearchTimer);
            state.projectSearchTimer = window.setTimeout(showProjectSuggestions, 120);
        });
        elements.projectSearch?.addEventListener("focus", showProjectSuggestions);
        elements.projectSearch?.addEventListener("keydown", event => {
            if (event.key === "ArrowDown") {
                event.preventDefault();
                if (elements.projectSuggestions.classList.contains("d-none")) showProjectSuggestions();
                else moveProjectSuggestion(1);
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                moveProjectSuggestion(-1);
            } else if (event.key === "Enter") {
                const project = state.suggestionResults[state.activeSuggestionIndex];
                if (project) {
                    event.preventDefault();
                    selectProject(project);
                }
            } else if (event.key === "Escape") {
                closeProjectSuggestions();
            }
        });

        document.addEventListener("pointerdown", event => {
            if (!elements.projectsWrap?.contains(event.target)) closeProjectSuggestions();
        });

        elements.run?.addEventListener("click", () => runReport());
        elements.clear?.addEventListener("click", clearReport);
        elements.export?.addEventListener("click", exportReport);
        elements.loadUnits?.addEventListener("click", () => runReport({ includeUnitBreakdown: true, keepPosition: true }));
        elements.edit?.addEventListener("click", () => {
            root.querySelector('.pf-analysis-setup')?.scrollIntoView({ behavior: "smooth", block: "start" });
        });
    }

    initialisePeriods();
    bindEvents();
    updateScopeUi();
    updatePeriodUi();
    renderProjectChips();
    Promise.allSettled([loadLookups(), loadProjects()]);
})();
