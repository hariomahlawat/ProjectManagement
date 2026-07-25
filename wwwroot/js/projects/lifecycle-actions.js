(() => {
    'use strict';

    function initialiseLifecycleActions(root) {
        if (!(root instanceof HTMLElement) || root.dataset.lifecycleReady === 'true') {
            return;
        }

        root.dataset.lifecycleReady = 'true';

        const defaultAction = root.dataset.defaultAction || 'none';
        const tabs = [...root.querySelectorAll('[data-lifecycle-action-tab]')];
        const panes = [...root.querySelectorAll('[data-lifecycle-action-pane]')];
        const footerActions = [...root.querySelectorAll('[data-lifecycle-footer-action]')];
        const completionForm = root.querySelector('[data-project-completion-form]');
        const precisionOptions = completionForm
            ? [...completionForm.querySelectorAll('[data-completion-precision]')]
            : [];
        const completionFields = completionForm
            ? [...completionForm.querySelectorAll('[data-completion-field]')]
            : [];

        let activeAction = defaultAction;
        let submitting = false;

        function activateAction(action, focusTab = false) {
            if (!action || action === 'none') {
                return;
            }

            activeAction = action;

            tabs.forEach((tab) => {
                const active = tab.dataset.lifecycleActionTab === action;
                tab.classList.toggle('is-active', active);
                tab.setAttribute('aria-selected', active ? 'true' : 'false');
                tab.tabIndex = active ? 0 : -1;
                if (active && focusTab) {
                    tab.focus();
                }
            });

            panes.forEach((pane) => {
                const active = pane.dataset.lifecycleActionPane === action;
                pane.classList.toggle('d-none', !active);
            });

            footerActions.forEach((button) => {
                const active = button.dataset.lifecycleFooterAction === action;
                button.classList.toggle('d-none', !active);
            });
        }

        function refreshCompletionPrecision() {
            if (!completionForm) {
                return;
            }

            const selected = precisionOptions.find((option) => option.checked)?.value || 'NotKnown';

            completionFields.forEach((field) => {
                const active = field.dataset.completionField === selected;
                field.classList.toggle('d-none', !active);

                field.querySelectorAll('input, select, textarea').forEach((control) => {
                    control.disabled = !active;
                    control.required = active && selected !== 'NotKnown';
                });
            });
        }

        function setSubmitting(form, isSubmitting) {
            const formId = form?.id;
            if (!formId) {
                return;
            }

            const button = root.querySelector(`[data-lifecycle-submit][form="${CSS.escape(formId)}"]`);
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            button.disabled = isSubmitting;
            button.querySelector('[data-lifecycle-submit-spinner]')?.classList.toggle('d-none', !isSubmitting);

            const label = button.querySelector('[data-lifecycle-submit-label]');
            if (label) {
                if (isSubmitting) {
                    label.dataset.originalText = label.textContent || '';
                    label.textContent = 'Saving…';
                } else if (label.dataset.originalText) {
                    label.textContent = label.dataset.originalText;
                    delete label.dataset.originalText;
                }
            }
        }

        tabs.forEach((tab) => {
            tab.addEventListener('click', () => {
                activateAction(tab.dataset.lifecycleActionTab || defaultAction);
            });

            tab.addEventListener('keydown', (event) => {
                if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
                    return;
                }

                event.preventDefault();
                const currentIndex = tabs.indexOf(tab);
                let nextIndex = currentIndex;

                if (event.key === 'ArrowRight') {
                    nextIndex = (currentIndex + 1) % tabs.length;
                } else if (event.key === 'ArrowLeft') {
                    nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
                } else if (event.key === 'Home') {
                    nextIndex = 0;
                } else if (event.key === 'End') {
                    nextIndex = tabs.length - 1;
                }

                const nextTab = tabs[nextIndex];
                activateAction(nextTab.dataset.lifecycleActionTab || defaultAction, true);
            });
        });

        precisionOptions.forEach((option) => {
            option.addEventListener('change', refreshCompletionPrecision);
        });

        root.querySelectorAll('form').forEach((form) => {
            form.addEventListener('submit', (event) => {
                if (submitting) {
                    event.preventDefault();
                    return;
                }

                if (!form.checkValidity()) {
                    return;
                }

                submitting = true;
                setSubmitting(form, true);
            });
        });

        root.addEventListener('shown.bs.modal', () => {
            submitting = false;
            root.querySelectorAll('form').forEach((form) => setSubmitting(form, false));
            activateAction(activeAction === 'none' ? defaultAction : activeAction);
            refreshCompletionPrecision();

            const activePane = root.querySelector(`[data-lifecycle-action-pane="${CSS.escape(activeAction)}"]`);
            const firstControl = activePane?.querySelector('input:not([type="hidden"]):not(:disabled), textarea:not(:disabled), select:not(:disabled)');
            if (firstControl instanceof HTMLElement) {
                window.setTimeout(() => firstControl.focus(), 120);
            }
        });

        activateAction(defaultAction);
        refreshCompletionPrecision();
    }

    document.querySelectorAll('[data-lifecycle-actions-root]').forEach(initialiseLifecycleActions);
})();
