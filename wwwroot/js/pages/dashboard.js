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

        // A safety net: whatever has not been revealed by now is shown anyway, so a panel never
        // stays blank (a background tab, an observer that never fires).
        window.setTimeout(() => sections.forEach(section => section.classList.add('is-visible')), 2500);
    }

    if (typeof window.Chart === 'undefined') return;

    // Colours come from the stylesheet: each card owns one spectrum hue (--db-hue), and the
    // point halo is the card surface, so Light and Dark both read their own tokens.
    function cssVar(element, name, fallback) {
        const value = window.getComputedStyle(element).getPropertyValue(name).trim();
        return value || fallback;
    }

    function withAlpha(colour, alpha) {
        const hex = colour.match(/^#([0-9a-f]{3,8})$/i);
        if (hex) {
            let h = hex[1];
            if (h.length === 3 || h.length === 4) h = h.split('').map(c => c + c).join('');
            const r = parseInt(h.slice(0, 2), 16), g = parseInt(h.slice(2, 4), 16), b = parseInt(h.slice(4, 6), 16);
            return `rgba(${r},${g},${b},${alpha})`;
        }
        const rgb = colour.match(/^rgba?\(([^)]+)\)$/i);
        if (rgb) {
            const [r, g, b] = rgb[1].split(',').map(part => part.trim());
            return `rgba(${r},${g},${b},${alpha})`;
        }
        return colour;
    }

    function chartColours(target) {
        const line = cssVar(target, '--db-hue', cssVar(target, '--pm-primary', '#2563eb'));
        return { line, fill: withAlpha(line, .12), halo: cssVar(target, '--db-surface', '#ffffff') };
    }

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

    function renderSpark(target) {
        if (!target || target.dataset.rendered === 'true') return;
        const { values, labels } = parseSeries(target);
        const canvas = target.querySelector('canvas');
        if (!canvas || !values.length) return;

        target.dataset.rendered = 'true';
        const colours = chartColours(target);
        const scale = commonScales(values);

        new window.Chart(canvas.getContext('2d'), {
            type: 'line',
            data: {
                labels,
                datasets: [{
                    data: values,
                    borderColor: colours.line,
                    backgroundColor: colours.fill,
                    borderWidth: 2.75,
                    pointRadius: context => context.dataIndex === values.length - 1 ? 3 : 0,
                    pointHoverRadius: 3.5,
                    pointBackgroundColor: colours.line,
                    pointBorderColor: colours.halo,
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
        const colours = chartColours(target);
        const scale = commonScales(values);
        new window.Chart(canvas.getContext('2d'), {
            type: 'line',
            data: {
                labels,
                datasets: [{
                    data: values,
                    borderColor: colours.line,
                    backgroundColor: colours.fill,
                    borderWidth: 2.75,
                    pointRadius: context => context.raw > 0 ? 2.5 : 0,
                    pointHoverRadius: 4,
                    pointBackgroundColor: colours.line,
                    pointBorderColor: colours.halo,
                    pointBorderWidth: 1.5,
                    tension: .34,
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
                renderSpark(entry.target);
            }
            chartObserver.unobserve(entry.target);
        });
    }, { rootMargin: '120px' });

    chartTargets.forEach(target => chartObserver.observe(target));
})();
