(() => {
    'use strict';

    // SECTION: Constants
    const DEFAULT_MAX_DESCRIPTION_LENGTH = 5000;
    const LOG_PREFIX = '[projects-meta-edit]';

    // SECTION: Line ending normalization
    const normalizeMarkdownLineEndings = (markdown) => (markdown ?? '').replace(/\r?\n/g, '\r\n');

    // SECTION: Max length resolution
    const resolveDescriptionMaxLength = (descriptionField) => {
        const parsedMaxLength = Number.parseInt(descriptionField.dataset.maxlength ?? '', 10);

        if (Number.isInteger(parsedMaxLength) && parsedMaxLength > 0) {
            return parsedMaxLength;
        }

        const attributeMaxLength = Number.parseInt(descriptionField.getAttribute('maxlength') ?? '', 10);
        if (Number.isInteger(attributeMaxLength) && attributeMaxLength > 0) {
            return attributeMaxLength;
        }

        return DEFAULT_MAX_DESCRIPTION_LENGTH;
    };

    // SECTION: Markdown to plain text normalization
    const normalizeFallbackPlainText = (markdown) => {
        if (!markdown) {
            return '';
        }

        return markdown
            .replace(/\r\n?/g, '\n')
            .split('\n')
            .map((line) => line
                .replace(/^\s{0,3}#{1,6}\s+/, '')
                .replace(/^\s{0,3}>{1,}\s?/, '')
                .replace(/^\s{0,3}(?:[-*+]|\d+[.)])\s+/, ''))
            .join('\n');
    };

    // SECTION: Plain text to markdown conversion
    const convertPlainTextToSafeMarkdown = (plainText) => {
        const normalizedText = (plainText ?? '').replace(/\r\n?/g, '\n').trim();

        if (!normalizedText) {
            return '';
        }

        const paragraphs = normalizedText
            .split(/\n{2,}/)
            .map((segment) => segment
                .split('\n')
                .map((line) => line.trimEnd())
                .join('  \n')
                .trim())
            .filter((segment) => segment.length > 0);

        return paragraphs.join('\n\n');
    };

    // SECTION: Bootstrap editor on page load
    document.addEventListener('DOMContentLoaded', () => {
        const editorHost = document.querySelector('[data-pm-desc-editor]');
        const descriptionHiddenField = document.querySelector('[data-pm-desc-hidden]');
        const fallbackTextarea = document.querySelector('[data-pm-desc-fallback]');

        if (!editorHost || !descriptionHiddenField || !fallbackTextarea) {
            return;
        }

        const maxDescriptionLength = resolveDescriptionMaxLength(descriptionHiddenField);
        const countNode = document.querySelector(`[data-char-count-for="${descriptionHiddenField.id}"]`);
        const limitMessageNode = document.querySelector('[data-pm-desc-limit-message]');
        const unavailableMessageNode = document.querySelector('[data-pm-desc-unavailable-message]');
        const form = descriptionHiddenField.closest('form');

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

        const updateFallbackFromHiddenMarkdown = () => {
            fallbackTextarea.value = normalizeFallbackPlainText(descriptionHiddenField.value);
        };

        const syncHiddenFromFallbackPlainText = () => {
            descriptionHiddenField.value = convertPlainTextToSafeMarkdown(fallbackTextarea.value);
            updateCounter(descriptionHiddenField.value.length);
            updateLimitMessage(descriptionHiddenField.value.length);
        };

        const activatePlainTextMode = ({ showUnavailableWarning = false } = {}) => {
            fallbackTextarea.classList.remove('d-none');
            editorHost.classList.add('d-none');

            if (unavailableMessageNode) {
                unavailableMessageNode.classList.toggle('d-none', !showUnavailableWarning);
            }

            updateFallbackFromHiddenMarkdown();
            syncHiddenFromFallbackPlainText();
        };

        const activateRichEditorMode = () => {
            fallbackTextarea.classList.add('d-none');
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
        fallbackTextarea.addEventListener('input', syncHiddenFromFallbackPlainText);

        // SECTION: Progressive enhancement bootstrap
        let previousValidMarkdown = normalizeMarkdownLineEndings(descriptionHiddenField.value || editorHost.dataset.initialMarkdown || '');
        descriptionHiddenField.value = previousValidMarkdown;
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

            const applyMarkdownWithLimitGuard = (markdown, { revertEditorOnOverflow = true, focusEditorOnOverflow = false } = {}) => {
                const normalizedMarkdown = normalizeMarkdownLineEndings(markdown);

                if (normalizedMarkdown.length > maxDescriptionLength) {
                    if (revertEditorOnOverflow) {
                        editor.setMarkdown(previousValidMarkdown, false);
                    }
                    if (limitMessageNode) {
                        limitMessageNode.classList.remove('d-none');
                    }

                    if (focusEditorOnOverflow && typeof editor.focus === 'function') {
                        editor.focus();
                    }

                    updateCounter(previousValidMarkdown.length);
                    descriptionHiddenField.value = previousValidMarkdown;
                    return false;
                }

                previousValidMarkdown = normalizedMarkdown;
                descriptionHiddenField.value = normalizedMarkdown;
                descriptionHiddenField.dispatchEvent(new Event('input', { bubbles: true }));

                updateCounter(normalizedMarkdown.length);
                updateLimitMessage(normalizedMarkdown.length);
                return true;
            };

            const syncMarkdownToField = () => {
                applyMarkdownWithLimitGuard(editor.getMarkdown());
            };

            // SECTION: Event wiring
            editor.on('change', syncMarkdownToField);

            if (form) {
                form.addEventListener('submit', (event) => {
                    const isValid = applyMarkdownWithLimitGuard(editor.getMarkdown(), {
                        revertEditorOnOverflow: false,
                        focusEditorOnOverflow: true
                    });

                    if (!isValid) {
                        event.preventDefault();
                    }
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
