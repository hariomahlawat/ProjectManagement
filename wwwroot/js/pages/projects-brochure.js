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
    const approvedPrintContent = parseJson(form.querySelector("[data-brochure-approved-print-content]"), {});
    const projectById = new Map(projects.map(project => [Number(project.projectId), project]));
    const rows = [...form.querySelectorAll("[data-brochure-project-row]")];
    const rowById = new Map(rows.map(row => [Number(row.dataset.projectId), row]));
    const checkboxes = new Map(rows.map(row => [Number(row.dataset.projectId), row.querySelector("[data-brochure-project-checkbox]")]));

    const hiddenInputs = form.querySelector("[data-brochure-hidden-inputs]");
    const coverHeroInput = form.querySelector("[data-brochure-cover-hero-project]");
    const coverHeroPhotoInput = form.querySelector("[data-brochure-cover-hero-photo]");
    const coverHeroFocalXInput = form.querySelector("[data-brochure-cover-hero-focal-x]");
    const coverHeroFocalYInput = form.querySelector("[data-brochure-cover-hero-focal-y]");
    const coverReviewedInput = form.querySelector("[data-brochure-cover-reviewed]");
    const coverReviewFingerprintInput = form.querySelector("[data-brochure-cover-review-fingerprint]");
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
    const publicationProfileInputs = [...form.querySelectorAll("[data-brochure-profile]")];
    const profileOptions = [...form.querySelectorAll("[data-profile-option]")];
    const printOnlySections = [...form.querySelectorAll("[data-brochure-print-only]")];
    const digitalOnlySections = [...form.querySelectorAll("[data-brochure-digital-only]")];
    const previewButton = form.querySelector("[data-brochure-preview]");
    const generateButton = form.querySelector("[data-brochure-generate]");
    const generateSpinner = form.querySelector("[data-brochure-generate-spinner]");
    const generateIcon = form.querySelector("[data-brochure-generate-icon]");
    const generateLabel = form.querySelector("[data-brochure-generate-label]");
    const exportStatus = form.querySelector("[data-brochure-export-status]");
    const outputReadiness = form.querySelector("[data-output-readiness]");
    const outputReadinessIcon = form.querySelector("[data-output-readiness-icon]");
    const outputReadinessTitle = form.querySelector("[data-output-readiness-title]");
    const outputReadinessDetail = form.querySelector("[data-output-readiness-detail]");
    const printMatterFields = [...form.querySelectorAll("[data-brochure-print-matter]")];
    const restoreApprovedPrint = form.querySelector("[data-print-restore-approved]");
    const printPlanSummary = form.querySelector("[data-print-plan-summary]");
    const printEstimatePages = form.querySelector("[data-print-estimate-pages]");
    const printEstimateFill = form.querySelector("[data-print-estimate-fill]");
    const printLowestFill = form.querySelector("[data-print-lowest-fill]");
    const printFinalFill = form.querySelector("[data-print-final-fill]");
    const printEstimateClosing = form.querySelector("[data-print-estimate-closing]");
    const printSheetMap = form.querySelector("[data-print-sheet-map]");
    const smartFlowPanel = form.querySelector("[data-smart-flow]");
    const smartFlowSummary = form.querySelector("[data-smart-flow-summary]");
    const smartFlowPages = form.querySelector("[data-smart-flow-pages]");
    const smartFlowFill = form.querySelector("[data-smart-flow-fill]");
    const smartFlowMoves = form.querySelector("[data-smart-flow-moves]");
    const smartFlowMoveList = form.querySelector("[data-smart-flow-move-list]");
    const smartFlowTreatment = form.querySelector("[data-smart-flow-treatment]");
    const smartFlowSheetMap = form.querySelector("[data-smart-flow-sheet-map]");
    const smartFlowApply = form.querySelector("[data-smart-flow-apply]");
    const smartFlowUndo = form.querySelector("[data-smart-flow-undo]");

    const preflightSpinner = form.querySelector("[data-preflight-spinner]");
    const preflightMessage = form.querySelector("[data-preflight-message]");
    const preflightIssues = form.querySelector("[data-preflight-issues]");
    const preflightShowAll = form.querySelector("[data-preflight-show-all]");
    const preflightUrl = form.dataset.brochurePreflightUrl;
    const projectStateUrl = form.dataset.brochureProjectStateUrl;
    const previewUrl = form.dataset.brochurePreviewUrl;
    const generateUrl = form.dataset.brochureGenerateUrl;

    const photoEditor = form.querySelector("[data-brochure-photo-editor]");
    const photoEditorTitle = form.querySelector("[data-photo-editor-title]");
    const photoEditorProjectName = form.querySelector("[data-photo-editor-project-name]");
    const photoEditorCloseButtons = [...form.querySelectorAll("[data-photo-editor-close]")];
    const photoEditorDismiss = form.querySelector("[data-photo-editor-dismiss]");
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

    const institutionalArtworkPanel = form.querySelector("[data-brochure-institutional-artwork-panel]");
    const coverHeroPanel = form.querySelector("[data-brochure-cover-hero-panel]");
    const coverHeroCurrent = form.querySelector("[data-cover-hero-current]");
    const coverHeroImage = form.querySelector("[data-cover-hero-image]");
    const coverHeroName = form.querySelector("[data-cover-hero-name]");
    const coverHeroMeta = form.querySelector("[data-cover-hero-meta]");
    const coverHeroChoose = form.querySelector("[data-cover-hero-choose]");
    const coverHeroAutomatic = form.querySelector("[data-cover-hero-automatic]");
    const coverHeroCrop = form.querySelector("[data-cover-hero-crop]");
    const coverHeroApprove = form.querySelector("[data-cover-hero-approve]");
    const coverHeroReviewState = form.querySelector("[data-cover-hero-review-state]");
    const coverHeroChoices = form.querySelector("[data-cover-hero-choices]");
    const coverHeroCropPanel = form.querySelector("[data-cover-hero-crop-panel]");
    const coverHeroFocalStage = form.querySelector("[data-cover-hero-focal-stage]");
    const coverHeroFocalImage = form.querySelector("[data-cover-hero-focal-image]");
    const coverHeroCropFrame = form.querySelector("[data-cover-hero-crop-frame]");
    const coverHeroFocalMarker = form.querySelector("[data-cover-hero-focal-marker]");
    const coverHeroFocalReset = form.querySelector("[data-cover-hero-focal-reset]");
    const coverHeroCropClose = form.querySelector("[data-cover-hero-crop-close]");

    const reviewPanel = form.querySelector("[data-brochure-review-panel]");
    const reviewNotice = form.querySelector("[data-review-notice]");
    const reviewNoticeText = form.querySelector("[data-review-notice-text]");
    const reviewEmpty = form.querySelector("[data-review-empty]");
    const reviewWorkspace = form.querySelector("[data-review-workspace]");
    const reviewNav = form.querySelector("[data-review-nav]");
    const reviewReviewedCount = form.querySelector("[data-review-reviewed-count]");
    const reviewTotalCount = form.querySelector("[data-review-total-count]");
    const reviewNextUnreviewed = form.querySelector("[data-review-next-unreviewed]");
    const reviewPosition = form.querySelector("[data-review-position]");
    const reviewProjectName = form.querySelector("[data-review-project-name]");
    const reviewProjectMeta = form.querySelector("[data-review-project-meta]");
    const reviewState = form.querySelector("[data-review-state]");
    const reviewImageFrame = form.querySelector("[data-review-image-frame]");
    const reviewImageMeta = form.querySelector("[data-review-image-meta]");
    const reviewImageModeSelect = form.querySelector("[data-review-image-mode]");
    const reviewImageModeHelp = form.querySelector("[data-review-image-mode-help]");
    const reviewChangeImage = form.querySelector("[data-review-change-image]");
    const reviewAdjustCrop = form.querySelector("[data-review-adjust-crop]");
    const reviewNarrativeLabel = form.querySelector("[data-review-narrative-label]");
    const reviewWordCount = form.querySelector("[data-review-word-count]");
    const reviewNarrative = form.querySelector("[data-review-narrative]");
    const reviewOpenBrief = form.querySelector("[data-review-open-brief]");
    const reviewManagePhotos = form.querySelector("[data-review-manage-photos]");
    const reviewPrevious = form.querySelector("[data-review-previous]");
    const reviewNext = form.querySelector("[data-review-next]");
    const reviewMarkReviewed = form.querySelector("[data-review-mark-reviewed]");

    const normalize = value => (value ?? "").trim().toLowerCase();
    const clamp = value => Math.max(0, Math.min(1, Number.isFinite(Number(value)) ? Number(value) : 0.5));
    const projectPhotos = id => projectById.get(id)?.photos ?? [];
    const getPhoto = (id, photoId) => projectPhotos(id).find(photo => Number(photo.photoId) === Number(photoId)) ?? null;

    const countWords = value => String(value ?? "")
        .trim()
        .split(/\s+/u)
        .filter(Boolean)
        .length;

    const approvedPrintFieldMap = {
        "Input.PrintCentreStatement": "centreStatement",
        "Input.PrintIntroText": "openingNarrative",
        "Input.PrintFutureText": "futureNarrative",
        "Input.PrintProcurementText": "procurementGuidance",
        "Input.PrintDevelopingAgencyText": "developingAgency",
        "Input.PrintManufacturingAgencyText": "manufacturingAgency",
        "Input.PrintVisionaryText": "visionaryHorizons",
        "Input.PrintNewSimulatorsText": "newSimulatorsGuidance"
    };

    const updatePrintMatterWordCounts = () => {
        printMatterFields.forEach(field => {
            const label = field.closest(".brochure-field")?.querySelector(".brochure-print-word-status");
            const countNode = label?.querySelector("[data-print-word-count]");
            const limit = Number(field.dataset.printWordLimit || 0);
            const words = countWords(field.value);
            if (countNode) countNode.textContent = String(words);
            label?.classList.toggle("is-near-limit", limit > 0 && words >= Math.floor(limit * 0.85) && words <= limit);
            label?.classList.toggle("is-over-limit", limit > 0 && words > limit);
            field.classList.toggle("is-print-over-limit", limit > 0 && words > limit);
        });
    };

    const defaultConfig = project => ({
        projectId: Number(project.projectId),
        primaryPhotoId: project.defaultPrimaryPhotoId ?? null,
        secondaryPhotoId: project.defaultSecondaryPhotoId ?? null,
        primaryFocalX: 0.5,
        primaryFocalY: 0.5,
        secondaryFocalX: 0.5,
        secondaryFocalY: 0.5,
        imageMode: modeAutomatic,
        primaryPhotoConfirmed: false,
        isReviewed: false,
        reviewFingerprint: ""
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
                : modeAutomatic,
            primaryPhotoConfirmed: Boolean(selection.primaryPhotoConfirmed),
            isReviewed: Boolean(selection.isReviewed),
            reviewFingerprint: String(selection.reviewFingerprint || "")
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
    let photoEditorFocusMode = "select";
    let photoEditorReturnFocus = null;
    let activeReviewProjectId = orderedIds[0] ?? null;
    let draggedId = null;
    let preflightTimer = null;
    let preflightAbort = null;
    let smartFlowUndoOrder = null;
    let currentSmartFlowSuggestion = null;
    let projectStateAbort = null;
    let projectStateTimer = null;
    let lastProjectStateRefresh = 0;
    let lastPreflight = null;
    let currentProjectReviewFingerprints = new Map();
    let showAllFindings = false;
    let exportBusy = false;
    let reviewNoticeTimer = null;
    let explicitCoverHeroProjectId = Number(coverHeroInput?.value) > 0 ? Number(coverHeroInput.value) : null;
    let explicitCoverHeroPhotoId = Number(coverHeroPhotoInput?.value) > 0 ? Number(coverHeroPhotoInput.value) : null;
    let coverHeroFocalX = clamp(coverHeroFocalXInput?.value);
    let coverHeroFocalY = clamp(coverHeroFocalYInput?.value);
    let coverReviewed = String(coverReviewedInput?.value).toLowerCase() === "true";
    let coverReviewFingerprint = String(coverReviewFingerprintInput?.value || "");
    let coverSelectionTouched = false;

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

    const isContemporaryCover = () => {
        const checked = form.querySelector('[name="Input.CoverStyle"]:checked');
        return checked?.value === "2" || checked?.value === "Contemporary";
    };

    const isPrintCompactProfile = () => {
        const checked = form.querySelector('[name="Input.PublicationProfile"]:checked');
        return checked?.value === "1" || checked?.value === "PrintCompact";
    };

    const updatePublicationProfileUi = () => {
        const printCompact = isPrintCompactProfile();
        profileOptions.forEach(option => {
            const input = option.querySelector("[data-brochure-profile]");
            option.classList.toggle("is-selected", Boolean(input?.checked));
        });
        printOnlySections.forEach(section => {
            section.hidden = !printCompact;
        });
        digitalOnlySections.forEach(section => {
            section.hidden = printCompact;
        });
        if (printPlanSummary && !printCompact) {
            printPlanSummary.hidden = true;
        }
    };

    const createImage = (src, alt = "") => {
        const image = document.createElement("img");
        image.alt = alt;
        image.loading = "lazy";
        image.src = src;
        image.addEventListener("error", () => {
            image.classList.add("is-broken");
            image.removeAttribute("src");
            image.closest(".brochure-photo-thumb, .brochure-selected-item__thumb, .brochure-photo-choice, .brochure-cover-hero-current__image, .brochure-review-image__frame")?.classList.add("is-image-missing");
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
            inputs.push(makeHidden(`${prefix}.PrimaryPhotoConfirmed`, config.primaryPhotoConfirmed ? "true" : "false"));
            const hasBoundReview = Boolean(config.isReviewed && config.reviewFingerprint);
            inputs.push(makeHidden(`${prefix}.IsReviewed`, hasBoundReview ? "true" : "false"));
            if (config.reviewFingerprint) inputs.push(makeHidden(`${prefix}.ReviewFingerprint`, config.reviewFingerprint));
        });
        hiddenInputs.replaceChildren(...inputs);
        if (coverHeroInput) coverHeroInput.value = explicitCoverHeroProjectId == null ? "" : String(explicitCoverHeroProjectId);
        if (coverHeroPhotoInput) coverHeroPhotoInput.value = explicitCoverHeroPhotoId == null ? "" : String(explicitCoverHeroPhotoId);
        if (coverHeroFocalXInput) coverHeroFocalXInput.value = clamp(coverHeroFocalX).toFixed(4);
        if (coverHeroFocalYInput) coverHeroFocalYInput.value = clamp(coverHeroFocalY).toFixed(4);
        if (coverReviewedInput) coverReviewedInput.value = coverReviewed ? "true" : "false";
        if (coverReviewFingerprintInput) coverReviewFingerprintInput.value = coverReviewFingerprint;
    };

    const scheduleReviewNoticeDismiss = () => {
        if (!reviewNotice || reviewNotice.hidden) return;
        if (reviewNoticeTimer) window.clearTimeout(reviewNoticeTimer);
        reviewNoticeTimer = window.setTimeout(() => {
            reviewNotice.hidden = true;
            reviewNotice.classList.remove("is-warning", "is-info");
            reviewNoticeTimer = null;
        }, 6500);
    };

    const showReviewNotice = (message, tone = "warning") => {
        if (!reviewNotice || !reviewNoticeText) return;
        if (reviewNoticeTimer) {
            window.clearTimeout(reviewNoticeTimer);
            reviewNoticeTimer = null;
        }
        reviewNoticeText.textContent = message;
        reviewNotice.hidden = false;
        reviewNotice.classList.toggle("is-warning", tone === "warning");
        reviewNotice.classList.toggle("is-info", tone === "info");
        if (!photoEditor || photoEditor.hidden) scheduleReviewNoticeDismiss();
    };

    const invalidateReview = (id, { unconfirmPhoto = false, announce = false, reason = "Publication inputs changed" } = {}) => {
        const config = ensureConfig(id);
        if (!config) return false;
        const wasApproved = Boolean(config.isReviewed || config.reviewFingerprint);
        config.isReviewed = false;
        config.reviewFingerprint = "";
        if (unconfirmPhoto) config.primaryPhotoConfirmed = false;
        if (announce && wasApproved) {
            showReviewNotice(`${reason} · publication approval reset.`);
        }
        return wasApproved;
    };

    const invalidateAllReviews = () => {
        orderedIds.forEach(id => invalidateReview(id));
    };

    const invalidateCoverReview = () => {
        coverReviewed = false;
        coverReviewFingerprint = "";
        syncHiddenInputs();
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

    const updateRowPhotoSummary = id => {
        const row = rowById.get(id);
        const project = projectById.get(id);
        if (!row || !project) return;
        row.dataset.hasPhoto = project.photos?.length ? "true" : "false";
        const summary = row.querySelector(".brochure-photo-summary");
        const strong = summary?.querySelector("strong");
        const small = summary?.querySelector("small");
        if (strong) strong.textContent = `${project.photos?.length ?? 0} photo${project.photos?.length === 1 ? "" : "s"}`;
        if (small) small.textContent = project.photos?.length ? "quality checked when selected" : "no photo recorded";

        const currentThumb = summary?.querySelector(".brochure-photo-thumb, .brochure-photo-placeholder");
        const defaultPhoto = getPhoto(id, project.defaultPrimaryPhotoId);
        if (!currentThumb) return;
        if (!defaultPhoto?.thumbnailUrl) {
            currentThumb.className = "brochure-photo-placeholder";
            currentThumb.replaceChildren();
            const icon = document.createElement("i");
            icon.className = "bi bi-image";
            icon.setAttribute("aria-hidden", "true");
            currentThumb.append(icon);
            return;
        }

        currentThumb.className = "brochure-photo-thumb";
        const image = createImage(defaultPhoto.thumbnailUrl);
        const icon = document.createElement("i");
        icon.className = "bi bi-image";
        icon.setAttribute("aria-hidden", "true");
        currentThumb.replaceChildren(image, icon);
    };

    const modeLabel = mode => mode === modeSingle ? "Single" : mode === modeGalleryTwo ? "Gallery 2" : "Automatic";

    const selectedItem = (id, index) => {
        const project = projectById.get(id);
        const config = ensureConfig(id);
        const primary = getPhoto(id, config?.primaryPhotoId);
        const item = document.createElement("li");
        item.className = "brochure-selected-item";
        if (activePhotoProjectId === id) item.classList.add("is-photo-editing");
        if (config?.isReviewed && config.reviewFingerprint) item.classList.add("is-reviewed");
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
        name.title = name.textContent;
        const meta = document.createElement("span");
        meta.className = "brochure-selected-item__meta";
        const state = config?.isReviewed && config.reviewFingerprint
            ? "Approved"
            : config?.primaryPhotoConfirmed
                ? "Image confirmed"
                : "Approval required";
        meta.textContent = `${state} · ${modeLabel(config?.imageMode)}`;
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
            smartFlowUndoOrder = null;
            orderedIds = next;
            renderSelected(true);
        });
        return item;
    };

    const move = (id, delta) => {
        const index = orderedIds.indexOf(id);
        const target = index + delta;
        if (index < 0 || target < 0 || target >= orderedIds.length) return;
        const next = [...orderedIds];
        [next[index], next[target]] = [next[target], next[index]];
        smartFlowUndoOrder = null;
        orderedIds = next;
        renderSelected(true);
    };

    const add = id => {
        if (!projectById.has(id) || orderedIds.includes(id) || orderedIds.length >= MAX_PROJECTS) return;
        ensureConfig(id);
        smartFlowUndoOrder = null;
        orderedIds.push(id);
        if (activeReviewProjectId == null) activeReviewProjectId = id;
        renderSelected(true);
    };

    const remove = id => {
        smartFlowUndoOrder = null;
        orderedIds = orderedIds.filter(value => value !== id);
        if (activePhotoProjectId === id) closePhotoEditor();
        if (activeReviewProjectId === id) activeReviewProjectId = orderedIds[0] ?? null;
        if (explicitCoverHeroProjectId === id) {
            explicitCoverHeroProjectId = null;
            explicitCoverHeroPhotoId = null;
            coverHeroFocalX = 0.5;
            coverHeroFocalY = 0.5;
            coverReviewed = false;
            coverReviewFingerprint = "";
        }
        renderSelected(true);
    };

    const isProjectReviewed = id => {
        const config = ensureConfig(id);
        return Boolean(config?.isReviewed && config.reviewFingerprint);
    };
    const reviewedCount = () => orderedIds.filter(isProjectReviewed).length;
    const allReviewed = () => orderedIds.length > 0 && reviewedCount() === orderedIds.length;

    const renderSelected = (runPreflight = true, refreshState = true) => {
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
        renderReview();
        renderCoverHero();
        if (refreshState) scheduleProjectStateRefresh();
        if (runPreflight) schedulePreflight();
        else updateButtons(Boolean(lastPreflight?.canGenerate));
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

    const cropForFocal = (sourceWidth, sourceHeight, focalX, focalY, targetAspect = 16 / 9) => {
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

    const positionFocalOverlay = (stage, image, marker, cropFrame, x, y, targetAspect = 16 / 9) => {
        const metrics = sourceMetrics(stage, image);
        if (!metrics) return;
        const crop = cropForFocal(metrics.sourceWidth, metrics.sourceHeight, x, y, targetAspect);
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
        image.onerror = () => { stage.hidden = true; };
        if (image.src !== absolute) {
            image.onload = applyOverlay;
            image.src = photo.previewUrl;
        } else if (image.complete && image.naturalWidth > 0) {
            applyOverlay();
        } else {
            image.onload = applyOverlay;
        }
    };

    const updateCoverFocalStage = () => {
        if (!coverHeroCropPanel || !coverHeroFocalStage || !coverHeroFocalImage || !coverHeroFocalMarker) return;
        const hero = resolvedCoverHero();
        const photo = hero ? getPhoto(hero.projectId, hero.photoId) : null;
        coverHeroCropPanel.hidden = coverHeroCropPanel.hidden || !photo;
        if (!photo?.previewUrl || coverHeroCropPanel.hidden) return;

        const applyOverlay = () => positionFocalOverlay(
            coverHeroFocalStage,
            coverHeroFocalImage,
            coverHeroFocalMarker,
            coverHeroCropFrame,
            coverHeroFocalX,
            coverHeroFocalY,
            1800 / (isPrintCompactProfile() ? 1055 : 1100));

        const absolute = new URL(photo.previewUrl, window.location.href).href;
        coverHeroFocalImage.onerror = () => { coverHeroCropPanel.hidden = true; };
        if (coverHeroFocalImage.src !== absolute) {
            coverHeroFocalImage.onload = applyOverlay;
            coverHeroFocalImage.src = photo.previewUrl;
        } else if (coverHeroFocalImage.complete && coverHeroFocalImage.naturalWidth > 0) {
            applyOverlay();
        } else {
            coverHeroFocalImage.onload = applyOverlay;
        }
    };

    const ensureExplicitCoverHero = () => {
        const hero = resolvedCoverHero();
        if (!hero) return null;
        if (!hero.explicit) {
            explicitCoverHeroProjectId = hero.projectId;
            explicitCoverHeroPhotoId = hero.photoId;
            coverHeroFocalX = 0.5;
            coverHeroFocalY = 0.5;
            coverReviewed = false;
            coverReviewFingerprint = "";
        }
        syncHiddenInputs();
        return {
            projectId: explicitCoverHeroProjectId,
            photoId: explicitCoverHeroPhotoId
        };
    };

    const setCoverFocalFromEvent = event => {
        if (event.target.closest("button")) return;
        const hero = ensureExplicitCoverHero();
        if (!hero || !coverHeroFocalStage || !coverHeroFocalImage) return;

        const rect = coverHeroFocalStage.getBoundingClientRect();
        const metrics = sourceMetrics(coverHeroFocalStage, coverHeroFocalImage);
        if (!metrics) return;

        const localX = event.clientX - rect.left - metrics.offsetX;
        const localY = event.clientY - rect.top - metrics.offsetY;
        if (localX < 0 || localY < 0 || localX > metrics.renderedWidth || localY > metrics.renderedHeight) return;

        coverHeroFocalX = clamp(localX / metrics.renderedWidth);
        coverHeroFocalY = clamp(localY / metrics.renderedHeight);
        coverReviewed = false;
        coverReviewFingerprint = "";
        syncHiddenInputs();
        updateCoverFocalStage();
        renderCoverHero();
        schedulePreflight();
    };

    const resetCoverFocal = () => {
        const hero = ensureExplicitCoverHero();
        if (!hero) return;
        coverHeroFocalX = 0.5;
        coverHeroFocalY = 0.5;
        coverReviewed = false;
        coverReviewFingerprint = "";
        syncHiddenInputs();
        updateCoverFocalStage();
        renderCoverHero();
        schedulePreflight();
    };

    const renderPhotoEditor = () => {
        if (activePhotoProjectId == null || !photoEditor) return;
        const project = projectById.get(activePhotoProjectId);
        const config = ensureConfig(activePhotoProjectId);
        if (!project || !config) return;

        photoEditor.hidden = false;
        if (photoEditorTitle) photoEditorTitle.textContent = "Photograph setup";
        if (photoEditorProjectName) photoEditorProjectName.textContent = project.projectName;
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
                        config.primaryPhotoConfirmed = true;
                        invalidateReview(project.projectId, { announce: true, reason: "Primary image changed" });
                        renderPhotoEditor();
                        renderSelected(false);
                        schedulePreflight();
                    })));
            }
        }

        if (secondaryPicker) {
            const choices = [photoChoice(project.projectId, null, config.secondaryPhotoId == null, () => {
                config.secondaryPhotoId = null;
                invalidateReview(project.projectId, { announce: true, reason: "Second image removed" });
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
                        invalidateReview(project.projectId, { announce: true, reason: "Second image changed" });
                        renderPhotoEditor();
                        renderSelected(false);
                        schedulePreflight();
                    })));
            secondaryPicker.replaceChildren(...choices);
        }

        updateFocalStage("primary", activePhotoProjectId);
        updateFocalStage("secondary", activePhotoProjectId);
    };

    const focusPhotoEditor = () => {
        if (!photoEditor || photoEditor.hidden) return;
        window.requestAnimationFrame(() => {
            const body = photoEditor.querySelector(".publication-panel__body");
            if (!(body instanceof HTMLElement)) return;

            if (photoEditorFocusMode === "crop" && primaryStage && !primaryStage.hidden) {
                body.scrollTo({ top: Math.max(0, primaryStage.offsetTop - 18), behavior: "smooth" });
                primaryStage.setAttribute("tabindex", "-1");
                primaryStage.focus({ preventScroll: true });
            } else if (photoEditorFocusMode === "secondary" && secondarySection && !secondarySection.hidden) {
                body.scrollTo({ top: Math.max(0, secondarySection.offsetTop - 18), behavior: "smooth" });
                const firstSecondaryChoice = secondaryPicker?.querySelector("button:not(.is-none)") ?? secondaryPicker?.querySelector("button");
                if (firstSecondaryChoice instanceof HTMLElement) {
                    firstSecondaryChoice.focus({ preventScroll: true });
                }
            } else {
                body.scrollTo({ top: 0, behavior: "smooth" });
                const firstChoice = primaryPicker?.querySelector("button");
                if (firstChoice instanceof HTMLElement) {
                    firstChoice.focus({ preventScroll: true });
                }
            }
        });
    };

    const openPhotoEditor = (id, focusMode = "select") => {
        activePhotoProjectId = id;
        photoEditorFocusMode = focusMode === "crop" || focusMode === "secondary" ? focusMode : "select";
        photoEditorReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        document.body.classList.add("brochure-modal-open");
        renderSelected(false);
        renderPhotoEditor();
        focusPhotoEditor();
    };

    const closePhotoEditor = () => {
        activePhotoProjectId = null;
        photoEditorFocusMode = "select";
        if (photoEditor) photoEditor.hidden = true;
        document.body.classList.remove("brochure-modal-open");
        renderSelected(false);
        scheduleReviewNoticeDismiss();
        if (photoEditorReturnFocus?.isConnected) {
            window.requestAnimationFrame(() => photoEditorReturnFocus?.focus({ preventScroll: true }));
        }
        photoEditorReturnFocus = null;
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
        invalidateReview(activePhotoProjectId, { announce: true, reason: "Image crop changed" });
        updateFocalStage(kind, activePhotoProjectId);
        syncHiddenInputs();
        renderReview();
        renderCoverHero();
        schedulePreflight();
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
        invalidateReview(activePhotoProjectId, { announce: true, reason: "Image crop reset" });
        updateFocalStage(kind, activePhotoProjectId);
        syncHiddenInputs();
        renderReview();
        renderCoverHero();
        schedulePreflight();
    };

    const projectSignature = project => JSON.stringify({
        projectName: project.projectName ?? "",
        lifecycle: project.lifecycle ?? "",
        projectCategory: project.projectCategory ?? "",
        technicalCategory: project.technicalCategory ?? "",
        narrative: project.reviewNarrative ?? "",
        hasNarrative: project.reviewHasNarrative ?? false,
        narrativeWordCount: project.reviewNarrativeWordCount ?? 0,
        photos: (project.photos ?? []).map(photo => [Number(photo.photoId), Number(photo.version)])
    });

    const refreshProjectState = async () => {
        if (!projectStateUrl || !orderedIds.length) return;
        projectStateAbort?.abort();
        projectStateAbort = new AbortController();
        syncHiddenInputs();
        try {
            const response = await fetch(projectStateUrl, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" },
                signal: projectStateAbort.signal
            });
            if (!response.ok) throw new Error(`Project state refresh failed with HTTP ${response.status}`);
            const payload = await response.json();
            const photoProbes = payload.photoProbes ?? {};
            (payload.projects ?? []).forEach(updated => {
                const id = Number(updated.projectId);
                const project = projectById.get(id);
                if (!project) return;
                const oldSignature = project.__publicationSignature;
                Object.assign(project, {
                    projectName: updated.projectName,
                    lifecycle: updated.lifecycle,
                    projectCategory: updated.projectCategory,
                    technicalCategory: updated.technicalCategory,
                    reviewNarrative: updated.narrative,
                    reviewHasNarrative: Boolean(updated.hasNarrative),
                    reviewNarrativeWordCount: Number(updated.narrativeWordCount || 0),
                    hasProjectBrief: Boolean(updated.hasProjectBrief),
                    hasCapabilityOverview: Boolean(updated.hasCapabilityOverview),
                    hasFullDescription: Boolean(updated.hasFullDescription),
                    projectBriefWordCount: Number(updated.projectBriefWordCount || 0),
                    capabilityOverviewWordCount: Number(updated.capabilityOverviewWordCount || 0),
                    fullDescriptionWordCount: Number(updated.fullDescriptionWordCount || 0),
                    defaultPrimaryPhotoId: updated.defaultPrimaryPhotoId ?? null,
                    photos: updated.photos ?? [],
                    overviewUrl: updated.overviewUrl,
                    photosUrl: updated.photosUrl
                });
                project.photos = (project.photos ?? []).map(photo => {
                    const probe = photoProbes[String(photo.photoId)] ?? null;
                    return probe
                        ? {
                            ...photo,
                            publicationReady: Boolean(probe.isReady),
                            publicationWidth: Number(probe.width || 0),
                            publicationHeight: Number(probe.height || 0),
                            publicationQuality: probe.quality || null,
                            publicationSource: probe.sourceVariant || null
                        }
                        : photo;
                });
                const newSignature = projectSignature(project);
                if (oldSignature && oldSignature !== newSignature) {
                    invalidateReview(id, { announce: id === activeReviewProjectId, reason: "Authoritative project content changed" });
                    if (explicitCoverHeroProjectId === id) {
                        coverReviewed = false;
                        coverReviewFingerprint = "";
                    }
                }
                project.__publicationSignature = newSignature;

                const config = ensureConfig(id);
                if (config?.primaryPhotoId != null && !getPhoto(id, config.primaryPhotoId)) {
                    config.primaryPhotoId = project.defaultPrimaryPhotoId ?? null;
                    config.primaryFocalX = 0.5;
                    config.primaryFocalY = 0.5;
                    config.primaryPhotoConfirmed = false;
                    config.isReviewed = false;
                    config.reviewFingerprint = "";
                }
                if (config?.secondaryPhotoId != null && !getPhoto(id, config.secondaryPhotoId)) {
                    config.secondaryPhotoId = null;
                    config.secondaryFocalX = 0.5;
                    config.secondaryFocalY = 0.5;
                    config.isReviewed = false;
                    config.reviewFingerprint = "";
                }
                if (explicitCoverHeroProjectId === id
                    && explicitCoverHeroPhotoId != null
                    && !getPhoto(id, explicitCoverHeroPhotoId)) {
                    explicitCoverHeroProjectId = null;
                    explicitCoverHeroPhotoId = null;
                    coverHeroFocalX = 0.5;
                    coverHeroFocalY = 0.5;
                    coverReviewed = false;
                    coverReviewFingerprint = "";
                }
                updateRowPhotoSummary(id);
            });
            lastProjectStateRefresh = Date.now();
            updateNarrativeIndicators();
            applyFilters();
            syncHiddenInputs();
            renderSelected(false, false);
            renderReview();
            renderCoverHero();
            schedulePreflight();
        } catch (error) {
            if (error?.name !== "AbortError") console.error(error);
        }
    };

    const scheduleProjectStateRefresh = () => {
        window.clearTimeout(projectStateTimer);
        if (!orderedIds.length) return;
        projectStateTimer = window.setTimeout(refreshProjectState, 320);
    };

    const resolvedCoverHero = () => {
        if (explicitCoverHeroProjectId
            && explicitCoverHeroPhotoId
            && orderedIds.includes(explicitCoverHeroProjectId)
            && getPhoto(explicitCoverHeroProjectId, explicitCoverHeroPhotoId)) {
            return {
                projectId: explicitCoverHeroProjectId,
                photoId: explicitCoverHeroPhotoId,
                explicit: true
            };
        }

        const projectId = Number(lastPreflight?.resolvedCoverHeroProjectId);
        const photoId = Number(lastPreflight?.resolvedCoverHeroPhotoId);
        if (projectId > 0 && photoId > 0 && orderedIds.includes(projectId) && getPhoto(projectId, photoId)) {
            return { projectId, photoId, explicit: false };
        }

        return null;
    };

    const resolvedCoverHeroId = () => resolvedCoverHero()?.projectId ?? null;

    const renderCoverHero = () => {
        const contemporary = isContemporaryCover();
        if (institutionalArtworkPanel) institutionalArtworkPanel.hidden = contemporary;
        if (!coverHeroPanel) return;
        coverHeroPanel.hidden = !contemporary;
        if (coverHeroPanel.hidden) {
            if (coverHeroCropPanel) coverHeroCropPanel.hidden = true;
            return;
        }

        if (explicitCoverHeroProjectId && !orderedIds.includes(explicitCoverHeroProjectId)) {
            explicitCoverHeroProjectId = null;
            explicitCoverHeroPhotoId = null;
            coverHeroFocalX = 0.5;
            coverHeroFocalY = 0.5;
            coverReviewed = false;
            coverReviewFingerprint = "";
        }

        const hero = resolvedCoverHero();
        const project = hero ? projectById.get(hero.projectId) : null;
        const photo = hero ? getPhoto(hero.projectId, hero.photoId) : null;
        syncHiddenInputs();

        if (coverHeroName) coverHeroName.textContent = project?.projectName ?? "Waiting for a usable project image";
        if (coverHeroMeta) {
            if (!project || !photo) {
                coverHeroMeta.textContent = "Select projects to resolve the Cover B hero.";
            } else {
                const mode = hero.explicit ? "Selected hero" : "Automatic suggestion";
                const width = Number(lastPreflight?.resolvedCoverHeroWidth || photo.publicationWidth || photo.width || 0);
                const height = Number(lastPreflight?.resolvedCoverHeroHeight || photo.publicationHeight || photo.height || 0);
                const quality = String(lastPreflight?.resolvedCoverHeroQuality || photo.publicationQuality || "")
                    .replace(/([a-z])([A-Z])/g, "$1 $2");
                const details = [width > 0 && height > 0 ? `${width}×${height}` : null, quality || null]
                    .filter(Boolean)
                    .join(" · ");
                coverHeroMeta.textContent = `${mode}${details ? ` · ${details}` : ""}${hero.explicit ? " · independent cover artwork" : ""}`;
            }
        }

        if (coverHeroCrop) coverHeroCrop.disabled = !hero;
        if (coverHeroApprove) {
            const fingerprintReady = Boolean(lastPreflight?.coverReviewFingerprint);
            coverHeroApprove.disabled = !hero || !fingerprintReady || coverReviewed;
            coverHeroApprove.innerHTML = coverReviewed
                ? '<i class="bi bi-check-circle-fill" aria-hidden="true"></i> Cover approved'
                : '<i class="bi bi-check2-circle" aria-hidden="true"></i> Approve cover';
        }
        if (coverHeroAutomatic) coverHeroAutomatic.disabled = !hero?.explicit;
        if (coverHeroReviewState) {
            const coverIsReviewed = Boolean(coverReviewed && coverReviewFingerprint);
            coverHeroReviewState.classList.toggle("is-reviewed", coverIsReviewed);
            coverHeroReviewState.textContent = coverIsReviewed ? "Cover approved" : "Cover review required";
        }

        if (coverHeroImage) {
            coverHeroImage.classList.remove("is-image-missing");
            coverHeroImage.replaceChildren();
            if (photo?.thumbnailUrl) {
                coverHeroImage.append(createImage(photo.thumbnailUrl, project?.projectName ?? "Cover hero"));
            } else {
                const icon = document.createElement("i");
                icon.className = "bi bi-image";
                icon.setAttribute("aria-hidden", "true");
                coverHeroImage.append(icon);
            }
        }

        updateCoverFocalStage();
        updateButtons(Boolean(lastPreflight?.canGenerate));
    };

    const renderCoverHeroChoices = () => {
        if (!coverHeroChoices) return;
        const choices = orderedIds
            .flatMap(id => {
                const project = projectById.get(id);
                if (!project) return [];

                return (project.photos ?? []).map(photo => {
                    const button = document.createElement("button");
                    button.type = "button";
                    button.className = "brochure-cover-hero-choice";
                    if (explicitCoverHeroProjectId === id && explicitCoverHeroPhotoId === Number(photo.photoId)) {
                        button.classList.add("is-selected");
                    }

                    const thumb = document.createElement("span");
                    thumb.append(createImage(photo.thumbnailUrl, project.projectName));

                    const copy = document.createElement("span");
                    const name = document.createElement("strong");
                    name.textContent = project.projectName;

                    const meta = document.createElement("small");
                    const dimensions = photo.publicationWidth > 0 && photo.publicationHeight > 0
                        ? `${photo.publicationWidth}×${photo.publicationHeight}`
                        : photo.width > 0 && photo.height > 0
                            ? `${photo.width}×${photo.height}`
                            : null;
                    const quality = String(photo.publicationQuality || "")
                        .replace(/([a-z])([A-Z])/g, "$1 $2");
                    meta.textContent = [
                        photo.isCover ? "Project cover" : `Photo ${photo.photoId}`,
                        dimensions,
                        quality || null
                    ].filter(Boolean).join(" · ");

                    copy.append(name, meta);
                    button.append(thumb, copy);
                    button.addEventListener("click", () => {
                        explicitCoverHeroProjectId = id;
                        explicitCoverHeroPhotoId = Number(photo.photoId);
                        coverHeroFocalX = 0.5;
                        coverHeroFocalY = 0.5;
                        coverReviewed = false;
                        coverReviewFingerprint = "";
                        coverHeroChoices.hidden = true;
                        if (coverHeroCropPanel) coverHeroCropPanel.hidden = true;
                        syncHiddenInputs();
                        renderCoverHero();
                        schedulePreflight();
                    });

                    return button;
                });
            });

        if (!choices.length) {
            const empty = document.createElement("div");
            empty.className = "brochure-cover-hero-choice-empty";
            empty.textContent = "No selected project currently has a publication photograph.";
            coverHeroChoices.replaceChildren(empty);
        } else {
            coverHeroChoices.replaceChildren(...choices);
        }
    };

    const setActiveReview = id => {
        if (!orderedIds.includes(id)) return;
        activeReviewProjectId = id;
        renderReview();
        reviewPanel?.scrollIntoView({ block: "nearest", behavior: "smooth" });
    };

    const renderReview = () => {
        const total = orderedIds.length;
        const reviewed = reviewedCount();
        if (reviewTotalCount) reviewTotalCount.textContent = String(total);
        if (reviewReviewedCount) reviewReviewedCount.textContent = String(reviewed);
        if (reviewEmpty) reviewEmpty.hidden = total !== 0;
        if (reviewWorkspace) reviewWorkspace.hidden = total === 0;
        if (reviewNextUnreviewed) reviewNextUnreviewed.disabled = total === 0 || reviewed === total;
        if (total === 0) {
            activeReviewProjectId = null;
            updateButtons(Boolean(lastPreflight?.canGenerate));
            return;
        }

        if (!orderedIds.includes(activeReviewProjectId)) {
            activeReviewProjectId = orderedIds.find(id => !isProjectReviewed(id)) ?? orderedIds[0];
        }
        const id = activeReviewProjectId;
        const index = orderedIds.indexOf(id);
        const project = projectById.get(id);
        const config = ensureConfig(id);
        const primary = getPhoto(id, config?.primaryPhotoId);
        if (!project || !config) return;

        if (reviewNav) {
            const nodes = orderedIds.map((projectId, projectIndex) => {
                const nav = document.createElement("button");
                nav.type = "button";
                nav.className = "brochure-review-nav__item";
                nav.classList.toggle("is-active", projectId === id);
                nav.classList.toggle("is-reviewed", isProjectReviewed(projectId));
                nav.title = projectById.get(projectId)?.projectName ?? `Project ${projectId}`;
                nav.innerHTML = `<span>${projectIndex + 1}</span><i class="bi ${isProjectReviewed(projectId) ? "bi-check-circle-fill" : "bi-circle"}" aria-hidden="true"></i>`;
                nav.addEventListener("click", () => setActiveReview(projectId));
                return nav;
            });
            reviewNav.replaceChildren(...nodes);
        }

        if (reviewPosition) reviewPosition.textContent = `Project ${index + 1} of ${total}`;
        if (reviewProjectName) reviewProjectName.textContent = project.projectName;
        if (reviewProjectMeta) reviewProjectMeta.textContent = [project.lifecycle, project.technicalCategory].filter(Boolean).join(" · ");
        if (reviewState) {
            const isReviewed = isProjectReviewed(id);
            reviewState.classList.toggle("is-reviewed", isReviewed);
            reviewState.textContent = isReviewed ? "Approved for publication" : "Approval required";
        }

        if (reviewImageFrame) {
            reviewImageFrame.classList.remove("is-image-missing");
            reviewImageFrame.replaceChildren();
            if (primary?.previewUrl) {
                const image = createImage(primary.previewUrl, project.projectName);
                image.style.objectPosition = `${clamp(config.primaryFocalX) * 100}% ${clamp(config.primaryFocalY) * 100}%`;
                reviewImageFrame.append(image);
            } else {
                const icon = document.createElement("i");
                icon.className = "bi bi-image";
                icon.setAttribute("aria-hidden", "true");
                reviewImageFrame.append(icon);
            }
        }
        if (reviewImageMeta) {
            if (!primary) {
                reviewImageMeta.textContent = "No primary publication photograph selected";
            } else {
                const dimensions = primary.publicationWidth > 0 && primary.publicationHeight > 0
                    ? `${primary.publicationWidth}×${primary.publicationHeight}`
                    : null;
                const quality = String(primary.publicationQuality || "").replace(/([a-z])([A-Z])/g, "$1 $2");
                reviewImageMeta.textContent = [
                    config.primaryPhotoConfirmed ? "Confirmed publication image" : "Automatic publication image",
                    primary.isCover ? "project cover" : null,
                    dimensions,
                    quality || null
                ].filter(Boolean).join(" · ");
            }
        }
        if (reviewImageModeSelect) {
            reviewImageModeSelect.value = String(config.imageMode);
            reviewImageModeSelect.disabled = (project.photos?.length ?? 0) === 0;
        }
        if (reviewImageModeHelp) {
            if (config.imageMode === modeGalleryTwo) {
                reviewImageModeHelp.textContent = config.secondaryPhotoId
                    ? "Gallery 2 confirmed for this project. Both selected images will be considered by the print compositor."
                    : "Gallery 2 requires a second photograph. Select one to complete project review.";
            } else if (config.imageMode === modeSingle) {
                reviewImageModeHelp.textContent = "Single-image treatment locks this project to one photograph.";
            } else {
                reviewImageModeHelp.textContent = config.secondaryPhotoId
                    ? "Automatic lets the print planner use one or two selected images according to page geometry."
                    : "Automatic uses one image. Select a second image if you want the print planner to consider Gallery 2 when space permits.";
            }
        }
        if (reviewChangeImage) reviewChangeImage.disabled = project.photos?.length === 0;
        if (reviewAdjustCrop) reviewAdjustCrop.disabled = !primary;

        const info = narrativeInfo(project);
        if (reviewNarrativeLabel) reviewNarrativeLabel.textContent = info.label;
        if (reviewWordCount) reviewWordCount.textContent = `${project.reviewNarrativeWordCount ?? info.words} words`;
        if (reviewNarrative) {
            reviewNarrative.textContent = project.reviewNarrative ?? "Loading current publication copy…";
            reviewNarrative.classList.toggle("is-missing", project.reviewHasNarrative === false || !info.ready);
        }
        if (reviewOpenBrief) {
            reviewOpenBrief.href = project.overviewUrl ?? "#";
            reviewOpenBrief.textContent = `Open ${info.label}`;
        }
        if (reviewManagePhotos) reviewManagePhotos.href = project.photosUrl ?? "#";

        if (reviewPrevious) reviewPrevious.disabled = index <= 0;
        if (reviewNext) reviewNext.disabled = index >= total - 1;
        if (reviewMarkReviewed) {
            const imageTreatmentReady = config.imageMode !== modeGalleryTwo || config.secondaryPhotoId != null;
            const fingerprintReady = currentProjectReviewFingerprints.has(id);
            const canReview = project.reviewHasNarrative === true
                && typeof project.reviewNarrative === "string"
                && project.reviewNarrative.trim().length > 0
                && info.ready
                && imageTreatmentReady
                && fingerprintReady;
            const isReviewed = isProjectReviewed(id);
            reviewMarkReviewed.disabled = !canReview || isReviewed;
            reviewMarkReviewed.innerHTML = isReviewed
                ? '<i class="bi bi-check2-circle" aria-hidden="true"></i> Approved for publication'
                : '<i class="bi bi-check2-circle" aria-hidden="true"></i> Approve for publication';
        }
        updateButtons(Boolean(lastPreflight?.canGenerate));
    };

    const setMetric = (selector, value) => {
        const node = form.querySelector(selector);
        if (node) node.textContent = String(value);
    };

    const setOutputReadiness = (state, title, detail, iconClass) => {
        if (outputReadiness) {
            outputReadiness.classList.remove("is-pending", "is-blocked", "is-warning", "is-ready");
            outputReadiness.classList.add(`is-${state}`);
        }
        if (outputReadinessTitle) outputReadinessTitle.textContent = title;
        if (outputReadinessDetail) outputReadinessDetail.textContent = detail;
        if (outputReadinessIcon) {
            outputReadinessIcon.innerHTML = `<i class="bi ${iconClass}" aria-hidden="true"></i>`;
        }
    };

    const updateButtons = canGenerate => {
        const previewReady = canGenerate && orderedIds.length > 0 && !exportBusy;
        const coverReady = !isContemporaryCover() || Boolean(coverReviewed && coverReviewFingerprint);
        const finalReady = previewReady && allReviewed() && coverReady;
        const pendingApprovals = Math.max(0, orderedIds.length - reviewedCount());
        const blockers = Number(lastPreflight?.blockerCount || 0);
        const warnings = Number(lastPreflight?.warningCount || 0);

        if (previewButton) previewButton.disabled = !previewReady;
        if (generateButton) generateButton.disabled = !finalReady;

        if (!orderedIds.length) {
            setOutputReadiness("pending", "Select projects", "Choose at least one project to begin publication preflight.", "bi-journals");
        } else if (exportBusy) {
            setOutputReadiness("pending", "Preparing PDF", "PRISM is composing the exact offline publication.", "bi-arrow-repeat");
        } else if (!canGenerate) {
            const detail = blockers > 0
                ? `${blockers} blocker${blockers === 1 ? "" : "s"} must be resolved before preview or download.`
                : "Technical preflight is still running or requires attention.";
            setOutputReadiness(blockers > 0 ? "blocked" : "pending", blockers > 0 ? "Preflight blocked" : "Checking publication", detail, blockers > 0 ? "bi-x-octagon-fill" : "bi-hourglass-split");
        } else if (pendingApprovals > 0) {
            setOutputReadiness("pending", "Preview ready", `${pendingApprovals} project approval${pendingApprovals === 1 ? "" : "s"} remaining before final download.`, "bi-eye");
        } else if (!coverReady) {
            setOutputReadiness("pending", "Cover approval required", "Approve the Cover B hero and crop before final download.", "bi-image");
        } else if (warnings > 0) {
            setOutputReadiness("warning", "Ready with warnings", `${warnings} warning${warnings === 1 ? "" : "s"} remain for editorial review. Final download is available.`, "bi-exclamation-triangle-fill");
        } else {
            setOutputReadiness("ready", "Ready for final issue", `${orderedIds.length} project${orderedIds.length === 1 ? "" : "s"} approved · no blockers.`, "bi-check-circle-fill");
        }

        if (exportStatus && !exportBusy) {
            if (!orderedIds.length) {
                exportStatus.textContent = "";
            } else if (!canGenerate) {
                exportStatus.textContent = blockers > 0
                    ? "Resolve publication blockers to enable preview and download."
                    : "Waiting for technical preflight.";
            } else if (!allReviewed()) {
                exportStatus.textContent = `${pendingApprovals} selected project${pendingApprovals === 1 ? "" : "s"} still require publication approval.`;
            } else if (!coverReady) {
                exportStatus.textContent = "Approve the Cover B hero and crop before final download.";
            } else {
                exportStatus.textContent = "";
            }
        }
    };

    const renderPreflightMessage = () => {
        if (!preflightMessage || !lastPreflight) return;
        const result = lastPreflight;
        preflightMessage.classList.remove("is-checking", "is-blocked", "is-warning", "is-ready");
        if (!orderedIds.length) {
            preflightMessage.textContent = "Select projects to run publication preflight.";
        } else if ((result.blockerCount ?? 0) > 0) {
            preflightMessage.textContent = `${result.blockerCount} blocker${result.blockerCount === 1 ? "" : "s"} must be resolved before preview or download.`;
            preflightMessage.classList.add("is-blocked");
        } else if ((result.warningCount ?? 0) > 0) {
            const coverReady = !isContemporaryCover() || Boolean(coverReviewed && coverReviewFingerprint);
            const reviewStatus = !allReviewed()
                ? "Complete publication approval before final download."
                : !coverReady
                    ? "Approve the Cover B hero and crop before final issue."
                    : "All approvals are complete; review the warnings before final issue.";
            preflightMessage.textContent = `Preflight passed with ${result.warningCount} warning${result.warningCount === 1 ? "" : "s"}. ${reviewStatus}`;
            preflightMessage.classList.add("is-warning");
        } else if (!allReviewed()) {
            preflightMessage.textContent = "Publication preflight passed. Complete publication approval before final download.";
            preflightMessage.classList.add("is-ready");
        } else if (isContemporaryCover() && !(coverReviewed && coverReviewFingerprint)) {
            preflightMessage.textContent = "Publication preflight and project approvals are complete. Approve the Cover B hero and crop before final issue.";
            preflightMessage.classList.add("is-ready");
        } else {
            preflightMessage.textContent = "Publication preflight and all required approvals are complete. The brochure is ready for final issue.";
            preflightMessage.classList.add("is-ready");
        }
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
        "TextOnlyProject",
        "CoverHeroUnavailable",
        "CoverHeroInvalid"
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
            link.textContent = `Open ${narrativeInfo(project).label}`;
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

    const sameProjectSet = (left, right) => {
        if (!Array.isArray(left) || !Array.isArray(right) || left.length !== right.length) return false;
        const leftSet = new Set(left.map(Number));
        return right.every(id => leftSet.has(Number(id)));
    };

    const formatPositivePercent = value => {
        const numeric = Number(value || 0);
        return numeric > 0 ? `${numeric}%` : "—";
    };

    const renderSmartFlow = (suggestion, result) => {
        currentSmartFlowSuggestion = suggestion && Array.isArray(suggestion.suggestedProjectIds)
            ? suggestion
            : null;
        if (!smartFlowPanel) return;

        const canUndo = Array.isArray(smartFlowUndoOrder)
            && sameProjectSet(smartFlowUndoOrder, orderedIds);
        if (!currentSmartFlowSuggestion && !canUndo) {
            smartFlowPanel.hidden = true;
            smartFlowMoveList?.replaceChildren();
            smartFlowSheetMap?.replaceChildren();
            return;
        }

        smartFlowPanel.hidden = false;
        if (currentSmartFlowSuggestion) {
            if (smartFlowSummary) smartFlowSummary.textContent = currentSmartFlowSuggestion.summary || "A stronger forward-packed page flow is available without reducing project typography.";
            if (smartFlowPages) smartFlowPages.textContent = `${Number(currentSmartFlowSuggestion.currentPageCount || 0)} → ${Number(currentSmartFlowSuggestion.suggestedPageCount || 0)}`;
            if (smartFlowFill) smartFlowFill.textContent = `${formatPositivePercent(currentSmartFlowSuggestion.currentLowestProjectUtilizationPercent)} → ${formatPositivePercent(currentSmartFlowSuggestion.suggestedLowestProjectUtilizationPercent)}`;
            if (smartFlowMoves) smartFlowMoves.textContent = String(Number(currentSmartFlowSuggestion.movedProjectCount || 0));
            if (smartFlowTreatment) {
                smartFlowTreatment.textContent = currentSmartFlowSuggestion.adaptiveTreatmentSummary
                    || "Adaptive 9 pt geometry is selected automatically; typography is not reduced.";
            }
            if (smartFlowSheetMap) {
                const sheets = Array.isArray(currentSmartFlowSuggestion.suggestedSheetPlan)
                    ? currentSmartFlowSuggestion.suggestedSheetPlan
                    : [];
                smartFlowSheetMap.replaceChildren(...sheets.map(sheet => {
                    const chip = document.createElement("span");
                    chip.className = "brochure-smart-flow__sheet-chip";
                    if (sheet.isFinal) chip.classList.add("is-final");
                    if (!sheet.isFinal
                        && sheet.kind !== "front"
                        && Number(sheet.utilizationPercent || 0) < 85) chip.classList.add("is-low");
                    chip.textContent = `${Number(sheet.sheetNumber || 0)} · ${sheet.label || "Sheet"} · ${Number(sheet.utilizationPercent || 0)}%`;
                    return chip;
                }));
            }
            if (smartFlowMoveList) {
                const moves = Array.isArray(currentSmartFlowSuggestion.moves) ? currentSmartFlowSuggestion.moves : [];
                smartFlowMoveList.replaceChildren(...moves.map(move => {
                    const item = document.createElement("div");
                    item.className = "brochure-smart-flow__move";
                    const name = document.createElement("strong");
                    name.textContent = move.projectName || projectById.get(Number(move.projectId))?.projectName || `Project ${move.projectId}`;
                    const detail = document.createElement("span");
                    detail.textContent = `Position ${Number(move.fromOrdinal || 0)} → ${Number(move.toOrdinal || 0)}`;
                    item.append(name, detail);
                    return item;
                }));
            }
            if (smartFlowApply) smartFlowApply.hidden = false;
        } else {
            if (smartFlowSummary) smartFlowSummary.textContent = "Smart Flow order applied. Preflight is now showing the resulting composition.";
            if (smartFlowPages) smartFlowPages.textContent = String(Number(result?.estimatedPageCount || 0) || "—");
            if (smartFlowFill) smartFlowFill.textContent = formatPositivePercent(result?.lowestProjectPageUtilizationPercent);
            if (smartFlowMoves) smartFlowMoves.textContent = "Applied";
            smartFlowMoveList?.replaceChildren();
            smartFlowSheetMap?.replaceChildren();
            if (smartFlowTreatment) smartFlowTreatment.textContent = "Applied composition remains at the 9 pt publication typography floor.";
            if (smartFlowApply) smartFlowApply.hidden = true;
        }
        if (smartFlowUndo) smartFlowUndo.hidden = !canUndo;
    };

    const applySmartFlow = () => {
        const suggestion = currentSmartFlowSuggestion;
        const suggestedIds = Array.isArray(suggestion?.suggestedProjectIds)
            ? suggestion.suggestedProjectIds.map(Number).filter(Number.isFinite)
            : [];
        if (!suggestedIds.length || !sameProjectSet(suggestedIds, orderedIds)) return;

        smartFlowUndoOrder = [...orderedIds];
        orderedIds = suggestedIds;
        currentSmartFlowSuggestion = null;
        renderSelected(true, false);
    };

    const undoSmartFlow = () => {
        if (!Array.isArray(smartFlowUndoOrder) || !sameProjectSet(smartFlowUndoOrder, orderedIds)) return;
        orderedIds = [...smartFlowUndoOrder];
        smartFlowUndoOrder = null;
        currentSmartFlowSuggestion = null;
        renderSelected(true, false);
    };

    const renderPreflight = result => {
        lastPreflight = result;
        currentProjectReviewFingerprints = new Map(
            Object.entries(result.projectReviewFingerprints ?? {})
                .map(([projectId, fingerprint]) => [Number(projectId), String(fingerprint || "")])
                .filter(([projectId, fingerprint]) => projectId > 0 && fingerprint.length > 0));
        let invalidatedApproval = false;
        let activeApprovalInvalidated = false;
        orderedIds.forEach(id => {
            const config = ensureConfig(id);
            const current = currentProjectReviewFingerprints.get(id);
            if (config?.isReviewed && (!current || config.reviewFingerprint !== current)) {
                config.isReviewed = false;
                config.reviewFingerprint = "";
                invalidatedApproval = true;
                if (id === activeReviewProjectId) activeApprovalInvalidated = true;
            }
        });
        if (coverReviewed
            && (!result.coverReviewFingerprint || coverReviewFingerprint !== result.coverReviewFingerprint)) {
            coverReviewed = false;
            coverReviewFingerprint = "";
            invalidatedApproval = true;
        }
        if (invalidatedApproval) syncHiddenInputs();
        setMetric("[data-preflight-selected]", result.selectedProjectCount ?? orderedIds.length);
        setMetric("[data-preflight-blockers]", result.blockerCount ?? 0);
        setMetric("[data-preflight-warnings]", result.warningCount ?? 0);
        setMetric("[data-preflight-info]", result.informationCount ?? 0);
        preflightSpinner?.toggleAttribute("hidden", true);

        if (printPlanSummary) {
            const showPlan = isPrintCompactProfile() && orderedIds.length > 0 && Number(result.estimatedPageCount) > 0;
            printPlanSummary.hidden = !showPlan;
            if (showPlan) {
                if (printEstimatePages) printEstimatePages.textContent = String(result.estimatedPageCount);
                if (printEstimateFill) {
                    printEstimateFill.textContent = result.lowestProjectPageUtilizationPercent == null
                        ? "—"
                        : `${Number(result.estimatedAveragePageUtilizationPercent || 0)}%`;
                }
                if (printLowestFill) {
                    printLowestFill.textContent = formatPositivePercent(result.lowestProjectPageUtilizationPercent);
                }
                if (printFinalFill) {
                    const value = Number(result.finalPageUtilizationPercent || 0);
                    printFinalFill.textContent = value > 0 ? `${value}%` : "—";
                }
                if (printEstimateClosing) {
                    const count = Number(result.estimatedClosingPageProjectCount || 0);
                    printEstimateClosing.textContent = result.closingMatterSharesFinalPage
                        ? `${count} project${count === 1 ? "" : "s"} + closing`
                        : "Dedicated closing sheet";
                }
                if (printSheetMap) {
                    printSheetMap.replaceChildren();
                    const sheets = Array.isArray(result.printSheetPlan) ? result.printSheetPlan : [];
                    sheets.forEach(sheet => {
                        const chip = document.createElement("div");
                        chip.className = "brochure-print-sheet-chip";
                        if (sheet.kind === "front") chip.classList.add("is-front");
                        if (sheet.isFinal) chip.classList.add("is-final");
                        if (!sheet.isFinal
                            && sheet.kind !== "front"
                            && Number(sheet.utilizationPercent || 0) < 85) chip.classList.add("is-low");

                        const label = document.createElement("strong");
                        label.textContent = `${sheet.sheetNumber}. ${sheet.label || "Sheet"}`;
                        const fill = document.createElement("span");
                        fill.textContent = `${Number(sheet.utilizationPercent || 0)}%`;
                        chip.append(label, fill);
                        printSheetMap.appendChild(chip);
                    });
                }
            }
        }

        renderSmartFlow(result.smartFlowSuggestion ?? null, result);

        renderPreflightMessage();

        renderIssues(result.issues ?? []);
        if (invalidatedApproval) {
            renderSelected(false, false);
            renderReview();
            renderPreflightMessage();
            if (activeApprovalInvalidated) {
                showReviewNotice("Authoritative publication inputs changed · publication approval reset.");
            }
        }
        renderCoverHero();
        updateButtons(Boolean(result.canGenerate));
    };

    const runPreflight = async () => {
        if (!orderedIds.length) {
            showAllFindings = false;
            renderPreflight({ selectedProjectCount: 0, blockerCount: 0, warningCount: 0, informationCount: 0, canGenerate: false, issues: [], resolvedCoverHeroProjectId: null });
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
            currentProjectReviewFingerprints = new Map();
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
        currentProjectReviewFingerprints = new Map();
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
            selectVisibleLabel.textContent = `Deselect ${visible.length} matching`;
            selectVisibleButton.dataset.mode = "deselect";
        } else if (slots === 0) {
            selectVisibleLabel.textContent = `${MAX_PROJECTS} project limit reached`;
            selectVisibleButton.dataset.mode = "limit";
        } else {
            const count = Math.min(unselected.length, slots);
            selectVisibleLabel.textContent = unselected.length > slots
                ? `Select first ${count} matching`
                : `Select ${count} matching`;
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

    const setExportBusy = (busy, preview = false) => {
        exportBusy = busy;
        if (previewButton) {
            previewButton.setAttribute("aria-busy", busy && preview ? "true" : "false");
        }
        if (generateButton) {
            generateButton.setAttribute("aria-busy", busy && !preview ? "true" : "false");
        }
        generateSpinner?.classList.toggle("d-none", !(busy && !preview));
        generateIcon?.classList.toggle("d-none", busy && !preview);
        if (generateLabel) generateLabel.textContent = busy && !preview ? "Preparing brochure…" : "Download brochure PDF";
        if (exportStatus && busy) exportStatus.textContent = preview ? "Preparing exact PDF preview…" : "Preparing final brochure…";
        updateButtons(Boolean(lastPreflight?.canGenerate));
    };

    const responseError = async response => {
        const type = response.headers.get("content-type") ?? "";
        if (type.includes("application/json")) {
            const payload = await response.json();
            const errors = Array.isArray(payload.errors) ? payload.errors : [];
            return errors.length ? `${payload.message ?? "Publication request failed"} ${errors.join(" ")}` : payload.message ?? "Publication request failed.";
        }
        const text = await response.text();
        return text?.trim() || `Publication request failed with HTTP ${response.status}.`;
    };

    const fileNameFromResponse = response => {
        const explicit = response.headers.get("X-PRISM-Publication-FileName");
        if (explicit) return explicit;
        const disposition = response.headers.get("Content-Disposition") ?? "";
        const utf = disposition.match(/filename\*=UTF-8''([^;]+)/i);
        if (utf?.[1]) return decodeURIComponent(utf[1]);
        const basic = disposition.match(/filename="?([^";]+)"?/i);
        return basic?.[1] ?? "SDD_Capability_Brochure.pdf";
    };

    const requestPdf = async preview => {
        const targetUrl = preview ? previewUrl : generateUrl;
        if (!targetUrl || exportBusy) return;
        if (!lastPreflight?.canGenerate || !orderedIds.length) return;
        if (!preview && !allReviewed()) {
            if (exportStatus) exportStatus.textContent = "Approve all selected projects for publication before final download.";
            reviewPanel?.scrollIntoView({ block: "start", behavior: "smooth" });
            return;
        }

        const previewWindow = preview ? window.open("about:blank", "_blank") : null;
        syncHiddenInputs();
        setExportBusy(true, preview);
        try {
            const response = await fetch(targetUrl, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) throw new Error(await responseError(response));
            const type = response.headers.get("content-type") ?? "";
            if (!type.includes("application/pdf")) throw new Error("The server did not return a PDF publication.");
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const fileName = fileNameFromResponse(response);
            if (preview) {
                if (previewWindow) previewWindow.location.replace(url);
                else window.open(url, "_blank", "noopener");
                window.setTimeout(() => URL.revokeObjectURL(url), 120000);
                if (exportStatus) exportStatus.textContent = "Preview ready in a new tab.";
            } else {
                const link = document.createElement("a");
                link.href = url;
                link.download = fileName;
                document.body.append(link);
                link.click();
                link.remove();
                window.setTimeout(() => URL.revokeObjectURL(url), 30000);
                if (exportStatus) exportStatus.textContent = "Brochure ready. Download started.";
            }
        } catch (error) {
            if (previewWindow && !previewWindow.closed) previewWindow.close();
            if (exportStatus) exportStatus.textContent = error?.message || "The brochure could not be prepared.";
            console.error(error);
        } finally {
            setExportBusy(false, preview);
        }
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
        smartFlowUndoOrder = null;
        const visible = visibleRows();
        if (selectVisibleButton.dataset.mode === "deselect") {
            const visibleIds = new Set(visible.map(row => Number(row.dataset.projectId)));
            orderedIds = orderedIds.filter(id => !visibleIds.has(id));
            if (activePhotoProjectId != null && visibleIds.has(activePhotoProjectId)) closePhotoEditor();
            if (explicitCoverHeroProjectId != null && visibleIds.has(explicitCoverHeroProjectId)) {
                explicitCoverHeroProjectId = null;
                explicitCoverHeroPhotoId = null;
                coverHeroFocalX = 0.5;
                coverHeroFocalY = 0.5;
                coverReviewed = false;
                coverReviewFingerprint = "";
            }
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
        activeReviewProjectId = orderedIds.find(id => !isProjectReviewed(id)) ?? orderedIds[0] ?? null;
        renderSelected(true);
    });

    clearButton?.addEventListener("click", () => {
        smartFlowUndoOrder = null;
        orderedIds = [];
        explicitCoverHeroProjectId = null;
        explicitCoverHeroPhotoId = null;
        coverHeroFocalX = 0.5;
        coverHeroFocalY = 0.5;
        coverReviewed = false;
        coverReviewFingerprint = "";
        activeReviewProjectId = null;
        closePhotoEditor();
        renderSelected(true);
    });

    narrativeSource?.addEventListener("change", () => {
        const hadApprovals = orderedIds.some(id => isProjectReviewed(id));
        invalidateAllReviews();
        if (hadApprovals) {
            showReviewNotice("Narrative source changed · publication approvals reset.");
        }
        orderedIds.forEach(id => {
            const project = projectById.get(id);
            if (project) {
                delete project.reviewNarrative;
                delete project.reviewHasNarrative;
                delete project.reviewNarrativeWordCount;
                delete project.__publicationSignature;
            }
        });
        updateNarrativeIndicators();
        applyFilters();
        renderReview();
        scheduleProjectStateRefresh();
        schedulePreflight();
    });

    form.querySelectorAll("[data-brochure-preflight-trigger]").forEach(element => {
        if (element === narrativeSource || publicationProfileInputs.includes(element)) return;
        element.addEventListener("change", () => {
            renderCoverHero();
            schedulePreflight();
        });
    });

    printMatterFields.forEach(field => {
        field.addEventListener("input", () => {
            updatePrintMatterWordCounts();
            schedulePreflight();
        });
    });

    restoreApprovedPrint?.addEventListener("click", () => {
        const shouldRestore = window.confirm("Restore all hard-copy institutional text to the approved reference wording?");
        if (!shouldRestore) return;

        printMatterFields.forEach(field => {
            const key = approvedPrintFieldMap[field.name];
            if (!key || typeof approvedPrintContent[key] !== "string") return;
            field.value = approvedPrintContent[key];
        });
        updatePrintMatterWordCounts();
        schedulePreflight();
    });

    publicationProfileInputs.forEach(input => {
        input.addEventListener("change", () => {
            coverReviewed = false;
            coverReviewFingerprint = "";

            // Official/institutional is the natural hard-copy default; contemporary is the
            // screen-oriented default. Once the user explicitly chooses a cover, profile changes
            // preserve that editorial decision.
            if (!coverSelectionTouched) {
                const preferredValue = isPrintCompactProfile() ? "1" : "2";
                const preferred = form.querySelector(`[name="Input.CoverStyle"][value="${preferredValue}"]`);
                if (preferred instanceof HTMLInputElement) {
                    preferred.checked = true;
                    form.querySelectorAll("[data-cover-option]").forEach(option => {
                        option.classList.toggle("is-selected", option.querySelector("input")?.checked === true);
                    });
                }
            }

            updatePublicationProfileUi();
            syncHiddenInputs();
            renderCoverHero();
            schedulePreflight();
        });
    });

    form.querySelectorAll("[data-artwork-option] input[type=radio]").forEach(radio => {
        radio.addEventListener("change", () => {
            form.querySelectorAll("[data-artwork-option]").forEach(option => {
                option.classList.toggle("is-selected", option.querySelector("input")?.checked === true);
            });
            schedulePreflight();
        });
    });

    form.querySelectorAll("[data-cover-option] input[type=radio]").forEach(radio => {
        radio.addEventListener("change", () => {
            coverSelectionTouched = true;
            form.querySelectorAll("[data-cover-option]").forEach(option => {
                option.classList.toggle("is-selected", option.querySelector("input")?.checked === true);
            });
            coverReviewed = false;
            coverReviewFingerprint = "";
            if (coverHeroCropPanel) coverHeroCropPanel.hidden = true;
            syncHiddenInputs();
            renderCoverHero();
            schedulePreflight();
        });
    });

    form.querySelectorAll(
        '[name="Input.Title"], [name="Input.Subtitle"], [name="Input.Edition"], [name="Input.Strapline"], [name="Input.HandlingMarking"]'
    ).forEach(field => {
        field.addEventListener("input", () => {
            coverReviewed = false;
            coverReviewFingerprint = "";
            syncHiddenInputs();
            renderCoverHero();
            schedulePreflight();
        });
    });

    coverHeroChoose?.addEventListener("click", () => {
        if (!coverHeroChoices) return;
        renderCoverHeroChoices();
        coverHeroChoices.hidden = !coverHeroChoices.hidden;
        if (!coverHeroChoices.hidden) {
            window.requestAnimationFrame(() => coverHeroChoices.scrollIntoView({ block: "nearest", behavior: "smooth" }));
        }
    });
    coverHeroAutomatic?.addEventListener("click", () => {
        explicitCoverHeroProjectId = null;
        explicitCoverHeroPhotoId = null;
        coverHeroFocalX = 0.5;
        coverHeroFocalY = 0.5;
        coverReviewed = false;
        coverReviewFingerprint = "";
        if (coverHeroChoices) coverHeroChoices.hidden = true;
        if (coverHeroCropPanel) coverHeroCropPanel.hidden = true;
        syncHiddenInputs();
        renderCoverHero();
        renderPreflightMessage();
        schedulePreflight();
    });
    coverHeroCrop?.addEventListener("click", () => {
        const hero = ensureExplicitCoverHero();
        if (!hero || !coverHeroCropPanel) return;
        coverReviewed = false;
        coverReviewFingerprint = "";
        coverHeroCropPanel.hidden = false;
        syncHiddenInputs();
        renderCoverHero();
        updateCoverFocalStage();
        schedulePreflight();
        window.requestAnimationFrame(() => {
            coverHeroCropPanel.scrollIntoView({ block: "nearest", behavior: "smooth" });
            coverHeroFocalStage?.setAttribute("tabindex", "-1");
            coverHeroFocalStage?.focus({ preventScroll: true });
        });
    });
    coverHeroApprove?.addEventListener("click", () => {
        const hero = ensureExplicitCoverHero();
        if (!hero) return;
        const fingerprint = String(lastPreflight?.coverReviewFingerprint || "");
        if (!fingerprint) return;
        coverReviewed = true;
        coverReviewFingerprint = fingerprint;
        if (coverHeroCropPanel) coverHeroCropPanel.hidden = true;
        syncHiddenInputs();
        renderCoverHero();
    });
    coverHeroFocalStage?.addEventListener("click", setCoverFocalFromEvent);
    coverHeroFocalReset?.addEventListener("click", event => {
        event.stopPropagation();
        resetCoverFocal();
    });
    coverHeroCropClose?.addEventListener("click", () => {
        if (coverHeroCropPanel) coverHeroCropPanel.hidden = true;
    });

    imageModeSelect?.addEventListener("change", () => {
        if (activePhotoProjectId == null) return;
        const config = ensureConfig(activePhotoProjectId);
        if (!config) return;
        config.imageMode = Number(imageModeSelect.value) || modeAutomatic;
        invalidateReview(activePhotoProjectId, { announce: true, reason: "Image treatment changed" });
        renderPhotoEditor();
        renderSelected(false);
        schedulePreflight();
    });

    photoEditorCloseButtons.forEach(button => button.addEventListener("click", closePhotoEditor));
    photoEditorDismiss?.addEventListener("click", closePhotoEditor);
    primaryStage?.addEventListener("click", event => setFocalFromEvent("primary", event));
    secondaryStage?.addEventListener("click", event => setFocalFromEvent("secondary", event));
    primaryReset?.addEventListener("click", event => { event.stopPropagation(); resetFocal("primary"); });
    secondaryReset?.addEventListener("click", event => { event.stopPropagation(); resetFocal("secondary"); });

    reviewImageModeSelect?.addEventListener("change", () => {
        if (activeReviewProjectId == null) return;
        const config = ensureConfig(activeReviewProjectId);
        if (!config) return;
        const nextMode = Number(reviewImageModeSelect.value) || modeAutomatic;
        config.imageMode = [modeAutomatic, modeSingle, modeGalleryTwo].includes(nextMode) ? nextMode : modeAutomatic;
        invalidateReview(activeReviewProjectId, { announce: true, reason: "Image treatment changed" });
        syncHiddenInputs();
        renderSelected(false);
        renderReview();
        schedulePreflight();

        if (config.imageMode === modeGalleryTwo && config.secondaryPhotoId == null) {
            openPhotoEditor(activeReviewProjectId, "secondary");
        }
    });

    reviewChangeImage?.addEventListener("click", () => {
        if (activeReviewProjectId != null) openPhotoEditor(activeReviewProjectId, "select");
    });
    reviewAdjustCrop?.addEventListener("click", () => {
        if (activeReviewProjectId != null) openPhotoEditor(activeReviewProjectId, "crop");
    });
    reviewPrevious?.addEventListener("click", () => {
        const index = orderedIds.indexOf(activeReviewProjectId);
        if (index > 0) setActiveReview(orderedIds[index - 1]);
    });
    reviewNext?.addEventListener("click", () => {
        const index = orderedIds.indexOf(activeReviewProjectId);
        if (index >= 0 && index < orderedIds.length - 1) setActiveReview(orderedIds[index + 1]);
    });
    reviewNextUnreviewed?.addEventListener("click", () => {
        const id = orderedIds.find(projectId => !isProjectReviewed(projectId));
        if (id) setActiveReview(id);
    });
    reviewMarkReviewed?.addEventListener("click", () => {
        if (activeReviewProjectId == null) return;
        const project = projectById.get(activeReviewProjectId);
        const config = ensureConfig(activeReviewProjectId);
        if (!project || !config || project.reviewHasNarrative !== true || !narrativeInfo(project).ready) return;
        const fingerprint = currentProjectReviewFingerprints.get(activeReviewProjectId);
        if (!fingerprint) return;
        if (config.primaryPhotoId != null) config.primaryPhotoConfirmed = true;
        config.isReviewed = true;
        config.reviewFingerprint = fingerprint;
        if (reviewNotice) {
            reviewNotice.hidden = true;
            reviewNotice.classList.remove("is-warning", "is-info");
        }
        if (reviewNoticeTimer) {
            window.clearTimeout(reviewNoticeTimer);
            reviewNoticeTimer = null;
        }
        syncHiddenInputs();
        renderSelected(false, false);
        renderReview();
        const nextId = orderedIds.find(projectId => !isProjectReviewed(projectId));
        if (nextId) activeReviewProjectId = nextId;
        renderReview();
        renderPreflightMessage();
    });

    smartFlowApply?.addEventListener("click", applySmartFlow);
    smartFlowUndo?.addEventListener("click", undoSmartFlow);

    preflightShowAll?.addEventListener("click", () => {
        showAllFindings = !showAllFindings;
        renderIssues(lastPreflight?.issues ?? []);
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && photoEditor && !photoEditor.hidden) {
            event.preventDefault();
            closePhotoEditor();
        }
    });

    form.addEventListener("submit", event => {
        const isPreview = event.submitter?.matches("[data-brochure-preview]") === true;
        const isGenerate = event.submitter?.matches("[data-brochure-generate]") === true;
        if (!isPreview && !isGenerate) return;
        event.preventDefault();
        requestPdf(isPreview);
    });

    window.addEventListener("resize", () => {
        if (activePhotoProjectId != null) {
            updateFocalStage("primary", activePhotoProjectId);
            updateFocalStage("secondary", activePhotoProjectId);
        }
        if (coverHeroCropPanel && !coverHeroCropPanel.hidden) {
            updateCoverFocalStage();
        }
    });

    const refreshAfterExternalEdit = () => {
        if (!orderedIds.length || document.hidden) return;
        if (Date.now() - lastProjectStateRefresh < 500) return;
        scheduleProjectStateRefresh();
    };
    window.addEventListener("focus", refreshAfterExternalEdit);
    window.addEventListener("pageshow", refreshAfterExternalEdit);
    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) refreshAfterExternalEdit();
    });

    updatePrintMatterWordCounts();
    updatePublicationProfileUi();
    updateNarrativeIndicators();
    renderSelected(false);
    applyFilters();
    renderCoverHero();
    renderReview();
    scheduleProjectStateRefresh();
    schedulePreflight();
})();
