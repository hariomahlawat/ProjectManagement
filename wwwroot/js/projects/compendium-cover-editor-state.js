(() => {
    "use strict";

    const positiveId = value => {
        const parsed = Number(value);
        return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
    };

    const finiteDimension = value => {
        const parsed = Number(value);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    };

    const clamp01 = value => {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? Math.max(0, Math.min(1, parsed)) : .5;
    };

    const samePhoto = (slot, projectId, photoId) =>
        positiveId(slot?.projectId) === positiveId(projectId)
        && positiveId(slot?.photoId) === positiveId(photoId);

    const applyExplicitPhoto = (slot, projectId, photo) => {
        if (!slot || typeof slot !== "object") throw new TypeError("A cover image slot is required.");
        const cleanProjectId = positiveId(projectId);
        const cleanPhotoId = positiveId(photo?.photoId);
        if (!cleanProjectId || !cleanPhotoId) throw new TypeError("A valid project photograph is required.");

        const changed = !samePhoto(slot, cleanProjectId, cleanPhotoId);
        slot.imageMode = "Explicit";
        slot.projectId = cleanProjectId;
        slot.photoId = cleanPhotoId;
        slot.previewUrl = photo?.previewUrl || photo?.thumbnailUrl || null;
        slot.sourceWidth = finiteDimension(photo?.width);
        slot.sourceHeight = finiteDimension(photo?.height);

        // A focal point belongs to the source photograph, not merely to the visual slot.
        // Preserve the crop when the same photo is reselected; centre a genuinely new image.
        if (changed) {
            slot.focalX = .5;
            slot.focalY = .5;
        }

        return changed;
    };

    const slotReference = slot => {
        if (!slot || slot.imageMode === "None") return null;
        const projectId = positiveId(slot.projectId);
        const photoId = positiveId(slot.photoId);
        return projectId && photoId ? { projectId, photoId } : null;
    };

    const applyAutomaticPhoto = (slot, candidate, photo = null) => {
        if (!slot || typeof slot !== "object") throw new TypeError("A cover image slot is required.");
        const projectId = positiveId(candidate?.projectId);
        const photoId = positiveId(candidate?.photoId);
        if (!projectId || !photoId) throw new TypeError("A valid automatic cover photograph is required.");

        const changed = !samePhoto(slot, projectId, photoId);
        slot.imageMode = "Automatic";
        slot.projectId = projectId;
        slot.photoId = photoId;
        if (changed) {
            slot.focalX = clamp01(candidate?.focalX);
            slot.focalY = clamp01(candidate?.focalY);
        } else {
            slot.focalX = clamp01(slot.focalX);
            slot.focalY = clamp01(slot.focalY);
        }
        slot.previewUrl = photo?.previewUrl || photo?.thumbnailUrl || slot.previewUrl || null;
        slot.sourceWidth = finiteDimension(photo?.width) || slot.sourceWidth || null;
        slot.sourceHeight = finiteDimension(photo?.height) || slot.sourceHeight || null;
        return changed;
    };

    const resetAutomaticAssignment = slot => {
        if (!slot || typeof slot !== "object") throw new TypeError("A cover image slot is required.");
        slot.imageMode = "Automatic";
        slot.projectId = null;
        slot.photoId = null;
        slot.focalX = .5;
        slot.focalY = .5;
        slot.previewUrl = null;
        slot.sourceWidth = null;
        slot.sourceHeight = null;
    };

    const clearPreview = slot => {
        if (!slot || typeof slot !== "object") return;
        slot.previewUrl = null;
        slot.sourceWidth = null;
        slot.sourceHeight = null;
    };

    const isPhotoUsedByOtherSlot = (slots, surface, activeSlot, projectId, photoId) => {
        const cleanProjectId = positiveId(projectId);
        const cleanPhotoId = positiveId(photoId);
        if (!cleanProjectId || !cleanPhotoId) return false;
        return (Array.isArray(slots) ? slots : []).some(slot => {
            if (!slot || slot === activeSlot || slot.imageMode === "None") return false;
            if ((slot.surface || "").toString().toLowerCase() !== (surface || "").toString().toLowerCase()) return false;
            const reference = slotReference(slot);
            return reference?.projectId === cleanProjectId && reference?.photoId === cleanPhotoId;
        });
    };

    const shouldCommitPhotoRequest = (
        requestVersion,
        currentVersion,
        requestedProjectId,
        selectedProjectId) =>
        Number(requestVersion) === Number(currentVersion)
        && positiveId(requestedProjectId) === positiveId(selectedProjectId);

    globalThis.PrismCompendiumCoverState = Object.freeze({
        positiveId,
        samePhoto,
        applyExplicitPhoto,
        slotReference,
        applyAutomaticPhoto,
        resetAutomaticAssignment,
        clearPreview,
        isPhotoUsedByOtherSlot,
        shouldCommitPhotoRequest
    });
})();
