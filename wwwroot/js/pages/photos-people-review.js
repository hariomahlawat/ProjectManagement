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

    document.querySelectorAll("[data-group-decision]").forEach(form => {
        if (!(form instanceof HTMLFormElement)) return;
        const checkboxes = Array.from(form.querySelectorAll('input[type="checkbox"][name="faceIds"]'))
            .filter(input => input instanceof HTMLInputElement);
        const count = form.querySelector("[data-selected-count]");
        const toggle = form.querySelector("[data-toggle-group-selection]");

        const update = () => {
            const selected = checkboxes.filter(input => input.checked).length;
            if (count instanceof HTMLElement) {
                count.textContent = `${selected} selected`;
            }
            form.querySelectorAll('button[type="submit"]').forEach(button => {
                if (button instanceof HTMLButtonElement) {
                    button.disabled = selected === 0;
                }
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

            const handler = submitter.getAttribute("formaction") ?? "";
            if (handler.includes("AssignGroup")) {
                const select = form.querySelector('select[name="personId"]');
                if (select instanceof HTMLSelectElement && !select.value) {
                    event.preventDefault();
                    select.focus();
                    select.setCustomValidity("Select an existing person before assigning the group.");
                    select.reportValidity();
                    window.setTimeout(() => select.setCustomValidity(""), 0);
                }
            }
            if (handler.includes("CreateGroup")) {
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
    const submit = form.querySelector("[data-batch-identity-submit]");
    const count = form.querySelector("[data-batch-identity-count]");
    const person = form.querySelector('select[name="personId"]');

    const update = () => {
        const selected = checkboxes.filter(item => item.checked).length;
        if (count instanceof HTMLElement) count.textContent = String(selected);
        if (submit instanceof HTMLButtonElement) submit.disabled = selected === 0;
    };

    checkboxes.forEach(item => item.addEventListener("change", update));
    form.addEventListener("submit", event => {
        const selected = checkboxes.filter(item => item.checked).length;
        if (selected === 0) {
            event.preventDefault();
            return;
        }
        if (person instanceof HTMLSelectElement && !person.value) {
            event.preventDefault();
            person.focus();
            person.setCustomValidity("Select the confirmed person for the selected appearances.");
            person.reportValidity();
            window.setTimeout(() => person.setCustomValidity(""), 0);
        }
    });
    update();
})();
