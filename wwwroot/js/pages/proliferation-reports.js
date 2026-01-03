(() => {
  // SECTION: Constants
  const api = {
    run: "/api/proliferation/reports/run",
    export: "/api/proliferation/reports/export",
    unitSuggestions: "/api/proliferation/reports/unit-suggestions",
    projects: "/api/proliferation/projects",
    lookups: "/api/proliferation/lookups"
  };

  // SECTION: Helpers
  function $(id) { return document.getElementById(id); }
  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  async function fetchJson(url) {
    const res = await fetch(url, { headers: { "Accept": "application/json" } });
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      throw new Error(text || `Request failed: ${res.status}`);
    }
    return await res.json();
  }

  // SECTION: State
  const state = {
    page: 1,
    total: 0,
    columns: [],
    rows: [],
    projectId: null,
    projectLabel: "",
    lastQuery: null
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
    hint: $("rep-hint"),
    count: $("rep-count"),
    head: $("rep-head"),
    body: $("rep-body"),
    prev: $("rep-prev"),
    next: $("rep-next")
  };

  // SECTION: UI behavior
  function reportNeedsProject(kind) {
    return kind === "ProjectToUnits";
  }

  function reportNeedsUnit(kind) {
    return kind === "UnitToProjects";
  }

  function reportUsesDate(kind) {
    return kind !== "YearlyReconciliation";
  }

  function applyKindUI() {
    const kind = el.kind.value;

    el.projectWrap.classList.toggle("d-none", !reportNeedsProject(kind));
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
    el.project.value = "";
    el.unit.value = "";

    el.hint.textContent = kind === "YearlyReconciliation"
      ? "This report compares yearly totals and granular sums and shows the effective total based on preference mode."
      : "Dates and unit names are derived from granular entries only.";
  }

  // SECTION: Lookups
  async function loadLookups() {
    const data = await fetchJson(api.lookups);
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
    const url = `${api.projects}?q=${encodeURIComponent(q)}`;
    const list = await fetchJson(url);

    el.projectSuggest.innerHTML = (list || []).map(p => {
      const label = `${p.name}${p.code ? " (" + p.code + ")" : ""}`;
      return `<button type="button" class="list-group-item list-group-item-action" data-id="${p.id}" data-label="${esc(label)}">${esc(label)}</button>`;
    }).join("");

    el.projectSuggest.classList.toggle("d-none", (list || []).length === 0);
  }

  el.project.addEventListener("input", () => {
    state.projectId = null;
    if (projTimer) window.clearTimeout(projTimer);
    projTimer = window.setTimeout(() => refreshProjectSuggestions().catch(() => {}), 250);
  });

  el.projectSuggest.addEventListener("click", (e) => {
    const btn = e.target.closest("button[data-id]");
    if (!btn) return;
    state.projectId = Number(btn.dataset.id);
    state.projectLabel = btn.dataset.label || "";
    el.project.value = state.projectLabel;
    el.projectSuggest.classList.add("d-none");
  });

  document.addEventListener("click", (e) => {
    if (!el.projectWrap.contains(e.target)) {
      el.projectSuggest.classList.add("d-none");
    }
  });

  // SECTION: Query building
  function buildQuery() {
    const kind = el.kind.value;
    const pageSize = Number(el.pageSize.value) || 50;

    const query = new URLSearchParams();
    query.set("report", kind);
    query.set("page", String(state.page));
    query.set("pageSize", String(pageSize));

    const source = el.source.value.trim();
    if (source) query.set("source", source);

    const status = el.status.value.trim();
    if (status) query.set("approvalStatus", status);

    const projectCategoryId = el.projectCategory.value.trim();
    if (projectCategoryId) query.set("projectCategoryId", projectCategoryId);

    const technicalCategoryId = el.technicalCategory.value.trim();
    if (technicalCategoryId) query.set("technicalCategoryId", technicalCategoryId);

    if (reportNeedsProject(kind) && state.projectId) {
      query.set("projectId", String(state.projectId));
    }

    if (reportNeedsUnit(kind)) {
      const u = el.unit.value.trim();
      if (u) query.set("unitName", u);
    }

    if (reportUsesDate(kind)) {
      const from = el.from.value;
      const to = el.to.value;
      if (from) query.set("fromDateUtc", from);
      if (to) query.set("toDateUtc", to);
    }

    return query.toString();
  }

  // SECTION: Table rendering
  function renderTable(columns, rows) {
    el.head.innerHTML = (columns || []).map(c => `<th scope="col">${esc(c.label)}</th>`).join("");

    el.body.innerHTML = (rows || []).map(r => {
      const tds = (columns || []).map(c => {
        const key = c.key;
        const val = (r && Object.prototype.hasOwnProperty.call(r, key)) ? r[key] : "";
        return `<td>${esc(val ?? "")}</td>`;
      }).join("");
      return `<tr>${tds}</tr>`;
    }).join("");
  }

  // SECTION: Pager
  function updatePager() {
    const pageSize = Number(el.pageSize.value) || 50;
    const total = state.total || 0;

    const from = total === 0 ? 0 : ((state.page - 1) * pageSize + 1);
    const to = Math.min(state.page * pageSize, total);

    el.count.textContent = total === 0 ? "No rows." : `Showing ${from}-${to} of ${total} rows`;

    el.prev.disabled = state.page <= 1;
    el.next.disabled = (state.page * pageSize) >= total;

    el.export.disabled = total === 0;
  }

  // SECTION: Report execution
  async function runReport() {
    const kind = el.kind.value;

    if (reportNeedsProject(kind) && !state.projectId) {
      el.hint.textContent = "Select a project to run this report.";
      return;
    }
    if (reportNeedsUnit(kind) && !el.unit.value.trim()) {
      el.hint.textContent = "Enter a unit name to run this report.";
      return;
    }

    const qs = buildQuery();
    state.lastQuery = qs;

    const url = `${api.run}?${qs}`;
    const result = await fetchJson(url);

    state.columns = result.columns || [];
    state.rows = result.rows || [];
    state.total = result.total || 0;

    renderTable(state.columns, state.rows);
    updatePager();
  }

  function exportExcel() {
    if (!state.lastQuery) return;
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
    if (state.lastQuery) {
      runReport().catch(() => {});
    }
  });

  // SECTION: Init
  applyKindUI();
  loadLookups().catch(() => {
    el.hint.textContent = "Unable to load category filters.";
  });
})();
