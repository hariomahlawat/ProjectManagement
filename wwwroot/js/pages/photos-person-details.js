(() => {
    'use strict';

    const root = document.querySelector('[data-person-details]');
    if (!root) return;

    const checkboxes = Array.from(root.querySelectorAll('[data-person-face-select]'));
    const correctionButton = root.querySelector('[data-correct-selected]');
    const selectionSummary = root.querySelector('[data-selection-summary]');
    const modal = document.getElementById('appearanceCorrectionModal');
    const form = modal?.querySelector('[data-appearance-correction-form]');
    const hiddenHost = form?.querySelector('[data-selected-face-inputs]');
    const modalSummary = form?.querySelector('[data-modal-selection-summary]');
    const actionSelect = form?.querySelector('[data-correction-action]');
    const targetSection = form?.querySelector('[data-target-person]');
    const targetSelect = targetSection?.querySelector('select');
    const newPersonSection = form?.querySelector('[data-new-person]');
    const newPersonInput = newPersonSection?.querySelector('input');
    const reasonInput = form?.querySelector('textarea[name="reason"]');
    const submitButton = form?.querySelector('[data-correction-submit]');

    const selectedValues = () => checkboxes
        .filter(checkbox => checkbox.checked)
        .map(checkbox => checkbox.value)
        .filter(Boolean);

    function syncSelection() {
        const selected = selectedValues();
        if (selectionSummary) {
            selectionSummary.textContent = `${selected.length} selected`;
        }
        if (correctionButton) {
            correctionButton.disabled = selected.length === 0;
        }
        checkboxes.forEach(checkbox => {
            checkbox.closest('.person-photo-card')?.classList.toggle('is-selected', checkbox.checked);
        });
        return selected;
    }

    function syncAction() {
        if (!actionSelect || !submitButton) return;
        const action = actionSelect.value;
        const requiresTarget = action === 'move';
        const requiresName = action === 'split';

        if (targetSection) targetSection.hidden = !requiresTarget;
        if (newPersonSection) newPersonSection.hidden = !requiresName;
        if (targetSelect) targetSelect.required = requiresTarget;
        if (newPersonInput) newPersonInput.required = requiresName;

        const selected = selectedValues();
        const hasReason = Boolean(reasonInput?.value.trim());
        const hasTarget = !requiresTarget || Boolean(targetSelect?.value);
        const hasName = !requiresName || Boolean(newPersonInput?.value.trim());
        submitButton.disabled = selected.length === 0 || !action || !hasReason || !hasTarget || !hasName;
    }

    checkboxes.forEach(checkbox => checkbox.addEventListener('change', syncSelection));
    syncSelection();

    modal?.addEventListener('show.bs.modal', event => {
        const selected = syncSelection();
        if (selected.length === 0) {
            event.preventDefault();
            return;
        }

        hiddenHost?.replaceChildren(...selected.map(faceId => {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = 'faceIds';
            input.value = faceId;
            return input;
        }));
        if (modalSummary) {
            modalSummary.textContent = `${selected.length} appearance${selected.length === 1 ? '' : 's'} selected.`;
        }
        syncAction();
    });

    modal?.addEventListener('hidden.bs.modal', () => {
        if (form) form.reset();
        if (hiddenHost) hiddenHost.replaceChildren();
        if (targetSection) targetSection.hidden = true;
        if (newPersonSection) newPersonSection.hidden = true;
        if (submitButton) submitButton.disabled = true;
    });

    actionSelect?.addEventListener('change', syncAction);
    targetSelect?.addEventListener('change', syncAction);
    newPersonInput?.addEventListener('input', syncAction);
    reasonInput?.addEventListener('input', syncAction);
})();
