"use strict";

(() => {
    const root = document.querySelector("[data-arpp-details]");
    if (!root) return;

    const uploadInput = root.querySelector("[data-arpp-pdf-input]");
    const uploadButton = root.querySelector("[data-arpp-upload-button]");
    if (uploadInput && uploadButton) {
        const defaultLabel = uploadButton.textContent.trim();
        uploadInput.addEventListener("change", () => {
            const hasFile = uploadInput.files?.length > 0;
            uploadButton.disabled = !hasFile;
            uploadButton.title = hasFile ? uploadInput.files[0].name : "Select a PDF first";
            if (!hasFile) uploadButton.setAttribute("aria-label", defaultLabel);
        });
        uploadButton.disabled = !(uploadInput.files?.length > 0);
    }

    root.querySelectorAll("[data-arpp-delete-pdf-form]").forEach(form => {
        form.addEventListener("submit", async event => {
            if (form.dataset.confirmed === "true") return;
            event.preventDefault();

            const accepted = window.PrismConfirm?.show
                ? await window.PrismConfirm.show({
                    title: "Remove issued HQ PDF?",
                    message: "The structured ARPP issue and all rows will remain unchanged.",
                    detail: "The file will no longer be available from this ARPP record.",
                    confirmText: "Remove PDF",
                    cancelText: "Keep PDF",
                    tone: "danger"
                })
                : false;

            if (!accepted) return;
            form.dataset.confirmed = "true";
            form.requestSubmit();
        });
    });
})();
