(() => {
    'use strict';

    // Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
    const askConfirm = options => (window.PrismConfirm?.show
        ? window.PrismConfirm.show(options)
        : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));


    if (typeof bootstrap === 'undefined') return;

    // The shared ToT drawer (Pages/Projects/_ProjectTotDrawer.cshtml). It talks to the hosting
    // page's handlers at data-tot-endpoint; the project page also has a ToT card to keep current.
    const offcanvas = document.getElementById('offcanvasTot');
    if (!offcanvas) return;
    const card = document.getElementById('project-tot-card');

    // The register shows one project at a time and switches between them; the project page has one.
    let projectId = Number.parseInt(offcanvas.dataset.projectId || '0', 10);
    const projectNameLabel = offcanvas.querySelector('[data-tot-project-name]');
    const endpoint = offcanvas.dataset.totEndpoint || window.location.pathname;
    function handlerUrl(handler) {
        return `${endpoint}?handler=${encodeURIComponent(handler)}&id=${encodeURIComponent(projectId)}`;
    }
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
    const pendingBox = root?.querySelector('[data-tot-pending]');
    const pendingTitle = root?.querySelector('[data-tot-pending-title]');
    const pendingMeta = root?.querySelector('[data-tot-pending-meta]');
    const changesBody = root?.querySelector('[data-tot-changes]');
    const decideForm = root?.querySelector('[data-tot-decide-form]');
    const decideVersion = root?.querySelector('[data-tot-decide-version]');
    const decideReason = root?.querySelector('[data-tot-decide-reason]');
    const decideError = root?.querySelector('[data-tot-decide-error]');
    const decideButtons = root ? [...root.querySelectorAll('[data-tot-decide]')] : [];
    const historyWrap = root?.querySelector('[data-tot-history-wrap]');
    const historyList = root?.querySelector('[data-tot-history]');
    const openProjectLink = root?.querySelector('[data-tot-open-project]');

    let loaded = false;
    // A save, decision or remark made in the register leaves its rows out of date until reload.
    let changed = false;
    let editing = false;
    let remarking = false;
    let initialFormFingerprint = '';
    const cardTones = ['positive', 'info', 'neutral', 'warning'];
    const cardToneClasses = cardTones.map((tone) => `project-intelligence-card--tone-${tone}`);

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
        if (saveLabel) saveLabel.textContent = value ? 'Saving…' : (saveLabel.dataset.idleLabel || 'Save details');
    }
    function setEditMode(value) {
        editing = value;
        if (value) setRemarkMode(false);
        toggle(summaryView, value);
        toggle(editView, !value);
        toggle(summaryFooter, value);
        toggle(editFooter, !value);
        if (value) statusInput?.focus();
    }
    function setRemarkMode(value) {
        remarking = value;
        toggle(remarkForm, !value);
        if (!editing) toggle(summaryFooter, value);
        if (!value) {
            if (remarkBody) remarkBody.value = '';
            message(remarkError, '');
        } else {
            remarkBody?.focus();
        }
    }
    function setFieldGroup(group, enabled) {
        toggle(group, !enabled);
        group?.querySelectorAll('input, select, textarea').forEach((control) => { control.disabled = !enabled; });
    }
    function formFingerprint() {
        if (!(form instanceof HTMLFormElement)) return '';
        return Array.from(new FormData(form).entries())
            .map(([name, value]) => `${name}\u0000${typeof value === 'string' ? value : value.name}`)
            .join('\u0001');
    }
    function hasUnsavedDetails() {
        return editing && formFingerprint() !== initialFormFingerprint;
    }
    function updateConditionalFields() {
        if (!(statusInput instanceof HTMLSelectElement)) return;
        const status = statusInput.value;
        const showStart = status === 'InProgress' || status === 'Completed';
        const showCompletion = status === 'Completed';
        const showAdditional = status === 'InProgress' || status === 'Completed';
        setFieldGroup(startWrap, showStart);
        setFieldGroup(completionWrap, showCompletion);
        setFieldGroup(additional, showAdditional);
        if (statusGuidance) statusGuidance.textContent = {
            InProgress: 'Start date is required. Completion date is not applicable until ToT is completed.',
            Completed: 'Completion date is required. Start date is optional.',
            NotStarted: 'Start and completion dates are not applicable.',
            NotRequired: 'ToT dates and milestones are not applicable.',
            NotKnown: 'Use when nobody knows whether ToT took place. Dates and milestones are not applicable.'
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
        initialFormFingerprint = formFingerprint();
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
        renderPending(payload?.pendingApproval ? payload.pending : null);
        renderHistory(payload?.history);
        const latest = payload?.latestRemark;
        const latestBody = root?.querySelector('[data-tot-latest-remark]');
        const latestMeta = root?.querySelector('[data-tot-latest-remark-meta]');
        if (latestBody) latestBody.textContent = latest?.body || 'No ToT remark recorded.';
        if (latestMeta) latestMeta.textContent = latest?.meta || '';
    }
    // A request waiting for the HoD: only the fields it changes, now against proposed. Approvers
    // also get the decision form; everyone else sees who sent it and when.
    function renderPending(pending) {
        if (!pendingBox) return;
        if (!pending) {
            toggle(pendingBox, true);
            toggle(decideForm, true);
            return;
        }
        const canDecide = !!pending.canDecide && decideForm instanceof HTMLFormElement;
        if (pendingTitle) pendingTitle.textContent = canDecide ? 'Waiting for your decision' : 'Waiting for HoD approval';
        if (pendingMeta) pendingMeta.textContent = [pending.submittedBy, pending.submittedOn].filter(Boolean).join(' · ');
        if (changesBody) {
            const changes = Array.isArray(pending.changes) ? pending.changes : [];
            const rows = changes.map((change) => {
                const row = document.createElement('tr');
                const field = document.createElement('th'); field.scope = 'row'; field.textContent = change.field || '';
                const now = document.createElement('td'); now.textContent = change.now || '—';
                const proposed = document.createElement('td');
                const strong = document.createElement('strong'); strong.textContent = change.proposed || '—';
                proposed.appendChild(strong);
                row.append(field, now, proposed);
                return row;
            });
            if (rows.length === 0) {
                const row = document.createElement('tr');
                const cell = document.createElement('td'); cell.colSpan = 3; cell.textContent = 'No field changes.';
                row.appendChild(cell); rows.push(row);
            }
            changesBody.replaceChildren(...rows);
        }
        if (decideForm) {
            toggle(decideForm, !canDecide);
            if (decideVersion) decideVersion.value = pending.rowVersion || '';
            message(decideError, '');
        }
        toggle(pendingBox, false);
    }
    function renderHistory(entries) {
        if (!historyList) return;
        const items = (Array.isArray(entries) ? entries : []).map((entry) => {
            const item = document.createElement('li'); item.className = 'project-tot-history__item';
            const head = document.createElement('div'); head.className = 'project-tot-history__head';
            const label = document.createElement('strong'); label.textContent = entry.label || '';
            const meta = document.createElement('span'); meta.textContent = [entry.by, entry.on].filter(Boolean).join(' · ');
            head.append(label, meta); item.appendChild(head);
            if (entry.values) { const values = document.createElement('div'); values.textContent = entry.values; item.appendChild(values); }
            if (entry.reason) { const reason = document.createElement('div'); reason.className = 'project-tot-history__reason'; reason.textContent = `Reason: ${entry.reason}`; item.appendChild(reason); }
            return item;
        });
        historyList.replaceChildren(...items);
        toggle(historyWrap, items.length === 0);
    }
    function setDecideBusy(value) {
        decideButtons.forEach((button) => { if (button instanceof HTMLButtonElement) button.disabled = value; });
    }
    // Point every form and link at the project whose details are shown.
    function pointAtProject(payload) {
        if (form instanceof HTMLFormElement) form.action = handlerUrl('Tot');
        if (remarkForm instanceof HTMLFormElement) remarkForm.action = handlerUrl('TotRemark');
        if (decideForm instanceof HTMLFormElement) decideForm.action = handlerUrl('TotDecide');
        root?.querySelectorAll('[data-tot-project-field]').forEach((field) => { field.value = String(projectId); });
        if (openProjectLink instanceof HTMLAnchorElement) openProjectLink.href = `/Projects/Overview/${encodeURIComponent(projectId)}`;
        toggle(addRemarkButton, !payload?.canManage);
        if (projectNameLabel && payload?.projectName) projectNameLabel.textContent = payload.projectName;
    }
    // Forget the project shown so far: its text, history, pending request and every action. Used
    // when switching projects and when a load fails, so nothing can be posted against a project
    // other than the one whose details are on screen.
    function clearShownProject() {
        loaded = false;
        toggle(editButton, true);
        toggle(addRemarkButton, true);
        if (remarking) setRemarkMode(false);
        renderPending(null);
        renderHistory([]);
        renderFacts([]);
        ['[data-tot-status-label]', '[data-tot-summary-text]', '[data-tot-status-badge]', '[data-tot-latest-remark]', '[data-tot-latest-remark-meta]']
            .forEach((selector) => { const el = root?.querySelector(selector); if (el) el.textContent = ''; });
        root?.querySelectorAll('[data-tot-project-field]').forEach((field) => { field.value = ''; });
        if (decideVersion) decideVersion.value = '';
        // Typed text belongs to the project it was typed for.
        if (decideReason) decideReason.value = '';
        message(decideError, '');
        if (remarkBody) remarkBody.value = '';
    }
    function updateCard(payload) {
        if (!payload || !card) return;
        const title = card.querySelector('[data-tot-card-title]');
        const summary = card.querySelector('[data-tot-card-summary]');
        const tone = cardTones.includes(payload.tone)
            ? payload.tone
            : 'neutral';
        if (title) { title.textContent = payload.title || 'Not recorded'; title.title = title.textContent; }
        if (summary) { summary.textContent = payload.summary || 'Record ToT position'; summary.title = summary.textContent; }
        card.classList.remove(...cardToneClasses);
        card.classList.add(`project-intelligence-card--tone-${tone}`);
        card.dataset.totCardTone = tone;
    }
    // Each load is numbered: only the latest load for the project now shown may fill the drawer.
    // Otherwise a slow answer for a project opened earlier could show its values while the
    // forms point at the project opened since, and a save would write them to the wrong project.
    let loadSequence = 0;
    async function loadDetails(force = false) {
        if (loaded && !force) return;
        const requestedProjectId = projectId;
        const sequence = ++loadSequence;
        const isCurrent = () => sequence === loadSequence && requestedProjectId === projectId;
        setLoading(true); clearMessages();
        try {
            const response = await fetch(handlerUrl('TotDetails'), { credentials:'same-origin', headers:{ Accept:'application/json','X-Requested-With':'XMLHttpRequest' } });
            const payload = await response.json().catch(() => null);
            if (!isCurrent()) return;
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to load Transfer of Technology details.');
            if (payload.projectId != null && Number(payload.projectId) !== requestedProjectId) throw new Error('Unable to load Transfer of Technology details.');
            populateForm(payload.input); renderSummary(payload); updateCard(payload.card); pointAtProject(payload);
            if (editButton instanceof HTMLButtonElement) toggle(editButton, !payload.canManage || payload.pendingApproval);
            loaded = true;
        } catch (error) {
            if (!isCurrent()) return;
            // The form was not filled from the server, so editing it could overwrite the
            // stored details with defaults. Nothing may be posted until a later load succeeds.
            clearShownProject();
            message(globalError, error instanceof Error ? error.message : 'Unable to load Transfer of Technology details.');
        }
        finally { if (isCurrent()) setLoading(false); }
    }

    statusInput?.addEventListener('change', updateConditionalFields);
    fopmInput?.addEventListener('change', updateConditionalFields);
    editButton?.addEventListener('click', () => { clearMessages(); setEditMode(true); });
    cancelButton?.addEventListener('click', () => { clearMessages(); setEditMode(false); loadDetails(true); });
    saveButton?.addEventListener('click', async () => {
        if (!(form instanceof HTMLFormElement) || !loaded) return;
        clearMessages(); updateConditionalFields();
        if (!form.checkValidity()) { form.reportValidity(); return; }
        setBusy(true);
        try {
            const response = await fetch(form.action, { method:'POST', body:new FormData(form), credentials:'same-origin', headers:{ Accept:'application/json','X-Requested-With':'XMLHttpRequest' } });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to save Transfer of Technology details.');
            changed = true;
            updateCard(payload.card); loaded = false; await loadDetails(true); setEditMode(false); message(successBox, payload.message || 'Transfer of Technology details updated.');
        } catch (error) { message(errorBox, error instanceof Error ? error.message : 'Unable to save Transfer of Technology details.'); }
        finally { setBusy(false); }
    });

    decideButtons.forEach((button) => button.addEventListener('click', async () => {
        if (!(decideForm instanceof HTMLFormElement) || !loaded) return;
        const approve = button.getAttribute('data-tot-decide') === 'true';
        const reason = decideReason?.value?.trim() || '';
        if (!approve && !reason) {
            message(decideError, 'Give a reason to reject the update.');
            decideReason?.focus();
            return;
        }
        message(decideError, '');
        setDecideBusy(true);
        try {
            const body = new FormData(decideForm);
            body.set('approve', approve ? 'true' : 'false');
            const response = await fetch(decideForm.action, { method:'POST', body, credentials:'same-origin', headers:{ Accept:'application/json','X-Requested-With':'XMLHttpRequest' } });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to record the decision.');
            if (decideReason) decideReason.value = '';
            changed = true;
            updateCard(payload.card);
            loaded = false;
            await loadDetails(true);
            message(successBox, payload.message || 'Decision recorded.');
        } catch (error) {
            message(decideError, error instanceof Error ? error.message : 'Unable to record the decision.');
        } finally {
            setDecideBusy(false);
        }
    }));

    addRemarkButton?.addEventListener('click', () => setRemarkMode(true));
    cancelRemarkButton?.addEventListener('click', () => setRemarkMode(false));
    remarkForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (!(remarkForm instanceof HTMLFormElement) || !loaded) return;
        const body = remarkBody?.value?.trim() || '';
        if (body.length < 4) { message(remarkError, 'Remarks must be at least 4 characters long.'); return; }
        try {
            const response = await fetch(remarkForm.action, { method:'POST', body:new FormData(remarkForm), credentials:'same-origin', headers:{ Accept:'application/json','X-Requested-With':'XMLHttpRequest' } });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.error || 'Unable to save the remark.');
            setRemarkMode(false);
            changed = true;
            loaded = false;
            await loadDetails(true);
            message(successBox, payload.message || 'ToT remark added.');
        } catch (error) { message(remarkError, error instanceof Error ? error.message : 'Unable to save the remark.'); }
    });

    let lastLoad = Promise.resolve();
    // The register (no project card) shows its rows again with the change once the drawer closes.
    offcanvas.addEventListener('hidden.bs.offcanvas', () => {
        if (changed && !card) window.location.reload();
    });

    // Register rows open the drawer in place; their link (with SelectedProjectId) still works
    // without the script, or when opened in a new tab.
    function openProject(id, name) {
        if (!Number.isInteger(id) || id <= 0) return;
        if (id !== projectId) {
            projectId = id;
            offcanvas.dataset.projectId = String(id);
            // Nothing from the previous project may be shown or posted against this one.
            clearShownProject();
        }
        if (projectNameLabel) projectNameLabel.textContent = name || '';
        bootstrap.Offcanvas.getOrCreateInstance(offcanvas).show();
    }
    document.addEventListener('click', (event) => {
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const trigger = event.target instanceof Element ? event.target.closest('[data-tot-open-project-id]') : null;
        if (!trigger) return;
        event.preventDefault();
        openProject(Number.parseInt(trigger.getAttribute('data-tot-open-project-id') || '0', 10), trigger.getAttribute('data-tot-project-name') || '');
    });
    offcanvas.addEventListener('show.bs.offcanvas', () => { setRemarkMode(false); setEditMode(false); lastLoad = loadDetails(); });
    // Bootstrap needs the answer now, so the close is held, the question asked, and the
    // drawer closed again only if the user agrees.
    let closeApproved = false;
    offcanvas.addEventListener('hide.bs.offcanvas', (event) => {
        const hasRemarkDraft = remarking && (remarkBody?.value?.trim()?.length || 0) > 0;
        if (closeApproved || !(hasUnsavedDetails() || hasRemarkDraft)) {
            closeApproved = false;
            return;
        }
        event.preventDefault();
        askConfirm({
            tone: 'warning',
            title: 'Discard the unsaved Transfer of Technology changes?',
            message: 'Your edits in this drawer have not been saved.',
            confirmText: 'Discard changes',
            cancelText: 'Keep editing'
        }).then((accepted) => {
            if (!accepted) return;
            closeApproved = true;
            bootstrap.Offcanvas.getOrCreateInstance(offcanvas).hide();
        });
    });

    // ?tot=open opens the drawer; ?tot=edit also starts editing when this user may edit and
    // nothing is pending. The old /Projects/Tot/Edit address redirects here with ?tot=edit.
    (async () => {
        let mode = null;
        try { mode = new URLSearchParams(window.location.search).get('tot'); } catch { mode = null; }
        if (mode !== 'open' && mode !== 'edit') return;

        try {
            const url = new URL(window.location.href);
            url.searchParams.delete('tot');
            window.history.replaceState(window.history.state, '', `${url.pathname}${url.search}${url.hash}`);
        } catch { /* leave the address as it is */ }

        bootstrap.Offcanvas.getOrCreateInstance(offcanvas).show();
        if (mode !== 'edit') return;

        await lastLoad;
        if (!loaded) return; // the load error stays on screen
        const canEdit = editButton instanceof HTMLButtonElement && !editButton.classList.contains('d-none');
        if (canEdit && form instanceof HTMLFormElement) { clearMessages(); setEditMode(true); }
    })();
    // The register opens the drawer for ?SelectedProjectId= (links from the project page and
    // the Command desk), once every listener above is in place.
    if (offcanvas.dataset.totOpenOnLoad === 'true' && projectId > 0) openProject(projectId, projectNameLabel?.textContent || '');
})();
