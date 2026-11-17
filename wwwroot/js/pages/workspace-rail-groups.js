// Workspace rail groups. When the whole menu does not fit the rail (a short window, a short page,
// or a user with many groups), groups that do not hold the current page start folded to their
// headings, from the bottom up, until it fits. A heading opens or folds its group, and that choice is kept for the visit. When the
// menu fits, every group is open, as before. Without this script every group stays open.
const storageKey = (name) => `prism.workspace.railGroup.${name}`;

const readChoice = (storage, name) => {
    try { return storage?.getItem(storageKey(name)) ?? null; } catch { return null; }
};

const saveChoice = (storage, name, open) => {
    try { storage?.setItem(storageKey(name), open ? 'open' : 'folded'); } catch { /* storage is optional */ }
};

export function initWorkspaceRailGroups(root = document, options = {}) {
    const rail = root.querySelector('[data-workspace-rail]');
    if (!rail) return null;
    const groups = Array.from(rail.querySelectorAll('[data-rail-group]'));
    if (groups.length === 0) return null;

    let { storage } = options;
    if (storage === undefined) {
        try { storage = window.sessionStorage; } catch { storage = null; }
    }
    // Only the expanded rail folds: the icon rail and the phone row always show every item.
    const overflows = options.overflows
        ?? (() => rail.classList.contains('is-expanded') && rail.scrollHeight > rail.clientHeight + 1);

    // The page's own group never starts folded. A page outside every group folds nothing.
    const holdsPage = (group) => group.querySelector('a.is-active, a[aria-current="page"]') !== null;
    const anyHoldsPage = groups.some(holdsPage);

    const setOpen = (group, open) => {
        group.classList.toggle('is-folded', !open);
        group.querySelector('[data-rail-group-toggle]')?.setAttribute('aria-expanded', String(open));
    };

    const apply = () => {
        const foldable = [];
        groups.forEach((group) => {
            const choice = holdsPage(group) || !anyHoldsPage ? 'open' : readChoice(storage, group.dataset.railGroup);
            setOpen(group, choice !== 'folded');
            if (choice === null) foldable.push(group);
        });
        // From the bottom up, only as many as it takes to fit.
        for (const group of foldable.reverse()) {
            if (!overflows()) break;
            setOpen(group, false);
        }
    };

    groups.forEach((group) => {
        group.querySelector('[data-rail-group-toggle]')?.addEventListener('click', () => {
            const open = group.classList.contains('is-folded');
            setOpen(group, open);
            saveChoice(storage, group.dataset.railGroup, open);
        });
    });

    apply();
    // The rail is as tall as the page beside it, up to the window: measure again when its height
    // changes (a resized window, content or fonts arriving after the first measure).
    const view = root.defaultView;
    let measuredHeight = rail.clientHeight;
    let pending = false;
    const remeasure = () => {
        if (pending) return;
        pending = true;
        view.requestAnimationFrame(() => {
            pending = false;
            if (rail.clientHeight === measuredHeight) return;
            measuredHeight = rail.clientHeight;
            apply();
        });
    };
    if (view?.ResizeObserver) {
        new view.ResizeObserver(remeasure).observe(rail);
    } else {
        view?.addEventListener('resize', remeasure);
    }
    // Expanding the icon rail measures again (it folds nothing while it shows icons only).
    let wasExpanded = rail.classList.contains('is-expanded');
    if (view?.MutationObserver) {
        new view.MutationObserver(() => {
            const expanded = rail.classList.contains('is-expanded');
            if (expanded && !wasExpanded) apply();
            wasExpanded = expanded;
        }).observe(rail, { attributes: true, attributeFilter: ['class'] });
    }
    return { apply };
}

if (typeof document !== 'undefined' && document.querySelector?.('[data-workspace-rail]')) {
    initWorkspaceRailGroups();
}
