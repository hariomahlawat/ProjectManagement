(() => {
    'use strict';

    document.documentElement.classList.add('js');

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const sections = Array.from(document.querySelectorAll('[data-dashboard] .db-reveal'));

    if (reducedMotion || !('IntersectionObserver' in window)) {
        sections.forEach(section => section.classList.add('is-visible'));
    } else {
        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            });
        }, { rootMargin: '0px 0px -7% 0px', threshold: 0.07 });

        sections.forEach((section, index) => {
            section.style.transitionDelay = `${Math.min(index * 35, 140)}ms`;
            observer.observe(section);
        });
    }

    if (typeof window.Chart === 'undefined') return;

    const palette = [
        { line: '#3d6fe6', fill: 'rgba(61,111,230,.10)' },
        { line: '#6e58db', fill: 'rgba(110,88,219,.10)' },
        { line: '#168d8a', fill: 'rgba(22,141,138,.10)' },
        { line: '#d68700', fill: 'rgba(214,135,0,.10)' },
        { line: '#8359d8', fill: 'rgba(131,89,216,.10)' },
        { line: '#1c8a59', fill: 'rgba(28,138,89,.10)' }
    ];

    const sparkTargets = Array.from(document.querySelectorAll('[data-db-spark]'));

    function renderSpark(target, index) {
        if (target.dataset.rendered === 'true') return;

        let values = [];
        let labels = [];
        try {
            values = JSON.parse(target.getAttribute('data-values') || '[]');
            labels = JSON.parse(target.getAttribute('data-labels') || '[]');
        } catch {
            return;
        }
        if (!values.length) return;

        target.dataset.rendered = 'true';
        const colours = palette[index % palette.length];
        const min = Math.min(...values);
        const max = Math.max(...values);
        const pad = Math.max(1, Math.ceil((max - min) * .18));

        new window.Chart(target.querySelector('canvas').getContext('2d'), {
            type: 'line',
            data: {
                labels,
                datasets: [{
                    data: values,
                    borderColor: colours.line,
                    backgroundColor: colours.fill,
                    borderWidth: 2,
                    pointRadius: 0,
                    pointHoverRadius: 3,
                    tension: .38,
                    fill: true
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: reducedMotion ? false : { duration: 520, easing: 'easeOutQuart' },
                interaction: { intersect: false, mode: 'index' },
                scales: {
                    x: { display: false },
                    y: { display: false, suggestedMin: Math.max(0, min - pad), suggestedMax: max + pad }
                },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            title: items => items[0]?.label || '',
                            label: item => ` ${item.formattedValue}`
                        }
                    }
                }
            }
        });
    }

    if (!('IntersectionObserver' in window)) {
        sparkTargets.forEach(renderSpark);
    } else {
        const sparkObserver = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                const index = sparkTargets.indexOf(entry.target);
                renderSpark(entry.target, index);
                sparkObserver.unobserve(entry.target);
            });
        }, { rootMargin: '120px' });
        sparkTargets.forEach(target => sparkObserver.observe(target));
    }
})();
