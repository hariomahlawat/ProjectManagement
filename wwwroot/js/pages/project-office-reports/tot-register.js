// ToT register: live search. Typing refreshes the register (status buttons, filters in use, count
// and table) in place, from the page's ?handler=Register, and keeps the address current. Without
// this script the search form still submits normally.
(() => {
    'use strict';

    const form = document.querySelector('[data-tot-search]');
    const input = form?.querySelector('[data-tot-live-search]');
    if (!(form instanceof HTMLFormElement) || !(input instanceof HTMLInputElement)) return;

    let timer = 0;
    let sequence = 0;

    function query() {
        const params = new URLSearchParams();
        new FormData(form).forEach((value, key) => {
            if (typeof value === 'string' && value.trim() !== '') params.set(key, value.trim());
        });
        return params;
    }

    async function refresh() {
        const params = query();
        const current = ++sequence;
        const handlerParams = new URLSearchParams(params);
        handlerParams.set('handler', 'Register');
        try {
            const response = await fetch(`${window.location.pathname}?${handlerParams}`, {
                credentials: 'same-origin',
                headers: { Accept: 'text/html', 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) throw new Error(String(response.status));
            const html = await response.text();
            // A newer search has started; this answer is out of date.
            if (current !== sequence) return;
            const template = document.createElement('template');
            template.innerHTML = html.trim();
            const next = template.content.querySelector('[data-tot-register]');
            const existing = document.querySelector('[data-tot-register]');
            if (!next || !existing) throw new Error('No register in the response.');
            existing.replaceWith(next);
            const search = params.toString();
            window.history.replaceState(window.history.state, '', search ? `${window.location.pathname}?${search}` : window.location.pathname);
        } catch {
            // Fall back to a normal search.
            if (current === sequence) form.submit();
        }
    }

    input.addEventListener('input', () => {
        window.clearTimeout(timer);
        timer = window.setTimeout(refresh, 300);
    });
    form.addEventListener('submit', (event) => {
        event.preventDefault();
        window.clearTimeout(timer);
        refresh();
    });
})();
