// SECTION: Document reader interactions
(() => {
    const ready = (handler) => {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", handler);
        } else {
            handler();
        }
    };

    ready(() => {
        // SECTION: Details panel toggle
        const detailsPanel = document.querySelector("[data-docreader-details]");
        const detailsBackdrop = document.querySelector("[data-docreader-details-backdrop]");
        const toggleButtons = document.querySelectorAll("[data-docreader-details-toggle]");
        const closeButton = document.querySelector("[data-docreader-details-close]");

        const setExpandedState = (isExpanded) => {
            toggleButtons.forEach((button) => {
                button.setAttribute("aria-expanded", isExpanded ? "true" : "false");
            });
        };

        const openDetails = () => {
            if (!detailsPanel || !detailsBackdrop) {
                return;
            }

            detailsPanel.classList.add("is-open");
            detailsBackdrop.classList.add("is-visible");
            detailsPanel.setAttribute("aria-hidden", "false");
            setExpandedState(true);
        };

        const closeDetails = () => {
            if (!detailsPanel || !detailsBackdrop) {
                return;
            }

            detailsPanel.classList.remove("is-open");
            detailsBackdrop.classList.remove("is-visible");
            detailsPanel.setAttribute("aria-hidden", "true");
            setExpandedState(false);
        };

        toggleButtons.forEach((button) => {
            button.addEventListener("click", () => {
                if (detailsPanel?.classList.contains("is-open")) {
                    closeDetails();
                } else {
                    openDetails();
                }
            });
        });

        closeButton?.addEventListener("click", closeDetails);
        detailsBackdrop?.addEventListener("click", closeDetails);
        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                closeDetails();
            }
        });

        // SECTION: Viewer loading state
        const viewer = document.querySelector("[data-docreader-viewer]");
        const iframe = document.querySelector("[data-docreader-frame]");
        const warning = document.querySelector("[data-docreader-warning]");

        if (!viewer || !iframe) {
            return;
        }

        let timeoutHandle = null;
        const showWarning = () => {
            warning?.classList.remove("d-none");
        };

        const markLoaded = () => {
            viewer.classList.add("is-loaded");
            if (timeoutHandle) {
                clearTimeout(timeoutHandle);
            }
        };

        iframe.addEventListener("load", markLoaded, { once: true });
        timeoutHandle = window.setTimeout(showWarning, 8000);
    });
})();
