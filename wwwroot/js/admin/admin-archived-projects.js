(() => {
    "use strict";
    const modal = document.getElementById("archiveRestoreModal");
    if (!modal) return;
    const id = modal.querySelector("[data-archive-project-id]");
    const name = modal.querySelector("[data-archive-project-name]");
    const form = modal.querySelector("[data-archive-restore-form]");
    const submit = modal.querySelector("[data-archive-submit]");
    document.querySelectorAll("[data-archive-restore]").forEach(button => {
        button.addEventListener("click", () => {
            id.value = button.dataset.projectId || "0";
            name.textContent = button.dataset.projectName || "Selected project";
        });
    });
    form?.addEventListener("submit", () => { submit.disabled = true; submit.setAttribute("aria-busy", "true"); });
})();
