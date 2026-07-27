(() => {
    "use strict";

    const modalElement = document.getElementById("arppReferenceModal");
    if (!modalElement || !window.bootstrap) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const title = modalElement.querySelector("[data-reference-modal-title]");
    const kindLabel = modalElement.querySelector("[data-reference-kind-label]");
    const valueLabel = modalElement.querySelector("[data-reference-value-label]");
    const descriptionField = modalElement.querySelector("[data-reference-description-field]");
    const kindInput = modalElement.querySelector("[data-reference-kind]");
    const idInput = modalElement.querySelector("[data-reference-id]");
    const rowVersionInput = modalElement.querySelector("[data-reference-row-version]");
    const valueInput = modalElement.querySelector("[data-reference-value]");
    const descriptionInput = modalElement.querySelector("[data-reference-description]");
    const sortOrderInput = modalElement.querySelector("[data-reference-sort-order]");

    const configure = trigger => {
        const isEdit = trigger.hasAttribute("data-reference-edit");
        const kind = trigger.dataset.kind || "1";
        const label = trigger.dataset.kindLabel || "Reference";
        const isDfpds = kind === "3";

        if (title) title.textContent = isEdit ? `Edit ${label}` : `Add ${label}`;
        if (kindLabel) kindLabel.textContent = label;
        if (valueLabel) valueLabel.textContent = isDfpds ? "Schedule number" : label.replace(/s$/, "");
        descriptionField?.classList.toggle("d-none", !isDfpds);

        if (kindInput) kindInput.value = kind;
        if (idInput) idInput.value = isEdit ? trigger.dataset.id || "" : "";
        if (rowVersionInput) rowVersionInput.value = isEdit ? trigger.dataset.rowVersion || "" : "";
        if (valueInput) valueInput.value = isEdit ? trigger.dataset.value || "" : "";
        if (descriptionInput) descriptionInput.value = isEdit ? trigger.dataset.description || "" : "";
        if (sortOrderInput) sortOrderInput.value = isEdit ? trigger.dataset.sortOrder || "0" : "0";
    };

    document.querySelectorAll("[data-reference-add], [data-reference-edit]").forEach(trigger => {
        trigger.addEventListener("click", () => configure(trigger));
    });

    modalElement.addEventListener("shown.bs.modal", () => valueInput?.focus());

    if (modalElement.dataset.reopen === "true") {
        const kind = kindInput?.value || "1";
        const isDfpds = kind === "3";
        const label = kind === "1" ? "CFA" : kind === "2" ? "Fund" : "DFPDS schedule";
        if (title) title.textContent = idInput?.value ? `Edit ${label}` : `Add ${label}`;
        if (kindLabel) kindLabel.textContent = label;
        if (valueLabel) valueLabel.textContent = isDfpds ? "Schedule number" : label;
        descriptionField?.classList.toggle("d-none", !isDfpds);
        modal.show();
    }
})();
