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

    // Keep focus outside a modal before Bootstrap applies aria-hidden. This prevents the
    // accessibility warning seen when the close button retains focus during dismissal.
    document.querySelectorAll("[data-arpp-modal]").forEach(modal => {
        let trigger = null;

        modal.addEventListener("show.bs.modal", event => {
            trigger = event.relatedTarget instanceof HTMLElement
                ? event.relatedTarget
                : document.activeElement instanceof HTMLElement
                    ? document.activeElement
                    : null;
        });

        modal.addEventListener("hide.bs.modal", () => {
            if (modal.contains(document.activeElement) && document.activeElement instanceof HTMLElement) {
                document.activeElement.blur();
            }
        });

        modal.addEventListener("hidden.bs.modal", () => {
            if (trigger?.isConnected) trigger.focus();
        });
    });

    const unlockModal = document.querySelector("[data-arpp-unlock-modal]");
    const unlockForm = unlockModal?.querySelector("[data-arpp-unlock-form]");
    const unlockReason = unlockForm?.querySelector("[data-arpp-unlock-reason]");
    const unlockValidation = unlockForm?.querySelector("[data-arpp-unlock-validation]");
    const unlockSubmit = unlockForm?.querySelector("[data-arpp-unlock-submit]");
    const unlockSubmitLabel = unlockForm?.querySelector("[data-arpp-unlock-submit-label]");

    const unlockValidationMessage = "Enter a clear reason of at least 10 characters.";

    const validateUnlockReason = () => {
        if (!(unlockReason instanceof HTMLTextAreaElement)) return true;

        const isValid = unlockReason.value.trim().length >= 10;
        unlockReason.setCustomValidity(isValid ? "" : unlockValidationMessage);
        unlockReason.classList.toggle("is-invalid", !isValid);

        if (unlockValidation) {
            unlockValidation.textContent = isValid ? "" : unlockValidationMessage;
            unlockValidation.classList.toggle("field-validation-error", !isValid);
            unlockValidation.classList.toggle("field-validation-valid", isValid);
        }

        return isValid;
    };

    if (unlockReason) {
        unlockReason.addEventListener("input", () => {
            if (unlockReason.value.trim().length >= 10) {
                validateUnlockReason();
            } else {
                unlockReason.setCustomValidity("");
                unlockReason.classList.remove("is-invalid");
                if (unlockValidation) unlockValidation.textContent = "";
            }
        });

        unlockReason.addEventListener("invalid", () => {
            validateUnlockReason();
        });
    }

    if (unlockForm) {
        unlockForm.addEventListener("submit", event => {
            if (!validateUnlockReason()) {
                event.preventDefault();
                unlockReason?.focus();
                unlockReason?.reportValidity();
                return;
            }

            if (unlockSubmit instanceof HTMLButtonElement) {
                unlockSubmit.disabled = true;
            }
            if (unlockSubmitLabel) {
                unlockSubmitLabel.textContent = "Unlocking…";
            }
        });
    }

    if (root.dataset.arppReopenUnlockModal === "true" && unlockModal && window.bootstrap?.Modal) {
        const modal = window.bootstrap.Modal.getOrCreateInstance(unlockModal);
        unlockModal.addEventListener("shown.bs.modal", () => {
            unlockReason?.focus();
            validateUnlockReason();
        }, { once: true });
        modal.show();
    }
})();
