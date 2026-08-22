(() => {
    'use strict';

    const root = document.querySelector('[data-compendium-cover-editor]');
    if (!root) return;

    const bootstrapNode = root.querySelector('[data-cover-editor-bootstrap]');
    if (!bootstrapNode) return;

    let boot;
    try { boot = JSON.parse(bootstrapNode.textContent || '{}'); }
    catch { boot = {}; }

    const clone = value => JSON.parse(JSON.stringify(value ?? null));
    const by = selector => root.querySelector(selector);
    const all = selector => Array.from(root.querySelectorAll(selector));
    // Bootstrap modals are rendered as body-level portals outside the editor root.
    // Keep normal editor queries scoped, but resolve portal controls explicitly.
    const portalBy = selector => document.querySelector(selector);
    const csrf = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const modal = id => window.bootstrap?.Modal.getOrCreateInstance(document.getElementById(id));
    const clean = value => (value ?? '').toString().trim();
    const clamp01 = value => Math.max(0, Math.min(1, Number.isFinite(Number(value)) ? Number(value) : .5));
    const coverState = globalThis.PrismCompendiumCoverState;
    if (!coverState) {
        console.error('PRISM Compendium cover state contract is unavailable. Reload the page before editing the cover.');
        return;
    }
    const titleCase = value => (value || '').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase());
    const templateDisplayName = value => ({
        InstitutionalHero: 'Institutional Hero',
        FullBleedHero: 'Full-Bleed Hero',
        EditorialSplit: 'Editorial Split',
        Triptych: 'Portfolio Triptych',
        PortfolioQuartet: 'Portfolio Quartet',
        Minimal: 'Minimal',
        MinimalInstitutional: 'Minimal Institutional',
        ImageEcho: 'Image Echo',
        PortfolioStrip: 'Portfolio Strip',
        TypographyOnly: 'Typography Only',
        Clean: 'Clean Back'
    })[value] || titleCase(value);

    const coverPolicy = boot.coverPolicy || { front: [], back: [] };
    const identityPolicy = boot.coverIdentityPolicy || { themes: [], backgrounds: [], compatibility: {} };
    const typographyPolicy = boot.coverTypographyPolicy || {
        titleSoftLength: 42,
        titleMediumLength: 70,
        titleLongLength: 95,
        minimumTitleSize: 21,
        subtitleSoftLength: 105,
        subtitleLongLength: 145,
        minimumSubtitleSize: 12.5,
        frontTitleSizes: {},
        backTitleSizes: {}
    };
    const themeDefinition = key => (identityPolicy.themes || []).find(item => item.key === key)
        || (identityPolicy.themes || [])[0]
        || { key: 'InstitutionalGreen', displayName: 'Institutional Green', shortName: 'Green', primary: '#102A23', secondary: '#17382F', surface: '#21483D', foreground: '#FFFFFF', mutedForeground: '#D7E3DE', patternLight: '#769A8C', patternDark: '#091D18' };
    const backgroundDefinition = key => (identityPolicy.backgrounds || []).find(item => item.key === key)
        || (identityPolicy.backgrounds || [])[0]
        || { key: 'Solid', displayName: 'Solid', shortName: 'Solid' };
    const allowedBackgrounds = theme => new Set(identityPolicy.compatibility?.[theme] || (identityPolicy.backgrounds || []).map(item => item.key));
    const backgroundAllowed = (theme, treatment) => allowedBackgrounds(theme).has(treatment);

    const initialDesign = normaliseDesign(boot.coverDesign || {});
    const state = {
        design: clone(initialDesign),
        preferences: normalisePreferences(boot.photoPreferences || []),
        publication: boot.publication || {},
        projects: Array.isArray(boot.projects) ? boot.projects : [],
        rowVersion: boot.preset?.rowVersion || '',
        activeSurface: 'front',
        proofSurface: 'front',
        proofZoom: 'fit',
        activeSlot: null,
        photoCache: new Map(),
        previewCache: new Map(),
        autoResolved: new Map(),
        automaticCandidates: normaliseAutomaticCandidates(boot.automaticCandidates || []),
        automaticCandidatesDirty: false,
        hydrationVersions: new Map(),
        photoPickerRequestVersion: 0,
        photoPickerAbortController: null,
        overrideEditing: new Set(),
        dirty: false,
        leaveAfterSave: false,
        navigatingAway: false
    };
    const captureBackVisibility = () => ({
        showBackTitle: state.design.showBackTitle !== false,
        showBackSubtitle: state.design.showBackSubtitle !== false,
        showBackEdition: state.design.showBackEdition !== false,
        showBackLeftLogo: state.design.showBackLeftLogo !== false,
        showBackRightLogo: state.design.showBackRightLogo !== false,
        backLogoPlacement: state.design.backLogoPlacement || 'TopCorners',
        backEyebrow: state.design.backEyebrow || ''
    });
    const applyBackVisibility = value => Object.assign(state.design, value || {});
    state.standardBackVisibility = state.design.backTemplate === 'Clean'
        ? { showBackTitle: true, showBackSubtitle: true, showBackEdition: true, showBackLeftLogo: true, showBackRightLogo: true, backLogoPlacement: state.design.backLogoPlacement || 'TopCorners', backEyebrow: '' }
        : captureBackVisibility();
    state.cleanBackVisibility = state.design.backTemplate === 'Clean' ? captureBackVisibility() : null;

    if (!backgroundAllowed(state.design.publicationTheme, state.design.backgroundTreatment)) {
        state.design.backgroundTreatment = 'Solid';
    }

    function buildCoverSavePayload() {
        return {
            ...state.design,
            images: (state.design.images || []).map(item => {
                const { previewUrl, sourceWidth, sourceHeight, ...persisted } = item;
                return persisted;
            })
        };
    }

    function buildPreferenceSavePayload() {
        return normalisePreferences(state.preferences)
            .slice()
            .sort((left, right) => left.projectId - right.projectId || left.photoId - right.photoId);
    }

    const persistedSignature = () => JSON.stringify({
        design: buildCoverSavePayload(),
        preferences: buildPreferenceSavePayload()
    });
    let savedSignature = persistedSignature();

    const templatePolicy = (surface, template = null) => {
        const list = surface === 'front' ? coverPolicy.front : coverPolicy.back;
        const name = template || currentTemplate(surface);
        return (Array.isArray(list) ? list : []).find(item => item.template === name) || { slots: [], requiredSlots: [], minimumDistinctImages: 0, fillOnly: false };
    };

    function normaliseDesign(value) {
        return {
            frontTemplate: value.frontTemplate || 'InstitutionalHero',
            backTemplate: value.backTemplate || 'MinimalInstitutional',
            publicationTheme: value.publicationTheme || 'InstitutionalGreen',
            backgroundTreatment: value.backgroundTreatment || 'Solid',
            frontTitle: value.frontTitle ?? '',
            frontSubtitle: value.frontSubtitle ?? '',
            frontEdition: value.frontEdition ?? '',
            frontEyebrow: value.frontEyebrow ?? '',
            backTitle: value.backTitle ?? '',
            backSubtitle: value.backSubtitle ?? '',
            backEdition: value.backEdition ?? '',
            backEyebrow: value.backEyebrow ?? '',
            showFrontTitle: value.showFrontTitle !== false,
            showFrontSubtitle: value.showFrontSubtitle !== false,
            showFrontEdition: value.showFrontEdition !== false,
            showFrontLeftLogo: value.showFrontLeftLogo !== false,
            showFrontRightLogo: value.showFrontRightLogo !== false,
            frontLogoPlacement: value.frontLogoPlacement || 'TopCorners',
            showBackTitle: value.showBackTitle !== false,
            showBackSubtitle: value.showBackSubtitle !== false,
            showBackEdition: value.showBackEdition !== false,
            showBackLeftLogo: value.showBackLeftLogo !== false,
            showBackRightLogo: value.showBackRightLogo !== false,
            backLogoPlacement: value.backLogoPlacement || 'TopCorners',
            images: Array.isArray(value.images) ? value.images.map((item, index) => ({
                surface: titleCase(item.surface || 'Front'),
                slotKey: item.slotKey || `Slot${index + 1}`,
                imageMode: titleCase(item.imageMode || 'Automatic'),
                projectId: Number(item.projectId) || null,
                photoId: Number(item.photoId) || null,
                focalX: clamp01(item.focalX),
                focalY: clamp01(item.focalY),
                fitMode: titleCase(item.fitMode || 'Fill'),
                sortOrder: Number(item.sortOrder) || index,
                previewUrl: item.previewUrl || null,
                sourceWidth: item.sourceWidth || null,
                sourceHeight: item.sourceHeight || null
            })) : []
        };
    }

    function normalisePreferences(values) {
        const map = new Map();
        (Array.isArray(values) ? values : []).forEach(item => {
            const projectId = Number(item.projectId);
            const photoId = Number(item.photoId);
            if (!projectId || !photoId) return;
            map.set(`${projectId}:${photoId}`, {
                projectId,
                photoId,
                preferredForPublication: !!item.preferredForPublication,
                suitableForCoverHero: !!item.suitableForCoverHero
            });
        });
        return Array.from(map.values());
    }

    function normaliseAutomaticCandidates(values) {
        return (Array.isArray(values) ? values : [])
            .map(item => ({
                projectId: Number(item.projectId),
                photoId: Number(item.photoId),
                focalX: clamp01(item.focalX),
                focalY: clamp01(item.focalY),
                priority: Number(item.priority) || 0
            }))
            .filter(item => item.projectId > 0 && item.photoId > 0)
            .sort((left, right) => right.priority - left.priority || left.projectId - right.projectId || left.photoId - right.photoId);
    }

    function surfacePrefix(surface = state.activeSurface) { return surface === 'front' ? 'front' : 'back'; }
    function isFront(surface = state.activeSurface) { return surface === 'front'; }
    function currentTemplate(surface = state.activeSurface) { return isFront(surface) ? state.design.frontTemplate : state.design.backTemplate; }
    function templateSlots(surface = state.activeSurface) {
        const policy = templatePolicy(surface);
        if (Array.isArray(policy.slots)) return policy.slots;
        // Backward compatibility for a stale page bootstrap while deploying Phase 36.
        return Array.isArray(policy.requiredSlots) ? policy.requiredSlots : [];
    }
    function strictRequiredSlots(surface = state.activeSurface) {
        const policy = templatePolicy(surface);
        return Array.isArray(policy.requiredSlots) ? policy.requiredSlots : [];
    }
    function isFillOnlyTemplate(surface = state.activeSurface) { return templatePolicy(surface).fillOnly === true; }
    function isQuartet(surface = state.activeSurface) { return surface === 'front' && currentTemplate(surface) === 'PortfolioQuartet'; }
    function quartetResolved() {
        if (!isQuartet('front')) return true;
        const refs = new Set();
        for (const key of strictRequiredSlots('front')) {
            const slot = findSlot('front', key);
            if (!slot) return false;
            if (slot.imageMode === 'None' || !slot.previewUrl) return false;
            const auto = state.autoResolved.get(automaticSlotKey('front', key));
            const projectId = Number(slot.imageMode === 'Explicit' ? slot.projectId : auto?.projectId);
            const photoId = Number(slot.imageMode === 'Explicit' ? slot.photoId : auto?.photoId);
            if (!projectId || !photoId) return false;
            const ref = `${projectId}:${photoId}`;
            if (refs.has(ref)) return false;
            refs.add(ref);
        }
        return refs.size === 4;
    }
    function findSlot(surface, slotKey) {
        return state.design.images.find(item => item.surface.toLowerCase() === surface && item.slotKey.toLowerCase() === slotKey.toLowerCase());
    }
    function ensureSlot(surface, slotKey) {
        let item = findSlot(surface, slotKey);
        if (!item) {
            item = { surface: titleCase(surface), slotKey, imageMode: 'Automatic', projectId: null, photoId: null, focalX: .5, focalY: .5, fitMode: 'Fill', previewUrl: null };
            state.design.images.push(item);
        }
        return item;
    }

    function requiredSlotsConfigured(surface) {
        const required = strictRequiredSlots(surface);
        if (!required.length) return true;
        return required.every(key => {
            const slot = findSlot(surface, key);
            if (!slot || slot.imageMode === 'None') return false;
            if (slot.imageMode === 'Explicit') return !!Number(slot.projectId) && !!Number(slot.photoId);
            return state.automaticCandidates.length > 0;
        });
    }

    function coverReadyForSave() {
        return requiredSlotsConfigured('front')
            && requiredSlotsConfigured('back')
            && quartetResolved();
    }

    function setDirty() {
        const signature = persistedSignature();
        state.dirty = signature !== savedSignature;
        const save = by('[data-cover-save]');
        if (save) save.disabled = !state.dirty || !boot.canManage || !coverReadyForSave();
        const saveState = by('[data-cover-save-state]');
        if (saveState) {
            saveState.classList.toggle('is-modified', state.dirty);
            saveState.querySelector('i')?.classList.toggle('bi-check-circle', !state.dirty);
            saveState.querySelector('i')?.classList.toggle('bi-pencil', state.dirty);
            const label = saveState.querySelector('span');
            if (label) label.textContent = state.dirty ? 'Modified' : 'Saved';
        }
    }

    function inheritedText(field) {
        if (field === 'Title') return state.publication.title || '';
        if (field === 'Subtitle') return state.publication.subtitle || '';
        if (field === 'Edition') return state.publication.edition || '';
        return '';
    }

    function overrideKey(surface, field) { return `${surface}:${field.toLowerCase()}`; }

    function currentText(surface, field) {
        const prefix = surfacePrefix(surface);
        const override = clean(state.design[`${prefix}${field}`]);
        return override || inheritedText(field);
    }

    function currentShow(surface, field) {
        const prefix = surfacePrefix(surface);
        return state.design[`show${titleCase(prefix)}${field}`] !== false;
    }

    function updateInspector() {
        const surface = state.activeSurface;
        const front = isFront(surface);
        by('[data-cover-inspector-surface]').textContent = front ? 'Front cover' : 'Back cover';
        all('[data-cover-surface]').forEach(button => {
            const active = button.dataset.coverSurface === surface;
            button.classList.toggle('active', active);
            button.setAttribute('aria-selected', active ? 'true' : 'false');
        });
        by('[data-cover-front-templates]').hidden = !front;
        by('[data-cover-back-templates]').hidden = front;
        const list = front ? by('[data-cover-front-templates]') : by('[data-cover-back-templates]');
        list?.querySelectorAll('[data-cover-template]').forEach(button => {
            button.classList.toggle('active', button.dataset.coverTemplate === currentTemplate(surface));
            if (button.dataset.coverTemplate === 'PortfolioQuartet') {
                button.disabled = boot.portfolioQuartetEligible !== true;
                button.title = button.disabled
                    ? `Portfolio Quartet requires four usable photographs; ${Number(boot.portfolioQuartetUsablePhotoCount || 0)} currently resolved.`
                    : 'Four-image cover with independent focal crops.';
            }
        });

        ['title', 'subtitle', 'edition', 'eyebrow'].forEach(field => {
            const fieldName = titleCase(field);
            const property = `${surfacePrefix(surface)}${fieldName}`;
            const overrideValue = state.design[property] || '';
            const input = by(`[data-cover-text="${field}"]`);
            if (input) input.value = overrideValue;
            const show = by(`[data-cover-show="${field}"]`);
            if (show) {
                if (field === 'eyebrow') {
                    show.checked = !!clean(overrideValue);
                    show.disabled = false;
                } else {
                    show.checked = currentShow(surface, fieldName);
                }
            }

            if (field !== 'eyebrow') {
                const key = overrideKey(surface, field);
                const hasOverride = !!clean(overrideValue);
                const editing = hasOverride || state.overrideEditing.has(key);
                const inheritedView = by(`[data-cover-inherited-view="${field}"]`);
                const overrideEditor = by(`[data-cover-override-editor="${field}"]`);
                const inheritedValue = by(`[data-cover-inherited-value="${field}"]`);
                const fieldState = by(`[data-cover-field-state="${field}"]`);
                const wrap = by(`[data-cover-field-wrap="${field}"]`);
                if (inheritedValue) inheritedValue.textContent = inheritedText(fieldName) || 'Not recorded in Publication settings';
                if (inheritedView) inheritedView.hidden = editing;
                if (overrideEditor) overrideEditor.hidden = !editing;
                if (fieldState) {
                    fieldState.textContent = hasOverride ? 'Override' : editing ? 'New override' : 'Inherited';
                    fieldState.classList.toggle('is-override', editing);
                }
                if (wrap) wrap.classList.toggle('is-hidden', !currentShow(surface, fieldName));
            }
        });
        by('[data-cover-logo="left"]').checked = state.design[`show${front ? 'Front' : 'Back'}LeftLogo`] !== false;
        by('[data-cover-logo="right"]').checked = state.design[`show${front ? 'Front' : 'Back'}RightLogo`] !== false;
        const placement = by('[data-cover-logo-placement]');
        if (placement) placement.value = state.design[`${surfacePrefix(surface)}LogoPlacement`] || 'TopCorners';
        updateIdentityControls();
        renderSlots();
        renderProof();
        updateTemplateLabels();
    }

    function updateTemplateLabels() {
        by('[data-cover-front-template-label]').textContent = templateDisplayName(state.design.frontTemplate);
        by('[data-cover-back-template-label]').textContent = templateDisplayName(state.design.backTemplate);
        const theme = themeDefinition(state.design.publicationTheme);
        const background = backgroundDefinition(state.design.backgroundTreatment);
        const themeLabel = by('[data-cover-theme-label]');
        const backgroundLabel = by('[data-cover-background-label]');
        if (themeLabel) themeLabel.textContent = theme.displayName || theme.shortName || 'Institutional Green';
        if (backgroundLabel) backgroundLabel.textContent = `${background.shortName || background.displayName || 'Solid'} background`;
    }

    function updateIdentityControls() {
        const theme = state.design.publicationTheme || 'InstitutionalGreen';
        const treatment = state.design.backgroundTreatment || 'Solid';
        const definition = themeDefinition(theme);
        root.style.setProperty('--cover-identity-primary', definition.primary || '#102A23');
        root.style.setProperty('--cover-identity-secondary', definition.secondary || '#17382F');
        root.style.setProperty('--cover-identity-surface', definition.surface || definition.secondary || '#21483D');
        root.style.setProperty('--cover-identity-pattern', definition.patternLight || '#769A8C');
        root.style.setProperty('--cover-identity-pattern-dark', definition.patternDark || '#091D18');
        all('[data-cover-theme]').forEach(button => {
            const active = button.dataset.coverTheme === theme;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
        all('[data-cover-background]').forEach(button => {
            const allowed = backgroundAllowed(theme, button.dataset.coverBackground);
            const active = button.dataset.coverBackground === treatment;
            button.disabled = !allowed;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
            button.title = allowed ? '' : 'This background treatment is not approved for the selected publication theme.';
        });
        const note = by('[data-cover-background-note]');
        if (note) {
            note.textContent = state.activeSurface === 'back' && state.design.backTemplate === 'Clean'
                ? 'Clean Back intentionally uses the selected publication colour without a pattern.'
                : 'Patterns are deliberately low contrast; gold remains the common institutional accent.';
        }
    }

    function effectiveBackground(surface) {
        if (surface === 'back' && state.design.backTemplate === 'Clean') return 'Solid';
        return backgroundAllowed(state.design.publicationTheme, state.design.backgroundTreatment)
            ? state.design.backgroundTreatment
            : 'Solid';
    }

    function patternRegion(surface, template) {
        if (surface === 'back') {
            if (template === 'ImageEcho') return { top: 0, height: 300 };
            if (template === 'PortfolioStrip') return { top: 0, height: 510 };
            return { top: 0, height: 842 };
        }
        if (template === 'FullBleedHero') return { top: 527, height: 315 };
        if (template === 'EditorialSplit') return { top: 0, height: 355 };
        if (template === 'Triptych') return { top: 0, height: 395 };
        if (template === 'PortfolioQuartet') return { top: 0, height: 338 };
        return { top: 0, height: 842 };
    }

    function renderCoverPattern(surface, template, sheet) {
        const pattern = by('[data-cover-proof-pattern]');
        if (!pattern || !sheet) return;
        const theme = themeDefinition(state.design.publicationTheme);
        sheet.style.setProperty('--cover-theme-primary', theme.primary || '#102A23');
        sheet.style.setProperty('--cover-theme-secondary', theme.secondary || '#17382F');
        sheet.style.setProperty('--cover-theme-foreground', theme.foreground || '#FFFFFF');
        sheet.style.setProperty('--cover-theme-muted', theme.mutedForeground || '#D7E3DE');
        const treatment = effectiveBackground(surface);
        sheet.dataset.coverTheme = state.design.publicationTheme;
        sheet.dataset.coverBackground = treatment;
        if (treatment === 'Solid' || !boot.patternUrl) {
            pattern.hidden = true;
            pattern.removeAttribute('src');
            return;
        }
        const region = patternRegion(surface, template);
        pattern.style.top = `${region.top}px`;
        pattern.style.height = `${region.height}px`;
        const url = new URL(boot.patternUrl, window.location.origin);
        url.searchParams.set('theme', state.design.publicationTheme);
        url.searchParams.set('treatment', treatment);
        url.searchParams.set('surface', titleCase(surface));
        url.searchParams.set('backTemplate', state.design.backTemplate);
        pattern.src = url.toString();
        pattern.hidden = false;
    }

    function renderSlots() {
        const host = by('[data-cover-slot-list]');
        const section = by('[data-cover-image-section]');
        if (!host || !section) return;
        const slots = templateSlots();
        section.hidden = slots.length === 0;
        host.innerHTML = '';
        slots.forEach((slotKey, index) => {
            const slot = ensureSlot(state.activeSurface, slotKey);
            if (isFillOnlyTemplate()) slot.fitMode = 'Fill';
            const row = document.createElement('article');
            row.className = 'compendium-cover-slot-card';
            row.dataset.coverSlotKey = slotKey;
            const label = slotKey === 'Hero' ? 'Hero image' : `Supporting image ${index}`;
            const auto = state.autoResolved.get(`${state.activeSurface}:${slotKey}`);
            const source = slot.imageMode === 'Explicit'
                ? projectName(slot.projectId) || 'Selected photograph'
                : slot.imageMode === 'None'
                    ? 'No image'
                    : auto?.projectId
                        ? `Automatic · ${projectName(auto.projectId) || 'ranked photograph'}`
                        : 'Automatic selection';
            const resolvedFocal = slot.imageMode === 'Automatic'
                ? state.autoResolved.get(automaticSlotKey(state.activeSurface, slotKey))
                : null;
            const previewFocalX = resolvedFocal?.focalX ?? slot.focalX;
            const previewFocalY = resolvedFocal?.focalY ?? slot.focalY;
            const preview = slot.previewUrl
                ? `<img src="${escapeHtml(slot.previewUrl)}" alt="" style="object-fit:${slot.fitMode === 'Fit' ? 'contain' : 'cover'};object-position:${previewFocalX * 100}% ${previewFocalY * 100}%" />`
                : `<span class="compendium-cover-slot-placeholder"><i class="bi ${slot.imageMode === 'None' ? 'bi-image-alt' : 'bi-stars'}"></i></span>`;
            row.innerHTML = `
                <div class="compendium-cover-slot-thumb">${preview}</div>
                <div class="compendium-cover-slot-copy"><strong>${label}</strong><span>${escapeHtml(source)}</span><small>${slot.imageMode === 'Explicit' ? `${slot.fitMode} · independent crop` : slot.imageMode}</small></div>
                <div class="compendium-cover-slot-actions">
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-cover-choose-slot="${slotKey}"><i class="bi bi-images"></i> Change</button>
                    <div class="btn-group btn-group-sm" role="group" aria-label="Image fit">
                        <button type="button" class="btn btn-outline-secondary ${slot.fitMode === 'Fill' ? 'active' : ''}" data-cover-fit="Fill" data-cover-slot="${slotKey}">Fill</button>
                        <button type="button" class="btn btn-outline-secondary ${slot.fitMode === 'Fit' ? 'active' : ''}" data-cover-fit="Fit" data-cover-slot="${slotKey}" ${isFillOnlyTemplate() ? 'disabled title="Portfolio Quartet uses Fill only"' : ''}>Fit</button>
                    </div>
                    <button type="button" class="btn btn-sm btn-link" data-cover-crop-slot="${slotKey}" ${slot.imageMode === 'None' || !slot.previewUrl || slot.fitMode === 'Fit' ? 'disabled' : ''}>Adjust crop</button>
                </div>`;
            host.appendChild(row);
        });
        void hydrateVisibleSlotPreviews();
    }

    function projectName(projectId) { return state.projects.find(item => Number(item.projectId) === Number(projectId))?.projectName || ''; }

    function applyProofZoom(resetScroll = false) {
        const stage = by('.compendium-cover-proof-stage');
        const sheet = by('[data-cover-proof-sheet]');
        if (!stage || !sheet) return;

        let scale = 1;
        if (state.proofZoom === '75') scale = .75;
        else if (state.proofZoom === '100') scale = 1;
        else {
            const style = window.getComputedStyle(stage);
            const horizontalPadding = (parseFloat(style.paddingLeft) || 0) + (parseFloat(style.paddingRight) || 0);
            const verticalPadding = (parseFloat(style.paddingTop) || 0) + (parseFloat(style.paddingBottom) || 0);
            const availableWidth = Math.max(260, stage.clientWidth - horizontalPadding - 8);
            const availableHeight = Math.max(280, stage.clientHeight - verticalPadding - 8);
            scale = Math.min(1, availableWidth / 595, availableHeight / 842);
            scale = Math.max(.34, scale);
        }

        sheet.style.zoom = String(scale);
        sheet.dataset.proofZoom = state.proofZoom;
        all('[data-cover-proof-zoom]').forEach(button => {
            const active = button.dataset.coverProofZoom === state.proofZoom;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });

        if (resetScroll) {
            window.requestAnimationFrame(() => {
                const horizontal = Math.max(0, (stage.scrollWidth - stage.clientWidth) / 2);
                stage.scrollTo({ top: 0, left: horizontal, behavior: 'auto' });
            });
        }
    }

    function resetProofViewport() {
        state.proofZoom = 'fit';
        applyProofZoom(true);
    }

    function preferredTitleSize(surface, template) {
        const source = surface === 'front' ? typographyPolicy.frontTitleSizes : typographyPolicy.backTitleSizes;
        return Number(source?.[template]) || (surface === 'front' ? 34 : 25);
    }

    function resolveTitleSize(title, preferred) {
        const length = clean(title).replace(/\s+/g, ' ').length;
        let reduction = 0;
        if (length > Number(typographyPolicy.titleLongLength || 95)) reduction = 6;
        else if (length > Number(typographyPolicy.titleMediumLength || 70)) reduction = 4;
        else if (length > Number(typographyPolicy.titleSoftLength || 42)) reduction = 2;
        return Math.max(Number(typographyPolicy.minimumTitleSize || 21), preferred - reduction);
    }

    function resolveSubtitleSize(subtitle) {
        const length = clean(subtitle).replace(/\s+/g, ' ').length;
        let reduction = 0;
        if (length > Number(typographyPolicy.subtitleLongLength || 145)) reduction = 1.5;
        else if (length > Number(typographyPolicy.subtitleSoftLength || 105)) reduction = 1;
        return Math.max(Number(typographyPolicy.minimumSubtitleSize || 12.5), 14 - reduction);
    }

    function updateIdentityAdvisory(title, subtitle) {
        const node = by('[data-cover-title-advisory]');
        if (!node) return;
        const dense = clean(title).replace(/\s+/g, ' ').length > Number(typographyPolicy.titleLongLength || 95)
            || clean(subtitle).replace(/\s+/g, ' ').length > Number(typographyPolicy.subtitleLongLength || 145);
        node.hidden = !dense;
        node.textContent = dense
            ? 'Dense cover wording: PRISM has reduced cover typography within the approved range. Review the proof before final issue.'
            : '';
    }

    function renderProof() {
        const surface = state.proofSurface;
        const front = surface === 'front';
        const template = currentTemplate(surface);
        const sheet = by('[data-cover-proof-sheet]');
        const content = by('[data-cover-proof-content]');
        if (!sheet || !content) return;
        sheet.dataset.template = template;
        sheet.dataset.surface = surface;
        renderCoverPattern(surface, template, sheet);
        by('[data-cover-proof-title]').textContent = front ? 'Front cover proof' : 'Back cover proof';
        all('[data-cover-proof-surface]').forEach(button => button.classList.toggle('active', button.dataset.coverProofSurface === surface));

        const leftLogo = by('[data-cover-proof-left-logo]');
        const rightLogo = by('[data-cover-proof-right-logo]');
        const logoBand = leftLogo?.closest('.compendium-cover-proof-logos');
        leftLogo.hidden = !state.design[`show${front ? 'Front' : 'Back'}LeftLogo`];
        rightLogo.hidden = !state.design[`show${front ? 'Front' : 'Back'}RightLogo`];
        logoBand?.classList.toggle('is-centred', (state.design[`${surfacePrefix(surface)}LogoPlacement`] || 'TopCorners') === 'TopCenter');

        const title = currentShow(surface, 'Title') ? currentText(surface, 'Title') : '';
        const subtitle = currentShow(surface, 'Subtitle') ? currentText(surface, 'Subtitle') : '';
        const edition = currentShow(surface, 'Edition') ? currentText(surface, 'Edition') : '';
        const eyebrow = clean(state.design[`${surfacePrefix(surface)}Eyebrow`]);
        const slots = templateSlots(surface).map(key => ensureSlot(surface, key));
        const tile = (slot, cls = '') => {
            if (!slot) return '';
            const src = slot.previewUrl;
            if (!src) return `<div class="cover-proof-image ${cls} cover-proof-image--empty"><i class="bi bi-image"></i><span>${slot.imageMode === 'None' ? 'No imagery' : 'Automatic imagery'}</span></div>`;
            const resolved = slot.imageMode === 'Automatic'
                ? state.autoResolved.get(automaticSlotKey(surface, slot.slotKey))
                : null;
            const focalX = resolved?.focalX ?? slot.focalX;
            const focalY = resolved?.focalY ?? slot.focalY;
            return `<div class="cover-proof-image ${cls}"><img src="${escapeHtml(src)}" alt="" style="object-fit:${slot.fitMode === 'Fit' ? 'contain' : 'cover'};object-position:${focalX * 100}% ${focalY * 100}%" /></div>`;
        };
        const titleSize = resolveTitleSize(title, preferredTitleSize(surface, template));
        const subtitleSize = resolveSubtitleSize(subtitle);
        const identity = `<div class="cover-proof-identity">${eyebrow ? `<small>${escapeHtml(eyebrow)}</small>` : ''}${title ? `<h3 style="font-size:${titleSize}px">${escapeHtml(title)}</h3>` : ''}${subtitle ? `<p style="font-size:${subtitleSize}px">${escapeHtml(subtitle)}</p>` : ''}${edition ? `<b>${escapeHtml(edition)}</b>` : ''}</div>`;
        updateIdentityAdvisory(title, subtitle);

        if (front) {
            switch (template) {
                case 'FullBleedHero': content.innerHTML = `${tile(slots[0], 'cover-proof-image--full')}${identity}`; break;
                case 'EditorialSplit': {
                    const visible = slots.filter(slot => slot.previewUrl);
                    const rendered = visible.length ? visible : slots;
                    content.innerHTML = `${identity}<div class="cover-proof-split ${rendered.length === 1 ? 'is-single' : ''}">${rendered.map(slot => tile(slot)).join('')}</div>`;
                    break;
                }
                case 'Triptych': {
                    const visible = slots.filter(slot => slot.previewUrl);
                    const rendered = visible.length ? visible : slots;
                    content.innerHTML = `${identity}<div class="cover-proof-triptych is-${rendered.length}">${rendered.map(slot => tile(slot)).join('')}</div>`;
                    break;
                }
                case 'PortfolioQuartet': content.innerHTML = `${identity}<div class="cover-proof-quartet">${tile(slots[0], 'cover-proof-quartet__hero')}<div class="cover-proof-quartet__stack">${slots.slice(1).map(slot => tile(slot)).join('')}</div></div>`; break;
                case 'Minimal': content.innerHTML = identity; break;
                default: content.innerHTML = `${identity}${tile(slots[0], 'cover-proof-image--institutional')}`; break;
            }
        } else {
            switch (template) {
                case 'ImageEcho': content.innerHTML = `${tile(slots[0], 'cover-proof-image--echo')}${identity}`; break;
                case 'PortfolioStrip': content.innerHTML = `${identity}<div class="cover-proof-triptych cover-proof-triptych--back">${slots.map(slot => tile(slot)).join('')}</div>`; break;
                case 'Clean': content.innerHTML = identity; break;
                case 'TypographyOnly': content.innerHTML = identity; break;
                default: content.innerHTML = identity; break;
            }
        }
        applyProofZoom(false);
        void hydrateVisibleSlotPreviews(surface);
    }

    async function loadProjectPhotos(projectId, signal = null) {
        const key = Number(projectId);
        if (!key) return [];
        if (state.photoCache.has(key)) return state.photoCache.get(key);
        const url = new URL(boot.photosUrl, window.location.origin);
        url.searchParams.set('projectId', String(key));
        const response = await fetch(url, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            signal: signal
        });
        if (!response.ok) throw new Error((await safeJson(response))?.message || 'Project photography could not be loaded.');
        const data = await response.json();
        const photos = Array.isArray(data.photos) ? data.photos : [];
        state.photoCache.set(key, photos);
        return photos;
    }

    function automaticSlotKey(surface, slotKey) { return `${surface}:${slotKey}`; }

    function cancelPhotoPickerRequest() {
        state.photoPickerRequestVersion += 1;
        state.photoPickerAbortController?.abort();
        state.photoPickerAbortController = null;
    }

    function clearAutomaticResolutions(surface = null) {
        if (surface) state.hydrationVersions.set(surface, (state.hydrationVersions.get(surface) || 0) + 1);
        else ['front', 'back'].forEach(key => state.hydrationVersions.set(key, (state.hydrationVersions.get(key) || 0) + 1));
        state.design.images.forEach(slot => {
            if (slot.imageMode !== 'Automatic') return;
            if (surface && slot.surface.toLowerCase() !== surface) return;
            slot.previewUrl = null;
            slot.sourceWidth = null;
            slot.sourceHeight = null;
        });
        if (surface) {
            Array.from(state.autoResolved.keys()).forEach(key => { if (key.startsWith(`${surface}:`)) state.autoResolved.delete(key); });
        } else state.autoResolved.clear();
    }

    async function ensureAutomaticCandidates() {
        if (!state.automaticCandidatesDirty || !boot.automaticCandidatesUrl) return;
        const form = new FormData();
        form.set('presetId', String(boot.preset?.id || 0));
        form.set('photoPreferencesJson', JSON.stringify(buildPreferenceSavePayload()));
        if (csrf) form.set('__RequestVerificationToken', csrf);
        const response = await fetch(boot.automaticCandidatesUrl, {
            method: 'POST',
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            body: form
        });
        const result = await safeJson(response);
        if (!response.ok) throw new Error(result?.message || 'Automatic cover imagery could not be refreshed.');
        state.automaticCandidates = normaliseAutomaticCandidates(result?.candidates || []);
        state.automaticCandidatesDirty = false;
    }

    function automaticCandidateSequence(surface, usedProjects = new Set(), usedPhotos = new Set()) {
        const candidates = state.automaticCandidates || [];
        const unique = new Map();
        const append = item => {
            const key = `${item.projectId}:${item.photoId}`;
            if (!usedPhotos.has(key) && !unique.has(key)) unique.set(key, item);
        };
        candidates.filter(item => !usedProjects.has(item.projectId)).forEach(append);
        candidates.forEach(append);
        if (!isQuartet(surface)) candidates.forEach(item => {
            const key = `${item.projectId}:${item.photoId}`;
            if (!unique.has(key)) unique.set(key, item);
        });
        return Array.from(unique.values());
    }

    async function hydrateVisibleSlotPreviews(surface = state.activeSurface) {
        const hydrationVersion = (state.hydrationVersions.get(surface) || 0) + 1;
        state.hydrationVersions.set(surface, hydrationVersion);
        const isCurrentHydration = () => state.hydrationVersions.get(surface) === hydrationVersion;
        const slots = templateSlots(surface).map(key => ensureSlot(surface, key));
        let changed = false;
        const usedPhotos = new Set();
        const usedProjects = new Set();

        try {
            await ensureAutomaticCandidates();
        } catch {
            // Preserve the last server-resolved candidate set if refresh fails.
        }
        if (!isCurrentHydration()) return;

        state.design.images.forEach(slot => {
            if (slot.surface.toLowerCase() !== surface) return;
            if (slot.imageMode !== 'Explicit' || !slot.projectId || !slot.photoId) return;
            usedPhotos.add(`${Number(slot.projectId)}:${Number(slot.photoId)}`);
            usedProjects.add(Number(slot.projectId));
        });
        state.autoResolved.forEach((candidate, key) => {
            if (!key.startsWith(`${surface}:`)) return;
            if (!candidate?.projectId || !candidate?.photoId) return;
            usedPhotos.add(`${Number(candidate.projectId)}:${Number(candidate.photoId)}`);
            usedProjects.add(Number(candidate.projectId));
        });

        for (const slot of slots) {
            const key = automaticSlotKey(surface, slot.slotKey);
            if (slot.imageMode === 'None') continue;
            if (slot.imageMode === 'Explicit' && slot.previewUrl) continue;
            if (slot.imageMode === 'Automatic' && slot.previewUrl && state.autoResolved.has(key)) continue;

            try {
                if (slot.imageMode === 'Explicit') {
                    if (!slot.projectId || !slot.photoId) continue;
                    const photos = await loadProjectPhotos(slot.projectId);
                    if (!isCurrentHydration()) return;
                    const photo = photos.find(item => Number(item.photoId) === Number(slot.photoId));
                    if (!photo) continue;
                    slot.previewUrl = photo.previewUrl || photo.thumbnailUrl;
                    slot.sourceWidth = photo.width;
                    slot.sourceHeight = photo.height;
                    changed = true;
                    continue;
                }

                const sequence = automaticCandidateSequence(surface, usedProjects, usedPhotos);
                for (const candidate of sequence) {
                    const photos = await loadProjectPhotos(candidate.projectId);
                    if (!isCurrentHydration()) return;
                    const photo = photos.find(item => Number(item.photoId) === Number(candidate.photoId));
                    if (!photo) continue;

                    slot.previewUrl = photo.previewUrl || photo.thumbnailUrl;
                    slot.sourceWidth = photo.width;
                    slot.sourceHeight = photo.height;
                    const resolved = {
                        projectId: Number(candidate.projectId),
                        photoId: Number(candidate.photoId),
                        focalX: clamp01(candidate.focalX),
                        focalY: clamp01(candidate.focalY),
                        priority: Number(candidate.priority) || 0
                    };
                    state.autoResolved.set(key, resolved);
                    usedProjects.add(resolved.projectId);
                    usedPhotos.add(`${resolved.projectId}:${resolved.photoId}`);
                    changed = true;
                    break;
                }
            } catch {
                // Automatic preview hydration is best-effort; final export applies the same ranked fallback sequence.
            }
        }

        if (changed && isCurrentHydration()) {
            renderProof();
            if (surface === state.activeSurface) renderSlotsWithoutHydration();
            setDirty();
        }
    }

    function renderSlotsWithoutHydration() {
        renderSlots();
    }

    async function openPhotoPicker(slotKey) {
        cancelPhotoPickerRequest();
        const slot = ensureSlot(state.activeSurface, slotKey);
        state.activeSlot = slot;
        const resolved = state.autoResolved.get(automaticSlotKey(state.activeSurface, slotKey));
        const selectedProjectId = slot.projectId || resolved?.projectId || null;
        const slotLabel = portalBy('[data-cover-photo-modal-slot]');
        if (slotLabel) slotLabel.textContent = `${state.activeSurface === 'front' ? 'Front' : 'Back'} · ${slotKey === 'Hero' ? 'Hero image' : slotKey}`;
        const select = portalBy('[data-cover-project-select]');
        if (!select) return;
        select.innerHTML = '<option value="">Select project…</option>' + state.projects.map(project => `<option value="${project.projectId}" ${Number(project.projectId) === Number(selectedProjectId) ? 'selected' : ''}>${escapeHtml(project.projectName)}</option>`).join('');
        const search = portalBy('[data-cover-project-search]');
        if (search) search.value = '';
        applyProjectSearch();
        const grid = portalBy('[data-cover-photo-grid]');
        if (grid) grid.innerHTML = '';
        const photoState = portalBy('[data-cover-photo-state]');
        if (photoState) photoState.textContent = 'Select a project to view its publication photographs.';
        const noneButton = portalBy('[data-cover-slot-none]');
        if (noneButton) {
            const required = strictRequiredSlots(state.activeSurface)
                .some(key => key.toLowerCase() === slotKey.toLowerCase());
            noneButton.disabled = required;
            noneButton.title = required ? 'This image slot is required by the selected cover template.' : '';
        }
        modal('compendiumCoverPhotoModal')?.show();
        const selectedProjectExists = selectedProjectId
            && Array.from(select.options).some(option => Number(option.value) === Number(selectedProjectId));
        if (selectedProjectExists) await renderProjectPhotos(selectedProjectId);
        else if (selectedProjectId && photoState) {
            photoState.textContent = 'The project used by this saved cover image is no longer selected. Choose a current project photograph.';
        }
    }

    async function renderProjectPhotos(projectId) {
        const grid = portalBy('[data-cover-photo-grid]');
        const stateNode = portalBy('[data-cover-photo-state]');
        if (!grid || !stateNode) return;

        const requestedProjectId = coverState.positiveId(projectId);
        state.photoPickerAbortController?.abort();
        const requestVersion = ++state.photoPickerRequestVersion;
        const controller = typeof AbortController === 'function' ? new AbortController() : null;
        state.photoPickerAbortController = controller;
        const isCurrentRequest = () => coverState.shouldCommitPhotoRequest(
            requestVersion,
            state.photoPickerRequestVersion,
            requestedProjectId,
            portalBy('[data-cover-project-select]')?.value);

        grid.innerHTML = '';
        if (!requestedProjectId) {
            state.photoPickerAbortController = null;
            stateNode.textContent = 'Select a project to view its publication photographs.';
            return;
        }
        stateNode.textContent = 'Loading project photography…';
        try {
            const photos = await loadProjectPhotos(requestedProjectId, controller?.signal);
            if (!isCurrentRequest()) return;
            if (!photos.length) {
                stateNode.textContent = 'No usable publication photographs are recorded for this project.';
                return;
            }
            stateNode.textContent = `${photos.length} photograph${photos.length === 1 ? '' : 's'} available`;
            photos.forEach(photo => {
                const pref = preferenceFor(requestedProjectId, photo.photoId);
                const card = document.createElement('article');
                card.className = 'compendium-cover-photo-card';
                const selected = state.activeSlot?.imageMode === 'Explicit' && Number(state.activeSlot?.projectId) === requestedProjectId && Number(state.activeSlot?.photoId) === Number(photo.photoId);
                if (selected) card.classList.add('selected');
                card.innerHTML = `
                    <button type="button" class="compendium-cover-photo-select" data-cover-select-photo="${photo.photoId}">
                        <img src="${escapeHtml(photo.thumbnailUrl || photo.previewUrl || '')}" alt="" />
                        <span><b>${escapeHtml(photo.caption || `Photo ${photo.photoId}`)}</b><small>${photo.width} × ${photo.height} · ${titleCase(photo.quality)}</small></span>
                        ${selected ? '<i class="bi bi-check-circle-fill"></i>' : ''}
                    </button>
                    <div class="compendium-cover-photo-flags">
                        <label title="Prefer this image when PRISM fills automatic cover slots"><input type="checkbox" data-cover-pref="preferred" data-photo-id="${photo.photoId}" ${pref.preferredForPublication ? 'checked' : ''}/> Cover preferred</label>
                        <label title="Allow Automatic Hero to prioritise this image for the cover"><input type="checkbox" data-cover-pref="hero" data-photo-id="${photo.photoId}" ${pref.suitableForCoverHero ? 'checked' : ''}/> Cover suitable</label>
                    </div>`;
                card.querySelector('[data-cover-select-photo]').addEventListener('click', () => choosePhoto(requestedProjectId, photo));
                card.querySelectorAll('[data-cover-pref]').forEach(input => input.addEventListener('change', () => updatePreference(requestedProjectId, photo.photoId, input.dataset.coverPref, input.checked)));
                grid.appendChild(card);
            });
        } catch (error) {
            if (error?.name === 'AbortError' || !isCurrentRequest()) return;
            stateNode.textContent = error?.message || 'Project photography could not be loaded.';
        } finally {
            if (requestVersion === state.photoPickerRequestVersion) state.photoPickerAbortController = null;
        }
    }

    function preferenceFor(projectId, photoId) {
        return state.preferences.find(item => Number(item.projectId) === Number(projectId) && Number(item.photoId) === Number(photoId)) || { projectId: Number(projectId), photoId: Number(photoId), preferredForPublication: false, suitableForCoverHero: false };
    }

    function updatePreference(projectId, photoId, kind, checked) {
        let item = state.preferences.find(pref => Number(pref.projectId) === Number(projectId) && Number(pref.photoId) === Number(photoId));
        if (!item) {
            item = { projectId: Number(projectId), photoId: Number(photoId), preferredForPublication: false, suitableForCoverHero: false };
            state.preferences.push(item);
        }
        if (kind === 'hero') item.suitableForCoverHero = checked;
        else item.preferredForPublication = checked;
        if (!item.preferredForPublication && !item.suitableForCoverHero) state.preferences = state.preferences.filter(pref => pref !== item);
        state.automaticCandidatesDirty = true;
        clearAutomaticResolutions();
        setDirty();
        renderSlots();
        renderProof();
    }

    function choosePhoto(projectId, photo) {
        const slot = state.activeSlot;
        if (!slot) return;
        const surface = slot.surface.toLowerCase();
        state.autoResolved.delete(automaticSlotKey(surface, slot.slotKey));
        coverState.applyExplicitPhoto(slot, projectId, photo);
        // Explicit-photo uniqueness and automatic ranking are defined within one cover surface.
        // Do not erase the independently curated automatic preview on the opposite cover.
        clearAutomaticResolutions(surface);
        cancelPhotoPickerRequest();
        setDirty();
        renderSlots();
        renderProof();
        modal('compendiumCoverPhotoModal')?.hide();
    }

    function setSlotMode(mode) {
        if (!state.activeSlot) return;
        const surface = state.activeSlot.surface.toLowerCase();
        state.activeSlot.imageMode = mode;
        state.autoResolved.delete(automaticSlotKey(surface, state.activeSlot.slotKey));
        if (mode !== 'Explicit') {
            state.activeSlot.projectId = null;
            state.activeSlot.photoId = null;
            state.activeSlot.previewUrl = null;
        }
        clearAutomaticResolutions(surface);
        cancelPhotoPickerRequest();
        setDirty();
        renderSlots();
        renderProof();
        modal('compendiumCoverPhotoModal')?.hide();
    }

    function applyProjectSearch() {
        const term = clean(portalBy('[data-cover-project-search]')?.value).toLowerCase();
        const select = portalBy('[data-cover-project-select]');
        if (!select) return;
        Array.from(select.options).forEach((option, index) => { if (index) option.hidden = !!term && !option.text.toLowerCase().includes(term); });
    }

    async function pinResolvedAutomaticSlot(slot) {
        if (slot.imageMode === 'Explicit') return true;
        if (slot.imageMode !== 'Automatic') return false;

        const key = automaticSlotKey(state.activeSurface, slot.slotKey);
        if (!state.autoResolved.has(key) || !slot.previewUrl) {
            await hydrateVisibleSlotPreviews(state.activeSurface);
        }

        const resolved = state.autoResolved.get(key);
        if (!resolved?.projectId || !resolved?.photoId || !slot.previewUrl) return false;

        let photo = null;
        try {
            const photos = await loadProjectPhotos(resolved.projectId);
            photo = photos.find(item => Number(item.photoId) === Number(resolved.photoId)) || null;
        } catch {
            // The already resolved preview is sufficient to preserve the publisher's crop intent.
        }

        slot.imageMode = 'Explicit';
        slot.projectId = Number(resolved.projectId);
        slot.photoId = Number(resolved.photoId);
        slot.focalX = clamp01(resolved.focalX);
        slot.focalY = clamp01(resolved.focalY);
        slot.previewUrl = photo?.previewUrl || photo?.thumbnailUrl || slot.previewUrl;
        slot.sourceWidth = photo?.width || slot.sourceWidth;
        slot.sourceHeight = photo?.height || slot.sourceHeight;
        state.autoResolved.delete(key);
        setDirty();
        renderSlots();
        renderProof();
        return true;
    }

    async function openCrop(slotKey) {
        const slot = ensureSlot(state.activeSurface, slotKey);
        if (slot.fitMode === 'Fit' || slot.imageMode === 'None') return;
        if (slot.imageMode === 'Automatic' && !(await pinResolvedAutomaticSlot(slot))) return;
        if (slot.imageMode !== 'Explicit' || !slot.previewUrl) return;

        state.activeSlot = slot;
        const image = portalBy('[data-cover-crop-image]');
        if (!image) return;
        image.src = slot.previewUrl;
        positionCropTarget();
        modal('compendiumCoverCropModal')?.show();
    }

    function positionCropTarget() {
        const slot = state.activeSlot;
        const target = portalBy('[data-cover-crop-target]');
        if (!slot || !target) return;
        target.style.left = `${slot.focalX * 100}%`;
        target.style.top = `${slot.focalY * 100}%`;
    }

    async function save() {
        if (!boot.canManage || !state.dirty) return true;
        if (!coverReadyForSave()) {
            window.alert('The selected cover template has required image slots that are not currently resolvable. Restore or choose the required imagery before saving.');
            return false;
        }
        const button = by('[data-cover-save]');
        if (button) button.disabled = true;
        try {
            const form = new FormData();
            form.set('presetId', String(boot.preset?.id || 0));
            form.set('rowVersion', state.rowVersion || '');
            form.set('coverJson', JSON.stringify(buildCoverSavePayload()));
            form.set('photoPreferencesJson', JSON.stringify(buildPreferenceSavePayload()));
            if (csrf) form.set('__RequestVerificationToken', csrf);
            const response = await fetch(boot.saveUrl, {
                method: 'POST',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                body: form
            });
            const result = await safeJson(response);
            if (!response.ok) throw new Error(result?.message || 'Cover design could not be saved.');
            state.rowVersion = result?.preset?.rowVersion || state.rowVersion;
            if (result?.coverDesign) {
                state.design = normaliseDesign(result.coverDesign);
                state.activeSlot = null;
                state.overrideEditing.clear();
                clearAutomaticResolutions();
                state.automaticCandidatesDirty = true;
            }
            if (Array.isArray(result?.photoPreferences)) state.preferences = normalisePreferences(result.photoPreferences);
            savedSignature = persistedSignature();
            state.dirty = false;
            // Rehydrate from the server-returned canonical state. The save response intentionally
            // excludes transient preview URLs, so retaining the pre-save DOM would display stale
            // images/crops even though the persisted slot identities are already correct.
            await hydrateVisibleSlotPreviews('front');
            await hydrateVisibleSlotPreviews('back');
            updateInspector();
            setDirty();
            return true;
        } catch (error) {
            window.alert(error?.message || 'Cover design could not be saved.');
            setDirty();
            return false;
        }
    }

    async function safeJson(response) { try { return await response.json(); } catch { return null; } }
    function goBack() {
        state.navigatingAway = true;
        window.location.href = boot.returnUrl || `/Projects/Publications/Compendium?presetId=${encodeURIComponent(boot.preset?.id || '')}#compendium-settings`;
    }

    function escapeHtml(value) {
        return String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character]);
    }

    all('[data-cover-surface]').forEach(button => button.addEventListener('click', () => {
        state.activeSurface = button.dataset.coverSurface;
        state.proofSurface = state.activeSurface;
        updateInspector();
        resetProofViewport();
    }));
    all('[data-cover-proof-surface]').forEach(button => button.addEventListener('click', () => {
        state.proofSurface = button.dataset.coverProofSurface;
        renderProof();
        resetProofViewport();
    }));
    all('[data-cover-proof-zoom]').forEach(button => button.addEventListener('click', () => {
        state.proofZoom = button.dataset.coverProofZoom || 'fit';
        applyProofZoom(false);
    }));
    all('[data-cover-template]').forEach(button => button.addEventListener('click', () => {
        if (button.disabled) return;
        if (isFront()) state.design.frontTemplate = button.dataset.coverTemplate;
        else {
            const previous = state.design.backTemplate;
            const next = button.dataset.coverTemplate;
            if (previous !== 'Clean' && next === 'Clean') {
                state.standardBackVisibility = captureBackVisibility();
                state.design.backTemplate = next;
                if (state.cleanBackVisibility) applyBackVisibility(state.cleanBackVisibility);
                else applyBackVisibility({ showBackTitle: false, showBackSubtitle: false, showBackEdition: false, showBackLeftLogo: true, showBackRightLogo: true, backLogoPlacement: state.design.backLogoPlacement || 'TopCorners', backEyebrow: '' });
            } else if (previous === 'Clean' && next !== 'Clean') {
                state.cleanBackVisibility = captureBackVisibility();
                state.design.backTemplate = next;
                applyBackVisibility(state.standardBackVisibility);
            } else state.design.backTemplate = next;
        }
        templateSlots().forEach(key => {
            const slot = ensureSlot(state.activeSurface, key);
            if (isFillOnlyTemplate()) slot.fitMode = 'Fill';
        });
        clearAutomaticResolutions(state.activeSurface);
        setDirty(); updateInspector(); resetProofViewport();
    }));
    all('[data-cover-theme]').forEach(button => button.addEventListener('click', () => {
        state.design.publicationTheme = button.dataset.coverTheme || 'InstitutionalGreen';
        if (!backgroundAllowed(state.design.publicationTheme, state.design.backgroundTreatment)) {
            state.design.backgroundTreatment = 'Solid';
        }
        setDirty(); updateInspector();
    }));
    all('[data-cover-background]').forEach(button => button.addEventListener('click', () => {
        const treatment = button.dataset.coverBackground || 'Solid';
        if (button.disabled || !backgroundAllowed(state.design.publicationTheme, treatment)) return;
        state.design.backgroundTreatment = treatment;
        setDirty(); updateInspector();
    }));

    all('[data-cover-override]').forEach(button => button.addEventListener('click', () => {
        const field = button.dataset.coverOverride;
        const key = overrideKey(state.activeSurface, field);
        state.overrideEditing.add(key);
        updateInspector();
        const input = by(`[data-cover-text="${field}"]`);
        input?.focus();
        input?.select();
    }));
    all('[data-cover-reset]').forEach(button => button.addEventListener('click', () => {
        const field = button.dataset.coverReset;
        const fieldName = titleCase(field);
        state.design[`${surfacePrefix()}${fieldName}`] = '';
        state.overrideEditing.delete(overrideKey(state.activeSurface, field));
        setDirty();
        updateInspector();
    }));

    all('[data-cover-text]').forEach(input => input.addEventListener('input', () => {
        const field = titleCase(input.dataset.coverText);
        state.design[`${surfacePrefix()}${field}`] = input.value;
        if (field !== 'Eyebrow') {
            const stateLabel = by(`[data-cover-field-state="${input.dataset.coverText}"]`);
            if (stateLabel) {
                const hasOverride = !!clean(input.value);
                stateLabel.textContent = hasOverride ? 'Override' : 'New override';
                stateLabel.classList.add('is-override');
            }
        }
        setDirty(); renderProof();
    }));
    all('[data-cover-show]').forEach(input => input.addEventListener('change', () => {
        const field = titleCase(input.dataset.coverShow);
        if (field === 'Eyebrow') {
            if (!input.checked) state.design[`${surfacePrefix()}Eyebrow`] = '';
        } else state.design[`show${isFront() ? 'Front' : 'Back'}${field}`] = input.checked;
        setDirty(); updateInspector();
    }));
    all('[data-cover-logo]').forEach(input => input.addEventListener('change', () => {
        state.design[`show${isFront() ? 'Front' : 'Back'}${titleCase(input.dataset.coverLogo)}Logo`] = input.checked;
        setDirty(); renderProof();
    }));
    by('[data-cover-logo-placement]')?.addEventListener('change', event => {
        state.design[`${surfacePrefix()}LogoPlacement`] = event.target.value || 'TopCorners';
        setDirty(); renderProof();
    });

    by('[data-cover-slot-list]')?.addEventListener('click', event => {
        const choose = event.target.closest('[data-cover-choose-slot]');
        if (choose) { void openPhotoPicker(choose.dataset.coverChooseSlot); return; }
        const fit = event.target.closest('[data-cover-fit]');
        if (fit) {
            const slot = ensureSlot(state.activeSurface, fit.dataset.coverSlot);
            if (isFillOnlyTemplate() && fit.dataset.coverFit === 'Fit') return;
            slot.fitMode = isFillOnlyTemplate() ? 'Fill' : fit.dataset.coverFit;
            setDirty(); renderSlots(); renderProof(); return;
        }
        const crop = event.target.closest('[data-cover-crop-slot]');
        if (crop) void openCrop(crop.dataset.coverCropSlot);
    });

    portalBy('[data-cover-project-select]')?.addEventListener('change', event => {
        void renderProjectPhotos(Number(event.target.value) || null);
    });
    portalBy('[data-cover-project-search]')?.addEventListener('input', applyProjectSearch);
    portalBy('[data-cover-slot-auto]')?.addEventListener('click', () => setSlotMode('Automatic'));
    portalBy('[data-cover-slot-none]')?.addEventListener('click', event => { if (!event.currentTarget.disabled) setSlotMode('None'); });
    document.getElementById('compendiumCoverPhotoModal')?.addEventListener('hidden.bs.modal', cancelPhotoPickerRequest);

    portalBy('[data-cover-crop-stage]')?.addEventListener('click', event => {
        if (!state.activeSlot) return;
        const rect = event.currentTarget.getBoundingClientRect();
        state.activeSlot.focalX = clamp01((event.clientX - rect.left) / rect.width);
        state.activeSlot.focalY = clamp01((event.clientY - rect.top) / rect.height);
        positionCropTarget(); setDirty(); renderSlots(); renderProof();
    });
    portalBy('[data-cover-crop-centre]')?.addEventListener('click', () => {
        if (!state.activeSlot) return;
        state.activeSlot.focalX = .5; state.activeSlot.focalY = .5; positionCropTarget(); setDirty(); renderSlots(); renderProof();
    });

    by('[data-cover-save]')?.addEventListener('click', () => void save());
    by('[data-cover-back]')?.addEventListener('click', event => {
        event.preventDefault();
        if (!state.dirty) { goBack(); return; }
        modal('compendiumCoverLeaveModal')?.show();
    });
    portalBy('[data-cover-return-unsaved]')?.addEventListener('click', () => {
        modal('compendiumCoverLeaveModal')?.hide();
        goBack();
    });
    portalBy('[data-cover-save-return]')?.addEventListener('click', async () => {
        if (await save()) {
            modal('compendiumCoverLeaveModal')?.hide();
            goBack();
        }
    });
    window.addEventListener('beforeunload', event => {
        if (state.navigatingAway || !state.dirty) return;
        event.preventDefault();
        event.returnValue = '';
    });

    const proofStage = by('.compendium-cover-proof-stage');
    if (proofStage && 'ResizeObserver' in window) {
        const proofResizeObserver = new ResizeObserver(() => {
            if (state.proofZoom === 'fit') applyProofZoom(false);
        });
        proofResizeObserver.observe(proofStage);
    } else {
        window.addEventListener('resize', () => { if (state.proofZoom === 'fit') applyProofZoom(false); });
    }

    function syncCoverWorkspaceViewport() {
        const top = Math.max(0, root.getBoundingClientRect().top);
        root.style.setProperty('--cover-editor-viewport-top', `${top}px`);
    }

    syncCoverWorkspaceViewport();
    window.addEventListener('resize', syncCoverWorkspaceViewport);

    updateInspector();
    setDirty();
    resetProofViewport();
})();
