(function () {
    const input = document.querySelector('[data-docrepo-upload-input]');
    const frame = document.querySelector('[data-docrepo-preview-frame]');
    const placeholder = document.querySelector('[data-docrepo-preview-placeholder]');

    if (!input || !frame) {
        return;
    }

    // SECTION: Upload constraints
    const maxSizeRaw = parseInt(input.dataset.maxSize || '0', 10);
    const maxSizeBytes = Number.isNaN(maxSizeRaw) || maxSizeRaw <= 0 ? Number.POSITIVE_INFINITY : maxSizeRaw;

    const showPlaceholder = () => {
        frame.classList.add('d-none');
        frame.removeAttribute('src');
        if (placeholder) {
            placeholder.classList.remove('d-none');
        }
    };

    const showFrame = (url) => {
        if (placeholder) {
            placeholder.classList.add('d-none');
        }
        frame.classList.remove('d-none');
        frame.src = url;
        frame.onload = () => URL.revokeObjectURL(url);
    };

    if (!frame.getAttribute('src')) {
        frame.classList.add('d-none');
    } else if (placeholder) {
        placeholder.classList.add('d-none');
    }

    input.addEventListener('change', () => {
        const file = input.files && input.files[0];
        if (!file) {
            showPlaceholder();
            input.setCustomValidity('');
            return;
        }

        // SECTION: Client-side size validation
        if (file.size && file.size > maxSizeBytes) {
            const sizeMb = Math.max(1, Math.round(maxSizeBytes / (1024 * 1024)));
            const message = `File must be ${sizeMb} MB or smaller.`;
            input.setCustomValidity(message);
            input.reportValidity();
            input.value = '';
            showPlaceholder();
            return;
        }

        input.setCustomValidity('');
        const url = URL.createObjectURL(file);
        showFrame(url);
    });
})();
