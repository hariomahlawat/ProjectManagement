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
    const stickyChromeSelector = ".pm-topbar, .pm-module-subnav-wrap";
    let stickyResizeObserver = null;

    // SECTION: Sticky table geometry
    const isRendered = (element) => {
        const style = window.getComputedStyle(element);
        if (style.display === "none" || style.visibility === "hidden") {
            return false;
        }

        const rect = element.getBoundingClientRect();
        return rect.height > 0.5 && rect.width > 0.5;
    };

    const getStickyChromeBottom = () => {
        let bottom = 0;

        document.querySelectorAll(stickyChromeSelector).forEach((element) => {
            if (!isRendered(element)) {
                return;
            }

            const style = window.getComputedStyle(element);
            if (style.position !== "sticky" && style.position !== "fixed") {
                return;
            }

            const rect = element.getBoundingClientRect();
            if (rect.bottom <= 0 || rect.top >= window.innerHeight) {
                return;
            }

            bottom = Math.max(bottom, rect.bottom);
        });

        return Math.max(0, Math.ceil(bottom));
    };

    const syncStickyGeometry = () => {
        const headerRow = page.querySelector(".ffc-dtable thead tr");
        const stickyTop = getStickyChromeBottom() || 52;
        const headerHeight = headerRow
            ? Math.max(1, Math.ceil(headerRow.getBoundingClientRect().height))
            : 42;

        page.style.setProperty("--ffc-sticky-top", `${stickyTop}px`);
        page.style.setProperty("--ffc-column-header-height", `${headerHeight}px`);
    };

    const initialiseStickyGeometry = () => {
        let resizeTimer = 0;
        const scheduleSync = () => {
            window.clearTimeout(resizeTimer);
            resizeTimer = window.setTimeout(syncStickyGeometry, 60);
        };

        window.requestAnimationFrame(syncStickyGeometry);
        window.addEventListener("resize", scheduleSync, { passive: true });
        window.addEventListener("orientationchange", scheduleSync, { passive: true });
        window.addEventListener("pageshow", scheduleSync, { passive: true });

        if (document.fonts?.ready) {
            document.fonts.ready.then(syncStickyGeometry).catch(() => undefined);
        }

        if ("ResizeObserver" in window) {
            stickyResizeObserver = new ResizeObserver(scheduleSync);
            document.querySelectorAll(stickyChromeSelector).forEach((element) => stickyResizeObserver.observe(element));

            const headerRow = page.querySelector(".ffc-dtable thead tr");
            if (headerRow) {
                stickyResizeObserver.observe(headerRow);
            }
        }
    };

    // SECTION: Narrative expansion
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
        syncStickyGeometry();
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
        }, { passive: true });

        document.addEventListener("ffc:detailed-table-content-updated", measureAll);
    };

    // SECTION: Word export modal
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

    initialiseStickyGeometry();
    initialiseExpanders();
    initialiseWordExport();
})();
