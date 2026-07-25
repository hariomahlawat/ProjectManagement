(() => {
    'use strict';

    if (typeof bootstrap === 'undefined') return;

    const offcanvas = document.getElementById('offcanvasTot');
    const card = document.getElementById('project-tot-card');
    if (!offcanvas || !card) return;

    const projectId = Number.parseInt(offcanvas.dataset.projectId || '0', 10);
    const root = offcanvas.querySelector('[data-tot-root]');
    const loading = root?.querySelector('[data-tot-loading]');
    const content = root?.querySelector('[data-tot-content]');
    const summaryView = root?.querySelector('[data-tot-summary-view]');
    const editView = root?.querySelector('[data-tot-edit-view]');
    const form = root?.querySelector('[data-tot-form]');
    const editButton = root?.querySelector('[data-tot-edit-button]');
    const cancelButton = root?.querySelector('[data-tot-cancel-button]');
    const saveButton = root?.querySelector('[data-tot-save-button]');
    const saveLabel = root?.querySelector('[data-tot-save-label]');
    const saveSpinner = root?.querySelector('[data-tot-save-spinner]');
    const errorBox = root?.querySelector('[data-tot-errors]');
    const statusInput = form?.querySelector('[data-tot-status]');
    const startWrap = form?.querySelector('[data-tot-start-wrap]');
    const completionWrap = form?.querySelector('[data-tot-completion-wrap]');
    const fopmInput = form?.querySelector('[data-tot-fopm]');
    const fopmDateWrap = form?.querySelector('[data-tot-fopm-date-wrap]');

    let loaded = false;
    let editing = false;

    function showError(message) {
        if (!errorBox) return;
        errorBox.textContent = message || 'Unable to complete the request.';
        errorBox.classList.remove('d-none');
    }

    function clearError() {
        if (!errorBox) return;
        errorBox.textContent = '';
        errorBox.classList.add('d-none');
    }

    function setLoading(value) {
        loading?.classList.toggle('d-none', !value);
        content?.classList.toggle('d-none', value);
    }

    function setBusy(value) {
        if (saveButton instanceof HTMLButtonElement) saveButton.disabled = value;
        saveSpinner?.classList.toggle('d-none', !value);
        if (saveLabel) saveLabel.textContent = value ? 'Saving…' : 'Save details';
    }

    function setEditMode(value) {
        editing = value;
        summaryView?.classList.toggle('d-none', value);
        editView?.classList.toggle('d-none', !value);
        editButton?.classList.toggle('d-none', value);
        cancelButton?.classList.toggle('d-none', !value);
        saveButton?.classList.toggle('d-none', !value);
        if (value) statusInput?.focus();
    }

    function updateConditionalFields() {
        if (!(statusInput instanceof HTMLSelectElement)) return;
        const status = statusInput.value;
        const showStart = status === 'InProgress' || status === 'Completed';
        const showCompletion = status === 'Completed';
        startWrap?.classList.toggle('d-none', !showStart);
        completionWrap?.classList.toggle('d-none', !showCompletion);

        startWrap?.querySelectorAll('input').forEach((input) => { input.disabled = !showStart; });
        completionWrap?.querySelectorAll('input').forEach((input) => { input.disabled = !showCompletion; });

        const manufactured = fopmInput instanceof HTMLSelectElement && fopmInput.value === 'true';
        fopmDateWrap?.classList.toggle('d-none', !manufactured);
        fopmDateWrap?.querySelectorAll('input').forEach((input) => { input.disabled = !manufactured; });
    }

    function setValue(name, value) {
        const input = form?.querySelector(`[name="${name}"]`);
        if (input instanceof HTMLInputElement || input instanceof HTMLSelectElement || input instanceof HTMLTextAreaElement) {
            input.value = value == null ? '' : String(value);
        }
    }

    function populateForm(input) {
        if (!form || !input) return;
        setValue('Status', input.status || 'NotStarted');
        setValue('StartYear', input.startYear);
        setValue('StartMonth', input.startMonth);
        setValue('StartDay', input.startDay);
        setValue('CompletionYear', input.completionYear);
        setValue('CompletionMonth', input.completionMonth);
        setValue('CompletionDay', input.completionDay);
        setValue('MetDetails', input.metDetails);
        setValue('MetCompletedOn', input.metCompletedOn);
        setValue('FirstProductionModelManufactured', input.firstProductionModelManufactured == null ? '' : input.firstProductionModelManufactured);
        setValue('FirstProductionModelManufacturedOn', input.firstProductionModelManufacturedOn);
        updateConditionalFields();
    }

    function renderFacts(facts) {
        const host = root?.querySelector('[data-tot-facts]');
        if (!host) return;
        host.replaceChildren();
        (Array.isArray(facts) ? facts : []).forEach((fact) => {
            const row = document.createElement('div');
            row.className = 'project-tot-fact';
            const label = document.createElement('span');
            label.textContent = fact.label || '';
            const value = document.createElement('strong');
            value.textContent = fact.value || '—';
            row.append(label, value);
            host.appendChild(row);
        });
        host.classList.toggle('d-none', host.children.length === 0);
    }

    function renderSummary(payload) {
        const summary = payload?.summary;
        if (summary) {
            const statusLabel = root?.querySelector('[data-tot-status-label]');
            const summaryText = root?.querySelector('[data-tot-summary-text]');
            const statusBadge = root?.querySelector('[data-tot-status-badge]');
            if (statusLabel) statusLabel.textContent = summary.statusLabel || 'Not recorded';
            if (summaryText) summaryText.textContent = summary.summary || '';
            if (statusBadge) {
                statusBadge.textContent = summary.statusLabel || 'Not recorded';
                statusBadge.className = 'badge rounded-pill ' + badgeClass(payload.input?.status);
            }
            renderFacts(summary.facts);
        }

        const pendingBox = root?.querySelector('[data-tot-pending]');
        if (pendingBox) {
            if (payload.pendingApproval && payload.pending) {
                pendingBox.replaceChildren();
                const strong = document.createElement('strong');
                strong.className = 'd-block';
                strong.textContent = 'Pending approval';
                const text = document.createElement('span');
                text.textContent = `Proposed status: ${payload.pending.proposedStatusLabel}`;
                pendingBox.append(strong, text);
                pendingBox.classList.remove('d-none');
            } else {
                pendingBox.classList.add('d-none');
            }
        }
    }

    function badgeClass(status) {
        switch (status) {
            case 'Completed': return 'text-bg-success';
            case 'InProgress': return 'text-bg-info';
            case 'NotRequired': return 'text-bg-secondary';
            case 'NotStarted': return 'text-bg-warning';
            default: return 'text-bg-secondary';
        }
    }

    function updateCard(cardPayload) {
        if (!cardPayload) return;
        const title = card.querySelector('[data-tot-card-title]');
        const summary = card.querySelector('[data-tot-card-summary]');
        if (title) title.textContent = cardPayload.title || 'Not recorded';
        if (summary) summary.textContent = cardPayload.summary || 'Record ToT position';
    }

    async function loadDetails(force = false) {
        if (loaded && !force) return;
        setLoading(true);
        clearError();
        try {
            const response = await fetch(`${window.location.pathname}?handler=TotDetails&id=${encodeURIComponent(projectId)}`, {
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to load Transfer of Technology details.');
            populateForm(payload.input);
            renderSummary(payload);
            updateCard(payload.card);
            if (editButton instanceof HTMLButtonElement) {
                editButton.disabled = !payload.canManage || payload.pendingApproval;
                editButton.classList.toggle('d-none', !payload.canManage || payload.pendingApproval);
            }
            loaded = true;
        } catch (error) {
            showError(error instanceof Error ? error.message : 'Unable to load Transfer of Technology details.');
        } finally {
            setLoading(false);
        }
    }

    statusInput?.addEventListener('change', updateConditionalFields);
    fopmInput?.addEventListener('change', updateConditionalFields);
    editButton?.addEventListener('click', () => setEditMode(true));
    cancelButton?.addEventListener('click', () => {
        clearError();
        setEditMode(false);
        loadDetails(true);
    });

    saveButton?.addEventListener('click', async () => {
        if (!(form instanceof HTMLFormElement)) return;
        clearError();
        updateConditionalFields();
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
                headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to save Transfer of Technology details.');
            updateCard(payload.card);
            loaded = false;
            await loadDetails(true);
            setEditMode(false);
            if (window.ProjectRemarks?.showToast) {
                window.ProjectRemarks.showToast(payload.message || 'Transfer of Technology details updated.', 'success');
            }
        } catch (error) {
            showError(error instanceof Error ? error.message : 'Unable to save Transfer of Technology details.');
        } finally {
            setBusy(false);
        }
    });

    offcanvas.addEventListener('show.bs.offcanvas', () => {
        setEditMode(false);
        loadDetails();
    });

    offcanvas.addEventListener('hide.bs.offcanvas', (event) => {
        if (editing && form?.matches(':has(:focus)')) {
            const discard = window.confirm('Discard the unsaved Transfer of Technology changes?');
            if (!discard) event.preventDefault();
        }
    });
})();
