(() => {
    'use strict';

    // Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
    const askConfirm = options => (window.PrismConfirm?.show
        ? window.PrismConfirm.show(options)
        : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));


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
        // The shared glass toast (docs/ux/toasts.md); the code below is the fallback.
        if (window.PrismToast && message) {
            window.PrismToast.show({ tone: ({ success: 'success', danger: 'danger', error: 'danger', warning: 'warning' }[String(tone || '').toLowerCase()] || 'info'), text: message });
            return;
        }
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

        const organisationUrl = partner.organisationUrl || `/IndustryPartners/${partner.id}?projectId=${projectId}`;
        const contact = partner.primaryContact;
        const copyRow = (icon, href, value, label) => `
                <div class="project-jdp-editor__contact-row">
                    <i class="bi ${icon}" aria-hidden="true"></i>
                    <a href="${href}${escapeHtml(value)}" class="project-jdp-editor__contact-value" title="${escapeHtml(value)}">${escapeHtml(value)}</a>
                    <button type="button" class="project-jdp-editor__copy" data-copy-value="${escapeHtml(value)}" aria-label="${escapeHtml(label)} for ${escapeHtml(partner.name)}" title="${escapeHtml(label)}">
                        <i class="bi bi-copy" aria-hidden="true"></i>
                    </button>
                </div>`;
        const contactBlock = contact && (contact.phone || contact.email)
            ? `<div class="project-jdp-editor__contact">
                    <div class="project-jdp-editor__contact-name">
                        <span>${escapeHtml(contact.name)}</span>
                        <span class="project-jdp-editor__primary"><i class="bi bi-star-fill" aria-hidden="true"></i> Primary</span>
                    </div>
                    ${contact.phone ? copyRow('bi-telephone', 'tel:', contact.phone, 'Copy phone number') : ''}
                    ${contact.email ? copyRow('bi-envelope', 'mailto:', contact.email, 'Copy email address') : ''}
               </div>`
            : `<div class="project-jdp-editor__contact project-jdp-editor__contact--empty">
                    No contact recorded yet. <a href="${escapeHtml(organisationUrl)}">Add one in the Industry Directory</a>
               </div>`;

        return `
            <article class="project-jdp-editor__partner-card" data-partner-id="${partner.id}">
                <div class="project-jdp-editor__partner-card-main">
                    <span class="project-jdp-editor__icon" aria-hidden="true"><i class="bi bi-building"></i></span>
                    <div class="min-w-0 flex-grow-1">
                        <a class="project-jdp-editor__partner-name" href="${escapeHtml(organisationUrl)}">
                            <span>${escapeHtml(partner.name)}</span>
                        </a>
                        ${partner.location ? `<span class="project-jdp-editor__location">${escapeHtml(partner.location)}</span>` : ''}
                    </div>
                    <div class="dropdown">
                        <button type="button" class="btn btn-sm btn-light project-jdp-editor__menu" data-bs-toggle="dropdown" aria-expanded="false" aria-label="Actions for ${escapeHtml(partner.name)}">
                            <i class="bi bi-three-dots" aria-hidden="true"></i>
                        </button>
                        <ul class="dropdown-menu dropdown-menu-end">
                            <li><a class="dropdown-item" href="${escapeHtml(organisationUrl)}"><i class="bi bi-box-arrow-up-right" aria-hidden="true"></i> Open organisation</a></li>
                            ${canManage ? `<li><hr class="dropdown-divider" /></li>
                            <li><button type="button" class="dropdown-item text-danger" data-multi-jdp-remove="${partner.id}" data-partner-name="${escapeHtml(partner.name)}"><i class="bi bi-link-45deg" aria-hidden="true"></i> Remove JDP link…</button></li>` : ''}
                        </ul>
                    </div>
                </div>
                ${contactBlock}
                <span class="project-jdp-editor__usage">${escapeHtml(partner.usageSummary)}</span>
                ${related}
            </article>`;
    };

    // SECTION: Copy a JDP's phone number or email address (clipboard on HTTPS,
    // the older copy command on a plain-HTTP LAN address).
    const copyText = async value => {
        if (window.isSecureContext && navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(value);
            return;
        }

        const field = document.createElement('textarea');
        field.value = value;
        field.setAttribute('readonly', '');
        field.style.position = 'fixed';
        field.style.top = '-1000px';
        document.body.appendChild(field);
        field.select();
        const copied = document.execCommand('copy');
        field.remove();
        if (!copied) {
            throw new Error('Copy was blocked by the browser.');
        }
    };

    root.addEventListener('click', async event => {
        const button = event.target.closest('[data-copy-value]');
        if (!button || !root.contains(button)) return;

        event.preventDefault();
        const value = button.getAttribute('data-copy-value') || '';
        const icon = button.querySelector('i');
        try {
            await copyText(value);
            button.classList.add('is-copied');
            button.setAttribute('data-copy-label', 'Copied');
            icon?.classList.replace('bi-copy', 'bi-check2');
            showToast(`Copied ${value}`, 'success');
        } catch {
            showToast('Copy failed. Select the text and copy it instead.', 'error');
        }

        window.clearTimeout(button._copyTimer);
        button._copyTimer = window.setTimeout(() => {
            button.classList.remove('is-copied');
            button.removeAttribute('data-copy-label');
            icon?.classList.replace('bi-check2', 'bi-copy');
        }, 1600);
    });

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

    const discardJdp = () => askConfirm({
        tone: 'warning',
        title: 'Discard the selected JDP?',
        message: 'It has not been added to the project.',
        confirmText: 'Discard',
        cancelText: 'Keep it'
    });

    const closeAdd = async ({ confirmDirty = true } = {}) => {
        if (confirmDirty && dirty && !(await discardJdp())) return false;
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

        const name = button.dataset.partnerName || 'This JDP';
        const accepted = await askConfirm({
            tone: 'danger',
            title: 'Remove this JDP from the project?',
            message: 'It stops being listed as a JDP for this project.',
            subject: { icon: 'bi-building', name },
            confirmText: 'Remove JDP',
            cancelText: 'Keep JDP'
        });
        if (!accepted || busy) return;

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
    // Bootstrap needs the answer now, so the close is held, the question asked, and the
    // drawer closed again only if the user agrees.
    let closeApproved = false;
    drawer?.addEventListener('hide.bs.offcanvas', event => {
        if (!dirty || closeApproved) {
            closeApproved = false;
            return;
        }
        event.preventDefault();
        discardJdp().then(accepted => {
            if (!accepted) return;
            closeApproved = true;
            window.bootstrap?.Offcanvas.getOrCreateInstance(drawer).hide();
        });
    });

})();
