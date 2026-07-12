(() => {
    const root = document.documentElement;
    const nav = document.querySelector('.po-section-nav');
    const links = Array.from(document.querySelectorAll('.po-section-nav a[href^="#"]'));

    if (!nav || links.length === 0) {
        return;
    }

    const entries = links
        .map(link => {
            const selector = link.getAttribute('href');
            return selector ? { link, section: document.querySelector(selector) } : null;
        })
        .filter(entry => entry?.section);

    if (entries.length === 0) {
        return;
    }

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let manualActiveUntil = 0;
    let ticking = false;

    const setActive = activeLink => {
        for (const { link } of entries) {
            const isActive = link === activeLink;
            link.classList.toggle('active', isActive);
            if (isActive) {
                link.setAttribute('aria-current', 'page');
            } else {
                link.removeAttribute('aria-current');
            }
        }
    };

    const updateStickyMeasurements = () => {
        const siteHeader = document.querySelector('.pm-topbar');
        const topbarHeight = Math.max(0, Math.round(siteHeader?.getBoundingClientRect().height || 70));
        const navHeight = Math.max(42, Math.round(nav.getBoundingClientRect().height));

        root.style.setProperty('--po-topbar-height', `${topbarHeight}px`);
        root.style.setProperty('--po-section-nav-height', `${navHeight}px`);

        return { topbarHeight, navHeight };
    };

    const getScrollTarget = section => {
        const { topbarHeight, navHeight } = updateStickyMeasurements();
        const sectionTop = window.scrollY + section.getBoundingClientRect().top;
        return Math.max(0, sectionTop - topbarHeight - navHeight - 14);
    };

    const activateFromScroll = () => {
        const { topbarHeight, navHeight } = updateStickyMeasurements();
        const navTop = nav.getBoundingClientRect().top;
        const isStuck = navTop <= topbarHeight + 1 && window.scrollY > 12;
        nav.classList.toggle('is-stuck', isStuck);

        if (performance.now() < manualActiveUntil) {
            return;
        }

        const marker = topbarHeight + navHeight + 16;
        let activeEntry = entries[0];

        for (const entry of entries) {
            const heading = entry.section.querySelector('.po-panel__head') || entry.section;
            if (heading.getBoundingClientRect().top <= marker) {
                activeEntry = entry;
            } else {
                break;
            }
        }

        setActive(activeEntry.link);
    };

    const requestActivation = () => {
        if (ticking) {
            return;
        }

        ticking = true;
        window.requestAnimationFrame(() => {
            activateFromScroll();
            ticking = false;
        });
    };

    for (const { link, section } of entries) {
        link.addEventListener('click', event => {
            event.preventDefault();
            manualActiveUntil = performance.now() + (reducedMotion.matches ? 100 : 900);
            setActive(link);

            window.scrollTo({
                top: getScrollTarget(section),
                behavior: reducedMotion.matches ? 'auto' : 'smooth'
            });

            history.replaceState(null, '', link.getAttribute('href'));
        });
    }

    const resizeObserver = typeof ResizeObserver === 'function'
        ? new ResizeObserver(requestActivation)
        : null;

    const siteHeader = document.querySelector('.pm-topbar');
    if (resizeObserver) {
        resizeObserver.observe(nav);
        if (siteHeader) {
            resizeObserver.observe(siteHeader);
        }
    }

    window.addEventListener('scroll', requestActivation, { passive: true });
    window.addEventListener('resize', requestActivation);
    reducedMotion.addEventListener?.('change', requestActivation);

    updateStickyMeasurements();
    activateFromScroll();
})();
