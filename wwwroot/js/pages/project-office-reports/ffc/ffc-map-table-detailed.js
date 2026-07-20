/* SECTION: Detailed-table presentation controller */
(() => {
    "use strict";

    const page = document.querySelector(".ffc-detailed-page");
    if (!page) {
        return;
    }

    const expandableSelector = "[data-expandable]";
    const clampSelector = ".ffc-dtable__clamp";
    const buttonSelector = ".js-expand-status";

    const measureExpandable = (container) => {
        const text = container.querySelector(clampSelector);
        const button = container.querySelector(buttonSelector);
        if (!text || !button) {
            return;
        }

        if (!button.dataset.collapsedLabel) {
            button.dataset.collapsedLabel = button.textContent.trim();
            button.dataset.expandedLabel = "Show less";
        }

        const isExpanded = text.classList.contains("is-expanded");
        if (isExpanded) {
            button.classList.remove("d-none");
            return;
        }

        const overflows = text.scrollHeight > text.clientHeight + 1;
        button.classList.toggle("d-none", !overflows);
        button.setAttribute("aria-expanded", "false");
        button.textContent = button.dataset.collapsedLabel;
    };

    const measureAll = () => {
        document.querySelectorAll(expandableSelector).forEach(measureExpandable);
    };

    const handleExpand = (event) => {
        const button = event.currentTarget;
        event.preventDefault();
        event.stopPropagation();

        const container = button.closest(expandableSelector);
        const text = container?.querySelector(clampSelector);
        if (!container || !text) {
            return;
        }

        const willExpand = !text.classList.contains("is-expanded");
        text.classList.toggle("is-expanded", willExpand);
        button.setAttribute("aria-expanded", String(willExpand));
        button.textContent = willExpand
            ? button.dataset.expandedLabel || "Show less"
            : button.dataset.collapsedLabel || "Show full status";
    };

    const initialiseExpanders = () => {
        document.querySelectorAll(buttonSelector).forEach((button) => {
            button.addEventListener("click", handleExpand);
        });

        window.requestAnimationFrame(measureAll);
        if (document.fonts?.ready) {
            document.fonts.ready.then(measureAll).catch(() => undefined);
        }

        let resizeTimer = 0;
        window.addEventListener("resize", () => {
            window.clearTimeout(resizeTimer);
            resizeTimer = window.setTimeout(measureAll, 120);
        });

        document.addEventListener("ffc:detailed-table-content-updated", measureAll);
    };

    const initialiseWordExport = () => {
        const modalElement = document.getElementById("ffcWordExportModal");
        const form = document.querySelector("[data-ffc-word-export-form]");
        const submitButton = document.querySelector("[data-ffc-word-export-submit]");
        if (!modalElement || !form || !window.bootstrap?.Modal) {
            return;
        }

        const modal = window.bootstrap.Modal.getOrCreateInstance(modalElement);

        modalElement.addEventListener("shown.bs.modal", () => {
            modalElement.querySelector("input:not([type='hidden'])")?.focus();
        });

        form.addEventListener("submit", () => {
            if (!form.checkValidity()) {
                return;
            }

            if (submitButton) {
                submitButton.disabled = true;
                submitButton.dataset.originalText = submitButton.innerHTML;
                submitButton.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Generating…';
            }

            window.setTimeout(() => modal.hide(), 120);
            window.setTimeout(() => {
                if (submitButton) {
                    submitButton.disabled = false;
                    submitButton.innerHTML = submitButton.dataset.originalText || "Generate Word file";
                }
            }, 2500);
        });

        if (page.dataset.openWordExportModal === "true") {
            modal.show();
        }
    };

    initialiseExpanders();
    initialiseWordExport();
})();
