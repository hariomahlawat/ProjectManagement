const root = document.querySelector('[data-officer-conference]');

if (root) {
    const antiforgeryToken = root.querySelector('.oc-antiforgery input[name="__RequestVerificationToken"]')?.value ?? '';
    const selector = root.querySelector('[data-officer-selector]');
    let openEditor = null;

    const formatIstDate = (value) => {
        if (!value) return '';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '';
        return new Intl.DateTimeFormat('en-GB', {
            timeZone: 'Asia/Kolkata',
            day: '2-digit',
            month: 'short',
            year: 'numeric'
        }).format(date);
    };

    const setPageFeedback = (message) => {
        const feedback = root.querySelector('[data-oc-page-feedback]');
        if (!feedback) return;
        feedback.textContent = message;
        window.clearTimeout(setPageFeedback.timeoutId);
        setPageFeedback.timeoutId = window.setTimeout(() => {
            feedback.textContent = '';
        }, 3000);
    };

    const closeEditor = (editor, clear = true) => {
        if (!editor) return;
        editor.hidden = true;
        editor.classList.remove('is-saving');
        const input = editor.querySelector('[data-oc-input]');
        const feedback = editor.querySelector('[data-oc-feedback]');
        if (clear && input) input.value = '';
        if (feedback) {
            feedback.textContent = '';
            feedback.classList.remove('is-error');
        }
        const save = editor.querySelector('[data-oc-save]');
        if (save) save.disabled = true;
        if (openEditor === editor) openEditor = null;
    };

    const openInlineEditor = (item) => {
        const editor = item.querySelector('[data-oc-editor]');
        if (!editor) return;
        if (openEditor && openEditor !== editor) closeEditor(openEditor);
        editor.hidden = false;
        openEditor = editor;
        const input = editor.querySelector('[data-oc-input]');
        requestAnimationFrame(() => input?.focus());
    };

    const buildDirection = (direction) => {
        const wrapper = document.createElement('div');
        wrapper.className = 'oc-direction';
        wrapper.dataset.ocDirectionContent = '';

        const label = document.createElement('div');
        label.className = 'oc-direction__label';

        const badge = document.createElement('span');
        const icon = document.createElement('i');
        icon.className = 'bi bi-journal-check';
        icon.setAttribute('aria-hidden', 'true');
        badge.append(icon, document.createTextNode('Conference'));

        const meta = document.createElement('small');
        meta.textContent = `${direction.authorRole} · ${formatIstDate(direction.createdAtUtc)}`;
        label.append(badge, meta);

        const body = document.createElement('p');
        body.dataset.ocDirectionBody = '';
        body.textContent = direction.body;
        body.title = direction.body;

        const detail = document.createElement('div');
        detail.className = 'oc-direction__meta';
        const author = document.createElement('span');
        author.dataset.ocDirectionAuthor = '';
        author.textContent = direction.authorName;
        const snapshot = document.createElement('span');
        snapshot.dataset.ocDirectionSnapshot = '';
        snapshot.textContent = `${direction.snapshotLabel}: ${direction.snapshotValue}`;
        detail.append(author, snapshot);

        wrapper.append(label, body, detail);
        return wrapper;
    };

    const applySavedDirection = (item, payload) => {
        const directionHost = item.querySelector('[data-oc-direction]');
        if (directionHost) {
            directionHost.replaceChildren(buildDirection(payload.direction));
        }

        const progress = item.querySelector('[data-oc-progress-summary]');
        if (progress) progress.textContent = payload.progressSummary ?? '';

        const latest = item.querySelector('[data-oc-latest-progress]');
        if (latest) {
            const text = payload.latestProgressText ?? '';
            latest.textContent = text ? `Latest: ${text}` : '';
            latest.hidden = !text;
            latest.title = text;
        }

        item.classList.remove('is-saved');
        void item.offsetWidth;
        item.classList.add('is-saved');
        window.setTimeout(() => item.classList.remove('is-saved'), 1200);
    };

    const saveDirection = async (editor) => {
        if (editor.classList.contains('is-saving')) return;

        const item = editor.closest('[data-oc-item]');
        const input = editor.querySelector('[data-oc-input]');
        const save = editor.querySelector('[data-oc-save]');
        const feedback = editor.querySelector('[data-oc-feedback]');
        const body = input?.value.trim() ?? '';
        if (!item || !input || !save || !body) return;

        editor.classList.add('is-saving');
        save.disabled = true;
        if (feedback) {
            feedback.textContent = 'Saving…';
            feedback.classList.remove('is-error');
        }

        const data = new FormData(editor);
        if (antiforgeryToken && !data.has('__RequestVerificationToken')) {
            data.append('__RequestVerificationToken', antiforgeryToken);
        }

        try {
            const response = await fetch(`${window.location.pathname}?handler=Add`, {
                method: 'POST',
                body: data,
                headers: antiforgeryToken ? { 'X-CSRF-TOKEN': antiforgeryToken } : {},
                credentials: 'same-origin'
            });

            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(payload.message || 'The direction could not be saved.');
            }

            applySavedDirection(item, payload);
            closeEditor(editor);
            setPageFeedback('Conference direction saved.');
        } catch (error) {
            editor.classList.remove('is-saving');
            save.disabled = false;
            if (feedback) {
                feedback.textContent = error instanceof Error ? error.message : 'The direction could not be saved.';
                feedback.classList.add('is-error');
            }
            input.focus();
        }
    };

    selector?.addEventListener('change', () => {
        const destination = selector.value;
        if (!destination) return;
        window.location.assign(destination);
    });

    root.addEventListener('click', (event) => {
        const addButton = event.target.closest('[data-oc-add]');
        if (addButton) {
            openInlineEditor(addButton.closest('[data-oc-item]'));
            return;
        }

        const cancelButton = event.target.closest('[data-oc-cancel]');
        if (cancelButton) {
            closeEditor(cancelButton.closest('[data-oc-editor]'));
        }
    });

    root.addEventListener('input', (event) => {
        const input = event.target.closest('[data-oc-input]');
        if (!input) return;
        const editor = input.closest('[data-oc-editor]');
        const save = editor?.querySelector('[data-oc-save]');
        if (save) save.disabled = input.value.trim().length === 0;
    });

    root.addEventListener('keydown', (event) => {
        const input = event.target.closest('[data-oc-input]');
        if (!input) return;
        const editor = input.closest('[data-oc-editor]');
        if (!editor) return;

        if (event.key === 'Escape') {
            if (editor.classList.contains('is-saving')) return;
            event.preventDefault();
            closeEditor(editor);
            return;
        }

        if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
            event.preventDefault();
            void saveDirection(editor);
        }
    });

    root.addEventListener('submit', (event) => {
        const editor = event.target.closest('[data-oc-editor]');
        if (!editor) return;
        event.preventDefault();
        void saveDirection(editor);
    });
}
