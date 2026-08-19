(() => {
    'use strict';

    const panel = document.querySelector('[data-person-photo-discovery]');
    if (!panel) return;

    const grid = panel.querySelector('[data-person-discovery-grid]');
    const batchBar = panel.querySelector('[data-person-discovery-batch]');
    const selectedCount = panel.querySelector('[data-person-discovery-selected-count]');
    const notice = panel.querySelector('[data-person-discovery-notice]');
    const tokenForm = panel.querySelector('[data-person-discovery-batch-token-form]');
    const personId = panel.dataset.personId || '';
    const statusUrl = panel.dataset.statusUrl || '';
    let pollHandle = 0;
    let matchingActive = false;

    const checkboxes = () => Array.from(panel.querySelectorAll('[data-person-candidate-select]'));
    const selectedFaceIds = () => checkboxes().filter(input => input.checked).map(input => input.value);

    const updateSelectionState = () => {
        const selected = selectedFaceIds();
        if (selectedCount) selectedCount.textContent = String(selected.length);
        if (batchBar) batchBar.hidden = selected.length === 0;
        checkboxes().forEach(input => {
            input.closest('[data-person-candidate]')?.classList.toggle('is-selected', input.checked);
        });
    };

    const setNotice = (message, isError = false) => {
        if (!notice) return;
        notice.textContent = message || '';
        notice.hidden = !message;
        notice.classList.toggle('is-error', isError);
    };

    const setCountText = (selector, value) => {
        document.querySelectorAll(selector).forEach(element => {
            element.textContent = String(value ?? 0);
        });
    };

    const updateSummary = summary => {
        if (!summary) return;

        setCountText('[data-person-confirmed-count]', summary.confirmedPhotoCount);
        setCountText('[data-person-discovery-count]', summary.possibleMatchCount);
        setCountText('[data-person-discovery-panel-count]', summary.possibleMatchCount);

        document.querySelectorAll('[data-person-last-seen]').forEach(element => {
            if (summary.latestMediaDateLabel) element.textContent = summary.latestMediaDateLabel;
        });

        document.querySelectorAll('[data-person-discovery-action-count]').forEach(element => {
            element.textContent = String(summary.possibleMatchCount ?? 0);
            element.classList.toggle('d-none', !summary.possibleMatchCount);
        });

        const matching = panel.querySelector('[data-person-discovery-matching]');
        if (matching) {
            const count = Number(summary.backgroundMatchingCount || 0);
            matching.hidden = count === 0;
            if (count > 0) {
                matching.innerHTML = `<i class="bi bi-arrow-repeat" aria-hidden="true"></i> ${count} unresolved appearance${count === 1 ? ' is' : 's are'} still being checked`;
            }
        }

        matchingActive = Number(summary.backgroundMatchingCount || 0) > 0;
        if (matchingActive) {
            scheduleStatusPoll();
        } else {
            stopStatusPoll();
        }
    };

    const parseJsonResponse = async response => {
        let payload = null;
        try {
            payload = await response.json();
        } catch {
            // A non-JSON response is treated as a server-side failure below.
        }
        if (!response.ok || !payload?.ok) {
            const message = payload?.message || `The identity operation could not be completed (${response.status}).`;
            throw new Error(message);
        }
        return payload;
    };

    const postForm = async form => {
        const controls = form.querySelectorAll('button, input');
        controls.forEach(control => { control.disabled = true; });
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                credentials: 'same-origin',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
            return await parseJsonResponse(response);
        } finally {
            controls.forEach(control => { control.disabled = false; });
        }
    };

    const removeReviewedCards = faceIds => {
        const ids = new Set((faceIds || []).map(String));
        panel.querySelectorAll('[data-person-candidate]').forEach(card => {
            if (!ids.has(card.dataset.faceId || '')) return;
            card.classList.add('is-reviewed');
            window.setTimeout(() => card.remove(), 180);
        });
        window.setTimeout(() => {
            updateSelectionState();
            const remaining = panel.querySelectorAll('[data-person-candidate]').length;
            if (remaining === 0) {
                setNotice('This review set is complete. Any additional possible photos will surface automatically or when this section is opened again.');
            }
        }, 220);
    };

    panel.querySelectorAll('[data-person-candidate-form]').forEach(form => {
        form.addEventListener('submit', async event => {
            event.preventDefault();
            setNotice('');
            try {
                const payload = await postForm(form);
                removeReviewedCards(payload.faceIds);
                updateSummary(payload.summary);
                setNotice(payload.message || 'Identity review updated.');
            } catch (error) {
                setNotice(error?.message || 'The identity operation could not be completed.', true);
            }
        });
    });

    checkboxes().forEach(input => input.addEventListener('change', updateSelectionState));

    panel.querySelector('[data-person-discovery-batch-clear]')?.addEventListener('click', () => {
        checkboxes().forEach(input => { input.checked = false; });
        updateSelectionState();
    });

    const runBatch = async (url, confirmAction) => {
        const faceIds = selectedFaceIds();
        if (!url || !tokenForm || faceIds.length === 0) return;

        const prompt = confirmAction
            ? `Confirm ${faceIds.length} selected appearance${faceIds.length === 1 ? '' : 's'} as this person?`
            : `Reject this person for ${faceIds.length} selected appearance${faceIds.length === 1 ? '' : 's'}?`;
        if (!window.confirm(prompt)) return;

        const formData = new FormData(tokenForm);
        faceIds.forEach(faceId => formData.append('faceIds', faceId));
        setNotice('');
        if (batchBar) batchBar.querySelectorAll('button').forEach(button => { button.disabled = true; });
        try {
            const response = await fetch(url, {
                method: 'POST',
                body: formData,
                credentials: 'same-origin',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
            const payload = await parseJsonResponse(response);
            removeReviewedCards(payload.faceIds);
            updateSummary(payload.summary);
            setNotice(payload.message || 'Identity review updated.');
        } catch (error) {
            setNotice(error?.message || 'The batch identity operation could not be completed.', true);
        } finally {
            if (batchBar) batchBar.querySelectorAll('button').forEach(button => { button.disabled = false; });
        }
    };

    panel.querySelector('[data-person-discovery-batch-confirm]')?.addEventListener('click', () => {
        void runBatch(panel.dataset.confirmBatchUrl || '', true);
    });
    panel.querySelector('[data-person-discovery-batch-reject]')?.addEventListener('click', () => {
        void runBatch(panel.dataset.rejectBatchUrl || '', false);
    });

    const pollStatus = async () => {
        if (!statusUrl || document.hidden) return;
        try {
            const response = await fetch(statusUrl, {
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) return;
            const summary = await response.json();
            updateSummary(summary);
        } catch {
            // Background status is advisory; normal page operations remain available.
        }
    };

    function scheduleStatusPoll() {
        if (pollHandle || !statusUrl) return;
        pollHandle = window.setInterval(() => { void pollStatus(); }, 6000);
    }

    function stopStatusPoll() {
        if (!pollHandle) return;
        window.clearInterval(pollHandle);
        pollHandle = 0;
    }

    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
            stopStatusPoll();
            return;
        }
        void pollStatus();
        if (matchingActive) scheduleStatusPoll();
    });

    updateSelectionState();
    const initialMatching = panel.querySelector('[data-person-discovery-matching]');
    matchingActive = Boolean(initialMatching && !initialMatching.hidden);
    if (matchingActive) scheduleStatusPoll();
})();
