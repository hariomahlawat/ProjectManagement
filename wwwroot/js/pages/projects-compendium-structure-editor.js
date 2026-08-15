(() => {
    "use strict";

    const root = document.querySelector("[data-compendium-structure-editor]");
    if (!(root instanceof HTMLElement)) return;

    const bootstrapNode = root.querySelector("[data-structure-editor-bootstrap]");
    let boot = {};
    try { boot = JSON.parse(bootstrapNode?.textContent || "{}"); }
    catch { boot = {}; }

    const handoffApi = globalThis.PrismCompendiumStructure || null;
    const presetId = Number(boot?.preset?.id || 0);
    if (!presetId) return;

    const normalize = value => String(value ?? "").trim().toLowerCase();
    const cleanName = value => String(value ?? "").trim().replace(/\s+/g, " ").slice(0, 120);
    const cleanKey = value => String(value ?? "").trim().replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 40);
    const createSectionKey = () => `sec-${(globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`).replace(/[^a-zA-Z0-9]/g, "").slice(0, 32)}`;
    const clamp = value => Number.isFinite(Number(value)) ? Math.max(0, Math.min(1, Number(value))) : .5;
    const escapeHtml = value => String(value ?? "").replace(/[&<>'"]/g, character => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
    })[character]);

    const normalizeGrouping = value => ({
        technicalcategory: "TechnicalCategory",
        none: "None",
        customsections: "CustomSections"
    }[normalize(value)] || "TechnicalCategory");
    const normalizeSort = value => ({
        manual: "Manual",
        latestfirst: "LatestFirst",
        alphabetical: "Alphabetical"
    }[normalize(value)] || "Manual");

    const candidates = Array.isArray(boot.projects) ? boot.projects : [];
    const projectById = new Map(candidates.map(project => [Number(project.projectId), {
        ...project,
        projectId: Number(project.projectId),
        publicationYear: Number(project.publicationYear || 0),
        technicalCategorySortOrder: Number(project.technicalCategorySortOrder ?? Number.MAX_SAFE_INTEGER)
    }]));

    let groupingMode = normalizeGrouping(boot.groupingMode);
    let sortMode = normalizeSort(boot.sortMode);
    let rowVersion = String(boot?.preset?.rowVersion || "");
    const canManage = Boolean(boot.canManage);

    let customSections = (Array.isArray(boot.sections) ? boot.sections : [])
        .slice()
        .sort((a, b) => Number(a?.sortOrder || 0) - Number(b?.sortOrder || 0))
        .map((section, index) => ({
            sectionKey: cleanKey(section.sectionKey) || createSectionKey(),
            name: cleanName(section.name) || `Section ${index + 1}`,
            sortOrder: index
        }));

    let orderedIds = candidates
        .filter(project => Boolean(project.selected))
        .sort((a, b) => Number(a.sortOrder || 0) - Number(b.sortOrder || 0))
        .map(project => Number(project.projectId));

    const configById = new Map();
    candidates.forEach(project => {
        configById.set(Number(project.projectId), {
            primaryPhotoId: Number(project.primaryPhotoId || 0) || null,
            focalX: clamp(project.primaryFocalX),
            focalY: clamp(project.primaryFocalY),
            imageSelectionMode: normalize(project.imageSelectionMode) === "explicit" ? "explicit" : "automatic",
            imageFitMode: normalize(project.imageFitMode) === "fit" ? "fit" : "fill",
            dossierLayout: normalizeDossierLayout(project.dossierLayout), dossierImageCount: Math.max(1,Math.min(3,Number(project.dossierImageCount||1))),
            supportingPhoto1Id:Number(project.supportingPhoto1Id||0)||null, supportingPhoto1FocalX:clamp(project.supportingPhoto1FocalX), supportingPhoto1FocalY:clamp(project.supportingPhoto1FocalY), supportingPhoto1FitMode:normalize(project.supportingPhoto1FitMode)==="fit"?"fit":"fill",
            supportingPhoto2Id:Number(project.supportingPhoto2Id||0)||null, supportingPhoto2FocalX:clamp(project.supportingPhoto2FocalX), supportingPhoto2FocalY:clamp(project.supportingPhoto2FocalY), supportingPhoto2FitMode:normalize(project.supportingPhoto2FitMode)==="fit"?"fit":"fill",
            reviewFingerprint: null,
            customSectionKey: cleanKey(project.customSectionKey) || null,
            customSectionName: cleanName(project.customSectionName) || null,
            narrativeSourceOverride: project.narrativeSourceOverride ? String(project.narrativeSourceOverride) : null
        });
    });

    let projectStates = {};
    const incomingHandoff = handoffApi?.read?.(presetId) || null;
    const outsideStructureDirty = Boolean(incomingHandoff && incomingHandoff.persisted === false);
    let returnUrl = incomingHandoff?.returnUrl || boot.returnUrl || `/Projects/Publications/Compendium?presetId=${presetId}&resumeStructure=1#compendium-select`;

    if (incomingHandoff) {
        const incomingIds = incomingHandoff.orderedIds
            .map(Number)
            .filter(id => projectById.has(id));
        orderedIds = [...new Set(incomingIds)];
        if (incomingHandoff.sections?.length || groupingMode === "CustomSections") {
            customSections = (incomingHandoff.sections || []).map((section, index) => ({
                sectionKey: cleanKey(section.sectionKey) || createSectionKey(),
                name: cleanName(section.name) || `Section ${index + 1}`,
                sortOrder: index
            }));
        }
        if (incomingHandoff.editorialState) {
            groupingMode = normalizeGrouping(incomingHandoff.editorialState.groupingMode || groupingMode);
            sortMode = normalizeSort(incomingHandoff.editorialState.sortMode || sortMode);
        }
        if (incomingHandoff.rowVersion) rowVersion = incomingHandoff.rowVersion;
        orderedIds.forEach(id => {
            const incoming = incomingHandoff.configs?.[id] || incomingHandoff.configs?.[String(id)] || null;
            if (!incoming) return;
            const config = configById.get(id) || {};
            configById.set(id, {
                primaryPhotoId: Number(incoming.primaryPhotoId || 0) || null,
                focalX: clamp(incoming.focalX),
                focalY: clamp(incoming.focalY),
                imageSelectionMode: normalize(incoming.imageSelectionMode) === "explicit" ? "explicit" : "automatic",
                imageFitMode: normalize(incoming.imageFitMode) === "fit" ? "fit" : "fill",
                dossierLayout: normalizeDossierLayout(incoming.dossierLayout), dossierImageCount:Math.max(1,Math.min(3,Number(incoming.dossierImageCount||1))),
                supportingPhoto1Id:Number(incoming.supportingPhoto1Id||0)||null, supportingPhoto1FocalX:clamp(incoming.supportingPhoto1FocalX), supportingPhoto1FocalY:clamp(incoming.supportingPhoto1FocalY), supportingPhoto1FitMode:normalize(incoming.supportingPhoto1FitMode)==="fit"?"fit":"fill",
                supportingPhoto2Id:Number(incoming.supportingPhoto2Id||0)||null, supportingPhoto2FocalX:clamp(incoming.supportingPhoto2FocalX), supportingPhoto2FocalY:clamp(incoming.supportingPhoto2FocalY), supportingPhoto2FitMode:normalize(incoming.supportingPhoto2FitMode)==="fit"?"fit":"fill",
                reviewFingerprint: String(incoming.reviewFingerprint || "").trim() || null,
                customSectionKey: cleanKey(incoming.customSectionKey) || null,
                customSectionName: cleanName(incoming.customSectionName) || null,
                narrativeSourceOverride: incoming.narrativeSourceOverride ? String(incoming.narrativeSourceOverride) : null
            });
        });
        projectStates = incomingHandoff.projectStates || {};
    }

    const ensureConfig = id => {
        const projectId = Number(id);
        if (!configById.has(projectId)) {
            configById.set(projectId, {
                primaryPhotoId: null,
                focalX: .5,
                focalY: .5,
                imageSelectionMode: "automatic",
                imageFitMode: "fill", dossierLayout:"Automatic", dossierImageCount:1,
                supportingPhoto1Id:null, supportingPhoto1FocalX:.5, supportingPhoto1FocalY:.5, supportingPhoto1FitMode:"fill",
                supportingPhoto2Id:null, supportingPhoto2FocalX:.5, supportingPhoto2FocalY:.5, supportingPhoto2FitMode:"fill",
                reviewFingerprint: null,
                customSectionKey: null,
                customSectionName: null,
                narrativeSourceOverride: null
            });
        }
        return configById.get(projectId);
    };

    const sectionByKey = key => {
        const clean = cleanKey(key);
        return clean ? customSections.find(section => normalize(section.sectionKey) === normalize(clean)) || null : null;
    };

    // Normalize all section assignments against first-class section definitions.
    orderedIds.forEach(id => {
        const config = ensureConfig(id);
        let section = sectionByKey(config.customSectionKey);
        if (!section && config.customSectionName) {
            section = customSections.find(item => normalize(item.name) === normalize(config.customSectionName)) || null;
        }
        config.customSectionKey = section?.sectionKey || null;
        config.customSectionName = section?.name || null;
    });

    const search = root.querySelector("[data-editor-search]");
    const projectList = root.querySelector("[data-editor-project-list]");
    const canvas = root.querySelector("[data-editor-canvas]");
    const sectionNav = root.querySelector("[data-editor-section-nav]");
    const quickFilterButtons = [...root.querySelectorAll("[data-editor-filter]")];
    const selectionToolbar = root.querySelector("[data-editor-selection-toolbar]");
    const selectedCount = root.querySelector("[data-editor-selected-count]");
    const clearSelectionButton = root.querySelector("[data-editor-clear-selection]");
    const selectFilteredButton = root.querySelector("[data-editor-select-filtered]");
    const filteredCount = root.querySelector("[data-editor-filtered-count]");
    const bulk = root.querySelector("[data-editor-bulk]");
    const bulkCount = root.querySelector("[data-editor-bulk-count]");
    const bulkSection = root.querySelector("[data-editor-bulk-section]");
    const bulkMove = root.querySelector("[data-editor-bulk-move]");
    const bulkRemove = root.querySelector("[data-editor-bulk-remove]");
    const addSectionWrap = root.querySelector("[data-editor-add-section-wrap]");
    const newSectionInput = root.querySelector("[data-editor-new-section]");
    const addSectionButton = root.querySelector("[data-editor-add-section]");
    const collapseAll = root.querySelector("[data-editor-collapse-all]");
    const expandAll = root.querySelector("[data-editor-expand-all]");
    const sortNote = root.querySelector("[data-editor-sort-note]");
    const modeCopy = root.querySelector("[data-editor-mode-copy]");
    const guidance = root.querySelector("[data-editor-guidance]");
    const projectCount = root.querySelector("[data-editor-project-count]");
    const sectionCount = root.querySelector("[data-editor-section-count]");
    const orderLabel = root.querySelector("[data-editor-order-label]");
    const visibleCount = root.querySelector("[data-editor-visible-count]");
    const unassignedCallout = root.querySelector("[data-editor-unassigned-callout]");
    const unassignedCount = root.querySelector("[data-editor-unassigned-count]");
    const saveState = root.querySelector("[data-editor-save-state]");
    const saveButton = root.querySelector("[data-editor-save]");
    const backButton = root.querySelector("[data-structure-back]");
    const toggleSectionsButton = root.querySelector("[data-editor-toggle-sections]");
    const returnUnsaved = document.querySelector("[data-editor-return-unsaved]");
    const saveReturn = document.querySelector("[data-editor-save-return]");
    const leaveModalNode = document.getElementById("compendiumStructureLeaveModal");
    const leaveModal = leaveModalNode && globalThis.bootstrap?.Modal
        ? globalThis.bootstrap.Modal.getOrCreateInstance(leaveModalNode)
        : null;
    const token = document.querySelector('.compendium-structure-editor-token input[name="__RequestVerificationToken"]')?.value || "";

    const editorSelection = new Set();
    const collapsedKeys = new Set();
    let activeFilter = "all";
    let lastEditorSelectionAnchor = null;
    let draggedProjectId = null;
    let draggedSectionKey = null;
    let pendingReturnAfterSave = false;
    let navigatingAway = false;
    let autoScrollFrame = 0;
    let autoScrollDelta = 0;
    let sectionNavFrame = 0;
    let sectionsNavigatorCollapsed = false;

    const fitEditorViewport = () => {
        const desktop = window.innerWidth >= 1100;
        document.documentElement.classList.toggle("compendium-structure-editor-mode", desktop);
        document.body.classList.toggle("compendium-structure-editor-mode", desktop);
        if (!desktop) {
            root.style.removeProperty("--compendium-structure-editor-height");
            return;
        }
        const top = Math.max(0, root.getBoundingClientRect().top);
        const available = Math.max(500, window.innerHeight - top - 10);
        root.style.setProperty("--compendium-structure-editor-height", `${available}px`);
    };

    const projectState = id => {
        const state = projectStates?.[id] || projectStates?.[String(id)] || null;
        const config = ensureConfig(id);
        return {
            isReviewed: Boolean(state?.isReviewed || config.reviewFingerprint),
            isReviewStale: Boolean(state?.isReviewStale),
            severity: String(state?.severity || "").toLowerCase(),
            warningCount: Number(state?.warningCount || 0),
            blockerCount: Number(state?.blockerCount || 0)
        };
    };

    const normalizeSectionOrders = () => customSections.forEach((section, index) => { section.sortOrder = index; });
    const sortIds = ids => {
        const result = [...ids];
        if (sortMode === "LatestFirst") {
            return result.sort((a, b) => {
                const pa = projectById.get(a), pb = projectById.get(b);
                return Number(pb?.publicationYear || 0) - Number(pa?.publicationYear || 0)
                    || String(pa?.projectName || "").localeCompare(String(pb?.projectName || ""), undefined, { sensitivity: "base" });
            });
        }
        if (sortMode === "Alphabetical") {
            return result.sort((a, b) => String(projectById.get(a)?.projectName || "").localeCompare(String(projectById.get(b)?.projectName || ""), undefined, { sensitivity: "base" }));
        }
        return result;
    };

    const publicationGroups = () => {
        if (groupingMode === "None") {
            return [{ key: "all", name: "Projects", ids: sortIds(orderedIds), unassigned: false, technical: false }];
        }
        if (groupingMode === "CustomSections") {
            const groups = customSections.map(section => ({
                key: section.sectionKey,
                name: section.name,
                ids: sortIds(orderedIds.filter(id => normalize(ensureConfig(id).customSectionKey) === normalize(section.sectionKey))),
                unassigned: false,
                technical: false
            }));
            const validKeys = new Set(customSections.map(section => normalize(section.sectionKey)));
            const unassigned = sortIds(orderedIds.filter(id => {
                const key = ensureConfig(id).customSectionKey;
                return !key || !validKeys.has(normalize(key));
            }));
            if (unassigned.length) groups.push({ key: "__unassigned", name: "Unassigned", ids: unassigned, unassigned: true, technical: false });
            return groups;
        }

        const map = new Map();
        orderedIds.forEach(id => {
            const project = projectById.get(id);
            const name = String(project?.technicalCategory || "").trim() || "Not recorded";
            const key = `tech:${normalize(name)}`;
            if (!map.has(key)) {
                map.set(key, {
                    key,
                    name,
                    ids: [],
                    unassigned: false,
                    technical: true,
                    sortOrder: Number(project?.technicalCategorySortOrder ?? Number.MAX_SAFE_INTEGER)
                });
            }
            const group = map.get(key);
            group.ids.push(id);
            group.sortOrder = Math.min(group.sortOrder, Number(project?.technicalCategorySortOrder ?? Number.MAX_SAFE_INTEGER));
        });
        const groups = [...map.values()].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
        groups.forEach(group => { group.ids = sortIds(group.ids); });
        return groups;
    };

    const structureSignature = () => JSON.stringify({
        orderedIds,
        sections: customSections.map((section, index) => ({ sectionKey: section.sectionKey, name: section.name, sortOrder: index })),
        assignments: orderedIds.map(id => ({ id, sectionKey: ensureConfig(id).customSectionKey || null }))
    });
    let baselineSignature = structureSignature();

    const isDirty = () => structureSignature() !== baselineSignature;
    const renderDirty = () => {
        const dirty = isDirty();
        if (saveButton) saveButton.disabled = !dirty || !canManage;
        if (saveState) {
            const label = saveState.querySelector("span");
            const icon = saveState.querySelector("i");
            saveState.classList.toggle("is-dirty", dirty);
            saveState.classList.toggle("is-session", !dirty && outsideStructureDirty);
            const text = dirty ? "Modified" : (outsideStructureDirty ? "Session changes" : "Saved");
            if (label) label.textContent = text;
            if (icon) icon.className = `bi ${dirty ? "bi-pencil-square" : outsideStructureDirty ? "bi-clock-history" : "bi-check-circle"}`;
            saveState.title = dirty
                ? "Structure changes have not been saved to the shared Compendium."
                : outsideStructureDirty
                    ? "Other Compendium changes from the current browser session will return with you."
                    : "Publication structure is saved.";
        }
        return dirty;
    };

    const stateIcon = id => {
        const state = projectState(id);
        if (state.blockerCount > 0 || state.severity === "blocker") return '<i class="bi bi-x-octagon-fill is-blocker" title="Publication blocker" aria-label="Publication blocker"></i>';
        if (state.warningCount > 0 || state.severity === "warning") return '<i class="bi bi-exclamation-triangle-fill is-warning" title="Publication warning" aria-label="Publication warning"></i>';
        if (!state.isReviewed || state.isReviewStale) return '<i class="bi bi-circle is-review" title="Review required" aria-label="Review required"></i>';
        return '<i class="bi bi-check-circle-fill is-ready" title="Reviewed" aria-label="Reviewed"></i>';
    };

    const currentSectionName = id => {
        const section = sectionByKey(ensureConfig(id).customSectionKey);
        return section?.name || "Unassigned";
    };

    const filterMatches = id => {
        const project = projectById.get(id);
        if (!project) return false;
        const term = normalize(search?.value);
        if (term) {
            const haystack = normalize(`${project.projectName} ${project.technicalCategory} ${project.projectCategory} ${project.lifecycle} ${currentSectionName(id)}`);
            if (!haystack.includes(term)) return false;
        }
        const state = projectState(id);
        if (activeFilter === "unassigned") return groupingMode === "CustomSections" && !sectionByKey(ensureConfig(id).customSectionKey);
        if (activeFilter === "warning") return state.warningCount > 0 || state.severity === "warning" || state.blockerCount > 0 || state.severity === "blocker";
        if (activeFilter === "unreviewed") return !state.isReviewed || state.isReviewStale;
        return true;
    };

    const updateFilterCounts = () => {
        const all = orderedIds.length;
        const unassigned = groupingMode === "CustomSections"
            ? orderedIds.filter(id => !sectionByKey(ensureConfig(id).customSectionKey)).length
            : 0;
        const warning = orderedIds.filter(id => {
            const state = projectState(id);
            return state.warningCount > 0 || state.blockerCount > 0 || ["warning", "blocker"].includes(state.severity);
        }).length;
        const unreviewed = orderedIds.filter(id => {
            const state = projectState(id);
            return !state.isReviewed || state.isReviewStale;
        }).length;
        [["all", all], ["unassigned", unassigned], ["warning", warning], ["unreviewed", unreviewed]].forEach(([key, count]) => {
            const node = root.querySelector(`[data-filter-count="${key}"]`);
            if (node) node.textContent = String(count);
        });
    };

    const renderProjectList = () => {
        if (!projectList) return;
        const visible = orderedIds.filter(filterMatches);
        if (visibleCount) visibleCount.textContent = `${visible.length} shown`;
        if (filteredCount) filteredCount.textContent = String(visible.length);
        if (selectFilteredButton) {
            const allSelected = visible.length > 0 && visible.every(id => editorSelection.has(id));
            selectFilteredButton.disabled = visible.length === 0;
            selectFilteredButton.classList.toggle("is-all-selected", allSelected);
            selectFilteredButton.firstElementChild?.classList.toggle("bi-check2-square", !allSelected);
            selectFilteredButton.firstElementChild?.classList.toggle("bi-check2-all", allSelected);
        }
        projectList.innerHTML = visible.length
            ? visible.map(id => {
                const project = projectById.get(id);
                const selected = editorSelection.has(id);
                return `<div class="compendium-structure-editor-project-row${selected ? " is-selected" : ""}" data-editor-project-row data-project-id="${id}" role="option" aria-selected="${selected ? "true" : "false"}" tabindex="0">
                    <input class="form-check-input" type="checkbox" data-editor-project-checkbox ${selected ? "checked" : ""} aria-label="Select ${escapeHtml(project?.projectName)} for bulk action" />
                    <div class="compendium-structure-editor-project-copy">
                        <strong>${escapeHtml(project?.projectName || `Project ${id}`)}</strong>
                        <span>${escapeHtml(project?.technicalCategory || "Not recorded")} · ${escapeHtml(project?.lifecycle || "")}${project?.publicationYear ? ` · ${project.publicationYear}` : ""}</span>
                        ${groupingMode === "CustomSections" ? `<small>${escapeHtml(currentSectionName(id))}</small>` : ""}
                    </div>
                    <span class="compendium-structure-editor-project-state">${stateIcon(id)}</span>
                </div>`;
            }).join("")
            : '<div class="compendium-structure-editor-empty"><i class="bi bi-search"></i><strong>No projects match this view.</strong><span>Change the search or quick filter.</span></div>';
    };

    const renderSelectionToolbar = () => {
        const count = editorSelection.size;
        if (selectionToolbar) selectionToolbar.hidden = count === 0;
        if (selectedCount) selectedCount.textContent = String(count);
        if (bulk) bulk.hidden = count === 0;
        if (bulkCount) bulkCount.textContent = String(count);
        if (bulkSection) {
            const current = bulkSection.value;
            bulkSection.innerHTML = groupingMode === "CustomSections"
                ? `<option value="">Move to section...</option>${customSections.map(section => `<option value="${escapeHtml(section.sectionKey)}">${escapeHtml(section.name)}</option>`).join("")}<option value="__unassigned">Unassigned</option>`
                : '<option value="">Section assignment is controlled by the grouping mode</option>';
            bulkSection.disabled = groupingMode !== "CustomSections";
            if ([...bulkSection.options].some(option => option.value === current)) bulkSection.value = current;
        }
        if (bulkMove) bulkMove.hidden = groupingMode !== "CustomSections";
    };

    const projectCardMarkup = (id, group) => {
        const project = projectById.get(id);
        const selected = editorSelection.has(id);
        const manual = sortMode === "Manual";
        const draggable = groupingMode === "CustomSections" || manual;
        return `<div class="compendium-structure-editor-project-card${selected ? " is-selected" : ""}${manual ? "" : " is-auto-ordered"}"
                     data-editor-canvas-project data-project-id="${id}" data-group-key="${escapeHtml(group.key)}" draggable="${draggable ? "true" : "false"}">
            <button type="button" class="compendium-structure-editor-project-grip" data-project-drag-handle title="${draggable ? (manual ? "Drag to reorder or move" : "Drag to another section") : "Automatic project order is active"}" aria-label="${draggable ? "Drag project" : "Automatic project order"}"><i class="bi bi-grip-vertical" aria-hidden="true"></i></button>
            <input class="form-check-input" type="checkbox" data-editor-canvas-select ${selected ? "checked" : ""} aria-label="Select ${escapeHtml(project?.projectName)} for bulk action" />
            <div>
                <strong>${escapeHtml(project?.projectName || `Project ${id}`)}</strong>
                <span>${escapeHtml(project?.technicalCategory || "Not recorded")} · ${escapeHtml(project?.lifecycle || "")}${project?.publicationYear ? ` · ${project.publicationYear}` : ""}</span>
            </div>
            <span class="compendium-structure-editor-project-state">${stateIcon(id)}</span>
        </div>`;
    };

    const renderCanvas = () => {
        if (!canvas) return;
        const groups = publicationGroups();
        const custom = groupingMode === "CustomSections";
        const manual = sortMode === "Manual";
        canvas.innerHTML = groups.map((group, groupIndex) => {
            const collapsed = collapsedKeys.has(String(group.key));
            const isCustomSection = custom && !group.unassigned;
            const section = isCustomSection ? sectionByKey(group.key) : null;
            const headerIdentity = isCustomSection
                ? `<button type="button" class="compendium-structure-editor-section-grip" data-section-drag-handle data-section-key="${escapeHtml(group.key)}" draggable="true" title="Drag section" aria-label="Drag ${escapeHtml(group.name)} section"><i class="bi bi-grip-vertical"></i></button>
                   <label class="compendium-structure-editor-section-name">
                       <input type="text" maxlength="120" value="${escapeHtml(group.name)}" data-editor-section-name data-section-key="${escapeHtml(group.key)}" aria-label="Section name" />
                       <i class="bi bi-pencil-square" aria-hidden="true"></i>
                   </label>`
                : `<span class="compendium-structure-editor-section-label">${escapeHtml(group.name)}</span>`;
            const sectionActions = isCustomSection
                ? `<button type="button" data-editor-section-up data-section-key="${escapeHtml(group.key)}" ${groupIndex === 0 ? "disabled" : ""} title="Move section up"><i class="bi bi-chevron-up"></i></button>
                   <button type="button" data-editor-section-down data-section-key="${escapeHtml(group.key)}" ${groupIndex >= customSections.length - 1 ? "disabled" : ""} title="Move section down"><i class="bi bi-chevron-down"></i></button>
                   <button type="button" data-editor-section-delete data-section-key="${escapeHtml(group.key)}" title="Delete section"><i class="bi bi-trash"></i></button>`
                : "";
            const dropHint = group.ids.length === 0 && custom
                ? '<div class="compendium-structure-editor-drop-empty">Drop projects here</div>'
                : "";
            const body = collapsed ? "" : `${group.ids.map(id => projectCardMarkup(id, group)).join("")}${dropHint}`;
            return `<section class="compendium-structure-editor-section${group.unassigned ? " is-unassigned" : ""}${collapsed ? " is-collapsed" : ""}" data-editor-section data-section-key="${escapeHtml(group.key)}" id="structure-section-${escapeHtml(String(group.key).replace(/[^a-zA-Z0-9_-]/g, "-"))}">
                <header>
                    <div class="compendium-structure-editor-section-title">${headerIdentity}</div>
                    <div class="compendium-structure-editor-section-actions">
                        <small>${group.ids.length} project${group.ids.length === 1 ? "" : "s"}</small>
                        ${sectionActions}
                        <button type="button" data-editor-section-collapse data-section-key="${escapeHtml(group.key)}" title="${collapsed ? "Expand section" : "Collapse section"}" aria-expanded="${collapsed ? "false" : "true"}"><i class="bi ${collapsed ? "bi-chevron-right" : "bi-chevron-down"}"></i></button>
                    </div>
                </header>
                <div class="compendium-structure-editor-section-body" data-section-drop-zone>${body}</div>
            </section>`;
        }).join("");
    };

    const renderSectionNav = () => {
        if (!sectionNav) return;
        const groups = publicationGroups();
        sectionNav.innerHTML = groups.map(group => `<button type="button" data-editor-nav-section="${escapeHtml(group.key)}" class="${group.unassigned ? "is-unassigned" : ""}">
            <span>${escapeHtml(group.name)}</span><b>${group.ids.length}</b>
        </button>`).join("");
        const unassigned = groups.find(group => group.unassigned)?.ids.length || 0;
        if (unassignedCallout) unassignedCallout.hidden = unassigned === 0;
        if (unassignedCount) unassignedCount.textContent = String(unassigned);
    };

    const updateActiveSectionNav = () => {
        if (!canvas || !sectionNav) return;
        const sections = [...canvas.querySelectorAll("[data-editor-section]")];
        if (!sections.length) return;
        const canvasTop = canvas.getBoundingClientRect().top;
        let active = sections[0];
        let best = Number.POSITIVE_INFINITY;
        sections.forEach(section => {
            const distance = Math.abs(section.getBoundingClientRect().top - canvasTop - 8);
            if (distance < best) { best = distance; active = section; }
        });
        const key = String(active?.dataset.sectionKey || "");
        sectionNav.querySelectorAll("[data-editor-nav-section]").forEach(button => {
            button.classList.toggle("is-active", String(button.dataset.editorNavSection || "") === key);
        });
    };

    const renderSummary = () => {
        const groups = publicationGroups();
        const sectionValue = groupingMode === "CustomSections"
            ? customSections.length
            : groupingMode === "None" ? 1 : groups.length;
        if (projectCount) projectCount.textContent = String(orderedIds.length);
        if (sectionCount) sectionCount.textContent = String(sectionValue);
        const orderText = sortMode === "Manual" ? "Manual order" : sortMode === "LatestFirst" ? "Latest first" : "A–Z";
        if (orderLabel) orderLabel.textContent = orderText;
        const groupingText = groupingMode === "CustomSections" ? "Custom sections" : groupingMode === "TechnicalCategory" ? "Technical categories" : "No grouping";
        if (modeCopy) modeCopy.textContent = `${groupingText} · ${orderText}`;
        if (guidance) guidance.textContent = sortMode === "Manual"
            ? "Drag projects between sections, reorder directly, or use bulk Move to section for large publications."
            : "Project sequence is automatic inside each section. Drag or bulk-move projects between custom sections; section order remains editorial.";
        if (addSectionWrap) addSectionWrap.hidden = groupingMode !== "CustomSections";
        if (sortNote) sortNote.hidden = sortMode === "Manual";
        updateFilterCounts();
    };

    const renderAll = () => {
        normalizeSectionOrders();
        [...editorSelection].forEach(id => { if (!orderedIds.includes(id)) editorSelection.delete(id); });
        renderSummary();
        renderProjectList();
        renderSelectionToolbar();
        renderCanvas();
        renderSectionNav();
        root.classList.toggle("is-sections-collapsed", sectionsNavigatorCollapsed);
        if (toggleSectionsButton) {
            toggleSectionsButton.setAttribute("aria-pressed", sectionsNavigatorCollapsed ? "true" : "false");
            toggleSectionsButton.title = sectionsNavigatorCollapsed ? "Show section navigator" : "Hide section navigator";
            const label = toggleSectionsButton.querySelector("span");
            if (label) label.textContent = sectionsNavigatorCollapsed ? "Show sections" : "Sections";
        }
        requestAnimationFrame(updateActiveSectionNav);
        renderDirty();
    };

    const writeHandoff = persisted => {
        if (!handoffApi?.write) return false;
        const configs = {};
        const states = {};
        orderedIds.forEach(id => {
            const config = ensureConfig(id);
            const section = sectionByKey(config.customSectionKey);
            configs[id] = {
                primaryPhotoId: config.primaryPhotoId,
                focalX: clamp(config.focalX),
                focalY: clamp(config.focalY),
                imageSelectionMode: config.imageSelectionMode,
                imageFitMode: config.imageFitMode || "fill", dossierLayout:config.dossierLayout||"Automatic", dossierImageCount:config.dossierImageCount||1,
                supportingPhoto1Id:config.supportingPhoto1Id||null, supportingPhoto1FocalX:clamp(config.supportingPhoto1FocalX), supportingPhoto1FocalY:clamp(config.supportingPhoto1FocalY), supportingPhoto1FitMode:config.supportingPhoto1FitMode||"fill",
                supportingPhoto2Id:config.supportingPhoto2Id||null, supportingPhoto2FocalX:clamp(config.supportingPhoto2FocalX), supportingPhoto2FocalY:clamp(config.supportingPhoto2FocalY), supportingPhoto2FitMode:config.supportingPhoto2FitMode||"fill",
                reviewFingerprint: config.reviewFingerprint || null,
                customSectionKey: section?.sectionKey || null,
                customSectionName: section?.name || null,
                narrativeSourceOverride: config.narrativeSourceOverride || null
            };
            states[id] = projectState(id);
        });
        return handoffApi.write({
            presetId,
            rowVersion,
            persisted: persisted !== false,
            source: "structure-editor",
            returnUrl,
            editorialState: {
                narrativeSource: incomingHandoff?.editorialState?.narrativeSource || "ProjectBrief",
                groupingMode,
                sortMode
            },
            orderedIds,
            sections: customSections.map((section, index) => ({ sectionKey: section.sectionKey, name: section.name, sortOrder: index })),
            configs,
            projectStates: states
        });
    };

    const commit = () => {
        renderAll();
        writeHandoff(false);
    };

    const setEditorSelected = (id, selected) => {
        const projectId = Number(id);
        if (!orderedIds.includes(projectId)) return;
        if (selected) editorSelection.add(projectId); else editorSelection.delete(projectId);
    };

    const applyEditorSelectionRange = (anchorId, targetId, selected) => {
        const visible = orderedIds.filter(filterMatches);
        const from = visible.indexOf(Number(anchorId));
        const to = visible.indexOf(Number(targetId));
        if (from < 0 || to < 0) {
            setEditorSelected(targetId, selected);
            return;
        }
        const start = Math.min(from, to), end = Math.max(from, to);
        visible.slice(start, end + 1).forEach(id => setEditorSelected(id, selected));
    };

    const toggleEditorSelection = (id, checked, shiftKey = false) => {
        if (shiftKey && lastEditorSelectionAnchor) applyEditorSelectionRange(lastEditorSelectionAnchor, id, checked);
        else setEditorSelected(id, checked);
        lastEditorSelectionAnchor = Number(id);
        renderProjectList();
        renderSelectionToolbar();
        renderCanvas();
    };

    quickFilterButtons.forEach(button => button.addEventListener("click", () => {
        activeFilter = String(button.dataset.editorFilter || "all");
        quickFilterButtons.forEach(item => item.classList.toggle("active", item === button));
        renderProjectList();
    }));
    search?.addEventListener("input", renderProjectList);

    projectList?.addEventListener("click", event => {
        const row = event.target.closest("[data-editor-project-row]");
        if (!row) return;
        const id = Number(row.dataset.projectId);
        const checkbox = event.target.closest("[data-editor-project-checkbox]");
        if (checkbox) {
            event.stopPropagation();
            toggleEditorSelection(id, Boolean(checkbox.checked), event.shiftKey);
            return;
        }
        if (event.target.closest("button,a,select,input,label")) return;
        toggleEditorSelection(id, !editorSelection.has(id), event.shiftKey);
    });
    projectList?.addEventListener("keydown", event => {
        if (event.key !== " ") return;
        const row = event.target.closest("[data-editor-project-row]");
        if (!row) return;
        event.preventDefault();
        const id = Number(row.dataset.projectId);
        toggleEditorSelection(id, !editorSelection.has(id), event.shiftKey);
    });
    clearSelectionButton?.addEventListener("click", () => { editorSelection.clear(); renderAll(); });

    selectFilteredButton?.addEventListener("click", () => {
        const visible = orderedIds.filter(filterMatches);
        if (!visible.length) return;
        const allSelected = visible.every(id => editorSelection.has(id));
        visible.forEach(id => setEditorSelected(id, !allSelected));
        lastEditorSelectionAnchor = visible.at(-1) || null;
        renderAll();
    });

    toggleSectionsButton?.addEventListener("click", () => {
        sectionsNavigatorCollapsed = !sectionsNavigatorCollapsed;
        root.classList.toggle("is-sections-collapsed", sectionsNavigatorCollapsed);
        if (toggleSectionsButton) {
            toggleSectionsButton.setAttribute("aria-pressed", sectionsNavigatorCollapsed ? "true" : "false");
            toggleSectionsButton.title = sectionsNavigatorCollapsed ? "Show section navigator" : "Hide section navigator";
            const label = toggleSectionsButton.querySelector("span");
            if (label) label.textContent = sectionsNavigatorCollapsed ? "Show sections" : "Sections";
        }
        requestAnimationFrame(updateActiveSectionNav);
    });

    canvas?.addEventListener("click", event => {
        const select = event.target.closest("[data-editor-canvas-select]");
        if (select) {
            const card = select.closest("[data-editor-canvas-project]");
            if (card) toggleEditorSelection(Number(card.dataset.projectId), Boolean(select.checked), event.shiftKey);
            return;
        }
        const collapse = event.target.closest("[data-editor-section-collapse]");
        if (collapse) {
            const key = String(collapse.dataset.sectionKey || "");
            if (collapsedKeys.has(key)) collapsedKeys.delete(key); else collapsedKeys.add(key);
            renderCanvas(); renderSectionNav();
            return;
        }
        const up = event.target.closest("[data-editor-section-up]");
        const down = event.target.closest("[data-editor-section-down]");
        if (up || down) {
            const key = String((up || down).dataset.sectionKey || "");
            const index = customSections.findIndex(section => normalize(section.sectionKey) === normalize(key));
            const target = index + (up ? -1 : 1);
            if (index >= 0 && target >= 0 && target < customSections.length) {
                [customSections[index], customSections[target]] = [customSections[target], customSections[index]];
                commit();
            }
            return;
        }
        const removeSection = event.target.closest("[data-editor-section-delete]");
        if (removeSection) {
            const key = String(removeSection.dataset.sectionKey || "");
            const section = sectionByKey(key);
            if (!section) return;
            const count = orderedIds.filter(id => normalize(ensureConfig(id).customSectionKey) === normalize(key)).length;
            if (!window.confirm(count
                ? `Delete “${section.name}”? ${count} project${count === 1 ? "" : "s"} will move to Unassigned.`
                : `Delete the empty section “${section.name}”?`)) return;
            orderedIds.forEach(id => {
                const config = ensureConfig(id);
                if (normalize(config.customSectionKey) === normalize(key)) {
                    config.customSectionKey = null;
                    config.customSectionName = null;
                }
            });
            customSections = customSections.filter(item => normalize(item.sectionKey) !== normalize(key));
            collapsedKeys.delete(key);
            commit();
        }
    });

    canvas?.addEventListener("change", event => {
        const input = event.target.closest("[data-editor-section-name]");
        if (!input) return;
        const key = String(input.dataset.sectionKey || "");
        const section = sectionByKey(key);
        if (!section) return;
        const name = cleanName(input.value);
        if (!name) { input.value = section.name; return; }
        if (customSections.some(item => normalize(item.sectionKey) !== normalize(key) && normalize(item.name) === normalize(name))) {
            window.alert("A publication section with this name already exists.");
            input.value = section.name;
            return;
        }
        if (section.name === name) return;
        section.name = name;
        orderedIds.forEach(id => {
            const config = ensureConfig(id);
            if (normalize(config.customSectionKey) === normalize(key)) config.customSectionName = name;
        });
        commit();
    });

    addSectionButton?.addEventListener("click", () => {
        if (groupingMode !== "CustomSections") return;
        const name = cleanName(newSectionInput?.value);
        if (!name) { newSectionInput?.focus(); return; }
        if (customSections.some(section => normalize(section.name) === normalize(name))) {
            window.alert("A publication section with this name already exists.");
            newSectionInput?.focus();
            return;
        }
        customSections.push({ sectionKey: createSectionKey(), name, sortOrder: customSections.length });
        if (newSectionInput) newSectionInput.value = "";
        commit();
    });
    newSectionInput?.addEventListener("keydown", event => {
        if (event.key === "Enter") { event.preventDefault(); addSectionButton?.click(); }
    });

    collapseAll?.addEventListener("click", () => {
        publicationGroups().forEach(group => collapsedKeys.add(String(group.key)));
        renderCanvas();
    });
    expandAll?.addEventListener("click", () => { collapsedKeys.clear(); renderCanvas(); });

    sectionNav?.addEventListener("click", event => {
        const button = event.target.closest("[data-editor-nav-section]");
        if (!button) return;
        const key = String(button.dataset.editorNavSection || "");
        const target = canvas?.querySelector(`[data-editor-section][data-section-key="${CSS.escape(key)}"]`);
        if (target) {
            if (collapsedKeys.has(key)) { collapsedKeys.delete(key); renderCanvas(); }
            requestAnimationFrame(() => canvas?.querySelector(`[data-editor-section][data-section-key="${CSS.escape(key)}"]`)?.scrollIntoView({ behavior: "smooth", block: "start" }));
        }
    });

    canvas?.addEventListener("scroll", () => {
        if (sectionNavFrame) cancelAnimationFrame(sectionNavFrame);
        sectionNavFrame = requestAnimationFrame(() => {
            sectionNavFrame = 0;
            updateActiveSectionNav();
        });
    }, { passive: true });

    bulkMove?.addEventListener("click", () => {
        if (groupingMode !== "CustomSections" || editorSelection.size === 0) return;
        const value = String(bulkSection?.value || "");
        if (!value) return;
        const section = value === "__unassigned" ? null : sectionByKey(value);
        if (value !== "__unassigned" && !section) return;
        [...editorSelection].forEach(id => {
            const config = ensureConfig(id);
            config.customSectionKey = section?.sectionKey || null;
            config.customSectionName = section?.name || null;
        });
        editorSelection.clear();
        commit();
    });

    bulkRemove?.addEventListener("click", () => {
        if (editorSelection.size === 0) return;
        const count = editorSelection.size;
        if (!window.confirm(`Remove ${count} project${count === 1 ? "" : "s"} from this Compendium? Project master data will not be changed.`)) return;
        orderedIds = orderedIds.filter(id => !editorSelection.has(id));
        editorSelection.clear();
        commit();
    });

    const moveProjectBefore = (projectId, beforeId) => {
        const id = Number(projectId), before = Number(beforeId);
        const from = orderedIds.indexOf(id);
        if (from < 0) return;
        orderedIds.splice(from, 1);
        const target = orderedIds.indexOf(before);
        if (target >= 0) orderedIds.splice(target, 0, id); else orderedIds.push(id);
    };

    const moveProjectToEndOfGroup = (projectId, groupKey) => {
        const id = Number(projectId);
        orderedIds = orderedIds.filter(value => value !== id);
        // Appending globally still makes the project last within its filtered publication group.
        orderedIds.push(id);
    };

    const beginAutoScroll = delta => {
        autoScrollDelta = delta;
        if (autoScrollFrame) return;
        const tick = () => {
            if (!autoScrollDelta || !canvas) { autoScrollFrame = 0; return; }
            canvas.scrollTop += autoScrollDelta;
            autoScrollFrame = requestAnimationFrame(tick);
        };
        autoScrollFrame = requestAnimationFrame(tick);
    };
    const stopAutoScroll = () => {
        autoScrollDelta = 0;
        if (autoScrollFrame) cancelAnimationFrame(autoScrollFrame);
        autoScrollFrame = 0;
    };

    canvas?.addEventListener("dragstart", event => {
        const sectionHandle = event.target.closest("[data-section-drag-handle]");
        if (sectionHandle && groupingMode === "CustomSections") {
            draggedSectionKey = String(sectionHandle.dataset.sectionKey || "");
            draggedProjectId = null;
            event.dataTransfer?.setData("text/plain", `section:${draggedSectionKey}`);
            if (event.dataTransfer) event.dataTransfer.effectAllowed = "move";
            return;
        }
        const card = event.target.closest("[data-editor-canvas-project]");
        if (!card) return;
        const id = Number(card.dataset.projectId);
        const draggable = groupingMode === "CustomSections" || sortMode === "Manual";
        if (!draggable) { event.preventDefault(); return; }
        draggedProjectId = id;
        draggedSectionKey = null;
        card.classList.add("is-dragging");
        event.dataTransfer?.setData("text/plain", String(id));
        if (event.dataTransfer) event.dataTransfer.effectAllowed = "move";
    });

    canvas?.addEventListener("dragover", event => {
        if (!draggedProjectId && !draggedSectionKey) return;
        event.preventDefault();
        if (event.dataTransfer) event.dataTransfer.dropEffect = "move";
        const rect = canvas.getBoundingClientRect();
        const threshold = 72;
        if (event.clientY < rect.top + threshold) beginAutoScroll(-10);
        else if (event.clientY > rect.bottom - threshold) beginAutoScroll(10);
        else stopAutoScroll();
        canvas.querySelectorAll(".is-drop-target").forEach(node => node.classList.remove("is-drop-target"));
        event.target.closest("[data-editor-section]")?.classList.add("is-drop-target");
    });

    canvas?.addEventListener("drop", event => {
        event.preventDefault();
        stopAutoScroll();
        canvas.querySelectorAll(".is-drop-target").forEach(node => node.classList.remove("is-drop-target"));
        const targetSection = event.target.closest("[data-editor-section]");
        if (!targetSection) return;
        const targetKey = String(targetSection.dataset.sectionKey || "");

        if (draggedSectionKey && groupingMode === "CustomSections") {
            if (targetKey === "__unassigned" || normalize(targetKey) === normalize(draggedSectionKey)) return;
            const from = customSections.findIndex(section => normalize(section.sectionKey) === normalize(draggedSectionKey));
            const to = customSections.findIndex(section => normalize(section.sectionKey) === normalize(targetKey));
            if (from >= 0 && to >= 0 && from !== to) {
                const [section] = customSections.splice(from, 1);
                customSections.splice(to, 0, section);
                commit();
            }
            return;
        }

        if (!draggedProjectId) return;
        const id = draggedProjectId;
        const targetCard = event.target.closest("[data-editor-canvas-project]");
        const beforeId = Number(targetCard?.dataset.projectId || 0) || null;

        if (groupingMode === "CustomSections") {
            const section = targetKey === "__unassigned" ? null : sectionByKey(targetKey);
            if (targetKey !== "__unassigned" && !section) return;
            const config = ensureConfig(id);
            config.customSectionKey = section?.sectionKey || null;
            config.customSectionName = section?.name || null;
            if (sortMode === "Manual" && beforeId && beforeId !== id) moveProjectBefore(id, beforeId);
            else if (sortMode === "Manual") moveProjectToEndOfGroup(id, targetKey);
            commit();
            return;
        }

        if (sortMode !== "Manual") return;
        if (groupingMode === "TechnicalCategory") {
            const sourceProject = projectById.get(id);
            const expected = `tech:${normalize(sourceProject?.technicalCategory || "Not recorded")}`;
            if (normalize(targetKey) !== normalize(expected)) return;
        }
        if (beforeId && beforeId !== id) moveProjectBefore(id, beforeId);
        else moveProjectToEndOfGroup(id, targetKey);
        commit();
    });

    canvas?.addEventListener("dragend", () => {
        stopAutoScroll();
        canvas.querySelectorAll(".is-dragging,.is-drop-target").forEach(node => node.classList.remove("is-dragging", "is-drop-target"));
        draggedProjectId = null;
        draggedSectionKey = null;
    });

    const structurePayload = () => ({
        sections: customSections.map((section, index) => ({ sectionKey: section.sectionKey, name: section.name, sortOrder: index })),
        projects: orderedIds.map(id => {
            const config = ensureConfig(id);
            const section = sectionByKey(config.customSectionKey);
            return {
                projectId: id,
                customSectionKey: section?.sectionKey || null,
                primaryPhotoId: config.imageSelectionMode === "explicit" ? config.primaryPhotoId : null,
                focalX: clamp(config.focalX),
                focalY: clamp(config.focalY),
                imageSelectionMode: config.imageSelectionMode === "explicit" ? "Explicit" : "Automatic",
                imageFitMode: config.imageFitMode === "fit" ? "Fit" : "Fill",
                dossierLayout:config.dossierLayout||"Automatic", dossierImageCount:config.dossierImageCount||1,
                supportingPhoto1Id:config.supportingPhoto1Id||null, supportingPhoto1FocalX:clamp(config.supportingPhoto1FocalX), supportingPhoto1FocalY:clamp(config.supportingPhoto1FocalY), supportingPhoto1FitMode:config.supportingPhoto1FitMode==="fit"?"Fit":"Fill",
                supportingPhoto2Id:config.supportingPhoto2Id||null, supportingPhoto2FocalX:clamp(config.supportingPhoto2FocalX), supportingPhoto2FocalY:clamp(config.supportingPhoto2FocalY), supportingPhoto2FitMode:config.supportingPhoto2FitMode==="fit"?"Fit":"Fill",
                narrativeSourceOverride: config.narrativeSourceOverride || null
            };
        })
    });

    const saveStructure = async () => {
        if (!canManage || !isDirty()) return true;
        const payload = new FormData();
        if (token) payload.append("__RequestVerificationToken", token);
        payload.append("presetId", String(presetId));
        payload.append("rowVersion", rowVersion);
        payload.append("structureJson", JSON.stringify(structurePayload()));
        saveButton && (saveButton.disabled = true);
        try {
            const response = await fetch(boot.saveUrl, {
                method: "POST",
                body: payload,
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            const body = await response.json().catch(() => ({}));
            if (!response.ok) {
                const error = new Error(body.message || "The publication structure could not be saved.");
                error.code = body.code || null;
                throw error;
            }
            rowVersion = String(body?.preset?.rowVersion || rowVersion);
            baselineSignature = structureSignature();
            renderDirty();
            writeHandoff(!outsideStructureDirty);
            return true;
        } catch (error) {
            if (error.code === "presetConflict") {
                window.alert(`${error.message}\n\nReload the Structure Editor before making further changes.`);
            } else {
                window.alert(error.message);
            }
            renderDirty();
            return false;
        }
    };

    const navigateBack = persisted => {
        navigatingAway = true;
        writeHandoff(persisted);
        location.assign(returnUrl);
    };

    const requestDone = () => {
        if (!isDirty()) {
            navigateBack(!outsideStructureDirty);
            return;
        }
        if (leaveModal) leaveModal.show();
        else if (window.confirm("Return to the Compendium with these unsaved structure changes kept locally?")) navigateBack(false);
    };

    saveButton?.addEventListener("click", saveStructure);
    backButton?.addEventListener("click", event => { event.preventDefault(); requestDone(); });
    returnUnsaved?.addEventListener("click", () => { leaveModal?.hide(); navigateBack(false); });
    saveReturn?.addEventListener("click", async () => {
        pendingReturnAfterSave = true;
        const saved = await saveStructure();
        pendingReturnAfterSave = false;
        if (saved) { leaveModal?.hide(); navigateBack(!outsideStructureDirty); }
    });

    window.addEventListener("beforeunload", event => {
        if (navigatingAway || !isDirty()) return;
        event.preventDefault();
        event.returnValue = "";
    });

    let viewportFrame = 0;
    const scheduleViewportFit = () => {
        if (viewportFrame) cancelAnimationFrame(viewportFrame);
        viewportFrame = requestAnimationFrame(() => { viewportFrame = 0; fitEditorViewport(); });
    };
    window.addEventListener("resize", scheduleViewportFit, { passive: true });
    window.addEventListener("orientationchange", scheduleViewportFit, { passive: true });
    window.addEventListener("pageshow", scheduleViewportFit, { passive: true });

    fitEditorViewport();
    renderAll();
    writeHandoff(!outsideStructureDirty && !isDirty());
})();
