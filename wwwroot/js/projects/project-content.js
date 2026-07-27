(() => {
    'use strict';

    const root = document.querySelector('[data-project-content]');
    if (!root || root.dataset.projectContentInitialized === 'true') {
        return;
    }

    root.dataset.projectContentInitialized = 'true';

    const DEFAULT_SAVE_TIMEOUT_MS = 20000;
    const dynamicResetters = new WeakMap();
    let dirtyForm = null;

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
        const incomplete = parsePositiveInt(textarea.dataset.wordIncomplete, 150);
        const recommendedMinimum = parsePositiveInt(textarea.dataset.wordMin, 200);
        const recommendedMaximum = parsePositiveInt(textarea.dataset.wordRecommendedMax, 250);
        const hardMaximum = parsePositiveInt(textarea.dataset.wordHardMax, 300);

        if (words === 0) {
            return { words, text: 'Not recorded', statusClass: 'is-neutral' };
        }

        if (words < incomplete) {
            return { words, text: 'Brief incomplete', statusClass: 'is-warning' };
        }

        if (words < recommendedMinimum) {
            return { words, text: 'Below recommended length', statusClass: 'is-warning' };
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
        return true;
    };

    const restoreFormControls = (form, originallyDisabled) => {
        form.querySelectorAll('button, input, textarea, select').forEach((control) => {
            control.disabled = originallyDisabled.has(control);
        });
    };

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
        const hardMaximum = parsePositiveInt(textarea.dataset.wordHardMax, 300);
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

    const submitForm = async (form) => {
        if (form.dataset.submitting === 'true') {
            return;
        }

        setError(form, '');

        if (!validateBrief(form) || !validateCapabilities(form)) {
            return;
        }

        const formData = new FormData(form);
        const originallyDisabled = new Set(
            [...form.querySelectorAll('button, input, textarea, select')]
                .filter((control) => control.disabled));

        const timeoutMs = parsePositiveInt(
            root.dataset.saveTimeoutMs,
            DEFAULT_SAVE_TIMEOUT_MS);
        const controller = new AbortController();
        const timeoutId = window.setTimeout(
            () => controller.abort(),
            timeoutMs);

        setSubmitting(form, true, originallyDisabled);

        let navigationStarted = false;
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
            navigationStarted = true;
            const redirectUrl = payload.redirectUrl || window.location.href;
            window.location.replace(redirectUrl);

            window.setTimeout(() => {
                if (document.visibilityState === 'visible') {
                    window.location.assign(redirectUrl);
                }
            }, 1200);
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
            if (!navigationStarted) {
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
                        `Reorder capability statement ${ordinal}`);
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

            if (button.matches('[data-capability-up]')) {
                row.previousElementSibling?.before(row);
            } else if (button.matches('[data-capability-down]')) {
                row.nextElementSibling?.after(row);
            } else if (button.matches('[data-capability-remove]')) {
                if (rows().length === 1) {
                    const input = row.querySelector('input');
                    if (input) {
                        input.value = '';
                        input.focus();
                    }
                } else {
                    row.remove();
                    ensureOneRow();
                }
            } else {
                return;
            }

            markDirty(form);
            renumber();
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
