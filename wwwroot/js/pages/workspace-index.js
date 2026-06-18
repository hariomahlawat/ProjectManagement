// SECTION: Workspace left-rail scroll spy
(() => {
    const links = Array.from(document.querySelectorAll('.workspace-rail-link'));
    if (!links.length) {
        return;
    }

    const sectionMap = new Map();

    for (const link of links) {
        const target = link.getAttribute('data-workspace-section');
        if (!target) {
            continue;
        }

        const section = document.getElementById(target);
        if (!section) {
            continue;
        }

        sectionMap.set(target, { section, link });
    }

    const setActive = (target) => {
        for (const link of links) {
            link.classList.remove('active');
            link.removeAttribute('aria-current');
        }

        const mapped = sectionMap.get(target);
        if (mapped) {
            mapped.link.classList.add('active');
            mapped.link.setAttribute('aria-current', 'true');
        }
    };

    for (const link of links) {
        link.addEventListener('click', () => {
            const target = link.getAttribute('data-workspace-section');
            if (target) {
                setActive(target);
            }
        });
    }

    if (!('IntersectionObserver' in window)) {
        return;
    }

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
        setActive(target === 'action-queue' ? 'today' : target);
    }, {
        root: null,
        rootMargin: '-18% 0px -65% 0px',
        threshold: [0.15, 0.35, 0.55]
    });

    for (const target of preferredTargets) {
        observer.observe(sectionMap.get(target).section);
    }
})();
