(() => {
    'use strict';

    // SECTION: Constants
    const MAX_DESCRIPTION_LENGTH = 1000;

    // SECTION: Bootstrap editor on page load
    document.addEventListener('DOMContentLoaded', () => {
        const editorHost = document.querySelector('[data-pm-desc-editor]');
        const descriptionTextarea = document.querySelector('[data-pm-desc-hidden]');

        if (!editorHost || !descriptionTextarea) {
            return;
        }

        const countNode = document.querySelector(`[data-char-count-for="${descriptionTextarea.id}"]`);
        const limitMessageNode = document.querySelector('[data-pm-desc-limit-message]');
        const unavailableMessageNode = document.querySelector('[data-pm-desc-unavailable-message]');
        const form = descriptionTextarea.closest('form');

        // SECTION: Shared helpers
        const updateCounter = (length) => {
            if (countNode) {
                countNode.textContent = `${length}/${MAX_DESCRIPTION_LENGTH}`;
            }
        };

        const updateLimitMessage = (length) => {
            if (limitMessageNode) {
                limitMessageNode.classList.toggle('d-none', length < MAX_DESCRIPTION_LENGTH);
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

        // SECTION: Plain text mode event wiring
        descriptionTextarea.addEventListener('input', () => {
            updateCounter(descriptionTextarea.value.length);
            updateLimitMessage(descriptionTextarea.value.length);
        });

        // SECTION: Progressive enhancement guard
        if (typeof toastui === 'undefined' || !toastui.Editor) {
            activatePlainTextMode({ showUnavailableWarning: true });
            return;
        }

        let previousValidMarkdown = descriptionTextarea.value || editorHost.dataset.initialMarkdown || '';
        let editor;

        try {
            editor = new toastui.Editor({
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
        } catch (_error) {
            activatePlainTextMode({ showUnavailableWarning: true });
            return;
        }

        activateRichEditorMode();

        const syncMarkdownToField = () => {
            const markdown = editor.getMarkdown();

            if (markdown.length > MAX_DESCRIPTION_LENGTH) {
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
    });
})();
