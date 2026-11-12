// Conference review, choosing an officer (Pages/Workspace/Conference.cshtml).
//
// The find box narrows the officer tiles as a name is typed; Enter opens the first one left,
// Escape clears it. A picture that cannot be loaded is taken away, which leaves the initials
// that sit under it. Without this script the page is the full list of links, with no find box.
(() => {
    'use strict';

    const normalise = (text) => (text || '').trim().replace(/\s+/g, ' ').toLowerCase();

    // An officer matches when every word typed is found somewhere in "rank name".
    const matches = (name, typed) => {
        const words = normalise(typed).split(' ').filter(Boolean);
        const hay = normalise(name);
        return words.every(word => hay.includes(word));
    };

    const countText = (shown, total) => shown === total
        ? `${total} ${total === 1 ? 'officer' : 'officers'}`
        : `${shown} of ${total}`;

    const start = (root) => {
        root.querySelectorAll('[data-officer-portrait]').forEach(image => {
            const drop = () => image.remove();
            if (image.complete && image.naturalWidth === 0 && image.getAttribute('src')) drop();
            else image.addEventListener('error', drop, { once: true });
        });

        const find = root.querySelector('[data-officer-find]');
        const list = root.querySelector('[data-officer-list]');
        if (!(find instanceof HTMLInputElement) || !list) return;
        const cards = Array.from(list.querySelectorAll('[data-officer-card]'));
        const count = root.querySelector('[data-officer-count]');
        const none = root.querySelector('[data-officer-no-match]');
        const holder = find.closest('[hidden]');
        if (holder) holder.hidden = false;

        const apply = () => {
            let shown = 0;
            cards.forEach(card => {
                const keep = matches(card.dataset.officerName, find.value);
                card.hidden = !keep;
                if (keep) shown += 1;
            });
            if (count) count.textContent = countText(shown, cards.length);
            if (none) none.hidden = shown !== 0;
            list.hidden = shown === 0;
        };

        find.addEventListener('input', apply);
        find.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                const first = cards.find(card => !card.hidden);
                if (first) {
                    event.preventDefault();
                    first.click();
                }
            } else if (event.key === 'Escape' && find.value) {
                event.preventDefault();
                find.value = '';
                apply();
            }
        });
        apply();
    };

    if (typeof document !== 'undefined') {
        start(document);
    }
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { matches, countText };
    }
})();
