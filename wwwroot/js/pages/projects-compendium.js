(() => {
    "use strict";

    const form = document.querySelector("[data-compendium-generate-form]");
    const submitButton = document.querySelector("[data-compendium-generate-button]");
    const spinner = document.querySelector("[data-compendium-spinner]");
    const buttonIcon = document.querySelector("[data-compendium-button-icon]");
    const buttonLabel = document.querySelector("[data-compendium-button-label]");
    const showAllButton = document.querySelector("[data-compendium-show-all]");

    const setGenerating = (isGenerating) => {
        if (!submitButton) return;

        submitButton.disabled = isGenerating || submitButton.dataset.initiallyDisabled === "true";
        submitButton.setAttribute("aria-busy", isGenerating ? "true" : "false");
        spinner?.classList.toggle("d-none", !isGenerating);
        buttonIcon?.classList.toggle("d-none", isGenerating);

        if (buttonLabel) {
            buttonLabel.textContent = isGenerating ? "Generating…" : "Generate PDF";
        }
    };

    if (submitButton) {
        submitButton.dataset.initiallyDisabled = submitButton.disabled ? "true" : "false";
    }

    form?.addEventListener("submit", (event) => {
        if (typeof form.checkValidity === "function" && !form.checkValidity()) {
            return;
        }

        if (submitButton?.getAttribute("aria-busy") === "true") {
            event.preventDefault();
            return;
        }

        setGenerating(true);
    });

    showAllButton?.addEventListener("click", () => {
        document.querySelectorAll("[data-compendium-warning-row]").forEach((row) => {
            row.classList.remove("compendium-warning-row--hidden");
        });
        showAllButton.remove();
    });

    window.addEventListener("pageshow", () => setGenerating(false));
})();
