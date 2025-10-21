/* global bootstrap */
(() => {
  const api = {
    overview: "/api/proliferation/overview",
    export: "/api/proliferation/export",
    setPref: "/api/proliferation/year-preference",
    getPref: "/api/proliferation/year-preference",
    projects: "/api/proliferation/projects",
    lookups: "/api/proliferation/lookups"
  };

  const sourceLabels = new Map([
    [1, "SDD"],
    [2, "515 ABW"]
  ]);

  function formatSourceLabel(value) {
    if (value === null || value === undefined) return "";
    if (typeof value === "number") {
      return sourceLabels.get(value) ?? value.toString();
    }
    const text = String(value).trim();
    if (!text) return "";
    const numeric = Number(text);
    if (Number.isFinite(numeric)) {
      return sourceLabels.get(numeric) ?? numeric.toString();
    }
    const canonical = text.replace(/\s+/g, "").toLowerCase();
    if (canonical === "sdd") return sourceLabels.get(1) ?? "SDD";
    if (canonical === "abw515" || canonical === "515abw") return sourceLabels.get(2) ?? "515 ABW";
    return text;
  }

  function $(sel, root = document) { return root.querySelector(sel); }
  function $all(sel, root = document) { return [...root.querySelectorAll(sel)]; }

  function toast(message, variant = "success") {
    if (!message) return;
    const host = $("#toastHost");
    if (!host) return;
    const wrapper = document.createElement("div");
    wrapper.className = `toast align-items-center text-bg-${variant} border-0`;
    wrapper.role = "status";
    wrapper.innerHTML = `
      <div class="d-flex">
        <div class="toast-body">${message}</div>
        <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
      </div>`;
    host.append(wrapper);
    const inst = bootstrap.Toast.getOrCreateInstance(wrapper, { delay: 3500 });
    wrapper.addEventListener("hidden.bs.toast", () => wrapper.remove(), { once: true });
    inst.show();
  }

  const fmt = {
    date: (value) => {
      if (!value) return "—";
      const d = new Date(value);
      if (Number.isNaN(d.getTime())) return String(value);
      return d.toISOString().slice(0, 10);
    },
    number: (value) => {
      if (value === null || value === undefined) return "0";
      return Number(value).toLocaleString();
    },
    dateTime: (value) => {
      if (!value) return "—";
      const d = new Date(value);
      if (Number.isNaN(d.getTime())) return String(value);
      try {
        return new Intl.DateTimeFormat(undefined, {
          year: "numeric",
          month: "short",
          day: "2-digit",
          hour: "2-digit",
          minute: "2-digit"
        }).format(d);
      } catch (error) {
        return d.toISOString().replace("T", " ").slice(0, 16);
      }
    }
  };

  function escapeAttr(value) {
    if (value === null || value === undefined) return "";
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/"/g, "&quot;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  function escapeHtml(value) {
    if (value === null || value === undefined) return "";
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  const filterState = {
    projectId: "",
    projectLabel: "",
    sourceId: null,
    sourceLabel: "",
    type: "",
    year: "",
    search: "",
    page: 1,
    pageSize: 50
  };

  const filterStorageConfig = {
    key: "proliferation-dashboard-filter-state",
    version: 1
  };

  function getSessionStorage() {
    try {
      return window.sessionStorage;
    } catch {
      return null;
    }
  }

  function persistFilterState() {
    const storage = getSessionStorage();
    if (!storage) return;
    const payload = {
      version: filterStorageConfig.version,
      state: {
        projectId: filterState.projectId || "",
        projectLabel: filterState.projectLabel || "",
        sourceId: Number.isFinite(filterState.sourceId) ? filterState.sourceId : null,
        type: filterState.type || "",
        year: filterState.year || "",
        search: filterState.search || "",
        page: Number.isFinite(filterState.page) && filterState.page > 0 ? filterState.page : 1,
        pageSize: Number.isFinite(filterState.pageSize) && filterState.pageSize > 0 ? filterState.pageSize : 50
      }
    };
    try {
      storage.setItem(filterStorageConfig.key, JSON.stringify(payload));
    } catch {
      // ignore write failures
    }
  }

  function restoreFilterStateFromStorage() {
    const storage = getSessionStorage();
    if (!storage) return;
    let raw;
    try {
      raw = storage.getItem(filterStorageConfig.key);
    } catch {
      return;
    }
    if (!raw) return;
    try {
      const parsed = JSON.parse(raw);
      const version = parsed?.version ?? parsed?.v;
      if (version !== filterStorageConfig.version) {
        storage.removeItem(filterStorageConfig.key);
        return;
      }
      const state = parsed?.state;
      if (!state || typeof state !== "object") {
        storage.removeItem(filterStorageConfig.key);
        return;
      }
      const sanitizeString = (value) => (typeof value === "string" ? value : "");
      const sanitizeNumber = (value, { allowZero = false } = {}) => {
        const numeric = Number(value);
        if (!Number.isFinite(numeric)) return null;
        if (!allowZero && numeric <= 0) return null;
        return numeric;
      };

      const restored = {
        projectId: sanitizeString(state.projectId),
        projectLabel: sanitizeString(state.projectLabel),
        sourceId: sanitizeNumber(state.sourceId, { allowZero: false }),
        type: sanitizeString(state.type),
        year: sanitizeString(state.year),
        search: sanitizeString(state.search),
        page: sanitizeNumber(state.page, { allowZero: false }) ?? 1,
        pageSize: sanitizeNumber(state.pageSize, { allowZero: false }) ?? 50
      };

      filterState.projectId = restored.projectId;
      filterState.projectLabel = restored.projectLabel;
      filterState.sourceId = restored.sourceId;
      filterState.sourceLabel = formatSourceLabel(filterState.sourceId);
      filterState.type = restored.type;
      filterState.year = restored.year;
      filterState.search = restored.search;
      filterState.page = restored.page;
      filterState.pageSize = restored.pageSize;

      const projectSelect = $("#pf-filter-project");
      if (projectSelect) {
        projectSelect.value = filterState.projectId || "";
      }
      const sourceSelect = $("#pf-filter-source");
      if (sourceSelect) {
        sourceSelect.value = Number.isFinite(filterState.sourceId) ? String(filterState.sourceId) : "";
      }
      const typeSelect = $("#pf-filter-type");
      if (typeSelect) {
        typeSelect.value = filterState.type || "";
      }
      const yearInput = $("#pf-filter-year");
      if (yearInput) {
        yearInput.value = filterState.year || "";
      }
      const searchInput = $("#pf-filter-search");
      if (searchInput) {
        searchInput.value = filterState.search || "";
      }

      renderChips();
      updateFilterSummary();
      persistFilterState();
    } catch (error) {
      console.warn("Ignoring stored proliferation filters", error);
      try {
        storage.removeItem(filterStorageConfig.key);
      } catch {
        // ignore
      }
    }
  }

  const exportState = {
    mode: "years",
    years: [],
    fromDate: "",
    toDate: "",
    yearStart: "",
    yearEnd: ""
  };

  const defaultPreferenceMode = "UseYearlyAndGranular";
  const abwSourceId = 2;
  const preferenceLabels = new Map([
    ["UseYearlyAndGranular", "Use yearly + granular"],
    ["Auto", "Auto"],
    ["UseYearly", "Use yearly"],
    ["UseGranular", "Use granular"]
  ]);
  const lookupCache = new Map();
  const lookups = { projectCategories: [], technicalCategories: [] };
  let projectOptions = [];
  let currentProjectLookupKey = null;
  let preferenceFetchAbort = null;
  let exportModalInstance = null;
  const mixedCoverage = new Map();
  const manageNavigation = {
    url: "/ProjectOfficeReports/Proliferation/Manage",
    filtersKey: "proliferation-manage-filters",
    overridesKey: "proliferation-manage-preference-overrides",
    anchor: "pf-overrides-card"
  };
  let tableInteractionsBound = false;

  function collectFilters() {
    const projectSelect = $("#pf-filter-project");
    const sourceSelect = $("#pf-filter-source");
    const typeSelect = $("#pf-filter-type");
    const yearInput = $("#pf-filter-year");
    const searchInput = $("#pf-filter-search");

    const projectValue = projectSelect?.value ?? "";
    filterState.projectId = projectValue;
    if (projectSelect) {
      const label = projectSelect.options[projectSelect.selectedIndex]?.textContent?.trim();
      filterState.projectLabel = projectValue ? label || "Selected project" : "";
    } else {
      filterState.projectLabel = "";
    }

    const sourceRaw = sourceSelect?.value ?? "";
    if (sourceRaw) {
      const parsed = Number(sourceRaw);
      filterState.sourceId = Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } else {
      filterState.sourceId = null;
    }
    filterState.sourceLabel = formatSourceLabel(filterState.sourceId);
    filterState.type = typeSelect?.value ?? "";
    filterState.year = (yearInput?.value ?? "").trim();
    filterState.search = (searchInput?.value ?? "").trim();

    renderChips();
    updateFilterSummary();

    const yearNumber = parseInt(filterState.year, 10);
    const sourceValue = filterState.sourceId;
    const projectNumber = Number(filterState.projectId);

    const filters = {
      Years: Number.isFinite(yearNumber) ? [yearNumber] : [],
      Source: sourceValue,
      ProjectId: Number.isFinite(projectNumber) && projectNumber > 0 ? projectNumber : null,
      Kind: filterState.type || null,
      Search: filterState.search || null,
      Page: filterState.page,
      PageSize: filterState.pageSize
    };

    persistFilterState();
    return filters;
  }

  function buildFilterSummary() {
    const projectLabel = (filterState.projectLabel || "").trim();
    let projectPart = projectLabel;
    if (!projectPart && filterState.projectId) {
      projectPart = `Project ${filterState.projectId}`;
    }
    if (!projectPart) {
      projectPart = "All projects";
    }

    const sourceLabel = (filterState.sourceLabel || "").trim();
    let sourcePart = sourceLabel;
    if (!sourcePart && Number.isFinite(filterState.sourceId)) {
      sourcePart = formatSourceLabel(filterState.sourceId);
    }
    if (!sourcePart) {
      sourcePart = "All sources";
    }

    const typeRaw = (filterState.type || "").trim();
    const normalizedType = typeRaw.toLowerCase();
    let typePart = "";
    if (normalizedType) {
      if (normalizedType === "granular") {
        typePart = "Granular data";
      } else if (normalizedType === "yearly") {
        typePart = "Yearly data";
      } else {
        typePart = typeRaw.charAt(0).toUpperCase() + typeRaw.slice(1);
      }
    }

    const yearRaw = (filterState.year || "").trim();
    const yearPart = yearRaw || "All years";

    const searchRaw = (filterState.search || "").trim();

    const parts = [projectPart, sourcePart];
    if (typePart) {
      parts.push(typePart);
    }
    parts.push(yearPart);
    if (searchRaw) {
      parts.push(`Search “${searchRaw}”`);
    }

    return parts.join(" • ");
  }

  function updateFilterSummary() {
    const host = $("#pf-filter-summary");
    if (!host) return;
    host.textContent = buildFilterSummary();
  }

  function renderChips() {
    const host = $("#pf-filter-chips");
    if (!host) return;
    host.innerHTML = "";
    const chips = [];
    if (filterState.projectId && filterState.projectLabel) {
      chips.push({ key: "project", label: "Project", value: filterState.projectLabel });
    }
    if (filterState.sourceId && filterState.sourceLabel) {
      chips.push({ key: "source", label: "Source", value: filterState.sourceLabel });
    }
    if (filterState.type) {
      const typeLabel = filterState.type === "granular" ? "Granular" : filterState.type === "yearly" ? "Yearly" : filterState.type;
      chips.push({ key: "type", label: "Type", value: typeLabel });
    }
    if (filterState.year) {
      chips.push({ key: "year", label: "Year", value: filterState.year });
    }
    if (filterState.search) {
      chips.push({ key: "search", label: "Search", value: `"${filterState.search}"` });
    }

    for (const chip of chips) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "btn btn-sm btn-outline-secondary d-inline-flex align-items-center gap-2";
      button.dataset.filterKey = chip.key;
      const labelSpan = document.createElement("span");
      labelSpan.textContent = `${chip.label}: ${chip.value}`;
      const closeSpan = document.createElement("span");
      closeSpan.setAttribute("aria-hidden", "true");
      closeSpan.textContent = "×";
      const srSpan = document.createElement("span");
      srSpan.className = "visually-hidden";
      srSpan.textContent = `Remove ${chip.label.toLowerCase()} filter`;
      button.append(labelSpan, closeSpan, srSpan);
      host.append(button);
    }
  }

  function setExportMode(mode, { preserveValues = false } = {}) {
    const allowedModes = new Set(["all", "years", "yearRange", "range"]);
    const effectiveMode = allowedModes.has(mode) ? mode : "all";
    exportState.mode = effectiveMode;
    const yearsWrap = $("#expYearsWrap");
    const yearFromWrap = $("#expYearFromWrap");
    const yearToWrap = $("#expYearToWrap");
    const fromWrap = $("#expFromWrap");
    const toWrap = $("#expToWrap");
    const form = $("#proliferationExportForm");

    const showYears = effectiveMode === "years";
    const showYearRange = effectiveMode === "yearRange";
    const showDateRange = effectiveMode === "range";

    yearsWrap?.classList.toggle("d-none", !showYears);
    yearFromWrap?.classList.toggle("d-none", !showYearRange);
    yearToWrap?.classList.toggle("d-none", !showYearRange);
    fromWrap?.classList.toggle("d-none", !showDateRange);
    toWrap?.classList.toggle("d-none", !showDateRange);

    if (!preserveValues) {
      const yearsSelect = $("#expYears");
      const yearFromSelect = $("#expYearFrom");
      const yearToSelect = $("#expYearTo");
      const fromInput = form?.querySelector("#expFrom");
      const toInput = form?.querySelector("#expTo");

      if (showYears) {
        if (yearFromSelect) yearFromSelect.value = "";
        if (yearToSelect) yearToSelect.value = "";
        if (fromInput) fromInput.value = "";
        if (toInput) toInput.value = "";
      } else if (showYearRange) {
        if (yearsSelect) {
          $all("option", yearsSelect).forEach((opt) => { opt.selected = false; });
        }
        if (fromInput) fromInput.value = "";
        if (toInput) toInput.value = "";
      } else if (showDateRange) {
        if (yearsSelect) {
          $all("option", yearsSelect).forEach((opt) => { opt.selected = false; });
        }
        if (yearFromSelect) yearFromSelect.value = "";
        if (yearToSelect) yearToSelect.value = "";
      } else {
        if (yearsSelect) {
          $all("option", yearsSelect).forEach((opt) => { opt.selected = false; });
        }
        if (yearFromSelect) yearFromSelect.value = "";
        if (yearToSelect) yearToSelect.value = "";
        if (fromInput) fromInput.value = "";
        if (toInput) toInput.value = "";
      }
    }

    if (effectiveMode === "all") {
      const validation = $("#expValidationMessage");
      if (validation) {
        validation.classList.add("d-none");
        validation.textContent = "";
      }
    }
  }

  function populateExportModal() {
    const modal = $("#proliferationExportModal");
    if (!modal) return;
    const form = $("#proliferationExportForm");
    form?.classList.remove("was-validated");
    const validation = $("#expValidationMessage");
    if (validation) {
      validation.classList.add("d-none");
      validation.textContent = "";
    }

    const modeAllRadio = $("#expModeAll");
    const modeYearsRadio = $("#expModeYears");
    const modeYearRangeRadio = $("#expModeYearRange");
    const modeRangeRadio = $("#expModeRange");

    const hasExportYears = exportState.years.length > 0;
    const hasFilterYears = Boolean(filterState.year);
    const hasAnyYears = hasExportYears || hasFilterYears;
    const hasYearRange = Boolean(exportState.yearStart && exportState.yearEnd);
    const hasAnyRange = Boolean(exportState.fromDate || exportState.toDate);

    const allowedModes = new Set(["all", "years", "yearRange", "range"]);
    let mode = allowedModes.has(exportState.mode) ? exportState.mode : null;

    if (mode === "yearRange" && !hasYearRange) mode = null;
    if (mode === "range" && !hasAnyRange) mode = null;
    if (mode === "years" && !hasAnyYears) mode = null;
    if (mode === "all" && (hasAnyYears || hasYearRange || hasAnyRange)) mode = null;

    if (!mode) {
      if (hasYearRange) {
        mode = "yearRange";
      } else if (hasAnyYears) {
        mode = "years";
      } else if (hasAnyRange) {
        mode = "range";
      } else {
        mode = "all";
      }
    }

    if (modeAllRadio) modeAllRadio.checked = mode === "all";
    if (modeYearsRadio) modeYearsRadio.checked = mode === "years";
    if (modeYearRangeRadio) modeYearRangeRadio.checked = mode === "yearRange";
    if (modeRangeRadio) modeRangeRadio.checked = mode === "range";
    setExportMode(mode, { preserveValues: true });

    const yearsSelect = $("#expYears");
    if (yearsSelect) {
      const prefillYears = (hasExportYears ? exportState.years : (filterState.year ? [filterState.year] : [])) || [];
      const selectedYears = new Set(prefillYears.map((y) => String(y)));
      $all("option", yearsSelect).forEach((opt) => {
        opt.selected = selectedYears.has(opt.value);
      });
    }

    const yearFromSelect = $("#expYearFrom");
    const yearToSelect = $("#expYearTo");
    if (yearFromSelect) {
      yearFromSelect.value = exportState.yearStart || "";
    }
    if (yearToSelect) {
      yearToSelect.value = exportState.yearEnd || "";
    }

    const fromInput = $("#expFrom");
    const toInput = $("#expTo");
    if (fromInput) {
      fromInput.value = exportState.fromDate || "";
    }
    if (toInput) {
      toInput.value = exportState.toDate || "";
    }

    const sourceSelect = $("#expSource");
    if (sourceSelect) {
      sourceSelect.value = filterState.sourceId ? String(filterState.sourceId) : "";
    }

    const searchInput = $("#expSearch");
    if (searchInput) {
      searchInput.value = filterState.search || "";
    }
  }

  function buildExportQueryFromForm() {
    const params = new URLSearchParams();
    const modeControl = document.querySelector("input[name='ExportFilterMode']:checked");
    const mode = modeControl?.value ?? "all";
    const yearsSelect = $("#expYears");
    const fromInput = $("#expFrom");
    const toInput = $("#expTo");
    const yearFromSelect = $("#expYearFrom");
    const yearToSelect = $("#expYearTo");
    const sourceSelect = $("#expSource");
    const projectSelect = $("#expProjectCat");
    const techSelect = $("#expTechCat");
    const searchInput = $("#expSearch");

    const allowedModes = new Set(["all", "years", "range", "yearRange"]);
    const effectiveMode = allowedModes.has(mode) ? mode : "all";

    if (effectiveMode === "all") {
      exportState.years = [];
      exportState.yearStart = "";
      exportState.yearEnd = "";
      exportState.fromDate = "";
      exportState.toDate = "";
    } else if (effectiveMode === "years" && yearsSelect) {
      const selectedYears = $all("option:checked", yearsSelect)
        .map((opt) => opt.value)
        .filter((value) => value);
      selectedYears.forEach((value) => params.append("Years", value));
      exportState.years = selectedYears;
      exportState.yearStart = "";
      exportState.yearEnd = "";
      exportState.fromDate = "";
      exportState.toDate = "";
    } else if (effectiveMode === "range") {
      const fromValue = fromInput?.value ?? "";
      const toValue = toInput?.value ?? "";
      if (fromValue) params.set("FromDateUtc", fromValue);
      if (toValue) params.set("ToDateUtc", toValue);
      exportState.years = [];
      exportState.yearStart = "";
      exportState.yearEnd = "";
      exportState.fromDate = fromValue;
      exportState.toDate = toValue;
    } else if (effectiveMode === "yearRange") {
      const startValue = yearFromSelect?.value ?? "";
      const endValue = yearToSelect?.value ?? "";
      exportState.years = [];
      exportState.fromDate = "";
      exportState.toDate = "";
      exportState.yearStart = startValue;
      exportState.yearEnd = endValue;
      const startYear = parseInt(startValue, 10);
      const endYear = parseInt(endValue, 10);
      if (Number.isFinite(startYear) && Number.isFinite(endYear) && startYear <= endYear) {
        for (let year = startYear; year <= endYear; year += 1) {
          params.append("Years", String(year));
        }
      }
    }

    exportState.mode = effectiveMode;

    const sourceVal = sourceSelect?.value ?? "";
    if (sourceVal) params.set("Source", sourceVal);

    const projectVal = projectSelect?.value ?? "";
    if (projectVal) params.set("ProjectCategoryId", projectVal);

    const techVal = techSelect?.value ?? "";
    if (techVal) params.set("TechnicalCategoryId", techVal);

    const searchVal = (searchInput?.value ?? "").trim();
    if (searchVal) params.set("Search", searchVal);

    params.set("Page", "1");
    params.set("PageSize", "0");
    return params;
  }

  function parseContentDisposition(value) {
    if (!value) return null;
    const match = /filename\*=UTF-8''([^;]+)/i.exec(value);
    if (match && match[1]) {
      return decodeURIComponent(match[1]);
    }
    const fallback = /filename="?([^";]+)"?/i.exec(value);
    return fallback && fallback[1] ? fallback[1] : null;
  }

  async function submitExportRequest() {
    const form = $("#proliferationExportForm");
    if (!form) return;
    const validation = $("#expValidationMessage");
    const modeControl = document.querySelector("input[name='ExportFilterMode']:checked");
    const mode = modeControl?.value ?? "all";
    const yearsSelect = $("#expYears");
    const fromInput = $("#expFrom");
    const toInput = $("#expTo");
    const yearFromSelect = $("#expYearFrom");
    const yearToSelect = $("#expYearTo");
    const confirmBtn = $("#confirmExportBtn");
    const spinner = confirmBtn?.querySelector(".spinner-border");

    const selectedYears = yearsSelect ? $all("option:checked", yearsSelect) : [];
    const fromVal = fromInput?.value ?? "";
    const toVal = toInput?.value ?? "";
    const yearFromVal = yearFromSelect?.value ?? "";
    const yearToVal = yearToSelect?.value ?? "";

    const errors = [];
    const allowedModes = new Set(["all", "years", "range", "yearRange"]);
    const effectiveMode = allowedModes.has(mode) ? mode : "all";

    if (effectiveMode === "years") {
      if (selectedYears.length === 0) {
        errors.push("Select at least one year, choose a range, or export everything.");
      }
    } else if (effectiveMode === "range") {
      if (!fromVal && !toVal) {
        errors.push("Provide a from or to date when exporting by range.");
      }
      if (fromVal && toVal && fromVal > toVal) {
        errors.push("The date range is invalid.");
      }
    } else if (effectiveMode === "yearRange") {
      if (!yearFromVal || !yearToVal) {
        errors.push("Select both a start and end year for the export range.");
      } else {
        const startYear = parseInt(yearFromVal, 10);
        const endYear = parseInt(yearToVal, 10);
        if (Number.isFinite(startYear) && Number.isFinite(endYear) && startYear > endYear) {
          errors.push("The year range is invalid. Ensure the start year is not after the end year.");
        }
      }
    }

    if (validation) {
      if (errors.length) {
        validation.textContent = errors.join(" ");
        validation.classList.remove("d-none");
      } else {
        validation.classList.add("d-none");
        validation.textContent = "";
      }
    }

    if (errors.length) {
      return;
    }

    const params = buildExportQueryFromForm();
    try {
      if (spinner) spinner.classList.remove("d-none");
      if (confirmBtn) confirmBtn.disabled = true;
      const response = await fetch(`${api.export}?${params.toString()}`, {
        headers: { Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
      });
      if (!response.ok) {
        let message = "Unable to generate export.";
        try {
          const text = await response.text();
          if (text) message = text;
        } catch {
          // ignore
        }
        if (validation) {
          validation.textContent = message;
          validation.classList.remove("d-none");
        }
        toast(message, response.status === 400 ? "warning" : "danger");
        return;
      }

      const blob = await response.blob();
      if (blob.size === 0) {
        toast("The export did not contain any rows.", "warning");
        return;
      }

      const disposition = response.headers.get("Content-Disposition");
      const fileName = parseContentDisposition(disposition) || "proliferation-export.xlsx";
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = fileName;
      document.body.append(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      toast("Preparing download…", "success");
      if (exportModalInstance) {
        exportModalInstance.hide();
      }
    } catch (error) {
      console.warn("Export request failed", error);
      const message = "Unable to generate export. Please try again later.";
      if (validation) {
        validation.textContent = message;
        validation.classList.remove("d-none");
      }
      toast(message, "danger");
    } finally {
      if (spinner) spinner.classList.add("d-none");
      if (confirmBtn) confirmBtn.disabled = false;
    }
  }

  function renderKpis(kpis) {
    const data = kpis || {};
    const map = new Map([
      ["#kpiAllTimeCompletedProjects", data.TotalCompletedProjects ?? data.totalCompletedProjects],
      ["#kpiAllTimeEffective", data.TotalProliferationAllTime ?? data.totalProliferationAllTime],
      ["#kpiLastYearProjects", data.LastYearProjectsProliferated ?? data.lastYearProjectsProliferated],
      ["#kpiLastYearTotal", data.LastYearTotalProliferation ?? data.lastYearTotalProliferation]
    ]);
    map.forEach((value, selector) => {
      const el = $(selector);
      if (el) el.textContent = fmt.number(value);
    });
    const stamp = new Date();
    const ts = $("#kpiLastUpdated");
    if (ts) ts.textContent = `Updated ${stamp.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
  }

  function updateEntrySummary(totalCount, page, pageSize) {
    const badge = $("#entryListCount");
    const summary = $("#entryPagingSummary");
    const container = $("#entryPagination");
    const safePage = Number.isFinite(page) && page > 0 ? page : 1;
    const safeSize = Number.isFinite(pageSize) && pageSize > 0 ? pageSize : (totalCount || 1);
    const start = totalCount === 0 ? 0 : (safePage - 1) * safeSize + 1;
    const end = totalCount === 0 ? 0 : Math.min(totalCount, safePage * safeSize);
    const rangeText = totalCount === 0
      ? "No records to display"
      : `Showing ${start.toLocaleString()}–${end.toLocaleString()} of ${totalCount.toLocaleString()} records`;

    if (badge) {
      if (totalCount === 0) {
        badge.hidden = true;
        badge.textContent = "";
      } else {
        badge.hidden = false;
        badge.textContent = rangeText;
      }
    }

    if (summary) {
      summary.textContent = rangeText;
      summary.hidden = false;
    }

    if (container) {
      container.hidden = totalCount === 0;
    }
  }

  function updateSelectionSummary(selection) {
    const host = $("#entrySelectionSummary") || null;
    const placeholder = host?.querySelector("[data-selection-empty]") || $("#entrySelectionEmpty") || null;
    const detailSection = host?.querySelector("[data-selection-details]") || $("#entrySelectionDetails") || null;
    const fields = {
      project: host?.querySelector("[data-selection-project]") || $("[data-selection-project]") || null,
      source: host?.querySelector("[data-selection-source]") || $("[data-selection-source]") || null,
      dataType: host?.querySelector("[data-selection-data-type]") || $("[data-selection-data-type]") || null,
      year: host?.querySelector("[data-selection-year]") || $("[data-selection-year]") || null,
      date: host?.querySelector("[data-selection-date]") || $("[data-selection-date]") || null,
      quantity: host?.querySelector("[data-selection-quantity]") || $("[data-selection-quantity]") || null,
      unit: host?.querySelector("[data-selection-unit]") || $("[data-selection-unit]") || null,
      mode: host?.querySelector("[data-selection-mode]") || $("[data-selection-mode]") || null,
      summary: host?.querySelector("[data-selection-summary]") || $("[data-selection-summary]") || null
    };

    const knownNodes = [host, placeholder, detailSection, ...Object.values(fields)].filter(Boolean);
    if (knownNodes.length === 0) return;

    const clearFields = () => {
      Object.values(fields).forEach((node) => {
        if (node) {
          if (node.tagName === "INPUT" || node.tagName === "TEXTAREA") {
            node.value = "";
          } else {
            node.textContent = "";
          }
        }
      });
    };

    const normalizeSelection = (value) => {
      if (!value) return null;
      if (value instanceof Element) {
        const { dataset } = value;
        const parseQuantity = (input) => {
          if (input === null || input === undefined || input === "") return null;
          const numeric = Number(input);
          return Number.isFinite(numeric) ? numeric : input;
        };
        return {
          project: dataset.entryProject ?? value.getAttribute("data-entry-project") ?? "",
          source: dataset.entrySource ?? value.getAttribute("data-entry-source") ?? "",
          dataType: dataset.entryType ?? value.getAttribute("data-entry-type") ?? "",
          year: dataset.entryYear ?? value.getAttribute("data-entry-year") ?? "",
          date: dataset.entryDate ?? value.getAttribute("data-entry-date") ?? "",
          quantity: parseQuantity(dataset.entryQuantity ?? value.getAttribute("data-entry-quantity")),
          unit: dataset.entryUnit ?? value.getAttribute("data-entry-unit") ?? "",
          mode: dataset.entryMode ?? value.getAttribute("data-entry-mode") ?? ""
        };
      }
      if (typeof value === "object") {
        return {
          project: value.project ?? value.Project ?? "",
          source: value.source ?? value.Source ?? "",
          dataType: value.dataType ?? value.DataType ?? "",
          year: value.year ?? value.Year ?? "",
          date: value.date ?? value.Date ?? value.dateUtc ?? value.DateUtc ?? "",
          quantity: value.quantity ?? value.Quantity ?? null,
          unit: value.unit ?? value.Unit ?? value.unitName ?? value.UnitName ?? "",
          mode: value.mode ?? value.Mode ?? ""
        };
      }
      return null;
    };

    const normalized = normalizeSelection(selection);

    if (!normalized) {
      if (host) host.hidden = true;
      if (detailSection) detailSection.hidden = true;
      if (placeholder) placeholder.hidden = false;
      clearFields();
      return;
    }

    if (host) host.hidden = false;
    if (placeholder) placeholder.hidden = true;
    if (detailSection) detailSection.hidden = false;

    clearFields();

    if (fields.project) fields.project.textContent = normalized.project || "";
    if (fields.source) fields.source.textContent = normalized.source ? formatSourceLabel(normalized.source) : "";
    if (fields.dataType) fields.dataType.textContent = normalized.dataType || "";
    if (fields.year) fields.year.textContent = normalized.year || "";
    if (fields.date) fields.date.textContent = normalized.date ? fmt.date(normalized.date) : "";
    if (fields.mode) fields.mode.textContent = normalized.mode || "";

    const quantityValue = normalized.quantity;
    if (fields.quantity) {
      if (quantityValue === null || quantityValue === undefined || quantityValue === "") {
        fields.quantity.textContent = "";
      } else if (typeof quantityValue === "number") {
        fields.quantity.textContent = fmt.number(quantityValue);
      } else {
        fields.quantity.textContent = String(quantityValue);
      }
    }

    if (fields.unit) fields.unit.textContent = normalized.unit || "";

    if (fields.summary) {
      const parts = [];
      if (normalized.project) parts.push(normalized.project);
      const sourceLabel = normalized.source ? formatSourceLabel(normalized.source) : "";
      if (sourceLabel) parts.push(sourceLabel);
      if (normalized.year) parts.push(`Year ${normalized.year}`);
      if (normalized.dataType) parts.push(normalized.dataType);
      fields.summary.textContent = parts.join(" · ");
    }
  }

  function renderPagination(totalCount, page, pageSize) {
    const pager = $("#entryPager");
    if (!pager) return;
    const safePage = Number.isFinite(page) && page > 0 ? page : 1;
    const safeSize = Number.isFinite(pageSize) && pageSize > 0 ? pageSize : (totalCount || 1);
    const totalPages = safeSize > 0 ? Math.max(1, Math.ceil(totalCount / safeSize)) : 1;

    if (totalCount === 0 || totalPages <= 1) {
      pager.innerHTML = "";
      return;
    }

    const prevPage = Math.max(1, safePage - 1);
    const nextPage = Math.min(totalPages, safePage + 1);
    const prevDisabled = safePage <= 1;
    const nextDisabled = safePage >= totalPages;

    pager.innerHTML = `
      <li class="page-item ${prevDisabled ? "disabled" : ""}">
        <button type="button" class="page-link" data-page="${prevPage}" ${prevDisabled ? "disabled" : ""}>Previous</button>
      </li>
      <li class="page-item disabled"><span class="page-link">Page ${safePage} of ${totalPages}</span></li>
      <li class="page-item ${nextDisabled ? "disabled" : ""}">
        <button type="button" class="page-link" data-page="${nextPage}" ${nextDisabled ? "disabled" : ""}>Next</button>
      </li>`;
  }

  function wirePagination() {
    const pager = $("#entryPager");
    if (!pager) return;
    pager.addEventListener("click", (event) => {
      const button = event.target.closest("button[data-page]");
      if (!button || button.disabled) return;
      const targetPage = Number(button.dataset.page);
      if (!Number.isFinite(targetPage) || targetPage < 1 || targetPage === filterState.page) return;
      filterState.page = targetPage;
      persistFilterState();
      refresh();
    });
  }

  function buildMixedKey(projectId, sourceId, year) {
    return `${projectId ?? ""}|${sourceId ?? ""}|${year ?? ""}`;
  }

  function updateMixedCoverage(rows) {
    mixedCoverage.clear();
    if (!Array.isArray(rows)) {
      updateMixedIndicator();
      return;
    }

    for (const row of rows) {
      const projectId = Number(row.ProjectId ?? row.projectId);
      const sourceId = Number(row.Source ?? row.source);
      const year = Number(row.Year ?? row.year);
      if (!Number.isFinite(projectId) || !Number.isFinite(sourceId) || !Number.isFinite(year)) {
        continue;
      }
      const dataType = String(row.DataType ?? row.dataType ?? "").toLowerCase();
      if (!dataType) continue;
      const key = buildMixedKey(projectId, sourceId, year);
      const entry = mixedCoverage.get(key) ?? { yearly: false, granular: false };
      if (dataType.includes("year")) {
        entry.yearly = true;
      } else if (dataType.includes("granular")) {
        entry.granular = true;
      }
      mixedCoverage.set(key, entry);
    }

    updateMixedIndicator();
  }

  function updateMixedIndicator() {
    const indicator = $("#prefOverrideHint");
    if (!indicator) return;
    const projectId = Number($("#prefProjectId")?.value);
    const sourceId = Number($("#prefSource")?.value);
    const year = Number($("#prefYear")?.value);
    const valid = Number.isFinite(projectId) && projectId > 0 &&
      Number.isFinite(sourceId) && sourceId > 0 &&
      Number.isFinite(year);
    if (!valid) {
      indicator.hidden = true;
      return;
    }

    const coverage = mixedCoverage.get(buildMixedKey(projectId, sourceId, year));
    const hasMixed = Boolean(coverage?.yearly && coverage?.granular);
    indicator.hidden = !hasMixed;
  }

  function persistManageNavigation(projectId, sourceId, year, kind) {
    const projectNumeric = Number(projectId);
    const sourceNumeric = Number(sourceId);
    const yearNumeric = Number(year);
    const projectValue = Number.isFinite(projectNumeric) && projectNumeric > 0 ? String(projectNumeric) : "";
    const sourceValue = Number.isFinite(sourceNumeric) && sourceNumeric > 0 ? String(sourceNumeric) : "";
    const yearValue = Number.isFinite(yearNumeric) ? String(yearNumeric) : "";
    const kindValue = typeof kind === "string" && (kind.toLowerCase() === "yearly" || kind.toLowerCase() === "granular")
      ? kind.toLowerCase()
      : "";

    try {
      const rawFilters = sessionStorage.getItem(manageNavigation.filtersKey);
      const existingFilters = rawFilters ? JSON.parse(rawFilters) : {};
      const payload = existingFilters && typeof existingFilters === "object"
        ? { ...existingFilters }
        : {};
      payload.projectId = projectValue;
      payload.source = sourceValue;
      payload.year = yearValue;
      payload.kind = kindValue;
      sessionStorage.setItem(manageNavigation.filtersKey, JSON.stringify(payload));
    } catch (error) {
      // ignore persistence issues
    }

    try {
      const rawOverrides = sessionStorage.getItem(manageNavigation.overridesKey);
      const existingOverrides = rawOverrides ? JSON.parse(rawOverrides) : {};
      const overridesPayload = existingOverrides && typeof existingOverrides === "object"
        ? { ...existingOverrides }
        : {};
      overridesPayload.projectId = projectValue;
      overridesPayload.source = sourceValue;
      overridesPayload.year = yearValue;
      overridesPayload.search = "";
      overridesPayload.collapsed = false;
      sessionStorage.setItem(manageNavigation.overridesKey, JSON.stringify(overridesPayload));
    } catch (error) {
      // ignore persistence issues
    }
  }

  function navigateToManage(projectId, sourceId, year, kind, href) {
    const projectNumeric = Number(projectId);
    const sourceNumeric = Number(sourceId);
    const yearNumeric = Number(year);
    const kindText = typeof kind === "string" ? kind.toLowerCase() : "";
    const kindValue = kindText === "yearly" || kindText === "granular" ? kindText : "";
    if (!Number.isFinite(projectNumeric) || projectNumeric <= 0 ||
        !Number.isFinite(sourceNumeric) || sourceNumeric <= 0 ||
        !Number.isFinite(yearNumeric)) {
      return;
    }

    persistManageNavigation(projectNumeric, sourceNumeric, yearNumeric, kindValue);

    if (href) {
      window.location.href = href;
      return;
    }

    const params = new URLSearchParams();
    params.set('projectId', String(projectNumeric));
    params.set('source', String(sourceNumeric));
    params.set('year', String(yearNumeric));
    if (kindValue) {
      params.set('kind', kindValue);
    }

    const base = manageNavigation.url;
    const anchorId = manageNavigation.anchor ? manageNavigation.anchor.replace(/^#/, '') : '';
    const query = params.toString();
    let destination = query ? `${base}?${query}` : base;
    if (anchorId) {
      destination = `${destination}#${anchorId}`;
    }
    window.location.href = destination;
  }

  function renderPreferenceBadge(projectId, sourceId, year, mode) {
    const projectNumeric = Number(projectId);
    const sourceNumeric = Number(sourceId);
    const yearNumeric = Number(year);
    const hasContext = Number.isFinite(projectNumeric) && projectNumeric > 0 &&
      Number.isFinite(sourceNumeric) && sourceNumeric > 0 &&
      Number.isFinite(yearNumeric);

    const normalizedMode = typeof mode === "string" && mode ? mode : mode && typeof mode.toString === "function" ? mode.toString() : "";
    const modeLabel = formatPreferenceLabel(normalizedMode || null, Number.isFinite(sourceNumeric) ? sourceNumeric : null) || "—";
    const isOverride = Boolean(normalizedMode && normalizedMode !== "UseYearlyAndGranular");
    const statusText = isOverride ? "Override" : "Default";
    const summary = `${statusText}: ${modeLabel}`;

    const linkKind = normalizedMode === "UseYearly"
      ? "yearly"
      : normalizedMode === "UseGranular"
        ? "granular"
        : "";

    if (!hasContext) {
      return {
        html: `<span class="pf-pref-badge pf-pref-badge--${isOverride ? "override" : "default"} pf-pref-badge--static" aria-disabled="true"><span class="pf-pref-badge__status">${escapeHtml(statusText)}</span><span class="pf-pref-badge__mode">${escapeHtml(modeLabel)}</span></span>`,
        summary
      };
    }

    const params = new URLSearchParams();
    params.set('projectId', String(projectNumeric));
    params.set('source', String(sourceNumeric));
    params.set('year', String(yearNumeric));
    if (linkKind) {
      params.set('kind', linkKind);
    }

    const anchorId = manageNavigation.anchor ? manageNavigation.anchor.replace(/^#/, "") : "";
    let href = manageNavigation.url;
    const query = params.toString();
    if (query) {
      href = `${href}?${query}`;
    }
    if (anchorId) {
      href = `${href}#${anchorId}`;
    }
    const ariaLabel = `${statusText} preference — ${modeLabel}. Open proliferation manager.`;

    const attrs = [
      'data-pref-badge="true"',
      `data-pref-project="${escapeAttr(String(projectNumeric))}"`,
      `data-pref-source="${escapeAttr(String(sourceNumeric))}"`,
      `data-pref-year="${escapeAttr(String(yearNumeric))}"`,
      `data-pref-kind="${escapeAttr(linkKind)}"`
    ].join(" ");

    const html = `<a href="${escapeAttr(href)}" class="pf-pref-badge ${isOverride ? "pf-pref-badge--override" : "pf-pref-badge--default"}" ${attrs} title="${escapeAttr(ariaLabel)}" aria-label="${escapeAttr(ariaLabel)}"><span class="pf-pref-badge__status">${escapeHtml(statusText)}</span><span class="pf-pref-badge__mode">${escapeHtml(modeLabel)}</span></a>`;
    return { html, summary };
  }

  function handleTableInteraction(event) {
    const badge = event.target.closest('[data-pref-badge]');
    if (!badge) return;
    event.preventDefault();
    const projectId = Number(badge.getAttribute('data-pref-project'));
    const sourceId = Number(badge.getAttribute('data-pref-source'));
    const year = Number(badge.getAttribute('data-pref-year'));
    const kind = badge.getAttribute('data-pref-kind') || '';
    const href = badge.getAttribute('href');
    navigateToManage(projectId, sourceId, year, kind, href);
  }

  function renderTable(rows) {
    const host = $("#tableContainer");
    if (!host) return;

    updateMixedCoverage(rows);

    if (!rows || !rows.length) {
      host.innerHTML = `<div class="alert alert-light border d-flex align-items-center" role="status">
        <div class="me-2" aria-hidden="true">ℹ️</div>
        <div>No records match the current filters. Try adjusting filters or reset.</div>
      </div>`;
      host.removeAttribute("aria-busy");
      updateSelectionSummary(null);
      return;
    }

    const header = `
      <thead class="table-light">
        <tr>
          <th scope="col">Project</th>
          <th scope="col">Source</th>
          <th scope="col" class="text-nowrap">Year</th>
          <th scope="col" class="text-nowrap">Date</th>
          <th scope="col">Unit</th>
          <th scope="col" class="text-end text-nowrap">Quantity</th>
          <th scope="col" class="text-end text-nowrap">Effective total</th>
          <th scope="col">Preference</th>
          <th scope="col" class="text-nowrap">Last updated</th>
          <th scope="col">Data type</th>
          <th scope="col">Approval</th>
        </tr>
      </thead>`;

    const body = rows.map((row) => {
      const projectId = row.ProjectId ?? row.projectId ?? row.ProjectID ?? row.projectID;
      const project = row.Project ?? row.ProjectName ?? row.project ?? row.projectName ?? "";
      const sourceRaw = row.Source ?? row.source ?? row.SourceValue ?? row.sourceValue ?? "";
      const sourceLabel = formatSourceLabel(sourceRaw);
      let sourceId = Number(sourceRaw);
      if (!Number.isFinite(sourceId)) {
        const canonicalSource = String(sourceRaw).trim().toLowerCase();
        if (canonicalSource === "sdd") {
          sourceId = 1;
        } else if (canonicalSource === "515 abw" || canonicalSource === "abw515" || canonicalSource === "515abw") {
          sourceId = 2;
        }
      }
      const typeLabel = row.DataType ?? row.dataType ?? "";
      const pane = typeof typeLabel === "string" && typeLabel.toLowerCase().includes("year") ? "yearly" : "granular";
      const unit = row.UnitName ?? row.unitName ?? "";
      const dateRaw = row.DateUtc ?? row.dateUtc ?? row.ProliferationDate ?? row.proliferationDate ?? "";
      const quantityRaw = row.Quantity ?? row.quantity ?? 0;
      const quantityValue = Number(quantityRaw);
      const quantity = Number.isFinite(quantityValue) ? quantityValue : Number(quantityRaw) || 0;
      const effectiveRaw = row.EffectiveTotal ?? row.effectiveTotal ?? quantity;
      const effectiveValue = Number(effectiveRaw);
      const effective = Number.isFinite(effectiveValue) ? effectiveValue : quantity;
      const approval = row.ApprovalStatus ?? row.approvalStatus ?? "";
      const modeRaw = row.Mode ?? row.mode ?? null;
      const year = row.Year ?? row.year ?? "";
      const lastUpdatedRaw = row.LastUpdatedOnUtc ?? row.lastUpdatedOnUtc ??
        row.LastUpdatedUtc ?? row.lastUpdatedUtc ??
        row.LastModifiedOnUtc ?? row.lastModifiedOnUtc ??
        row.LastUpdated ?? row.lastUpdated ?? "";
      const preferenceInfo = renderPreferenceBadge(projectId, sourceId, year, modeRaw);
      const preferenceCell = preferenceInfo?.html ?? "—";
      const preferenceSummary = preferenceInfo?.summary ?? "";
      const attrs = [
        `data-entry-type="${pane}"`,
        `data-entry-project="${escapeAttr(project)}"`,
        `data-entry-source="${escapeAttr(sourceLabel)}"`,
        `data-entry-year="${escapeAttr(year)}"`,
        `data-entry-date="${escapeAttr(dateRaw)}"`,
        `data-entry-quantity="${escapeAttr(quantity)}"`,
        `data-entry-unit="${escapeAttr(unit)}"`,
        `data-entry-mode="${escapeAttr(preferenceSummary)}"`
      ].join(" ");
      return `
      <tr ${attrs}>
        <td class="table-proliferation__project">${project}</td>
        <td>${sourceLabel}</td>
        <td class="text-nowrap">${year || "—"}</td>
        <td class="text-nowrap">${fmt.date(dateRaw)}</td>
        <td>${unit || "—"}</td>
        <td class="text-end text-nowrap">${fmt.number(quantity)}</td>
        <td class="text-end text-nowrap">${fmt.number(effective)}</td>
        <td class="pf-pref-cell">${preferenceCell}</td>
        <td class="text-nowrap">${fmt.dateTime(lastUpdatedRaw)}</td>
        <td>${typeLabel}</td>
        <td>${approval}</td>
      </tr>`;
    }).join("");

    host.innerHTML = `<div class="table-responsive">
      <table class="table table-hover align-middle mb-0 table-proliferation">${header}<tbody>${body}</tbody></table>
    </div>`;
    host.setAttribute("aria-busy", "false");
    if (!tableInteractionsBound) {
      host.addEventListener("click", handleTableInteraction);
      tableInteractionsBound = true;
    }
  }

  function buildQuery(filters) {
    const params = new URLSearchParams();
    if (filters.Years?.length) filters.Years.forEach((y) => params.append("Years", y));
    if (filters.Source !== null && filters.Source !== undefined) {
      params.set("Source", filters.Source);
    }
    if (filters.ProjectId) params.set("ProjectId", filters.ProjectId);
    if (filters.Kind) params.set("Kind", filters.Kind);
    if (filters.Search) params.set("Search", filters.Search);
    params.set("Page", filters.Page ?? 1);
    params.set("PageSize", filters.PageSize ?? 50);
    return `?${params.toString()}`;
  }

  async function fetchOverview(filters, attempt = 1) {
    const host = $("#tableContainer");
    host?.setAttribute("aria-busy", "true");
    try {
      const response = await fetch(api.overview + buildQuery(filters), { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } catch (error) {
      if (attempt < 2) {
        await new Promise((resolve) => setTimeout(resolve, 400 * attempt));
        return fetchOverview(filters, attempt + 1);
      }
      throw error;
    }
  }

  async function refresh() {
    const host = $("#tableContainer");
    if (host) {
      host.innerHTML = `<div class="placeholder-glow">
        <div class="placeholder col-12" style="height: 140px;"></div>
      </div>`;
      host.setAttribute("aria-busy", "true");
    }
    try {
      const filters = collectFilters();
      const data = await fetchOverview(filters);
      renderKpis(data.Kpis ?? data.kpis);
      const totalCount = Number(data.TotalCount ?? data.totalCount ?? 0);
      const page = Number(data.Page ?? data.page ?? filters.Page ?? filterState.page);
      const pageSize = Number(data.PageSize ?? data.pageSize ?? filterState.pageSize);
      filterState.page = Number.isFinite(page) && page > 0 ? page : 1;
      filterState.pageSize = Number.isFinite(pageSize) && pageSize > 0 ? pageSize : filterState.pageSize;
      updateEntrySummary(totalCount, filterState.page, filterState.pageSize);
      renderPagination(totalCount, filterState.page, filterState.pageSize);
      const rows = data.Rows ?? data.rows ?? [];
      renderTable(rows);

      const lookupKey = buildProjectLookupKey("");
      if (lookupKey !== currentProjectLookupKey) {
        try {
          const options = await loadProjects("");
          currentProjectLookupKey = lookupKey;
          populateProjectControls(options.map((item) => ({
            id: item.id ?? item.Id ?? item.projectId,
            display: item.display ?? `${item.name ?? item.Name}${item.code ? ` (${item.code})` : ""}`
          })));
        } catch (error) {
          console.warn("Unable to refresh project options", error);
        }
      }
      persistFilterState();
    } catch (error) {
      console.warn("Failed to load overview", error);
      if (host) {
        host.innerHTML = `<div class="alert alert-danger" role="alert">
          Unable to load proliferation overview. Please try again later.
        </div>`;
        host.setAttribute("aria-busy", "false");
      }
      mixedCoverage.clear();
      updateMixedIndicator();
      updateEntrySummary(0, filterState.page, filterState.pageSize);
      renderPagination(0, filterState.page, filterState.pageSize);
      toast("Unable to load proliferation overview. Please try again later.", "danger");
      persistFilterState();
    }
  }

  function clearFilter(key) {
    const projectSelect = $("#pf-filter-project");
    const sourceSelect = $("#pf-filter-source");
    const typeSelect = $("#pf-filter-type");
    const yearInput = $("#pf-filter-year");
    const searchInput = $("#pf-filter-search");

    switch (key) {
      case "project":
        if (projectSelect) projectSelect.value = "";
        break;
      case "source":
        if (sourceSelect) sourceSelect.value = "";
        break;
      case "type":
        if (typeSelect) typeSelect.value = "";
        break;
      case "year":
        if (yearInput) yearInput.value = "";
        break;
      case "search":
        if (searchInput) searchInput.value = "";
        break;
      default:
        break;
    }
  }

  function handleFilterChange({ preservePage = false } = {}) {
    if (!preservePage) {
      filterState.page = 1;
    }
    collectFilters();
    refresh();
  }

  function wireFilters() {
    const projectSelect = $("#pf-filter-project");
    const sourceSelect = $("#pf-filter-source");
    const typeSelect = $("#pf-filter-type");
    const yearInput = $("#pf-filter-year");
    const searchInput = $("#pf-filter-search");
    const resetButton = $("#pf-filter-reset");
    const chipsHost = $("#pf-filter-chips");

    projectSelect?.addEventListener("change", () => handleFilterChange());
    sourceSelect?.addEventListener("change", () => handleFilterChange());
    typeSelect?.addEventListener("change", () => handleFilterChange());
    yearInput?.addEventListener("change", () => handleFilterChange());

    if (searchInput) {
      const onSearchInput = debounce(() => handleFilterChange(), 300);
      searchInput.addEventListener("input", onSearchInput);
    }

    resetButton?.addEventListener("click", () => {
      clearFilter("project");
      clearFilter("source");
      clearFilter("type");
      clearFilter("year");
      clearFilter("search");
      handleFilterChange();
    });

    chipsHost?.addEventListener("click", (event) => {
      const button = event.target.closest("button[data-filter-key]");
      if (!button) return;
      const { filterKey } = button.dataset;
      if (!filterKey) return;
      clearFilter(filterKey);
      handleFilterChange();
    });
  }

  function validateForm(form) {
    if (!form) return false;
    if (form.checkValidity()) {
      form.classList.remove("was-validated");
      return true;
    }
    form.classList.add("was-validated");
    return false;
  }

  function wireToolbar() {
    const button = $("#btnExport");
    const modalElement = $("#proliferationExportModal");
    if (!button || !modalElement) return;
    button.addEventListener("click", () => {
      populateExportModal();
      exportModalInstance = bootstrap.Modal.getOrCreateInstance(modalElement);
      exportModalInstance.show();
    });
  }

  function debounce(fn, delay = 200) {
    let timer;
    return (...args) => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => fn(...args), delay);
    };
  }

  function wireExportModal() {
    const modalElement = $("#proliferationExportModal");
    if (!modalElement) return;
    modalElement.addEventListener("show.bs.modal", populateExportModal);
    $("#expModeAll")?.addEventListener("change", () => setExportMode("all"));
    $("#expModeYears")?.addEventListener("change", () => setExportMode("years"));
    $("#expModeYearRange")?.addEventListener("change", () => setExportMode("yearRange"));
    $("#expModeRange")?.addEventListener("change", () => setExportMode("range"));
    $("#confirmExportBtn")?.addEventListener("click", submitExportRequest);
  }

  function populateExportLookups() {
    const fill = (select, items, defaultLabel) => {
      if (!select) return;
      const previous = select.value;
      select.innerHTML = `<option value="">${defaultLabel}</option>`;
      for (const item of items) {
        const id = Number(item.id ?? item.Id);
        const name = item.name ?? item.Name ?? "";
        if (!Number.isFinite(id)) continue;
        const option = document.createElement("option");
        option.value = String(id);
        option.textContent = name;
        select.append(option);
      }
      if (previous) {
        select.value = previous;
      }
    };

    fill($("#expProjectCat"), lookups.projectCategories, "All categories");
    fill($("#expTechCat"), lookups.technicalCategories, "All technical");
  }

  async function loadLookups() {
    try {
      const response = await fetch(api.lookups, { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error("Failed to load lookups");
      const data = await response.json();
      lookups.projectCategories = data.ProjectCategories ?? data.projectCategories ?? [];
      lookups.technicalCategories = data.TechnicalCategories ?? data.technicalCategories ?? [];
      populateExportLookups();
    } catch (error) {
      console.warn("Unable to load category lookups", error);
    }
  }

  function buildProjectLookupKey(term) {
    return term.trim().toLowerCase();
  }

  function buildProjectLookupUrl(term) {
    const params = new URLSearchParams();
    const trimmed = term.trim();
    if (trimmed) params.set("q", trimmed);
    const query = params.toString();
    return query ? `${api.projects}?${query}` : api.projects;
  }

  async function loadProjects(query = "") {
    const key = buildProjectLookupKey(query);
    if (lookupCache.has(key)) return lookupCache.get(key);
    const response = await fetch(buildProjectLookupUrl(query), { headers: { Accept: "application/json" } });
    if (!response.ok) throw new Error("Failed to load projects");
    const payload = await response.json();
    lookupCache.set(key, payload);
    return payload;
  }

  function populateProjectControls(options) {
    projectOptions = options || [];
    const datalist = $("#prefProjectOptions");
    if (datalist) {
      datalist.innerHTML = "";
      for (const item of projectOptions) {
        const opt = document.createElement("option");
        opt.value = item.display;
        opt.dataset.id = item.id;
        datalist.append(opt);
      }
    }

    const filterSelect = $("#pf-filter-project");
    if (filterSelect) {
      const previous = filterSelect.value;
      const target = filterState.projectId || previous;
      filterSelect.innerHTML = "<option value=\"\">All projects</option>";
      for (const item of projectOptions) {
        const option = document.createElement("option");
        option.value = String(item.id);
        option.textContent = item.display;
        filterSelect.append(option);
      }
      if (target) {
        filterSelect.value = String(target);
        if (filterSelect.value !== String(target)) {
          filterSelect.value = "";
        }
      }
      const selectedLabel = filterSelect.options[filterSelect.selectedIndex]?.textContent?.trim();
      filterState.projectLabel = filterSelect.value ? selectedLabel || "Selected project" : "";
      updateFilterSummary();
    }
  }

  function resolveProjectIdFromInput(value) {
    if (!value) return null;
    const datalist = $("#prefProjectOptions");
    const safeValue = window.CSS && window.CSS.escape ? window.CSS.escape(value) : value.replace(/"/g, '\\"');
    const option = datalist ? datalist.querySelector(`option[value="${safeValue}"]`) : null;
    if (option) {
      const id = Number(option.dataset.id);
      return Number.isFinite(id) ? id : null;
    }
    const exact = projectOptions.find((p) => p.display === value);
    return exact ? Number(exact.id) : null;
  }

  function formatPreferenceLabel(mode, sourceId) {
    if (!mode) {
      if (sourceId === abwSourceId) {
        return preferenceLabels.get("Auto") ?? "Auto";
      }
      return preferenceLabels.get(defaultPreferenceMode) ?? defaultPreferenceMode;
    }

    const normalized = String(mode);
    return preferenceLabels.get(normalized) ?? normalized.replace(/([A-Z])/g, " $1").trim();
  }

  async function refreshPreferenceSummary() {
    const projectId = Number($("#prefProjectId")?.value);
    const sourceRaw = $("#prefSource")?.value ?? "";
    const sourceId = Number(sourceRaw);
    const year = Number($("#prefYear")?.value);
    const current = $("#prefCurrentMode");
    updateMixedIndicator();
    if (!projectId || !Number.isFinite(sourceId) || sourceId <= 0 || !year || !current) {
      if (current) current.hidden = true;
      return;
    }

    preferenceFetchAbort?.abort();
    const ctrl = new AbortController();
    preferenceFetchAbort = ctrl;

    try {
      const response = await fetch(`${api.getPref}?projectId=${projectId}&source=${sourceId}&year=${year}`, { signal: ctrl.signal });
      if (response.status === 404) {
        const label = formatPreferenceLabel(null, sourceId);
        current.textContent = `Current mode: ${label} (default)`;
        current.hidden = false;
        return;
      }
      if (!response.ok) throw new Error();
      const data = await response.json();
      const mode = data.Mode ?? data.mode ?? defaultPreferenceMode;
      current.textContent = `Current mode: ${formatPreferenceLabel(mode, sourceId)}`;
      current.hidden = false;
    } catch (error) {
      if (error.name === "AbortError") return;
      current.textContent = "Unable to determine current preference.";
      current.hidden = false;
    }
  }

  function wirePreferences() {
    const form = $("#yearPreferenceForm");
    if (!form) return;
    const projectInput = $("#prefProject");
    const projectIdInput = $("#prefProjectId");
    const sourceSelect = $("#prefSource");
    const yearInput = $("#prefYear");
    const savedAt = $("#prefSavedAt");
    const resetBtn = $("#btnPrefReset");
    const saveBtn = $("#btnSavePreference");
    const canManage = form?.dataset.canManage === "true";

    const searchProjects = debounce(async () => {
      const term = projectInput.value.trim();
      try {
        const options = await loadProjects(term);
        populateProjectControls(options.map((item) => ({
          id: item.id ?? item.Id ?? item.projectId,
          display: item.display ?? `${item.name ?? item.Name}${item.code ? ` (${item.code})` : ""}`
        })));
      } catch {
        // ignore
      }
    }, 200);

    projectInput?.addEventListener("input", () => {
      projectIdInput.value = "";
      searchProjects();
      updateMixedIndicator();
    });

    projectInput?.addEventListener("change", () => {
      const resolved = resolveProjectIdFromInput(projectInput.value);
      projectIdInput.value = resolved ? String(resolved) : "";
      refreshPreferenceSummary();
    });

    const setPreferenceMode = (mode) => {
      $all("input[name='prefMode']", form).forEach((radio) => {
        radio.checked = radio.value === mode;
      });
    };

    setPreferenceMode(defaultPreferenceMode);

    sourceSelect?.addEventListener("change", () => {
      const selectedMode = form.querySelector("input[name='prefMode']:checked")?.value;
      if (Number(sourceSelect.value) === abwSourceId) {
        setPreferenceMode("Auto");
      } else if (selectedMode === "Auto") {
        setPreferenceMode(defaultPreferenceMode);
      }
      refreshPreferenceSummary();
    });
    yearInput?.addEventListener("change", refreshPreferenceSummary);

    resetBtn?.addEventListener("click", () => {
      projectInput.value = "";
      projectIdInput.value = "";
      sourceSelect.value = "";
      yearInput.value = "";
      savedAt.hidden = true;
      $("#prefCurrentMode").hidden = true;
      setPreferenceMode(defaultPreferenceMode);
      updateMixedIndicator();
    });

    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      if (!canManage) {
        toast("You do not have permission to update year preferences.", "warning");
        return;
      }
      const projectId = Number(projectIdInput.value);
      if (!projectId) {
        toast("Select a completed project", "warning");
        return;
      }
      if (!validateForm(form)) return;
      const sourceId = Number(sourceSelect.value);
      if (!Number.isFinite(sourceId) || sourceId <= 0) {
        toast("Select a source", "warning");
        return;
      }
      const submitSpinner = saveBtn?.querySelector(".spinner-border");
      if (submitSpinner) submitSpinner.hidden = false;
      if (saveBtn) saveBtn.disabled = true;
      const payload = {
        ProjectId: projectId,
        Source: sourceId,
        Year: Number(yearInput.value),
        Mode: form.querySelector("input[name='prefMode']:checked")?.value || defaultPreferenceMode
      };
      try {
        const response = await fetch(api.setPref, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
        if (!response.ok) {
          let message = "Unable to save preference";
          if (response.status === 403) {
            message = "You do not have permission to update year preferences.";
          } else {
            try {
              const text = await response.text();
              if (text) message = text;
            } catch {
              // ignore
            }
          }
          toast(message, response.status === 400 ? "warning" : "danger");
          return;
        }
        toast("Preference saved");
        const now = new Date();
        if (savedAt) {
          savedAt.textContent = `Saved at ${now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
          savedAt.hidden = false;
        }
        refreshPreferenceSummary();
        refresh();
      } catch {
        toast("Unable to save preference", "danger");
      } finally {
        if (submitSpinner) submitSpinner.hidden = true;
        if (saveBtn) saveBtn.disabled = false;
      }
    });
  }

  function populateYears() {
    const selects = [$("#expYears"), $("#expYearFrom"), $("#expYearTo")].filter(Boolean);
    if (selects.length === 0) return;
    const current = new Date().getUTCFullYear();
    for (const select of selects) {
      const isMultiple = select.multiple;
      const previous = isMultiple
        ? new Set($all("option:checked", select).map((opt) => opt.value))
        : select.value;
      select.innerHTML = "";
      if (!isMultiple) {
        const placeholder = document.createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Select year";
        select.append(placeholder);
      }
      for (let y = current; y >= current - 5; y -= 1) {
        const option = document.createElement("option");
        option.value = String(y);
        option.textContent = String(y);
        if (isMultiple) {
          option.selected = previous.has(option.value);
        } else if (previous === option.value) {
          option.selected = true;
        }
        select.append(option);
      }
    }
  }

  document.addEventListener("DOMContentLoaded", async () => {
    populateYears();
    restoreFilterStateFromStorage();
    await loadLookups();
    wireFilters();
    wireExportModal();
    wireToolbar();
    wirePagination();
    wirePreferences();
    try {
      const options = await loadProjects("");
      currentProjectLookupKey = buildProjectLookupKey("");
      populateProjectControls(options.map((item) => ({
        id: item.id ?? item.Id ?? item.projectId,
        display: item.display ?? `${item.name ?? item.Name}${item.code ? ` (${item.code})` : ""}`
      })));
    } catch (error) {
      console.warn("Unable to preload project options", error);
    }
    refresh();
  });
})();
