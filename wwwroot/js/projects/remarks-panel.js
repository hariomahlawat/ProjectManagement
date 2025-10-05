(function (global) {
    'use strict';

    if (!global) {
        return;
    }

    const bootstrap = global.bootstrap;

    function showToast(message, variant) {
        if (!message) {
            return;
        }

        const doc = global.document;
        if (!doc) {
            return;
        }

        const variantMap = new Map([
            ['success', 'text-bg-success'],
            ['danger', 'text-bg-danger'],
            ['warning', 'text-bg-warning'],
            ['info', 'text-bg-primary']
        ]);

        const cssClass = variantMap.get(variant) || 'text-bg-primary';

        let container = doc.getElementById('overviewToastContainer');
        if (!container) {
            container = doc.createElement('div');
            container.id = 'overviewToastContainer';
            container.className = 'toast-container position-fixed top-0 end-0 p-3';
            container.setAttribute('aria-live', 'polite');
            container.setAttribute('aria-atomic', 'true');
            doc.body.appendChild(container);
        }

        const toast = doc.createElement('div');
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

        if (bootstrap && bootstrap.Toast && typeof bootstrap.Toast.getOrCreateInstance === 'function') {
            const instance = bootstrap.Toast.getOrCreateInstance(toast, { delay: 4000 });
            instance.show();

            toast.addEventListener('hidden.bs.toast', () => {
                toast.remove();
                if (container.childElementCount === 0) {
                    container.remove();
                }
            });
        } else {
            if (typeof global.setTimeout === 'function') {
                global.setTimeout(() => {
                    toast.remove();
                    if (container.childElementCount === 0 && container.parentElement) {
                        container.remove();
                    }
                }, 4000);
            }
        }
    }

    class RemarkMentionAutocomplete {
        constructor(textarea, options) {
            this.textarea = textarea;
            this.doc = textarea?.ownerDocument || global.document;
            this.search = options?.search || (async () => []);
            this.onInsert = typeof options?.onInsert === 'function' ? options.onInsert : () => { };
            this.registerMention = typeof options?.registerMention === 'function' ? options.registerMention : null;
            this.items = [];
            this.activeIndex = -1;
            this.triggerIndex = null;
            this.currentQuery = '';
            this.searchTimer = null;
            this.container = this.createContainer();
            this.visible = false;
            this.bindEvents();
        }

        createContainer() {
            const container = this.doc.createElement('div');
            container.className = 'remark-mention-autocomplete dropdown-menu shadow';
            container.style.position = 'absolute';
            container.style.display = 'none';
            container.style.zIndex = '1056';
            container.setAttribute('role', 'listbox');
            this.doc.body.appendChild(container);
            return container;
        }

        bindEvents() {
            if (!this.textarea) {
                return;
            }

            this.textarea.addEventListener('input', () => {
                this.handleInput();
            });

            this.textarea.addEventListener('keydown', (event) => {
                if (!this.visible) {
                    return;
                }

                if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    this.moveSelection(1);
                } else if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    this.moveSelection(-1);
                } else if (event.key === 'Enter') {
                    if (this.activeIndex >= 0 && this.activeIndex < this.items.length) {
                        event.preventDefault();
                        this.select(this.activeIndex);
                    }
                } else if (event.key === 'Escape') {
                    event.preventDefault();
                    this.hide();
                }
            });

            this.textarea.addEventListener('blur', () => {
                global.setTimeout(() => this.hide(), 100);
            });
        }

        handleInput() {
            const context = this.getTriggerContext();
            if (!context) {
                this.hide();
                return;
            }

            this.triggerIndex = context.index;
            const query = context.query.trim();
            if (query.length === 0) {
                this.hide();
                return;
            }

            if (this.currentQuery === query) {
                return;
            }

            this.currentQuery = query;
            if (this.searchTimer) {
                global.clearTimeout(this.searchTimer);
            }

            this.searchTimer = global.setTimeout(() => {
                this.performSearch(query);
            }, 150);
        }

        async performSearch(query) {
            try {
                const results = await this.search(query);
                if (!Array.isArray(results) || results.length === 0) {
                    this.hide();
                    return;
                }

                this.items = results;
                this.renderItems();
                this.activeIndex = 0;
                this.updateActiveItem();
                this.positionMenu();
                this.visible = true;
                this.container.style.display = 'block';
            } catch (error) {
                this.hide();
            }
        }

        renderItems() {
            if (!this.doc) {
                return;
            }

            this.container.innerHTML = '';

            this.items.forEach((item, index) => {
                const button = this.doc.createElement('button');
                button.type = 'button';
                button.className = 'dropdown-item';
                button.setAttribute('role', 'option');
                button.dataset.index = index.toString();
                button.textContent = item.displayName || item.id;
                button.addEventListener('mousedown', (event) => {
                    event.preventDefault();
                    this.select(index);
                });
                this.container.appendChild(button);
            });
        }

        moveSelection(offset) {
            if (!this.visible || this.items.length === 0) {
                return;
            }

            this.activeIndex = (this.activeIndex + offset + this.items.length) % this.items.length;
            this.updateActiveItem();
        }

        updateActiveItem() {
            const children = Array.from(this.container.querySelectorAll('[role="option"]'));
            children.forEach((child, index) => {
                child.classList.toggle('active', index === this.activeIndex);
            });
        }

        select(index) {
            if (index < 0 || index >= this.items.length) {
                return;
            }

            const item = this.items[index];
            this.insertPlaceholder(item);
            this.hide();
        }

        insertPlaceholder(item) {
            if (!this.textarea || this.triggerIndex === null || typeof this.triggerIndex !== 'number') {
                return;
            }

            const label = (item.displayName || item.id || '')
                .replace(/[\r\n\[\]]+/g, ' ')
                .trim();
            const safeLabel = label.length > 0 ? label : item.id;
            const insertion = `@${safeLabel}`;
            const placeholder = `${insertion} `;
            const start = this.triggerIndex;
            const end = this.textarea.selectionEnd ?? this.textarea.value.length;
            const before = this.textarea.value.slice(0, start);
            const after = this.textarea.value.slice(end);
            this.textarea.value = `${before}${placeholder}${after}`;
            const caret = before.length + placeholder.length;
            this.textarea.setSelectionRange(caret, caret);
            this.textarea.dispatchEvent(new Event('input', { bubbles: true }));
            if (this.registerMention) {
                this.registerMention({ id: item.id, label: safeLabel });
            }
            this.onInsert();
        }

        hide() {
            if (this.visible) {
                this.visible = false;
                this.container.style.display = 'none';
            }

            this.items = [];
            this.activeIndex = -1;
            this.triggerIndex = null;
            this.currentQuery = '';
        }

        positionMenu() {
            if (!this.textarea) {
                return;
            }

            const rect = this.textarea.getBoundingClientRect();
            this.container.style.minWidth = `${rect.width}px`;
            this.container.style.left = `${rect.left + global.scrollX}px`;
            this.container.style.top = `${rect.bottom + global.scrollY}px`;
        }

        getTriggerContext() {
            if (!this.textarea) {
                return null;
            }

            const caret = this.textarea.selectionStart;
            if (typeof caret !== 'number') {
                return null;
            }

            const value = this.textarea.value;
            const before = value.slice(0, caret);
            const atIndex = before.lastIndexOf('@');
            if (atIndex === -1) {
                return null;
            }

            if (atIndex > 0) {
                const preceding = before[atIndex - 1];
                if (preceding && !/[\s\n\t\r]/.test(preceding)) {
                    return null;
                }
            }

            const query = before.slice(atIndex + 1);
            if (/[\s\n\t\r]/.test(query)) {
                return null;
            }

            if (query.includes('](user:')) {
                return null;
            }

            return { index: atIndex, query };
        }
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
            const parsedPageSize = Number.parseInt(this.config.pageSize, 10);
            this.pageSize = Number.isFinite(parsedPageSize) && parsedPageSize > 0 ? parsedPageSize : 20;
            this.timeZone = this.config.timeZone || 'Asia/Kolkata';
            this.today = this.config.today || new Date().toISOString().slice(0, 10);
            const initialPageSource = typeof this.config.initialPage !== 'undefined' && this.config.initialPage !== null
                ? this.config.initialPage
                : (this.root?.getAttribute ? this.root.getAttribute('data-initial-page') : null);
            this.initialPage = this.resolveInitialPage(initialPageSource);
            this.state = {
                type: 'all',
                timeRange: 'all',
                includeDeleted: false,
                page: this.initialPage,
                total: 0,
                totalPages: 1,
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
            this.mentionAutocompletes = new WeakMap();
            this.mentionMaps = new WeakMap();
            this.editMentionMap = null;
            this.mentionEndpoint = '/api/users/mentions';
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
            this.paginationContainer = this.root.querySelector('[data-remarks-pagination]');
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

            if (this.paginationContainer) {
                this.paginationContainer.addEventListener('click', (event) => {
                    const targetElement = event.target instanceof HTMLElement ? event.target.closest('[data-remarks-page]') : null;
                    if (!targetElement) {
                        return;
                    }

                    event.preventDefault();
                    const pageValue = Number.parseInt(targetElement.getAttribute('data-remarks-page') || '', 10);
                    if (!Number.isFinite(pageValue)) {
                        return;
                    }

                    this.goToPage(pageValue);
                });
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

                if (this.bodyField) {
                    this.attachMentionAutocomplete(this.bodyField, () => this.validateComposer());
                }
            }
        }

        async ensureLoaded() {
            if (this.state.initialised) {
                return;
            }

            const initialPage = this.initialPage > 0 ? this.initialPage : 1;
            this.state.page = initialPage;
            await this.fetchPage(initialPage, false);
        }

        attachMentionAutocomplete(textarea, onInsert) {
            if (!textarea || this.mentionAutocompletes.has(textarea)) {
                return;
            }

            this.ensureMentionMap(textarea);
            const autocomplete = new RemarkMentionAutocomplete(textarea, {
                search: (term) => this.searchMentions(term),
                onInsert: typeof onInsert === 'function' ? onInsert : () => { },
                registerMention: (mention) => this.registerMention(textarea, mention)
            });

            this.mentionAutocompletes.set(textarea, autocomplete);
        }

        ensureMentionMap(textarea, initialMap = null) {
            if (!textarea) {
                return null;
            }

            if (initialMap && typeof initialMap.set === 'function') {
                this.mentionMaps.set(textarea, initialMap);
                return initialMap;
            }

            if (this.mentionMaps.has(textarea)) {
                return this.mentionMaps.get(textarea);
            }

            const map = new Map();
            this.mentionMaps.set(textarea, map);
            return map;
        }

        clearMentionMap(textarea) {
            if (!textarea) {
                return;
            }

            const map = this.mentionMaps.get(textarea);
            if (map) {
                map.clear();
            }
        }

        registerMention(textarea, mention) {
            if (!mention) {
                return;
            }

            const map = this.ensureMentionMap(textarea);
            if (!map) {
                return;
            }

            this.registerMentionInMap(map, mention);
        }

        registerMentionInMap(map, mention) {
            if (!map || typeof map.set !== 'function' || !mention) {
                return;
            }

            const id = mention.id ? mention.id.toString().trim() : '';
            const label = mention.label ? mention.label.toString().trim() : '';
            if (!id || !label) {
                return;
            }

            map.set(label, { id, label });
        }

        serializeTextWithMentions(textarea, value, fallbackMap = null) {
            const sourceText = typeof value === 'string'
                ? value
                : (textarea && 'value' in textarea ? textarea.value : '');
            if (!sourceText) {
                return '';
            }

            const map = textarea ? this.mentionMaps.get(textarea) : null;
            const mentionMap = map || (fallbackMap && typeof fallbackMap.get === 'function' ? fallbackMap : null);
            if (!mentionMap || mentionMap.size === 0) {
                return sourceText;
            }

            const entries = Array.from(mentionMap.values())
                .filter((entry) => entry && entry.id && entry.label)
                .sort((a, b) => b.label.length - a.label.length);

            let text = sourceText;
            entries.forEach((entry) => {
                const result = this.replaceMentionInText(text, entry.label, entry.id);
                text = result.text;
            });

            return text;
        }

        replaceMentionInText(text, label, id) {
            if (!text || !label || !id) {
                return { text, count: 0 };
            }

            const needle = `@${label}`;
            let index = text.indexOf(needle);
            if (index === -1) {
                return { text, count: 0 };
            }

            let result = '';
            let lastIndex = 0;
            let replacements = 0;

            while (index !== -1) {
                const beforeIndex = index - 1;
                const beforeChar = beforeIndex >= 0 ? text.charAt(beforeIndex) : '';
                if (beforeChar && !/[\s([{\"'`]/.test(beforeChar)) {
                    result += text.slice(lastIndex, index + needle.length);
                    lastIndex = index + needle.length;
                    index = text.indexOf(needle, lastIndex);
                    continue;
                }

                const afterIndex = index + needle.length;
                const afterChar = afterIndex < text.length ? text.charAt(afterIndex) : '';
                if (afterChar && /[A-Za-z0-9_]/.test(afterChar)) {
                    result += text.slice(lastIndex, index + needle.length);
                    lastIndex = index + needle.length;
                    index = text.indexOf(needle, lastIndex);
                    continue;
                }

                result += text.slice(lastIndex, index);
                result += `@[${label}](user:${id})`;
                lastIndex = afterIndex;
                replacements += 1;
                index = text.indexOf(needle, lastIndex);
            }

            if (replacements === 0) {
                return { text, count: 0 };
            }

            result += text.slice(lastIndex);
            return { text: result, count: replacements };
        }

        async searchMentions(query) {
            const trimmed = (query || '').trim();
            if (!trimmed) {
                return [];
            }

            const params = new URLSearchParams();
            params.set('q', trimmed);
            params.set('limit', '8');

            try {
                const response = await fetch(`${this.mentionEndpoint}?${params.toString()}`, {
                    headers: { Accept: 'application/json' }
                });

                if (!response.ok) {
                    return [];
                }

                const data = await response.json();
                if (!Array.isArray(data)) {
                    return [];
                }

                return data
                    .map((item) => ({
                        id: item.id || item.userId || '',
                        displayName: item.displayName || item.name || item.fullName || item.id || '',
                        initials: item.initials || ''
                    }))
                    .filter((item) => item.id);
            } catch (error) {
                return [];
            }
        }

        decorateMentions(container) {
            if (!container) {
                return;
            }

            const spans = container.querySelectorAll('span.remark-mention[data-user-id]');
            spans.forEach((span) => {
                span.classList.add('remark-mention-highlight');
            });
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

        resolveInitialPage(value) {
            if (typeof value === 'number') {
                if (Number.isFinite(value) && value > 0) {
                    return Math.floor(value);
                }

                return 1;
            }

            if (typeof value !== 'string') {
                return 1;
            }

            const trimmed = value.trim();
            if (trimmed.length === 0) {
                return 1;
            }

            const parsed = Number.parseInt(trimmed, 10);
            return Number.isFinite(parsed) && parsed > 0 ? parsed : 1;
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

            const requestedPage = Number.parseInt(page, 10);
            const targetPage = Number.isFinite(requestedPage) && requestedPage > 0 ? requestedPage : 1;
            const wasInitialised = this.state.initialised;

            if (!this.actorRole) {
                if (!append) {
                    this.state.items = [];
                    this.state.page = targetPage;
                    this.state.total = 0;
                    this.state.totalPages = 1;
                    this.state.hasMore = false;
                    if (this.listContainer) {
                        this.listContainer.innerHTML = '';
                    }

                    if (this.emptyState) {
                        this.emptyState.classList.remove('d-none');
                        this.emptyState.textContent = 'Remarks are unavailable for your account.';
                    }

                    if (this.paginationContainer) {
                        this.paginationContainer.innerHTML = '';
                        this.paginationContainer.classList.add('d-none');
                    }
                }

                this.state.initialised = true;
                return false;
            }

            if (!append) {
                this.setLoading(true);
            }
            this.state.loading = true;

            const params = this.buildQueryParams(targetPage);
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

                if (Number.isFinite(totalNumber) && totalNumber >= 0) {
                    this.state.total = totalNumber;
                } else if (!append) {
                    this.state.total = this.state.items.length;
                }

                const effectivePage = Number.isFinite(pageNumber) && pageNumber > 0 ? pageNumber : targetPage;
                this.state.page = effectivePage;

                const computedTotalPages = this.pageSize > 0
                    ? Math.max(1, Math.ceil(this.state.total / this.pageSize))
                    : 1;
                this.state.totalPages = computedTotalPages;
                this.state.hasMore = this.state.page < this.state.totalPages;
                this.state.initialised = true;
                this.renderList();
                this.dispatchPageChanged(!wasInitialised && !append);
                return true;
            } catch (error) {
                this.toastHandler('Unable to load remarks.', 'danger');
                this.state.initialised = wasInitialised;
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
                this.renderPagination();
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

            this.renderPagination();
        }

        renderPagination() {
            if (!this.paginationContainer) {
                return;
            }

            const totalCount = Number.isFinite(this.state.total) ? this.state.total : 0;
            const pageSize = this.pageSize > 0 ? this.pageSize : 1;
            const totalPages = Number.isFinite(this.state.totalPages) && this.state.totalPages > 0
                ? Math.max(1, Math.floor(this.state.totalPages))
                : Math.max(1, Math.ceil(totalCount / pageSize));
            const currentPage = Number.isFinite(this.state.page) && this.state.page > 0 ? Math.floor(this.state.page) : 1;

            if (totalPages <= 1) {
                this.paginationContainer.classList.add('d-none');
                this.paginationContainer.innerHTML = '';
                return;
            }

            this.paginationContainer.classList.remove('d-none');
            this.paginationContainer.innerHTML = '';

            const list = document.createElement('ul');
            list.className = 'pagination pagination-sm mb-0';

            const addItem = (label, pageNumber, disabled, active, ariaLabel) => {
                const item = document.createElement('li');
                item.className = 'page-item';
                if (disabled) {
                    item.classList.add('disabled');
                }
                if (active) {
                    item.classList.add('active');
                }

                const link = document.createElement('a');
                link.className = 'page-link';
                link.href = '#';
                link.textContent = label;
                if (ariaLabel) {
                    link.setAttribute('aria-label', ariaLabel);
                }

                if (active) {
                    link.setAttribute('aria-current', 'page');
                }

                if (disabled) {
                    link.setAttribute('tabindex', '-1');
                    link.setAttribute('aria-disabled', 'true');
                } else {
                    link.setAttribute('data-remarks-page', String(pageNumber));
                }

                item.appendChild(link);
                list.appendChild(item);
            };

            const addEllipsis = () => {
                const item = document.createElement('li');
                item.className = 'page-item disabled';
                const span = document.createElement('span');
                span.className = 'page-link';
                span.innerHTML = '&hellip;';
                span.setAttribute('aria-hidden', 'true');
                item.appendChild(span);
                list.appendChild(item);
            };

            addItem('‹', currentPage - 1, currentPage <= 1, false, 'Previous page');

            const windowSize = 5;
            let start = Math.max(1, currentPage - Math.floor(windowSize / 2));
            let end = Math.min(totalPages, start + windowSize - 1);
            if (end - start + 1 < windowSize) {
                start = Math.max(1, end - windowSize + 1);
            }

            if (start > 1) {
                addItem('1', 1, false, currentPage === 1);
                if (start > 2) {
                    addEllipsis();
                }
            }

            for (let pageNumber = start; pageNumber <= end; pageNumber += 1) {
                addItem(String(pageNumber), pageNumber, false, currentPage === pageNumber);
            }

            if (end < totalPages) {
                if (end < totalPages - 1) {
                    addEllipsis();
                }

                addItem(String(totalPages), totalPages, false, currentPage === totalPages);
            }

            addItem('›', currentPage + 1, currentPage >= totalPages, false, 'Next page');

            this.paginationContainer.appendChild(list);
        }

        goToPage(pageNumber) {
            if (this.state.loading) {
                return;
            }

            const parsed = Number.parseInt(pageNumber, 10);
            if (!Number.isFinite(parsed) || parsed <= 0) {
                return;
            }

            const totalPages = Number.isFinite(this.state.totalPages) && this.state.totalPages > 0
                ? Math.floor(this.state.totalPages)
                : Math.max(1, Math.ceil((Number.isFinite(this.state.total) ? this.state.total : 0) / (this.pageSize > 0 ? this.pageSize : 1)));
            const clamped = Math.min(Math.max(parsed, 1), totalPages);
            if (Number.isFinite(this.state.page) && Math.floor(this.state.page) === clamped) {
                return;
            }

            this.fetchPage(clamped, false);
        }

        dispatchPageChanged(initialLoad) {
            if (!this.root) {
                return;
            }

            const detail = {
                page: this.state.page,
                total: this.state.total,
                pageSize: this.pageSize,
                totalPages: this.state.totalPages,
                initialLoad: !!initialLoad
            };

            if (typeof window !== 'undefined' && typeof window.CustomEvent === 'function') {
                const event = new CustomEvent('remarks:page-changed', { detail, bubbles: true });
                this.root.dispatchEvent(event);
                return;
            }

            if (typeof document !== 'undefined' && typeof document.createEvent === 'function') {
                const legacyEvent = document.createEvent('CustomEvent');
                legacyEvent.initCustomEvent('remarks:page-changed', true, false, detail);
                this.root.dispatchEvent(legacyEvent);
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
            roleBadge.className = 'badge border remarks-role-badge';
            const roleAccentClass = this.getRoleAccentClass(remark.authorRole);
            if (roleAccentClass) {
                roleBadge.classList.add(roleAccentClass);
                article.classList.add(roleAccentClass);
            } else {
                roleBadge.classList.add('bg-light', 'text-dark');
            }
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
                const existingMap = this.editingId === remark.id ? this.editMentionMap : null;
                const mentionMap = this.ensureMentionMap(textarea, existingMap || null);
                const initialValue = this.editDraft || this.decodeBody(remark.body, mentionMap);
                textarea.value = initialValue;
                textarea.setAttribute('data-remark-edit', 'body');
                textarea.addEventListener('input', () => {
                    this.editDraft = textarea.value;
                });
                body.appendChild(textarea);
                this.attachMentionAutocomplete(textarea);
            } else {
                body.innerHTML = remark.body || '';
                this.decorateMentions(body);
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

        getRoleAccentClass(role) {
            if (!role) {
                return '';
            }

            const canonical = this.resolveCanonicalRole(role);
            const normalized = this.normalizeRoleKey(canonical || role);
            if (normalized === 'comdt') {
                return 'remarks-role-comdt';
            }

            if (normalized === 'hod') {
                return 'remarks-role-hod';
            }

            if (normalized === 'mco') {
                return 'remarks-role-mco';
            }

            return '';
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

        decodeBody(bodyHtml, mentionMap = null) {
            if (!bodyHtml) {
                return '';
            }

            const doc = this.root?.ownerDocument || global.document;
            const temp = doc.createElement('div');
            temp.innerHTML = bodyHtml;

            const mentions = temp.querySelectorAll('span.remark-mention[data-user-id]');
            mentions.forEach((span) => {
                const userId = span.getAttribute('data-user-id');
                const label = (span.textContent || '').replace(/[\r\n]+/g, ' ').trim();
                if (!userId) {
                    return;
                }

                const placeholderLabel = label.replace(/[\[\]]+/g, ' ').trim() || userId;
                const visible = `@${placeholderLabel}`;
                if (mentionMap && typeof mentionMap.set === 'function') {
                    this.registerMentionInMap(mentionMap, { id: userId, label: placeholderLabel });
                }
                const textNode = doc.createTextNode(visible);
                span.replaceWith(textNode);
            });

            const normalised = temp.innerHTML
                .replace(/<br\s*\/?>/gi, '\n')
                .replace(/<\/p>\s*<p>/gi, '\n\n');

            const container = doc.createElement('div');
            container.innerHTML = normalised;
            const text = container.textContent || container.innerText || '';
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
            this.editMentionMap = new Map();
            this.editDraft = this.decodeBody(remark.body, this.editMentionMap);
            this.renderList();
        }

        cancelEdit() {
            this.editingId = null;
            this.editDraft = '';
            this.editMentionMap = null;
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
            const rawValue = textarea && 'value' in textarea ? textarea.value : this.editDraft;
            const mentionMap = textarea ? this.mentionMaps.get(textarea) : (this.editingId === remarkId ? this.editMentionMap : null);
            const serializedBody = this.serializeTextWithMentions(textarea, rawValue, mentionMap);
            const bodyText = serializedBody.trim();

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
                this.editMentionMap = null;
                if (textarea) {
                    this.clearMentionMap(textarea);
                }
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
                this.clearMentionMap(this.bodyField);
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

            const serialized = this.serializeTextWithMentions(this.bodyField);
            const body = serialized.trim();
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
                this.state.page = 1;
                await this.fetchPage(1, false);
            } catch (error) {
                this.setFeedback('Unable to add remark.', 'danger');
            }
        }
    }



    const namespace = Object.assign({}, global.ProjectRemarks);
    if (typeof namespace.showToast !== 'function') {
        namespace.showToast = showToast;
    }
    namespace.RemarksPanel = RemarksPanel;
    namespace.createRemarksPanel = function createRemarksPanel(element, toast) {
        const handler = typeof toast === 'function' ? toast : namespace.showToast;
        return new RemarksPanel(element, handler);
    };

    global.ProjectRemarks = namespace;
}(typeof window !== 'undefined' ? window : null));
