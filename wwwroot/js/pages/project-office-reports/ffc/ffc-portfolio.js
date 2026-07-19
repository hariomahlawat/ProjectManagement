(() => {
    "use strict";

    const pageRoot = document.querySelector(".ffc-portfolio");
    if (!pageRoot) {
        return;
    }

    const storageKey = `ffc-portfolio-open:${window.location.pathname}${window.location.search}`;
    const recordDetails = Array.from(pageRoot.querySelectorAll("[data-ffc-portfolio-row]"));
    const advancedFilters = pageRoot.querySelector("[data-ffc-advanced-filters]");

    const readOpenIds = () => {
        try {
            const value = window.sessionStorage.getItem(storageKey);
            const parsed = value ? JSON.parse(value) : [];
            return new Set(Array.isArray(parsed) ? parsed.map(String) : []);
        } catch {
            return new Set();
        }
    };

    const writeOpenIds = () => {
        try {
            const openIds = recordDetails
                .filter((details) => details.open)
                .map((details) => details.closest("[data-ffc-record-id]")?.dataset.ffcRecordId)
                .filter(Boolean);

            window.sessionStorage.setItem(storageKey, JSON.stringify(openIds));
        } catch {
            // Storage is an enhancement only. The page remains fully usable without it.
        }
    };

    const syncExpandedState = (details) => {
        const summary = details.querySelector(":scope > summary");
        if (summary) {
            summary.setAttribute("aria-expanded", details.open ? "true" : "false");
        }
    };

    const openIds = readOpenIds();
    recordDetails.forEach((details) => {
        const recordId = details.closest("[data-ffc-record-id]")?.dataset.ffcRecordId;
        if (recordId && openIds.has(recordId)) {
            details.open = true;
        }

        syncExpandedState(details);
        details.addEventListener("toggle", () => {
            syncExpandedState(details);
            writeOpenIds();
        });

        details.addEventListener("keydown", (event) => {
            if (event.key !== "Escape" || !details.open) {
                return;
            }

            details.open = false;
            details.querySelector(":scope > summary")?.focus();
        });
    });

    if (advancedFilters) {
        if (advancedFilters.dataset.active === "true") {
            advancedFilters.open = true;
        }

        const advancedSummary = advancedFilters.querySelector(":scope > summary");
        const syncAdvancedState = () => {
            advancedSummary?.setAttribute(
                "aria-expanded",
                advancedFilters.open ? "true" : "false");
        };

        syncAdvancedState();
        advancedFilters.addEventListener("toggle", syncAdvancedState);
    }
})();
