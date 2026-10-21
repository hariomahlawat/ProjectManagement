(function () {
    'use strict';

    // SECTION: Card expand and collapse
    // The toggle button is the accessible control; a click anywhere on the card head
    // that is not itself a link or control is forwarded to it.
    function setExpanded(card, expanded) {
        const toggle = card.querySelector('[data-bd-toggle]');
        const body = toggle ? document.getElementById(toggle.getAttribute('aria-controls')) : null;
        if (!toggle || !body) {
            return;
        }

        toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        body.hidden = !expanded;
        card.classList.toggle('is-expanded', expanded);
    }

    function wireCard(card) {
        const toggle = card.querySelector('[data-bd-toggle]');
        if (!toggle) {
            return;
        }

        toggle.addEventListener('click', function () {
            setExpanded(card, toggle.getAttribute('aria-expanded') !== 'true');
        });

        const head = card.querySelector('.bd-card__head');
        if (head) {
            head.addEventListener('click', function (event) {
                if (event.target instanceof Element && event.target.closest('a, button, input, textarea, select, summary')) {
                    return;
                }

                toggle.click();
            });
        }

        // SECTION: Conference preview
        // Shows the card as the Conference view presents it, in place of the working area.
        const previewToggle = card.querySelector('[data-bd-preview-toggle]');
        const preview = previewToggle ? document.getElementById(previewToggle.getAttribute('aria-controls')) : null;
        const work = card.querySelector('.bd-card__work');
        const label = previewToggle ? previewToggle.querySelector('[data-bd-preview-label]') : null;
        if (previewToggle && preview) {
            previewToggle.addEventListener('click', function () {
                const showing = previewToggle.getAttribute('aria-expanded') === 'true';
                previewToggle.setAttribute('aria-expanded', showing ? 'false' : 'true');
                preview.hidden = showing;
                if (work) {
                    work.hidden = !showing;
                }
                if (label) {
                    label.textContent = showing ? 'See as the Comdt sees it' : 'Back to preparing';
                }
            });
        }

        // SECTION: Double-submit guard
        card.querySelectorAll('form').forEach(function (form) {
            form.addEventListener('submit', function () {
                const submit = form.querySelector('button[type="submit"]');
                if (submit) {
                    submit.disabled = true;
                    submit.setAttribute('aria-busy', 'true');
                }
            });
        });
    }

    function init() {
        const desk = document.querySelector('[data-briefing-desk]');
        if (!desk) {
            return;
        }

        desk.querySelectorAll('[data-bd-card]').forEach(wireCard);

        // A post returns here with the card's id in the hash: open it and move focus to it.
        const hash = window.location.hash ? window.location.hash.slice(1) : '';
        const target = hash ? document.getElementById(hash) : null;
        if (target && target.matches('[data-bd-card]')) {
            setExpanded(target, true);
            const toggle = target.querySelector('[data-bd-toggle]');
            if (toggle) {
                toggle.focus({ preventScroll: true });
            }
            if (typeof target.scrollIntoView === 'function') {
                target.scrollIntoView({ block: 'start' });
            }
        }
    }

    // The script is loaded at the end of the body, so the desk is normally already parsed.
    if (document.querySelector('[data-briefing-desk]')) {
        init();
    } else {
        document.addEventListener('DOMContentLoaded', init);
    }
})();
