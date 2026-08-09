(() => {
    'use strict';

    const roots = () => Array.from(document.querySelectorAll('[data-at-v2-task-root]'));

    function closeCommandPanels(root) {
        root.querySelectorAll('[data-at-v2-panel]').forEach((panel) => {
            panel.hidden = true;
        });
    }

    function openCommandPanel(root, name) {
        const panel = root.querySelector(`[data-at-v2-panel="${name}"]`);
        if (!panel) return;

        closeCommandPanels(root);
        panel.hidden = false;
        const target = panel.querySelector('textarea, select, input:not([type="hidden"]), button');
        target?.focus({ preventScroll: false });
        panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function initCommandPanels() {
        document.addEventListener('click', (event) => {
            const opener = event.target.closest('[data-at-v2-open]');
            if (opener) {
                const root = opener.closest('[data-at-v2-task-root]');
                if (!root) return;
                event.preventDefault();
                openCommandPanel(root, opener.getAttribute('data-at-v2-open'));
                return;
            }

            const cancel = event.target.closest('[data-at-v2-cancel]');
            if (!cancel) return;
            const root = cancel.closest('[data-at-v2-task-root]');
            if (!root) return;
            event.preventDefault();
            closeCommandPanels(root);
        });

        document.addEventListener('keydown', (event) => {
            if (event.key !== 'Escape') return;
            roots().forEach(closeCommandPanels);
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

    function initTaskIntent() {
        roots().forEach((root) => {
            const intent = (root.getAttribute('data-at-task-intent') || '').trim().toLowerCase();
            if (!intent) return;

            if (intent === 'remark') {
                const composer = root.querySelector('[data-at-v2-remark-composer]');
                const body = composer?.querySelector('[data-at-v2-remark-body]');
                composer?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                window.setTimeout(() => body?.focus({ preventScroll: true }), 150);
                return;
            }

            if (['submit', 'block', 'return', 'close'].includes(intent)) {
                window.setTimeout(() => openCommandPanel(root, intent), 50);
            }
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        initCommandPanels();
        initRemarkComposers();
        initTaskIntent();
    });
})();
