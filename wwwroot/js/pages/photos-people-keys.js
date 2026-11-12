// Keyboard and phone help for Review people and a person's page.
//
// Keyboard: the tick boxes of one set of faces work as a grid. Arrow keys move between them,
// Home and End go to the first and last, Space ticks (the browser's own behaviour) and Enter
// goes on to the step that confirms: the name field of the form the faces belong to, or the
// bar that acts on the ticked pictures. A face that is not ticked is ticked by Enter first when
// nothing else is, so Enter never leads to a step with nothing to act on.
//
// Phones: the sort and filter row is closed until asked for, and the chip of the list being
// shown is brought into view.
(() => {
    'use strict';

    const BOXES = '[data-batch-identity-face], [data-group-face-checkbox], [data-person-face-select]';

    // Where an arrow key leads from one box. `rects` are the boxes' places on the page, in
    // document order: { left, top, width, height }. Returns the index to move to (the same
    // index when there is nowhere to go).
    const nextIndex = (rects, index, key) => {
        const last = rects.length - 1;
        if (index < 0 || index > last) return index;
        if (key === 'Home') return 0;
        if (key === 'End') return last;
        if (key === 'ArrowLeft') return Math.max(0, index - 1);
        if (key === 'ArrowRight') return Math.min(last, index + 1);
        if (key !== 'ArrowUp' && key !== 'ArrowDown') return index;

        const from = rects[index];
        const centre = rect => rect.left + rect.width / 2;
        const down = key === 'ArrowDown';
        // The nearest row in that direction, then the box in it closest to this column.
        let rowTop = null;
        rects.forEach(rect => {
            const beyond = down ? rect.top > from.top + 1 : rect.top < from.top - 1;
            if (!beyond) return;
            if (rowTop === null || (down ? rect.top < rowTop : rect.top > rowTop)) rowTop = rect.top;
        });
        if (rowTop === null) return index;
        let best = index;
        let bestDistance = Infinity;
        rects.forEach((rect, candidate) => {
            if (Math.abs(rect.top - rowTop) > 1) return;
            const distance = Math.abs(centre(rect) - centre(from));
            if (distance < bestDistance) { best = candidate; bestDistance = distance; }
        });
        return best;
    };

    // The boxes that make one grid with this one: the same kind, and for a group of faces the
    // same form.
    const siblings = (box) => {
        const kind = ['data-batch-identity-face', 'data-group-face-checkbox', 'data-person-face-select']
            .find(name => box.hasAttribute(name));
        return Array.from(document.querySelectorAll(`[${kind}]`))
            .filter(other => !other.disabled && (kind !== 'data-group-face-checkbox' || other.form === box.form));
    };

    // The step that confirms what is ticked.
    const confirmStep = (box) => {
        if (box.hasAttribute('data-person-face-select')) {
            return document.querySelector('[data-correct-selected]');
        }
        const form = box.form;
        if (!form) return null;
        return form.querySelector('[data-name-field-ui]:not([hidden]) [data-name-field-text]')
            || form.querySelector('select[name="personId"], button[type="submit"]:not([disabled])');
    };

    const onKeydown = (event) => {
        const box = event.target;
        if (!(box instanceof HTMLInputElement) || !box.matches(BOXES)) return;
        if (event.altKey || event.ctrlKey || event.metaKey) return;

        if (event.key === 'Enter') {
            const set = siblings(box);
            if (!set.some(other => other.checked)) {
                box.checked = true;
                box.dispatchEvent(new Event('change', { bubbles: true }));
            }
            const step = confirmStep(box);
            if (step instanceof HTMLElement) {
                event.preventDefault();
                step.focus();
            }
            return;
        }

        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
        const set = siblings(box);
        const rects = set.map(other => {
            const tile = other.closest('.face-triage-card, [data-group-face-tile], .person-photo-card') || other;
            return tile.getBoundingClientRect();
        });
        const target = set[nextIndex(rects, set.indexOf(box), event.key)];
        event.preventDefault();
        if (target && target !== box) target.focus();
    };

    const onPhones = () => {
        const more = document.querySelector('[data-face-review-more]');
        if (more instanceof HTMLDetailsElement && typeof window.matchMedia === 'function') {
            const phone = window.matchMedia('(max-width: 575.98px)');
            const apply = () => { more.open = !phone.matches; };
            apply();
            if (typeof phone.addEventListener === 'function') phone.addEventListener('change', apply);
        }

        const chip = document.querySelector('.people-queues .photos-chip.is-active');
        const row = chip?.parentElement;
        if (chip instanceof HTMLElement && row instanceof HTMLElement && row.scrollWidth > row.clientWidth) {
            row.scrollLeft = Math.max(0, chip.offsetLeft - row.offsetLeft - (row.clientWidth - chip.offsetWidth) / 2);
        }
    };

    if (typeof document !== 'undefined') {
        document.addEventListener('keydown', onKeydown);
        onPhones();
    }
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { nextIndex };
    }
})();
