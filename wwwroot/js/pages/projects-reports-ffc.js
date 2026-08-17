(() => {
    "use strict";

    const form = document.querySelector("[data-ffc-report-controls]");
    if (!form) {
        return;
    }

    const selectionMode = form.querySelector("#ffcSelectionMode");
    const countryYears = form.querySelector("#ffcCountryYears");
    const checkboxes = Array.from(form.querySelectorAll("[data-ffc-country-year]"));
    const summary = form.querySelector("[data-ffc-selection-summary]");
    const menuCount = form.querySelector("[data-ffc-menu-count]");
    const overallStatus = form.querySelector("[data-ffc-overall-status]");
    const refreshButton = form.querySelector("[data-ffc-refresh]");
    const actionButtons = Array.from(form.querySelectorAll("[data-ffc-country-action]"));

    const checked = () => checkboxes.filter(input => input.checked);

    const updateCount = () => {
        const count = checked().length;
        if (summary) {
            summary.textContent = `${count} of ${checkboxes.length} selected`;
        }

        if (menuCount) {
            menuCount.textContent = `${count} selected`;
        }
    };

    const syncHiddenSelection = () => {
        if (countryYears) {
            countryYears.value = checked().map(input => input.value).join(",");
        }
    };

    checkboxes.forEach(input => {
        input.addEventListener("change", () => {
            if (selectionMode) {
                selectionMode.value = "Custom";
            }
            updateCount();
        });
    });

    actionButtons.forEach(button => {
        button.addEventListener("click", () => {
            const action = button.dataset.ffcCountryAction;

            if (action === "default") {
                checkboxes.forEach(input => {
                    input.checked = input.dataset.defaultIncluded === "true";
                });
                if (selectionMode) {
                    selectionMode.value = "DefaultActive";
                }
            } else if (action === "all") {
                checkboxes.forEach(input => { input.checked = true; });
                if (selectionMode) {
                    selectionMode.value = "Custom";
                }
            } else if (action === "clear") {
                checkboxes.forEach(input => { input.checked = false; });
                if (selectionMode) {
                    selectionMode.value = "Custom";
                }
            }

            updateCount();
        });
    });

    form.addEventListener("submit", () => {
        syncHiddenSelection();
    });

    overallStatus?.addEventListener("change", () => {
        refreshButton?.classList.add("report-refresh-button--pending");
    });

    updateCount();
})();
