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
        shouldCommitPhotoRequest
    });
})();
