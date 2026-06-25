(() => {
    'use strict';

    const root = document.querySelector('.project-portfolio');
    if (!root) return;

    const prefersReducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

    function scrollToTarget(selector) {
        if (!selector) return;
        const target = document.querySelector(selector);
        if (!target) return;
        target.scrollIntoView({ behavior: prefersReducedMotion ? 'auto' : 'smooth', block: 'start' });
    }

    root.addEventListener('click', (event) => {
        const scrollButton = event.target.closest('[data-scroll-target]');
        if (scrollButton) {
            event.preventDefault();
            scrollToTarget(scrollButton.getAttribute('data-scroll-target'));
            return;
        }

        const stage = event.target.closest('[data-timeline-stage]');
        if (!stage || !stage.classList.contains('is-complete')) return;
        if (event.target.closest('button, a, input, select, textarea, .dropdown-menu')) return;

        stage.classList.toggle('is-expanded');
        stage.querySelector('[data-timeline-stage-card]')?.setAttribute(
            'aria-expanded',
            stage.classList.contains('is-expanded') ? 'true' : 'false'
        );
    });

    function initializeTimelineDensity() {
        const completed = Array.from(root.querySelectorAll('[data-timeline-stage].is-complete'));
        completed.forEach((item) => {
            item.classList.remove('is-expanded');
            item.querySelector('[data-timeline-stage-card]')?.setAttribute('aria-expanded', 'false');
        });
        const lastCompleted = completed.at(-1);
        if (lastCompleted) {
            lastCompleted.classList.add('is-expanded');
            lastCompleted.querySelector('[data-timeline-stage-card]')?.setAttribute('aria-expanded', 'true');
        }
    }

    function activateRemarksPanel() {
        const toggle = document.querySelector('[data-panel-target="remarks"]');
        toggle?.click();
        window.setTimeout(() => {
            const launcher = root.querySelector('[data-project-remark-launcher]');
            if (launcher) launcher.click();
            else root.querySelector('[data-remarks-body]')?.focus();
            scrollToTarget('#remarks');
        }, 80);
    }

    root.querySelectorAll('[data-project-quick-action="remark"]').forEach((button) => {
        button.addEventListener('click', activateRemarksPanel);
    });

    function initializeRemarkComposer() {
        const composer = root.querySelector('.remarks-composer');
        if (!composer || composer.dataset.portfolioEnhanced === 'true') return;
        composer.dataset.portfolioEnhanced = 'true';
        composer.classList.add('is-collapsed');

        const launcher = document.createElement('button');
        launcher.type = 'button';
        launcher.className = 'project-remark-launcher';
        launcher.dataset.projectRemarkLauncher = 'true';
        launcher.innerHTML = '<i class="bi bi-plus-circle me-2" aria-hidden="true"></i>Add a project remark…';
        composer.before(launcher);

        launcher.addEventListener('click', () => {
            launcher.hidden = true;
            composer.classList.remove('is-collapsed');
            composer.querySelector('[data-remarks-body]')?.focus();
        });

        composer.querySelector('[data-remarks-reset]')?.addEventListener('click', () => {
            window.setTimeout(() => {
                composer.classList.add('is-collapsed');
                launcher.hidden = false;
            }, 0);
        });
    }

    initializeTimelineDensity();
    initializeRemarkComposer();

    document.addEventListener('pm:remarks-rendered', initializeRemarkComposer);
})();
