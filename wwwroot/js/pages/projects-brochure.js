(() => {
    "use strict";

    const form = document.querySelector("[data-brochure-form]");
    if (!form) return;

    const MAX_PROJECTS = 100;
    const projectDataNode = form.querySelector("[data-brochure-project-data]");
    const initialSelectionsNode = form.querySelector("[data-brochure-initial-selections]");
    const parseJson = (node, fallback) => {
        try {
            return node?.textContent ? JSON.parse(node.textContent) : fallback;
        } catch {
            return fallback;
        }
    };

    const projects = parseJson(projectDataNode, []);
    const initialSelections = parseJson(initialSelectionsNode, []);
    const projectById = new Map(projects.map(project => [Number(project.projectId), project]));

    const rows = [...form.querySelectorAll("[data-brochure-project-row]")];
    const rowById = new Map(rows.map(row => [Number(row.dataset.projectId), row]));
    const checkboxes = new Map(rows.map(row => [Number(row.dataset.projectId), row.querySelector("[data-brochure-project-checkbox]")]));
    const hiddenInputs = form.querySelector("[data-brochure-hidden-inputs]");
    const selectedList = form.querySelector("[data-brochure-selected-list]");
    const selectedEmpty = form.querySelector("[data-brochure-selected-empty]");
    const selectedCount = form.querySelector("[data-brochure-selected-count]");
    const clearButton = form.querySelector("[data-brochure-clear]");
    const selectVisibleButton = form.querySelector("[data-brochure-select-visible]");
    const searchInput = form.querySelector("[data-brochure-search]");
    const filters = [...form.querySelectorAll("[data-brochure-filter]")];
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
    const preflightUrl = form.dataset.brochurePreflightUrl;

    const normalize = value => (value ?? "").trim().toLowerCase();
    const clamp = value => Math.max(0, Math.min(1, Number.isFinite(Number(value)) ? Number(value) : 0.5));
    const modeAutomatic = 1;
    const modeSingle = 2;
    const modeGalleryTwo = 3;

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
    let orderedIds = initialSelections.length
        ? initialSelections.map(selection => Number(selection.projectId)).filter(id => projectById.has(id))
        : initialChecked.filter(id => projectById.has(id));
    orderedIds = [...new Set(orderedIds)].slice(0, MAX_PROJECTS);
    orderedIds.forEach(id => {
        if (!configs.has(id)) configs.set(id, defaultConfig(projectById.get(id)));
    });

    let draggedId = null;
    let activePhotoProjectId = null;
    let preflightTimer = null;
    let preflightAbort = null;
    let lastPreflight = null;

    const projectPhotos = id => projectById.get(id)?.photos ?? [];
    const getPhoto = (id, photoId) => projectPhotos(id).find(photo => Number(photo.photoId) === Number(photoId)) ?? null;
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
            return { ready: project.hasCapabilityOverview, words: project.capabilityOverviewWordCount, label: "Capability Overview" };
        }
        if (source === "description") {
            return { ready: project.hasFullDescription, words: project.fullDescriptionWordCount, label: "Full Description" };
        }
        return { ready: project.hasProjectBrief, words: project.projectBriefWordCount, label: "Project Brief" };
    };

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
            const label = row.querySelector("[data-brochure-narrative-label]");
            const words = row.querySelector("[data-brochure-word-count]");
            if (indicator) {
                indicator.classList.toggle("is-ready", Boolean(info.ready));
                indicator.classList.toggle("is-missing", !info.ready);
                indicator.title = info.ready ? `${info.label} ready` : `${info.label} missing`;
            }
            if (label) label.textContent = info.label;
            if (words) words.textContent = info.ready ? `${info.words ?? 0} words` : "Not recorded";
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
            const image = document.createElement("img");
            image.src = primary.thumbnailUrl;
            image.alt = "";
            image.loading = "lazy";
            thumb.append(image);
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
        imageButton.innerHTML = '<i class="bi bi-crop" aria-hidden="true"></i> Images';
        imageButton.addEventListener("click", () => openPhotoEditor(id));
        actions.append(imageButton);

        [["moveUp", -1, "bi-chevron-up", "Move up"], ["moveDown", 1, "bi-chevron-down", "Move down"]].forEach(([key, delta, icon, title]) => {
            const button = document.createElement("button");
            button.type = "button";
            button.dataset[key] = "";
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
            const copyIds = [...orderedIds];
            copyIds.splice(from, 1);
            copyIds.splice(to, 0, draggedId);
            orderedIds = copyIds;
            renderSelected(false);
        });
        return item;
    };

    const move = (id, delta) => {
        const index = orderedIds.indexOf(id);
        const target = index + delta;
        if (index < 0 || target < 0 || target >= orderedIds.length) return;
        const copy = [...orderedIds];
        [copy[index], copy[target]] = [copy[target], copy[index]];
        orderedIds = copy;
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
        const checkbox = checkboxes.get(id);
        if (checkbox) checkbox.checked = false;
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
        if (selectedEmpty) selectedEmpty.hidden = orderedIds.length !== 0;
        if (clearButton) clearButton.disabled = orderedIds.length === 0;
        syncHiddenInputs();
        if (runPreflight) schedulePreflight();
    };

    const photoChoice = (projectId, photo, selected, onClick, allowNone = false) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "brochure-photo-choice";
        if (selected) button.classList.add("is-selected");
        if (allowNone && photo == null) {
            button.classList.add("brochure-photo-choice--none");
            button.textContent = "No second image";
            button.addEventListener("click", onClick);
            return button;
        }

        const image = document.createElement("img");
        image.src = photo.thumbnailUrl;
        image.alt = photo.caption || "Project photograph";
        image.loading = "lazy";
        button.append(image);

        const badges = document.createElement("span");
        badges.className = "brochure-photo-choice__badges";
        if (photo.isCover) {
            const cover = document.createElement("span");
            cover.textContent = "COVER";
            badges.append(cover);
        }
        if (photo.isLowResolution) {
            const warning = document.createElement("span");
            warning.className = "is-warning";
            warning.textContent = "LOW RES";
            badges.append(warning);
        }
        if (badges.children.length) button.append(badges);
        button.addEventListener("click", onClick);
        return button;
    };

    const sourceMetrics = (stage, image, photo) => {
        const stageWidth = stage?.clientWidth ?? 0;
        const stageHeight = stage?.clientHeight ?? 0;
        const sourceWidth = image?.naturalWidth || Number(photo?.width) || 0;
        const sourceHeight = image?.naturalHeight || Number(photo?.height) || 0;
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

    const positionFocalOverlay = (stage, image, marker, cropFrame, photo, x, y) => {
        const metrics = sourceMetrics(stage, image, photo);
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
        const photoId = primary ? config?.primaryPhotoId : config?.secondaryPhotoId;
        const photo = getPhoto(projectId, photoId);
        const stage = primary ? primaryStage : secondaryStage;
        const image = primary ? primaryImage : secondaryImage;
        const marker = primary ? primaryMarker : secondaryMarker;
        const cropFrame = primary ? primaryCropFrame : secondaryCropFrame;
        const x = clamp(primary ? config?.primaryFocalX : config?.secondaryFocalX);
        const y = clamp(primary ? config?.primaryFocalY : config?.secondaryFocalY);
        if (!stage || !image || !marker) return;

        stage.hidden = !photo;
        if (!photo) return;
        const src = photo.previewUrl || photo.thumbnailUrl;
        if (!src) {
            stage.hidden = true;
            return;
        }
        const applyOverlay = () => positionFocalOverlay(stage, image, marker, cropFrame, photo, x, y);
        if (image.src !== new URL(src, window.location.href).href) {
            image.addEventListener("load", applyOverlay, { once: true });
            image.src = src;
        } else if (image.complete) {
            applyOverlay();
        } else {
            image.addEventListener("load", applyOverlay, { once: true });
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
                        if (Number(config.secondaryPhotoId) === Number(photo.photoId)) {
                            // Do not silently substitute another secondary image. A second
                            // brochure photograph is an explicit editorial selection.
                            config.secondaryPhotoId = null;
                        }
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
        const photo = getPhoto(activePhotoProjectId, primary ? config?.primaryPhotoId : config?.secondaryPhotoId);
        if (!stage || !image || !photo || event.target.closest("button")) return;

        const rect = stage.getBoundingClientRect();
        const metrics = sourceMetrics(stage, image, photo);
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
                preflightMessage.textContent = `${result.blockerCount} blocker${result.blockerCount === 1 ? "" : "s"} must be resolved before preview or export.`;
                preflightMessage.classList.add("is-blocked");
            } else if ((result.warningCount ?? 0) > 0) {
                preflightMessage.textContent = `Preflight passed with ${result.warningCount} warning${result.warningCount === 1 ? "" : "s"}. Preview the PDF before final export.`;
                preflightMessage.classList.add("is-warning");
            } else {
                preflightMessage.textContent = "Publication preflight passed. The selected records and image files are ready.";
                preflightMessage.classList.add("is-ready");
            }
        }

        if (preflightIssues) {
            const issues = (result.issues ?? []).slice(0, 8).map(issue => {
                const item = document.createElement("div");
                item.className = `brochure-preflight-issue is-${issue.severity}`;
                item.innerHTML = `<i class="bi ${issueIcon(issue.severity)}" aria-hidden="true"></i><span></span>`;
                const content = item.querySelector("span");
                if (issue.projectName) {
                    const strong = document.createElement("strong");
                    strong.textContent = issue.projectName;
                    content.append(strong);
                }
                content.append(document.createTextNode(issue.message));
                return item;
            });
            preflightIssues.replaceChildren(...issues);
        }
        updateButtons(Boolean(result.canGenerate));
    };

    const runPreflight = async () => {
        if (!orderedIds.length) {
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
            preflightMessage.textContent = "Checking selected narratives and photograph files…";
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
            renderPreflight(await response.json());
        } catch (error) {
            if (error?.name === "AbortError") return;
            preflightSpinner?.toggleAttribute("hidden", true);
            lastPreflight = null;
            updateButtons(false);
            if (preflightMessage) {
                preflightMessage.classList.remove("is-checking", "is-warning", "is-ready");
                preflightMessage.classList.add("is-blocked");
                preflightMessage.textContent = "Publication preflight could not be completed. Preview and export remain disabled until the server check succeeds.";
            }
            if (preflightIssues) preflightIssues.replaceChildren();
            console.error(error);
        }
    };

    const schedulePreflight = () => {
        window.clearTimeout(preflightTimer);
        lastPreflight = null;
        updateButtons(false);
        preflightTimer = window.setTimeout(runPreflight, 280);
    };

    const applyFilters = () => {
        const query = normalize(searchInput?.value);
        const filterValues = new Map(filters.map(filter => [filter.dataset.brochureFilter, normalize(filter.value)]));
        let visible = 0;
        rows.forEach(row => {
            const show = (!query || normalize(row.dataset.projectName).includes(query))
                && (!filterValues.get("lifecycle") || row.dataset.lifecycle === filterValues.get("lifecycle"))
                && (!filterValues.get("category") || row.dataset.category === filterValues.get("category"))
                && (!filterValues.get("technical") || row.dataset.technical === filterValues.get("technical"));
            row.hidden = !show;
            if (show) visible++;
        });
        if (emptyFilterState) emptyFilterState.hidden = visible !== 0;
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
    selectVisibleButton?.addEventListener("click", () => {
        for (const row of rows.filter(candidate => !candidate.hidden)) {
            if (orderedIds.length >= MAX_PROJECTS) break;
            const id = Number(row.dataset.projectId);
            if (!orderedIds.includes(id)) {
                ensureConfig(id);
                orderedIds.push(id);
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
        config.imageMode = Number(imageModeSelect.value) || modeAutomatic;
        if (config.imageMode === modeGalleryTwo && config.secondaryPhotoId == null) {
            config.secondaryPhotoId = projectPhotos(activePhotoProjectId)
                .find(photo => Number(photo.photoId) !== Number(config.primaryPhotoId))?.photoId ?? null;
        }
        renderPhotoEditor();
        renderSelected(false);
        schedulePreflight();
    });
    photoEditorClose?.addEventListener("click", closePhotoEditor);
    primaryStage?.addEventListener("click", event => setFocalFromEvent("primary", event));
    secondaryStage?.addEventListener("click", event => setFocalFromEvent("secondary", event));
    primaryReset?.addEventListener("click", event => { event.stopPropagation(); resetFocal("primary"); });
    secondaryReset?.addEventListener("click", event => { event.stopPropagation(); resetFocal("secondary"); });

    form.addEventListener("submit", event => {
        const submitter = event.submitter;
        const isPreview = submitter?.matches("[data-brochure-preview]") === true;
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
        if (generateLabel) generateLabel.textContent = "Generate brochure PDF";
        updateButtons(Boolean(lastPreflight?.canGenerate));
    });

    updateNarrativeIndicators();
    renderSelected(false);
    applyFilters();
    schedulePreflight();
})();
