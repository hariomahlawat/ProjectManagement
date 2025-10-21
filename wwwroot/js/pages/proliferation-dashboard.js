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

  const filterState = {
    years: [],
    from: null,
    to: null,
    sourceId: null,
    sourceLabel: "",
    projectCategory: "",
    technicalCategory: "",
    search: "",
    page: 1,
    pageSize: 50
  };

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

  function collectFilters() {
    const byYearToggle = $("#fltByYear");
    const byYear = byYearToggle ? byYearToggle.checked : true;
    const sourceRaw = $("#fltSource")?.value ?? "";
    if (sourceRaw) {
      const parsed = Number(sourceRaw);
      filterState.sourceId = Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } else {
      filterState.sourceId = null;
    }
    filterState.sourceLabel = formatSourceLabel(filterState.sourceId);
    const projectCategoryRaw = $("#fltProjectCat")?.value ?? "";
    const technicalCategoryRaw = $("#fltTechCat")?.value ?? "";
    filterState.projectCategory = projectCategoryRaw;
    filterState.technicalCategory = technicalCategoryRaw;
    filterState.search = ($("#fltSearch")?.value ?? "").trim();

    if (byYear) {
      filterState.years = $all("#fltYears option:checked").map((opt) => parseInt(opt.value, 10)).filter(Number.isFinite);
      filterState.from = null;
      filterState.to = null;
    } else {
      const fromVal = $("#fltFrom")?.value ?? "";
      const toVal = $("#fltTo")?.value ?? "";
      filterState.from = fromVal ? new Date(fromVal).toISOString() : null;
      filterState.to = toVal ? new Date(toVal).toISOString() : null;
      filterState.years = [];
    }

    renderChips();
    const projectCategoryId = projectCategoryRaw ? Number(projectCategoryRaw) : NaN;
    const technicalCategoryId = technicalCategoryRaw ? Number(technicalCategoryRaw) : NaN;

    return {
      Years: filterState.years,
      FromDateUtc: filterState.from,
      ToDateUtc: filterState.to,
      Source: filterState.sourceId,
      ProjectCategoryId: Number.isFinite(projectCategoryId) ? projectCategoryId : null,
      TechnicalCategoryId: Number.isFinite(technicalCategoryId) ? technicalCategoryId : null,
      Search: filterState.search || null,
      Page: filterState.page,
      PageSize: filterState.pageSize
    };
  }

  function resolveLookupLabel(source, value) {
    const id = Number(value);
    if (!Number.isFinite(id)) return null;
    const match = source.find((item) => {
      const itemId = item.id ?? item.Id;
      if (Number.isFinite(itemId)) return itemId === id;
      const parsed = Number(itemId);
      return Number.isFinite(parsed) && parsed === id;
    });
    if (!match) return null;
    return match.name ?? match.Name ?? null;
  }

  function resolveProjectCategoryLabel(value) {
    return resolveLookupLabel(lookups.projectCategories, value);
  }

  function resolveTechnicalCategoryLabel(value) {
    return resolveLookupLabel(lookups.technicalCategories, value);
  }

  function renderChips() {
    const host = $("#activeChips");
    if (!host) return;
    host.innerHTML = "";
    const chips = [];
    if (filterState.years.length) chips.push({ label: "Years", value: filterState.years.join(", ") });
    if (filterState.from || filterState.to) chips.push({ label: "Range", value: `${filterState.from?.slice(0, 10) || "…"} → ${filterState.to?.slice(0, 10) || "…"}` });
    if (filterState.sourceLabel) chips.push({ label: "Source", value: filterState.sourceLabel });
    const projectLabel = resolveProjectCategoryLabel(filterState.projectCategory);
    if (projectLabel) chips.push({ label: "Project category", value: projectLabel });
    const technicalLabel = resolveTechnicalCategoryLabel(filterState.technicalCategory);
    if (technicalLabel) chips.push({ label: "Technical", value: technicalLabel });
    if (filterState.search) chips.push({ label: "Search", value: `"${filterState.search}"` });

    for (const chip of chips) {
      const badge = document.createElement("span");
      badge.className = "badge rounded-pill text-bg-light me-1";
      badge.textContent = `${chip.label}: ${chip.value}`;
      host.append(badge);
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
    const hasFilterYears = filterState.years.length > 0;
    const hasAnyYears = hasExportYears || hasFilterYears;
    const hasYearRange = Boolean(exportState.yearStart && exportState.yearEnd);
    const hasAnyRange = Boolean(exportState.fromDate || exportState.toDate || filterState.from || filterState.to);

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
      const prefillYears = (hasExportYears ? exportState.years : filterState.years.map((y) => String(y))) || [];
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
      const savedFrom = exportState.fromDate || (filterState.from ? filterState.from.slice(0, 10) : "");
      fromInput.value = savedFrom;
    }
    if (toInput) {
      const savedTo = exportState.toDate || (filterState.to ? filterState.to.slice(0, 10) : "");
      toInput.value = savedTo;
    }

    const sourceSelect = $("#expSource");
    if (sourceSelect) {
      sourceSelect.value = filterState.sourceId ? String(filterState.sourceId) : "";
    }

    const projectCat = $("#expProjectCat");
    if (projectCat) {
      projectCat.value = filterState.projectCategory || "";
    }

    const techCat = $("#expTechCat");
    if (techCat) {
      techCat.value = filterState.technicalCategory || "";
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
      ["#kpiTotalCompletedProjects", data.TotalCompletedProjects ?? data.totalCompletedProjects],
      ["#kpiTotalEffectiveAll", data.TotalProliferationAllTime ?? data.totalProliferationAllTime],
      ["#kpiTotalEffectiveSdd", data.TotalProliferationSdd ?? data.totalProliferationSdd],
      ["#kpiTotalEffectiveAbw", data.TotalProliferationAbw515 ?? data.totalProliferationAbw515],
      ["#kpiLastYearProjects", data.LastYearProjectsProliferated ?? data.lastYearProjectsProliferated],
      ["#kpiLastYearTotal", data.LastYearTotalProliferation ?? data.lastYearTotalProliferation],
      ["#kpiLastYearSdd", data.LastYearSdd ?? data.lastYearSdd],
      ["#kpiLastYearAbw", data.LastYearAbw515 ?? data.lastYearAbw515]
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
    const valid = Number.isFinite(projectId) && projectId > 0 && Number.isFinite(sourceId) && sourceId > 0 && Number.isFinite(year);
    if (!valid) {
      indicator.hidden = true;
      return;
    }

    const coverage = mixedCoverage.get(buildMixedKey(projectId, sourceId, year));
    const hasMixed = Boolean(coverage?.yearly && coverage?.granular);
    indicator.hidden = !hasMixed;
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
          <th scope="col">Year</th>
          <th scope="col">Project</th>
          <th scope="col">Source</th>
          <th scope="col">Data type</th>
          <th scope="col">Unit</th>
          <th scope="col">Date</th>
          <th scope="col" class="text-end">Quantity</th>
          <th scope="col" class="text-end">Effective total</th>
          <th scope="col">Status</th>
          <th scope="col">Mode</th>
        </tr>
      </thead>`;

    const body = rows.map((row) => {
      const project = row.Project ?? row.ProjectName ?? row.project ?? row.projectName ?? "";
      const sourceLabel = formatSourceLabel(row.Source ?? row.source ?? "");
      const typeLabel = row.DataType ?? row.dataType ?? "";
      const pane = typeLabel.toLowerCase().includes("year") ? "yearly" : "granular";
      const unit = row.UnitName ?? row.unitName ?? "";
      const dateRaw = row.DateUtc ?? row.dateUtc ?? row.ProliferationDate ?? row.proliferationDate ?? "";
      const quantity = row.Quantity ?? row.quantity ?? 0;
      const effective = row.EffectiveTotal ?? row.effectiveTotal ?? quantity;
      const approval = row.ApprovalStatus ?? row.approvalStatus ?? "";
      const mode = row.Mode ?? row.mode ?? "—";
      const year = row.Year ?? row.year ?? "";
      const attrs = [
        `data-entry-type="${pane}"`,
        `data-entry-project="${escapeAttr(project)}"`,
        `data-entry-source="${escapeAttr(sourceLabel)}"`,
        `data-entry-year="${escapeAttr(year)}"`,
        `data-entry-date="${escapeAttr(dateRaw)}"`,
        `data-entry-quantity="${escapeAttr(quantity)}"`,
        `data-entry-unit="${escapeAttr(unit)}"`
      ].join(" ");
      return `
      <tr ${attrs}>
        <td>${year}</td>
        <td>${project}</td>
        <td>${sourceLabel}</td>
        <td>${typeLabel}</td>
        <td>${unit}</td>
        <td>${fmt.date(dateRaw)}</td>
        <td class="text-end">${fmt.number(quantity)}</td>
        <td class="text-end">${fmt.number(effective)}</td>
        <td>${approval}</td>
        <td>${mode}</td>
      </tr>`;
    }).join("");

    host.innerHTML = `<div class="table-responsive">
      <table class="table table-hover align-middle mb-0 table-proliferation">${header}<tbody>${body}</tbody></table>
    </div>`;
    host.setAttribute("aria-busy", "false");
  }

  function buildQuery(filters) {
    const params = new URLSearchParams();
    if (filters.Years?.length) filters.Years.forEach((y) => params.append("Years", y));
    if (filters.FromDateUtc) params.set("FromDateUtc", filters.FromDateUtc);
    if (filters.ToDateUtc) params.set("ToDateUtc", filters.ToDateUtc);
    if (filters.Source !== null && filters.Source !== undefined) {
      params.set("Source", filters.Source);
    }
    if (filters.ProjectCategoryId) params.set("ProjectCategoryId", filters.ProjectCategoryId);
    if (filters.TechnicalCategoryId) params.set("TechnicalCategoryId", filters.TechnicalCategoryId);
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
    }
  }

  function wireFilters() {
    $("#btnApply")?.addEventListener("click", () => {
      filterState.page = 1;
      refresh();
    });

    $("#btnReset")?.addEventListener("click", () => {
      const byYearRadio = $("#fltByYear");
      const byDateRadio = $("#fltByDate");
      if (byYearRadio) byYearRadio.checked = true;
      if (byDateRadio) byDateRadio.checked = false;
      const yearSelect = $("#fltYears");
      if (yearSelect) yearSelect.selectedIndex = -1;
      const fromInput = $("#fltFrom");
      const toInput = $("#fltTo");
      if (fromInput) fromInput.value = "";
      if (toInput) toInput.value = "";
      const sourceSelect = $("#fltSource");
      if (sourceSelect) sourceSelect.value = "";
      const projectCatSelect = $("#fltProjectCat");
      if (projectCatSelect) projectCatSelect.value = "";
      const techCatSelect = $("#fltTechCat");
      if (techCatSelect) techCatSelect.value = "";
      const searchInput = $("#fltSearch");
      if (searchInput) searchInput.value = "";
      $("#fltYearsWrap")?.classList.remove("d-none");
      $("#fltFromWrap")?.classList.add("d-none");
      $("#fltToWrap")?.classList.add("d-none");
      filterState.page = 1;
      refresh();
    });

    $("#fltByYear")?.addEventListener("change", () => {
      $("#fltYearsWrap")?.classList.remove("d-none");
      $("#fltFromWrap")?.classList.add("d-none");
      $("#fltToWrap")?.classList.add("d-none");
    });

    $("#fltByDate")?.addEventListener("change", () => {
      $("#fltYearsWrap")?.classList.add("d-none");
      $("#fltFromWrap")?.classList.remove("d-none");
      $("#fltToWrap")?.classList.remove("d-none");
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

  function populateCategoryFilters() {
    const fill = (select, items, defaultLabel, stateValue) => {
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
      const target = stateValue || previous;
      if (target) {
        select.value = target;
      }
    };

    fill($("#fltProjectCat"), lookups.projectCategories, "All categories", filterState.projectCategory);
    fill($("#expProjectCat"), lookups.projectCategories, "All categories", filterState.projectCategory);
    fill($("#fltTechCat"), lookups.technicalCategories, "All technical", filterState.technicalCategory);
    fill($("#expTechCat"), lookups.technicalCategories, "All technical", filterState.technicalCategory);
  }

  async function loadLookups() {
    try {
      const response = await fetch(api.lookups, { headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error("Failed to load lookups");
      const data = await response.json();
      lookups.projectCategories = data.ProjectCategories ?? data.projectCategories ?? [];
      lookups.technicalCategories = data.TechnicalCategories ?? data.technicalCategories ?? [];
      populateCategoryFilters();
    } catch (error) {
      console.warn("Unable to load category lookups", error);
    }
  }

  function buildProjectLookupKey(term) {
    const trimmed = term.trim().toLowerCase();
    return JSON.stringify({
      term: trimmed,
      projectCategory: filterState.projectCategory || "",
      technicalCategory: filterState.technicalCategory || ""
    });
  }

  function buildProjectLookupUrl(term) {
    const params = new URLSearchParams();
    const trimmed = term.trim();
    if (trimmed) params.set("q", trimmed);
    if (filterState.projectCategory) params.set("projectCategoryId", filterState.projectCategory);
    if (filterState.technicalCategory) params.set("technicalCategoryId", filterState.technicalCategory);
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
    if (!datalist) return;
    datalist.innerHTML = "";
    for (const item of projectOptions) {
      const opt = document.createElement("option");
      opt.value = item.display;
      opt.dataset.id = item.id;
      datalist.append(opt);
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
    const selects = [$("#fltYears"), $("#expYears"), $("#expYearFrom"), $("#expYearTo")].filter(Boolean);
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
