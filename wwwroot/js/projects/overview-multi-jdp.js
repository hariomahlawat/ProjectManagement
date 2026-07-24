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

    const partnerList = root.querySelector('[data-multi-jdp-partner-list]');
    const emptyState = root.querySelector('[data-multi-jdp-empty]');
    const countBadge = root.querySelector('[data-multi-jdp-count]');
    const showAdd = root.querySelector('[data-multi-jdp-show-add]');
    const addForm = root.querySelector('[data-multi-jdp-add-form]');
    const cancelAdd = root.querySelector('[data-multi-jdp-cancel-add]');
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
    const drawer = root.closest('.offcanvas');

    let profile = null;
    let searchTimer = null;
    let busy = false;
    let dirty = false;

    const escapeHtml = value => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');

    const request = async (url, options = {}) => {
        const response = await fetch(url, {
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                ...(options.method && options.method !== 'GET' ? { 'RequestVerificationToken': token } : {})
            },
            ...options
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(payload.error || 'Unable to update JDP details.');
        return payload;
    };

    const usageSummary = partner => {
        if (!partner.otherProjectCount) return 'Not linked to any other project';
        const parts = [];
        if (partner.otherOngoingProjectCount) parts.push(`${partner.otherOngoingProjectCount} ongoing`);
        if (partner.otherCompletedProjectCount) parts.push(`${partner.otherCompletedProjectCount} completed`);
        const other = Math.max(0, partner.otherProjectCount - partner.otherOngoingProjectCount - partner.otherCompletedProjectCount);
        if (other) parts.push(`${other} other`);
        return `Also linked to ${partner.otherProjectCount} other ${partner.otherProjectCount === 1 ? 'project' : 'projects'}${parts.length ? ` · ${parts.join(' · ')}` : ''}`;
    };

    const render = data => {
        profile = data;
        const partners = data.partners || [];
        countBadge.textContent = String(partners.length);
        emptyState.classList.toggle('d-none', partners.length > 0);
        partnerList.innerHTML = partners.map(partner => `
            <article class="project-jdp-editor__partner-card" data-partner-id="${partner.id}">
                <div class="project-jdp-editor__partner-card-main">
                    <span class="project-jdp-editor__icon" aria-hidden="true"><i class="bi bi-building"></i></span>
                    <div class="min-w-0 flex-grow-1">
                        <a class="project-jdp-editor__partner-name" href="/IndustryPartners?id=${partner.id}&projectId=${projectId}&tab=projects">
                            <span>${escapeHtml(partner.name)}</span><i class="bi bi-box-arrow-up-right" aria-hidden="true"></i>
                        </a>
                        ${partner.location ? `<span class="project-jdp-editor__location">${escapeHtml(partner.location)}</span>` : ''}
                        <span class="project-jdp-editor__usage">${escapeHtml(usageSummary(partner))}</span>
                    </div>
                    ${canManage ? `<button type="button" class="btn btn-sm btn-outline-danger project-jdp-editor__remove-partner" data-multi-jdp-remove="${partner.id}" data-partner-name="${escapeHtml(partner.name)}">Remove</button>` : ''}
                </div>
                ${partner.otherProjects?.length ? `<div class="project-jdp-editor__project-list">${partner.otherProjects.map(project => `
                    <a class="project-jdp-editor__project" href="/Projects/Overview/${project.projectId}">
                        <span class="min-w-0"><strong>${escapeHtml(project.projectName)}</strong>${project.caseFileNumber ? `<small>${escapeHtml(project.caseFileNumber)}</small>` : ''}</span>
                        <span class="badge rounded-pill project-jdp-editor__status project-jdp-editor__status--${project.statusLabel.toLowerCase()}">${escapeHtml(project.statusLabel)}</span>
                    </a>`).join('')}</div>` : ''}
            </article>`).join('');

        document.querySelector('[data-jdp-card-title]')?.replaceChildren(document.createTextNode(data.cardTitle));
        document.querySelector('[data-jdp-card-summary]')?.replaceChildren(document.createTextNode(data.cardSummary));
        document.querySelectorAll('[data-jdp-lower-panel]').forEach(panel => panel.dataset.refreshRequired = 'true');
    };

    const loadProfile = async () => {
        const payload = await request(profileUrl);
        render(payload.profile);
    };

    const resetAdd = () => {
        dirty = false;
        selectedId.value = '';
        searchInput.value = '';
        results.classList.add('d-none');
        results.innerHTML = '';
        selectedWrap.classList.add('d-none');
        clearButton.classList.add('d-none');
        saveButton.disabled = true;
        errorBox.classList.add('d-none');
        errorBox.textContent = '';
    };

    const setBusy = value => {
        busy = value;
        spinner?.classList.toggle('d-none', !value);
        saveButton.disabled = value || !selectedId.value;
    };

    const openAdd = () => {
        addForm.classList.remove('d-none');
        showAdd.classList.add('d-none');
        searchInput.focus();
    };

    const closeAdd = () => {
        if (dirty && !confirm('Discard the selected JDP?')) return;
        resetAdd();
        addForm.classList.add('d-none');
        showAdd.classList.remove('d-none');
    };

    showAdd?.addEventListener('click', openAdd);
    cancelAdd?.addEventListener('click', closeAdd);
    clearButton?.addEventListener('click', resetAdd);

    searchInput?.addEventListener('input', () => {
        dirty = false;
        selectedId.value = '';
        selectedWrap.classList.add('d-none');
        saveButton.disabled = true;
        clearTimeout(searchTimer);
        searchTimer = setTimeout(async () => {
            const query = searchInput.value.trim();
            if (!query) {
                results.classList.add('d-none');
                return;
            }
            try {
                const payload = await request(`${optionsUrl}&query=${encodeURIComponent(query)}`);
                const options = payload.options || payload.items || payload;
                const linkedIds = new Set((profile?.partners || []).map(item => item.id));
                results.innerHTML = options.map(option => `
                    <button type="button" class="project-jdp-picker__option" role="option" data-option-id="${option.id}" ${linkedIds.has(option.id) ? 'disabled' : ''}>
                        <span><strong>${escapeHtml(option.name)}</strong>${option.location ? `<small>${escapeHtml(option.location)}</small>` : ''}</span>
                        <small>${linkedIds.has(option.id) ? 'Already linked to this project' : escapeHtml(option.usageSummary || '')}</small>
                    </button>`).join('');
                results.classList.toggle('d-none', options.length === 0);
            } catch (error) {
                errorBox.textContent = error.message;
                errorBox.classList.remove('d-none');
            }
        }, 250);
    });

    results?.addEventListener('click', event => {
        const option = event.target.closest('[data-option-id]');
        if (!option || option.disabled) return;
        const name = option.querySelector('strong')?.textContent?.trim() || '';
        const meta = option.querySelector('small:last-child')?.textContent?.trim() || '';
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
        if (busy || !selectedId.value) return;
        setBusy(true);
        try {
            const body = new URLSearchParams({ partnerId: selectedId.value });
            const payload = await request(addUrl, { method: 'POST', body });
            render(payload.profile);
            resetAdd();
            addForm.classList.add('d-none');
            showAdd.classList.remove('d-none');
            window.ProjectToast?.success?.(payload.message || 'JDP added.');
        } catch (error) {
            errorBox.textContent = error.message;
            errorBox.classList.remove('d-none');
        } finally {
            setBusy(false);
        }
    });

    partnerList?.addEventListener('click', async event => {
        const button = event.target.closest('[data-multi-jdp-remove]');
        if (!button || busy) return;
        const name = button.dataset.partnerName || 'this JDP';
        if (!confirm(`Remove ${name} as a JDP for this project?`)) return;
        busy = true;
        try {
            const body = new URLSearchParams({ partnerId: button.dataset.multiJdpRemove });
            const payload = await request(removeUrl, { method: 'POST', body });
            render(payload.profile);
            window.ProjectToast?.success?.(payload.message || 'JDP removed.');
        } catch (error) {
            alert(error.message);
        } finally {
            busy = false;
        }
    });

    drawer?.addEventListener('show.bs.offcanvas', loadProfile);
    drawer?.addEventListener('hide.bs.offcanvas', event => {
        if (dirty && !confirm('Discard the selected JDP?')) event.preventDefault();
    });
})();
