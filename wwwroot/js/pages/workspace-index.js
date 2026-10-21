(() => {
    'use strict';

    const workspace = document.querySelector('[data-project-officer-workspace]');
    if (!workspace) return;

    const rail = workspace.querySelector('[data-workspace-rail]');
    const toggle = workspace.querySelector('[data-workspace-rail-toggle]');
    const storageKey = 'prism.workspace.navigationExpanded';
    const desktopRail = window.matchMedia('(min-width: 992px)');

    const readNavigationPreference = () => {
        if (!desktopRail.matches) return false;
        try {
            const stored = window.localStorage.getItem(storageKey);
            return stored === null ? true : stored === 'true';
        } catch {
            return true;
        }
    };

    const saveNavigationPreference = (expanded) => {
        try { window.localStorage.setItem(storageKey, String(expanded)); } catch { /* optional storage */ }
    };

    const setRailExpanded = (expanded, options = {}) => {
        const { returnFocus = false, persist = false } = options;
        workspace.classList.toggle('is-nav-expanded', expanded);
        rail?.classList.toggle('is-expanded', expanded);
        toggle?.setAttribute('aria-expanded', String(expanded));
        toggle?.setAttribute('aria-label', expanded ? 'Collapse workspace navigation' : 'Expand workspace navigation');
        toggle?.setAttribute('title', expanded ? 'Collapse workspace navigation' : 'Expand workspace navigation');
        if (persist) saveNavigationPreference(expanded);
        if (returnFocus) toggle?.focus();
    };

    setRailExpanded(readNavigationPreference());

    toggle?.addEventListener('click', () => {
        const expanded = toggle.getAttribute('aria-expanded') !== 'true';
        setRailExpanded(expanded, { persist: desktopRail.matches });
    });

    document.addEventListener('pointerdown', (event) => {
        if (desktopRail.matches || !rail?.classList.contains('is-expanded')) return;
        if (rail.contains(event.target)) return;
        setRailExpanded(false);
    });

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape' || desktopRail.matches || !rail?.classList.contains('is-expanded')) return;
        event.preventDefault();
        setRailExpanded(false, { returnFocus: true });
    });

    rail?.querySelectorAll('a').forEach((link) => {
        link.addEventListener('click', () => {
            if (!desktopRail.matches) setRailExpanded(false);
        });
    });

    desktopRail.addEventListener?.('change', (event) => {
        if (!event.matches) setRailExpanded(false);
        else setRailExpanded(readNavigationPreference());
    });

    const normalize = (value) => (value || '').toLowerCase().trim();
    const hasValue = (input) => normalize(input?.value).length > 0;

    const bindClear = (container, inputs, apply) => {
        const button = container?.querySelector('[data-po-clear-filters]');
        const sync = () => {
            if (button) button.disabled = !inputs.some(hasValue);
        };

        button?.addEventListener('click', () => {
            inputs.forEach((input) => { input.value = ''; });
            apply();
            inputs[0]?.focus();
        });

        return sync;
    };

    const updateEmpty = (container, count) => {
        const page = container?.closest('.po-dedicated-page');
        const empty = page?.querySelector('[data-po-filter-empty]');
        if (empty) empty.hidden = count !== 0;
    };

        const projectFilters = document.querySelector('[data-po-project-filters]');
    if (projectFilters) {
        const search = projectFilters.querySelector('[data-po-project-search]');
        const stage = projectFilters.querySelector('[data-po-project-stage]');
        const attention = projectFilters.querySelector('[data-po-project-attention]');
        const rows = [...document.querySelectorAll('[data-po-project-row]')];
        let syncClear = () => {};

        const apply = () => {
            const query = normalize(search?.value);
            const selectedStage = normalize(stage?.value);
            const selectedAttention = normalize(attention?.value);
            let visible = 0;

            rows.forEach((row) => {
                const show = (!query || normalize(row.dataset.filterText).includes(query))
                    && (!selectedStage || normalize(row.dataset.stage) === selectedStage)
                    && (!selectedAttention || normalize(row.dataset.attention) === selectedAttention);
                row.hidden = !show;
                if (show) visible++;
            });

            updateEmpty(projectFilters, visible);
            syncClear();
        };

        search?.addEventListener('input', apply);
        stage?.addEventListener('change', apply);
        attention?.addEventListener('change', apply);
        syncClear = bindClear(projectFilters, [search, stage, attention].filter(Boolean), apply);
        apply();
    }

            document.querySelectorAll('[data-po-document-tabs]').forEach((tabsRoot) => {
        const tabs = [...tabsRoot.querySelectorAll('[data-document-tab]')];
        const panels = [...tabsRoot.querySelectorAll('[data-document-panel]')];
        const activate = (name, focus = false) => {
            tabs.forEach((tab) => {
                const selected = tab.dataset.documentTab === name;
                tab.setAttribute('aria-selected', String(selected));
                tab.tabIndex = selected ? 0 : -1;
                if (selected && focus) tab.focus();
            });
            panels.forEach((panel) => { panel.hidden = panel.dataset.documentPanel !== name; });
        };

        tabs.forEach((tab, index) => {
            tab.addEventListener('click', () => activate(tab.dataset.documentTab));
            tab.addEventListener('keydown', (event) => {
                let next = null;
                if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
                if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
                if (event.key === 'Home') next = 0;
                if (event.key === 'End') next = tabs.length - 1;
                if (next !== null) {
                    event.preventDefault();
                    activate(tabs[next].dataset.documentTab, true);
                }
            });
        });
    });
})();
