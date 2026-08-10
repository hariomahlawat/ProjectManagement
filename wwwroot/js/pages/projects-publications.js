(() => {
    "use strict";

    const form = document.querySelector("[data-compendium-publication-form]");
    const button = document.querySelector("[data-compendium-publication-generate]");
    if (!form || !button) return;

    const spinner = button.querySelector(".spinner-border");
    const icon = button.querySelector(".bi-file-earmark-pdf");
    const label = button.querySelector("[data-compendium-publication-label]");
    const initiallyDisabled = button.disabled;
    const showAllWarnings = document.querySelector("[data-compendium-show-all-warnings]");

    const setGenerating = generating => {
        button.setAttribute("aria-busy", generating ? "true" : "false");
        button.disabled = generating || initiallyDisabled;
        spinner?.classList.toggle("d-none", !generating);
        icon?.classList.toggle("d-none", generating);
        if (label) label.textContent = generating ? "Generating…" : "Generate compendium PDF";
    };

    showAllWarnings?.addEventListener("click", () => {
        document.querySelectorAll("[data-compendium-warning-row][hidden]").forEach(row => {
            row.hidden = false;
        });
        showAllWarnings.remove();
    });

    form.addEventListener("submit", event => {
        if (button.getAttribute("aria-busy") === "true") {
            event.preventDefault();
            return;
        }
        setGenerating(true);
    });

    window.addEventListener("pageshow", () => setGenerating(false));
})();
