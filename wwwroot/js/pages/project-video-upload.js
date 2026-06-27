const form = document.querySelector('[data-video-upload-form]');

if (form instanceof HTMLFormElement) {
    const input = form.querySelector('[data-video-file-input]');
    const dropzone = form.querySelector('[data-video-dropzone]');
    const selected = form.querySelector('[data-video-selected]');
    const fileName = form.querySelector('[data-video-file-name]');
    const fileDetails = form.querySelector('[data-video-file-details]');
    const replaceButton = form.querySelector('[data-video-replace]');
    const removeButton = form.querySelector('[data-video-remove]');
    const error = form.querySelector('[data-video-error]');
    const submit = form.querySelector('[data-video-submit]');
    const maxSize = Number(form.dataset.maxSize || 0);
    const allowedExtensions = new Set(['mp4', 'webm', 'ogg', 'ogv']);

    if (input instanceof HTMLInputElement && submit instanceof HTMLButtonElement) {
        submit.disabled = input.files?.length !== 1;

        input.addEventListener('change', () => showFile(input.files?.[0]));
        replaceButton?.addEventListener('click', () => input.click());
        removeButton?.addEventListener('click', clearFile);

        for (const eventName of ['dragenter', 'dragover']) {
            dropzone?.addEventListener(eventName, event => {
                event.preventDefault();
                dropzone.classList.add('is-dragging');
            });
        }

        for (const eventName of ['dragleave', 'drop']) {
            dropzone?.addEventListener(eventName, event => {
                event.preventDefault();
                dropzone.classList.remove('is-dragging');
            });
        }

        dropzone?.addEventListener('drop', event => {
            const file = event.dataTransfer?.files?.[0];
            if (!file) return;

            const transfer = new DataTransfer();
            transfer.items.add(file);
            input.files = transfer.files;
            showFile(file);
        });

        form.addEventListener('submit', event => {
            if (!validateFile(input.files?.[0])) {
                event.preventDefault();
            }
        });
    }

    function showFile(file) {
        if (!validateFile(file)) {
            clearFile(false);
            return;
        }

        if (fileName) fileName.textContent = file.name;
        if (fileDetails) fileDetails.textContent = `${formatSize(file.size)} · Ready to upload`;
        if (selected instanceof HTMLElement) selected.hidden = false;
        if (dropzone instanceof HTMLElement) dropzone.hidden = true;
        if (submit instanceof HTMLButtonElement) submit.disabled = false;
    }

    function validateFile(file) {
        hideError();
        if (!file) {
            showError('Choose a video to upload.');
            return false;
        }

        const extension = file.name.includes('.') ? file.name.split('.').pop().toLowerCase() : '';
        if (!allowedExtensions.has(extension)) {
            showError('Choose an MP4, WebM or OGG video.');
            return false;
        }

        if (maxSize > 0 && file.size > maxSize) {
            showError(`Choose a video smaller than ${formatSize(maxSize)}.`);
            return false;
        }

        return true;
    }

    function clearFile(clearError = true) {
        if (input instanceof HTMLInputElement) input.value = '';
        if (selected instanceof HTMLElement) selected.hidden = true;
        if (dropzone instanceof HTMLElement) dropzone.hidden = false;
        if (submit instanceof HTMLButtonElement) submit.disabled = true;
        if (clearError) hideError();
    }

    function showError(message) {
        if (!(error instanceof HTMLElement)) return;
        error.textContent = message;
        error.hidden = false;
    }

    function hideError() {
        if (!(error instanceof HTMLElement)) return;
        error.textContent = '';
        error.hidden = true;
    }

    function formatSize(bytes) {
        if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
        const units = ['B', 'KB', 'MB', 'GB'];
        let value = bytes;
        let unit = 0;
        while (value >= 1024 && unit < units.length - 1) {
            value /= 1024;
            unit += 1;
        }
        return `${value.toFixed(value >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
    }
}
