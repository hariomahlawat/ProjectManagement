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
        container.innerHTML = '';

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
        container.innerHTML = '';
        download.href = '#';
    });
})();

// SECTION: Project Ideas attachment delete confirmation
(() => {
    document.querySelectorAll('.js-confirm-delete-attachment').forEach(form => {
        form.addEventListener('submit', event => {
            if (!window.confirm('Delete this attachment?')) {
                event.preventDefault();
            }
        });
    });
})();
