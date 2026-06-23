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
                entry.target.style.removeProperty('transition-delay');
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

    function parseSeries(target) {
        try {
            return {
                values: JSON.parse(target.getAttribute('data-values') || '[]'),
                labels: JSON.parse(target.getAttribute('data-labels') || '[]')
            };
        } catch {
            return { values: [], labels: [] };
        }
    }

    function commonScales(values) {
        const min = Math.min(...values, 0);
        const max = Math.max(...values, 1);
        const pad = Math.max(1, Math.ceil((max - min) * .18));
        return { suggestedMin: Math.max(0, min - pad), suggestedMax: max + pad };
    }

    function renderSpark(target, index) {
        if (!target || target.dataset.rendered === 'true') return;
        const { values, labels } = parseSeries(target);
        const canvas = target.querySelector('canvas');
        if (!canvas || !values.length) return;

        target.dataset.rendered = 'true';
        const colours = palette[index % palette.length];
        const scale = commonScales(values);

        new window.Chart(canvas.getContext('2d'), {
            type: 'line',
            data: {
                labels,
                datasets: [{
                    data: values,
                    borderColor: colours.line,
                    backgroundColor: colours.fill,
                    borderWidth: 2,
                    pointRadius: context => context.dataIndex === values.length - 1 ? 2.5 : 0,
                    pointHoverRadius: 3,
                    pointBackgroundColor: colours.line,
                    pointBorderColor: '#ffffff',
                    pointBorderWidth: 1.5,
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
                    y: { display: false, ...scale }
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

    function renderActivityTrend(target) {
        if (!target || target.dataset.rendered === 'true') return;
        const { values, labels } = parseSeries(target);
        const canvas = target.querySelector('canvas');
        if (!canvas || !values.length) return;

        target.dataset.rendered = 'true';
        const scale = commonScales(values);
        new window.Chart(canvas.getContext('2d'), {
            type: 'bar',
            data: {
                labels,
                datasets: [{
                    data: values,
                    backgroundColor: 'rgba(22,139,136,.18)',
                    borderColor: '#168b88',
                    borderWidth: 1,
                    borderRadius: 4,
                    maxBarThickness: 18
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: reducedMotion ? false : { duration: 520, easing: 'easeOutQuart' },
                interaction: { intersect: false, mode: 'index' },
                scales: {
                    x: { display: false },
                    y: { display: false, beginAtZero: true, ...scale }
                },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            title: items => items[0]?.label || '',
                            label: item => ` ${item.formattedValue} activities`
                        }
                    }
                }
            }
        });
    }

    const sparkTargets = Array.from(document.querySelectorAll('[data-db-spark]'));
    const activityTarget = document.querySelector('[data-db-activity-trend]');

    const renderAll = () => {
        sparkTargets.forEach(renderSpark);
        renderActivityTrend(activityTarget);
    };

    if (!('IntersectionObserver' in window)) {
        renderAll();
        return;
    }

    const chartTargets = [...sparkTargets, activityTarget].filter(Boolean);
    const chartObserver = new IntersectionObserver(entries => {
        entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            if (entry.target.matches('[data-db-activity-trend]')) {
                renderActivityTrend(entry.target);
            } else {
                renderSpark(entry.target, sparkTargets.indexOf(entry.target));
            }
            chartObserver.unobserve(entry.target);
        });
    }, { rootMargin: '120px' });

    chartTargets.forEach(target => chartObserver.observe(target));
})();
