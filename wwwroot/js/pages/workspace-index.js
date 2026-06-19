// SECTION: Workspace deterministic left-rail smooth scroll navigation
(() => {
    const links = Array.from(document.querySelectorAll('.workspace-rail-link'));
    if (!links.length) {
        return;
    }

    const sectionEntries = links
        .map(link => {
            const target = link.getAttribute('data-workspace-section');
            const section = target ? document.getElementById(target) : null;

            return {
                target,
                section,
                link
            };
        })
        .filter(entry => entry.target && entry.section);

    if (!sectionEntries.length) {
        return;
    }

    let manualScrollInProgress = false;
    let manualScrollTimer = null;

    const setActive = (target, preferredLink = null) => {
        for (const link of links) {
            link.classList.remove('active');
            link.removeAttribute('aria-current');
        }

        const entry = preferredLink ? { link: preferredLink } : sectionEntries.find(item => item.target === target);
        if (entry) {
            entry.link.classList.add('active');
            entry.link.setAttribute('aria-current', 'true');
        }
    };

    for (const entry of sectionEntries) {
        entry.link.addEventListener('click', event => {
            event.preventDefault();

            manualScrollInProgress = true;
            setActive(entry.target, entry.link);

            if (typeof entry.section.scrollIntoView === 'function') {
                entry.section.scrollIntoView({
                    behavior: 'smooth',
                    block: 'start'
                });
            }

            window.history.replaceState(null, '', `#${entry.target}`);

            if (manualScrollTimer) {
                window.clearTimeout(manualScrollTimer);
            }

            manualScrollTimer = window.setTimeout(() => {
                manualScrollInProgress = false;
            }, 700);
        });
    }

    const getCurrentSection = () => {
        const viewportReference = window.scrollY + 120;
        const orderedEntries = sectionEntries
            .map(entry => ({
                ...entry,
                top: entry.section.getBoundingClientRect().top + window.scrollY
            }))
            .sort((a, b) => a.top - b.top);
        let current = orderedEntries[0];

        for (const entry of orderedEntries) {
            if (entry.top <= viewportReference) {
                current = entry;
            } else {
                break;
            }
        }

        return current;
    };

    const updateActiveFromScroll = () => {
        if (manualScrollInProgress) {
            return;
        }

        const current = getCurrentSection();
        if (current) {
            setActive(current.target);
        }
    };

    let ticking = false;

    window.addEventListener('scroll', () => {
        if (ticking) {
            return;
        }

        window.requestAnimationFrame(() => {
            updateActiveFromScroll();
            ticking = false;
        });

        ticking = true;
    }, { passive: true });

    updateActiveFromScroll();
})();
