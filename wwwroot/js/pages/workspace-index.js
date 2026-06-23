(() => {
    const nav = document.querySelector('.po-section-nav');
    const links = Array.from(document.querySelectorAll('.po-section-nav a[href^="#"]'));
    if (!nav || !links.length) return;

    const entries = links
        .map(link => ({ link, section: document.querySelector(link.getAttribute('href')) }))
        .filter(entry => entry.section);

    const setActive = activeLink => {
        for (const { link } of entries) {
            const active = link === activeLink;
            link.classList.toggle('active', active);
            if (active) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        }
    };

    const stickyOffset = () => {
        const siteHeader = document.querySelector('.pm-topbar');
        const headerHeight = siteHeader?.getBoundingClientRect().height || 70;
        document.documentElement.style.setProperty('--po-topbar-height', `${Math.round(headerHeight)}px`);
        return Math.round(headerHeight + nav.getBoundingClientRect().height + 16);
    };

    const activateFromScroll = () => {
        const topbar = document.querySelector('.pm-topbar');
        const stickyTop = Math.round(topbar?.getBoundingClientRect().height || 70);
        nav.classList.toggle('is-stuck', nav.getBoundingClientRect().top <= stickyTop + 1 && window.scrollY > 12);

        const marker = stickyTop + nav.getBoundingClientRect().height + 2;
        let active = entries[0];
        for (const entry of entries) {
            const heading = entry.section.querySelector('.po-panel__head, .po-readiness__head') || entry.section;
            if (heading.getBoundingClientRect().top <= marker) {
                active = entry;
            } else {
                break;
            }
        }
        setActive(active.link);
    };

    for (const { link, section } of entries) {
        link.addEventListener('click', event => {
            event.preventDefault();
            const top = window.scrollY + section.getBoundingClientRect().top - stickyOffset();
            window.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
            history.replaceState(null, '', link.getAttribute('href'));
            setActive(link);
        });
    }

    let ticking = false;
    window.addEventListener('scroll', () => {
        if (ticking) return;
        ticking = true;
        window.requestAnimationFrame(() => {
            activateFromScroll();
            ticking = false;
        });
    }, { passive: true });

    window.addEventListener('resize', activateFromScroll);
    activateFromScroll();
})();
