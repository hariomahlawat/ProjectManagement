// SECTION: Document repository row navigation
(() => {
    const interactiveSelector = "a, button, input, select, textarea, .dropdown, .dropdown-menu, [data-no-row-nav]";

    document.addEventListener("click", (event) => {
        const row = event.target.closest(".docrepo-row");
        if (!row) {
            return;
        }

        if (event.target.closest(interactiveSelector)) {
            return;
        }

        const href = row.dataset.href;
        if (!href) {
            return;
        }

        window.location.href = href;
    });
})();
