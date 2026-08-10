(() => {
    "use strict";

    const form = document.querySelector("[data-brochure-form]");
    if (!form) return;

    const rows = [...form.querySelectorAll("[data-project-row]")];
    const rowById = new Map(rows.map(row => [Number(row.dataset.projectId), row]));
    const checkboxes = new Map(rows.map(row => [Number(row.dataset.projectId), row.querySelector("[data-brochure-project-checkbox]")]));
    const selectedList = form.querySelector("[data-brochure-selected-list]");
    const selectedEmpty = form.querySelector("[data-brochure-selected-empty]");
    const hiddenInputs = form.querySelector("[data-brochure-hidden-inputs]");
    const selectedCount = form.querySelector("[data-brochure-selected-count]");
    const clearButton = form.querySelector("[data-brochure-clear-selection]");
    const selectVisibleButton = form.querySelector("[data-brochure-select-visible]");
    const searchInput = form.querySelector("[data-brochure-project-search]");
    const filters = [...form.querySelectorAll("[data-brochure-filter]")];
    const emptyFilterState = form.querySelector("[data-brochure-project-empty]");
    const narrativeSource = form.querySelector("[data-brochure-narrative-source]");
    const generateButton = form.querySelector("[data-brochure-generate]");
    const generateSpinner = form.querySelector("[data-brochure-spinner]");
    const generateIcon = form.querySelector("[data-brochure-generate-icon]");
    const generateLabel = form.querySelector("[data-brochure-generate-label]");

    const initialHiddenIds = [...hiddenInputs?.querySelectorAll('input[name="Input.ProjectIds"]') ?? []]
        .map(input => Number(input.value))
        .filter(id => Number.isFinite(id) && rowById.has(id));
    const initialCheckedIds = rows
        .filter(row => row.querySelector("[data-brochure-project-checkbox]")?.checked)
        .map(row => Number(row.dataset.projectId));

    let orderedIds = [...new Set(initialHiddenIds.length ? initialHiddenIds : initialCheckedIds)];
    let draggedId = null;

    const normalize = value => (value ?? "").trim().toLowerCase();

    const hasNarrative = row => {
        const source = narrativeSource?.value ?? "ProjectBrief";
        if (source === "CapabilityOverview") return row.dataset.hasCapabilityOverview === "true";
        if (source === "FullDescription") return row.dataset.hasFullDescription === "true";
        return row.dataset.hasProjectBrief === "true";
    };

    const narrativeWordCount = row => {
        const source = narrativeSource?.value ?? "ProjectBrief";
        const raw = source === "CapabilityOverview"
            ? row.dataset.capabilityOverviewWords
            : source === "FullDescription"
                ? row.dataset.fullDescriptionWords
                : row.dataset.projectBriefWords;
        const parsed = Number(raw);
        return Number.isFinite(parsed) ? parsed : 0;
    };

    const syncHiddenInputs = () => {
        if (!hiddenInputs) return;
        hiddenInputs.replaceChildren(...orderedIds.map(id => {
            const input = document.createElement("input");
            input.type = "hidden";
            input.name = "Input.ProjectIds";
            input.value = String(id);
            return input;
        }));
    };

    const move = (id, delta) => {
        const index = orderedIds.indexOf(id);
        const target = index + delta;
        if (index < 0 || target < 0 || target >= orderedIds.length) return;
        const copy = [...orderedIds];
        [copy[index], copy[target]] = [copy[target], copy[index]];
        orderedIds = copy;
        renderSelected();
    };

    const remove = id => {
        orderedIds = orderedIds.filter(value => value !== id);
        const checkbox = checkboxes.get(id);
        if (checkbox) checkbox.checked = false;
        renderSelected();
    };

    const selectedItem = (id, index) => {
        const row = rowById.get(id);
        const item = document.createElement("li");
        item.className = "brochure-selected-item";
        item.draggable = true;
        item.dataset.selectedId = String(id);
        item.innerHTML = `
            <span class="brochure-selected-item__handle" title="Drag to reorder"><i class="bi bi-grip-vertical" aria-hidden="true"></i></span>
            <span class="brochure-selected-item__name"></span>
            <span class="brochure-selected-item__actions">
                <button type="button" data-move-up title="Move up" ${index === 0 ? "disabled" : ""}><i class="bi bi-chevron-up" aria-hidden="true"></i></button>
                <button type="button" data-move-down title="Move down" ${index === orderedIds.length - 1 ? "disabled" : ""}><i class="bi bi-chevron-down" aria-hidden="true"></i></button>
                <button type="button" data-remove title="Remove"><i class="bi bi-x-lg" aria-hidden="true"></i></button>
            </span>`;
        item.querySelector(".brochure-selected-item__name").textContent = row?.dataset.projectName ?? `Project ${id}`;
        item.querySelector("[data-move-up]")?.addEventListener("click", () => move(id, -1));
        item.querySelector("[data-move-down]")?.addEventListener("click", () => move(id, 1));
        item.querySelector("[data-remove]")?.addEventListener("click", () => remove(id));

        item.addEventListener("dragstart", event => {
            draggedId = id;
            item.classList.add("is-dragging");
            event.dataTransfer.effectAllowed = "move";
            event.dataTransfer.setData("text/plain", String(id));
        });
        item.addEventListener("dragend", () => {
            draggedId = null;
            item.classList.remove("is-dragging");
        });
        item.addEventListener("dragover", event => {
            if (draggedId == null || draggedId === id) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = "move";
        });
        item.addEventListener("drop", event => {
            if (draggedId == null || draggedId === id) return;
            event.preventDefault();
            const from = orderedIds.indexOf(draggedId);
            const to = orderedIds.indexOf(id);
            if (from < 0 || to < 0) return;
            const copy = [...orderedIds];
            copy.splice(from, 1);
            copy.splice(to, 0, draggedId);
            orderedIds = copy;
            renderSelected();
        });
        return item;
    };

    const updateNarrativeIndicators = () => {
        const source = narrativeSource?.value ?? "ProjectBrief";
        const title = source === "CapabilityOverview"
            ? "Capability Overview"
            : source === "FullDescription"
                ? "Full Description"
                : "Project Brief";
        rows.forEach(row => {
            const indicator = row.querySelector("[data-brochure-narrative-status]");
            if (!indicator) return;
            const ready = hasNarrative(row);
            indicator.classList.toggle("is-ready", ready);
            indicator.classList.toggle("is-missing", !ready);
            indicator.title = title;
        });
    };

    const updatePreflight = () => {
        const selectedRows = orderedIds.map(id => rowById.get(id)).filter(Boolean);
        const missingNarrative = selectedRows.filter(row => !hasNarrative(row)).length;
        const missingPhoto = selectedRows.filter(row => row.dataset.hasPhoto !== "true").length;
        const lowRes = selectedRows.filter(row => row.dataset.hasPhoto === "true" && row.dataset.hasPrintPhoto !== "true").length;
        const longCopy = selectedRows.filter(row => hasNarrative(row) && narrativeWordCount(row) > 210).length;

        const set = (selector, value) => {
            const element = form.querySelector(selector);
            if (element) element.textContent = String(value);
        };
        set("[data-preflight-selected]", selectedRows.length);
        set("[data-preflight-narrative]", missingNarrative);
        set("[data-preflight-photo]", missingPhoto);
        set("[data-preflight-lowres]", lowRes);
        set("[data-preflight-longcopy]", longCopy);

        const message = form.querySelector("[data-preflight-message]");
        if (message) {
            message.classList.remove("is-ready", "is-warning");
            if (selectedRows.length === 0) {
                message.textContent = "Select projects to run brochure preflight.";
            } else if (missingNarrative + missingPhoto + lowRes + longCopy === 0) {
                message.textContent = "Selected projects have the required narrative and print-ready photographs.";
                message.classList.add("is-ready");
            } else {
                const sourceLabel = narrativeSource?.selectedOptions?.[0]?.textContent?.split("—")[0]?.trim() || "selected narrative";
                const longCopyNote = longCopy > 0 ? ` ${longCopy} project(s) exceed 210 words and will receive continuation feature page(s).` : "";
                message.textContent = `Review the warnings before publication. Missing copy is assessed against ${sourceLabel}. Generation remains available.${longCopyNote}`;
                message.classList.add("is-warning");
            }
        }
    };

    const renderSelected = () => {
        orderedIds = orderedIds.filter(id => rowById.has(id));
        if (selectedList) {
            selectedList.replaceChildren(...orderedIds.map(selectedItem));
        }
        for (const [id, checkbox] of checkboxes.entries()) {
            if (checkbox) checkbox.checked = orderedIds.includes(id);
        }
        if (selectedCount) selectedCount.textContent = String(orderedIds.length);
        if (selectedEmpty) selectedEmpty.hidden = orderedIds.length !== 0;
        if (clearButton) {
            clearButton.disabled = orderedIds.length === 0;
            clearButton.toggleAttribute("disabled", orderedIds.length === 0);
        }
        if (generateButton) generateButton.disabled = orderedIds.length === 0;
        syncHiddenInputs();
        updateNarrativeIndicators();
        updatePreflight();
    };

    const add = id => {
        if (!rowById.has(id) || orderedIds.includes(id)) return;
        orderedIds.push(id);
        renderSelected();
    };

    rows.forEach(row => {
        const id = Number(row.dataset.projectId);
        row.querySelector("[data-brochure-project-checkbox]")?.addEventListener("change", event => {
            if (event.currentTarget.checked) add(id);
            else remove(id);
        });
    });

    const applyFilters = () => {
        const query = normalize(searchInput?.value);
        const filterValues = new Map(filters.map(filter => [filter.dataset.brochureFilter, normalize(filter.value)]));
        let visible = 0;
        rows.forEach(row => {
            const matchesQuery = !query || normalize(row.dataset.projectName).includes(query);
            const matchesLifecycle = !filterValues.get("lifecycle") || row.dataset.lifecycle === filterValues.get("lifecycle");
            const matchesCategory = !filterValues.get("category") || row.dataset.category === filterValues.get("category");
            const matchesTechnical = !filterValues.get("technical") || row.dataset.technical === filterValues.get("technical");
            const show = matchesQuery && matchesLifecycle && matchesCategory && matchesTechnical;
            row.hidden = !show;
            if (show) visible++;
        });
        if (emptyFilterState) emptyFilterState.hidden = visible !== 0;
    };

    searchInput?.addEventListener("input", applyFilters);
    filters.forEach(filter => filter.addEventListener("change", applyFilters));

    selectVisibleButton?.addEventListener("click", () => {
        rows.filter(row => !row.hidden).forEach(row => {
            const id = Number(row.dataset.projectId);
            if (!orderedIds.includes(id)) orderedIds.push(id);
        });
        renderSelected();
    });

    clearButton?.addEventListener("click", () => {
        orderedIds = [];
        renderSelected();
    });

    narrativeSource?.addEventListener("change", () => {
        updateNarrativeIndicators();
        updatePreflight();
    });

    form.querySelectorAll("[data-cover-option] input[type=radio]").forEach(radio => {
        radio.addEventListener("change", () => {
            form.querySelectorAll("[data-cover-option]").forEach(option => {
                option.classList.toggle("is-selected", option.querySelector("input")?.checked === true);
            });
        });
    });

    form.addEventListener("submit", event => {
        if (orderedIds.length === 0) {
            event.preventDefault();
            return;
        }
        if (generateButton?.getAttribute("aria-busy") === "true") {
            event.preventDefault();
            return;
        }
        syncHiddenInputs();
        if (generateButton) {
            generateButton.setAttribute("aria-busy", "true");
            generateButton.disabled = true;
        }
        generateSpinner?.classList.remove("d-none");
        generateIcon?.classList.add("d-none");
        if (generateLabel) generateLabel.textContent = "Generating brochure…";
    });

    window.addEventListener("pageshow", () => {
        if (generateButton) {
            generateButton.setAttribute("aria-busy", "false");
            generateButton.disabled = orderedIds.length === 0;
        }
        generateSpinner?.classList.add("d-none");
        generateIcon?.classList.remove("d-none");
        if (generateLabel) generateLabel.textContent = "Generate brochure PDF";
    });

    renderSelected();
    applyFilters();
})();
