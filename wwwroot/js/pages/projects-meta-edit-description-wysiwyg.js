(() => {
    'use strict';

    // SECTION: Constants
    const MAX_DESCRIPTION_LENGTH = 1000;

    // SECTION: Bootstrap editor on page load
    document.addEventListener('DOMContentLoaded', () => {
        const editorHost = document.querySelector('[data-pm-desc-editor]');
        const hiddenTextarea = document.querySelector('[data-pm-desc-hidden]');

        if (!editorHost || !hiddenTextarea || typeof toastui === 'undefined' || !toastui.Editor) {
            return;
        }

        const countNode = document.querySelector(`[data-char-count-for="${hiddenTextarea.id}"]`);
        const limitMessageNode = document.querySelector('[data-pm-desc-limit-message]');
        const form = hiddenTextarea.closest('form');

        let previousValidMarkdown = hiddenTextarea.value || editorHost.dataset.initialMarkdown || '';

        const editor = new toastui.Editor({
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

        // SECTION: Helpers
        const updateCounter = (length) => {
            if (countNode) {
                countNode.textContent = `${length}/${MAX_DESCRIPTION_LENGTH}`;
            }
        };

        const syncMarkdownToField = () => {
            const markdown = editor.getMarkdown();

            if (markdown.length > MAX_DESCRIPTION_LENGTH) {
                editor.setMarkdown(previousValidMarkdown, false);
                if (limitMessageNode) {
                    limitMessageNode.classList.remove('d-none');
                }
                updateCounter(previousValidMarkdown.length);
                hiddenTextarea.value = previousValidMarkdown;
                return;
            }

            previousValidMarkdown = markdown;
            hiddenTextarea.value = markdown;
            hiddenTextarea.dispatchEvent(new Event('input', { bubbles: true }));

            if (limitMessageNode) {
                limitMessageNode.classList.toggle('d-none', markdown.length < MAX_DESCRIPTION_LENGTH);
            }

            updateCounter(markdown.length);
        };

        // SECTION: Event wiring
        editor.on('change', syncMarkdownToField);

        if (form) {
            form.addEventListener('submit', () => {
                hiddenTextarea.value = editor.getMarkdown();
            });
        }

        syncMarkdownToField();
    });
})();
