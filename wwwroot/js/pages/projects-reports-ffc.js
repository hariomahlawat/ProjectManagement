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
    const updateRequired = form.querySelector("[data-ffc-update-required]");
    const actionButtons = Array.from(form.querySelectorAll("[data-ffc-country-action]"));
    const exportLinks = Array.from(document.querySelectorAll("[data-ffc-export]"));

    const baseExportDisabled = new Map(
        exportLinks.map(link => [
            link,
            link.classList.contains("disabled")
                || String(link.getAttribute("aria-disabled")).toLowerCase() === "true"
        ]));

    const checked = () => checkboxes.filter(input => input.checked);

    const selectedSignature = () => checked()
        .map(input => String(input.value))
        .sort((left, right) => left.localeCompare(right, undefined, { numeric: true }))
        .join(",");

    const appliedState = Object.freeze({
        selection: selectedSignature(),
        overallStatus: Boolean(overallStatus?.checked)
    });

    const hasPendingChanges = () =>
        selectedSignature() !== appliedState.selection
        || Boolean(overallStatus?.checked) !== appliedState.overallStatus;

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

    const setExportDisabled = (link, disabled) => {
        link.classList.toggle("disabled", disabled);
        link.setAttribute("aria-disabled", disabled ? "true" : "false");

        if (disabled) {
            link.setAttribute("tabindex", "-1");
        } else {
            link.removeAttribute("tabindex");
        }
    };

    const updatePendingState = () => {
        const pending = hasPendingChanges();

        refreshButton?.classList.toggle(
            "report-refresh-button--pending",
            pending);

        if (updateRequired) {
            updateRequired.hidden = !pending;
        }

        exportLinks.forEach(link => {
            const disabled = pending || Boolean(baseExportDisabled.get(link));
            setExportDisabled(link, disabled);
        });
    };

    checkboxes.forEach(input => {
        input.addEventListener("change", () => {
            if (selectionMode) {
                selectionMode.value = "Custom";
            }

            updateCount();
            updatePendingState();
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
                checkboxes.forEach(input => {
                    input.checked = true;
                });

                if (selectionMode) {
                    selectionMode.value = "Custom";
                }
            } else if (action === "clear") {
                checkboxes.forEach(input => {
                    input.checked = false;
                });

                if (selectionMode) {
                    selectionMode.value = "Custom";
                }
            }

            updateCount();
            updatePendingState();
        });
    });

    overallStatus?.addEventListener("change", updatePendingState);

    form.addEventListener("submit", () => {
        syncHiddenSelection();
    });

    exportLinks.forEach(link => {
        link.addEventListener("click", event => {
            if (String(link.getAttribute("aria-disabled")).toLowerCase() === "true") {
                event.preventDefault();
            }
        });
    });

    updateCount();
    updatePendingState();
})();
