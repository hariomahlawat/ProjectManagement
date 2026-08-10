(() => {
    "use strict";

    const form = document.querySelector("[data-compendium-publication-form]");
    const generateButton = document.querySelector("[data-compendium-publication-generate]");
    const previewButton = document.querySelector("[data-compendium-publication-preview]");
    const showAllWarnings = document.querySelector("[data-compendium-show-all-warnings]");

    if (!form) return;

    const spinner = generateButton?.querySelector(".spinner-border");
    const icon = generateButton?.querySelector(".bi-download");
    const label = generateButton?.querySelector("[data-compendium-publication-label]");
    const initiallyGenerateDisabled = Boolean(generateButton?.disabled);
    const initiallyPreviewDisabled = Boolean(previewButton?.disabled);

    const setGenerating = generating => {
        if (!generateButton) return;

        generateButton.setAttribute("aria-busy", generating ? "true" : "false");
        generateButton.disabled = generating || initiallyGenerateDisabled;
        if (previewButton) previewButton.disabled = generating || initiallyPreviewDisabled;
        spinner?.classList.toggle("d-none", !generating);
        icon?.classList.toggle("d-none", generating);
        if (label) label.textContent = generating ? "Generating…" : "Download PDF";
    };

    showAllWarnings?.addEventListener("click", () => {
        document.querySelectorAll("[data-compendium-warning-row][hidden]").forEach(row => {
            row.hidden = false;
        });
        showAllWarnings.remove();
    });

    form.addEventListener("submit", event => {
        const submitter = event.submitter;

        // Preview opens in a separate tab and leaves the builder interactive.
        if (submitter === previewButton) {
            return;
        }

        if (submitter !== generateButton) {
            return;
        }

        if (generateButton?.getAttribute("aria-busy") === "true") {
            event.preventDefault();
            return;
        }

        setGenerating(true);
    });

    window.addEventListener("pageshow", () => setGenerating(false));
})();
