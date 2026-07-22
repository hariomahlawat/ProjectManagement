"use strict";

(() => {
    const input = document.getElementById("pf-report-project-total");
    const hidden = document.getElementById("pf-report-project-total-id");
    const suggestions = document.getElementById("pf-report-project-total-suggestions");
    const clearButton = document.getElementById("pf-report-project-total-clear");
    const status = document.getElementById("pf-report-project-total-status");
    const openButton = document.getElementById("pf-report-open-project");
    if (!input || !hidden || !suggestions || !openButton || !window.ProliferationProjectPicker) return;

    const picker = new window.ProliferationProjectPicker({
        input,
        hiddenInput: hidden,
        suggestions,
        clearButton,
        statusElement: status,
        minimumLength: 0,
        onSelected: project => {
            openButton.disabled = false;
            openButton.dataset.projectId = String(project.id);
        },
        onCleared: () => {
            openButton.disabled = true;
            delete openButton.dataset.projectId;
        }
    });

    openButton.addEventListener("click", () => {
        const id = Number(hidden.value || openButton.dataset.projectId);
        if (Number.isInteger(id) && id > 0) {
            window.location.assign(`/ProjectOfficeReports/Proliferation/Project/${id}`);
        }
    });

    input.addEventListener("keydown", event => {
        if (event.key === "Enter" && hidden.value) {
            event.preventDefault();
            openButton.click();
        }
    });

    window.addEventListener("pagehide", () => picker.destroy(), { once: true });
})();
