(() => {

    // Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
    const askConfirm = options => (window.PrismConfirm?.show
        ? window.PrismConfirm.show(options)
        : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));

    const toolbar = document.getElementById('classification-review-toolbar');
    const page = document.getElementById('select-page');
    const boxes = [...document.querySelectorAll('.js-classification-select')];
    const button = document.getElementById('bulk-apply');
    const clear = document.getElementById('bulk-clear');
    const count = document.getElementById('selected-count');
    const form = document.getElementById('bulk-classification-form');
    const bulkClassification = document.getElementById('bulk-classification');

    const updateStickyOffset = () => {
        const candidates = [
            document.querySelector('.pm-topbar'),
            document.querySelector('.app-header'),
            document.querySelector('header.sticky-top'),
            document.querySelector('.navbar.sticky-top')
        ].filter(Boolean);
        const bottom = candidates.reduce((maximum, element) => {
            const rectangle = element.getBoundingClientRect();
            return Math.max(maximum, rectangle.bottom > 0 ? rectangle.bottom : 0);
        }, 0);
        document.documentElement.style.setProperty('--classification-sticky-top', `${Math.ceil(bottom + 8)}px`);
    };

    const sync = () => {
        const selected = boxes.filter(box => box.checked);
        boxes.forEach(box => {
            const token = box.closest('.classification-card')?.querySelector('.js-classification-token');
            if (token) token.disabled = !box.checked;
        });
        if (count) count.textContent = `${selected.length} selected`;
        if (button) button.disabled = selected.length === 0;
        toolbar?.classList.toggle('has-selection', selected.length > 0);
        if (page) {
            page.checked = boxes.length > 0 && selected.length === boxes.length;
            page.indeterminate = selected.length > 0 && selected.length < boxes.length;
        }
    };

    const clearSelection = () => {
        boxes.forEach(box => { box.checked = false; });
        if (bulkClassification) bulkClassification.value = '';
        form?.querySelector('input[name="reason"]')?.setAttribute('value', '');
        const reason = form?.querySelector('input[name="reason"]');
        if (reason) reason.value = '';
        sync();
    };

    page?.addEventListener('change', () => {
        boxes.forEach(box => { box.checked = page.checked; });
        sync();
    });
    boxes.forEach(box => box.addEventListener('change', sync));
    clear?.addEventListener('click', clearSelection);

    // The bulk submit is held while the user decides, then sent once (with the pressed button) on yes.
    let bulkApproved = false;
    form?.addEventListener('submit', event => {
        if (bulkApproved) {
            bulkApproved = false;
            return;
        }
        const selected = boxes.filter(box => box.checked).length;
        if (!selected) {
            event.preventDefault();
            return;
        }
        const images = `${selected} image${selected === 1 ? '' : 's'}`;
        let question = null;
        if (bulkClassification?.value === 'Photograph') {
            question = {
                tone: 'warning',
                icon: 'bi-camera',
                title: `Classify ${images} as natural photographs?`,
                message: 'This does not approve biometric face processing; each image will still need separate admission.',
                confirmText: 'Classify as photographs',
                cancelText: 'Cancel'
            };
        } else if (selected > 10) {
            question = {
                title: `Apply this classification to ${images}?`,
                message: 'Every selected image takes the chosen classification.',
                confirmText: 'Apply classification',
                cancelText: 'Cancel'
            };
        }
        if (!question) return;
        event.preventDefault();
        const submitter = event.submitter;
        askConfirm(question).then(accepted => {
            if (!accepted) return;
            bulkApproved = true;
            form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
        });
    });

    document.querySelectorAll('[data-manual-form]').forEach(manualForm => {
        const select = manualForm.querySelector('[data-classification-select]');
        const reason = manualForm.querySelector('[data-classification-reason]');
        const prediction = manualForm.dataset.prediction;
        const updateReason = () => {
            const isPhotograph = select?.value === 'Photograph';
            const correcting = select?.value && select.value !== prediction;
            const required = Boolean(isPhotograph || correcting);
            if (reason) {
                reason.required = required;
                reason.setAttribute('aria-required', required ? 'true' : 'false');
            }
        };
        select?.addEventListener('change', updateReason);
        updateReason();
    });

    updateStickyOffset();
    window.addEventListener('resize', updateStickyOffset, { passive: true });
    sync();
})();
