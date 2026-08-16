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
        Clean: 'Clean'
    })[value] || titleCase(value);

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
        hydrationVersions: new Map(),
        overrideEditing: new Set(),
        dirty: false,
        leaveAfterSave: false
    };
    const initialSignature = () => JSON.stringify({ design: state.design, preferences: state.preferences });
    let savedSignature = initialSignature();

    const coverPolicy = boot.coverPolicy || { front: [], back: [] };
    const templatePolicy = (surface, template = null) => {
        const list = surface === 'front' ? coverPolicy.front : coverPolicy.back;
        const name = template || currentTemplate(surface);
        return (Array.isArray(list) ? list : []).find(item => item.template === name) || { slots: [], requiredSlots: [], minimumDistinctImages: 0, fillOnly: false };
    };

    function normaliseDesign(value) {
        return {
            frontTemplate: value.frontTemplate || 'InstitutionalHero',
            backTemplate: value.backTemplate || 'MinimalInstitutional',
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
            const slot = ensureSlot('front', key);
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

    function setDirty() {
        const signature = initialSignature();
        state.dirty = signature !== savedSignature;
        const save = by('[data-cover-save]');
        if (save) save.disabled = !state.dirty || !boot.canManage || !quartetResolved();
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
        renderSlots();
        renderProof();
        updateTemplateLabels();
    }

    function updateTemplateLabels() {
        by('[data-cover-front-template-label]').textContent = templateDisplayName(state.design.frontTemplate);
        by('[data-cover-back-template-label]').textContent = templateDisplayName(state.design.backTemplate);
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
            const preview = slot.previewUrl
                ? `<img src="${escapeHtml(slot.previewUrl)}" alt="" style="object-fit:${slot.fitMode === 'Fit' ? 'contain' : 'cover'};object-position:${slot.focalX * 100}% ${slot.focalY * 100}%" />`
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

    function renderProof() {
        const surface = state.proofSurface;
        const front = surface === 'front';
        const template = currentTemplate(surface);
        const sheet = by('[data-cover-proof-sheet]');
        const content = by('[data-cover-proof-content]');
        if (!sheet || !content) return;
        sheet.dataset.template = template;
        sheet.dataset.surface = surface;
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
            return `<div class="cover-proof-image ${cls}"><img src="${escapeHtml(src)}" alt="" style="object-fit:${slot.fitMode === 'Fit' ? 'contain' : 'cover'};object-position:${slot.focalX * 100}% ${slot.focalY * 100}%" /></div>`;
        };
        const identity = `<div class="cover-proof-identity">${eyebrow ? `<small>${escapeHtml(eyebrow)}</small>` : ''}${title ? `<h3>${escapeHtml(title)}</h3>` : ''}${subtitle ? `<p>${escapeHtml(subtitle)}</p>` : ''}${edition ? `<b>${escapeHtml(edition)}</b>` : ''}</div>`;

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

    async function loadProjectPhotos(projectId) {
        const key = Number(projectId);
        if (!key) return [];
        if (state.photoCache.has(key)) return state.photoCache.get(key);
        const url = new URL(boot.photosUrl, window.location.origin);
        url.searchParams.set('projectId', String(key));
        const response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (!response.ok) throw new Error((await safeJson(response))?.message || 'Project photography could not be loaded.');
        const data = await response.json();
        const photos = Array.isArray(data.photos) ? data.photos : [];
        state.photoCache.set(key, photos);
        return photos;
    }

    function automaticSlotKey(surface, slotKey) { return `${surface}:${slotKey}`; }

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

    async function hydrateVisibleSlotPreviews(surface = state.activeSurface) {
        const hydrationVersion = (state.hydrationVersions.get(surface) || 0) + 1;
        state.hydrationVersions.set(surface, hydrationVersion);
        const isCurrentHydration = () => state.hydrationVersions.get(surface) === hydrationVersion;
        const slots = templateSlots(surface).map(key => ensureSlot(surface, key));
        let changed = false;
        const usedPhotos = new Set();
        const usedProjects = new Set();

        // Explicitly curated imagery always wins. Automatic slots avoid those assets and projects
        // where a different suitable project is available.
        state.design.images.forEach(slot => {
            if (slot.imageMode !== 'Explicit' || !slot.projectId || !slot.photoId) return;
            usedPhotos.add(`${Number(slot.projectId)}:${Number(slot.photoId)}`);
            usedProjects.add(Number(slot.projectId));
        });
        state.autoResolved.forEach(candidate => {
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
                let projectId = slot.projectId;
                let photoId = slot.photoId;
                if (slot.imageMode !== 'Explicit') {
                    const preferred = chooseAutomaticCandidate(surface, usedProjects, usedPhotos);
                    projectId = preferred?.projectId || null;
                    photoId = preferred?.photoId || null;
                }
                if (!projectId) continue;
                const photos = await loadProjectPhotos(projectId);
                if (!isCurrentHydration()) return;
                let photo = photoId ? photos.find(item => Number(item.photoId) === Number(photoId)) : null;
                photo ??= photos.find(item => item.isCover && !usedPhotos.has(`${Number(projectId)}:${Number(item.photoId)}`));
                photo ??= photos.find(item => !usedPhotos.has(`${Number(projectId)}:${Number(item.photoId)}`));
                if (!isQuartet(surface)) photo ??= photos.find(item => item.isCover) || photos[0];
                if (!photo) continue;
                slot.previewUrl = photo.previewUrl || photo.thumbnailUrl;
                slot.sourceWidth = photo.width;
                slot.sourceHeight = photo.height;
                if (slot.imageMode === 'Explicit') {
                    if (!slot.photoId) slot.photoId = photo.photoId;
                } else {
                    const resolved = { projectId: Number(projectId), photoId: Number(photo.photoId) };
                    state.autoResolved.set(key, resolved);
                    usedProjects.add(resolved.projectId);
                    usedPhotos.add(`${resolved.projectId}:${resolved.photoId}`);
                }
                changed = true;
            } catch { /* automatic preview is best-effort */ }
        }
        if (changed && isCurrentHydration()) {
            renderProof();
            if (surface === state.activeSurface) renderSlotsWithoutHydration();
            setDirty();
        }
    }

    function renderSlotsWithoutHydration() {
        const original = hydrateVisibleSlotPreviews;
        // renderSlots will schedule hydration again but populated preview URLs make it a no-op.
        renderSlots();
    }

    function chooseAutomaticCandidate(surface, usedProjects = new Set(), usedPhotos = new Set()) {
        const candidates = [];
        const seen = new Set();
        const add = (candidate, priority) => {
            const projectId = Number(candidate?.projectId);
            const photoId = Number(candidate?.photoId) || null;
            if (!projectId) return;
            const key = `${projectId}:${photoId || 0}`;
            if (seen.has(key)) return;
            seen.add(key);
            candidates.push({ projectId, photoId, priority });
        };
        state.preferences.forEach(item => {
            if (item.suitableForCoverHero) add(item, 500);
            else if (item.preferredForPublication) add(item, 350);
        });
        state.projects.forEach((project, index) => {
            if (project.primaryPhotoId) add({ projectId: project.projectId, photoId: project.primaryPhotoId }, 160 - index / 1000);
            else if (project.photoCount > 0) add({ projectId: project.projectId, photoId: null }, 80 - index / 1000);
        });
        candidates.sort((a, b) => b.priority - a.priority);
        return candidates.find(item => !usedProjects.has(item.projectId) && (!item.photoId || !usedPhotos.has(`${item.projectId}:${item.photoId}`)))
            || candidates.find(item => !item.photoId || !usedPhotos.has(`${item.projectId}:${item.photoId}`))
            || (isQuartet(surface) ? null : candidates[0])
            || null;
    }

    async function openPhotoPicker(slotKey) {
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
            const required = isQuartet(state.activeSurface);
            noneButton.disabled = required;
            noneButton.title = required ? 'Portfolio Quartet requires all four image slots.' : '';
        }
        modal('compendiumCoverPhotoModal')?.show();
        if (selectedProjectId) await renderProjectPhotos(selectedProjectId);
    }

    async function renderProjectPhotos(projectId) {
        const grid = portalBy('[data-cover-photo-grid]');
        const stateNode = portalBy('[data-cover-photo-state]');
        if (!grid || !stateNode) return;
        grid.innerHTML = '';
        stateNode.textContent = 'Loading project photography…';
        try {
            const photos = await loadProjectPhotos(projectId);
            if (!photos.length) {
                stateNode.textContent = 'No usable publication photographs are recorded for this project.';
                return;
            }
            stateNode.textContent = `${photos.length} photograph${photos.length === 1 ? '' : 's'} available`;
            photos.forEach(photo => {
                const pref = preferenceFor(projectId, photo.photoId);
                const card = document.createElement('article');
                card.className = 'compendium-cover-photo-card';
                const selected = state.activeSlot?.imageMode === 'Explicit' && Number(state.activeSlot?.projectId) === Number(projectId) && Number(state.activeSlot?.photoId) === Number(photo.photoId);
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
                card.querySelector('[data-cover-select-photo]').addEventListener('click', () => choosePhoto(projectId, photo));
                card.querySelectorAll('[data-cover-pref]').forEach(input => input.addEventListener('change', () => updatePreference(projectId, photo.photoId, input.dataset.coverPref, input.checked)));
                grid.appendChild(card);
            });
        } catch (error) {
            stateNode.textContent = error?.message || 'Project photography could not be loaded.';
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
        clearAutomaticResolutions();
        setDirty();
        renderSlots();
        renderProof();
    }

    function choosePhoto(projectId, photo) {
        const slot = state.activeSlot;
        if (!slot) return;
        slot.imageMode = 'Explicit';
        state.autoResolved.delete(automaticSlotKey(state.activeSurface, slot.slotKey));
        slot.projectId = Number(projectId);
        slot.photoId = Number(photo.photoId);
        slot.previewUrl = photo.previewUrl || photo.thumbnailUrl;
        slot.sourceWidth = photo.width;
        slot.sourceHeight = photo.height;
        clearAutomaticResolutions();
        setDirty();
        renderSlots();
        renderProof();
        modal('compendiumCoverPhotoModal')?.hide();
    }

    function setSlotMode(mode) {
        if (!state.activeSlot) return;
        state.activeSlot.imageMode = mode;
        state.autoResolved.delete(automaticSlotKey(state.activeSurface, state.activeSlot.slotKey));
        if (mode !== 'Explicit') {
            state.activeSlot.projectId = null;
            state.activeSlot.photoId = null;
            state.activeSlot.previewUrl = null;
        }
        if (mode === 'Automatic') clearAutomaticResolutions();
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
        if (!quartetResolved()) {
            window.alert('Portfolio Quartet requires four distinct usable photographs before it can be saved.');
            return false;
        }
        const button = by('[data-cover-save]');
        if (button) button.disabled = true;
        try {
            const form = new FormData();
            form.set('presetId', String(boot.preset?.id || 0));
            form.set('rowVersion', state.rowVersion || '');
            form.set('coverJson', JSON.stringify(state.design));
            form.set('photoPreferencesJson', JSON.stringify(state.preferences));
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
                state.autoResolved.clear();
            }
            if (Array.isArray(result?.photoPreferences)) state.preferences = normalisePreferences(result.photoPreferences);
            savedSignature = initialSignature();
            state.dirty = false;
            setDirty();
            return true;
        } catch (error) {
            window.alert(error?.message || 'Cover design could not be saved.');
            setDirty();
            return false;
        }
    }

    async function safeJson(response) { try { return await response.json(); } catch { return null; } }
    function goBack() { window.location.href = boot.returnUrl || `/Projects/Publications/Compendium?presetId=${encodeURIComponent(boot.preset?.id || '')}#compendium-settings`; }

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
        else state.design.backTemplate = button.dataset.coverTemplate;
        templateSlots().forEach(key => {
            const slot = ensureSlot(state.activeSurface, key);
            if (isFillOnlyTemplate()) slot.fitMode = 'Fill';
        });
        clearAutomaticResolutions(state.activeSurface);
        setDirty(); updateInspector(); resetProofViewport();
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

    portalBy('[data-cover-project-select]')?.addEventListener('change', event => { if (event.target.value) void renderProjectPhotos(Number(event.target.value)); });
    portalBy('[data-cover-project-search]')?.addEventListener('input', applyProjectSearch);
    portalBy('[data-cover-slot-auto]')?.addEventListener('click', () => setSlotMode('Automatic'));
    portalBy('[data-cover-slot-none]')?.addEventListener('click', event => { if (!event.currentTarget.disabled) setSlotMode('None'); });

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
    portalBy('[data-cover-return-unsaved]')?.addEventListener('click', goBack);
    portalBy('[data-cover-save-return]')?.addEventListener('click', async () => { if (await save()) goBack(); });
    window.addEventListener('beforeunload', event => { if (state.dirty) { event.preventDefault(); event.returnValue = ''; } });

    const proofStage = by('.compendium-cover-proof-stage');
    if (proofStage && 'ResizeObserver' in window) {
        const proofResizeObserver = new ResizeObserver(() => {
            if (state.proofZoom === 'fit') applyProofZoom(false);
        });
        proofResizeObserver.observe(proofStage);
    } else {
        window.addEventListener('resize', () => { if (state.proofZoom === 'fit') applyProofZoom(false); });
    }

    updateInspector();
    setDirty();
    resetProofViewport();
})();
