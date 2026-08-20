(() => {
    'use strict';

    const modal = document.getElementById('prismUserLinkConfirmModal');
    if (!modal) return;

    const form = modal.querySelector('[data-prism-link-confirm-form]');
    const userId = modal.querySelector('[data-prism-link-user-id]');
    const accountName = modal.querySelector('[data-prism-link-account-name]');
    const accountMeta = modal.querySelector('[data-prism-link-account-meta]');
    const accountInitials = modal.querySelector('[data-prism-link-account-initials]');
    const verified = modal.querySelector('[data-prism-link-verified]');
    const submit = modal.querySelector('[data-prism-link-submit]');

    if (!form || !userId || !accountName || !accountMeta || !accountInitials || !verified || !submit) return;

    const initialsFor = (value) => {
        const parts = String(value || '')
            .trim()
            .split(/\s+/)
            .filter(Boolean);
        if (parts.length === 0) return '?';
        if (parts.length === 1) return parts[0].slice(0, 1).toUpperCase();
        return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
    };

    const resetVerification = () => {
        verified.checked = false;
        submit.disabled = true;
    };

    modal.addEventListener('show.bs.modal', (event) => {
        const trigger = event.relatedTarget?.closest?.('[data-prism-link-candidate]') || event.relatedTarget;
        if (!trigger || !trigger.matches?.('[data-prism-link-candidate]')) return;

        const displayName = trigger.dataset.userName || 'Selected PRISM user';
        const rank = trigger.dataset.userRank || '';
        const username = trigger.dataset.userUsername || '';

        userId.value = trigger.dataset.userId || '';
        accountName.textContent = displayName;
        accountMeta.textContent = [rank, username].filter(Boolean).join(' · ');
        accountInitials.textContent = initialsFor(displayName || username);
        resetVerification();
    });

    verified.addEventListener('change', () => {
        submit.disabled = !verified.checked || !userId.value;
    });

    modal.addEventListener('hidden.bs.modal', () => {
        userId.value = '';
        accountName.textContent = 'Selected PRISM user';
        accountMeta.textContent = '';
        accountInitials.textContent = '?';
        resetVerification();
    });
})();
