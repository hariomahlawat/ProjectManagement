(() => {
    "use strict";

    const root = document.querySelector("[data-arpp-reconcile]");
    const form = root?.querySelector("[data-arpp-reconciliation-form]");
    if (!root || !form) return;

    const button = root.querySelector("[data-arpp-link-button]");
    const state = root.querySelector("[data-arpp-reconcile-state]");

    const refresh = () => {
        const selected = Array.from(root.querySelectorAll("[data-arpp-project-id]"))
            .filter(input => Number(input.value) > 0).length;
        if (button) button.disabled = selected === 0;
        if (state) state.textContent = selected === 0
            ? "Select one or more confirmed matches."
            : `${selected} ${selected === 1 ? "link" : "links"} ready to save.`;
    };

    root.querySelectorAll("[data-arpp-suggestion]").forEach(suggestion => {
        suggestion.addEventListener("click", () => {
            const card = suggestion.closest("[data-arpp-reconciliation-row]");
            const picker = card?.querySelector("[data-arpp-project-picker]");
            if (!picker || !window.PrismArppProjectPicker) return;
            window.PrismArppProjectPicker.selectProject(picker, {
                id: Number(suggestion.dataset.projectId),
                name: suggestion.dataset.projectName || "",
                caseFileNumber: suggestion.dataset.projectCaseFile || null,
                statusLabel: suggestion.dataset.projectStatus || ""
            });
        });
    });

    root.addEventListener("arpp:project-selected", refresh);
    root.addEventListener("arpp:project-cleared", refresh);
    root.addEventListener("change", event => {
        if (event.target.matches("[data-arpp-project-id]")) refresh();
    });

    form.addEventListener("submit", () => {
        if (button) {
            button.disabled = true;
            button.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Saving…';
        }
    });

    refresh();
})();
