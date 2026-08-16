(() => {
    "use strict";

    const VERSION = 4;
    const PREFIX = "prism:compendium:structure:";
    const MAX_AGE_MS = 4 * 60 * 60 * 1000;

    const asNumber = value => Number.isFinite(Number(value)) ? Number(value) : 0;
    const cleanKey = value => String(value ?? "").trim().replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 40);
    const cleanName = value => String(value ?? "").trim().replace(/\s+/g, " ").slice(0, 120);
    const normalize = value => String(value ?? "").trim().toLowerCase();

    const storageKey = presetId => `${PREFIX}${asNumber(presetId)}`;

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
                    focalX: Number.isFinite(Number(source.focalX)) ? Math.max(0, Math.min(1, Number(source.focalX))) : .5,
                    focalY: Number.isFinite(Number(source.focalY)) ? Math.max(0, Math.min(1, Number(source.focalY))) : .5,
                    imageSelectionMode: normalize(source.imageSelectionMode) === "explicit" ? "explicit" : "automatic",
                    imageFitMode: normalize(source.imageFitMode) === "fit" ? "fit" : "fill",
                    dossierLayout: ({ automatic:"Automatic", visualhero:"VisualHero", balanced:"Balanced", multiimageeditorial:"MultiImageEditorial", technical:"Technical" }[normalize(source.dossierLayout)] || "Automatic"),
                    balancedTextFlowMode: normalize(source.balancedTextFlowMode) === "sidecolumn" ? "SideColumn" : "FlowBelowImage",
                    dossierImageCount: Math.max(1, Math.min(3, asNumber(source.dossierImageCount) || 1)),
                    supportingPhoto1Id: asNumber(source.supportingPhoto1Id) > 0 ? asNumber(source.supportingPhoto1Id) : null,
                    supportingPhoto1FocalX: Number.isFinite(Number(source.supportingPhoto1FocalX)) ? Math.max(0, Math.min(1, Number(source.supportingPhoto1FocalX))) : .5,
                    supportingPhoto1FocalY: Number.isFinite(Number(source.supportingPhoto1FocalY)) ? Math.max(0, Math.min(1, Number(source.supportingPhoto1FocalY))) : .5,
                    supportingPhoto1FitMode: normalize(source.supportingPhoto1FitMode) === "fit" ? "fit" : "fill",
                    supportingPhoto2Id: asNumber(source.supportingPhoto2Id) > 0 ? asNumber(source.supportingPhoto2Id) : null,
                    supportingPhoto2FocalX: Number.isFinite(Number(source.supportingPhoto2FocalX)) ? Math.max(0, Math.min(1, Number(source.supportingPhoto2FocalX))) : .5,
                    supportingPhoto2FocalY: Number.isFinite(Number(source.supportingPhoto2FocalY)) ? Math.max(0, Math.min(1, Number(source.supportingPhoto2FocalY))) : .5,
                    supportingPhoto2FitMode: normalize(source.supportingPhoto2FitMode) === "fit" ? "fit" : "fill",
                    reviewFingerprint: String(source.reviewFingerprint || "").trim() || null,
                    customSectionKey: cleanKey(source.customSectionKey) || null,
                    customSectionName: cleanName(source.customSectionName) || null,
                    narrativeSourceOverride: source.narrativeSourceOverride ? String(source.narrativeSourceOverride) : null,
                    narrativeAlignmentOverride: normalize(source.narrativeAlignmentOverride) === "justified" ? "Justified" : normalize(source.narrativeAlignmentOverride) === "left" ? "Left" : null,
                    additionalNote: String(source.additionalNote || "").trim() || null
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
            editorialState: snapshot.editorialState && typeof snapshot.editorialState === "object"
                ? {
                    narrativeSource: String(snapshot.editorialState.narrativeSource || "ProjectBrief"),
                    narrativeAlignment: normalize(snapshot.editorialState.narrativeAlignment) === "justified" ? "Justified" : "Left",
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
