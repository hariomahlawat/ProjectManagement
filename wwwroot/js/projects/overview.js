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
                'RequestVerificationToken': tokenInput.value
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

    initProjectModeration();

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
            const firstInput = backfillModal.querySelector('[data-backfill-start], [data-backfill-completed]');
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
                        RequestVerificationToken: tokenInput.value
                    },
                    body: JSON.stringify(payload),
                    credentials: 'same-origin'
                });

                if (response.ok) {
                    const instance = bootstrap.Modal.getInstance(backfillModal);
                    instance?.hide();
                    showToast('Stage dates updated.', 'success');
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

        function setActive(name) {
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
                setActive(target);
            });
        });

        const override = getTimelineOverride();
        const initial = override || getStored();
        setActive(initial);
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
