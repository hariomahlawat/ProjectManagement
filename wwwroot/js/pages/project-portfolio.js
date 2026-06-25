(() => {
    'use strict';

    const root = document.querySelector('.project-portfolio');
    if (!root) return;

    const prefersReducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    const expandedStageStorageKey = 'prism.projectPortfolio.expandedStage';

    function scrollToTarget(selector) {
        if (!selector) return;
        const target = document.querySelector(selector);
        if (!target) return;
        target.scrollIntoView({ behavior: prefersReducedMotion ? 'auto' : 'smooth', block: 'start' });
    }

    function setStageExpanded(stage, expanded, persist = true) {
        if (!stage) return;
        stage.classList.toggle('is-expanded', expanded);

        const toggle = stage.querySelector('[data-timeline-toggle]');
        if (toggle) {
            toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
            const stageName = stage.querySelector('[data-stage-name]')?.textContent?.trim() || 'stage';
            toggle.setAttribute('aria-label', `${expanded ? 'Collapse' : 'Expand'} details for ${stageName}`);
        }

        if (!persist) return;
        const stageCode = stage.getAttribute('data-stage-row');
        if (expanded && stageCode) sessionStorage.setItem(expandedStageStorageKey, stageCode);
        else if (sessionStorage.getItem(expandedStageStorageKey) === stageCode) sessionStorage.removeItem(expandedStageStorageKey);
    }

    root.addEventListener('click', (event) => {
        const scrollButton = event.target.closest('[data-scroll-target]');
        if (scrollButton) {
            event.preventDefault();
            scrollToTarget(scrollButton.getAttribute('data-scroll-target'));
            return;
        }

        const toggle = event.target.closest('[data-timeline-toggle]');
        if (!toggle) return;
        const stage = toggle.closest('[data-timeline-stage]');
        if (!stage || !stage.classList.contains('is-complete')) return;
        setStageExpanded(stage, !stage.classList.contains('is-expanded'));
    });

    function initializeTimelineDensity() {
        const completed = Array.from(root.querySelectorAll('[data-timeline-stage].is-complete'));
        completed.forEach((item) => setStageExpanded(item, false, false));

        const rememberedCode = sessionStorage.getItem(expandedStageStorageKey);
        const remembered = rememberedCode
            ? completed.find((item) => item.getAttribute('data-stage-row') === rememberedCode)
            : null;
        const stageToExpand = remembered || completed.at(-1);
        if (stageToExpand) setStageExpanded(stageToExpand, true, false);
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
