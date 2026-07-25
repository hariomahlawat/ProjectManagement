(() => {
    'use strict';

    const root = document.querySelector('[data-multi-jdp-root]');
    if (!root) return;

    const canManage = root.dataset.canManage === 'true';
    const projectId = Number(root.dataset.projectId || 0);
    const profileUrl = root.dataset.profileUrl;
    const optionsUrl = root.dataset.optionsUrl;
    const addUrl = root.dataset.addUrl;
    const removeUrl = root.dataset.removeUrl;
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const drawer = root.closest('.offcanvas');

    const partnerList = root.querySelector('[data-multi-jdp-partner-list]');
    const emptyState = root.querySelector('[data-multi-jdp-empty]');
    const countBadge = root.querySelector('[data-multi-jdp-count]');
    const showAddButton = root.querySelector('[data-multi-jdp-show-add]');
    const addForm = root.querySelector('[data-multi-jdp-add-form]');
    const cancelAddButton = root.querySelector('[data-multi-jdp-cancel-add]');
    const searchInput = root.querySelector('[data-multi-jdp-search]');
    const results = root.querySelector('[data-multi-jdp-results]');
    const selectedId = root.querySelector('[data-multi-jdp-selected-id]');
    const selectedWrap = root.querySelector('[data-multi-jdp-selection]');
    const selectedName = root.querySelector('[data-multi-jdp-selected-name]');
    const selectedMeta = root.querySelector('[data-multi-jdp-selected-meta]');
    const clearButton = root.querySelector('[data-multi-jdp-clear]');
    const saveButton = root.querySelector('[data-multi-jdp-save]');
    const spinner = root.querySelector('[data-multi-jdp-spinner]');
    const errorBox = root.querySelector('[data-multi-jdp-error]');

    let profile = null;
    let searchTimer = null;
    let busy = false;
    let dirty = false;
    let searchAbortController = null;

    const escapeHtml = value => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');

    const showToast = (message, tone = 'success') => {
        if (window.ProjectToast?.[tone]) {
            window.ProjectToast[tone](message);
            return;
        }

        if (tone === 'error') {
            window.alert(message);
        }
    };

    const request = async (url, options = {}) => {
        const method = options.method || 'GET';
        const response = await fetch(url, {
            ...options,
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                ...(method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });

        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(payload.error || 'Unable to update JDP details.');
        }

        return payload;
    };

    const statusClass = value => String(value || 'other').toLowerCase().replaceAll(' ', '-');

    const renderProject = project => `
        <a class="project-jdp-editor__project" href="/Projects/Overview/${project.projectId}">
            <span class="min-w-0">
                <strong>${escapeHtml(project.projectName)}</strong>
                ${project.caseFileNumber ? `<small>${escapeHtml(project.caseFileNumber)}</small>` : ''}
            </span>
            <span class="d-flex align-items-center gap-2 flex-shrink-0">
                <span class="badge rounded-pill project-jdp-editor__status project-jdp-editor__status--${statusClass(project.statusLabel)}">${escapeHtml(project.statusLabel)}</span>
                <i class="bi bi-chevron-right" aria-hidden="true"></i>
            </span>
        </a>`;

    const renderPartner = partner => {
        const otherProjects = partner.otherProjects || [];
        const related = otherProjects.length
            ? `<details class="project-jdp-editor__related-projects">
                    <summary>
                        <span>View ${otherProjects.length} linked ${otherProjects.length === 1 ? 'project' : 'projects'}</span>
                        <i class="bi bi-chevron-down" aria-hidden="true"></i>
                    </summary>
                    <div class="project-jdp-editor__project-list">${otherProjects.map(renderProject).join('')}</div>
               </details>`
            : '';

        return `
            <article class="project-jdp-editor__partner-card" data-partner-id="${partner.id}">
                <div class="project-jdp-editor__partner-card-main">
                    <span class="project-jdp-editor__icon" aria-hidden="true"><i class="bi bi-building"></i></span>
                    <div class="min-w-0 flex-grow-1">
                        <a class="project-jdp-editor__partner-name" href="/IndustryPartners?id=${partner.id}&projectId=${projectId}&tab=projects">
                            <span>${escapeHtml(partner.name)}</span>
                            <i class="bi bi-box-arrow-up-right" aria-hidden="true"></i>
                        </a>
                        ${partner.location ? `<span class="project-jdp-editor__location">${escapeHtml(partner.location)}</span>` : ''}
                        <span class="project-jdp-editor__usage">${escapeHtml(partner.usageSummary)}</span>
                    </div>
                    ${canManage ? `<button type="button" class="btn btn-link text-danger project-jdp-editor__remove-link" data-multi-jdp-remove="${partner.id}" data-partner-name="${escapeHtml(partner.name)}">Remove link</button>` : ''}
                </div>
                ${related}
            </article>`;
    };

    const updateHeader = data => {
        const title = document.querySelector('[data-jdp-card-title]');
        const summary = document.querySelector('[data-jdp-card-summary]');
        if (title) {
            title.textContent = data.cardTitle;
            title.title = data.cardTitle;
        }
        if (summary) {
            summary.textContent = data.cardSummary;
            summary.title = data.cardSummary;
        }
    };

    const render = data => {
        profile = data;
        const partners = data.partners || [];
        countBadge.textContent = `${partners.length} linked`;
        emptyState.classList.toggle('d-none', partners.length > 0);
        partnerList.innerHTML = partners.map(renderPartner).join('');
        updateHeader(data);
    };

    const loadProfile = async () => {
        try {
            const payload = await request(profileUrl);
            render(payload.profile);
        } catch (error) {
            showToast(error.message, 'error');
        }
    };

    const resetAdd = () => {
        dirty = false;
        if (selectedId) selectedId.value = '';
        if (searchInput) searchInput.value = '';
        results?.classList.add('d-none');
        if (results) results.innerHTML = '';
        selectedWrap?.classList.add('d-none');
        clearButton?.classList.add('d-none');
        if (saveButton) saveButton.disabled = true;
        errorBox?.classList.add('d-none');
        if (errorBox) errorBox.textContent = '';
    };

    const setBusy = value => {
        busy = value;
        spinner?.classList.toggle('d-none', !value);
        if (saveButton) saveButton.disabled = value || !selectedId?.value;
        root.querySelectorAll('[data-multi-jdp-remove]').forEach(button => {
            button.disabled = value;
        });
    };

    const openAdd = () => {
        resetAdd();
        addForm?.classList.remove('d-none');
        showAddButton?.classList.add('d-none');
        searchInput?.focus();
    };

    const closeAdd = ({ confirmDirty = true } = {}) => {
        if (confirmDirty && dirty && !window.confirm('Discard the selected JDP?')) return false;
        resetAdd();
        addForm?.classList.add('d-none');
        showAddButton?.classList.remove('d-none');
        return true;
    };

    showAddButton?.addEventListener('click', openAdd);
    cancelAddButton?.addEventListener('click', () => closeAdd());
    clearButton?.addEventListener('click', () => {
        resetAdd();
        searchInput?.focus();
    });

    searchInput?.addEventListener('input', () => {
        dirty = false;
        if (selectedId) selectedId.value = '';
        selectedWrap?.classList.add('d-none');
        clearButton?.classList.toggle('d-none', !searchInput.value);
        if (saveButton) saveButton.disabled = true;
        errorBox?.classList.add('d-none');
        clearTimeout(searchTimer);
        searchAbortController?.abort();

        searchTimer = setTimeout(async () => {
            const query = searchInput.value.trim();
            if (!query) {
                results?.classList.add('d-none');
                return;
            }

            searchAbortController = new AbortController();
            try {
                const separator = optionsUrl.includes('?') ? '&' : '?';
                const payload = await request(`${optionsUrl}${separator}q=${encodeURIComponent(query)}`, {
                    signal: searchAbortController.signal
                });
                const options = payload.items || [];
                if (!results) return;

                results.innerHTML = options.length
                    ? options.map(option => `
                        <button type="button" class="project-jdp-picker__option" role="option" data-option-id="${option.id}">
                            <span class="project-jdp-picker__option-identity">
                                <strong>${escapeHtml(option.name)}</strong>
                                ${option.location ? `<small>${escapeHtml(option.location)}</small>` : ''}
                            </span>
                            <small class="project-jdp-picker__option-usage">${escapeHtml(option.usageSummary || '')}</small>
                        </button>`).join('')
                    : '<div class="project-jdp-picker__empty">No unlinked organisation matches the search.</div>';
                results.classList.remove('d-none');
            } catch (error) {
                if (error.name === 'AbortError') return;
                if (errorBox) {
                    errorBox.textContent = error.message;
                    errorBox.classList.remove('d-none');
                }
            }
        }, 250);
    });

    results?.addEventListener('click', event => {
        const option = event.target.closest('[data-option-id]');
        if (!option) return;

        const name = option.querySelector('strong')?.textContent?.trim() || '';
        const meta = option.querySelector('.project-jdp-picker__option-usage')?.textContent?.trim() || '';
        selectedId.value = option.dataset.optionId;
        selectedName.textContent = name;
        selectedMeta.textContent = meta;
        searchInput.value = name;
        selectedWrap.classList.remove('d-none');
        clearButton.classList.remove('d-none');
        results.classList.add('d-none');
        dirty = true;
        saveButton.disabled = false;
    });

    addForm?.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !selectedId?.value) return;

        setBusy(true);
        try {
            const body = new URLSearchParams({ partnerId: selectedId.value });
            const payload = await request(addUrl, { method: 'POST', body });
            render(payload.profile);
            closeAdd({ confirmDirty: false });
            showToast(payload.message || 'JDP added to the project.');
        } catch (error) {
            if (errorBox) {
                errorBox.textContent = error.message;
                errorBox.classList.remove('d-none');
            }
        } finally {
            setBusy(false);
        }
    });

    partnerList?.addEventListener('click', async event => {
        const button = event.target.closest('[data-multi-jdp-remove]');
        if (!button || busy) return;

        const name = button.dataset.partnerName || 'this JDP';
        if (!window.confirm(`Remove ${name} as a JDP for this project?`)) return;

        setBusy(true);
        try {
            const body = new URLSearchParams({ partnerId: button.dataset.multiJdpRemove });
            const payload = await request(removeUrl, { method: 'POST', body });
            render(payload.profile);
            showToast(payload.message || 'JDP removed from the project.');
        } catch (error) {
            showToast(error.message, 'error');
        } finally {
            setBusy(false);
        }
    });

    drawer?.addEventListener('show.bs.offcanvas', loadProfile);
    drawer?.addEventListener('hide.bs.offcanvas', event => {
        if (dirty && !window.confirm('Discard the selected JDP?')) {
            event.preventDefault();
        }
    });

    document.querySelectorAll('[data-project-cover-image]').forEach(image => {
        image.addEventListener('error', () => {
            image.closest('picture')?.remove();
            image.closest('.project-photo-cover-frame')
                ?.querySelector('[data-project-cover-fallback]')
                ?.classList.remove('d-none');
        }, { once: true });
    });
})();
