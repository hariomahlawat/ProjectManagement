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
    const summaryFooter = root?.querySelector('[data-tot-summary-footer]');
    const editFooter = root?.querySelector('[data-tot-edit-footer]');
    const form = root?.querySelector('[data-tot-form]');
    const editButton = root?.querySelector('[data-tot-edit-button]');
    const cancelButton = root?.querySelector('[data-tot-cancel-button]');
    const saveButton = root?.querySelector('[data-tot-save-button]');
    const saveLabel = root?.querySelector('[data-tot-save-label]');
    const saveSpinner = root?.querySelector('[data-tot-save-spinner]');
    const errorBox = root?.querySelector('[data-tot-errors]');
    const globalError = root?.querySelector('[data-tot-global-error]');
    const successBox = root?.querySelector('[data-tot-success]');
    const statusInput = form?.querySelector('[data-tot-status]');
    const startWrap = form?.querySelector('[data-tot-start-wrap]');
    const completionWrap = form?.querySelector('[data-tot-completion-wrap]');
    const statusGuidance = form?.querySelector('[data-tot-status-guidance]');
    const startHelp = form?.querySelector('[data-tot-start-help]');
    const additional = form?.querySelector('[data-tot-additional]');
    const fopmInput = form?.querySelector('[data-tot-fopm]');
    const fopmDateWrap = form?.querySelector('[data-tot-fopm-date-wrap]');
    const remarkForm = root?.querySelector('[data-tot-remark-form]');
    const addRemarkButton = root?.querySelector('[data-tot-add-remark]');
    const cancelRemarkButton = root?.querySelector('[data-tot-remark-cancel]');
    const remarkBody = root?.querySelector('[data-tot-remark-body]');
    const remarkError = root?.querySelector('[data-tot-remark-error]');

    let loaded = false;
    let editing = false;

    function toggle(el, hidden) { el?.classList.toggle('d-none', hidden); }
    function message(el, value) {
        if (!el) return;
        el.textContent = value || '';
        toggle(el, !value);
    }
    function setLoading(value) { toggle(loading, !value); toggle(content, value); }
    function clearMessages() { message(errorBox, ''); message(globalError, ''); message(successBox, ''); }
    function setBusy(value) {
        if (saveButton instanceof HTMLButtonElement) saveButton.disabled = value;
        toggle(saveSpinner, !value);
        if (saveLabel) saveLabel.textContent = value ? 'Saving…' : 'Save details';
    }
    function setEditMode(value) {
        editing = value;
        toggle(summaryView, value);
        toggle(editView, !value);
        toggle(summaryFooter, value);
        toggle(editFooter, !value);
        if (value) statusInput?.focus();
    }
    function setFieldGroup(group, enabled) {
        toggle(group, !enabled);
        group?.querySelectorAll('input, select, textarea').forEach((control) => { control.disabled = !enabled; });
    }
    function updateConditionalFields() {
        if (!(statusInput instanceof HTMLSelectElement)) return;
        const status = statusInput.value;
        const showStart = status === 'InProgress' || status === 'Completed';
        const showCompletion = status === 'Completed';
        const showAdditional = status !== 'NotRequired';
        setFieldGroup(startWrap, showStart);
        setFieldGroup(completionWrap, showCompletion);
        setFieldGroup(additional, showAdditional);
        if (statusGuidance) statusGuidance.textContent = {
            InProgress: 'Start date is required. Completion date is not applicable until ToT is completed.',
            Completed: 'Completion date is required. Start date is optional.',
            NotStarted: 'Start and completion dates are not applicable.',
            NotRequired: 'ToT dates and milestones are not applicable.'
        }[status] || '';
        if (startHelp) startHelp.textContent = status === 'Completed'
            ? 'Optional. Enter the best information available.'
            : 'Required. Enter year, month and year, or exact date.';
        const manufactured = showAdditional && fopmInput instanceof HTMLSelectElement && fopmInput.value === 'true';
        setFieldGroup(fopmDateWrap, manufactured);
    }
    function setValue(name, value) {
        const input = form?.querySelector(`[name="${name}"]`);
        if (input instanceof HTMLInputElement || input instanceof HTMLSelectElement || input instanceof HTMLTextAreaElement) {
            input.value = value == null ? '' : String(value);
        }
    }
    function populateForm(input) {
        if (!input) return;
        setValue('Status', input.status || 'NotStarted');
        ['StartYear','StartMonth','StartDay','CompletionYear','CompletionMonth','CompletionDay','MetDetails','MetCompletedOn','FirstProductionModelManufacturedOn']
            .forEach((name) => setValue(name, input[name.charAt(0).toLowerCase() + name.slice(1)]));
        setValue('FirstProductionModelManufactured', input.firstProductionModelManufactured == null ? '' : input.firstProductionModelManufactured);
        updateConditionalFields();
    }
    function renderFacts(facts) {
        const host = root?.querySelector('[data-tot-facts]');
        if (!host) return;
        host.replaceChildren();
        (Array.isArray(facts) ? facts : []).forEach((fact) => {
            const row = document.createElement('div'); row.className = 'project-tot-fact';
            const label = document.createElement('span'); label.textContent = fact.label || '';
            const value = document.createElement('strong'); value.textContent = fact.value || '—';
            row.append(label, value); host.appendChild(row);
        });
        toggle(host, host.children.length === 0);
    }
    function badgeClass(status) {
        return { Completed:'text-bg-success', InProgress:'text-bg-info', NotRequired:'text-bg-secondary', NotStarted:'text-bg-warning' }[status] || 'text-bg-secondary';
    }
    function renderSummary(payload) {
        const summary = payload?.summary;
        if (summary) {
            const statusLabel = root?.querySelector('[data-tot-status-label]');
            const summaryText = root?.querySelector('[data-tot-summary-text]');
            const statusBadge = root?.querySelector('[data-tot-status-badge]');
            if (statusLabel) statusLabel.textContent = summary.statusLabel || 'Not recorded';
            if (summaryText) summaryText.textContent = summary.summary || '';
            if (statusBadge) { statusBadge.textContent = summary.statusLabel || 'Not recorded'; statusBadge.className = `badge rounded-pill ${badgeClass(payload.input?.status)}`; }
            renderFacts(summary.facts);
        }
        const pendingBox = root?.querySelector('[data-tot-pending]');
        if (pendingBox) {
            if (payload.pendingApproval && payload.pending) {
                pendingBox.innerHTML = `<strong class="d-block">Pending approval</strong><span>Proposed status: ${payload.pending.proposedStatusLabel}</span>`;
                toggle(pendingBox, false);
            } else toggle(pendingBox, true);
        }
        const latest = payload?.latestRemark;
        const latestBody = root?.querySelector('[data-tot-latest-remark]');
        const latestMeta = root?.querySelector('[data-tot-latest-remark-meta]');
        if (latestBody) latestBody.textContent = latest?.body || 'No ToT remark recorded.';
        if (latestMeta) latestMeta.textContent = latest?.meta || '';
    }
    function updateCard(payload) {
        if (!payload) return;
        const title = card.querySelector('[data-tot-card-title]');
        const summary = card.querySelector('[data-tot-card-summary]');
        if (title) { title.textContent = payload.title || 'Not recorded'; title.title = title.textContent; }
        if (summary) { summary.textContent = payload.summary || 'Record ToT position'; summary.title = summary.textContent; }
    }
    async function loadDetails(force = false) {
        if (loaded && !force) return;
        setLoading(true); clearMessages();
        try {
            const response = await fetch(`${window.location.pathname}?handler=TotDetails&id=${encodeURIComponent(projectId)}`, { credentials:'same-origin', headers:{ Accept:'application/json','X-Requested-With':'XMLHttpRequest' } });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to load Transfer of Technology details.');
            populateForm(payload.input); renderSummary(payload); updateCard(payload.card);
            if (editButton instanceof HTMLButtonElement) toggle(editButton, !payload.canManage || payload.pendingApproval);
            loaded = true;
        } catch (error) { message(globalError, error instanceof Error ? error.message : 'Unable to load Transfer of Technology details.'); }
        finally { setLoading(false); }
    }

    statusInput?.addEventListener('change', updateConditionalFields);
    fopmInput?.addEventListener('change', updateConditionalFields);
    editButton?.addEventListener('click', () => { clearMessages(); setEditMode(true); });
    cancelButton?.addEventListener('click', () => { clearMessages(); setEditMode(false); loadDetails(true); });
    saveButton?.addEventListener('click', async () => {
        if (!(form instanceof HTMLFormElement)) return;
        clearMessages(); updateConditionalFields();
        if (!form.checkValidity()) { form.reportValidity(); return; }
        setBusy(true);
        try {
            const response = await fetch(form.action, { method:'POST', body:new FormData(form), credentials:'same-origin', headers:{ Accept:'application/json','X-Requested-With':'XMLHttpRequest' } });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to save Transfer of Technology details.');
            updateCard(payload.card); loaded = false; await loadDetails(true); setEditMode(false); message(successBox, payload.message || 'Transfer of Technology details updated.');
        } catch (error) { message(errorBox, error instanceof Error ? error.message : 'Unable to save Transfer of Technology details.'); }
        finally { setBusy(false); }
    });

    addRemarkButton?.addEventListener('click', () => { toggle(remarkForm, false); remarkBody?.focus(); });
    cancelRemarkButton?.addEventListener('click', () => { toggle(remarkForm, true); if (remarkBody) remarkBody.value = ''; message(remarkError, ''); });
    remarkForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (!(remarkForm instanceof HTMLFormElement)) return;
        const body = remarkBody?.value?.trim() || '';
        if (body.length < 4) { message(remarkError, 'Remarks must be at least 4 characters long.'); return; }
        try {
            const response = await fetch(remarkForm.action, { method:'POST', body:new FormData(remarkForm), credentials:'same-origin', headers:{ Accept:'application/json','X-Requested-With':'XMLHttpRequest' } });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to save the remark.');
            if (remarkBody) remarkBody.value = ''; toggle(remarkForm, true); message(successBox, payload.message || 'ToT remark added.'); loaded = false; await loadDetails(true);
        } catch (error) { message(remarkError, error instanceof Error ? error.message : 'Unable to save the remark.'); }
    });

    offcanvas.addEventListener('show.bs.offcanvas', () => { setEditMode(false); loadDetails(); });
    offcanvas.addEventListener('hide.bs.offcanvas', (event) => {
        if (editing && !window.confirm('Discard the unsaved Transfer of Technology changes?')) event.preventDefault();
    });
})();
