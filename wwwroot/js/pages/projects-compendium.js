(() => {
    "use strict";

    const form = document.querySelector("[data-compendium-builder]");
    if (!(form instanceof HTMLFormElement)) return;

    const parseJson = (node, fallback) => {
        try { return node?.textContent ? JSON.parse(node.textContent) : fallback; }
        catch { return fallback; }
    };
    const clamp = value => Number.isFinite(Number(value)) ? Math.max(0, Math.min(1, Number(value))) : 0.5;
    const normalize = value => String(value ?? "").trim().toLowerCase();
    const escapeHtml = value => String(value ?? "").replace(/[&<>'"]/g, c => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
    })[c]);
    const roundFocal = value => Number(clamp(value).toFixed(4));
    const formToken = () => form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const setControlDisabled = (control, disabled) => {
        if (!control) return;
        control.disabled = Boolean(disabled);
        control.setAttribute("aria-disabled", disabled ? "true" : "false");
    };

    const projects = parseJson(form.querySelector("[data-compendium-projects]"), []);
    const projectById = new Map(projects.map(project => [Number(project.projectId), project]));
    const presetSeed = parseJson(form.querySelector("[data-compendium-presets]"), []);
    const presets = new Map(presetSeed.map(preset => [Number(preset.id), { ...preset, id: Number(preset.id) }]));
    const activeSeed = parseJson(form.querySelector("[data-compendium-active-preset]"), {});
    const canManage = Boolean(activeSeed?.canManage);
    const canMaintainProjectData = String(form.dataset.canMaintainProjectData || "").toLowerCase() === "true";
    const frameWidthPoints = Number(form.dataset.photoFrameWidth || 519) || 519;
    const frameHeightPoints = Number(form.dataset.photoFrameHeight || 240) || 240;

    const selectedInput = form.querySelector("[data-selected-project-ids]");
    const selectionsInput = form.querySelector("[data-project-selections]");
    const sectionsInput = form.querySelector("[data-custom-sections]");
    const narrativeInput = form.querySelector("[data-narrative-source]");
    const groupingInput = form.querySelector("[data-grouping-mode]");
    const sortInput = form.querySelector("[data-sort-mode]");
    const normalizeNarrative = value => ({ projectbrief: "ProjectBrief", capabilityoverview: "CapabilityOverview", projectdescription: "ProjectDescription" }[normalize(value)] || "ProjectBrief");
    const normalizeGrouping = value => ({ technicalcategory: "TechnicalCategory", none: "None", customsections: "CustomSections" }[normalize(value)] || "TechnicalCategory");
    const normalizeSort = value => ({ manual: "Manual", latestfirst: "LatestFirst", alphabetical: "Alphabetical" }[normalize(value)] || "Manual");
    const editorialState = {
        narrativeSource: normalizeNarrative(narrativeInput?.value),
        groupingMode: normalizeGrouping(groupingInput?.value),
        sortMode: normalizeSort(sortInput?.value)
    };
    const cleanSectionName = value => String(value ?? "").trim().replace(/\s+/g, " ").slice(0, 120);
    const cleanSectionKey = value => String(value ?? "").trim().replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 40);
    const createSectionKey = () => `sec-${(globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`).replace(/[^a-zA-Z0-9]/g, "").slice(0, 32)}`;
    const sectionSeed = (() => {
        try { return sectionsInput?.value ? JSON.parse(sectionsInput.value) : []; }
        catch { return []; }
    })();
    let customSections = [];
    if (Array.isArray(sectionSeed)) {
        const seenKeys = new Set(), seenNames = new Set();
        sectionSeed
            .slice()
            .sort((a,b) => Number(a?.sortOrder || 0) - Number(b?.sortOrder || 0))
            .forEach(item => {
                const name = cleanSectionName(item?.name);
                if (!name || seenNames.has(normalize(name))) return;
                let key = cleanSectionKey(item?.sectionKey) || createSectionKey();
                if (seenKeys.has(normalize(key))) key = createSectionKey();
                seenKeys.add(normalize(key)); seenNames.add(normalize(name));
                customSections.push({ sectionKey: key, name, sortOrder: customSections.length });
            });
    }
    const activeIdInput = form.querySelector("[data-active-preset-id]");
    const activeVersionInput = form.querySelector("[data-active-preset-row-version]");
    const coverModeInput = form.querySelector("[data-cover-image-mode]");
    const coverProjectInput = form.querySelector("[data-cover-hero-project]");
    const coverPhotoInput = form.querySelector("[data-cover-hero-photo]");
    const coverFocalXInput = form.querySelector("[data-cover-focal-x]");
    const coverFocalYInput = form.querySelector("[data-cover-focal-y]");
    const coverState = {
        imageMode: normalize(coverModeInput?.value) === "explicit" ? "explicit" : normalize(coverModeInput?.value) === "none" ? "none" : "automatic",
        heroProjectId: Number(coverProjectInput?.value || 0) || null,
        heroPhotoId: Number(coverPhotoInput?.value || 0) || null,
        focalX: roundFocal(coverFocalXInput?.value),
        focalY: roundFocal(coverFocalYInput?.value)
    };

    let activePresetId = Number(activeIdInput?.value || activeSeed?.id || 0) || null;
    let activeRowVersion = String(activeVersionInput?.value || activeSeed?.rowVersion || "");
    let orderedIds = String(selectedInput?.value || "")
        .split(",")
        .map(Number)
        .filter(id => id > 0 && projectById.has(id));
    orderedIds = [...new Set(orderedIds)];

    const configById = new Map();
    const configSeed = (() => {
        try { return selectionsInput?.value ? JSON.parse(selectionsInput.value) : []; }
        catch { return []; }
    })();
    if (Array.isArray(configSeed)) {
        configSeed.forEach(item => {
            const id = Number(item?.projectId || 0);
            if (!id || !projectById.has(id)) return;
            const mode = normalize(item.imageSelectionMode) === "explicit" ? "explicit" : "automatic";
            configById.set(id, {
                primaryPhotoId: mode === "explicit" && Number(item.primaryPhotoId) > 0 ? Number(item.primaryPhotoId) : null,
                focalX: roundFocal(item.focalX),
                focalY: roundFocal(item.focalY),
                imageSelectionMode: mode,
                reviewFingerprint: String(item.reviewFingerprint || "").trim() || null,
                customSectionKey: cleanSectionKey(item.customSectionKey) || null,
                customSectionName: cleanSectionName(item.customSectionName) || null,
                narrativeSourceOverride: item.narrativeSourceOverride ? normalizeNarrative(item.narrativeSourceOverride) : null
            });
        });
    }

    const ensureConfig = id => {
        const projectId = Number(id);
        if (!configById.has(projectId)) {
            configById.set(projectId, {
                primaryPhotoId: null,
                focalX: 0.5,
                focalY: 0.5,
                imageSelectionMode: "automatic",
                reviewFingerprint: null,
                customSectionKey: null,
                customSectionName: null,
                narrativeSourceOverride: null
            });
        }
        return configById.get(projectId);
    };
    orderedIds.forEach(ensureConfig);

    // One-time browser normalization for a Phase-25 payload where sections only existed on projects.
    if (customSections.length === 0) {
        orderedIds.forEach(id => {
            const config = ensureConfig(id);
            const name = cleanSectionName(config.customSectionName);
            if (!name) return;
            let section = customSections.find(item => normalize(item.name) === normalize(name));
            if (!section) {
                section = { sectionKey: cleanSectionKey(config.customSectionKey) || createSectionKey(), name, sortOrder: customSections.length };
                customSections.push(section);
            }
            config.customSectionKey = section.sectionKey;
            config.customSectionName = section.name;
        });
    } else {
        orderedIds.forEach(id => {
            const config = ensureConfig(id);
            let section = config.customSectionKey
                ? customSections.find(item => normalize(item.sectionKey) === normalize(config.customSectionKey))
                : null;
            if (!section && config.customSectionName) section = customSections.find(item => normalize(item.name) === normalize(config.customSectionName));
            config.customSectionKey = section?.sectionKey || null;
            config.customSectionName = section?.name || null;
        });
    }

    let activeReviewId = orderedIds[0] ?? null;
    let activeReviewData = null;
    let reviewRequestController = null;
    let reviewRequestRevision = 0;
    let reviewRefreshTimer = null;
    let preflightTimer = null;
    let preflightController = null;
    let preflightRevision = 0;
    let preflightPending = false;
    let lastPreflight = null;
    let projectStateById = new Map();
    let lastVerifiedPdf = null;
    let exportBusy = false;
    let baselineSnapshot = null;
    let pendingLoadPresetId = null;
    let saveMode = "create";
    let findingSeverity = "all";

    const $ = selector => form.querySelector(selector);
    const rows = [...form.querySelectorAll("[data-project-row]")];
    const search = $("[data-filter-search]");
    const lifecycle = $("[data-filter-lifecycle]");
    const category = $("[data-filter-category]");
    const technical = $("[data-filter-technical]");
    const proliferation = $("[data-filter-proliferation]");
    const selectedOnly = $("[data-filter-selected]");
    const matchingCount = $("[data-compendium-matching]");
    const selectedCount = $("[data-compendium-selected-count]");
    const selectMatching = $("[data-select-matching]");
    const clearSelection = $("[data-clear-selection]");
    const orderList = $("[data-order-list]");
    const railCount = $("[data-rail-count]");
    const orderModeCopy = $("[data-order-mode-copy]");
    const orderNote = $("[data-order-note]");
    const customSectionToolbar = $("[data-custom-section-toolbar]");
    const customSectionName = $("[data-custom-section-name]");
    const customSectionAdd = $("[data-custom-section-add]");
    const customSectionSummary = $("[data-custom-section-summary]");
    const composerNote = $("[data-composer-note]");
    const narrativeButtons = [...form.querySelectorAll("[data-narrative-value]")];
    const groupingButtons = [...form.querySelectorAll("[data-grouping-value]")];
    const sortButtons = [...form.querySelectorAll("[data-sort-value]")];

    const reviewEmpty = $("[data-review-empty]");
    const reviewCard = $("[data-review-card]");
    const reviewProgress = $("[data-review-progress]");
    const reviewOrdinal = $("[data-review-ordinal]");
    const reviewName = $("[data-review-name]");
    const reviewMeta = $("[data-review-meta]");
    const reviewState = $("[data-review-state]");
    const reviewFacts = $("[data-review-facts]");
    const reviewDescription = $("[data-review-description]");
    const reviewDescriptionState = $("[data-review-description-state]");
    const reviewNarrativeLabel = $("[data-review-narrative-label]");
    const reviewNarrativeOptions = $("[data-review-narrative-options]");
    const reviewOpen = $("[data-review-open-project]");
    const reviewEdit = $("[data-review-edit-record]");
    const reviewManagePhotos = $("[data-review-manage-photos]");
    const reviewPrevious = $("[data-review-previous]");
    const reviewNext = $("[data-review-next]");
    const reviewNextAttention = $("[data-review-next-attention]");
    const reviewMarkReviewed = $("[data-review-mark-reviewed]");
    const reviewFooterTitle = $("[data-review-footer-title]");
    const reviewFooterCopy = $("[data-review-footer-copy]");
    const reviewImageFrame = $("[data-review-image-frame]");
    const reviewImage = $("[data-review-image]");
    const reviewImageEmpty = $("[data-review-image-empty]");
    const reviewImageMode = $("[data-review-image-mode]");
    const reviewImageQuality = $("[data-review-image-quality]");
    const reviewImageDetail = $("[data-review-image-detail]");
    const reviewChangeImage = $("[data-review-change-image]");
    const reviewAdjustCrop = $("[data-review-adjust-crop]");
    const reviewUseAutomatic = $("[data-review-use-automatic]");
    const reviewUseCover = $("[data-review-use-cover]");

    const coverPreview = $("[data-cover-preview]");
    const coverPreviewImage = $("[data-cover-preview-image]");
    const coverStatus = $("[data-cover-status]");
    const coverDetail = $("[data-cover-detail]");
    const coverAutomatic = $("[data-cover-automatic]");
    const coverChoose = $("[data-cover-choose]");
    const coverNone = $("[data-cover-none]");

    const readySelected = $("[data-ready-selected]");
    const readyBlockers = $("[data-ready-blockers]");
    const readyWarnings = $("[data-ready-warnings]");
    const readyInfo = $("[data-ready-info]");
    const readyReviewed = $("[data-ready-reviewed]");
    const readyCategories = $("[data-ready-categories]");
    const readyStructureCopy = $("[data-ready-structure-copy]");
    const readyFindings = $("[data-ready-findings]");
    const findingToolbar = $("[data-finding-toolbar]");
    const findingsCurrentOnly = $("[data-findings-current-only]");
    const findingFilterButtons = [...form.querySelectorAll("[data-finding-filter]")];
    const preflightSpinner = $("[data-preflight-spinner]");
    const preview = $("[data-preview]");
    const generate = $("[data-generate]");
    const outputStatus = $("[data-output-status]");
    const outputVerification = $("[data-output-verification]");
    const outputVerificationText = $("[data-output-verification-text]");
    const previewUrl = form.dataset.previewUrl || "";
    const generateUrl = form.dataset.generateUrl || "";

    const presetSelect = document.querySelector("[data-compendium-preset-select]");
    const presetLoad = document.querySelector("[data-compendium-preset-load]");
    const presetDirty = document.querySelector("[data-compendium-preset-dirty]");
    const presetMeta = document.querySelector("[data-compendium-preset-meta]");
    const saveAsNew = document.querySelector("[data-compendium-save-as-new]");
    const saveChanges = document.querySelector("[data-compendium-save-changes]");
    const renameButton = document.querySelector("[data-compendium-rename]");
    const duplicateButton = document.querySelector("[data-compendium-duplicate]");
    const deleteButton = document.querySelector("[data-compendium-delete]");

    const photoModalNode = document.getElementById("compendiumPhotoModal");
    const photoModalProject = document.querySelector("[data-photo-modal-project]");
    const photoPicker = document.querySelector("[data-photo-picker]");
    const photoCropStage = document.querySelector("[data-photo-crop-stage]");
    const photoCropImage = document.querySelector("[data-photo-crop-image]");
    const photoCropFrame = document.querySelector("[data-photo-crop-frame]");
    const photoFocalMarker = document.querySelector("[data-photo-focal-marker]");
    const photoCropEmpty = document.querySelector("[data-photo-crop-empty]");
    const photoCropSelection = document.querySelector("[data-photo-crop-selection]");
    const photoCropQuality = document.querySelector("[data-photo-crop-quality]");
    const photoUseAutomatic = document.querySelector("[data-photo-use-automatic]");
    const photoResetCrop = document.querySelector("[data-photo-reset-crop]");
    const photoManageLink = document.querySelector("[data-photo-manage-link]");

    const bootstrapModal = id => {
        const node = document.getElementById(id);
        return node && window.bootstrap?.Modal ? window.bootstrap.Modal.getOrCreateInstance(node) : null;
    };
    const photoModal = bootstrapModal("compendiumPhotoModal");
    const coverHeroModal = bootstrapModal("compendiumCoverHeroModal");
    const coverHeroPicker = document.querySelector("[data-cover-hero-picker]");
    const coverHeroEmpty = document.querySelector("[data-cover-hero-empty]");
    const discardModal = bootstrapModal("compendiumDiscardModal");
    const saveModal = bootstrapModal("compendiumSaveModal");
    const renameModal = bootstrapModal("compendiumRenameModal");
    const sectionDeleteModal = bootstrapModal("compendiumSectionDeleteModal");
    const sectionDeleteMessage = document.querySelector("[data-section-delete-message]");
    const sectionDeleteConfirm = document.querySelector("[data-section-delete-confirm]");
    const deleteModal = bootstrapModal("compendiumDeleteModal");
    let pendingSectionDeleteKey = null;
    const saveName = document.querySelector("[data-save-name]");
    const saveDescription = document.querySelector("[data-save-description]");
    const saveMessage = document.querySelector("[data-save-message]");
    const renameName = document.querySelector("[data-rename-name]");

    const isSelected = id => orderedIds.includes(Number(id));
    const sectionByKey = key => {
        const clean = cleanSectionKey(key);
        return clean ? customSections.find(section => normalize(section.sectionKey) === normalize(clean)) || null : null;
    };
    const serializeSections = () => customSections.map((section, index) => ({
        sectionKey: section.sectionKey,
        name: section.name,
        sortOrder: index
    }));
    const serializeConfigs = (includeReviewFingerprint = true) => orderedIds.map(id => {
        const config = ensureConfig(id);
        const section = sectionByKey(config.customSectionKey);
        return {
            projectId: id,
            primaryPhotoId: config.imageSelectionMode === "explicit" ? config.primaryPhotoId : null,
            focalX: roundFocal(config.focalX),
            focalY: roundFocal(config.focalY),
            imageSelectionMode: config.imageSelectionMode === "explicit" ? "Explicit" : "Automatic",
            customSectionKey: section?.sectionKey || null,
            customSectionName: section?.name || null,
            narrativeSourceOverride: config.narrativeSourceOverride || null,
            ...(includeReviewFingerprint ? { reviewFingerprint: config.reviewFingerprint || null } : {})
        };
    });

    const syncHidden = () => {
        if (selectedInput) selectedInput.value = orderedIds.join(",");
        if (selectionsInput) selectionsInput.value = JSON.stringify(serializeConfigs(true));
        if (sectionsInput) sectionsInput.value = JSON.stringify(serializeSections());
        if (narrativeInput) narrativeInput.value = editorialState.narrativeSource;
        if (groupingInput) groupingInput.value = editorialState.groupingMode;
        if (sortInput) sortInput.value = editorialState.sortMode;
        if (activeIdInput) activeIdInput.value = activePresetId ? String(activePresetId) : "";
        if (activeVersionInput) activeVersionInput.value = activeRowVersion || "";
        if (coverModeInput) coverModeInput.value = coverState.imageMode === "explicit" ? "Explicit" : coverState.imageMode === "none" ? "None" : "Automatic";
        if (coverProjectInput) coverProjectInput.value = coverState.imageMode === "explicit" && coverState.heroProjectId ? String(coverState.heroProjectId) : "";
        if (coverPhotoInput) coverPhotoInput.value = coverState.imageMode === "explicit" && coverState.heroPhotoId ? String(coverState.heroPhotoId) : "";
        if (coverFocalXInput) coverFocalXInput.value = String(roundFocal(coverState.focalX));
        if (coverFocalYInput) coverFocalYInput.value = String(roundFocal(coverState.focalY));
    };

    const captureSnapshot = () => JSON.stringify({
        title: String(form.elements["Input.Title"]?.value || "").trim(),
        subtitle: String(form.elements["Input.Subtitle"]?.value || "").trim(),
        edition: String(form.elements["Input.Edition"]?.value || "").trim(),
        handlingMarking: String(form.elements["Input.HandlingMarking"]?.value || "").trim(),
        narrativeSource: editorialState.narrativeSource,
        groupingMode: editorialState.groupingMode,
        sortMode: editorialState.sortMode,
        cover: { imageMode: coverState.imageMode, heroProjectId: coverState.imageMode === "explicit" ? coverState.heroProjectId : null, heroPhotoId: coverState.imageMode === "explicit" ? coverState.heroPhotoId : null, focalX: roundFocal(coverState.focalX), focalY: roundFocal(coverState.focalY) },
        sections: serializeSections(),
        projects: serializeConfigs(false)
    });
    const renderDirty = () => {
        const dirty = baselineSnapshot != null && captureSnapshot() !== baselineSnapshot;
        if (presetDirty) {
            presetDirty.hidden = !dirty;
            presetDirty.textContent = canManage ? "Modified" : "Modified locally";
        }
        if (saveChanges) saveChanges.disabled = !activePresetId || !dirty;
        if (renameButton) renameButton.disabled = !activePresetId;
        if (duplicateButton) duplicateButton.disabled = !activePresetId;
        if (deleteButton) deleteButton.disabled = !activePresetId;
        return dirty;
    };
    const markClean = () => {
        baselineSnapshot = captureSnapshot();
        renderDirty();
    };

    const photoPreviewUrl = (projectId, photoId) => {
        const url = new URL(window.location.href); url.search = "";
        url.searchParams.set("handler", "Photo"); url.searchParams.set("projectId", String(projectId));
        url.searchParams.set("photoId", String(photoId)); url.searchParams.set("mode", "source"); url.searchParams.set("v", "0");
        return url.toString();
    };
    const sortProjectIds = ids => {
        const result = [...ids];
        if (editorialState.sortMode === "LatestFirst") {
            return result.sort((a, b) => {
                const pa = projectById.get(a), pb = projectById.get(b);
                const yearDiff = Number(pb?.publicationYear || 0) - Number(pa?.publicationYear || 0);
                return yearDiff || String(pa?.projectName || "").localeCompare(String(pb?.projectName || ""), undefined, { sensitivity: "base" });
            });
        }
        if (editorialState.sortMode === "Alphabetical") {
            return result.sort((a, b) => String(projectById.get(a)?.projectName || "").localeCompare(String(projectById.get(b)?.projectName || ""), undefined, { sensitivity: "base" }));
        }
        return result;
    };
    const publicationGroups = () => {
        if (editorialState.groupingMode === "None") return [{ key: "all", name: "Projects", ids: sortProjectIds(orderedIds), unassigned: false }];

        if (editorialState.groupingMode === "CustomSections") {
            const groups = customSections.map(section => ({
                key: section.sectionKey,
                name: section.name,
                ids: sortProjectIds(orderedIds.filter(id => normalize(ensureConfig(id).customSectionKey) === normalize(section.sectionKey))),
                unassigned: false
            }));
            const known = new Set(customSections.map(section => normalize(section.sectionKey)));
            const unassignedIds = sortProjectIds(orderedIds.filter(id => !ensureConfig(id).customSectionKey || !known.has(normalize(ensureConfig(id).customSectionKey))));
            if (unassignedIds.length) groups.push({ key: "__unassigned", name: "Unassigned", ids: unassignedIds, unassigned: true });
            return groups;
        }

        const groups = [], byName = new Map();
        // Section order is derived from authored/manual order and therefore stays stable when
        // Latest First or A-Z is applied inside each technical category.
        orderedIds.forEach(id => {
            const project = projectById.get(id);
            const name = String(project?.technicalCategory || "").trim() || "Not recorded";
            const key = normalize(name);
            if (!byName.has(key)) { const group = { key, name, ids: [], unassigned: false }; byName.set(key, group); groups.push(group); }
            byName.get(key).ids.push(id);
        });
        groups.forEach(group => { group.ids = sortProjectIds(group.ids); });
        return groups;
    };
    const publicationOrderIds = () => publicationGroups().flatMap(group => group.ids);
    const knownCustomSections = () => customSections.map(section => section.name);
    const effectiveNarrativeSource = id => ensureConfig(id).narrativeSourceOverride || editorialState.narrativeSource;

    const automaticCoverCandidate = () => orderedIds.map(id => ({ id, state: stateFor(id) })).filter(item => item.state?.resolvedPhotoId).sort((a, b) => {
        const rd = Number(Boolean(b.state?.isReviewed)) - Number(Boolean(a.state?.isReviewed)); if (rd) return rd;
        const rank = value => ({ good: 3, acceptable: 2, low: 1 }[normalize(value)] || 0);
        const qd = rank(b.state?.imageQuality) - rank(a.state?.imageQuality); return qd || publicationOrderIds().indexOf(a.id) - publicationOrderIds().indexOf(b.id);
    })[0] || null;
    const renderCoverSetting = () => {
        let projectId = null, photoId = null, focalX = coverState.focalX, focalY = coverState.focalY;
        if (coverState.imageMode === "explicit") { projectId = coverState.heroProjectId; photoId = coverState.heroPhotoId; }
        else if (coverState.imageMode === "automatic") { const candidate = automaticCoverCandidate(); projectId = candidate?.id || null; photoId = candidate?.state?.resolvedPhotoId || null; if (projectId) { const config = ensureConfig(projectId); focalX = config.focalX; focalY = config.focalY; } }
        const project = projectId ? projectById.get(Number(projectId)) : null;
        if (coverStatus) coverStatus.textContent = coverState.imageMode === "none" ? "No imagery" : coverState.imageMode === "explicit" ? `Selected hero · ${project?.projectName || "Project image"}` : "Automatic hero";
        if (coverDetail) coverDetail.textContent = coverState.imageMode === "none" ? "The cover will use the institutional graphic treatment without project imagery." : coverState.imageMode === "explicit" ? "This hero is independent of project order. Re-select from Review to copy a newer project crop." : "PRISM uses the strongest available reviewed publication image from the selected projects.";
        if (coverPreviewImage && coverPreview) {
            if (projectId && photoId) { coverPreviewImage.src = photoPreviewUrl(projectId, photoId); coverPreviewImage.style.objectPosition = `${clamp(focalX) * 100}% ${clamp(focalY) * 100}%`; coverPreviewImage.alt = `${project?.projectName || "Compendium"} cover hero`; coverPreviewImage.hidden = false; coverPreview.classList.add("has-image"); }
            else { coverPreviewImage.hidden = true; coverPreviewImage.removeAttribute("src"); coverPreview.classList.remove("has-image"); }
        }
        coverAutomatic?.classList.toggle("active", coverState.imageMode === "automatic");
        coverChoose?.classList.toggle("active", coverState.imageMode === "explicit");
        coverNone?.classList.toggle("active", coverState.imageMode === "none");
    };
    const coverChanged = () => { syncHidden(); renderCoverSetting(); renderDirty(); schedulePreflight(); };

    const visibleRows = () => rows.filter(row => !row.hidden);
    const applyFilters = () => {
        const term = normalize(search?.value);
        const life = normalize(lifecycle?.value);
        const cat = normalize(category?.value);
        const tech = normalize(technical?.value);
        const prol = normalize(proliferation?.value);
        const only = Boolean(selectedOnly?.checked);
        let count = 0;
        rows.forEach(row => {
            const id = Number(row.dataset.id);
            const visible = (!term || normalize(row.dataset.name).includes(term))
                && (!life || normalize(row.dataset.lifecycle) === life)
                && (!cat || normalize(row.dataset.category) === cat)
                && (!tech || normalize(row.dataset.technical) === tech)
                && (!prol || normalize(row.dataset.proliferation) === prol)
                && (!only || isSelected(id));
            row.hidden = !visible;
            if (visible) count++;
        });
        if (matchingCount) matchingCount.textContent = String(count);
        if (selectMatching) {
            const selectable = visibleRows().filter(row => !isSelected(Number(row.dataset.id))).length;
            selectMatching.disabled = selectable === 0;
            selectMatching.textContent = selectable > 100
                ? "Select first 100 matching"
                : selectable === 1 ? "Select 1 matching" : `Select ${selectable} matching`;
        }
    };

    const updateCheckboxes = () => rows.forEach(row => {
        const id = Number(row.dataset.id);
        const box = row.querySelector("[data-project-checkbox]");
        if (box instanceof HTMLInputElement) box.checked = isSelected(id);
        row.classList.toggle("is-selected", isSelected(id));
    });

    const stateFor = id => projectStateById.get(Number(id)) || null;
    const findingsFor = id => (lastPreflight?.findings || []).filter(finding => Number(finding.projectId) === Number(id));
    const hasFinding = (id, predicate) => findingsFor(id).some(predicate);
    const visualProjectState = (id, fallbackState = null) => {
        const state = stateFor(id) || fallbackState || {};
        const findings = findingsFor(id);
        if (findings.some(finding => finding.severity === "blocker")) return "blocker";
        if (state.isReviewStale || !state.isReviewed) return "review";
        if (findings.some(finding => finding.severity === "warning")) return "warning";
        return "ready";
    };
    const orderStateMarkup = id => {
        const state = visualProjectState(id);
        if (state === "blocker") return '<span class="compendium-order-state is-blocker" title="Publication blocker" aria-label="Publication blocker"><i class="bi bi-x-octagon-fill" aria-hidden="true"></i></span>';
        if (state === "warning") return '<span class="compendium-order-state is-warning" title="Reviewed with warning" aria-label="Reviewed with warning"><i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i></span>';
        if (state === "review") return '<span class="compendium-order-state is-review" title="Review required" aria-label="Review required"><i class="bi bi-circle" aria-hidden="true"></i></span>';
        return '<span class="compendium-order-state is-ready" title="Ready" aria-label="Ready"><i class="bi bi-check-circle-fill" aria-hidden="true"></i></span>';
    };

    const renderEditorialControls = () => {
        narrativeButtons.forEach(button => button.classList.toggle("active", normalizeNarrative(button.dataset.narrativeValue) === editorialState.narrativeSource));
        groupingButtons.forEach(button => button.classList.toggle("active", normalizeGrouping(button.dataset.groupingValue) === editorialState.groupingMode));
        sortButtons.forEach(button => button.classList.toggle("active", normalizeSort(button.dataset.sortValue) === editorialState.sortMode));
        const customMode = editorialState.groupingMode === "CustomSections";
        if (customSectionToolbar) customSectionToolbar.hidden = !customMode;
        form.closest(".compendium-builder-page")?.classList.toggle("is-custom-structure", customMode);
        if (customSectionSummary) {
            const unassigned = orderedIds.filter(id => !sectionByKey(ensureConfig(id).customSectionKey)).length;
            customSectionSummary.textContent = customSections.length
                ? `${customSections.length} section${customSections.length === 1 ? "" : "s"}${unassigned ? ` · ${unassigned} unassigned` : ""}.`
                : "No custom sections yet.";
        }
        if (orderModeCopy) orderModeCopy.textContent = editorialState.sortMode === "Manual" ? "manual order" : editorialState.sortMode === "LatestFirst" ? "latest first within sections" : "A–Z within sections";
        if (orderNote) {
            orderNote.textContent = editorialState.groupingMode === "TechnicalCategory"
                ? "Technical-category section order remains stable; the selected order mode is applied within each section."
                : customMode
                    ? "Custom section order is authored independently. Latest First and A–Z only reorder projects inside each section."
                    : "Projects publish as one continuous catalogue; the selected order mode applies to the complete project stream.";
        }
        if (composerNote) composerNote.textContent = customMode
            ? "Custom sections are publication metadata only. Empty sections are preserved when saved; project master data is never changed."
            : "Technical categories remain authoritative project data. Publication composition only changes this Compendium.";
        form.querySelectorAll("[data-source-readiness]").forEach(node => node.classList.toggle("is-source-active", normalizeNarrative(node.dataset.sourceReadiness) === editorialState.narrativeSource));
    };

    const renderOrder = () => {
        renderEditorialControls();
        if (selectedCount) selectedCount.textContent = String(orderedIds.length);
        if (railCount) railCount.textContent = String(orderedIds.length);
        if (clearSelection) { const empty = orderedIds.length === 0; clearSelection.hidden = empty; setControlDisabled(clearSelection, empty); }
        if (!orderList) return;

        const manual = editorialState.sortMode === "Manual";
        const custom = editorialState.groupingMode === "CustomSections";
        const groups = publicationGroups();
        if (orderedIds.length === 0 && !customSections.length) {
            orderList.innerHTML = '<div class="compendium-order-empty"><i class="bi bi-journal"></i><span>Select projects from the portfolio.</span></div>';
            return;
        }

        const order = publicationOrderIds();
        const htmlForItem = (id, group, indexInGroup) => {
            const project = projectById.get(id); if (!project) return "";
            const config = ensureConfig(id);
            const sectionSelect = custom ? `<select class="compendium-order-section-select" data-section-select aria-label="Publication section for ${escapeHtml(project.projectName)}">
                <option value="">Unassigned</option>${customSections.map(section => `<option value="${escapeHtml(section.sectionKey)}" ${normalize(config.customSectionKey) === normalize(section.sectionKey) ? "selected" : ""}>${escapeHtml(section.name)}</option>`).join("")}
            </select>` : "";
            const orderIcon = manual ? "bi-grip-vertical" : editorialState.sortMode === "LatestFirst" ? "bi-calendar3" : "bi-sort-alpha-down";
            return `<div class="compendium-order-item${id === activeReviewId ? " is-active" : ""}${manual ? "" : " is-auto-ordered"}" data-order-id="${id}" draggable="${manual ? "true" : "false"}">
                <span class="compendium-order-handle" aria-label="${manual ? "Drag to reorder" : "Automatic project order"}"><i class="bi ${orderIcon}"></i></span>
                <div class="compendium-order-main">
                    <button class="compendium-order-copy" type="button" data-order-review title="Review ${escapeHtml(project.projectName)}"><strong>${escapeHtml(project.projectName)}</strong><small>${escapeHtml(project.technicalCategory || "Technical category not recorded")} · ${escapeHtml(project.lifecycle)}${project.publicationYear ? ` · ${escapeHtml(project.publicationYear)}` : ""}</small></button>
                    ${sectionSelect}
                </div>
                <div class="compendium-order-actions">${orderStateMarkup(id)}<button type="button" data-move-up title="Move up within section" ${!manual || indexInGroup === 0 ? "disabled" : ""}><i class="bi bi-chevron-up"></i></button><button type="button" data-move-down title="Move down within section" ${!manual || indexInGroup === group.ids.length - 1 ? "disabled" : ""}><i class="bi bi-chevron-down"></i></button><button type="button" data-remove title="Remove from Compendium"><i class="bi bi-x-lg"></i></button></div>
            </div>`;
        };

        if (editorialState.groupingMode === "None") {
            orderList.innerHTML = order.map((id, index) => htmlForItem(id, groups[0], index)).join("");
            return;
        }

        orderList.innerHTML = groups.map((group, groupIndex) => {
            const realSection = custom && !group.unassigned ? sectionByKey(group.key) : null;
            const customHeader = custom
                ? (group.unassigned
                    ? `<div class="compendium-order-group__identity"><span>Unassigned</span><small>Assign before final issue where possible</small></div>`
                    : `<div class="compendium-order-group__custom"><span class="compendium-order-section-grip" draggable="true" data-section-drag-handle data-section-key="${escapeHtml(group.key)}" title="Drag to reorder section"><i class="bi bi-grip-vertical"></i></span><input value="${escapeHtml(group.name)}" maxlength="120" data-section-rename data-section-key="${escapeHtml(group.key)}" aria-label="Rename section ${escapeHtml(group.name)}"><button type="button" data-section-group-up data-section-key="${escapeHtml(group.key)}" title="Move section up" ${groupIndex === 0 ? "disabled" : ""}><i class="bi bi-arrow-up"></i></button><button type="button" data-section-group-down data-section-key="${escapeHtml(group.key)}" title="Move section down" ${groupIndex >= customSections.length - 1 ? "disabled" : ""}><i class="bi bi-arrow-down"></i></button><button type="button" data-section-delete data-section-key="${escapeHtml(group.key)}" title="Delete section"><i class="bi bi-trash3"></i></button></div>`)
                : `<span>${escapeHtml(group.name)}</span>`;
            const empty = group.ids.length === 0 ? `<div class="compendium-order-group__empty" data-section-drop-zone>Drop projects here</div>` : "";
            return `<section class="compendium-order-group${group.unassigned ? " is-unassigned" : ""}" data-section-group="${escapeHtml(group.key)}" data-section-key="${escapeHtml(group.key)}"><header>${customHeader}<small>${group.ids.length} project${group.ids.length === 1 ? "" : "s"}</small></header>${empty}${group.ids.map((id, index) => htmlForItem(id, group, index)).join("")}</section>`;
        }).join("");
    };

    const qualityLabel = (quality, dpi) => {
        if (quality === "good") return dpi ? `Good · ${dpi} DPI` : "Good";
        if (quality === "acceptable") return dpi ? `Usable · ${dpi} DPI` : "Usable";
        if (quality === "low") return dpi ? `Low · ${dpi} DPI` : "Low resolution";
        return "Quality unavailable";
    };
    const availabilityLabel = value => value === true
        ? "Available for proliferation"
        : value === false ? "Not available for proliferation" : "Not assessed";
    const renderInlineMarkdown = value => escapeHtml(value)
        .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
        .replace(/__([^_]+)__/g, "<strong>$1</strong>")
        .replace(/`([^`]+)`/g, "<code>$1</code>")
        .replace(/\*([^*\n]+)\*/g, "<em>$1</em>");
    const formatDescription = value => {
        const text = String(value || "").replace(/\r\n/g, "\n").trim();
        if (!text) return '<span class="compendium-review-missing">Not recorded</span>';

        const lines = text.split("\n");
        const html = [];
        let list = null;
        const closeList = () => {
            if (!list) return;
            html.push(`</${list}>`);
            list = null;
        };

        for (const rawLine of lines) {
            const line = rawLine.trim();
            if (!line) { closeList(); continue; }
            const bullet = line.match(/^[-*]\s+(.+)$/);
            const numbered = line.match(/^\d+[.)]\s+(.+)$/);
            if (bullet || numbered) {
                const desired = bullet ? "ul" : "ol";
                if (list !== desired) { closeList(); list = desired; html.push(`<${list}>`); }
                html.push(`<li>${renderInlineMarkdown((bullet || numbered)[1])}</li>`);
                continue;
            }
            closeList();
            const heading = line.match(/^#{1,4}\s+(.+)$/);
            if (heading) { html.push(`<p class="compendium-review-markdown-heading">${renderInlineMarkdown(heading[1])}</p>`); continue; }
            html.push(`<p>${renderInlineMarkdown(line)}</p>`);
        }
        closeList();
        return html.join("");
    };

    const renderReviewLoading = id => {
        const project = projectById.get(id);
        if (!project) return;
        if (reviewEmpty) reviewEmpty.hidden = true;
        if (reviewCard) reviewCard.hidden = false;
        const reviewOrder = publicationOrderIds();
        const ordinal = reviewOrder.indexOf(id) + 1;
        if (reviewOrdinal) reviewOrdinal.textContent = `PROJECT ${ordinal} OF ${reviewOrder.length}`;
        if (reviewName) reviewName.textContent = project.projectName;
        if (reviewMeta) reviewMeta.textContent = `${project.lifecycle} · ${project.technicalCategory || "Technical category not recorded"}`;
        if (reviewState) { reviewState.textContent = "Loading…"; reviewState.className = "compendium-review-state is-loading"; }
        if (reviewFacts) reviewFacts.innerHTML = '<div class="compendium-review-loading">Loading current project information…</div>';
        if (reviewDescription) reviewDescription.innerHTML = '<span class="compendium-review-loading-line"></span><span class="compendium-review-loading-line"></span>';
        setControlDisabled(reviewMarkReviewed, true);
        setControlDisabled(reviewChangeImage, true);
        setControlDisabled(reviewAdjustCrop, true);
    };

    const currentReviewPhoto = review => {
        if (!review?.resolvedPhotoId || !Array.isArray(review.photos)) return null;
        return review.photos.find(photo => Number(photo.photoId) === Number(review.resolvedPhotoId)) || null;
    };

    const renderReviewData = review => {
        if (!review || Number(review.projectId) !== Number(activeReviewId)) return;
        activeReviewData = review;
        const config = ensureConfig(review.projectId);
        const state = stateFor(review.projectId);
        const reviewed = state ? Boolean(state.isReviewed) : Boolean(review.isReviewed);
        const stale = state ? Boolean(state.isReviewStale) : Boolean(review.isReviewStale);
        const reviewOrder = publicationOrderIds();
        const ordinal = reviewOrder.indexOf(Number(review.projectId)) + 1;
        if (reviewOrdinal) reviewOrdinal.textContent = `PROJECT ${ordinal} OF ${reviewOrder.length}`;
        if (reviewName) reviewName.textContent = review.projectName;
        if (reviewMeta) reviewMeta.textContent = `${review.lifecycleDisplay} · ${review.technicalCategoryName || "Technical category not recorded"}`;

        const visualState = visualProjectState(review.projectId, { isReviewed: reviewed, isReviewStale: stale });
        if (reviewState) {
            reviewState.className = "compendium-review-state";
            if (visualState === "blocker") { reviewState.textContent = "Blocked"; reviewState.classList.add("is-blocker"); }
            else if (visualState === "review") { reviewState.textContent = "Review required"; reviewState.classList.add(stale ? "is-warning" : "is-required"); }
            else if (visualState === "warning") { reviewState.textContent = "Warning"; reviewState.classList.add("is-warning"); }
            else { reviewState.textContent = "Ready"; reviewState.classList.add("is-reviewed"); }
        }

        const facts = [
            ["Lifecycle", review.lifecycleDisplay || "Not recorded"],
            ["Project category", review.projectCategoryName || "Not recorded"],
            ["Technical category", review.technicalCategoryName || "Not recorded"],
            ["Arm / Service", review.armServiceDisplay || "Not recorded"]
        ];
        if (normalize(review.lifecycleDisplay) === "completed" && String(review.completionDisplay || "").trim()) facts.push(["Completed", review.completionDisplay]);
        if (review.proliferationAvailability !== null && review.proliferationAvailability !== undefined) facts.push(["Proliferation", availabilityLabel(review.proliferationAvailability)]);
        if (review.proliferationAvailability === true || review.proliferationCostLakhs != null) facts.push(["Indicative cost", review.proliferationCostDisplay || "Not recorded"]);
        if (reviewFacts) reviewFacts.innerHTML = facts.map(([label, value]) => `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`).join("");
        if (reviewDescription) reviewDescription.innerHTML = formatDescription(review.descriptionMarkdown);
        if (reviewNarrativeLabel) reviewNarrativeLabel.textContent = review.narrativeLabel || "Project Brief";
        if (reviewDescriptionState) reviewDescriptionState.textContent = String(review.descriptionMarkdown || "").trim() ? "Current PRISM content" : "Missing";
        if (reviewNarrativeOptions) {
            const options = [
                ["ProjectBrief", "Project Brief", Boolean(review.hasProjectBrief), Number(review.projectBriefWordCount || 0) ? `${review.projectBriefWordCount} words` : "Not recorded"],
                ["CapabilityOverview", "Capability Overview", Boolean(review.hasCapabilityOverview), Number(review.capabilityStatementCount || 0) ? `${review.capabilityStatementCount} statements` : "Not recorded"],
                ["ProjectDescription", "Project Description", Boolean(review.hasProjectDescription), Number(review.descriptionWordCount || 0) ? `${review.descriptionWordCount} words` : "Not recorded"]
            ];
            const effectiveSource = normalizeNarrative(review.narrativeSource || effectiveNarrativeSource(review.projectId));
            const override = ensureConfig(review.projectId).narrativeSourceOverride;
            const defaultLabel = { ProjectBrief: "Project Brief", CapabilityOverview: "Capability Overview", ProjectDescription: "Project Description" }[editorialState.narrativeSource] || "Project Brief";
            reviewNarrativeOptions.innerHTML = `<div class="compendium-review-narrative-context"><span>Publication default · <strong>${escapeHtml(defaultLabel)}</strong></span>${override ? '<button type="button" data-review-narrative-default>Use publication default</button>' : '<small>Inherited</small>'}</div>`
                + options.map(([value,label,available,detail]) => `<button type="button" data-review-narrative-value="${value}" class="${effectiveSource === value ? "active" : ""} ${available ? "is-available" : "is-missing"}"><span>${escapeHtml(label)}</span><small>${escapeHtml(detail)}${editorialState.narrativeSource !== value && effectiveSource === value ? " · override" : ""}</small></button>`).join("");
        }

        if (reviewOpen) reviewOpen.href = review.projectUrl || `/Projects/Overview?id=${review.projectId}`;
        if (reviewManagePhotos) reviewManagePhotos.href = review.photosUrl || `/Projects/Photos/Index?id=${review.projectId}`;
        if (reviewEdit) {
            reviewEdit.hidden = !review.completedEditUrl;
            if (review.completedEditUrl) reviewEdit.href = review.completedEditUrl;
        }

        if (reviewImageFrame) { const fw = Number(review.imageFrameWidthPoints || frameWidthPoints) || frameWidthPoints; const fh = Number(review.imageFrameHeightPoints || frameHeightPoints) || frameHeightPoints; reviewImageFrame.style.aspectRatio = `${fw} / ${fh}`; }
        const photo = currentReviewPhoto(review);
        if (reviewImage && reviewImageEmpty) {
            if (photo?.previewUrl) {
                reviewImage.src = photo.previewUrl;
                reviewImage.style.objectPosition = `${clamp(config.focalX) * 100}% ${clamp(config.focalY) * 100}%`;
                reviewImage.alt = `${review.projectName} publication photograph`;
                reviewImage.hidden = false;
                reviewImageEmpty.hidden = true;
            } else {
                reviewImage.removeAttribute("src");
                reviewImage.hidden = true;
                reviewImageEmpty.hidden = false;
            }
        }
        if (reviewImageMode) {
            reviewImageMode.textContent = config.imageSelectionMode === "explicit" ? "Locked image" : "Automatic image";
            reviewImageMode.className = `compendium-image-badge ${config.imageSelectionMode === "explicit" ? "is-explicit" : "is-automatic"}`;
        }
        if (reviewImageQuality) {
            const quality = normalize(review.imageQuality);
            reviewImageQuality.textContent = qualityLabel(quality, review.effectiveDpi);
            reviewImageQuality.className = `compendium-image-badge is-${quality || "unknown"}`;
        }
        if (reviewImageDetail) {
            if (!photo) reviewImageDetail.textContent = "No usable photograph is currently available.";
            else if (review.explicitPhotoUnavailable) reviewImageDetail.textContent = "The saved image is unavailable; PRISM is temporarily showing the current automatic choice.";
            else reviewImageDetail.textContent = `${photo.width}×${photo.height} source · ${review.photoSelectionSource === "explicitpublication" ? "publication selection" : "current project selection"}`;
        }
        setControlDisabled(reviewChangeImage, false);
        setControlDisabled(reviewAdjustCrop, !photo);
        setControlDisabled(reviewUseCover, !photo);
        if (reviewUseAutomatic) reviewUseAutomatic.hidden = config.imageSelectionMode !== "explicit";

        if (reviewFooterTitle && reviewFooterCopy && reviewMarkReviewed) {
            if (stale) {
                reviewFooterTitle.textContent = "Review again";
                reviewFooterCopy.textContent = "Project facts or publication imagery changed after the previous review.";
                setControlDisabled(reviewMarkReviewed, false);
                reviewMarkReviewed.innerHTML = `<i class="bi bi-check2-circle" aria-hidden="true"></i> ${nextUnreviewedId(review.projectId) ? "Review & next" : "Finish review"}`;
                reviewMarkReviewed.title = nextUnreviewedId(review.projectId) ? "Review & next · Ctrl+Enter" : "Finish review · Ctrl+Enter";
            } else if (!reviewed) {
                reviewFooterTitle.textContent = "Review required";
                reviewFooterCopy.textContent = visualState === "blocker"
                    ? "Resolve the publication blocker, then confirm the current project version."
                    : "Confirm the current project facts and publication image before final issue.";
                setControlDisabled(reviewMarkReviewed, visualState === "blocker");
                reviewMarkReviewed.innerHTML = `<i class="bi bi-check2-circle" aria-hidden="true"></i> ${nextUnreviewedId(review.projectId) ? "Review & next" : "Finish review"}`;
                reviewMarkReviewed.title = nextUnreviewedId(review.projectId) ? "Review & next · Ctrl+Enter" : "Finish review · Ctrl+Enter";
            } else if (visualState === "warning") {
                reviewFooterTitle.textContent = "Reviewed with warnings";
                reviewFooterCopy.textContent = "The current version is reviewed; resolve the remaining publication-quality warning where practical.";
                setControlDisabled(reviewMarkReviewed, true);
                reviewMarkReviewed.innerHTML = '<i class="bi bi-check-circle-fill" aria-hidden="true"></i> Reviewed';
            } else {
                reviewFooterTitle.textContent = "Ready for publication";
                reviewFooterCopy.textContent = "The current version is reviewed and has no publication warnings.";
                setControlDisabled(reviewMarkReviewed, true);
                reviewMarkReviewed.innerHTML = '<i class="bi bi-check-circle-fill" aria-hidden="true"></i> Reviewed';
            }
        }
        updateReviewNavigation();
    };

    const refreshReviewProgress = () => {
        const reviewed = orderedIds.filter(id => stateFor(id)?.isReviewed).length;
        if (reviewProgress) reviewProgress.textContent = `${reviewed} of ${orderedIds.length} reviewed`;
        if (readyReviewed) readyReviewed.textContent = String(reviewed);
    };

    const loadReview = async (id = activeReviewId) => {
        const projectId = Number(id || 0);
        if (!projectId || !isSelected(projectId)) {
            activeReviewData = null;
            if (reviewEmpty) reviewEmpty.hidden = false;
            if (reviewCard) reviewCard.hidden = true;
            refreshReviewProgress();
            return null;
        }
        activeReviewId = projectId;
        renderOrder();
        renderReviewLoading(projectId);
        reviewRequestController?.abort();
        reviewRequestController = new AbortController();
        const revision = ++reviewRequestRevision;
        syncHidden();
        const payload = new FormData(form);
        payload.set("projectId", String(projectId));
        try {
            const response = await fetch(form.dataset.reviewUrl, {
                method: "POST",
                body: payload,
                signal: reviewRequestController.signal,
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            const body = await response.json().catch(() => ({}));
            if (revision !== reviewRequestRevision) return null;
            if (!response.ok) throw new Error(body.message || "The project review could not be loaded.");
            renderReviewData(body);
            renderFindings();
            return body;
        } catch (error) {
            if (error?.name === "AbortError") return null;
            if (revision !== reviewRequestRevision) return null;
            if (reviewFacts) reviewFacts.innerHTML = `<div class="compendium-review-load-error">${escapeHtml(error.message || "The project review could not be loaded.")}</div>`;
            return null;
        }
    };

    const scheduleReviewRefresh = () => {
        window.clearTimeout(reviewRefreshTimer);
        reviewRefreshTimer = window.setTimeout(() => loadReview(activeReviewId), 180);
    };

    const navigateReview = offset => {
        const reviewOrder = publicationOrderIds();
        if (reviewOrder.length <= 1) return;
        let index = reviewOrder.indexOf(Number(activeReviewId));
        if (index < 0) index = 0;
        activeReviewId = reviewOrder[(index + offset + reviewOrder.length) % reviewOrder.length];
        loadReview(activeReviewId);
    };

    const isReviewedNow = id => { const state = stateFor(id); if (state) return Boolean(state.isReviewed) && !state.isReviewStale; return Boolean(ensureConfig(id).reviewFingerprint); };
    const nextUnreviewedId = (fromId = activeReviewId, skipCurrent = true) => {
        const reviewOrder = publicationOrderIds(); if (!reviewOrder.length) return null; const currentIndex = Math.max(0, reviewOrder.indexOf(Number(fromId)));
        for (let delta = skipCurrent ? 1 : 0; delta < reviewOrder.length + (skipCurrent ? 0 : 1); delta++) { const id = reviewOrder[(currentIndex + delta) % reviewOrder.length]; if (!isReviewedNow(id)) return id; }
        return null;
    };
    const nextWarningId = (fromId = activeReviewId) => {
        const reviewOrder = publicationOrderIds(); if (!reviewOrder.length) return null; const currentIndex = Math.max(0, reviewOrder.indexOf(Number(fromId)));
        for (let delta = 1; delta <= reviewOrder.length; delta++) { const id = reviewOrder[(currentIndex + delta) % reviewOrder.length]; if (findingsFor(id).some(finding => finding.severity === "blocker" || finding.severity === "warning")) return id; }
        return null;
    };
    const nextAttentionId = () => nextUnreviewedId(activeReviewId, true) || nextWarningId(activeReviewId);
    const updateReviewNavigation = () => {
        const canCycle = orderedIds.length > 1; setControlDisabled(reviewPrevious, !canCycle); setControlDisabled(reviewNext, !canCycle);
        const hasUnreviewed = orderedIds.some(id => !isReviewedNow(id)); const attentionId = hasUnreviewed ? nextUnreviewedId(activeReviewId, true) : nextWarningId(activeReviewId);
        setControlDisabled(reviewNextAttention, attentionId == null);
        if (reviewNextAttention) reviewNextAttention.textContent = orderedIds.length === 0 ? "Next requiring attention" : attentionId == null ? "No further attention" : hasUnreviewed ? "Next requiring attention" : "Review warnings";
    };

    const goNextAttention = () => {
        const id = nextAttentionId();
        if (!id) return;
        activeReviewId = id;
        loadReview(id);
    };

    const invalidateProjectReview = id => {
        const config = ensureConfig(id);
        config.reviewFingerprint = null;
        const state = stateFor(id);
        if (state) projectStateById.set(Number(id), { ...state, isReviewed: false, isReviewStale: false });
    };

    const publicationConfigChanged = (id, { refreshReview = true } = {}) => {
        invalidateProjectReview(id);
        syncHidden();
        renderDirty();
        renderOrder();
        refreshReviewProgress();
        updateReviewNavigation();
        if (activeReviewData && Number(activeReviewData.projectId) === Number(id)) {
            renderReviewData({ ...activeReviewData, isReviewed: false, isReviewStale: false });
        }
        schedulePreflight();
        if (refreshReview && Number(activeReviewId) === Number(id)) scheduleReviewRefresh();
    };

    const publicationStructureChanged = ({ refreshReview = false } = {}) => {
        syncHidden(); renderDirty(); renderOrder(); updateReviewNavigation(); schedulePreflight();
        if (refreshReview && activeReviewId) scheduleReviewRefresh();
    };

    const changeNarrativeSource = value => {
        const next = normalizeNarrative(value);
        if (next === editorialState.narrativeSource) return;
        editorialState.narrativeSource = next;
        orderedIds.filter(id => !ensureConfig(id).narrativeSourceOverride).forEach(invalidateProjectReview);
        activeReviewData = null;
        syncHidden(); renderDirty(); renderOrder(); refreshReviewProgress(); updateReviewNavigation(); schedulePreflight();
        if (activeReviewId) loadReview(activeReviewId);
    };

    const setProjectNarrativeSource = (projectId, value) => {
        const id = Number(projectId); if (!id || !isSelected(id)) return;
        const config = ensureConfig(id);
        const next = value ? normalizeNarrative(value) : null;
        config.narrativeSourceOverride = next && next !== editorialState.narrativeSource ? next : null;
        invalidateProjectReview(id);
        activeReviewData = null;
        syncHidden(); renderDirty(); renderOrder(); refreshReviewProgress(); updateReviewNavigation(); schedulePreflight();
        if (Number(activeReviewId) === id) loadReview(id);
    };

    const activeFrame = () => ({ width: Number(activeReviewData?.imageFrameWidthPoints || frameWidthPoints) || frameWidthPoints, height: Number(activeReviewData?.imageFrameHeightPoints || frameHeightPoints) || frameHeightPoints });
    const effectiveDpi = photo => {
        const width = Number(photo?.width || 0), height = Number(photo?.height || 0); if (!width || !height) return null;
        const frame = activeFrame(), aspect = frame.width / frame.height, sourceAspect = width / height; let cropWidth, cropHeight;
        if (sourceAspect > aspect) { cropHeight = height; cropWidth = cropHeight * aspect; } else { cropWidth = width; cropHeight = cropWidth / aspect; }
        return Math.floor(Math.min(cropWidth / (frame.width / 72), cropHeight / (frame.height / 72)));
    };
    const classifyDpi = dpi => dpi == null ? "unknown" : dpi < 150 ? "low" : dpi < 180 ? "acceptable" : "good";

    const sourceMetrics = (stage, image) => {
        const stageWidth = stage?.clientWidth || 0, stageHeight = stage?.clientHeight || 0;
        const sourceWidth = image?.naturalWidth || 0, sourceHeight = image?.naturalHeight || 0;
        if (!stageWidth || !stageHeight || !sourceWidth || !sourceHeight) return null;
        const scale = Math.min(stageWidth / sourceWidth, stageHeight / sourceHeight);
        const renderedWidth = sourceWidth * scale, renderedHeight = sourceHeight * scale;
        return { sourceWidth, sourceHeight, scale, renderedWidth, renderedHeight, offsetX: (stageWidth-renderedWidth)/2, offsetY: (stageHeight-renderedHeight)/2 };
    };
    const cropForFocal = (sourceWidth, sourceHeight, focalX, focalY) => {
        const sourceAspect = sourceWidth / sourceHeight;
        let cropWidth, cropHeight;
        const aspect = activeFrame().width / activeFrame().height;
        if (sourceAspect > aspect) { cropHeight = sourceHeight; cropWidth = cropHeight * aspect; }
        else { cropWidth = sourceWidth; cropHeight = cropWidth / aspect; }
        cropWidth = Math.min(cropWidth, sourceWidth); cropHeight = Math.min(cropHeight, sourceHeight);
        const x = Math.max(0, Math.min(sourceWidth-cropWidth, clamp(focalX)*sourceWidth - cropWidth/2));
        const y = Math.max(0, Math.min(sourceHeight-cropHeight, clamp(focalY)*sourceHeight - cropHeight/2));
        return { x, y, width: cropWidth, height: cropHeight };
    };
    const positionCropOverlay = () => {
        if (!photoCropStage || !photoCropImage || !photoCropFrame || !photoFocalMarker || !activeReviewId) return;
        const metrics = sourceMetrics(photoCropStage, photoCropImage);
        if (!metrics) return;
        const config = ensureConfig(activeReviewId);
        const crop = cropForFocal(metrics.sourceWidth, metrics.sourceHeight, config.focalX, config.focalY);
        photoFocalMarker.style.left = `${metrics.offsetX + clamp(config.focalX)*metrics.renderedWidth}px`;
        photoFocalMarker.style.top = `${metrics.offsetY + clamp(config.focalY)*metrics.renderedHeight}px`;
        photoCropFrame.style.left = `${metrics.offsetX + crop.x*metrics.scale}px`;
        photoCropFrame.style.top = `${metrics.offsetY + crop.y*metrics.scale}px`;
        photoCropFrame.style.width = `${crop.width*metrics.scale}px`;
        photoCropFrame.style.height = `${crop.height*metrics.scale}px`;
    };

    const photoForModal = () => {
        if (!activeReviewData || !activeReviewId) return null;
        const config = ensureConfig(activeReviewId);
        const id = config.imageSelectionMode === "explicit" && config.primaryPhotoId
            ? config.primaryPhotoId
            : activeReviewData.resolvedPhotoId;
        return activeReviewData.photos?.find(photo => Number(photo.photoId) === Number(id)) || null;
    };
    const renderPhotoModal = () => {
        if (!activeReviewData || !activeReviewId) return;
        const config = ensureConfig(activeReviewId);
        if (photoModalProject) photoModalProject.textContent = activeReviewData.projectName;
        if (photoManageLink) photoManageLink.href = activeReviewData.photosUrl || `/Projects/Photos/Index?id=${activeReviewId}`;
        const resolvedId = Number(activeReviewData.resolvedPhotoId || 0);
        if (photoPicker) {
            const photos = Array.isArray(activeReviewData.photos) ? activeReviewData.photos : [];
            if (!photos.length) {
                photoPicker.innerHTML = '<div class="compendium-photo-picker-empty"><i class="bi bi-image"></i><strong>No project photographs</strong><span>Add a suitable photograph to the project, then return to the Compendium.</span></div>';
            } else {
                photoPicker.innerHTML = photos.map(photo => {
                    const explicitSelected = config.imageSelectionMode === "explicit" && Number(config.primaryPhotoId) === Number(photo.photoId);
                    const automaticCurrent = config.imageSelectionMode === "automatic" && Number(photo.photoId) === resolvedId;
                    const dpi = effectiveDpi(photo), quality = classifyDpi(dpi);
                    return `<button type="button" class="compendium-photo-choice${explicitSelected ? " is-selected" : ""}${automaticCurrent ? " is-automatic" : ""}" data-photo-id="${photo.photoId}" ${photo.isUsable === false ? "disabled" : ""}>
                        <span class="compendium-photo-choice__image"><img src="${escapeHtml(photo.thumbnailUrl || photo.previewUrl || "")}" alt="" loading="lazy" /></span>
                        <span class="compendium-photo-choice__copy"><strong>${escapeHtml(photo.caption || `Project photograph ${photo.photoId}`)}</strong><small>${photo.width}×${photo.height} · ${escapeHtml(qualityLabel(quality, dpi))}</small></span>
                        <span class="compendium-photo-choice__badges">${photo.isCover ? '<span>Cover</span>' : ''}${automaticCurrent ? '<span>Automatic</span>' : ''}${explicitSelected ? '<span>Selected</span>' : ''}</span>
                    </button>`;
                }).join("");
            }
        }
        const photo = photoForModal();
        if (photoCropImage && photoCropEmpty && photoCropStage) {
            if (photo?.previewUrl) {
                photoCropEmpty.hidden = true;
                photoCropImage.hidden = false;
                photoCropImage.onload = positionCropOverlay;
                photoCropImage.src = photo.previewUrl;
                if (photoCropImage.complete && photoCropImage.naturalWidth > 0) positionCropOverlay();
            } else {
                photoCropImage.removeAttribute("src");
                photoCropImage.hidden = true;
                photoCropEmpty.hidden = false;
            }
        }
        const dpi = effectiveDpi(photo), quality = classifyDpi(dpi);
        if (photoCropSelection) photoCropSelection.textContent = photo
            ? `${config.imageSelectionMode === "explicit" ? "Locked publication image" : "Automatic publication image"} · focal ${Math.round(config.focalX*100)}% / ${Math.round(config.focalY*100)}%`
            : "No publication image selected";
        if (photoCropQuality) photoCropQuality.textContent = photo ? qualityLabel(quality, dpi) : "";
        if (photoResetCrop) photoResetCrop.disabled = !photo;
    };
    const openPhotoEditor = async focusCrop => {
        if (!activeReviewId) return;
        const review = activeReviewData && Number(activeReviewData.projectId) === Number(activeReviewId)
            ? activeReviewData
            : await loadReview(activeReviewId);
        if (!review) return;
        renderPhotoModal();
        photoModal?.show();
        if (focusCrop) window.setTimeout(() => photoCropStage?.scrollIntoView({ block: "center", behavior: "smooth" }), 250);
    };

    const setFindingToolbarAvailability = enabled => {
        const disabled = !enabled;
        findingToolbar?.classList.toggle("is-disabled", disabled);
        if (findingToolbar) findingToolbar.setAttribute("aria-disabled", disabled ? "true" : "false");
        findingFilterButtons.forEach(button => setControlDisabled(button, disabled));
        setControlDisabled(findingsCurrentOnly, disabled || activeReviewId == null);
    };

    const findingTitle = finding => ({
        missingArmService: "Arm / Service not recorded",
        missingCost: "Proliferation cost incomplete",
        zeroCost: "Zero proliferation cost",
        missingDescription: "Selected narrative missing",
        missingCompletionYear: "Completion year missing",
        lowResolutionPhoto: "Low-resolution publication imagery",
        acceptableResolutionPhoto: "Publication imagery has limited resolution reserve",
        missingPhoto: "Publication photograph missing",
        selectedPhotoUnavailable: "Selected photograph unavailable",
        publicationImageUnavailable: "Locked publication image unavailable",
        possibleTitleTypo: "Possible project-title typo",
        reviewRequired: "Project review required",
        projectChangedAfterReview: "Project changed after review",
        customSectionUnassigned: "Custom section assignment required",
        projectUnavailable: "Selected project unavailable"
    }[finding.code] || finding.message || "Publication finding");

    const findingProjectAction = finding => {
        const projectId = Number(finding.projectId || 0) || null;
        const project = projectId ? projectById.get(projectId) : null;
        if (!projectId || !project) return "";
        const imageAction = ["publicationImageUnavailable","selectedPhotoUnavailable","missingPhoto","lowResolutionPhoto","acceptableResolutionPhoto"].includes(finding.code);
        const reviewAction = ["projectChangedAfterReview","reviewRequired","missingDescription","customSectionUnassigned"].includes(finding.code);
        if (imageAction) return `<button type="button" class="btn btn-sm btn-outline-secondary" data-finding-action="image" data-finding-project="${projectId}">Review image</button>`;
        if (reviewAction) return `<button type="button" class="btn btn-sm btn-outline-secondary" data-finding-action="review" data-finding-project="${projectId}">Review project</button>`;
        const canEdit = canMaintainProjectData && normalize(project.lifecycle) === "completed";
        return `<a class="btn btn-sm btn-outline-secondary" href="${canEdit ? `/Projects/CompletedSummary/Edit?id=${projectId}&returnUrl=${encodeURIComponent(location.pathname + location.search)}` : `/Projects/Overview?id=${projectId}`}">${canEdit ? "Edit record" : "Open project"}</a>`;
    };

    const renderFindings = () => {
        if (!readyFindings) return;
        if (orderedIds.length === 0) {
            readyFindings.innerHTML = '<div class="compendium-readiness-empty"><i class="bi bi-journal-check" aria-hidden="true"></i><span>Select projects to begin publication readiness checks.</span></div>';
            return;
        }
        const all = Array.isArray(lastPreflight?.findings) ? lastPreflight.findings : [];
        const onlyCurrent = Boolean(findingsCurrentOnly?.checked) && activeReviewId != null;
        const filtered = all.filter(finding => (findingSeverity === "all" || finding.severity === findingSeverity)
            && (!onlyCurrent || Number(finding.projectId) === Number(activeReviewId)));
        if (!filtered.length) {
            readyFindings.innerHTML = `<div class="compendium-findings-empty"><i class="bi bi-check-circle"></i><span>${all.length ? "No findings match the current filter." : "No publication findings."}</span></div>`;
            return;
        }

        const grouped = [];
        const byCode = new Map();
        filtered.forEach(finding => {
            const key = `${finding.severity}|${finding.code || finding.message}`;
            if (!byCode.has(key)) { const group = { severity: finding.severity, code: finding.code, title: findingTitle(finding), findings: [] }; byCode.set(key, group); grouped.push(group); }
            byCode.get(key).findings.push(finding);
        });

        readyFindings.innerHTML = grouped.map((group, index) => {
            const count = group.findings.length;
            const first = group.findings[0];
            const projectRows = group.findings.map(finding => {
                const detail = String(finding.message || "").trim();
                return `<div class="compendium-finding-group__project"><div><strong>${escapeHtml(finding.projectName || "Publication")}</strong><span>${escapeHtml(detail)}</span></div><div>${findingProjectAction(finding)}</div></div>`;
            }).join("");
            if (count === 1 && onlyCurrent) {
                return `<article class="compendium-finding is-${escapeHtml(group.severity)}"><div class="compendium-finding__copy"><strong>${escapeHtml(first.projectName || group.title)}</strong><span>${escapeHtml(first.message)}</span></div><div class="compendium-finding__action">${findingProjectAction(first)}</div></article>`;
            }
            return `<details class="compendium-finding-group is-${escapeHtml(group.severity)}" ${group.severity === "blocker" ? "open" : ""}>
                <summary><span class="compendium-finding-group__icon"><i class="bi ${group.severity === "blocker" ? "bi-x-octagon-fill" : group.severity === "warning" ? "bi-exclamation-triangle-fill" : "bi-info-circle-fill"}"></i></span><div><strong>${escapeHtml(group.title)}</strong><small>${count} finding${count === 1 ? "" : "s"}${count === 1 && first.projectName ? ` · ${escapeHtml(first.projectName)}` : ""}</small></div><span class="compendium-finding-group__chevron"><i class="bi bi-chevron-down"></i></span></summary>
                <div class="compendium-finding-group__body">${projectRows}</div>
            </details>`;
        }).join("");
    };

    const renderPdfVerification = () => {
        if (!outputVerification) return;
        const verified = Boolean(lastVerifiedPdf?.verified) && Number(lastVerifiedPdf?.pageCount || 0) > 0;
        outputVerification.hidden = !verified;
        if (verified && outputVerificationText) {
            const pages = Number(lastVerifiedPdf.pageCount);
            outputVerificationText.textContent = `PDF verified · ${pages} page${pages === 1 ? "" : "s"}`;
        }
    };

    const invalidatePdfVerification = () => {
        lastVerifiedPdf = null;
        renderPdfVerification();
    };

    const updateOutput = () => {
        const selected = orderedIds.length;
        const blockers = Number(lastPreflight?.blockers ?? (selected ? 0 : 1));
        const warnings = Number(lastPreflight?.warnings ?? 0);
        const reviewed = Number(lastPreflight?.reviewed ?? 0);
        const technicallyValid = selected > 0 && !preflightPending && Boolean(lastPreflight?.canGenerate) && blockers === 0;
        const allReviewed = selected > 0 && reviewed === selected;
        const canPreview = technicallyValid && !exportBusy;
        const canDownload = technicallyValid && allReviewed && !exportBusy;
        setControlDisabled(preview, !canPreview);
        setControlDisabled(generate, !canDownload);
        if (preview) preview.title = canPreview
            ? "Preview the current Compendium PDF"
            : "Preview becomes available when the current publication is technically valid.";
        if (generate) generate.title = canDownload
            ? "Download the verified Compendium PDF"
            : (!allReviewed && technicallyValid
                ? "Review all selected projects before final download."
                : "Download becomes available when publication blockers are cleared and review is complete.");
        if (!outputStatus) return;
        if (!selected) {
            outputStatus.className = "compendium-output-status";
            outputStatus.innerHTML = '<i class="bi bi-journal"></i><div><strong>Select projects</strong><span>Choose at least one project to begin publication preflight.</span></div>';
        } else if (preflightPending) {
            outputStatus.className = "compendium-output-status is-pending";
            outputStatus.innerHTML = '<i class="bi bi-arrow-repeat"></i><div><strong>Checking publication</strong><span>Refreshing readiness for the current project selection.</span></div>';
        } else if (blockers > 0) {
            outputStatus.className = "compendium-output-status is-blocked";
            outputStatus.innerHTML = `<i class="bi bi-x-octagon"></i><div><strong>${blockers} blocker${blockers === 1 ? "" : "s"}</strong><span>Resolve publication blockers before generating the Compendium.</span></div>`;
        } else if (!allReviewed) {
            outputStatus.className = "compendium-output-status is-review";
            outputStatus.innerHTML = `<i class="bi bi-journal-check"></i><div><strong>Review required</strong><span>${reviewed} of ${selected} projects reviewed${warnings ? ` · ${warnings} warning${warnings === 1 ? "" : "s"}` : ""}. Preview remains available.</span></div>`;
        } else if (warnings > 0) {
            outputStatus.className = "compendium-output-status is-warning";
            outputStatus.innerHTML = `<i class="bi bi-exclamation-triangle"></i><div><strong>Ready with warnings</strong><span>${warnings} warning${warnings === 1 ? "" : "s"} remain · all ${selected} projects reviewed.</span></div>`;
        } else {
            outputStatus.className = "compendium-output-status is-ready";
            outputStatus.innerHTML = `<i class="bi bi-check-circle"></i><div><strong>Ready to issue</strong><span>All ${selected} projects reviewed · no publication warnings.</span></div>`;
        }
    };

    const invalidatePreflight = () => {
        invalidatePdfVerification();
        preflightRevision++;
        preflightController?.abort();
        preflightController = null;
        lastPreflight = null;
        projectStateById = new Map();
        preflightPending = orderedIds.length > 0;
        preflightSpinner?.classList.toggle("d-none", !preflightPending);
        if (preflightPending) {
            setFindingToolbarAvailability(false);
            if (readySelected) readySelected.textContent = String(orderedIds.length);
            if (readyBlockers) readyBlockers.textContent = "…";
            if (readyWarnings) readyWarnings.textContent = "…";
            if (readyInfo) readyInfo.textContent = "…";
            if (readyStructureCopy) readyStructureCopy.textContent = `${orderedIds.length} project${orderedIds.length === 1 ? "" : "s"} selected · checking catalogue structure`;
            if (readyFindings) readyFindings.innerHTML = '<div class="compendium-readiness-pending"><span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span>Checking the selected projects against current publication requirements…</span></div>';
        }
        updateOutput();
    };

    const runPreflight = async revision => {
        if (revision !== preflightRevision || !orderedIds.length) return;
        syncHidden();
        preflightController = new AbortController();
        try {
            const response = await fetch(form.dataset.preflightUrl, {
                method: "POST",
                body: new FormData(form),
                signal: preflightController.signal,
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            const body = await response.json().catch(() => ({}));
            if (revision !== preflightRevision) return;
            if (!response.ok) throw new Error(body.message || "Publication readiness could not be refreshed.");
            lastPreflight = body;
            projectStateById = new Map((body.projects || []).map(state => [Number(state.projectId), state]));
            renderCoverSetting();
            const selectedValue = Number(body.selected ?? orderedIds.length);
            const categoryValue = Number(body.categories ?? 0);
            if (readySelected) readySelected.textContent = String(selectedValue);
            if (readyBlockers) readyBlockers.textContent = String(body.blockers ?? 0);
            if (readyWarnings) readyWarnings.textContent = String(body.warnings ?? 0);
            if (readyInfo) readyInfo.textContent = String(body.info ?? 0);
            if (readyCategories) readyCategories.textContent = String(categoryValue);
            if (readyReviewed) readyReviewed.textContent = String(body.reviewed ?? 0);
            if (readyStructureCopy) {
                const structureLabel = editorialState.groupingMode === "None" ? "continuous catalogue" : editorialState.groupingMode === "CustomSections" ? `${categoryValue} publication section${categoryValue === 1 ? "" : "s"}` : `${categoryValue} technical categor${categoryValue === 1 ? "y" : "ies"}`;
                const orderLabel = editorialState.sortMode === "Manual" ? "manual order" : editorialState.sortMode === "LatestFirst" ? "latest first" : "A–Z";
                readyStructureCopy.textContent = `${selectedValue} project${selectedValue === 1 ? "" : "s"} · ${structureLabel} · ${orderLabel}`;
            }
            setFindingToolbarAvailability(true);
            refreshReviewProgress();
            renderFindings();
            renderOrder();
            updateReviewNavigation();
            if (activeReviewData && Number(activeReviewData.projectId) === Number(activeReviewId)) renderReviewData(activeReviewData);
        } catch (error) {
            if (error?.name === "AbortError") return;
            if (revision !== preflightRevision) return;
            lastPreflight = { selected: orderedIds.length, blockers: 1, warnings: 0, info: 0, categories: 0, reviewed: 0, canGenerate: false, findings: [{ severity: "blocker", code: "preflightUnavailable", message: error.message || "Publication readiness could not be refreshed." }] };
            if (readySelected) readySelected.textContent = String(orderedIds.length);
            if (readyBlockers) readyBlockers.textContent = "1";
            if (readyWarnings) readyWarnings.textContent = "0";
            if (readyInfo) readyInfo.textContent = "0";
            if (readyStructureCopy) readyStructureCopy.textContent = `${orderedIds.length} project${orderedIds.length === 1 ? "" : "s"} selected · readiness unavailable`;
            setFindingToolbarAvailability(true);
            renderFindings();
            updateReviewNavigation();
        } finally {
            if (revision === preflightRevision) {
                preflightPending = false;
                preflightSpinner?.classList.add("d-none");
                updateOutput();
            }
        }
    };

    const schedulePreflight = () => {
        window.clearTimeout(preflightTimer);
        invalidatePreflight();
        const revision = preflightRevision;
        if (!orderedIds.length) {
            preflightPending = false;
            if (readySelected) readySelected.textContent = "0";
            if (readyBlockers) readyBlockers.textContent = "—";
            if (readyWarnings) readyWarnings.textContent = "—";
            if (readyInfo) readyInfo.textContent = "—";
            if (readyReviewed) readyReviewed.textContent = "0";
            if (readyCategories) readyCategories.textContent = "0";
            if (readyStructureCopy) readyStructureCopy.textContent = "Select projects to build the catalogue structure.";
            if (findingsCurrentOnly) findingsCurrentOnly.checked = false;
            lastPreflight = { selected: 0, blockers: 1, warnings: 0, info: 0, categories: 0, reviewed: 0, canGenerate: false, findings: [{ severity: "blocker", code: "noSelection", message: "Select at least one project to begin publication preflight." }] };
            setFindingToolbarAvailability(false);
            renderFindings();
            refreshReviewProgress();
            updateReviewNavigation();
            updateOutput();
            return;
        }
        preflightTimer = window.setTimeout(() => runPreflight(revision), 220);
    };

    const selectionChanged = () => {
        if (coverState.imageMode === "explicit" && !orderedIds.includes(Number(coverState.heroProjectId))) { coverState.imageMode = "automatic"; coverState.heroProjectId = null; coverState.heroPhotoId = null; coverState.focalX = 0.5; coverState.focalY = 0.5; }
        syncHidden(); renderCoverSetting();
        updateCheckboxes();
        renderOrder();
        applyFilters();
        renderDirty();
        refreshReviewProgress();
        updateReviewNavigation();
        if (activeReviewId == null || !isSelected(activeReviewId)) activeReviewId = orderedIds[0] ?? null;
        if (activeReviewId) loadReview(activeReviewId); else { activeReviewData = null; if (reviewEmpty) reviewEmpty.hidden = false; if (reviewCard) reviewCard.hidden = true; }
        schedulePreflight();
    };

    rows.forEach(row => row.querySelector("[data-project-checkbox]")?.addEventListener("change", event => {
        const id = Number(row.dataset.id);
        if (event.currentTarget.checked) {
            if (!isSelected(id)) orderedIds.push(id);
            ensureConfig(id);
            activeReviewId ??= id;
        } else {
            orderedIds = orderedIds.filter(projectId => projectId !== id);
            if (activeReviewId === id) activeReviewId = orderedIds[0] ?? null;
        }
        selectionChanged();
    }));
    [search, lifecycle, category, technical, proliferation, selectedOnly].forEach(control => control?.addEventListener(control === search ? "input" : "change", applyFilters));
    selectMatching?.addEventListener("click", () => {
        visibleRows().filter(row => !isSelected(Number(row.dataset.id))).slice(0, 100).forEach(row => {
            const id = Number(row.dataset.id); orderedIds.push(id); ensureConfig(id);
        });
        orderedIds = [...new Set(orderedIds)];
        activeReviewId ??= orderedIds[0] ?? null;
        selectionChanged();
    });
    clearSelection?.addEventListener("click", () => { orderedIds = []; activeReviewId = null; activeReviewData = null; selectionChanged(); });

    const normalizeSectionOrders = () => customSections.forEach((section, index) => { section.sortOrder = index; });
    const moveSection = (sectionKey, delta) => {
        const index = customSections.findIndex(section => normalize(section.sectionKey) === normalize(sectionKey));
        const target = index + delta;
        if (index < 0 || target < 0 || target >= customSections.length) return;
        [customSections[index], customSections[target]] = [customSections[target], customSections[index]];
        normalizeSectionOrders();
        publicationStructureChanged();
    };
    const assignProjectToSection = (projectId, sectionKey) => {
        const id = Number(projectId); if (!id) return;
        const section = sectionByKey(sectionKey);
        const config = ensureConfig(id);
        const previousKey = config.customSectionKey;
        config.customSectionKey = section?.sectionKey || null;
        config.customSectionName = section?.name || null;
        if (normalize(previousKey) !== normalize(config.customSectionKey)) invalidateProjectReview(id);
    };
    const moveProjectRelative = (id, delta) => {
        const group = publicationGroups().find(item => item.ids.includes(id));
        if (!group) return false;
        const index = group.ids.indexOf(id), targetId = group.ids[index + delta];
        if (!targetId) return false;
        const from = orderedIds.indexOf(id), target = orderedIds.indexOf(targetId);
        if (from < 0 || target < 0) return false;
        orderedIds.splice(from, 1);
        const insertAt = orderedIds.indexOf(targetId) + (delta > 0 ? 1 : 0);
        orderedIds.splice(insertAt, 0, id);
        return true;
    };

    orderList?.addEventListener("click", event => {
        const sectionAction = event.target.closest("[data-section-group-up],[data-section-group-down],[data-section-delete]");
        if (sectionAction && editorialState.groupingMode === "CustomSections") {
            const key = sectionAction.dataset.sectionKey || sectionAction.closest("[data-section-key]")?.dataset.sectionKey;
            if (!key || key === "__unassigned") return;
            if (sectionAction.matches("[data-section-group-up]")) moveSection(key, -1);
            else if (sectionAction.matches("[data-section-group-down]")) moveSection(key, 1);
            else {
                const section = sectionByKey(key); if (!section) return;
                const count = orderedIds.filter(id => normalize(ensureConfig(id).customSectionKey) === normalize(key)).length;
                pendingSectionDeleteKey = key;
                if (sectionDeleteMessage) {
                    sectionDeleteMessage.textContent = count
                        ? `Delete “${section.name}”? ${count} project${count === 1 ? "" : "s"} will move to Unassigned. Project master data is not changed.`
                        : `Delete the empty publication section “${section.name}”?`;
                }
                sectionDeleteModal?.show();
            }
            return;
        }

        const item = event.target.closest("[data-order-id]");
        if (!item) return;
        const id = Number(item.dataset.orderId);
        if (event.target.closest("[data-order-review]")) { activeReviewId = id; loadReview(id); document.getElementById("compendium-review")?.scrollIntoView({ behavior: "smooth", block: "start" }); return; }
        if (event.target.closest("[data-remove]")) { orderedIds = orderedIds.filter(projectId => projectId !== id); if (activeReviewId === id) activeReviewId = orderedIds[0] ?? null; selectionChanged(); return; }
        if (editorialState.sortMode !== "Manual") return;
        if (event.target.closest("[data-move-up]") && moveProjectRelative(id, -1)) selectionChanged();
        else if (event.target.closest("[data-move-down]") && moveProjectRelative(id, 1)) selectionChanged();
    });

    sectionDeleteConfirm?.addEventListener("click", () => {
        const key = pendingSectionDeleteKey;
        if (!key) return;
        orderedIds
            .filter(id => normalize(ensureConfig(id).customSectionKey) === normalize(key))
            .forEach(id => assignProjectToSection(id, null));
        customSections = customSections.filter(item => normalize(item.sectionKey) !== normalize(key));
        normalizeSectionOrders();
        pendingSectionDeleteKey = null;
        sectionDeleteModal?.hide();
        publicationStructureChanged();
    });

    let draggedOrderId = null;
    let draggedSectionKey = null;
    orderList?.addEventListener("change", event => {
        const renameInput = event.target.closest("[data-section-rename]");
        if (renameInput && editorialState.groupingMode === "CustomSections") {
            const key = renameInput.dataset.sectionKey;
            const section = sectionByKey(key); if (!section) return;
            const next = cleanSectionName(renameInput.value);
            if (!next) { renderOrder(); return; }
            const duplicate = customSections.some(item => normalize(item.sectionKey) !== normalize(key) && normalize(item.name) === normalize(next));
            if (duplicate) { window.alert("A custom section with this name already exists."); renderOrder(); return; }
            if (section.name === next) return;
            section.name = next;
            orderedIds.filter(id => normalize(ensureConfig(id).customSectionKey) === normalize(key)).forEach(id => {
                ensureConfig(id).customSectionName = next;
                invalidateProjectReview(id);
            });
            publicationStructureChanged();
            return;
        }
        const select = event.target.closest("[data-section-select]");
        if (!select) return;
        const item = select.closest("[data-order-id]"); const id = Number(item?.dataset.orderId || 0); if (!id) return;
        assignProjectToSection(id, select.value || null);
        publicationStructureChanged({ refreshReview: id === activeReviewId });
    });

    orderList?.addEventListener("keydown", event => {
        const renameInput = event.target.closest("[data-section-rename]");
        if (renameInput && event.key === "Enter") { event.preventDefault(); renameInput.blur(); }
    });

    orderList?.addEventListener("dragstart", event => {
        const sectionHandle = event.target.closest("[data-section-drag-handle]");
        if (sectionHandle && editorialState.groupingMode === "CustomSections") {
            draggedSectionKey = sectionHandle.dataset.sectionKey || sectionHandle.closest("[data-section-key]")?.dataset.sectionKey || null;
            draggedOrderId = null;
            if (event.dataTransfer) { event.dataTransfer.effectAllowed = "move"; event.dataTransfer.setData("text/plain", `section:${draggedSectionKey || ""}`); }
            return;
        }
        if (editorialState.sortMode !== "Manual") { event.preventDefault(); return; }
        const item = event.target.closest("[data-order-id]");
        if (!item) return;
        draggedOrderId = Number(item.dataset.orderId) || null; draggedSectionKey = null;
        item.classList.add("is-dragging");
        if (event.dataTransfer) { event.dataTransfer.effectAllowed = "move"; event.dataTransfer.setData("text/plain", String(draggedOrderId || "")); }
    });
    orderList?.addEventListener("dragover", event => {
        if (draggedOrderId != null || draggedSectionKey) { event.preventDefault(); if (event.dataTransfer) event.dataTransfer.dropEffect = "move"; }
    });
    orderList?.addEventListener("drop", event => {
        if (draggedSectionKey && editorialState.groupingMode === "CustomSections") {
            event.preventDefault();
            const targetGroup = event.target.closest("[data-section-key]");
            const targetKey = targetGroup?.dataset.sectionKey;
            if (!targetKey || targetKey === "__unassigned" || normalize(targetKey) === normalize(draggedSectionKey)) return;
            const from = customSections.findIndex(section => normalize(section.sectionKey) === normalize(draggedSectionKey));
            const to = customSections.findIndex(section => normalize(section.sectionKey) === normalize(targetKey));
            if (from < 0 || to < 0) return;
            const [moved] = customSections.splice(from, 1); customSections.splice(to, 0, moved); normalizeSectionOrders(); publicationStructureChanged();
            return;
        }
        if (editorialState.sortMode !== "Manual" || draggedOrderId == null) return;
        event.preventDefault();
        const targetGroup = event.target.closest("[data-section-key]");
        if (editorialState.groupingMode === "CustomSections" && targetGroup) {
            const targetSectionKey = targetGroup.dataset.sectionKey === "__unassigned" ? null : targetGroup.dataset.sectionKey;
            assignProjectToSection(draggedOrderId, targetSectionKey);
        }
        const target = event.target.closest("[data-order-id]");
        const targetId = Number(target?.dataset.orderId || 0);
        if (targetId && targetId !== draggedOrderId) {
            const from = orderedIds.indexOf(draggedOrderId), to = orderedIds.indexOf(targetId);
            if (from >= 0 && to >= 0) { const [moved] = orderedIds.splice(from, 1); orderedIds.splice(to, 0, moved); }
        } else if (targetGroup) {
            const groupKey = targetGroup.dataset.sectionKey;
            const group = publicationGroups().find(item => normalize(item.key) === normalize(groupKey));
            const peers = group?.ids.filter(id => id !== draggedOrderId) || [];
            const from = orderedIds.indexOf(draggedOrderId); if (from >= 0) orderedIds.splice(from, 1);
            const lastPeer = peers.at(-1);
            const insertAt = lastPeer ? orderedIds.indexOf(lastPeer) + 1 : orderedIds.length;
            orderedIds.splice(Math.max(0, insertAt), 0, draggedOrderId);
        }
        selectionChanged();
    });
    orderList?.addEventListener("dragend", () => { orderList.querySelectorAll(".is-dragging").forEach(item => item.classList.remove("is-dragging")); draggedOrderId = null; draggedSectionKey = null; });

    narrativeButtons.forEach(button => button.addEventListener("click", () => changeNarrativeSource(button.dataset.narrativeValue)));
    groupingButtons.forEach(button => button.addEventListener("click", () => { const next = normalizeGrouping(button.dataset.groupingValue); if (next === editorialState.groupingMode) return; editorialState.groupingMode = next; publicationStructureChanged(); }));
    sortButtons.forEach(button => button.addEventListener("click", () => { const next = normalizeSort(button.dataset.sortValue); if (next === editorialState.sortMode) return; editorialState.sortMode = next; publicationStructureChanged(); if (activeReviewId) loadReview(activeReviewId); }));
    customSectionAdd?.addEventListener("click", () => {
        const name = cleanSectionName(customSectionName?.value); if (!name) return;
        if (customSections.some(section => normalize(section.name) === normalize(name))) { window.alert("A custom section with this name already exists."); customSectionName?.focus(); return; }
        customSections.push({ sectionKey: createSectionKey(), name, sortOrder: customSections.length });
        if (customSectionName) customSectionName.value = "";
        publicationStructureChanged();
    });
    customSectionName?.addEventListener("keydown", event => { if (event.key === "Enter") { event.preventDefault(); customSectionAdd?.click(); } });
    reviewNarrativeOptions?.addEventListener("click", event => {
        const reset = event.target.closest("[data-review-narrative-default]");
        if (reset && activeReviewId) { setProjectNarrativeSource(activeReviewId, null); return; }
        const button = event.target.closest("[data-review-narrative-value]");
        if (button && activeReviewId) setProjectNarrativeSource(activeReviewId, button.dataset.reviewNarrativeValue);
    });

    reviewPrevious?.addEventListener("click", () => navigateReview(-1));
    reviewNext?.addEventListener("click", () => navigateReview(1));
    reviewNextAttention?.addEventListener("click", goNextAttention);
    const reviewAndAdvance = () => {
        if (!activeReviewId || !activeReviewData?.reviewFingerprint || reviewMarkReviewed?.disabled) return;
        const reviewedId = Number(activeReviewId); ensureConfig(reviewedId).reviewFingerprint = String(activeReviewData.reviewFingerprint);
        const current = stateFor(reviewedId) || { projectId: reviewedId, reviewFingerprint: activeReviewData.reviewFingerprint };
        projectStateById.set(reviewedId, { ...current, reviewFingerprint: activeReviewData.reviewFingerprint, isReviewed: true, isReviewStale: false });
        syncHidden(); refreshReviewProgress(); renderOrder(); const nextId = nextUnreviewedId(reviewedId, true);
        renderReviewData({ ...activeReviewData, isReviewed: true, isReviewStale: false }); schedulePreflight();
        if (nextId) { activeReviewId = nextId; window.setTimeout(() => loadReview(nextId), 40); }
    };
    reviewMarkReviewed?.addEventListener("click", reviewAndAdvance);
    reviewChangeImage?.addEventListener("click", () => openPhotoEditor(false));
    reviewAdjustCrop?.addEventListener("click", () => openPhotoEditor(true));
    reviewUseAutomatic?.addEventListener("click", () => {
        if (!activeReviewId) return;
        const config = ensureConfig(activeReviewId);
        config.imageSelectionMode = "automatic"; config.primaryPhotoId = null; config.focalX = 0.5; config.focalY = 0.5;
        publicationConfigChanged(activeReviewId);
    });

    const renderCoverHeroPicker = () => {
        if (!coverHeroPicker) return;
        const candidates = publicationOrderIds().map(id => { const project = projectById.get(id), state = stateFor(id), config = ensureConfig(id); const photoId = Number(state?.resolvedPhotoId || project?.defaultPhotoId || 0); return { id, project, state, config, photoId }; }).filter(item => item.project && item.photoId);
        if (coverHeroEmpty) coverHeroEmpty.hidden = candidates.length > 0;
        coverHeroPicker.innerHTML = candidates.map(item => `<button type="button" class="compendium-cover-hero-choice${coverState.imageMode === "explicit" && Number(coverState.heroProjectId) === item.id && Number(coverState.heroPhotoId) === item.photoId ? " is-selected" : ""}" data-cover-hero-choice data-project-id="${item.id}" data-photo-id="${item.photoId}"><span class="compendium-cover-hero-thumb"><img src="${escapeHtml(photoPreviewUrl(item.id,item.photoId))}" alt="" style="object-position:${clamp(item.config.focalX)*100}% ${clamp(item.config.focalY)*100}%"></span><span><strong>${escapeHtml(item.project.projectName)}</strong><small>${escapeHtml(item.project.technicalCategory || "Technical category not recorded")} · ${escapeHtml(qualityLabel(normalize(item.state?.imageQuality), item.state?.effectiveDpi))}</small></span><i class="bi bi-check-circle-fill"></i></button>`).join("");
    };
    coverChoose?.addEventListener("click", () => { renderCoverHeroPicker(); coverHeroModal?.show(); });
    coverHeroPicker?.addEventListener("click", event => {
        const choice = event.target.closest("[data-cover-hero-choice]"); if (!choice) return;
        const projectId = Number(choice.dataset.projectId || 0), photoId = Number(choice.dataset.photoId || 0); if (!projectId || !photoId) return;
        const config = ensureConfig(projectId); coverState.imageMode = "explicit"; coverState.heroProjectId = projectId; coverState.heroPhotoId = photoId; coverState.focalX = roundFocal(config.focalX); coverState.focalY = roundFocal(config.focalY); coverChanged(); renderCoverHeroPicker(); coverHeroModal?.hide();
    });

    reviewUseCover?.addEventListener("click", () => {
        if (!activeReviewId || !activeReviewData) return; const photo = currentReviewPhoto(activeReviewData); if (!photo) return; const config = ensureConfig(activeReviewId);
        coverState.imageMode = "explicit"; coverState.heroProjectId = Number(activeReviewId); coverState.heroPhotoId = Number(photo.photoId); coverState.focalX = roundFocal(config.focalX); coverState.focalY = roundFocal(config.focalY); coverChanged();
    });
    coverAutomatic?.addEventListener("click", () => { coverState.imageMode = "automatic"; coverState.heroProjectId = null; coverState.heroPhotoId = null; coverState.focalX = 0.5; coverState.focalY = 0.5; coverChanged(); });
    coverNone?.addEventListener("click", () => { coverState.imageMode = "none"; coverState.heroProjectId = null; coverState.heroPhotoId = null; coverState.focalX = 0.5; coverState.focalY = 0.5; coverChanged(); });

    photoPicker?.addEventListener("click", event => {
        const choice = event.target.closest("[data-photo-id]");
        if (!choice || !activeReviewId) return;
        const photoId = Number(choice.dataset.photoId || 0); if (!photoId) return;
        const config = ensureConfig(activeReviewId);
        config.imageSelectionMode = "explicit"; config.primaryPhotoId = photoId; config.focalX = 0.5; config.focalY = 0.5;
        publicationConfigChanged(activeReviewId, { refreshReview: false });
        const selectedPhoto = activeReviewData?.photos?.find(photo => Number(photo.photoId) === photoId);
        if (selectedPhoto && activeReviewData) activeReviewData = { ...activeReviewData, resolvedPhotoId: photoId, imageSelectionMode: "explicit", focalX: 0.5, focalY: 0.5 };
        renderPhotoModal();
        scheduleReviewRefresh();
    });
    photoUseAutomatic?.addEventListener("click", () => {
        if (!activeReviewId) return;
        const config = ensureConfig(activeReviewId);
        config.imageSelectionMode = "automatic"; config.primaryPhotoId = null; config.focalX = 0.5; config.focalY = 0.5;
        publicationConfigChanged(activeReviewId, { refreshReview: false });
        scheduleReviewRefresh();
        window.setTimeout(renderPhotoModal, 240);
    });
    photoResetCrop?.addEventListener("click", () => {
        if (!activeReviewId || !photoForModal()) return;
        const config = ensureConfig(activeReviewId); config.focalX = 0.5; config.focalY = 0.5;
        publicationConfigChanged(activeReviewId, { refreshReview: false }); renderPhotoModal(); scheduleReviewRefresh();
    });
    photoCropStage?.addEventListener("click", event => {
        if (!activeReviewId || !photoForModal() || !photoCropImage) return;
        const metrics = sourceMetrics(photoCropStage, photoCropImage); if (!metrics) return;
        const rect = photoCropStage.getBoundingClientRect();
        const px = event.clientX - rect.left, py = event.clientY - rect.top;
        const sourceX = (px - metrics.offsetX) / metrics.renderedWidth;
        const sourceY = (py - metrics.offsetY) / metrics.renderedHeight;
        if (sourceX < 0 || sourceX > 1 || sourceY < 0 || sourceY > 1) return;
        const config = ensureConfig(activeReviewId); config.focalX = roundFocal(sourceX); config.focalY = roundFocal(sourceY);
        publicationConfigChanged(activeReviewId, { refreshReview: false }); positionCropOverlay();
        if (photoCropSelection) photoCropSelection.textContent = `${config.imageSelectionMode === "explicit" ? "Locked publication image" : "Automatic publication image"} · focal ${Math.round(config.focalX*100)}% / ${Math.round(config.focalY*100)}%`;
        scheduleReviewRefresh();
    });
    window.addEventListener("resize", () => { if (photoModalNode?.classList.contains("show")) positionCropOverlay(); });

    findingFilterButtons.forEach(button => button.addEventListener("click", () => {
        findingSeverity = String(button.dataset.findingFilter || "all");
        findingFilterButtons.forEach(item => item.classList.toggle("active", item === button));
        renderFindings();
    }));
    findingsCurrentOnly?.addEventListener("change", renderFindings);
    readyFindings?.addEventListener("click", event => {
        const action = event.target.closest("[data-finding-action]");
        if (!action) return;
        const id = Number(action.dataset.findingProject || 0); if (!id || !isSelected(id)) return;
        activeReviewId = id;
        loadReview(id).then(() => {
            document.getElementById("compendium-review")?.scrollIntoView({ behavior: "smooth", block: "start" });
            if (action.dataset.findingAction === "image") window.setTimeout(() => openPhotoEditor(false), 250);
        });
    });

    form.querySelectorAll("[data-compendium-durable]").forEach(input => input.addEventListener("input", () => { renderDirty(); schedulePreflight(); }));
    const publicationErrorFromResponse = async response => {
        const type = response.headers.get("content-type") || "";
        if (type.includes("application/json")) {
            const payload = await response.json().catch(() => ({}));
            const error = new Error(payload?.message || `Publication request failed with HTTP ${response.status}.`);
            error.code = payload?.code || null;
            return error;
        }
        const text = await response.text();
        return new Error(text?.trim() || `Publication request failed with HTTP ${response.status}.`);
    };

    const fileNameFromResponse = response => {
        const explicit = response.headers.get("X-PRISM-Publication-FileName");
        if (explicit) return explicit;
        const disposition = response.headers.get("Content-Disposition") || "";
        const utf = disposition.match(/filename\*=UTF-8''([^;]+)/i);
        if (utf?.[1]) return decodeURIComponent(utf[1]);
        const basic = disposition.match(/filename="?([^";]+)"?/i);
        return basic?.[1] || "SDD_Simulators_Compendium.pdf";
    };

    const setExportBusy = (busy, previewRequest = false) => {
        exportBusy = Boolean(busy);
        if (preview) preview.innerHTML = busy && previewRequest
            ? '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Preparing preview'
            : '<i class="bi bi-eye"></i> Preview PDF';
        if (generate) generate.innerHTML = busy && !previewRequest
            ? '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Preparing PDF'
            : '<i class="bi bi-download"></i> Download Compendium PDF';
        updateOutput();
    };

    const requestPdf = async previewRequest => {
        const targetUrl = previewRequest ? previewUrl : generateUrl;
        if (!targetUrl || exportBusy) return;
        const selected = orderedIds.length;
        const blockers = Number(lastPreflight?.blockers ?? 0);
        const reviewed = Number(lastPreflight?.reviewed ?? 0);
        if (!selected || preflightPending || blockers > 0 || !lastPreflight?.canGenerate) return;
        if (!previewRequest && reviewed !== selected) {
            document.getElementById("compendium-review")?.scrollIntoView({ behavior: "smooth", block: "start" });
            return;
        }

        const previewWindow = previewRequest ? window.open("about:blank", "_blank") : null;
        syncHidden();
        setExportBusy(true, previewRequest);
        try {
            const response = await fetch(targetUrl, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) throw await publicationErrorFromResponse(response);
            const type = response.headers.get("content-type") || "";
            if (!type.includes("application/pdf")) throw new Error("The server did not return a PDF publication.");

            const verified = response.headers.get("X-PRISM-Publication-Composition-Verified") === "true";
            const pageCount = Number(response.headers.get("X-PRISM-Publication-Page-Count") || 0);
            if (!verified || pageCount <= 0) throw new Error("The generated PDF did not return a valid physical composition verification result.");
            lastVerifiedPdf = { verified: true, pageCount };
            renderPdfVerification();

            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            if (previewRequest) {
                if (previewWindow) previewWindow.location.replace(url);
                else window.open(url, "_blank", "noopener");
                window.setTimeout(() => URL.revokeObjectURL(url), 120000);
            } else {
                const link = document.createElement("a");
                link.href = url;
                link.download = fileNameFromResponse(response);
                document.body.append(link);
                link.click();
                link.remove();
                window.setTimeout(() => URL.revokeObjectURL(url), 30000);
            }
        } catch (error) {
            if (previewWindow && !previewWindow.closed) previewWindow.close();
            window.alert(error?.message || "The Compendium PDF could not be generated.");
        } finally {
            setExportBusy(false, previewRequest);
        }
    };

    preview?.addEventListener("click", event => { event.preventDefault(); requestPdf(true); });
    generate?.addEventListener("click", event => { event.preventDefault(); requestPdf(false); });

    form.addEventListener("submit", () => syncHidden());

    const updatePresetOption = preset => {
        if (!presetSelect || !preset) return;
        let option = [...presetSelect.options].find(item => Number(item.value) === Number(preset.id));
        if (!option) { option = new Option(preset.name, String(preset.id)); presetSelect.add(option); }
        option.textContent = preset.name; presets.set(Number(preset.id), { ...preset, id: Number(preset.id) });
    };
    const setActivePreset = preset => {
        activePresetId = Number(preset?.id || 0) || null;
        activeRowVersion = String(preset?.rowVersion || "");
        syncHidden();
        if (presetSelect) presetSelect.value = activePresetId ? String(activePresetId) : "";
        if (presetMeta && preset) presetMeta.textContent = `Shared · ${preset.projectCount} projects · Updated ${new Date(preset.updatedAtUtc).toLocaleDateString()} · ${preset.updatedByDisplay}`;
        if (activePresetId) history.replaceState(null, "", `${location.pathname}?presetId=${activePresetId}`);
        markClean();
    };
    const presetUrl = id => id ? `${location.pathname}?presetId=${Number(id)}` : location.pathname;
    const requestLoad = id => {
        const target = Number(id) || null;
        if (renderDirty()) { pendingLoadPresetId = target; discardModal?.show(); return; }
        location.assign(presetUrl(target));
    };
    presetLoad?.addEventListener("click", () => requestLoad(presetSelect?.value));
    document.querySelector("[data-discard-load]")?.addEventListener("click", () => { discardModal?.hide(); location.assign(presetUrl(pendingLoadPresetId)); });

    const post = async (url, payload) => {
        const response = await fetch(url, { method: "POST", body: payload, headers: { "X-Requested-With": "XMLHttpRequest" } });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) { const error = new Error(body.message || "The saved Compendium operation failed."); error.code = body.code; error.status = response.status; throw error; }
        return body;
    };
    const openSave = mode => {
        if (!canManage) return;
        saveMode = mode;
        const source = activePresetId ? presets.get(activePresetId) : null;
        if (saveName) saveName.value = mode === "duplicate" && source ? `${source.name} — Copy` : mode === "create" ? (form.elements["Input.Title"]?.value || "Simulators Compendium") : source?.name || "";
        if (saveDescription) saveDescription.value = source?.description || "";
        if (saveMessage) saveMessage.textContent = "";
        saveModal?.show();
    };
    saveAsNew?.addEventListener("click", () => openSave("create"));
    duplicateButton?.addEventListener("click", () => activePresetId && openSave("duplicate"));
    document.querySelector("[data-save-confirm]")?.addEventListener("click", async () => {
        const name = String(saveName?.value || "").trim();
        if (name.length < 3) { if (saveMessage) saveMessage.textContent = "Enter a name of at least 3 characters."; return; }
        syncHidden();
        try {
            let result;
            if (saveMode === "duplicate") {
                const payload = new FormData(); const token = formToken(); if (token) payload.append("__RequestVerificationToken", token);
                payload.append("presetId", String(activePresetId)); payload.append("rowVersion", activeRowVersion); payload.append("name", name); payload.append("description", String(saveDescription?.value || ""));
                result = await post(form.dataset.duplicateUrl, payload);
            } else {
                const payload = new FormData(form); payload.set("saveAsNew", "true"); payload.set("presetName", name); payload.set("presetDescription", String(saveDescription?.value || ""));
                result = await post(form.dataset.saveUrl, payload);
            }
            updatePresetOption(result.preset); setActivePreset(result.preset); saveModal?.hide();
        } catch (error) { if (saveMessage) saveMessage.textContent = error.message; }
    });
    saveChanges?.addEventListener("click", async () => {
        if (!activePresetId || !renderDirty()) return;
        syncHidden();
        try {
            const payload = new FormData(form); payload.set("saveAsNew", "false");
            const result = await post(form.dataset.saveUrl, payload); updatePresetOption(result.preset); setActivePreset(result.preset);
        } catch (error) {
            if (error.code === "presetConflict") { pendingLoadPresetId = activePresetId; discardModal?.show(); }
            else window.alert(error.message);
        }
    });
    renameButton?.addEventListener("click", () => { const preset = presets.get(activePresetId); if (!preset) return; if (renameName) renameName.value = preset.name; renameModal?.show(); });
    document.querySelector("[data-rename-confirm]")?.addEventListener("click", async () => {
        const name = String(renameName?.value || "").trim(); if (name.length < 3) return;
        const payload = new FormData(); const token = formToken(); if (token) payload.append("__RequestVerificationToken", token);
        payload.append("presetId", String(activePresetId)); payload.append("rowVersion", activeRowVersion); payload.append("name", name);
        try { const result = await post(form.dataset.renameUrl, payload); updatePresetOption(result.preset); setActivePreset(result.preset); renameModal?.hide(); }
        catch (error) { window.alert(error.message); }
    });
    deleteButton?.addEventListener("click", () => activePresetId && deleteModal?.show());
    document.querySelector("[data-delete-confirm]")?.addEventListener("click", async () => {
        const payload = new FormData(); const token = formToken(); if (token) payload.append("__RequestVerificationToken", token);
        payload.append("presetId", String(activePresetId)); payload.append("rowVersion", activeRowVersion);
        try { await post(form.dataset.deleteUrl, payload); deleteModal?.hide(); location.assign(location.pathname); }
        catch (error) { window.alert(error.message); }
    });

    document.addEventListener("keydown", event => {
        if (!(event.ctrlKey && event.key === "Enter")) return;
        const target = event.target;
        if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || target?.isContentEditable) return;
        if (document.querySelector(".modal.show")) return;
        if (!reviewCard || reviewCard.hidden || !reviewMarkReviewed || reviewMarkReviewed.disabled) return;
        event.preventDefault();
        reviewAndAdvance();
    });

    syncHidden();
    renderCoverSetting();
    updateCheckboxes();
    applyFilters();
    renderOrder();
    refreshReviewProgress();
    updateReviewNavigation();
    setFindingToolbarAvailability(orderedIds.length > 0);
    baselineSnapshot = captureSnapshot();
    renderDirty();
    renderPdfVerification();
    if (activeReviewId) loadReview(activeReviewId);
    schedulePreflight();
})();
