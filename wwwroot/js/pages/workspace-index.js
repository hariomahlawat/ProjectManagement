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
        const siteHeader = document.querySelector('header, .navbar, .app-header');
        const headerHeight = siteHeader?.getBoundingClientRect().height || 70;
        return Math.round(headerHeight + nav.getBoundingClientRect().height + 16);
    };

    const activateFromScroll = () => {
        const marker = stickyOffset();
        let active = entries[0];
        for (const entry of entries) {
            if (entry.section.getBoundingClientRect().top <= marker) active = entry;
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
