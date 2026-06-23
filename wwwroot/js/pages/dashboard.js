(() => {
    'use strict';

    document.documentElement.classList.add('js');

    const sections = Array.from(document.querySelectorAll('[data-dashboard] .db-reveal'));
    if (!sections.length) return;

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches || !('IntersectionObserver' in window)) {
        sections.forEach(section => section.classList.add('is-visible'));
        return;
    }

    const observer = new IntersectionObserver(entries => {
        entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            entry.target.classList.add('is-visible');
            observer.unobserve(entry.target);
        });
    }, {
        rootMargin: '0px 0px -8% 0px',
        threshold: 0.08
    });

    sections.forEach((section, index) => {
        section.style.transitionDelay = `${Math.min(index * 45, 180)}ms`;
        observer.observe(section);
    });
})();
