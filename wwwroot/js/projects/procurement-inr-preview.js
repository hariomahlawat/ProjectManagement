// Procurement editor: reads each rupee amount back in lakh or crore as it is typed, so a
// missing or extra zero is visible before the form is saved. Display only; the server
// parses and validates the submitted value.
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    }
    if (root && root.document) {
        // Exposed so a page that swaps in new procurement fields (the briefing desk after a
        // post without a reload) can bind them too.
        root.ProcurementInrPreview = api;
        api.init(root.document);
    }
}(typeof window !== 'undefined' ? window : null, function () {
    const CRORE = 10000000;
    const LAKH = 100000;

    function trimmed(value) {
        return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/\.?0+$/, '');
    }

    // Compares two non-negative decimal strings without going through floating point, which
    // cannot hold the column maximum (9999999999999999.99) exactly.
    function exceeds(text, max) {
        const [intPart, fraction = ''] = text.split('.');
        const [maxInt, maxFraction = ''] = String(max).split('.');
        const a = intPart.replace(/^0+(?=\d)/, '');
        if (a.length !== maxInt.length) {
            return a.length > maxInt.length;
        }
        if (a !== maxInt) {
            return a > maxInt;
        }
        // Full fraction, as the server's range check compares the exact value.
        const scale = Math.max(fraction.length, maxFraction.length);
        return fraction.padEnd(scale, '0') > maxFraction.padEnd(scale, '0');
    }

    // Mirrors what the save accepts: digits with an optional decimal part (the server binds
    // the amount with the current culture, where group separators are not reliable), up to
    // the column maximum; extra decimal places are rounded to paise by the decimal(18,2) column.
    function describe(raw, max) {
        const text = String(raw ?? '').trim();
        if (text === '') {
            return null;
        }
        if (!/^\d+(\.\d+)?$/.test(text)) {
            return { valid: false, text: 'Use digits only, without commas or units.' };
        }
        if (max && exceeds(text, max)) {
            return { valid: false, text: 'This is larger than the largest amount that can be saved.' };
        }
        const fraction = text.split('.')[1] || '';
        const rounding = fraction.length > 2 ? ' (saved to two decimal places)' : '';
        const amount = Number(text);
        if (amount >= CRORE) {
            return { valid: true, text: `Reads as ₹${trimmed(Math.round((amount / CRORE) * 100) / 100)} crore${rounding}` };
        }
        if (amount >= LAKH) {
            return { valid: true, text: `Reads as ₹${trimmed(Math.round((amount / LAKH) * 100) / 100)} lakh${rounding}` };
        }
        return { valid: true, text: `Reads as ₹${amount.toLocaleString('en-IN', { maximumFractionDigits: 2 })}${rounding}` };
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
            const result = describe(input.value, input.dataset.inrMax);
            target.textContent = result ? result.text : placeholder;
            target.classList.toggle('text-danger', Boolean(result && !result.valid));
            target.classList.toggle('procurement-inr-preview--live', Boolean(result && result.valid));
        };
        input.addEventListener('input', update);
        update();
    }

    // root is a document, or an element already in the document (newly inserted content).
    function init(root) {
        const run = () => root.querySelectorAll('[data-inr-preview-source]').forEach(bind);
        if (root.readyState === 'loading') {
            root.addEventListener('DOMContentLoaded', run, { once: true });
        } else {
            run();
        }
    }

    return { describe, init };
}));
