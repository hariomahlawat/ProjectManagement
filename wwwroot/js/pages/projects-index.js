(() => {
    'use strict';

    const root = document.querySelector('[data-projects-page]');
    if (!root) return;

    const form = root.querySelector('[data-project-filter-form]');
    const search = root.querySelector('[data-project-search]');
    const liveStatus = root.querySelector('[data-project-live-status]');
    const searchActivity = root.querySelector('[data-project-search-activity]');
    const viewButtons = root.querySelectorAll('[data-project-view]');
    const storageKey = 'prism.projects.view';
    const debounceMilliseconds = 300;
    const replaceableSelectors = [
        '[data-project-header-summary]',
        '[data-project-lifecycle-region]',
        '[data-project-results-region]',
        '[data-project-live-metadata]'
    ];

    if (!form || !search) return;

    let searchTimer = 0;
    let isComposing = false;
    let activeController = null;
    let requestSequence = 0;
    let preferredView = readPreferredView();

    const supportsLiveSearch =
        typeof window.fetch === 'function' &&
        typeof window.AbortController === 'function' &&
        typeof window.DOMParser === 'function' &&
        typeof window.URL === 'function' &&
        typeof window.URLSearchParams === 'function' &&
        typeof window.history?.replaceState === 'function';

    function readPreferredView() {
        try {
            return window.localStorage.getItem(storageKey) || 'cards';
        } catch {
            return 'cards';
        }
    }

    function setView(view, persist = true) {
        const resolved = view === 'table' ? 'table' : 'cards';
        preferredView = resolved;

        viewButtons.forEach(button => {
            const isActive = button.dataset.projectView === resolved;
            button.classList.toggle('is-active', isActive);
            button.setAttribute('aria-pressed', String(isActive));
        });

        root.querySelectorAll('[data-project-results]').forEach(container => {
            const isActive = container.dataset.projectResults === resolved;
            container.classList.toggle('is-active', isActive);
            container.hidden = !isActive;
        });

        if (!persist) return;

        try {
            window.localStorage.setItem(storageKey, resolved);
        } catch {
            // Local storage is an optional enhancement.
        }
    }

    function submitNormally() {
        window.clearTimeout(searchTimer);
        HTMLFormElement.prototype.submit.call(form);
    }

    function formToPublicUrl({ resetPage = false } = {}) {
        const url = new URL(form.action || window.location.href, window.location.href);
        url.search = '';
        url.hash = '';

        const checkboxNames = new Set(
            Array.from(form.elements)
                .filter(element => element instanceof HTMLInputElement && element.type === 'checkbox' && element.name)
                .map(element => element.name)
        );

        const params = new URLSearchParams();
        Array.from(form.elements).forEach(element => {
            if (!(element instanceof HTMLElement) || !('name' in element)) return;
            if (!element.name || element.disabled) return;

            const type = (element.type || '').toLowerCase();
            if (['submit', 'button', 'reset', 'file'].includes(type)) return;
            if (type === 'hidden' && checkboxNames.has(element.name)) return;

            if ((type === 'checkbox' || type === 'radio') && !element.checked) return;

            const value = typeof element.value === 'string' ? element.value.trim() : '';
            if (!value || value.toLowerCase() === 'false') return;
            params.set(element.name, value);
        });

        if (resetPage) {
            params.delete('p');
        }

        if ((params.get('Sort') || '').toLowerCase() === 'operational') {
            params.delete('Sort');
            params.delete('Dir');
        }

        if (params.get('p') === '1') {
            params.delete('p');
        }

        params.delete('handler');
        url.search = params.toString();
        return url;
    }

    function createLiveRequestUrl(publicUrl) {
        const requestUrl = new URL(publicUrl.toString());
        requestUrl.searchParams.set('handler', 'Live');
        return requestUrl;
    }

    function setLoading(isLoading) {
        root.classList.toggle('is-live-loading', isLoading);
        root.setAttribute('aria-busy', String(isLoading));
        search.setAttribute('aria-busy', String(isLoading));

        const results = root.querySelector('[data-project-results-region]');
        if (results) {
            results.setAttribute('aria-busy', String(isLoading));
        }

        if (searchActivity) {
            searchActivity.hidden = !isLoading;
        }
    }

    function announce(message) {
        if (!liveStatus) return;
        liveStatus.textContent = '';
        window.requestAnimationFrame(() => {
            liveStatus.textContent = message;
        });
    }

    function replaceLiveFragments(documentFragment) {
        const replacements = replaceableSelectors.map(selector => {
            const current = root.querySelector(selector);
            const incoming = documentFragment.querySelector(selector);
            if (!current || !incoming) {
                throw new Error(`Live project response is missing ${selector}.`);
            }
            return { current, incoming };
        });

        replacements.forEach(({ current, incoming }) => {
            current.replaceWith(document.importNode(incoming, true));
        });
    }

    function getChoiceLabel(input) {
        const label = input.closest('label');
        const source = label?.querySelector('span');
        if (!source) return '';

        const clone = source.cloneNode(true);
        clone.querySelectorAll('small').forEach(node => node.remove());
        return clone.textContent.trim();
    }

    function selectedOptionText(name) {
        const select = form.elements.namedItem(name);
        if (!(select instanceof HTMLSelectElement) || !select.value) return '';
        return select.selectedOptions[0]?.textContent?.trim() || '';
    }

    function updateFilterSummary() {
        const chips = [];
        const query = search.value.trim();
        if (query) chips.push(`Search: ${query}`);

        const selectFilters = [
            ['CategoryId', 'Category'],
            ['TechnicalCategoryId', 'Technical category'],
            ['HodUserId', 'HoD'],
            ['LeadPoUserId', 'Project Officer'],
            ['CompletedYear', 'Completion year'],
            ['TotStatus', 'ToT']
        ];

        selectFilters.forEach(([name, label]) => {
            const value = selectedOptionText(name);
            if (value) chips.push(`${label}: ${value}`);
        });

        const unclassified = form.querySelector('[data-project-type-unclassified]');
        const selectedType = form.querySelector('[data-project-type]:checked');
        if (unclassified instanceof HTMLInputElement && unclassified.checked) {
            chips.push('Project type: Unclassified');
        } else if (selectedType instanceof HTMLInputElement && selectedType.value) {
            const label = getChoiceLabel(selectedType);
            if (label) chips.push(`Project type: ${label}`);
        }

        const selectedBuild = form.querySelector('input[name="Build"]:checked');
        if (selectedBuild instanceof HTMLInputElement && selectedBuild.value) {
            chips.push(selectedBuild.value.toLowerCase() === 'repeat' ? 'Repeat build' : 'New development');
        }

        const includeArchived = form.querySelector('input[type="checkbox"][name="IncludeArchived"]');
        if (includeArchived instanceof HTMLInputElement && includeArchived.checked) {
            chips.push('Archived included');
        }

        const summary = root.querySelector('[data-project-filter-summary]');
        const chipHost = root.querySelector('[data-project-filter-chips]');
        const countHost = root.querySelector('[data-project-filter-count-host]');

        if (summary) summary.classList.toggle('d-none', chips.length === 0);

        if (chipHost) {
            chipHost.replaceChildren(...chips.map(text => {
                const chip = document.createElement('span');
                chip.className = 'projects-filter-chip';
                chip.textContent = text;
                return chip;
            }));
        }

        if (countHost) {
            countHost.replaceChildren();
            if (chips.length > 0) {
                const count = document.createElement('span');
                count.className = 'projects-toolbar__filter-count';
                count.dataset.projectFilterCount = '';
                count.textContent = String(chips.length);
                countHost.append(count);
            }
        }

        const clearLink = root.querySelector('[data-project-clear-filters]');
        if (clearLink instanceof HTMLAnchorElement) {
            const clearUrl = new URL(form.action || window.location.href, window.location.href);
            clearUrl.search = '';
            const lifecycle = form.elements.namedItem('Lifecycle');
            if (lifecycle && typeof lifecycle.value === 'string' && lifecycle.value) {
                clearUrl.searchParams.set('Lifecycle', lifecycle.value);
            }
            clearLink.href = clearUrl.toString();
        }
    }

    function updateFilterCounts() {
        const metadata = root.querySelector('[data-project-live-metadata]');
        if (!(metadata instanceof HTMLElement)) return;

        root.querySelectorAll('[data-project-build-count]').forEach(element => {
            const build = element.dataset.projectBuildCount;
            const count = build === 'Repeat'
                ? metadata.dataset.repeatBuildCount
                : metadata.dataset.newBuildCount;
            element.textContent = count || '0';
        });

        const unclassified = root.querySelector('[data-project-type-unclassified-count]');
        if (unclassified) {
            unclassified.textContent = metadata.dataset.unclassifiedCount || '0';
        }

        const counts = new Map();
        metadata.querySelectorAll('[data-project-type-count-value]').forEach(element => {
            counts.set(element.dataset.projectTypeCountValue, element.dataset.count || '0');
        });

        root.querySelectorAll('[data-project-type-count-display]').forEach(element => {
            element.textContent = counts.get(element.dataset.projectTypeCountDisplay) || '0';
        });
    }

    function initialiseCoverImages() {
        root.querySelectorAll('[data-project-card-cover-image]').forEach(image => {
            if (!(image instanceof HTMLImageElement) || image.dataset.projectCoverInitialised === 'true') return;
            image.dataset.projectCoverInitialised = 'true';

            const host = image.closest('[data-project-card-cover]');
            if (!host) return;

            const showFallback = () => {
                image.hidden = true;
                image.removeAttribute('srcset');
                host.classList.add('project-card__visual--icon');
            };

            image.addEventListener('error', showFallback, { once: true });
            if (image.complete && image.naturalWidth === 0) {
                showFallback();
            }
        });
    }

    function closeFilterDrawer() {
        const drawer = root.querySelector('#projectFilters');
        if (!drawer || !window.bootstrap?.Offcanvas) return;
        const instance = window.bootstrap.Offcanvas.getInstance(drawer);
        instance?.hide();
    }

    function normaliseProjectTypeControls() {
        const unclassified = form.querySelector('[data-project-type-unclassified]');
        const clearUnclassified = form.querySelector('[data-clear-unclassified]');
        const selectedType = form.querySelector('[data-project-type]:checked');

        if (unclassified instanceof HTMLInputElement && unclassified.checked) {
            form.querySelectorAll('[data-project-type]').forEach(radio => {
                radio.checked = false;
            });
            if (clearUnclassified instanceof HTMLInputElement) {
                clearUnclassified.checked = false;
            }
        } else if (selectedType instanceof HTMLInputElement && selectedType.checked) {
            if (unclassified instanceof HTMLInputElement) {
                unclassified.checked = false;
            }
        } else if (clearUnclassified instanceof HTMLInputElement) {
            clearUnclassified.checked = true;
        }
    }

    function syncFormFromUrl(url) {
        const params = url.searchParams;
        const radioGroups = new Map();

        Array.from(form.elements).forEach(element => {
            if (!(element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement)) return;
            if (!element.name || element.disabled) return;

            if (element instanceof HTMLInputElement && element.type === 'radio') {
                if (!radioGroups.has(element.name)) radioGroups.set(element.name, []);
                radioGroups.get(element.name).push(element);
                return;
            }

            if (element instanceof HTMLInputElement && element.type === 'checkbox') {
                const values = params.getAll(element.name);
                element.checked = values.some(value => value.toLowerCase() !== 'false');
                return;
            }

            const defaults = {
                p: '1',
                Sort: 'Operational',
                Dir: 'Asc',
                PageSize: '25'
            };
            element.value = params.has(element.name)
                ? params.get(element.name) || ''
                : (defaults[element.name] || '');
        });

        radioGroups.forEach((radios, name) => {
            const selectedValue = params.get(name) || '';
            let matched = false;
            radios.forEach(radio => {
                radio.checked = radio.value === selectedValue;
                matched ||= radio.checked;
            });
            if (!matched) {
                const empty = radios.find(radio => radio.value === '');
                if (empty) empty.checked = true;
            }
        });

        normaliseProjectTypeControls();
        updateFilterSummary();
    }

    function updateHistory(publicUrl, mode) {
        if (mode === 'none') return;
        const relativeUrl = `${publicUrl.pathname}${publicUrl.search}${publicUrl.hash}`;
        if (mode === 'push') {
            window.history.pushState({ prismProjects: true }, '', relativeUrl);
        } else {
            window.history.replaceState({ prismProjects: true }, '', relativeUrl);
        }
    }

    async function refreshProjects(publicUrl, options = {}) {
        const {
            historyMode = 'replace',
            closeFilters = false,
            announceResults = true
        } = options;

        if (!supportsLiveSearch) {
            window.location.assign(publicUrl.toString());
            return false;
        }

        window.clearTimeout(searchTimer);
        activeController?.abort();
        activeController = new AbortController();
        const sequence = ++requestSequence;
        setLoading(true);

        try {
            const response = await window.fetch(createLiveRequestUrl(publicUrl), {
                method: 'GET',
                credentials: 'same-origin',
                cache: 'no-store',
                signal: activeController.signal,
                headers: {
                    Accept: 'text/html',
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            if (!response.ok) {
                throw new Error(`Project search failed with HTTP ${response.status}.`);
            }

            const html = await response.text();
            if (sequence !== requestSequence) return false;

            const documentFragment = new DOMParser().parseFromString(html, 'text/html');
            replaceLiveFragments(documentFragment);
            updateFilterCounts();
            updateFilterSummary();
            setView(preferredView, false);
            initialiseCoverImages();
            updateHistory(publicUrl, historyMode);

            if (closeFilters) closeFilterDrawer();

            if (announceResults) {
                const results = root.querySelector('[data-project-results-region]');
                const count = results?.dataset.projectResultCount || '0';
                const label = results?.dataset.projectResultLabel || 'projects';
                announce(`${count} ${label} found.`);
            }

            return true;
        } catch (error) {
            if (error?.name === 'AbortError') return false;
            console.error('PRISM project live search failed.', error);
            announce('Unable to update project results. Your current results have been retained.');
            return false;
        } finally {
            if (sequence === requestSequence) {
                setLoading(false);
                activeController = null;
            }
        }
    }

    function cancelActiveRequest() {
        if (!activeController) return;

        // Invalidate the in-flight response immediately when the user types
        // again. Waiting for the next debounce interval would allow an older
        // result set to flash briefly while a newer query is already present.
        requestSequence += 1;
        activeController.abort();
        activeController = null;
        setLoading(false);
    }

    function scheduleSearch() {
        if (!supportsLiveSearch || isComposing) return;
        window.clearTimeout(searchTimer);
        cancelActiveRequest();
        searchTimer = window.setTimeout(() => {
            const url = formToPublicUrl({ resetPage: true });
            refreshProjects(url, { historyMode: 'replace' });
        }, debounceMilliseconds);
    }

    function isLiveNavigationClick(event, link) {
        if (event.defaultPrevented || event.button !== 0) return false;
        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return false;
        if (link.target && link.target !== '_self') return false;
        if (link.hasAttribute('download')) return false;
        if (link.closest('.page-item.disabled')) return false;

        const url = new URL(link.href, window.location.href);
        return url.origin === window.location.origin && url.pathname === new URL(form.action, window.location.href).pathname;
    }

    function openProjectRow(row) {
        const href = row.dataset.href;
        if (href) window.location.assign(href);
    }

    // View preference and result-only enhancements are independent of live search.
    viewButtons.forEach(button => {
        button.addEventListener('click', () => setView(button.dataset.projectView));
    });
    setView(preferredView, false);
    initialiseCoverImages();

    root.addEventListener('click', event => {
        const row = event.target.closest('[data-project-row]');
        if (row && !event.target.closest('a, button, input, select, textarea, label')) {
            openProjectRow(row);
            return;
        }

        const link = event.target.closest([
            '.projects-lifecycle__tab',
            '.projects-archive-filter',
            '.projects-sort',
            '.projects-results-header__order-reset',
            '.projects-pagination a.page-link',
            '[data-project-clear-filters]',
            '.projects-empty-state a.btn'
        ].join(','));

        if (!supportsLiveSearch || !(link instanceof HTMLAnchorElement) || !isLiveNavigationClick(event, link)) return;

        event.preventDefault();
        const url = new URL(link.href, window.location.href);
        syncFormFromUrl(url);
        refreshProjects(url, { historyMode: 'push' });
    });

    root.addEventListener('keydown', event => {
        const row = event.target.closest('[data-project-row]');
        if (!row || (event.key !== 'Enter' && event.key !== ' ')) return;
        event.preventDefault();
        openProjectRow(row);
    });

    root.addEventListener('change', event => {
        const target = event.target;

        if (target.matches('[data-project-type]') && target.checked) {
            const unclassified = form.querySelector('[data-project-type-unclassified]');
            if (unclassified) unclassified.checked = false;
        }

        if (target.matches('[data-project-type-unclassified]') && target.checked) {
            form.querySelectorAll('[data-project-type]').forEach(radio => {
                radio.checked = false;
            });
            const clearUnclassified = form.querySelector('[data-clear-unclassified]');
            if (clearUnclassified) clearUnclassified.checked = false;
        }

        if (target.matches('[data-clear-unclassified]') && target.checked) {
            const unclassified = form.querySelector('[data-project-type-unclassified]');
            if (unclassified) unclassified.checked = false;
        }

        if (supportsLiveSearch && target.matches('[data-project-auto-submit]')) {
            const url = formToPublicUrl({ resetPage: true });
            refreshProjects(url, { historyMode: 'push' });
        }
    });

    search.addEventListener('compositionstart', () => {
        isComposing = true;
        window.clearTimeout(searchTimer);
        cancelActiveRequest();
    });

    search.addEventListener('compositionend', () => {
        isComposing = false;
        scheduleSearch();
    });

    search.addEventListener('input', scheduleSearch);

    form.addEventListener('submit', event => {
        if (!supportsLiveSearch) return;
        event.preventDefault();
        const url = formToPublicUrl({ resetPage: true });
        const fromFilterDrawer = event.submitter?.matches('[data-project-filter-apply]') === true;
        refreshProjects(url, {
            historyMode: fromFilterDrawer ? 'push' : 'replace',
            closeFilters: fromFilterDrawer
        });
    });

    window.addEventListener('popstate', () => {
        const url = new URL(window.location.href);
        syncFormFromUrl(url);
        refreshProjects(url, { historyMode: 'none' });
    });

    // Progressive fallback: retain a predictable submit path if a required
    // browser API is unavailable instead of attaching a disruptive live handler.
    if (!supportsLiveSearch) {
        search.removeEventListener('input', scheduleSearch);
        search.addEventListener('change', submitNormally);
        root.querySelectorAll('[data-project-auto-submit]').forEach(control => {
            control.addEventListener('change', submitNormally);
        });
    }
})();
