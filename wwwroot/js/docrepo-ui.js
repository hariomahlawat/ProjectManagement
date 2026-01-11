// SECTION: Document repository row navigation
(() => {
    const interactiveSelector = "a, button, input, select, textarea, .dropdown, .dropdown-menu, [data-no-row-nav]";
    const navigableSelector = ".docrepo-row, .docrepo-card";

    // SECTION: Click navigation
    document.addEventListener("click", (event) => {
        const target = event.target;
        const navigable = target.closest(navigableSelector);
        if (!navigable) {
            return;
        }

        if (target.closest(interactiveSelector)) {
            return;
        }

        const href = navigable.dataset.href;
        if (!href) {
            return;
        }

        window.location.href = href;
    });

    // SECTION: Keyboard navigation
    document.addEventListener("keydown", (event) => {
        const target = event.target;
        const row = target.closest(".docrepo-row");
        if (!row) {
            return;
        }

        if (target !== row && target.closest(interactiveSelector)) {
            return;
        }

        if (event.key === "Enter") {
            const href = row.dataset.href;
            if (href) {
                window.location.href = href;
            }
        }

        if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
            return;
        }

        const rowContainer = row.closest("tbody") ?? row.parentElement;
        if (!rowContainer) {
            return;
        }

        const rows = Array.from(rowContainer.querySelectorAll(".docrepo-row"));
        const index = rows.indexOf(row);
        if (index < 0) {
            return;
        }

        const nextIndex = event.key === "ArrowDown" ? index + 1 : index - 1;
        const nextRow = rows[nextIndex];
        if (nextRow) {
            event.preventDefault();
            nextRow.focus();
        }
    });
})();

// SECTION: Document repository facets + collapse
(() => {
    const storageKey = "docrepo.filters.collapsed";
    const filtersEl = document.getElementById("docrepoFilters");

    if (!filtersEl) {
        return;
    }

    const toggleButton = document.getElementById("docrepoFiltersToggle");
    const expandButton = document.getElementById("docrepoFiltersExpandBtn");

    const setCollapsed = (collapsed) => {
        filtersEl.classList.toggle("docrepo-filters--collapsed", collapsed);

        if (toggleButton) {
            toggleButton.title = collapsed ? "Expand filters" : "Collapse filters";
            toggleButton.setAttribute("aria-label", toggleButton.title);
            const icon = toggleButton.querySelector("i");
            if (icon) {
                icon.classList.toggle("bi-chevron-right", collapsed);
                icon.classList.toggle("bi-chevron-left", !collapsed);
            }
        }

        try {
            localStorage.setItem(storageKey, collapsed ? "true" : "false");
        } catch (error) {
            // Ignore storage failures.
        }
    };

    // SECTION: Collapse initialization
    try {
        const collapsed = localStorage.getItem(storageKey) === "true";
        setCollapsed(collapsed);
    } catch (error) {
        setCollapsed(false);
    }

    if (toggleButton) {
        toggleButton.addEventListener("click", () => {
            const collapsed = !filtersEl.classList.contains("docrepo-filters--collapsed");
            setCollapsed(collapsed);
        });
    }

    if (expandButton) {
        expandButton.addEventListener("click", () => setCollapsed(false));
    }

    // SECTION: Single-select facets
    const facetGroups = Array.from(document.querySelectorAll("[data-facet-single]"));
    facetGroups.forEach((group) => {
        const hiddenSelector = group.getAttribute("data-target-hidden");
        const hiddenInput = hiddenSelector ? document.querySelector(hiddenSelector) : null;

        const syncHidden = () => {
            if (!hiddenInput) {
                return;
            }

            const checked = group.querySelector("input.docrepo-check__input:checked");
            hiddenInput.value = checked ? checked.getAttribute("data-value") || "" : "";
        };

        group.addEventListener("change", (event) => {
            const target = event.target;
            if (!(target instanceof HTMLInputElement) || !target.classList.contains("docrepo-check__input")) {
                return;
            }

            const allInputs = Array.from(group.querySelectorAll("input.docrepo-check__input"));

            if (target.checked) {
                allInputs.forEach((input) => {
                    if (input !== target) {
                        input.checked = false;
                    }
                });
            } else {
                const allOption = allInputs.find((input) => (input.getAttribute("data-value") || "") === "");
                if (allOption) {
                    allOption.checked = true;
                }
            }

            const customYearInput = document.querySelector("[data-custom-year]");
            if (group.getAttribute("data-facet-single") === "year" && customYearInput) {
                if (target.checked) {
                    customYearInput.value = "";
                }
            }

            syncHidden();
        });

        syncHidden();
    });

    // SECTION: Custom year precedence
    const customYearInput = document.querySelector("[data-custom-year]");
    const yearGroup = document.querySelector('[data-facet-single="year"]');
    const yearHidden = document.getElementById("yearHidden");

    if (customYearInput && yearGroup && yearHidden) {
        const clearYearChecks = () => {
            const yearChecks = Array.from(yearGroup.querySelectorAll("input.docrepo-check__input"));
            yearChecks.forEach((input) => {
                input.checked = false;
            });
        };

        const syncYearHidden = () => {
            const value = customYearInput.value.trim();
            if (value.length > 0) {
                yearHidden.value = value;
                clearYearChecks();
            } else {
                let checked = yearGroup.querySelector("input.docrepo-check__input:checked");
                if (!checked) {
                    const allOption = yearGroup.querySelector('input.docrepo-check__input[data-value=""]');
                    if (allOption) {
                        allOption.checked = true;
                        checked = allOption;
                    }
                }
                yearHidden.value = checked ? checked.getAttribute("data-value") || "" : "";
            }
        };

        customYearInput.addEventListener("input", syncYearHidden);
        syncYearHidden();
    }
})();
