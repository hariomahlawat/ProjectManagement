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

        const maxSize = parseInt(input.dataset.maxSize || '0', 10) || Number.POSITIVE_INFINITY;
        const allowed = (input.dataset.allowed || '')
            .split(',')
            .map(s => s.trim().toLowerCase())
            .filter(Boolean);

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

        function validateFile(file) {
            if (!file) {
                input.setCustomValidity('');
                setFileFeedback('', false);
                return true;
            }

            const fileType = (file.type || '').toLowerCase();
            const extension = file.name.toLowerCase().endsWith('.pdf');
            const mimeAllowed = allowed.length === 0 || allowed.includes(fileType) || fileType === 'application/pdf';

            if (!mimeAllowed || !extension) {
                const error = 'Only PDF files are allowed.';
                input.setCustomValidity(error);
                setFileFeedback(error, true);
                return false;
            }

            if (file.size && file.size > maxSize) {
                const sizeMb = (maxSize / (1024 * 1024)).toFixed(0);
                const error = `File must be ${sizeMb} MB or smaller.`;
                input.setCustomValidity(error);
                setFileFeedback(error, true);
                return false;
            }

            input.setCustomValidity('');
            const sizeSummary = file.size ? ` (${(file.size / (1024 * 1024)).toFixed(2)} MB)` : '';
            setFileFeedback(`${file.name}${sizeSummary}`, false);
            return true;
        }

        function updateFileSelection(fileList) {
            if (!fileList || fileList.length === 0) {
                input.value = '';
                input.setCustomValidity('');
                setFileFeedback('', false);
                return;
            }

            const [file] = fileList;
            if (!validateFile(file)) {
                input.value = '';
                return;
            }

            if (typeof DataTransfer !== 'undefined') {
                const transfer = new DataTransfer();
                transfer.items.add(file);
                input.files = transfer.files;
            }

            const event = new Event('change', { bubbles: true });
            input.dispatchEvent(event);
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
                validateFile(input.files[0]);
                const event = new Event('input', { bubbles: true });
                input.dispatchEvent(event);
            } else {
                setFileFeedback('', false);
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
                updateFileSelection(event.dataTransfer.files);
            }
        });
    }

    document.querySelectorAll('[data-doc-dropzone]').forEach(setupDropZone);
})();
