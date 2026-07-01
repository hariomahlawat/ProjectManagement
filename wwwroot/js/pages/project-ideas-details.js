// SECTION: Project Ideas attachment preview interactions
(() => {
    const modal = document.getElementById('previewDocumentModal');
    if (!modal) {
        return;
    }

    const title = document.getElementById('previewDocumentModalLabel');
    const fileName = document.getElementById('previewDocumentFileName');
    const container = document.getElementById('previewDocumentContainer');
    const download = document.getElementById('previewDocumentDownload');

    modal.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        if (!trigger) {
            return;
        }

        const previewUrl = trigger.getAttribute('data-preview-url');
        const downloadUrl = trigger.getAttribute('data-download-url');
        const name = trigger.getAttribute('data-file-name') || 'Attachment';
        const fileType = trigger.getAttribute('data-file-type');

        title.textContent = fileType === 'pdf' ? 'PDF Preview' : 'Image Preview';
        fileName.textContent = name;
        download.href = downloadUrl || previewUrl || '#';
        container.replaceChildren();

        if (fileType === 'image') {
            const image = document.createElement('img');
            image.src = previewUrl;
            image.alt = name;
            image.className = 'idea-preview-image';
            container.appendChild(image);
            return;
        }

        if (fileType === 'pdf') {
            const frame = document.createElement('iframe');
            frame.src = previewUrl;
            frame.title = name;
            frame.className = 'idea-preview-frame';
            container.appendChild(frame);
            return;
        }

        const fallback = document.createElement('div');
        fallback.className = 'text-muted p-4 text-center';
        fallback.textContent = 'Preview is not available for this file type.';
        container.appendChild(fallback);
    });

    modal.addEventListener('hidden.bs.modal', () => {
        container.replaceChildren();
        download.href = '#';
    });
})();

// SECTION: Attachment delete confirmation
(() => {
    document.querySelectorAll('.js-confirm-delete-attachment').forEach(form => {
        form.addEventListener('submit', event => {
            const fileName = form.getAttribute('data-file-name') || 'this attachment';
            if (!window.confirm(`Delete “${fileName}”? This action cannot be undone.`)) {
                event.preventDefault();
            }
        });
    });
})();

// SECTION: Note composer behaviour
(() => {
    const composer = document.getElementById('noteComposer');
    if (!composer) {
        return;
    }

    const titleInput = composer.querySelector('#NoteTitle');
    const errorAlert = document.querySelector('.alert-danger');

    composer.addEventListener('shown.bs.collapse', () => {
        titleInput?.focus({ preventScroll: true });
        composer.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    });

    if (composer.classList.contains('show') && errorAlert) {
        requestAnimationFrame(() => {
            composer.scrollIntoView({ behavior: 'smooth', block: 'center' });
            titleInput?.focus({ preventScroll: true });
        });
    }
})();

// SECTION: Comfortable textarea growth without manual resizing
(() => {
    const autoSize = textarea => {
        textarea.style.height = 'auto';
        textarea.style.height = `${Math.min(textarea.scrollHeight, 260)}px`;
    };

    document.querySelectorAll('.pi-comment-composer textarea, .pi-note-composer textarea').forEach(textarea => {
        textarea.addEventListener('input', () => autoSize(textarea));
        autoSize(textarea);
    });
})();
