const root = document.querySelector('[data-officer-conference]');

if (root) {
    const antiforgeryToken = root.querySelector('.oc-antiforgery input[name="__RequestVerificationToken"]')?.value ?? '';
    const selector = root.querySelector('[data-officer-selector]');
    const structuredProgressKinds = new Set([1, 2]);
    let openEditor = null;

    const formatIstDateTime = (value) => {
        if (!value) return '';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '';

        return new Intl.DateTimeFormat('en-GB', {
            timeZone: 'Asia/Kolkata',
            day: '2-digit',
            month: 'short',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            hour12: true
        }).format(date);
    };

    const setRowStatus = (item, message, isError = false) => {
        const status = item?.querySelector('[data-oc-row-status]');
        if (!status) return;

        status.textContent = message;
        status.classList.toggle('is-error', isError);
        window.clearTimeout(status._clearTimer);
        if (message && !isError) {
            status._clearTimer = window.setTimeout(() => {
                status.textContent = '';
                status.classList.remove('is-error');
            }, 3500);
        }
    };

    const getAddButton = (editor) => editor?.closest('[data-oc-item]')?.querySelector('[data-oc-add]') ?? null;

    const closeEditor = (editor, { clear = true, restoreFocus = false } = {}) => {
        if (!editor) return;

        editor.hidden = true;
        editor.classList.remove('is-saving');
        editor.setAttribute('aria-busy', 'false');

        const input = editor.querySelector('[data-oc-input]');
        const feedback = editor.querySelector('[data-oc-feedback]');
        if (clear && input) input.value = '';
        input?.removeAttribute('aria-invalid');

        if (feedback) {
            feedback.textContent = '';
            feedback.classList.remove('is-error');
        }

        const save = editor.querySelector('[data-oc-save]');
        if (save) save.disabled = true;

        const addButton = getAddButton(editor);
        addButton?.setAttribute('aria-expanded', 'false');
        if (restoreFocus) addButton?.focus();

        if (openEditor === editor) openEditor = null;
    };

    const openInlineEditor = (item) => {
        const editor = item?.querySelector('[data-oc-editor]');
        if (!editor) return;

        if (openEditor && openEditor !== editor) {
            closeEditor(openEditor);
        }

        setRowStatus(item, '');
        editor.hidden = false;
        editor.setAttribute('aria-busy', 'false');
        getAddButton(editor)?.setAttribute('aria-expanded', 'true');
        openEditor = editor;

        const input = editor.querySelector('[data-oc-input]');
        requestAnimationFrame(() => input?.focus());
    };

    const setDirectionExpanded = (direction, expanded) => {
        const body = direction?.querySelector('[data-oc-direction-body]');
        const toggle = direction?.querySelector('[data-oc-direction-toggle]');
        if (!body || !toggle) return;

        body.classList.toggle('is-expanded', expanded);
        toggle.setAttribute('aria-expanded', String(expanded));
        toggle.textContent = expanded ? 'Less' : 'More';
    };

    const configureDirectionToggle = (direction) => {
        const body = direction?.querySelector('[data-oc-direction-body]');
        const toggle = direction?.querySelector('[data-oc-direction-toggle]');
        if (!body || !toggle) return;

        const wasExpanded = toggle.getAttribute('aria-expanded') === 'true';
        setDirectionExpanded(direction, false);
        toggle.hidden = true;

        requestAnimationFrame(() => {
            const isOverflowing = body.scrollHeight > body.clientHeight + 1;
            toggle.hidden = !isOverflowing;
            if (isOverflowing && wasExpanded) {
                setDirectionExpanded(direction, true);
            }
        });
    };

    const setProgressExpanded = (entry, expanded) => {
        const body = entry?.querySelector('[data-oc-progress-body]');
        const toggle = entry?.querySelector('[data-oc-progress-toggle]');
        if (!body || !toggle) return;

        body.classList.toggle('is-expanded', expanded);
        toggle.setAttribute('aria-expanded', String(expanded));
        toggle.textContent = expanded ? 'Less' : 'More';
    };

    const configureProgressToggle = (entry) => {
        const body = entry?.querySelector('[data-oc-progress-body]');
        const toggle = entry?.querySelector('[data-oc-progress-toggle]');
        if (!body || !toggle) return;

        const wasExpanded = toggle.getAttribute('aria-expanded') === 'true';
        setProgressExpanded(entry, false);
        toggle.hidden = true;

        requestAnimationFrame(() => {
            const isOverflowing = body.scrollHeight > body.clientHeight + 1;
            toggle.hidden = !isOverflowing;
            if (isOverflowing && wasExpanded) {
                setProgressExpanded(entry, true);
            }
        });
    };

    const buildDirection = (direction, item) => {
        const wrapper = document.createElement('div');
        wrapper.className = 'oc-direction';
        wrapper.dataset.ocDirectionContent = '';

        const label = document.createElement('div');
        label.className = 'oc-direction__label';
        const labelText = document.createElement('span');
        const icon = document.createElement('i');
        icon.className = 'bi bi-file-earmark-check';
        icon.setAttribute('aria-hidden', 'true');
        labelText.append(icon, document.createTextNode('Directions from last conference'));
        label.append(labelText);

        const instruction = document.createElement('div');
        instruction.className = 'oc-direction__instruction';

        const body = document.createElement('p');
        body.className = 'oc-direction__body';
        body.dataset.ocDirectionBody = '';
        body.id = `oc-direction-body-${item.dataset.itemKind}-${item.dataset.itemId}`;
        body.textContent = direction.body;

        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'oc-direction-toggle';
        toggle.dataset.ocDirectionToggle = '';
        toggle.setAttribute('aria-controls', body.id);
        toggle.setAttribute('aria-expanded', 'false');
        toggle.textContent = 'More';
        toggle.hidden = true;
        instruction.append(body, toggle);

        const timestamp = document.createElement('time');
        timestamp.className = 'oc-direction__timestamp';
        timestamp.dateTime = direction.createdAtUtc ?? '';
        timestamp.textContent = formatIstDateTime(direction.createdAtUtc);

        wrapper.append(label, instruction, timestamp);
        return wrapper;
    };

    const buildProgressEntry = (entry, item, index) => {
        const section = document.createElement('section');
        section.className = 'oc-progress-entry';
        section.dataset.ocProgressEntry = '';

        const label = document.createElement('span');
        label.className = 'oc-progress-entry__label';
        label.textContent = entry.label ?? '';
        section.append(label);

        if (entry.title) {
            const title = document.createElement('strong');
            title.className = 'oc-progress-entry__title';
            title.textContent = entry.title;
            section.append(title);
        }

        if (entry.body) {
            const body = document.createElement('p');
            body.className = 'oc-progress-entry__body';
            body.dataset.ocProgressBody = '';
            body.id = `oc-progress-body-${item.dataset.itemKind}-${item.dataset.itemId}-${index}`;
            body.textContent = entry.body;

            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'oc-progress-toggle';
            toggle.dataset.ocProgressToggle = '';
            toggle.setAttribute('aria-controls', body.id);
            toggle.setAttribute('aria-expanded', 'false');
            toggle.textContent = 'More';
            toggle.hidden = true;
            section.append(body, toggle);
        } else if (entry.emptyText) {
            section.classList.add('oc-progress-entry--empty');
            const empty = document.createElement('p');
            empty.className = 'oc-progress-entry__empty';
            empty.textContent = entry.emptyText;
            section.append(empty);
        }

        const dateText = formatIstDateTime(entry.activityAtUtc);
        if (entry.authorName || dateText) {
            const meta = document.createElement('small');
            meta.className = 'oc-progress-entry__meta';
            if (entry.authorName) {
                const author = document.createElement('span');
                author.textContent = entry.authorName;
                meta.append(author);
            }
            if (dateText) {
                const time = document.createElement('time');
                time.dateTime = entry.activityAtUtc ?? '';
                time.textContent = dateText;
                meta.append(time);
            }
            section.append(meta);
        }

        return section;
    };

    const renderProgress = (item, payload) => {
        const host = item.querySelector('[data-oc-progress-content]');
        if (!host) return;

        host.replaceChildren();
        const label = document.createElement('span');
        label.className = 'oc-progress-label';
        label.textContent = 'Progress after direction';
        host.append(label);

        const kind = Number(item.dataset.itemKind);
        if (structuredProgressKinds.has(kind)) {
            const entries = Array.isArray(payload.progressEntries) ? payload.progressEntries : [];
            if (entries.length > 0) {
                const list = document.createElement('div');
                list.className = 'oc-progress-list';
                entries.forEach((entry, index) => {
                    const element = buildProgressEntry(entry, item, index);
                    list.append(element);
                    configureProgressToggle(element);
                });
                host.append(list);
            } else if (payload.emptyProgressText) {
                const empty = document.createElement('p');
                empty.className = 'oc-progress-empty';
                empty.textContent = payload.emptyProgressText;
                host.append(empty);
            }
            return;
        }

        const summary = document.createElement('p');
        summary.className = 'oc-task-progress-summary';
        summary.dataset.ocProgressSummary = '';
        summary.textContent = payload.progressSummary ?? '';
        host.append(summary);

        if (payload.latestProgressText) {
            const latest = document.createElement('span');
            latest.className = 'oc-latest-update';
            latest.dataset.ocLatestProgress = '';
            latest.textContent = `Latest: ${payload.latestProgressText}`;
            latest.title = payload.latestProgressText;
            host.append(latest);
        }
    };

    const applySavedDirection = (item, payload) => {
        const directionHost = item.querySelector('[data-oc-direction]');
        if (directionHost) {
            const direction = buildDirection(payload.direction, item);
            directionHost.replaceChildren(direction);
            configureDirectionToggle(direction);
        }

        renderProgress(item, payload);
        item.querySelector('.oc-item__actions')?.classList.remove('oc-item__actions--empty');
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
        editor.setAttribute('aria-busy', 'true');
        input.removeAttribute('aria-invalid');
        save.disabled = true;
        setRowStatus(item, '');
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
                const reference = payload.traceId ? ` Reference: ${payload.traceId}.` : '';
                throw new Error(`${payload.message || 'The direction could not be saved.'}${reference}`);
            }

            applySavedDirection(item, payload);
            closeEditor(editor, { restoreFocus: true });
            setRowStatus(item, 'Direction saved.');
        } catch (error) {
            editor.classList.remove('is-saving');
            editor.setAttribute('aria-busy', 'false');
            save.disabled = false;
            input.setAttribute('aria-invalid', 'true');
            const message = error instanceof Error ? error.message : 'The direction could not be saved.';
            if (feedback) {
                feedback.textContent = message;
                feedback.classList.add('is-error');
            }
            setRowStatus(item, 'Save failed.', true);
            input.focus();
        }
    };

    selector?.addEventListener('change', () => {
        const destination = selector.value;
        if (!destination) return;
        window.location.assign(destination);
    });

    root.addEventListener('click', (event) => {
        const directionToggle = event.target.closest('[data-oc-direction-toggle]');
        if (directionToggle) {
            const direction = directionToggle.closest('[data-oc-direction-content]');
            const expanded = directionToggle.getAttribute('aria-expanded') === 'true';
            setDirectionExpanded(direction, !expanded);
            return;
        }

        const progressToggle = event.target.closest('[data-oc-progress-toggle]');
        if (progressToggle) {
            const entry = progressToggle.closest('[data-oc-progress-entry]');
            const expanded = progressToggle.getAttribute('aria-expanded') === 'true';
            setProgressExpanded(entry, !expanded);
            return;
        }

        const addButton = event.target.closest('[data-oc-add]');
        if (addButton) {
            openInlineEditor(addButton.closest('[data-oc-item]'));
            return;
        }

        const cancelButton = event.target.closest('[data-oc-cancel]');
        if (cancelButton) {
            closeEditor(cancelButton.closest('[data-oc-editor]'), { restoreFocus: true });
        }
    });

    root.addEventListener('input', (event) => {
        const input = event.target.closest('[data-oc-input]');
        if (!input) return;

        input.removeAttribute('aria-invalid');
        const editor = input.closest('[data-oc-editor]');
        const save = editor?.querySelector('[data-oc-save]');
        const feedback = editor?.querySelector('[data-oc-feedback]');
        if (save) save.disabled = input.value.trim().length === 0;
        if (feedback?.classList.contains('is-error')) {
            feedback.textContent = '';
            feedback.classList.remove('is-error');
        }
    });

    root.addEventListener('keydown', (event) => {
        const input = event.target.closest('[data-oc-input]');
        if (!input) return;
        const editor = input.closest('[data-oc-editor]');
        if (!editor) return;

        if (event.key === 'Escape') {
            if (editor.classList.contains('is-saving')) return;
            event.preventDefault();
            closeEditor(editor, { restoreFocus: true });
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

    root.querySelectorAll('[data-oc-direction-content]').forEach(configureDirectionToggle);
    root.querySelectorAll('[data-oc-progress-entry]').forEach(configureProgressToggle);

    let resizeTimer = 0;
    window.addEventListener('resize', () => {
        window.clearTimeout(resizeTimer);
        resizeTimer = window.setTimeout(() => {
            root.querySelectorAll('[data-oc-direction-content]').forEach(configureDirectionToggle);
            root.querySelectorAll('[data-oc-progress-entry]').forEach(configureProgressToggle);
        }, 120);
    });
}
