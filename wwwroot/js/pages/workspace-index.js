// SECTION: Workspace left-rail scroll spy
(() => {
    const links = Array.from(document.querySelectorAll('.workspace-rail-link'));
    if (!links.length) {
        return;
    }

    // SECTION: Build a section index that supports several rail links sharing one target.
    const sectionMap = new Map();
    const activeLinkByTarget = new Map();

    for (const link of links) {
        const target = link.getAttribute('data-workspace-section');
        if (!target) {
            continue;
        }

        const section = document.getElementById(target);
        if (!section) {
            continue;
        }

        const mapped = sectionMap.get(target) || { section, links: [] };
        mapped.links.push(link);
        sectionMap.set(target, mapped);

        if (link.classList.contains('active') || !activeLinkByTarget.has(target)) {
            activeLinkByTarget.set(target, link);
        }
    }

    // SECTION: Activate one rail link while remembering the user's choice for shared targets.
    const setActive = (target, preferredLink = null) => {
        for (const link of links) {
            link.classList.remove('active');
            link.removeAttribute('aria-current');
        }

        const mapped = sectionMap.get(target);
        if (!mapped) {
            return;
        }

        const linkToActivate = mapped.links.includes(preferredLink)
            ? preferredLink
            : activeLinkByTarget.get(target) || mapped.links[0];

        activeLinkByTarget.set(target, linkToActivate);
        linkToActivate.classList.add('active');
        linkToActivate.setAttribute('aria-current', 'true');
    };

    // SECTION: Preserve the exact clicked rail item even when anchors are shared.
    for (const link of links) {
        link.addEventListener('click', () => {
            const target = link.getAttribute('data-workspace-section');
            if (target) {
                setActive(target, link);
            }
        });
    }

    if (!('IntersectionObserver' in window)) {
        return;
    }

    // SECTION: Observe workspace content sections and keep the rail synced while scrolling.
    const preferredTargets = ['today', 'action-queue', 'assigned-projects', 'my-ideas-reminders', 'reminders']
        .filter(target => sectionMap.has(target));

    const observer = new IntersectionObserver((entries) => {
        const visible = entries
            .filter(entry => entry.isIntersecting)
            .sort((a, b) => b.intersectionRatio - a.intersectionRatio);

        if (!visible.length) {
            return;
        }

        const target = visible[0].target.id;
        setActive(target);
    }, {
        root: null,
        rootMargin: '-18% 0px -65% 0px',
        threshold: [0.15, 0.35, 0.55]
    });

    for (const target of preferredTargets) {
        observer.observe(sectionMap.get(target).section);
    }
})();
