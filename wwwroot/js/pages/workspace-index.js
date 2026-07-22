(() => {
    'use strict';

    const workspace = document.querySelector('[data-project-officer-workspace]');
    if (!workspace) return;

    const rail = workspace.querySelector('[data-workspace-rail]');
    const toggle = workspace.querySelector('[data-workspace-rail-toggle]');
    const storageKey = 'prism.projectOfficerWorkspace.navExpanded';

    const setRailExpanded = (expanded) => {
        workspace.classList.toggle('is-nav-expanded', expanded);
        rail?.classList.toggle('is-expanded', expanded);
        toggle?.setAttribute('aria-expanded', String(expanded));
        toggle?.setAttribute('aria-label', expanded ? 'Collapse workspace navigation' : 'Expand workspace navigation');
        toggle?.setAttribute('title', expanded ? 'Collapse workspace navigation' : 'Expand workspace navigation');
    };

    const desktopRail = window.matchMedia('(min-width: 992px)');
    const mobileRail = window.matchMedia('(max-width: 767.98px)');

    if (desktopRail.matches) {
        setRailExpanded(localStorage.getItem(storageKey) !== 'false');
    } else {
        setRailExpanded(mobileRail.matches);
    }

    toggle?.addEventListener('click', () => {
        const expanded = toggle.getAttribute('aria-expanded') !== 'true';
        setRailExpanded(expanded);
        localStorage.setItem(storageKey, String(expanded));
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

    const actionFilters = document.querySelector('[data-po-action-filters]');
    if (actionFilters) {
        const search = actionFilters.querySelector('[data-po-action-search]');
        const type = actionFilters.querySelector('[data-po-action-type]');
        const groups = [...document.querySelectorAll('[data-po-action-row]')];
        let syncClear = () => {};

        const apply = () => {
            const query = normalize(search?.value);
            const selectedType = normalize(type?.value);
            let visibleGroups = 0;

            groups.forEach((group) => {
                const groupTitleMatches = !query || normalize(group.dataset.groupTitle).includes(query);
                const items = [...group.querySelectorAll('[data-po-action-item]')];
                let visibleActionCount = 0;

                items.forEach((item) => {
                    const searchMatches = groupTitleMatches
                        || !query
                        || normalize(item.dataset.filterText).includes(query);
                    const typeMatches = !selectedType || normalize(item.dataset.actionType) === selectedType;
                    const show = searchMatches && typeMatches;
                    item.hidden = !show;
                    if (show) visibleActionCount += Number.parseInt(item.dataset.actionCount || '1', 10) || 1;
                });

                group.hidden = visibleActionCount === 0;
                if (!group.hidden) visibleGroups++;

                const count = group.querySelector('[data-po-action-count]');
                if (count) {
                    count.textContent = `${visibleActionCount} action${visibleActionCount === 1 ? '' : 's'}`;
                }
            });

            updateEmpty(actionFilters, visibleGroups);
            syncClear();
        };

        search?.addEventListener('input', apply);
        type?.addEventListener('change', apply);
        syncClear = bindClear(actionFilters, [search, type].filter(Boolean), apply);
        apply();
    }

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

    const taskFilters = document.querySelector('[data-po-task-filters]');
    if (taskFilters) {
        const search = taskFilters.querySelector('[data-po-task-search]');
        const status = taskFilters.querySelector('[data-po-task-status]');
        const due = taskFilters.querySelector('[data-po-task-due]');
        const rows = [...document.querySelectorAll('[data-po-task-row]')];
        let syncClear = () => {};

        const apply = () => {
            const query = normalize(search?.value);
            const selectedStatus = normalize(status?.value);
            const selectedDue = normalize(due?.value);
            let visible = 0;

            rows.forEach((row) => {
                const show = (!query || normalize(row.dataset.filterText).includes(query))
                    && (!selectedStatus || normalize(row.dataset.status) === selectedStatus)
                    && (!selectedDue || normalize(row.dataset.due) === selectedDue);
                row.hidden = !show;
                if (show) visible++;
            });

            updateEmpty(taskFilters, visible);
            syncClear();
        };

        search?.addEventListener('input', apply);
        status?.addEventListener('change', apply);
        due?.addEventListener('change', apply);
        syncClear = bindClear(taskFilters, [search, status, due].filter(Boolean), apply);
        apply();
    }

    const ideaFilters = document.querySelector('[data-po-idea-filters]');
    if (ideaFilters) {
        const search = ideaFilters.querySelector('[data-po-idea-search]');
        const state = ideaFilters.querySelector('[data-po-idea-state]');
        const rows = [...document.querySelectorAll('[data-po-idea-row]')];
        let syncClear = () => {};

        const apply = () => {
            const query = normalize(search?.value);
            const selectedState = normalize(state?.value);
            let visible = 0;

            rows.forEach((row) => {
                const show = (!query || normalize(row.dataset.filterText).includes(query))
                    && (!selectedState || normalize(row.dataset.state) === selectedState);
                row.hidden = !show;
                if (show) visible++;
            });

            updateEmpty(ideaFilters, visible);
            syncClear();
        };

        search?.addEventListener('input', apply);
        state?.addEventListener('change', apply);
        syncClear = bindClear(ideaFilters, [search, state].filter(Boolean), apply);
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
