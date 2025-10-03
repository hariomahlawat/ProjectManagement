(function () {
    if (typeof bootstrap === 'undefined') {
        return;
    }

    function showToast(message, variant) {
        if (!message) {
            return;
        }

        const variantMap = new Map([
            ['success', 'text-bg-success'],
            ['danger', 'text-bg-danger'],
            ['warning', 'text-bg-warning'],
            ['info', 'text-bg-primary']
        ]);

        const cssClass = variantMap.get(variant) || 'text-bg-primary';

        let container = document.getElementById('overviewToastContainer');
        if (!container) {
            container = document.createElement('div');
            container.id = 'overviewToastContainer';
            container.className = 'toast-container position-fixed top-0 end-0 p-3';
            container.setAttribute('aria-live', 'polite');
            container.setAttribute('aria-atomic', 'true');
            document.body.appendChild(container);
        }

        const toast = document.createElement('div');
        toast.className = `toast align-items-center ${cssClass} border-0`;
        toast.setAttribute('role', 'status');
        toast.setAttribute('aria-live', 'polite');
        toast.setAttribute('aria-atomic', 'true');
        toast.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">${message}</div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>`;

        container.appendChild(toast);

        const instance = bootstrap.Toast.getOrCreateInstance(toast, { delay: 4000 });
        instance.show();

        toast.addEventListener('hidden.bs.toast', () => {
            toast.remove();
            if (container.childElementCount === 0) {
                container.remove();
            }
        });
    }

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
            const stages = stageRows().map((row) => {
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
            }).filter((stage) => stage.stageCode);

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

    class RemarksPanel {
        constructor(root, toast) {
            this.root = root;
            this.toastHandler = typeof toast === 'function' ? toast : () => { };
            this.config = this.parseConfig(root.dataset.config);
            this.apiBase = this.buildApiBase();
            this.currentUserId = this.config.currentUserId || null;
            this.actorHasOverride = !!this.config.actorHasOverride;
            this.allowExternal = !!this.config.allowExternal;
            this.pageSize = this.config.pageSize || 20;
            this.timeZone = this.config.timeZone || 'Asia/Kolkata';
            this.today = this.config.today || new Date().toISOString().slice(0, 10);
            this.state = {
                type: 'all',
                timeRange: 'all',
                includeDeleted: false,
                page: 1,
                total: 0,
                items: [],
                loading: false,
                hasMore: false,
                initialised: false
            };
            this.editingId = null;
            this.editDraft = '';
            this.roleLabels = new Map();
            this.roleCanonicalMap = new Map();
            this.stageLabels = new Map();
            (this.config.roleOptions || []).forEach((option) => {
                if (!option) {
                    return;
                }

                const label = option.label || option.value || option.canonical || '';
                const canonical = option.canonical || option.value || '';
                const candidates = [
                    option.value,
                    option.label,
                    option.canonical,
                    this.toCamelCase(option?.value),
                    this.toCamelCase(option?.canonical)
                ];

                candidates.forEach((candidate) => {
                    this.registerRoleLabel(candidate, label);
                    this.registerRoleCanonical(candidate, canonical);
                });
            });
            if (this.config.actorRole && this.config.actorRoleLabel) {
                this.registerRoleLabel(this.config.actorRole, this.config.actorRoleLabel);
                this.registerRoleCanonical(this.config.actorRole, this.config.actorRole);
                this.registerRoleLabel(this.config.actorRoleLabel, this.config.actorRoleLabel);
                this.registerRoleCanonical(this.config.actorRoleLabel, this.config.actorRole);
            }
            const resolvedActorRoles = Array.isArray(this.config.actorRoles)
                ? this.config.actorRoles
                    .map((role) => this.resolveCanonicalRole(role))
                    .filter((role) => typeof role === 'string' && role.length > 0)
                : [];
            this.actorRoles = new Set(resolvedActorRoles);
            this.actorRole = this.resolveCanonicalRole(this.config.actorRole)
                || (this.config.actorRoleLabel ? this.resolveCanonicalRole(this.config.actorRoleLabel) : null)
                || (resolvedActorRoles.length > 0 ? resolvedActorRoles[0] : null);
            if (this.actorRole) {
                this.registerRoleCanonical(this.actorRole, this.actorRole);
                this.actorRoles.add(this.actorRole);
            }
            this.actorRoleLabel = this.config.actorRoleLabel || (this.actorRole ? this.getRoleLabel(this.actorRole) : '');
            (this.config.stageOptions || []).forEach((option) => {
                if (option && option.value) {
                    this.stageLabels.set(option.value, option.label || option.value);
                }
            });
            this.cacheElements();
            this.bindEvents();
            this.updateTypeButtons();
            this.updateTimeButtons();
        }

        parseConfig(raw) {
            if (!raw) {
                return {};
            }

            try {
                return JSON.parse(raw);
            } catch (error) {
                try {
                    const decoded = raw.replace(/&quot;/g, '"');
                    return JSON.parse(decoded);
                } catch (innerError) {
                    return {};
                }
            }
        }

        buildApiBase() {
            const projectId = this.config.projectId || this.root.getAttribute('data-panel-project-id');
            if (!projectId) {
                return '/api/projects/0/remarks';
            }

            return `/api/projects/${projectId}/remarks`;
        }

        cacheElements() {
            const remarksContainer = this.root.closest('[data-panel-project-id]') || this.root;
            this.typeButtons = Array.from(remarksContainer.querySelectorAll('[data-remarks-type]'));
            this.timeButtons = Array.from(remarksContainer.querySelectorAll('[data-remarks-time]'));
            this.includeDeletedToggle = remarksContainer.querySelector('[data-remarks-include-deleted]');
            this.listContainer = this.root.querySelector('[data-remarks-items]');
            this.emptyState = this.root.querySelector('[data-remarks-empty]');
            this.loadMoreButton = this.root.querySelector('[data-remarks-load-more]');
            this.modalElement = this.root.querySelector('[data-remarks-modal]');
            this.modalTriggers = Array.from(this.root.querySelectorAll('[data-remarks-open-modal]'));
            this.modalInstance = this.modalElement ? bootstrap.Modal.getOrCreateInstance(this.modalElement) : null;
            this.composerForm = this.root.querySelector('[data-remarks-composer]');
            this.feedback = this.root.querySelector('[data-remarks-feedback]');
            this.externalFields = this.root.querySelector('[data-remarks-external-fields]');
            this.eventDateInput = this.root.querySelector('[data-remarks-event-date]');
            this.stageSelect = this.root.querySelector('[data-remarks-stage]');
            this.resetButton = this.root.querySelector('[data-remarks-reset]');
            this.submitButton = this.root.querySelector('[data-remarks-submit]');
            this.bodyField = this.composerForm ? this.composerForm.querySelector('[data-remarks-body]') : null;
            this.tokenInput = this.composerForm ? this.composerForm.querySelector('input[name="__RequestVerificationToken"]') : null;
            this.composerType = 'Internal';
        }

        bindEvents() {
            if (this.modalTriggers && this.modalTriggers.length > 0 && this.modalInstance) {
                this.modalTriggers.forEach((button) => {
                    button.addEventListener('click', (event) => {
                        event.preventDefault();
                        this.resetComposer();
                        this.modalInstance?.show();
                    });
                });
            }

            if (this.modalElement) {
                this.modalElement.addEventListener('shown.bs.modal', () => {
                    if (this.bodyField instanceof HTMLElement) {
                        this.bodyField.focus();
                    }
                });

                this.modalElement.addEventListener('hidden.bs.modal', () => {
                    this.resetComposer();
                });
            }

            if (this.typeButtons.length > 0) {
                this.typeButtons.forEach((button) => {
                    button.addEventListener('click', () => {
                        this.setTypeFilter(button.dataset.remarksType || 'all');
                    });
                });
            }

            if (this.timeButtons.length > 0) {
                this.timeButtons.forEach((button) => {
                    button.addEventListener('click', () => {
                        this.setTimeFilter(button.getAttribute('data-remarks-time') || 'all');
                    });
                });
            }

            if (this.includeDeletedToggle) {
                this.includeDeletedToggle.addEventListener('change', () => {
                    this.state.includeDeleted = this.includeDeletedToggle.checked;
                    this.reload();
                });
            }

            if (this.loadMoreButton) {
                this.loadMoreButton.addEventListener('click', () => this.loadMore());
            }

            if (this.listContainer) {
                this.listContainer.addEventListener('click', (event) => {
                    const target = event.target;
                    if (!(target instanceof HTMLElement)) {
                        return;
                    }

                    const action = target.getAttribute('data-remark-action');
                    if (!action) {
                        return;
                    }

                    const article = target.closest('[data-remark-id]');
                    if (!article) {
                        return;
                    }

                    const remarkId = Number.parseInt(article.getAttribute('data-remark-id') || '', 10);
                    if (!Number.isFinite(remarkId) || remarkId <= 0) {
                        return;
                    }

                    if (action === 'edit') {
                        this.startEdit(remarkId);
                    } else if (action === 'cancel') {
                        this.cancelEdit();
                    } else if (action === 'save') {
                        this.saveEdit(remarkId);
                    } else if (action === 'delete') {
                        this.deleteRemark(remarkId);
                    }
                });
            }

            if (this.composerForm) {
                this.composerForm.addEventListener('submit', (event) => {
                    event.preventDefault();
                    this.postRemark();
                });

                this.composerForm.addEventListener('reset', (event) => {
                    event.preventDefault();
                    this.resetComposer();
                });

                if (this.resetButton) {
                    this.resetButton.addEventListener('click', (event) => {
                        event.preventDefault();
                        this.resetComposer();
                    });
                }

                const options = Array.from(this.composerForm.querySelectorAll('[data-remarks-composer-option]'));
                if (options.length > 0) {
                    options.forEach((button) => {
                        button.addEventListener('click', () => {
                            this.setComposerType(button.getAttribute('data-remarks-composer-option') || 'Internal');
                        });
                    });
                }

                if (this.bodyField) {
                    this.bodyField.addEventListener('input', () => {
                        this.validateComposer();
                    });
                }

                if (this.eventDateInput) {
                    this.eventDateInput.addEventListener('input', () => {
                        this.validateComposer();
                    });
                }

                this.setComposerType('Internal');
                this.validateComposer();
            }
        }

        async ensureLoaded() {
            if (this.state.initialised) {
                return;
            }

            const loaded = await this.fetchPage(1, false);
            if (loaded) {
                this.state.initialised = true;
            }
        }

        setTypeFilter(type) {
            const value = (type || 'all').toString().toLowerCase();
            if (value === 'internal') {
                this.state.type = 'Internal';
            } else if (value === 'external') {
                this.state.type = 'External';
            } else {
                this.state.type = 'all';
            }

            this.updateTypeButtons();
            this.reload();
        }

        updateTypeButtons() {
            this.typeButtons.forEach((button) => {
                const value = button.getAttribute('data-remarks-type') || '';
                const isActive = (this.state.type === 'all' && value === 'all') || value === this.state.type;
                button.classList.toggle('active', isActive);
                button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
            });
        }

        setTimeFilter(range) {
            const value = this.normalizeTimeRange(range);
            if (this.state.timeRange === value) {
                this.updateTimeButtons();
                return;
            }

            this.state.timeRange = value;
            this.updateTimeButtons();
            this.reload();
        }

        updateTimeButtons() {
            if (!Array.isArray(this.timeButtons)) {
                return;
            }

            this.timeButtons.forEach((button) => {
                const value = this.normalizeTimeRange(button.getAttribute('data-remarks-time') || '');
                const isActive = value === this.state.timeRange;
                button.classList.toggle('active', isActive);
                button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
            });
        }

        normalizeTimeRange(range) {
            if (typeof range !== 'string') {
                return 'all';
            }

            return range.toLowerCase() === 'last-month' ? 'last-month' : 'all';
        }

        reload() {
            this.state.page = 1;
            this.fetchPage(1, false);
        }

        loadMore() {
            if (this.state.loading || !this.state.hasMore) {
                return;
            }

            const nextPage = this.state.page + 1;
            this.fetchPage(nextPage, true);
        }

        resolveDateFrom() {
            if (this.state.timeRange !== 'last-month') {
                return '';
            }

            const reference = this.parseDateOnly(this.today);
            if (!reference) {
                return '';
            }

            const start = new Date(reference.getTime());
            start.setUTCMonth(start.getUTCMonth() - 1);
            return this.formatDateOnly(start);
        }

        parseDateOnly(value) {
            if (typeof value !== 'string' || value.trim().length === 0) {
                return null;
            }

            const parts = value.split('-').map((part) => Number.parseInt(part, 10));
            if (parts.length !== 3 || parts.some((part) => !Number.isFinite(part))) {
                return null;
            }

            const [year, month, day] = parts;
            const date = new Date(Date.UTC(year, month - 1, day));
            return Number.isNaN(date.getTime()) ? null : date;
        }

        formatDateOnly(date) {
            if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
                return '';
            }

            const year = date.getUTCFullYear();
            const month = String(date.getUTCMonth() + 1).padStart(2, '0');
            const day = String(date.getUTCDate()).padStart(2, '0');
            return `${year}-${month}-${day}`;
        }

        buildQueryParams(page) {
            const params = new URLSearchParams();
            params.set('page', String(page));
            params.set('pageSize', String(this.pageSize));

            if (this.actorRole) {
                params.set('actorRole', this.actorRole);
            }

            if (this.state.type === 'Internal' || this.state.type === 'External') {
                params.set('type', this.state.type);
            }

            const dateFrom = this.resolveDateFrom();
            if (dateFrom) {
                params.set('dateFrom', dateFrom);
            }

            if (this.state.includeDeleted) {
                params.set('includeDeleted', 'true');
            }

            return params;
        }

        async fetchPage(page, append) {
            if (this.state.loading) {
                return false;
            }

            if (!this.actorRole) {
                if (!append) {
                    this.state.items = [];
                    this.state.page = 1;
                    this.state.total = 0;
                    this.state.hasMore = false;
                    if (this.listContainer) {
                        this.listContainer.innerHTML = '';
                    }

                    if (this.emptyState) {
                        this.emptyState.classList.remove('d-none');
                        this.emptyState.textContent = 'Remarks are unavailable for your account.';
                    }
                }

                this.state.initialised = true;
                return false;
            }

            if (!append) {
                this.setLoading(true);
            }
            this.state.loading = true;

            const params = this.buildQueryParams(page);
            try {
                const response = await fetch(`${this.apiBase}?${params.toString()}`, {
                    headers: { Accept: 'application/json' },
                    credentials: 'same-origin'
                });

                if (!response.ok) {
                    const problem = await this.readProblemDetails(response);
                    this.toastHandler(problem || 'Unable to load remarks.', 'danger');
                    this.state.initialised = false;
                    return false;
                }

                const payload = await response.json();
                const items = Array.isArray(payload.items) ? payload.items : (Array.isArray(payload.Items) ? payload.Items : []);
                const normalized = items.map((item) => this.normalizeRemark(item));

                if (append) {
                    this.state.items = this.state.items.concat(normalized);
                } else {
                    this.state.items = normalized;
                    if (this.listContainer) {
                        this.listContainer.scrollTop = 0;
                    }
                }

                const totalRaw = payload.total ?? payload.Total;
                const pageRaw = payload.page ?? payload.Page;
                const totalNumber = Number(totalRaw);
                const pageNumber = Number(pageRaw);

                if (Number.isFinite(totalNumber)) {
                    this.state.total = totalNumber;
                } else if (!append) {
                    this.state.total = this.state.items.length;
                }

                this.state.page = Number.isFinite(pageNumber) ? pageNumber : page;
                this.state.hasMore = this.state.items.length < this.state.total;
                this.state.initialised = true;
                this.renderList();
                return true;
            } catch (error) {
                this.toastHandler('Unable to load remarks.', 'danger');
                this.state.initialised = false;
                return false;
            } finally {
                this.state.loading = false;
                this.setLoading(false);
            }
        }

        setLoading(isLoading) {
            if (this.loadMoreButton) {
                this.loadMoreButton.disabled = isLoading;
            }

            if (isLoading && this.emptyState) {
                this.emptyState.classList.remove('d-none');
                this.emptyState.textContent = 'Loading remarks…';
            }

            if (!isLoading && this.emptyState && (!this.state.items || this.state.items.length === 0)) {
                this.emptyState.classList.remove('d-none');
                this.emptyState.textContent = 'No remarks yet.';
            }
        }

        renderList() {
            if (!this.listContainer) {
                return;
            }

            this.listContainer.innerHTML = '';

            if (!Array.isArray(this.state.items) || this.state.items.length === 0) {
                if (this.emptyState) {
                    this.emptyState.classList.remove('d-none');
                    this.emptyState.textContent = this.state.loading ? 'Loading remarks…' : 'No remarks yet.';
                }
                if (this.loadMoreButton) {
                    this.loadMoreButton.classList.add('d-none');
                }
                return;
            }

            if (this.emptyState) {
                this.emptyState.classList.add('d-none');
            }

            this.state.items.forEach((remark) => {
                const element = this.buildRemarkElement(remark);
                this.listContainer.appendChild(element);
            });

            if (this.loadMoreButton) {
                this.loadMoreButton.classList.toggle('d-none', !this.state.hasMore);
            }
        }

        buildRemarkElement(remark) {
            const article = document.createElement('article');
            article.className = 'remarks-item d-flex flex-column gap-2';
            article.setAttribute('data-remark-id', String(remark.id));
            article.setAttribute('data-row-version', remark.rowVersion || '');

            if (remark.isDeleted) {
                article.classList.add('remarks-item-deleted');
            }

            const header = document.createElement('div');
            header.className = 'd-flex gap-3 align-items-start';

            const avatar = document.createElement('div');
            avatar.className = 'remarks-avatar avatar';
            avatar.textContent = (remark.authorInitials || '?').slice(0, 2).toUpperCase();
            header.appendChild(avatar);

            const identity = document.createElement('div');
            identity.className = 'flex-grow-1';

            const nameRow = document.createElement('div');
            nameRow.className = 'd-flex flex-wrap align-items-center gap-2';
            const name = document.createElement('span');
            name.className = 'fw-semibold';
            name.textContent = remark.authorDisplayName || remark.authorUserId;
            nameRow.appendChild(name);

            const roleBadge = document.createElement('span');
            roleBadge.className = 'badge bg-light text-dark border';
            roleBadge.textContent = this.getRoleLabel(remark.authorRole);
            nameRow.appendChild(roleBadge);

            if (remark.createdAtUtc) {
                const timestamp = document.createElement('time');
                timestamp.className = 'text-muted small';
                const formatted = this.formatTimestamp(remark.createdAtUtc);
                timestamp.textContent = formatted.label;
                timestamp.dateTime = formatted.iso;
                if (formatted.tooltip) {
                    timestamp.title = formatted.tooltip;
                }
                nameRow.appendChild(timestamp);
            }

            identity.appendChild(nameRow);

            const metaRow = document.createElement('div');
            metaRow.className = 'remarks-meta d-flex flex-wrap align-items-center gap-2';
            const typeBadge = document.createElement('span');
            typeBadge.className = 'badge rounded-pill text-bg-secondary';
            typeBadge.textContent = remark.type === 'External' ? 'External' : 'Internal';
            metaRow.appendChild(typeBadge);

            if (remark.type === 'External') {
                if (remark.eventDate) {
                    const eventBadge = document.createElement('span');
                    eventBadge.className = 'badge rounded-pill text-bg-info-subtle text-dark border border-info-subtle';
                    eventBadge.textContent = `Event ${this.formatEventDate(remark.eventDate)}`;
                    metaRow.appendChild(eventBadge);
                }

                const stageLabel = this.getStageLabel(remark.stageRef) || remark.stageName;
                if (stageLabel) {
                    const stageBadge = document.createElement('span');
                    stageBadge.className = 'badge rounded-pill bg-light border';
                    stageBadge.textContent = stageLabel;
                    metaRow.appendChild(stageBadge);
                }
            } else if (remark.stageName) {
                const stageBadge = document.createElement('span');
                stageBadge.className = 'badge rounded-pill bg-light border';
                stageBadge.textContent = remark.stageName;
                metaRow.appendChild(stageBadge);
            }

            identity.appendChild(metaRow);

            if (remark.lastEditedAtUtc && !remark.isDeleted) {
                const edited = document.createElement('div');
                edited.className = 'text-muted small';
                const formatted = this.formatTimestamp(remark.lastEditedAtUtc);
                edited.textContent = `Edited ${formatted.label.toLowerCase()}`;
                edited.title = formatted.tooltip;
                identity.appendChild(edited);
            }

            header.appendChild(identity);
            article.appendChild(header);

            const body = document.createElement('div');
            body.className = 'remarks-body';
            body.setAttribute('data-remark-body', '');

            if (this.editingId === remark.id) {
                const textarea = document.createElement('textarea');
                textarea.className = 'form-control';
                textarea.rows = 4;
                textarea.maxLength = 4000;
                textarea.value = this.editDraft || this.decodeBody(remark.body);
                textarea.setAttribute('data-remark-edit', 'body');
                textarea.addEventListener('input', () => {
                    this.editDraft = textarea.value;
                });
                body.appendChild(textarea);
            } else {
                body.innerHTML = remark.body || '';
            }

            article.appendChild(body);

            if (remark.isDeleted) {
                const tombstone = document.createElement('div');
                tombstone.className = 'text-muted small';
                tombstone.textContent = this.formatDeletionLabel(remark);
                article.appendChild(tombstone);
            }

            const actions = document.createElement('div');
            actions.className = 'remarks-actions d-flex flex-wrap align-items-center gap-2';

            if (this.editingId === remark.id) {
                const saveButton = document.createElement('button');
                saveButton.type = 'button';
                saveButton.className = 'btn btn-sm btn-success';
                saveButton.setAttribute('data-remark-action', 'save');
                saveButton.textContent = 'Save';
                actions.appendChild(saveButton);

                const cancelButton = document.createElement('button');
                cancelButton.type = 'button';
                cancelButton.className = 'btn btn-sm btn-outline-secondary';
                cancelButton.setAttribute('data-remark-action', 'cancel');
                cancelButton.textContent = 'Cancel';
                actions.appendChild(cancelButton);
            } else if (!remark.isDeleted) {
                if (this.canEditRemark(remark)) {
                    const editButton = document.createElement('button');
                    editButton.type = 'button';
                    editButton.className = 'btn btn-sm btn-outline-secondary';
                    editButton.setAttribute('data-remark-action', 'edit');
                    editButton.textContent = 'Edit';
                    actions.appendChild(editButton);

                    const deleteButton = document.createElement('button');
                    deleteButton.type = 'button';
                    deleteButton.className = 'btn btn-sm btn-outline-danger';
                    deleteButton.setAttribute('data-remark-action', 'delete');
                    deleteButton.textContent = 'Delete';
                    actions.appendChild(deleteButton);
                } else if (remark.authorUserId === this.currentUserId) {
                    const notice = document.createElement('div');
                    notice.className = 'text-muted small';
                    notice.textContent = 'You can edit your remark within 3 hours of posting.';
                    actions.appendChild(notice);
                }
            }

            article.appendChild(actions);

            return article;
        }

        formatTimestamp(value) {
            if (!value) {
                return { label: '', tooltip: '', iso: '' };
            }

            const date = new Date(value);
            if (Number.isNaN(date.getTime())) {
                return { label: '', tooltip: '', iso: '' };
            }

            const shortFormatter = new Intl.DateTimeFormat('en-IN', {
                dateStyle: 'medium',
                timeStyle: 'short',
                timeZone: this.timeZone
            });
            const longFormatter = new Intl.DateTimeFormat('en-GB', {
                dateStyle: 'full',
                timeStyle: 'long',
                timeZone: this.timeZone
            });

            return {
                label: `${shortFormatter.format(date)} IST`,
                tooltip: `${longFormatter.format(date)} (IST)`,
                iso: date.toISOString()
            };
        }

        formatEventDate(value) {
            if (!value) {
                return '';
            }

            try {
                const date = new Date(`${value}T00:00:00`);
                if (Number.isNaN(date.getTime())) {
                    return value;
                }

                const formatter = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeZone: this.timeZone });
                return formatter.format(date);
            } catch (error) {
                return value;
            }
        }

        registerRoleLabel(value, label) {
            if (value === null || value === undefined) {
                return;
            }

            const raw = value.toString().trim();
            if (raw.length === 0) {
                return;
            }

            const resolvedLabel = label && label.toString().trim().length > 0 ? label.toString().trim() : raw;
            this.roleLabels.set(raw, resolvedLabel);

            const normalized = this.normalizeRoleKey(raw);
            if (normalized.length > 0) {
                this.roleLabels.set(normalized, resolvedLabel);
            }
        }

        registerRoleCanonical(value, canonical) {
            if (value === null || value === undefined || canonical === null || canonical === undefined) {
                return;
            }

            const rawValue = value.toString().trim();
            const resolvedCanonical = canonical.toString().trim();
            if (rawValue.length === 0 || resolvedCanonical.length === 0) {
                return;
            }

            this.roleCanonicalMap.set(rawValue, resolvedCanonical);

            const normalizedValue = this.normalizeRoleKey(rawValue);
            if (normalizedValue.length > 0) {
                this.roleCanonicalMap.set(normalizedValue, resolvedCanonical);
            }

            const normalizedCanonical = this.normalizeRoleKey(resolvedCanonical);
            if (normalizedCanonical.length > 0) {
                this.roleCanonicalMap.set(normalizedCanonical, resolvedCanonical);
            }
        }

        normalizeRoleKey(value) {
            if (value === null || value === undefined) {
                return '';
            }

            return value.toString().toLowerCase().replace(/[^a-z0-9]/g, '');
        }

        resolveCanonicalRole(value) {
            if (value === null || value === undefined) {
                return null;
            }

            const raw = value.toString().trim();
            if (raw.length === 0) {
                return null;
            }

            if (this.roleCanonicalMap.has(raw)) {
                return this.roleCanonicalMap.get(raw);
            }

            const normalized = this.normalizeRoleKey(raw);
            if (normalized.length > 0 && this.roleCanonicalMap.has(normalized)) {
                return this.roleCanonicalMap.get(normalized);
            }

            return raw;
        }

        toCamelCase(value) {
            if (value === null || value === undefined) {
                return '';
            }

            const input = value.toString().trim();
            if (input.length === 0) {
                return '';
            }

            const spaced = input.replace(/([a-z])([A-Z])/g, '$1 $2');
            const parts = spaced.split(/[\s_\-]+/).filter((part) => part.length > 0);
            if (parts.length === 0) {
                return '';
            }

            const [first, ...rest] = parts;
            return first.toLowerCase() + rest.map((part) => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase()).join('');
        }

        getRoleLabel(role) {
            if (!role) {
                return '';
            }

            const raw = role.toString();
            if (this.roleLabels.has(raw)) {
                return this.roleLabels.get(raw);
            }

            const normalized = this.normalizeRoleKey(raw);
            if (normalized && this.roleLabels.has(normalized)) {
                return this.roleLabels.get(normalized);
            }

            return raw
                .replace(/[_\-]+/g, ' ')
                .replace(/([a-z])([A-Z])/g, '$1 $2')
                .trim();
        }

        getStageLabel(code) {
            if (!code) {
                return '';
            }

            return this.stageLabels.get(code) || '';
        }

        decodeBody(bodyHtml) {
            if (!bodyHtml) {
                return '';
            }

            const normalised = bodyHtml
                .replace(/<br\s*\/?>/gi, '\n')
                .replace(/<\/p>\s*<p>/gi, '\n\n');
            const temp = document.createElement('div');
            temp.innerHTML = normalised;
            const text = temp.textContent || temp.innerText || '';
            return text.replace(/\u00a0/g, ' ').trim();
        }

        getActionRestrictionMessage(remark, action) {
            if (!remark || remark.isDeleted) {
                return 'You do not have permission for this action.';
            }

            if (this.actorHasOverride) {
                return '';
            }

            if (!this.currentUserId || remark.authorUserId !== this.currentUserId) {
                return 'You do not have permission for this action.';
            }

            if (!this.isWithinEditWindow(remark.createdAtUtc)) {
                return action === 'delete'
                    ? 'You can delete your remark within 3 hours of posting.'
                    : 'You can edit your remark within 3 hours of posting.';
            }

            return '';
        }

        canEditRemark(remark) {
            if (remark.isDeleted) {
                return false;
            }

            if (this.actorHasOverride) {
                return true;
            }

            if (!this.currentUserId || remark.authorUserId !== this.currentUserId) {
                return false;
            }

            return this.isWithinEditWindow(remark.createdAtUtc);
        }

        isWithinEditWindow(value) {
            if (!value) {
                return false;
            }

            const created = new Date(value);
            if (Number.isNaN(created.getTime())) {
                return false;
            }

            const diff = Date.now() - created.getTime();
            return diff <= 3 * 60 * 60 * 1000;
        }

        startEdit(remarkId) {
            const remark = this.state.items.find((item) => item.id === remarkId);
            if (!remark) {
                return;
            }

            const restriction = this.getActionRestrictionMessage(remark, 'edit');
            if (restriction) {
                this.toastHandler(restriction, 'warning');
                return;
            }

            this.editingId = remarkId;
            this.editDraft = this.decodeBody(remark.body);
            this.renderList();
        }

        cancelEdit() {
            this.editingId = null;
            this.editDraft = '';
            this.renderList();
        }

        async saveEdit(remarkId) {
            const remark = this.state.items.find((item) => item.id === remarkId);
            if (!remark) {
                return;
            }

            const restriction = this.getActionRestrictionMessage(remark, 'edit');
            if (restriction) {
                this.toastHandler(restriction, 'warning');
                return;
            }

            const article = this.listContainer ? this.listContainer.querySelector(`[data-remark-id="${remarkId}"]`) : null;
            const textarea = article ? article.querySelector('[data-remark-edit="body"]') : null;
            const bodyText = textarea && 'value' in textarea ? textarea.value.trim() : this.editDraft.trim();

            if (!bodyText) {
                this.toastHandler('Remark text cannot be empty.', 'danger');
                return;
            }

            const payload = {
                body: bodyText,
                eventDate: remark.eventDate,
                stageRef: remark.stageRef,
                stageName: remark.stageName,
                actorRole: this.actorRole,
                rowVersion: remark.rowVersion
            };

            try {
                const response = await fetch(`${this.apiBase}/${remarkId}`, {
                    method: 'PUT',
                    headers: this.buildHeaders(),
                    credentials: 'same-origin',
                    body: JSON.stringify(payload)
                });

                if (response.status === 409) {
                    this.toastHandler('This remark was changed by someone else. Reload to continue.', 'warning');
                    return;
                }

                if (response.status === 403) {
                    const problem = await this.readProblemDetails(response);
                    this.toastHandler(problem || 'You do not have permission for this action.', 'warning');
                    return;
                }

                if (!response.ok) {
                    const problem = await this.readProblemDetails(response);
                    this.toastHandler(problem || 'Unable to update remark.', 'danger');
                    return;
                }

                const data = await response.json();
                const updated = this.normalizeRemark(data);
                const index = this.state.items.findIndex((item) => item.id === remarkId);
                if (index >= 0) {
                    this.state.items[index] = updated;
                }
                this.editingId = null;
                this.editDraft = '';
                this.renderList();
                this.toastHandler('Remark updated.', 'success');
            } catch (error) {
                this.toastHandler('Unable to update remark.', 'danger');
            }
        }

        async deleteRemark(remarkId) {
            const remark = this.state.items.find((item) => item.id === remarkId);
            if (!remark) {
                return;
            }

            const restriction = this.getActionRestrictionMessage(remark, 'delete');
            if (restriction) {
                this.toastHandler(restriction, 'warning');
                return;
            }

            if (!window.confirm('Delete this remark?')) {
                return;
            }

            const payload = {
                rowVersion: remark.rowVersion,
                actorRole: this.actorRole
            };

            try {
                const response = await fetch(`${this.apiBase}/${remarkId}`, {
                    method: 'DELETE',
                    headers: this.buildHeaders(),
                    credentials: 'same-origin',
                    body: JSON.stringify(payload)
                });

                if (response.status === 409) {
                    this.toastHandler('This remark was changed by someone else. Reload to continue.', 'warning');
                    return;
                }

                if (response.status === 403) {
                    const problem = await this.readProblemDetails(response);
                    this.toastHandler(problem || 'You do not have permission for this action.', 'warning');
                    return;
                }

                if (!response.ok) {
                    const problem = await this.readProblemDetails(response);
                    this.toastHandler(problem || 'Unable to delete remark.', 'danger');
                    return;
                }

                this.toastHandler('Remark deleted.', 'success');
                this.editingId = null;
                this.editDraft = '';
                this.state.page = 1;
                await this.fetchPage(1, false);
            } catch (error) {
                this.toastHandler('Unable to delete remark.', 'danger');
            }
        }

        async readProblemDetails(response) {
            try {
                const problem = await response.json();
                return problem?.detail || problem?.title || '';
            } catch (error) {
                return '';
            }
        }

        normalizeRemark(data) {
            if (!data) {
                return {
                    id: 0,
                    projectId: this.config.projectId || 0,
                    type: 'Internal',
                    authorRole: '',
                    authorUserId: '',
                    authorDisplayName: '',
                    authorInitials: '?',
                    body: '',
                    eventDate: this.today,
                    stageRef: '',
                    stageName: '',
                    createdAtUtc: null,
                    lastEditedAtUtc: null,
                    isDeleted: false,
                    deletedAtUtc: null,
                    deletedByUserId: null,
                    deletedByRole: null,
                    deletedByDisplayName: null,
                    rowVersion: null
                };
            }

            const idRaw = data.id ?? data.Id;
            const projectRaw = data.projectId ?? data.ProjectId ?? this.config.projectId ?? 0;
            const typeRaw = data.type ?? data.Type ?? 'Internal';
            const type = typeRaw === 'External' ? 'External' : 'Internal';
            const id = Number.parseInt(idRaw, 10);

            return {
                id: Number.isFinite(id) ? id : 0,
                projectId: Number.isFinite(Number(projectRaw)) ? Number(projectRaw) : 0,
                type,
                authorRole: data.authorRole ?? data.AuthorRole ?? '',
                authorUserId: data.authorUserId ?? data.AuthorUserId ?? '',
                authorDisplayName: data.authorDisplayName ?? data.AuthorDisplayName ?? (data.authorUserId ?? data.AuthorUserId ?? ''),
                authorInitials: data.authorInitials ?? data.AuthorInitials ?? '?',
                body: data.body ?? data.Body ?? '',
                eventDate: data.eventDate ?? data.EventDate ?? this.today,
                stageRef: data.stageRef ?? data.StageRef ?? '',
                stageName: data.stageName ?? data.StageName ?? '',
                createdAtUtc: data.createdAtUtc ?? data.CreatedAtUtc ?? null,
                lastEditedAtUtc: data.lastEditedAtUtc ?? data.LastEditedAtUtc ?? null,
                isDeleted: Boolean(data.isDeleted ?? data.IsDeleted ?? false),
                deletedAtUtc: data.deletedAtUtc ?? data.DeletedAtUtc ?? null,
                deletedByUserId: data.deletedByUserId ?? data.DeletedByUserId ?? null,
                deletedByRole: data.deletedByRole ?? data.DeletedByRole ?? null,
                deletedByDisplayName: data.deletedByDisplayName ?? data.DeletedByDisplayName ?? null,
                rowVersion: data.rowVersion ?? data.RowVersion ?? null
            };
        }

        formatDeletionLabel(remark) {
            const actor = remark.deletedByDisplayName || this.getRoleLabel(remark.deletedByRole) || 'Unknown';
            if (remark.deletedAtUtc) {
                const formatted = this.formatTimestamp(remark.deletedAtUtc);
                return `Deleted by ${actor} on ${formatted.label}`;
            }

            return `Deleted by ${actor}`;
        }

        buildHeaders() {
            const headers = { 'Content-Type': 'application/json' };
            if (this.tokenInput && this.tokenInput.value) {
                headers.RequestVerificationToken = this.tokenInput.value;
            }

            return headers;
        }

        setComposerType(type) {
            const target = type === 'External' && this.allowExternal ? 'External' : 'Internal';
            this.composerType = target;

            if (this.composerForm) {
                const options = Array.from(this.composerForm.querySelectorAll('[data-remarks-composer-option]'));
                options.forEach((button) => {
                    const value = button.getAttribute('data-remarks-composer-option');
                    const isActive = value === target;
                    button.classList.toggle('active', isActive);
                    button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
                });
            }

            if (this.externalFields) {
                if (target === 'External') {
                    this.externalFields.classList.remove('d-none');
                    if (this.eventDateInput && !this.eventDateInput.value) {
                        this.eventDateInput.value = this.today;
                    }
                } else {
                    this.externalFields.classList.add('d-none');
                }
            }

            this.validateComposer();
        }

        resetComposer() {
            if (this.bodyField) {
                this.bodyField.value = '';
            }

            if (this.eventDateInput) {
                this.eventDateInput.value = this.today;
            }

            if (this.stageSelect) {
                this.stageSelect.value = '';
            }

            this.setComposerType('Internal');
            this.clearFeedback();
            this.validateComposer();
        }

        isComposerValid() {
            if (!this.bodyField) {
                return false;
            }

            const body = this.bodyField.value ? this.bodyField.value.trim() : '';
            if (!body) {
                return false;
            }

            if (this.composerType === 'External') {
                if (!this.eventDateInput || !this.eventDateInput.value) {
                    return false;
                }

                if (this.eventDateInput.value > this.today) {
                    return false;
                }
            }

            return true;
        }

        validateComposer() {
            if (this.submitButton) {
                this.submitButton.disabled = !this.isComposerValid();
            }
        }

        setFeedback(message, variant) {
            if (!this.feedback) {
                if (message) {
                    this.toastHandler(message, variant === 'success' ? 'success' : 'danger');
                }
                return;
            }

            this.feedback.textContent = message || '';
            this.feedback.classList.remove('text-danger', 'text-success');
            if (!message) {
                return;
            }

            if (variant === 'success') {
                this.feedback.classList.add('text-success');
            } else {
                this.feedback.classList.add('text-danger');
            }
        }

        clearFeedback() {
            if (this.feedback) {
                this.feedback.textContent = '';
                this.feedback.classList.remove('text-danger', 'text-success');
            }
        }

        async postRemark() {
            if (!this.composerForm || !this.bodyField) {
                return;
            }

            const body = this.bodyField.value.trim();
            if (!body) {
                this.setFeedback('Remark cannot be empty.', 'danger');
                return;
            }

            let eventDate = this.today;
            let stageRef = null;
            let stageName = null;

            if (this.composerType === 'External') {
                if (!this.eventDateInput || !this.eventDateInput.value) {
                    this.setFeedback('Select an event date for external remarks.', 'danger');
                    return;
                }

                if (this.eventDateInput.value > this.today) {
                    this.setFeedback('Event date cannot be in the future.', 'danger');
                    return;
                }

                eventDate = this.eventDateInput.value;
                if (this.stageSelect && this.stageSelect.value) {
                    stageRef = this.stageSelect.value;
                    stageName = this.getStageLabel(stageRef);
                }
            }

            const payload = {
                type: this.composerType,
                body,
                eventDate,
                stageRef,
                stageName,
                actorRole: this.actorRole
            };

            try {
                const response = await fetch(this.apiBase, {
                    method: 'POST',
                    headers: this.buildHeaders(),
                    credentials: 'same-origin',
                    body: JSON.stringify(payload)
                });

                if (response.status === 403) {
                    const problem = await this.readProblemDetails(response);
                    this.setFeedback(problem || 'You do not have permission for this action.', 'danger');
                    return;
                }

                if (!response.ok) {
                    const problem = await this.readProblemDetails(response);
                    this.setFeedback(problem || 'Unable to add remark.', 'danger');
                    return;
                }

                this.setFeedback('Remark added.', 'success');
                this.resetComposer();
                if (this.modalInstance) {
                    this.modalInstance.hide();
                }
                this.state.page = 1;
                await this.fetchPage(1, false);
            } catch (error) {
                this.setFeedback('Unable to add remark.', 'danger');
            }
        }
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

        setActive(getStored());
    }

    const remarksElement = document.querySelector('[data-remarks-panel]');
    let remarksPanelInstance = null;
    if (remarksElement) {
        remarksPanelInstance = new RemarksPanel(remarksElement, showToast);
    }

    const panelCard = document.querySelector('[data-panel-project-id]');
    if (panelCard) {
        initPanelToggle(panelCard, remarksPanelInstance);
    } else if (remarksPanelInstance) {
        remarksPanelInstance.ensureLoaded();
    }
})();
