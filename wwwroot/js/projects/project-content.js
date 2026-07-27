(() => {
    'use strict';

    const root = document.querySelector('[data-project-content]');
    if (!root || root.dataset.projectContentInitialized === 'true') {
        return;
    }

    root.dataset.projectContentInitialized = 'true';

    const DEFAULT_SAVE_TIMEOUT_MS = 20000;
    const DEFAULT_RELOAD_RECOVERY_MS = 2500;
    const dynamicResetters = new WeakMap();
    const previewControllers = new WeakMap();
    const savedReloadRecoveries = new Map();
    let dirtyForm = null;
    let pageIsUnloading = false;

    window.addEventListener('pagehide', () => {
        pageIsUnloading = true;
    });

    window.addEventListener('pageshow', () => {
        pageIsUnloading = false;
        savedReloadRecoveries.forEach((_context, form) => recoverSavedForm(form));
    });

    const parsePositiveInt = (value, fallback) => {
        const parsed = Number.parseInt(value ?? '', 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
    };

    const countWords = (value) => (value.match(/\S+/g) || []).length;

    const setError = (form, message) => {
        const host = form.querySelector('[data-content-error]');
        if (!host) {
            return;
        }

        host.textContent = message || '';
        host.classList.toggle('d-none', !message);
    };

    const markDirty = (form) => {
        if (form.dataset.submitting === 'true') {
            return;
        }

        dirtyForm = form;
        form.dataset.dirty = 'true';
    };

    const clearDirty = (form) => {
        if (dirtyForm === form) {
            dirtyForm = null;
        }

        form.dataset.dirty = 'false';
    };

    const setStatus = (element, text, statusClass) => {
        if (!element) {
            return;
        }

        element.textContent = text;
        element.classList.remove(
            'is-neutral',
            'is-warning',
            'is-ready',
            'is-danger');

        if (statusClass) {
            element.classList.add(statusClass);
        }
    };

    const getBriefReadiness = (textarea) => {
        const words = countWords(textarea.value);
        const recommendedMinimum = parsePositiveInt(textarea.dataset.wordMin, 100);
        const recommendedMaximum = parsePositiveInt(textarea.dataset.wordRecommendedMax, 150);
        const hardMaximum = parsePositiveInt(textarea.dataset.wordHardMax, 200);

        if (words === 0) {
            return { words, text: 'Not recorded', statusClass: 'is-neutral' };
        }

        if (words < recommendedMinimum) {
            return { words, text: 'Concise', statusClass: 'is-neutral' };
        }

        if (words <= recommendedMaximum) {
            return { words, text: 'Recommended length', statusClass: 'is-ready' };
        }

        if (words <= hardMaximum) {
            return { words, text: 'Consider shortening', statusClass: 'is-warning' };
        }

        return { words, text: 'Maximum exceeded', statusClass: 'is-danger' };
    };

    const updateBriefReadiness = (textarea) => {
        const form = textarea.closest('form');
        if (!form) {
            return;
        }

        const readiness = getBriefReadiness(textarea);
        const countHost = form.querySelector('[data-word-count]');
        if (countHost) {
            countHost.textContent = `${readiness.words} ${readiness.words === 1 ? 'word' : 'words'}`;
            countHost.classList.toggle(
                'text-danger',
                readiness.statusClass === 'is-danger');
        }

        setStatus(
            form.querySelector('[data-word-status]'),
            readiness.text,
            readiness.statusClass);
    };

    const updateCharacterCount = (textarea) => {
        const form = textarea.closest('form');
        const host = form?.querySelector('[data-character-count]');
        if (!host) {
            return;
        }

        const maximum = parsePositiveInt(textarea.getAttribute('maxlength'), 0);
        const current = textarea.value.length;
        host.textContent = maximum > 0
            ? `${current.toLocaleString()} / ${maximum.toLocaleString()} characters`
            : `${current.toLocaleString()} characters`;
        host.classList.toggle('text-danger', maximum > 0 && current >= maximum);
    };

    const cancelDescriptionPreviewRequest = (form) => {
        previewControllers.get(form)?.abort();
        previewControllers.delete(form);

        const trigger = form.querySelector('[data-description-preview-trigger]');
        const label = form.querySelector('[data-description-preview-label]');
        const panel = form.querySelector('[data-description-preview-panel]');

        if (trigger && form.dataset.submitting !== 'true') {
            trigger.disabled = false;
        }
        if (label && label.textContent === 'Loading…') {
            label.textContent = panel && !panel.classList.contains('d-none')
                ? (form.dataset.previewStale === 'true' ? 'Update preview' : 'Refresh preview')
                : 'Preview';
        }
    };

    const resetDescriptionPreview = (form) => {
        cancelDescriptionPreviewRequest(form);

        const panel = form.querySelector('[data-description-preview-panel]');
        const content = form.querySelector('[data-description-preview-content]');
        const error = form.querySelector('[data-description-preview-error]');
        const label = form.querySelector('[data-description-preview-label]');
        const trigger = form.querySelector('[data-description-preview-trigger]');

        panel?.classList.add('d-none');
        if (content) {
            content.replaceChildren();
        }
        if (error) {
            error.textContent = '';
            error.classList.add('d-none');
        }
        if (label) {
            label.textContent = 'Preview';
        }
        if (trigger) {
            trigger.disabled = false;
        }
        form.dataset.previewStale = 'false';
    };

    const openEditor = (pane) => {
        if (!pane) {
            return;
        }

        const view = pane.querySelector('[data-content-view]');
        const form = pane.querySelector('[data-content-form]');
        if (!view || !form || form.dataset.submitting === 'true') {
            return;
        }

        view.classList.add('d-none');
        form.classList.remove('d-none');
        form.querySelector('textarea, input:not([type="hidden"])')?.focus();
    };

    const closeEditor = (pane, force = false) => {
        if (!pane) {
            return true;
        }

        const view = pane.querySelector('[data-content-view]');
        const form = pane.querySelector('[data-content-form]');
        if (!view || !form) {
            return true;
        }

        if (form.dataset.submitting === 'true') {
            return false;
        }

        if (!force &&
            form.dataset.dirty === 'true' &&
            !window.confirm('Discard the unsaved changes in this tab?')) {
            return false;
        }

        form.reset();
        dynamicResetters.get(form)?.();
        clearDirty(form);
        setError(form, '');
        form.classList.remove('is-saving');
        form.removeAttribute('aria-busy');
        form.classList.add('d-none');
        view.classList.remove('d-none');
        form.querySelectorAll('[data-word-counter]').forEach(updateBriefReadiness);
        form.querySelectorAll('[data-character-counter]').forEach(updateCharacterCount);
        return true;
    };

    const restoreFormControls = (form, originallyDisabled) => {
        form.querySelectorAll('button, input, textarea, select').forEach((control) => {
            control.disabled = originallyDisabled.has(control);
        });
    };

    function recoverSavedForm(form) {
        const context = savedReloadRecoveries.get(form);
        if (!context) {
            return;
        }

        if (context.timerId) {
            window.clearTimeout(context.timerId);
        }

        savedReloadRecoveries.delete(form);
        setSubmitting(form, false, context.originallyDisabled);
        setError(
            form,
            'Your changes were saved, but the page did not refresh. Refresh the page to load the latest content.');
    }

    const setSubmitting = (form, isSubmitting, originallyDisabled = new Set()) => {
        const saveButton = form.querySelector('[data-content-save]');
        const spinner = form.querySelector('[data-content-spinner]');
        const label = form.querySelector('[data-content-save-label]');

        form.dataset.submitting = isSubmitting ? 'true' : 'false';
        form.classList.toggle('is-saving', isSubmitting);
        if (isSubmitting) {
            form.setAttribute('aria-busy', 'true');
        } else {
            form.removeAttribute('aria-busy');
        }

        if (isSubmitting) {
            form.querySelectorAll('button, input, textarea, select').forEach((control) => {
                control.disabled = true;
            });

            if (saveButton) {
                saveButton.disabled = true;
            }

            spinner?.classList.remove('d-none');
            if (label) {
                label.dataset.originalLabel ||= label.textContent?.trim() || 'Save';
                label.textContent = 'Saving…';
            }
        } else {
            restoreFormControls(form, originallyDisabled);
            spinner?.classList.add('d-none');
            if (label) {
                label.textContent = label.dataset.originalLabel || 'Save';
            }
        }
    };

    const validateBrief = (form) => {
        const textarea = form.querySelector('[data-word-counter]');
        if (!textarea) {
            return true;
        }

        const readiness = getBriefReadiness(textarea);
        const hardMaximum = parsePositiveInt(textarea.dataset.wordHardMax, 200);
        if (readiness.words <= hardMaximum) {
            return true;
        }

        setError(
            form,
            `Project brief is ${readiness.words} words. Reduce it to ${hardMaximum} words or fewer.`);
        textarea.focus();
        return false;
    };

    const getCapabilityValues = (form) =>
        [...form.querySelectorAll('[data-capability-row] input')]
            .map((input) => input.value.trim())
            .filter(Boolean);

    const validateCapabilities = (form) => {
        if (!form.matches('[data-capability-editor]')) {
            return true;
        }

        const values = getCapabilityValues(form);
        const normalized = values.map((value) => value.toLocaleLowerCase());
        if (new Set(normalized).size === normalized.length) {
            return true;
        }

        setError(form, 'Remove duplicate capability statements before saving.');
        return false;
    };

    const readJsonResponse = async (response) => {
        const responseText = await response.text();
        if (!responseText) {
            return null;
        }

        try {
            return JSON.parse(responseText);
        } catch {
            return null;
        }
    };

    const requestPageReload = (payload) => {
        const reloadEvent = new CustomEvent('projectcontent:reload-requested', {
            bubbles: true,
            cancelable: true,
            detail: {
                section: payload?.section || null,
                message: payload?.message || null
            }
        });

        if (!root.dispatchEvent(reloadEvent)) {
            return;
        }

        try {
            window.location.reload();
        } catch {
            // The recovery timer restores the editor if the browser does not unload.
        }
    };

    const submitForm = async (form) => {
        if (form.dataset.submitting === 'true') {
            return;
        }

        setError(form, '');

        if (!validateBrief(form) || !validateCapabilities(form)) {
            return;
        }

        cancelDescriptionPreviewRequest(form);

        const formData = new FormData(form);
        const originallyDisabled = new Set(
            [...form.querySelectorAll('button, input, textarea, select')]
                .filter((control) => control.disabled));

        const timeoutMs = parsePositiveInt(
            root.dataset.saveTimeoutMs,
            DEFAULT_SAVE_TIMEOUT_MS);
        const reloadRecoveryMs = parsePositiveInt(
            root.dataset.reloadRecoveryMs,
            DEFAULT_RELOAD_RECOVERY_MS);
        const controller = new AbortController();
        const timeoutId = window.setTimeout(
            () => controller.abort(),
            timeoutMs);

        setSubmitting(form, true, originallyDisabled);

        let reloadRequested = false;
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: formData,
                credentials: 'same-origin',
                cache: 'no-store',
                signal: controller.signal,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'application/json'
                }
            });

            const payload = await readJsonResponse(response);
            if (!response.ok || !payload?.ok) {
                const fallback = response.status === 409
                    ? 'This project was changed by another user. Refresh the page and try again.'
                    : 'The project content could not be saved.';
                throw new Error(payload?.error || fallback);
            }

            clearDirty(form);
            reloadRequested = true;

            const recoveryContext = {
                originallyDisabled,
                timerId: 0
            };
            savedReloadRecoveries.set(form, recoveryContext);
            recoveryContext.timerId = window.setTimeout(() => {
                if (!pageIsUnloading) {
                    recoverSavedForm(form);
                }
            }, reloadRecoveryMs);

            requestPageReload(payload);
        } catch (error) {
            if (error instanceof DOMException && error.name === 'AbortError') {
                setError(
                    form,
                    'Saving took longer than expected. Your entries are still on screen. Check the connection and try again.');
            } else {
                setError(
                    form,
                    error instanceof Error
                        ? error.message
                        : 'The project content could not be saved.');
            }
        } finally {
            window.clearTimeout(timeoutId);
            if (!reloadRequested) {
                setSubmitting(form, false, originallyDisabled);
            }
        }
    };

    const initializeCapabilityEditor = (form) => {
        const list = form.querySelector('[data-capability-list]');
        const template = form.querySelector('[data-capability-template]');
        const addButton = form.querySelector('[data-capability-add]');
        const countHost = form.querySelector('[data-capability-count]');
        const statusHost = form.querySelector('[data-capability-status]');
        const minimum = parsePositiveInt(form.dataset.capabilityMin, 5);
        const maximum = parsePositiveInt(form.dataset.capabilityMax, 8);

        if (!list || !template || !addButton) {
            return;
        }

        const initialMarkup = list.innerHTML;

        const rows = () => [...list.querySelectorAll('[data-capability-row]')];

        const updateReadiness = () => {
            const populated = getCapabilityValues(form).length;
            if (countHost) {
                countHost.textContent = `${populated} of ${maximum}`;
            }

            if (populated === 0) {
                setStatus(statusHost, 'Not recorded', 'is-neutral');
            } else if (populated < minimum) {
                setStatus(statusHost, 'Draft for briefing', 'is-warning');
            } else {
                setStatus(statusHost, 'Presentation ready', 'is-ready');
            }
        };

        const renumber = () => {
            const currentRows = rows();
            currentRows.forEach((row, index) => {
                const ordinal = index + 1;
                const number = row.querySelector('[data-capability-number]');
                const input = row.querySelector('input');
                const actionGroup = row.querySelector('[role="group"]');
                const up = row.querySelector('[data-capability-up]');
                const down = row.querySelector('[data-capability-down]');
                const remove = row.querySelector('[data-capability-remove]');

                if (number) {
                    number.textContent = String(ordinal);
                }
                if (input) {
                    input.setAttribute('aria-label', `Capability statement ${ordinal}`);
                }
                if (actionGroup) {
                    actionGroup.setAttribute(
                        'aria-label',
                        `Actions for capability statement ${ordinal}`);
                }
                if (up) {
                    up.disabled = index === 0;
                    up.setAttribute(
                        'aria-label',
                        `Move capability statement ${ordinal} up`);
                }
                if (down) {
                    down.disabled = index === currentRows.length - 1;
                    down.setAttribute(
                        'aria-label',
                        `Move capability statement ${ordinal} down`);
                }
                if (remove) {
                    remove.setAttribute(
                        'aria-label',
                        `Remove capability statement ${ordinal}`);
                }
            });

            const populated = getCapabilityValues(form).length;
            addButton.disabled =
                currentRows.length >= maximum ||
                (currentRows.length > populated && currentRows.at(-1)?.querySelector('input')?.value.trim() === '');
            updateReadiness();
        };

        const ensureOneRow = () => {
            if (rows().length > 0) {
                return;
            }

            list.append(template.content.cloneNode(true));
        };

        dynamicResetters.set(form, () => {
            list.innerHTML = initialMarkup;
            ensureOneRow();
            renumber();
        });

        list.addEventListener('click', (event) => {
            const button = event.target.closest('button');
            const row = button?.closest('[data-capability-row]');
            if (!button || !row || form.dataset.submitting === 'true') {
                return;
            }

            let focusTarget = button;
            let fallbackActionSelector = null;
            if (button.matches('[data-capability-up]')) {
                row.previousElementSibling?.before(row);
                fallbackActionSelector = '[data-capability-down]';
            } else if (button.matches('[data-capability-down]')) {
                row.nextElementSibling?.after(row);
                fallbackActionSelector = '[data-capability-up]';
            } else if (button.matches('[data-capability-remove]')) {
                if (rows().length === 1) {
                    const input = row.querySelector('input');
                    if (input) {
                        input.value = '';
                        focusTarget = input;
                    }
                } else {
                    focusTarget =
                        row.nextElementSibling?.querySelector('input') ||
                        row.previousElementSibling?.querySelector('input');
                    row.remove();
                    ensureOneRow();
                    focusTarget ||= rows().at(-1)?.querySelector('input');
                }
            } else {
                return;
            }

            markDirty(form);
            renumber();
            if (focusTarget instanceof HTMLButtonElement && focusTarget.disabled) {
                focusTarget =
                    (fallbackActionSelector && row.querySelector(fallbackActionSelector)) ||
                    row.querySelector('input');
            }
            focusTarget?.focus();
        });

        list.addEventListener('input', () => {
            markDirty(form);
            renumber();
        });

        addButton.addEventListener('click', () => {
            if (form.dataset.submitting === 'true') {
                return;
            }

            const currentRows = rows();
            const lastInput = currentRows.at(-1)?.querySelector('input');
            if (lastInput && !lastInput.value.trim()) {
                lastInput.focus();
                return;
            }

            if (currentRows.length >= maximum) {
                return;
            }

            list.append(template.content.cloneNode(true));
            markDirty(form);
            renumber();
            rows().at(-1)?.querySelector('input')?.focus();
        });

        ensureOneRow();
        renumber();
    };

    const initializeDescriptionEditor = (form) => {
        const textarea = form.querySelector('[data-character-counter]');
        const trigger = form.querySelector('[data-description-preview-trigger]');
        const closeButton = form.querySelector('[data-description-preview-close]');
        const panel = form.querySelector('[data-description-preview-panel]');
        const content = form.querySelector('[data-description-preview-content]');
        const errorHost = form.querySelector('[data-description-preview-error]');
        const label = form.querySelector('[data-description-preview-label]');
        const previewUrl = form.dataset.descriptionPreviewUrl;

        if (textarea) {
            updateCharacterCount(textarea);
            textarea.addEventListener('input', () => {
                updateCharacterCount(textarea);
                if (panel && !panel.classList.contains('d-none')) {
                    form.dataset.previewStale = 'true';
                    if (label) {
                        label.textContent = 'Update preview';
                    }
                }
            });
        }

        dynamicResetters.set(form, () => {
            resetDescriptionPreview(form);
            if (textarea) {
                updateCharacterCount(textarea);
            }
        });

        closeButton?.addEventListener('click', () => {
            panel?.classList.add('d-none');
            trigger?.focus();
        });

        if (!trigger || !panel || !content || !previewUrl) {
            return;
        }

        trigger.addEventListener('click', async () => {
            previewControllers.get(form)?.abort();
            const controller = new AbortController();
            previewControllers.set(form, controller);

            trigger.disabled = true;
            if (label) {
                label.textContent = 'Loading…';
            }
            if (errorHost) {
                errorHost.textContent = '';
                errorHost.classList.add('d-none');
            }
            panel.classList.remove('d-none');
            content.innerHTML = '<p class="text-muted mb-0">Loading preview…</p>';

            try {
                const response = await fetch(previewUrl, {
                    method: 'POST',
                    body: new FormData(form),
                    credentials: 'same-origin',
                    cache: 'no-store',
                    signal: controller.signal,
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest',
                        'Accept': 'application/json'
                    }
                });

                const payload = await readJsonResponse(response);
                if (!response.ok || !payload?.ok) {
                    throw new Error(payload?.error || 'The preview could not be generated.');
                }

                content.innerHTML = payload.html || '<p class="text-muted mb-0">Nothing to preview.</p>';
                form.dataset.previewStale = 'false';
            } catch (error) {
                if (error instanceof DOMException && error.name === 'AbortError') {
                    return;
                }

                content.innerHTML = '<p class="text-muted mb-0">Preview unavailable.</p>';
                if (errorHost) {
                    errorHost.textContent = error instanceof Error
                        ? error.message
                        : 'The preview could not be generated.';
                    errorHost.classList.remove('d-none');
                }
            } finally {
                if (previewControllers.get(form) === controller) {
                    previewControllers.delete(form);
                    trigger.disabled = form.dataset.submitting === 'true';
                    if (label) {
                        label.textContent = form.dataset.previewStale === 'true'
                            ? 'Update preview'
                            : 'Refresh preview';
                    }
                }
            }
        });
    };

    root.querySelectorAll('[data-content-edit]').forEach((button) => {
        button.addEventListener('click', () => {
            openEditor(button.closest('.tab-pane'));
        });
    });

    root.querySelectorAll('[data-content-cancel]').forEach((button) => {
        button.addEventListener('click', () => {
            closeEditor(button.closest('.tab-pane'));
        });
    });

    root.querySelectorAll('[data-content-form]').forEach((form) => {
        form.dataset.dirty = 'false';
        form.dataset.submitting = 'false';

        form.addEventListener('input', () => markDirty(form));
        form.addEventListener('change', () => markDirty(form));
        form.addEventListener('submit', (event) => {
            event.preventDefault();
            void submitForm(form);
        });

        form.querySelectorAll('[data-word-counter]').forEach((textarea) => {
            updateBriefReadiness(textarea);
            textarea.addEventListener('input', () => {
                updateBriefReadiness(textarea);
            });
        });
    });

    root.querySelectorAll('[data-capability-editor]').forEach(initializeCapabilityEditor);
    root.querySelectorAll('[data-description-editor]').forEach(initializeDescriptionEditor);

    root.querySelectorAll('[data-bs-toggle="tab"]').forEach((tab) => {
        tab.addEventListener('show.bs.tab', (event) => {
            if (!dirtyForm || dirtyForm.dataset.dirty !== 'true') {
                return;
            }

            const currentPane = dirtyForm.closest('.tab-pane');
            const nextTarget = event.target.getAttribute('data-bs-target');
            if (!currentPane || `#${currentPane.id}` === nextTarget) {
                return;
            }

            if (!window.confirm(
                'You have unsaved project-content changes. Discard them and change tabs?')) {
                event.preventDefault();
                return;
            }

            closeEditor(currentPane, true);
        });

        tab.addEventListener('shown.bs.tab', (event) => {
            const key = event.target.dataset.contentTab;
            if (!key) {
                return;
            }

            const url = new URL(window.location.href);
            url.searchParams.set('content', key);
            url.hash = `content-${key}`;
            window.history.replaceState(null, '', url);
        });
    });

    root.querySelectorAll('[data-description-toggle]').forEach((button) => {
        button.addEventListener('click', () => {
            const container = button.closest('[data-description-container]');
            if (!container) {
                return;
            }

            const expanded = button.getAttribute('aria-expanded') === 'true';
            container.classList.toggle('is-collapsed', expanded);
            button.setAttribute('aria-expanded', String(!expanded));
            button.textContent = expanded
                ? 'Show full description'
                : 'Show less';
        });
    });

    window.addEventListener('beforeunload', (event) => {
        if (!dirtyForm || dirtyForm.dataset.dirty !== 'true') {
            return;
        }

        event.preventDefault();
        event.returnValue = '';
    });
})();
