(() => {
    const links = Array.from(document.querySelectorAll('.po-section-nav a[href^="#"]'));
    if (!links.length) return;

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

    for (const { link, section } of entries) {
        link.addEventListener('click', event => {
            event.preventDefault();
            section.scrollIntoView({ behavior: 'smooth', block: 'start' });
            history.replaceState(null, '', link.getAttribute('href'));
            setActive(link);
        });
    }

    if ('IntersectionObserver' in window) {
        const observer = new IntersectionObserver(observed => {
            const visible = observed
                .filter(item => item.isIntersecting)
                .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
            if (!visible) return;
            const match = entries.find(entry => entry.section === visible.target);
            if (match) setActive(match.link);
        }, { rootMargin: '-22% 0px -65% 0px', threshold: [0, .15, .35] });

        for (const { section } of entries) observer.observe(section);
    }
})();
