const root = document.querySelector('[data-officer-conference]');

if (root) {
    const antiforgeryToken = root.querySelector('.oc-antiforgery input[name="__RequestVerificationToken"]')?.value ?? '';
    const selector = root.querySelector('[data-officer-selector]');
    let openEditor = null;
    let openTaskEditor = null;

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
        if (openTaskEditor) {
            closeTaskEditor(openTaskEditor);
        }

        setRowStatus(item, '');
        editor.hidden = false;
        editor.setAttribute('aria-busy', 'false');
        getAddButton(editor)?.setAttribute('aria-expanded', 'true');
        openEditor = editor;

        const input = editor.querySelector('[data-oc-input]');
        requestAnimationFrame(() => input?.focus());
    };

    const getTaskAddButton = (editor) => editor?.closest('[data-oc-section="tasks"]')?.querySelector('[data-oc-task-add]') ?? null;

    const clearTaskErrors = (editor) => {
        editor?.querySelectorAll('[data-oc-task-error]').forEach((element) => {
            element.textContent = '';
        });
        editor?.querySelectorAll('[data-oc-task-input]').forEach((input) => {
            input.removeAttribute('aria-invalid');
        });
    };

    const taskFormIsComplete = (editor) => {
        const title = editor?.querySelector('[data-oc-task-input="Title"]')?.value.trim() ?? '';
        const description = editor?.querySelector('[data-oc-task-input="Description"]')?.value.trim() ?? '';
        const dueDate = editor?.querySelector('[data-oc-task-input="DueDate"]')?.value ?? '';
        return title.length > 0
            && description.length > 0
            && dueDate.length > 0
            && (typeof editor.checkValidity !== 'function' || editor.checkValidity());
    };

    const syncTaskSaveState = (editor) => {
        const save = editor?.querySelector('[data-oc-task-save]');
        if (save) save.disabled = !taskFormIsComplete(editor) || editor.classList.contains('is-saving');
    };

    const closeTaskEditor = (editor, { clear = true, restoreFocus = false } = {}) => {
        if (!editor) return;

        editor.hidden = true;
        editor.classList.remove('is-saving');
        editor.setAttribute('aria-busy', 'false');
        clearTaskErrors(editor);

        const feedback = editor.querySelector('[data-oc-task-feedback]');
        if (feedback) {
            feedback.textContent = '';
            feedback.classList.remove('is-error');
        }

        if (clear) editor.reset();
        syncTaskSaveState(editor);

        const addButton = getTaskAddButton(editor);
        addButton?.setAttribute('aria-expanded', 'false');
        if (restoreFocus) addButton?.focus();
        if (openTaskEditor === editor) openTaskEditor = null;
    };

    const openTaskCreation = (section) => {
        const editor = section?.querySelector('[data-oc-task-editor]');
        if (!editor) return;

        if (openEditor) closeEditor(openEditor);
        if (openTaskEditor && openTaskEditor !== editor) closeTaskEditor(openTaskEditor);

        editor.hidden = false;
        editor.setAttribute('aria-busy', 'false');
        clearTaskErrors(editor);
        getTaskAddButton(editor)?.setAttribute('aria-expanded', 'true');
        openTaskEditor = editor;
        syncTaskSaveState(editor);

        requestAnimationFrame(() => editor.querySelector('[data-oc-task-input="Title"]')?.focus());
    };

    const applyTaskErrors = (editor, errors) => {
        if (!errors || typeof errors !== 'object') return;

        Object.entries(errors).forEach(([field, messages]) => {
            const input = editor.querySelector(`[data-oc-task-input="${field}"]`);
            const error = editor.querySelector(`[data-oc-task-error="${field}"]`);
            const message = Array.isArray(messages) ? messages[0] : String(messages ?? '');
            if (input) input.setAttribute('aria-invalid', 'true');
            if (error) error.textContent = message;
        });
    };

    const appendCreatedTask = (task) => {
        const section = root.querySelector('[data-oc-section="tasks"]');
        const template = section?.querySelector('[data-oc-task-item-template]');
        if (!section || !template || !task) return null;

        const item = template.content.firstElementChild.cloneNode(true);
        const currentOfficerId = section.querySelector('[data-oc-task-editor]')?.dataset.officerId ?? '';
        const itemKind = String(task.kind ?? 3);
        const itemId = String(task.itemId ?? '');
        const editorId = `oc-editor-${itemKind}-${itemId}`;
        const feedbackId = `${editorId}-feedback`;

        item.dataset.officerId = currentOfficerId;
        item.dataset.itemKind = itemKind;
        item.dataset.itemId = itemId;
        item.classList.toggle('oc-item--attention', Boolean(task.requiresAttention));

        const title = item.querySelector('[data-oc-new-task-title]');
        if (title) {
            title.textContent = task.title ?? 'New task';
            title.href = task.openUrl ?? '#';
        }

        const state = item.querySelector('[data-oc-new-task-state]');
        if (state) state.textContent = task.currentStateName ?? task.currentStateCode ?? 'Assigned';

        const context = item.querySelector('[data-oc-new-task-context]');
        if (context) context.textContent = task.currentContext ?? '';

        const attention = item.querySelector('[data-oc-new-task-attention]');
        if (attention) {
            attention.hidden = !task.attentionText;
            attention.querySelector('span').textContent = task.attentionText ?? '';
        }

        const openLink = item.querySelector('[data-oc-new-task-open]');
        if (openLink) openLink.href = task.openUrl ?? '#';

        const addDirection = item.querySelector('[data-oc-add]');
        addDirection?.setAttribute('aria-controls', editorId);

        const directionEditor = item.querySelector('[data-oc-editor]');
        if (directionEditor) {
            directionEditor.id = editorId;
            directionEditor.querySelector('input[name="officerUserId"]').value = currentOfficerId;
            directionEditor.querySelector('input[name="kind"]').value = itemKind;
            directionEditor.querySelector('input[name="itemId"]').value = itemId;
            const textarea = directionEditor.querySelector('[data-oc-input]');
            const feedback = directionEditor.querySelector('[data-oc-feedback]');
            if (textarea) textarea.setAttribute('aria-describedby', feedbackId);
            if (feedback) feedback.id = feedbackId;
        }

        let list = section.querySelector('[data-oc-item-list]');
        if (!list) {
            list = document.createElement('div');
            list.className = 'oc-item-list';
            list.dataset.ocItemList = '';
            section.querySelector('[data-oc-section-empty]')?.remove();
            template.before(list);
        }
        list.append(item);

        const count = section.querySelector('[data-oc-section-count]');
        const nextCount = Number.parseInt(count?.textContent ?? '0', 10) + 1;
        if (count) count.textContent = String(nextCount);

        const headerCount = root.querySelector('[data-oc-header-task-count]');
        const headerLabel = root.querySelector('[data-oc-header-task-label]');
        if (headerCount) headerCount.textContent = String(nextCount);
        if (headerLabel) headerLabel.textContent = nextCount === 1 ? 'other task' : 'other tasks';

        item.classList.add('is-saved');
        window.setTimeout(() => item.classList.remove('is-saved'), 1200);
        return item;
    };

    const saveTask = async (editor) => {
        if (!editor || editor.classList.contains('is-saving')) return;
        if (!taskFormIsComplete(editor)) {
            editor.reportValidity?.();
            return;
        }

        const save = editor.querySelector('[data-oc-task-save]');
        const feedback = editor.querySelector('[data-oc-task-feedback]');
        editor.classList.add('is-saving');
        editor.setAttribute('aria-busy', 'true');
        clearTaskErrors(editor);
        if (save) save.disabled = true;
        if (feedback) {
            feedback.textContent = 'Creating task…';
            feedback.classList.remove('is-error');
        }

        const data = new FormData(editor);
        if (antiforgeryToken && !data.has('__RequestVerificationToken')) {
            data.append('__RequestVerificationToken', antiforgeryToken);
        }

        try {
            const response = await fetch(editor.action, {
                method: 'POST',
                body: data,
                headers: antiforgeryToken ? { 'X-CSRF-TOKEN': antiforgeryToken } : {},
                credentials: 'same-origin'
            });
            const payload = await response.json().catch(() => ({}));
            if (!response.ok) {
                applyTaskErrors(editor, payload.errors);
                const reference = payload.traceId ? ` Reference: ${payload.traceId}.` : '';
                throw new Error(`${payload.message || 'The task could not be assigned.'}${reference}`);
            }

            const item = appendCreatedTask(payload.task);
            closeTaskEditor(editor, { restoreFocus: true });
            if (item) {
                setRowStatus(item, 'Task assigned.');
                item.querySelector('[data-oc-new-task-title]')?.focus({ preventScroll: true });
            }
        } catch (error) {
            editor.classList.remove('is-saving');
            editor.setAttribute('aria-busy', 'false');
            syncTaskSaveState(editor);
            const message = error instanceof Error ? error.message : 'The task could not be assigned.';
            if (feedback) {
                feedback.textContent = message;
                feedback.classList.add('is-error');
            }
            const firstInvalid = editor.querySelector('[aria-invalid="true"]');
            (firstInvalid ?? editor.querySelector('[data-oc-task-input="Title"]'))?.focus();
        }
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
        labelText.append(icon, document.createTextNode('Latest conference direction'));
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
    };

    const applySavedDirection = (item, payload) => {
        const directionHost = item.querySelector('[data-oc-direction]');
        if (directionHost) {
            const direction = buildDirection(payload.direction, item);
            directionHost.replaceChildren(direction);
            directionHost.classList.remove('oc-item__direction--empty');
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
        const addTaskButton = event.target.closest('[data-oc-task-add]');
        if (addTaskButton) {
            openTaskCreation(addTaskButton.closest('[data-oc-section="tasks"]'));
            return;
        }

        const cancelTaskButton = event.target.closest('[data-oc-task-cancel]');
        if (cancelTaskButton) {
            closeTaskEditor(cancelTaskButton.closest('[data-oc-task-editor]'), { restoreFocus: true });
            return;
        }

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
        const taskInput = event.target.closest('[data-oc-task-input]');
        if (taskInput) {
            const editor = taskInput.closest('[data-oc-task-editor]');
            taskInput.removeAttribute('aria-invalid');
            const field = taskInput.dataset.ocTaskInput;
            const error = editor?.querySelector(`[data-oc-task-error="${field}"]`);
            if (error) error.textContent = '';
            const feedback = editor?.querySelector('[data-oc-task-feedback]');
            if (feedback?.classList.contains('is-error')) {
                feedback.textContent = '';
                feedback.classList.remove('is-error');
            }
            syncTaskSaveState(editor);
            return;
        }

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

    root.addEventListener('change', (event) => {
        const taskInput = event.target.closest('[data-oc-task-input]');
        if (!taskInput) return;
        syncTaskSaveState(taskInput.closest('[data-oc-task-editor]'));
    });

    root.addEventListener('keydown', (event) => {
        const taskEditor = event.target.closest('[data-oc-task-editor]');
        if (taskEditor) {
            if (event.key === 'Escape') {
                if (taskEditor.classList.contains('is-saving')) return;
                event.preventDefault();
                closeTaskEditor(taskEditor, { restoreFocus: true });
                return;
            }
            if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
                event.preventDefault();
                void saveTask(taskEditor);
                return;
            }
        }

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
        const taskEditor = event.target.closest('[data-oc-task-editor]');
        if (taskEditor) {
            event.preventDefault();
            void saveTask(taskEditor);
            return;
        }

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
