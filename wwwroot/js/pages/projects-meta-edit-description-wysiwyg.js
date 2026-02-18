(() => {
    'use strict';

    // SECTION: Constants
    const DEFAULT_MAX_DESCRIPTION_LENGTH = 5000;
    const LOG_PREFIX = '[projects-meta-edit]';

    // SECTION: Max length resolution
    const resolveDescriptionMaxLength = (descriptionTextarea) => {
        const parsedMaxLength = Number.parseInt(descriptionTextarea.dataset.maxlength ?? '', 10);

        if (Number.isInteger(parsedMaxLength) && parsedMaxLength > 0) {
            return parsedMaxLength;
        }

        const attributeMaxLength = Number.parseInt(descriptionTextarea.getAttribute('maxlength') ?? '', 10);
        if (Number.isInteger(attributeMaxLength) && attributeMaxLength > 0) {
            return attributeMaxLength;
        }

        return DEFAULT_MAX_DESCRIPTION_LENGTH;
    };

    // SECTION: Bootstrap editor on page load
    document.addEventListener('DOMContentLoaded', () => {
        const editorHost = document.querySelector('[data-pm-desc-editor]');
        const descriptionTextarea = document.querySelector('[data-pm-desc-hidden]');

        if (!editorHost || !descriptionTextarea) {
            return;
        }

        const maxDescriptionLength = resolveDescriptionMaxLength(descriptionTextarea);
        const countNode = document.querySelector(`[data-char-count-for="${descriptionTextarea.id}"]`);
        const limitMessageNode = document.querySelector('[data-pm-desc-limit-message]');
        const unavailableMessageNode = document.querySelector('[data-pm-desc-unavailable-message]');
        const form = descriptionTextarea.closest('form');

        // SECTION: Shared helpers
        const updateCounter = (length) => {
            if (countNode) {
                countNode.textContent = `${length}/${maxDescriptionLength}`;
            }
        };

        const updateLimitMessage = (length) => {
            if (limitMessageNode) {
                limitMessageNode.classList.toggle('d-none', length < maxDescriptionLength);
            }
        };

        const activatePlainTextMode = ({ showUnavailableWarning = false } = {}) => {
            descriptionTextarea.classList.remove('d-none');
            editorHost.classList.add('d-none');

            if (unavailableMessageNode) {
                unavailableMessageNode.classList.toggle('d-none', !showUnavailableWarning);
            }

            updateCounter(descriptionTextarea.value.length);
            updateLimitMessage(descriptionTextarea.value.length);
        };

        const activateRichEditorMode = () => {
            descriptionTextarea.classList.add('d-none');
            editorHost.classList.remove('d-none');

            if (unavailableMessageNode) {
                unavailableMessageNode.classList.add('d-none');
            }
        };

        const logFallbackWarning = (reason, error) => {
            if (error) {
                console.warn(`${LOG_PREFIX} fallback mode: ${reason}`, error);
                return;
            }

            console.warn(`${LOG_PREFIX} fallback mode: ${reason}`);
        };

        // SECTION: Plain text mode event wiring
        descriptionTextarea.addEventListener('input', () => {
            updateCounter(descriptionTextarea.value.length);
            updateLimitMessage(descriptionTextarea.value.length);
        });

        // SECTION: Progressive enhancement bootstrap
        let previousValidMarkdown = descriptionTextarea.value || editorHost.dataset.initialMarkdown || '';
        let editor;

        try {
            // SECTION: Progressive enhancement guard
            const toastUiRuntime = window.toastui;
            if (!toastUiRuntime || !toastUiRuntime.Editor) {
                logFallbackWarning('toastui missing');
                activatePlainTextMode({ showUnavailableWarning: true });
                return;
            }

            editor = new toastUiRuntime.Editor({
                el: editorHost,
                initialEditType: 'wysiwyg',
                previewStyle: 'tab',
                hideModeSwitch: true,
                height: '300px',
                initialValue: previousValidMarkdown,
                toolbarItems: [
                    ['heading', 'bold', 'italic'],
                    ['hr'],
                    ['ul', 'ol'],
                    ['quote'],
                    ['code', 'codeblock'],
                    ['link']
                ]
            });

            activateRichEditorMode();

            const syncMarkdownToField = () => {
                const markdown = editor.getMarkdown();

                if (markdown.length > maxDescriptionLength) {
                    editor.setMarkdown(previousValidMarkdown, false);
                    if (limitMessageNode) {
                        limitMessageNode.classList.remove('d-none');
                    }
                    updateCounter(previousValidMarkdown.length);
                    descriptionTextarea.value = previousValidMarkdown;
                    return;
                }

                previousValidMarkdown = markdown;
                descriptionTextarea.value = markdown;
                descriptionTextarea.dispatchEvent(new Event('input', { bubbles: true }));

                updateCounter(markdown.length);
            };

            // SECTION: Event wiring
            editor.on('change', syncMarkdownToField);

            if (form) {
                form.addEventListener('submit', () => {
                    descriptionTextarea.value = editor.getMarkdown();
                });
            }

            syncMarkdownToField();
        } catch (error) {
            logFallbackWarning('init exception', error);
            activatePlainTextMode({ showUnavailableWarning: true });
            return;
        }
    });
})();
