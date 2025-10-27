(function () {
    function setupDropZone(zone) {
        const input = zone.querySelector('[data-doc-file]');
        const target = zone.querySelector('[data-doc-drop-target]');
        const label = zone.querySelector('[data-doc-drop-label]');
        const status = zone.querySelector('[data-doc-drop-filename]');

        if (!input || !target || !label || !status) {
            return;
        }

        status.setAttribute('aria-live', 'polite');

        const maxSizeRaw = parseInt(input.dataset.maxSize || '0', 10);
        const maxSizeBytes = Number.isNaN(maxSizeRaw) || maxSizeRaw <= 0 ? Number.POSITIVE_INFINITY : maxSizeRaw;
        const maxFilesRaw = parseInt(input.dataset.maxFiles || '0', 10);
        const maxFiles = Number.isNaN(maxFilesRaw) || maxFilesRaw <= 0 ? Number.POSITIVE_INFINITY : maxFilesRaw;
        const allowMultiple = input.hasAttribute('multiple');

        const allowedTypes = new Set((input.dataset.allowed || '')
            .split(',')
            .map((s) => s.trim().toLowerCase())
            .filter(Boolean));
        const allowedExtensions = new Set((input.dataset.extensions || '')
            .split(',')
            .map((s) => s.trim().toLowerCase())
            .filter(Boolean));

        if (allowedExtensions.size === 0 && allowedTypes.has('application/pdf')) {
            allowedExtensions.add('pdf');
        }

        function resetVisualState() {
            target.classList.remove('border-primary', 'bg-light');
        }

        function setFileFeedback(message, isError) {
            status.textContent = message || '';
            if (isError) {
                target.classList.add('border-danger');
                target.classList.remove('border-primary');
            } else {
                target.classList.remove('border-danger');
            }
        }

        function describeFile(file) {
            if (!file) {
                return '';
            }

            const sizeSummary = file.size ? ` (${(file.size / (1024 * 1024)).toFixed(2)} MB)` : '';
            return `${file.name}${sizeSummary}`;
        }

        function validateFile(file) {
            if (!file) {
                return { valid: false, error: 'A file is required.' };
            }

            const fileType = (file.type || '').toLowerCase();
            const extension = file.name.includes('.')
                ? file.name.substring(file.name.lastIndexOf('.') + 1).toLowerCase()
                : '';

            const extensionAllowed = allowedExtensions.size === 0 || allowedExtensions.has(extension);
            const mimeAllowed = allowedTypes.size === 0 || allowedTypes.has(fileType);
            const mimeUnknown = !fileType || fileType === 'application/octet-stream';

            if (!extensionAllowed && allowedExtensions.size > 0) {
                return { valid: false, error: `File extension not allowed: ${file.name}.` };
            }

            if (!mimeAllowed && !mimeUnknown) {
                return { valid: false, error: `File type not allowed: ${file.name}.` };
            }

            if (file.size && file.size > maxSizeBytes) {
                const sizeMb = (maxSizeBytes / (1024 * 1024)).toFixed(0);
                return { valid: false, error: `File must be ${sizeMb} MB or smaller: ${file.name}.` };
            }

            if (file.size === 0) {
                return { valid: false, error: `${file.name} is empty.` };
            }

            return { valid: true };
        }

        function updateFileSelection(fileList, triggerChangeEvent) {
            if (!fileList || fileList.length === 0) {
                input.value = '';
                input.setCustomValidity('');
                setFileFeedback('', false);
                return;
            }

            const files = allowMultiple ? Array.from(fileList) : [fileList[0]];
            const results = files.map(validateFile);
            const errors = results.filter((result) => !result.valid).map((result) => result.error).filter(Boolean);

            if (errors.length > 0) {
                input.value = '';
                input.setCustomValidity(errors[0] || 'Invalid file selection.');
                setFileFeedback(errors.join(' '), true);
                return;
            }

            if (maxFiles !== Number.POSITIVE_INFINITY && files.length > maxFiles) {
                const error = `Select up to ${maxFiles} file${maxFiles === 1 ? '' : 's'}.`;
                input.value = '';
                input.setCustomValidity(error);
                setFileFeedback(error, true);
                return;
            }

            if (triggerChangeEvent && typeof DataTransfer !== 'undefined') {
                const transfer = new DataTransfer();
                files.forEach((file) => transfer.items.add(file));
                input.files = transfer.files;
            }

            input.setCustomValidity('');

            if (allowMultiple) {
                const summary = files.length === 1
                    ? describeFile(files[0])
                    : `${files.length} files ready: ${files.map((file) => file.name).join(', ')}`;
                setFileFeedback(summary, false);
            } else {
                setFileFeedback(describeFile(files[0]), false);
            }

            if (triggerChangeEvent) {
                const changeEvent = new Event('change', { bubbles: true });
                input.dispatchEvent(changeEvent);
            }
        }

        target.addEventListener('click', () => input.click());
        target.addEventListener('keydown', (event) => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                input.click();
            }
        });

        input.addEventListener('change', () => {
            if (input.files && input.files.length > 0) {
                updateFileSelection(input.files, false);
                const event = new Event('input', { bubbles: true });
                input.dispatchEvent(event);
            } else {
                setFileFeedback('', false);
                input.setCustomValidity('');
            }
        });

        target.addEventListener('dragover', (event) => {
            event.preventDefault();
            target.classList.add('border-primary', 'bg-light');
        });

        target.addEventListener('dragleave', (event) => {
            if (event.target === target) {
                resetVisualState();
            }
        });

        target.addEventListener('drop', (event) => {
            event.preventDefault();
            resetVisualState();
            if (event.dataTransfer && event.dataTransfer.files && event.dataTransfer.files.length > 0) {
                updateFileSelection(event.dataTransfer.files, true);
            }
        });
    }

    document.querySelectorAll('[data-doc-dropzone]').forEach(setupDropZone);
})();
