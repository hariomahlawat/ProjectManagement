const bootstrap = window.bootstrap;

if (bootstrap) {
    const createModalEl = document.querySelector('[data-lookup-modal="create"]');
    const editModalEl = document.querySelector('[data-lookup-modal="edit"]');

    const resetForm = (form) => {
        if (!form) {
            return;
        }

        form.reset();

        form.querySelectorAll('.input-validation-error').forEach((input) => {
            input.classList.remove('input-validation-error');
            input.classList.remove('is-invalid');
        });

        form.querySelectorAll('.field-validation-error, .text-danger').forEach((validation) => {
            if (validation.dataset.valmsgFor) {
                validation.textContent = '';
                validation.classList.remove('field-validation-error');
                validation.classList.add('field-validation-valid');
            }
        });

        if (window.jQuery) {
            const validator = window.jQuery(form).data('validator');
            if (validator) {
                validator.resetForm();
            }
        }
    };

    if (createModalEl) {
        const createModal = bootstrap.Modal.getOrCreateInstance(createModalEl);

        if (createModalEl.getAttribute('data-open') === 'true') {
            createModal.show();
            createModalEl.setAttribute('data-open', 'false');
        }

        createModalEl.addEventListener('hidden.bs.modal', () => {
            const form = createModalEl.querySelector('form');
            resetForm(form);
        });
    }

    if (editModalEl) {
        const editModal = bootstrap.Modal.getOrCreateInstance(editModalEl);
        const nameDisplay = editModalEl.querySelector('[data-lookup-edit-name]');

        if (editModalEl.getAttribute('data-open') === 'true') {
            editModal.show();
            editModalEl.setAttribute('data-open', 'false');
        }

        editModalEl.addEventListener('show.bs.modal', (event) => {
            const trigger = event.relatedTarget;
            if (!trigger) {
                return;
            }

            const idInput = editModalEl.querySelector('input[name="EditInput.Id"]');
            const nameInput = editModalEl.querySelector('input[name="EditInput.Name"]');
            const sortInput = editModalEl.querySelector('input[name="EditInput.SortOrder"]');

            if (idInput) {
                idInput.value = trigger.getAttribute('data-unit-id') ?? '';
            }
            if (nameInput) {
                const value = trigger.getAttribute('data-unit-name') ?? '';
                nameInput.value = value;
                nameInput.dispatchEvent(new Event('input', { bubbles: true }));
                if (nameDisplay) {
                    nameDisplay.textContent = value;
                }
            }
            if (sortInput) {
                sortInput.value = trigger.getAttribute('data-unit-sort') ?? '';
                sortInput.dispatchEvent(new Event('input', { bubbles: true }));
            }
        });

        editModalEl.addEventListener('hidden.bs.modal', () => {
            const form = editModalEl.querySelector('form');
            resetForm(form);
            if (nameDisplay) {
                nameDisplay.textContent = '';
            }
        });
    }
}
