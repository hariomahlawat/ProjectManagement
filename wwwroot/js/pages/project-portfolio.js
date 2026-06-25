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

        target.scrollIntoView({
            behavior: prefersReducedMotion ? 'auto' : 'smooth',
            block: 'start'
        });

        if (target.matches('[data-timeline-stage]')) {
            target.classList.remove('is-focus-pulse');
            window.requestAnimationFrame(() => target.classList.add('is-focus-pulse'));
            window.setTimeout(() => target.classList.remove('is-focus-pulse'), 1500);
        }
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
        if (expanded && stageCode) {
            sessionStorage.setItem(expandedStageStorageKey, stageCode);
        } else if (sessionStorage.getItem(expandedStageStorageKey) === stageCode) {
            sessionStorage.removeItem(expandedStageStorageKey);
        }
    }

    function toggleCompletedStage(stage) {
        if (!stage || !stage.classList.contains('is-complete')) return;
        setStageExpanded(stage, !stage.classList.contains('is-expanded'));
    }

    root.addEventListener('click', (event) => {
        const scrollButton = event.target.closest('[data-scroll-target]');
        if (scrollButton) {
            event.preventDefault();
            scrollToTarget(scrollButton.getAttribute('data-scroll-target'));
            return;
        }

        const toggle = event.target.closest('[data-timeline-toggle]');
        if (toggle) {
            toggleCompletedStage(toggle.closest('[data-timeline-stage]'));
            return;
        }

        const stageCard = event.target.closest('[data-timeline-stage-card]');
        if (!stageCard) return;
        if (event.target.closest('button, a, input, select, textarea, [role="menu"], .dropdown-menu')) return;
        toggleCompletedStage(stageCard.closest('[data-timeline-stage]'));
    });

    function initializeTimelineDensity() {
        const completed = Array.from(root.querySelectorAll('[data-timeline-stage].is-complete'));
        completed.forEach((item) => setStageExpanded(item, false, false));

        const rememberedCode = sessionStorage.getItem(expandedStageStorageKey);
        const remembered = rememberedCode
            ? completed.find((item) => item.getAttribute('data-stage-row') === rememberedCode)
            : null;

        if (remembered) {
            setStageExpanded(remembered, true, false);
        }
    }

    function openRemarkComposer() {
        const composer = root.querySelector('.remarks-composer');
        const launcher = root.querySelector('[data-project-remark-launcher]');
        if (!composer) return;
        launcher?.setAttribute('hidden', 'hidden');
        composer.classList.remove('is-collapsed');
        composer.querySelector('[data-remarks-body]')?.focus();
    }

    function closeRemarkComposer() {
        const composer = root.querySelector('.remarks-composer');
        const launcher = root.querySelector('[data-project-remark-launcher]');
        if (!composer) return;
        composer.classList.add('is-collapsed');
        launcher?.removeAttribute('hidden');
    }

    function activateRemarksPanel() {
        const toggle = document.querySelector('[data-panel-target="remarks"]');
        toggle?.click();
        window.setTimeout(() => {
            openRemarkComposer();
            scrollToTarget('#remarks');
        }, 80);
    }

    root.querySelectorAll('[data-project-quick-action="remark"]').forEach((button) => {
        button.addEventListener('click', activateRemarksPanel);
    });

    function initializeRemarkComposer() {
        const composer = root.querySelector('.remarks-composer');
        const launcher = root.querySelector('[data-project-remark-launcher]');
        if (!composer || !launcher) return;

        if (composer.dataset.portfolioEnhanced !== 'true') {
            composer.dataset.portfolioEnhanced = 'true';
            composer.classList.add('is-collapsed');
            launcher.removeAttribute('hidden');

            launcher.addEventListener('click', openRemarkComposer);
            composer.querySelector('[data-remarks-reset]')?.addEventListener('click', () => {
                window.setTimeout(closeRemarkComposer, 0);
            });
        }
    }

    initializeTimelineDensity();
    initializeRemarkComposer();

    document.addEventListener('pm:remarks-rendered', () => {
        initializeRemarkComposer();
        const body = root.querySelector('[data-remarks-body]');
        if (body instanceof HTMLTextAreaElement && body.value.trim().length === 0) {
            closeRemarkComposer();
        }
    });
})();
