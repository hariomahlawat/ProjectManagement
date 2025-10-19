/* global bootstrap */
(() => {
  const api = {
    overview: "/api/proliferation/overview",
    createYearly: "/api/proliferation/yearly",
    createGranular: "/api/proliferation/granular",
    importYearly: "/api/proliferation/import/yearly",
    importGranular: "/api/proliferation/import/granular",
    exportCsv: "/api/proliferation/export",
    setPref: "/api/proliferation/year-preference",
    getPref: "/api/proliferation/year-preference",
    projects: "/api/proliferation/projects"
  };

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
    source: "",
    projectCategory: "",
    technicalCategory: "",
    search: "",
    page: 1,
    pageSize: 50
  };

  const lookupCache = new Map();
  let projectOptions = [];
  let preferenceFetchAbort = null;

  function collectFilters() {
    const byYearToggle = $("#fltByYear");
    const byYear = byYearToggle ? byYearToggle.checked : true;
    filterState.source = $("#fltSource")?.value ?? "";
    filterState.projectCategory = $("#fltProjectCat")?.value ?? "";
    filterState.technicalCategory = $("#fltTechCat")?.value ?? "";
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
    return {
      Years: filterState.years,
      FromDateUtc: filterState.from,
      ToDateUtc: filterState.to,
      Source: filterState.source || null,
      ProjectCategory: filterState.projectCategory || null,
      TechnicalCategory: filterState.technicalCategory || null,
      Search: filterState.search || null,
      Page: filterState.page,
      PageSize: filterState.pageSize
    };
  }

  function renderChips() {
    const host = $("#activeChips");
    if (!host) return;
    host.innerHTML = "";
    const chips = [];
    if (filterState.years.length) chips.push({ label: "Years", value: filterState.years.join(", ") });
    if (filterState.from || filterState.to) chips.push({ label: "Range", value: `${filterState.from?.slice(0, 10) || "…"} → ${filterState.to?.slice(0, 10) || "…"}` });
    if (filterState.source) chips.push({ label: "Source", value: filterState.source });
    if (filterState.projectCategory) chips.push({ label: "Project category", value: filterState.projectCategory });
    if (filterState.technicalCategory) chips.push({ label: "Technical", value: filterState.technicalCategory });
    if (filterState.search) chips.push({ label: "Search", value: `"${filterState.search}"` });

    for (const chip of chips) {
      const badge = document.createElement("span");
      badge.className = "badge rounded-pill text-bg-light me-1";
      badge.textContent = `${chip.label}: ${chip.value}`;
      host.append(badge);
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

  function updateEntryCount(count) {
    const badge = $("#entryListCount");
    if (!badge) return;
    if (!count) {
      badge.hidden = true;
      badge.textContent = "";
      return;
    }
    const total = Number(count);
    badge.textContent = `${total.toLocaleString()} ${total === 1 ? "record" : "records"}`;
    badge.hidden = false;
  }

  function renderTable(rows) {
    const host = $("#tableContainer");
    if (!host) return;

    if (!rows || !rows.length) {
      host.innerHTML = `<div class="alert alert-light border d-flex align-items-center" role="status">
        <div class="me-2" aria-hidden="true">ℹ️</div>
        <div>No records match the current filters. Try adjusting filters or reset.</div>
      </div>`;
      host.removeAttribute("aria-busy");
      updateEntryCount(0);
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
          <th scope="col">Simulator</th>
          <th scope="col">Date</th>
          <th scope="col" class="text-end">Quantity</th>
          <th scope="col" class="text-end">Effective total</th>
          <th scope="col">Status</th>
          <th scope="col">Mode</th>
        </tr>
      </thead>`;

    const body = rows.map((row) => {
      const project = row.Project ?? row.ProjectName ?? row.project ?? row.projectName ?? "";
      const source = row.Source ?? row.source ?? "";
      const typeLabel = row.DataType ?? row.dataType ?? "";
      const pane = typeLabel.toLowerCase().includes("year") ? "yearly" : "granular";
      const unit = row.UnitName ?? row.unitName ?? "";
      const simulator = row.SimulatorName ?? row.simulatorName ?? "";
      const dateRaw = row.DateUtc ?? row.dateUtc ?? row.ProliferationDate ?? row.proliferationDate ?? "";
      const quantity = row.Quantity ?? row.quantity ?? 0;
      const effective = row.EffectiveTotal ?? row.effectiveTotal;
      const approval = row.ApprovalStatus ?? row.approvalStatus ?? "";
      const mode = row.Mode ?? row.mode ?? "—";
      const year = row.Year ?? row.year ?? "";
      const attrs = [
        `data-entry-type="${pane}"`,
        `data-entry-project="${escapeAttr(project)}"`,
        `data-entry-source="${escapeAttr(source)}"`,
        `data-entry-year="${escapeAttr(year)}"`,
        `data-entry-date="${escapeAttr(dateRaw)}"`,
        `data-entry-quantity="${escapeAttr(quantity)}"`,
        `data-entry-unit="${escapeAttr(unit)}"`,
        `data-entry-simulator="${escapeAttr(simulator)}"`
      ].join(" ");
      return `
      <tr ${attrs}>
        <td>${year}</td>
        <td>${project}</td>
        <td>${source}</td>
        <td>${typeLabel}</td>
        <td>${unit}</td>
        <td>${simulator}</td>
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
    updateEntryCount(rows.length);
    updateSelectionSummary(null);
    wireEntryTable();
  }

  function clearEntrySelection() {
    $all("#tableContainer tbody tr").forEach((row) => row.classList.remove("is-selected"));
    updateSelectionSummary(null);
  }

  function updateSelectionSummary(row) {
    const summary = $("#entrySelectionSummary");
    if (!summary) return;
    if (!row) {
      summary.hidden = true;
      summary.textContent = "";
      return;
    }
    const project = row.dataset.entryProject || "—";
    const type = row.dataset.entryType === "yearly" ? "yearly total" : "granular entry";
    const quantity = Number(row.dataset.entryQuantity || 0).toLocaleString();
    const details = [];
    if (row.dataset.entryType === "yearly" && row.dataset.entryYear) {
      details.push(`Year ${row.dataset.entryYear}`);
    }
    if (row.dataset.entryType !== "yearly" && row.dataset.entryDate) {
      details.push(`Date ${fmt.date(row.dataset.entryDate)}`);
    }
    if (row.dataset.entrySource) {
      details.push(`Source ${row.dataset.entrySource}`);
    }
    if (row.dataset.entryUnit) {
      details.push(`Unit ${row.dataset.entryUnit}`);
    }
    if (row.dataset.entrySimulator) {
      details.push(`Simulator ${row.dataset.entrySimulator}`);
    }
    details.push(`Quantity ${quantity}`);
    summary.innerHTML = `<strong>${project}</strong> ${type}.<br><span class="text-muted">${details.join(" · ")}</span>`;
    summary.hidden = false;
  }

  function wireEntryTable() {
    const rows = $all("#tableContainer tbody tr");
    if (!rows.length) {
      clearEntrySelection();
      return;
    }
    rows.forEach((row) => {
      row.addEventListener("click", () => {
        rows.forEach((r) => r.classList.remove("is-selected"));
        row.classList.add("is-selected");
        const target = row.dataset.entryType === "yearly" ? "yearly" : "granular";
        activateEntryPane(target, { focus: false });
        updateSelectionSummary(row);
      });
    });
  }

  function activateEntryPane(name, options = {}) {
    const target = name === "yearly" ? "yearly" : "granular";
    const forms = $all("[data-entry-form]");
    forms.forEach((form) => {
      const isMatch = form.dataset.entryForm === target;
      form.classList.toggle("d-none", !isMatch);
      form.setAttribute("aria-hidden", String(!isMatch));
      if (!isMatch) form.classList.remove("was-validated");
    });
    const toggles = $all("[data-entry-pane]");
    toggles.forEach((btn) => {
      const active = btn.dataset.entryPane === target;
      btn.classList.toggle("active", active);
      btn.setAttribute("aria-pressed", active ? "true" : "false");
    });
    if (options.focus !== false) {
      const host = document.querySelector(`[data-entry-form="${target}"]`);
      const focusTarget = host?.querySelector("[data-entry-autofocus]");
      if (focusTarget) focusTarget.focus();
    }
  }

  function wireEntryPaneToggle() {
    $all("[data-entry-pane]").forEach((btn) => {
      btn.addEventListener("click", () => {
        activateEntryPane(btn.dataset.entryPane);
      });
    });
  }

  function wireEntryShortcuts() {
    $all("[data-entry-target]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const target = btn.dataset.entryTarget;
        activateEntryPane(target);
        $("#entryWorkspace")?.scrollIntoView({ behavior: "smooth", block: "start" });
      });
    });
  }

  function buildQuery(filters) {
    const params = new URLSearchParams();
    if (filters.Years?.length) filters.Years.forEach((y) => params.append("Years", y));
    if (filters.FromDateUtc) params.set("FromDateUtc", filters.FromDateUtc);
    if (filters.ToDateUtc) params.set("ToDateUtc", filters.ToDateUtc);
    if (filters.Source) params.set("Source", filters.Source);
    if (filters.ProjectCategory) params.set("ProjectCategoryId", filters.ProjectCategory);
    if (filters.TechnicalCategory) params.set("TechnicalCategoryId", filters.TechnicalCategory);
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
      renderTable(data.Rows ?? data.rows ?? []);
    } catch (error) {
      console.warn("Failed to load overview", error);
      if (host) {
        host.innerHTML = `<div class="alert alert-danger" role="alert">
          Unable to load proliferation overview. Please try again later.
        </div>`;
        host.setAttribute("aria-busy", "false");
      }
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

  function wireEntryForms() {
    const granularForm = $("#formGranular");
    if (granularForm && !granularForm.dataset.wired) {
      granularForm.dataset.wired = "true";
      granularForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!validateForm(granularForm)) return;
        const submitButton = granularForm.querySelector("[type='submit']");
        if (submitButton) submitButton.disabled = true;
        const body = {
          ProjectId: Number($("#gProjectId")?.value),
          SimulatorName: $("#gSimulator")?.value.trim(),
          UnitName: $("#gUnit")?.value.trim(),
          ProliferationDateUtc: new Date($("#gDate")?.value).toISOString(),
          Quantity: Number($("#gQty")?.value),
          Remarks: ($("#gRemarks")?.value || "").trim() || null
        };
        try {
          const response = await fetch(api.createGranular, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
          });
          if (!response.ok) throw new Error();
          toast("Granular entry saved");
          granularForm.reset();
          activateEntryPane("granular");
          clearEntrySelection();
          refresh();
        } catch {
          toast("Failed to save granular entry", "danger");
        } finally {
          if (submitButton) submitButton.disabled = false;
        }
      });
      granularForm.addEventListener("reset", () => {
        granularForm.classList.remove("was-validated");
      });
    }

    const yearlyForm = $("#formYearly");
    if (yearlyForm && !yearlyForm.dataset.wired) {
      yearlyForm.dataset.wired = "true";
      yearlyForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!validateForm(yearlyForm)) return;
        const submitButton = yearlyForm.querySelector("[type='submit']");
        if (submitButton) submitButton.disabled = true;
        const body = {
          ProjectId: Number($("#yProjectId")?.value),
          Source: Number($("#ySource")?.value),
          Year: Number($("#yYear")?.value),
          TotalQuantity: Number($("#yQty")?.value),
          Remarks: ($("#yRemarks")?.value || "").trim() || null
        };
        try {
          const response = await fetch(api.createYearly, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
          });
          if (!response.ok) throw new Error();
          toast("Yearly total saved");
          yearlyForm.reset();
          activateEntryPane("yearly");
          clearEntrySelection();
          refresh();
        } catch {
          toast("Failed to save yearly total", "danger");
        } finally {
          if (submitButton) submitButton.disabled = false;
        }
      });
      yearlyForm.addEventListener("reset", () => {
        yearlyForm.classList.remove("was-validated");
      });
    }
  }

  function wireImport() {
    $("#btnRunImport")?.addEventListener("click", async () => {
      const type = $("#impType")?.value || "yearly";
      const file = $("#impFile")?.files?.[0];
      if (!file) {
        toast("Choose a CSV file to import", "warning");
        return;
      }
      const bar = $("#impProgress .progress-bar");
      const host = $("#impProgress");
      host?.classList.remove("d-none");
      host?.setAttribute("aria-hidden", "false");
      bar.style.width = "30%";
      bar.textContent = "Uploading…";
      const fd = new FormData();
      fd.append("file", file);
      const endpoint = type === "granular" ? api.importGranular : api.importYearly;
      try {
        const response = await fetch(endpoint, { method: "POST", body: fd });
        if (!response.ok) throw new Error();
        const payload = await response.json();
        bar.style.width = "100%";
        bar.textContent = "Done";
        $("#impResult").innerHTML = `<div class="alert alert-light border">Imported: <strong>${payload.Accepted ?? payload.accepted ?? 0}</strong>, Rejected: <strong>${payload.Rejected ?? payload.rejected ?? 0}</strong></div>`;
        toast("Import finished");
        refresh();
      } catch {
        bar.style.width = "100%";
        bar.classList.add("bg-danger");
        bar.textContent = "Failed";
        toast("Import failed", "danger");
      } finally {
        setTimeout(() => {
          bar.style.width = "0%";
          bar.classList.remove("bg-danger");
          bar.textContent = "0%";
          host?.classList.add("d-none");
          host?.setAttribute("aria-hidden", "true");
          $("#impFile").value = "";
        }, 800);
      }
    });
  }

  function wireToolbar() {
    $("#btnExport")?.addEventListener("click", () => {
      const filters = collectFilters();
      window.location.href = api.exportCsv + buildQuery(filters);
    });
  }

  function debounce(fn, delay = 200) {
    let timer;
    return (...args) => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => fn(...args), delay);
    };
  }

  async function loadProjects(query = "") {
    const key = query.trim().toLowerCase();
    if (lookupCache.has(key)) return lookupCache.get(key);
    const response = await fetch(api.projects + (query ? `?q=${encodeURIComponent(query)}` : ""), { headers: { Accept: "application/json" } });
    if (!response.ok) throw new Error("Failed to load projects");
    const payload = await response.json();
    lookupCache.set(key, payload);
    return payload;
  }

  function populateProjectControls(options) {
    projectOptions = options || [];
    const granularSelect = $("#gProjectId");
    const yearlySelect = $("#yProjectId");
    const datalist = $("#prefProjectOptions");
    const insertPlaceholder = (select) => {
      const placeholder = document.createElement("option");
      placeholder.value = "";
      placeholder.textContent = "Select a completed project";
      placeholder.disabled = true;
      placeholder.selected = true;
      select.append(placeholder);
      return placeholder;
    };
    if (granularSelect) {
      const previous = granularSelect.value;
      granularSelect.innerHTML = "";
      const placeholder = insertPlaceholder(granularSelect);
      for (const item of projectOptions) {
        const opt = document.createElement("option");
        opt.value = String(item.id);
        opt.textContent = item.display;
        if (previous && String(item.id) === previous) {
          opt.selected = true;
          placeholder.selected = false;
        }
        granularSelect.append(opt);
      }
    }
    if (yearlySelect) {
      const previous = yearlySelect.value;
      yearlySelect.innerHTML = "";
      const placeholder = insertPlaceholder(yearlySelect);
      for (const item of projectOptions) {
        const opt = document.createElement("option");
        opt.value = String(item.id);
        opt.textContent = item.display;
        if (previous && String(item.id) === previous) {
          opt.selected = true;
          placeholder.selected = false;
        }
        yearlySelect.append(opt);
      }
    }
    if (datalist) {
      datalist.innerHTML = "";
      for (const item of projectOptions) {
        const opt = document.createElement("option");
        opt.value = item.display;
        opt.dataset.id = item.id;
        datalist.append(opt);
      }
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

  async function refreshPreferenceSummary() {
    const projectId = Number($("#prefProjectId")?.value);
    const source = $("#prefSource")?.value;
    const year = Number($("#prefYear")?.value);
    const current = $("#prefCurrentMode");
    if (!projectId || !source || !year || !current) {
      if (current) current.hidden = true;
      return;
    }

    preferenceFetchAbort?.abort();
    const ctrl = new AbortController();
    preferenceFetchAbort = ctrl;

    try {
      const response = await fetch(`${api.getPref}?projectId=${projectId}&source=${encodeURIComponent(source)}&year=${year}`, { signal: ctrl.signal });
      if (response.status === 404) {
        current.textContent = "Current mode: Auto (default)";
        current.hidden = false;
        return;
      }
      if (!response.ok) throw new Error();
      const data = await response.json();
      const mode = data.Mode ?? data.mode ?? "Auto";
      current.textContent = `Current mode: ${mode.replace(/([A-Z])/g, " $1").trim()}`;
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
    });

    projectInput?.addEventListener("change", () => {
      const resolved = resolveProjectIdFromInput(projectInput.value);
      projectIdInput.value = resolved ? String(resolved) : "";
      refreshPreferenceSummary();
    });

    sourceSelect?.addEventListener("change", refreshPreferenceSummary);
    yearInput?.addEventListener("change", refreshPreferenceSummary);

    resetBtn?.addEventListener("click", () => {
      projectInput.value = "";
      projectIdInput.value = "";
      sourceSelect.value = "";
      yearInput.value = "";
      savedAt.hidden = true;
      $("#prefCurrentMode").hidden = true;
      $all("input[name='prefMode']").forEach((radio) => { radio.checked = radio.value === "Auto"; });
    });

    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      const projectId = Number(projectIdInput.value);
      if (!projectId) {
        toast("Select a completed project", "warning");
        return;
      }
      if (!validateForm(form)) return;
      const submitSpinner = saveBtn?.querySelector(".spinner-border");
      if (submitSpinner) submitSpinner.hidden = false;
      saveBtn.disabled = true;
      const payload = {
        ProjectId: projectId,
        Source: sourceSelect.value,
        Year: Number(yearInput.value),
        Mode: form.querySelector("input[name='prefMode']:checked")?.value || "Auto"
      };
      try {
        const response = await fetch(api.setPref, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
        if (!response.ok) throw new Error();
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
        saveBtn.disabled = false;
      }
    });
  }

  function populateYears() {
    const select = $("#fltYears");
    if (!select) return;
    const current = new Date().getUTCFullYear();
    for (let y = current; y >= current - 5; y -= 1) {
      const option = document.createElement("option");
      option.value = String(y);
      option.textContent = String(y);
      select.append(option);
    }
  }

  document.addEventListener("DOMContentLoaded", async () => {
    populateYears();
    wireFilters();
    wireEntryForms();
    wireEntryPaneToggle();
    wireEntryShortcuts();
    activateEntryPane("granular", { focus: false });
    wireImport();
    wireToolbar();
    wirePreferences();
    try {
      const options = await loadProjects("");
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
