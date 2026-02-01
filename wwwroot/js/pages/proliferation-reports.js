(() => {
  // SECTION: Constants
  const api = {
    run: "/api/proliferation/reports/run",
    export: "/api/proliferation/reports/export",
    unitSuggestions: "/api/proliferation/reports/unit-suggestions",
    projects: "/api/proliferation/projects",
    lookups: "/api/proliferation/lookups"
  };

  const reportLabels = {
    ProjectToUnits: "Project to units (detail)",
    UnitToProjects: "Unit to projects (detail)",
    ProjectCoverageSummary: "Project coverage summary",
    GranularLedger: "Granular ledger",
    YearlyReconciliation: "Yearly reconciliation"
  };

  const reportSortKeys = {
    ProjectToUnits: ["projectName", "sourceLabel", "unitName", "proliferationDate", "year", "quantity"],
    UnitToProjects: ["unitName", "projectName", "sourceLabel", "proliferationDate", "year", "quantity"],
    ProjectCoverageSummary: ["projectName", "sourceLabel", "totalQuantity", "uniqueUnits", "firstDate", "lastDate"],
    GranularLedger: ["projectName", "sourceLabel", "proliferationDate", "unitName", "year", "quantity"],
    YearlyReconciliation: ["projectName", "sourceLabel", "year", "yearlyApprovedTotal", "granularApprovedTotal", "preferenceMode"]
  };

  const defaultVisibleColumns = {
    ProjectToUnits: ["projectName", "sourceLabel", "unitName", "proliferationDate", "year", "quantity", "approvalStatus"],
    UnitToProjects: ["unitName", "projectName", "sourceLabel", "proliferationDate", "year", "quantity", "approvalStatus"],
    ProjectCoverageSummary: ["projectName", "sourceLabel", "totalQuantity", "uniqueUnits", "firstDate", "lastDate"],
    GranularLedger: ["projectName", "sourceLabel", "proliferationDate", "unitName", "quantity", "approvalStatus"],
    YearlyReconciliation: ["projectName", "sourceLabel", "year", "yearlyApprovedTotal", "granularApprovedTotal", "preferenceMode", "effectiveTotal"]
  };

  const presetsStorageKey = "prolifReports.presets";

  // SECTION: Helpers
  function $(id) { return document.getElementById(id); }
  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function colsStorageKey(kind) {
    return `prolifReports.cols.${kind}`;
  }

  function getSavedCols(kind) {
    try {
      return JSON.parse(localStorage.getItem(colsStorageKey(kind)) || "null");
    } catch {
      return null;
    }
  }

  function saveCols(kind, keys) {
    localStorage.setItem(colsStorageKey(kind), JSON.stringify(keys));
  }

  function formatDateTime(iso) {
    if (!iso) return "";
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? "" : date.toLocaleString();
  }

  function normalizeSortDir(dir) {
    return String(dir || "").toLowerCase() === "asc" ? "asc" : "desc";
  }

  // SECTION: Fetch helpers
  async function readErrorMessage(response) {
    const contentType = response.headers.get("content-type") || "";
    const text = await response.text().catch(() => "");
    if (contentType.includes("application/json") && text) {
      try {
        const json = JSON.parse(text);
        if (json?.message) return json.message;
      } catch {
        return text;
      }
    }
    return text;
  }

  async function fetchJson(url) {
    const res = await fetch(url, { headers: { "Accept": "application/json" } });
    const contentType = res.headers.get("content-type") || "";
    const isJson = contentType.includes("application/json");

    if (!res.ok) {
      const message = await readErrorMessage(res);
      throw new Error(message || `Request failed: ${res.status}`);
    }

    if (!isJson) {
      await res.text().catch(() => "");
      throw new Error("Unexpected response received from the server. Please refresh the page and try again.");
    }

    try {
      return await res.json();
    } catch {
      throw new Error("Unable to read the server response. Please try again.");
    }
  }

  // SECTION: State
  const root = document.querySelector('[data-page="proliferation-reports"]');
  const canManageRecords = root?.dataset?.canManageRecords === "true";

  const state = {
    page: 1,
    total: 0,
    columns: [],
    allColumns: [],
    rows: [],
    projectId: null,
    projectLabel: "",
    lastQuery: null,
    lastQueryState: null,
    lastRunAtUtc: null,
    sortBy: null,
    sortDir: "desc",
    projectMismatch: false,
    visibleColumnKeys: [],
    lookups: {
      projectCategories: [],
      technicalCategories: []
    }
  };

  // SECTION: Elements
  const el = {
    kind: $("rep-kind"),
    projectWrap: $("rep-project-wrap"),
    project: $("rep-project"),
    projectSuggest: $("rep-project-suggest"),
    unitWrap: $("rep-unit-wrap"),
    unit: $("rep-unit"),
    unitList: $("rep-unit-list"),
    projectCategory: $("rep-project-category"),
    technicalCategory: $("rep-technical-category"),
    source: $("rep-source"),
    status: $("rep-status"),
    from: $("rep-from"),
    to: $("rep-to"),
    pageSize: $("rep-pagesize"),
    run: $("rep-run"),
    export: $("rep-export"),
    columnsButton: $("rep-columns"),
    columnsModalList: $("rep-columns-list"),
    columnsApply: $("rep-columns-apply"),
    columnsReset: $("rep-columns-reset"),
    columnsWarning: $("rep-columns-warning"),
    presets: $("rep-presets"),
    presetLoad: $("rep-preset-load"),
    presetSave: $("rep-preset-save"),
    presetSaveConfirm: $("rep-preset-save-confirm"),
    presetDelete: $("rep-preset-delete"),
    presetName: $("rep-preset-name"),
    hint: $("rep-hint"),
    context: $("reportContext"),
    chips: $("reportChips"),
    clear: $("rep-clear"),
    count: $("rep-count"),
    head: $("rep-head"),
    body: $("rep-body"),
    prev: $("rep-prev"),
    next: $("rep-next")
  };

  // SECTION: UI behavior
  function reportShowsProjectFilter(kind) {
    return kind === "ProjectToUnits";
  }

  function reportRequiresProject(kind) {
    return false;
  }

  function reportSupportsManageActions(kind) {
    return kind === "ProjectToUnits" || kind === "UnitToProjects" || kind === "GranularLedger";
  }

  function reportNeedsUnit(kind) {
    return kind === "UnitToProjects";
  }

  function reportUsesDate(kind) {
    return kind !== "YearlyReconciliation";
  }

  function applyKindUI() {
    const kind = el.kind.value;

    el.projectWrap.classList.toggle("d-none", !reportShowsProjectFilter(kind));
    el.unitWrap.classList.toggle("d-none", !reportNeedsUnit(kind));

    const usesDate = reportUsesDate(kind);
    el.from.disabled = !usesDate;
    el.to.disabled = !usesDate;

    if (!usesDate) {
      el.from.value = "";
      el.to.value = "";
    }

    state.page = 1;
    state.projectId = null;
    state.projectLabel = "";
    state.sortBy = null;
    state.sortDir = "desc";
    state.projectMismatch = false;
    el.project.value = "";
    el.unit.value = "";
    invalidateResults();
    applySavedColumns(kind);
    renderColumnsModal(kind);

    if (kind === "YearlyReconciliation") {
      el.hint.textContent = "This report compares yearly totals and granular sums and shows the effective total based on preference mode.";
      return;
    }

    if (kind === "ProjectToUnits") {
      el.hint.textContent = "Select a project to narrow this report (optional). Dates and unit names are derived from granular entries only.";
      return;
    }

    el.hint.textContent = "Dates and unit names are derived from granular entries only.";
  }

  // SECTION: Lookups
  async function loadLookups() {
    const data = await fetchJson(api.lookups);
    state.lookups.projectCategories = data.projectCategories || [];
    state.lookups.technicalCategories = data.technicalCategories || [];
    const projectOptions = (data.projectCategories || [])
      .map(o => `<option value="${o.id}">${esc(o.name)}</option>`)
      .join("");
    const technicalOptions = (data.technicalCategories || [])
      .map(o => `<option value="${o.id}">${esc(o.name)}</option>`)
      .join("");

    el.projectCategory.innerHTML = `<option value="">All categories</option>${projectOptions}`;
    el.technicalCategory.innerHTML = `<option value="">All categories</option>${technicalOptions}`;
  }

  // SECTION: Unit suggestions
  let unitSuggestTimer = null;
  async function refreshUnitSuggestions() {
    const q = el.unit.value.trim();
    if (q.length < 2) {
      el.unitList.innerHTML = "";
      return;
    }
    const url = `${api.unitSuggestions}?q=${encodeURIComponent(q)}&take=25`;
    const list = await fetchJson(url);
    el.unitList.innerHTML = (list || [])
      .map(x => `<option value="${esc(x)}"></option>`)
      .join("");
  }

  el.unit.addEventListener("input", () => {
    state.page = 1;
    invalidateResults();
    if (unitSuggestTimer) window.clearTimeout(unitSuggestTimer);
    unitSuggestTimer = window.setTimeout(() => refreshUnitSuggestions().catch(() => {}), 250);
  });

  // SECTION: Project suggestions
  let projTimer = null;
  async function refreshProjectSuggestions() {
    const q = el.project.value.trim();
    if (q.length < 2) {
      el.projectSuggest.classList.add("d-none");
      el.projectSuggest.innerHTML = "";
      return;
    }
    const pc = el.projectCategory.value.trim();
    const tc = el.technicalCategory.value.trim();

    const qs = new URLSearchParams();
    qs.set("q", q);
    if (pc) qs.set("projectCategoryId", pc);
    if (tc) qs.set("technicalCategoryId", tc);

    const url = `${api.projects}?${qs.toString()}`;
    const list = await fetchJson(url);

    el.projectSuggest.innerHTML = (list || []).map(p => {
      const label = `${p.name}${p.code ? " (" + p.code + ")" : ""}`;
      return `<button type="button" class="list-group-item list-group-item-action" data-id="${p.id}" data-label="${esc(label)}">${esc(label)}</button>`;
    }).join("");

    el.projectSuggest.classList.toggle("d-none", (list || []).length === 0);
  }

  el.project.addEventListener("input", () => {
    state.page = 1;
    state.projectId = null;
    invalidateResults();
    if (projTimer) window.clearTimeout(projTimer);
    projTimer = window.setTimeout(() => refreshProjectSuggestions().catch(() => {}), 250);
  });

  el.projectSuggest.addEventListener("click", (e) => {
    const btn = e.target.closest("button[data-id]");
    if (!btn) return;
    state.projectId = Number(btn.dataset.id);
    state.projectLabel = btn.dataset.label || "";
    state.projectMismatch = false;
    el.project.value = state.projectLabel;
    el.projectSuggest.classList.add("d-none");
  });

  document.addEventListener("click", (e) => {
    if (!el.projectWrap.contains(e.target)) {
      el.projectSuggest.classList.add("d-none");
    }
  });

  // SECTION: Query building
  function buildQueryState() {
    const kind = el.kind.value;
    const pageSize = Number(el.pageSize.value) || 50;

    return {
      report: kind,
      page: state.page,
      pageSize,
      source: el.source.value.trim() || "",
      approvalStatus: el.status.value.trim() || "",
      projectCategoryId: el.projectCategory.value.trim() || "",
      technicalCategoryId: el.technicalCategory.value.trim() || "",
      projectId: state.projectId,
      projectLabel: state.projectLabel,
      unitName: reportNeedsUnit(kind) ? el.unit.value.trim() : "",
      fromDateUtc: reportUsesDate(kind) ? el.from.value : "",
      toDateUtc: reportUsesDate(kind) ? el.to.value : "",
      sortBy: state.sortBy,
      sortDir: state.sortDir
    };
  }

  function buildFilterState() {
    const q = buildQueryState();
    return {
      source: q.source,
      approvalStatus: q.approvalStatus,
      projectCategoryId: q.projectCategoryId,
      technicalCategoryId: q.technicalCategoryId,
      projectId: q.projectId,
      projectLabel: q.projectLabel,
      unitName: q.unitName,
      fromDateUtc: q.fromDateUtc,
      toDateUtc: q.toDateUtc
    };
  }

  function buildQueryString(q) {
    const query = new URLSearchParams();
    query.set("report", q.report);
    query.set("page", String(q.page));
    query.set("pageSize", String(q.pageSize));

    if (q.source) query.set("source", q.source);
    if (q.approvalStatus) query.set("approvalStatus", q.approvalStatus);
    if (q.projectCategoryId) query.set("projectCategoryId", q.projectCategoryId);
    if (q.technicalCategoryId) query.set("technicalCategoryId", q.technicalCategoryId);
    if (q.projectId) query.set("projectId", String(q.projectId));
    if (q.unitName) query.set("unitName", q.unitName);
    if (q.fromDateUtc) query.set("fromDateUtc", q.fromDateUtc);
    if (q.toDateUtc) query.set("toDateUtc", q.toDateUtc);
    if (q.sortBy) query.set("sortBy", q.sortBy);
    if (q.sortBy && q.sortDir) query.set("sortDir", normalizeSortDir(q.sortDir));

    return query.toString();
  }

  // SECTION: Table rendering
  function getVisibleColumnKeys(kind, columns) {
    const saved = getSavedCols(kind);
    const fallback = defaultVisibleColumns[kind] || [];
    const base = Array.isArray(saved) && saved.length ? saved : fallback;
    const columnKeys = (columns || []).map(c => c.key);
    const filtered = base.filter(key => columnKeys.includes(key));
    return filtered.length ? filtered : columnKeys.filter(key => key !== "__actions");
  }

  function applySavedColumns(kind) {
    state.visibleColumnKeys = [];
    if (!state.allColumns.length) return;
    state.visibleColumnKeys = getVisibleColumnKeys(kind, state.allColumns);
  }

  function renderColumnsModal(kind) {
    if (!el.columnsModalList) return;
    const columns = state.allColumns.filter(c => c.key !== "__actions");
    const selected = new Set(getVisibleColumnKeys(kind, state.allColumns));

    el.columnsModalList.innerHTML = columns.map(c => `
      <div class="col-6 col-md-4">
        <div class="form-check">
          <input class="form-check-input" type="checkbox" id="col-${esc(c.key)}" data-col-key="${esc(c.key)}" ${selected.has(c.key) ? "checked" : ""}>
          <label class="form-check-label" for="col-${esc(c.key)}">${esc(c.label)}</label>
        </div>
      </div>
    `).join("");
  }

  function getSelectedModalColumns() {
    const selected = [...el.columnsModalList.querySelectorAll("input[type='checkbox']:checked")]
      .map(x => x.dataset.colKey)
      .filter(Boolean);
    return selected;
  }

  function sortIndicator(key) {
    if (state.sortBy !== key) return "";
    return state.sortDir === "asc" ? " ▲" : " ▼";
  }

  function isSortable(kind, key) {
    return (reportSortKeys[kind] || []).includes(key);
  }

  function buildHeaderCell(kind, column) {
    const label = esc(column.label);
    if (!isSortable(kind, column.key)) {
      return `<th scope="col">${label}</th>`;
    }

    return `<th scope="col">
      <button type="button" class="btn btn-link p-0 text-decoration-none text-reset rep-sort" data-sort-key="${esc(column.key)}">
        ${label}<span class="ms-1">${esc(sortIndicator(column.key))}</span>
      </button>
    </th>`;
  }

  function buildManageUrl(row) {
    const params = new URLSearchParams();
    if (row && row.projectId) params.set("projectId", String(row.projectId));
    if (row && row.source !== undefined && row.source !== null) params.set("source", String(row.source));
    if (row && row.year) params.set("year", String(row.year));
    params.set("kind", "Granular");
    return `/ProjectOfficeReports/Proliferation/Manage?${params.toString()}`;
  }

  function renderTable(columns, rows) {
    const kind = el.kind.value;
    const visibleKeys = new Set(state.visibleColumnKeys);
    const visibleColumns = (columns || []).filter(c => c.key === "__actions" || visibleKeys.has(c.key));

    el.head.innerHTML = visibleColumns.map(c => buildHeaderCell(kind, c)).join("");
    el.body.innerHTML = "";

    if (!rows || rows.length === 0) {
      const tr = document.createElement("tr");
      const td = document.createElement("td");
      td.colSpan = Math.max(visibleColumns.length, 1);
      td.className = "text-muted py-4";
      td.textContent = "No rows match the current filters.";
      tr.appendChild(td);
      el.body.appendChild(tr);
      return;
    }

    (rows || []).forEach(r => {
      const tr = document.createElement("tr");
      visibleColumns.forEach(c => {
        const td = document.createElement("td");
        if (c.key === "__actions") {
          const url = buildManageUrl(r);
          td.innerHTML = `<a href="${url}" class="btn btn-link btn-sm p-0">Manage</a>`;
        } else {
          const key = c.key;
          const val = (r && Object.prototype.hasOwnProperty.call(r, key)) ? r[key] : "";
          td.textContent = String(val ?? "");
        }
        tr.appendChild(td);
      });
      el.body.appendChild(tr);
    });
  }

  // SECTION: Context + chips
  function addChip(label, style) {
    const span = document.createElement("span");
    span.className = `badge rounded-pill ${style || "text-bg-light border"}`;
    span.textContent = label;
    el.chips.appendChild(span);
  }

  function lookupLabel(options, id) {
    if (!id) return "";
    const match = (options || []).find(o => String(o.id) === String(id));
    return match ? match.name : "";
  }

  function buildChips() {
    el.chips.innerHTML = "";
    if (!state.lastQueryState) return;

    const q = state.lastQueryState;

    addChip(reportLabels[q.report] || q.report, "text-bg-secondary");

    if (q.projectLabel) addChip(`Project: ${q.projectLabel}`, "text-bg-light border");
    if (q.unitName) addChip(`Unit: ${q.unitName}`, "text-bg-light border");
    if (q.source) addChip(`Source: ${q.source === "1" ? "SDD" : "515 ABW"}`, "text-bg-light border");
    if (q.approvalStatus && q.approvalStatus !== "Approved") addChip(`Status: ${q.approvalStatus}`, "text-bg-light border");

    const projectCategoryLabel = lookupLabel(state.lookups.projectCategories, q.projectCategoryId);
    if (projectCategoryLabel) addChip(`Project category: ${projectCategoryLabel}`, "text-bg-light border");

    const technicalCategoryLabel = lookupLabel(state.lookups.technicalCategories, q.technicalCategoryId);
    if (technicalCategoryLabel) addChip(`Technical category: ${technicalCategoryLabel}`, "text-bg-light border");

    if (q.fromDateUtc || q.toDateUtc) {
      const fromLabel = q.fromDateUtc || "Any";
      const toLabel = q.toDateUtc || "Any";
      addChip(`Date: ${fromLabel} to ${toLabel}`, "text-bg-light border");
    }

    if (q.sortBy) {
      const dirLabel = normalizeSortDir(q.sortDir) === "asc" ? "Asc" : "Desc";
      addChip(`Sort: ${q.sortBy} ${dirLabel}`, "text-bg-light border");
    }

    if (state.projectMismatch) {
      addChip("Project does not match selected categories", "text-bg-warning");
    }
  }

  function updateContext() {
    if (!state.lastQueryState) {
      el.context.textContent = "Not run yet.";
      el.chips.innerHTML = "";
      return;
    }

    const runAt = formatDateTime(state.lastRunAtUtc);
    const filtersText = state.lastQueryState ? "Filters: see chips." : "Filters: none.";
    el.context.textContent = `Last run: ${runAt}, Rows: ${state.total}. ${filtersText}`;
    buildChips();
  }

  // SECTION: Column chooser
  function resetColumnsToDefault(kind) {
    const defaults = defaultVisibleColumns[kind] || [];
    saveCols(kind, defaults);
    state.visibleColumnKeys = defaults.slice();
    renderColumnsModal(kind);
    renderTable(state.allColumns, state.rows);
  }

  // SECTION: Presets
  function loadPresets() {
    try {
      const raw = JSON.parse(localStorage.getItem(presetsStorageKey) || "null");
      if (!raw || !Array.isArray(raw.items)) return { version: 1, items: [] };
      return { version: 1, items: raw.items };
    } catch {
      return { version: 1, items: [] };
    }
  }

  function savePresets(presets) {
    localStorage.setItem(presetsStorageKey, JSON.stringify(presets));
  }

  function renderPresetsList() {
    const presets = loadPresets();
    const options = presets.items
      .sort((a, b) => (a.createdUtc || "").localeCompare(b.createdUtc || ""))
      .map(p => `<option value="${esc(p.id)}">${esc(p.name)}</option>`);
    el.presets.innerHTML = `<option value="">Select a preset</option>${options.join("")}`;
    el.presetLoad.disabled = !presets.items.length;
    el.presetDelete.disabled = true;
  }

  function getSelectedPreset() {
    const id = el.presets.value;
    if (!id) return null;
    const presets = loadPresets();
    return presets.items.find(p => p.id === id) || null;
  }

  function generatePresetId() {
    return `preset-${Date.now()}-${Math.floor(Math.random() * 1000)}`;
  }

  async function validateProjectSelection(projectId, projectLabel) {
    if (!projectId || !projectLabel) return true;
    const pc = el.projectCategory.value.trim();
    const tc = el.technicalCategory.value.trim();
    const qs = new URLSearchParams();
    qs.set("q", projectLabel);
    if (pc) qs.set("projectCategoryId", pc);
    if (tc) qs.set("technicalCategoryId", tc);
    const list = await fetchJson(`${api.projects}?${qs.toString()}`);
    return (list || []).some(p => Number(p.id) === Number(projectId));
  }

  function applyPresetState(presetState) {
    el.kind.value = presetState.reportKind || "ProjectToUnits";
    applyKindUI();

    el.source.value = presetState.filters?.source || "";
    el.status.value = presetState.filters?.approvalStatus || "Approved";
    el.projectCategory.value = presetState.filters?.projectCategoryId || "";
    el.technicalCategory.value = presetState.filters?.technicalCategoryId || "";
    el.from.value = presetState.filters?.fromDateUtc || "";
    el.to.value = presetState.filters?.toDateUtc || "";
    el.pageSize.value = String(presetState.pageSize || 50);
    state.sortBy = presetState.sortBy || null;
    state.sortDir = presetState.sortDir || "desc";
    if (state.sortBy && !isSortable(el.kind.value, state.sortBy)) {
      state.sortBy = null;
      state.sortDir = "desc";
    }

    state.projectId = presetState.filters?.projectId || null;
    state.projectLabel = presetState.filters?.projectLabel || "";
    el.project.value = state.projectLabel;

    el.unit.value = presetState.filters?.unitName || "";
    state.page = 1;
    invalidateResults();
  }

  function resetFiltersToDefault() {
    el.kind.value = el.kind.options[0]?.value || "ProjectToUnits";
    el.source.value = "";
    el.status.value = "Approved";
    el.projectCategory.value = "";
    el.technicalCategory.value = "";
    el.from.value = "";
    el.to.value = "";
    el.pageSize.value = "50";
    state.projectId = null;
    state.projectLabel = "";
    el.project.value = "";
    el.unit.value = "";
    state.sortBy = null;
    state.sortDir = "desc";
    el.hint.textContent = "";
    applyKindUI();
  }

  // SECTION: Pager
  function updatePager() {
    const pageSize = Number(el.pageSize.value) || 50;
    const total = state.total || 0;

    const from = total === 0 ? 0 : ((state.page - 1) * pageSize + 1);
    const to = Math.min(state.page * pageSize, total);

    if (total === 0) {
      el.count.innerHTML = state.lastQuery
        ? "No rows match the current filters. <button type=\"button\" class=\"btn btn-link btn-sm p-0\" id=\"rep-reset-link\">Reset filters</button>"
        : "No data loaded.";
    } else {
      el.count.textContent = `Showing ${from}-${to} of ${total} rows`;
    }

    el.prev.disabled = state.page <= 1;
    el.next.disabled = (state.page * pageSize) >= total;

    el.export.disabled = !state.lastQuery || total === 0;
  }

  // SECTION: Table invalidation
  function invalidateResults() {
    state.total = 0;
    state.columns = [];
    state.allColumns = [];
    state.rows = [];
    state.lastQuery = null;
    state.lastQueryState = null;
    state.lastRunAtUtc = null;
    state.projectMismatch = false;
    renderTable([], []);
    updatePager();
    updateContext();
  }

  // SECTION: Report execution
  async function runReport() {
    const kind = el.kind.value;

    if (reportRequiresProject(kind) && !state.projectId) {
      el.hint.textContent = "Select a project to run this report.";
      return;
    }
    if (reportNeedsUnit(kind) && !el.unit.value.trim()) {
      el.hint.textContent = "Enter a unit name to run this report.";
      return;
    }

    if (reportUsesDate(kind)) {
      const from = el.from.value;
      const to = el.to.value;
      if (from && to && from > to) {
        el.hint.textContent = "From date must be on or before To date.";
        return;
      }
    }

    if (state.projectId && (el.projectCategory.value.trim() || el.technicalCategory.value.trim())) {
      try {
        state.projectMismatch = !(await validateProjectSelection(state.projectId, state.projectLabel));
      } catch {
        state.projectMismatch = false;
      }
    } else {
      state.projectMismatch = false;
    }

    const queryState = buildQueryState();
    const qs = buildQueryString(queryState);
    state.lastQuery = qs;
    state.lastQueryState = queryState;

    const url = `${api.run}?${qs}`;
    const result = await fetchJson(url);

    const baseColumns = result.columns || [];
    const columns = reportSupportsManageActions(kind) && canManageRecords
      ? [...baseColumns, { key: "__actions", label: "Actions" }]
      : baseColumns;

    state.allColumns = columns;
    applySavedColumns(kind);
    state.columns = columns;
    state.rows = result.rows || [];
    state.total = result.total || 0;
    state.lastRunAtUtc = new Date().toISOString();

    renderColumnsModal(kind);
    renderTable(state.columns, state.rows);
    updatePager();
    updateContext();
  }

  function exportExcel() {
    if (!state.lastQuery) return;
    if (state.total === 0) {
      el.hint.textContent = "Nothing to export.";
      return;
    }
    const url = `${api.export}?${state.lastQuery}`;
    window.location.href = url;
  }

  // SECTION: Events
  el.run.addEventListener("click", () => {
    runReport().catch(err => {
      el.hint.textContent = err?.message ? `Error: ${err.message}` : "Error running report.";
    });
  });

  el.export.addEventListener("click", exportExcel);

  el.head.addEventListener("click", (e) => {
    const btn = e.target.closest("button[data-sort-key]");
    if (!btn) return;
    const key = btn.dataset.sortKey;
    if (state.sortBy === key) {
      state.sortDir = state.sortDir === "asc" ? "desc" : "asc";
    } else {
      state.sortBy = key;
      state.sortDir = "desc";
    }
    state.page = 1;
    runReport().catch(() => {});
  });

  el.columnsApply.addEventListener("click", () => {
    const kind = el.kind.value;
    const selected = getSelectedModalColumns();
    if (!selected.length) {
      el.columnsWarning.classList.remove("d-none");
      return;
    }
    el.columnsWarning.classList.add("d-none");
    saveCols(kind, selected);
    state.visibleColumnKeys = selected.slice();
    renderTable(state.allColumns, state.rows);
    if (window.bootstrap?.Modal) {
      const modal = window.bootstrap.Modal.getInstance(document.getElementById("rep-columns-modal"));
      modal?.hide();
    }
  });

  el.columnsReset.addEventListener("click", () => {
    const kind = el.kind.value;
    el.columnsWarning.classList.add("d-none");
    resetColumnsToDefault(kind);
  });

  el.clear.addEventListener("click", resetFiltersToDefault);

  el.prev.addEventListener("click", () => {
    if (state.page <= 1) return;
    state.page--;
    runReport().catch(() => {});
  });

  el.next.addEventListener("click", () => {
    const pageSize = Number(el.pageSize.value) || 50;
    if ((state.page * pageSize) >= (state.total || 0)) return;
    state.page++;
    runReport().catch(() => {});
  });

  el.kind.addEventListener("change", applyKindUI);
  el.pageSize.addEventListener("change", () => {
    state.page = 1;
    invalidateResults();
  });

  el.presets.addEventListener("change", () => {
    const hasSelection = Boolean(el.presets.value);
    el.presetLoad.disabled = !hasSelection;
    el.presetDelete.disabled = !hasSelection;
  });

  el.presetLoad.addEventListener("click", async () => {
    const preset = getSelectedPreset();
    if (!preset) return;
    applyPresetState(preset.state || {});
    try {
      const isValid = await validateProjectSelection(state.projectId, state.projectLabel);
      if (!isValid) {
        state.projectId = null;
        state.projectLabel = "";
        el.project.value = "";
        el.hint.textContent = "Saved preset project is not available for the selected categories.";
      }
    } catch {
      el.hint.textContent = "Unable to validate preset project selection.";
    }
  });

  el.presetSaveConfirm.addEventListener("click", () => {
    const name = el.presetName.value.trim();
    if (!name) {
      el.hint.textContent = "Enter a preset name.";
      return;
    }
    const presets = loadPresets();
    const newPreset = {
      id: generatePresetId(),
      name,
      createdUtc: new Date().toISOString(),
      state: {
        reportKind: el.kind.value,
        filters: buildFilterState(),
        sortBy: state.sortBy,
        sortDir: state.sortDir,
        pageSize: Number(el.pageSize.value) || 50
      }
    };
    presets.items.push(newPreset);
    if (presets.items.length > 30) {
      presets.items = presets.items
        .sort((a, b) => (a.createdUtc || "").localeCompare(b.createdUtc || ""))
        .slice(-30);
    }
    savePresets(presets);
    renderPresetsList();
    el.presetName.value = "";
    if (window.bootstrap?.Modal) {
      const modal = window.bootstrap.Modal.getInstance(document.getElementById("rep-preset-modal"));
      modal?.hide();
    }
  });

  el.presetDelete.addEventListener("click", () => {
    const preset = getSelectedPreset();
    if (!preset) return;
    const presets = loadPresets();
    presets.items = presets.items.filter(p => p.id !== preset.id);
    savePresets(presets);
    renderPresetsList();
  });

  // SECTION: Filter change tracking
  function onFilterChanged() {
    state.page = 1;
    invalidateResults();
    state.projectId = null;
    state.projectLabel = "";
    state.projectMismatch = false;
    el.project.value = "";
    el.projectSuggest.classList.add("d-none");
  }

  el.source.addEventListener("change", () => {
    state.page = 1;
    invalidateResults();
  });

  el.status.addEventListener("change", () => {
    state.page = 1;
    invalidateResults();
  });

  el.from.addEventListener("change", () => {
    state.page = 1;
    invalidateResults();
  });

  el.to.addEventListener("change", () => {
    state.page = 1;
    invalidateResults();
  });

  el.projectCategory.addEventListener("change", onFilterChanged);
  el.technicalCategory.addEventListener("change", onFilterChanged);

  el.count.addEventListener("click", (e) => {
    const btn = e.target.closest("#rep-reset-link");
    if (!btn) return;
    resetFiltersToDefault();
  });

  // SECTION: Init
  applyKindUI();
  renderPresetsList();
  loadLookups().catch(() => {
    el.hint.textContent = "Unable to load category filters.";
  });

  const columnsModal = document.getElementById("rep-columns-modal");
  if (columnsModal) {
    columnsModal.addEventListener("show.bs.modal", () => {
      renderColumnsModal(el.kind.value);
      el.columnsWarning.classList.add("d-none");
    });
  }
})();
