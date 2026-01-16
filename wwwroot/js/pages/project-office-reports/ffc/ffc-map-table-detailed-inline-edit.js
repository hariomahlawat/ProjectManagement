// SECTION: FFC inline remark editor
(() => {
    const selector = {
        table: '.ffc-detailed-table',
        editableCell: '.ffc-dtable__editable-cell',
        display: '.ffc-dtable__remark-display',
        editor: '.ffc-dtable__remark-editor',
        textarea: '.ffc-remark-input',
        saveButton: '.ffc-remark-save',
        cancelButton: '.ffc-remark-cancel',
        status: '.ffc-dtable__save-indicator',
        error: '.ffc-dtable__error',
        savedBadge: '.ffc-dtable__saved-badge',
        tokenInput: '#ffc-inline-edit-token input[name="__RequestVerificationToken"]'
    };

    const handlers = {
        row: 'UpdateRowRemark',
        overall: 'UpdateOverallRemark'
    };

    let activeCell = null;

    const getToken = () => {
        const input = document.querySelector(selector.tokenInput);
        return input ? input.value : '';
    };

    const toggleEditor = (cell, isEditing) => {
        const display = cell.querySelector(selector.display);
        const editor = cell.querySelector(selector.editor);

        if (!display || !editor) {
            return;
        }

        display.classList.toggle('d-none', isEditing);
        editor.classList.toggle('d-none', !isEditing);
        cell.classList.toggle('editing', isEditing);

        if (isEditing) {
            const textarea = cell.querySelector(selector.textarea);
            textarea?.focus();
        }
    };

    const setStatus = (cell, message, isError = false) => {
        const status = cell.querySelector(selector.status);
        const error = cell.querySelector(selector.error);

        if (status) {
            status.textContent = isError ? '' : message;
            status.classList.toggle('d-none', isError || !message);
        }

        if (error) {
            error.textContent = isError ? message : '';
            error.classList.toggle('d-none', !isError || !message);
        }
    };

    const showSavedBadge = (cell) => {
        const badge = cell.querySelector(selector.savedBadge);
        if (!badge) {
            return;
        }

        badge.classList.remove('d-none');
        window.setTimeout(() => badge.classList.add('d-none'), 2000);
    };

    const enterEdit = (cell) => {
        if (activeCell && activeCell !== cell) {
            return;
        }

        activeCell = cell;
        cell.dataset.originalDisplay = cell.querySelector(selector.display)?.innerHTML ?? '';
        cell.dataset.originalValue = cell.querySelector(selector.textarea)?.value ?? '';
        setStatus(cell, '');
        toggleEditor(cell, true);
    };

    const exitEdit = (cell, restoreOriginal) => {
        if (!cell) {
            return;
        }

        const display = cell.querySelector(selector.display);
        const textarea = cell.querySelector(selector.textarea);

        if (restoreOriginal) {
            if (display) {
                display.innerHTML = cell.dataset.originalDisplay ?? '';
            }
            if (textarea) {
                textarea.value = cell.dataset.originalValue ?? '';
            }
        }

        setStatus(cell, '');
        toggleEditor(cell, false);
        activeCell = null;
    };

    const buildDisplayMarkup = (cell, remark) => {
        const hasRemark = Boolean(remark?.trim());
        const derived = cell.dataset.editKind === 'row' ? (cell.dataset.derived ?? '') : '';
        const hasDerived = Boolean(derived.trim());
        const emptyMessage = cell.dataset.editKind === 'overall'
            ? 'No overall remarks recorded.'
            : 'No progress remarks recorded.';

        if (hasRemark || hasDerived) {
            return '<div class="ffc-dtable__remark-text"></div><span class="ffc-dtable__saved-badge d-none" role="status">Saved</span>';
        }

        return `<span class="ffc-dtable__remarks-empty">${emptyMessage}</span>`
            + '<span class="ffc-dtable__saved-badge d-none" role="status">Saved</span>';
    };

    const updateDisplayContent = (cell, remark) => {
        const display = cell.querySelector(selector.display);
        if (!display) {
            return;
        }

        display.innerHTML = buildDisplayMarkup(cell, remark);
        const remarkNode = display.querySelector('.ffc-dtable__remark-text');

        if (remarkNode) {
            const derived = cell.dataset.editKind === 'row' ? (cell.dataset.derived ?? '') : '';
            const content = remark?.trim() ? remark : derived;
            remarkNode.textContent = content;
        }
    };

    const saveRemark = async (cell) => {
        const kind = cell.dataset.editKind;
        const handler = handlers[kind];
        const id = Number(cell.dataset.id);
        const rowVersion = cell.dataset.rowversion;
        const textarea = cell.querySelector(selector.textarea);
        const token = getToken();

        if (!handler || !id || !rowVersion || !textarea) {
            setStatus(cell, 'Unable to save. Missing data.', true);
            return;
        }

        const payload = {
            remark: textarea.value,
            rowVersion
        };

        if (kind === 'row') {
            payload.ffcProjectId = id;
        } else {
            payload.ffcRecordId = id;
        }

        setStatus(cell, 'Saving...');
        textarea.disabled = true;
        cell.querySelector(selector.saveButton)?.setAttribute('disabled', 'disabled');
        cell.querySelector(selector.cancelButton)?.setAttribute('disabled', 'disabled');

        try {
            const response = await fetch(`${window.location.pathname}?handler=${handler}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify(payload)
            });

            const data = await response.json().catch(() => ({}));

            if (!response.ok) {
                let message = data?.message || 'Unable to save remarks.';
                if (response.status === 401 || response.status === 403) {
                    message = 'Not authorised.';
                } else if (response.status === 409) {
                    message = 'Someone else updated this record. Please refresh.';
                }

                setStatus(cell, message, true);
                return;
            }

            cell.dataset.rowversion = data?.rowVersion || rowVersion;
            updateDisplayContent(cell, data?.remark ?? '');
            textarea.value = data?.remark ?? '';
            exitEdit(cell, false);
            showSavedBadge(cell);
        } catch (error) {
            setStatus(cell, 'Unable to save remarks right now.', true);
        } finally {
            textarea.disabled = false;
            cell.querySelector(selector.saveButton)?.removeAttribute('disabled');
            cell.querySelector(selector.cancelButton)?.removeAttribute('disabled');
        }
    };

    const init = () => {
        const table = document.querySelector(selector.table);
        if (!table) {
            return;
        }

        table.addEventListener('click', (event) => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const cell = target.closest(selector.editableCell);
            if (!cell || cell.classList.contains('editing')) {
                return;
            }

            if (target.closest(selector.saveButton) || target.closest(selector.cancelButton)) {
                return;
            }

            enterEdit(cell);
        });

        table.addEventListener('click', (event) => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const saveButton = target.closest(selector.saveButton);
            const cancelButton = target.closest(selector.cancelButton);
            const cell = target.closest(selector.editableCell);

            if (!cell) {
                return;
            }

            if (saveButton) {
                event.preventDefault();
                saveRemark(cell);
                return;
            }

            if (cancelButton) {
                event.preventDefault();
                exitEdit(cell, true);
            }
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
