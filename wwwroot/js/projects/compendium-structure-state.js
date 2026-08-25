(() => {
    "use strict";

    const VERSION = 5;
    const PREFIX = "prism:compendium:structure:";
    const MAX_AGE_MS = 4 * 60 * 60 * 1000;

    const asNumber = value => Number.isFinite(Number(value)) ? Number(value) : 0;
    const clamp = value => Number.isFinite(Number(value)) ? Math.max(0, Math.min(1, Number(value))) : .5;
    const cleanKey = value => String(value ?? "").trim().replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 40);
    const cleanName = value => String(value ?? "").trim().replace(/\s+/g, " ").slice(0, 120);
    const cleanText = (value, maximumLength) => String(value ?? "").trim().replace(/\s+/g, " ").slice(0, maximumLength);
    const normalize = value => String(value ?? "").trim().toLowerCase();
    const normalizeLayout = value => ({
        automatic: "Automatic",
        visualhero: "VisualHero",
        balanced: "Balanced",
        multiimageeditorial: "MultiImageEditorial",
        technical: "Technical"
    }[normalize(value)] || "Automatic");
    const normalizeFlow = value => normalize(value) === "sidecolumn" ? "SideColumn" : "FlowBelowImage";
    const normalizeFit = value => normalize(value) === "fit" ? "fit" : "fill";
    const normalizeNullableLayout = value => value == null || normalize(value) === "" ? null : normalizeLayout(value);
    const normalizeNullableFlow = value => value == null || normalize(value) === "" ? null : normalizeFlow(value);
    const normalizeNullableFit = value => value == null || normalize(value) === "" ? null : normalizeFit(value);

    const storageKey = presetId => `${PREFIX}${asNumber(presetId)}`;

    const sanitizePublication = source => {
        if (!source || typeof source !== "object") return null;
        return {
            title: cleanText(source.title, 120),
            subtitle: cleanText(source.subtitle, 160),
            edition: cleanText(source.edition, 80),
            handlingMarking: cleanText(source.handlingMarking, 80)
        };
    };

    const sanitizeCoverDesign = source => {
        if (!source || typeof source !== "object") return null;
        const stringValue = (value, fallback = "") => String(value ?? fallback).trim().slice(0, 80);
        const optionalText = (value, maximumLength) => {
            const text = cleanText(value, maximumLength);
            return text || null;
        };
        const booleanValue = (value, fallback = true) => typeof value === "boolean" ? value : fallback;
        const images = (Array.isArray(source.images) ? source.images : [])
            .slice(0, 12)
            .map((item, index) => ({
                surface: stringValue(item?.surface, "Front"),
                slotKey: cleanKey(item?.slotKey) || `Slot${index + 1}`,
                imageMode: normalize(item?.imageMode) === "explicit"
                    ? "Explicit"
                    : normalize(item?.imageMode) === "none" ? "None" : "Automatic",
                projectId: asNumber(item?.projectId) > 0 ? asNumber(item.projectId) : null,
                photoId: asNumber(item?.photoId) > 0 ? asNumber(item.photoId) : null,
                focalX: clamp(item?.focalX),
                focalY: clamp(item?.focalY),
                fitMode: normalize(item?.fitMode) === "fit" ? "Fit" : "Fill",
                sortOrder: Number.isFinite(Number(item?.sortOrder)) ? Number(item.sortOrder) : index
            }));

        return {
            frontTemplate: stringValue(source.frontTemplate, "InstitutionalHero"),
            backTemplate: stringValue(source.backTemplate, "MinimalInstitutional"),
            publicationTheme: stringValue(source.publicationTheme, "InstitutionalGreen"),
            backgroundTreatment: stringValue(source.backgroundTreatment, "Solid"),
            frontTitle: optionalText(source.frontTitle, 120),
            frontSubtitle: optionalText(source.frontSubtitle, 160),
            frontEdition: optionalText(source.frontEdition, 80),
            frontEyebrow: optionalText(source.frontEyebrow, 80),
            backTitle: optionalText(source.backTitle, 120),
            backSubtitle: optionalText(source.backSubtitle, 160),
            backEdition: optionalText(source.backEdition, 80),
            backEyebrow: optionalText(source.backEyebrow, 80),
            showFrontTitle: booleanValue(source.showFrontTitle),
            showFrontSubtitle: booleanValue(source.showFrontSubtitle),
            showFrontEdition: booleanValue(source.showFrontEdition),
            showFrontLeftLogo: booleanValue(source.showFrontLeftLogo),
            showFrontRightLogo: booleanValue(source.showFrontRightLogo),
            frontLogoPlacement: stringValue(source.frontLogoPlacement, "TopCorners"),
            showBackTitle: booleanValue(source.showBackTitle),
            showBackSubtitle: booleanValue(source.showBackSubtitle),
            showBackEdition: booleanValue(source.showBackEdition),
            showBackLeftLogo: booleanValue(source.showBackLeftLogo),
            showBackRightLogo: booleanValue(source.showBackRightLogo),
            backLogoPlacement: stringValue(source.backLogoPlacement, "TopCorners"),
            images
        };
    };

    const sanitizePhotoPreferences = (source, selectedIds) => {
        if (!Array.isArray(source)) return [];
        const selected = new Set(selectedIds);
        const seen = new Set();
        const result = [];
        for (const item of source) {
            const projectId = asNumber(item?.projectId);
            const photoId = asNumber(item?.photoId);
            if (projectId <= 0 || photoId <= 0 || !selected.has(projectId)) continue;
            const key = `${projectId}:${photoId}`;
            if (seen.has(key)) continue;
            seen.add(key);
            const preferredForPublication = item?.preferredForPublication === true;
            const suitableForCoverHero = item?.suitableForCoverHero === true;
            if (!preferredForPublication && !suitableForCoverHero) continue;
            result.push({ projectId, photoId, preferredForPublication, suitableForCoverHero });
            if (result.length >= selectedIds.length * 6) break;
        }
        return result;
    };

    const sanitize = snapshot => {
        if (!snapshot || typeof snapshot !== "object") return null;
        const presetId = asNumber(snapshot.presetId);
        if (presetId <= 0) return null;

        const ids = [];
        const seenIds = new Set();
        (Array.isArray(snapshot.orderedIds) ? snapshot.orderedIds : []).forEach(value => {
            const id = asNumber(value);
            if (id > 0 && !seenIds.has(id)) {
                seenIds.add(id);
                ids.push(id);
            }
        });

        const sections = [];
        const seenKeys = new Set();
        const seenNames = new Set();
        (Array.isArray(snapshot.sections) ? snapshot.sections : [])
            .slice()
            .sort((a, b) => asNumber(a?.sortOrder) - asNumber(b?.sortOrder))
            .forEach(item => {
                const sectionKey = cleanKey(item?.sectionKey);
                const name = cleanName(item?.name);
                if (!sectionKey || !name) return;
                if (seenKeys.has(normalize(sectionKey)) || seenNames.has(normalize(name))) return;
                seenKeys.add(normalize(sectionKey));
                seenNames.add(normalize(name));
                sections.push({ sectionKey, name, sortOrder: sections.length });
            });

        const configs = {};
        if (snapshot.configs && typeof snapshot.configs === "object") {
            ids.forEach(id => {
                const source = snapshot.configs[id] || snapshot.configs[String(id)] || {};
                configs[id] = {
                    primaryPhotoId: asNumber(source.primaryPhotoId) > 0 ? asNumber(source.primaryPhotoId) : null,
                    focalX: clamp(source.focalX),
                    focalY: clamp(source.focalY),
                    imageSelectionMode: normalize(source.imageSelectionMode) === "explicit" ? "explicit" : "automatic",
                    imageFitMode: normalizeFit(source.imageFitMode),
                    imageFitModeOverride: normalizeNullableFit(source.imageFitModeOverride),
                    dossierLayout: normalizeLayout(source.dossierLayout),
                    dossierLayoutOverride: normalizeNullableLayout(source.dossierLayoutOverride),
                    balancedTextFlowMode: normalizeFlow(source.balancedTextFlowMode),
                    balancedTextFlowModeOverride: normalizeNullableFlow(source.balancedTextFlowModeOverride),
                    dossierImageCount: Math.max(1, Math.min(3, asNumber(source.dossierImageCount) || 1)),
                    supportingPhoto1Id: asNumber(source.supportingPhoto1Id) > 0 ? asNumber(source.supportingPhoto1Id) : null,
                    supportingPhoto1FocalX: clamp(source.supportingPhoto1FocalX),
                    supportingPhoto1FocalY: clamp(source.supportingPhoto1FocalY),
                    supportingPhoto1FitMode: normalize(source.supportingPhoto1FitMode) === "fit" ? "fit" : "fill",
                    supportingPhoto2Id: asNumber(source.supportingPhoto2Id) > 0 ? asNumber(source.supportingPhoto2Id) : null,
                    supportingPhoto2FocalX: clamp(source.supportingPhoto2FocalX),
                    supportingPhoto2FocalY: clamp(source.supportingPhoto2FocalY),
                    supportingPhoto2FitMode: normalize(source.supportingPhoto2FitMode) === "fit" ? "fit" : "fill",
                    reviewFingerprint: String(source.reviewFingerprint || "").trim() || null,
                    customSectionKey: cleanKey(source.customSectionKey) || null,
                    customSectionName: cleanName(source.customSectionName) || null,
                    narrativeSourceOverride: source.narrativeSourceOverride ? String(source.narrativeSourceOverride) : null,
                    narrativeAlignmentOverride: normalize(source.narrativeAlignmentOverride) === "justified" ? "Justified" : normalize(source.narrativeAlignmentOverride) === "left" ? "Left" : null,
                    additionalNote: String(source.additionalNote || "").trim() || null,
                    additionalNoteSpecified: source.additionalNoteSpecified === true
                        || Object.prototype.hasOwnProperty.call(source, "additionalNote")
                };
            });
        }

        const projectStates = {};
        if (snapshot.projectStates && typeof snapshot.projectStates === "object") {
            ids.forEach(id => {
                const source = snapshot.projectStates[id] || snapshot.projectStates[String(id)] || null;
                if (!source || typeof source !== "object") return;
                projectStates[id] = {
                    isReviewed: Boolean(source.isReviewed),
                    isReviewStale: Boolean(source.isReviewStale),
                    severity: String(source.severity || "").toLowerCase(),
                    warningCount: Math.max(0, asNumber(source.warningCount)),
                    blockerCount: Math.max(0, asNumber(source.blockerCount))
                };
            });
        }

        return {
            version: VERSION,
            presetId,
            rowVersion: String(snapshot.rowVersion || ""),
            updatedAt: asNumber(snapshot.updatedAt) || Date.now(),
            source: String(snapshot.source || "compendium"),
            returnUrl: String(snapshot.returnUrl || ""),
            persisted: snapshot.persisted !== false,
            publication: sanitizePublication(snapshot.publication),
            coverDesign: sanitizeCoverDesign(snapshot.coverDesign),
            photoPreferences: sanitizePhotoPreferences(snapshot.photoPreferences, ids),
            editorialState: snapshot.editorialState && typeof snapshot.editorialState === "object"
                ? {
                    narrativeSource: String(snapshot.editorialState.narrativeSource || "ProjectBrief"),
                    narrativeAlignment: normalize(snapshot.editorialState.narrativeAlignment) === "justified" ? "Justified" : "Left",
                    defaultDossierLayout: normalizeLayout(snapshot.editorialState.defaultDossierLayout),
                    defaultBalancedTextFlowMode: normalizeFlow(snapshot.editorialState.defaultBalancedTextFlowMode),
                    defaultImageFitMode: normalizeFit(snapshot.editorialState.defaultImageFitMode),
                    projectParticularsStyle: normalize(snapshot.editorialState.projectParticularsStyle) === "minimal" ? "Minimal" : "Panel",
                    groupingMode: String(snapshot.editorialState.groupingMode || "TechnicalCategory"),
                    sortMode: String(snapshot.editorialState.sortMode || "Manual")
                }
                : null,
            orderedIds: ids,
            sections,
            configs,
            projectStates
        };
    };

    const write = snapshot => {
        const clean = sanitize(snapshot);
        if (!clean || !globalThis.sessionStorage) return false;
        try {
            clean.updatedAt = Date.now();
            sessionStorage.setItem(storageKey(clean.presetId), JSON.stringify(clean));
            return true;
        } catch {
            return false;
        }
    };

    const read = (presetId, maxAgeMs = MAX_AGE_MS) => {
        if (!globalThis.sessionStorage) return null;
        try {
            const raw = sessionStorage.getItem(storageKey(presetId));
            if (!raw) return null;
            const clean = sanitize(JSON.parse(raw));
            if (!clean || clean.presetId !== asNumber(presetId)) return null;
            if (Date.now() - clean.updatedAt > Math.max(60_000, asNumber(maxAgeMs) || MAX_AGE_MS)) {
                sessionStorage.removeItem(storageKey(presetId));
                return null;
            }
            return clean;
        } catch {
            return null;
        }
    };

    const clear = presetId => {
        try { sessionStorage?.removeItem(storageKey(presetId)); }
        catch { /* best effort */ }
    };

    globalThis.PrismCompendiumStructure = Object.freeze({
        version: VERSION,
        maxAgeMs: MAX_AGE_MS,
        storageKey,
        sanitize,
        write,
        read,
        clear
    });
})();
