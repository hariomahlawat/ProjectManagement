(() => {
    'use strict';

    const roots = () => Array.from(document.querySelectorAll('[data-at-v2-task-root]'));

    function closeActionPanels(root) {
        root.querySelectorAll('[data-at-v22-panel], [data-at-v2-panel]').forEach((panel) => {
            panel.hidden = true;
        });
    }

    function findActionPanel(root, name) {
        return root.querySelector(`[data-at-v22-panel="${name}"]`)
            || root.querySelector(`[data-at-v2-panel="${name}"]`);
    }

    function openActionPanel(root, name) {
        const panel = findActionPanel(root, name);
        if (!panel) return;

        closeActionPanels(root);
        panel.hidden = false;
        const target = panel.querySelector('textarea, select, input:not([type="hidden"]), button');
        window.setTimeout(() => target?.focus({ preventScroll: true }), 60);
    }

    function initActionPanels() {
        document.addEventListener('click', (event) => {
            const opener = event.target.closest('[data-at-v22-open], [data-at-v2-open]');
            if (opener) {
                const root = opener.closest('[data-at-v2-task-root]');
                if (!root) return;
                event.preventDefault();
                const name = opener.getAttribute('data-at-v22-open') || opener.getAttribute('data-at-v2-open');
                openActionPanel(root, name);
                return;
            }

            const cancel = event.target.closest('[data-at-v22-cancel], [data-at-v2-cancel]');
            if (!cancel) return;
            const root = cancel.closest('[data-at-v2-task-root]');
            if (!root) return;
            event.preventDefault();
            closeActionPanels(root);
        });

        document.addEventListener('keydown', (event) => {
            if (event.key !== 'Escape') return;
            roots().forEach(closeActionPanels);
        });
    }

    // Kept for compatible legacy controls elsewhere in the Task module. V2.2
    // deliberately avoids automatic page movement so inline actions stay in place.
    function closeInlineEditors(root, exceptName) {
        root.querySelectorAll('[data-at-v2-inline-panel]').forEach((panel) => {
            if (panel.getAttribute('data-at-v2-inline-panel') !== exceptName) {
                panel.hidden = true;
            }
        });
    }

    function openInlineEditor(root, name) {
        const panel = root.querySelector(`[data-at-v2-inline-panel="${name}"]`);
        if (!panel) return;
        closeInlineEditors(root, name);
        panel.hidden = false;
        const target = panel.querySelector('input:not([type="hidden"]), select, textarea, button');
        window.setTimeout(() => target?.focus({ preventScroll: true }), 50);
    }

    function initInlineEditors() {
        document.addEventListener('click', (event) => {
            const opener = event.target.closest('[data-at-v2-inline-toggle]');
            if (opener) {
                const root = opener.closest('[data-at-v2-task-root]');
                if (!root) return;
                event.preventDefault();
                openInlineEditor(root, opener.getAttribute('data-at-v2-inline-toggle'));
                return;
            }

            const cancel = event.target.closest('[data-at-v2-inline-cancel]');
            if (!cancel) return;
            const panel = cancel.closest('[data-at-v2-inline-panel]');
            if (!panel) return;
            event.preventDefault();
            panel.hidden = true;
        });
    }

    function initRemarkComposers() {
        document.querySelectorAll('[data-at-v2-remark-composer]').forEach((form) => {
            const typeInput = form.querySelector('[data-at-v2-remark-type]');
            const options = Array.from(form.querySelectorAll('[data-at-v2-remark-option]'));
            const body = form.querySelector('[data-at-v2-remark-body]');
            const fileInput = form.querySelector('[data-at-v2-file-input]');
            const fileSummary = form.querySelector('[data-at-v2-file-summary]');

            const applyType = (value) => {
                if (!typeInput) return;
                const normalized = value === 'Conference' ? 'Conference' : 'Comment';
                typeInput.value = normalized;
                options.forEach((option) => {
                    const active = option.getAttribute('data-at-v2-remark-option') === normalized;
                    option.classList.toggle('is-active', active);
                    option.setAttribute('aria-pressed', active ? 'true' : 'false');
                });
                form.classList.toggle('is-conference', normalized === 'Conference');
                if (body) {
                    body.placeholder = normalized === 'Conference'
                        ? 'Record the conference direction…'
                        : 'Add a remark…';
                }
            };

            options.forEach((option) => {
                option.addEventListener('click', () => {
                    applyType(option.getAttribute('data-at-v2-remark-option'));
                    body?.focus({ preventScroll: true });
                });
            });

            if (typeInput) {
                applyType(typeInput.value || 'Comment');
            }

            const clearComposerValidity = () => body?.setCustomValidity('');
            const validateComposer = () => {
                if (!body) return true;

                clearComposerValidity();
                const text = body.value.trim();
                const fileCount = fileInput?.files?.length || 0;
                const isConference = typeInput?.value === 'Conference';

                if (isConference && !text) {
                    body.setCustomValidity('Enter the conference direction or observation.');
                } else if (!text && fileCount === 0) {
                    body.setCustomValidity('Enter a remark or attach at least one file.');
                }

                if (!body.checkValidity()) {
                    body.reportValidity();
                    body.focus({ preventScroll: false });
                    return false;
                }

                return true;
            };

            body?.addEventListener('input', clearComposerValidity);
            form.addEventListener('submit', (event) => {
                if (!validateComposer()) {
                    event.preventDefault();
                }
            });

            body?.addEventListener('keydown', (event) => {
                if (!(event.ctrlKey || event.metaKey) || event.key !== 'Enter') return;
                event.preventDefault();
                if (validateComposer()) {
                    form.requestSubmit();
                }
            });

            fileInput?.addEventListener('change', () => {
                clearComposerValidity();
                if (!fileSummary) return;
                const count = fileInput.files?.length || 0;
                if (count === 0) {
                    fileSummary.textContent = '';
                } else if (count === 1) {
                    fileSummary.textContent = fileInput.files[0].name;
                } else {
                    fileSummary.textContent = `${count} files selected`;
                }
            });
        });
    }

    function initRemarkActions() {
        document.addEventListener('click', (event) => {
            const editToggle = event.target.closest('[data-at-update-edit-toggle]');
            if (editToggle) {
                event.preventDefault();
                const card = editToggle.closest('[data-at-update-card]');
                const panel = card?.querySelector('[data-at-update-edit-panel]');
                if (!panel) return;
                const willOpen = panel.hidden;
                document.querySelectorAll('[data-at-update-edit-panel]').forEach((candidate) => {
                    candidate.hidden = true;
                });
                panel.hidden = !willOpen;
                if (willOpen) {
                    window.setTimeout(() => panel.querySelector('textarea')?.focus({ preventScroll: true }), 40);
                }
                return;
            }

            const editCancel = event.target.closest('[data-at-update-edit-cancel]');
            if (editCancel) {
                event.preventDefault();
                const panel = editCancel.closest('[data-at-update-edit-panel]');
                if (panel) panel.hidden = true;
            }
        });

        document.addEventListener('submit', (event) => {
            const form = event.target.closest('[data-at-update-delete-form]');
            if (!form) return;
            const message = form.getAttribute('data-confirm') || 'Delete this remark?';
            if (!window.confirm(message)) {
                event.preventDefault();
            }
        });
    }

    function initTaskIntent() {
        const panelIntents = new Set([
            'submit', 'block', 'return', 'accept-close', 'close-direct', 'change-date',
            'edit-task', 'reassign', 'priority', 'assign-sprint', 'add-sprint', 'remove-sprint', 'backlog'
        ]);

        roots().forEach((root) => {
            const intent = (root.getAttribute('data-at-task-intent') || '').trim().toLowerCase();
            if (!intent) return;

            if (intent === 'remark') {
                const composer = root.querySelector('[data-at-v2-remark-composer]');
                const body = composer?.querySelector('[data-at-v2-remark-body]');
                window.setTimeout(() => body?.focus({ preventScroll: false }), 80);
                return;
            }

            if (panelIntents.has(intent)) {
                window.setTimeout(() => openActionPanel(root, intent), 40);
            }
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        initActionPanels();
        initInlineEditors();
        initRemarkComposers();
        initRemarkActions();
        initTaskIntent();
    });
})();
