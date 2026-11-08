// The shared list page's Edit dialog (docs/ux/admin-review-3-fixes.md, PR 12). A row's Edit
// button carries the item's values (data-id, data-value, data-sort-order, data-description,
// data-row-version) and names the dialog in data-bs-target; this fills the dialog's fields
// marked data-reference-field. A dialog marked data-reopen="true" opens again on load, to show
// what was wrong with the last submit.
(() => {
    "use strict";

    if (!window.bootstrap) return;

    const fill = trigger => {
        const dialog = document.querySelector(trigger.getAttribute("data-bs-target") || "");
        if (!dialog) return;
        const values = {
            id: trigger.dataset.id,
            value: trigger.dataset.value,
            sortOrder: trigger.dataset.sortOrder,
            description: trigger.dataset.description,
            rowVersion: trigger.dataset.rowVersion
        };
        dialog.querySelectorAll("[data-reference-field]").forEach(field => {
            const value = values[field.getAttribute("data-reference-field")];
            field.value = value === undefined ? "" : value;
        });
    };

    document.querySelectorAll("[data-reference-edit]").forEach(trigger => {
        trigger.addEventListener("click", () => fill(trigger));
    });

    document.querySelectorAll(".modal").forEach(dialog => {
        dialog.addEventListener("shown.bs.modal", () => {
            dialog.querySelector("input:not([type=hidden]), textarea")?.focus();
        });
        if (dialog.dataset.reopen === "true") {
            bootstrap.Modal.getOrCreateInstance(dialog).show();
        }
    });
})();
