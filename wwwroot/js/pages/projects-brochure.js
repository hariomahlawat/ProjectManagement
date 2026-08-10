(() => {
    "use strict";

    const form = document.querySelector("[data-brochure-form]");
    if (!(form instanceof HTMLFormElement)) return;

    const MAX_PROJECTS = 100;
    const modeAutomatic = 1;
    const modeSingle = 2;
    const modeGalleryTwo = 3;

    const parseJson = (node, fallback) => {
        try {
            return node?.textContent ? JSON.parse(node.textContent) : fallback;
        } catch {
            return fallback;
        }
    };

    const projects = parseJson(form.querySelector("[data-brochure-project-data]"), []);
    const initialSelections = parseJson(form.querySelector("[data-brochure-initial-selections]"), []);
    const projectById = new Map(projects.map(project => [Number(project.projectId), project]));
    const rows = [...form.querySelectorAll("[data-brochure-project-row]")];
    const rowById = new Map(rows.map(row => [Number(row.dataset.projectId), row]));
    const checkboxes = new Map(rows.map(row => [Number(row.dataset.projectId), row.querySelector("[data-brochure-project-checkbox]")]));

    const hiddenInputs = form.querySelector("[data-brochure-hidden-inputs]");
    const selectedList = form.querySelector("[data-brochure-selected-list]");
    const selectedEmpty = form.querySelector("[data-brochure-selected-empty]");
    const selectedCount = form.querySelector("[data-brochure-selected-count]");
    const selectedInline = form.querySelector("[data-brochure-selected-inline]");
    const matchCount = form.querySelector("[data-brochure-match-count]");
    const clearButton = form.querySelector("[data-brochure-clear]");
    const selectVisibleButton = form.querySelector("[data-brochure-select-visible]");
    const selectVisibleLabel = form.querySelector("[data-brochure-select-visible-label]");
    const searchInput = form.querySelector("[data-brochure-search]");
    const filters = [...form.querySelectorAll("[data-brochure-filter]")];
    const selectedOnly = form.querySelector("[data-brochure-selected-only]");
    const emptyFilterState = form.querySelector("[data-brochure-empty-filter]");
    const narrativeSource = form.querySelector("[data-brochure-narrative-source]");
    const previewButton = form.querySelector("[data-brochure-preview]");
    const generateButton = form.querySelector("[data-brochure-generate]");
    const generateSpinner = form.querySelector("[data-brochure-generate-spinner]");
    const generateIcon = form.querySelector("[data-brochure-generate-icon]");
    const generateLabel = form.querySelector("[data-brochure-generate-label]");

    const preflightSpinner = form.querySelector("[data-preflight-spinner]");
    const preflightMessage = form.querySelector("[data-preflight-message]");
    const preflightIssues = form.querySelector("[data-preflight-issues]");
    const preflightShowAll = form.querySelector("[data-preflight-show-all]");
    const preflightUrl = form.dataset.brochurePreflightUrl;

    const photoEditor = form.querySelector("[data-brochure-photo-editor]");
    const photoEditorTitle = form.querySelector("[data-photo-editor-title]");
    const photoEditorClose = form.querySelector("[data-photo-editor-close]");
    const imageModeSelect = form.querySelector("[data-photo-image-mode]");
    const primaryPicker = form.querySelector("[data-primary-photo-picker]");
    const secondaryPicker = form.querySelector("[data-secondary-photo-picker]");
    const secondarySection = form.querySelector("[data-secondary-photo-section]");
    const primaryStage = form.querySelector("[data-primary-focal-stage]");
    const primaryImage = form.querySelector("[data-primary-focal-image]");
    const primaryCropFrame = form.querySelector("[data-primary-crop-frame]");
    const primaryMarker = form.querySelector("[data-primary-focal-marker]");
    const primaryReset = form.querySelector("[data-primary-focal-reset]");
    const secondaryStage = form.querySelector("[data-secondary-focal-stage]");
    const secondaryImage = form.querySelector("[data-secondary-focal-image]");
    const secondaryCropFrame = form.querySelector("[data-secondary-crop-frame]");
    const secondaryMarker = form.querySelector("[data-secondary-focal-marker]");
    const secondaryReset = form.querySelector("[data-secondary-focal-reset]");

    const normalize = value => (value ?? "").trim().toLowerCase();
    const clamp = value => Math.max(0, Math.min(1, Number.isFinite(Number(value)) ? Number(value) : 0.5));
    const projectPhotos = id => projectById.get(id)?.photos ?? [];
    const getPhoto = (id, photoId) => projectPhotos(id).find(photo => Number(photo.photoId) === Number(photoId)) ?? null;

    const defaultConfig = project => ({
        projectId: Number(project.projectId),
        primaryPhotoId: project.defaultPrimaryPhotoId ?? null,
        secondaryPhotoId: project.defaultSecondaryPhotoId ?? null,
        primaryFocalX: 0.5,
        primaryFocalY: 0.5,
        secondaryFocalX: 0.5,
        secondaryFocalY: 0.5,
        imageMode: modeAutomatic
    });

    const configs = new Map();
    initialSelections.forEach(selection => {
        const id = Number(selection.projectId);
        const project = projectById.get(id);
        if (!project) return;
        const fallback = defaultConfig(project);
        configs.set(id, {
            projectId: id,
            primaryPhotoId: selection.primaryPhotoId ?? fallback.primaryPhotoId,
            secondaryPhotoId: selection.secondaryPhotoId ?? fallback.secondaryPhotoId,
            primaryFocalX: clamp(selection.primaryFocalX),
            primaryFocalY: clamp(selection.primaryFocalY),
            secondaryFocalX: clamp(selection.secondaryFocalX),
            secondaryFocalY: clamp(selection.secondaryFocalY),
            imageMode: [modeAutomatic, modeSingle, modeGalleryTwo].includes(Number(selection.imageMode))
                ? Number(selection.imageMode)
                : modeAutomatic
        });
    });

    const initialChecked = rows
        .filter(row => row.querySelector("[data-brochure-project-checkbox]")?.checked)
        .map(row => Number(row.dataset.projectId));
    let orderedIds = (initialSelections.length
        ? initialSelections.map(selection => Number(selection.projectId))
        : initialChecked)
        .filter(id => projectById.has(id));
    orderedIds = [...new Set(orderedIds)].slice(0, MAX_PROJECTS);
    orderedIds.forEach(id => {
        if (!configs.has(id)) configs.set(id, defaultConfig(projectById.get(id)));
    });

    let activePhotoProjectId = null;
    let draggedId = null;
    let preflightTimer = null;
    let preflightAbort = null;
    let lastPreflight = null;
    let showAllFindings = false;

    const ensureConfig = id => {
        if (!configs.has(id)) {
            const project = projectById.get(id);
            if (project) configs.set(id, defaultConfig(project));
        }
        return configs.get(id);
    };

    const sourceKind = () => {
        const value = narrativeSource?.value ?? "ProjectBrief";
        if (value === "2" || value === "CapabilityOverview") return "capability";
        if (value === "3" || value === "FullDescription") return "description";
        return "brief";
    };

    const narrativeInfo = project => {
        const source = sourceKind();
        if (source === "capability") {
            return { ready: Boolean(project.hasCapabilityOverview), words: Number(project.capabilityOverviewWordCount || 0), label: "Capability Overview" };
        }
        if (source === "description") {
            return { ready: Boolean(project.hasFullDescription), words: Number(project.fullDescriptionWordCount || 0), label: "Full Description" };
        }
        return { ready: Boolean(project.hasProjectBrief), words: Number(project.projectBriefWordCount || 0), label: "Project Brief" };
    };

    const createImage = (src, alt = "") => {
        const image = document.createElement("img");
        image.alt = alt;
        image.loading = "lazy";
        image.src = src;
        image.addEventListener("error", () => {
            image.classList.add("is-broken");
            image.removeAttribute("src");
            image.closest(".brochure-photo-thumb, .brochure-selected-item__thumb, .brochure-photo-choice")?.classList.add("is-image-missing");
        }, { once: true });
        return image;
    };

    form.querySelectorAll("[data-brochure-photo-thumb]").forEach(image => {
        image.addEventListener("error", () => {
            image.classList.add("is-broken");
            image.closest(".brochure-photo-thumb")?.classList.add("is-image-missing");
        }, { once: true });
    });

    const makeHidden = (name, value) => {
        const input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        input.value = String(value);
        return input;
    };

    const syncHiddenInputs = () => {
        if (!hiddenInputs) return;
        const inputs = [];
        orderedIds.forEach((id, index) => {
            const config = ensureConfig(id);
            if (!config) return;
            const prefix = `Input.Selections[${index}]`;
            inputs.push(makeHidden(`${prefix}.ProjectId`, id));
            if (config.primaryPhotoId != null) inputs.push(makeHidden(`${prefix}.PrimaryPhotoId`, config.primaryPhotoId));
            if (config.secondaryPhotoId != null) inputs.push(makeHidden(`${prefix}.SecondaryPhotoId`, config.secondaryPhotoId));
            inputs.push(makeHidden(`${prefix}.PrimaryFocalX`, clamp(config.primaryFocalX).toFixed(4)));
            inputs.push(makeHidden(`${prefix}.PrimaryFocalY`, clamp(config.primaryFocalY).toFixed(4)));
            inputs.push(makeHidden(`${prefix}.SecondaryFocalX`, clamp(config.secondaryFocalX).toFixed(4)));
            inputs.push(makeHidden(`${prefix}.SecondaryFocalY`, clamp(config.secondaryFocalY).toFixed(4)));
            inputs.push(makeHidden(`${prefix}.ImageMode`, Number(config.imageMode) || modeAutomatic));
        });
        hiddenInputs.replaceChildren(...inputs);
    };

    const updateNarrativeIndicators = () => {
        rows.forEach(row => {
            const project = projectById.get(Number(row.dataset.projectId));
            if (!project) return;
            const info = narrativeInfo(project);
            const indicator = row.querySelector("[data-brochure-narrative-status]");
            const icon = indicator?.querySelector("i");
            const label = row.querySelector("[data-brochure-narrative-label]");
            const words = row.querySelector("[data-brochure-word-count]");
            indicator?.classList.toggle("is-ready", info.ready);
            indicator?.classList.toggle("is-missing", !info.ready);
            if (icon) icon.className = `bi ${info.ready ? "bi-check-circle-fill" : "bi-exclamation-circle-fill"}`;
            if (label) label.textContent = info.label;
            if (words) words.textContent = info.ready ? `${info.words} word${info.words === 1 ? "" : "s"}` : "Not recorded";
        });
    };

    const modeLabel = mode => mode === modeSingle ? "Single" : mode === modeGalleryTwo ? "Gallery 2" : "Automatic";

    const selectedItem = (id, index) => {
        const project = projectById.get(id);
        const config = ensureConfig(id);
        const primary = getPhoto(id, config?.primaryPhotoId);
        const item = document.createElement("li");
        item.className = "brochure-selected-item";
        if (activePhotoProjectId === id) item.classList.add("is-photo-editing");
        item.draggable = true;
        item.dataset.selectedId = String(id);

        const handle = document.createElement("span");
        handle.className = "brochure-selected-item__handle";
        handle.title = "Drag to reorder";
        handle.innerHTML = '<i class="bi bi-grip-vertical" aria-hidden="true"></i>';

        const thumb = document.createElement("span");
        thumb.className = "brochure-selected-item__thumb";
        if (primary?.thumbnailUrl) {
            thumb.append(createImage(primary.thumbnailUrl));
            const fallback = document.createElement("i");
            fallback.className = "bi bi-image";
            fallback.setAttribute("aria-hidden", "true");
            thumb.append(fallback);
        } else {
            thumb.classList.add("is-empty");
            thumb.innerHTML = '<i class="bi bi-image" aria-hidden="true"></i>';
        }

        const copy = document.createElement("span");
        copy.className = "brochure-selected-item__copy";
        const name = document.createElement("span");
        name.className = "brochure-selected-item__name";
        name.textContent = project?.projectName ?? `Project ${id}`;
        const meta = document.createElement("span");
        meta.className = "brochure-selected-item__meta";
        meta.textContent = `${modeLabel(config?.imageMode)} · ${projectPhotos(id).length} photo${projectPhotos(id).length === 1 ? "" : "s"}`;
        copy.append(name, meta);

        const actions = document.createElement("span");
        actions.className = "brochure-selected-item__actions";
        const imageButton = document.createElement("button");
        imageButton.type = "button";
        imageButton.dataset.editImages = "";
        imageButton.title = "Configure brochure imagery";
        imageButton.innerHTML = '<i class="bi bi-crop" aria-hidden="true"></i><span>Images</span>';
        imageButton.addEventListener("click", () => openPhotoEditor(id));
        actions.append(imageButton);

        [[-1, "bi-chevron-up", "Move up"], [1, "bi-chevron-down", "Move down"]].forEach(([delta, icon, title]) => {
            const button = document.createElement("button");
            button.type = "button";
            button.title = title;
            button.disabled = delta < 0 ? index === 0 : index === orderedIds.length - 1;
            button.innerHTML = `<i class="bi ${icon}" aria-hidden="true"></i>`;
            button.addEventListener("click", () => move(id, delta));
            actions.append(button);
        });

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.title = "Remove";
        removeButton.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
        removeButton.addEventListener("click", () => remove(id));
        actions.append(removeButton);

        item.append(handle, thumb, copy, actions);
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
            const next = [...orderedIds];
            next.splice(from, 1);
            next.splice(to, 0, draggedId);
            orderedIds = next;
            renderSelected(false);
        });
        return item;
    };

    const move = (id, delta) => {
        const index = orderedIds.indexOf(id);
        const target = index + delta;
        if (index < 0 || target < 0 || target >= orderedIds.length) return;
        const next = [...orderedIds];
        [next[index], next[target]] = [next[target], next[index]];
        orderedIds = next;
        renderSelected(false);
    };

    const add = id => {
        if (!projectById.has(id) || orderedIds.includes(id) || orderedIds.length >= MAX_PROJECTS) return;
        ensureConfig(id);
        orderedIds.push(id);
        renderSelected(true);
    };

    const remove = id => {
        orderedIds = orderedIds.filter(value => value !== id);
        if (activePhotoProjectId === id) closePhotoEditor();
        renderSelected(true);
    };

    const renderSelected = (runPreflight = true) => {
        orderedIds = orderedIds.filter(id => projectById.has(id)).slice(0, MAX_PROJECTS);
        selectedList?.replaceChildren(...orderedIds.map(selectedItem));
        for (const [id, checkbox] of checkboxes.entries()) {
            if (checkbox) checkbox.checked = orderedIds.includes(id);
        }
        if (selectedCount) selectedCount.textContent = String(orderedIds.length);
        if (selectedInline) selectedInline.textContent = String(orderedIds.length);
        if (selectedEmpty) selectedEmpty.hidden = orderedIds.length !== 0;
        if (clearButton) clearButton.disabled = orderedIds.length === 0;
        syncHiddenInputs();
        applyFilters();
        if (runPreflight) schedulePreflight();
    };

    const photoChoice = (projectId, photo, selected, onClick, allowNone = false) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "brochure-photo-choice";
        button.classList.toggle("is-selected", selected);
        if (allowNone && photo == null) {
            button.classList.add("brochure-photo-choice--none");
            button.innerHTML = '<i class="bi bi-slash-circle" aria-hidden="true"></i><span>No second image</span>';
            button.addEventListener("click", onClick);
            return button;
        }

        button.append(createImage(photo.thumbnailUrl, photo.caption || "Project photograph"));
        const fallback = document.createElement("span");
        fallback.className = "brochure-photo-choice__fallback";
        fallback.innerHTML = '<i class="bi bi-image" aria-hidden="true"></i><span>Preview unavailable</span>';
        button.append(fallback);

        if (photo.isCover) {
            const badges = document.createElement("span");
            badges.className = "brochure-photo-choice__badges";
            const cover = document.createElement("span");
            cover.textContent = "COVER";
            badges.append(cover);
            button.append(badges);
        }
        button.addEventListener("click", onClick);
        return button;
    };

    const sourceMetrics = (stage, image) => {
        const stageWidth = stage?.clientWidth ?? 0;
        const stageHeight = stage?.clientHeight ?? 0;
        const sourceWidth = image?.naturalWidth ?? 0;
        const sourceHeight = image?.naturalHeight ?? 0;
        if (!stageWidth || !stageHeight || !sourceWidth || !sourceHeight) return null;
        const scale = Math.min(stageWidth / sourceWidth, stageHeight / sourceHeight);
        const renderedWidth = sourceWidth * scale;
        const renderedHeight = sourceHeight * scale;
        return {
            sourceWidth,
            sourceHeight,
            scale,
            renderedWidth,
            renderedHeight,
            offsetX: (stageWidth - renderedWidth) / 2,
            offsetY: (stageHeight - renderedHeight) / 2
        };
    };

    const cropForFocal = (sourceWidth, sourceHeight, focalX, focalY) => {
        const targetAspect = 16 / 9;
        const sourceAspect = sourceWidth / sourceHeight;
        let cropWidth;
        let cropHeight;
        if (sourceAspect > targetAspect) {
            cropHeight = sourceHeight;
            cropWidth = cropHeight * targetAspect;
        } else {
            cropWidth = sourceWidth;
            cropHeight = cropWidth / targetAspect;
        }
        cropWidth = Math.min(cropWidth, sourceWidth);
        cropHeight = Math.min(cropHeight, sourceHeight);
        const x = Math.max(0, Math.min(sourceWidth - cropWidth, (clamp(focalX) * sourceWidth) - (cropWidth / 2)));
        const y = Math.max(0, Math.min(sourceHeight - cropHeight, (clamp(focalY) * sourceHeight) - (cropHeight / 2)));
        return { x, y, width: cropWidth, height: cropHeight };
    };

    const positionFocalOverlay = (stage, image, marker, cropFrame, x, y) => {
        const metrics = sourceMetrics(stage, image);
        if (!metrics) return;
        const crop = cropForFocal(metrics.sourceWidth, metrics.sourceHeight, x, y);
        marker.style.left = `${metrics.offsetX + (clamp(x) * metrics.renderedWidth)}px`;
        marker.style.top = `${metrics.offsetY + (clamp(y) * metrics.renderedHeight)}px`;
        if (cropFrame) {
            cropFrame.style.left = `${metrics.offsetX + (crop.x * metrics.scale)}px`;
            cropFrame.style.top = `${metrics.offsetY + (crop.y * metrics.scale)}px`;
            cropFrame.style.width = `${crop.width * metrics.scale}px`;
            cropFrame.style.height = `${crop.height * metrics.scale}px`;
        }
    };

    const updateFocalStage = (kind, projectId) => {
        const config = ensureConfig(projectId);
        const primary = kind === "primary";
        const photo = getPhoto(projectId, primary ? config?.primaryPhotoId : config?.secondaryPhotoId);
        const stage = primary ? primaryStage : secondaryStage;
        const image = primary ? primaryImage : secondaryImage;
        const marker = primary ? primaryMarker : secondaryMarker;
        const cropFrame = primary ? primaryCropFrame : secondaryCropFrame;
        const x = clamp(primary ? config?.primaryFocalX : config?.secondaryFocalX);
        const y = clamp(primary ? config?.primaryFocalY : config?.secondaryFocalY);
        if (!stage || !image || !marker) return;

        stage.hidden = !photo;
        if (!photo?.previewUrl) return;

        const applyOverlay = () => positionFocalOverlay(stage, image, marker, cropFrame, x, y);
        const absolute = new URL(photo.previewUrl, window.location.href).href;
        image.onerror = () => {
            stage.hidden = true;
        };
        if (image.src !== absolute) {
            image.onload = applyOverlay;
            image.src = photo.previewUrl;
        } else if (image.complete && image.naturalWidth > 0) {
            applyOverlay();
        } else {
            image.onload = applyOverlay;
        }
    };

    const renderPhotoEditor = () => {
        if (activePhotoProjectId == null || !photoEditor) return;
        const project = projectById.get(activePhotoProjectId);
        const config = ensureConfig(activePhotoProjectId);
        if (!project || !config) return;

        photoEditor.hidden = false;
        if (photoEditorTitle) photoEditorTitle.textContent = project.projectName;
        if (imageModeSelect) imageModeSelect.value = String(config.imageMode);
        if (secondarySection) secondarySection.hidden = config.imageMode === modeSingle;

        if (primaryPicker) {
            if (!project.photos.length) {
                const empty = document.createElement("div");
                empty.className = "brochure-photo-picker-empty";
                empty.textContent = "No project photograph is recorded.";
                primaryPicker.replaceChildren(empty);
            } else {
                primaryPicker.replaceChildren(...project.photos.map(photo => photoChoice(
                    project.projectId,
                    photo,
                    Number(config.primaryPhotoId) === Number(photo.photoId),
                    () => {
                        config.primaryPhotoId = Number(photo.photoId);
                        if (Number(config.secondaryPhotoId) === Number(photo.photoId)) config.secondaryPhotoId = null;
                        config.primaryFocalX = 0.5;
                        config.primaryFocalY = 0.5;
                        renderPhotoEditor();
                        renderSelected(false);
                        schedulePreflight();
                    })));
            }
        }

        if (secondaryPicker) {
            const choices = [photoChoice(project.projectId, null, config.secondaryPhotoId == null, () => {
                config.secondaryPhotoId = null;
                renderPhotoEditor();
                renderSelected(false);
                schedulePreflight();
            }, true)];
            project.photos
                .filter(photo => Number(photo.photoId) !== Number(config.primaryPhotoId))
                .forEach(photo => choices.push(photoChoice(
                    project.projectId,
                    photo,
                    Number(config.secondaryPhotoId) === Number(photo.photoId),
                    () => {
                        config.secondaryPhotoId = Number(photo.photoId);
                        config.secondaryFocalX = 0.5;
                        config.secondaryFocalY = 0.5;
                        renderPhotoEditor();
                        renderSelected(false);
                        schedulePreflight();
                    })));
            secondaryPicker.replaceChildren(...choices);
        }

        updateFocalStage("primary", activePhotoProjectId);
        updateFocalStage("secondary", activePhotoProjectId);
    };

    const openPhotoEditor = id => {
        activePhotoProjectId = id;
        renderSelected(false);
        renderPhotoEditor();
        window.requestAnimationFrame(() => photoEditor?.scrollIntoView({ block: "nearest", behavior: "smooth" }));
    };

    const closePhotoEditor = () => {
        activePhotoProjectId = null;
        if (photoEditor) photoEditor.hidden = true;
        renderSelected(false);
    };

    const setFocalFromEvent = (kind, event) => {
        if (activePhotoProjectId == null) return;
        const primary = kind === "primary";
        const stage = primary ? primaryStage : secondaryStage;
        const image = primary ? primaryImage : secondaryImage;
        const config = ensureConfig(activePhotoProjectId);
        if (!stage || !image || !config || event.target.closest("button")) return;

        const rect = stage.getBoundingClientRect();
        const metrics = sourceMetrics(stage, image);
        if (!metrics) return;
        const localX = event.clientX - rect.left - metrics.offsetX;
        const localY = event.clientY - rect.top - metrics.offsetY;
        if (localX < 0 || localY < 0 || localX > metrics.renderedWidth || localY > metrics.renderedHeight) return;

        const x = clamp(localX / metrics.renderedWidth);
        const y = clamp(localY / metrics.renderedHeight);
        if (primary) {
            config.primaryFocalX = x;
            config.primaryFocalY = y;
        } else {
            config.secondaryFocalX = x;
            config.secondaryFocalY = y;
        }
        updateFocalStage(kind, activePhotoProjectId);
        syncHiddenInputs();
    };

    const resetFocal = kind => {
        if (activePhotoProjectId == null) return;
        const config = ensureConfig(activePhotoProjectId);
        if (!config) return;
        if (kind === "primary") {
            config.primaryFocalX = 0.5;
            config.primaryFocalY = 0.5;
        } else {
            config.secondaryFocalX = 0.5;
            config.secondaryFocalY = 0.5;
        }
        updateFocalStage(kind, activePhotoProjectId);
        syncHiddenInputs();
    };

    const setMetric = (selector, value) => {
        const node = form.querySelector(selector);
        if (node) node.textContent = String(value);
    };

    const updateButtons = canGenerate => {
        if (previewButton) previewButton.disabled = !canGenerate;
        if (generateButton && generateButton.getAttribute("aria-busy") !== "true") generateButton.disabled = !canGenerate;
    };

    const issueIcon = severity => severity === "blocker"
        ? "bi-x-octagon-fill"
        : severity === "warning"
            ? "bi-exclamation-triangle-fill"
            : "bi-info-circle-fill";

    const photoIssueCodes = new Set([
        "MissingPrimaryPhoto",
        "SelectedPhotoInvalid",
        "SelectedPhotoUnavailable",
        "LowResolutionPhoto",
        "GallerySecondPhotoRequired",
        "GallerySecondPhotoInvalid",
        "GallerySecondPhotoUnavailable",
        "TextOnlyProject"
    ]);

    const resetFiltersForProject = projectId => {
        if (searchInput) searchInput.value = "";
        filters.forEach(filter => { filter.value = ""; });
        if (selectedOnly) selectedOnly.checked = false;
        applyFilters();
        const row = rowById.get(Number(projectId));
        if (!row) return;
        row.scrollIntoView({ block: "center", behavior: "smooth" });
        row.classList.add("is-highlighted");
        window.setTimeout(() => row.classList.remove("is-highlighted"), 1800);
    };

    const createIssueAction = issue => {
        if (!issue.projectId) return null;
        const project = projectById.get(Number(issue.projectId));
        if (!project) return null;
        const actions = document.createElement("span");
        actions.className = "brochure-preflight-issue__actions";

        const locate = document.createElement("button");
        locate.type = "button";
        locate.textContent = "Locate";
        locate.addEventListener("click", () => resetFiltersForProject(issue.projectId));
        actions.append(locate);

        if (issue.code === "MissingNarrative" && project.overviewUrl) {
            const link = document.createElement("a");
            link.href = project.overviewUrl;
            link.target = "_blank";
            link.rel = "noopener";
            link.textContent = "Open project brief";
            actions.append(link);
        } else if (photoIssueCodes.has(issue.code)) {
            if (orderedIds.includes(Number(issue.projectId)) && project.photos?.length) {
                const configure = document.createElement("button");
                configure.type = "button";
                configure.textContent = "Configure image";
                configure.addEventListener("click", () => openPhotoEditor(Number(issue.projectId)));
                actions.append(configure);
            }
            if (project.photosUrl) {
                const link = document.createElement("a");
                link.href = project.photosUrl;
                link.target = "_blank";
                link.rel = "noopener";
                link.textContent = project.photos?.length ? "Manage photos" : "Add photo";
                actions.append(link);
            }
        }
        return actions;
    };

    const renderIssues = issues => {
        if (!preflightIssues) return;
        const ordered = [...issues].sort((a, b) => {
            const rank = { blocker: 0, warning: 1, information: 2 };
            return (rank[a.severity] ?? 3) - (rank[b.severity] ?? 3);
        });
        const visible = showAllFindings ? ordered : ordered.slice(0, 6);
        const nodes = visible.map(issue => {
            const item = document.createElement("div");
            item.className = `brochure-preflight-issue is-${issue.severity}`;
            const icon = document.createElement("i");
            icon.className = `bi ${issueIcon(issue.severity)}`;
            icon.setAttribute("aria-hidden", "true");
            const body = document.createElement("span");
            body.className = "brochure-preflight-issue__body";
            if (issue.projectName) {
                const strong = document.createElement("strong");
                strong.textContent = issue.projectName;
                body.append(strong);
            }
            const message = document.createElement("span");
            message.textContent = issue.message;
            body.append(message);
            const actions = createIssueAction(issue);
            if (actions) body.append(actions);
            item.append(icon, body);
            return item;
        });
        preflightIssues.replaceChildren(...nodes);
        if (preflightShowAll) {
            const hasMore = ordered.length > 6;
            preflightShowAll.hidden = !hasMore;
            preflightShowAll.textContent = showAllFindings
                ? "Show fewer findings"
                : `Show all ${ordered.length} findings`;
        }
    };

    const renderPreflight = result => {
        lastPreflight = result;
        setMetric("[data-preflight-selected]", result.selectedProjectCount ?? orderedIds.length);
        setMetric("[data-preflight-blockers]", result.blockerCount ?? 0);
        setMetric("[data-preflight-warnings]", result.warningCount ?? 0);
        setMetric("[data-preflight-info]", result.informationCount ?? 0);
        preflightSpinner?.toggleAttribute("hidden", true);

        if (preflightMessage) {
            preflightMessage.classList.remove("is-checking", "is-blocked", "is-warning", "is-ready");
            if (!orderedIds.length) {
                preflightMessage.textContent = "Select projects to run publication preflight.";
            } else if ((result.blockerCount ?? 0) > 0) {
                preflightMessage.textContent = `${result.blockerCount} blocker${result.blockerCount === 1 ? "" : "s"} must be resolved before preview or download.`;
                preflightMessage.classList.add("is-blocked");
            } else if ((result.warningCount ?? 0) > 0) {
                preflightMessage.textContent = `Preflight passed with ${result.warningCount} warning${result.warningCount === 1 ? "" : "s"}. Preview the PDF before final download.`;
                preflightMessage.classList.add("is-warning");
            } else {
                preflightMessage.textContent = "Publication preflight passed. Selected records and source images are ready.";
                preflightMessage.classList.add("is-ready");
            }
        }

        renderIssues(result.issues ?? []);
        updateButtons(Boolean(result.canGenerate));
    };

    const runPreflight = async () => {
        if (!orderedIds.length) {
            showAllFindings = false;
            renderPreflight({ selectedProjectCount: 0, blockerCount: 0, warningCount: 0, informationCount: 0, canGenerate: false, issues: [] });
            return;
        }
        if (!preflightUrl) return;

        preflightAbort?.abort();
        preflightAbort = new AbortController();
        syncHiddenInputs();
        updateButtons(false);
        preflightSpinner?.removeAttribute("hidden");
        if (preflightMessage) {
            preflightMessage.classList.remove("is-blocked", "is-warning", "is-ready");
            preflightMessage.classList.add("is-checking");
            preflightMessage.textContent = "Checking selected narratives and publication source images…";
        }

        try {
            const response = await fetch(preflightUrl, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" },
                signal: preflightAbort.signal
            });
            if (!response.ok) throw new Error(`Preflight failed with HTTP ${response.status}`);
            showAllFindings = false;
            renderPreflight(await response.json());
        } catch (error) {
            if (error?.name === "AbortError") return;
            preflightSpinner?.toggleAttribute("hidden", true);
            lastPreflight = null;
            updateButtons(false);
            if (preflightMessage) {
                preflightMessage.classList.remove("is-checking", "is-warning", "is-ready");
                preflightMessage.classList.add("is-blocked");
                preflightMessage.textContent = "Publication preflight could not be completed. Preview and download remain disabled until the server check succeeds.";
            }
            preflightIssues?.replaceChildren();
            if (preflightShowAll) preflightShowAll.hidden = true;
            console.error(error);
        }
    };

    const schedulePreflight = () => {
        window.clearTimeout(preflightTimer);
        lastPreflight = null;
        updateButtons(false);
        preflightTimer = window.setTimeout(runPreflight, 280);
    };

    const readinessMatches = (project, value) => {
        if (!value) return true;
        const narrative = narrativeInfo(project);
        const hasPhoto = (project.photos?.length ?? 0) > 0;
        if (value === "ready") return narrative.ready && hasPhoto;
        if (value === "missing-copy") return !narrative.ready;
        if (value === "missing-photo") return !hasPhoto;
        return true;
    };

    const visibleRows = () => rows.filter(row => !row.hidden);

    const updateSelectVisible = () => {
        if (!selectVisibleButton || !selectVisibleLabel) return;
        const visible = visibleRows();
        const unselected = visible.filter(row => !orderedIds.includes(Number(row.dataset.projectId)));
        const allSelected = visible.length > 0 && unselected.length === 0;
        const slots = Math.max(0, MAX_PROJECTS - orderedIds.length);
        selectVisibleButton.disabled = visible.length === 0 || (!allSelected && slots === 0);
        if (allSelected) {
            selectVisibleLabel.textContent = `Deselect ${visible.length} visible`;
            selectVisibleButton.dataset.mode = "deselect";
        } else if (slots === 0) {
            selectVisibleLabel.textContent = `${MAX_PROJECTS} project limit reached`;
            selectVisibleButton.dataset.mode = "limit";
        } else {
            const count = Math.min(unselected.length, slots);
            selectVisibleLabel.textContent = `Select ${count} visible`;
            selectVisibleButton.dataset.mode = "select";
        }
    };

    const applyFilters = () => {
        const query = normalize(searchInput?.value);
        const filterValues = new Map(filters.map(filter => [filter.dataset.brochureFilter, normalize(filter.value)]));
        let visible = 0;
        rows.forEach(row => {
            const id = Number(row.dataset.projectId);
            const project = projectById.get(id);
            const show = Boolean(project)
                && (!query || normalize(row.dataset.projectName).includes(query))
                && (!filterValues.get("lifecycle") || row.dataset.lifecycle === filterValues.get("lifecycle"))
                && (!filterValues.get("category") || row.dataset.category === filterValues.get("category"))
                && (!filterValues.get("technical") || row.dataset.technical === filterValues.get("technical"))
                && readinessMatches(project, filterValues.get("readiness"))
                && (!selectedOnly?.checked || orderedIds.includes(id));
            row.hidden = !show;
            if (show) visible += 1;
        });
        if (matchCount) matchCount.textContent = String(visible);
        if (emptyFilterState) emptyFilterState.hidden = visible !== 0;
        updateSelectVisible();
    };

    rows.forEach(row => {
        const id = Number(row.dataset.projectId);
        row.querySelector("[data-brochure-project-checkbox]")?.addEventListener("change", event => {
            if (event.currentTarget.checked) add(id);
            else remove(id);
        });
    });

    searchInput?.addEventListener("input", applyFilters);
    filters.forEach(filter => filter.addEventListener("change", applyFilters));
    selectedOnly?.addEventListener("change", applyFilters);

    selectVisibleButton?.addEventListener("click", () => {
        const visible = visibleRows();
        if (selectVisibleButton.dataset.mode === "deselect") {
            const visibleIds = new Set(visible.map(row => Number(row.dataset.projectId)));
            orderedIds = orderedIds.filter(id => !visibleIds.has(id));
            if (activePhotoProjectId != null && visibleIds.has(activePhotoProjectId)) closePhotoEditor();
        } else {
            for (const row of visible) {
                if (orderedIds.length >= MAX_PROJECTS) break;
                const id = Number(row.dataset.projectId);
                if (!orderedIds.includes(id)) {
                    ensureConfig(id);
                    orderedIds.push(id);
                }
            }
        }
        renderSelected(true);
    });

    clearButton?.addEventListener("click", () => {
        orderedIds = [];
        closePhotoEditor();
        renderSelected(true);
    });

    narrativeSource?.addEventListener("change", () => {
        updateNarrativeIndicators();
        applyFilters();
        schedulePreflight();
    });

    form.querySelectorAll("[data-brochure-preflight-trigger]").forEach(element => {
        if (element === narrativeSource) return;
        element.addEventListener("change", schedulePreflight);
    });

    form.querySelectorAll("[data-cover-option] input[type=radio]").forEach(radio => {
        radio.addEventListener("change", () => {
            form.querySelectorAll("[data-cover-option]").forEach(option => {
                option.classList.toggle("is-selected", option.querySelector("input")?.checked === true);
            });
        });
    });

    imageModeSelect?.addEventListener("change", () => {
        if (activePhotoProjectId == null) return;
        const config = ensureConfig(activePhotoProjectId);
        if (!config) return;
        config.imageMode = Number(imageModeSelect.value) || modeAutomatic;
        // Gallery 2 deliberately does not auto-pick a second image. The user must make
        // the editorial choice, and preflight will block until that choice is complete.
        renderPhotoEditor();
        renderSelected(false);
        schedulePreflight();
    });

    photoEditorClose?.addEventListener("click", closePhotoEditor);
    primaryStage?.addEventListener("click", event => setFocalFromEvent("primary", event));
    secondaryStage?.addEventListener("click", event => setFocalFromEvent("secondary", event));
    primaryReset?.addEventListener("click", event => { event.stopPropagation(); resetFocal("primary"); });
    secondaryReset?.addEventListener("click", event => { event.stopPropagation(); resetFocal("secondary"); });

    preflightShowAll?.addEventListener("click", () => {
        showAllFindings = !showAllFindings;
        renderIssues(lastPreflight?.issues ?? []);
    });

    form.addEventListener("submit", event => {
        const isPreview = event.submitter?.matches("[data-brochure-preview]") === true;
        if (!lastPreflight?.canGenerate || orderedIds.length === 0) {
            event.preventDefault();
            return;
        }
        syncHiddenInputs();
        if (isPreview) return;
        if (generateButton?.getAttribute("aria-busy") === "true") {
            event.preventDefault();
            return;
        }
        if (generateButton) {
            generateButton.setAttribute("aria-busy", "true");
            generateButton.disabled = true;
        }
        generateSpinner?.classList.remove("d-none");
        generateIcon?.classList.add("d-none");
        if (generateLabel) generateLabel.textContent = "Generating brochure…";
    });

    window.addEventListener("resize", () => {
        if (activePhotoProjectId != null) {
            updateFocalStage("primary", activePhotoProjectId);
            updateFocalStage("secondary", activePhotoProjectId);
        }
    });

    window.addEventListener("pageshow", () => {
        if (generateButton) generateButton.setAttribute("aria-busy", "false");
        generateSpinner?.classList.add("d-none");
        generateIcon?.classList.remove("d-none");
        if (generateLabel) generateLabel.textContent = "Download brochure PDF";
        updateButtons(Boolean(lastPreflight?.canGenerate));
    });

    updateNarrativeIndicators();
    renderSelected(false);
    applyFilters();
    schedulePreflight();
})();
