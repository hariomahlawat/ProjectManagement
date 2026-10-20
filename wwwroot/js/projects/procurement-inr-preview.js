// Procurement editor: reads each rupee amount back in lakh or crore as it is typed, so a
// missing or extra zero is visible before the form is saved. Display only; the server
// parses and validates the submitted value.
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    }
    if (root && root.document) {
        api.init(root.document);
    }
}(typeof window !== 'undefined' ? window : null, function () {
    const CRORE = 10000000;
    const LAKH = 100000;

    function trimmed(value) {
        return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/\.?0+$/, '');
    }

    function describe(raw) {
        // Digits only: the server binds the amount with the current culture, where group
        // separators are not reliable.
        const text = String(raw ?? '').trim();
        if (text === '') {
            return null;
        }
        if (!/^\d+(\.\d{0,2})?$/.test(text)) {
            return { valid: false, text: 'Use digits only, with at most two decimal places.' };
        }
        const amount = Number(text);
        if (amount >= CRORE) {
            return { valid: true, text: `Reads as ₹${trimmed(Math.round((amount / CRORE) * 100) / 100)} crore` };
        }
        if (amount >= LAKH) {
            return { valid: true, text: `Reads as ₹${trimmed(Math.round((amount / LAKH) * 100) / 100)} lakh` };
        }
        return { valid: true, text: `Reads as ₹${amount.toLocaleString('en-IN')}` };
    }

    function bind(input) {
        const hintId = input.getAttribute('aria-describedby');
        const hint = hintId ? input.ownerDocument.getElementById(hintId) : null;
        const target = hint ? hint.querySelector('[data-inr-preview]') : null;
        if (!target) {
            return;
        }
        const placeholder = target.textContent;
        const update = () => {
            const result = describe(input.value);
            target.textContent = result ? result.text : placeholder;
            target.classList.toggle('text-danger', Boolean(result && !result.valid));
            target.classList.toggle('procurement-inr-preview--live', Boolean(result && result.valid));
        };
        input.addEventListener('input', update);
        update();
    }

    function init(doc) {
        const run = () => doc.querySelectorAll('[data-inr-preview-source]').forEach(bind);
        if (doc.readyState === 'loading') {
            doc.addEventListener('DOMContentLoaded', run, { once: true });
        } else {
            run();
        }
    }

    return { describe, init };
}));
