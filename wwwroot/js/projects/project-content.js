(() => {
    'use strict';

    const root = document.querySelector('[data-project-content]');
    if (!root) return;

    let dirtyForm = null;
    const dynamicResetters = new WeakMap();

    const setError = (form, message) => {
        const host = form.querySelector('[data-content-error]');
        if (!host) return;
        host.textContent = message || '';
        host.classList.toggle('d-none', !message);
    };

    const markDirty = (form) => {
        dirtyForm = form;
        form.dataset.dirty = 'true';
    };

    const clearDirty = (form) => {
        if (dirtyForm === form) dirtyForm = null;
        form.dataset.dirty = 'false';
    };

    const updateWordCount = (textarea) => {
        const form = textarea.closest('form');
        const output = form?.querySelector('[data-word-count]');
        if (!output) return;
        const words = (textarea.value.match(/\S+/g) || []).length;
        const limit = Number.parseInt(textarea.dataset.wordLimit || '250', 10);
        output.textContent = `${words} words`;
        output.classList.toggle('text-danger', words > limit);
    };

    const openEditor = (pane) => {
        const view = pane.querySelector('[data-content-view]');
        const form = pane.querySelector('[data-content-form]');
        if (!view || !form) return;
        view.classList.add('d-none');
        form.classList.remove('d-none');
        form.querySelector('textarea, input:not([type="hidden"])')?.focus();
    };

    const closeEditor = (pane, force = false) => {
        const view = pane.querySelector('[data-content-view]');
        const form = pane.querySelector('[data-content-form]');
        if (!view || !form) return true;
        if (!force && form.dataset.dirty === 'true' && !window.confirm('Discard the unsaved changes in this tab?')) {
            return false;
        }
        form.reset();
        dynamicResetters.get(form)?.();
        clearDirty(form);
        setError(form, '');
        form.classList.add('d-none');
        view.classList.remove('d-none');
        form.querySelectorAll('[data-word-counter]').forEach(updateWordCount);
        return true;
    };

    root.querySelectorAll('[data-content-edit]').forEach((button) => {
        button.addEventListener('click', () => openEditor(button.closest('.tab-pane')));
    });

    root.querySelectorAll('[data-content-cancel]').forEach((button) => {
        button.addEventListener('click', () => closeEditor(button.closest('.tab-pane')));
    });

    root.querySelectorAll('[data-content-form]').forEach((form) => {
        form.addEventListener('input', () => markDirty(form));
        form.querySelectorAll('[data-word-counter]').forEach((textarea) => {
            updateWordCount(textarea);
            textarea.addEventListener('input', () => updateWordCount(textarea));
        });

        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            setError(form, '');

            const wordCounter = form.querySelector('[data-word-counter]');
            if (wordCounter) {
                const count = (wordCounter.value.match(/\S+/g) || []).length;
                const limit = Number.parseInt(wordCounter.dataset.wordLimit || '250', 10);
                if (count > limit) {
                    setError(form, `Project brief is ${count} words. Reduce it to ${limit} words or fewer.`);
                    wordCounter.focus();
                    return;
                }
            }

            const capabilityEditor = form.matches('[data-capability-editor]') ? form : null;
            if (capabilityEditor) {
                const values = [...capabilityEditor.querySelectorAll('[data-capability-row] input')]
                    .map((input) => input.value.trim())
                    .filter(Boolean);
                const normalized = values.map((value) => value.toLocaleLowerCase());
                if (new Set(normalized).size !== normalized.length) {
                    setError(form, 'Remove duplicate capability statements before saving.');
                    return;
                }
            }

            const button = form.querySelector('[data-content-save]');
            const spinner = form.querySelector('[data-content-spinner]');
            button?.setAttribute('disabled', 'disabled');
            spinner?.classList.remove('d-none');
            root.classList.add('is-saving');

            try {
                const response = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest',
                        'Accept': 'application/json'
                    }
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.ok) {
                    throw new Error(payload?.error || 'The project content could not be saved.');
                }
                clearDirty(form);
                window.location.assign(payload.redirectUrl);
            } catch (error) {
                setError(form, error instanceof Error ? error.message : 'The project content could not be saved.');
                button?.removeAttribute('disabled');
                spinner?.classList.add('d-none');
                root.classList.remove('is-saving');
            }
        });
    });

    root.querySelectorAll('[data-bs-toggle="tab"]').forEach((tab) => {
        tab.addEventListener('show.bs.tab', (event) => {
            if (!dirtyForm || dirtyForm.dataset.dirty !== 'true') return;
            const currentPane = dirtyForm.closest('.tab-pane');
            const nextTarget = event.target.getAttribute('data-bs-target');
            if (currentPane && `#${currentPane.id}` !== nextTarget &&
                !window.confirm('You have unsaved project-content changes. Discard them and change tabs?')) {
                event.preventDefault();
                return;
            }
            closeEditor(currentPane, true);
        });

        tab.addEventListener('shown.bs.tab', (event) => {
            const key = event.target.dataset.contentTab;
            if (!key) return;
            const url = new URL(window.location.href);
            url.searchParams.set('content', key);
            url.hash = `content-${key}`;
            window.history.replaceState(null, '', url);
        });
    });

    window.addEventListener('beforeunload', (event) => {
        if (!dirtyForm || dirtyForm.dataset.dirty !== 'true') return;
        event.preventDefault();
        event.returnValue = '';
    });

    const initializeCapabilityEditor = (form) => {
        const list = form.querySelector('[data-capability-list]');
        const template = form.querySelector('[data-capability-template]');
        const addButton = form.querySelector('[data-capability-add]');
        const countHost = form.querySelector('[data-capability-count]');
        const max = Number.parseInt(form.dataset.capabilityMax || '8', 10);
        if (!list || !template || !addButton) return;

        const initialMarkup = list.innerHTML;
        const rows = () => [...list.querySelectorAll('[data-capability-row]')];
        const renumber = () => {
            const currentRows = rows();
            currentRows.forEach((row, index) => {
                const number = row.querySelector('[data-capability-number]');
                const input = row.querySelector('input');
                if (number) number.textContent = String(index + 1);
                if (input) input.setAttribute('aria-label', `Capability statement ${index + 1}`);
                row.querySelector('[data-capability-up]')?.toggleAttribute('disabled', index === 0);
                row.querySelector('[data-capability-down]')?.toggleAttribute('disabled', index === currentRows.length - 1);
            });
            const populated = currentRows.filter((row) => row.querySelector('input')?.value.trim()).length;
            if (countHost) countHost.textContent = `${populated} of ${max}`;
            addButton.toggleAttribute('disabled', currentRows.length >= max);
        };

        dynamicResetters.set(form, () => {
            list.innerHTML = initialMarkup;
            renumber();
        });

        list.addEventListener('click', (event) => {
            const button = event.target.closest('button');
            const row = button?.closest('[data-capability-row]');
            if (!button || !row) return;
            if (button.matches('[data-capability-up]')) row.previousElementSibling?.before(row);
            if (button.matches('[data-capability-down]')) row.nextElementSibling?.after(row);
            if (button.matches('[data-capability-remove]')) {
                if (rows().length === 1) row.querySelector('input').value = '';
                else row.remove();
            }
            markDirty(form);
            renumber();
        });

        list.addEventListener('input', renumber);
        addButton.addEventListener('click', () => {
            if (rows().length >= max) return;
            list.append(template.content.cloneNode(true));
            markDirty(form);
            renumber();
            rows().at(-1)?.querySelector('input')?.focus();
        });
        renumber();
    };

    root.querySelectorAll('[data-capability-editor]').forEach(initializeCapabilityEditor);
})();
