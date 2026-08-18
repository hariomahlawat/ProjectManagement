(() => {
    "use strict";

    const modal = document.getElementById("assignExistingModal");
    if (modal) {
        modal.addEventListener("show.bs.modal", event => {
            const trigger = event.relatedTarget;
            if (!(trigger instanceof HTMLElement)) return;

            const faceIdInput = modal.querySelector('input[name="faceId"]');
            const context = modal.querySelector("[data-face-context]");
            const personSelect = modal.querySelector('select[name="personId"]');

            if (faceIdInput instanceof HTMLInputElement) {
                faceIdInput.value = trigger.dataset.faceId ?? "";
            }
            if (context instanceof HTMLElement) {
                const title = trigger.dataset.faceContext?.trim();
                context.textContent = title
                    ? `Choose the confirmed identity for the face detected in “${title}”.`
                    : "Choose the confirmed identity for this face.";
            }
            if (personSelect instanceof HTMLSelectElement) {
                personSelect.value = "";
            }
        });

        modal.addEventListener("shown.bs.modal", () => {
            const personSelect = modal.querySelector('select[name="personId"]');
            if (personSelect instanceof HTMLSelectElement) {
                personSelect.focus();
            }
        });
    }

    document.querySelectorAll("[data-use-group-candidate]").forEach(button => {
        if (!(button instanceof HTMLButtonElement)) return;
        button.addEventListener("click", () => {
            const targetId = button.dataset.targetSelect;
            const personId = button.dataset.personId;
            if (!targetId || !personId) return;
            const select = document.getElementById(targetId);
            if (!(select instanceof HTMLSelectElement)) return;
            select.value = personId;
            select.focus({ preventScroll: true });
            select.scrollIntoView({ block: "nearest", behavior: "smooth" });
        });
    });

    document.querySelectorAll("[data-group-decision]").forEach(form => {
        if (!(form instanceof HTMLFormElement)) return;
        const checkboxes = Array.from(form.querySelectorAll('input[type="checkbox"][name="faceIds"]'))
            .filter(input => input instanceof HTMLInputElement);
        const count = form.querySelector("[data-selected-count]");
        const toggle = form.querySelector("[data-toggle-group-selection]");
        const externalCandidateButtons = form.id
            ? Array.from(document.querySelectorAll(`[form="${CSS.escape(form.id)}"][data-group-candidate-submit]`))
                .filter(button => button instanceof HTMLButtonElement)
            : [];

        const update = () => {
            const selected = checkboxes.filter(input => input.checked).length;
            const allSelected = checkboxes.length > 0 && selected === checkboxes.length;
            if (count instanceof HTMLElement) {
                count.textContent = `${selected} selected`;
            }
            if (toggle instanceof HTMLButtonElement) {
                toggle.textContent = allSelected ? "Clear all" : "Select all";
            }
            form.querySelectorAll('button[type="submit"]').forEach(button => {
                if (button instanceof HTMLButtonElement) {
                    button.disabled = selected === 0;
                }
            });
            externalCandidateButtons.forEach(button => {
                button.disabled = selected === 0;
            });
        };

        checkboxes.forEach(input => input.addEventListener("change", update));
        if (toggle instanceof HTMLButtonElement) {
            toggle.addEventListener("click", () => {
                const allSelected = checkboxes.length > 0 && checkboxes.every(input => input.checked);
                checkboxes.forEach(input => {
                    input.checked = !allSelected;
                });
                update();
            });
        }

        form.addEventListener("submit", event => {
            const submitter = event.submitter;
            if (!(submitter instanceof HTMLButtonElement)) return;
            const selected = checkboxes.filter(input => input.checked).length;
            if (selected === 0) {
                event.preventDefault();
                return;
            }

            const action = submitter.formAction || submitter.getAttribute("formaction") || "";
            if (action.includes("handler=AssignGroup") || action.includes("handler%3DAssignGroup")) {
                const select = form.querySelector('select[name="personId"]');
                if (select instanceof HTMLSelectElement && !select.value) {
                    event.preventDefault();
                    select.focus();
                    select.setCustomValidity("Select an existing person before assigning the selected appearances.");
                    select.reportValidity();
                    window.setTimeout(() => select.setCustomValidity(""), 0);
                }
            }
            if (action.includes("handler=CreateGroup") || action.includes("handler%3DCreateGroup")) {
                const name = form.querySelector('input[name="displayName"]');
                if (name instanceof HTMLInputElement && !name.value.trim()) {
                    event.preventDefault();
                    name.focus();
                    name.setCustomValidity("Enter the person's name before creating the identity.");
                    name.reportValidity();
                    window.setTimeout(() => name.setCustomValidity(""), 0);
                }
            }
        });

        update();
    });
})();

(() => {
    "use strict";

    const form = document.querySelector("[data-batch-identity-form]");
    if (!(form instanceof HTMLFormElement)) return;

    const checkboxes = Array.from(document.querySelectorAll("[data-batch-identity-face]"))
        .filter(item => item instanceof HTMLInputElement);
    const count = form.querySelector("[data-batch-identity-count]");
    const person = form.querySelector('select[name="personId"]');
    const selectAll = form.querySelector("[data-batch-select-all]");
    const clear = form.querySelector("[data-batch-clear]");
    const submitButtons = Array.from(form.querySelectorAll('button[type="submit"]'))
        .filter(button => button instanceof HTMLButtonElement);

    const update = () => {
        const selected = checkboxes.filter(item => item.checked).length;
        if (count instanceof HTMLElement) count.textContent = String(selected);
        submitButtons.forEach(button => {
            button.disabled = selected === 0;
        });
        if (selectAll instanceof HTMLButtonElement) {
            selectAll.textContent = selected === checkboxes.length && checkboxes.length > 0
                ? "All visible selected"
                : "Select all visible";
        }
    };

    checkboxes.forEach(item => item.addEventListener("change", update));
    selectAll?.addEventListener("click", () => {
        checkboxes.forEach(item => { item.checked = true; });
        update();
    });
    clear?.addEventListener("click", () => {
        checkboxes.forEach(item => { item.checked = false; });
        update();
    });

    form.addEventListener("submit", event => {
        const submitter = event.submitter;
        if (!(submitter instanceof HTMLButtonElement)) return;
        const selected = checkboxes.filter(item => item.checked).length;
        if (selected === 0) {
            event.preventDefault();
            return;
        }

        if (submitter.hasAttribute("data-requires-person")
            && person instanceof HTMLSelectElement
            && !person.value) {
            event.preventDefault();
            person.focus();
            person.setCustomValidity("Select the confirmed person for the selected appearances.");
            person.reportValidity();
            window.setTimeout(() => person.setCustomValidity(""), 0);
        }
    });

    update();
})();
