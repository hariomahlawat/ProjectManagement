(() => {
    "use strict";

    const form = document.querySelector("[data-document-recovery-form]");
    const modalElement = document.getElementById("documentRecoveryModal");
    if (!form || !modalElement) return;

    const actionInput = form.querySelector("[data-document-command-action]");
    const idInput = form.querySelector("[data-document-command-id]");
    const selected = [...form.querySelectorAll("[data-document-select]")];
    const selectAll = form.querySelector("[data-document-select-all]");
    const selectionToolbar = form.querySelector("[data-document-selection-toolbar]");
    const selectedCount = form.querySelector("[data-document-selected-count]");
    const title = modalElement.querySelector("[data-document-operation-title]");
    const eyebrow = modalElement.querySelector("[data-document-operation-eyebrow]");
    const summaryTitle = modalElement.querySelector("[data-document-summary-title]");
    const summaryDetail = modalElement.querySelector("[data-document-summary-detail]");
    const restorePanel = modalElement.querySelector("[data-document-restore-panel]");
    const deletePanel = modalElement.querySelector("[data-document-delete-panel]");
    const submit = modalElement.querySelector("[data-document-submit]");
    const confirmation = modalElement.querySelector("[data-document-confirmation]");
    const acknowledge = modalElement.querySelector("[data-document-acknowledge]");

    const humanBytes = (bytes) => {
        let value = Number(bytes) || 0;
        const units = ["B", "KB", "MB", "GB", "TB"];
        let unit = 0;
        while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit += 1; }
        return `${value.toFixed(value >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
    };

    const currentSelection = () => selected.filter(item => item.checked);
    const updateSelection = () => {
        const checked = currentSelection();
        if (selectedCount) selectedCount.textContent = String(checked.length);
        if (selectionToolbar) selectionToolbar.hidden = checked.length === 0;
        if (selectAll) {
            selectAll.checked = selected.length > 0 && checked.length === selected.length;
            selectAll.indeterminate = checked.length > 0 && checked.length < selected.length;
        }
    };

    selectAll?.addEventListener("change", () => {
        selected.forEach(item => { item.checked = selectAll.checked; });
        updateSelection();
    });
    selected.forEach(item => item.addEventListener("change", updateSelection));

    const configure = (action, documentId, documentTitle, size, isBulk) => {
        const deleting = action.startsWith("delete");
        const checked = currentSelection();
        actionInput.value = action;
        idInput.value = documentId || "0";
        if (confirmation) confirmation.value = "";
        if (acknowledge) acknowledge.checked = false;
        restorePanel.hidden = deleting;
        deletePanel.hidden = !deleting;
        eyebrow.textContent = deleting ? "Controlled permanent deletion" : "Recovery operation";
        title.textContent = deleting ? "Delete permanently" : "Restore document";

        if (isBulk) {
            const totalBytes = checked.reduce((sum, item) => sum + (Number(item.dataset.documentSize) || 0), 0);
            summaryTitle.textContent = `${checked.length} selected document${checked.length === 1 ? "" : "s"}`;
            summaryDetail.textContent = `${humanBytes(totalBytes)} measured storage`;
        } else {
            summaryTitle.textContent = documentTitle || "Selected document";
            summaryDetail.textContent = humanBytes(size);
        }

        submit.className = deleting ? "btn btn-danger" : "btn btn-primary";
        submit.innerHTML = deleting
            ? '<i class="bi bi-trash"></i> Delete permanently'
            : '<i class="bi bi-arrow-counterclockwise"></i> Restore';
    };

    document.querySelectorAll("[data-document-action]").forEach(button => {
        button.addEventListener("click", () => configure(
            button.dataset.documentAction || "restore",
            button.dataset.documentId || "0",
            button.dataset.documentTitle || "",
            button.dataset.documentSize || "0",
            false));
    });
    document.querySelectorAll("[data-document-bulk-action]").forEach(button => {
        button.addEventListener("click", event => {
            if (currentSelection().length === 0) { event.preventDefault(); return; }
            configure(button.dataset.documentBulkAction || "restore-selected", "0", "", "0", true);
        });
    });

    form.addEventListener("submit", () => {
        submit.disabled = true;
        submit.setAttribute("aria-busy", "true");
    });
    updateSelection();
})();
