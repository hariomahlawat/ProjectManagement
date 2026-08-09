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

    document.querySelectorAll('.pi-comment-composer textarea, .pi-note-composer textarea, .pi-comment-edit-form textarea').forEach(textarea => {
        textarea.addEventListener('input', () => autoSize(textarea));
        autoSize(textarea);
    });
})();

// SECTION: Unified General / Conference comment composer.
(() => {
    const composer = document.querySelector('[data-pi-comment-composer]');
    if (!composer) {
        return;
    }

    const typeInput = composer.querySelector('[data-pi-comment-type]');
    const body = composer.querySelector('[data-pi-comment-body]');
    const guidance = composer.querySelector('[data-pi-comment-guidance]');
    const submitLabel = composer.querySelector('[data-pi-comment-submit-label]');
    const options = Array.from(composer.querySelectorAll('[data-pi-comment-type-option]'));

    const applyType = value => {
        const isConference = value === 'Conference';
        if (typeInput) {
            typeInput.value = isConference ? 'Conference' : 'General';
        }

        options.forEach(option => {
            const active = option.getAttribute('data-pi-comment-type-option') === (isConference ? 'Conference' : 'General');
            option.classList.toggle('is-active', active);
            option.setAttribute('aria-pressed', active ? 'true' : 'false');
        });

        composer.classList.toggle('is-conference', isConference);
        if (body) {
            body.placeholder = isConference
                ? 'Record the direction or observation issued during the conference...'
                : 'Write a remark...';
        }
        if (guidance) {
            guidance.textContent = isConference
                ? 'Visible as a command direction in the same discussion record.'
                : 'Share a concise update or question.';
        }
        if (submitLabel) {
            submitLabel.textContent = isConference ? 'Add direction' : 'Send';
        }
    };

    options.forEach(option => {
        option.addEventListener('click', () => {
            applyType(option.getAttribute('data-pi-comment-type-option'));
            body?.focus({ preventScroll: true });
        });
    });

    applyType(typeInput?.value || 'General');
})();

// SECTION: Inline discussion remark editing
(() => {
    const closeEditor = article => {
        const panel = article?.querySelector('[data-pi-comment-edit-panel]');
        const text = article?.querySelector('[data-pi-comment-text]');
        if (!panel) return;
        panel.hidden = true;
        if (text) text.hidden = false;
    };

    document.querySelectorAll('[data-pi-comment-edit]').forEach(button => {
        button.addEventListener('click', () => {
            const article = button.closest('[data-pi-comment-id]');
            const panel = article?.querySelector('[data-pi-comment-edit-panel]');
            const text = article?.querySelector('[data-pi-comment-text]');
            if (!article || !panel) return;

            document.querySelectorAll('[data-pi-comment-id]').forEach(candidate => {
                if (candidate !== article) closeEditor(candidate);
            });

            panel.hidden = false;
            if (text) text.hidden = true;
            const textarea = panel.querySelector('textarea');
            if (textarea) {
                textarea.style.height = 'auto';
                textarea.style.height = `${Math.min(textarea.scrollHeight, 260)}px`;
                textarea.focus({ preventScroll: true });
                textarea.setSelectionRange(textarea.value.length, textarea.value.length);
            }
        });
    });

    document.querySelectorAll('[data-pi-comment-edit-cancel]').forEach(button => {
        button.addEventListener('click', () => {
            const article = button.closest('[data-pi-comment-id]');
            closeEditor(article);
            article?.querySelector('[data-pi-comment-edit]')?.focus({ preventScroll: true });
        });
    });
})();

// SECTION: Discussion remark delete confirmation
(() => {
    document.querySelectorAll('[data-pi-delete-comment]').forEach(form => {
        let confirmed = false;
        form.addEventListener('submit', async event => {
            if (confirmed) return;
            event.preventDefault();

            const isConference = String(form.getAttribute('data-comment-type') || '').toLowerCase() === 'conference';
            const options = isConference
                ? {
                    title: 'Delete Conference direction?',
                    message: 'This direction will be removed from the Idea discussion and will no longer be considered in Conference Review.',
                    detail: 'Historical audit information will be retained.',
                    confirmText: 'Delete direction',
                    cancelText: 'Cancel',
                    tone: 'danger'
                }
                : {
                    title: 'Delete discussion remark?',
                    message: 'This remark will be removed from the Idea discussion.',
                    detail: 'Historical audit information will be retained.',
                    confirmText: 'Delete remark',
                    cancelText: 'Cancel',
                    tone: 'danger'
                };

            const accepted = window.PrismConfirm?.show
                ? await window.PrismConfirm.show(options)
                : window.confirm(`${options.title}\n\n${options.message}`);

            if (!accepted) return;
            confirmed = true;
            form.requestSubmit();
        });
    });
})();

