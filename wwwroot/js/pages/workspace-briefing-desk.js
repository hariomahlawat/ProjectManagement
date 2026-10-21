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

    // SECTION: Weekly round
    // Walks the PO through every not-ready card, one at a time. The round is kept in
    // sessionStorage (this tab only) so a post that reloads the page resumes on the same card.
    const ROUND_KEY = 'prism.briefingDesk.round';

    function readRound() {
        try {
            const raw = window.sessionStorage.getItem(ROUND_KEY);
            const saved = raw ? JSON.parse(raw) : null;
            return saved && Array.isArray(saved.ids) && saved.ids.length > 0 ? saved : null;
        } catch (error) {
            return null;
        }
    }

    function writeRound(state) {
        try {
            if (state) {
                window.sessionStorage.setItem(ROUND_KEY, JSON.stringify(state));
            } else {
                window.sessionStorage.removeItem(ROUND_KEY);
            }
        } catch (error) {
            // Storage can be unavailable (private mode, blocked site data); the round still works in-page.
        }
    }

    function initRound(desk) {
        const banner = desk.querySelector('[data-bd-round]');
        const done = desk.querySelector('[data-bd-round-done]');
        if (!banner || !done) {
            return;
        }

        const title = banner.querySelector('[data-bd-round-title]');
        const bar = banner.querySelector('[data-bd-round-bar]');
        const prev = banner.querySelector('[data-bd-round-prev]');
        const next = banner.querySelector('[data-bd-round-next]');
        const summary = done.querySelector('[data-bd-round-summary]');
        const cards = function () { return Array.from(desk.querySelectorAll('[data-bd-card]')); };
        let state = null;

        function focusCard(card) {
            const toggle = card.querySelector('[data-bd-toggle]');
            if (toggle) {
                toggle.focus({ preventScroll: true });
            }
            if (typeof banner.scrollIntoView === 'function') {
                banner.scrollIntoView({ block: 'start' });
            }
        }

        function leave() {
            desk.classList.remove('is-round');
            banner.hidden = true;
            cards().forEach(function (card) { card.classList.remove('bd-round-hidden'); });
            writeRound(null);
            state = null;
        }

        function finish() {
            const ids = state ? state.ids : [];
            const ready = ids.filter(function (id) {
                const card = document.getElementById(id);
                return card && card.dataset.bdReady === 'true';
            }).length;
            leave();
            summary.textContent = ready === ids.length
                ? 'All ' + ids.length + ' item' + (ids.length === 1 ? ' is' : 's are') + ' brief-ready. Run the round again next week.'
                : ready + ' of ' + ids.length + ' items from this round are brief-ready. The rest stay on the desk.';
            done.hidden = false;
            done.focus();
        }

        function render() {
            // A card can disappear between posts (for example a task the assigner closed).
            state.ids = state.ids.filter(function (id) { return document.getElementById(id); });
            if (state.ids.length === 0) {
                finish();
                return null;
            }

            state.index = Math.max(0, Math.min(state.index, state.ids.length - 1));
            const currentId = state.ids[state.index];
            desk.classList.add('is-round');
            banner.hidden = false;
            done.hidden = true;
            cards().forEach(function (card) { card.classList.toggle('bd-round-hidden', card.id !== currentId); });

            const card = document.getElementById(currentId);
            setExpanded(card, true);
            const position = state.index + 1;
            const total = state.ids.length;
            title.textContent = 'Item ' + position + ' of ' + total + ': ' + (card.dataset.bdTitle || '')
                + (card.dataset.bdReady === 'true' ? ' (brief-ready)' : '');
            bar.max = total;
            bar.value = position;
            prev.disabled = state.index === 0;
            next.textContent = position >= total ? 'Finish round' : 'Next item';
            writeRound(state);
            return card;
        }

        function move(step) {
            if (!state) {
                return;
            }
            if (step > 0 && state.index + 1 >= state.ids.length) {
                finish();
                return;
            }
            state.index += step;
            const card = render();
            if (card) {
                focusCard(card);
            }
        }

        const start = desk.querySelector('[data-bd-round-start]');
        if (start) {
            start.addEventListener('click', function () {
                // Recently completed projects are listed apart and are not part of the round.
                const ids = cards()
                    .filter(function (card) { return card.dataset.bdReady !== 'true' && !card.closest('.bd-completed'); })
                    .map(function (card) { return card.id; });
                if (ids.length === 0) {
                    return;
                }
                state = { ids: ids, index: 0 };
                const card = render();
                if (card) {
                    focusCard(card);
                }
            });
        }

        prev.addEventListener('click', function () { move(-1); });
        next.addEventListener('click', function () { move(1); });
        banner.querySelector('[data-bd-round-exit]').addEventListener('click', leave);
        done.querySelector('[data-bd-round-close]').addEventListener('click', function () { done.hidden = true; });

        // Resume after a post: the redirect names the card the PO was on.
        const saved = readRound();
        if (saved) {
            state = { ids: saved.ids, index: Number(saved.index) || 0 };
            const hash = window.location.hash ? window.location.hash.slice(1) : '';
            const hashIndex = state.ids.indexOf(hash);
            if (hashIndex >= 0) {
                state.index = hashIndex;
            }
            render();
        }
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

        initRound(desk);
    }

    // The script is loaded at the end of the body, so the desk is normally already parsed.
    if (document.querySelector('[data-briefing-desk]')) {
        init();
    } else {
        document.addEventListener('DOMContentLoaded', init);
    }
})();
