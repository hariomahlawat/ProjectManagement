(function () {
    if (typeof bootstrap === 'undefined') {
        return;
    }

    const remarksNamespace = window.ProjectRemarks || {};
    const showToast = typeof remarksNamespace.showToast === 'function'
        ? remarksNamespace.showToast
        : (message) => {
            if (!message) {
                return;
            }

            if (typeof window !== 'undefined' && typeof window.alert === 'function') {
                window.alert(message);
            }
        };

    function parseErrorResponse(response) {
        if (!response) {
            return Promise.resolve('Unable to complete the request.');
        }

        return response.json().then((data) => {
            if (data && typeof data.error === 'string' && data.error.trim().length > 0) {
                return data.error;
            }

            return 'Unable to complete the request.';
        }).catch(() => 'Unable to complete the request.');
    }

    function initProliferationEditor() {
        const offcanvas = document.getElementById('offcanvasProliferation');
        const form = offcanvas?.querySelector('[data-proliferation-form]');
        if (!offcanvas || !(form instanceof HTMLFormElement)) {
            return;
        }

        const reasonWrap = form.querySelector('[data-proliferation-reason-wrap]');
        const reasonInput = form.querySelector('[name="ProliferationInput.NotAvailableReason"]');
        const availabilityInputs = Array.from(form.querySelectorAll('[name="ProliferationInput.AvailableForProliferation"]'));
        const errorSummary = form.querySelector('[data-proliferation-errors]');
        const saveButton = form.querySelector('[data-proliferation-save]');
        const spinner = form.querySelector('[data-proliferation-spinner]');
        const saveLabel = form.querySelector('[data-proliferation-save-label]');
        const card = document.getElementById('project-proliferation-card');

        function availabilityValue() {
            const selected = availabilityInputs.find((input) => input.checked);
            return selected?.value ?? '';
        }

        function updateReasonVisibility() {
            const notAvailable = availabilityValue() === 'false';
            reasonWrap?.classList.toggle('d-none', !notAvailable);
            if (reasonInput instanceof HTMLTextAreaElement) {
                reasonInput.required = notAvailable;
                if (!notAvailable) {
                    reasonInput.setCustomValidity('');
                }
            }
        }

        function updateCharacterCount(textarea) {
            if (!(textarea instanceof HTMLTextAreaElement) || !textarea.id) {
                return;
            }

            const counter = form.querySelector(`[data-character-count-for="${textarea.id}"]`);
            if (counter) {
                const maximum = Number.parseInt(textarea.getAttribute('maxlength') || '500', 10);
                counter.textContent = `${textarea.value.length} / ${Number.isFinite(maximum) ? maximum : 500}`;
            }
        }

        function clearErrors() {
            form.querySelectorAll('[data-proliferation-field-error]').forEach((element) => {
                element.textContent = '';
            });

            if (errorSummary) {
                errorSummary.textContent = '';
                errorSummary.classList.add('d-none');
            }
        }

        function normalizeFieldName(key) {
            if (typeof key !== 'string' || key.length === 0) {
                return '';
            }

            const parts = key.split('.');
            return parts[parts.length - 1];
        }

        function renderErrors(errors, fallbackMessage) {
            clearErrors();
            const messages = [];

            if (errors && typeof errors === 'object') {
                Object.entries(errors).forEach(([key, values]) => {
                    const fieldName = normalizeFieldName(key);
                    const fieldTarget = form.querySelector(`[data-proliferation-field-error="${fieldName}"]`);
                    const fieldMessages = Array.isArray(values) ? values : [values];
                    const cleanMessages = fieldMessages
                        .filter((value) => typeof value === 'string' && value.trim().length > 0)
                        .map((value) => value.trim());

                    if (fieldTarget && cleanMessages.length > 0) {
                        fieldTarget.textContent = cleanMessages[0];
                    }

                    messages.push(...cleanMessages);
                });
            }

            if (messages.length === 0 && fallbackMessage) {
                messages.push(fallbackMessage);
            }

            if (errorSummary && messages.length > 0) {
                const list = document.createElement('ul');
                list.className = 'mb-0 ps-3';
                Array.from(new Set(messages)).forEach((message) => {
                    const item = document.createElement('li');
                    item.textContent = message;
                    list.appendChild(item);
                });
                errorSummary.appendChild(list);
                errorSummary.classList.remove('d-none');
            }

            const firstError = form.querySelector('[data-proliferation-field-error]:not(:empty)');
            firstError?.closest('.mb-4, fieldset')?.querySelector('input, textarea')?.focus();
        }

        function setBusy(busy) {
            if (saveButton instanceof HTMLButtonElement) {
                saveButton.disabled = busy;
            }
            spinner?.classList.toggle('d-none', !busy);
            if (saveLabel) {
                saveLabel.textContent = busy ? 'Saving…' : 'Save details';
            }
        }

        function updateCard(profile) {
            if (!card || !profile) {
                return;
            }

            const cost = card.querySelector('[data-proliferation-cost]');
            const status = card.querySelector('[data-proliferation-status]');
            const statusWrap = card.querySelector('[data-proliferation-status-wrap]');

            if (cost && typeof profile.costDisplay === 'string') {
                cost.textContent = profile.costDisplay;
            }
            if (status && typeof profile.availabilityDisplay === 'string') {
                status.textContent = profile.availabilityDisplay;
            }
            if (statusWrap && typeof profile.availabilityTone === 'string') {
                statusWrap.classList.remove(
                    'project-intelligence-card__status--positive',
                    'project-intelligence-card__status--negative',
                    'project-intelligence-card__status--neutral');
                statusWrap.classList.add(`project-intelligence-card__status--${profile.availabilityTone}`);
            }
        }

        function synchronizeForm(profile) {
            if (!profile) {
                return;
            }

            const costInput = form.querySelector('[name="ProliferationInput.CostLakhs"]');
            if (costInput instanceof HTMLInputElement) {
                costInput.value = profile.costLakhs == null ? '' : String(profile.costLakhs);
            }

            const desiredAvailability = profile.availableForProliferation == null
                ? ''
                : String(profile.availableForProliferation);
            availabilityInputs.forEach((input) => {
                input.checked = input.value === desiredAvailability;
            });

            if (reasonInput instanceof HTMLTextAreaElement) {
                reasonInput.value = profile.notAvailableReason || '';
                updateCharacterCount(reasonInput);
            }

            const remarksInput = form.querySelector('[name="ProliferationInput.Remarks"]');
            if (remarksInput instanceof HTMLTextAreaElement) {
                remarksInput.value = profile.remarks || '';
                updateCharacterCount(remarksInput);
            }

            const updated = form.querySelector('[data-proliferation-updated] span');
            if (updated && typeof profile.updatedDisplay === 'string') {
                updated.textContent = profile.updatedDisplay;
            }

            updateReasonVisibility();
        }

        availabilityInputs.forEach((input) => {
            input.addEventListener('change', () => {
                updateReasonVisibility();
                clearErrors();
            });
        });

        form.querySelectorAll('textarea[maxlength]').forEach((textarea) => {
            updateCharacterCount(textarea);
            textarea.addEventListener('input', () => updateCharacterCount(textarea));
        });

        offcanvas.addEventListener('shown.bs.offcanvas', () => {
            clearErrors();
            updateReasonVisibility();
            const costInput = form.querySelector('[name="ProliferationInput.CostLakhs"]');
            costInput?.focus();
        });

        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            clearErrors();

            if (availabilityValue() === 'false' && reasonInput instanceof HTMLTextAreaElement && !reasonInput.value.trim()) {
                renderErrors({ NotAvailableReason: ['Enter the reason the project is not available for proliferation.'] });
                return;
            }

            if (!form.checkValidity()) {
                form.reportValidity();
                return;
            }

            setBusy(true);
            try {
                const response = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    credentials: 'same-origin',
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest',
                        'Accept': 'application/json'
                    }
                });

                let payload = null;
                try {
                    payload = await response.json();
                } catch (error) {
                    payload = null;
                }

                if (!response.ok || !payload?.success) {
                    renderErrors(payload?.errors, payload?.error || 'Unable to update proliferation details.');
                    return;
                }

                updateCard(payload.profile);
                synchronizeForm(payload.profile);
                bootstrap.Offcanvas.getOrCreateInstance(offcanvas).hide();
                showToast(payload.message || 'Proliferation details updated.', 'success');
            } catch (error) {
                renderErrors(null, 'A network error prevented the proliferation details from being saved.');
            } finally {
                setBusy(false);
            }
        });

        updateReasonVisibility();
    }

    function initProjectModeration() {
        const tokenInput = document.querySelector('[data-project-moderation-token]');
        if (!tokenInput) {
            return;
        }

        const trashModal = document.getElementById('projectTrashModal');
        if (trashModal) {
            const reasonInput = trashModal.querySelector('[data-project-trash-reason]');
            const errorContainer = trashModal.querySelector('[data-project-trash-errors]');
            trashModal.addEventListener('shown.bs.modal', () => {
                if (reasonInput instanceof HTMLTextAreaElement) {
                    reasonInput.focus();
                }
            });
            trashModal.addEventListener('hidden.bs.modal', () => {
                if (reasonInput instanceof HTMLTextAreaElement) {
                    reasonInput.value = '';
                }
                if (errorContainer) {
                    errorContainer.textContent = '';
                    errorContainer.classList.add('d-none');
                }
            });
        }

        async function handleModeration(button) {
            const action = button.getAttribute('data-action');
            const endpoint = button.getAttribute('data-endpoint');
            if (!action || !endpoint) {
                return;
            }

            const modalEl = button.closest('.modal');
            const modalInstance = modalEl ? bootstrap.Modal.getOrCreateInstance(modalEl) : null;
            let payload = {};
            const headers = {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': tokenInput.value
            };

            if (action === 'trash' && modalEl) {
                const reasonInput = modalEl.querySelector('[data-project-trash-reason]');
                const errorContainer = modalEl.querySelector('[data-project-trash-errors]');
                const reason = typeof reasonInput?.value === 'string' ? reasonInput.value.trim() : '';
                if (errorContainer) {
                    errorContainer.textContent = '';
                    errorContainer.classList.add('d-none');
                }

                if (!reason) {
                    if (errorContainer) {
                        errorContainer.textContent = 'Please provide a reason to move this project to Trash.';
                        errorContainer.classList.remove('d-none');
                    }
                    reasonInput?.focus();
                    return;
                }

                payload = { reason };
            }

            button.disabled = true;
            button.classList.add('disabled');

            try {
                const response = await fetch(endpoint, {
                    method: 'POST',
                    headers,
                    body: JSON.stringify(payload),
                    credentials: 'include'
                });

                if (response.ok) {
                    modalInstance?.hide();
                    window.location.reload();
                    return;
                }

                const message = await parseErrorResponse(response);

                if (action === 'trash' && modalEl) {
                    const errorContainer = modalEl.querySelector('[data-project-trash-errors]');
                    if (errorContainer) {
                        errorContainer.textContent = message;
                        errorContainer.classList.remove('d-none');
                    } else {
                        showToast(message, 'danger');
                    }
                } else {
                    showToast(message, 'danger');
                }
            } catch (error) {
                showToast('A network error prevented the request from completing.', 'danger');
            } finally {
                button.disabled = false;
                button.classList.remove('disabled');
            }
        }

        document.querySelectorAll('[data-project-moderation-submit]').forEach((button) => {
            button.addEventListener('click', (event) => {
                event.preventDefault();
                if (button.disabled) {
                    return;
                }
                handleModeration(button);
            });
        });
    }


    function initJdpEditor() {
        const offcanvas = document.getElementById('offcanvasJdp');
        const root = offcanvas?.querySelector('[data-jdp-root]');
        if (!offcanvas || !root) {
            return;
        }

        const form = root.querySelector('[data-jdp-form]');
        const removeForm = root.querySelector('[data-jdp-remove-form]');
        const card = document.getElementById('project-jdp-card');
        const searchInput = form?.querySelector('[data-jdp-search]');
        const partnerIdInput = form?.querySelector('[data-jdp-partner-id]');
        const results = form?.querySelector('[data-jdp-results]');
        const selection = form?.querySelector('[data-jdp-selection]');
        const selectedName = form?.querySelector('[data-jdp-selected-name]');
        const selectedMeta = form?.querySelector('[data-jdp-selected-meta]');
        const clearButton = form?.querySelector('[data-jdp-clear]');
        const errorTarget = form?.querySelector('[data-jdp-error]');
        const saveButton = form?.querySelector('[data-jdp-save]');
        const saveSpinner = form?.querySelector('[data-jdp-spinner]');
        const saveLabel = form?.querySelector('[data-jdp-save-label]');
        const optionsUrl = form?.getAttribute('data-options-url');

        let searchTimer = null;
        let searchController = null;

        function setError(message) {
            if (!errorTarget) {
                return;
            }

            errorTarget.textContent = message || '';
            errorTarget.classList.toggle('d-none', !message);
        }

        function setBusy(busy, mode = 'save') {
            if (saveButton instanceof HTMLButtonElement) {
                saveButton.disabled = busy;
            }

            const removeButton = removeForm?.querySelector('[data-jdp-remove]');
            if (removeButton instanceof HTMLButtonElement) {
                removeButton.disabled = busy;
            }

            saveSpinner?.classList.toggle('d-none', !busy || mode !== 'save');
            if (saveLabel) {
                saveLabel.textContent = busy ? 'Saving…' : (partnerIdInput?.value ? 'Save JDP' : 'Link JDP');
            }
        }

        function closeResults() {
            if (!results) {
                return;
            }

            results.classList.add('d-none');
            results.replaceChildren();
            searchInput?.setAttribute('aria-expanded', 'false');
        }

        function selectOption(option) {
            if (!form || !partnerIdInput || !searchInput) {
                return;
            }

            partnerIdInput.value = String(option.id);
            searchInput.value = option.name;
            searchInput.setAttribute('aria-expanded', 'false');

            if (selectedName) {
                selectedName.textContent = option.name;
            }
            if (selectedMeta) {
                selectedMeta.textContent = option.usageSummary || 'Not linked to any other project';
            }

            selection?.classList.remove('d-none');
            clearButton?.classList.remove('d-none');
            setError('');
            closeResults();

            if (saveLabel) {
                saveLabel.textContent = 'Save JDP';
            }
        }

        function clearSelection({ clearSearch = true } = {}) {
            if (partnerIdInput) {
                partnerIdInput.value = '';
            }

            if (clearSearch && searchInput) {
                searchInput.value = '';
            }

            selection?.classList.add('d-none');
            clearButton?.classList.add('d-none');
            setError('');

            if (saveLabel) {
                saveLabel.textContent = 'Link JDP';
            }
        }

        function renderResults(items) {
            if (!results) {
                return;
            }

            results.replaceChildren();
            const options = Array.isArray(items) ? items : [];

            if (options.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'project-jdp-picker__empty';
                empty.textContent = 'No matching organisation found.';
                results.appendChild(empty);
            } else {
                options.forEach((option) => {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'project-jdp-picker__option';
                    button.setAttribute('role', 'option');
                    button.setAttribute('aria-selected', option.isLinkedToProject ? 'true' : 'false');

                    const identity = document.createElement('span');
                    identity.className = 'project-jdp-picker__option-identity';

                    const name = document.createElement('strong');
                    name.textContent = option.name;
                    identity.appendChild(name);

                    if (option.location) {
                        const location = document.createElement('small');
                        location.textContent = option.location;
                        identity.appendChild(location);
                    }

                    const usage = document.createElement('span');
                    usage.className = 'project-jdp-picker__option-usage';
                    usage.textContent = option.isLinkedToProject
                        ? 'Linked to this project'
                        : (option.usageSummary || 'Not linked to any other project');

                    button.append(identity, usage);
                    button.addEventListener('click', () => selectOption(option));
                    results.appendChild(button);
                });
            }

            results.classList.remove('d-none');
            searchInput?.setAttribute('aria-expanded', 'true');
        }

        async function searchPartners(query) {
            if (!optionsUrl || !results) {
                return;
            }

            searchController?.abort();
            searchController = new AbortController();

            try {
                const url = new URL(optionsUrl, window.location.origin);
                if (query) {
                    url.searchParams.set('q', query);
                } else {
                    url.searchParams.delete('q');
                }

                const response = await fetch(url, {
                    credentials: 'same-origin',
                    headers: {
                        'Accept': 'application/json',
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    signal: searchController.signal
                });

                if (!response.ok) {
                    closeResults();
                    return;
                }

                const payload = await response.json();
                renderResults(payload?.items);
            } catch (error) {
                if (error?.name !== 'AbortError') {
                    closeResults();
                }
            }
        }

        function renderOtherProjects(profile) {
            const section = root.querySelector('[data-jdp-projects-section]');
            const list = root.querySelector('[data-jdp-project-list]');
            const count = root.querySelector('[data-jdp-project-count]');
            const projects = Array.isArray(profile?.otherProjects) ? profile.otherProjects : [];

            section?.classList.toggle('d-none', projects.length === 0);
            if (count) {
                count.textContent = String(projects.length);
            }
            if (!list) {
                return;
            }

            list.replaceChildren();
            projects.forEach((project) => {
                const link = document.createElement('a');
                link.className = 'project-jdp-editor__project';
                link.href = `/Projects/Overview/${project.projectId}`;

                const identity = document.createElement('span');
                identity.className = 'min-w-0';

                const name = document.createElement('strong');
                name.textContent = project.projectName;
                identity.appendChild(name);

                if (project.caseFileNumber) {
                    const fileNumber = document.createElement('small');
                    fileNumber.textContent = project.caseFileNumber;
                    identity.appendChild(fileNumber);
                }

                const status = document.createElement('span');
                const statusKey = String(project.statusLabel || 'other').toLowerCase();
                status.className = `badge rounded-pill project-jdp-editor__status project-jdp-editor__status--${statusKey}`;
                status.textContent = project.statusLabel || 'Other';

                link.append(identity, status);
                list.appendChild(link);
            });
        }

        function updateProfile(profile) {
            if (!profile) {
                return;
            }

            const hasJdp = !!profile.hasJdp;
            const cardTitle = card?.querySelector('[data-jdp-card-title]');
            const cardSummary = card?.querySelector('[data-jdp-card-summary]');
            if (cardTitle) {
                const titleText = profile.cardTitle || (hasJdp ? profile.partnerName : 'No JDP linked');
                cardTitle.textContent = titleText;
                cardTitle.setAttribute('title', titleText || '');
            }
            if (cardSummary) {
                const summaryText = profile.cardSummary || (hasJdp
                    ? 'Not linked to any other project'
                    : 'Link an industry partner');
                cardSummary.textContent = summaryText;
                cardSummary.setAttribute('title', summaryText);
            }

            const lowerPanel = document.querySelector('[data-jdp-lower-panel]');
            if (lowerPanel) {
                const lowerEmpty = lowerPanel.querySelector('[data-jdp-lower-empty]');
                const lowerLinked = lowerPanel.querySelector('[data-jdp-lower-linked]');
                const lowerName = lowerPanel.querySelector('[data-jdp-lower-name]');
                const lowerLocation = lowerPanel.querySelector('[data-jdp-lower-location]');
                const lowerSummary = lowerPanel.querySelector('[data-jdp-lower-summary]');
                const lowerLink = lowerPanel.querySelector('[data-jdp-lower-link]');

                lowerEmpty?.classList.toggle('d-none', hasJdp);
                lowerLinked?.classList.toggle('d-none', !hasJdp);

                if (lowerName) {
                    lowerName.textContent = profile.partnerName || '';
                }
                if (lowerLocation) {
                    lowerLocation.textContent = profile.partnerLocation || '';
                    lowerLocation.classList.toggle('d-none', !profile.partnerLocation);
                }
                if (lowerSummary) {
                    lowerSummary.textContent = profile.cardSummary || '';
                }
                if (lowerLink instanceof HTMLAnchorElement && profile.partnerId) {
                    const projectId = root.getAttribute('data-project-id');
                    lowerLink.href = `/IndustryPartners?id=${profile.partnerId}&projectId=${projectId}&tab=projects`;
                }
            }

            const lowerCard = document.querySelector('.project-exploitation-card--jdp');
            const lowerBadge = lowerCard?.querySelector('.pm-right-summary-card__header .badge');
            if (lowerBadge) {
                lowerBadge.textContent = hasJdp ? 'Linked' : 'Not linked';
                lowerBadge.classList.toggle('text-bg-primary', hasJdp);
                lowerBadge.classList.toggle('text-bg-secondary', !hasJdp);
            }
            document.querySelectorAll('[data-jdp-lower-action]').forEach((action) => {
                action.textContent = hasJdp ? 'Manage JDP' : 'Link JDP';
                if (action.classList.contains('btn')) {
                    action.classList.toggle('btn-primary', !hasJdp);
                    action.classList.toggle('btn-outline-secondary', hasJdp);
                }
            });

            const linkedSummary = root.querySelector('[data-jdp-linked-summary]');
            const emptyState = root.querySelector('[data-jdp-empty-state]');
            linkedSummary?.classList.toggle('d-none', !hasJdp);
            emptyState?.classList.toggle('d-none', hasJdp);

            const partnerName = root.querySelector('[data-jdp-partner-name]');
            const partnerLocation = root.querySelector('[data-jdp-partner-location]');
            const partnerLink = root.querySelector('[data-jdp-partner-link]');
            if (partnerName) {
                partnerName.textContent = profile.partnerName || '';
            }
            if (partnerLocation) {
                partnerLocation.textContent = profile.partnerLocation || '';
                partnerLocation.classList.toggle('d-none', !profile.partnerLocation);
            }
            if (partnerLink && profile.partnerId) {
                const projectId = root.getAttribute('data-project-id');
                partnerLink.href = `/IndustryPartners?id=${profile.partnerId}&projectId=${projectId}&tab=projects`;
            }

            const warning = root.querySelector('[data-jdp-multiple-warning]');
            warning?.classList.toggle('d-none', !profile.hasMultipleProjectLinks);

            renderOtherProjects(profile);

            if (partnerIdInput) {
                partnerIdInput.value = profile.partnerId == null ? '' : String(profile.partnerId);
            }
            if (searchInput) {
                searchInput.value = profile.partnerName || '';
            }
            if (selectedName) {
                selectedName.textContent = profile.partnerName || '';
            }
            if (selectedMeta) {
                selectedMeta.textContent = profile.cardSummary || '';
            }

            selection?.classList.toggle('d-none', !hasJdp);
            clearButton?.classList.toggle('d-none', !hasJdp);
            removeForm?.classList.toggle('d-none', !hasJdp);

            if (saveLabel) {
                saveLabel.textContent = hasJdp ? 'Save JDP' : 'Link JDP';
            }
        }

        if (form instanceof HTMLFormElement && searchInput instanceof HTMLInputElement) {
            searchInput.addEventListener('focus', () => {
                searchPartners(searchInput.value.trim());
            });

            searchInput.addEventListener('input', () => {
                clearSelection({ clearSearch: false });
                window.clearTimeout(searchTimer);
                searchTimer = window.setTimeout(() => {
                    searchPartners(searchInput.value.trim());
                }, 220);
            });

            searchInput.addEventListener('keydown', (event) => {
                if (event.key === 'Escape') {
                    closeResults();
                    return;
                }

                if (event.key === 'ArrowDown') {
                    const firstOption = results?.querySelector('[role="option"]');
                    if (firstOption instanceof HTMLButtonElement) {
                        event.preventDefault();
                        firstOption.focus();
                    }
                }
            });

            clearButton?.addEventListener('click', () => {
                clearSelection();
                closeResults();
                searchInput.focus();
            });

            form.addEventListener('submit', async (event) => {
                event.preventDefault();
                setError('');

                if (!partnerIdInput?.value) {
                    setError('Select a JDP from the search results.');
                    searchInput.focus();
                    return;
                }

                setBusy(true);
                try {
                    const response = await fetch(form.action, {
                        method: 'POST',
                        body: new FormData(form),
                        credentials: 'same-origin',
                        headers: {
                            'Accept': 'application/json',
                            'X-Requested-With': 'XMLHttpRequest'
                        }
                    });

                    const payload = await response.json().catch(() => null);
                    if (!response.ok) {
                        setError(payload?.error || 'Unable to update the JDP.');
                        return;
                    }

                    updateProfile(payload?.profile);
                    bootstrap.Offcanvas.getOrCreateInstance(offcanvas).hide();
                    showToast(payload?.message || 'JDP updated.', 'success');
                } catch (error) {
                    setError('A network error prevented the JDP from being updated.');
                } finally {
                    setBusy(false);
                }
            });
        }

        if (removeForm instanceof HTMLFormElement) {
            removeForm.addEventListener('submit', async (event) => {
                event.preventDefault();
                setError('');

                if (!window.confirm('Remove the JDP from this project? The organisation and its links to other projects will remain unchanged.')) {
                    return;
                }

                setBusy(true, 'remove');
                try {
                    const response = await fetch(removeForm.action, {
                        method: 'POST',
                        body: new FormData(removeForm),
                        credentials: 'same-origin',
                        headers: {
                            'Accept': 'application/json',
                            'X-Requested-With': 'XMLHttpRequest'
                        }
                    });

                    const payload = await response.json().catch(() => null);
                    if (!response.ok) {
                        setError(payload?.error || 'Unable to remove the JDP.');
                        return;
                    }

                    updateProfile(payload?.profile);
                    bootstrap.Offcanvas.getOrCreateInstance(offcanvas).hide();
                    showToast(payload?.message || 'JDP removed from the project.', 'success');
                } catch (error) {
                    setError('A network error prevented the JDP from being removed.');
                } finally {
                    setBusy(false);
                }
            });
        }

        offcanvas.addEventListener('shown.bs.offcanvas', () => {
            setError('');
            if (searchInput instanceof HTMLInputElement && !searchInput.value) {
                searchInput.focus();
            }
        });

        document.addEventListener('click', (event) => {
            if (!results || results.classList.contains('d-none')) {
                return;
            }

            if (event.target instanceof Node &&
                !results.contains(event.target) &&
                event.target !== searchInput) {
                closeResults();
            }
        });
    }

    initProjectModeration();
    initProliferationEditor();
    initJdpEditor();

    function setBackfillVisibility(hasBackfill) {
        const banner = document.querySelector('[data-backfill-banner]');
        if (banner) {
            banner.classList.toggle('d-none', !hasBackfill);
        }

        const summaryBadge = document.querySelector('[data-backfill-summary]');
        if (summaryBadge) {
            summaryBadge.classList.toggle('d-none', !hasBackfill);
        }
    }

    document.addEventListener('pm:backfill-state-changed', (event) => {
        const hasBackfill = !!event.detail?.hasBackfill;
        setBackfillVisibility(hasBackfill);
    });

    const procurement = document.getElementById('offcanvasProcurement');
    if (procurement) {
        procurement.addEventListener('shown.bs.offcanvas', function () {
            const firstField = procurement.querySelector('input,select,textarea');
            if (firstField) {
                firstField.focus();
            }
        });

        const marker = document.getElementById('open-procurement');
        if (marker && marker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(procurement);
            instance.show();
        }
    }

    const assignRoles = document.getElementById('offcanvasAssignRoles');
    if (assignRoles) {
        assignRoles.addEventListener('shown.bs.offcanvas', function () {
            const firstField = assignRoles.querySelector('select, input, textarea');
            if (firstField) {
                firstField.focus();
            }
        });

        const assignMarker = document.getElementById('open-assign-roles');
        if (assignMarker && assignMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(assignRoles);
            instance.show();
        }
    }

    const planEdit = document.getElementById('offcanvasPlanEdit');
    if (planEdit) {
        planEdit.addEventListener('shown.bs.offcanvas', function () {
            const firstDate = planEdit.querySelector('input[type="date"]');
            if (firstDate) {
                firstDate.focus();
            }
        });

        const planMarker = document.getElementById('open-plan-edit');
        if (planMarker && planMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(planEdit);
            instance.show();
        }
    }

    const actualsEdit = document.getElementById('offcanvasActualDates');
    if (actualsEdit) {
        actualsEdit.addEventListener('shown.bs.offcanvas', function () {
            const firstDate = actualsEdit.querySelector('input[type="date"]');
            if (firstDate) {
                firstDate.focus();
            }
        });

        const actualsMarker = document.getElementById('open-actuals-edit');
        if (actualsMarker && actualsMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(actualsEdit);
            instance.show();
        }
    }

    // SECTION: Backfill modal handling
    const backfillModal = document.getElementById('backfillModal');
    if (backfillModal) {
        const openButtons = document.querySelectorAll('[data-action="open-backfill"]');
        const modalInstance = bootstrap.Modal.getOrCreateInstance(backfillModal);
        const submitButton = backfillModal.querySelector('#submitBackfillBtn');
        const form = backfillModal.querySelector('[data-backfill-form]');
        const errorContainer = backfillModal.querySelector('[data-backfill-errors]');
        const projectInput = backfillModal.querySelector('[data-backfill-project]');
        const tokenInput = backfillModal.querySelector('[data-backfill-token]');
        const emptyMessage = backfillModal.querySelector('[data-backfill-empty-message]');

        function stageRows() {
            return Array.from(backfillModal.querySelectorAll('[data-backfill-row]'));
        }

        function toggleSubmitState(disabled) {
            if (!submitButton) {
                return;
            }

            submitButton.disabled = disabled || stageRows().length === 0;
        }

        function clearErrors() {
            if (!errorContainer) {
                return;
            }

            errorContainer.classList.add('d-none');
            errorContainer.innerHTML = '';
        }

        function renderErrors(messages) {
            if (!errorContainer) {
                return;
            }

            if (!Array.isArray(messages) || messages.length === 0) {
                clearErrors();
                return;
            }

            const safe = messages
                .filter((msg) => typeof msg === 'string' && msg.trim().length > 0)
                .map((msg) => msg
                    .replace(/&/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;'));

            if (safe.length === 0) {
                clearErrors();
                return;
            }

            errorContainer.classList.remove('d-none');
            errorContainer.innerHTML = safe.map((line) => `<div>${line}</div>`).join('');
        }

        function collectPayload() {
            const projectId = Number.parseInt(projectInput?.value || '0', 10);
            const stages = stageRows()
                .map((row) => {
                    const stageCode = row.getAttribute('data-stage-code') || '';
                    const startInput = row.querySelector('[data-backfill-start]');
                    const completedInput = row.querySelector('[data-backfill-completed]');
                    const actualStart = startInput && startInput.value ? startInput.value : null;
                    const completedOn = completedInput && completedInput.value ? completedInput.value : null;

                    return {
                        stageCode,
                        actualStart,
                        completedOn
                    };
                })
                .filter((stage) => stage.stageCode && (stage.actualStart || stage.completedOn));

            return {
                projectId,
                stages
            };
        }

        openButtons.forEach((button) => {
            button.addEventListener('click', (event) => {
                event.preventDefault();

                if (emptyMessage) {
                    emptyMessage.classList.toggle('d-none', stageRows().length > 0);
                }

                clearErrors();
                toggleSubmitState(false);
                modalInstance.show();
            });
        });

        backfillModal.addEventListener('shown.bs.modal', () => {
            const firstInput = backfillModal.querySelector('[data-backfill-completed], [data-backfill-start]');
            if (firstInput instanceof HTMLInputElement) {
                firstInput.focus();
            }
        });

        backfillModal.addEventListener('hidden.bs.modal', () => {
            clearErrors();
        });

        async function submitBackfill() {
            if (!submitButton || !tokenInput) {
                return;
            }

            const payload = collectPayload();

            if (!payload.projectId || payload.stages.length === 0) {
                renderErrors(['Add at least one stage update before saving.']);
                return;
            }

            toggleSubmitState(true);
            clearErrors();

            try {
                const response = await fetch('/Projects/Stages/BackfillApply', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-CSRF-TOKEN': tokenInput.value
                    },
                    body: JSON.stringify(payload),
                    credentials: 'same-origin'
                });

                if (response.ok) {
                    const instance = bootstrap.Modal.getInstance(backfillModal);
                    instance?.hide();
                    showToast('Stage completion details updated.', 'success');
                    setTimeout(() => window.location.reload(), 500);
                    return;
                }

                if (response.status === 422) {
                    const data = await response.json().catch(() => null);
                    renderErrors(Array.isArray(data?.details) ? data.details : ['Validation failed.']);
                } else if (response.status === 409) {
                    const data = await response.json().catch(() => null);
                    const message = typeof data?.message === 'string'
                        ? data.message
                        : 'Some stages no longer require backfill. Refresh the page and try again.';
                    renderErrors([message]);
                } else if (response.status === 400) {
                    renderErrors(['The security token is no longer valid. Refresh the page and try again.']);
                } else if (response.status === 404) {
                    renderErrors(['Project or stages were not found. Refresh the page and try again.']);
                } else if (response.status === 403) {
                    renderErrors(['You are not authorised to backfill this project.']);
                } else {
                    renderErrors(['Unexpected error saving backfill changes.']);
                }
            } catch (error) {
                console.error('Backfill request failed', error);
                renderErrors(['Network error while saving backfill changes.']);
            } finally {
                toggleSubmitState(false);
            }
        }

        if (submitButton) {
            submitButton.addEventListener('click', submitBackfill);
        }

        if (form) {
            form.addEventListener('submit', (event) => {
                event.preventDefault();
                submitBackfill();
            });
        }
    }

    const planReview = document.getElementById('offcanvasPlanReview');
    if (planReview) {
        planReview.addEventListener('shown.bs.offcanvas', function () {
            const firstAction = planReview.querySelector('button, input, select, textarea');
            if (firstAction) {
                firstAction.focus();
            }
        });

        planReview.addEventListener('hidden.bs.offcanvas', function () {
            planReview.querySelectorAll('[data-plan-review-note]').forEach(function (note) {
                note.setAttribute('hidden', '');
                const textarea = note.querySelector('textarea');
                if (textarea) {
                    textarea.value = '';
                }
            });
        });

        const reviewMarker = document.getElementById('open-plan-review');
        if (reviewMarker && reviewMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(planReview);
            instance.show();
        }

        planReview.querySelectorAll('[data-plan-review-form]').forEach(function (form) {
            const noteContainer = form.querySelector('[data-plan-review-note]');
            const rejectButton = form.querySelector('[data-plan-review-reject]');
            if (!noteContainer || !rejectButton) {
                return;
            }

            rejectButton.addEventListener('click', function (event) {
                if (rejectButton.disabled) {
                    return;
                }

                if (noteContainer.hasAttribute('hidden')) {
                    event.preventDefault();
                    noteContainer.removeAttribute('hidden');
                    const textarea = noteContainer.querySelector('textarea');
                    if (textarea) {
                        textarea.focus();
                    }
                }
            });
        });
    }

    function initPanelToggle(card, remarksPanel) {
        const switchGroup = card.querySelector('[data-panel-switch]');
        if (!switchGroup) {
            if (remarksPanel) {
                remarksPanel.ensureLoaded();
            }
            return;
        }

        const buttons = Array.from(switchGroup.querySelectorAll('[data-panel-target]'));
        const sections = Array.from(card.querySelectorAll('[data-panel-section]'));
        const bodies = Array.from(card.querySelectorAll('[data-panel]'));
        const projectId = card.getAttribute('data-panel-project-id') || '';
        const storageKey = projectId ? `pm:project:right-panel:${projectId}` : 'pm:project:right-panel';

        function getStored() {
            try {
                const stored = sessionStorage.getItem(storageKey);
                if (stored === 'remarks' || stored === 'timeline') {
                    return stored;
                }
            } catch (error) {
                // ignore storage errors
            }

            return 'timeline';
        }

        function getTimelineOverride() {
            if (typeof window === 'undefined') {
                return null;
            }

            const hash = typeof window.location.hash === 'string'
                ? window.location.hash.trim().toLowerCase()
                : '';

            if (hash === '#remarks' || hash === '#project-panel-toggle-remarks' || hash === '#project-panel-body-remarks') {
                return 'remarks';
            }

            if (hash === '#timeline' || hash === '#project-panel-toggle-timeline' || hash === '#project-panel-body-timeline' || hash.startsWith('#timeline-stage')) {
                return 'timeline';
            }

            const search = typeof window.location.search === 'string'
                ? window.location.search
                : '';

            if (!search) {
                return null;
            }

            try {
                const params = new URLSearchParams(search);
                if (params.has('timeline-stage')) {
                    return 'timeline';
                }

                const panel = params.get('panel');
                if (typeof panel === 'string' && panel.toLowerCase() === 'timeline') {
                    return 'timeline';
                }

                if (params.has('timeline')) {
                    const value = params.get('timeline');
                    if (!value) {
                        return 'timeline';
                    }

                    const normalized = value.toLowerCase();
                    if (normalized === '1' || normalized === 'true' || normalized === 'yes' || normalized === 'timeline') {
                        return 'timeline';
                    }
                }
            } catch (error) {
                // Ignore malformed query parameters
            }

            return null;
        }

        function setActive(name, syncUrl = false) {
            const target = name === 'remarks' ? 'remarks' : 'timeline';
            buttons.forEach((button) => {
                const value = button.getAttribute('data-panel-target');
                const isActive = value === target;
                button.classList.toggle('active', isActive);
                button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
                button.setAttribute('aria-expanded', isActive ? 'true' : 'false');
                const controls = button.getAttribute('aria-controls');
                if (controls) {
                    const controlled = document.getElementById(controls);
                    if (controlled) {
                        controlled.setAttribute('aria-hidden', isActive ? 'false' : 'true');
                    }
                }
            });

            sections.forEach((section) => {
                const value = section.getAttribute('data-panel-section');
                const isActive = value === target;
                section.classList.toggle('d-none', !isActive);
                section.setAttribute('aria-hidden', isActive ? 'false' : 'true');
            });

            bodies.forEach((body) => {
                const value = body.getAttribute('data-panel');
                const isActive = value === target;
                body.classList.toggle('d-none', !isActive);
                body.setAttribute('aria-hidden', isActive ? 'false' : 'true');
            });

            try {
                sessionStorage.setItem(storageKey, target);
            } catch (error) {
                // ignore storage failures
            }

            if (syncUrl && typeof window.history?.replaceState === 'function') {
                const desiredHash = target === 'remarks' ? '#remarks' : '#timeline';
                if (window.location.hash !== desiredHash) {
                    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}${desiredHash}`);
                }
            }

            if (target === 'remarks' && remarksPanel) {
                remarksPanel.ensureLoaded();
            }
        }

        buttons.forEach((button) => {
            button.addEventListener('click', () => {
                const target = button.getAttribute('data-panel-target');
                if (!target) {
                    return;
                }
                setActive(target, true);
            });
        });

        const override = getTimelineOverride();
        const initial = override || getStored();
        setActive(initial, false);

        window.addEventListener('hashchange', () => {
            const hashTarget = getTimelineOverride();
            if (hashTarget) {
                setActive(hashTarget, false);
            }
        });
    }

    function getTimelineStageTarget() {
        if (typeof window === 'undefined') {
            return null;
        }

        const search = typeof window.location.search === 'string'
            ? window.location.search
            : '';

        if (search) {
            try {
                const params = new URLSearchParams(search);
                const stage = params.get('timeline-stage');
                if (typeof stage === 'string' && stage.trim().length > 0) {
                    return stage.trim();
                }
            } catch (error) {
                // Ignore malformed query parameters
            }
        }

        const hash = typeof window.location.hash === 'string'
            ? window.location.hash.trim()
            : '';

        if (!hash) {
            return null;
        }

        const match = hash.match(/^#timeline-stage[-=]?(.+)$/i);
        if (!match || match.length < 2) {
            return null;
        }

        const raw = match[1];
        if (!raw) {
            return null;
        }

        try {
            const decoded = decodeURIComponent(raw);
            return decoded.trim() || null;
        } catch (error) {
            return raw.trim() || null;
        }
    }

    function highlightTimelineStage(stageCode, attempt = 0) {
        if (!stageCode || attempt > 10) {
            return;
        }

        const timeline = document.querySelector('[data-panel="timeline"]');
        if (!timeline) {
            window.setTimeout(() => highlightTimelineStage(stageCode, attempt + 1), 150);
            return;
        }

        const target = Array.from(timeline.querySelectorAll('[data-stage-row]')).find((element) => {
            const value = element.getAttribute('data-stage-row');
            return typeof value === 'string' && value.toLowerCase() === stageCode.toLowerCase();
        });

        if (!target) {
            window.setTimeout(() => highlightTimelineStage(stageCode, attempt + 1), 150);
            return;
        }

        target.classList.add('is-target');

        if (typeof target.scrollIntoView === 'function') {
            try {
                const prefersReducedMotion = typeof window.matchMedia === 'function'
                    && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
                target.scrollIntoView({
                    behavior: prefersReducedMotion ? 'auto' : 'smooth',
                    block: 'center',
                    inline: 'nearest'
                });
            } catch (error) {
                target.scrollIntoView();
            }
        }

        window.setTimeout(() => {
            target.classList.remove('is-target');
        }, 8000);
    }

    /* ---------- Stage progress meter ---------- */
    function initStageProgressBar() {
        const progressBar = document.querySelector('[data-stage-progress]');
        if (!progressBar) {
            return;
        }

        const rawProgress = progressBar.getAttribute('data-stage-progress');
        const parsedProgress = Number.parseInt(rawProgress, 10);
        const normalizedProgress = Number.isFinite(parsedProgress)
            ? Math.min(Math.max(parsedProgress, 0), 100)
            : 0;

        progressBar.style.setProperty('--pm-progress-width', `${normalizedProgress}%`);
    }

    initStageProgressBar();

    const remarksElement = document.querySelector('[data-remarks-panel]');
    let remarksPanelInstance = null;
    const createRemarksPanel = typeof remarksNamespace.createRemarksPanel === 'function'
        ? remarksNamespace.createRemarksPanel
        : null;
    if (remarksElement && createRemarksPanel) {
        remarksPanelInstance = createRemarksPanel(remarksElement, showToast);
    }

    const panelCard = document.querySelector('[data-panel-project-id]');
    if (panelCard) {
        initPanelToggle(panelCard, remarksPanelInstance);
    } else if (remarksPanelInstance) {
        remarksPanelInstance.ensureLoaded();
    }

    const timelineStageTarget = getTimelineStageTarget();
    if (timelineStageTarget) {
        window.setTimeout(() => highlightTimelineStage(timelineStageTarget), 200);
    }
})();
