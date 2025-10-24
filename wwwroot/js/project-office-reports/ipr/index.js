(() => {
    const offcanvasElement = document.getElementById('iprRecordOffcanvas');
    if (!offcanvasElement) {
        return;
    }

    const mode = (offcanvasElement.getAttribute('data-ipr-mode') || '').toLowerCase();
    const hasForm = offcanvasElement.getAttribute('data-ipr-has-form') === 'true';
    const inlineActive = offcanvasElement.getAttribute('data-ipr-inline-active') === 'true';
    const triggers = Array.from(document.querySelectorAll('[data-ipr-offcanvas-trigger]'));

    const ensureInlineVisibility = () => {
        if (hasForm && (mode === 'create' || mode === 'edit')) {
            offcanvasElement.classList.add('show', 'position-static', 'border', 'shadow-sm', 'mb-4');
            offcanvasElement.style.visibility = 'visible';
            offcanvasElement.setAttribute('data-ipr-inline-active', 'true');
            offcanvasElement.setAttribute('aria-hidden', 'false');
        }
    };

    if (typeof bootstrap === 'undefined' || !bootstrap.Offcanvas) {
        ensureInlineVisibility();
        return;
    }

    if (inlineActive) {
        offcanvasElement.classList.remove('position-static', 'border', 'shadow-sm', 'mb-4');
        offcanvasElement.style.removeProperty('visibility');
        offcanvasElement.setAttribute('data-ipr-inline-active', 'false');
        offcanvasElement.setAttribute('aria-hidden', 'true');
        if (!offcanvasElement.getAttribute('style')) {
            offcanvasElement.removeAttribute('style');
        }
    }

    const offcanvasInstance = bootstrap.Offcanvas.getOrCreateInstance(offcanvasElement);

    const supportsUrlApi = typeof URL === 'function' && URL.prototype && 'searchParams' in URL.prototype;

    const parseSearchParams = () => {
        const params = {};
        const search = window.location.search ? window.location.search.substring(1) : '';
        if (!search) {
            return params;
        }

        search.split('&').forEach(part => {
            if (!part) {
                return;
            }

            const [rawKey, rawValue = ''] = part.split('=');
            const key = decodeURIComponent(rawKey.replace(/\+/g, ' '));
            const value = decodeURIComponent(rawValue.replace(/\+/g, ' '));
            params[key] = value;
        });

        return params;
    };

    const getQueryParam = key => {
        if (supportsUrlApi) {
            const currentUrl = new URL(window.location.href);
            return currentUrl.searchParams.get(key);
        }

        const params = parseSearchParams();
        return Object.prototype.hasOwnProperty.call(params, key) ? params[key] : null;
    };

    const buildUrlWithUpdates = updates => {
        if (supportsUrlApi) {
            const url = new URL(window.location.href);
            Object.entries(updates).forEach(([key, value]) => {
                if (value === null) {
                    url.searchParams.delete(key);
                } else {
                    url.searchParams.set(key, value);
                }
            });
            return url.toString();
        }

        const location = window.location;
        const origin = location.origin || `${location.protocol}//${location.host}`;
        const base = `${origin}${location.pathname}`;
        const params = parseSearchParams();

        Object.entries(updates).forEach(([key, value]) => {
            if (value === null) {
                delete params[key];
            } else {
                params[key] = value;
            }
        });

        const query = Object.keys(params)
            .map(paramKey => `${encodeURIComponent(paramKey)}=${encodeURIComponent(params[paramKey])}`)
            .join('&');

        const hash = location.hash || '';
        return query ? `${base}?${query}${hash}` : `${base}${hash}`;
    };

    const updateTriggerStates = (activeMode, activeId) => {
        const normalizedMode = (activeMode || '').toLowerCase();
        const normalizedId = activeId ?? '';

        triggers.forEach(trigger => {
            const triggerMode = (trigger.dataset.iprOffcanvasTrigger || '').toLowerCase();
            const triggerId = trigger.dataset.iprRecordId ?? '';

            let expanded = false;

            if (normalizedMode === 'create' && triggerMode === 'create') {
                expanded = true;
            } else if (normalizedMode === 'edit' && triggerMode === 'edit') {
                expanded = normalizedId !== '' && triggerId === normalizedId;
            }

            trigger.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        });
    };

    if (triggers.length > 0) {
        const initialMode = (getQueryParam('mode') || '').toLowerCase();
        const initialId = getQueryParam('id') ?? '';
        updateTriggerStates(initialMode, initialId);

        triggers.forEach(trigger => {
            trigger.addEventListener('click', event => {
                const triggerMode = (trigger.dataset.iprOffcanvasTrigger || '').toLowerCase();
                if (triggerMode !== 'create' && triggerMode !== 'edit') {
                    return;
                }

                event.preventDefault();

                const updates = { mode: triggerMode };

                if (triggerMode === 'edit') {
                    const triggerId = trigger.dataset.iprRecordId;
                    if (!triggerId) {
                        return;
                    }

                    updates.id = triggerId;
                    updateTriggerStates(triggerMode, triggerId);
                } else {
                    updates.id = null;
                    updateTriggerStates(triggerMode, '');
                }

                window.location.assign(buildUrlWithUpdates(updates));
            });
        });
    }

    if (hasForm && (mode === 'create' || mode === 'edit')) {
        offcanvasInstance.show();
    }

    offcanvasElement.addEventListener('hidden.bs.offcanvas', () => {
        const updates = { mode: null, id: null };
        const nextUrl = buildUrlWithUpdates(updates);

        if (typeof window.history !== 'undefined' && typeof window.history.replaceState === 'function') {
            window.history.replaceState({}, document.title, nextUrl);
        } else {
            window.location.assign(nextUrl);
        }
        updateTriggerStates('', '');
    });
})();

(() => {
    const formIds = ['iprCreateForm', 'iprEditForm'];
    const forms = formIds
        .map(id => document.getElementById(id))
        .filter(form => form !== null);

    if (forms.length === 0) {
        return;
    }

    const scrollToFirstInvalid = form => {
        const firstInvalid = form.querySelector(':invalid');
        if (firstInvalid) {
            firstInvalid.scrollIntoView({ block: 'center' });
        }
    };

    forms.forEach(form => {
        form.addEventListener('invalid', () => {
            window.setTimeout(() => {
                scrollToFirstInvalid(form);
            }, 0);
        }, true);
    });
})();

(() => {
    const filterModalElement = document.getElementById('iprFilterModal');
    const filterTrigger = document.querySelector('[data-ipr-filter-trigger]');
    if (!filterModalElement || !filterTrigger || typeof bootstrap === 'undefined' || !bootstrap.Modal) {
        return;
    }

    filterModalElement.addEventListener('show.bs.modal', () => {
        filterTrigger.setAttribute('aria-expanded', 'true');
    });

    filterModalElement.addEventListener('shown.bs.modal', () => {
        const initialFocus = filterModalElement.querySelector('[data-ipr-filter-initial-focus]');
        if (initialFocus) {
            initialFocus.focus();
        }
    });

    filterModalElement.addEventListener('hidden.bs.modal', () => {
        filterTrigger.setAttribute('aria-expanded', 'false');
        filterTrigger.focus();
    });
})();

(() => {
    const confirmForms = document.querySelectorAll('form[data-ipr-confirm]');
    if (confirmForms.length === 0) {
        return;
    }

    confirmForms.forEach(form => {
        form.addEventListener('submit', event => {
            const message = form.getAttribute('data-ipr-confirm') || 'Are you sure?';

            if (!window.confirm(message)) {
                event.preventDefault();
                event.stopImmediatePropagation();
            }
        });
    });
})();
