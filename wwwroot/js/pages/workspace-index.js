(() => {
    const nav = document.querySelector('.po-section-nav');
    const links = Array.from(document.querySelectorAll('.po-section-nav a[href^="#"]'));
    if (!nav || !links.length) return;

    const entries = links
        .map(link => ({ link, section: document.querySelector(link.getAttribute('href')) }))
        .filter(entry => entry.section);

    let manualActiveUntil = 0;

    const setActive = activeLink => {
        for (const { link } of entries) {
            const active = link === activeLink;
            link.classList.toggle('active', active);
            if (active) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        }
    };

    const updateTopbarHeight = () => {
        const siteHeader = document.querySelector('.pm-topbar');
        const headerHeight = Math.round(siteHeader?.getBoundingClientRect().height || 70);
        document.documentElement.style.setProperty('--po-topbar-height', `${headerHeight}px`);
        return headerHeight;
    };

    const stickyOffset = () => updateTopbarHeight() + Math.round(nav.getBoundingClientRect().height) + 14;

    const scrollTrackedEntries = () => entries
        .slice()
        .sort((a, b) => a.section.offsetTop - b.section.offsetTop);

    const activateFromScroll = () => {
        const stickyTop = updateTopbarHeight();
        const isStuck = nav.getBoundingClientRect().top <= stickyTop + 1 && window.scrollY > 12;
        nav.classList.toggle('is-stuck', isStuck);

        if (performance.now() < manualActiveUntil) return;

        // Switch as the next section heading enters the content band directly
        // below the toolbar, rather than waiting until most of it is visible.
        const marker = stickyTop + nav.getBoundingClientRect().height + 14;
        const tracked = scrollTrackedEntries();
        let active = tracked[0];

        for (const entry of tracked) {
            const heading = entry.section.querySelector('.po-panel__head, .po-readiness__head') || entry.section;
            if (heading.getBoundingClientRect().top <= marker) active = entry;
            else break;
        }

        if (active) setActive(active.link);
    };

    for (const { link, section } of entries) {
        link.addEventListener('click', event => {
            event.preventDefault();
            const top = window.scrollY + section.getBoundingClientRect().top - stickyOffset();
            manualActiveUntil = performance.now() + 900;
            window.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
            history.replaceState(null, '', link.getAttribute('href'));
            setActive(link);
        });
    }

    let ticking = false;
    const requestActivation = () => {
        if (ticking) return;
        ticking = true;
        window.requestAnimationFrame(() => {
            activateFromScroll();
            ticking = false;
        });
    };

    window.addEventListener('scroll', requestActivation, { passive: true });
    window.addEventListener('resize', requestActivation);
    activateFromScroll();
})();
