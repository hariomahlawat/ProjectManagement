'use strict';

// Organisation-wide album forms: keep the create/add interaction compact and explicit.
(() => {
    'use strict';

    const form = document.querySelector('[data-selection-album-form]');
    if (form) {
        const choices = Array.from(form.querySelectorAll('[data-album-choice]'));
        const existingFields = form.querySelector('[data-album-existing-fields]');
        const newFields = form.querySelector('[data-album-new-fields]');
        const existingSelect = existingFields?.querySelector('select[name="albumId"]');
        const newName = newFields?.querySelector('input[name="newAlbumName"]');

        const sync = () => {
            const mode = choices.find(choice => choice.checked)?.value || (choices.length === 0 ? 'new' : 'existing');
            const isNew = mode === 'new';
            if (existingFields) existingFields.hidden = isNew;
            if (newFields) newFields.hidden = !isNew;
            if (existingSelect) existingSelect.disabled = isNew;
            if (newName) {
                newName.disabled = !isNew;
                newName.required = isNew;
            }
        };

        choices.forEach(choice => choice.addEventListener('change', sync));
        sync();
    }

    const captionButton = document.querySelector('[data-info-edit-caption]');
    const captionModal = document.getElementById('editCaptionModal');
    const captionForm = captionModal?.querySelector('[data-caption-form]');
    captionButton?.addEventListener('click', () => {
        if (!captionModal || !captionForm) return;
        const assetInput = captionForm.querySelector('[data-caption-asset-id]');
        const tokenInput = captionForm.querySelector('[data-caption-token]');
        const captionInput = captionForm.querySelector('[data-caption-value]');
        if (assetInput) assetInput.value = captionButton.dataset.assetId || '';
        if (tokenInput) tokenInput.value = captionButton.dataset.token || '';
        if (captionInput) captionInput.value = captionButton.dataset.caption || '';

        // Close the full-screen viewer first so its background-inert contract is restored
        // before Bootstrap moves focus into the editorial modal.
        document.querySelector('[data-photos-viewer] [data-viewer-close]')?.click();
        window.setTimeout(() => {
            if (window.bootstrap?.Modal) window.bootstrap.Modal.getOrCreateInstance(captionModal).show();
        }, 0);
    });
})();

// Album ordering is intentionally confined to explicit Organise mode. The browser sends
// only the ordered asset IDs; the server revalidates album ownership and membership.
(() => {
    'use strict';

    const grid = document.querySelector('[data-album-sortable="true"]');
    const form = document.querySelector('[data-album-reorder-form]');
    const status = document.querySelector('[data-album-organize-status]');
    if (!grid || !form) return;

    let dragging = null;
    let saveTimer = null;
    let saving = false;
    let queued = false;

    const tiles = () => Array.from(grid.querySelectorAll('[data-media-item][data-asset-id]'));
    const setStatus = (message, state = '') => {
        if (!status) return;
        status.textContent = message;
        status.dataset.state = state;
    };

    const save = async () => {
        if (saving) {
            queued = true;
            return;
        }
        const orderedAssetIds = tiles()
            .map(tile => Number.parseInt(tile.dataset.assetId || '', 10))
            .filter(value => Number.isSafeInteger(value) && value > 0);
        if (orderedAssetIds.length === 0) return;

        saving = true;
        queued = false;
        setStatus('Saving album order…', 'saving');
        try {
            const payload = new FormData(form);
            payload.append('albumId', form.dataset.albumId || '');
            orderedAssetIds.forEach(id => payload.append('orderedAssetIds', String(id)));
            const response = await fetch(form.action, {
                method: 'POST',
                body: payload,
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' }
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success) {
                setStatus(result?.message || 'Album order could not be saved. Reload before trying again.', 'error');
                return;
            }
            setStatus('Album order saved.', 'saved');
        } catch {
            setStatus('Album order could not be saved. Check the connection and retry.', 'error');
        } finally {
            saving = false;
            if (queued) void save();
        }
    };

    const queueSave = () => {
        window.clearTimeout(saveTimer);
        saveTimer = window.setTimeout(() => void save(), 300);
    };

    grid.addEventListener('dragstart', event => {
        const tile = event.target.closest('[data-media-item]');
        if (!tile || !tile.dataset.assetId) return;
        dragging = tile;
        tile.classList.add('is-dragging');
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', tile.dataset.assetId);
    });

    grid.addEventListener('dragover', event => {
        if (!dragging) return;
        const target = event.target.closest('[data-media-item]');
        if (!target || target === dragging) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        const rect = target.getBoundingClientRect();
        const before = event.clientY < rect.top + rect.height / 2
            || (Math.abs(event.clientY - (rect.top + rect.height / 2)) < rect.height * .25
                && event.clientX < rect.left + rect.width / 2);
        grid.insertBefore(dragging, before ? target : target.nextSibling);
    });

    grid.addEventListener('drop', event => {
        if (!dragging) return;
        event.preventDefault();
        queueSave();
    });

    grid.addEventListener('dragend', () => {
        if (!dragging) return;
        dragging.classList.remove('is-dragging');
        dragging = null;
        queueSave();
    });
})();
