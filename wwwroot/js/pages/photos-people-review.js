(() => {
    "use strict";

    const modal = document.getElementById("assignExistingModal");
    if (!modal) return;

    modal.addEventListener("show.bs.modal", event => {
        const trigger = event.relatedTarget;
        if (!(trigger instanceof HTMLElement)) return;

        const faceIdInput = modal.querySelector('input[name="faceId"]');
        const context = modal.querySelector("[data-face-context]");
        const personSelect = modal.querySelector('select[name="personId"]');

        if (faceIdInput instanceof HTMLInputElement) {
            faceIdInput.value = trigger.dataset.faceId ?? "";
        }
        if (context instanceof HTMLElement) {
            const title = trigger.dataset.faceContext?.trim();
            context.textContent = title
                ? `Choose the confirmed identity for the face detected in “${title}”.`
                : "Choose the confirmed identity for this face.";
        }
        if (personSelect instanceof HTMLSelectElement) {
            personSelect.value = "";
        }
    });

    modal.addEventListener("shown.bs.modal", () => {
        const personSelect = modal.querySelector('select[name="personId"]');
        if (personSelect instanceof HTMLSelectElement) {
            personSelect.focus();
        }
    });
})();
